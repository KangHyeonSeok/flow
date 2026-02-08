// E2EServer.cs - Unity E2E Test HTTP Server
// 
// Unity 앱에 내장되는 HTTP 서버. E2E 테스트 도구로부터 시나리오를 수신하고
// UI 자동화를 실행한 뒤 결과(스크린샷, 로그)를 반환합니다.
//
// 사용법:
//   1. 이 파일을 Assets/Scripts/E2E/ 폴더에 복사
//   2. 빈 GameObject에 E2EServer 컴포넌트 추가
//   3. Scripting Define Symbols에 E2E_TESTS 추가
//
// 엔드포인트:
//   POST /e2e/run    - 시나리오 제출 및 실행
//   GET  /e2e/status - 실행 상태 조회
//   GET  /e2e/result - 결과 수집 (스크린샷, 로그)
//   GET  /e2e/health - 헬스체크

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
            first = true;
            lock (_logs)
            {
                foreach (var log in _logs)
                {
                    if (!first) sb.Append(",");
                    sb.Append("{");
                    sb.Append($"\"timestamp\":\"{EscapeJson(log.timestamp)}\",");
                    sb.Append($"\"level\":\"{EscapeJson(log.level)}\",");
                    sb.Append($"\"message\":\"{EscapeJson(log.message)}\"");
                    sb.Append("}");
                    first = false;
                }
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

        // ─────────────────────────────────────────────
        // Scenario Execution (Main Thread)
        // ─────────────────────────────────────────────

        private IEnumerator ExecuteScenario(string scenarioJson)
        {
            ScenarioData scenario;

            try
            {
                scenario = JsonUtility.FromJson<ScenarioData>(scenarioJson);
            }
            catch (Exception ex)
            {
                _error = $"Failed to parse scenario: {ex.Message}";
                _status = "failed";
                AddLog("error", _error);
                yield break;
            }

            _totalSteps = scenario.steps?.Length ?? 0;
            AddLog("info", $"Executing {_totalSteps} steps");

            if (scenario.steps != null)
            {
                for (int i = 0; i < scenario.steps.Length; i++)
                {
                    _currentStep = i + 1;
                    _progress = (float)_currentStep / _totalSteps;

                    var step = scenario.steps[i];
                    AddLog("info", $"Step {_currentStep}/{_totalSteps}: {step.type} → {step.target}");

                    bool success = false;
                    yield return ExecuteStep(step, result => success = result);

                    if (!success)
                    {
                        _error = $"Step {_currentStep} failed: {step.type} on {step.target}";
                        _status = "failed";
                        AddLog("error", _error);
                        yield break;
                    }
                }
            }

            _progress = 1.0f;
            _status = "completed";
            AddLog("info", "All steps completed successfully");
        }

        /// <summary>
        /// Execute a single test step. Override for custom step types.
        /// </summary>
        protected virtual IEnumerator ExecuteStep(StepData step, Action<bool> callback)
        {
            try
            {
                switch (step.type?.ToLower())
                {
                    case "input":
                        ExecuteInput(step);
                        callback(true);
                        break;

                    case "click":
                        ExecuteClick(step);
                        callback(true);
                        break;

                    case "select":
                        ExecuteSelect(step);
                        callback(true);
                        break;

                    case "wait":
                        yield return new WaitForSeconds((step.ms > 0 ? step.ms : 1000) / 1000f);
                        callback(true);
                        break;

                    case "screenshot":
                        yield return CaptureScreenshot(step.target ?? "screenshot");
                        callback(true);
                        break;

                    default:
                        // Try custom step handler
                        yield return ExecuteCustomStep(step, callback);
                        break;
                }
            }
            catch (Exception ex)
            {
                AddLog("error", $"Step execution error: {ex.Message}");
                callback(false);
            }
        }

        /// <summary>
        /// Override this method to handle custom step types specific to your app.
        /// </summary>
        protected virtual IEnumerator ExecuteCustomStep(StepData step, Action<bool> callback)
        {
            AddLog("warn", $"Unknown step type: {step.type}");
            callback(false);
            yield break;
        }

        // ─────────────────────────────────────────────
        // Step Implementations
        // ─────────────────────────────────────────────

        private void ExecuteInput(StepData step)
        {
            var go = FindUIElement(step.target);

#if TMP_PRESENT
            if (go.TryGetComponent<TMP_InputField>(out var tmpInput))
            {
                tmpInput.text = step.text ?? "";
                tmpInput.onValueChanged?.Invoke(tmpInput.text);
                AddLog("info", $"Input '{step.text}' into {step.target}");
                return;
            }
#endif

            if (go.TryGetComponent<InputField>(out var input))
            {
                input.text = step.text ?? "";
                input.onValueChanged?.Invoke(input.text);
                AddLog("info", $"Input '{step.text}' into {step.target}");
                return;
            }

            throw new Exception($"No InputField found on {step.target}");
        }

        private void ExecuteClick(StepData step)
        {
            var go = FindUIElement(step.target);

            if (go.TryGetComponent<Button>(out var button))
            {
                button.onClick?.Invoke();
                AddLog("info", $"Clicked {step.target}");
                return;
            }

            throw new Exception($"No Button found on {step.target}");
        }

        private void ExecuteSelect(StepData step)
        {
            var go = FindUIElement(step.target);

#if TMP_PRESENT
            if (go.TryGetComponent<TMP_Dropdown>(out var tmpDropdown))
            {
                var idx = tmpDropdown.options.FindIndex(o => o.text == step.value);
                if (idx < 0) throw new Exception($"Option '{step.value}' not found in {step.target}");
                tmpDropdown.value = idx;
                AddLog("info", $"Selected '{step.value}' in {step.target}");
                return;
            }
#endif

            if (go.TryGetComponent<Dropdown>(out var dropdown))
            {
                var idx = dropdown.options.FindIndex(o => o.text == step.value);
                if (idx < 0) throw new Exception($"Option '{step.value}' not found in {step.target}");
                dropdown.value = idx;
                AddLog("info", $"Selected '{step.value}' in {step.target}");
                return;
            }

            throw new Exception($"No Dropdown found on {step.target}");
        }

        private IEnumerator CaptureScreenshot(string name)
        {
            yield return new WaitForEndOfFrame();

            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var png = tex.EncodeToPNG();
            _screenshots[name] = Convert.ToBase64String(png);
            Destroy(tex);

            AddLog("info", $"Screenshot captured: {name}");
        }

        // ─────────────────────────────────────────────
        // UI Element Finder
        // ─────────────────────────────────────────────

        /// <summary>
        /// Find a UI element by target selector.
        /// Supports:
        ///   #name  - Find by GameObject name
        ///   .tag   - Find by tag
        ///   /path  - Find by hierarchy path
        /// </summary>
        protected GameObject FindUIElement(string target)
        {
            if (string.IsNullOrEmpty(target))
                throw new Exception("Target selector is empty");

            GameObject go = null;

            if (target.StartsWith("#"))
            {
                // Find by name
                var name = target[1..];
                go = GameObject.Find(name) ?? FindInactiveByName(name);
            }
            else if (target.StartsWith("."))
            {
                // Find by tag
                go = GameObject.FindWithTag(target[1..]);
            }
            else if (target.StartsWith("/"))
            {
                // Find by path
                go = GameObject.Find(target);
            }
            else
            {
                // Try direct name search
                go = GameObject.Find(target) ?? FindInactiveByName(target);
            }

            if (go == null)
                throw new Exception($"UI element not found: {target}");

            return go;
        }

        private static GameObject FindInactiveByName(string name)
        {
            // Search all root objects and their children (including inactive)
            foreach (var root in UnityEngine.SceneManagement.SceneManager
                         .GetActiveScene().GetRootGameObjects())
            {
                var found = root.transform.Find(name);
                if (found != null) return found.gameObject;

                // Deep recursive search
                var result = FindInChildren(root.transform, name);
                if (result != null) return result;
            }
            return null;
        }

        private static GameObject FindInChildren(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child.gameObject;

                var result = FindInChildren(child, name);
                if (result != null) return result;
            }
            return null;
        }

        // ─────────────────────────────────────────────
        // Utilities
        // ─────────────────────────────────────────────

        private void AddLog(string level, string message)
        {
            lock (_logs)
            {
                _logs.Add(new LogEntry
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    level = level,
                    message = message
                });
            }
            Debug.Log($"[E2E][{level}] {message}");
        }

        private static void SendJson(HttpListenerResponse response, int statusCode, string json)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        // ─────────────────────────────────────────────
        // Data Models (JSON serializable)
        // ─────────────────────────────────────────────

        [Serializable]
        private class ScenarioData
        {
            public MetaData meta;
            public StepData[] steps;
        }

        [Serializable]
        private class MetaData
        {
            public string app;
            public string platform;
            public string resolution;
            public int timeout;
        }

        [Serializable]
        public class StepData
        {
            public string type;
            public string target;
            public string text;
            public string value;
            public int ms;
        }

        [Serializable]
        private class LogEntry
        {
            public string timestamp;
            public string level;
            public string message;
        }

        [Serializable]
        private class RunResponse
        {
            public string session_id;
            public string status;
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

        private class HttpException : Exception
        {
            public int StatusCode { get; }
            public HttpException(int statusCode, string message) : base(message)
            {
                StatusCode = statusCode;
            }
        }
    }
}

#endif // E2E_TESTS
