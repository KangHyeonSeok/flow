using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace FlowCore.Backend;

/// <summary>
/// Copilot CLI ACP 백엔드.
/// ACP(Agent Client Protocol): JSON-RPC 2.0 over NDJSON (stdin/stdout).
/// 프로세스는 장기 실행, 세션은 RunPromptAsync 호출 단위로 생성·폐기.
/// </summary>
public sealed class CopilotAcpBackend : ICliBackend
{
    private readonly string _command;
    private readonly string _defaultMode;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Process? _process;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private Task? _stderrDrainTask;
    private bool _initialized;
    private int _nextId = 1;

    /// <summary>write 계열 tool 이름 패턴 (file edit 권한 필요)</summary>
    private static readonly HashSet<string> WriteTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "write_file", "edit_file", "create_file", "delete_file",
        "rename_file", "move_file", "patch_file",
        "Write", "Edit", "MultiEdit"
    };

    public string BackendId => "copilot-acp";

    public CopilotAcpBackend(string command = "copilot", string defaultMode = "code")
    {
        _command = command;
        _defaultMode = defaultMode;
    }

    public async Task<CliResponse> RunPromptAsync(
        string prompt, CliBackendOptions options, CancellationToken ct = default)
    {
        try
        {
            await _lock.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return new CliResponse
            {
                ResponseText = string.Empty,
                Success = false,
                ErrorMessage = "cancelled",
                StopReason = CliStopReason.Cancelled
            };
        }

        try
        {
            return await RunPromptCoreAsync(prompt, options, ct);
        }
        catch (OperationCanceledException)
        {
            return new CliResponse
            {
                ResponseText = string.Empty,
                Success = false,
                ErrorMessage = "cancelled",
                StopReason = CliStopReason.Cancelled
            };
        }
        catch (Exception ex)
        {
            // 프로세스가 예상치 못하게 종료된 경우 재시작 준비
            await KillProcessAsync();
            return new CliResponse
            {
                ResponseText = string.Empty,
                Success = false,
                ErrorMessage = $"ACP error: {ex.Message}",
                StopReason = CliStopReason.Error
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<CliResponse> RunPromptCoreAsync(
        string prompt, CliBackendOptions options, CancellationToken ct)
    {
        // 1. 프로세스 확보 + initialize
        await EnsureProcessAsync(ct);

        using var hardCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        hardCts.CancelAfter(options.HardTimeout);
        var token = hardCts.Token;

        // 2. session/new
        var sessionResult = await SendRequestAsync<SessionNewResult>(
            "session/new",
            new SessionNewParams { Cwd = options.WorkingDirectory },
            token);

        var sessionId = sessionResult?.SessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            return new CliResponse
            {
                ResponseText = string.Empty,
                Success = false,
                ErrorMessage = "session/new returned no sessionId",
                StopReason = CliStopReason.Error
            };
        }

        try
        {
            // 3. session/set_mode
            await SendRequestAsync<object>(
                "session/set_mode",
                new SessionSetModeParams { SessionId = sessionId, Mode = _defaultMode },
                token);

            // 4. session/prompt
            var promptId = _nextId++;
            await SendRawRequestAsync(new AcpRequest
            {
                Id = promptId,
                Method = "session/prompt",
                Params = new SessionPromptParams { SessionId = sessionId, Prompt = prompt }
            }, token);

            // 5. 응답 수집: session/update 스트리밍 + request_permission 핸들링
            var responseText = new StringBuilder();
            string? stopReason = null;
            var lastOutputTime = DateTime.UtcNow;
            var idleTimeout = options.IdleTimeout;

            while (stopReason == null)
            {
                // idle timeout 검사
                var elapsed = DateTime.UtcNow - lastOutputTime;
                var remaining = idleTimeout - elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    await TryCancelSessionAsync(sessionId);
                    return new CliResponse
                    {
                        ResponseText = responseText.ToString(),
                        Success = false,
                        ErrorMessage = $"idle timeout ({idleTimeout.TotalSeconds}s without output)",
                        StopReason = CliStopReason.Timeout
                    };
                }

                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                idleCts.CancelAfter(remaining);

                string? line;
                try
                {
                    line = await _reader!.ReadLineAsync(idleCts.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    await TryCancelSessionAsync(sessionId);
                    return new CliResponse
                    {
                        ResponseText = responseText.ToString(),
                        Success = false,
                        ErrorMessage = $"idle timeout ({idleTimeout.TotalSeconds}s without output)",
                        StopReason = CliStopReason.Timeout
                    };
                }

                if (line == null)
                {
                    // EOF — 프로세스 종료됨
                    _initialized = false;
                    return new CliResponse
                    {
                        ResponseText = responseText.ToString(),
                        Success = false,
                        ErrorMessage = "ACP process terminated unexpectedly",
                        StopReason = CliStopReason.Error
                    };
                }

                if (string.IsNullOrWhiteSpace(line)) continue;
                lastOutputTime = DateTime.UtcNow;

                var msg = JsonSerializer.Deserialize<AcpRawMessage>(line, AcpJsonOptions.Default);
                if (msg == null) continue;

                // JSON-RPC 응답 (prompt response with stopReason)
                if (msg.Id == promptId && msg.Result.HasValue)
                {
                    var result = msg.Result.Value;
                    if (result.TryGetProperty("stopReason", out var sr))
                        stopReason = sr.GetString();
                    if (result.TryGetProperty("responseText", out var rt))
                        responseText.Append(rt.GetString());
                    break;
                }

                // notification: session/update
                if (msg.IsNotification && msg.Method == "session/update" && msg.Params.HasValue)
                {
                    var update = JsonSerializer.Deserialize<SessionUpdateParams>(
                        msg.Params.Value.GetRawText(), AcpJsonOptions.Default);
                    if (update != null)
                    {
                        if (update.Message != null)
                            responseText.Append(update.Message);
                        if (update.ResponseText != null)
                            responseText.Append(update.ResponseText);
                        if (update.StopReason != null)
                        {
                            stopReason = update.StopReason;
                            break;
                        }
                    }
                }

                // request (id + method): request_permission
                if (msg.Id != null && msg.Method == "request_permission")
                {
                    var action = "allow_always";

                    // AllowFileEdits가 false면 write 계열 tool은 거부
                    if (!options.AllowFileEdits && msg.Params.HasValue)
                    {
                        var permReq = JsonSerializer.Deserialize<RequestPermissionParams>(
                            msg.Params.Value.GetRawText(), AcpJsonOptions.Default);
                        if (permReq?.Tool != null && WriteTools.Contains(permReq.Tool))
                            action = "deny";
                    }

                    await SendRawResponseAsync(msg.Id.Value,
                        new PermissionResponse { Action = action }, token);
                }

                // error
                if (msg.Error != null)
                {
                    return new CliResponse
                    {
                        ResponseText = responseText.ToString(),
                        Success = false,
                        ErrorMessage = $"ACP error {msg.Error.Code}: {msg.Error.Message}",
                        StopReason = CliStopReason.Error
                    };
                }
            }

            // stopReason 해석
            var success = stopReason is "end_turn" or null;
            var cliStopReason = stopReason switch
            {
                "end_turn" => CliStopReason.Completed,
                "max_tokens" => CliStopReason.Completed,
                "cancelled" => CliStopReason.Cancelled,
                "refusal" => CliStopReason.Error,
                _ => CliStopReason.Completed
            };

            return new CliResponse
            {
                ResponseText = responseText.ToString(),
                Success = success,
                ErrorMessage = stopReason == "refusal" ? "agent refused the prompt" : null,
                StopReason = cliStopReason
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Hard timeout
            await TryCancelSessionAsync(sessionId);
            return new CliResponse
            {
                ResponseText = string.Empty,
                Success = false,
                ErrorMessage = $"hard timeout ({options.HardTimeout.TotalSeconds}s)",
                StopReason = CliStopReason.Timeout
            };
        }
    }

    // ── 프로세스 관리 ──

    private async Task EnsureProcessAsync(CancellationToken ct)
    {
        if (_process != null && !_process.HasExited && _initialized)
            return;

        // 기존 프로세스 정리
        await KillProcessAsync();

        var (fileName, prefixArgs) = ResolveCommand();

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in prefixArgs)
            psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add("--acp");
        psi.ArgumentList.Add("--stdio");

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start ACP process: {fileName}");

        _writer = _process.StandardInput;
        _reader = _process.StandardOutput;

        // stderr를 비동기로 drain하여 버퍼 데드락 방지
        _stderrDrainTask = Task.Run(async () =>
        {
            try { await _process.StandardError.ReadToEndAsync(); }
            catch { /* 프로세스 종료 시 무시 */ }
        });

        // initialize handshake
        var result = await SendRequestAsync<InitializeResult>(
            "initialize", new InitializeParams(), ct);

        if (result == null)
            throw new InvalidOperationException("ACP initialize failed: no response");

        _initialized = true;
    }

    /// <summary>Windows .ps1/.bat 래퍼 감지</summary>
    private (string fileName, List<string> prefixArgs) ResolveCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _command.EndsWith(".ps1"))
            return ("pwsh", ["-NonInteractive", "-File", _command]);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _command.EndsWith(".bat"))
            return ("cmd.exe", ["/c", _command]);

        return (_command, []);
    }

    private async Task KillProcessAsync()
    {
        _initialized = false;
        if (_process == null) return;

        try
        {
            if (!_process.HasExited)
                await ProcessKiller.GracefulKillAsync(_process, TimeSpan.FromSeconds(3));
        }
        catch { }

        _process.Dispose();
        _process = null;
        _writer = null;
        _reader = null;
    }

    // ── JSON-RPC 통신 ──

    private async Task<T?> SendRequestAsync<T>(string method, object? parameters, CancellationToken ct)
    {
        var id = _nextId++;
        await SendRawRequestAsync(new AcpRequest
        {
            Id = id,
            Method = method,
            Params = parameters
        }, ct);

        // 응답 읽기 (notification 건너뛰기)
        while (true)
        {
            var line = await _reader!.ReadLineAsync(ct);
            if (line == null)
                throw new InvalidOperationException($"ACP process EOF while waiting for {method} response");
            if (string.IsNullOrWhiteSpace(line)) continue;

            var response = JsonSerializer.Deserialize<AcpResponse<T>>(line, AcpJsonOptions.Default);
            if (response == null) continue;

            // 매칭되는 응답
            if (response.Id == id)
            {
                if (response.Error != null)
                    throw new InvalidOperationException(
                        $"ACP {method} error {response.Error.Code}: {response.Error.Message}");
                return response.Result;
            }

            // notification → 무시하고 다음 줄 읽기
        }
    }

    private async Task SendRawRequestAsync(AcpRequest request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, AcpJsonOptions.Default);
        await _writer!.WriteLineAsync(json.AsMemory(), ct);
        await _writer.FlushAsync(ct);
    }

    private async Task SendRawResponseAsync(int id, object result, CancellationToken ct)
    {
        var response = new { jsonrpc = "2.0", id, result };
        var json = JsonSerializer.Serialize(response, AcpJsonOptions.Default);
        await _writer!.WriteLineAsync(json.AsMemory(), ct);
        await _writer.FlushAsync(ct);
    }

    private async Task TryCancelSessionAsync(string sessionId)
    {
        try
        {
            if (_process != null && !_process.HasExited)
            {
                await SendRawRequestAsync(new AcpRequest
                {
                    Id = _nextId++,
                    Method = "session/cancel",
                    Params = new SessionCancelParams { SessionId = sessionId }
                }, CancellationToken.None);
            }
        }
        catch { /* best-effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        await KillProcessAsync();
        _lock.Dispose();
    }
}
