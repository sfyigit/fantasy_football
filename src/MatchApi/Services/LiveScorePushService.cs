using System.Net.WebSockets;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Contracts;
using Shared.Domain.Events;
using Shared.Messaging;

namespace MatchApi.Services;

/// <summary>
/// Background service that consumes <see cref="MatchUpdatedEvent"/> from RabbitMQ and
/// fans out opcode 1004 <c>LIVE_SCORE_UPDATE</c> pushes to all subscribed WebSocket clients.
///
/// This is the server-initiated (S→C) path for live score updates.
/// </summary>
public class LiveScorePushService(
    IConnectionFactory connectionFactory,
    SubscriptionManager subscriptions,
    ILogger<LiveScorePushService> logger
) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = ApiJsonOptions.Options;
    private const string QueueName = "match-api.match-updated";

    private IConnection? _connection;
    private IChannel?    _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectWithRetryAsync(stoppingToken);

        await _channel!.ExchangeDeclareAsync(
            MessageBusConstants.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);

        await _channel!.QueueDeclareAsync(
            queue: QueueName, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel!.QueueBindAsync(
            queue: QueueName,
            exchange: MessageBusConstants.Exchange,
            routingKey: MessageBusConstants.RoutingKeys.MatchUpdated,
            cancellationToken: stoppingToken);

        await _channel!.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMatchUpdatedAsync;

        await _channel!.BasicConsumeAsync(
            queue: QueueName, autoAck: true, consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("LiveScorePushService listening on queue '{Queue}'", QueueName);

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task OnMatchUpdatedAsync(object sender, BasicDeliverEventArgs ea)
    {
        MatchUpdatedEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<MatchUpdatedEvent>(ea.Body.Span, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to deserialise MatchUpdatedEvent");
            return;
        }

        if (evt is null) return;

        var subscribers = subscriptions.GetSubscribers(evt.MatchId);
        if (subscribers.Count == 0) return;

        var push = OpcodeResponse.Ok(
            Opcode.LiveScoreUpdate,
            Guid.NewGuid().ToString(),
            new LiveScoreUpdateData(
                MatchId:   evt.MatchId,
                Minute:    evt.Minute,
                Score:     new MatchScoreDto(evt.ScoreHome, evt.ScoreAway),
                EventType: null,
                PlayerId:  null));

        var bytes = JsonSerializer.SerializeToUtf8Bytes(push, JsonOpts);

        foreach (var ws in subscribers)
        {
            if (ws.State != WebSocketState.Open) continue;
            try
            {
                await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to push 1004 to a subscriber for match {MatchId}", evt.MatchId);
                subscriptions.UnregisterAll(ws);
            }
        }

        logger.LogDebug("Pushed 1004 LIVE_SCORE_UPDATE for match {MatchId} to {Count} subscriber(s)",
            evt.MatchId, subscribers.Count);
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _connection = await connectionFactory.CreateConnectionAsync(ct);
                _channel    = await _connection.CreateChannelAsync(cancellationToken: ct);
                logger.LogInformation("LiveScorePushService connected to RabbitMQ");
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RabbitMQ not ready — retrying in {Delay}s", delay.TotalSeconds);
                await Task.Delay(delay, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel    is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
