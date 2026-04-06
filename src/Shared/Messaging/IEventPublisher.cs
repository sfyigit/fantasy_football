namespace Shared.Messaging;

public interface IEventPublisher
{
    Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : class;
}
