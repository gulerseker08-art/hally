using System.Text.Json;
using FluentAssertions;
using Hally.Api.Features.Captures;
using Hally.Domain.Capture;

namespace Hally.Api.Tests;

/// <summary>AI evaluation: whatever the model returns, the server only keeps grounded, family-scoped data.</summary>
public class ExtractionEvaluationTests
{
    private static readonly Guid Elif = Guid.NewGuid();
    private static readonly Guid Can = Guid.NewGuid();
    private static readonly FamilyContext Family = new("Europe/Istanbul", new DateOnly(2026, 11, 7), "tr", [(Elif, "Elif"), (Can, "Can")]);

    private static string Item(string title, string? dateText = null, string? date = null, string? time = null, string? child = null,
        string kind = "task", string? quote = null, params string[] materials) =>
        JsonSerializer.Serialize(new
        {
            kind,
            title,
            dateText,
            date,
            time,
            childName = child,
            materials,
            notes = (string?)null,
            location = (string?)null,
            confidence = "high",
            sourceQuote = quote,
        });

    private static ExtractionResult Run(params string[] items)
    {
        using var doc = JsonDocument.Parse($"{{\"summary\":\"s\",\"items\":[{string.Join(',', items)}]}}");
        return ExtractionService.Validate(doc.RootElement, Family);
    }

    [Fact]
    public void Reference_scenario_red_shirt_and_leaves()
    {
        var r = Run(
            Item("Kırmızı tişört giydir", "cuma", "2026-11-13", child: "Elif", quote: "Cuma günü kırmızı tişört"),
            Item("10 yaprak topla", "pazartesi", "2026-11-09", child: "elif", quote: "pazartesi 10 yaprak", materials: "10 yaprak"));

        r.Drafts.Should().HaveCount(2);
        r.Drafts[0].Payload.Date.Should().Be("2026-11-13");
        r.Drafts[0].Payload.ChildId.Should().Be(Elif);
        r.Drafts[1].Payload.Date.Should().Be("2026-11-09");
        r.Drafts[1].Payload.Notes.Should().Contain("Gerekenler: 10 yaprak");
        r.Drafts.Should().OnlyContain(d => d.Payload.AssigneeUserId == null);
    }

    [Fact]
    public void Date_without_a_source_phrase_is_treated_as_invented()
    {
        var d = Run(Item("Veli toplantısı", dateText: null, date: "2026-11-10", kind: "event")).Drafts.Single();
        d.Payload.Date.Should().BeNull();
        d.Payload.NeedsInfo.Should().Contain("date");
        d.Payload.Confidence.Should().NotBe("high");
    }

    [Fact]
    public void Server_resolution_wins_over_a_shifted_model_date()
    {
        var d = Run(Item("Gezi parası", "haftaya cuma", "2026-11-20")).Drafts.Single();
        d.Payload.Date.Should().Be("2026-11-13");
    }

    [Fact]
    public void Model_date_is_accepted_only_with_a_phrase_and_in_a_sane_range()
    {
        Run(Item("Kostüm", "gösteri günü", "2026-11-25")).Drafts.Single().Payload.Date.Should().Be("2026-11-25");
        Run(Item("Kostüm", "gösteri günü", "2019-01-01")).Drafts.Single().Payload.Date.Should().BeNull();
    }

    [Fact]
    public void Time_is_kept_only_when_cited_in_the_source()
    {
        Run(Item("Toplantı", "yarın", "2026-11-08", "18:00", quote: "yarın toplantı var")).Drafts.Single().Payload.Time.Should().BeNull();
        Run(Item("Toplantı", "yarın", "2026-11-08", "18:00", quote: "yarın 18.00'de toplantı")).Drafts.Single().Payload.Time.Should().Be("18:00");
    }

    [Fact]
    public void Unknown_child_is_not_guessed()
    {
        var d = Run(Item("Defter al", "yarın", child: "Zeynep")).Drafts.Single();
        d.Payload.ChildId.Should().BeNull();
        d.Payload.NeedsInfo.Should().Contain("child");
    }

    [Fact]
    public void Whatsapp_dump_with_many_announcements_is_capped_and_deduplicated()
    {
        var items = Enumerable.Range(0, 30).Select(i => Item($"Görev {i % 25}", "yarın")).ToArray();
        var r = Run(items);
        r.Drafts.Should().HaveCount(ExtractionService.MaxItems);
        r.Drafts.Select(x => x.Payload.Title).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Garbage_output_produces_no_proposals()
    {
        using var doc = JsonDocument.Parse("""{"items":[1,"x",{"title":""},{"kind":"task"}]}""");
        ExtractionService.Validate(doc.RootElement, Family).Drafts.Should().BeEmpty();
        using var notObject = JsonDocument.Parse("[]");
        ExtractionService.Validate(notObject.RootElement, Family).Drafts.Should().BeEmpty();
    }

    [Fact]
    public void Event_kind_is_mapped()
    {
        Run(Item("Gösteri", "15 Kasım", kind: "event")).Drafts.Single().Kind.Should().Be(ProposalKind.CreateEvent);
    }
}
