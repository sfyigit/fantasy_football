namespace Shared.Domain.Entities;

/// <summary>
/// Fantasy league. Not present in mock data as a standalone file;
/// league IDs are discovered from users.json and seeded automatically by MatchDataIngestion.
/// </summary>
public class League
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}
