using FluentValidation;
using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Domain.Common;
using Hally.Domain.Notifications;
using Hally.Domain.Planning;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Notifications;

public record ReminderDto(Guid Id, string Target, Guid TargetId, Guid? RecipientUserId, DateTimeOffset FireAt, string Status);

/// <param name="ForEveryone">false = only the caller is reminded; true = every adult in the family.</param>
public record CreateReminderRequest(ReminderTarget Target, Guid TargetId, DateTimeOffset FireAt, bool ForEveryone);

public class CreateReminderRequestValidator : AbstractValidator<CreateReminderRequest>
{
    public CreateReminderRequestValidator()
    {
        RuleFor(x => x.Target).IsInEnum();
        RuleFor(x => x.TargetId).NotEmpty();
    }
}

[ApiController]
[Authorize(Policy = Policies.FamilyMember)]
[Route("api/v1/families/{familyId:guid}/reminders")]
public class RemindersController(HallyDbContext db, IAnalytics analytics, Billing.EntitlementService entitlements, TimeProvider clock) : ControllerBase
{
    /// <summary>Scheduled reminders, optionally for one task or event.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReminderDto>>> List(Guid familyId, [FromQuery] Guid? targetId, CancellationToken ct)
    {
        var query = db.Reminders.AsNoTracking().Where(r => r.FamilyId == familyId && r.Status == ReminderStatus.Scheduled);
        if (targetId is { } t)
        {
            query = query.Where(r => r.TargetId == t);
        }

        return (await query.OrderBy(r => r.FireAt).Take(200).ToListAsync(ct)).Select(ToDto).ToList();
    }

    [HttpPost]
    [Idempotent]
    [ProducesResponseType<ReminderDto>(201)]
    public async Task<IActionResult> Create(Guid familyId, CreateReminderRequest request, CancellationToken ct)
    {
        var exists = request.Target switch
        {
            ReminderTarget.Task => await db.Tasks.AnyAsync(x => x.Id == request.TargetId && x.FamilyId == familyId && x.State == TaskState.Open, ct),
            _ => await db.Events.AnyAsync(x => x.Id == request.TargetId && x.FamilyId == familyId, ct),
        };
        if (!exists)
        {
            throw new NotFoundException("reminder.target_not_found", "The task or event was not found in this family.");
        }

        var scheduled = await db.Reminders.CountAsync(r => r.TargetId == request.TargetId && r.Status == ReminderStatus.Scheduled, ct);
        if (scheduled >= Reminder.MaxPerTarget)
        {
            throw new DomainException("reminder.limit", $"At most {Reminder.MaxPerTarget} reminders per item.");
        }

        // Free: one reminder per item; more ("advanced reminders") is Family Plus.
        await entitlements.EnsureCanRemindAsync(familyId, request.TargetId, ct);

        var userId = User.UserId();
        var now = clock.GetUtcNow();
        var reminder = new Reminder(familyId, userId, request.Target, request.TargetId, request.ForEveryone ? null : userId, request.FireAt, now);
        db.Reminders.Add(reminder);
        analytics.Track(AnalyticsEvents.ReminderCreated, userId, familyId, new Dictionary<string, object?>
        {
            ["target"] = request.Target.ToString().ToLowerInvariant(),
            ["for_everyone"] = request.ForEveryone,
        });
        await db.SaveChangesAsync(ct);
        return StatusCode(201, ToDto(reminder));
    }

    [HttpDelete("{reminderId:guid}")]
    public async Task<IActionResult> Cancel(Guid familyId, Guid reminderId, CancellationToken ct)
    {
        var reminder = await db.Reminders.FirstOrDefaultAsync(r => r.Id == reminderId && r.FamilyId == familyId, ct)
                       ?? throw new NotFoundException("reminder.not_found", "Reminder not found.");
        reminder.Cancel();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public static ReminderDto ToDto(Reminder r) => new(r.Id, r.Target.ToString(), r.TargetId, r.RecipientUserId, r.FireAt, r.Status.ToString());
}
