using System.Globalization;
using FluentValidation;
using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Api.Features.Proposals;
using Hally.Domain.Common;
using Hally.Domain.Planning;
using Hally.Domain.Sharing;
using Hally.Domain.Users;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hally.Api.Features.Sharing;

/// <param name="Date">Family-local day (yyyy-MM-dd) for <c>Day</c> links.</param>
/// <param name="ExpiresInHours">1-168, default 72.</param>
public record CreateShareLinkRequest(ShareLinkKind Kind, Guid? TaskId, string? Date, int? ExpiresInHours);
public record ShareLinkDto(Guid Id, string Kind, Guid? TaskId, string? Date, DateTimeOffset ExpiresAt, int OpenCount, DateTimeOffset? LastOpenedAt);
public record ShareLinkCreatedDto(Guid Id, string Url, string ShareText, string WhatsAppUrl, DateTimeOffset ExpiresAt);

public class CreateShareLinkRequestValidator : AbstractValidator<CreateShareLinkRequest>
{
    public CreateShareLinkRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.TaskId).NotEmpty().When(x => x.Kind == ShareLinkKind.Task);
        RuleFor(x => x.Date).NotEmpty().Must(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", out _)).When(x => x.Kind == ShareLinkKind.Day)
            .WithMessage("Date must be yyyy-MM-dd.");
        RuleFor(x => x.ExpiresInHours).InclusiveBetween(1, 168).When(x => x.ExpiresInHours.HasValue);
    }
}

[ApiController]
[Authorize(Policy = Policies.FamilyMember)]
[Route("api/v1/families/{familyId:guid}/share-links")]
public class ShareLinksController(HallyDbContext db, IAnalytics analytics, IOptions<AppOptions> app, Billing.EntitlementService entitlements, TimeProvider clock) : ControllerBase
{
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<ShareLinkCreatedDto>(201)]
    public async Task<IActionResult> Create(Guid familyId, CreateShareLinkRequest request, CancellationToken ct)
    {
        await entitlements.EnsureCanShareAsync(familyId, ct);
        var userId = User.UserId();
        var now = clock.GetUtcNow();
        var token = Crypto.NewToken();
        var lifetime = request.ExpiresInHours is { } h ? TimeSpan.FromHours(h) : (TimeSpan?)null;
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        ShareLink link;
        string subject;
        if (request.Kind == ShareLinkKind.Task)
        {
            var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TaskId && t.FamilyId == familyId, ct)
                       ?? throw new NotFoundException("task.not_found", "Task not found.");
            link = ShareLink.ForTask(familyId, userId, task.Id, Crypto.Sha256(token), now, lifetime);
            subject = task.Title;
        }
        else
        {
            var day = DateOnly.ParseExact(request.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            link = ShareLink.ForDay(familyId, userId, day, Crypto.Sha256(token), now, lifetime);
            subject = user.Locale == Locales.English ? day.ToString("dddd, MMM d", CultureInfo.InvariantCulture) : day.ToString("d MMMM dddd", new CultureInfo("tr-TR"));
        }

        db.ShareLinks.Add(link);
        analytics.Track(AnalyticsEvents.HelperLinkCreated, userId, familyId, new Dictionary<string, object?> { ["kind"] = request.Kind.ToString().ToLowerInvariant() });
        await db.SaveChangesAsync(ct);

        var url = $"{app.Value.PublicBaseUrl.TrimEnd('/')}/s/{token}";
        var first = user.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? user.DisplayName;
        var text = user.Locale == Locales.English
            ? $"{first} shared \"{subject}\" with you on {app.Value.Name}: {url}"
            : $"{first} seninle \"{subject}\" paylaştı ({app.Value.Name}): {url}";
        return StatusCode(201, new ShareLinkCreatedDto(link.Id, url, text, $"https://wa.me/?text={Uri.EscapeDataString(text)}", link.ExpiresAt));
    }

    /// <summary>Active (not expired, not revoked) links of the family.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ShareLinkDto>>> List(Guid familyId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var links = await db.ShareLinks.AsNoTracking()
            .Where(l => l.FamilyId == familyId && l.RevokedAt == null && l.ExpiresAt > now)
            .OrderByDescending(l => l.CreatedAt).ToListAsync(ct);
        return links.Select(l => new ShareLinkDto(l.Id, l.Kind.ToString(), l.TaskId, l.Day?.ToString("yyyy-MM-dd"), l.ExpiresAt, l.OpenCount, l.LastOpenedAt)).ToList();
    }

    [HttpDelete("{linkId:guid}")]
    public async Task<IActionResult> Revoke(Guid familyId, Guid linkId, CancellationToken ct)
    {
        var link = await db.ShareLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.FamilyId == familyId, ct)
                   ?? throw new NotFoundException("share_link.not_found", "Share link not found.");
        link.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>The helper's view: a tiny server-rendered page, no account and no app needed.</summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("s/{token}")]
