using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hally.Api.Common;
using Sentry;

namespace Hally.Api.Tests;

[Collection(ApiCollection.Name)]
public class OperationsTests(HallyApiFactory api)
{
    [Fact]
    public async Task Metrics_need_the_admin_key_and_show_aggregates_only()
    {
        var owner = await api.SignInAsync($"{Guid.NewGuid():N}@example.com", "Ayşe Yılmaz");
        await owner.PostIdempotentAsync("/api/v1/families", new { name = "Yılmaz", timeZoneId = "Europe/Istanbul" });

        var anonymous = api.CreateClient();
        (await anonymous.GetAsync("/api/v1/admin/metrics")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/metrics?days=7");
        request.Headers.Add("X-Admin-Key", HallyApiFactory.AdminKey);
        var res = await anonymous.SendAsync(request);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("families").GetInt32().Should().BePositive();
        body.GetProperty("funnel").EnumerateArray().First(s => s.GetProperty("step").GetString() == "family_created")
            .GetProperty("families").GetInt32().Should().BePositive();
        body.GetProperty("daily").EnumerateArray().Should().Contain(d => d.GetProperty("event").GetString() == "family_created");
        (await res.Content.ReadAsStringAsync()).Should().NotContain("Ayşe").And.NotContain("@example.com");
    }

    [Fact]
    public void Error_reports_lose_personal_data_and_link_tokens()
    {
        var e = new SentryEvent { User = new SentryUser { Email = "a@b.c", Id = "u1" } };
        e.Request.Url = "https://api.hally.app/s/AbCdEf123/tasks/42/done";
        e.Request.Data = "{\"text\":\"Elif okul gezisi\"}";
        e.Request.QueryString = "?token=secret";
        e.Request.Headers["Authorization"] = "Bearer xyz";
        e.Request.Headers["User-Agent"] = "Hally/1.0";

        var scrubbed = ErrorMonitoring.Scrub(e, new SentryHint());
        scrubbed.User.Email.Should().BeNull();
        scrubbed.User.Id.Should().BeNull();
        scrubbed.Request.Data.Should().BeNull();
        scrubbed.Request.QueryString.Should().BeNull();
        scrubbed.Request.Headers.Should().ContainKey("User-Agent").And.NotContainKey("Authorization");
        scrubbed.Request.Url.Should().Be("https://api.hally.app/s/[token]/tasks/42/done");
        ErrorMonitoring.MaskTokens("/api/v1/invitations/tok123/accept").Should().Be("/api/v1/invitations/[token]/accept");
        ErrorMonitoring.MaskTokens("/api/v1/families/1/invitations").Should().Be("/api/v1/families/1/invitations");
    }
}
