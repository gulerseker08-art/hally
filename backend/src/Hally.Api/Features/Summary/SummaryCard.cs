using System.Globalization;
using SkiaSharp;

namespace Hally.Api.Features.Summary;

/// <summary>Draws the weekly summary as a 1080×1350 (4:5) PNG for sharing. Uses bundled OFL fonts so Turkish characters render everywhere.</summary>
public static class SummaryCard
{
    private const int W = 1080;
    private const int H = 1350;
    private static readonly string FontDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
    private static readonly Lazy<SKTypeface> Display = new(() => Load("BricolageGrotesque-800.ttf"));
    private static readonly Lazy<SKTypeface> Bold = new(() => Load("Figtree-700.ttf"));
    private static readonly Lazy<SKTypeface> Regular = new(() => Load("Figtree-500.ttf"));

    private static SKTypeface Load(string file)
    {
        var path = Path.Combine(FontDir, file);
        return File.Exists(path) ? SKTypeface.FromFile(path) : SKTypeface.Default;
    }

    public static byte[] Render(WeeklySummaryDto s, string familyName, bool english)
    {
        string T(string tr, string en) => english ? en : tr;
        var culture = english ? CultureInfo.InvariantCulture : new CultureInfo("tr-TR");
        var start = DateOnly.ParseExact(s.WeekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = DateOnly.ParseExact(s.WeekEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var range = $"{start.ToString(english ? "MMM d" : "d MMM", culture)} – {end.ToString(english ? "MMM d" : "d MMM", culture)}";

        using var surface = SKSurface.Create(new SKImageInfo(W, H));
        var c = surface.Canvas;
        using (var bg = new SKPaint())
        {
            bg.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(W, H),
                [new SKColor(0x2B, 0x21, 0x60), new SKColor(0x7C, 0x6B, 0xD6), new SKColor(0xE2, 0x7A, 0xA6)], [0f, 0.6f, 1f], SKShaderTileMode.Clamp);
            c.DrawRect(0, 0, W, H, bg);
        }

        Glow(c, 900, 140, 320, new SKColor(0xF0, 0xA3, 0x6A, 90));
        Glow(c, 120, 1240, 360, new SKColor(0x6F, 0xD3, 0xB4, 80));

        Text(c, "Hally", 80, 130, Display.Value, 44, SKColors.White.WithAlpha(230));
        Text(c, T("Haftanın özeti", "Our week"), 80, 250, Display.Value, 88, SKColors.White);
        Text(c, $"{familyName} · {range}", 80, 320, Regular.Value, 38, SKColors.White.WithAlpha(200), maxWidth: W - 160);

        var stats = new (string Value, string Label)[]
        {
            ($"{s.TasksCompleted}", T("görev tamamlandı", "tasks done")),
            ($"{s.EventsCount}", T("etkinlik", "events")),
            ($"{s.AiActionsApproved}", T("Hally önerisi onaylandı", "Hally suggestions approved")),
        };
        var y = 400f;
        foreach (var (value, label) in stats)
        {
            Card(c, 80, y, W - 160, 150);
            Text(c, value, 120, y + 105, Display.Value, 84, new SKColor(0x2B, 0x21, 0x60));
            Text(c, label, 120 + Math.Max(110, Measure(value, Display.Value, 84) + 30), y + 92, Bold.Value, 40, new SKColor(0x4A, 0x43, 0x75), maxWidth: W - 400);
            y += 175;
        }

        // Who did what: a simple bar per adult (first names only).
        var total = Math.Max(1, s.Adults.Sum(a => a.Completed) + s.HelperCompleted);
        Text(c, T("Kim ne yaptı", "Who did what"), 80, y + 50, Bold.Value, 40, SKColors.White);
        y += 80;
        var rows = s.Adults.Take(3).Select(a => (a.FirstName, a.Completed)).ToList();
        if (s.HelperCompleted > 0)
        {
            rows.Add((T("Yardımcı", "Helper"), s.HelperCompleted));
        }

        foreach (var (name, count) in rows)
        {
            Text(c, name, 80, y + 34, Bold.Value, 34, SKColors.White, maxWidth: 260);
            using var track = new SKPaint { Color = SKColors.White.WithAlpha(60), IsAntialias = true };
            c.DrawRoundRect(360, y + 6, W - 520, 36, 18, 18, track);
            using var fill = new SKPaint { Color = new SKColor(0xFF, 0xD7, 0x9A), IsAntialias = true };
            c.DrawRoundRect(360, y + 6, Math.Max(36, (W - 520) * count / (float)total), 36, 18, 18, fill);
            Text(c, $"{count}", W - 140, y + 36, Bold.Value, 34, SKColors.White);
            y += 62;
        }

        if (s.Children.Count > 0)
        {
            var kids = string.Join(" · ", s.Children.Take(3).Select(k => $"{k.FirstName} {k.Items}"));
            Text(c, kids, 80, y + 40, Regular.Value, 32, SKColors.White.WithAlpha(220), maxWidth: W - 160);
        }

        Text(c, T("Çek, söyle, Hally halletsin.", "Snap it. Say it. Hally handles it."), 80, H - 70, Regular.Value, 32, SKColors.White.WithAlpha(200));

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private static void Glow(SKCanvas c, float x, float y, float r, SKColor color)
    {
        using var p = new SKPaint { IsAntialias = true };
        p.Shader = SKShader.CreateRadialGradient(new SKPoint(x, y), r, [color, color.WithAlpha(0)], SKShaderTileMode.Clamp);
        c.DrawCircle(x, y, r, p);
    }

    private static void Card(SKCanvas c, float x, float y, float w, float h)
    {
        using var p = new SKPaint { Color = SKColors.White.WithAlpha(240), IsAntialias = true };
        c.DrawRoundRect(x, y, w, h, 36, 36, p);
    }

    private static SKPaint TextPaint(SKTypeface face, float size, SKColor color) =>
        new() { Typeface = face, TextSize = size, Color = color, IsAntialias = true, SubpixelText = true };

    private static float Measure(string text, SKTypeface face, float size)
    {
        using var paint = TextPaint(face, size, SKColors.Black);
        return paint.MeasureText(text);
    }

    private static void Text(SKCanvas c, string text, float x, float y, SKTypeface face, float size, SKColor color, float? maxWidth = null)
    {
        using var paint = TextPaint(face, size, color);
        if (maxWidth is { } max && paint.MeasureText(text) > max)
        {
            while (text.Length > 1 && paint.MeasureText(text + "…") > max)
            {
                text = text[..^1];
            }

            text += "…";
        }

        c.DrawText(text, x, y, paint);
    }
}
