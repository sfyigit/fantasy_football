using System.Net.WebSockets;
using MatchApi.Dispatcher;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts;
using Shared.Data;

namespace MatchApi.Handlers;

/// <summary>Opcode 2003 GET_PLAYER_STATS — returns player info and cumulative statistics.</summary>
public class GetPlayerStatsHandler(IServiceScopeFactory scopeFactory) : IOpcodeHandler
{
    public int Opcode => Shared.Contracts.Opcode.GetPlayerStats;

    public async Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, CancellationToken ct)
    {
        var req = request.Payload.Deserialize<GetPlayerStatsRequest>(ApiJsonOptions.Options);

        if (req is null || string.IsNullOrEmpty(req.PlayerId))
            return OpcodeResponse.Fail(request.Opcode, request.RequestId,
                "MISSING_PLAYER_ID", "player_id is required");

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players
            .Include(p => p.Stats)
            .FirstOrDefaultAsync(p => p.Id == req.PlayerId, ct);

        if (player is null)
            return OpcodeResponse.Fail(request.Opcode, request.RequestId,
                "PLAYER_NOT_FOUND", $"Player {req.PlayerId} not found");

        // Total fantasy points: sum of all scored events (optionally filtered by gameweek)
        var pointsQuery = db.PlayerGameweekScores.Where(s => s.PlayerId == req.PlayerId);
        if (req.Gameweek.HasValue)
            pointsQuery = pointsQuery.Where(s => s.Gameweek == req.Gameweek);

        int fantasyPoints = await pointsQuery.SumAsync(s => s.Points, ct);

        return OpcodeResponse.Ok(request.Opcode, request.RequestId, new GetPlayerStatsResponse(
            Player: new PlayerDto(player.Id, player.Name, player.Position, player.TeamId, player.Price),
            Stats: new PlayerStatsDto(
                player.Stats.Goals, player.Stats.Assists, player.Stats.YellowCards,
                player.Stats.RedCards, player.Stats.MinutesPlayed, player.Stats.CleanSheets,
                player.Stats.OwnGoals, player.Stats.PenaltiesMissed, player.Stats.Saves),
            FantasyPoints: fantasyPoints));
    }
}
