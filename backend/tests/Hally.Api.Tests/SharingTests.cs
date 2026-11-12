using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hally.Api.Tests;

public record ShareCreatedBody(Guid Id, string Url, string ShareText, string WhatsAppUrl, DateTimeOffset ExpiresAt);
public record AdultShareBody(string FirstName, int Completed);
public record ChildCountBody(string FirstName, int Items);
public record WeeklyBody(string WeekStart, string WeekEnd, int TasksCompleted, int EventsCount, int AiActionsApproved, int HelperCompleted,
    List<AdultShareBody> Adults, List<ChildCountBody> Children);

[Collection(ApiCollection.Name)]
public class SharingTests(HallyApiFactory api)
{
    private HttpClient Anonymous() => api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(HttpClient Client, Guid FamilyId)> Family(string name = "Ayşe Yılmaz")
    {
        var client = await api.SignInAsync($"{Guid.NewGuid():N}@example.com", name);
        var family = (await (await client.PostIdempotentAsync("/api/v1/families", new { name = "Yılmaz", timeZoneId = "Europe/Istanbul" })).Content.ReadFromJsonAsync<FamilyBody>())!;
        return (client, family.Id);
    }

    private static async Task<TaskBody> Task(HttpClient c, Guid familyId, string title, DateTimeOffset? dueAt = null, Guid? childId = null) =>
        (await (await c.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks", new { title, dueAt, allDay = false, childId })).Content.ReadFromJsonAsync<TaskBody>())!;

    private static string PathOf(ShareCreatedBody link) => new Uri(link.Url).AbsolutePath;

    [Fact]
    public async Task Helper_can_view_and_complete_a_shared_task_without_an_account()
    {
        var (owner, familyId) = await Family();
        var task = await Task(owner, familyId, "<b>Elif'i</b> okuldan al", api.Clock.GetUtcNow().AddHours(5));
        var res = await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/share-links", new { kind = "Task", taskId = task.Id });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var link = (await res.Content.ReadFromJsonAsync<ShareCreatedBody>())!;
        link.ExpiresAt.Should().BeCloseTo(api.Clock.GetUtcNow().AddHours(72), TimeSpan.FromMinutes(1));
        link.ShareText.Should().StartWith("Ayşe seninle").And.Contain(link.Url);

        var helper = Anonymous();
        var page = await helper.GetAsync(PathOf(link));
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        page.Headers.GetValues("Referrer-Policy").Should().Contain("no-referrer");
        var html = await page.Content.ReadAsStringAsync();
        html.Should().Contain("&lt;b&gt;Elif&#x27;i&lt;/b&gt;").And.NotContain("<b>Elif").And.Contain("Tamamlandı olarak işaretle");
        html.Should().NotContain("Yılmaz", "the family name is not shown to helpers");

        var done = await helper.PostAsync($"{PathOf(link)}/tasks/{task.Id}/done", null);
        done.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await (await helper.GetAsync(PathOf(link))).Content.ReadAsStringAsync()).Should().Contain("✓ Tamamlandı");
        (await owner.GetFromJsonAsync<TaskBody>($"/api/v1/families/{familyId}/tasks/{task.Id}"))!.State.Should().Be("Done");

        var list = await owner.GetFromJsonAsync<List<JsonElement>>($"/api/v1/families/{familyId}/share-links");
        list!.Single().GetProperty("openCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Day_link_shows_only_that_day_and_cannot_complete_other_tasks()
    {
        var (owner, familyId) = await Family();
        var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var localTomorrow = TimeZoneInfo.ConvertTime(api.Clock.GetUtcNow(), istanbul).Date.AddDays(1);
        var at = new DateTimeOffset(localTomorrow.AddHours(10), istanbul.GetUtcOffset(localTomorrow));
        var inDay = await Task(owner, familyId, "Yarınki görev", at);
        var otherDay = await Task(owner, familyId, "Başka gün", at.AddDays(2));
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/events", new { title = "Yüzme dersi", startsAt = at.AddHours(5), allDay = false });

        var link = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/share-links",
            new { kind = "Day", date = localTomorrow.ToString("yyyy-MM-dd") })).Content.ReadFromJsonAsync<ShareCreatedBody>())!;

        var helper = Anonymous();
        var html = await (await helper.GetAsync(PathOf(link))).Content.ReadAsStringAsync();
        html.Should().Contain("Yarınki görev").And.Contain("Yüzme dersi").And.Contain("15:00").And.NotContain("Başka gün");
        (await helper.PostAsync($"{PathOf(link)}/tasks/{otherDay.Id}/done", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetFromJsonAsync<TaskBody>($"/api/v1/families/{familyId}/tasks/{otherDay.Id}"))!.State.Should().Be("Open");
        inDay.State.Should().Be("Open");
    }

    [Fact]
    public async Task Links_can_be_revoked_expire_and_are_scoped_to_the_family()
    {
        var (owner, familyId) = await Family();
        var (stranger, strangerFamily) = await Family();
        var task = await Task(owner, familyId, "Gizli");

        (await stranger.PostIdempotentAsync($"/api/v1/families/{strangerFamily}/share-links", new { kind = "Task", taskId = task.Id }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/share-links", new { kind = "Task", taskId = task.Id, expiresInHours = 500 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var revoked = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/share-links", new { kind = "Task", taskId = task.Id }))
            .Content.ReadFromJsonAsync<ShareCreatedBody>())!;
        (await stranger.DeleteAsync($"/api/v1/families/{familyId}/share-links/{revoked.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.DeleteAsync($"/api/v1/families/{familyId}/share-links/{revoked.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var gone = await Anonymous().GetAsync(PathOf(revoked));
        gone.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await Anonymous().PostAsync($"{PathOf(revoked)}/tasks/{task.Id}/done", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var shortLived = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/share-links", new { kind = "Task", taskId = task.Id, expiresInHours = 1 }))
            .Content.ReadFromJsonAsync<ShareCreatedBody>())!;
        api.Clock.Advance(TimeSpan.FromHours(2));
        try
        {
            (await Anonymous().GetAsync(PathOf(shortLived))).StatusCode.Should().Be(HttpStatusCode.Gone);
        }
        finally
        {
            api.Clock.Advance(TimeSpan.FromHours(-2));
        }

        (await Anonymous().GetAsync("/s/not-a-real-token")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Weekly_summary_counts_the_week_and_hides_child_names_by_default()
    {
        var (owner, familyId) = await Family();
        var child = await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/children", new { firstName = "Elif" })).Content.ReadFromJsonAsync<JsonElement>();
        var childId = child.GetProperty("id").GetGuid();
        var a = await Task(owner, familyId, "Ödev", childId: childId);
        var b = await Task(owner, familyId, "Market");
        await Task(owner, familyId, "Açık kalan");
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks/{a.Id}/complete", null);
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks/{b.Id}/complete", null);
        var link = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/share-links", new { kind = "Task", taskId = (await Task(owner, familyId, "Yardımcı işi")).Id }))
            .Content.ReadFromJsonAsync<ShareCreatedBody>())!;
        var helperTask = (await owner.GetFromJsonAsync<List<TaskBody>>($"/api/v1/families/{familyId}/tasks?state=Open"))!.Single(t => t.Title == "Yardımcı işi");
        await Anonymous().PostAsync($"{PathOf(link)}/tasks/{helperTask.Id}/done", null);

        var summary = (await owner.GetFromJsonAsync<WeeklyBody>($"/api/v1/families/{familyId}/weekly-summary"))!;
        summary.TasksCompleted.Should().Be(3);
        summary.HelperCompleted.Should().Be(1);
        summary.Adults.Should().ContainSingle().Which.Should().Be(new AdultShareBody("Ayşe", 2));
        summary.Children.Should().BeEmpty();

        var withKids = (await owner.GetFromJsonAsync<WeeklyBody>($"/api/v1/families/{familyId}/weekly-summary?includeChildNames=true"))!;
        withKids.Children.Should().ContainSingle().Which.Should().Be(new ChildCountBody("Elif", 1));

        var lastWeek = (await owner.GetFromJsonAsync<WeeklyBody>($"/api/v1/families/{familyId}/weekly-summary?offset=-1"))!;
        lastWeek.TasksCompleted.Should().Be(0);

        var card = await owner.GetAsync($"/api/v1/families/{familyId}/weekly-summary/card.png");
        card.StatusCode.Should().Be(HttpStatusCode.OK);
        card.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var bytes = await card.Content.ReadAsByteArrayAsync();
        bytes.Take(4).Should().Equal(0x89, 0x50, 0x4E, 0x47);
        bytes.Length.Should().BeGreaterThan(10_000);
        if (Environment.GetEnvironmentVariable("HALLY_CARD_OUT") is { Length: > 0 } outPath)
        {
            await File.WriteAllBytesAsync(outPath, bytes);
        }
    }
}
