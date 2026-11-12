using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Hally.Api.Tests;

[Collection(ApiCollection.Name)]
public class PlanningTests(HallyApiFactory api)
{
    private async Task<(HttpClient Client, Guid FamilyId)> Family(string tz = "Europe/Istanbul")
    {
        var client = await api.SignInAsync($"{Guid.NewGuid():N}@example.com", "Ayşe");
        var family = (await (await client.PostIdempotentAsync("/api/v1/families", new { name = "Aile", timeZoneId = tz })).Content.ReadFromJsonAsync<FamilyBody>())!;
        return (client, family.Id);
    }

    [Fact]
    public async Task Task_lifecycle_complete_and_reopen()
    {
        var (client, familyId) = await Family();
        var created = (await (await client.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks",
            new { title = "Ekmek al", dueAt = DateTimeOffset.UtcNow.AddHours(-1), allDay = false })).Content.ReadFromJsonAsync<TaskBody>())!;

        var url = $"/api/v1/families/{familyId}/tasks/{created.Id}";
        (await client.GetFromJsonAsync<Dictionary<string, object>>(url))!["isOverdue"].ToString().Should().Be("True");
        (await (await client.PostIdempotentAsync($"{url}/complete", null)).Content.ReadFromJsonAsync<TaskBody>())!.State.Should().Be("Done");
        (await (await client.PostIdempotentAsync($"{url}/reopen", null)).Content.ReadFromJsonAsync<TaskBody>())!.State.Should().Be("Open");
    }

    [Fact]
    public async Task Tasks_of_another_family_are_not_reachable_by_id()
    {
        var (a, familyA) = await Family();
        var task = (await (await a.PostIdempotentAsync($"/api/v1/families/{familyA}/tasks", new { title = "Gizli", allDay = false })).Content.ReadFromJsonAsync<TaskBody>())!;

        var (b, familyB) = await Family();
        (await b.GetAsync($"/api/v1/families/{familyB}/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.PostIdempotentAsync($"/api/v1/families/{familyB}/tasks/{task.Id}/complete", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.GetAsync($"/api/v1/families/{familyA}/tasks")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Overlapping_events_are_reported_as_conflicts()
    {
        var (client, familyId) = await Family();
        var start = new DateTimeOffset(2026, 12, 1, 15, 0, 0, TimeSpan.Zero);
        await client.PostIdempotentAsync($"/api/v1/families/{familyId}/events", new { title = "Yüzme", startsAt = start, endsAt = start.AddHours(1), allDay = false });
        var second = (await (await client.PostIdempotentAsync($"/api/v1/families/{familyId}/events",
            new { title = "Diş hekimi", startsAt = start.AddMinutes(30), allDay = false })).Content.ReadFromJsonAsync<SavedEventBody>())!;
        second.Conflicts.Should().ContainSingle(c => c.Title == "Yüzme");

        var third = (await (await client.PostIdempotentAsync($"/api/v1/families/{familyId}/events",
            new { title = "Piyano", startsAt = start.AddHours(2), allDay = false })).Content.ReadFromJsonAsync<SavedEventBody>())!;
        third.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task Today_uses_the_family_time_zone()
    {
        var (client, familyId) = await Family();
        var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var now = api.Clock.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(now, istanbul);
        var localMidnight = new DateTimeOffset(localNow.Date, istanbul.GetUtcOffset(localNow.Date));

        await client.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks", new { title = "Bugünkü", dueAt = localMidnight, allDay = true });
        await client.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks", new { title = "Dünkü", dueAt = localMidnight.AddDays(-1), allDay = true });
        await client.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks", new { title = "Yarınki", dueAt = localMidnight.AddDays(1), allDay = true });

        var today = (await client.GetFromJsonAsync<TodayBody>($"/api/v1/families/{familyId}/today"))!;
        today.Date.Should().Be(localNow.ToString("yyyy-MM-dd"));
        today.DueToday.Select(t => t.Title).Should().Equal("Bugünkü");
        today.Overdue.Select(t => t.Title).Should().Equal("Dünkü");
    }

    [Fact]
    public async Task Invalid_event_range_is_rejected()
    {
        var (client, familyId) = await Family();
        (await client.GetAsync($"/api/v1/families/{familyId}/events?from=2026-12-01T00:00:00Z&to=2026-11-01T00:00:00Z"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
