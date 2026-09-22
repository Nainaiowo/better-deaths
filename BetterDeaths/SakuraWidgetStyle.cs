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
            1 => light ? new Vector4(.76f, .82f, .96f, 1) : new Vector4(.25f, .33f, .47f, 1),
            2 or 3 => light ? new Vector4(.96f, .77f, .85f, 1) : new Vector4(.48f, .27f, .35f, 1),
            4 => light ? new Vector4(.77f, .94f, .81f, 1) : new Vector4(.25f, .47f, .32f, 1),
            _ => light ? new Vector4(.88f, .84f, .91f, 1) : new Vector4(.38f, .32f, .42f, 1),
        };
        return MeterThemeStyle.TintedRow(canvas, text, tint, index, detail);
    }

}
