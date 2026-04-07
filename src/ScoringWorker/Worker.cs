using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Data;
using Shared.Domain.Entities;
using Shared.Domain.Events;
using Shared.Messaging;
using ScoringWorker.Services;

namespace ScoringWorker;

public class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory scopeFactory,
    IConnectionFactory connectionFactory,
    IEventPublisher publisher
) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectWithRetryAsync(stoppingToken);

        // Declare exchange + queue and bind
        await _channel!.ExchangeDeclareAsync(
            MessageBusConstants.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);

        await _channel!.QueueDeclareAsync(
            queue: MessageBusConstants.Queues.ScoringWorker,
            durable: true, exclusive: false, autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            queue: MessageBusConstants.Queues.ScoringWorker,
            exchange: MessageBusConstants.Exchange,
            routingKey: MessageBusConstants.RoutingKeys.PlayerStatUpdated,
            cancellationToken: stoppingToken);

        // One message at a time — prevents overload and simplifies idempotency
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: MessageBusConstants.Queues.ScoringWorker,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("ScoringWorker listening on queue '{Queue}'", MessageBusConstants.Queues.ScoringWorker);

        // Keep the hosted service alive until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        PlayerStatUpdatedEvent? evt = null;
        try
        {
            evt = JsonSerializer.Deserialize<PlayerStatUpdatedEvent>(ea.Body.Span, JsonOptions);
            if (evt is null)
            {
                logger.LogWarning("Received null or undeserializable PlayerStatUpdatedEvent — nacking");
                await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            await ProcessEventAsync(evt, ea.DeliveryTag);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error processing PlayerStatUpdatedEvent {IdempotencyKey}",
                evt?.IdempotencyKey ?? "<unknown>");

            // Requeue false — send to dead-letter if configured, prevents infinite loop
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private async Task ProcessEventAsync(PlayerStatUpdatedEvent evt, ulong deliveryTag)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // ── Idempotency check ─────────────────────────────────────────────────
        bool alreadyProcessed = await db.PlayerGameweekScores
            .AnyAsync(s => s.IdempotencyKey == evt.IdempotencyKey);

        if (alreadyProcessed)
        {
            logger.LogDebug("Skipping duplicate event {IdempotencyKey}", evt.IdempotencyKey);
            await _channel!.BasicAckAsync(deliveryTag, multiple: false);
            return;
        }

        // ── Fetch player (needed for position-based scoring) ──────────────────
        var player = await db.Players.FindAsync(evt.PlayerId);
        if (player is null)
        {
            logger.LogWarning("Player {PlayerId} not found — nacking event {IdempotencyKey}",
                evt.PlayerId, evt.IdempotencyKey);
            await _channel!.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
            return;
        }

        // ── Fetch match (needed for gameweek) ─────────────────────────────────
        var match = await db.Matches.FindAsync(evt.MatchId);
        if (match is null)
        {
            logger.LogWarning("Match {MatchId} not found — nacking event {IdempotencyKey}",
                evt.MatchId, evt.IdempotencyKey);
            await _channel!.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
            return;
        }

        // ── Calculate points ──────────────────────────────────────────────────
        int points = ScoringEngine.Calculate(evt.EventType, player.Position);

        // ── Persist ───────────────────────────────────────────────────────────
        db.PlayerGameweekScores.Add(new PlayerGameweekScore
        {
            PlayerId       = evt.PlayerId,
            MatchId        = evt.MatchId,
            Gameweek       = match.Gameweek,
            Points         = points,
            EventType      = evt.EventType,
            IdempotencyKey = evt.IdempotencyKey,
            ProcessedAt    = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Scored {Points} pts for player {PlayerId} [{Position}] — {EventType} in match {MatchId} (key: {Key})",
            points, evt.PlayerId, player.Position, evt.EventType, evt.MatchId, evt.IdempotencyKey);

        // ── Publish ScoreCalculated ───────────────────────────────────────────
        await publisher.PublishAsync(new ScoreCalculatedEvent(
            PlayerId:       evt.PlayerId,
            MatchId:        evt.MatchId,
            Gameweek:       match.Gameweek,
            Points:         points,
            IdempotencyKey: evt.IdempotencyKey));

        await _channel!.BasicAckAsync(deliveryTag, multiple: false);
    }

    // ── Connection bootstrap with retry ──────────────────────────────────────

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _connection = await connectionFactory.CreateConnectionAsync(ct);
                _channel    = await _connection.CreateChannelAsync(cancellationToken: ct);
                logger.LogInformation("Connected to RabbitMQ");
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
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
