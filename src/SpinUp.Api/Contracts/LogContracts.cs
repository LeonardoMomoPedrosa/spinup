namespace SpinUp.Api.Contracts;

public record ServiceLogEntry(
    long Sequence,
    Guid ServiceId,
    DateTimeOffset Timestamp,
    string Stream,
    string Message);

public record ServiceLogListResponse(
    Guid ServiceId,
    IReadOnlyList<ServiceLogEntry> Logs);

public record StreamEvent(
    string Type,
    DateTimeOffset Timestamp,
    Guid ServiceId,
    object Payload);
