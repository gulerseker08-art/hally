using Hally.Api.Features.Proposals;
using Hally.Domain.Common;
using Hally.Domain.Notifications;
using Hally.Domain.Planning;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Hally.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hally.Api.Features.Notifications;

/// <summary>Every minute: sends due reminders. Reminders more than 12 hours late (e.g. after an outage) are cancelled, not sent.</summary>
public class ReminderDispatcher(HallyDbContext db, NotificationPublisher publisher, IAnalytics analytics, TimeProvider clock, ILogger<ReminderDispatcher> logger)
{
    public static readonly TimeSpan MaxLateness = TimeSpan.FromHours(12);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await db.Reminders.Where(r => r.Status == ReminderStatus.Scheduled && r.FireAt <= now)
            .OrderBy(r => r.FireAt).Take(200).ToListAsync(ct);
        if (due.Count == 0)
        {
            return 0;
        }

        var familyIds = due.Select(r => r.FamilyId).Distinct().ToList();
        var zones = await db.Families.AsNoTracking().Where(f => familyIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.TimeZoneId, ct);
        var members = await db.FamilyMembers.AsNoTracking().Where(m => familyIds.Contains(m.FamilyId))
            .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { m.FamilyId, u.Id, u.Locale }).ToListAsync(ct);

        var sent = 0;
        foreach (var reminder in due)
        {
            var target = await LoadTarget(reminder, ct);
            if (target is null || now - reminder.FireAt > MaxLateness)
            {
                reminder.Cancel();
                continue;
            }

            var zone = zones[reminder.FamilyId];
            var recipients = members.Where(m => m.FamilyId == reminder.FamilyId && (reminder.RecipientUserId is null || m.Id == reminder.RecipientUserId));
            foreach (var user in recipients)
            {
                var localAt = target.Value.At is { } at ? FamilyTimeZone.ToFamilyTime(at, zone) : (DateTimeOffset?)null;
                var (title, body) = NotificationTexts.Reminder(user.Locale, target.Value.Title, localAt, target.Value.AllDay, FamilyTimeZone.ToFamilyTime(now, zone));
                await publisher.AddAsync(user.Id, reminder.FamilyId, NotificationKind.Reminder, title, body, reminder.Target, reminder.TargetId,
                    $"rem:{reminder.Id:N}:{user.Id:N}", ct);
            }

            reminder.MarkSent(now);
            analytics.Track(AnalyticsEvents.ReminderSent, null, reminder.FamilyId, new Dictionary<string, object?> { ["target"] = reminder.Target.ToString().ToLowerInvariant() });
            sent++;
        }

        await db.SaveChangesAsync(ct);
        await publisher.FlushPushAsync(ct);
        logger.LogInformation("Reminders: {Sent} sent, {Cancelled} cancelled", sent, due.Count - sent);
        return sent;
    }

    private async Task<(string Title, DateTimeOffset? At, bool AllDay)?> LoadTarget(Reminder r, CancellationToken ct)
    {
        if (r.Target == ReminderTarget.Task)
        {
            var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.TargetId && t.FamilyId == r.FamilyId, ct);
            return task is { State: TaskState.Open } ? (task.Title, task.DueAt, task.AllDay) : null;
        }

        var evt = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == r.TargetId && e.FamilyId == r.FamilyId, ct);
        return evt is null ? null : (evt.Title, evt.StartsAt, evt.AllDay);
    }
}

