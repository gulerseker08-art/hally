using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Domain.Capture;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Proposals;

public record ProposalDto(Guid Id, Guid? CaptureId, string Kind, string Status, ProposalPayload Payload, DateTimeOffset ExpiresAt, Guid? ResultEntityId);

[ApiController]
[Authorize(Policy = Policies.FamilyMember)]
[Route("api/v1/families/{familyId:guid}/proposals")]
public class ProposalsController(HallyDbContext db, ProposalEngine engine, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProposalDto>>> List(Guid familyId, [FromQuery] string? status, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var query = db.Proposals.AsNoTracking().Where(p => p.FamilyId == familyId);
        if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.Status == ProposalStatus.Pending && p.ExpiresAt > now);
        }

        var items = await query.OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync(ct);
        return items.Select(p => ToDto(p, now)).ToList();
    }

    [HttpPost("{proposalId:guid}/approve")]
    [Idempotent]
    public async Task<ActionResult<ApprovalResult>> Approve(Guid familyId, Guid proposalId, [FromBody] ProposalEdits? edits, CancellationToken ct) =>
        Ok(await engine.ApproveAsync(familyId, proposalId, User.UserId(), edits, ct));

    [HttpPost("{proposalId:guid}/reject")]
    [Idempotent]
    public async Task<IActionResult> Reject(Guid familyId, Guid proposalId, CancellationToken ct)
    {
        await engine.RejectAsync(familyId, proposalId, User.UserId(), ct);
        return Ok(new { proposalId, status = "Rejected" });
    }

    public static ProposalDto ToDto(Proposal p, DateTimeOffset now) =>
        new(p.Id, p.CaptureId, p.Kind.ToString(), p.StatusAt(now).ToString(), ProposalPayload.FromJson(p.Payload), p.ExpiresAt, p.ResultEntityId);
}
