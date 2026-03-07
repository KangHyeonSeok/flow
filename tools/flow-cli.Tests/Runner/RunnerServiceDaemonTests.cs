using System.Text.Json;
using FlowCLI.Services.Runner;
using Xunit;

namespace FlowCLI.Tests.Runner;

/// <summary>
/// RunnerService 데몬 안정성 테스트.
/// ConPTY 환경에서 데몬이 비정상 종료될 때의 복구 로직과 PID 파일 관리를 검증한다.
/// </summary>
public class RunnerServiceDaemonTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _flowRoot;

    public RunnerServiceDaemonTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-daemon-test-{Guid.NewGuid():N}");
        _flowRoot = _tempDir;
        Directory.CreateDirectory(_flowRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>
    /// PID 파일이 없으면 GetRunningInstance는 null을 반환해야 한다.
    /// 새로운 데몬을 안전하게 시작할 수 있는지 확인하는 사전 조건이다.
    /// </summary>
    [Fact]
    public void GetRunningInstance_NoPidFile_ReturnsNull()
    {
        var instance = RunnerService.GetRunningInstance(_flowRoot);
        Assert.Null(instance);
    }

    /// <summary>
    /// 존재하지 않는 프로세스 ID로 PID 파일이 있으면 "crashed" 상태를 반환해야 한다.
    /// ConPTY 간섭으로 데몬이 비정상 종료된 후의 상태를 시뮬레이션한다.
    /// </summary>
    [Fact]
    public void GetRunningInstance_StalePidFile_ReturnsCrashedStatus()
    {
        // 존재하지 않을 PID (매우 큰 값)
        var staleInstance = new RunnerInstance
        {
            InstanceId = "runner-32348-030557",
            ProcessId = int.MaxValue - 1,
            StartedAt = DateTime.UtcNow.AddMinutes(-30).ToString("o"),
            Status = "running"
        };

        var pidPath = Path.Combine(_flowRoot, "runner.pid");
        File.WriteAllText(pidPath, JsonSerializer.Serialize(staleInstance, new JsonSerializerOptions { WriteIndented = true }));

        var result = RunnerService.GetRunningInstance(_flowRoot);

        Assert.NotNull(result);
        Assert.Equal("crashed", result!.Status);
        Assert.Equal("runner-32348-030557", result.InstanceId);
    }

    /// <summary>
    /// 현재 프로세스 ID로 PID 파일이 있으면 "running" 상태를 반환해야 한다.
    /// 정상 실행 중인 데몬의 상태 확인 로직을 검증한다.
    /// </summary>
    [Fact]
    public void GetRunningInstance_ValidPidFile_ReturnsRunningStatus()
    {
        var activeInstance = new RunnerInstance
        {
            InstanceId = $"runner-{Environment.ProcessId}-test",
            ProcessId = Environment.ProcessId, // 현재 프로세스 (테스트 러너)
            StartedAt = DateTime.UtcNow.ToString("o"),
            Status = "running"
        };

        var pidPath = Path.Combine(_flowRoot, "runner.pid");
        File.WriteAllText(pidPath, JsonSerializer.Serialize(activeInstance, new JsonSerializerOptions { WriteIndented = true }));

        var result = RunnerService.GetRunningInstance(_flowRoot);

        Assert.NotNull(result);
        Assert.Equal("running", result!.Status);
        Assert.Equal(Environment.ProcessId, result.ProcessId);
    }

    /// <summary>
    /// 손상된 PID 파일(JSON 파싱 불가)이 있을 때 GetRunningInstance는 null을 반환해야 한다.
    /// ConPTY 간섭으로 PID 파일이 불완전하게 기록된 경우를 커버한다.
    /// </summary>
    [Fact]
    public void GetRunningInstance_CorruptedPidFile_ReturnsNull()
    {
        var pidPath = Path.Combine(_flowRoot, "runner.pid");
        File.WriteAllText(pidPath, "{ invalid json content !!!"); // 손상된 JSON

        var result = RunnerService.GetRunningInstance(_flowRoot);

        Assert.Null(result);
    }

    /// <summary>
    /// 커스텀 PID 파일 이름을 지정해도 정상 동작해야 한다.
    /// </summary>
    [Fact]
    public void GetRunningInstance_CustomPidFileName_WorksCorrectly()
    {
        const string customPidFile = "custom-runner.pid";
        var result = RunnerService.GetRunningInstance(_flowRoot, customPidFile);
        Assert.Null(result);
    }
}
