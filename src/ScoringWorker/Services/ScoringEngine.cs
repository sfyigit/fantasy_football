namespace ScoringWorker.Services;

/// <summary>
/// Pure, stateless fantasy point calculation.
/// All scoring rules are sourced from the CLAUDE.md specification.
/// </summary>
public static class ScoringEngine
{
    /// <summary>
    /// Returns fantasy points for a single fixture event.
    /// Returns 0 for event types that carry no point value (e.g. unknown types).
    /// </summary>
    public static int Calculate(string eventType, string position) => eventType switch
    {
        "goal"            => GoalPoints(position),
        "assist"          => 3,
        "clean_sheet"     => CleanSheetPoints(position),
        "yellow_card"     => -1,
        "red_card"        => -3,
        "penalty_missed"  => -2,
        "own_goal"        => -2,
        "appearance_full" => 2,   // ≥60 minutes
        "appearance_sub"  => 1,   // <60 minutes
        _                 => 0
    };

    private static int GoalPoints(string position) => position switch
    {
        "FW" => 6,
        "MF" => 5,
        "DF" => 6,
        "GK" => 6,
        _    => 4   // fallback for unknown positions
    };

    private static int CleanSheetPoints(string position) => position switch
    {
        "GK" => 4,
        "DF" => 4,
        "MF" => 1,
        _    => 0   // FW and unknowns earn no clean sheet points
    };
}
