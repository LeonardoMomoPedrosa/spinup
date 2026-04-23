namespace SpinUp.Api.Models;

public class ServiceDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string? Args { get; set; }
    public Dictionary<string, string> Env { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
