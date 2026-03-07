using FlowCLI.Services.Runner;
using Xunit;

namespace FlowCLI.Tests.Runner;

/// <summary>
/// RunnerLogService 테스트.
/// 로그 파일 생성, 내용 기록, 조회 기능을 검증한다.
/// </summary>
public class RunnerLogServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RunnerLogService _log;

    public RunnerLogServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _log = new RunnerLogService(_tempDir, "logs", "test-runner-001");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Info_CreatesLogFile()
    {
        _log.Info("test-action", "테스트 메시지");

        var files = Directory.GetFiles(Path.Combine(_tempDir, "logs"), "runner-*.log");
        Assert.Single(files);
    }

    [Fact]
    public void LogEntry_ContainsExpectedFields()
    {
        _log.Info("daemon-start", "데몬 시작", "F-091");

        var content = _log.ReadLatestLog(100);
        Assert.NotNull(content);
        Assert.Contains("INFO", content);
        Assert.Contains("test-runner-001", content);
        Assert.Contains("daemon-start", content);
        Assert.Contains("데몬 시작", content);
        Assert.Contains("F-091", content);
    }

    [Fact]
    public void Warn_LogsCorrectLevel()
    {
        _log.Warn("recovery", "비정상 종료 감지");

        var content = _log.ReadLatestLog(100);
        Assert.NotNull(content);
        Assert.Contains("WARN", content);
        Assert.Contains("recovery", content);
    }

    [Fact]
    public void Error_LogsCorrectLevel()
    {
        _log.Error("conpty", "ConPTY 연결 실패");

        var content = _log.ReadLatestLog(100);
        Assert.NotNull(content);
        Assert.Contains("ERROR", content);
        Assert.Contains("ConPTY 연결 실패", content);
    }

    [Fact]
    public void ListLogFiles_ReturnsCreatedFiles()
    {
        _log.Info("test", "첫 번째 로그");

        var files = _log.ListLogFiles();
        Assert.NotEmpty(files);
    }

    [Fact]
    public void ReadLatestLog_NoFiles_ReturnsNull()
    {
        // 빈 로그 디렉토리에서 읽기 시도
        var emptyLog = new RunnerLogService(_tempDir, "empty-logs", "test-runner-002");
        // 로그 디렉토리는 생성되지만 파일은 없음
        var content = emptyLog.ReadLatestLog();
        Assert.Null(content);
    }

    [Fact]
    public void ReadLatestLog_TailLines_ReturnsOnlyLastLines()
    {
        for (int i = 0; i < 20; i++)
            _log.Info("cycle", $"사이클 {i}");

        var content = _log.ReadLatestLog(tailLines: 5);
        Assert.NotNull(content);
        var lines = content!.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= 5);
    }
}
