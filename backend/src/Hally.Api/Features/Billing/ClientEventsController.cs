using System.Text.Json;
using Hally.Api.Common;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Billing;

public record ClientEventRequest(string Name, Guid? FamilyId, Dictionary<string, JsonElement>? Properties);

/// <summary>Product events that only the app can see (paywall views, card shares, onboarding steps).
/// Only allow-listed names and property keys are stored, and values must be short scalars, so free text
/// and PII cannot reach analytics through this door.</summary>
[ApiController]
[Authorize]
[Route("api/v1/analytics/events")]
public class ClientEventsController(HallyDbContext db, IAnalytics analytics) : ControllerBase
{
    public static readonly IReadOnlyDictionary<string, string[]> Allowed = new Dictionary<string, string[]>
    {
        [AnalyticsEvents.PaywallViewed] = ["trigger"],
        ["paywall_dismissed"] = ["trigger"],
        ["purchase_started"] = ["product", "store"],
        ["onboarding_completed"] = [],
        ["summary_card_shared"] = ["offset", "children"],
        ["invitation_shared"] = ["channel"],
    };

    [HttpPost]
    public async Task<IActionResult> Track(ClientEventRequest request, CancellationToken ct)
    {
        if (!Allowed.TryGetValue(request.Name ?? string.Empty, out var keys))
        {
            return Problem(statusCode: 400, title: "Unknown event.", type: "https://hally.app/problems/analytics.unknown_event");
        }

        var userId = User.UserId();
        if (request.FamilyId is { } fid && !await db.FamilyMembers.AnyAsync(m => m.FamilyId == fid && m.UserId == userId, ct))
        {
            return NotFound();
        }

        var props = new Dictionary<string, object?>();
        foreach (var (key, value) in request.Properties ?? [])
        {
            if (!keys.Contains(key))
            {
                continue;
            }

            object? scalar = value.ValueKind switch
            {
                JsonValueKind.String when value.GetString()!.Length <= 40 => value.GetString(),
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
            if (scalar is not null)
            {
                props[key] = scalar;
            }
        }

        analytics.Track(request.Name!, userId, request.FamilyId, props.Count == 0 ? null : props);
        await db.SaveChangesAsync(ct);
        return Accepted();
    }
}
