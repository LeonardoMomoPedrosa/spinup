using System.Collections.Concurrent;
using System.Threading.Channels;
using SpinUp.Api.Contracts;

namespace SpinUp.Api.Services;

public class EventStreamBroadcaster : IEventStreamBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<StreamEvent>> _subscribers = new();

    public Guid Subscribe(Channel<StreamEvent> channel)
    {
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return id;
    }

    public void Unsubscribe(Guid subscriptionId)
    {
        _subscribers.TryRemove(subscriptionId, out _);
    }

    public ValueTask PublishAsync(StreamEvent @event, CancellationToken cancellationToken = default)
    {
        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(@event);
        }

        return ValueTask.CompletedTask;
    }
}
