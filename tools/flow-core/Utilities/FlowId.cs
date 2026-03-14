namespace FlowCore.Utilities;

/// <summary>prefix + 8자리 hex UUID ID 생성</summary>
public static class FlowId
{
    public static string New(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
}
