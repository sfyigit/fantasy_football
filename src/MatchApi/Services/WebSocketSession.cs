using System.Net.WebSockets;
using MatchApi.Auth;
using MatchApi.Dispatcher;
using Shared.Contracts;

namespace MatchApi.Services;

/// <summary>
/// Manages the full lifecycle of a single WebSocket connection:
///   - JWT identity extracted once from the HTTP upgrade handshake headers
///   - Receive loop: deserialise → rate-limit check → dispatch (with auth context) → send response
///   - Teardown: unregister all subscriptions on disconnect
///
/// Per-session rate limit: 120 messages / minute (fixed window). Heartbeat opcode (9000)
/// is excluded from the count so keep-alive frames never consume quota.
/// </summary>
public class WebSocketSession(
    WebSocket ws,
    OpcodeDispatcher dispatcher,
    SubscriptionManager subscriptions,
    AuthContext? auth,
    ILogger<WebSocketSession> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = ApiJsonOptions.Options;

    // ── Per-session fixed-window rate limiter ────────────────────────────────
    private const int RateLimitPerWindow = 120;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    private int  _windowCount;
    private long _windowStartTicks = DateTime.UtcNow.Ticks;

    /// <summary>
    /// Returns true and increments the counter if the message is within quota.
    /// Heartbeat (9000) is never counted.
    /// </summary>
    private bool TryConsumeRateLimit(int opcode)
    {
        if (opcode == Opcode.Heartbeat) return true;

        var now = DateTime.UtcNow.Ticks;
        if (now - _windowStartTicks >= RateLimitWindow.Ticks)
        {
            _windowCount      = 0;
            _windowStartTicks = now;
        }

        if (_windowCount >= RateLimitPerWindow) return false;
        _windowCount++;
        return true;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation("WebSocket session started user={UserId}", auth?.UserId ?? "anonymous");
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

                // ── Per-session rate limit ────────────────────────────────────
                if (!TryConsumeRateLimit(request.Opcode))
                {
                    logger.LogWarning("Rate limit exceeded for session user={UserId}", auth?.UserId ?? "anonymous");
                    await SendAsync(OpcodeResponse.Fail(
                        Opcode.Error, request.RequestId,
                        "RATE_LIMIT_EXCEEDED", $"Too many messages. Limit: {RateLimitPerWindow} per minute."), ct);
                    continue;
                }

                // ── Dispatch (auth context is fixed for the lifetime of this WS session)
                var response = await dispatcher.DispatchAsync(request, ws, auth, ct);
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
            logger.LogInformation("WebSocket session ended user={UserId}", auth?.UserId ?? "anonymous");
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
