namespace SpinUp.Api.Contracts;

public record ApiError(
    string Code,
    string Message,
    IDictionary<string, string[]>? Details = null);
