using Hally.Domain.Common;

namespace Hally.Domain.Families;

public class Family : Entity
{
    public const int MaxAdults = 6;

    private readonly List<FamilyMember> _members = [];
    private readonly List<Child> _children = [];

    private Family() { }

    public Family(string name, string timeZoneId, Guid ownerUserId)
    {
        Rename(name);
        SetTimeZone(timeZoneId);
        _members.Add(new FamilyMember(Id, ownerUserId, FamilyRole.Owner));
    }

    public string Name { get; private set; } = default!;
    public string TimeZoneId { get; private set; } = FamilyTimeZone.Default;
    public IReadOnlyCollection<FamilyMember> Members => _members;
    public IReadOnlyCollection<Child> Children => _children;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
        {
            throw new DomainException("family.name_invalid", "Family name must be 1-80 characters.");
        }

        Name = name.Trim();
    }

    public void SetTimeZone(string timeZoneId)
    {
        if (!FamilyTimeZone.IsValid(timeZoneId))
        {
            throw new DomainException("family.timezone_invalid", $"Unknown IANA time zone '{timeZoneId}'.");
        }

        TimeZoneId = timeZoneId;
    }

    public bool IsMember(Guid userId) => _members.Any(m => m.UserId == userId);

    public FamilyMember AddAdult(Guid userId)
    {
        var existing = _members.FirstOrDefault(m => m.UserId == userId);
        if (existing is not null)
        {
            return existing;
        }

        if (_members.Count >= MaxAdults)
        {
            throw new DomainException("family.adult_limit", $"A family can have at most {MaxAdults} adults.");
        }

        var member = new FamilyMember(Id, userId, FamilyRole.Adult);
        _members.Add(member);
        return member;
    }

    public Child AddChild(string firstName, int? birthYear)
    {
        var child = new Child(Id, firstName, birthYear);
        _children.Add(child);
        return child;
    }
}

public enum FamilyRole
{
    Owner = 1,
    Adult = 2,
}

public class FamilyMember : Entity
{
    private FamilyMember() { }

    internal FamilyMember(Guid familyId, Guid userId, FamilyRole role)
    {
        FamilyId = familyId;
        UserId = userId;
        Role = role;
    }

    public Guid FamilyId { get; private set; }
    public Guid UserId { get; private set; }
    public FamilyRole Role { get; private set; }
}

/// <summary>A child profile. Only a first name is kept (no surname) to minimise PII.</summary>
public class Child : Entity
{
    private Child() { }

    internal Child(Guid familyId, string firstName, int? birthYear)
    {
        if (string.IsNullOrWhiteSpace(firstName) || firstName.Trim().Length > 40)
        {
            throw new DomainException("child.name_invalid", "Child first name must be 1-40 characters.");
        }

        if (birthYear is { } y && (y < DateTime.UtcNow.Year - 25 || y > DateTime.UtcNow.Year))
        {
            throw new DomainException("child.birth_year_invalid", "Birth year is out of range.");
        }

        FamilyId = familyId;
        FirstName = firstName.Trim();
        BirthYear = birthYear;
    }

    public Guid FamilyId { get; private set; }
    public string FirstName { get; private set; } = default!;
    public int? BirthYear { get; private set; }
}
