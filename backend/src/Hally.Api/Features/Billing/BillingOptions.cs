using Hally.Domain.Billing;

namespace Hally.Api.Features.Billing;

/// <summary>Plans, store products and store credentials. Limits and products are configuration so pricing
/// and packaging can change without an app release. Prices themselves always come from the stores.</summary>
public class BillingOptions
{
    public const string Section = "Billing";

    /// <summary>Days of Plus kept after a store renewal fails while the store is still retrying.</summary>
    public int GraceDays { get; set; } = 3;

    public int TrialDays { get; set; } = 7;

    public PlanLimitOptions Free { get; set; } = new() { MaxAdults = 2, ScansPerWeek = 3, ScansPerMonth = 0, ActiveShareLinks = 3, RemindersPerItem = 1 };

    public PlanLimitOptions Plus { get; set; } = new() { MaxAdults = 6, ScansPerWeek = 0, ScansPerMonth = 100, ActiveShareLinks = 0, RemindersPerItem = 5 };

    /// <summary>Store product ids. Empty = <see cref="DefaultProducts"/>.</summary>
    public List<ProductOptions> Products { get; set; } = [];

    /// <summary>Accepts "sandbox:..." purchase tokens without a store. Honoured only in Development and Testing.</summary>
    public bool SandboxPurchases { get; set; }

    public AppleStoreOptions Apple { get; set; } = new();

    public GooglePlayOptions Google { get; set; } = new();

    public IReadOnlyList<ProductOptions> EffectiveProducts => Products.Count > 0 ? Products : DefaultProducts;

    public static readonly IReadOnlyList<ProductOptions> DefaultProducts =
    [
        new() { Id = "hally_plus_monthly", Period = "monthly" },
        new() { Id = "hally_plus_yearly", Period = "yearly" },
    ];

    public TimeSpan Grace => TimeSpan.FromDays(Math.Max(0, GraceDays));

    public PlanLimits LimitsFor(Plan plan)
    {
        var o = plan == Plan.Plus ? Plus : Free;
        return new PlanLimits(o.MaxAdults, o.ScansPerWeek, o.ScansPerMonth, o.ActiveShareLinks, o.RemindersPerItem);
    }
}

public class PlanLimitOptions
{
    public int MaxAdults { get; set; }
    public int ScansPerWeek { get; set; }
    public int ScansPerMonth { get; set; }
    public int ActiveShareLinks { get; set; }
    public int RemindersPerItem { get; set; }
}

public class ProductOptions
{
    /// <summary>Same id in App Store Connect and Google Play Console.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>monthly | yearly</summary>
    public string Period { get; set; } = "monthly";

    /// <summary>Optional display prices used only until the store SDK returns the localized price,
    /// e.g. { "TRY": "₺139,99", "USD": "$7.99" }. Not a source of truth.</summary>
    public Dictionary<string, string> FallbackPrices { get; set; } = [];
}

public class AppleStoreOptions
{
    public string BundleId { get; set; } = string.Empty;
    public string IssuerId { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;

    /// <summary>App Store Connect API .p8 key (PEM). Secret: environment only.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>Production | Sandbox</summary>
    public string Environment { get; set; } = "Production";

    public bool IsConfigured => BundleId.Length > 0 && IssuerId.Length > 0 && KeyId.Length > 0 && PrivateKey.Length > 0;
}

public class GooglePlayOptions
{
    public string PackageName { get; set; } = string.Empty;

    /// <summary>Service account JSON with Android Publisher access. Secret: environment only.</summary>
    public string ServiceAccountJson { get; set; } = string.Empty;

    /// <summary>Shared secret in the Pub/Sub push URL (?token=...). Secret: environment only.</summary>
    public string NotificationToken { get; set; } = string.Empty;

    public bool IsConfigured => PackageName.Length > 0 && ServiceAccountJson.Length > 0;
}
