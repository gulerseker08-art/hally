using Hally.Domain.Common;

namespace Hally.Domain.Notifications;

public enum ReminderTarget
{
    Task,
    Event,
}

public enum ReminderStatus
{
    Scheduled,
    Sent,
    Cancelled,
}

/// <summary>A point in time at which Hally nudges one adult (or every adult) about a task or event.
/// Reminders are evaluated lazily: if the target is done, cancelled or deleted when the reminder fires,
/// the reminder is cancelled instead of sent.</summary>
public class Reminder : Entity
{
    public const int MaxPerTarget = 5;

    private Reminder() { }

    public Reminder(Guid familyId, Guid createdByUserId, ReminderTarget target, Guid targetId, Guid? recipientUserId, DateTimeOffset fireAt, DateTimeOffset now)
    {
        var at = fireAt.ToUniversalTime();
        if (at < now.AddMinutes(-1))
        {
            throw new DomainException("reminder.in_past", "A reminder must be in the future.");
        }

        if (at > now.AddDays(400))
        {
            throw new DomainException("reminder.too_far", "A reminder can be at most 400 days ahead.");
        }

        FamilyId = familyId;
        CreatedByUserId = createdByUserId;
        Target = target;
        TargetId = targetId;
        RecipientUserId = recipientUserId;
        FireAt = at;
        CreatedAt = now;
    }

    public Guid FamilyId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public ReminderTarget Target { get; private set; }
    public Guid TargetId { get; private set; }

    /// <summary>Null means every adult in the family.</summary>
    public Guid? RecipientUserId { get; private set; }

    public DateTimeOffset FireAt { get; private set; }
    public ReminderStatus Status { get; private set; } = ReminderStatus.Scheduled;
    public DateTimeOffset? SentAt { get; private set; }

    public void MarkSent(DateTimeOffset now)
    {
        if (Status != ReminderStatus.Scheduled)
        {
            return;
        }

        Status = ReminderStatus.Sent;
        SentAt = now;
    }

    public void Cancel()
    {
        if (Status == ReminderStatus.Scheduled)
        {
            Status = ReminderStatus.Cancelled;
        }
    }
}

/// <summary>A push-capable device of a user. Tokens are rotated by the OS, so the token is the identity.</summary>
public class Device : Entity
{
    private Device() { }

    public Device(Guid userId, string platform, string token, DateTimeOffset now)
    {
        if (platform is not ("android" or "ios"))
        {
            throw new DomainException("device.platform_invalid", "Platform must be 'android' or 'ios'.");
        }

        if (string.IsNullOrWhiteSpace(token) || token.Length > 512)
        {
            throw new DomainException("device.token_invalid", "Push token must be 1-512 characters.");
        }

        UserId = userId;
        Platform = platform;
        Token = token.Trim();
        LastSeenAt = now;
    }

    public Guid UserId { get; private set; }
    public string Platform { get; private set; } = default!;
    public string Token { get; private set; } = default!;
    public DateTimeOffset LastSeenAt { get; private set; }

    public void Touch(Guid userId, DateTimeOffset now)
    {
        UserId = userId; // a token moves with the device when another user signs in
        LastSeenAt = now;
    }
}

public enum NotificationKind
{
    Reminder,
    EveningPrep,
}

/// <summary>An in-app inbox entry; the same content is pushed to the user's devices.
/// <see cref="DedupKey"/> makes job reruns safe: one entry per reminder/recipient or per prep day/recipient.</summary>
public class Notification : Entity
{
    private Notification() { }

    public Notification(Guid userId, Guid familyId, NotificationKind kind, string title, string body, ReminderTarget? targetKind, Guid? targetId, string dedupKey, DateTimeOffset now)
    {
        UserId = userId;
        FamilyId = familyId;
        Kind = kind;
        Title = title.Length > 200 ? title[..200] : title;
        Body = body.Length > 1000 ? body[..1000] : body;
        TargetKind = targetKind;
        TargetId = targetId;
        DedupKey = dedupKey;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Title { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public ReminderTarget? TargetKind { get; private set; }
    public Guid? TargetId { get; private set; }
    public string DedupKey { get; private set; } = default!;
    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
