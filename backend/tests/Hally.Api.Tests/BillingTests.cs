using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Hally.Api.Features.Billing;
using Hally.Domain.Billing;
using Hally.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hally.Api.Tests;

public record LimitsBody(int MaxAdults, int ScansPerWeek, int ScansPerMonth, int ActiveShareLinks, int RemindersPerItem);
public record UsageBody(int Adults, int ScansThisWeek, int ScansThisMonth, int ActiveShareLinks);
public record EntitlementBody(string Plan, string State, string? Source, string? ProductId, DateTimeOffset? ExpiresAt, bool WillRenew,
    bool TrialAvailable, bool PurchasedByMe, LimitsBody Limits, UsageBody Usage);

[Collection(ApiCollection.Name)]
public class BillingTests(HallyApiFactory api)
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private async Task<(HttpClient Client, Guid FamilyId)> Family(string? email = null)
    {
        var client = await api.SignInAsync(email ?? $"{Guid.NewGuid():N}@example.com", "Ayşe Yılmaz");
        var family = (await (await client.PostIdempotentAsync("/api/v1/families", new { name = "Yılmaz", timeZoneId = "Europe/Istanbul" })).Content.ReadFromJsonAsync<FamilyBody>())!;
        return (client, family.Id);
    }

    private static Task<HttpResponseMessage> Scan(HttpClient client, Guid familyId)
    {
        var form = new MultipartFormDataContent { { new StringContent("Camera"), "source" } };
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "photo.png");
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/families/{familyId}/captures") { Content = form };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> TextCapture(HttpClient client, Guid familyId)
    {
        var form = new MultipartFormDataContent { { new StringContent("Text"), "source" }, { new StringContent("Cuma veli toplantısı"), "text" } };
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/families/{familyId}/captures") { Content = form };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return client.SendAsync(req);
    }

    private static async Task<EntitlementBody> Entitlement(HttpClient c, Guid familyId) =>
        (await c.GetFromJsonAsync<EntitlementBody>($"/api/v1/families/{familyId}/entitlements"))!;

    private static async Task ShouldHitPaywall(HttpResponseMessage res, string trigger)
    {
        res.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("trigger").GetString().Should().Be(trigger);
        body.GetProperty("limit").GetInt32().Should().BePositive();
    }

    [Fact]
    public async Task Free_family_gets_three_scans_a_week_then_the_paywall_and_the_trial_unlocks_more()
    {
        var (owner, familyId) = await Family();
        var start = await Entitlement(owner, familyId);
        start.Plan.Should().Be("Free");
        start.TrialAvailable.Should().BeTrue();
        start.Limits.ScansPerWeek.Should().Be(3);

        for (var i = 0; i < 3; i++)
        {
            (await Scan(owner, familyId)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await ShouldHitPaywall(await Scan(owner, familyId), PaywallTriggers.ScanLimit);
        (await TextCapture(owner, familyId)).StatusCode.Should().Be(HttpStatusCode.Created, "typing or pasting text is not a scan");
        (await Entitlement(owner, familyId)).Usage.ScansThisWeek.Should().Be(3);

        var trial = await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/subscription/trial", null);
        trial.StatusCode.Should().Be(HttpStatusCode.OK);
        var plus = (await trial.Content.ReadFromJsonAsync<EntitlementBody>())!;
        plus.Plan.Should().Be("Plus");
        plus.State.Should().Be("Trialing");
        plus.ExpiresAt.Should().BeCloseTo(api.Clock.GetUtcNow().AddDays(7), TimeSpan.FromMinutes(1));
        plus.TrialAvailable.Should().BeFalse();
        (await Scan(owner, familyId)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/subscription/trial", null)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "the trial is once per family");

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HallyDbContext>();
            (await db.AnalyticsEvents.CountAsync(e => e.FamilyId == familyId && e.Name == "trial_started")).Should().Be(1);
        }
    }

    [Fact]
    public async Task Trial_ends_after_seven_days_and_existing_data_stays_readable()
    {
        var (owner, familyId) = await Family();
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/subscription/trial", null);
        var task = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks", new { title = "Kırtasiye", allDay = false })).Content.ReadFromJsonAsync<TaskBody>())!;

        api.Clock.Advance(TimeSpan.FromDays(8));
        try
        {
            var after = await Entitlement(owner, familyId);
            after.Plan.Should().Be("Free");
            after.State.Should().Be("Expired");
            after.TrialAvailable.Should().BeFalse();
            (await owner.GetAsync($"/api/v1/families/{familyId}/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await owner.GetAsync($"/api/v1/families/{familyId}/today")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            api.Clock.Advance(TimeSpan.FromDays(-8));
        }
    }

    [Fact]
    public async Task One_adult_cannot_start_a_second_trial_in_another_family()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var (owner, first) = await Family(email);
        await owner.PostIdempotentAsync($"/api/v1/families/{first}/subscription/trial", null);
        var second = (await (await owner.PostIdempotentAsync("/api/v1/families", new { name = "İkinci" })).Content.ReadFromJsonAsync<FamilyBody>())!;
        (await Entitlement(owner, second.Id)).TrialAvailable.Should().BeFalse();
        (await owner.PostIdempotentAsync($"/api/v1/families/{second.Id}/subscription/trial", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Third_adult_needs_family_plus()
    {
        var (owner, familyId) = await Family();
        var early = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/invitations", null)).Content.ReadFromJsonAsync<InvitationBody>())!;
        var spare = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/invitations", null)).Content.ReadFromJsonAsync<InvitationBody>())!;
        var partner = await api.SignInAsync($"{Guid.NewGuid():N}@example.com", "Mehmet");
        (await partner.PostIdempotentAsync($"/api/v1/invitations/{early.Token}/accept", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await ShouldHitPaywall(await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/invitations", null), PaywallTriggers.AdultLimit);
        var grandma = await api.SignInAsync($"{Guid.NewGuid():N}@example.com", "Fatma");
        await ShouldHitPaywall(await grandma.PostIdempotentAsync($"/api/v1/invitations/{spare.Token}/accept", null), PaywallTriggers.AdultLimit);

        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/subscription/trial", null);
        (await grandma.PostIdempotentAsync($"/api/v1/invitations/{spare.Token}/accept", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Entitlement(owner, familyId)).Usage.Adults.Should().Be(3);
    }

    [Fact]
    public async Task Free_plan_keeps_three_active_helper_links_and_one_reminder_per_item()
    {
        var (owner, familyId) = await Family();
        var task = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks", new { title = "Okul servisi", allDay = false })).Content.ReadFromJsonAsync<TaskBody>())!;
        var links = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var res = await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/share-links", new { kind = "Task", taskId = task.Id });
            res.StatusCode.Should().Be(HttpStatusCode.Created);
            links.Add((await res.Content.ReadFromJsonAsync<ShareCreatedBody>())!.Id);
        }

        await ShouldHitPaywall(await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/share-links", new { kind = "Task", taskId = task.Id }), PaywallTriggers.ShareLinkLimit);
        (await owner.DeleteAsync($"/api/v1/families/{familyId}/share-links/{links[0]}")).IsSuccessStatusCode.Should().BeTrue();
        (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/share-links", new { kind = "Task", taskId = task.Id })).StatusCode
            .Should().Be(HttpStatusCode.Created, "a revoked link frees its slot");

        object Reminder(int hours) => new { target = "Task", targetId = task.Id, fireAt = api.Clock.GetUtcNow().AddHours(hours), forEveryone = true };
        (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/reminders", Reminder(1))).StatusCode.Should().Be(HttpStatusCode.Created);
        await ShouldHitPaywall(await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/reminders", Reminder(2)), PaywallTriggers.ReminderLimit);

        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/subscription/trial", null);
        (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/reminders", Reminder(2))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/share-links", new { kind = "Task", taskId = task.Id })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Store_purchase_is_verified_server_side_and_cannot_unlock_two_families()
    {
        var (owner, familyId) = await Family();
        var token = $"sandbox:hally_plus_yearly:{Guid.NewGuid():N}";
        var res = await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/subscription/purchases",
            new { store = "GooglePlay", productId = "hally_plus_yearly", token });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var plus = (await res.Content.ReadFromJsonAsync<EntitlementBody>())!;
        plus.Plan.Should().Be("Plus");
        plus.State.Should().Be("Active");
        plus.Source.Should().Be("GooglePlay");
        plus.ProductId.Should().Be("hally_plus_yearly");
        plus.WillRenew.Should().BeTrue();
        plus.PurchasedByMe.Should().BeTrue();
        plus.Limits.MaxAdults.Should().Be(6);
        plus.Limits.ScansPerMonth.Should().Be(100);

        var (other, otherFamily) = await Family();
        (await other.PostIdempotentAsync($"/api/v1/families/{otherFamily}/subscription/purchases",
            new { store = "GooglePlay", productId = "hally_plus_yearly", token })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await other.PostIdempotentAsync($"/api/v1/families/{otherFamily}/subscription/purchases",
            new { store = "AppStore", productId = "hally_plus_monthly", token = "sandbox-expired:hally_plus_monthly:x" })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await other.PostIdempotentAsync($"/api/v1/families/{otherFamily}/subscription/purchases",
            new { store = "AppStore", productId = "x", token = "made-up-receipt" })).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable, "real store credentials are not configured in tests");
        (await Entitlement(other, otherFamily)).Plan.Should().Be("Free");

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HallyDbContext>();
        (await db.AnalyticsEvents.CountAsync(e => e.FamilyId == familyId && e.Name == "subscription_started")).Should().Be(1);
    }

    [Fact]
    public async Task Store_notifications_only_trigger_a_recheck()
    {
        var anonymous = api.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/billing/notifications/google?token=wrong", new { message = new { data = "e30=" } })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/billing/notifications/apple", new { signedPayload = "not-a-jws" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        static string Jws(object payload) => $"e30.{Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.sig";
        var forged = Jws(new { notificationType = "DID_RENEW", data = new { signedTransactionInfo = Jws(new { originalTransactionId = "999" }) } });
        (await anonymous.PostAsJsonAsync("/api/v1/billing/notifications/apple", new { signedPayload = forged })).StatusCode
            .Should().Be(HttpStatusCode.OK, "unknown purchases are acknowledged and ignored");
    }

    [Fact]
    public async Task Catalog_lists_products_and_limits_and_client_events_are_allow_listed()
    {
        var (owner, familyId) = await Family();
        var catalog = await owner.GetFromJsonAsync<JsonElement>("/api/v1/billing/catalog");
        catalog.GetProperty("products").EnumerateArray().Select(p => p.GetProperty("id").GetString())
            .Should().BeEquivalentTo("hally_plus_monthly", "hally_plus_yearly");
        catalog.GetProperty("trialDays").GetInt32().Should().Be(7);
        catalog.GetProperty("free").GetProperty("maxAdults").GetInt32().Should().Be(2);

        (await owner.PostAsJsonAsync("/api/v1/analytics/events", new { name = "anything_else" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync("/api/v1/analytics/events", new
        {
            name = "paywall_viewed",
            familyId,
            properties = new Dictionary<string, object> { ["trigger"] = "scan_limit", ["email"] = "a@b.c" },
        })).StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HallyDbContext>();
        var stored = await db.AnalyticsEvents.SingleAsync(e => e.FamilyId == familyId && e.Name == "paywall_viewed");
        stored.Properties.Should().Contain("scan_limit").And.NotContain("a@b.c");
    }
}

public class SubscriptionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromDays(3);

    [Fact]
    public void Failed_renewal_keeps_plus_during_grace_then_expires()
    {
        var sub = new Subscription(Guid.NewGuid());
        sub.ApplyStore(SubscriptionSource.AppStore, "hally_plus_monthly", "1000", Now.AddDays(-1), willRenew: true, Guid.NewGuid(), Now)
            .Should().Be(SubscriptionChange.None);
        sub.StateAt(Now, Grace).Should().Be(SubscriptionState.GracePeriod);
        sub.PlanAt(Now, Grace).Should().Be(Plan.Plus);
        sub.StateAt(Now.AddDays(3), Grace).Should().Be(SubscriptionState.Expired);
    }

    [Fact]
    public void Turning_off_auto_renew_is_a_cancellation_but_keeps_access_until_the_end()
    {
        var sub = new Subscription(Guid.NewGuid());
        sub.ApplyStore(SubscriptionSource.GooglePlay, "hally_plus_monthly", "tok", Now.AddDays(20), true, Guid.NewGuid(), Now).Should().Be(SubscriptionChange.Started);
        sub.ApplyStore(SubscriptionSource.GooglePlay, "hally_plus_monthly", "tok", Now.AddDays(20), false, null, Now).Should().Be(SubscriptionChange.Cancelled);
        sub.PlanAt(Now.AddDays(19), Grace).Should().Be(Plan.Plus);
        sub.StateAt(Now.AddDays(21), Grace).Should().Be(SubscriptionState.Expired, "no grace when the user cancelled");
        sub.ApplyStore(SubscriptionSource.GooglePlay, "hally_plus_monthly", "tok", Now.AddDays(50), true, null, Now).Should().Be(SubscriptionChange.Renewed);
    }

    [Fact]
    public void Google_response_is_parsed_and_unpaid_or_on_hold_purchases_give_no_access()
    {
        var products = new HashSet<string> { "hally_plus_monthly" };
        const string active = """
            {"subscriptionState":"SUBSCRIPTION_STATE_ACTIVE","acknowledgementState":"ACKNOWLEDGEMENT_STATE_PENDING",
             "lineItems":[{"productId":"hally_plus_monthly","expiryTime":"2026-12-13T09:00:00.123Z","autoRenewingPlan":{"autoRenewEnabled":true}}]}
            """;
        var p = GooglePlayVerifier.ParseSubscription(active, "tok", products, out var ack)!;
        p.ExpiresAt.Should().Be(new DateTimeOffset(2026, 12, 13, 9, 0, 0, 123, TimeSpan.Zero));
        p.WillRenew.Should().BeTrue();
        ack.Should().BeTrue();

        var onHold = active.Replace("SUBSCRIPTION_STATE_ACTIVE", "SUBSCRIPTION_STATE_ON_HOLD");
        GooglePlayVerifier.ParseSubscription(onHold, "tok", products, out _)!.ExpiresAt.Should().Be(DateTimeOffset.UnixEpoch);
        GooglePlayVerifier.ParseSubscription(active.Replace("SUBSCRIPTION_STATE_ACTIVE", "SUBSCRIPTION_STATE_PENDING"), "tok", products, out var pendingAck)
            .Should().BeNull();
        pendingAck.Should().BeFalse();
        GooglePlayVerifier.ParseSubscription(active, "tok", new HashSet<string> { "other" }, out _).Should().BeNull();
    }

    [Fact]
    public void Apple_status_response_is_parsed_and_refunds_give_no_access()
    {
        static string Jws(object payload) => $"e30.{Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.sig";
        var expires = new DateTimeOffset(2026, 12, 13, 9, 0, 0, TimeSpan.Zero);
        string Response(int status, bool autoRenew, bool revoked = false) => JsonSerializer.Serialize(new
        {
            bundleId = "app.hally",
            data = new[]
            {
                new
                {
                    lastTransactions = new[]
                    {
                        new
                        {
                            originalTransactionId = "2000",
                            status,
                            signedTransactionInfo = revoked
                                ? Jws(new { productId = "hally_plus_yearly", expiresDate = expires.ToUnixTimeMilliseconds(), revocationDate = 1 })
                                : Jws(new { productId = "hally_plus_yearly", expiresDate = expires.ToUnixTimeMilliseconds() }),
                            signedRenewalInfo = Jws(new { autoRenewStatus = autoRenew ? 1 : 0 }),
                        },
                    },
                },
            },
        });

        var products = new HashSet<string> { "hally_plus_yearly" };
        var p = AppStoreVerifier.ParseStatuses(Response(1, true), "app.hally", products)!;
        p.StoreReference.Should().Be("2000");
        p.ExpiresAt.Should().Be(expires);
        p.WillRenew.Should().BeTrue();
        AppStoreVerifier.ParseStatuses(Response(1, false), "app.hally", products)!.WillRenew.Should().BeFalse();
        AppStoreVerifier.ParseStatuses(Response(1, true, revoked: true), "app.hally", products).Should().BeNull();
        AppStoreVerifier.ParseStatuses(Response(1, true), "someone.else", products).Should().BeNull();
        Encoding.UTF8.GetString(Convert.FromBase64String("e30=")).Should().Be("{}");
    }
}
