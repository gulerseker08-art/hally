using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hally.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hally.Api.Tests;

[Collection(ApiCollection.Name)]
public class PrivacyTests(HallyApiFactory api)
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static string Email() => $"{Guid.NewGuid():N}@example.com";

    private static async Task<Guid> CreateFamily(HttpClient c, string name = "Yılmaz") =>
        (await (await c.PostIdempotentAsync("/api/v1/families", new { name, timeZoneId = "Europe/Istanbul" })).Content.ReadFromJsonAsync<FamilyBody>())!.Id;

    private async Task<(HttpClient Owner, HttpClient Partner, Guid FamilyId, Guid PartnerId)> Couple()
    {
        var owner = await api.SignInAsync(Email(), "Ayşe Yılmaz");
        var familyId = await CreateFamily(owner);
        var invite = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/invitations", null)).Content.ReadFromJsonAsync<InvitationBody>())!;
        var partner = await api.SignInAsync(Email(), "Mehmet Yılmaz");
        await partner.PostIdempotentAsync($"/api/v1/invitations/{invite.Token}/accept", null);
        var me = await partner.GetFromJsonAsync<MeBody>("/api/v1/me");
        return (owner, partner, familyId, me!.Id);
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

    [Fact]
    public async Task Deleting_the_only_adult_deletes_the_family_its_data_and_stored_photos()
    {
        var email = Email();
        var owner = await api.SignInAsync(email, "Ayşe Yılmaz");
        var familyId = await CreateFamily(owner);
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks", new { title = "Kırtasiye", allDay = false });
        (await Scan(owner, familyId)).StatusCode.Should().Be(HttpStatusCode.Created);
        var filesBefore = Directory.Exists(api.StorageRoot) ? Directory.GetFiles(api.StorageRoot, "*", SearchOption.AllDirectories).Length : 0;
        filesBefore.Should().BePositive();

        (await owner.DeleteAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "confirmation is required");
        (await owner.DeleteAsync("/api/v1/me?confirm=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HallyDbContext>();
        (await db.Users.AnyAsync(u => u.Email == email)).Should().BeFalse();
        (await db.Families.AnyAsync(f => f.Id == familyId)).Should().BeFalse();
        (await db.Tasks.AnyAsync(t => t.FamilyId == familyId)).Should().BeFalse();
        (await db.Captures.AnyAsync(c => c.FamilyId == familyId)).Should().BeFalse();
        Directory.GetFiles(api.StorageRoot, "*", SearchOption.AllDirectories).Length.Should().Be(filesBefore - 1);

        (await owner.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the access token's user no longer exists");

        var again = await api.SignInAsync(email, "Ayşe Yılmaz");
        (await again.GetFromJsonAsync<MeBody>("/api/v1/me"))!.Families.Should().BeEmpty("signing in again starts a fresh account");
    }

    [Fact]
    public async Task Deleting_one_adult_keeps_the_shared_family_and_hands_over_ownership()
    {
        var (owner, partner, familyId, _) = await Couple();
        var task = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks",
            new { title = "Elif'i okuldan al", allDay = false })).Content.ReadFromJsonAsync<TaskBody>())!;
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/share-links", new { kind = "Task", taskId = task.Id });
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/invitations", null);

        (await owner.DeleteAsync("/api/v1/me?confirm=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var family = await partner.GetFromJsonAsync<FamilyBody>($"/api/v1/families/{familyId}");
        family!.Members.Should().ContainSingle();
        (await partner.GetFromJsonAsync<JsonElement>($"/api/v1/families/{familyId}")).GetProperty("members")[0].GetProperty("role").GetString()
            .Should().Be("Owner");
        (await partner.GetAsync($"/api/v1/families/{familyId}/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.OK, "family data stays");
        (await partner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/families/{familyId}/share-links"))!.Should().BeEmpty("the deleted adult's links are turned off");

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HallyDbContext>();
        (await db.Invitations.Where(i => i.FamilyId == familyId && i.AcceptedAt == null).AllAsync(i => i.RevokedAt != null)).Should().BeTrue();
    }

    [Fact]
    public async Task Leaving_a_family_removes_access_and_unassigns_open_tasks()
    {
        var (owner, partner, familyId, partnerId) = await Couple();
        var task = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks",
            new { title = "Veli toplantısı", allDay = false, assigneeUserId = partnerId })).Content.ReadFromJsonAsync<TaskBody>())!;

        (await partner.PostIdempotentAsync($"/api/v1/families/{familyId}/leave", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await partner.GetAsync($"/api/v1/families/{familyId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.GetFromJsonAsync<TaskBody>($"/api/v1/families/{familyId}/tasks/{task.Id}"))!.AssigneeUserId.Should().BeNull();
    }

    [Fact]
    public async Task Export_contains_the_users_data_and_nothing_from_other_families()
    {
        var (owner, _, familyId, _) = await Couple();
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks", new { title = "Kırtasiye alışverişi", allDay = false });
        var stranger = await api.SignInAsync(Email(), "Yabancı");
        var other = await CreateFamily(stranger, "Başka");
        await stranger.PostIdempotentAsync($"/api/v1/families/{other}/tasks", new { title = "Gizli görev", allDay = false });

        var res = await owner.GetAsync("/api/v1/me/export");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentDisposition!.FileName.Should().StartWith("hally-export-");
        var json = await res.Content.ReadAsStringAsync();
        json.Should().Contain("Kırtasiye alışverişi").And.Contain("Mehmet Yılmaz").And.Contain("\"displayName\": \"Ayşe Yılmaz\"");
        json.Should().NotContain("Gizli görev").And.NotContain("Başka");
    }

    [Fact]
    public async Task Privacy_info_does_not_claim_no_training_unless_configured()
    {
        var info = await api.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/privacy");
        info.GetProperty("aiProviderNoTraining").GetBoolean().Should().BeFalse();
        info.GetProperty("originalsRetentionDays").GetInt32().Should().Be(30);
    }
}
