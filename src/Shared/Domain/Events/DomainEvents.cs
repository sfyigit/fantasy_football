namespace Shared.Domain.Events;

public record MatchUpdatedEvent(
    string MatchId,
    string Status,
    int ScoreHome,
    int ScoreAway,
    int? Minute
);

/// <summary>
/// Published when a new Fixture is persisted. IdempotencyKey = Fixture.Id (e.g. "f-001").
/// </summary>
public record PlayerStatUpdatedEvent(
    string PlayerId,
    string MatchId,
    string EventType,
    string IdempotencyKey
);

/// <summary>
/// Published by ScoringWorker after calculating fantasy points for a player event.
/// Player-centric: one event per fixture, not per user.
/// LeaderboardWorker resolves which users own this player (via squad lookup) and updates
/// the Redis sorted sets accordingly.
/// IdempotencyKey = Fixture.Id — prevents double-counting if the event is redelivered.
/// </summary>
public record ScoreCalculatedEvent(
    string PlayerId,
    string MatchId,
    int Gameweek,
    int Points,
    string IdempotencyKey
);
