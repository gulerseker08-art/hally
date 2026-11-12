using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Hally.Infrastructure.Ai;

namespace Hally.Api.Tests;

public record ChatMessageBody(Guid Id, string Role, string Content, List<Guid> ProposalIds, bool Fallback);
public record ProposalPayloadBody(string Title, string? Date, string? Time, List<string> NeedsInfo);
public record ChatProposalBody(Guid Id, string Kind, string Status, ProposalPayloadBody Payload);
public record AssistantReplyBody(Guid ConversationId, ChatMessageBody Reply, List<ChatProposalBody> Proposals);

/// <summary>Uses the shared fake chat model, so these tests run one at a time inside the collection.</summary>
[Collection(ApiCollection.Name)]
public class AssistantTests(HallyApiFactory api)
{
    private async Task<(HttpClient Client, Guid FamilyId)> Family()
    {
        var client = await api.SignInAsync($"{Guid.NewGuid():N}@example.com", "Ayşe");
        var family = (await (await client.PostIdempotentAsync("/api/v1/families", new { name = "Yılmaz", timeZoneId = "Europe/Istanbul" })).Content.ReadFromJsonAsync<FamilyBody>())!;
        return (client, family.Id);
    }

    private static Task<HttpResponseMessage> Say(HttpClient c, Guid familyId, string text, Guid? conversationId = null) =>
        c.PostIdempotentAsync($"/api/v1/families/{familyId}/assistant/messages", new { conversationId, text });

    private DateTimeOffset LocalTomorrowAt(int hour)
    {
        var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var tomorrow = TimeZoneInfo.ConvertTime(api.Clock.GetUtcNow(), istanbul).Date.AddDays(1);
        return new DateTimeOffset(tomorrow.AddHours(hour), istanbul.GetUtcOffset(tomorrow));
    }

    [Fact]
    public async Task Read_tools_only_see_the_callers_family_and_history_is_kept()
    {
        var (owner, familyId) = await Family();
        var (other, otherFamily) = await Family();
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks", new { title = "Kırmızı tişört", dueAt = LocalTomorrowAt(8), allDay = false });
        await other.PostIdempotentAsync($"/api/v1/families/{otherFamily}/tasks", new { title = "BAŞKA AİLENİN SIRRI", dueAt = LocalTomorrowAt(8), allDay = false });

        string? toolResult = null;
        api.Chat.Respond = messages =>
        {
            var tool = messages.LastOrDefault(m => m.Role == "tool");
            if (tool is null)
            {
                return new ChatCompletion(null, [new ChatToolCall("c1", "list_tasks", """{"state":"open"}""")], 100, 10);
            }

            toolResult = tool.Content;
            return new ChatCompletion("Yarın Kırmızı tişört lazım.", [], 150, 20);
        };
        try
        {
            var res = await Say(owner, familyId, "Yarın ne lazım?");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var reply = (await res.Content.ReadFromJsonAsync<AssistantReplyBody>())!;
            reply.Reply.Content.Should().Be("Yarın Kırmızı tişört lazım.");
            reply.Reply.Fallback.Should().BeFalse();
            toolResult.Should().Contain("Kırmızı tişört").And.NotContain("SIRRI");

            await Say(owner, familyId, "Teşekkürler", reply.ConversationId);
            api.Chat.Calls.Last().Select(m => m.Content).Should().Contain(["Yarın ne lazım?", "Yarın Kırmızı tişört lazım.", "Teşekkürler"]);

            var history = await owner.GetFromJsonAsync<List<ChatMessageBody>>($"/api/v1/families/{familyId}/assistant/conversations/{reply.ConversationId}/messages");
            history!.Select(m => m.Role).Should().Equal("User", "Assistant", "User", "Assistant");
            (await other.GetAsync($"/api/v1/families/{otherFamily}/assistant/conversations/{reply.ConversationId}/messages")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Say(other, otherFamily, "merhaba", reply.ConversationId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            api.Chat.Respond = _ => throw new AiUnavailableException("reset");
        }
    }

    [Fact]
    public async Task Suggestions_become_pending_proposals_with_server_resolved_dates()
    {
        var (owner, familyId) = await Family();
        api.Chat.Respond = messages => messages.Any(m => m.Role == "tool")
            ? new ChatCompletion("Ekledim, onayını bekliyor.", [], 0, 0)
            : new ChatCompletion(null,
            [
                // The model claims a wrong date; the server trusts the user's words ("yarın").
                new ChatToolCall("p1", "propose_event", """{"title":"Diş hekimi","dateText":"yarın saat 9'da","date":"2030-01-01","time":"09:00"}"""),
                new ChatToolCall("p2", "propose_event", """{"title":"Diş hekimi","dateText":"yarın saat 9'da"}"""),
            ], 0, 0);
        try
        {
            var reply = (await (await Say(owner, familyId, "Yarın saat 9'da diş hekimi randevusu ekle")).Content.ReadFromJsonAsync<AssistantReplyBody>())!;
            var tomorrow = LocalTomorrowAt(9);
            reply.Proposals.Should().ContainSingle("the same suggestion twice is one proposal");
            var p = reply.Proposals[0];
            (p.Kind, p.Status, p.Payload.Date, p.Payload.Time).Should().Be(("CreateEvent", "Pending", tomorrow.ToString("yyyy-MM-dd"), "09:00"));
            reply.Reply.ProposalIds.Should().Equal(p.Id);

            var pending = await owner.GetFromJsonAsync<List<ChatProposalBody>>($"/api/v1/families/{familyId}/proposals?status=pending");
            pending!.Should().Contain(x => x.Id == p.Id);
            (await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/proposals/{p.Id}/approve", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            api.Chat.Respond = _ => throw new AiUnavailableException("reset");
        }
    }

    [Fact]
    public async Task Without_ai_day_questions_are_answered_from_family_data()
    {
        var (owner, familyId) = await Family();
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/events", new { title = "Okul gezisi", startsAt = LocalTomorrowAt(9), allDay = false });
        await owner.PostIdempotentAsync($"/api/v1/families/{familyId}/tasks",
            new { title = "Beslenme çantası", notes = "Gerekenler: kırmızı tişört, şapka", dueAt = LocalTomorrowAt(7), allDay = false });

        var reply = (await (await Say(owner, familyId, "Yarın ne lazım?")).Content.ReadFromJsonAsync<AssistantReplyBody>())!;
        reply.Reply.Fallback.Should().BeTrue();
        reply.Reply.Content.Should().StartWith("Yarın:").And.Contain("Okul gezisi (09:00)").And.Contain("Beslenme çantası (07:00)")
            .And.Contain("Hazırlanacaklar: kırmızı tişört, şapka");

        var other = (await (await Say(owner, familyId, "Bir şiir yaz")).Content.ReadFromJsonAsync<AssistantReplyBody>())!;
        other.Reply.Content.Should().Contain("çevrimdışı");
    }
}
