namespace Hally.Domain.Common;

/// <summary>IANA time zone helpers. Family dates are always resolved in the family's zone.</summary>
public static class FamilyTimeZone
{
    public const string Default = "Europe/Istanbul";

    public static bool IsValid(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.Contains('/') && id != "UTC")
        {
            return false;
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
    }

    public static DateTimeOffset ToFamilyTime(DateTimeOffset utc, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return TimeZoneInfo.ConvertTime(utc, zone);
    }
}
