using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using SpinUp.Api.Contracts;
using SpinUp.Api.Data;
using SpinUp.Api.Models;

namespace SpinUp.Api.Services;

public class ServiceRuntimeManager(
    IServiceScopeFactory scopeFactory,
    ILogger<ServiceRuntimeManager> logger,
    IServiceLogStore logStore,
    IEventStreamBroadcaster broadcaster) : IServiceRuntimeManager
{
    private const int StopTimeoutSeconds = 10;
    private const int StartupWarmupSeconds = 2;
    private const int StartupHealthTimeoutSeconds = 30;
    private const string HealthCheckUrlEnvKey = "SPINUP_HEALTHCHECK_URL";
    private const string StartupTimeoutSecondsEnvKey = "SPINUP_STARTUP_TIMEOUT_SECONDS";
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly ConcurrentDictionary<Guid, ManagedProcess> _processes = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<Guid, ServiceRuntimeResponse> _runtime = new();
    private readonly ConcurrentDictionary<Guid, bool> _stopRequested = new();
    private readonly ConcurrentDictionary<Guid, Uri> _healthCheckUrls = new();
    private readonly ConcurrentDictionary<Guid, int> _startupTimeoutSeconds = new();
    /// <summary>
    /// When user stops a service that SpinUp did not start (health-only UP, no managed PID),
    /// we keep status Down until Start succeeds, so health polling does not immediately flip back to Up.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte> _externalHealthSuppressed = new();

    public async Task<RuntimeActionResponse> StartAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        var gate = GetLock(serviceId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var service = await GetServiceAsync(serviceId, cancellationToken);
            if (service is null)
            {
                return Fail(serviceId, "not_found", $"Service '{serviceId}' was not found.");
            }

            if (_processes.TryGetValue(serviceId, out var existing) && !existing.Process.HasExited)
            {
                return Fail(serviceId, "already_running", $"Service '{service.Name}' is already running.");
            }

            if (!Directory.Exists(service.Path))
            {
                var message = $"Service path '{service.Path}' does not exist.";
                UpdateRuntime(serviceId, RuntimeStatus.Error, null, null, null, message);
                PublishRuntimeSnapshot(serviceId);
                return Fail(serviceId, "invalid_path", message);
            }

            var healthCheckConfigErrorForProbe = ConfigureHealthCheck(service);
            if (healthCheckConfigErrorForProbe is not null)
            {
                UpdateRuntime(serviceId, RuntimeStatus.Error, null, null, null, healthCheckConfigErrorForProbe);
                PublishRuntimeSnapshot(serviceId);
                return Fail(serviceId, "invalid_healthcheck", healthCheckConfigErrorForProbe);
            }

            if (_healthCheckUrls.TryGetValue(serviceId, out var probeUri)
                && await IsHealthEndpointSuccessAsync(probeUri, cancellationToken))
            {
                return Fail(
                    serviceId,
                    "already_running",
                    $"Service '{service.Name}' health check already succeeds (another process may own this port). Stop that process, then start from SpinUp.");
            }

            UpdateRuntime(serviceId, RuntimeStatus.Starting, null, null, null, null);
            PublishRuntimeSnapshot(serviceId);
            _startupTimeoutSeconds[serviceId] = ResolveStartupTimeoutSeconds(service);

            var (fileName, baseArgs) = ParseCommand(service.Command);
            var combinedArgs = string.IsNullOrWhiteSpace(service.Args)
                ? baseArgs
                : string.IsNullOrWhiteSpace(baseArgs) ? service.Args : $"{baseArgs} {service.Args}";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = combinedArgs,
                    WorkingDirectory = service.Path,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            foreach (var pair in service.Env)
            {
                process.StartInfo.Environment[pair.Key] = pair.Value;
            }

            process.Exited += (_, _) => HandleProcessExit(serviceId, process);
            process.OutputDataReceived += (_, args) => HandleLogLine(serviceId, "stdout", args.Data);
            process.ErrorDataReceived += (_, args) => HandleLogLine(serviceId, "stderr", args.Data);

            try
            {
                if (!process.Start())
                {
                    var message = $"Failed to start service '{service.Name}'.";
                    UpdateRuntime(serviceId, RuntimeStatus.Error, null, null, null, message);
                    return Fail(serviceId, "start_failed", message);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed starting service {ServiceId}", serviceId);
                UpdateRuntime(serviceId, RuntimeStatus.Error, null, null, null, ex.Message);
                return Fail(serviceId, "start_failed", ex.Message);
            }

            _externalHealthSuppressed.TryRemove(serviceId, out _);

            var startedAt = DateTimeOffset.UtcNow;
            _processes[serviceId] = new ManagedProcess(process, startedAt);
            _stopRequested[serviceId] = false;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var runtime = UpdateRuntime(serviceId, RuntimeStatus.Starting, process.Id, startedAt, null, null);
            PublishRuntimeSnapshot(serviceId);
            _ = PromoteToUpWhenReadyAsync(serviceId, process, CancellationToken.None);
            return new RuntimeActionResponse(true, runtime);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RuntimeActionResponse> StopAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        var gate = GetLock(serviceId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var service = await GetServiceAsync(serviceId, cancellationToken);
            if (service is null)
            {
                return Fail(serviceId, "not_found", $"Service '{serviceId}' was not found.");
            }

            if (!_processes.TryGetValue(serviceId, out var managed) || managed.Process.HasExited)
            {
                var runtimeNow = GetRuntime(serviceId);
                if (runtimeNow.Status is RuntimeStatus.Up or RuntimeStatus.Starting)
                {
                    _externalHealthSuppressed[serviceId] = 1;
                    _ = ConfigureHealthCheck(service);
                    _startupTimeoutSeconds[serviceId] = ResolveStartupTimeoutSeconds(service);
                    var suppressedDown = UpdateRuntime(serviceId, RuntimeStatus.Down, null, null, null, null);
                    PublishRuntimeSnapshot(serviceId);
                    return new RuntimeActionResponse(true, suppressedDown);
                }

                var idleDown = UpdateRuntime(serviceId, RuntimeStatus.Down, null, null, null, null);
                return new RuntimeActionResponse(true, idleDown);
            }

            _stopRequested[serviceId] = true;
            UpdateRuntime(serviceId, RuntimeStatus.Stopping, managed.Process.Id, managed.StartedAt, null, null);
            PublishRuntimeSnapshot(serviceId);

            try
            {
                if (!managed.Process.CloseMainWindow())
                {
                    managed.Process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                managed.Process.Kill(entireProcessTree: true);
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(StopTimeoutSeconds));
                await managed.Process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!managed.Process.HasExited)
                {
                    managed.Process.Kill(entireProcessTree: true);
                    await managed.Process.WaitForExitAsync(cancellationToken);
                }
            }

            int? exitCode = managed.Process.HasExited ? managed.Process.ExitCode : null;
            _processes.TryRemove(serviceId, out _);
            _healthCheckUrls.TryRemove(serviceId, out _);
            _startupTimeoutSeconds.TryRemove(serviceId, out _);
            _externalHealthSuppressed.TryRemove(serviceId, out _);
            var runtime = UpdateRuntime(serviceId, RuntimeStatus.Down, null, null, exitCode, null);
            PublishRuntimeSnapshot(serviceId);
            managed.Process.Dispose();
            return new RuntimeActionResponse(true, runtime);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RuntimeActionResponse> RestartAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        var stopResult = await StopAsync(serviceId, cancellationToken);
        if (!stopResult.Success && stopResult.Code != "not_found")
        {
            return stopResult;
        }

        return await StartAsync(serviceId, cancellationToken);
    }

    public async Task<BulkRuntimeActionResponse> StartAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<RuntimeActionResponse>();
        var ids = await GetAllServiceIdsAsync(cancellationToken);
        foreach (var id in ids)
        {
            results.Add(await StartAsync(id, cancellationToken));
        }

        return new BulkRuntimeActionResponse(results);
    }

    public async Task<BulkRuntimeActionResponse> StopAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<RuntimeActionResponse>();
        var ids = await GetAllServiceIdsAsync(cancellationToken);
        foreach (var id in ids)
        {
            results.Add(await StopAsync(id, cancellationToken));
        }

        return new BulkRuntimeActionResponse(results);
    }

    public ServiceRuntimeResponse GetRuntime(Guid serviceId)
    {
        return _runtime.TryGetValue(serviceId, out var runtime)
            ? runtime
            : new ServiceRuntimeResponse(serviceId, RuntimeStatus.Down, null, null, null, null);
    }

    public IReadOnlyList<ServiceRuntimeResponse> GetAllRuntime()
    {
        return _runtime.Values.OrderBy(x => x.ServiceId).ToList();
    }

    public async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        await RefreshHealthChecksFromDefinitionsAsync(cancellationToken);

        foreach (var (serviceId, managed) in _processes.ToArray())
        {
            if (managed.Process.HasExited)
            {
                continue;
            }

            if (!_healthCheckUrls.TryGetValue(serviceId, out var healthCheckUri))
            {
                continue;
            }

            await CheckServiceHealthAsync(serviceId, managed, healthCheckUri, cancellationToken);
        }

        foreach (var (serviceId, healthCheckUri) in _healthCheckUrls.ToArray())
        {
            // Still tracked (even if exit cleanup is pending) — never treat as "external only" here.
            if (_processes.ContainsKey(serviceId))
            {
                continue;
            }

            await CheckExternalServiceHealthAsync(serviceId, healthCheckUri, cancellationToken);
        }
    }

    private void HandleProcessExit(Guid serviceId, Process process)
    {
        var stopRequested = _stopRequested.TryGetValue(serviceId, out var requested) && requested;
        var exitCode = SafeExitCode(process);

        _processes.TryRemove(serviceId, out _);
        _healthCheckUrls.TryRemove(serviceId, out _);
        _startupTimeoutSeconds.TryRemove(serviceId, out _);
        _externalHealthSuppressed.TryRemove(serviceId, out _);

        if (stopRequested || exitCode == 0)
        {
            UpdateRuntime(serviceId, RuntimeStatus.Down, null, null, exitCode, null);
            PublishRuntimeSnapshot(serviceId);
        }
        else
        {
            var message = $"Process exited unexpectedly with code {exitCode}.";
            UpdateRuntime(serviceId, RuntimeStatus.Error, null, null, exitCode, message);
            PublishRuntimeSnapshot(serviceId);
        }

        _stopRequested[serviceId] = false;
    }

    private static int? SafeExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch
        {
            return null;
        }
    }

    private SemaphoreSlim GetLock(Guid serviceId)
    {
        return _locks.GetOrAdd(serviceId, _ => new SemaphoreSlim(1, 1));
    }

    private static (string FileName, string Arguments) ParseCommand(string command)
    {
        var value = command.Trim();
        if (value.StartsWith('"'))
        {
            var endQuote = value.IndexOf('"', 1);
            if (endQuote > 1)
            {
                var fileName = value[1..endQuote];
                var args = value[(endQuote + 1)..].Trim();
                return (fileName, args);
            }
        }

        var firstSpace = value.IndexOf(' ');
        if (firstSpace <= 0)
        {
            return (value, string.Empty);
        }

        return (value[..firstSpace], value[(firstSpace + 1)..].Trim());
    }

    private async Task<ServiceDefinition?> GetServiceAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpinUpDbContext>();
        return await db.ServiceDefinitions.FirstOrDefaultAsync(x => x.Id == serviceId, cancellationToken);
    }

    private async Task<List<Guid>> GetAllServiceIdsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpinUpDbContext>();
        return await db.ServiceDefinitions
            .OrderBy(x => x.Name)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    private RuntimeActionResponse Fail(Guid serviceId, string code, string message)
    {
        return new RuntimeActionResponse(false, GetRuntime(serviceId), code, message);
    }

    private async Task CheckServiceHealthAsync(
        Guid serviceId,
        ManagedProcess managed,
        Uri healthCheckUri,
        CancellationToken cancellationToken)
    {
        if (managed.Process.HasExited)
        {
            return;
        }

        var current = GetRuntime(serviceId);
        RuntimeStatus nextStatus;
        string? nextError;

        try
        {
            var response = await HealthClient.GetAsync(healthCheckUri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                nextStatus = RuntimeStatus.Up;
                nextError = null;
            }
            else
            {
                nextStatus = RuntimeStatus.Down;
                nextError = null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            nextStatus = RuntimeStatus.Down;
            nextError = null;
        }

        if (current.Status == nextStatus && string.Equals(current.LastError, nextError, StringComparison.Ordinal))
        {
            return;
        }

        UpdateRuntime(
            serviceId,
            nextStatus,
            managed.Process.Id,
            managed.StartedAt,
            current.LastExitCode,
            nextError);
        PublishRuntimeSnapshot(serviceId);
    }

    private async Task CheckExternalServiceHealthAsync(
        Guid serviceId,
        Uri healthCheckUri,
        CancellationToken cancellationToken)
    {
        var current = GetRuntime(serviceId);
        if (_externalHealthSuppressed.ContainsKey(serviceId))
        {
            if (current.Status is not RuntimeStatus.Down)
            {
                UpdateRuntime(serviceId, RuntimeStatus.Down, null, null, current.LastExitCode, null);
                PublishRuntimeSnapshot(serviceId);
            }

            return;
        }

        RuntimeStatus nextStatus;
        string? nextError;

        try
        {
            var response = await HealthClient.GetAsync(healthCheckUri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                nextStatus = RuntimeStatus.Up;
                nextError = null;
            }
            else
            {
                nextStatus = RuntimeStatus.Down;
                nextError = null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            nextStatus = RuntimeStatus.Down;
            nextError = null;
        }

        if (current.Status == nextStatus
            && current.Pid is null
            && string.Equals(current.LastError, nextError, StringComparison.Ordinal))
        {
            return;
        }

        UpdateRuntime(
            serviceId,
            nextStatus,
            null,
            current.StartedAt,
            current.LastExitCode,
            nextError);
        PublishRuntimeSnapshot(serviceId);
    }

    private async Task PromoteToUpWhenReadyAsync(Guid serviceId, Process process, CancellationToken cancellationToken)
    {
        try
        {
            if (_healthCheckUrls.TryGetValue(serviceId, out var healthCheckUri))
            {
                var startupTimeoutSeconds = _startupTimeoutSeconds.TryGetValue(serviceId, out var configuredTimeout)
                    ? configuredTimeout
                    : StartupHealthTimeoutSeconds;
                var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(startupTimeoutSeconds);
                while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < timeoutAt)
                {
                    if (process.HasExited)
                    {
                        return;
                    }

                    try
                    {
                        var response = await HealthClient.GetAsync(healthCheckUri, cancellationToken);
                        if (response.IsSuccessStatusCode)
                        {
                            PromoteToUp(serviceId, process);
                            return;
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                    {
                        // Continue probing until timeout.
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }

                if (!process.HasExited)
                {
                    var message = $"Startup health check did not become ready within {startupTimeoutSeconds} seconds.";
                    UpdateRuntime(serviceId, RuntimeStatus.Error, process.Id, GetRuntime(serviceId).StartedAt, null, message);
                    PublishRuntimeSnapshot(serviceId);
                }

                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(StartupWarmupSeconds), cancellationToken);
            if (!process.HasExited)
            {
                PromoteToUp(serviceId, process);
            }
        }
        catch (OperationCanceledException)
        {
            // Startup check cancelled by request shutdown.
        }
    }

    private void PromoteToUp(Guid serviceId, Process process)
    {
        var current = GetRuntime(serviceId);
        if (current.Status is RuntimeStatus.Down or RuntimeStatus.Error or RuntimeStatus.Stopping)
        {
            return;
        }

        UpdateRuntime(serviceId, RuntimeStatus.Up, process.Id, current.StartedAt, current.LastExitCode, null);
        PublishRuntimeSnapshot(serviceId);
    }

    private ServiceRuntimeResponse UpdateRuntime(
        Guid serviceId,
        RuntimeStatus status,
        int? pid,
        DateTimeOffset? startedAt,
        int? lastExitCode,
        string? lastError)
    {
        var runtime = new ServiceRuntimeResponse(serviceId, status, pid, startedAt, lastExitCode, lastError);
        _runtime[serviceId] = runtime;
        return runtime;
    }

    private void HandleLogLine(Guid serviceId, string stream, string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return;
        }

        var entry = logStore.Append(serviceId, stream, data);
        _ = broadcaster.PublishAsync(new StreamEvent("log", entry.Timestamp, serviceId, entry));
    }

    private void PublishRuntimeSnapshot(Guid serviceId)
    {
        var r = GetRuntime(serviceId);
        var payload = new
        {
            status = r.Status,
            message = r.LastError,
            exitCode = r.LastExitCode,
            pid = r.Pid,
            startedAt = r.StartedAt
        };
        _ = broadcaster.PublishAsync(new StreamEvent("runtime", DateTimeOffset.UtcNow, serviceId, payload));
    }

    private string? ConfigureHealthCheck(ServiceDefinition service)
    {
        if (!service.Env.TryGetValue(HealthCheckUrlEnvKey, out var value) || string.IsNullOrWhiteSpace(value))
        {
            _healthCheckUrls.TryRemove(service.Id, out _);
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            _healthCheckUrls.TryRemove(service.Id, out _);
            return $"Configured health check URL is invalid: '{value}'.";
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            _healthCheckUrls.TryRemove(service.Id, out _);
            return $"Configured health check URL must use HTTP or HTTPS: '{value}'.";
        }

        _healthCheckUrls[service.Id] = uri;
        return null;
    }

    private int ResolveStartupTimeoutSeconds(ServiceDefinition service)
    {
        if (!service.Env.TryGetValue(StartupTimeoutSecondsEnvKey, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return StartupHealthTimeoutSeconds;
        }

        if (!int.TryParse(value.Trim(), out var timeoutSeconds))
        {
            logger.LogWarning(
                "Service {ServiceId} has invalid {EnvKey} value '{Value}'. Using default {DefaultSeconds}s.",
                service.Id,
                StartupTimeoutSecondsEnvKey,
                value,
                StartupHealthTimeoutSeconds);
            return StartupHealthTimeoutSeconds;
        }

        var clampedTimeout = Math.Clamp(timeoutSeconds, 5, 600);
        if (clampedTimeout != timeoutSeconds)
        {
            logger.LogWarning(
                "Service {ServiceId} configured {EnvKey}={ConfiguredSeconds} outside supported range; using {EffectiveSeconds}s.",
                service.Id,
                StartupTimeoutSecondsEnvKey,
                timeoutSeconds,
                clampedTimeout);
        }

        return clampedTimeout;
    }

    private async Task RefreshHealthChecksFromDefinitionsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpinUpDbContext>();
        var definitions = await db.ServiceDefinitions.ToListAsync(cancellationToken);

        foreach (var service in definitions)
        {
            _ = ConfigureHealthCheck(service);
        }
    }

    private static async Task<bool> IsHealthEndpointSuccessAsync(Uri healthCheckUri, CancellationToken cancellationToken)
    {
        try
        {
            var response = await HealthClient.GetAsync(healthCheckUri, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private sealed record ManagedProcess(Process Process, DateTimeOffset StartedAt);
}
