namespace Shared.Domain.Entities;

/// <summary>
/// A single in-match event (goal, card, etc.).
/// The <see cref="Id"/> (e.g. "f-001") comes directly from the mock data and is used as the
/// idempotency key in MatchDataIngestion: if a row with this Id already exists the event is skipped.
/// </summary>
public class Fixture
{
    public string Id { get; set; } = null!;
    public string MatchId { get; set; } = null!;
    public int Minute { get; set; }
    public string Type { get; set; } = null!;
    public string PlayerId { get; set; } = null!;
    public string? AssistPlayerId { get; set; }
    public string TeamId { get; set; } = null!;
}
