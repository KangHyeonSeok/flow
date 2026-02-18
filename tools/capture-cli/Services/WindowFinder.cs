using System.Diagnostics;
using System.Text;
using CaptureCli.Interop;
using CaptureCli.Models;

namespace CaptureCli.Services;

/// <summary>
/// 실행 중인 윈도우를 탐색하는 서비스
/// </summary>
internal static class WindowFinder
{
    /// <summary>
    /// 모든 보이는 윈도우 목록 반환
    /// </summary>
    public static List<WindowInfo> GetAllWindows()
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            var length = NativeMethods.GetWindowTextLength(hWnd);
            if (length == 0)
                return true;

            var sb = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (string.IsNullOrWhiteSpace(title))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            var processName = GetProcessName((int)pid);

            windows.Add(new WindowInfo(hWnd, title, processName, (int)pid));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    /// <summary>
    /// 창 제목으로 검색 (부분 일치)
    /// </summary>
    public static List<WindowInfo> FindByName(string name)
    {
        return GetAllWindows()
            .Where(w => w.Title.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 프로세스 이름으로 검색
    /// </summary>
    public static List<WindowInfo> FindByProcess(string processName)
    {
        return GetAllWindows()
            .Where(w => w.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 모니터 HMONITOR 핸들 목록 반환
    /// </summary>
    public static List<(IntPtr Handle, NativeMethods.RECT Bounds, string Name)> GetMonitors()
    {
        var monitors = new List<(IntPtr, NativeMethods.RECT, string)>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
            {
                var info = new NativeMethods.MONITORINFOEX();
                info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>();
                NativeMethods.GetMonitorInfo(hMonitor, ref info);
                monitors.Add((hMonitor, info.rcMonitor, info.szDevice));
                return true;
            }, IntPtr.Zero);

        return monitors;
    }

    private static string GetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return "<unknown>";
        }
    }
}
