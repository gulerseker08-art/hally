using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Hally.Domain.Common;
using Hally.Domain.Sharing;

namespace Hally.Api.Features.Sharing;

/// <summary>Renders the helper page. Every dynamic value is HTML-encoded.</summary>
public static class SharedPage
{
    public record Item(Guid Id, string Title, DateTimeOffset? At, bool AllDay, bool Done, string? Location = null);

    public record Model(string AppName, string SharerFirstName, ShareLinkKind Kind, bool Turkish, string TimeZoneId, DateOnly? Day,
        IReadOnlyList<Item> Tasks, IReadOnlyList<Item> Events, DateTimeOffset ExpiresAt);

    // Encodes markup characters but keeps Turkish letters readable in the page source.
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    public static string Render(Model m, string basePath)
    {
        var culture = m.Turkish ? new CultureInfo("tr-TR") : CultureInfo.InvariantCulture;
        string T(string tr, string en) => m.Turkish ? tr : en;
        string When(Item i)
        {
            if (i.At is not { } at)
            {
                return "";
            }

            var local = FamilyTimeZone.ToFamilyTime(at, m.TimeZoneId);
            var date = local.ToString(m.Turkish ? "d MMMM dddd" : "dddd, MMM d", culture);
            return m.Kind == ShareLinkKind.Day ? (i.AllDay ? T("Tüm gün", "All day") : local.ToString("HH:mm")) : i.AllDay ? date : $"{date} · {local:HH:mm}";
        }

        var title = m.Kind == ShareLinkKind.Day && m.Day is { } d
            ? d.ToString(m.Turkish ? "d MMMM dddd" : "dddd, MMM d", culture)
            : T("Paylaşılan görev", "Shared task");
        var sb = new StringBuilder();
        sb.Append($"<p class=\"from\">{Html.Encode(T($"{m.SharerFirstName} seninle paylaştı", $"{m.SharerFirstName} shared this with you"))}</p>");
        sb.Append($"<h1>{Html.Encode(title)}</h1>");

        if (m.Events.Count > 0)
        {
            sb.Append($"<h2>{T("Etkinlikler", "Events")}</h2><ul>");
            foreach (var e in m.Events)
            {
                sb.Append($"<li><span class=\"t\">{Html.Encode(e.Title)}</span><span class=\"w\">{Html.Encode(When(e))}{(e.Location is null ? "" : " · " + Html.Encode(e.Location))}</span></li>");
            }

            sb.Append("</ul>");
        }

        if (m.Tasks.Count > 0)
        {
            sb.Append($"<h2>{T("Yapılacaklar", "To do")}</h2><ul>");
            foreach (var t in m.Tasks)
            {
                sb.Append($"<li class=\"{(t.Done ? "done" : "")}\"><span class=\"t\">{Html.Encode(t.Title)}</span><span class=\"w\">{Html.Encode(When(t))}</span>");
                sb.Append(t.Done
                    ? $"<span class=\"ok\">✓ {T("Tamamlandı", "Done")}</span>"
                    : $"<form method=\"post\" action=\"{Html.Encode($"{basePath}/tasks/{t.Id}/done")}\"><button type=\"submit\">{T("Tamamlandı olarak işaretle", "Mark as done")}</button></form>");
                sb.Append("</li>");
            }

            sb.Append("</ul>");
        }

        if (m.Tasks.Count == 0 && m.Events.Count == 0)
        {
            sb.Append($"<p class=\"empty\">{T("Burada şu an bir şey yok.", "Nothing here right now.")}</p>");
        }

        var expires = FamilyTimeZone.ToFamilyTime(m.ExpiresAt, m.TimeZoneId).ToString(m.Turkish ? "d MMMM HH:mm" : "MMM d, HH:mm", culture);
        sb.Append($"<p class=\"foot\">{Html.Encode(T($"Bu link {expires} tarihine kadar geçerli.", $"This link works until {expires}."))}</p>");
        return Layout(m.AppName, Html.Encode(title), sb.ToString(), m.Turkish);
    }

    public static string Gone(string appName, bool turkish) => Layout(appName, turkish ? "Link geçersiz" : "Link not available",
        $"<h1>{(turkish ? "Bu link artık geçerli değil" : "This link is no longer available")}</h1><p class=\"empty\">{(turkish ? "Süresi dolmuş ya da paylaşan kişi kapatmış olabilir." : "It may have expired or been turned off by the person who shared it.")}</p>",
        turkish);

    private static string Layout(string appName, string title, string body, bool turkish) => $$$"""
        <!doctype html>
        <html lang="{{{(turkish ? "tr" : "en")}}}">
        <head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="robots" content="noindex">
        <title>{{{title}}} · {{{Html.Encode(appName)}}}</title>
        <style>
        :root{--bg:#F7F5FF;--card:#fff;--ink:#1F1A3D;--muted:#6B6688;--brand:#7C6BD6;--ok:#23937A;--line:#E7E3F7}
        @media (prefers-color-scheme:dark){:root{--bg:#14112A;--card:#1E1A3A;--ink:#F3F1FF;--muted:#A9A3C9;--line:#2E2952}}
        *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.45 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
        main{max-width:560px;margin:0 auto;padding:24px 16px 40px}
        .brand{font-weight:800;color:var(--brand);letter-spacing:.3px}
        h1{font-size:26px;line-height:1.2;margin:6px 0 18px}h2{font-size:14px;text-transform:uppercase;letter-spacing:1px;color:var(--muted);margin:22px 0 8px}
        ul{list-style:none;margin:0;padding:0}li{background:var(--card);border:1px solid var(--line);border-radius:16px;padding:14px 16px;margin-bottom:10px;display:flex;flex-direction:column;gap:4px}
        .t{font-weight:600}.w{color:var(--muted);font-size:14px}.from{color:var(--muted);margin:16px 0 0}
        li.done .t{text-decoration:line-through;color:var(--muted)}.ok{color:var(--ok);font-weight:600;font-size:14px}
        form{margin:6px 0 0}button{font:inherit;font-weight:600;border:0;border-radius:999px;padding:10px 16px;background:var(--brand);color:#fff;cursor:pointer;width:100%}
        .empty,.foot{color:var(--muted)}.foot{font-size:13px;margin-top:28px}
        </style>
        </head>
        <body><main><div class="brand">{{{Html.Encode(appName)}}}</div>{{{body}}}</main></body>
        </html>
        """;
}
