using Hally.Domain.Common;

namespace Hally.Domain.Sharing;

public enum ShareLinkKind
{
    /// <summary>One task.</summary>
    Task,

    /// <summary>The family's plan for one day: that day's open tasks and events.</summary>
    Day,
}

/// <summary>A read-only web link for a helper who doesn't use the app (grandparent, nanny, driver).
/// Only the token hash is stored. Links are scoped to the shared items, revocable and short-lived.</summary>
public class ShareLink : Entity
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(72);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(7);

    private ShareLink() { }

    private ShareLink(Guid familyId, Guid createdByUserId, ShareLinkKind kind, Guid? taskId, DateOnly? day, string tokenHash, DateTimeOffset now, TimeSpan? lifetime)
    {
        var life = lifetime ?? DefaultLifetime;
        if (life <= TimeSpan.Zero || life > MaxLifetime)
        {
            throw new DomainException("share_link.lifetime_invalid", "A share link can live between 1 hour and 7 days.");
        }

        FamilyId = familyId;
        CreatedByUserId = createdByUserId;
        Kind = kind;
        TaskId = taskId;
        Day = day;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = now + life;
    }

    public static ShareLink ForTask(Guid familyId, Guid userId, Guid taskId, string tokenHash, DateTimeOffset now, TimeSpan? lifetime = null) =>
        new(familyId, userId, ShareLinkKind.Task, taskId, null, tokenHash, now, lifetime);

    public static ShareLink ForDay(Guid familyId, Guid userId, DateOnly day, string tokenHash, DateTimeOffset now, TimeSpan? lifetime = null) =>
        new(familyId, userId, ShareLinkKind.Day, null, day, tokenHash, now, lifetime);

    public Guid FamilyId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public ShareLinkKind Kind { get; private set; }
    public Guid? TaskId { get; private set; }
    public DateOnly? Day { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public int OpenCount { get; private set; }
    public DateTimeOffset? LastOpenedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    public void Opened(DateTimeOffset now)
    {
        OpenCount++;
        LastOpenedAt = now;
    }
}
