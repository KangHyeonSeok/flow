// E2EServer.cs - Unity E2E Test HTTP Server
//
// Unity app embedded HTTP server. Receives scenarios from the E2E tool,
// drives UI automation, and returns results (screenshots, logs).
//
// Usage:
//   1. Add E2EServer to a GameObject
//   2. Add E2E_TESTS to Scripting Define Symbols
//
// Endpoints:
//   POST /e2e/run    - Submit scenario
//   GET  /e2e/status - Poll status
//   GET  /e2e/result - Collect results
//   GET  /e2e/health - Health check

#if E2E_TESTS

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

#if TMP_PRESENT
using TMPro;
#endif

namespace FlowE2E
{
    /// <summary>
    /// HTTP server for receiving and executing E2E test scenarios.
    /// Only active when E2E_TESTS scripting define symbol is set.
    /// </summary>
    public class E2EServer : MonoBehaviour
    {
        [Header("Server Settings")]
        [Tooltip("HTTP server port")]
        [SerializeField] private int port = 51321;

        [Tooltip("Application name for identification")]
        [SerializeField] private string appName = "my-unity-app";

        // --- Internal State ---
        private HttpListener _listener;
        private Thread _listenerThread;
        private volatile bool _isRunning;

        // Test execution state
        private volatile string _status = "idle"; // idle, running, completed, failed
        private volatile float _progress;
        private volatile int _currentStep;
        private volatile int _totalSteps;
        private string _sessionId;
        private string _error;

        // Results
        private readonly Dictionary<string, string> _screenshots = new(); // name -> base64
        private readonly List<LogEntry> _logs = new();

        // Pending scenario (set by HTTP thread, consumed by main thread)
        private volatile string _pendingScenario;

        /// <summary>Server port, readable by E2EBeacon.</summary>
        public int Port => port;

        /// <summary>Application name.</summary>
        public string AppName => appName;

        // ─────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────

        private void Start()
        {
            StartServer();
            AddLog("info", $"E2E Server started on port {port}");
        }

        private void OnDestroy()
        {
            StopServer();
        }

        private void Update()
        {
            // Check if there's a pending scenario to execute (thread-safe handoff)
            if (_pendingScenario != null)
            {
                var scenario = _pendingScenario;
                _pendingScenario = null;
                StartCoroutine(ExecuteScenario(scenario));
            }
        }

        // ─────────────────────────────────────────────
        // HTTP Server
        // ─────────────────────────────────────────────

        private void StartServer()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{port}/");
            _listener.Start();

