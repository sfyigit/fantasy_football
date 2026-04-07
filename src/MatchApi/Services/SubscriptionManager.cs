using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace MatchApi.Services;

/// <summary>
/// Thread-safe in-memory registry that maps match IDs to the set of WebSocket connections
/// currently subscribed for live score updates (opcodes 1002 / 1003 / 1004).
/// </summary>
public class SubscriptionManager
{
    // matchId → { WebSocket → placeholder byte }
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<WebSocket, byte>> _subs = new();

    /// <summary>Subscribe <paramref name="ws"/> to live score updates for <paramref name="matchId"/>.</summary>
    public void Register(string matchId, WebSocket ws)
    {
        var set = _subs.GetOrAdd(matchId, _ => new ConcurrentDictionary<WebSocket, byte>());
        set[ws] = 0;
    }

    /// <summary>Unsubscribe <paramref name="ws"/> from a specific match.</summary>
    public void Unregister(string matchId, WebSocket ws)
    {
        if (_subs.TryGetValue(matchId, out var set))
            set.TryRemove(ws, out _);
    }

    /// <summary>
    /// Remove <paramref name="ws"/> from every subscription.
    /// Called when a WebSocket session ends to prevent stale references.
    /// </summary>
    public void UnregisterAll(WebSocket ws)
    {
        foreach (var (_, set) in _subs)
            set.TryRemove(ws, out _);
    }

    /// <summary>Returns a snapshot of all subscribers for <paramref name="matchId"/>.</summary>
    public IReadOnlyList<WebSocket> GetSubscribers(string matchId)
    {
        if (_subs.TryGetValue(matchId, out var set))
            return [.. set.Keys];
        return [];
    }
}
