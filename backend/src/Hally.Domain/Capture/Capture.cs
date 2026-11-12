using Hally.Domain.Common;

namespace Hally.Domain.Capture;

public enum CaptureSource
{
    Camera,
    Gallery,
    Screenshot,
    Share,
    Text,
}

public enum CaptureStatus
{
    Pending,
    Extracted,
    Failed,
}

/// <summary>Something the user captured (photo, screenshot, shared text). The original file is stored
/// encrypted outside the database; only a storage key is kept here.</summary>
public class Capture : Entity
{
    private Capture() { }

    public Capture(Guid familyId, Guid createdByUserId, CaptureSource source, string? text, string? storageKey, string? contentType, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text) && storageKey is null)
        {
            throw new DomainException("capture.empty", "A capture needs text or a file.");
        }

        FamilyId = familyId;
        CreatedByUserId = createdByUserId;
        Source = source;
        Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        StorageKey = storageKey;
        ContentType = contentType;
        CreatedAt = now;
    }

    public Guid FamilyId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public CaptureSource Source { get; private set; }
    public string? Text { get; private set; }
    public string? StorageKey { get; private set; }
    public string? ContentType { get; private set; }
    public CaptureStatus Status { get; private set; } = CaptureStatus.Pending;
    public string? FailureCode { get; private set; }
    public string? Summary { get; private set; }

    public void MarkExtracted(string? summary)
    {
        Status = CaptureStatus.Extracted;
        Summary = summary;
        FailureCode = null;
    }

    public void MarkFailed(string code)
    {
        Status = CaptureStatus.Failed;
        FailureCode = code;
    }

    public void ForgetOriginal() => StorageKey = null;
}
