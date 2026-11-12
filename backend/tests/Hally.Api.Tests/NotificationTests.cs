using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Hally.Api.Features.Notifications;
using Hally.Domain.Capture;
using Hally.Infrastructure.Persistence;
using Hally.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hally.Api.Tests;

public record ReminderBody(Guid Id, string Target, Guid TargetId, Guid? RecipientUserId, DateTimeOffset FireAt, string Status);
public record NotificationBody(Guid Id, Guid FamilyId, string Kind, string Title, string Body, string? TargetKind, Guid? TargetId, bool Read);
public record InboxBody(int Unread, List<NotificationBody> Items);

[Collection(ApiCollection.Name)]
public class NotificationTests(HallyApiFactory api)
{
    private async Task<(HttpClient Owner, HttpClient Partner, Guid FamilyId)> TwoAdults(string tz = "Europe/Istanbul")
    {
        var owner = await api.SignInAsync($"{Guid.NewGuid():N}@example.com", "Ayşe");
        var family = (await (await owner.PostIdempotentAsync("/api/v1/families", new { name = "Yılmaz", timeZoneId = tz })).Content.ReadFromJsonAsync<FamilyBody>())!;
        var invite = (await (await owner.PostIdempotentAsync($"/api/v1/families/{family.Id}/invitations", null)).Content.ReadFromJsonAsync<InvitationBody>())!;
        var partner = await api.SignInAsync($"{Guid.NewGuid():N}@example.com", "Mehmet", "en");
        await partner.PostIdempotentAsync($"/api/v1/invitations/{invite.Token}/accept", null);
        return (owner, partner, family.Id);
    }

    private async Task<T> Run<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = api.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private Task<int> Dispatch() => Run(sp => sp.GetRequiredService<ReminderDispatcher>().RunAsync(default));

    private async Task WithClockAdvanced(TimeSpan by, Func<Task> action)
    {
        api.Clock.Advance(by);
        try
        {
            await action();
        }
        finally
        {
            api.Clock.Advance(-by);
        }
    }

