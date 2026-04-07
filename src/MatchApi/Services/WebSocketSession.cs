using System.Net.WebSockets;
using System.Text.Json;
using MatchApi.Dispatcher;
using Shared.Contracts;

namespace MatchApi.Services;

/// <summary>
/// Manages the full lifecycle of a single WebSocket connection:
///   - Receive loop: deserialise → dispatch → send response
///   - Teardown: unregister all subscriptions on disconnect
/// </summary>
public class WebSocketSession(
    WebSocket ws,
    OpcodeDispatcher dispatcher,
    SubscriptionManager subscriptions,
    ILogger<WebSocketSession> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = ApiJsonOptions.Options;

    public async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation("WebSocket session started");
        var buffer = new byte[8192];

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // ── Receive a full message (may arrive in multiple frames) ────
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    await ms.WriteAsync(buffer.AsMemory(0, result.Count), ct);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Goodbye", ct);
                    break;
                }

                // ── Deserialise ───────────────────────────────────────────────
                ms.Seek(0, SeekOrigin.Begin);
                OpcodeRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<OpcodeRequest>(ms, JsonOpts);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Failed to deserialise WebSocket message");
                    await SendAsync(OpcodeResponse.Fail(
                        Opcode.Error, Guid.NewGuid().ToString(),
                        "PARSE_ERROR", "Invalid JSON payload"), ct);
                    continue;
                }

                if (request is null)
                {
                    await SendAsync(OpcodeResponse.Fail(
                        Opcode.Error, Guid.NewGuid().ToString(),
                        "NULL_REQUEST", "Request was null"), ct);
                    continue;
                }

                // ── Dispatch ──────────────────────────────────────────────────
                var response = await dispatcher.DispatchAsync(request, ws, ct);
                await SendAsync(response, ct);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (WebSocketException ex)
        {
            logger.LogWarning(ex, "WebSocket connection closed unexpectedly");
        }
        finally
        {
            subscriptions.UnregisterAll(ws);
            logger.LogInformation("WebSocket session ended");
        }
    }

    /// <summary>Serialises and sends a response frame on this connection.</summary>
    public async Task SendAsync(OpcodeResponse response, CancellationToken ct = default)
    {
        if (ws.State != WebSocketState.Open) return;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(response, JsonOpts);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send WebSocket message opcode={Opcode}", response.Opcode);
        }
    }
}
