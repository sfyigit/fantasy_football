using System.Net.WebSockets;
using MatchApi.Auth;
using MatchApi.Dispatcher;
using Shared.Contracts;

namespace MatchApi.Handlers;

/// <summary>Opcode 9000 HEARTBEAT — echoes timestamp back as a pong.</summary>
public class HeartbeatHandler : IOpcodeHandler
{
    public int Opcode => Shared.Contracts.Opcode.Heartbeat;

    public Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, AuthContext? auth, CancellationToken ct)
    {
        var req = request.Payload.Deserialize<HeartbeatRequest>(ApiJsonOptions.Options);
        var response = OpcodeResponse.Ok(request.Opcode, request.RequestId, new HeartbeatResponse(
            Timestamp:  req?.Timestamp ?? string.Empty,
            ServerTime: DateTime.UtcNow.ToString("O")));

        return Task.FromResult(response);
    }
}