    private static async Task<TaskBody> CreateTask(HttpClient client, Guid familyId, string title, DateTimeOffset? dueAt) =>
        (await (await client.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks", new { title, dueAt, allDay = false })).Content.ReadFromJsonAsync<TaskBody>())!;

    [Fact]
    public async Task Reminder_for_everyone_reaches_both_adults_once_in_their_language()
    {
        var (owner, partner, familyId) = await TwoAdults();
        await partner.PutAsJsonAsync("/api/v1/me/devices", new { platform = "android", token = $"tok-{Guid.NewGuid():N}" });
        var task = await CreateTask(owner, familyId, "Veli toplantısı", api.Clock.GetUtcNow().AddHours(3));
        var res = await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/reminders",
            new { target = "Task", targetId = task.Id, fireAt = api.Clock.GetUtcNow().AddHours(2), forEveryone = true });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var reminder = (await res.Content.ReadFromJsonAsync<ReminderBody>())!;

        await WithClockAdvanced(TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(1)), async () =>
        {
            await Dispatch();
            await Dispatch(); // a rerun must not duplicate
        });

        var ownerInbox = (await owner.GetFromJsonAsync<InboxBody>("/api/v1/me/notifications"))!;
        ownerInbox.Items.Should().ContainSingle(n => n.TargetId == task.Id).Which.Body.Should().StartWith("Bugün").And.MatchRegex(@"\d\d:\d\d");
        ownerInbox.Unread.Should().Be(1);
        var partnerInbox = (await partner.GetFromJsonAsync<InboxBody>("/api/v1/me/notifications"))!;
        partnerInbox.Items.Should().ContainSingle(n => n.TargetId == task.Id).Which.Body.Should().StartWith("Today");
        api.Push.Sent.Should().Contain(p => p.Message.Data["targetId"] == task.Id.ToString());

        var list = (await owner.GetFromJsonAsync<List<ReminderBody>>($"/api/v1/families/{familyId}/reminders?targetId={task.Id}"))!;
        list.Should().NotContain(r => r.Id == reminder.Id, "sent reminders are no longer scheduled");

        (await owner.PostIdempotentAsync("/api/v1/me/notifications/read-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetFromJsonAsync<InboxBody>("/api/v1/me/notifications"))!.Unread.Should().Be(0);
    }

    [Fact]
    public async Task Personal_reminder_only_reaches_its_creator_and_done_tasks_are_skipped()
    {
        var (owner, partner, familyId) = await TwoAdults();
        var open = await CreateTask(owner, familyId, "İlaç", api.Clock.GetUtcNow().AddHours(2));
        var done = await CreateTask(owner, familyId, "Fatura", api.Clock.GetUtcNow().AddHours(2));
        var at = api.Clock.GetUtcNow().AddMinutes(30);
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/reminders", new { target = "Task", targetId = open.Id, fireAt = at, forEveryone = false });
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/reminders", new { target = "Task", targetId = done.Id, fireAt = at, forEveryone = true });
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks/{done.Id}/complete", null);

        await WithClockAdvanced(TimeSpan.FromMinutes(31), Dispatch);

        var ownerInbox = (await owner.GetFromJsonAsync<InboxBody>("/api/v1/me/notifications"))!;
        ownerInbox.Items.Select(n => n.TargetId).Should().Contain(open.Id).And.NotContain(done.Id);
        var partnerInbox = (await partner.GetFromJsonAsync<InboxBody>("/api/v1/me/notifications"))!;
        partnerInbox.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Reminders_are_validated_and_scoped_to_the_family()
    {
        var (owner, _, familyId) = await TwoAdults();
        var (other, _, otherFamily) = await TwoAdults();
        var foreign = await CreateTask(other, otherFamily, "Başkasının", null);
        var mine = await CreateTask(owner, familyId, "Benim", null);

        (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/reminders",
            new { target = "Task", targetId = foreign.Id, fireAt = api.Clock.GetUtcNow().AddHours(1), forEveryone = false }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/reminders",
            new { target = "Task", targetId = mine.Id, fireAt = api.Clock.GetUtcNow().AddHours(-1), forEveryone = false }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var created = (await (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/reminders",
            new { target = "Task", targetId = mine.Id, fireAt = api.Clock.GetUtcNow().AddHours(1), forEveryone = false })).Content.ReadFromJsonAsync<ReminderBody>())!;
        (await owner.DeleteAsync($"/api/v1/families/{familyId}/reminders/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await other.DeleteAsync($"/api/v1/families/{familyId}/reminders/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.GetFromJsonAsync<List<ReminderBody>>($"/api/v1/families/{familyId}/reminders"))!.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_push_tokens_are_removed()
    {
        var (owner, _, familyId) = await TwoAdults();
        var dead = $"dead-{Guid.NewGuid():N}";
        await owner.PutAsJsonAsync("/api/v1/me/devices", new { platform = "ios", token = dead });
        var task = await CreateTask(owner, familyId, "Kurs", null);
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/reminders", new { target = "Task", targetId = task.Id, fireAt = api.Clock.GetUtcNow().AddMinutes(5), forEveryone = false });

        await WithClockAdvanced(TimeSpan.FromMinutes(6), Dispatch);

        api.Push.Sent.Should().Contain(p => p.Token == dead);
        (await Run(sp => sp.GetRequiredService<HallyDbContext>().Devices.AnyAsync(d => d.Token == dead))).Should().BeFalse();
    }

    [Fact]
    public async Task Evening_prep_summarises_tomorrow_once_and_respects_the_setting()
    {
        // Pick a fixed-offset zone where it is 20:xx right now, so the default prep hour matches.
        var utcHour = api.Clock.GetUtcNow().Hour;
        var offset = 20 - utcHour;
        if (offset < -12) offset += 24;
        if (offset > 14) offset -= 24;
        var tz = offset == 0 ? "UTC" : $"Etc/GMT{(offset > 0 ? "-" : "+")}{Math.Abs(offset)}";

        var (owner, partner, familyId) = await TwoAdults(tz);
        (await partner.PutAsJsonAsync("/api/v1/me/notification-settings", new { eveningPrepEnabled = false, eveningPrepHour = 20 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(tz);
        var localTomorrow = TimeZoneInfo.ConvertTime(api.Clock.GetUtcNow(), zone).Date.AddDays(1);
        var swimAt = new DateTimeOffset(localTomorrow.AddHours(9), zone.GetUtcOffset(localTomorrow));
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/events", new { title = "Yüzme", startsAt = swimAt, allDay = false });
        await CreateTask(owner, familyId, "Kırmızı tişört", swimAt.AddHours(-1));

        await Run(sp => sp.GetRequiredService<EveningPrepJob>().RunAsync(default));
        await Run(sp => sp.GetRequiredService<EveningPrepJob>().RunAsync(default));

        var inbox = (await owner.GetFromJsonAsync<InboxBody>("/api/v1/me/notifications"))!;
        var prep = inbox.Items.Should().ContainSingle(n => n.Kind == "EveningPrep").Subject;
        prep.Title.Should().Be("Yarın için hazırlık");
        prep.Body.Should().Contain("1 etkinlik").And.Contain("1 görev").And.Contain("Yüzme 09:00");
        (await partner.GetFromJsonAsync<InboxBody>("/api/v1/me/notifications"))!.Items.Should().BeEmpty();

        (await partner.PutAsJsonAsync("/api/v1/me/notification-settings", new { eveningPrepEnabled = true, eveningPrepHour = 12 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Retention_deletes_old_originals_but_keeps_the_capture()
    {
        var (owner, _, familyId) = await TwoAdults();
        var (key, captureId) = await Run(async sp =>
        {
            var store = sp.GetRequiredService<IDocumentStore>();
            var db = sp.GetRequiredService<HallyDbContext>();
            var k = await store.SaveAsync(familyId, [1, 2, 3], default);
            var me = await owner.GetFromJsonAsync<MeBody>("/api/v1/me");
            var capture = new Capture(familyId, me!.Id, CaptureSource.Camera, null, k, "image/jpeg", api.Clock.GetUtcNow().AddDays(-31));
            db.Captures.Add(capture);
            await db.SaveChangesAsync();
            return (k, capture.Id);
        });

        await Run(sp => sp.GetRequiredService<RetentionJob>().RunAsync(default));

        await Run(async sp =>
        {
            (await sp.GetRequiredService<IDocumentStore>().ReadAsync(key, default)).Should().BeNull();
            var capture = await sp.GetRequiredService<HallyDbContext>().Captures.AsNoTracking().FirstAsync(c => c.Id == captureId);
            capture.StorageKey.Should().BeNull();
            return 0;
        });
    }
}
