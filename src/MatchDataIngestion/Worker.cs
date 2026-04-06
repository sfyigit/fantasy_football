using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Domain.Entities;
using Shared.Domain.Events;
using Shared.Messaging;
using Shared.MockData;

namespace MatchDataIngestion;

public class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    IConfiguration configuration
) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run immediately on startup, then every 60 seconds.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        do
        {
            await IngestAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task IngestAsync(CancellationToken ct)
    {
        logger.LogInformation("Starting data ingestion cycle");
        try
        {
            var dataPath = configuration["MockData:Path"]
                ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "mock-data");
            dataPath = Path.GetFullPath(dataPath);

            var teams    = await ReadJsonAsync<TeamsJson>(dataPath,   "teams.json",    ct);
            var players  = await ReadJsonAsync<PlayersJson>(dataPath,  "players.json",  ct);
            var matches  = await ReadJsonAsync<MatchesJson>(dataPath,  "matches.json",  ct);
            var fixtures = await ReadJsonAsync<FixturesJson>(dataPath, "fixtures.json", ct);
            var users    = await ReadJsonAsync<UsersJson>(dataPath,    "users.json",    ct);
            var squads   = await ReadJsonAsync<SquadsJson>(dataPath,   "squads.json",   ct);

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await SeedLeaguesAsync(db, users, ct);
            await UpsertTeamsAsync(db, teams, ct);
            await UpsertPlayersAsync(db, players, ct);
            await UpsertMatchesAsync(db, matches, ct);
            await ProcessFixturesAsync(db, fixtures, ct);
            await UpsertUsersAsync(db, users, ct);
            await UpsertSquadsAsync(db, squads, ct);

            logger.LogInformation("Data ingestion cycle complete");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Data ingestion cycle failed");
        }
    }

    // ── Leagues ───────────────────────────────────────────────────────────────

    /// <summary>
    /// users.json contains league_id references but there is no leagues.json.
    /// We derive distinct league IDs from users and ensure each one exists in the DB.
    /// </summary>
    private static async Task SeedLeaguesAsync(AppDbContext db, UsersJson data, CancellationToken ct)
    {
        var ids = data.Users
            .Where(u => u.LeagueId is not null)
            .Select(u => u.LeagueId!)
            .Distinct();

        foreach (var id in ids)
        {
            if (await db.Leagues.FindAsync([id], ct) is null)
                db.Leagues.Add(new League { Id = id, Name = id });
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Teams ─────────────────────────────────────────────────────────────────

    private static async Task UpsertTeamsAsync(AppDbContext db, TeamsJson data, CancellationToken ct)
    {
        foreach (var t in data.Teams)
        {
            var existing = await db.Teams.FindAsync([t.Id], ct);
            if (existing is null)
            {
                db.Teams.Add(new Team { Id = t.Id, Name = t.Name, Short = t.Short, Stadium = t.Stadium });
            }
            else
            {
                existing.Name = t.Name;
                existing.Short = t.Short;
                existing.Stadium = t.Stadium;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Players ───────────────────────────────────────────────────────────────

    private static async Task UpsertPlayersAsync(AppDbContext db, PlayersJson data, CancellationToken ct)
    {
        foreach (var p in data.Players)
        {
            var existing = await db.Players.Include(x => x.Stats).FirstOrDefaultAsync(x => x.Id == p.Id, ct);
            if (existing is null)
            {
                db.Players.Add(new Player
                {
                    Id = p.Id, Name = p.Name, Position = p.Position,
                    TeamId = p.TeamId, Price = p.Price, TotalPoints = p.TotalPoints,
                    Stats = MapStats(p.Stats)
                });
            }
            else
            {
                existing.Name = p.Name;
                existing.Position = p.Position;
                existing.TeamId = p.TeamId;
                existing.Price = p.Price;
                existing.TotalPoints = p.TotalPoints;
                ApplyStats(existing.Stats, p.Stats);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static PlayerStats MapStats(PlayerStatsJson s) => new()
    {
        Goals = s.Goals, Assists = s.Assists, YellowCards = s.YellowCards,
        RedCards = s.RedCards, MinutesPlayed = s.MinutesPlayed, CleanSheets = s.CleanSheets,
        OwnGoals = s.OwnGoals, PenaltiesMissed = s.PenaltiesMissed, Saves = s.Saves
    };

    private static void ApplyStats(PlayerStats target, PlayerStatsJson s)
    {
        target.Goals = s.Goals; target.Assists = s.Assists; target.YellowCards = s.YellowCards;
        target.RedCards = s.RedCards; target.MinutesPlayed = s.MinutesPlayed;
        target.CleanSheets = s.CleanSheets; target.OwnGoals = s.OwnGoals;
        target.PenaltiesMissed = s.PenaltiesMissed; target.Saves = s.Saves;
    }

    // ── Matches ───────────────────────────────────────────────────────────────

    private async Task UpsertMatchesAsync(AppDbContext db, MatchesJson data, CancellationToken ct)
    {
        foreach (var m in data.Matches)
        {
            var existing = await db.Matches.FindAsync([m.Id], ct);
            if (existing is null)
            {
                db.Matches.Add(new Match
                {
                    Id = m.Id, HomeTeamId = m.HomeTeamId, AwayTeamId = m.AwayTeamId,
                    Gameweek = m.Gameweek, Kickoff = m.Kickoff,
                    Status = m.Status, ScoreHome = m.Score.Home, ScoreAway = m.Score.Away,
                    Minute = m.Minute
                });

                await publisher.PublishAsync(new MatchUpdatedEvent(
                    m.Id, m.Status, m.Score.Home, m.Score.Away, m.Minute), ct);
            }
            else
            {
                bool changed = existing.Status != m.Status
                    || existing.ScoreHome != m.Score.Home
                    || existing.ScoreAway != m.Score.Away
                    || existing.Minute != m.Minute;

                existing.Status = m.Status;
                existing.ScoreHome = m.Score.Home;
                existing.ScoreAway = m.Score.Away;
                existing.Minute = m.Minute;

                if (changed)
                {
                    await publisher.PublishAsync(new MatchUpdatedEvent(
                        m.Id, m.Status, m.Score.Home, m.Score.Away, m.Minute), ct);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Each fixture's <c>id</c> (e.g. "f-001") is the idempotency key.
    /// If a row with that ID already exists the event has already been processed — skip.
    /// </summary>
    private async Task ProcessFixturesAsync(AppDbContext db, FixturesJson data, CancellationToken ct)
    {
        foreach (var f in data.Fixtures)
        {
            if (await db.Fixtures.FindAsync([f.Id], ct) is not null)
                continue; // Already processed — idempotency guard

            db.Fixtures.Add(new Fixture
            {
                Id = f.Id, MatchId = f.MatchId, Minute = f.Minute,
                Type = f.Type, PlayerId = f.PlayerId,
                AssistPlayerId = f.AssistPlayerId, TeamId = f.TeamId
            });

            await publisher.PublishAsync(new PlayerStatUpdatedEvent(
                PlayerId: f.PlayerId,
                MatchId: f.MatchId,
                EventType: f.Type,
                IdempotencyKey: f.Id
            ), ct);

            logger.LogInformation(
                "New fixture {FixtureId}: {Type} by player {PlayerId} in match {MatchId}",
                f.Id, f.Type, f.PlayerId, f.MatchId);
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Users ─────────────────────────────────────────────────────────────────

    private static async Task UpsertUsersAsync(AppDbContext db, UsersJson data, CancellationToken ct)
    {
        foreach (var u in data.Users)
        {
            var existing = await db.Users.FindAsync([u.Id], ct);
            if (existing is null)
            {
                db.Users.Add(new User
                {
                    Id = u.Id, Username = u.Username, Email = u.Email,
                    LeagueId = u.LeagueId, TotalPoints = u.TotalPoints
                });
            }
            else
            {
                existing.Username = u.Username;
                existing.Email = u.Email;
                existing.LeagueId = u.LeagueId;
                existing.TotalPoints = u.TotalPoints;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Squads ────────────────────────────────────────────────────────────────

    private static async Task UpsertSquadsAsync(AppDbContext db, SquadsJson data, CancellationToken ct)
    {
        foreach (var s in data.Squads)
        {
            var existing = await db.Squads
                .Include(x => x.Players)
                .FirstOrDefaultAsync(x => x.Id == s.Id, ct);

            if (existing is null)
            {
                existing = new Squad { Id = s.Id, UserId = s.UserId, Gameweek = s.Gameweek };
                db.Squads.Add(existing);
            }
            else
            {
                existing.UserId = s.UserId;
                existing.Gameweek = s.Gameweek;
                db.SquadPlayers.RemoveRange(existing.Players);
                existing.Players.Clear();
            }

            foreach (var p in s.Players)
            {
                existing.Players.Add(new SquadPlayer
                {
                    SquadId = s.Id,
                    PlayerId = p.PlayerId,
                    PositionSlot = p.PositionSlot,
                    IsCaptain = p.IsCaptain,
                    IsViceCaptain = p.IsViceCaptain,
                    IsBench = p.IsBench
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<T> ReadJsonAsync<T>(string basePath, string fileName, CancellationToken ct)
    {
        var path = Path.Combine(basePath, fileName);
        await using var stream = File.OpenRead(path);
        var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
        return result ?? throw new InvalidOperationException($"Failed to deserialize {fileName}");
    }
}
