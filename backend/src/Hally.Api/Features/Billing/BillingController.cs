using System.Text;
using System.Text.Json;
using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Api.Features.Proposals;
using Hally.Domain.Billing;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hally.Api.Features.Billing;

public record PlanLimitsDto(int MaxAdults, int ScansPerWeek, int ScansPerMonth, int ActiveShareLinks, int RemindersPerItem);
public record ProductDto(string Id, string Period, IReadOnlyDictionary<string, string> FallbackPrices);
public record CatalogDto(IReadOnlyList<ProductDto> Products, int TrialDays, PlanLimitsDto Free, PlanLimitsDto Plus, IReadOnlyList<string> Stores);
public record UsageDto(int Adults, int ScansThisWeek, int ScansThisMonth, int ActiveShareLinks);

public record EntitlementDto(
    string Plan,
    string State,
    string? Source,
    string? ProductId,
    DateTimeOffset? ExpiresAt,
    bool WillRenew,
    bool TrialAvailable,
    bool PurchasedByMe,
    PlanLimitsDto Limits,
    UsageDto Usage);

public record PurchaseRequest(SubscriptionSource Store, string ProductId, string Token);

[ApiController]
[Authorize]
public class BillingController(
    HallyDbContext db,
    EntitlementService entitlements,
    SubscriptionUpdater updater,
    IOptions<BillingOptions> options,
    StoreVerification verification,
    IAnalytics analytics,
    TimeProvider clock) : ControllerBase
{
    /// <summary>Product ids and plan limits. Prices come from the store SDK on the device.</summary>
    [HttpGet("api/v1/billing/catalog")]
    public CatalogDto Catalog()
    {
        var o = options.Value;
        var stores = new List<string>();
        if (o.Apple.IsConfigured || verification.SandboxEnabled)
        {
            stores.Add(nameof(SubscriptionSource.AppStore));
        }

        if (o.Google.IsConfigured || verification.SandboxEnabled)
        {
            stores.Add(nameof(SubscriptionSource.GooglePlay));
        }

        return new CatalogDto(
            o.EffectiveProducts.Select(p => new ProductDto(p.Id, p.Period, p.FallbackPrices)).ToList(),
            o.TrialDays,
            ToDto(o.LimitsFor(Plan.Free)),
            ToDto(o.LimitsFor(Plan.Plus)),
            stores);
    }

    [HttpGet("api/v1/families/{familyId:guid}/entitlements")]
    [Authorize(Policy = Policies.FamilyMember)]
    public async Task<EntitlementDto> Get(Guid familyId, CancellationToken ct) => await Build(familyId, ct);

    /// <summary>Starts the 7-day Family Plus trial. Once per family, and once per adult across families.</summary>
    [HttpPost("api/v1/families/{familyId:guid}/subscription/trial")]
    [Authorize(Policy = Policies.FamilyMember)]
    [Idempotent]
    public async Task<EntitlementDto> StartTrial(Guid familyId, CancellationToken ct)
    {
        var userId = User.UserId();
        var current = await entitlements.GetAsync(familyId, userId, ct);
        if (!current.TrialAvailable)
        {
            throw new Domain.Common.DomainException("billing.trial_unavailable",
                current.Plan == Plan.Plus ? "This family already has Family Plus." : "The free trial was already used.");
        }

        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.FamilyId == familyId, ct);
        if (sub is null)
        {
            sub = new Subscription(familyId);
            db.Subscriptions.Add(sub);
        }

        sub.StartTrial(userId, clock.GetUtcNow(), TimeSpan.FromDays(Math.Clamp(options.Value.TrialDays, 1, 31)));
        analytics.Track(AnalyticsEvents.TrialStarted, userId, familyId);
        await db.SaveChangesAsync(ct);
        return await Build(familyId, ct);
    }

    /// <summary>Registers a store purchase made on the device. The store is asked directly; the request body is only a pointer.
    /// Also used for "restore purchases".</summary>
    [HttpPost("api/v1/families/{familyId:guid}/subscription/purchases")]
    [Authorize(Policy = Policies.FamilyMember)]
    [Idempotent]
    public async Task<EntitlementDto> Purchase(Guid familyId, PurchaseRequest request, CancellationToken ct)
    {
        if (request.Store is not (SubscriptionSource.AppStore or SubscriptionSource.GooglePlay) ||
            string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 1000)
        {
            throw new Domain.Common.DomainException("billing.purchase_invalid", "Unknown store or purchase token.");
        }

        var purchase = await verification.VerifyAsync(request.Store, request.Token.Trim(), ct);
        if (purchase is null || purchase.ExpiresAt <= clock.GetUtcNow())
        {
            throw new Domain.Common.DomainException("billing.purchase_not_found", "The store does not show an active Family Plus purchase.");
        }

        var owner = await db.Subscriptions.AsNoTracking()
            .Where(s => s.StoreReference == purchase.StoreReference && s.FamilyId != familyId)
            .Select(s => (Guid?)s.FamilyId).FirstOrDefaultAsync(ct);
        if (owner is not null)
        {
            throw new ConflictException("billing.purchase_in_use", "This purchase already unlocks Family Plus for another family.");
        }

        await updater.ApplyAsync(familyId, request.Store, purchase, User.UserId(), ct);
        return await Build(familyId, ct);
    }

    private async Task<EntitlementDto> Build(Guid familyId, CancellationToken ct)
    {
        var userId = User.UserId();
        var e = await entitlements.GetAsync(familyId, userId, ct);
        var u = await entitlements.UsageAsync(familyId, ct);
        var sub = e.Subscription;
        return new EntitlementDto(
            e.Plan.ToString(),
            e.State.ToString(),
            sub is null || sub.Source == SubscriptionSource.None ? null : sub.Source.ToString(),
            sub?.ProductId,
            sub?.ExpiresAt,
            sub?.WillRenew ?? false,
            e.TrialAvailable,
            sub?.PurchasedByUserId == userId,
            ToDto(e.Limits),
            new UsageDto(u.Adults, u.ScansThisWeek, u.ScansThisMonth, u.ActiveShareLinks));
    }

    private static PlanLimitsDto ToDto(PlanLimits l) => new(l.MaxAdults, l.ScansPerWeek, l.ScansPerMonth, l.ActiveShareLinks, l.RemindersPerItem);
}

