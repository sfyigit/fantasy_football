using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shared.Data;

namespace MatchDataIngestion;

/// <summary>
/// Used only by the EF Core tooling (dotnet ef migrations add / update).
/// Not invoked at runtime.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=fantasyfootball;Username=postgres;Password=postgres")
            .Options;

        return new AppDbContext(options);
    }
}
