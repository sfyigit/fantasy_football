namespace Shared.Messaging;

/// <summary>
/// Redis key naming conventions shared across services.
/// LeaderboardWorker writes these; MatchApi reads them for opcodes 3001 and 3002.
/// </summary>
public static class RedisKeys
{
    /// <summary>Global leaderboard sorted set — all users, scored by total fantasy points.</summary>
    public const string GlobalLeaderboard = "leaderboard:global";

    /// <summary>Per-league leaderboard sorted set.</summary>
    public static string LeagueLeaderboard(string leagueId) => $"leaderboard:league:{leagueId}";

    /// <summary>
    /// Idempotency guard for LeaderboardWorker.
    /// SET NX with 7-day TTL — if key exists the ScoreCalculated event was already applied.
    /// </summary>
    public static string ProcessedScore(string idempotencyKey) => $"leaderboard:processed:{idempotencyKey}";
}
