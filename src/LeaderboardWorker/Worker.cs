using System.Text.Json;
using LeaderboardWorker.Services;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Data;
using Shared.Domain.Events;
using Shared.Messaging;

namespace LeaderboardWorker;

public class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory scopeFactory,
    IConnectionFactory connectionFactory,
    LeaderboardService leaderboard
) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectWithRetryAsync(stoppingToken);

        await _channel!.ExchangeDeclareAsync(
            MessageBusConstants.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);

        await _channel!.QueueDeclareAsync(
            queue: MessageBusConstants.Queues.LeaderboardWorker,
            durable: true, exclusive: false, autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel!.QueueBindAsync(
            queue: MessageBusConstants.Queues.LeaderboardWorker,
            exchange: MessageBusConstants.Exchange,
            routingKey: MessageBusConstants.RoutingKeys.ScoreCalculated,
            cancellationToken: stoppingToken);

        // One message at a time — Redis writes are fast but we want ordered processing per user
        await _channel!.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel!.BasicConsumeAsync(
            queue: MessageBusConstants.Queues.LeaderboardWorker,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("LeaderboardWorker listening on queue '{Queue}'", MessageBusConstants.Queues.LeaderboardWorker);

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        ScoreCalculatedEvent? evt = null;
        try
        {
            evt = JsonSerializer.Deserialize<ScoreCalculatedEvent>(ea.Body.Span, JsonOptions);
            if (evt is null)
            {
                logger.LogWarning("Received null or undeserializable ScoreCalculatedEvent — nacking");
                await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            await ProcessEventAsync(evt, ea.DeliveryTag);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error processing ScoreCalculatedEvent {IdempotencyKey}",
                evt?.IdempotencyKey ?? "<unknown>");
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private async Task ProcessEventAsync(ScoreCalculatedEvent evt, ulong deliveryTag)
    {
        // ── Idempotency check (Redis SET NX) ──────────────────────────────────
        bool isNew = await leaderboard.TryMarkProcessedAsync(evt.IdempotencyKey);
        if (!isNew)
        {
            logger.LogDebug("Skipping duplicate ScoreCalculated event {IdempotencyKey}", evt.IdempotencyKey);
            await _channel!.BasicAckAsync(deliveryTag, multiple: false);
            return;
        }

        // ── Find which users own this player in this gameweek ─────────────────
        // ScoreCalculated is player-centric. We fan out to every user whose squad
        // contains this player for the matching gameweek (bench players excluded).
        // Captain multiplier (×2) is applied here.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ownerEntries = await (
            from sp in db.SquadPlayers
            join s  in db.Squads on sp.SquadId equals s.Id
            join u  in db.Users  on s.UserId  equals u.Id
            where sp.PlayerId == evt.PlayerId
               && s.Gameweek  == evt.Gameweek
               && !sp.IsBench
            select new { s.UserId, sp.IsCaptain, u.LeagueId }
        ).ToListAsync();

        if (ownerEntries.Count == 0)
        {
            logger.LogDebug(
                "No active squad owners for player {PlayerId} in gameweek {Gameweek} — acking",
                evt.PlayerId, evt.Gameweek);
            await _channel!.BasicAckAsync(deliveryTag, multiple: false);
            return;
        }

        // ── Fan-out: update Redis for each owning user ────────────────────────
        foreach (var entry in ownerEntries)
        {
            double effectivePoints = entry.IsCaptain ? evt.Points * 2.0 : evt.Points;

            await leaderboard.IncrementUserScoreAsync(entry.UserId, entry.LeagueId, effectivePoints);

            logger.LogInformation(
                "Leaderboard updated — user={UserId} gameweek={Gameweek} player={PlayerId} " +
                "points={Points}{CaptainFlag} league={LeagueId}",
                entry.UserId, evt.Gameweek, evt.PlayerId,
                effectivePoints, entry.IsCaptain ? " (captain ×2)" : "",
                entry.LeagueId ?? "—");
        }

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
        if (_channel    is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
