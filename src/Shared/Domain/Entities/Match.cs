namespace Shared.Domain.Entities;

public class Match
{
    public string Id { get; set; } = null!;
    public string HomeTeamId { get; set; } = null!;
    public string AwayTeamId { get; set; } = null!;
    public int Gameweek { get; set; }
    public DateTime Kickoff { get; set; }
    public string Status { get; set; } = null!;
    public int ScoreHome { get; set; }
    public int ScoreAway { get; set; }

    /// <summary>
    /// Current match minute; null when not live (scheduled / finished).
    /// Present in mock data but absent from the CLAUDE.md spec example — included for live score support.
    /// </summary>
    public int? Minute { get; set; }
}
