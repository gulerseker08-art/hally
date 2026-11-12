namespace Hally.Domain.Planning;

/// <summary>Append-only record of state changes made on behalf of a user (including approved AI actions).</summary>
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FamilyId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = default!;
    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; }
    public string? Data { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
