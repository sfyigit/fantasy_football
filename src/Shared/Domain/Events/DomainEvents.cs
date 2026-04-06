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
/// Published by ScoringWorker after calculating fantasy points for a player in a gameweek.
/// </summary>
public record ScoreCalculatedEvent(
    string UserId,
    string PlayerId,
    int Gameweek,
    int Points,
    string IdempotencyKey
);
