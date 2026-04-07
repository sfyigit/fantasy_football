using System.Net.WebSockets;
using MatchApi.Dispatcher;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts;
using Shared.Data;

namespace MatchApi.Handlers;

/// <summary>Opcode 1001 GET_MATCHES — returns active/upcoming matches, optionally filtered.</summary>
public class GetMatchesHandler(IServiceScopeFactory scopeFactory) : IOpcodeHandler
{
    public int Opcode => Shared.Contracts.Opcode.GetMatches;

    public async Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, CancellationToken ct)
    {
        var req = request.Payload.Deserialize<GetMatchesRequest>(ApiJsonOptions.Options);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.Matches.AsQueryable();

        if (req?.Gameweek is not null)
            query = query.Where(m => m.Gameweek == req.Gameweek);

        if (!string.IsNullOrEmpty(req?.Status))
            query = query.Where(m => m.Status == req.Status);

        var matches = await query
            .OrderBy(m => m.Kickoff)
            .Select(m => new MatchDto(
                m.Id, m.HomeTeamId, m.AwayTeamId, m.Gameweek, m.Kickoff, m.Status,
                new MatchScoreDto(m.ScoreHome, m.ScoreAway), m.Minute))
            .ToListAsync(ct);

        return OpcodeResponse.Ok(request.Opcode, request.RequestId, new GetMatchesResponse(matches));
    }
}
