using Microsoft.EntityFrameworkCore;
using Shared.Domain.Entities;

namespace Shared.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Fixture> Fixtures => Set<Fixture>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Squad> Squads => Set<Squad>();
    public DbSet<SquadPlayer> SquadPlayers => Set<SquadPlayer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>()
            .OwnsOne(p => p.Stats);

        modelBuilder.Entity<Squad>()
            .HasMany(s => s.Players)
            .WithOne()
            .HasForeignKey(sp => sp.SquadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
