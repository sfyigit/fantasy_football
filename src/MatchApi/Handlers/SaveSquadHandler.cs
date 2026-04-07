using System.Net.WebSockets;
using MatchApi.Auth;
using MatchApi.Dispatcher;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts;
using Shared.Data;
using Shared.Domain.Entities;

namespace MatchApi.Handlers;

/// <summary>
/// Opcode 2001 SAVE_SQUAD — upserts the weekly fantasy squad for a user.
/// userId is sourced from the verified JWT claim (not the payload).
/// </summary>
public class SaveSquadHandler(IServiceScopeFactory scopeFactory) : IOpcodeHandler, IAuthenticatedHandler
{
    public int Opcode => Shared.Contracts.Opcode.SaveSquad;

    public async Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, AuthContext? auth, CancellationToken ct)
    {
        // auth is guaranteed non-null by OpcodeDispatcher for IAuthenticatedHandler
        var userId = auth!.UserId;

        var req = request.Payload.Deserialize<SaveSquadRequest>(ApiJsonOptions.Options);

        if (req is null)
            return OpcodeResponse.Fail(request.Opcode, request.RequestId,
                "INVALID_PAYLOAD", "players and gameweek are required");

        if (req.Players is null || req.Players.Count == 0)
            return OpcodeResponse.Fail(request.Opcode, request.RequestId,
                "EMPTY_SQUAD", "Squad must contain at least one player");

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Squads
            .Include(s => s.Players)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Gameweek == req.Gameweek, ct);

        string squadId;
        if (existing is null)
        {
            squadId  = Guid.NewGuid().ToString();
            existing = new Squad { Id = squadId, UserId = userId, Gameweek = req.Gameweek };
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
                SquadId       = squadId,
                PlayerId      = p.PlayerId,
                PositionSlot  = p.PositionSlot,
                IsCaptain     = p.IsCaptain,
                IsViceCaptain = p.IsViceCaptain,
                IsBench       = p.IsBench
            });
        }

        await db.SaveChangesAsync(ct);

        return OpcodeResponse.Ok(request.Opcode, request.RequestId,
            new SaveSquadResponse(SquadId: squadId, SavedAt: DateTime.UtcNow));
    }
}
