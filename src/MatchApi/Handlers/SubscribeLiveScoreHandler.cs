using System.Net.WebSockets;
using MatchApi.Auth;
using MatchApi.Dispatcher;
using MatchApi.Services;
using Shared.Contracts;

namespace MatchApi.Handlers;

/// <summary>
/// Opcode 1002 SUBSCRIBE_LIVE_SCORE — registers this WebSocket connection to receive
/// opcode 1004 pushes whenever the specified match score is updated.
/// </summary>
public class SubscribeLiveScoreHandler(SubscriptionManager subscriptions) : IOpcodeHandler
{
    public int Opcode => Shared.Contracts.Opcode.SubscribeLiveScore;

    public Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, AuthContext? auth, CancellationToken ct)
    {
        var req = request.Payload.Deserialize<SubscribeLiveScoreRequest>(ApiJsonOptions.Options);

        if (req is null || string.IsNullOrEmpty(req.MatchId))
            return Task.FromResult(OpcodeResponse.Fail(
                request.Opcode, request.RequestId,
                "MISSING_MATCH_ID", "match_id is required"));

        if (ws is null)
            return Task.FromResult(OpcodeResponse.Fail(
                request.Opcode, request.RequestId,
                "WS_REQUIRED", "Opcode 1002 requires a WebSocket connection"));

        subscriptions.Register(req.MatchId, ws);

        return Task.FromResult(OpcodeResponse.Ok(
            request.Opcode, request.RequestId,
            new SubscribeLiveScoreResponse(Subscribed: true, MatchId: req.MatchId)));
    }
}
