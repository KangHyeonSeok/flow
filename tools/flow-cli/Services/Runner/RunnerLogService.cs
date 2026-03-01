using System.Text.Encodings.Web;
using System.Text.Json;

namespace FlowCLI.Services.Runner;

/// <summary>
/// Runner 실행 로그를 파일에 기록하는 서비스.
/// .flow/logs/runner-{date}.log 형식.
/// </summary>
public class RunnerLogService
{
    private readonly string _logDir;
    private readonly string _instanceId;
    private static readonly object Lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public RunnerLogService(string flowRoot, string logSubDir, string instanceId)
    {
        _logDir = Path.Combine(flowRoot, logSubDir);
        _instanceId = instanceId;
        Directory.CreateDirectory(_logDir);
    }

    public void Info(string action, string message, string? specId = null)
        => Write("INFO", action, message, specId);

    public void Warn(string action, string message, string? specId = null)
        => Write("WARN", action, message, specId);

    public void Error(string action, string message, string? specId = null)
        => Write("ERROR", action, message, specId);

    private void Write(string level, string action, string message, string? specId)
    {
        var entry = new RunnerLogEntry
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            Level = level,
            InstanceId = _instanceId,
            SpecId = specId,
            Action = action,
            Message = message
        };

        var line = entry.ToString();
        var logPath = GetCurrentLogPath();

        lock (Lock)
        {
            File.AppendAllText(logPath, line + Environment.NewLine);
        }

        // stderr에도 출력 (인터랙티브 디버깅용)
        var color = level switch
        {
            "ERROR" => ConsoleColor.Red,
            "WARN" => ConsoleColor.Yellow,
            _ => ConsoleColor.Gray
        };
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Error.WriteLine(line);
        Console.ForegroundColor = prev;
    }

    /// <summary>현재 날짜 기반 로그 파일 경로</summary>
    private string GetCurrentLogPath()
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return Path.Combine(_logDir, $"runner-{date}.log");
    }

    /// <summary>로그 파일 목록 반환</summary>
    public List<string> ListLogFiles()
    {
        if (!Directory.Exists(_logDir))
            return new List<string>();

        return Directory.GetFiles(_logDir, "runner-*.log")
            .OrderDescending()
            .ToList();
    }

    /// <summary>최근 로그 파일 내용 반환</summary>
    public string? ReadLatestLog(int tailLines = 50)
    {
        var files = ListLogFiles();
        if (files.Count == 0) return null;

        var lines = File.ReadAllLines(files[0]);
        var start = Math.Max(0, lines.Length - tailLines);
        return string.Join(Environment.NewLine, lines[start..]);
    }
}
