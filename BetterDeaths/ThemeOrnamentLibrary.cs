using System;
using System.Collections.Generic;
using System.Numerics;

namespace BetterDeaths;

internal static class ThemeOrnamentLibrary
{
    // Local coordinates are 32 x 32. Filled pieces are convex with ImGui's clockwise winding.
    private static readonly ThemeDecorationPath[][] Glyphs = BuildGlyphs();
    private static readonly ThemeDecorationPath[][] Bands = BuildBands();

    public static ReadOnlySpan<ThemeDecorationPath> GetGlyph(ThemeDecoration decoration) =>
        (uint)decoration < (uint)Glyphs.Length ? Glyphs[(int)decoration] : [];

    public static ReadOnlySpan<ThemeDecorationPath> GetBand(ThemeDecoration decoration) =>
        (uint)decoration < (uint)Bands.Length ? Bands[(int)decoration] : [];

    private static ThemeDecorationPath[][] BuildGlyphs()
    {
        var result = new ThemeDecorationPath[(int)ThemeDecoration.Lanterns + 1][];
        result[0] = [];
        result[(int)ThemeDecoration.Sakura] = [Polygon([new(5, 8), new(16, 5), new(26, 12), new(24, 20), new(15, 26), new(8, 19)])];
        result[(int)ThemeDecoration.Chains] = [Ellipse(12, 14, 8, 5), Ellipse(21, 19, 8, 5, true)];
        result[(int)ThemeDecoration.Constellation] =
        [
            Stroke([new(4, 17), new(16, 17), new(24, 5)], true),
            Polygon([new(16, 9), new(20, 17), new(16, 25), new(12, 17)], true),
        ];
        result[(int)ThemeDecoration.Frost] =
        [
            Stroke([new(16, 3), new(16, 29)]),
            Stroke([new(5, 9), new(27, 23)]), Stroke([new(5, 23), new(27, 9)]),
            Stroke([new(10, 5), new(16, 10), new(22, 5)], true),
            Stroke([new(10, 27), new(16, 22), new(22, 27)], true),
        ];
        result[(int)ThemeDecoration.Circuit] =
        [
            Stroke([new(2, 24), new(10, 24), new(21, 10), new(29, 10)]),
            Polygon([new(18, 7), new(24, 7), new(24, 13), new(18, 13)], true),
        ];
        result[(int)ThemeDecoration.Sweets] =
        [
            Polygon([new(2, 9), new(11, 14), new(11, 18), new(2, 23)], true),
            Polygon([new(21, 14), new(30, 9), new(30, 23), new(21, 18)], true),
            new(EllipsePoints(16, 16, 9, 8), Filled: true),
            Stroke([new(13, 10), new(19, 22)], true),
        ];
        result[(int)ThemeDecoration.Waves] =
        [
            Stroke([new(2, 14), new(7, 8), new(13, 7), new(18, 11), new(17, 16), new(13, 17), new(12, 13)]),
            Stroke([new(2, 23), new(8, 20), new(16, 23), new(23, 20), new(30, 23)], true),
            Stroke([new(17, 16), new(24, 15), new(30, 18)]),
        ];
        result[(int)ThemeDecoration.Leaves] =
        [
            Polygon([new(5, 25), new(6, 14), new(15, 6), new(28, 4), new(25, 17), new(16, 24)]),
            Stroke([new(4, 28), new(23, 9)], true),
            Stroke([new(11, 21), new(11, 13)], true),
            Stroke([new(17, 16), new(24, 16)], true),
        ];
        result[(int)ThemeDecoration.Deco] =
        [
            Stroke([new(3, 26), new(3, 14), new(16, 3), new(29, 14), new(29, 26)]),
            Stroke([new(8, 26), new(8, 16), new(16, 9), new(24, 16), new(24, 26)], true),
            Stroke([new(16, 3), new(16, 29)]),
            Stroke([new(3, 26), new(29, 26)], true),
        ];
        result[(int)ThemeDecoration.Pixels] =
        [
            Polygon([new(4, 4), new(13, 4), new(13, 13), new(4, 13)]),
            Polygon([new(16, 4), new(25, 4), new(25, 13), new(16, 13)], true),
            Polygon([new(16, 16), new(25, 16), new(25, 25), new(16, 25)]),
            Stroke([new(4, 21), new(4, 28), new(12, 28)], true),
        ];
        result[(int)ThemeDecoration.Glass] =
        [
            Polygon([new(16, 2), new(29, 12), new(16, 18)]),
            Polygon([new(16, 2), new(16, 18), new(3, 12)], true),
            Polygon([new(3, 12), new(16, 18), new(16, 30)]),
            Polygon([new(16, 18), new(29, 12), new(16, 30)], true),
        ];
        result[(int)ThemeDecoration.Thorns] =
        [
            Stroke([new(3, 27), new(10, 21), new(18, 19), new(24, 10), new(29, 4)]),
            Polygon([new(8, 23), new(7, 11), new(13, 20)], true),
            Polygon([new(19, 18), new(22, 13), new(30, 22)], true),
            new(EllipsePoints(17, 7, 5, 5), Closed: true),
            Stroke([new(17, 4), new(20, 7), new(17, 10), new(14, 7), new(17, 4)], true),
        ];
        result[(int)ThemeDecoration.Paper] =
        [
            Polygon([new(2, 11), new(29, 3), new(13, 18)]),
            Polygon([new(13, 18), new(29, 3), new(22, 29)], true),
            Stroke([new(3, 27), new(8, 22)]),
            Stroke([new(2, 21), new(5, 18)]),
        ];
        result[(int)ThemeDecoration.Lanterns] =
        [
            Polygon([new(8, 8), new(24, 8), new(26, 20), new(6, 20)]),
            Stroke([new(16, 1), new(16, 6)], true),
            Stroke([new(8, 6), new(24, 6)], true),
            Stroke([new(8, 22), new(24, 22)], true),
            Stroke([new(16, 23), new(16, 30)], true),
            Stroke([new(12, 10), new(12, 18)], true),
            Stroke([new(20, 10), new(20, 18)], true),
        ];
        return result;
    }

    private static ThemeDecorationPath[][] BuildBands()
    {
        var result = new ThemeDecorationPath[Glyphs.Length][];
        for (var i = 0; i < result.Length; i++)
        {
            var paths = new List<ThemeDecorationPath>();
            if (i >= (int)ThemeDecoration.Sweets)
            {
                for (var copy = 0; copy < 3; copy++)
                    foreach (var path in Glyphs[i])
                    {
                        var points = path.Points.ToArray();
                        for (var p = 0; p < points.Length; p++)
                            points[p] = points[p] * 0.78f + new Vector2(3 + copy * 32, 3);
                        paths.Add(path with { Points = points });
                    }
            }
            result[i] = paths.ToArray();
        }
        return result;
    }

    private static ThemeDecorationPath Polygon(Vector2[] points, bool highlight = false) => new(points, Filled: true, Highlight: highlight);
    private static ThemeDecorationPath Stroke(Vector2[] points, bool highlight = false) => new(points, Highlight: highlight);
    private static ThemeDecorationPath Ellipse(float x, float y, float rx, float ry, bool highlight = false) =>
        new(EllipsePoints(x, y, rx, ry), Closed: true, Highlight: highlight);

    private static Vector2[] EllipsePoints(float x, float y, float rx, float ry)
    {
        var points = new Vector2[12];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = i * MathF.Tau / points.Length;
            points[i] = new Vector2(x + MathF.Cos(angle) * rx, y + MathF.Sin(angle) * ry);
        }
        return points;
    }
}
