using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Infrastructure.Ai;
using Hally.Infrastructure.Persistence;
using Hally.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hally.Api.Features.Privacy;

/// <summary>Facts the privacy screen shows. Statements about the AI provider are configuration, because they are
/// only true for some providers and contracts: the app must not claim "never used for training" unless it is.</summary>
public class PrivacyOptions
{
    public const string Section = "Privacy";

    /// <summary>Set to true only when the configured AI provider contractually does not train on API data
    /// (and, ideally, has zero data retention). The app then shows "never used to train AI models".</summary>
    public bool AiProviderNoTraining { get; set; }

    /// <summary>Shown as the AI provider name on the privacy screen, e.g. "OpenAI". Empty = not shown.</summary>
    public string AiProviderName { get; set; } = string.Empty;

    public string ContactEmail { get; set; } = string.Empty;
    public string PolicyUrl { get; set; } = string.Empty;
}

public record PrivacyInfoDto(
    bool AiConfigured,
    bool AiProviderNoTraining,
    string? AiProviderName,
    int OriginalsRetentionDays,
    string? ContactEmail,
    string? PolicyUrl);

[ApiController]
[Authorize]
public class PrivacyController(
    HallyDbContext db,
    AccountService accounts,
    IOptions<PrivacyOptions> privacy,
    IOptions<StorageOptions> storage,
    IOptions<AiOptions> ai,
    TimeProvider clock) : ControllerBase
{
    private static readonly JsonSerializerOptions ExportJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // readable Turkish in a file meant for people
        Converters = { new JsonStringEnumConverter() },
    };

    [HttpGet("api/v1/privacy")]
    [AllowAnonymous]
    public PrivacyInfoDto Info()
    {
        var p = privacy.Value;
        return new PrivacyInfoDto(
            ai.Value.IsConfigured,
            p.AiProviderNoTraining,
            string.IsNullOrWhiteSpace(p.AiProviderName) ? null : p.AiProviderName,
            Math.Max(storage.Value.RetentionDays, 1),
            string.IsNullOrWhiteSpace(p.ContactEmail) ? null : p.ContactEmail,
            string.IsNullOrWhiteSpace(p.PolicyUrl) ? null : p.PolicyUrl);
    }

    /// <summary>Everything stored about the caller and the families they belong to, as a JSON file (data portability).
    /// Photos are not included: originals are encrypted and deleted after the retention period; extracted data is.</summary>
    [HttpGet("api/v1/me/export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var userId = User.UserId();
        var user = await db.Users.AsNoTracking().Include(u => u.Logins).FirstAsync(u => u.Id == userId, ct);
        var familyIds = await db.FamilyMembers.Where(m => m.UserId == userId).Select(m => m.FamilyId).ToListAsync(ct);

        var families = new List<object>();
        foreach (var familyId in familyIds)
        {
            var family = await db.Families.AsNoTracking().Include(f => f.Members).Include(f => f.Children).FirstAsync(f => f.Id == familyId, ct);
            var memberIds = family.Members.Select(m => m.UserId).ToList();
            var names = await db.Users.AsNoTracking().Where(u => memberIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
            families.Add(new
            {
                family.Id,
                family.Name,
                family.TimeZoneId,
                family.CreatedAt,
                Members = family.Members.Select(m => new { Name = names.GetValueOrDefault(m.UserId), m.Role, Me = m.UserId == userId }),
                Children = family.Children.Select(c => new { c.FirstName, c.BirthYear }),
                Tasks = await db.Tasks.AsNoTracking().Where(t => t.FamilyId == familyId).OrderBy(t => t.CreatedAt)
                    .Select(t => new { t.Title, t.Notes, t.DueAt, t.AllDay, t.State, t.CreatedAt, t.CompletedAt, AssignedToMe = t.AssigneeUserId == userId })
                    .ToListAsync(ct),
                Events = await db.Events.AsNoTracking().Where(e => e.FamilyId == familyId).OrderBy(e => e.StartsAt)
                    .Select(e => new { e.Title, e.StartsAt, e.EndsAt, e.AllDay, e.Location, e.CreatedAt })
                    .ToListAsync(ct),
                Reminders = await db.Reminders.AsNoTracking().Where(r => r.FamilyId == familyId).OrderBy(r => r.FireAt)
                    .Select(r => new { r.Target, r.FireAt, r.Status, ForEveryone = r.RecipientUserId == null })
                    .ToListAsync(ct),
                Captures = await db.Captures.AsNoTracking().Where(c => c.FamilyId == familyId).OrderBy(c => c.CreatedAt)
                    .Select(c => new { c.Source, c.Text, c.Summary, c.Status, c.CreatedAt, ByMe = c.CreatedByUserId == userId, OriginalStored = c.StorageKey != null })
                    .ToListAsync(ct),
                HelperLinks = await db.ShareLinks.AsNoTracking().Where(l => l.FamilyId == familyId).OrderBy(l => l.CreatedAt)
                    .Select(l => new { l.Kind, l.Day, l.CreatedAt, l.ExpiresAt, l.RevokedAt, l.OpenCount })
                    .ToListAsync(ct),
                Subscription = await db.Subscriptions.AsNoTracking().Where(s => s.FamilyId == familyId)
                    .Select(s => new { s.Source, s.ProductId, s.StartedAt, s.ExpiresAt, s.WillRenew })
                    .FirstOrDefaultAsync(ct),
            });
        }

        var conversations = await db.Conversations.AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var conversationIds = conversations.Select(c => c.Id).ToList();
        var messages = await db.ChatMessages.AsNoTracking().Where(m => conversationIds.Contains(m.ConversationId)).OrderBy(m => m.CreatedAt)
            .Select(m => new { m.ConversationId, m.Role, m.Content, m.CreatedAt }).ToListAsync(ct);

        var export = new
        {
            ExportedAt = clock.GetUtcNow(),
            Profile = new
            {
                user.DisplayName,
                user.Email,
                user.Locale,
                user.CreatedAt,
                user.EveningPrepEnabled,
                user.EveningPrepHour,
                SignInWith = user.Logins.Select(l => l.Provider),
            },
            Families = families,
            AssistantConversations = conversations.Select(c => new
            {
                c.Title,
                c.CreatedAt,
                Messages = messages.Where(m => m.ConversationId == c.Id).Select(m => new { m.Role, m.Content, m.CreatedAt }),
            }),
            Notifications = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).OrderBy(n => n.CreatedAt)
                .Select(n => new { n.Kind, n.Title, n.Body, n.CreatedAt, n.ReadAt }).ToListAsync(ct),
            Devices = await db.Devices.AsNoTracking().Where(d => d.UserId == userId).Select(d => new { d.Platform, d.LastSeenAt }).ToListAsync(ct),
        };

        Response.Headers.CacheControl = "private, no-store";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(export, ExportJson);
        return File(bytes, "application/json", $"hally-export-{clock.GetUtcNow():yyyy-MM-dd}.json");
    }

    /// <summary>Leaves a family. The last adult leaving deletes the family and everything in it.</summary>
    [HttpPost("api/v1/families/{familyId:guid}/leave")]
    [Authorize(Policy = Policies.FamilyMember)]
    public async Task<IActionResult> Leave(Guid familyId, CancellationToken ct)
    {
        await accounts.LeaveFamilyAsync(User.UserId(), familyId, ct);
        return NoContent();
    }

    /// <summary>Deletes the account. <c>confirm=true</c> guards against accidental calls; the app also asks twice.
    /// A store subscription is not cancelled by this: the store owns billing, and the app says so before deleting.</summary>
    [HttpDelete("api/v1/me")]
    public async Task<IActionResult> Delete([FromQuery] bool confirm, CancellationToken ct)
    {
        if (!confirm)
        {
            return Problem(statusCode: 400, title: "Pass confirm=true to delete the account.", type: "https://hally.app/problems/account.confirm_required");
        }

        await accounts.DeleteAccountAsync(User.UserId(), ct);
        return NoContent();
    }
}
