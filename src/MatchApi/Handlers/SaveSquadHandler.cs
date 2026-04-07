using System.Net.WebSockets;
using MatchApi.Dispatcher;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts;
using Shared.Data;
using Shared.Domain.Entities;

namespace MatchApi.Handlers;

/// <summary>
/// Opcode 2001 SAVE_SQUAD — upserts the weekly fantasy squad for a user.
/// Existing squad players for the same userId + gameweek are replaced atomically.
/// </summary>
public class SaveSquadHandler(IServiceScopeFactory scopeFactory) : IOpcodeHandler
{
    public int Opcode => Shared.Contracts.Opcode.SaveSquad;

    public async Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, CancellationToken ct)
    {
        var req = request.Payload.Deserialize<SaveSquadRequest>(ApiJsonOptions.Options);

        if (req is null || string.IsNullOrEmpty(req.UserId))
            return OpcodeResponse.Fail(request.Opcode, request.RequestId,
                "INVALID_PAYLOAD", "user_id and players are required");

        if (req.Players is null || req.Players.Count == 0)
            return OpcodeResponse.Fail(request.Opcode, request.RequestId,
                "EMPTY_SQUAD", "Squad must contain at least one player");

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Squads
            .Include(s => s.Players)
            .FirstOrDefaultAsync(s => s.UserId == req.UserId && s.Gameweek == req.Gameweek, ct);

        string squadId;
        if (existing is null)
        {
            squadId  = Guid.NewGuid().ToString();
            existing = new Squad { Id = squadId, UserId = req.UserId, Gameweek = req.Gameweek };
            db.Squads.Add(existing);
        }
        else
        {
            squadId = existing.Id;
            db.SquadPlayers.RemoveRange(existing.Players);
            existing.Players.Clear();
        }

        foreach (var p in req.Players)
        {
            existing.Players.Add(new SquadPlayer
            {
                SquadId      = squadId,
                PlayerId     = p.PlayerId,
                PositionSlot = p.PositionSlot,
                IsCaptain    = p.IsCaptain,
                IsViceCaptain = p.IsViceCaptain,
                IsBench      = p.IsBench
            });
        }

        await db.SaveChangesAsync(ct);

        return OpcodeResponse.Ok(request.Opcode, request.RequestId,
            new SaveSquadResponse(SquadId: squadId, SavedAt: DateTime.UtcNow));
    }
}
