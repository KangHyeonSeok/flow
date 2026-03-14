using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FlowCore.Backend;

/// <summary>크로스 플랫폼 프로세스 종료 헬퍼</summary>
public static class ProcessKiller
{
    /// <summary>
    /// 프로세스를 graceful하게 종료한다.
    /// Windows: 즉시 Kill(entireProcessTree).
    /// macOS/Linux: SIGTERM → gracePeriod 대기 → SIGKILL fallback.
    /// </summary>
    public static async Task GracefulKillAsync(Process process, TimeSpan gracePeriod)
    {
        try
        {
            if (process.HasExited)
                return;
        }
        catch (InvalidOperationException)
        {
            return; // 이미 종료됨
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            KillTree(process);
            return;
        }

        // POSIX: SIGTERM 먼저 시도
        try
        {
            using var sigterm = Process.Start(new ProcessStartInfo
            {
                FileName = "kill",
                ArgumentList = { "-15", process.Id.ToString() },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            sigterm?.WaitForExit(1000);
        }
        catch
        {
            // kill 명령 실행 실패 시 바로 Kill fallback
            KillTree(process);
            return;
        }

        // gracePeriod 동안 종료 대기
        try
        {
            using var cts = new CancellationTokenSource(gracePeriod);
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 아직 살아있으면 SIGKILL (Kill)
            KillTree(process);
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }
}
