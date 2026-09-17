using System.Numerics;

namespace BetterDeaths;


internal static class ConfectionWidgetStyle
{

    public static Vector4 Summary(Vector4 canvas) => Tint(canvas, new Vector4(.78f, .92f, .87f, 1), .82f);
    public static Vector4 ColumnHeader(Vector4 canvas) => Tint(canvas, new Vector4(.54f, .76f, .67f, 1), .84f);

    public static WidgetRowColors Row(Vector4 canvas, Vector4 text, byte role, int index, bool detail)
    {
        var tint = role switch
        {
            1 => new Vector4(.85f, .86f, .90f, 1),
            2 or 3 => new Vector4(.93f, .83f, .87f, 1),
            4 => new Vector4(.85f, .89f, .84f, 1),
            _ => new Vector4(.88f, .86f, .90f, 1),
        };
        var surface = Tint(canvas, tint, detail ? .18f : index % 2 == 0 ? .72f : .58f);
        var accent = Vector4.Lerp(tint, new Vector4(.12f, .16f, .18f, 1), .70f);
        return new WidgetRowColors(surface, WidgetSurfacePolicy.ReadableText(text, surface),
            WidgetSurfacePolicy.ReadableText(accent, surface));
    }

    private static Vector4 Tint(Vector4 canvas, Vector4 tint, float amount)
    {
        // Custom dark canvases remain dark; their text still gets contrast checked.
        var light = canvas.X * .2126f + canvas.Y * .7152f + canvas.Z * .0722f >= .55f;
        return Vector4.Lerp(canvas, tint, light ? amount : amount * .18f) with { W = 1 };
    }

}
