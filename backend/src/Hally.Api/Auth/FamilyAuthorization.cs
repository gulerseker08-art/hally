using Hally.Api.Common;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Auth;

public static class Policies
{
    /// <summary>Caller must be a member of the family named by the {familyId} route value.</summary>
    public const string FamilyMember = "family-member";
}

public class FamilyMemberRequirement : IAuthorizationRequirement;

/// <summary>Enforces family isolation: every family-scoped route is checked against membership.</summary>
public class FamilyMemberHandler(HallyDbContext db, IHttpContextAccessor http) : AuthorizationHandler<FamilyMemberRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, FamilyMemberRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || http.HttpContext is not { } ctx)
        {
            return;
        }

        if (!Guid.TryParse(ctx.GetRouteValue("familyId")?.ToString(), out var familyId))
        {
            return;
        }

        var userId = context.User.UserId();
        if (await db.FamilyMembers.AnyAsync(m => m.FamilyId == familyId && m.UserId == userId, ctx.RequestAborted))
        {
            context.Succeed(requirement);
        }
    }
}
