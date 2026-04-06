namespace Shared.Domain.Entities;

/// <summary>
/// Persisted by ScoringWorker for every processed fixture event.
/// One row = one scorable action (goal, assist, card, etc.) for a player in a match.
///
/// IdempotencyKey = Fixture.Id — ScoringWorker checks this before inserting to prevent
/// double-counting on message redelivery.
///
/// Used by MatchApi (opcode 2002 GET_MY_SCORE) to aggregate a user's gameweek points
/// by joining with Squad → SquadPlayer (applying ×2 captain multiplier).
/// </summary>
public class PlayerGameweekScore
{
    public int Id { get; set; }
    public string PlayerId { get; set; } = null!;
    public string MatchId { get; set; } = null!;
    public int Gameweek { get; set; }

    /// <summary>Fantasy points awarded for this specific action (pre-captain-multiplier).</summary>
    public int Points { get; set; }

    /// <summary>The fixture event type that triggered this score (goal, assist, yellow_card, …).</summary>
    public string EventType { get; set; } = null!;

    /// <summary>Fixture.Id — idempotency guard in ScoringWorker.</summary>
    public string IdempotencyKey { get; set; } = null!;

    public DateTime ProcessedAt { get; set; }
}
