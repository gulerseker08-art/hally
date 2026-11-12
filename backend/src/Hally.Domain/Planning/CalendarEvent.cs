using Hally.Domain.Common;

namespace Hally.Domain.Planning;

public class CalendarEvent : Entity
{
    private CalendarEvent() { }

    public CalendarEvent(Guid familyId, Guid createdByUserId, string title, DateTimeOffset startsAt, DateTimeOffset? endsAt,
        bool allDay, string? location, Guid? childId, Guid? sourceProposalId = null)
    {
        FamilyId = familyId;
        CreatedByUserId = createdByUserId;
        SourceProposalId = sourceProposalId;
        Update(title, startsAt, endsAt, allDay, location, childId);
    }

    public Guid FamilyId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string Title { get; private set; } = default!;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset? EndsAt { get; private set; }
    public bool AllDay { get; private set; }
    public string? Location { get; private set; }
    public Guid? ChildId { get; private set; }
    public Guid? SourceProposalId { get; private set; }

    public void Update(string title, DateTimeOffset startsAt, DateTimeOffset? endsAt, bool allDay, string? location, Guid? childId)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            throw new DomainException("event.title_invalid", "Event title must be 1-200 characters.");
        }

        if (endsAt is { } end && end < startsAt)
        {
            throw new DomainException("event.end_before_start", "Event end must be after its start.");
        }

        Title = title.Trim();
        StartsAt = startsAt.ToUniversalTime(); // PostgreSQL timestamptz accepts UTC only
        EndsAt = endsAt?.ToUniversalTime();
        AllDay = allDay;
        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        ChildId = childId;
    }

    public DateTimeOffset EffectiveEnd => EndsAt ?? (AllDay ? StartsAt.AddDays(1) : StartsAt.AddHours(1));

    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => StartsAt < end && start < EffectiveEnd;
}
