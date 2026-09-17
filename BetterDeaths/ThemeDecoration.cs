using System;
using System.Collections.Generic;
using System.Numerics;

namespace BetterDeaths;

internal enum ThemeDecoration
{
    None,
    Sakura,
    Chains,
    Constellation,
    Frost,
    Circuit,
    Sweets,
    Waves,
    Leaves,
    Deco,
    Pixels,
    Glass,
    Thorns,
    Paper,
    Lanterns,
}

internal readonly record struct ThemeDecorationPath(
    ReadOnlyMemory<Vector2> Points,
    bool Filled = false,
    bool Closed = false,
    bool Highlight = false);

internal readonly record struct ThemeDecorationLayout(
    float Height,
    float Scale,
    Vector2 Left,
    Vector2 Right,
    bool ShowRight);

internal static class ThemeDecorationGeometry
{
    public const float Width = 96.0f;
    public const float Height = 32.0f;

    // Construct once; animated rendering transforms cached paths without rebuilding geometry.
    private static readonly ThemeDecorationPath[] Sakura = CreateSakura();
    private static readonly ThemeDecorationPath[] Chains = CreateChains();
    private static readonly ThemeDecorationPath[] Constellation = CreateConstellation();
    private static readonly ThemeDecorationPath[] Frost = CreateFrost();
    private static readonly ThemeDecorationPath[] Circuit = CreateCircuit();

    public static ReadOnlySpan<ThemeDecorationPath> GetPaths(ThemeDecoration decoration) => decoration switch
    {
        ThemeDecoration.None => ReadOnlySpan<ThemeDecorationPath>.Empty,
        ThemeDecoration.Sakura => Sakura,
        ThemeDecoration.Chains => Chains,
        ThemeDecoration.Constellation => Constellation,
        ThemeDecoration.Frost => Frost,
        ThemeDecoration.Circuit => Circuit,
        _ => ThemeOrnamentLibrary.GetBand(decoration),
    };

    public static ThemeDecorationLayout GetLayout(ThemeDecoration decoration, float width, float uiScale, bool compact)
    {
        if (GetPaths(decoration).IsEmpty || !float.IsFinite(width) || width < 16.0f)
        {
            return default;
        }

        uiScale = float.IsFinite(uiScale) && uiScale > 0 ? Math.Clamp(uiScale, 0.5f, 3.0f) : 1.0f;
        var height = (compact ? 24.0f : 36.0f) * uiScale;
        var inset = 4.0f * uiScale;
        var scale = MathF.Min((height - 4.0f * uiScale) / Height, (width - 2.0f * inset) / Width);
        if (scale <= 0)
        {
            return default;
        }

        var motifWidth = Width * scale;
        var y = (height - Height * scale) * 0.5f;
        var showRight = width >= 2.0f * (motifWidth + inset) + 24.0f * uiScale;
        return new ThemeDecorationLayout(
            height, scale, new Vector2(inset, y),
            new Vector2(width - inset - motifWidth, y), showRight);
    }

    private static ThemeDecorationPath[] CreateSakura()
    {
        var paths = new List<ThemeDecorationPath>();
        paths.Add(new(new Vector2[] { new(4, 26), new(24, 23), new(46, 14), new(78, 9), new(92, 4) }, Highlight: true));
        paths.Add(new(new Vector2[] { new(24, 23), new(30, 13) }, Highlight: true));
        AddBlossom(paths, new Vector2(32, 12), 7.0f);
        AddBlossom(paths, new Vector2(61, 12), 8.0f);
        AddPetal(paths, new Vector2(16, 12), 5.0f, -0.5f);
        AddPetal(paths, new Vector2(80, 24), 5.0f, 0.9f);
        AddPetal(paths, new Vector2(47, 27), 4.0f, -1.0f);
        return paths.ToArray();
    }

