using System.Security.Cryptography;
using System.Text;
using Hally.Domain.Billing;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Admin;

public record DailyCount(string Day, string Event, int Count);
public record FunnelStep(string Step, int Families);
public record MetricsDto(DateTimeOffset From, DateTimeOffset To, int Users, int Families, int PlusFamilies,
    IReadOnlyList<FunnelStep> Funnel, IReadOnlyList<DailyCount> Daily);

/// <summary>Beta metrics for the team: daily event counts and the activation funnel. Aggregates only, no ids or PII.
/// Protected by the <c>X-Admin-Key</c> header (<c>Admin:ApiKey</c>); with no key configured the endpoint does not exist.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/admin/metrics")]
public class MetricsController(HallyDbContext db, IConfiguration config, TimeProvider clock) : ControllerBase
{
    private static readonly string[] FunnelEvents =
    [
        AnalyticsEvents.FamilyCreated,
        AnalyticsEvents.InvitationAccepted,
        AnalyticsEvents.CaptureStarted,
        AnalyticsEvents.ProposalApproved,
        AnalyticsEvents.HelperLinkCreated,
        AnalyticsEvents.PaywallViewed,
        AnalyticsEvents.TrialStarted,
        AnalyticsEvents.SubscriptionStarted,
    ];

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var expected = config["Admin:ApiKey"];
        if (string.IsNullOrEmpty(expected) || expected.Length < 24)
        {
            return NotFound();
        }

        var given = Request.Headers["X-Admin-Key"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
        {
            return Unauthorized();
        }

        var to = clock.GetUtcNow();
        var from = to.AddDays(-Math.Clamp(days, 1, 180));
        var events = db.AnalyticsEvents.AsNoTracking().Where(e => e.OccurredAt >= from);

        var raw = await events.GroupBy(e => new { Day = e.OccurredAt.Date, e.Name })
            .Select(g => new { g.Key.Day, g.Key.Name, Count = g.Count() }).ToListAsync(ct);
        var daily = raw.OrderBy(r => r.Day).ThenBy(r => r.Name)
            .Select(r => new DailyCount(r.Day.ToString("yyyy-MM-dd"), r.Name, r.Count)).ToList();

        var funnel = new List<FunnelStep>();
        foreach (var name in FunnelEvents)
        {
            var families = await events.Where(e => e.Name == name && e.FamilyId != null).Select(e => e.FamilyId).Distinct().CountAsync(ct);
            funnel.Add(new FunnelStep(name, families));
        }

        var now = to;
        var plus = await db.Subscriptions.AsNoTracking().CountAsync(s => s.Source != SubscriptionSource.None && s.ExpiresAt > now, ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(new MetricsDto(from, to, await db.Users.CountAsync(ct), await db.Families.CountAsync(ct), plus, funnel, daily));
    }
}
