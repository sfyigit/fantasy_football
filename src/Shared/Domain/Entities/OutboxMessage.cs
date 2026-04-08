namespace Shared.Domain.Entities;

/// <summary>
/// Transactional Outbox entry. Written in the same DB transaction as the business entity
/// (e.g. Fixture insert) so that a crash between the DB write and the RabbitMQ publish
/// cannot lose the event. The OutboxRelayService polls pending rows and publishes them.
/// </summary>
public class OutboxMessage
{
    public Guid      Id         { get; set; } = Guid.NewGuid();
    public string    EventType  { get; set; } = null!;
    public string    Payload    { get; set; } = null!;
    public string    RoutingKey { get; set; } = null!;
    public DateTime  CreatedAt  { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public int       Attempts   { get; set; }
}
