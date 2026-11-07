using FluentValidation;
using Hally.Api.Common;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Me;

public record FamilySummary(Guid Id, string Name, string Role);
public record MeResponse(Guid Id, string Email, string DisplayName, string Locale, IReadOnlyList<FamilySummary> Families);
public record UpdateProfileRequest(string DisplayName, string Locale);

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Locale).NotEmpty().Must(l => l is "tr" or "en").WithMessage("Locale must be 'tr' or 'en'.");
    }
}

[ApiController]
[Authorize]
[Route("api/v1/me")]
public class MeController(HallyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken ct) => await Load(User.UserId(), ct);

    [HttpPut]
    public async Task<ActionResult<MeResponse>> Update(UpdateProfileRequest request, CancellationToken ct)
    {
        var userId = User.UserId();
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.UpdateProfile(request.DisplayName, request.Locale);
        await db.SaveChangesAsync(ct);
        return await Load(userId, ct);
    }

    private async Task<MeResponse> Load(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new UnauthorizedAccessException();
        var families = await db.FamilyMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(db.Families, m => m.FamilyId, f => f.Id, (m, f) => new { f.Id, f.Name, m.Role })
            .ToListAsync(ct);
        return new MeResponse(user.Id, user.Email, user.DisplayName, user.Locale,
            families.Select(f => new FamilySummary(f.Id, f.Name, f.Role.ToString())).ToList());
    }
}
