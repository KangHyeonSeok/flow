using FlowCLI.Utils;
using Xunit;

namespace FlowCLI.Tests.Runner;

/// <summary>
/// ConPTY 안정성 수정 관련 테스트.
/// VS Code ConPTY 터미널에서 CTRL_C_EVENT 브로드캐스트로 인한 데몬 즉시 종료 문제 방지 로직을 검증한다.
/// </summary>
public class ConsoleHelperTests
{
    /// <summary>
    /// DetachConsoleForDaemon은 플랫폼에 무관하게 예외를 던지지 않아야 한다.
    /// Windows에서는 SetConsoleCtrlHandler(null, true)로 CTRL_C_EVENT 무시 등록,
    /// 비Windows에서는 no-op이다.
    /// </summary>
    [Fact]
    public void DetachConsoleForDaemon_DoesNotThrow()
    {
        var ex = Record.Exception(() => ConsoleHelper.DetachConsoleForDaemon());
        Assert.Null(ex);
    }

    /// <summary>
    /// DetachConsoleForDaemon은 반복 호출 시에도 예외를 던지지 않아야 한다.
    /// 데몬 재시작 시나리오를 커버한다.
    /// </summary>
    [Fact]
    public void DetachConsoleForDaemon_CalledMultipleTimes_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            ConsoleHelper.DetachConsoleForDaemon();
            ConsoleHelper.DetachConsoleForDaemon();
            ConsoleHelper.DetachConsoleForDaemon();
        });
        Assert.Null(ex);
    }
}
