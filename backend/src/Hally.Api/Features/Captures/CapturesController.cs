using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Api.Features.Proposals;
using Hally.Domain.Capture;
using Hally.Infrastructure.Ai;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Hally.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Captures;

public record CaptureDto(Guid Id, string Source, string Status, string? FailureCode, string? Summary, IReadOnlyList<ProposalDto> Proposals, int DuplicatesSkipped);

[ApiController]
[Authorize(Policy = Policies.FamilyMember)]
[Route("api/v1/families/{familyId:guid}/captures")]
public class CapturesController(
    HallyDbContext db,
    IDocumentStore store,
    ExtractionService extraction,
    IAnalytics analytics,
    TimeProvider clock,
    ILogger<CapturesController> logger) : ControllerBase
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    private static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp", "image/heic"];

    /// <summary>Uploads a photo/screenshot and/or text, runs AI extraction and returns proposals for review.
    /// Nothing is created in tasks or calendar until a proposal is approved.</summary>
    [HttpPost]
    [Idempotent]
    [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
    [ProducesResponseType<CaptureDto>(201)]
    public async Task<IActionResult> Create(Guid familyId, [FromForm] string? text, [FromForm] CaptureSource source, IFormFile? file, CancellationToken ct)
    {
        if (file is null && string.IsNullOrWhiteSpace(text))
        {
            return Problem(statusCode: 400, title: "Send a file or text.", type: "https://hally.app/problems/capture.empty");
        }

        if (text?.Length > 20000)
        {
            return Problem(statusCode: 400, title: "Text is too long (max 20000 characters).", type: "https://hally.app/problems/capture.text_too_long");
        }

        byte[]? bytes = null;
        if (file is not null)
        {
            if (file.Length == 0 || file.Length > MaxFileBytes || !ImageTypes.Contains(file.ContentType))
            {
                return Problem(statusCode: 400, title: "File must be a JPEG, PNG, WebP or HEIC image up to 10 MB.", type: "https://hally.app/problems/capture.file_invalid");
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var userId = User.UserId();
        var storageKey = bytes is null ? null : await store.SaveAsync(familyId, bytes, ct);
        var capture = new Capture(familyId, userId, source, text, storageKey, file?.ContentType, clock.GetUtcNow());
        db.Captures.Add(capture);
        analytics.Track(AnalyticsEvents.CaptureStarted, userId, familyId, new Dictionary<string, object?> { ["source"] = source.ToString() });
        await db.SaveChangesAsync(ct);

        var images = bytes is null ? [] : new List<ImagePart> { new(bytes, file!.ContentType) };
        return StatusCode(201, await RunExtraction(capture, images, ct));
    }

    [HttpGet("{captureId:guid}")]
    public async Task<ActionResult<CaptureDto>> Get(Guid familyId, Guid captureId, CancellationToken ct)
    {
        var capture = await db.Captures.AsNoTracking().FirstOrDefaultAsync(c => c.Id == captureId && c.FamilyId == familyId, ct)
                      ?? throw new NotFoundException("capture.not_found", "Capture not found.");
        return await ToDto(capture, 0, ct);
    }

    /// <summary>Retries extraction for a capture that failed (e.g. AI provider unavailable).</summary>
    [HttpPost("{captureId:guid}/retry")]
    [Idempotent]
    public async Task<ActionResult<CaptureDto>> Retry(Guid familyId, Guid captureId, CancellationToken ct)
    {
        var capture = await db.Captures.FirstOrDefaultAsync(c => c.Id == captureId && c.FamilyId == familyId, ct)
                      ?? throw new NotFoundException("capture.not_found", "Capture not found.");
        if (capture.Status != CaptureStatus.Failed)
        {
            return await ToDto(capture, 0, ct);
        }

        var images = new List<ImagePart>();
        if (capture.StorageKey is not null && await store.ReadAsync(capture.StorageKey, ct) is { } bytes)
        {
            images.Add(new ImagePart(bytes, capture.ContentType ?? "image/jpeg"));
        }

        return await RunExtraction(capture, images, ct);
    }

    private async Task<CaptureDto> RunExtraction(Capture capture, IReadOnlyList<ImagePart> images, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var family = await db.Families.AsNoTracking().FirstAsync(f => f.Id == capture.FamilyId, ct);
        var children = await db.Children.AsNoTracking().Where(c => c.FamilyId == capture.FamilyId).ToListAsync(ct);
        var locale = await db.Users.Where(u => u.Id == capture.CreatedByUserId).Select(u => u.Locale).FirstAsync(ct);
        var context = FamilyContextFactory.Create(family, children, locale, now);

        ExtractionResult result;
        try
        {
            result = await extraction.ExtractAsync(context, capture.Text, images, ct);
        }
        catch (AiUnavailableException ex)
        {
            logger.LogWarning(ex, "Extraction failed for capture {CaptureId}", capture.Id);
            capture.MarkFailed("ai.unavailable");
            await db.SaveChangesAsync(ct);
            return await ToDto(capture, 0, ct);
        }

        // Duplicate prevention across captures: skip items already pending or approved in this family.
        var fingerprints = result.Drafts.Select(d => ProposalPayload.FingerprintOf(d.Kind, d.Payload.Title, d.Payload.Date)).ToList();
        var existing = await db.Proposals.AsNoTracking()
            .Where(p => p.FamilyId == capture.FamilyId && fingerprints.Contains(p.Fingerprint)
                        && (p.Status == ProposalStatus.Approved || (p.Status == ProposalStatus.Pending && p.ExpiresAt > now)))
            .Select(p => p.Fingerprint).ToListAsync(ct);

        var skipped = 0;
        var order = 0;
        foreach (var (draft, fingerprint) in result.Drafts.Zip(fingerprints))
        {
            if (existing.Contains(fingerprint))
            {
                skipped++;
                continue;
            }

            db.Proposals.Add(new Proposal(capture.FamilyId, capture.Id, draft.Kind, draft.Payload.ToJson(), fingerprint, now.AddTicks(order++)));
        }

        capture.MarkExtracted(result.Summary);
        analytics.Track(AnalyticsEvents.DocumentScanned, capture.CreatedByUserId, capture.FamilyId,
            new Dictionary<string, object?> { ["type"] = images.Count > 0 ? "image" : "text" });
        analytics.Track(AnalyticsEvents.ProposalShown, capture.CreatedByUserId, capture.FamilyId,
            new Dictionary<string, object?> { ["count"] = result.Drafts.Count - skipped });
        await db.SaveChangesAsync(ct);
        return await ToDto(capture, skipped, ct);
    }

    private async Task<CaptureDto> ToDto(Capture capture, int skipped, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var proposals = await db.Proposals.AsNoTracking().Where(p => p.CaptureId == capture.Id).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        return new CaptureDto(capture.Id, capture.Source.ToString(), capture.Status.ToString(), capture.FailureCode, capture.Summary,
            proposals.Select(p => ProposalsController.ToDto(p, now)).ToList(), skipped);
    }
}
