using FluentAssertions;
using Hally.Domain.Common;

namespace Hally.Api.Tests;

/// <summary>AI evaluation: date phrases are resolved by the server, not by the model.
/// Reference "today" is Saturday 2026-11-07.</summary>
public class RelativeDateResolverTests
{
    private static readonly DateOnly Today = new(2026, 11, 7);

    [Theory]
    [InlineData("bugün", "2026-11-07", null)]
    [InlineData("yarın", "2026-11-08", null)]
    [InlineData("yarın sabah", "2026-11-08", null)]
    [InlineData("yarın saat 14:30", "2026-11-08", "14:30")]
    [InlineData("öbür gün", "2026-11-09", null)]
    [InlineData("cuma", "2026-11-13", null)]
    [InlineData("Cuma günü", "2026-11-13", null)]
    [InlineData("haftaya cuma", "2026-11-13", null)]
    [InlineData("haftaya pazartesi", "2026-11-09", null)]
    [InlineData("pazartesiye", "2026-11-09", null)]
    [InlineData("cumartesi", "2026-11-07", null)]
    [InlineData("bu cuma", "2026-11-06", null)]
    [InlineData("ayın 15'i", "2026-11-15", null)]
    [InlineData("ayın 15'inde", "2026-11-15", null)]
    [InlineData("ayın 3'ü", "2026-12-03", null)]
    [InlineData("15 Kasım", "2026-11-15", null)]
    [InlineData("15 Kasım saat 09.00", "2026-11-15", "09:00")]
    [InlineData("3 Ocak", "2027-01-03", null)]
    [InlineData("12.11", "2026-11-12", null)]
    [InlineData("12/11/2026", "2026-11-12", null)]
    [InlineData("2026-12-01", "2026-12-01", null)]
    [InlineData("İkİ GÜN SONRA DEĞİL, YARIN", "2026-11-08", null)]
    [InlineData("tomorrow", "2026-11-08", null)]
    [InlineData("tomorrow 3pm", "2026-11-08", "15:00")]
    [InlineData("next Friday", "2026-11-13", null)]
    [InlineData("on Monday", "2026-11-09", null)]
    [InlineData("November 20th", "2026-11-20", null)]
    [InlineData("on the 20th", "2026-11-20", null)]
    public void Resolves_phrases(string phrase, string expectedDate, string? expectedTime)
    {
        var result = RelativeDateResolver.Resolve(phrase, Today);
        result.Should().NotBeNull();
        result!.Date.ToString("yyyy-MM-dd").Should().Be(expectedDate);
        result.Time?.ToString("HH:mm").Should().Be(expectedTime);
        if (expectedTime is null)
        {
            result.Time.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("haftaya")]
    [InlineData("yakında")]
    [InlineData("soon")]
    [InlineData("31.02")]
    public void Ambiguous_or_invalid_phrases_return_null(string? phrase) =>
        RelativeDateResolver.Resolve(phrase, Today).Should().BeNull();
}
