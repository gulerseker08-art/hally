using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Hally.Domain.Billing;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hally.Api.Features.Billing;

/// <summary>What a store says about a purchase. <see cref="StoreReference"/> is the stable key we re-check with later
/// (Apple originalTransactionId, Google purchase token).</summary>
public record StorePurchase(string ProductId, string StoreReference, DateTimeOffset ExpiresAt, bool WillRenew);

/// <summary>Server-side purchase validation (App Store Server API / Google Play Developer API).
/// The client's own claim about a purchase is never trusted.</summary>
public interface IStoreVerifier
{
    SubscriptionSource Store { get; }
    bool IsConfigured { get; }

    /// <summary>Returns null when the store does not know the purchase.</summary>
    Task<StorePurchase?> VerifyAsync(string token, CancellationToken ct);
}

public class BillingUnavailableException(string message) : Exception(message);

/// <summary>Picks the verifier for a store, or the sandbox for "sandbox:" tokens in Development/Testing.</summary>
public class StoreVerification(IEnumerable<IStoreVerifier> verifiers, SandboxStoreVerifier sandbox, IOptions<BillingOptions> options, IHostEnvironment env)
{
    public bool SandboxEnabled => options.Value.SandboxPurchases && (env.IsDevelopment() || env.IsEnvironment("Testing"));

    public Task<StorePurchase?> VerifyAsync(SubscriptionSource store, string token, CancellationToken ct)
    {
        if (token.StartsWith(SandboxStoreVerifier.Prefix, StringComparison.Ordinal))
        {
            return SandboxEnabled ? sandbox.VerifyAsync(token, ct) : Task.FromResult<StorePurchase?>(null);
        }

        var verifier = verifiers.FirstOrDefault(v => v.Store == store && v.IsConfigured)
                       ?? throw new BillingUnavailableException($"{store} purchases cannot be verified yet: store credentials are not configured.");
        return verifier.VerifyAsync(token, ct);
    }
}

/// <summary>Test purchases: "sandbox:{product}:{ref}" renews, "sandbox-cancelled:..." will not renew,
/// "sandbox-expired:..." has already expired. Only used when <see cref="StoreVerification.SandboxEnabled"/>.</summary>
public class SandboxStoreVerifier(IOptions<BillingOptions> options, TimeProvider clock)
{
    public const string Prefix = "sandbox";

    public Task<StorePurchase?> VerifyAsync(string token, CancellationToken ct)
    {
        var parts = token.Split(':');
        if (parts.Length < 3)
        {
            return Task.FromResult<StorePurchase?>(null);
        }

        var product = options.Value.EffectiveProducts.FirstOrDefault(p => p.Id == parts[1]);
        if (product is null)
        {
            return Task.FromResult<StorePurchase?>(null);
        }

        var now = clock.GetUtcNow();
        var expires = parts[0] switch
        {
            "sandbox-expired" => now.AddDays(-10),
            _ => product.Period == "yearly" ? now.AddYears(1) : now.AddMonths(1),
        };
        return Task.FromResult<StorePurchase?>(new StorePurchase(product.Id, token, expires, parts[0] == "sandbox"));
    }
}

/// <summary>App Store Server API: GET /inApps/v1/subscriptions/{transactionId}.</summary>
public class AppStoreVerifier(HttpClient http, IOptions<BillingOptions> options, TimeProvider clock, ILogger<AppStoreVerifier> logger) : IStoreVerifier
{
    public SubscriptionSource Store => SubscriptionSource.AppStore;
    public bool IsConfigured => options.Value.Apple.IsConfigured;

    public async Task<StorePurchase?> VerifyAsync(string transactionId, CancellationToken ct)
    {
        var apple = options.Value.Apple;
        if (!transactionId.All(char.IsDigit))
        {
            return null;
        }

        var host = apple.Environment.Equals("Sandbox", StringComparison.OrdinalIgnoreCase)
            ? "https://api.storekit-sandbox.itunes.apple.com"
            : "https://api.storekit.itunes.apple.com";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/inApps/v1/subscriptions/{transactionId}");
        request.Headers.Authorization = new("Bearer", CreateToken(apple, clock.GetUtcNow()));
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("App Store Server API answered {Status}", (int)response.StatusCode);
            throw new BillingUnavailableException("The App Store could not be reached.");
        }

