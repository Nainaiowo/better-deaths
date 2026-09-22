using System;
using System.Numerics;

namespace BetterDeaths;

internal readonly record struct MeterThemeProfile(Vector4 Summary, Vector4 Strip, MeterBackgroundKind Background, bool TimerFirst);
internal readonly record struct MeterSummaryLayout(float TextWidth, float MetricsWidth, bool InlineHeading, bool StackMetrics);

internal static class MeterThemeStyle
{
    public const int ThemeCount = 34;

    public static bool IsSupported(BetterDeathsTheme theme) => (int)theme is >= 0 and <= 32 || theme == BetterDeathsTheme.Confection;
    public const float ValueScale = 1.05f;

    public static MeterThemeProfile Profile(BetterDeathsTheme theme) => theme switch
    {
        BetterDeathsTheme.Classic => P(0x17443F, 0x394957, MeterBackgroundKind.Satin),
        BetterDeathsTheme.Rose => P(0x67394B, 0x365B50, MeterBackgroundKind.Petals),
        BetterDeathsTheme.Verdant => P(0x305744, 0x59503B, MeterBackgroundKind.Leaves),
        BetterDeathsTheme.Ember => P(0x623D30, 0x384B52, MeterBackgroundKind.Heat),
        BetterDeathsTheme.Pink => P(0x713D63, 0x3C5C63, MeterBackgroundKind.Bubblegum),
        BetterDeathsTheme.Lavender => P(0x493B65, 0x3C5C59, MeterBackgroundKind.Liquid, true),
        BetterDeathsTheme.PastelBlue => P(0x344E69, 0x625443, MeterBackgroundKind.Moonlight, true),
        BetterDeathsTheme.Wisteria => P(0xDDD0EC, 0xB9D4CD, MeterBackgroundKind.Wisteria),
        BetterDeathsTheme.Blush => P(0xEBD1D6, 0xC3D7D0, MeterBackgroundKind.Hearts),
        BetterDeathsTheme.Bright => P(0xD8ECEB, 0xDFD1B5, MeterBackgroundKind.Daylight),
        BetterDeathsTheme.Marble => P(0xE0E5E8, 0xB7CBCA, MeterBackgroundKind.Marble, true),
        BetterDeathsTheme.Tabasco => P(0x673D35, 0x415840, MeterBackgroundKind.Spice),
        BetterDeathsTheme.Mint => P(0xCAE6DC, 0xD9C9DC, MeterBackgroundKind.Mint),
        BetterDeathsTheme.Sky => P(0xD0E2F3, 0xDFD5C4, MeterBackgroundKind.Clouds, true),
        BetterDeathsTheme.Peach => P(0xF0D3C2, 0xC3DBD4, MeterBackgroundKind.Peach),
        BetterDeathsTheme.Cloud => P(0xE1E7EF, 0xD7CDE1, MeterBackgroundKind.Fabric, true),
        BetterDeathsTheme.Abyss => P(0x293F53, 0x46425E, MeterBackgroundKind.Depth, true),
        BetterDeathsTheme.Graphite => P(0x3D454E, 0x466162, MeterBackgroundKind.Brushed),
        BetterDeathsTheme.Grape => P(0x4E365F, 0x455C4A, MeterBackgroundKind.Grapes),
        BetterDeathsTheme.Soda => P(0x315569, 0x614C60, MeterBackgroundKind.Fizz),
        BetterDeathsTheme.Callus => P(0xDCD0E9, 0xDFCEA6, MeterBackgroundKind.Ribbons, true),
        BetterDeathsTheme.Lemonade => P(0xF0E7C0, 0xC3D6C7, MeterBackgroundKind.Citrus),
        BetterDeathsTheme.Cotton => P(0xDBE5F4, 0xE5CADA, MeterBackgroundKind.Cotton),
        BetterDeathsTheme.Banana => P(0xF1DF8A, 0xBBCBE5, MeterBackgroundKind.Banana),
        BetterDeathsTheme.Hamtaro => P(0xEFD9BB, 0xCBD8C1, MeterBackgroundKind.Weave),
        BetterDeathsTheme.DrPepper => P(0x61333F, 0x535963, MeterBackgroundKind.Fizz),
        BetterDeathsTheme.Sprite => P(0xD4EACF, 0xDDE2B1, MeterBackgroundKind.Citrus),
        BetterDeathsTheme.MountainDew => P(0xDCE8AC, 0xCCDCCF, MeterBackgroundKind.Fizz),
        BetterDeathsTheme.Coke => P(0x603636, 0x4E545C, MeterBackgroundKind.Fizz),
        BetterDeathsTheme.Fanta => P(0xF0D29E, 0xBDD2E7, MeterBackgroundKind.Citrus),
        BetterDeathsTheme.GingerAle => P(0xE6DFC1, 0xC2D5C2, MeterBackgroundKind.Fizz),
        BetterDeathsTheme.Pepsi => P(0x324C71, 0x69444E, MeterBackgroundKind.Fizz),
        BetterDeathsTheme.Sakura => P(0x754A59, 0x3E6255, MeterBackgroundKind.Petals, true),
        BetterDeathsTheme.Confection => P(0xC7EBDE, 0x89C2AB, MeterBackgroundKind.Candy),
        _ => P(0x17443F, 0x394957, MeterBackgroundKind.Satin),
    };

