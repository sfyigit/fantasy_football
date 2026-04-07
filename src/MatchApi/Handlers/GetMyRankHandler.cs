using System.Net.WebSockets;
using MatchApi.Dispatcher;
using Shared.Contracts;
using Shared.Messaging;
using StackExchange.Redis;

namespace MatchApi.Handlers;

/// <summary>
/// Opcode 3002 GET_MY_RANK — returns the requesting user's rank, total points, and percentile
/// from the Redis Sorted Set (global or league-scoped).
/// </summary>
public class GetMyRankHandler(IConnectionMultiplexer redis) : IOpcodeHandler
{
    public int Opcode => Shared.Contracts.Opcode.GetMyRank;

    public async Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, CancellationToken ct)
    {
        var req = request.Payload.Deserialize<GetMyRankRequest>(ApiJsonOptions.Options);

        if (req is null || string.IsNullOrEmpty(req.UserId))
            return OpcodeResponse.Fail(request.Opcode, request.RequestId,
                "MISSING_USER_ID", "user_id is required");

        var key = req.LeagueId is null
            ? RedisKeys.GlobalLeaderboard
            : RedisKeys.LeagueLeaderboard(req.LeagueId);

        var db = redis.GetDatabase();

        // Run all three Redis commands concurrently
        var rankTask  = db.SortedSetRankAsync(key, req.UserId, Order.Descending);
        var scoreTask = db.SortedSetScoreAsync(key, req.UserId);
        var countTask = db.SortedSetLengthAsync(key);

        await Task.WhenAll(rankTask, scoreTask, countTask);

        long? rank  = rankTask.Result;
        double? score = scoreTask.Result;
        long total  = countTask.Result;

        if (!rank.HasValue)
            return OpcodeResponse.Fail(request.Opcode, request.RequestId,
                "USER_NOT_RANKED", $"User {req.UserId} has no score on the leaderboard");

        long rank1Based = rank.Value + 1;
        double percentile = total > 0
            ? Math.Round((double)(total - rank.Value) / total * 100.0, 2)
            : 0.0;

        return OpcodeResponse.Ok(request.Opcode, request.RequestId, new GetMyRankResponse(
            Rank:        rank1Based,
            TotalPoints: (int)(score ?? 0),
            Percentile:  percentile));
    }
}
