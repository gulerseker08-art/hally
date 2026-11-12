using Hally.Api.Features.Captures;
using Hally.Api.Features.Proposals;
using Hally.Domain.Common;
using Hally.Domain.Users;
using Hally.Infrastructure.Ai;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hally.Api.Features.Demo;

public record DemoItemDto(string Kind, ProposalPayload Payload);
public record DemoScanDto(string? Summary, IReadOnlyList<DemoItemDto> Items);

/// <summary>
/// Pre-signup "aha" moment: scan the bundled sample (or your own photo) and see proposals before creating
/// an account. Nothing is stored: no capture, no proposal, no file. Rate limited per client IP.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/demo")]
[EnableRateLimiting(DemoController.RateLimitPolicy)]
public class DemoController(ExtractionService extraction, IAnalytics analytics, HallyDbContext db, TimeProvider clock) : ControllerBase
{
    public const string RateLimitPolicy = "demo";
    private static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp", "image/heic"];

    [HttpPost("scan")]
    [RequestSizeLimit(CapturesController.MaxFileBytes + 64 * 1024)]
    [ProducesResponseType<DemoScanDto>(200)]
    public async Task<IActionResult> Scan([FromForm] string? sampleId, [FromForm] string? locale, [FromForm] string? timeZoneId, IFormFile? file, CancellationToken ct)
    {
        var text = DemoSamples.Get(sampleId);
        var images = new List<ImagePart>();
        if (file is not null)
        {
            if (file.Length == 0 || file.Length > CapturesController.MaxFileBytes || !ImageTypes.Contains(file.ContentType))
            {
                return Problem(statusCode: 400, title: "File must be a JPEG, PNG, WebP or HEIC image up to 10 MB.", type: "https://hally.app/problems/capture.file_invalid");
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            images.Add(new ImagePart(ms.ToArray(), file.ContentType));
        }

        if (text is null && images.Count == 0)
        {
            return Problem(statusCode: 400, title: "Send a known sampleId or an image.", type: "https://hally.app/problems/demo.empty");
        }

        var zone = FamilyTimeZone.IsValid(timeZoneId) ? timeZoneId! : FamilyTimeZone.Default;
        var today = DateOnly.FromDateTime(FamilyTimeZone.ToFamilyTime(clock.GetUtcNow(), zone).DateTime);
        var context = new FamilyContext(zone, today, Locales.Normalize(locale), []);
        var result = await extraction.ExtractAsync(context, text, images, ct); // AiUnavailableException → 503 problem

        analytics.Track(AnalyticsEvents.OnboardingDemoScanned, null, null,
            new Dictionary<string, object?> { ["source"] = images.Count > 0 ? "photo" : "sample", ["count"] = result.Drafts.Count });
        await db.SaveChangesAsync(ct); // only the anonymous analytics event is persisted
        return Ok(new DemoScanDto(result.Summary, result.Drafts.Select(d => new DemoItemDto(d.Kind.ToString(), d.Payload)).ToList()));
    }
}
