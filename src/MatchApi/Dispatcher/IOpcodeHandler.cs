using System.Net.WebSockets;
using Shared.Contracts;

namespace MatchApi.Dispatcher;

/// <summary>
/// Implemented by every opcode handler.
/// Handlers that only make sense over WebSocket (1002, 1003) receive the live <see cref="WebSocket"/>.
/// Handlers called via HTTP POST /opcode receive <c>null</c> for <paramref name="ws"/>.
/// </summary>
public interface IOpcodeHandler
{
    int Opcode { get; }
    Task<OpcodeResponse> HandleAsync(OpcodeRequest request, WebSocket? ws, CancellationToken ct);
}
