using System.Net.WebSockets;
using Shared.Contracts;

namespace MatchApi.Dispatcher;

/// <summary>
/// Routes incoming <see cref="OpcodeRequest"/> messages to the correct <see cref="IOpcodeHandler"/>
/// via a dictionary keyed on opcode integer. Both WebSocket and HTTP paths share this dispatcher.
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

    public async Task<OpcodeResponse> DispatchAsync(OpcodeRequest request, WebSocket? ws, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(request.Opcode, out var handler))
        {
            _logger.LogWarning("Unknown opcode {Opcode} from requestId={RequestId}", request.Opcode, request.RequestId);
            return OpcodeResponse.Fail(
                Opcode.Error, request.RequestId,
                "UNKNOWN_OPCODE", $"Opcode {request.Opcode} is not supported");
        }

        _logger.LogDebug("Dispatching opcode {Opcode} requestId={RequestId}", request.Opcode, request.RequestId);

        try
        {
            return await handler.HandleAsync(request, ws, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler for opcode {Opcode} threw an exception", request.Opcode);
            return OpcodeResponse.Fail(request.Opcode, request.RequestId, "INTERNAL_ERROR", "An internal error occurred");
        }
    }
}
