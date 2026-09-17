using System.Numerics;

namespace BetterDeaths;


internal static class SakuraWidgetStyle
{

    private static bool IsLight(Vector4 canvas) => canvas.X * .2126f + canvas.Y * .7152f + canvas.Z * .0722f >= .55f;

    public static Vector4 Summary(Vector4 canvas) => Vector4.Lerp(canvas, IsLight(canvas)
        ? new Vector4(.95f, .89f, .92f, 1) : new Vector4(.46f, .29f, .35f, 1), .8f) with { W = 1 };

    public static Vector4 ColumnHeader(Vector4 canvas) => Vector4.Lerp(canvas, IsLight(canvas)
        ? new Vector4(.74f, .85f, .79f, 1) : new Vector4(.22f, .32f, .29f, 1), .9f) with { W = 1 };

    public static WidgetRowColors Row(Vector4 canvas, Vector4 text, byte role, int index, bool detail)
    {
        var light = IsLight(canvas);
        var tint = role switch
        {
            1 => light ? new Vector4(.85f, .86f, .89f, 1) : new Vector4(.30f, .32f, .34f, 1),
            2 or 3 => light ? new Vector4(.91f, .83f, .86f, 1) : new Vector4(.35f, .29f, .32f, 1),
            4 => light ? new Vector4(.84f, .89f, .86f, 1) : new Vector4(.29f, .35f, .32f, 1),
            _ => light ? new Vector4(.88f, .86f, .89f, 1) : new Vector4(.31f, .32f, .33f, 1),
        };
        var surface = Vector4.Lerp(canvas, tint, detail ? .22f : index % 2 == 0 ? .86f : .72f) with { W = 1 };
        var accent = Vector4.Lerp(tint, light ? new Vector4(.12f, .18f, .16f, 1) : Vector4.One, .65f);
        return new WidgetRowColors(surface, WidgetSurfacePolicy.ReadableText(text, surface),
            WidgetSurfacePolicy.ReadableText(accent, surface));
    }

}
