using System.Text.Json.Serialization;

namespace Shared.Contracts;

// ── Opcode constants ──────────────────────────────────────────────────────────

public static class Opcode
{
    public const int GetMatches          = 1001;
    public const int SubscribeLiveScore  = 1002;
    public const int UnsubscribeLiveScore = 1003;
    public const int LiveScoreUpdate     = 1004;
    public const int SaveSquad           = 2001;
    public const int GetMyScore          = 2002;
    public const int GetPlayerStats      = 2003;
    public const int GetLeaderboard      = 3001;
    public const int GetMyRank           = 3002;
    public const int Heartbeat           = 9000;
    public const int Error               = 9999;
}

// ── 1001 GET_MATCHES ──────────────────────────────────────────────────────────

public record GetMatchesRequest(
    [property: JsonPropertyName("gameweek")] int? Gameweek,
    [property: JsonPropertyName("status")]   string? Status
);

// ── 1002 / 1003 SUBSCRIBE / UNSUBSCRIBE_LIVE_SCORE ───────────────────────────

public record SubscribeLiveScoreRequest(
    [property: JsonPropertyName("match_id")] string MatchId
);

public record UnsubscribeLiveScoreRequest(
    [property: JsonPropertyName("match_id")] string MatchId
);

// ── 2001 SAVE_SQUAD ───────────────────────────────────────────────────────────

public record SaveSquadRequest(
    [property: JsonPropertyName("user_id")]  string UserId,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("players")]  List<SaveSquadPlayerEntry> Players
);

/// <summary>
/// Single player entry in a SAVE_SQUAD request.
/// Includes <see cref="PositionSlot"/> and <see cref="IsBench"/> which are present in
/// squads.json but were missing from the original CLAUDE.md spec example.
/// </summary>
public record SaveSquadPlayerEntry(
    [property: JsonPropertyName("player_id")]       string PlayerId,
    [property: JsonPropertyName("position_slot")]   string PositionSlot,
    [property: JsonPropertyName("is_captain")]      bool IsCaptain,
    [property: JsonPropertyName("is_vice_captain")] bool IsViceCaptain,
    [property: JsonPropertyName("is_bench")]        bool IsBench
);

// ── 2002 GET_MY_SCORE ─────────────────────────────────────────────────────────

public record GetMyScoreRequest(
    [property: JsonPropertyName("user_id")]  string UserId,
    [property: JsonPropertyName("gameweek")] int? Gameweek
);

// ── 2003 GET_PLAYER_STATS ─────────────────────────────────────────────────────

public record GetPlayerStatsRequest(
    [property: JsonPropertyName("player_id")] string PlayerId,
    [property: JsonPropertyName("gameweek")]  int? Gameweek
);

// ── 3001 GET_LEADERBOARD ──────────────────────────────────────────────────────

public record GetLeaderboardRequest(
    [property: JsonPropertyName("league_id")] string? LeagueId,
    [property: JsonPropertyName("page")]      int Page,
    [property: JsonPropertyName("page_size")] int PageSize
);

// ── 3002 GET_MY_RANK ──────────────────────────────────────────────────────────

public record GetMyRankRequest(
    [property: JsonPropertyName("user_id")]   string UserId,
    [property: JsonPropertyName("league_id")] string? LeagueId
);

// ── 9000 HEARTBEAT ────────────────────────────────────────────────────────────

public record HeartbeatRequest(
    [property: JsonPropertyName("timestamp")] string Timestamp
);
