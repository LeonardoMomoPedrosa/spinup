namespace SpinUp.Api.Contracts;

public enum RuntimeStatus
{
    Down,
    Starting,
    Up,
    Stopping,
    Error
}

public record ServiceRuntimeResponse(
    Guid ServiceId,
    RuntimeStatus Status,
    int? Pid,
    DateTimeOffset? StartedAt,
    int? LastExitCode,
    string? LastError);

public record RuntimeActionResponse(
    bool Success,
    ServiceRuntimeResponse Runtime,
    string? Code = null,
    string? Message = null);

public record BulkRuntimeActionResponse(
    IReadOnlyList<RuntimeActionResponse> Results);
