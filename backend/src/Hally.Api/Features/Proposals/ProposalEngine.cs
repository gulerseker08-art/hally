using System.Globalization;
using System.Text.Json;
using Hally.Api.Common;
using Hally.Domain.Capture;
using Hally.Domain.Common;
using Hally.Domain.Planning;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Proposals;

public record ProposalEdits(
    string? Title,
    string? Notes,
    string? Date,
    string? Time,
    string? EndTime,
    string? Location,
    Guid? ChildId,
    Guid? AssigneeUserId,
    bool ClearTime = false,
    bool ClearChild = false);

public record ApprovalResult(Guid ProposalId, ProposalKind Kind, Guid EntityId);

public class ConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Executes approved proposals: validate → re-check authorization of every referenced id → write the
/// task/event, the proposal decision and the audit entry in one SaveChanges (one transaction).
/// The proposal row version and the unique SourceProposalId index stop double execution.
/// </summary>
public class ProposalEngine(HallyDbContext db, IAnalytics analytics, TimeProvider clock)
{
    public async Task<ApprovalResult> ApproveAsync(Guid familyId, Guid proposalId, Guid userId, ProposalEdits? edits, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var proposal = await db.Proposals.FirstOrDefaultAsync(p => p.Id == proposalId && p.FamilyId == familyId, ct)
                       ?? throw new NotFoundException("proposal.not_found", "Proposal not found.");
        proposal.EnsurePending(now);

        var family = await db.Families.AsNoTracking().FirstAsync(f => f.Id == familyId, ct);
        var payload = Apply(ProposalPayload.FromJson(proposal.Payload), edits);
        await EnsureReferencesBelongToFamily(familyId, payload, ct);

        var duplicate = await db.Proposals.AnyAsync(p => p.FamilyId == familyId && p.Id != proposal.Id
                                                         && p.Status == ProposalStatus.Approved
                                                         && p.Fingerprint == ProposalPayload.FingerprintOf(proposal.Kind, payload.Title, payload.Date), ct);
        if (duplicate)
        {
            throw new ConflictException("proposal.duplicate", "An identical item was already approved.");
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(family.TimeZoneId);
        Guid entityId;
        if (proposal.Kind == ProposalKind.CreateTask)
        {
            var due = ToUtc(payload.Date, payload.Time, zone);
            var task = new FamilyTask(familyId, userId, payload.Title, payload.Notes, due, payload.Time is null, payload.AssigneeUserId, payload.ChildId, proposal.Id);
            db.Tasks.Add(task);
            entityId = task.Id;
        }
        else
        {
            var start = ToUtc(payload.Date, payload.Time, zone)
                        ?? throw new DomainException("proposal.date_required", "An event needs a date before it can be approved.");
            var end = payload.EndTime is null ? (DateTimeOffset?)null : ToUtc(payload.Date, payload.EndTime, zone);
            var evt = new CalendarEvent(familyId, userId, payload.Title, start, end, payload.Time is null, payload.Location, payload.ChildId, proposal.Id);
            db.Events.Add(evt);
            entityId = evt.Id;
        }

        var finalPayload = payload with { NeedsInfo = [] };
        proposal.Approve(userId, finalPayload.ToJson(), entityId, now);
        db.AuditEntries.Add(new AuditEntry
        {
            FamilyId = familyId,
            ActorUserId = userId,
            Action = "proposal.approved",
            EntityType = proposal.Kind == ProposalKind.CreateTask ? "task" : "event",
            EntityId = entityId,
            Data = JsonSerializer.Serialize(new { proposalId = proposal.Id, edited = edits is not null }),
            At = now,
        });
        analytics.Track(AnalyticsEvents.ProposalApproved, userId, familyId,
            new Dictionary<string, object?> { ["kind"] = proposal.Kind.ToString(), ["edited"] = edits is not null, ["count"] = 1 });

        await SaveOrConflict(ct);
        return new ApprovalResult(proposal.Id, proposal.Kind, entityId);
    }

    public async Task RejectAsync(Guid familyId, Guid proposalId, Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var proposal = await db.Proposals.FirstOrDefaultAsync(p => p.Id == proposalId && p.FamilyId == familyId, ct)
                       ?? throw new NotFoundException("proposal.not_found", "Proposal not found.");
        proposal.Reject(userId, now);
        db.AuditEntries.Add(new AuditEntry
        {
            FamilyId = familyId,
            ActorUserId = userId,
            Action = "proposal.rejected",
            EntityType = "proposal",
            EntityId = proposal.Id,
            At = now,
        });
        analytics.Track(AnalyticsEvents.ProposalRejected, userId, familyId);
        await SaveOrConflict(ct);
    }

    private async Task SaveOrConflict(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw new ConflictException("proposal.conflict", "This proposal was already decided.");
        }
    }

    private async Task EnsureReferencesBelongToFamily(Guid familyId, ProposalPayload payload, CancellationToken ct)
    {
        if (payload.AssigneeUserId is { } assignee && !await db.FamilyMembers.AnyAsync(m => m.FamilyId == familyId && m.UserId == assignee, ct))
        {
            throw new DomainException("proposal.assignee_not_member", "The assignee is not a member of this family.");
        }

        if (payload.ChildId is { } child && !await db.Children.AnyAsync(c => c.FamilyId == familyId && c.Id == child, ct))
        {
            throw new DomainException("proposal.child_not_in_family", "The child does not belong to this family.");
        }
    }

    private static ProposalPayload Apply(ProposalPayload p, ProposalEdits? e)
    {
        if (e is null)
        {
            return p;
        }

        p = p with
        {
            Title = string.IsNullOrWhiteSpace(e.Title) ? p.Title : e.Title.Trim(),
            Notes = e.Notes ?? p.Notes,
            Date = e.Date ?? p.Date,
            Time = e.ClearTime ? null : e.Time ?? p.Time,
            EndTime = e.ClearTime ? null : e.EndTime ?? p.EndTime,
            Location = e.Location ?? p.Location,
            ChildId = e.ClearChild ? null : e.ChildId ?? p.ChildId,
            AssigneeUserId = e.AssigneeUserId ?? p.AssigneeUserId,
        };

        if (p.Date is not null && !DateOnly.TryParseExact(p.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new DomainException("proposal.date_invalid", "Date must be yyyy-MM-dd.");
        }

        foreach (var t in new[] { p.Time, p.EndTime })
        {
            if (t is not null && !TimeOnly.TryParseExact(t, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new DomainException("proposal.time_invalid", "Time must be HH:mm.");
            }
        }

        return p;
    }

    /// <summary>Family-local date (+ optional time) to UTC. All-day items use local midnight.</summary>
    public static DateTimeOffset? ToUtc(string? date, string? time, TimeZoneInfo zone)
    {
        if (date is null)
        {
            return null;
        }

        var d = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var t = time is null ? TimeOnly.MinValue : TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture);
        var local = d.ToDateTime(t, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1); // skipped by a DST jump: move to the first valid time
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