    private static MeterThemeProfile P(int summary, int strip, MeterBackgroundKind background, bool timer = false) =>
        new(Color(summary), Color(strip), background, timer);

    private static Vector4 Color(int rgb) => new((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1);
    private static bool Light(Vector4 color) => color.X * .2126f + color.Y * .7152f + color.Z * .0722f >= .55f;

    private static Vector4 Surface(Vector4 canvas, Vector4 color, float amount)
    {
        // Keep custom light/dark canvases in their chosen family, without discarding the theme hue.
        if (Light(canvas) != Light(color))
            color = Vector4.Lerp(color, Light(canvas) ? Vector4.One : new Vector4(.06f, .07f, .08f, 1), .72f);
        return Vector4.Lerp(canvas, color, amount) with { W = 1 };
    }

    public static Vector4 Summary(BetterDeathsTheme theme, Vector4 canvas) => theme switch
    {
        BetterDeathsTheme.Sakura => SakuraWidgetStyle.Summary(canvas),
        BetterDeathsTheme.Confection => ConfectionWidgetStyle.Summary(canvas),
        _ => Surface(canvas, Profile(theme).Summary, .88f),
    };

    public static Vector4 ColumnHeader(BetterDeathsTheme theme, Vector4 canvas) => theme switch
    {
        BetterDeathsTheme.Sakura => SakuraWidgetStyle.ColumnHeader(canvas),
        BetterDeathsTheme.Confection => ConfectionWidgetStyle.ColumnHeader(canvas),
        _ => Surface(canvas, Profile(theme).Strip, .88f),
    };

    public static WidgetRowColors Row(BetterDeathsTheme theme, Vector4 canvas, Vector4 text, byte role, int index, bool detail)
    {
        if (theme == BetterDeathsTheme.Sakura)
            return SakuraWidgetStyle.Row(canvas, text, role, index, detail);
        if (theme == BetterDeathsTheme.Confection)
            return ConfectionWidgetStyle.Row(canvas, text, role, index, detail);
        var light = Light(canvas);
        var tint = role switch
        {
            1 => light ? Color(0xC5DDF0) : Color(0x385671),
            2 or 3 => light ? Color(0xF1C7D8) : Color(0x704057),
            4 => light ? Color(0xCAE8D5) : Color(0x38674F),
            _ => light ? Color(0xDFD8E9) : Color(0x544B69),
        };
        return TintedRow(Surface(canvas, Profile(theme).Summary, .14f), text, tint, index, detail);
    }

    internal static WidgetRowColors TintedRow(Vector4 canvas, Vector4 text, Vector4 tint, int index, bool detail)
    {
        var light = Light(canvas);
        var roleSurface = Vector4.Lerp(canvas, tint, .78f);
        var shadeTarget = light ? new Vector4(.12f, .14f, .16f, 1) : Vector4.One;
        var shaded = Vector4.Lerp(roleSurface, shadeTarget, index % 2 == 0 ? .015f : .105f);
        var background = (detail ? Vector4.Lerp(canvas, shaded, .38f) : shaded) with { W = 1 };
        var accent = Vector4.Lerp(tint, shadeTarget, light ? .56f : .48f);
        return new(background, WidgetSurfacePolicy.ReadableText(text, background),
            WidgetSurfacePolicy.ReadableText(accent, background));
    }

    public static MeterSummaryLayout Layout(float width, float scale, float headingWidth, float firstWidth, float secondWidth, float controlsWidth = 0)
    {
        width = float.IsFinite(width) ? MathF.Max(1, width) : 1;
        scale = float.IsFinite(scale) ? Math.Clamp(scale, .5f, 3) : 1;
        headingWidth = float.IsFinite(headingWidth) ? MathF.Max(0, headingWidth) : 0;
        firstWidth = float.IsFinite(firstWidth) ? MathF.Max(0, firstWidth) : 0;
        secondWidth = float.IsFinite(secondWidth) ? MathF.Max(0, secondWidth) : 0;
        controlsWidth = float.IsFinite(controlsWidth) ? MathF.Max(0, controlsWidth) : 0;
        var metricsWidth = firstWidth + secondWidth + 12 * scale;
        var stackMetrics = width < metricsWidth;
        return new(width, MathF.Min(width, metricsWidth),
            !stackMetrics && width >= headingWidth + controlsWidth + metricsWidth + 24 * scale, stackMetrics);
    }
}