            _isRunning = true;
            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "E2EServer"
            };
            _listenerThread.Start();
        }

        private void StopServer()
        {
            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
        }

        private void ListenLoop()
        {
            while (_isRunning)
            {
                try
                {
                    var context = _listener.GetContext();
                    ProcessRequest(context);
                }
                catch (HttpListenerException)
                {
                    // Listener stopped
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[E2E] HTTP error: {ex.Message}");
                }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            var path = request.Url.AbsolutePath.ToLower();
            var method = request.HttpMethod.ToUpper();

            string json;

            try
            {
                json = (path, method) switch
                {
                    ("/e2e/run", "POST") => HandleRun(request),
                    ("/e2e/status", "GET") => HandleStatus(),
                    ("/e2e/result", "GET") => HandleResult(),
                    ("/e2e/health", "GET") => HandleHealth(),
                    _ => throw new HttpException(404, $"Not found: {path}")
                };

                SendJson(response, 200, json);
            }
            catch (HttpException ex)
            {
                SendJson(response, ex.StatusCode,
                    JsonUtility.ToJson(new ErrorResponse { error = ex.Message }));
            }
            catch (Exception ex)
            {
                SendJson(response, 500,
                    JsonUtility.ToJson(new ErrorResponse { error = ex.Message }));
            }
        }

        // ─────────────────────────────────────────────
        // Endpoint Handlers
        // ─────────────────────────────────────────────

        private string HandleRun(HttpListenerRequest request)
        {
            if (_status == "running")
                throw new HttpException(409, "Test already running");

            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();

            if (string.IsNullOrWhiteSpace(body))
                throw new HttpException(400, "Request body is empty");

            // Reset state
            _screenshots.Clear();
            _logs.Clear();
            _error = null;
            _progress = 0;
            _currentStep = 0;
            _status = "running";
            _sessionId = $"e2e-{Guid.NewGuid():N}"[..12];

            // Queue scenario for main thread execution
            _pendingScenario = body;

            AddLog("info", $"Test session started: {_sessionId}");

            return JsonUtility.ToJson(new RunResponse
            {
                session_id = _sessionId,
                status = "running"
            });
        }

        private string HandleStatus()
        {
            return JsonUtility.ToJson(new StatusResponse
            {
                status = _status,
                progress = _progress,
                current_step = _currentStep,
                total_steps = _totalSteps
            });
        }

        private string HandleResult()
        {
            if (_status == "running")
                throw new HttpException(409, "Test still running");

            if (_status == "idle")
                throw new HttpException(404, "No test results available");

            // Build result JSON manually for complex nested structures
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"status\":\"{_status}\",");

            // Screenshots array
            sb.Append("\"screenshots\":[");
            var first = true;
            foreach (var kv in _screenshots)
            {
                if (!first) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"name\":\"{EscapeJson(kv.Key)}\",");
                sb.Append($"\"data\":\"{kv.Value}\"");
                sb.Append("}");
                first = false;
            }
            sb.Append("],");

            // Logs array
            sb.Append("\"logs\":[");
            for (int i = 0; i < _logs.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(JsonUtility.ToJson(_logs[i]));
            }
            sb.Append("]");

            if (!string.IsNullOrEmpty(_error))
            {
                sb.Append($",\"error\":\"{EscapeJson(_error)}\"");
            }

            sb.Append("}");

            return sb.ToString();
        }

        private string HandleHealth()
        {
            return JsonUtility.ToJson(new HealthResponse
            {
                status = "ok",
                app = appName,
                platform = "unity"
            });
        }

        private static void SendJson(HttpListenerResponse response, int status, string json)
        {
            response.StatusCode = status;
            response.ContentType = "application/json";

            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            using var output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
        }

        // ─────────────────────────────────────────────
        // Scenario Execution (Main Thread)
        // ─────────────────────────────────────────────

        private IEnumerator ExecuteScenario(string json)
        {
            try
            {
                var scenario = JsonUtility.FromJson<ScenarioRequestWrapper>(json);
                if (scenario == null)
                    throw new Exception("Invalid scenario JSON");

                var steps = scenario.steps ?? new List<ScenarioStep>();
                _totalSteps = steps.Count;
                _currentStep = 0;

                for (int i = 0; i < steps.Count; i++)
                {
                    _currentStep = i + 1;
                    _progress = (float)_currentStep / _totalSteps;
                    var step = steps[i];

                    AddLog("info", $"Step {_currentStep}/{_totalSteps}: {step.type} -> {step.target}");

                    yield return ExecuteStep(step);
                }

                _status = "completed";
                AddLog("info", "Scenario completed successfully");
            }
            catch (Exception ex)
            {
                _status = "failed";
                _error = ex.Message;
                AddLog("error", $"Scenario failed: {ex.Message}");
            }

            _progress = 1f;
        }

        private IEnumerator ExecuteStep(ScenarioStep step)
        {
            switch (step.type)
            {
                case "click":
                    Click(step.target);
                    break;
                case "input":
                    Input(step.target, step.text);
                    break;
                case "wait":
                    yield return new WaitForSeconds(step.ms / 1000f);
                    break;
                case "screenshot":
                    yield return CaptureScreenshot(step.target);
                    break;
                default:
                    throw new Exception($"Unknown step type: {step.type}");
            }
        }

        // ─────────────────────────────────────────────
        // UI Automation Helpers
        // ─────────────────────────────────────────────

        private void Click(string selector)
        {
            var go = GameObject.Find(selector.TrimStart('#'));
            if (go == null)
                throw new Exception($"UI element not found: {selector}");

            var button = go.GetComponent<Button>();
            if (button == null)
                throw new Exception($"Button not found on: {selector}");

            button.onClick.Invoke();
            AddLog("info", $"Clicked {selector}");
        }

        private void Input(string selector, string text)
        {
            var go = GameObject.Find(selector.TrimStart('#'));
            if (go == null)
                throw new Exception($"UI element not found: {selector}");

#if TMP_PRESENT
            var tmpInput = go.GetComponent<TMP_InputField>();
            if (tmpInput != null)
            {
                tmpInput.text = text;
                AddLog("info", $"Input set for {selector}");
                return;
            }
#endif

            var input = go.GetComponent<InputField>();
            if (input == null)
                throw new Exception($"InputField not found on: {selector}");

            input.text = text;
            AddLog("info", $"Input set for {selector}");
        }

        private IEnumerator CaptureScreenshot(string name)
        {
            yield return new WaitForEndOfFrame();

            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var bytes = tex.EncodeToPNG();
            Destroy(tex);

            var base64 = Convert.ToBase64String(bytes);
            _screenshots[name] = base64;
            AddLog("info", $"Screenshot captured: {name}");
        }

        // ─────────────────────────────────────────────
        // Logging
        // ─────────────────────────────────────────────

        private void AddLog(string level, string message)
        {
            _logs.Add(new LogEntry
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                level = level,
                message = message
            });
        }

        // ─────────────────────────────────────────────
        // Models
        // ─────────────────────────────────────────────

        [Serializable]
        private class ScenarioRequestWrapper
        {
            public List<ScenarioStep> steps;
        }

        [Serializable]
        private class ScenarioStep
        {
            public string type;
            public string target;
            public string text;
            public int ms;
        }

        [Serializable]
        private class StatusResponse
        {
            public string status;
            public float progress;
            public int current_step;
            public int total_steps;
        }

        [Serializable]
        private class RunResponse
        {
            public string session_id;
            public string status;
        }

        [Serializable]
        private class HealthResponse
        {
            public string status;
            public string app;
            public string platform;
        }

        [Serializable]
        private class ErrorResponse
        {
            public string error;
        }

        private class LogEntry
        {
            public string timestamp;
            public string level;
            public string message;
        }

        private class HttpException : Exception
        {
            public int StatusCode { get; }

            public HttpException(int statusCode, string message) : base(message)
            {
                StatusCode = statusCode;
            }
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}

#endif // E2E_TESTS
