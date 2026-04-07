using System.Net.WebSockets;
using MatchApi.Auth;
using MatchApi.Dispatcher;
using Shared.Contracts;
using Shared.Messaging;
using StackExchange.Redis;

namespace MatchApi.Handlers;

/// <summary>
/// Opcode 3002 GET_MY_RANK — returns the requesting user's rank, total points, and percentile.
/// userId is sourced from the verified JWT claim (not the payload).
/// </summary>
public class GetMyRankHandler(IConnectionMultiplexer redis) : IOpcodeHandler, IAuthenticatedHandler
{
    public int Opcode => Shared.Contracts.Opcode.GetMyRank;

    public async Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, AuthContext? auth, CancellationToken ct)
    {
        var userId = auth!.UserId;

        var req = request.Payload.Deserialize<GetMyRankRequest>(ApiJsonOptions.Options);

        var key = req?.LeagueId is null
            ? RedisKeys.GlobalLeaderboard
            : RedisKeys.LeagueLeaderboard(req.LeagueId);

        var db = redis.GetDatabase();

        var rankTask  = db.SortedSetRankAsync(key, userId, Order.Descending);
        var scoreTask = db.SortedSetScoreAsync(key, userId);
        var countTask = db.SortedSetLengthAsync(key);

        await Task.WhenAll(rankTask, scoreTask, countTask);

        long?   rank  = rankTask.Result;
        double? score = scoreTask.Result;
        long    total = countTask.Result;

        if (!rank.HasValue)
            return OpcodeResponse.Fail(request.Opcode, request.RequestId,
                "USER_NOT_RANKED", $"User {userId} has no score on the leaderboard");

        long   rank1Based  = rank.Value + 1;
        double percentile  = total > 0
            ? Math.Round((double)(total - rank.Value) / total * 100.0, 2)
            : 0.0;

        return OpcodeResponse.Ok(request.Opcode, request.RequestId, new GetMyRankResponse(
            Rank:        rank1Based,
            TotalPoints: (int)(score ?? 0),
            Percentile:  percentile));
    }
}
