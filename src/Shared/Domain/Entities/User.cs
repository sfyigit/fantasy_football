namespace Shared.Domain.Entities;

public class User
{
    public string Id { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;

    /// <summary>League the user belongs to. Nullable — a user may not be in a league yet.</summary>
    public string? LeagueId { get; set; }

    public int TotalPoints { get; set; }
}
