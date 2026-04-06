namespace Shared.Domain.Entities;

public class Squad
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public int Gameweek { get; set; }
    public List<SquadPlayer> Players { get; set; } = [];
}

public class SquadPlayer
{
    public int Id { get; set; }
    public string SquadId { get; set; } = null!;
    public string PlayerId { get; set; } = null!;

    /// <summary>
    /// Positional slot in the formation (GK, DF, MF, FW).
    /// Present in mock data; required by Opcode 2001 SAVE_SQUAD.
    /// </summary>
    public string PositionSlot { get; set; } = null!;

    public bool IsCaptain { get; set; }
    public bool IsViceCaptain { get; set; }

    /// <summary>
    /// Whether this player is on the bench (substitutes).
    /// Present in mock data; required by Opcode 2001 SAVE_SQUAD.
    /// </summary>
    public bool IsBench { get; set; }
}
