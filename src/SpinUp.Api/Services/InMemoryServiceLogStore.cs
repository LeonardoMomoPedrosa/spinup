using System.Collections.Concurrent;
using SpinUp.Api.Contracts;

namespace SpinUp.Api.Services;

public class InMemoryServiceLogStore(LogBufferOptions options) : IServiceLogStore
{
    private readonly int _maxLinesPerService = Math.Max(10, options.MaxLinesPerService);
    private readonly ConcurrentDictionary<Guid, ServiceLogBuffer> _buffers = new();

    public ServiceLogEntry Append(Guid serviceId, string stream, string message)
    {
        var buffer = _buffers.GetOrAdd(serviceId, _ => new ServiceLogBuffer(_maxLinesPerService));
        return buffer.Append(serviceId, stream, message);
    }

    public IReadOnlyList<ServiceLogEntry> GetRecent(Guid serviceId, int take = 200, DateTimeOffset? since = null)
    {
        if (!_buffers.TryGetValue(serviceId, out var buffer))
        {
            return [];
        }

        var cappedTake = Math.Clamp(take, 1, 1000);
        return buffer.GetRecent(cappedTake, since);
    }

    public void Clear(Guid serviceId)
    {
        _buffers.TryRemove(serviceId, out _);
    }

    private sealed class ServiceLogBuffer(int max)
    {
        private readonly int _max = max;
        private readonly Queue<ServiceLogEntry> _entries = new();
        private long _sequence;
        private readonly object _sync = new();

        public ServiceLogEntry Append(Guid serviceId, string stream, string message)
        {
            lock (_sync)
            {
                _sequence++;
                var entry = new ServiceLogEntry(
                    _sequence,
                    serviceId,
                    DateTimeOffset.UtcNow,
                    stream,
                    message);

                _entries.Enqueue(entry);
                while (_entries.Count > _max)
                {
                    _entries.Dequeue();
                }

                return entry;
            }
        }

        public IReadOnlyList<ServiceLogEntry> GetRecent(int take, DateTimeOffset? since)
        {
            lock (_sync)
            {
                IEnumerable<ServiceLogEntry> query = _entries;
                if (since.HasValue)
                {
                    query = query.Where(x => x.Timestamp >= since.Value);
                }

                return query.TakeLast(take).ToList();
            }
        }
    }
}
