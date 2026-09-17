using System.Numerics;
using System.Text.Json;

namespace BetterDeaths;

public sealed class ThemeDecorationTests
{
    [Fact]
    public void ExistingThemeNumbersRemainCompatibleWithSavedConfigurations()
    {
        string[] legacyNames =
        [
            "Classic", "Rose", "Verdant", "Ember", "Pink", "Lavender", "PastelBlue", "Wisteria",
            "Blush", "Bright", "Marble", "Tabasco", "Mint", "Sky", "Peach", "Cloud", "Abyss",
            "Graphite", "Grape", "Soda", "Callus", "Lemonade", "Cotton", "Banana", "Hamtaro",
            "DrPepper", "Sprite", "MountainDew", "Coke", "Fanta", "GingerAle", "Pepsi",
        ];
        for (var index = 0; index < legacyNames.Length; index++)
        {
            Assert.Equal(legacyNames[index], Enum.GetName((BetterDeathsTheme)index));
        }
    }

    [Theory]
    [InlineData(BetterDeathsTheme.Sakura, 32)]
    [InlineData(BetterDeathsTheme.Ironbound, 33)]
    [InlineData(BetterDeathsTheme.Celestial, 34)]
    [InlineData(BetterDeathsTheme.Frost, 35)]
    [InlineData(BetterDeathsTheme.Bloom, 36)]
    [InlineData(BetterDeathsTheme.Circuit, 37)]
    [InlineData(BetterDeathsTheme.Confection, 38)]
    [InlineData(BetterDeathsTheme.Tidal, 39)]
    [InlineData(BetterDeathsTheme.Herbarium, 40)]
    [InlineData(BetterDeathsTheme.ArtDeco, 41)]
    [InlineData(BetterDeathsTheme.Arcade, 42)]
    [InlineData(BetterDeathsTheme.Prism, 43)]
    [InlineData(BetterDeathsTheme.Thorn, 44)]
    [InlineData(BetterDeathsTheme.PaperSky, 45)]
    [InlineData(BetterDeathsTheme.Festival, 46)]
    public void StyleThemesRoundTripByNumberAndStableKey(BetterDeathsTheme theme, int id)
    {
        Assert.Equal(id, (int)theme);
        Assert.Equal(theme, JsonSerializer.Deserialize<BetterDeathsTheme>(JsonSerializer.Serialize(theme)));
        Assert.True(ThemeIdentityPolicy.TryResolve<BetterDeathsTheme>(ThemeIdentityPolicy.GetKey(theme), out var restored));
        Assert.Equal(theme, restored);
    }

    [Fact]
    public void MotifsStayWithinTheirBoundsAndHaveABoundedDrawCost()
    {
        foreach (var decoration in Enum.GetValues<ThemeDecoration>().Where(value => value != ThemeDecoration.None))
        {
            var paths = ThemeDecorationGeometry.GetPaths(decoration);
            Assert.InRange(paths.Length, 1, 40);
            var pointCount = 0;
            foreach (var path in paths)
            {
                Assert.InRange(path.Points.Length, path.Filled ? 3 : 2, 20);
                foreach (var point in path.Points.Span)
                {
                    Assert.InRange(point.X, 0, ThemeDecorationGeometry.Width);
                    Assert.InRange(point.Y, 0, ThemeDecorationGeometry.Height);
                    pointCount++;
                }
            }

            Assert.InRange(pointCount, 1, 160);
        }
    }

    [Fact]
    public void AllFilledOrnamentsAreConvexAndClockwiseForImGui()
    {
        foreach (var decoration in Enum.GetValues<ThemeDecoration>())
        foreach (var path in ThemeDecorationGeometry.GetPaths(decoration))
        {
            if (!path.Filled)
            {
                continue;
            }

            var points = path.Points.Span;
            for (var i = 0; i < points.Length; i++)
            {
                var a = points[(i + 1) % points.Length] - points[i];
                var b = points[(i + 2) % points.Length] - points[(i + 1) % points.Length];
                Assert.True(a.X * b.Y - a.Y * b.X > 0);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySizeAndScaleKeepsOrnamentsInsideReservedSpace(bool compact)
    {
        float[] widths = [16, 28, 44, 80, 180, 240, 400, 760, 1200, 2400];
        float[] scales = [0.5f, 1, 1.5f, 2, 3];
        foreach (var width in widths)
        foreach (var scale in scales)
        {
            var layout = ThemeDecorationGeometry.GetLayout(ThemeDecoration.Sakura, width, scale, compact);
            if (layout.Height == 0)
            {
                continue;
            }

            Assert.InRange(layout.Height, 1, 108);
            AssertFits(layout.Left);
            if (layout.ShowRight)
            {
                AssertFits(layout.Right);
                Assert.True(layout.Left.X + ThemeDecorationGeometry.Width * layout.Scale < layout.Right.X);
            }

            void AssertFits(Vector2 position)
            {
                Assert.InRange(position.X, 0, width);
                Assert.InRange(position.Y, 0, layout.Height);
                Assert.True(position.X + ThemeDecorationGeometry.Width * layout.Scale <= width + 0.001f);
                Assert.True(position.Y + ThemeDecorationGeometry.Height * layout.Scale <= layout.Height + 0.001f);
            }
        }
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(8)]
    public void UnusableWidthsDoNotReserveAnySpace(float width)
    {
        Assert.Equal(default, ThemeDecorationGeometry.GetLayout(ThemeDecoration.Sakura, width, 1, false));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(0)]
    public void InvalidScaleUsesNormalScale(float scale)
    {
        Assert.Equal(
            ThemeDecorationGeometry.GetLayout(ThemeDecoration.Chains, 760, 1, true),
            ThemeDecorationGeometry.GetLayout(ThemeDecoration.Chains, 760, scale, true));
    }

    [Fact]
    public void PlainOrUnknownDecorationsDoNotChangeLayout()
    {
        foreach (var decoration in new[] { ThemeDecoration.None, (ThemeDecoration)99 })
        {
            Assert.True(ThemeDecorationGeometry.GetPaths(decoration).IsEmpty);
            Assert.Equal(default, ThemeDecorationGeometry.GetLayout(decoration, 760, 1, false));
        }
    }

    [Fact]
    public void RepeatedRenderingLookupsDoNotAllocate()
    {
        _ = ThemeDecorationGeometry.GetPaths(ThemeDecoration.Frost).Length;
        _ = ThemeDecorationGeometry.GetLayout(ThemeDecoration.Frost, 760, 1, true);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var points = 0;
        for (var i = 0; i < 1000; i++)
        {
            foreach (var path in ThemeDecorationGeometry.GetPaths(ThemeDecoration.Frost))
            {
                points += path.Points.Length;
            }
            _ = ThemeDecorationGeometry.GetLayout(ThemeDecoration.Frost, 760, 1, true);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(points > 0);
        Assert.Equal(0, allocated);
    }
}
