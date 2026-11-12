using Hally.Api.Auth;
using Hally.Api.Features.Events;
using Hally.Api.Features.Proposals;
using Hally.Api.Features.Tasks;
using Hally.Domain.Capture;
using Hally.Domain.Common;
using Hally.Domain.Planning;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Today;

public record TodayDto(string Date, string TimeZoneId, IReadOnlyList<TaskDto> Overdue, IReadOnlyList<TaskDto> DueToday,
    IReadOnlyList<EventDto> Events, IReadOnlyList<ProposalDto> WaitingForYou);

[ApiController]
[Authorize(Policy = Policies.FamilyMember)]
[Route("api/v1/families/{familyId:guid}/today")]
public class TodayController(HallyDbContext db, TimeProvider clock) : ControllerBase
{
    /// <summary>The family's day in its own time zone: overdue and due tasks, events and pending proposals.</summary>
    [HttpGet]
    public async Task<ActionResult<TodayDto>> Get(Guid familyId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var family = await db.Families.AsNoTracking().FirstAsync(f => f.Id == familyId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(family.TimeZoneId);
        var localToday = DateOnly.FromDateTime(FamilyTimeZone.ToFamilyTime(now, family.TimeZoneId).DateTime);
        var dayStart = ProposalEngine.ToUtc(localToday.ToString("yyyy-MM-dd"), null, zone)!.Value;
        var dayEnd = ProposalEngine.ToUtc(localToday.AddDays(1).ToString("yyyy-MM-dd"), null, zone)!.Value;

        var open = await db.Tasks.AsNoTracking()
            .Where(t => t.FamilyId == familyId && t.State == TaskState.Open && t.DueAt != null && t.DueAt < dayEnd)
            .OrderBy(t => t.DueAt).ToListAsync(ct);
        var events = await db.Events.AsNoTracking()
            .Where(e => e.FamilyId == familyId && e.StartsAt < dayEnd && e.StartsAt >= dayStart.AddDays(-2))
            .OrderBy(e => e.StartsAt).ToListAsync(ct);
        var pending = await db.Proposals.AsNoTracking()
            .Where(p => p.FamilyId == familyId && p.Status == ProposalStatus.Pending && p.ExpiresAt > now)
            .OrderBy(p => p.CreatedAt).Take(50).ToListAsync(ct);

        return new TodayDto(
            localToday.ToString("yyyy-MM-dd"),
            family.TimeZoneId,
            open.Where(t => t.DueAt < dayStart).Select(t => TasksController.ToDto(t, now)).ToList(),
            open.Where(t => t.DueAt >= dayStart).Select(t => TasksController.ToDto(t, now)).ToList(),
            events.Where(e => e.Overlaps(dayStart, dayEnd)).Select(EventsController.ToDto).ToList(),
            pending.Select(p => ProposalsController.ToDto(p, now)).ToList());
    }
}
