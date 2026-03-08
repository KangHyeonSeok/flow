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

    /// <summary>
    /// 빈 PID 파일이 있을 때 GetRunningInstance는 null을 반환해야 한다.
    /// ConPTY 간섭으로 PID 파일 기록이 시작되기 전에 프로세스가 종료된 엣지 케이스를 처리한다.
    /// </summary>
    [Fact]
    public void GetRunningInstance_EmptyPidFile_ReturnsNull()
    {
        var pidPath = Path.Combine(_flowRoot, "runner.pid");
        File.WriteAllText(pidPath, "");

        var result = RunnerService.GetRunningInstance(_flowRoot);

        Assert.Null(result);
    }

    /// <summary>
    /// 부분적으로 기록된 PID 파일이 있을 때 null을 반환해야 한다.
    /// ConPTY CTRL_C_EVENT 신호로 인해 PID 파일 직렬화 도중 프로세스가 종료된 경우를 시뮬레이션한다.
    /// </summary>
    [Fact]
    public void GetRunningInstance_PartiallyWrittenPidFile_ReturnsNull()
    {
        var pidPath = Path.Combine(_flowRoot, "runner.pid");
        // JSON 기록 중 ConPTY 신호로 인해 쓰기가 중단된 불완전한 파일
        File.WriteAllText(pidPath, "{ \"InstanceId\": \"runner-123-000000\", \"ProcessI");

        var result = RunnerService.GetRunningInstance(_flowRoot);

        Assert.Null(result);
    }

    /// <summary>
    /// PID 파일의 모든 필드가 직렬화/역직렬화 후에도 정확히 보존됨을 검증한다.
    /// ConPTY 크래시 후 인스턴스 정보 무결성 확인에 사용된다.
    /// </summary>
    [Fact]
    public void GetRunningInstance_PidFileFields_ArePreservedCorrectly()
    {
        var expectedInstanceId = "runner-12345-010203";
        var expectedStartedAt = "2026-01-01T00:00:00.0000000Z";
        var expectedPid = Environment.ProcessId;

        var instance = new RunnerInstance
        {
            InstanceId = expectedInstanceId,
            ProcessId = expectedPid,
            StartedAt = expectedStartedAt,
            Status = "running"
        };

        var pidPath = Path.Combine(_flowRoot, "runner.pid");
        File.WriteAllText(pidPath,
            JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true }));

        var result = RunnerService.GetRunningInstance(_flowRoot);

        Assert.NotNull(result);
        Assert.Equal(expectedInstanceId, result!.InstanceId);
        Assert.Equal(expectedPid, result.ProcessId);
        Assert.Equal(expectedStartedAt, result.StartedAt);
        Assert.Equal("running", result.Status);
    }

    /// <summary>
    /// ConPTY 간섭으로 crashed 상태의 인스턴스가 있어도 새 데몬 시작 가능 여부를 판단할 수 있어야 한다.
    /// crashed 상태는 null 이 아닌 RunnerInstance를 반환하여 호출자가 상태를 명확히 판단하게 한다.
    /// </summary>
    [Fact]
    public void GetRunningInstance_ConPtyCrashSimulation_ReturnsCrashedNotNull()
    {
        // ConPTY 크래시 시뮬레이션: VS Code 터미널 닫기 → CTRL_C_EVENT 브로드캐스트 → 데몬 즉시 종료
        var crashedInstance = new RunnerInstance
        {
            InstanceId = "runner-99999-conpty-crash",
            ProcessId = int.MaxValue - 2, // 존재하지 않는 PID
            StartedAt = DateTime.UtcNow.AddMinutes(-45).ToString("o"),
            Status = "running" // 정상 종료 없이 종료되어 "running"으로 남음
        };

        var pidPath = Path.Combine(_flowRoot, "runner.pid");
        File.WriteAllText(pidPath,
            JsonSerializer.Serialize(crashedInstance, new JsonSerializerOptions { WriteIndented = true }));

        var result = RunnerService.GetRunningInstance(_flowRoot);

        // crashed 상태로 반환 (null이 아님) → 호출자가 crashed 여부를 확인 후 새 데몬 시작 가능
        Assert.NotNull(result);
        Assert.Equal("crashed", result!.Status);
        Assert.Equal("runner-99999-conpty-crash", result.InstanceId);
    }

    /// <summary>
    /// 여러 다른 PID 파일이 각각 독립적으로 관리됨을 검증한다.
    /// 동시에 여러 runner 설정이 있어도 서로 간섭하지 않아야 한다.
    /// </summary>
    [Fact]
    public void GetRunningInstance_MultipleCustomPidFiles_AreIndependent()
    {
        const string pidFile1 = "runner-a.pid";
        const string pidFile2 = "runner-b.pid";

        var instance = new RunnerInstance
        {
            InstanceId = "runner-a-instance",
            ProcessId = Environment.ProcessId,
            StartedAt = DateTime.UtcNow.ToString("o"),
            Status = "running"
        };

        var pidPath1 = Path.Combine(_flowRoot, pidFile1);
        File.WriteAllText(pidPath1,
            JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true }));

        // pidFile1은 running, pidFile2는 없음
        var result1 = RunnerService.GetRunningInstance(_flowRoot, pidFile1);
        var result2 = RunnerService.GetRunningInstance(_flowRoot, pidFile2);

        Assert.NotNull(result1);
        Assert.Equal("running", result1!.Status);
        Assert.Null(result2);
    }
}
