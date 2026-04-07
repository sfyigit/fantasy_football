using System.Text.Json;
using RabbitMQ.Client;
using Shared.Messaging;

namespace ScoringWorker.Services;

public sealed class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly IConnectionFactory _factory;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqEventPublisher(IConnectionFactory factory, ILogger<RabbitMqEventPublisher> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    private async Task EnsureConnectedAsync()
    {
        if (_connection is null || !_connection.IsOpen)
            _connection = await _factory.CreateConnectionAsync();

        if (_channel is null || !_channel.IsOpen)
        {
            _channel = await _connection.CreateChannelAsync();
            await _channel.ExchangeDeclareAsync(MessageBusConstants.Exchange, ExchangeType.Topic, durable: true);
        }
    }

    public async Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : class
    {
        await EnsureConnectedAsync();

        var routingKey = typeof(T).Name;
        var body = JsonSerializer.SerializeToUtf8Bytes(@event);

        await _channel!.BasicPublishAsync(
            exchange: MessageBusConstants.Exchange,
            routingKey: routingKey,
            body: body,
            cancellationToken: ct);

        _logger.LogDebug("Published {EventType}", typeof(T).Name);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