    private static void AddBlossom(List<ThemeDecorationPath> paths, Vector2 center, float size)
    {
        for (var i = 0; i < 5; i++)
        {
            AddPetal(paths, center, size, i * MathF.Tau / 5.0f);
        }
    }

    private static void AddPetal(List<ThemeDecorationPath> paths, Vector2 origin, float size, float angle)
    {
        var forward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var side = new Vector2(-forward.Y, forward.X);
        paths.Add(new(new Vector2[]
        {
            origin,
            origin + forward * size * 0.45f - side * size * 0.35f,
            origin + forward * size * 0.82f - side * size * 0.25f,
            origin + forward * size,
            origin + forward * size * 0.82f + side * size * 0.25f,
            origin + forward * size * 0.45f + side * size * 0.35f,
        }, Filled: true));
    }

    private static ThemeDecorationPath[] CreateChains()
    {
        var paths = new List<ThemeDecorationPath>();
        for (var link = 0; link < 7; link++)
        {
            var center = new Vector2(12 + link * 12, 16 + (link % 2 == 0 ? -2 : 2));
            var points = new Vector2[17];
            var angle = link % 2 == 0 ? -0.35f : 0.35f;
            var axis = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var cross = new Vector2(-axis.Y, axis.X);
            for (var i = 0; i < points.Length; i++)
            {
                var theta = i * MathF.Tau / (points.Length - 1);
                points[i] = center + axis * (9 * MathF.Cos(theta)) + cross * (4 * MathF.Sin(theta));
            }

            paths.Add(new(points, Closed: true, Highlight: link % 2 != 0));
        }

        return paths.ToArray();
    }

    private static ThemeDecorationPath[] CreateConstellation()
    {
        var paths = new List<ThemeDecorationPath>();
        Vector2[] stars = [new(8, 22), new(29, 9), new(50, 21), new(74, 11), new(89, 19)];
        paths.Add(new(stars));
        for (var i = 0; i < stars.Length; i++)
        {
            var center = stars[i];
            var radius = i % 2 == 0 ? 3.0f : 5.0f;
            paths.Add(new(new Vector2[] { center - new Vector2(radius, 0), center + new Vector2(radius, 0) }, Highlight: true));
            paths.Add(new(new Vector2[] { center - new Vector2(0, radius), center + new Vector2(0, radius) }, Highlight: true));
        }

        return paths.ToArray();
    }

    private static ThemeDecorationPath[] CreateCircuit()
    {
        return
        [
            new(new Vector2[] { new(3, 8), new(24, 8), new(34, 18), new(63, 18), new(75, 6), new(93, 6) }),
            new(new Vector2[] { new(3, 26), new(17, 26), new(29, 14), new(48, 14), new(61, 27), new(93, 27) }, Highlight: true),
            new(new Vector2[] { new(43, 3), new(51, 3), new(51, 9), new(43, 9) }, Closed: true, Highlight: true),
            new(new Vector2[] { new(78, 14), new(84, 14), new(84, 20), new(78, 20) }, Closed: true),
        ];
    }

    private static ThemeDecorationPath[] CreateFrost()
    {
        var paths = new List<ThemeDecorationPath>();
        for (var crystal = 0; crystal < 3; crystal++)
        {
            var center = new Vector2(16 + crystal * 32, 16);
            var radius = crystal == 1 ? 13.0f : 8.0f;
            for (var ray = 0; ray < 6; ray++)
            {
                var angle = ray * MathF.Tau / 6.0f;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var side = new Vector2(-direction.Y, direction.X);
                var tip = center + direction * radius;
                var fork = center + direction * radius * 0.55f;
                paths.Add(new(new Vector2[] { center, tip }, Highlight: crystal != 1));
                paths.Add(new(new Vector2[]
                {
                    fork + direction * radius * 0.2f + side * radius * 0.22f,
                    fork,
                    fork + direction * radius * 0.2f - side * radius * 0.22f,
                }, Highlight: crystal != 1));
            }
        }

        return paths.ToArray();
    }
}
