using FluentAssertions;
using Hally.Domain.Common;
using Hally.Domain.Families;

namespace Hally.Api.Tests;

public class DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Europe/Istanbul", true)]
    [InlineData("Europe/London", true)]
    [InlineData("America/New_York", true)]
    [InlineData("UTC", true)]
    [InlineData("Turkey Standard Time", false)]
    [InlineData("Nowhere/City", false)]
    public void Time_zone_validation(string id, bool valid) => FamilyTimeZone.IsValid(id).Should().Be(valid);

    [Fact]
    public void Istanbul_has_no_dst_and_is_utc_plus_3()
    {
        var summer = FamilyTimeZone.ToFamilyTime(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero), "Europe/Istanbul");
        var winter = FamilyTimeZone.ToFamilyTime(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero), "Europe/Istanbul");
        summer.Offset.Should().Be(TimeSpan.FromHours(3));
        winter.Offset.Should().Be(TimeSpan.FromHours(3));
    }

    [Fact]
    public void London_switches_offset_across_dst()
    {
        FamilyTimeZone.ToFamilyTime(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero), "Europe/London").Offset.Should().Be(TimeSpan.FromHours(1));
        FamilyTimeZone.ToFamilyTime(new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero), "Europe/London").Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Creator_becomes_owner()
    {
        var owner = Guid.NewGuid();
        var family = new Family("Aile", "Europe/Istanbul", owner);
        family.Members.Should().ContainSingle(m => m.UserId == owner && m.Role == FamilyRole.Owner);
    }

    [Fact]
    public void Adult_limit_is_enforced()
    {
        var family = new Family("Aile", "Europe/Istanbul", Guid.NewGuid());
        for (var i = 1; i < Family.MaxAdults; i++)
        {
            family.AddAdult(Guid.NewGuid());
        }

        var act = () => family.AddAdult(Guid.NewGuid());
        act.Should().Throw<DomainException>().Which.Code.Should().Be("family.adult_limit");
    }

    [Fact]
    public void Invitation_status_transitions()
    {
        var family = new Family("Aile", "Europe/Istanbul", Guid.NewGuid());
        var invitation = new Invitation(family.Id, family.Members.First().UserId, "hash", Now);

        invitation.StatusAt(Now).Should().Be(InvitationStatus.Pending);
        invitation.StatusAt(Now.AddDays(7)).Should().Be(InvitationStatus.Expired);

        var partner = Guid.NewGuid();
        invitation.Accept(family, partner, Now.AddHours(1));
        invitation.StatusAt(Now.AddDays(30)).Should().Be(InvitationStatus.Accepted);
        family.IsMember(partner).Should().BeTrue();

        // Accepting again as an existing member is a no-op, not an error.
        invitation.Accept(family, partner, Now.AddHours(2));
    }

    [Fact]
    public void Revoked_invitation_cannot_be_accepted()
    {
        var family = new Family("Aile", "Europe/Istanbul", Guid.NewGuid());
        var invitation = new Invitation(family.Id, Guid.NewGuid(), "hash", Now);
        invitation.Revoke(Now);
        var act = () => invitation.Accept(family, Guid.NewGuid(), Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("invitation.revoked");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Child_requires_first_name(string name)
    {
        var family = new Family("Aile", "Europe/Istanbul", Guid.NewGuid());
        var act = () => family.AddChild(name, null);
        act.Should().Throw<DomainException>();
    }
}
