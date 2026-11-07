using Hally.Domain.Common;

namespace Hally.Domain.Families;

/// <summary>An invitation for another adult to join a family. Only the token hash is stored.</summary>
public class Invitation : Entity
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

    private Invitation() { }

    public Invitation(Guid familyId, Guid invitedByUserId, string tokenHash, DateTimeOffset now, TimeSpan? lifetime = null)
    {
        FamilyId = familyId;
        InvitedByUserId = invitedByUserId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = now + (lifetime ?? DefaultLifetime);
    }

    public Guid FamilyId { get; private set; }
    public Family? Family { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public InvitationStatus StatusAt(DateTimeOffset now) => this switch
    {
        { RevokedAt: not null } => InvitationStatus.Revoked,
        { AcceptedAt: not null } => InvitationStatus.Accepted,
        _ when ExpiresAt <= now => InvitationStatus.Expired,
        _ => InvitationStatus.Pending,
    };

    public void Accept(Family family, Guid userId, DateTimeOffset now)
    {
        if (family.Id != FamilyId)
        {
            throw new InvalidOperationException("Family does not match invitation.");
        }

        if (family.IsMember(userId))
        {
            return; // already in the family: accepting again is a no-op
        }

        var status = StatusAt(now);
        if (status != InvitationStatus.Pending)
        {
            throw new DomainException($"invitation.{status.ToString().ToLowerInvariant()}", $"Invitation is {status}.");
        }

        family.AddAdult(userId);
        AcceptedByUserId = userId;
        AcceptedAt = now;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

public enum InvitationStatus
{
    Pending,
    Accepted,
    Expired,
    Revoked,
}
