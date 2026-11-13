using Hally.Domain.Common;

namespace Hally.Domain.Billing;

public enum Plan
{
    Free,
    Plus,
}

/// <summary>Where the current entitlement came from.</summary>
public enum SubscriptionSource
{
    None,
    Trial,
    AppStore,
    GooglePlay,
}

/// <summary>What the client sees. Computed from the stored dates, never stored.</summary>
public enum SubscriptionState
{
    Free,
    Trialing,
    Active,
    GracePeriod,
    Expired,
}

/// <summary>The family's Family Plus entitlement. One row per family; it is kept after expiry so that the
/// free trial cannot be started twice. Store purchases are only ever written from a server-side verification,
/// never from what the client says it bought.</summary>
public class Subscription : Entity
{
    public static readonly TimeSpan DefaultTrialLength = TimeSpan.FromDays(7);

    private Subscription() { }

    public Subscription(Guid familyId)
    {
        FamilyId = familyId;
    }

    public Guid FamilyId { get; private set; }
    public SubscriptionSource Source { get; private set; }
    public string? ProductId { get; private set; }

    /// <summary>Apple originalTransactionId or Google purchase token: the key used to re-check the purchase.</summary>
    public string? StoreReference { get; private set; }

    public Guid? PurchasedByUserId { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public bool WillRenew { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? TrialStartedAt { get; private set; }
    public Guid? TrialStartedByUserId { get; private set; }
    public DateTimeOffset? LastVerifiedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool TrialUsed => TrialStartedAt is not null;

    public SubscriptionState StateAt(DateTimeOffset now, TimeSpan grace)
    {
        if (Source == SubscriptionSource.None || ExpiresAt is null)
        {
            return SubscriptionState.Free;
        }

        if (ExpiresAt > now)
        {
            return Source == SubscriptionSource.Trial ? SubscriptionState.Trialing : SubscriptionState.Active;
        }

        // Stores retry failed renewals for a few days; keep Plus during that window so a card hiccup
        // does not lock a family out mid-week.
        if (Source != SubscriptionSource.Trial && WillRenew && ExpiresAt + grace > now)
        {
            return SubscriptionState.GracePeriod;
        }

        return SubscriptionState.Expired;
    }

    public Plan PlanAt(DateTimeOffset now, TimeSpan grace) => StateAt(now, grace) switch
    {
        SubscriptionState.Trialing or SubscriptionState.Active or SubscriptionState.GracePeriod => Plan.Plus,
        _ => Plan.Free,
    };

    public void StartTrial(Guid userId, DateTimeOffset now, TimeSpan? length = null)
    {
        if (TrialUsed)
        {
            throw new DomainException("billing.trial_used", "This family already used its free trial.");
        }

        if (Source is SubscriptionSource.AppStore or SubscriptionSource.GooglePlay && ExpiresAt > now)
        {
            throw new DomainException("billing.already_subscribed", "This family already has Family Plus.");
        }

        Source = SubscriptionSource.Trial;
        ProductId = null;
        StoreReference = null;
        TrialStartedAt = now;
        TrialStartedByUserId = userId;
        StartedAt = now;
        ExpiresAt = now + (length ?? DefaultTrialLength);
        WillRenew = false;
        CancelledAt = null;
        UpdatedAt = now;
    }

    /// <summary>Applies a store's answer. Returns what changed so the caller can record analytics.</summary>
    public SubscriptionChange ApplyStore(SubscriptionSource store, string productId, string storeReference, DateTimeOffset expiresAt,
        bool willRenew, Guid? purchasedBy, DateTimeOffset now)
    {
        if (store is not (SubscriptionSource.AppStore or SubscriptionSource.GooglePlay))
        {
            throw new ArgumentOutOfRangeException(nameof(store));
        }

        var wasPaid = Source == store && StoreReference == storeReference && ExpiresAt > now;
        var wasRenewing = WillRenew;

        Source = store;
        ProductId = productId;
        StoreReference = storeReference;
        PurchasedByUserId = purchasedBy ?? PurchasedByUserId;
        StartedAt = wasPaid ? StartedAt : now;
        ExpiresAt = expiresAt;
        WillRenew = willRenew;
        LastVerifiedAt = now;
        UpdatedAt = now;

        if (!willRenew && wasRenewing)
        {
            CancelledAt = now;
            return SubscriptionChange.Cancelled;
        }

        if (willRenew)
        {
            CancelledAt = null;
        }

        return wasPaid ? SubscriptionChange.Renewed : expiresAt > now ? SubscriptionChange.Started : SubscriptionChange.None;
    }
}

public enum SubscriptionChange
{
    None,
    Started,
    Renewed,
    Cancelled,
}

/// <summary>What a plan allows. Values come from configuration (Billing:Plans) so they can change without a release.</summary>
public record PlanLimits(int MaxAdults, int ScansPerWeek, int ScansPerMonth, int ActiveShareLinks, int RemindersPerItem)
{
    /// <summary>A limit at or below zero means "no limit".</summary>
    public static bool Allows(int limit, int used) => limit <= 0 || used < limit;
}
