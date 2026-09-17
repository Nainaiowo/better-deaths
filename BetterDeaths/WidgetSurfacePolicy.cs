using System;
using System.Numerics;

namespace BetterDeaths;

internal enum WidgetSurfaceStyle
{
    Standard,
    Bloom,
    Circuit,
    Confection,
    Tidal,
    Herbarium,
    ArtDeco,
    Arcade,
    Prism,
    Thorn,
    PaperSky,
    Festival,
    Minimal,
}

internal readonly record struct WidgetRowColors(Vector4 Background, Vector4 Text, Vector4 Accent);

internal static class WidgetSurfacePolicy
{
    public static bool IsExpressive(WidgetSurfaceStyle style) =>
        style is >= WidgetSurfaceStyle.Bloom and <= WidgetSurfaceStyle.Minimal;

    public static bool IsSoft(WidgetSurfaceStyle style) => style is WidgetSurfaceStyle.Bloom or
        WidgetSurfaceStyle.Confection or WidgetSurfaceStyle.Tidal or WidgetSurfaceStyle.Herbarium or WidgetSurfaceStyle.PaperSky;

    public static Vector4 GetQuietSurface(Vector4 color)
    {
        var light = color.X * 0.2126f + color.Y * 0.7152f + color.Z * 0.0722f >= 0.55f;
        var neutral = light ? new Vector4(0.975f, 0.978f, 0.982f, color.W) : new Vector4(0.065f, 0.072f, 0.080f, color.W);
        return Vector4.Lerp(neutral, color, 0.38f) with { W = color.W };
    }

    public static float GetEdgeGutter(float width, float scale = 1) =>
        !float.IsFinite(width) || width <= 0 ? 0 : MathF.Min(width * 0.08f,
            20 * (float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 3) : 1));

    public static float GetSignatureWidth(float width, float scale = 1)
    {
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.5f, 3) : 1;
        return float.IsFinite(width) && width >= 280 * scale ? 58 * scale : 0;
    }

    public static Vector4 GetRowOverlay(Vector4 canvas, int index, bool detail = false)
    {
        var light = canvas.X * 0.2126f + canvas.Y * 0.7152f + canvas.Z * 0.0722f >= 0.55f;
        var opacity = detail ? 0.025f : index % 2 == 0 ? 0.055f : 0.012f;
        return new Vector4(light ? 0 : 1, light ? 0 : 1, light ? 0 : 1, opacity);
    }

    public static Vector4 CompositeSurface(Vector4 background, Vector4 overlay) =>
        Vector4.Lerp(background, overlay, overlay.W) with { W = 1 };

    public static WidgetRowColors GetRowColors(
        WidgetSurfaceStyle style, byte jobRole, Vector4 panel, Vector4 preferredText, bool detail = false)
    {
        if (!IsExpressive(style))
        {
            return new WidgetRowColors(panel, preferredText, panel);
        }

        // These are the game's ClassJob.Role categories, not damage or job-specific combat rules.
        var tint = jobRole switch
        {
            1 => new Vector4(0.34f, 0.69f, 0.94f, 1),
            4 => new Vector4(0.51f, 0.78f, 0.43f, 1),
            2 or 3 => new Vector4(0.95f, 0.45f, 0.49f, 1),
            _ => new Vector4(0.66f, 0.62f, 0.80f, 1),
        };
        var background = panel with { W = 1 };
        var light = background.X * 0.2126f + background.Y * 0.7152f + background.Z * 0.0722f >= 0.55f;
        var accent = light ? Vector4.Lerp(tint, new Vector4(0, 0, 0, 1), 0.48f) : tint;
        var text = ReadableText(preferredText, background);
        return new WidgetRowColors(background, text, detail ? text : ReadableText(accent, background));
    }

    public static Vector4 ReadableText(Vector4 preferred, Vector4 background)
    {
        preferred.W = 1;
        if (Contrast(preferred, background) >= 4.5f)
        {
            return preferred;
        }

        var dark = new Vector4(0, 0, 0, 1);
        var light = Vector4.One;
        return Contrast(dark, background) >= Contrast(light, background) ? dark : light;
    }

    public static float Contrast(Vector4 first, Vector4 second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (MathF.Max(a, b) + 0.05f) / (MathF.Min(a, b) + 0.05f);
    }

    private static float Luminance(Vector4 color)
    {
        static float Linear(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
        return 0.2126f * Linear(color.X) + 0.7152f * Linear(color.Y) + 0.0722f * Linear(color.Z);
    }

    public static bool StackSummary(float availableWidth, float timerWidth, float dpsWidth, float spacing) =>
        availableWidth < timerWidth + dpsWidth + spacing;
}
