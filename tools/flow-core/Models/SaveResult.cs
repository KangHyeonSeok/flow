namespace FlowCore.Models;

/// <summary>저장 결과 상태</summary>
public enum SaveStatus
{
    Success,
    Conflict,
    ValidationError,
    IOError
}

/// <summary>저장 연산 결과</summary>
public sealed class SaveResult
{
    public required SaveStatus Status { get; init; }
    public int? CurrentVersion { get; init; }
    public string? Message { get; init; }
    public bool IsSuccess => Status == SaveStatus.Success;

    public static SaveResult Ok() => new() { Status = SaveStatus.Success };

    public static SaveResult ConflictAt(int currentVersion) =>
        new() { Status = SaveStatus.Conflict, CurrentVersion = currentVersion };

    public static SaveResult Validation(string message) =>
        new() { Status = SaveStatus.ValidationError, Message = message };

    public static SaveResult IoError(string message) =>
        new() { Status = SaveStatus.IOError, Message = message };
}
