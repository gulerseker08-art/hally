using Hally.Domain.Billing;
using Hally.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hally.Api.Features.Billing;

/// <summary>Hourly safety net for missed store notifications: re-checks store subscriptions that are about to
/// expire or are inside the grace window, so renewals and cancellations land even without a webhook.</summary>
public class SubscriptionRefreshJob(
    HallyDbContext db,
    StoreVerification verification,
    SubscriptionUpdater updater,
    IOptions<BillingOptions> options,
    TimeProvider clock,
    ILogger<SubscriptionRefreshJob> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var soon = now.AddHours(1);
        var since = now - options.Value.Grace - TimeSpan.FromDays(1);
        var verifiedBefore = now.AddHours(-1);
        var due = await db.Subscriptions.AsNoTracking()
            .Where(s => (s.Source == SubscriptionSource.AppStore || s.Source == SubscriptionSource.GooglePlay) &&
                        s.StoreReference != null && s.ExpiresAt < soon && s.ExpiresAt > since &&
                        (s.LastVerifiedAt == null || s.LastVerifiedAt < verifiedBefore))
            .OrderBy(s => s.ExpiresAt).Take(200).ToListAsync(ct);

        var refreshed = 0;
        foreach (var sub in due)
        {
            try
            {
                var purchase = await verification.VerifyAsync(sub.Source, sub.StoreReference!, ct);
                if (purchase is not null)
                {
                    await updater.ApplyAsync(sub.FamilyId, sub.Source, purchase, null, ct);
                    refreshed++;
                }
            }
            catch (BillingUnavailableException ex)
            {
                logger.LogWarning("Subscription refresh skipped: {Reason}", ex.Message);
                break;
            }
        }

        return refreshed;
    }
}
