using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Hally.Api.Tests;

public record MemberBody(Guid UserId, string DisplayName, string Role);
public record FamilyBody(Guid Id, string Name, string TimeZoneId, List<MemberBody> Members);
public record InvitationBody(Guid Id, string Token, string Url, string ShareText, string WhatsAppUrl, DateTimeOffset ExpiresAt);
public record MeBody(Guid Id, string Email, string DisplayName, string Locale, List<FamilySummaryBody> Families);
public record FamilySummaryBody(Guid Id, string Name, string Role);

[Collection(ApiCollection.Name)]
public class FamilyFlowTests(HallyApiFactory api)
{
    private static string Email() => $"{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Sprint1_demo_two_adults_sign_in_and_join_the_same_family()
    {
        var ayse = await api.SignInAsync(Email(), "Ayşe");
        var created = await ayse.PostIdempotentAsync("/api/v1/families", new { name = "Yılmaz Ailesi", timeZoneId = "Europe/Istanbul" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var family = (await created.Content.ReadFromJsonAsync<FamilyBody>())!;

        var inviteRes = await ayse.PostIdempotentAsync($"/api/v1/families/{family.Id}/invitations", null);
        inviteRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var invite = (await inviteRes.Content.ReadFromJsonAsync<InvitationBody>())!;
        invite.Url.Should().StartWith("https://hally.test/i/");
        invite.ShareText.Should().Contain("Ayşe").And.Contain(invite.Url);
        invite.WhatsAppUrl.Should().StartWith("https://wa.me/?text=");

        var preview = await api.CreateClient().GetFromJsonAsync<Dictionary<string, object>>($"/api/v1/invitations/{invite.Token}");
        preview!["familyName"].ToString().Should().Be("Yılmaz Ailesi");
        preview["status"].ToString().Should().Be("pending");

        var mehmet = await api.SignInAsync(Email(), "Mehmet");
        var accept = await mehmet.PostIdempotentAsync($"/api/v1/invitations/{invite.Token}/accept", null);
        accept.StatusCode.Should().Be(HttpStatusCode.OK);

        var members = await mehmet.GetFromJsonAsync<List<MemberBody>>($"/api/v1/families/{family.Id}/members");
        members!.Select(m => (m.DisplayName, m.Role)).Should().Equal(("Ayşe", "Owner"), ("Mehmet", "Adult"));

        var me = await mehmet.GetFromJsonAsync<MeBody>("/api/v1/me");
        me!.Families.Should().ContainSingle(f => f.Id == family.Id && f.Role == "Adult");
    }

    [Fact]
    public async Task Family_data_is_isolated_from_non_members()
    {
        var owner = await api.SignInAsync(Email(), "Owner");
        var family = (await (await owner.PostIdempotentAsync("/api/v1/families", new { name = "A" })).Content.ReadFromJsonAsync<FamilyBody>())!;

        var stranger = await api.SignInAsync(Email(), "Stranger");
        (await stranger.GetAsync($"/api/v1/families/{family.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.GetAsync($"/api/v1/families/{family.Id}/members")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.PostIdempotentAsync($"/api/v1/families/{family.Id}/invitations", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.PostIdempotentAsync($"/api/v1/families/{family.Id}/children", new { firstName = "Elif" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var client = api.CreateClient();
        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostIdempotentAsync("/api/v1/families", new { name = "X" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Same_idempotency_key_creates_one_family()
    {
        var user = await api.SignInAsync(Email(), "Idem");
        var key = Guid.NewGuid().ToString();
        var first = await user.PostIdempotentAsync("/api/v1/families", new { name = "Tek Aile" }, key);
        var second = await user.PostIdempotentAsync("/api/v1/families", new { name = "Tek Aile" }, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.GetValues("Idempotent-Replayed").Should().Contain("true");
        (await second.Content.ReadFromJsonAsync<FamilyBody>())!.Id
            .Should().Be((await first.Content.ReadFromJsonAsync<FamilyBody>())!.Id);

        var me = await user.GetFromJsonAsync<MeBody>("/api/v1/me");
        me!.Families.Should().ContainSingle();

        var reused = await user.PostIdempotentAsync("/api/v1/families", new { name = "Başka" }, key);
        reused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Missing_idempotency_key_is_rejected()
    {
        var user = await api.SignInAsync(Email(), "NoKey");
        var res = await user.PostAsJsonAsync("/api/v1/families", new { name = "Aile" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("GMT+3")]
    [InlineData("")]
    public async Task Invalid_time_zone_returns_validation_problem(string tz)
    {
        var user = await api.SignInAsync(Email(), "Tz");
        var res = await user.PostIdempotentAsync("/api/v1/families", new { name = "Aile", timeZoneId = tz });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().Contain("TimeZoneId");
    }

    [Fact]
    public async Task Expired_invitation_cannot_be_accepted()
    {
        var owner = await api.SignInAsync(Email(), "Owner");
        var family = (await (await owner.PostIdempotentAsync("/api/v1/families", new { name = "B" })).Content.ReadFromJsonAsync<FamilyBody>())!;
        var invite = (await (await owner.PostIdempotentAsync($"/api/v1/families/{family.Id}/invitations", null)).Content.ReadFromJsonAsync<InvitationBody>())!;

        var late = await api.SignInAsync(Email(), "Late");
        api.Clock.Advance(TimeSpan.FromDays(8));
        try
        {
            var res = await late.PostIdempotentAsync($"/api/v1/invitations/{invite.Token}/accept", null);
            res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await res.Content.ReadAsStringAsync()).Should().Contain("invitation.expired");
        }
        finally
        {
            api.Clock.Advance(TimeSpan.FromDays(-8));
        }
    }

    [Fact]
    public async Task Accepted_invitation_cannot_be_reused_by_a_third_adult()
    {
        var owner = await api.SignInAsync(Email(), "Owner");
        var family = (await (await owner.PostIdempotentAsync("/api/v1/families", new { name = "C" })).Content.ReadFromJsonAsync<FamilyBody>())!;
        var invite = (await (await owner.PostIdempotentAsync($"/api/v1/families/{family.Id}/invitations", null)).Content.ReadFromJsonAsync<InvitationBody>())!;

        var partner = await api.SignInAsync(Email(), "Partner");
        (await partner.PostIdempotentAsync($"/api/v1/invitations/{invite.Token}/accept", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var third = await api.SignInAsync(Email(), "Third");
        var res = await third.PostIdempotentAsync($"/api/v1/invitations/{invite.Token}/accept", null);
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res.Content.ReadAsStringAsync()).Should().Contain("invitation.accepted");
    }

    [Fact]
    public async Task Unknown_invitation_returns_404()
    {
        var res = await api.CreateClient().GetAsync("/api/v1/invitations/not-a-real-token");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Child_is_added_with_first_name_only()
    {
        var owner = await api.SignInAsync(Email(), "Owner");
        var family = (await (await owner.PostIdempotentAsync("/api/v1/families", new { name = "D" })).Content.ReadFromJsonAsync<FamilyBody>())!;
        var res = await owner.PostIdempotentAsync($"/api/v1/families/{family.Id}/children", new { firstName = "Elif", birthYear = 2018 });
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var full = await owner.GetFromJsonAsync<Dictionary<string, object>>($"/api/v1/families/{family.Id}");
        full!["children"].ToString().Should().Contain("Elif");
    }
}
