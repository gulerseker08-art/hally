using System.Text.Json;
using Hally.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Hally.Infrastructure.Analytics;

/// <summary>Product analytics abstraction. Events are family-scoped and must never carry PII
/// (no names, emails, child names or free text). A vendor sink can be added behind this interface.</summary>
public interface IAnalytics
{
    void Track(string name, Guid? userId, Guid? familyId, IReadOnlyDictionary<string, object?>? properties = null);
}

public static class AnalyticsEvents
{
    public const string SignedIn = "auth_signed_in";
    public const string SignedUp = "auth_signed_up";
    public const string FamilyCreated = "family_created";
    public const string InvitationCreated = "invitation_created";
    public const string InvitationAccepted = "invitation_accepted";
    public const string ChildAdded = "child_added";
    public const string OnboardingDemoScanned = "onboarding_demo_scanned";
    public const string CaptureStarted = "capture_started";
    public const string DocumentScanned = "document_scanned";
    public const string ProposalShown = "proposal_shown";
    public const string ProposalApproved = "proposal_approved";
    public const string ProposalRejected = "proposal_rejected";
    public const string TaskCreated = "task_created";
    public const string TaskCompleted = "task_completed";
    public const string EventCreated = "event_created";
}

public class AnalyticsEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = default!;
    public Guid? UserId { get; set; }
    public Guid? FamilyId { get; set; }
    public string? Properties { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Writes events to the same unit of work so they commit with the business change.</summary>
public class DbAnalytics(HallyDbContext db, ILogger<DbAnalytics> logger) : IAnalytics
{
    public void Track(string name, Guid? userId, Guid? familyId, IReadOnlyDictionary<string, object?>? properties = null)
    {
        db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            Name = name,
            UserId = userId,
            FamilyId = familyId,
            Properties = properties is null ? null : JsonSerializer.Serialize(properties),
        });
        logger.LogInformation("analytics {Event} family={FamilyId}", name, familyId);
    }
}
