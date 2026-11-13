using System.Text.RegularExpressions;
using Sentry;

namespace Hally.Api.Common;

/// <summary>Sentry error monitoring, on only when <c>Sentry:Dsn</c> is set. Events never carry PII:
/// no request bodies, cookies or auth headers, no user identity, and link tokens in URLs are masked.</summary>
public static partial class ErrorMonitoring
{
    public static void AddHallyErrorMonitoring(this WebApplicationBuilder builder)
    {
        var dsn = builder.Configuration["Sentry:Dsn"];
        if (string.IsNullOrWhiteSpace(dsn))
        {
            return;
        }

        builder.WebHost.UseSentry(o =>
        {
            o.Dsn = dsn;
            o.Environment = builder.Configuration["Sentry:Environment"] ?? builder.Environment.EnvironmentName.ToLowerInvariant();
            o.Release = builder.Configuration["Sentry:Release"];
            o.SendDefaultPii = false;
            o.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;
            o.TracesSampleRate = builder.Configuration.GetValue("Sentry:TracesSampleRate", 0.1);
            o.MinimumEventLevel = LogLevel.Error;
            o.SetBeforeSend(Scrub);
        });
    }

    /// <summary>Strips anything personal before an event leaves the server.</summary>
    public static SentryEvent Scrub(SentryEvent e, SentryHint _)
    {
        e.User = new SentryUser();
        e.Request.Data = null;
        e.Request.Cookies = null;
        e.Request.QueryString = null;
        foreach (var header in e.Request.Headers.Keys.Where(k => !SafeHeaders.Contains(k)).ToList())
        {
            e.Request.Headers.Remove(header);
        }

        if (e.Request.Url is { } url)
        {
            e.Request.Url = MaskTokens(url);
        }

        return e;
    }

    /// <summary>Helper links (/s/{token}) and invitations (/i/{token}, /invitations/{token}) carry secrets in the path.</summary>
    public static string MaskTokens(string url) => TokenSegment().Replace(url, "$1/[token]");

    private static readonly HashSet<string> SafeHeaders = new(StringComparer.OrdinalIgnoreCase) { "User-Agent", "Accept", "Accept-Language", "Content-Type" };

    [GeneratedRegex(@"(/(?:s|i|invitations))/[^/?#]+", RegexOptions.IgnoreCase)]
    private static partial Regex TokenSegment();
}
