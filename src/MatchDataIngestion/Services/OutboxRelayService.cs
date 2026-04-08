using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Shared.Data;
using Shared.Messaging;

namespace MatchDataIngestion.Services;

/// <summary>
/// Polls the OutboxMessages table every 5 seconds and publishes any pending rows to RabbitMQ.
/// This decouples the DB write from the broker publish, eliminating the dual-write risk:
/// if the process crashes after writing a Fixture but before publishing the event, the
/// OutboxMessage row survives and will be published on the next relay cycle.
/// </summary>
public sealed class OutboxRelayService(
    IServiceScopeFactory scopeFactory,
    IConnectionFactory connectionFactory,
    ILogger<OutboxRelayService> logger
) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int MaxAttemptsBeforeAbandon = 5;

    private IConnection? _connection;
    private IChannel?    _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectWithRetryAsync(stoppingToken);

        await _channel!.ExchangeDeclareAsync(
            MessageBusConstants.Exchange, ExchangeType.Topic, durable: true,
            cancellationToken: stoppingToken);

        logger.LogInformation("OutboxRelayService started — polling every {Interval}s", PollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RelayPendingAsync(stoppingToken);
        }
    }

    private async Task RelayPendingAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.PublishedAt == null && m.Attempts < MaxAttemptsBeforeAbandon)
            .OrderBy(m => m.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        foreach (var msg in pending)
        {
            try
            {
                await EnsureChannelOpenAsync();

                var body = System.Text.Encoding.UTF8.GetBytes(msg.Payload);
                await _channel!.BasicPublishAsync(
                    exchange:   MessageBusConstants.Exchange,
                    routingKey: msg.RoutingKey,
                    body:       body,
                    cancellationToken: ct);

                msg.PublishedAt = DateTime.UtcNow;
                logger.LogDebug("Outbox published {EventType} id={Id}", msg.EventType, msg.Id);
            }
            catch (Exception ex)
            {
                msg.Attempts++;
                logger.LogWarning(ex, "Failed to publish outbox message {Id} (attempt {Attempts})",
                    msg.Id, msg.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureChannelOpenAsync()
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true }) return;
        await ConnectWithRetryAsync(CancellationToken.None);
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
                logger.LogInformation("OutboxRelayService connected to RabbitMQ");
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
