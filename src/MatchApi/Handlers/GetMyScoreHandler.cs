using System.Net.WebSockets;
using MatchApi.Dispatcher;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts;
using Shared.Data;
using Shared.Messaging;
using StackExchange.Redis;

namespace MatchApi.Handlers;

/// <summary>
/// Opcode 2002 GET_MY_SCORE — returns a user's total and gameweek fantasy points.
///
/// Gameweek points are computed by joining the user's squad (with captain flag) against
/// PlayerGameweekScore rows. Captain's points are doubled. Global rank is read from Redis.
/// </summary>
public class GetMyScoreHandler(IServiceScopeFactory scopeFactory, IConnectionMultiplexer redis) : IOpcodeHandler
{
    public int Opcode => Shared.Contracts.Opcode.GetMyScore;

    public async Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, CancellationToken ct)
    {
        var req = request.Payload.Deserialize<GetMyScoreRequest>(ApiJsonOptions.Options);

        if (req is null || string.IsNullOrEmpty(req.UserId))
            return OpcodeResponse.Fail(request.Opcode, request.RequestId,
                "MISSING_USER_ID", "user_id is required");

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Use the requested gameweek, or fall back to the latest active gameweek
        int gameweek = req.Gameweek ?? await db.Matches
            .Where(m => m.Status == "live" || m.Status == "scheduled")
            .Select(m => m.Gameweek)
            .OrderBy(g => g)
            .FirstOrDefaultAsync(ct);

        var squad = await db.Squads
            .Include(s => s.Players)
            .FirstOrDefaultAsync(s => s.UserId == req.UserId && s.Gameweek == gameweek, ct);

        if (squad is null)
            return OpcodeResponse.Fail(request.Opcode, request.RequestId,
                "SQUAD_NOT_FOUND", $"No squad found for user {req.UserId} in gameweek {gameweek}");

        var activePlayerIds  = squad.Players.Where(p => !p.IsBench).Select(p => p.PlayerId).ToList();
        var captainPlayerId  = squad.Players.FirstOrDefault(p => p.IsCaptain)?.PlayerId;

        var scores = await db.PlayerGameweekScores
            .Where(s => activePlayerIds.Contains(s.PlayerId) && s.Gameweek == gameweek)
            .ToListAsync(ct);

        int gameweekPoints = 0;
        foreach (var score in scores)
        {
            int pts = score.PlayerId == captainPlayerId ? score.Points * 2 : score.Points;
            gameweekPoints += pts;
        }

        // Global rank and total score from Redis
        var redisDb    = redis.GetDatabase();
        long?   rank   = await redisDb.SortedSetRankAsync(RedisKeys.GlobalLeaderboard, req.UserId, Order.Descending);
        double? rScore = await redisDb.SortedSetScoreAsync(RedisKeys.GlobalLeaderboard, req.UserId);

        return OpcodeResponse.Ok(request.Opcode, request.RequestId, new GetMyScoreResponse(
            TotalPoints:    (int)(rScore ?? 0),
            GameweekPoints: gameweekPoints,
            Rank:           rank.HasValue ? rank.Value + 1 : 0));
    }
}
