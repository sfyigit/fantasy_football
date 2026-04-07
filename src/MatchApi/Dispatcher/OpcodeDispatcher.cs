using System.Net.WebSockets;
using MatchApi.Auth;
using Shared.Contracts;

namespace MatchApi.Dispatcher;

/// <summary>
/// Routes incoming <see cref="OpcodeRequest"/> messages to the correct <see cref="IOpcodeHandler"/>
/// via a dictionary keyed on opcode integer.
///
/// Auth enforcement:
///   If a handler implements <see cref="IAuthenticatedHandler"/> and <paramref name="auth"/> is null,
///   the dispatcher short-circuits with a 9999 UNAUTHORIZED response before calling the handler.
/// </summary>
public class OpcodeDispatcher
{
    private readonly Dictionary<int, IOpcodeHandler> _handlers;
    private readonly ILogger<OpcodeDispatcher> _logger;

    public OpcodeDispatcher(IEnumerable<IOpcodeHandler> handlers, ILogger<OpcodeDispatcher> logger)
    {
        _handlers = handlers.ToDictionary(h => h.Opcode);
        _logger   = logger;
    }

    public async Task<OpcodeResponse> DispatchAsync(
        OpcodeRequest request, WebSocket? ws, AuthContext? auth, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(request.Opcode, out var handler))
        {
            _logger.LogWarning("Unknown opcode {Opcode} requestId={RequestId}", request.Opcode, request.RequestId);
            return OpcodeResponse.Fail(
                Opcode.Error, request.RequestId,
                "UNKNOWN_OPCODE", $"Opcode {request.Opcode} is not supported");
        }

        // ── Auth enforcement ─────────────────────────────────────────────────
        if (handler is IAuthenticatedHandler && auth is null)
        {
            _logger.LogWarning("Unauthenticated request for protected opcode {Opcode}", request.Opcode);
            return OpcodeResponse.Fail(
                Opcode.Error, request.RequestId,
                "UNAUTHORIZED", "A valid Bearer token is required for this opcode");
        }

        _logger.LogDebug("Dispatching opcode {Opcode} requestId={RequestId}", request.Opcode, request.RequestId);

        try
        {
            return await handler.HandleAsync(request, ws, auth, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler for opcode {Opcode} threw an exception", request.Opcode);
            return OpcodeResponse.Fail(request.Opcode, request.RequestId, "INTERNAL_ERROR", "An internal error occurred");
        }
    }
}
