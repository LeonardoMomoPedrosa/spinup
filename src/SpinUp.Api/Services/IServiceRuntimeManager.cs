using SpinUp.Api.Contracts;

namespace SpinUp.Api.Services;

public interface IServiceRuntimeManager
{
    Task<RuntimeActionResponse> StartAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<RuntimeActionResponse> StopAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<RuntimeActionResponse> RestartAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<BulkRuntimeActionResponse> StartAllAsync(CancellationToken cancellationToken = default);
    Task<BulkRuntimeActionResponse> StopAllAsync(CancellationToken cancellationToken = default);
    Task CheckHealthAsync(CancellationToken cancellationToken = default);
    ServiceRuntimeResponse GetRuntime(Guid serviceId);
    IReadOnlyList<ServiceRuntimeResponse> GetAllRuntime();
}
