using SpinUp.Api.Contracts;

namespace SpinUp.Api.Services;

public interface IServiceLogStore
{
    ServiceLogEntry Append(Guid serviceId, string stream, string message);
    IReadOnlyList<ServiceLogEntry> GetRecent(Guid serviceId, int take = 200, DateTimeOffset? since = null);
    void Clear(Guid serviceId);
}
