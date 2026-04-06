namespace Shared.Domain.Entities;

public class Player
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Position { get; set; } = null!;
    public string TeamId { get; set; } = null!;
    public decimal Price { get; set; }
    public int TotalPoints { get; set; }
    public PlayerStats Stats { get; set; } = new();
}

/// <summary>
/// Stored as an owned type (same table as Player).
/// Saves is tracked from mock data but has no scoring rule (scoring rules are in ScoringWorker).
/// </summary>
public class PlayerStats
{
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
    public int MinutesPlayed { get; set; }
    public int CleanSheets { get; set; }
    public int OwnGoals { get; set; }
    public int PenaltiesMissed { get; set; }
    public int Saves { get; set; }
}
