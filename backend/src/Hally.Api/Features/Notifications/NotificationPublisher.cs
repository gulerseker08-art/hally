using Hally.Domain.Notifications;
using Hally.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Notifications;

/// <summary>Adds inbox entries to the current unit of work and pushes them after the caller has committed.
/// Push is best effort: the inbox is the source of truth.</summary>
public class NotificationPublisher(HallyDbContext db, IPushSender push, TimeProvider clock)
{
    private readonly List<Notification> _pending = [];

    /// <returns>false when this notification already exists (the job ran before), so nothing is added.</returns>
    public async Task<bool> AddAsync(Guid userId, Guid familyId, NotificationKind kind, string title, string body,
        ReminderTarget? targetKind, Guid? targetId, string dedupKey, CancellationToken ct)
    {
        if (_pending.Any(n => n.DedupKey == dedupKey) || await db.Notifications.AnyAsync(n => n.DedupKey == dedupKey, ct))
        {
            return false;
        }

        var notification = new Notification(userId, familyId, kind, title, body, targetKind, targetId, dedupKey, clock.GetUtcNow());
        db.Notifications.Add(notification);
        _pending.Add(notification);
        return true;
    }

    public async Task FlushPushAsync(CancellationToken ct)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var userIds = _pending.Select(n => n.UserId).Distinct().ToList();
        var devices = await db.Devices.Where(d => userIds.Contains(d.UserId)).ToListAsync(ct);
        var invalid = new HashSet<string>();
        foreach (var n in _pending)
        {
            var tokens = devices.Where(d => d.UserId == n.UserId).Select(d => d.Token).ToList();
            if (tokens.Count == 0)
            {
                continue;
            }

            var data = new Dictionary<string, string> { ["kind"] = n.Kind.ToString(), ["notificationId"] = n.Id.ToString(), ["familyId"] = n.FamilyId.ToString() };
            if (n.TargetId is { } target)
            {
                data["targetKind"] = n.TargetKind.ToString()!;
                data["targetId"] = target.ToString();
            }

            foreach (var token in await push.SendAsync(tokens, new PushMessage(n.Title, n.Body, data), ct))
            {
                invalid.Add(token);
            }
        }

        _pending.Clear();
        if (invalid.Count > 0)
        {
            await db.Devices.Where(d => invalid.Contains(d.Token)).ExecuteDeleteAsync(ct);
        }
    }
}

/// <summary>Short, localized notification texts. Times are shown in the family's time zone.</summary>
public static class NotificationTexts
{
    public static (string Title, string Body) Reminder(string locale, string title, DateTimeOffset? at, bool allDay, DateTimeOffset localNow)
    {
        var tr = locale != "en";
        if (at is not { } when)
        {
            return (title, tr ? "Bu görev hâlâ açık." : "This task is still open.");
        }

        var day = DateOnly.FromDateTime(when.DateTime);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var dayText = day == today ? (tr ? "Bugün" : "Today")
            : day == today.AddDays(1) ? (tr ? "Yarın" : "Tomorrow")
            : tr ? when.ToString("dd.MM") : when.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
        return (title, allDay ? dayText : $"{dayText} {when:HH:mm}");
    }

    public static (string Title, string Body) EveningPrep(string locale, int taskCount, int eventCount, IReadOnlyList<string> highlights)
    {
        var tr = locale != "en";
        var counts = tr
            ? string.Join(", ", new[] { eventCount > 0 ? $"{eventCount} etkinlik" : null, taskCount > 0 ? $"{taskCount} görev" : null }.OfType<string>())
            : string.Join(", ", new[] { eventCount > 0 ? $"{eventCount} event{(eventCount > 1 ? "s" : "")}" : null, taskCount > 0 ? $"{taskCount} task{(taskCount > 1 ? "s" : "")}" : null }.OfType<string>());
        var body = highlights.Count == 0 ? counts : $"{counts}: {string.Join(" · ", highlights)}";
        return (tr ? "Yarın için hazırlık" : "Ready for tomorrow", body);
    }
}
