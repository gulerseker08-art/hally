using Hally.Domain.Common;

namespace Hally.Domain.Capture;

public enum ProposalKind
{
    CreateTask,
    CreateEvent,
}

public enum ProposalStatus
{
    Pending,
    Approved,
    Rejected,
    Expired,
}

/// <summary>
/// An action suggested by the AI. Nothing is written to tasks or calendar until a family member approves it.
/// <see cref="Payload"/> is the validated JSON of the action; <see cref="Fingerprint"/> prevents duplicates.
/// </summary>
public class Proposal : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private Proposal() { }

    public Proposal(Guid familyId, Guid? captureId, ProposalKind kind, string payload, string fingerprint, DateTimeOffset now)
    {
        FamilyId = familyId;
        CaptureId = captureId;
        Kind = kind;
        Payload = payload;
        Fingerprint = fingerprint;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
    }

    public Guid FamilyId { get; private set; }
    public Guid? CaptureId { get; private set; }
    public ProposalKind Kind { get; private set; }
    public string Payload { get; private set; } = default!;
    public string Fingerprint { get; private set; } = default!;
    public ProposalStatus Status { get; private set; } = ProposalStatus.Pending;
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? ResultEntityId { get; private set; }
    public uint Version { get; private set; }

    public ProposalStatus StatusAt(DateTimeOffset now) =>
        Status == ProposalStatus.Pending && ExpiresAt <= now ? ProposalStatus.Expired : Status;

    public void EnsurePending(DateTimeOffset now)
    {
        var status = StatusAt(now);
        if (status != ProposalStatus.Pending)
        {
            throw new DomainException($"proposal.{status.ToString().ToLowerInvariant()}", $"Proposal is already {status}.");
        }
    }

    public void Approve(Guid userId, string finalPayload, Guid resultEntityId, DateTimeOffset now)
    {
        EnsurePending(now);
        Status = ProposalStatus.Approved;
        Payload = finalPayload;
        DecidedByUserId = userId;
        DecidedAt = now;
        ResultEntityId = resultEntityId;
    }

    public void Reject(Guid userId, DateTimeOffset now)
    {
        EnsurePending(now);
        Status = ProposalStatus.Rejected;
        DecidedByUserId = userId;
        DecidedAt = now;
    }
}
