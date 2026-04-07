using System.Net.WebSockets;
using MatchApi.Auth;
using MatchApi.Dispatcher;
using MatchApi.Services;
using Shared.Contracts;

namespace MatchApi.Handlers;

/// <summary>Opcode 1003 UNSUBSCRIBE_LIVE_SCORE — removes this connection from the live score stream.</summary>
public class UnsubscribeLiveScoreHandler(SubscriptionManager subscriptions) : IOpcodeHandler
{
    public int Opcode => Shared.Contracts.Opcode.UnsubscribeLiveScore;

    public Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, AuthContext? auth, CancellationToken ct)
    {
        var req = request.Payload.Deserialize<UnsubscribeLiveScoreRequest>(ApiJsonOptions.Options);

        if (req is null || string.IsNullOrEmpty(req.MatchId))
            return Task.FromResult(OpcodeResponse.Fail(
                request.Opcode, request.RequestId,
                "MISSING_MATCH_ID", "match_id is required"));

        if (ws is not null)
            subscriptions.Unregister(req.MatchId, ws);

        return Task.FromResult(OpcodeResponse.Ok(
            request.Opcode, request.RequestId,
            new UnsubscribeLiveScoreResponse(Unsubscribed: true)));
    }
}