public class SharedPageController(HallyDbContext db, IAnalytics analytics, IOptions<AppOptions> app, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Show(string token, CancellationToken ct)
    {
        var tr = !Request.Headers.AcceptLanguage.ToString().StartsWith("en", StringComparison.OrdinalIgnoreCase);
        var link = await db.ShareLinks.FirstOrDefaultAsync(l => l.TokenHash == Crypto.Sha256(token), ct);
        var now = clock.GetUtcNow();
        if (link is null || !link.IsActive(now))
        {
            return Page(SharedPage.Gone(app.Value.Name, tr), link is null ? 404 : 410);
        }

        link.Opened(now);
        analytics.Track(AnalyticsEvents.HelperLinkOpened, null, link.FamilyId, new Dictionary<string, object?> { ["kind"] = link.Kind.ToString().ToLowerInvariant() });
        await db.SaveChangesAsync(ct);

        var family = await db.Families.AsNoTracking().FirstAsync(f => f.Id == link.FamilyId, ct);
        var sharer = await db.Users.AsNoTracking().Where(u => u.Id == link.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "";
        var (tasks, events) = await Items(link, family.TimeZoneId, ct);
        var model = new SharedPage.Model(app.Value.Name, sharer.Split(' ').FirstOrDefault() ?? "", link.Kind, tr, family.TimeZoneId, link.Day,
            tasks.Select(t => new SharedPage.Item(t.Id, t.Title, t.DueAt, t.AllDay, t.State == TaskState.Done)).ToList(),
            events.Select(e => new SharedPage.Item(e.Id, e.Title, e.StartsAt, e.AllDay, false, e.Location)).ToList(),
            link.ExpiresAt);
        return Page(SharedPage.Render(model, $"/s/{Uri.EscapeDataString(token)}"), 200);
    }

    /// <summary>"Mark as done" from the helper page. Only tasks covered by the link can be completed.</summary>
    [HttpPost("tasks/{taskId:guid}/done")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Done(string token, Guid taskId, CancellationToken ct)
    {
        var link = await db.ShareLinks.AsNoTracking().FirstOrDefaultAsync(l => l.TokenHash == Crypto.Sha256(token), ct);
        var now = clock.GetUtcNow();
        if (link is null || !link.IsActive(now))
        {
            return NotFound();
        }

        var family = await db.Families.AsNoTracking().FirstAsync(f => f.Id == link.FamilyId, ct);
        var (tasks, _) = await Items(link, family.TimeZoneId, ct);
        if (tasks.All(t => t.Id != taskId))
        {
            return NotFound();
        }

        var task = await db.Tasks.FirstAsync(t => t.Id == taskId && t.FamilyId == link.FamilyId, ct);
        if (task.State == TaskState.Open)
        {
            task.CompleteByHelper(now);
            db.AuditEntries.Add(new AuditEntry { FamilyId = link.FamilyId, Action = "task.completed_by_helper", EntityType = "task", EntityId = task.Id, At = now });
            analytics.Track(AnalyticsEvents.HelperTaskCompleted, null, link.FamilyId);
            await db.SaveChangesAsync(ct);
        }

        return Redirect($"/s/{Uri.EscapeDataString(token)}");
    }

    private async Task<(List<FamilyTask> Tasks, List<CalendarEvent> Events)> Items(ShareLink link, string timeZoneId, CancellationToken ct)
    {
        if (link.Kind == ShareLinkKind.Task)
        {
            var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == link.TaskId && t.FamilyId == link.FamilyId && t.State != TaskState.Cancelled, ct);
            return (task is null ? [] : [task], []);
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var start = ProposalEngine.ToUtc(link.Day!.Value.ToString("yyyy-MM-dd"), null, zone)!.Value;
        var end = ProposalEngine.ToUtc(link.Day!.Value.AddDays(1).ToString("yyyy-MM-dd"), null, zone)!.Value;
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.FamilyId == link.FamilyId && t.State != TaskState.Cancelled && t.DueAt >= start && t.DueAt < end)
            .OrderBy(t => t.DueAt).ToListAsync(ct);
        var events = (await db.Events.AsNoTracking()
                .Where(e => e.FamilyId == link.FamilyId && e.StartsAt < end && e.StartsAt >= start.AddDays(-2))
                .OrderBy(e => e.StartsAt).ToListAsync(ct))
            .Where(e => e.Overlaps(start, end)).ToList();
        return (tasks, events);
    }

    private ContentResult Page(string html, int status)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'";
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = status };
    }
}
