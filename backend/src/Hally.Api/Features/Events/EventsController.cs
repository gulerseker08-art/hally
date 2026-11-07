using FluentValidation;
using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Domain.Planning;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Events;

public record EventDto(Guid Id, string Title, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool AllDay, string? Location, Guid? ChildId, Guid? SourceProposalId);
public record SaveEventRequest(string Title, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool AllDay, string? Location, Guid? ChildId);
public record SavedEventDto(EventDto Event, IReadOnlyList<EventDto> Conflicts);

public class SaveEventRequestValidator : AbstractValidator<SaveEventRequest>
{
    public SaveEventRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Location).MaximumLength(200);
        RuleFor(x => x.EndsAt).GreaterThanOrEqualTo(x => x.StartsAt).When(x => x.EndsAt.HasValue);
    }
}

[ApiController]
[Authorize(Policy = Policies.FamilyMember)]
[Route("api/v1/families/{familyId:guid}/events")]
public class EventsController(HallyDbContext db, IAnalytics analytics, TimeProvider clock) : ControllerBase
{
    /// <summary>Events overlapping [from, to). Defaults to the next 31 days.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EventDto>>> List(Guid familyId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var start = from ?? clock.GetUtcNow();
        var end = to ?? start.AddDays(31);
        if (end <= start || end - start > TimeSpan.FromDays(400))
        {
            return Problem(statusCode: 400, title: "Range must be positive and at most 400 days.", type: "https://hally.app/problems/events.range_invalid");
        }

        return (await InRange(familyId, start, end, ct)).Select(ToDto).ToList();
    }

    [HttpPost]
    [Idempotent]
    [ProducesResponseType<SavedEventDto>(201)]
    public async Task<IActionResult> Create(Guid familyId, SaveEventRequest request, CancellationToken ct)
    {
        await EnsureChild(familyId, request.ChildId, ct);
        var userId = User.UserId();
        var evt = new CalendarEvent(familyId, userId, request.Title, request.StartsAt, request.EndsAt, request.AllDay, request.Location, request.ChildId);
        var conflicts = await Conflicts(familyId, evt, ct);
        db.Events.Add(evt);
        db.AuditEntries.Add(new AuditEntry { FamilyId = familyId, ActorUserId = userId, Action = "event.created", EntityType = "event", EntityId = evt.Id, At = clock.GetUtcNow() });
        analytics.Track(AnalyticsEvents.EventCreated, userId, familyId, new Dictionary<string, object?> { ["source"] = "manual" });
        await db.SaveChangesAsync(ct);
        return StatusCode(201, new SavedEventDto(ToDto(evt), conflicts));
    }

    [HttpPut("{eventId:guid}")]
    public async Task<ActionResult<SavedEventDto>> Update(Guid familyId, Guid eventId, SaveEventRequest request, CancellationToken ct)
    {
        await EnsureChild(familyId, request.ChildId, ct);
        var evt = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.FamilyId == familyId, ct)
                  ?? throw new NotFoundException("event.not_found", "Event not found.");
        evt.Update(request.Title, request.StartsAt, request.EndsAt, request.AllDay, request.Location, request.ChildId);
        db.AuditEntries.Add(new AuditEntry { FamilyId = familyId, ActorUserId = User.UserId(), Action = "event.updated", EntityType = "event", EntityId = evt.Id, At = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return new SavedEventDto(ToDto(evt), await Conflicts(familyId, evt, ct));
    }

    [HttpDelete("{eventId:guid}")]
    public async Task<IActionResult> Delete(Guid familyId, Guid eventId, CancellationToken ct)
    {
        var evt = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.FamilyId == familyId, ct)
                  ?? throw new NotFoundException("event.not_found", "Event not found.");
        db.Events.Remove(evt);
        db.AuditEntries.Add(new AuditEntry { FamilyId = familyId, ActorUserId = User.UserId(), Action = "event.deleted", EntityType = "event", EntityId = evt.Id, At = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<List<CalendarEvent>> InRange(Guid familyId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        // Load a slightly wider window and apply the exact overlap rule (with default durations) in memory.
        var candidates = await db.Events.AsNoTracking()
            .Where(e => e.FamilyId == familyId && e.StartsAt < end && e.StartsAt >= start.AddDays(-2))
            .OrderBy(e => e.StartsAt).ToListAsync(ct);
        return candidates.Where(e => e.Overlaps(start, end)).ToList();
    }

    private async Task<IReadOnlyList<EventDto>> Conflicts(Guid familyId, CalendarEvent evt, CancellationToken ct) =>
        evt.AllDay ? [] : (await InRange(familyId, evt.StartsAt, evt.EffectiveEnd, ct))
            .Where(e => e.Id != evt.Id && !e.AllDay).Select(ToDto).ToList();

    private async Task EnsureChild(Guid familyId, Guid? child, CancellationToken ct)
    {
        if (child is { } c && !await db.Children.AnyAsync(x => x.FamilyId == familyId && x.Id == c, ct))
        {
            throw new Domain.Common.DomainException("event.child_not_in_family", "The child does not belong to this family.");
        }
    }

    public static EventDto ToDto(CalendarEvent e) => new(e.Id, e.Title, e.StartsAt, e.EndsAt, e.AllDay, e.Location, e.ChildId, e.SourceProposalId);
}
