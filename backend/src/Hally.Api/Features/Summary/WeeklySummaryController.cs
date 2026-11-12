using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Api.Features.Proposals;
using Hally.Domain.Capture;
using Hally.Domain.Common;
using Hally.Domain.Planning;
using Hally.Domain.Users;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Summary;

public record AdultShareDto(string FirstName, int Completed);
public record ChildCountDto(string FirstName, int Items);

/// <param name="Children">Only filled when <c>includeChildNames=true</c>; child names never appear otherwise.</param>
public record WeeklySummaryDto(string WeekStart, string WeekEnd, int TasksCompleted, int EventsCount, int AiActionsApproved,
    int HelperCompleted, IReadOnlyList<AdultShareDto> Adults, IReadOnlyList<ChildCountDto> Children);

[ApiController]
[Authorize(Policy = Policies.FamilyMember)]
[Route("api/v1/families/{familyId:guid}/weekly-summary")]
public class WeeklySummaryController(HallyDbContext db, TimeProvider clock) : ControllerBase
{
    /// <summary>The family's week (Monday-Sunday, family time zone). <paramref name="offset"/> 0 = this week, -1 = last week.</summary>
    [HttpGet]
    public async Task<ActionResult<WeeklySummaryDto>> Get(Guid familyId, [FromQuery] int offset = 0, [FromQuery] bool includeChildNames = false, CancellationToken ct = default) =>
        await Build(familyId, Math.Clamp(offset, -8, 0), includeChildNames, ct);

    /// <summary>The same week as a shareable 1080×1350 PNG card.</summary>
    [HttpGet("card.png")]
    [Produces("image/png")]
    public async Task<IActionResult> Card(Guid familyId, [FromQuery] int offset = 0, [FromQuery] bool includeChildNames = false, CancellationToken ct = default)
    {
        var summary = await Build(familyId, Math.Clamp(offset, -8, 0), includeChildNames, ct);
        var locale = await db.Users.AsNoTracking().Where(u => u.Id == User.UserId()).Select(u => u.Locale).FirstAsync(ct);
        var family = await db.Families.AsNoTracking().Where(f => f.Id == familyId).Select(f => f.Name).FirstAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return File(SummaryCard.Render(summary, family, locale == Locales.English), "image/png");
    }

    private async Task<WeeklySummaryDto> Build(Guid familyId, int offset, bool includeChildNames, CancellationToken ct)
    {
        var family = await db.Families.AsNoTracking().Include(f => f.Members).Include(f => f.Children).FirstAsync(f => f.Id == familyId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(family.TimeZoneId);
        var today = DateOnly.FromDateTime(FamilyTimeZone.ToFamilyTime(clock.GetUtcNow(), family.TimeZoneId).DateTime);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7) + 7 * offset);
        var start = ProposalEngine.ToUtc(monday.ToString("yyyy-MM-dd"), null, zone)!.Value;
        var end = ProposalEngine.ToUtc(monday.AddDays(7).ToString("yyyy-MM-dd"), null, zone)!.Value;

        var done = await db.Tasks.AsNoTracking()
            .Where(t => t.FamilyId == familyId && t.State == TaskState.Done && t.CompletedAt >= start && t.CompletedAt < end)
            .Select(t => new { t.CompletedByUserId, t.CompletedByHelper, t.ChildId }).ToListAsync(ct);
        var events = await db.Events.AsNoTracking()
            .Where(e => e.FamilyId == familyId && e.StartsAt >= start && e.StartsAt < end).Select(e => e.ChildId).ToListAsync(ct);
        var approved = await db.Proposals.AsNoTracking()
            .CountAsync(p => p.FamilyId == familyId && p.Status == ProposalStatus.Approved && p.DecidedAt >= start && p.DecidedAt < end, ct);

        var memberIds = family.Members.Select(m => m.UserId).ToList();
        var names = await db.Users.AsNoTracking().Where(u => memberIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var adults = memberIds
            .Select(id => new AdultShareDto(FirstName(names.GetValueOrDefault(id, "")), done.Count(t => t.CompletedByUserId == id)))
            .OrderByDescending(a => a.Completed).ToList();
        var children = includeChildNames
            ? family.Children.Select(c => new ChildCountDto(c.FirstName, done.Count(t => t.ChildId == c.Id) + events.Count(e => e == c.Id)))
                .Where(c => c.Items > 0).OrderByDescending(c => c.Items).ToList()
            : [];

        return new WeeklySummaryDto(monday.ToString("yyyy-MM-dd"), monday.AddDays(6).ToString("yyyy-MM-dd"), done.Count, events.Count, approved,
            done.Count(t => t.CompletedByHelper), adults, children);
    }

    private static string FirstName(string displayName) => displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
}
