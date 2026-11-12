using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hally.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hally.Api.Tests;

public record DemoItemBody(string Kind, PayloadBody Payload);
public record DemoScanBody(string? Summary, List<DemoItemBody> Items);

[Collection(ApiCollection.Name)]
public class DemoScanTests(HallyApiFactory api)
{
    private static MultipartFormDataContent Form(string? sampleId, string locale = "tr")
    {
        var form = new MultipartFormDataContent { { new StringContent(locale), "locale" }, { new StringContent("Europe/Istanbul"), "timeZoneId" } };
        if (sampleId is not null)
        {
            form.Add(new StringContent(sampleId), "sampleId");
        }

        return form;
    }

    [Fact]
    public async Task Demo_scan_works_without_an_account_stores_nothing_and_is_rate_limited()
    {
        api.Ai.Respond = _ => JsonSerializer.Serialize(new
        {
            summary = "Okul duyurusu",
            items = new[]
            {
                new { kind = "task", title = "Kırmızı tişört giydir", dateText = (string?)"Cuma günü", date = (string?)null, time = (string?)null, childName = (string?)null,
                      materials = Array.Empty<string>(), notes = (string?)null, location = (string?)null, confidence = "high", sourceQuote = (string?)"Cuma günü sınıfımız kırmızı giyiyor" },
                new { kind = "event", title = "Veli toplantısı", dateText = (string?)"Ayın 25'inde saat 18:30", date = (string?)null, time = (string?)"18:30", childName = (string?)null,
                      materials = Array.Empty<string>(), notes = (string?)null, location = (string?)null, confidence = "high", sourceQuote = (string?)"Ayın 25'inde saat 18:30'da veli toplantımız" },
            },
        });

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HallyDbContext>();
        var proposalsBefore = await db.Proposals.CountAsync();
        var capturesBefore = await db.Captures.CountAsync();

        var client = api.CreateClient();
        var res = await client.PostAsync("/api/v1/demo/scan", Form("school-tr"));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await res.Content.ReadFromJsonAsync<DemoScanBody>())!;
        body.Items.Should().HaveCount(2);
        body.Items[1].Payload.Time.Should().Be("18:30");
        body.Items.Should().OnlyContain(i => i.Payload.Date != null);
        api.Ai.Requests.Last().UserContent.Should().Contain("kırmızı tişört");

        (await db.Proposals.CountAsync()).Should().Be(proposalsBefore);
        (await db.Captures.CountAsync()).Should().Be(capturesBefore);
        (await db.AnalyticsEvents.AnyAsync(e => e.Name == "onboarding_demo_scanned" && e.UserId == null)).Should().BeTrue();

        (await client.PostAsync("/api/v1/demo/scan", Form("unknown-sample"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            statuses.Add((await client.PostAsync("/api/v1/demo/scan", Form("school-en", "en"))).StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }
}
