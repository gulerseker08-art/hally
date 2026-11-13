using Hally.Api.Features.Proposals;
using Hally.Domain.Billing;
using Hally.Domain.Capture;
using Hally.Domain.Common;
using Hally.Domain.Notifications;
using Hally.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hally.Api.Features.Billing;

/// <summary>Why the paywall opened. Sent to the client in the 402 body and used as the paywall_viewed trigger.</summary>
public static class PaywallTriggers
{
    public const string ScanLimit = "scan_limit";
    public const string AdultLimit = "adult_limit";
    public const string ShareLinkLimit = "share_link_limit";
    public const string ReminderLimit = "advanced_reminder";
}

/// <summary>Thrown when an action needs Family Plus. Mapped to 402 with <c>code</c>, <c>trigger</c> and <c>limit</c>.
/// Viewing existing data is never gated: only creating more than the plan allows.</summary>
public class PlanLimitException(string code, string trigger, int limit, string message) : Exception(message)
{
    public string Code { get; } = code;
    public string Trigger { get; } = trigger;
    public int Limit { get; } = limit;
}

public record Usage(int Adults, int ScansThisWeek, int ScansThisMonth, int ActiveShareLinks);

public record Entitlement(Plan Plan, SubscriptionState State, PlanLimits Limits, Subscription? Subscription, bool TrialAvailable);

public class EntitlementService(HallyDbContext db, IOptions<BillingOptions> options, TimeProvider clock)
{
    private BillingOptions O => options.Value;

    public async Task<Entitlement> GetAsync(Guid familyId, Guid? userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var sub = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.FamilyId == familyId, ct);
        var plan = sub?.PlanAt(now, O.Grace) ?? Plan.Free;
        var state = sub?.StateAt(now, O.Grace) ?? SubscriptionState.Free;
        var trialAvailable = plan == Plan.Free && sub?.TrialUsed != true &&
                             (userId is null || !await db.Subscriptions.AnyAsync(s => s.TrialStartedByUserId == userId, ct));
        return new Entitlement(plan, state, O.LimitsFor(plan), sub, trialAvailable);
    }

    public async Task<Usage> UsageAsync(Guid familyId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var (weekStart, monthStart) = await WindowsAsync(familyId, now, ct);
        var scans = db.Captures.Where(c => c.FamilyId == familyId && c.ContentType != null && c.Status != CaptureStatus.Failed);
        return new Usage(
            await db.FamilyMembers.CountAsync(m => m.FamilyId == familyId, ct),
            await scans.CountAsync(c => c.CreatedAt >= weekStart, ct),
            await scans.CountAsync(c => c.CreatedAt >= monthStart, ct),
            await db.ShareLinks.CountAsync(l => l.FamilyId == familyId && l.RevokedAt == null && l.ExpiresAt > now, ct));
    }

    /// <summary>Before a photo or document scan (text-only captures are not scans).</summary>
    public async Task EnsureCanScanAsync(Guid familyId, CancellationToken ct)
    {
        var (plan, limits) = await LimitsAsync(familyId, ct);
        var usage = await UsageAsync(familyId, ct);
        if (!PlanLimits.Allows(limits.ScansPerWeek, usage.ScansThisWeek))
        {
            throw new PlanLimitException("plan.scan_limit", PaywallTriggers.ScanLimit, limits.ScansPerWeek,
                $"The {plan} plan includes {limits.ScansPerWeek} scans per week.");
        }

        if (!PlanLimits.Allows(limits.ScansPerMonth, usage.ScansThisMonth))
        {
            throw new PlanLimitException("plan.scan_limit", PaywallTriggers.ScanLimit, limits.ScansPerMonth,
                $"The {plan} plan includes {limits.ScansPerMonth} scans per month.");
        }
    }

    /// <summary>Before inviting or adding another adult.</summary>
    public async Task EnsureCanAddAdultAsync(Guid familyId, CancellationToken ct)
    {
        var (plan, limits) = await LimitsAsync(familyId, ct);
        var adults = await db.FamilyMembers.CountAsync(m => m.FamilyId == familyId, ct);
        if (!PlanLimits.Allows(limits.MaxAdults, adults))
        {
            throw new PlanLimitException("plan.adult_limit", PaywallTriggers.AdultLimit, limits.MaxAdults,
                $"The {plan} plan includes {limits.MaxAdults} adults.");
        }
    }

    public async Task EnsureCanShareAsync(Guid familyId, CancellationToken ct)
    {
        var (plan, limits) = await LimitsAsync(familyId, ct);
        var now = clock.GetUtcNow();
        var active = await db.ShareLinks.CountAsync(l => l.FamilyId == familyId && l.RevokedAt == null && l.ExpiresAt > now, ct);
        if (!PlanLimits.Allows(limits.ActiveShareLinks, active))
        {
            throw new PlanLimitException("plan.share_link_limit", PaywallTriggers.ShareLinkLimit, limits.ActiveShareLinks,
                $"The {plan} plan includes {limits.ActiveShareLinks} active helper links.");
        }
    }

    public async Task EnsureCanRemindAsync(Guid familyId, Guid targetId, CancellationToken ct)
    {
        var (plan, limits) = await LimitsAsync(familyId, ct);
        var scheduled = await db.Reminders.CountAsync(r => r.TargetId == targetId && r.Status == ReminderStatus.Scheduled, ct);
        if (!PlanLimits.Allows(limits.RemindersPerItem, scheduled))
        {
            throw new PlanLimitException("plan.reminder_limit", PaywallTriggers.ReminderLimit, limits.RemindersPerItem,
                $"The {plan} plan includes {limits.RemindersPerItem} reminder(s) per item.");
        }
    }

    private async Task<(Plan Plan, PlanLimits Limits)> LimitsAsync(Guid familyId, CancellationToken ct)
    {
        var sub = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.FamilyId == familyId, ct);
        var plan = sub?.PlanAt(clock.GetUtcNow(), O.Grace) ?? Plan.Free;
        return (plan, O.LimitsFor(plan));
    }

    /// <summary>Calendar week (Monday) and month in the family's own time zone.</summary>
    private async Task<(DateTimeOffset WeekStart, DateTimeOffset MonthStart)> WindowsAsync(Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        var tz = await db.Families.Where(f => f.Id == familyId).Select(f => f.TimeZoneId).FirstAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(tz);
        var today = DateOnly.FromDateTime(FamilyTimeZone.ToFamilyTime(now, tz).DateTime);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var first = new DateOnly(today.Year, today.Month, 1);
        return (ProposalEngine.ToUtc(monday.ToString("yyyy-MM-dd"), null, zone)!.Value,
                ProposalEngine.ToUtc(first.ToString("yyyy-MM-dd"), null, zone)!.Value);
    }
}
