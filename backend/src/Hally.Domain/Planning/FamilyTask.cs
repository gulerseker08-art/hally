using Hally.Domain.Common;

namespace Hally.Domain.Planning;

public enum TaskState
{
    Open,
    Done,
    Cancelled,
}

/// <summary>A to-do for the family. Due time is stored in UTC; <see cref="AllDay"/> tasks use the
/// family-local date at 00:00 converted to UTC.</summary>
public class FamilyTask : Entity
{
    private FamilyTask() { }

    public FamilyTask(Guid familyId, Guid createdByUserId, string title, string? notes, DateTimeOffset? dueAt, bool allDay,
        Guid? assigneeUserId, Guid? childId, Guid? sourceProposalId = null)
    {
        FamilyId = familyId;
        CreatedByUserId = createdByUserId;
        SourceProposalId = sourceProposalId;
        Update(title, notes, dueAt, allDay, childId);
        AssigneeUserId = assigneeUserId;
    }

    public Guid FamilyId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Notes { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }
    public bool AllDay { get; private set; }
    public Guid? AssigneeUserId { get; private set; }
    public Guid? ChildId { get; private set; }
    public TaskState State { get; private set; } = TaskState.Open;
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public Guid? SourceProposalId { get; private set; }

    public void Update(string title, string? notes, DateTimeOffset? dueAt, bool allDay, Guid? childId)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            throw new DomainException("task.title_invalid", "Task title must be 1-200 characters.");
        }

        Title = title.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        DueAt = dueAt?.ToUniversalTime(); // PostgreSQL timestamptz accepts UTC only
        AllDay = dueAt is not null && allDay;
        ChildId = childId;
    }

    public void Assign(Guid? userId) => AssigneeUserId = userId;

    public void Complete(Guid userId, DateTimeOffset now)
    {
        if (State == TaskState.Cancelled)
        {
            throw new DomainException("task.cancelled", "A cancelled task cannot be completed.");
        }

        State = TaskState.Done;
        CompletedAt ??= now;
        CompletedByUserId ??= userId;
    }

    public void Reopen()
    {
        State = TaskState.Open;
        CompletedAt = null;
        CompletedByUserId = null;
    }

    public void Cancel() => State = TaskState.Cancelled;

    public bool IsOverdue(DateTimeOffset now) => State == TaskState.Open && DueAt is { } due && due < now;
}