/// <summary>Writes a verified store state onto the family's subscription and records the analytics events.</summary>
public class SubscriptionUpdater(HallyDbContext db, IAnalytics analytics, TimeProvider clock)
{
    public async Task ApplyAsync(Guid familyId, SubscriptionSource store, StorePurchase purchase, Guid? userId, CancellationToken ct)
    {
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.FamilyId == familyId, ct);
        if (sub is null)
        {
            sub = new Subscription(familyId);
            db.Subscriptions.Add(sub);
        }

        var change = sub.ApplyStore(store, purchase.ProductId, purchase.StoreReference, purchase.ExpiresAt, purchase.WillRenew, userId, clock.GetUtcNow());
        var props = new Dictionary<string, object?> { ["product"] = purchase.ProductId, ["store"] = store.ToString() };
        switch (change)
        {
            case SubscriptionChange.Started:
                analytics.Track(AnalyticsEvents.SubscriptionStarted, userId ?? sub.PurchasedByUserId, familyId, props);
                break;
            case SubscriptionChange.Cancelled:
                analytics.Track(AnalyticsEvents.SubscriptionCancelled, sub.PurchasedByUserId, familyId, props);
                break;
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Store server notifications. The payloads are only used to find which purchase changed;
/// its state is always re-read from the store API, so a forged notification can at most cause a re-check.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/billing/notifications")]
public class StoreNotificationsController(
    HallyDbContext db,
    StoreVerification verification,
    SubscriptionUpdater updater,
    IOptions<BillingOptions> options,
    ILogger<StoreNotificationsController> logger) : ControllerBase
{
    /// <summary>App Store Server Notifications V2: { "signedPayload": "..." }.</summary>
    [HttpPost("apple")]
    public async Task<IActionResult> Apple([FromBody] JsonElement body, CancellationToken ct)
    {
        string? originalTransactionId = null;
        try
        {
            var payload = AppStoreVerifier.JwsPayload(body.GetProperty("signedPayload").GetString()!);
            var tx = AppStoreVerifier.JwsPayload(payload.GetProperty("data").GetProperty("signedTransactionInfo").GetString()!);
            originalTransactionId = tx.GetProperty("originalTransactionId").GetString();
        }
        catch (Exception ex) when (ex is FormatException or KeyNotFoundException or InvalidOperationException or JsonException or ArgumentException)
        {
            return BadRequest();
        }

        await Refresh(SubscriptionSource.AppStore, originalTransactionId, ct);
        return Ok();
    }

    /// <summary>Google Play real-time developer notifications via a Pub/Sub push subscription
    /// whose URL carries <c>?token=</c> (Billing:Google:NotificationToken).</summary>
    [HttpPost("google")]
    public async Task<IActionResult> Google([FromQuery] string? token, [FromBody] JsonElement body, CancellationToken ct)
    {
        var expected = options.Value.Google.NotificationToken;
        if (expected.Length == 0 || token is null ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)))
        {
            return Unauthorized();
        }

        string? purchaseToken = null;
        try
        {
            var data = Convert.FromBase64String(body.GetProperty("message").GetProperty("data").GetString()!);
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("subscriptionNotification", out var n))
            {
                purchaseToken = n.GetProperty("purchaseToken").GetString();
            }
        }
        catch (Exception ex) when (ex is FormatException or KeyNotFoundException or InvalidOperationException or JsonException)
        {
            return BadRequest();
        }

        await Refresh(SubscriptionSource.GooglePlay, purchaseToken, ct);
        return Ok(); // ack so Pub/Sub does not redeliver
    }

    private async Task Refresh(SubscriptionSource store, string? reference, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return;
        }

        var sub = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Source == store && s.StoreReference == reference, ct);
        if (sub is null)
        {
            logger.LogInformation("Store notification for an unknown {Store} purchase", store);
            return;
        }

        var purchase = await verification.VerifyAsync(store, reference, ct);
        if (purchase is null)
        {
            return;
        }

        await updater.ApplyAsync(sub.FamilyId, store, purchase, null, ct);
    }
}
