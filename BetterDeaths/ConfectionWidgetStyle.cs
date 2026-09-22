using System.Numerics;

namespace BetterDeaths;


internal static class ConfectionWidgetStyle
{

    public static Vector4 Summary(Vector4 canvas) => Tint(canvas, new Vector4(.78f, .92f, .87f, 1), .82f);
    public static Vector4 ColumnHeader(Vector4 canvas) => Tint(canvas, new Vector4(.54f, .76f, .67f, 1), .84f);

    public static WidgetRowColors Row(Vector4 canvas, Vector4 text, byte role, int index, bool detail)
    {
        var light = canvas.X * .2126f + canvas.Y * .7152f + canvas.Z * .0722f >= .55f;
        var tint = role switch
        {
            1 => light ? new Vector4(.78f, .86f, .96f, 1) : new Vector4(.28f, .39f, .52f, 1),
            2 or 3 => light ? new Vector4(.97f, .76f, .83f, 1) : new Vector4(.51f, .28f, .37f, 1),
            4 => light ? new Vector4(.80f, .93f, .79f, 1) : new Vector4(.31f, .47f, .33f, 1),
            _ => light ? new Vector4(.91f, .84f, .94f, 1) : new Vector4(.41f, .32f, .47f, 1),
        };
        return MeterThemeStyle.TintedRow(canvas, text, tint, index, detail);
    }

    private static Vector4 Tint(Vector4 canvas, Vector4 tint, float amount)
    {
        // Custom dark canvases remain dark; their text still gets contrast checked.
        var light = canvas.X * .2126f + canvas.Y * .7152f + canvas.Z * .0722f >= .55f;
        return Vector4.Lerp(canvas, tint, light ? amount : amount * .18f) with { W = 1 };
    }

}
