using VoiceHire.Domain.Enums;

namespace VoiceHire.Domain.Entities;

public class Organization
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<OrganizationMember> Members { get; set; } = [];
}

/// <summary>Source of truth for tenant membership. Never trust an organization id sent by the client.</summary>
public class OrganizationMember
{
    public Guid OrganizationId { get; set; }
    public required string UserId { get; set; }
    public OrganizationRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AuditEvent
{
    public long Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public string? ActorUserId { get; set; }
    public required string Action { get; set; }
    public string? EntityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
