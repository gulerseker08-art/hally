using FluentValidation;
using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Domain.Planning;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Tasks;

public record TaskDto(Guid Id, string Title, string? Notes, DateTimeOffset? DueAt, bool AllDay, Guid? AssigneeUserId, Guid? ChildId,
    string State, bool IsOverdue, DateTimeOffset? CompletedAt, Guid? SourceProposalId);

public record SaveTaskRequest(string Title, string? Notes, DateTimeOffset? DueAt, bool AllDay, Guid? AssigneeUserId, Guid? ChildId);

public class SaveTaskRequestValidator : AbstractValidator<SaveTaskRequest>
{
    public SaveTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

[ApiController]
[Authorize(Policy = Policies.FamilyMember)]
[Route("api/v1/families/{familyId:guid}/tasks")]
public class TasksController(HallyDbContext db, IAnalytics analytics, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaskDto>>> List(Guid familyId, [FromQuery] string? state, [FromQuery] Guid? assignee,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.Tasks.AsNoTracking().Where(t => t.FamilyId == familyId);
        if (Enum.TryParse<TaskState>(state, true, out var s))
        {
            query = query.Where(t => t.State == s);
        }

        if (assignee is { } a)
        {
            query = query.Where(t => t.AssigneeUserId == a);
        }

        var items = await query.OrderBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenBy(t => t.CreatedAt)
            .Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var now = clock.GetUtcNow();
        return items.Select(t => ToDto(t, now)).ToList();
    }

    [HttpGet("{taskId:guid}")]
    public async Task<ActionResult<TaskDto>> Get(Guid familyId, Guid taskId, CancellationToken ct) =>
        ToDto(await Find(familyId, taskId, ct, track: false), clock.GetUtcNow());

    [HttpPost]
    [Idempotent]
    [ProducesResponseType<TaskDto>(201)]
    public async Task<IActionResult> Create(Guid familyId, SaveTaskRequest request, CancellationToken ct)
    {
        await EnsureRefs(familyId, request.AssigneeUserId, request.ChildId, ct);
        var userId = User.UserId();
        var task = new FamilyTask(familyId, userId, request.Title, request.Notes, request.DueAt, request.AllDay, request.AssigneeUserId, request.ChildId);
        db.Tasks.Add(task);
        Audit(familyId, userId, "task.created", task.Id);
        analytics.Track(AnalyticsEvents.TaskCreated, userId, familyId, new Dictionary<string, object?> { ["source"] = "manual" });
        await db.SaveChangesAsync(ct);
        return StatusCode(201, ToDto(task, clock.GetUtcNow()));
    }

    [HttpPut("{taskId:guid}")]
    public async Task<ActionResult<TaskDto>> Update(Guid familyId, Guid taskId, SaveTaskRequest request, CancellationToken ct)
    {
        await EnsureRefs(familyId, request.AssigneeUserId, request.ChildId, ct);
        var task = await Find(familyId, taskId, ct);
        task.Update(request.Title, request.Notes, request.DueAt, request.AllDay, request.ChildId);
        task.Assign(request.AssigneeUserId);
        Audit(familyId, User.UserId(), "task.updated", task.Id);
        await db.SaveChangesAsync(ct);
        return ToDto(task, clock.GetUtcNow());
    }

    [HttpPost("{taskId:guid}/complete")]
    [Idempotent]
    public async Task<ActionResult<TaskDto>> Complete(Guid familyId, Guid taskId, CancellationToken ct)
    {
        var task = await Find(familyId, taskId, ct);
        var userId = User.UserId();
        task.Complete(userId, clock.GetUtcNow());
        Audit(familyId, userId, "task.completed", task.Id);
        analytics.Track(AnalyticsEvents.TaskCompleted, userId, familyId);
        await db.SaveChangesAsync(ct);
        return ToDto(task, clock.GetUtcNow());
    }

    [HttpPost("{taskId:guid}/reopen")]
    [Idempotent]
    public async Task<ActionResult<TaskDto>> Reopen(Guid familyId, Guid taskId, CancellationToken ct)
    {
        var task = await Find(familyId, taskId, ct);
        task.Reopen();
        Audit(familyId, User.UserId(), "task.reopened", task.Id);
        await db.SaveChangesAsync(ct);
        return ToDto(task, clock.GetUtcNow());
    }

    [HttpPost("{taskId:guid}/cancel")]
    [Idempotent]
    public async Task<ActionResult<TaskDto>> Cancel(Guid familyId, Guid taskId, CancellationToken ct)
    {
        var task = await Find(familyId, taskId, ct);
        task.Cancel();
        Audit(familyId, User.UserId(), "task.cancelled", task.Id);
        await db.SaveChangesAsync(ct);
        return ToDto(task, clock.GetUtcNow());
    }

    private async Task<FamilyTask> Find(Guid familyId, Guid taskId, CancellationToken ct, bool track = true)
    {
        var q = track ? db.Tasks : db.Tasks.AsNoTracking();
        return await q.FirstOrDefaultAsync(t => t.Id == taskId && t.FamilyId == familyId, ct)
               ?? throw new NotFoundException("task.not_found", "Task not found.");
    }

    private async Task EnsureRefs(Guid familyId, Guid? assignee, Guid? child, CancellationToken ct)
    {
        if (assignee is { } a && !await db.FamilyMembers.AnyAsync(m => m.FamilyId == familyId && m.UserId == a, ct))
        {
            throw new Domain.Common.DomainException("task.assignee_not_member", "The assignee is not a member of this family.");
        }

        if (child is { } c && !await db.Children.AnyAsync(x => x.FamilyId == familyId && x.Id == c, ct))
        {
            throw new Domain.Common.DomainException("task.child_not_in_family", "The child does not belong to this family.");
        }
    }

    private void Audit(Guid familyId, Guid userId, string action, Guid id) =>
        db.AuditEntries.Add(new AuditEntry { FamilyId = familyId, ActorUserId = userId, Action = action, EntityType = "task", EntityId = id, At = clock.GetUtcNow() });

    public static TaskDto ToDto(FamilyTask t, DateTimeOffset now) =>
        new(t.Id, t.Title, t.Notes, t.DueAt, t.AllDay, t.AssigneeUserId, t.ChildId, t.State.ToString(), t.IsOverdue(now), t.CompletedAt, t.SourceProposalId);
}
