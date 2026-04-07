using System.Net.WebSockets;
using MatchApi.Auth;
using Shared.Contracts;

namespace MatchApi.Dispatcher;

/// <summary>
/// Implemented by every opcode handler.
/// <paramref name="ws"/> is null when the handler is called via HTTP POST /opcode.
/// <paramref name="auth"/> is null when the opcode is public (no JWT required).
/// Protected handlers that also implement <see cref="IAuthenticatedHandler"/> receive
/// a guaranteed non-null <paramref name="auth"/> — the dispatcher enforces this.
/// </summary>
public interface IOpcodeHandler
{
    int Opcode { get; }
    Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, AuthContext? auth, CancellationToken ct);
}
