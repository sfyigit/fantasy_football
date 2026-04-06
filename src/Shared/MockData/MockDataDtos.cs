using System.Text.Json.Serialization;

namespace Shared.MockData;

// ── players.json ──────────────────────────────────────────────────────────────

public record PlayersJson([property: JsonPropertyName("players")] List<PlayerJson> Players);

public record PlayerJson(
    [property: JsonPropertyName("id")]           string Id,
    [property: JsonPropertyName("name")]         string Name,
    [property: JsonPropertyName("position")]     string Position,
    [property: JsonPropertyName("team_id")]      string TeamId,
    [property: JsonPropertyName("price")]        decimal Price,
    [property: JsonPropertyName("total_points")] int TotalPoints,
    [property: JsonPropertyName("stats")]        PlayerStatsJson Stats
);

public record PlayerStatsJson(
    [property: JsonPropertyName("goals")]            int Goals,
    [property: JsonPropertyName("assists")]          int Assists,
    [property: JsonPropertyName("yellow_cards")]     int YellowCards,
    [property: JsonPropertyName("red_cards")]        int RedCards,
    [property: JsonPropertyName("minutes_played")]   int MinutesPlayed,
    [property: JsonPropertyName("clean_sheets")]     int CleanSheets,
    [property: JsonPropertyName("own_goals")]        int OwnGoals,
    [property: JsonPropertyName("penalties_missed")] int PenaltiesMissed,
    [property: JsonPropertyName("saves")]            int Saves
);

// ── matches.json ──────────────────────────────────────────────────────────────

public record MatchesJson([property: JsonPropertyName("matches")] List<MatchJson> Matches);

public record MatchJson(
    [property: JsonPropertyName("id")]           string Id,
    [property: JsonPropertyName("home_team_id")] string HomeTeamId,
    [property: JsonPropertyName("away_team_id")] string AwayTeamId,
    [property: JsonPropertyName("gameweek")]     int Gameweek,
    [property: JsonPropertyName("kickoff")]      DateTime Kickoff,
    [property: JsonPropertyName("status")]       string Status,
    [property: JsonPropertyName("score")]        MatchScoreJson Score,
    [property: JsonPropertyName("minute")]       int? Minute
);

public record MatchScoreJson(
    [property: JsonPropertyName("home")] int Home,
    [property: JsonPropertyName("away")] int Away
);

// ── fixtures.json ─────────────────────────────────────────────────────────────

public record FixturesJson([property: JsonPropertyName("fixtures")] List<FixtureJson> Fixtures);

public record FixtureJson(
    [property: JsonPropertyName("id")]               string Id,
    [property: JsonPropertyName("match_id")]         string MatchId,
    [property: JsonPropertyName("minute")]           int Minute,
    [property: JsonPropertyName("type")]             string Type,
    [property: JsonPropertyName("player_id")]        string PlayerId,
    [property: JsonPropertyName("assist_player_id")] string? AssistPlayerId,
    [property: JsonPropertyName("team_id")]          string TeamId
);

// ── teams.json ────────────────────────────────────────────────────────────────

public record TeamsJson([property: JsonPropertyName("teams")] List<TeamJson> Teams);

public record TeamJson(
    [property: JsonPropertyName("id")]      string Id,
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("short")]   string Short,
    [property: JsonPropertyName("stadium")] string Stadium
);

// ── users.json ────────────────────────────────────────────────────────────────

public record UsersJson([property: JsonPropertyName("users")] List<UserJson> Users);

public record UserJson(
    [property: JsonPropertyName("id")]           string Id,
    [property: JsonPropertyName("username")]     string Username,
    [property: JsonPropertyName("email")]        string Email,
    [property: JsonPropertyName("league_id")]    string? LeagueId,
    [property: JsonPropertyName("total_points")] int TotalPoints
);

// ── squads.json ───────────────────────────────────────────────────────────────

public record SquadsJson([property: JsonPropertyName("squads")] List<SquadJson> Squads);

public record SquadJson(
    [property: JsonPropertyName("id")]       string Id,
    [property: JsonPropertyName("user_id")]  string UserId,
    [property: JsonPropertyName("gameweek")] int Gameweek,
    [property: JsonPropertyName("players")]  List<SquadPlayerJson> Players
);

public record SquadPlayerJson(
    [property: JsonPropertyName("player_id")]       string PlayerId,
    [property: JsonPropertyName("position_slot")]   string PositionSlot,
    [property: JsonPropertyName("is_captain")]      bool IsCaptain,
    [property: JsonPropertyName("is_vice_captain")] bool IsViceCaptain,
    [property: JsonPropertyName("is_bench")]        bool IsBench
);
