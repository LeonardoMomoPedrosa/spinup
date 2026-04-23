using Microsoft.EntityFrameworkCore;
using SpinUp.Api.Data;

namespace SpinUp.Api.Services;

public sealed class ServiceReadinessProbe(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    SpinUpDbContext dbContext)
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public async Task<(bool IsReady, List<string> Issues)> CheckReadinessAsync(CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();

        try
        {
            await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            issues.Add($"Database connection failed: {ex.Message}");
        }

        var storageRoot = ResolveStorageRoot();
        try
        {
            Directory.CreateDirectory(storageRoot);
            var probeFile = Path.Combine(storageRoot, ".spinup-write-probe");
            await File.WriteAllTextAsync(probeFile, "ok", cancellationToken);
            File.Delete(probeFile);
        }
        catch (Exception ex)
        {
            issues.Add($"Storage path is not writable ('{storageRoot}'): {ex.Message}");
        }

        return (issues.Count == 0, issues);
    }

    public object BuildStartupDiagnostics()
    {
        var process = Environment.ProcessPath ?? "unknown";
        var entryAssembly = typeof(ServiceReadinessProbe).Assembly.GetName().Version?.ToString() ?? "unknown";

        return new
        {
            startedAt = _startedAt,
            environment = environment.EnvironmentName,
            machine = Environment.MachineName,
            processPath = process,
            entryAssemblyVersion = entryAssembly,
            dbConnection = configuration.GetConnectionString("SpinUp") ?? "Data Source=spinup.db",
            urls = configuration["ASPNETCORE_URLS"] ?? "not-set",
            storageRoot = ResolveStorageRoot()
        };
    }

    private string ResolveStorageRoot()
    {
        var configured = configuration["ServiceHosting:StorageRoot"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SpinUp");
    }
}
