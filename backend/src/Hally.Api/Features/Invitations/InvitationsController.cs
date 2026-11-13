using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Domain.Families;
using Hally.Domain.Users;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hally.Api.Features.Invitations;

public record InvitationCreatedDto(Guid Id, string Token, string Url, string ShareText, string WhatsAppUrl, DateTimeOffset ExpiresAt);
public record InvitationPreviewDto(string FamilyName, string InvitedBy, string Status, DateTimeOffset ExpiresAt);
public record InvitationAcceptedDto(Guid FamilyId, string FamilyName);

[ApiController]
[Authorize]
public class InvitationsController(
    HallyDbContext db,
    IAnalytics analytics,
    IOptions<AppOptions> app,
    Billing.EntitlementService entitlements,
    TimeProvider clock) : ControllerBase
{
    /// <summary>Creates a 7-day invitation link for another adult, with a prefilled WhatsApp message.</summary>
    [HttpPost("api/v1/families/{familyId:guid}/invitations")]
    [Idempotent]
    [Authorize(Policy = Policies.FamilyMember)]
    [ProducesResponseType<InvitationCreatedDto>(201)]
    public async Task<IActionResult> Create(Guid familyId, CancellationToken ct)
    {
        await entitlements.EnsureCanAddAdultAsync(familyId, ct);
        var userId = User.UserId();
        var inviter = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        var token = Crypto.NewToken();
        var invitation = new Invitation(familyId, userId, Crypto.Sha256(token), clock.GetUtcNow());
        db.Invitations.Add(invitation);
        analytics.Track(AnalyticsEvents.InvitationCreated, userId, familyId);
        await db.SaveChangesAsync(ct);

        var url = $"{app.Value.PublicBaseUrl.TrimEnd('/')}/i/{token}";
        var text = ShareText(inviter, app.Value.Name, url);
        return StatusCode(201, new InvitationCreatedDto(invitation.Id, token, url, text,
            $"https://wa.me/?text={Uri.EscapeDataString(text)}", invitation.ExpiresAt));
    }

    /// <summary>Public preview shown on the invite landing page and in the app before accepting.</summary>
    [HttpGet("api/v1/invitations/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<InvitationPreviewDto>> Preview(string token, CancellationToken ct)
    {
        var invitation = await Find(token, ct);
        var inviter = await db.Users.AsNoTracking().Where(u => u.Id == invitation.InvitedByUserId)
            .Select(u => u.DisplayName).FirstAsync(ct);
        return new InvitationPreviewDto(invitation.Family!.Name, inviter,
            invitation.StatusAt(clock.GetUtcNow()).ToString().ToLowerInvariant(), invitation.ExpiresAt);
    }

    [HttpPost("api/v1/invitations/{token}/accept")]
    [Idempotent]
    [ProducesResponseType<InvitationAcceptedDto>(200)]
    public async Task<IActionResult> Accept(string token, CancellationToken ct)
    {
        var userId = User.UserId();
        var invitation = await Find(token, ct, track: true);
        var family = await db.Families.Include(f => f.Members).FirstAsync(f => f.Id == invitation.FamilyId, ct);
        var wasMember = family.IsMember(userId);
        if (!wasMember && invitation.StatusAt(clock.GetUtcNow()) == InvitationStatus.Pending)
        {
            // Checked after the invitation itself so an expired or used link still says so.
            await entitlements.EnsureCanAddAdultAsync(family.Id, ct);
        }

        invitation.Accept(family, userId, clock.GetUtcNow());
        if (!wasMember)
        {
            analytics.Track(AnalyticsEvents.InvitationAccepted, userId, family.Id);
        }

        await db.SaveChangesAsync(ct);
        return Ok(new InvitationAcceptedDto(family.Id, family.Name));
    }

    private async Task<Invitation> Find(string token, CancellationToken ct, bool track = false)
    {
        var hash = Crypto.Sha256(token);
        var query = db.Invitations.Include(i => i.Family).AsQueryable();
        if (!track)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(i => i.TokenHash == hash, ct)
               ?? throw new NotFoundException("invitation.not_found", "Invitation not found.");
    }

    private static string ShareText(User inviter, string appName, string url) => inviter.Locale == Locales.English
        ? $"{inviter.DisplayName} invited you to share family planning on {appName}. Join here: {url}"
        : $"{inviter.DisplayName} seni {appName}'de aile planlamasını paylaşmaya davet etti. Buradan katıl: {url}";
}
