namespace CaptureCli.Models;

/// <summary>
/// 창 정보를 나타내는 레코드
/// </summary>
public record WindowInfo(IntPtr Handle, string Title, string ProcessName, int ProcessId);
