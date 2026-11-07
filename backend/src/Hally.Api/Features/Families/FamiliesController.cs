using FluentValidation;
using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Domain.Common;
using Hally.Domain.Families;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Families;

public record CreateFamilyRequest(string Name, string? TimeZoneId);
public record AddChildRequest(string FirstName, int? BirthYear);
public record MemberDto(Guid UserId, string DisplayName, string Role);
public record ChildDto(Guid Id, string FirstName, int? BirthYear);
public record FamilyDto(Guid Id, string Name, string TimeZoneId, IReadOnlyList<MemberDto> Members, IReadOnlyList<ChildDto> Children);

public class CreateFamilyRequestValidator : AbstractValidator<CreateFamilyRequest>
{
    public CreateFamilyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.TimeZoneId).Must(tz => tz is null || FamilyTimeZone.IsValid(tz))
            .WithMessage("TimeZoneId must be a valid IANA time zone, e.g. Europe/Istanbul.");
    }
}

public class AddChildRequestValidator : AbstractValidator<AddChildRequest>
{
    public AddChildRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(40);
        RuleFor(x => x.BirthYear).InclusiveBetween(DateTime.UtcNow.Year - 25, DateTime.UtcNow.Year).When(x => x.BirthYear.HasValue);
    }
}

[ApiController]
[Authorize]
[Route("api/v1/families")]
public class FamiliesController(HallyDbContext db, IAnalytics analytics) : ControllerBase
{
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<FamilyDto>(201)]
    public async Task<IActionResult> Create(CreateFamilyRequest request, CancellationToken ct)
    {
        var userId = User.UserId();
        var family = new Family(request.Name, request.TimeZoneId ?? FamilyTimeZone.Default, userId);
        db.Families.Add(family);
        analytics.Track(AnalyticsEvents.FamilyCreated, userId, family.Id,
            new Dictionary<string, object?> { ["time_zone"] = family.TimeZoneId });
        await db.SaveChangesAsync(ct);

        var dto = await LoadDto(family.Id, ct);
        return CreatedAtAction(nameof(Get), new { familyId = family.Id }, dto);
    }

    [HttpGet("{familyId:guid}")]
    [Authorize(Policy = Policies.FamilyMember)]
    public async Task<ActionResult<FamilyDto>> Get(Guid familyId, CancellationToken ct) => await LoadDto(familyId, ct);

    [HttpGet("{familyId:guid}/members")]
    [Authorize(Policy = Policies.FamilyMember)]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> Members(Guid familyId, CancellationToken ct) =>
        Ok((await LoadDto(familyId, ct)).Members);

    [HttpPost("{familyId:guid}/children")]
    [Idempotent]
    [Authorize(Policy = Policies.FamilyMember)]
    [ProducesResponseType<ChildDto>(201)]
    public async Task<IActionResult> AddChild(Guid familyId, AddChildRequest request, CancellationToken ct)
    {
        var family = await db.Families.Include(f => f.Children).FirstAsync(f => f.Id == familyId, ct);
        var child = family.AddChild(request.FirstName, request.BirthYear);
        analytics.Track(AnalyticsEvents.ChildAdded, User.UserId(), familyId);
        await db.SaveChangesAsync(ct);
        return StatusCode(201, new ChildDto(child.Id, child.FirstName, child.BirthYear));
    }

    private async Task<FamilyDto> LoadDto(Guid familyId, CancellationToken ct)
    {
        var family = await db.Families.AsNoTracking().Include(f => f.Children)
                         .FirstOrDefaultAsync(f => f.Id == familyId, ct)
                     ?? throw new NotFoundException("family.not_found", "Family not found.");
        var members = await db.FamilyMembers.AsNoTracking()
            .Where(m => m.FamilyId == familyId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.UserId, u.DisplayName, m.Role, m.CreatedAt })
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return new FamilyDto(family.Id, family.Name, family.TimeZoneId,
            members.Select(m => new MemberDto(m.UserId, m.DisplayName, m.Role.ToString())).ToList(),
            family.Children.OrderBy(c => c.CreatedAt).Select(c => new ChildDto(c.Id, c.FirstName, c.BirthYear)).ToList());
    }
}
