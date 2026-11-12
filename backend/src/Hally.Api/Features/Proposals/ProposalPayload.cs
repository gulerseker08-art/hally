using System.Text.Json;
using Hally.Api.Common;
using Hally.Domain.Capture;

namespace Hally.Api.Features.Proposals;

/// <summary>
/// The validated, editable content of a proposal. Dates are family-local (yyyy-MM-dd / HH:mm) and are
/// converted to UTC with the family's time zone only when the proposal is executed.
/// </summary>
public record ProposalPayload
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Title { get; init; } = default!;
    public string? Notes { get; init; }
    public string? Date { get; init; }
    public string? Time { get; init; }
    public string? EndTime { get; init; }
    public string? Location { get; init; }
    public Guid? ChildId { get; init; }
    public Guid? AssigneeUserId { get; init; }
    public IReadOnlyList<string> NeedsInfo { get; init; } = [];
    public string Confidence { get; init; } = "medium";
    public string? SourceQuote { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ProposalPayload FromJson(string json) => JsonSerializer.Deserialize<ProposalPayload>(json, Json)!;

    public static string FingerprintOf(ProposalKind kind, string title, string? date) =>
        Crypto.Sha256($"{kind}|{Normalize(title)}|{date}");

    private static string Normalize(string s) =>
        string.Join(' ', s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