/// <summary>Every 15 minutes: at each adult's chosen evening hour (family-local), sends a summary of tomorrow.
/// Nothing is sent when tomorrow is empty. The dedup key makes repeated runs within the hour harmless.</summary>
public class EveningPrepJob(HallyDbContext db, NotificationPublisher publisher, IAnalytics analytics, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var families = await db.Families.AsNoTracking().Select(f => new { f.Id, f.TimeZoneId }).ToListAsync(ct);
        var sent = 0;
        foreach (var family in families)
        {
            var local = FamilyTimeZone.ToFamilyTime(now, family.TimeZoneId);
            var users = await db.FamilyMembers.AsNoTracking().Where(m => m.FamilyId == family.Id)
                .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => u)
                .Where(u => u.EveningPrepEnabled && u.EveningPrepHour == local.Hour).ToListAsync(ct);
            if (users.Count == 0)
            {
                continue;
            }

            var zone = TimeZoneInfo.FindSystemTimeZoneById(family.TimeZoneId);
            var tomorrow = DateOnly.FromDateTime(local.DateTime).AddDays(1);
            var start = ProposalEngine.ToUtc(tomorrow.ToString("yyyy-MM-dd"), null, zone)!.Value;
            var end = ProposalEngine.ToUtc(tomorrow.AddDays(1).ToString("yyyy-MM-dd"), null, zone)!.Value;
            var tasks = await db.Tasks.AsNoTracking()
                .Where(t => t.FamilyId == family.Id && t.State == TaskState.Open && t.DueAt >= start && t.DueAt < end)
                .OrderBy(t => t.DueAt).ToListAsync(ct);
            var events = (await db.Events.AsNoTracking()
                    .Where(e => e.FamilyId == family.Id && e.StartsAt < end && e.StartsAt >= start.AddDays(-2))
                    .OrderBy(e => e.StartsAt).ToListAsync(ct))
                .Where(e => e.Overlaps(start, end)).ToList();
            if (tasks.Count == 0 && events.Count == 0)
            {
                continue;
            }

            var highlights = events.Select(e => e.AllDay ? e.Title : $"{e.Title} {FamilyTimeZone.ToFamilyTime(e.StartsAt, family.TimeZoneId):HH:mm}")
                .Concat(tasks.Select(t => t.Title)).Take(3).ToList();
            foreach (var user in users)
            {
                var (title, body) = NotificationTexts.EveningPrep(user.Locale, tasks.Count, events.Count, highlights);
                if (await publisher.AddAsync(user.Id, family.Id, NotificationKind.EveningPrep, title, body, null, null,
                        $"prep:{family.Id:N}:{tomorrow:yyyyMMdd}:{user.Id:N}", ct))
                {
                    analytics.Track(AnalyticsEvents.EveningPrepSent, user.Id, family.Id, new Dictionary<string, object?> { ["tasks"] = tasks.Count, ["events"] = events.Count });
                    sent++;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await publisher.FlushPushAsync(ct);
        return sent;
    }
}

/// <summary>Nightly: deletes capture originals after <see cref="StorageOptions.RetentionDays"/> (extracted data is kept)
/// and prunes short-lived technical records.</summary>
public class RetentionJob(HallyDbContext db, IDocumentStore store, IOptions<StorageOptions> storage, TimeProvider clock, ILogger<RetentionJob> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cutoff = now.AddDays(-Math.Max(storage.Value.RetentionDays, 1));
        var expired = await db.Captures.Where(c => c.StorageKey != null && c.CreatedAt < cutoff).Take(500).ToListAsync(ct);
        foreach (var capture in expired)
        {
            await store.DeleteAsync(capture.StorageKey!, ct);
            capture.ForgetOriginal();
        }

        await db.SaveChangesAsync(ct);

        var dayAgo = now.AddDays(-1);
        var monthAgo = now.AddDays(-30);
        var quarterAgo = now.AddDays(-90);
        await db.IdempotencyRecords.Where(r => r.CreatedAt < dayAgo).ExecuteDeleteAsync(ct);
        await db.RefreshTokens.Where(t => t.ExpiresAt < monthAgo).ExecuteDeleteAsync(ct);
        await db.Notifications.Where(n => n.CreatedAt < quarterAgo).ExecuteDeleteAsync(ct);
        await db.Reminders.Where(r => r.Status != ReminderStatus.Scheduled && r.FireAt < quarterAgo).ExecuteDeleteAsync(ct);
        logger.LogInformation("Retention: {Count} original(s) deleted", expired.Count);
        return expired.Count;
    }
}
