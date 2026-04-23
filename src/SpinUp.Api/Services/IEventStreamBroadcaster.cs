using System.Threading.Channels;
using SpinUp.Api.Contracts;

namespace SpinUp.Api.Services;

public interface IEventStreamBroadcaster
{
    Guid Subscribe(Channel<StreamEvent> channel);
    void Unsubscribe(Guid subscriptionId);
    ValueTask PublishAsync(StreamEvent @event, CancellationToken cancellationToken = default);
}
