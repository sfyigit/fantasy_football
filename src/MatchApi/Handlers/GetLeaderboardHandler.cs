using System.Net.WebSockets;
using MatchApi.Dispatcher;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts;
using Shared.Data;
using Shared.Messaging;
using StackExchange.Redis;

namespace MatchApi.Handlers;

/// <summary>
/// Opcode 3001 GET_LEADERBOARD — returns a paginated leaderboard from Redis Sorted Sets.
/// Usernames are enriched from PostgreSQL in a single batch query.
/// </summary>
public class GetLeaderboardHandler(IConnectionMultiplexer redis, IServiceScopeFactory scopeFactory) : IOpcodeHandler
{
    public int Opcode => Shared.Contracts.Opcode.GetLeaderboard;

    public async Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, CancellationToken ct)
    {
        var req = request.Payload.Deserialize<GetLeaderboardRequest>(ApiJsonOptions.Options);

        int page     = Math.Max(1, req?.Page     ?? 1);
        int pageSize = Math.Clamp(req?.PageSize  ?? 20, 1, 100);

        var key = req?.LeagueId is null
            ? RedisKeys.GlobalLeaderboard
            : RedisKeys.LeagueLeaderboard(req.LeagueId);

        var db     = redis.GetDatabase();
        long start = (long)(page - 1) * pageSize;
        long stop  = start + pageSize - 1;

        var entries    = await db.SortedSetRangeByRankWithScoresAsync(key, start, stop, Order.Descending);
        long totalCount = await db.SortedSetLengthAsync(key);

        if (entries.Length == 0)
            return OpcodeResponse.Ok(request.Opcode, request.RequestId,
                new GetLeaderboardResponse([], (int)totalCount));

        // Batch-fetch usernames from PostgreSQL
        var userIds = entries.Select(e => e.Element.ToString()).ToList();
        await using var scope = scopeFactory.CreateAsyncScope();
        var efDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usernames = await efDb.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        var dtos = entries.Select((e, i) =>
        {
            var userId = e.Element.ToString();
            return new LeaderboardEntryDto(
                Rank:        start + i + 1,
                UserId:      userId,
                Username:    usernames.GetValueOrDefault(userId, "unknown"),
                TotalPoints: (int)e.Score);
        }).ToList();

        return OpcodeResponse.Ok(request.Opcode, request.RequestId,
            new GetLeaderboardResponse(dtos, (int)totalCount));
    }
}
