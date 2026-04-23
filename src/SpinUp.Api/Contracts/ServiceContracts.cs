using SpinUp.Api.Models;

namespace SpinUp.Api.Contracts;

public record ServiceCreateRequest(
    string Name,
    string Path,
    string Command,
    string? Args,
    Dictionary<string, string>? Env);

public record ServiceUpdateRequest(
    string Name,
    string Path,
    string Command,
    string? Args,
    Dictionary<string, string>? Env);

public record ServiceResponse(
    Guid Id,
    string Name,
    string Path,
    string Command,
    string? Args,
    Dictionary<string, string> Env,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ServiceResponse FromEntity(ServiceDefinition entity)
    {
        return new ServiceResponse(
            entity.Id,
            entity.Name,
            entity.Path,
            entity.Command,
            entity.Args,
            entity.Env,
            entity.CreatedAt,
            entity.UpdatedAt);
    }
}
