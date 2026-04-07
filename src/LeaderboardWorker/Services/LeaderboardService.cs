using Shared.Messaging;
using StackExchange.Redis;

namespace LeaderboardWorker.Services;

/// <summary>
/// Wraps all Redis Sorted Set operations for the leaderboard.
///
/// Write path (LeaderboardWorker):
///   - TryMarkProcessedAsync  — idempotency guard via SET NX
///   - IncrementUserScoreAsync — ZINCRBY on global + league sorted sets
///
/// Read path (MatchApi opcodes 3001 / 3002):
///   MatchApi uses the same RedisKeys constants and queries Redis directly.
///   These helper methods are also available if MatchApi references this assembly,
///   but the interface is intentionally kept Redis-key-centric so either service
///   can read without a cross-service HTTP call.
/// </summary>
public class LeaderboardService(IConnectionMultiplexer redis, ILogger<LeaderboardService> logger)
{
    private static readonly TimeSpan ProcessedKeyTtl = TimeSpan.FromDays(7);

    private IDatabase Db => redis.GetDatabase();

    // ── Idempotency ───────────────────────────────────────────────────────────

    /// <summary>
    /// Atomically marks the idempotency key as processed.
    /// Returns <c>true</c> if this is the first time (safe to proceed),
    /// <c>false</c> if the key already existed (duplicate — skip).
    /// </summary>
    public async Task<bool> TryMarkProcessedAsync(string idempotencyKey)
    {
        var key = RedisKeys.ProcessedScore(idempotencyKey);
        bool set = await Db.StringSetAsync(key, 1, ProcessedKeyTtl, When.NotExists);
        return set;
    }

    // ── Leaderboard writes ────────────────────────────────────────────────────

    /// <summary>
    /// Adds <paramref name="points"/> to <paramref name="userId"/> in:
    ///   - <c>leaderboard:global</c>
    ///   - <c>leaderboard:league:{leagueId}</c> (when <paramref name="leagueId"/> is not null)
    /// </summary>
    public async Task IncrementUserScoreAsync(string userId, string? leagueId, double points)
    {
        var db = Db;
        var tasks = new List<Task>
        {
            db.SortedSetIncrementAsync(RedisKeys.GlobalLeaderboard, userId, points)
        };

        if (!string.IsNullOrEmpty(leagueId))
            tasks.Add(db.SortedSetIncrementAsync(RedisKeys.LeagueLeaderboard(leagueId), userId, points));

        await Task.WhenAll(tasks);

        logger.LogDebug("Leaderboard updated: user={UserId} league={LeagueId} +{Points}pts",
            userId, leagueId ?? "—", points);
    }

    // ── Leaderboard reads (used by MatchApi opcode 3001 / 3002) ──────────────

    /// <summary>
    /// Returns the top <paramref name="count"/> entries from the leaderboard, highest score first.
    /// Pass <paramref name="leagueId"/> for a league-scoped result, or null for the global board.
    /// </summary>
    public async Task<IEnumerable<LeaderboardEntry>> GetTopAsync(int count, string? leagueId = null)
    {
        var key = leagueId is null
            ? RedisKeys.GlobalLeaderboard
            : RedisKeys.LeagueLeaderboard(leagueId);

        var entries = await Db.SortedSetRangeByRankWithScoresAsync(
            key, start: 0, stop: count - 1, order: Order.Descending);

        return entries.Select((e, i) => new LeaderboardEntry(
            Rank: i + 1,
            UserId: e.Element.ToString(),
            Points: (int)e.Score));
    }

    /// <summary>
    /// Returns the 0-based rank of <paramref name="userId"/> (highest score = rank 0).
    /// Returns null when the user has no score in the sorted set.
    /// </summary>
    public async Task<long?> GetUserRankAsync(string userId, string? leagueId = null)
    {
        var key = leagueId is null
            ? RedisKeys.GlobalLeaderboard
            : RedisKeys.LeagueLeaderboard(leagueId);

        // ZREVRANK returns 0-based rank where 0 = highest score
        return await Db.SortedSetRankAsync(key, userId, Order.Descending);
    }

    /// <summary>Returns the total score for a user, or null if not on the board.</summary>
    public async Task<double?> GetUserScoreAsync(string userId, string? leagueId = null)
    {
        var key = leagueId is null
            ? RedisKeys.GlobalLeaderboard
            : RedisKeys.LeagueLeaderboard(leagueId);

        return await Db.SortedSetScoreAsync(key, userId);
    }
}

public record LeaderboardEntry(int Rank, string UserId, int Points);