        var products = options.Value.EffectiveProducts.Select(p => p.Id).ToHashSet();
        return ParseStatuses(await response.Content.ReadAsStringAsync(ct), apple.BundleId, products);
    }

    /// <summary>Picks the subscription for one of our products from a subscription-status response.
    /// The response comes straight from Apple over TLS, so the JWS payloads are read without re-checking Apple's
    /// certificate chain; notifications from outside are never trusted this way, they only trigger this call.</summary>
    public static StorePurchase? ParseStatuses(string json, string bundleId, IReadOnlySet<string> products)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("bundleId", out var bid) && bid.GetString() != bundleId)
        {
            return null;
        }

        StorePurchase? best = null;
        foreach (var group in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            foreach (var last in group.GetProperty("lastTransactions").EnumerateArray())
            {
                var tx = JwsPayload(last.GetProperty("signedTransactionInfo").GetString()!);
                var productId = tx.GetProperty("productId").GetString()!;
                if (!products.Contains(productId) || !tx.TryGetProperty("expiresDate", out var exp))
                {
                    continue;
                }

                if (tx.TryGetProperty("revocationDate", out _))
                {
                    continue; // refunded
                }

                var status = last.GetProperty("status").GetInt32(); // 1 active, 2 expired, 3 billing retry, 4 grace, 5 revoked
                var renew = status is 1 or 3 or 4;
                if (last.TryGetProperty("signedRenewalInfo", out var renewal) && renewal.GetString() is { Length: > 0 } r)
                {
                    renew = renew && JwsPayload(r).TryGetProperty("autoRenewStatus", out var ar) && ar.GetInt32() == 1;
                }

                var candidate = new StorePurchase(productId, last.GetProperty("originalTransactionId").GetString()!,
                    DateTimeOffset.FromUnixTimeMilliseconds(exp.GetInt64()), renew);
                if (status == 5)
                {
                    candidate = candidate with { ExpiresAt = DateTimeOffset.UnixEpoch, WillRenew = false };
                }

                if (best is null || candidate.ExpiresAt > best.ExpiresAt)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    public static JsonElement JwsPayload(string jws)
    {
        var parts = jws.Split('.');
        if (parts.Length != 3)
        {
            throw new FormatException("Not a JWS.");
        }

        return JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1])).RootElement.Clone();
    }

    private static string CreateToken(AppleStoreOptions apple, DateTimeOffset now)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(apple.PrivateKey);
        var credentials = new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = apple.KeyId }, SecurityAlgorithms.EcdsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };
        var header = new JwtHeader(credentials);
        var payload = new JwtPayload
        {
            ["iss"] = apple.IssuerId,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(10).ToUnixTimeSeconds(),
            ["aud"] = "appstoreconnect-v1",
            ["bid"] = apple.BundleId,
        };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }
}

/// <summary>Google Play Developer API: purchases.subscriptionsv2.get, and acknowledges new purchases
/// (unacknowledged purchases are refunded by Google after three days).</summary>
public class GooglePlayVerifier(HttpClient http, IOptions<BillingOptions> options, ILogger<GooglePlayVerifier> logger) : IStoreVerifier
{
    private const string Api = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications";
    private ITokenAccess? _credential;

    public SubscriptionSource Store => SubscriptionSource.GooglePlay;
    public bool IsConfigured => options.Value.Google.IsConfigured;

    public async Task<StorePurchase?> VerifyAsync(string purchaseToken, CancellationToken ct)
    {
        var google = options.Value.Google;
        _credential ??= GoogleCredential.FromJson(google.ServiceAccountJson)
            .CreateScoped("https://www.googleapis.com/auth/androidpublisher").UnderlyingCredential;
        var accessToken = await _credential.GetAccessTokenForRequestAsync(cancellationToken: ct);
        var pkg = Uri.EscapeDataString(google.PackageName);
        var token = Uri.EscapeDataString(purchaseToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Api}/{pkg}/purchases/subscriptionsv2/tokens/{token}");
        request.Headers.Authorization = new("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Gone)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Google Play Developer API answered {Status}", (int)response.StatusCode);
            throw new BillingUnavailableException("Google Play could not be reached.");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var products = options.Value.EffectiveProducts.Select(p => p.Id).ToHashSet();
        var purchase = ParseSubscription(json, purchaseToken, products, out var needsAck);
        if (purchase is not null && needsAck)
        {
            using var ack = new HttpRequestMessage(HttpMethod.Post,
                $"{Api}/{pkg}/purchases/subscriptions/{Uri.EscapeDataString(purchase.ProductId)}/tokens/{token}:acknowledge")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            ack.Headers.Authorization = new("Bearer", accessToken);
            using var ackResponse = await http.SendAsync(ack, ct);
            if (!ackResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Google Play acknowledge answered {Status}", (int)ackResponse.StatusCode);
            }
        }

        return purchase;
    }

    public static StorePurchase? ParseSubscription(string json, string purchaseToken, IReadOnlySet<string> products, out bool needsAcknowledge)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        needsAcknowledge = root.TryGetProperty("acknowledgementState", out var ack) && ack.GetString() == "ACKNOWLEDGEMENT_STATE_PENDING";
        var state = root.TryGetProperty("subscriptionState", out var s) ? s.GetString() : null;

        StorePurchase? best = null;
        foreach (var item in root.GetProperty("lineItems").EnumerateArray())
        {
            var productId = item.GetProperty("productId").GetString()!;
            if (!products.Contains(productId) || !item.TryGetProperty("expiryTime", out var exp))
            {
                continue;
            }

            var renew = item.TryGetProperty("autoRenewingPlan", out var plan) &&
                        plan.TryGetProperty("autoRenewEnabled", out var enabled) && enabled.GetBoolean();
            var expires = DateTimeOffset.Parse(exp.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            if (state is "SUBSCRIPTION_STATE_PENDING" or "SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED")
            {
                continue; // not paid yet
            }

            if (best is null || expires > best.ExpiresAt)
            {
                best = new StorePurchase(productId, purchaseToken, expires,
                    renew && state is "SUBSCRIPTION_STATE_ACTIVE" or "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" or "SUBSCRIPTION_STATE_ON_HOLD");
            }
        }

        if (best is not null && state is "SUBSCRIPTION_STATE_ON_HOLD" or "SUBSCRIPTION_STATE_PAUSED")
        {
            // Account hold / paused: no access until the payment is fixed, even if expiryTime is in the future.
            best = best with { ExpiresAt = DateTimeOffset.UnixEpoch };
        }

        needsAcknowledge &= best is not null;
        return best;
    }
}
