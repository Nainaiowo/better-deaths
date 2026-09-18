using System.Numerics;

namespace BetterDeaths;

public sealed class MeterThemeStyleTests
{
    public static IEnumerable<object[]> Themes => Enum.GetValues<BetterDeathsTheme>().Where(MeterThemeStyle.IsSupported).Select(theme => new object[] { theme });

    [Fact]
    public void OnlyExistingPresetsPlusSakuraAndConfectionHaveExplicitProfiles()
    {
        var themes = Enum.GetValues<BetterDeathsTheme>().Where(MeterThemeStyle.IsSupported).ToArray();
        Assert.Equal(34, themes.Length);
        Assert.Equal(MeterThemeStyle.ThemeCount, themes.Length);
        Assert.All(Enumerable.Range(0, 33), i => Assert.Contains((BetterDeathsTheme)i, themes));
        Assert.Contains(BetterDeathsTheme.Confection, themes);
        Assert.Equal(34, themes.Select(MeterThemeStyle.Profile).Distinct().Count());
        Assert.Equal(Enum.GetValues<MeterBackgroundKind>().Length,
            themes.Select(t => MeterThemeStyle.Profile(t).Background).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void AllThemesKeepNamesValuesAndAbilityRowsReadable(BetterDeathsTheme theme)
    {
        for (var level = 0; level <= 10; level++)
        foreach (byte role in new byte[] { 0, 1, 2, 3, 4, 255 })
        foreach (var detail in new[] { false, true })
        foreach (var text in new[] { Vector4.Zero, Vector4.One, new Vector4(.63f, .18f, .39f, 1) })
        {
            var canvas = new Vector4(level / 10f, level / 10f, level / 10f, .4f);
            var row = MeterThemeStyle.Row(theme, canvas, text, role, level, detail);
            Assert.True(WidgetSurfacePolicy.Contrast(row.Text, row.Background) >= 4.5f);
            Assert.True(WidgetSurfacePolicy.Contrast(row.Accent, row.Background) >= 4.5f);
            Assert.Equal(1, row.Background.W);
            foreach (var surface in new[] { MeterThemeStyle.Summary(theme, canvas), MeterThemeStyle.ColumnHeader(theme, canvas) })
                Assert.True(WidgetSurfacePolicy.Contrast(WidgetSurfacePolicy.ReadableText(text, surface), surface) >= 4.5f);
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void MeleeAndRangedMatchAndRoleDifferencesStaySubtle(BetterDeathsTheme theme)
    {
        foreach (var canvas in new[] { new Vector4(.08f, .10f, .09f, 1), Vector4.One })
        foreach (var detail in new[] { false, true })
        for (var index = 0; index < 8; index++)
        {
            var melee = MeterThemeStyle.Row(theme, canvas, Vector4.One, 2, index, detail);
            Assert.Equal(melee, MeterThemeStyle.Row(theme, canvas, Vector4.One, 3, index, detail));
            foreach (byte role in new byte[] { 1, 4 })
                Assert.InRange(Vector4.Distance(melee.Background,
                    MeterThemeStyle.Row(theme, canvas, Vector4.One, role, index, detail).Background), 0, .11f);
        }
    }

    [Fact]
    public void ApprovedSakuraAndConfectionPalettesArePreserved()
    {
        foreach (var canvas in new[] { Vector4.One, new Vector4(.08f, .1f, .09f, 1) })
        {
            Assert.Equal(SakuraWidgetStyle.Summary(canvas), MeterThemeStyle.Summary(BetterDeathsTheme.Sakura, canvas));
            Assert.Equal(ConfectionWidgetStyle.Summary(canvas), MeterThemeStyle.Summary(BetterDeathsTheme.Confection, canvas));
            Assert.Equal(SakuraWidgetStyle.ColumnHeader(canvas), MeterThemeStyle.ColumnHeader(BetterDeathsTheme.Sakura, canvas));
            Assert.Equal(ConfectionWidgetStyle.ColumnHeader(canvas), MeterThemeStyle.ColumnHeader(BetterDeathsTheme.Confection, canvas));
        }
    }

    [Theory]
    [InlineData(780, 1, 170, 70, false)]
    [InlineData(480, 1, 170, 70, false)]
    [InlineData(240, 1, 170, 70, true)]
    [InlineData(780, 2, 340, 140, false)]
    [InlineData(500, 1, 400, 100, true)]
    public void HeaderUsesFullWidthAndStacksMeasuredMetrics(float width, float scale, float first, float second, bool stack)
    {
        var layout = MeterThemeStyle.Layout(width, scale, first, second);
        Assert.Equal(width, layout.TextWidth);
        Assert.Equal(stack, layout.StackMetrics);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void BackgroundsArePeriodicBoundedAndOptional(BetterDeathsTheme theme)
    {
        var size = new Vector2(780, 110);
        for (var time = 0; time < 80; time++)
        {
            Assert.Equal(0, MeterHeaderMotionPolicy.Phase(time, false, false));
            Assert.Equal(0, MeterHeaderMotionPolicy.Phase(time, true, true));
            var phase = MeterHeaderMotionPolicy.Phase(time, true, false);
            Assert.Equal(phase, MeterHeaderMotionPolicy.Phase(time + 36, true, false));
            for (var index = 0; index < MeterHeaderMotionPolicy.ParticleCount; index++)
            {
                var p = MeterHeaderMotionPolicy.Particle(theme, index, size, phase, 1);
                Assert.InRange(p.Center.X, 0, size.X);
                Assert.InRange(p.Center.Y, 0, size.Y);
                Assert.InRange(p.Radius, 2, 6);
                Assert.InRange(p.Opacity, 0, 1);
            }
            for (var lane = 0; lane < 8; lane++)
            for (var segment = 0; segment <= MeterHeaderMotionPolicy.Segments; segment++)
            {
                var p = MeterHeaderMotionPolicy.Contour(theme, size, lane, segment, phase);
                Assert.InRange(p.X, 0, size.X);
                Assert.InRange(p.Y, 0, size.Y);
            }
        }
        for (var index = 0; index < MeterHeaderMotionPolicy.ParticleCount; index++)
        {
            var before = MeterHeaderMotionPolicy.Particle(theme, index, size, .99999f, 1);
            var after = MeterHeaderMotionPolicy.Particle(theme, index, size, 0, 1);
            // A particle wrapping vertically must be invisible at that boundary.
            Assert.True(Vector2.Distance(before.Center, after.Center) < .1f || MathF.Max(before.Opacity, after.Opacity) < .001f);
        }
    }

    [Fact]
    public void ClippingSkipsOnlyFullyHiddenBackgrounds()
    {
        var min = new Vector2(20, 30);
        var max = new Vector2(120, 80);
        Assert.True(MeterHeaderMotionPolicy.IntersectsClip(new(30, 40), new(20), min, max));
        Assert.True(MeterHeaderMotionPolicy.IntersectsClip(new(10, 40), new(20), min, max));
        Assert.True(MeterHeaderMotionPolicy.IntersectsClip(new(100, 60), new(40), min, max));
        Assert.True(MeterHeaderMotionPolicy.IntersectsClip(Vector2.Zero, new(200), min, max));
        Assert.False(MeterHeaderMotionPolicy.IntersectsClip(Vector2.Zero, new(20, 30), min, max));
        Assert.False(MeterHeaderMotionPolicy.IntersectsClip(max, new(100), min, max));
        Assert.False(MeterHeaderMotionPolicy.IntersectsClip(min, new(100), max, min));
        Assert.False(MeterHeaderMotionPolicy.IntersectsClip(min, new(100), min, min));
        Assert.False(MeterHeaderMotionPolicy.IntersectsClip(new(float.NaN), new(100), min, max));
        Assert.False(MeterHeaderMotionPolicy.IntersectsClip(min, new(float.NaN), min, max));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void LowDetailContoursKeepTheSameShapeAndBounds(BetterDeathsTheme theme)
    {
        var size = new Vector2(32, 24);
        for (var segment = 0; segment <= 6; segment++)
        {
            var p = MeterHeaderMotionPolicy.Contour(theme, size, 0, segment, .3f, 6);
            Assert.InRange(p.X, 0, size.X);
            Assert.InRange(p.Y, 0, size.Y);
        }
        Assert.Equal(MeterHeaderMotionPolicy.Contour(theme, size, 0, 0, .3f),
            MeterHeaderMotionPolicy.Contour(theme, size, 0, 0, .3f, 6));
        Assert.Equal(MeterHeaderMotionPolicy.Contour(theme, size, 0, 32, .3f),
            MeterHeaderMotionPolicy.Contour(theme, size, 0, 6, .3f, 6));
    }

    [Fact]
    public void InvalidInputsRemainFinite()
    {
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1 })
        {
            Assert.Equal(0, MeterHeaderMotionPolicy.Phase(value, true, false));
            Assert.False(MeterHeaderMotionPolicy.ValidSize(new Vector2(value, 110)));
            Assert.Equal(default, MeterHeaderMotionPolicy.Particle(BetterDeathsTheme.Sakura, 0, new Vector2(value, 110), 0, 1));
            Assert.Equal(default, MeterHeaderMotionPolicy.Motif(BetterDeathsTheme.Sakura, 0, new Vector2(value, 110), 0, 1));
            Assert.Equal(0, MeterHeaderMotionPolicy.MotifCount(new Vector2(value, 110), 1, false));
            Assert.Equal(0, MeterHeaderMotionPolicy.DetailVisibility(Vector2.Zero, new Vector2(value, 110), false));
            Assert.True(float.IsFinite(MeterThemeStyle.Layout(value, value, value, value).TextWidth));
        }
    }

    [Theory]
    [InlineData(BetterDeathsTheme.Sakura, (int)MeterBackgroundKind.Petals)]
    [InlineData(BetterDeathsTheme.Soda, (int)MeterBackgroundKind.Fizz)]
    [InlineData(BetterDeathsTheme.Pepsi, (int)MeterBackgroundKind.Fizz)]
    [InlineData(BetterDeathsTheme.Confection, (int)MeterBackgroundKind.Candy)]
    [InlineData(BetterDeathsTheme.Fanta, (int)MeterBackgroundKind.Citrus)]
    [InlineData(BetterDeathsTheme.Wisteria, (int)MeterBackgroundKind.Wisteria)]
    [InlineData(BetterDeathsTheme.Lavender, (int)MeterBackgroundKind.Liquid)]
    [InlineData(BetterDeathsTheme.Mint, (int)MeterBackgroundKind.Mint)]
    [InlineData(BetterDeathsTheme.Peach, (int)MeterBackgroundKind.Peach)]
    [InlineData(BetterDeathsTheme.Grape, (int)MeterBackgroundKind.Grapes)]
    [InlineData(BetterDeathsTheme.Cloud, (int)MeterBackgroundKind.Fabric)]
    [InlineData(BetterDeathsTheme.Sky, (int)MeterBackgroundKind.Clouds)]
    public void ThemeScenesReflectTheirSubject(BetterDeathsTheme theme, int kind) =>
        Assert.Equal((MeterBackgroundKind)kind, MeterThemeStyle.Profile(theme).Background);

    [Theory]
    [MemberData(nameof(Themes))]
    public void MotifsRemainBoundedPeriodicAndResponsive(BetterDeathsTheme theme)
    {
        foreach (var size in new[] { new Vector2(32, 24), new Vector2(240, 210), new Vector2(780, 110), new Vector2(3840, 220) })
        foreach (var scale in new[] { .5f, 1, 3 })
        for (var i = 0; i < 3; i++)
        {
            var p = MeterHeaderMotionPolicy.Motif(theme, i, size, .25f, scale);
            Assert.Equal(p, MeterHeaderMotionPolicy.Motif(theme, i, size, 1.25f, scale));
            Assert.InRange(p.Center.X, p.Radius, size.X - p.Radius);
            Assert.InRange(p.Center.Y, p.Radius, size.Y - p.Radius);
            Assert.InRange(p.Radius, 0, 30 * scale);
            Assert.InRange(p.Opacity, 0, 1);
            Assert.InRange(MeterHeaderMotionPolicy.DetailVisibility(p.Center, size, false), .18f, 1);
            Assert.Equal(1, MeterHeaderMotionPolicy.MotifCount(size, scale, true));
            Assert.InRange(MeterHeaderMotionPolicy.MotifCount(size, scale, false), 1, 3);
            var before = MeterHeaderMotionPolicy.Motif(theme, i, size, .99999f, scale);
            var after = MeterHeaderMotionPolicy.Motif(theme, i, size, 0, scale);
            Assert.True(Vector2.Distance(before.Center, after.Center) < .01f);
        }
        Assert.Equal(1, MeterHeaderMotionPolicy.MotifCount(new(300, 110), 1, false));
        Assert.Equal(3, MeterHeaderMotionPolicy.MotifCount(new(780, 110), 1, false));
    }

    [Fact]
    public void HeadingAndTimerHaveLessBackgroundDetail()
    {
        var size = new Vector2(780, 110);
        var middle = MeterHeaderMotionPolicy.DetailVisibility(new(450, 55), size, false);
        Assert.True(MeterHeaderMotionPolicy.DetailVisibility(new(50, 55), size, false) < middle);
        Assert.True(MeterHeaderMotionPolicy.DetailVisibility(new(750, 55), size, false) < middle);
        Assert.Equal(0, MeterHeaderMotionPolicy.DetailVisibility(new(float.NaN), size, false));
    }

    [Fact]
    public void PoliciesDoNotAllocateOnTheDrawPath()
    {
        static void Sample()
        {
            for (var i = 0; i <= 38; i++)
            {
                var theme = (BetterDeathsTheme)i;
                if (!MeterThemeStyle.IsSupported(theme)) continue;
                _ = MeterThemeStyle.Profile(theme);
                _ = MeterThemeStyle.Row(theme, Vector4.One, Vector4.Zero, 3, i, false);
                var phase = MeterHeaderMotionPolicy.Phase(12, true, false);
                for (var index = 0; index < MeterHeaderMotionPolicy.MotifCount(new(780, 110), 1, false); index++)
                {
                    var motif = MeterHeaderMotionPolicy.Motif(theme, index, new(780, 110), phase, 1);
                    _ = MeterHeaderMotionPolicy.DetailVisibility(motif.Center, new(780, 110), false);
                }
                for (var index = 0; index < MeterHeaderMotionPolicy.ParticleCount; index++)
                    _ = MeterHeaderMotionPolicy.Particle(theme, index, new Vector2(780, 110), phase, 1);
                for (var index = 0; index <= MeterHeaderMotionPolicy.Segments; index++)
                    _ = MeterHeaderMotionPolicy.Contour(theme, new Vector2(780, 110), 0, index, phase);
            }
        }
        Sample();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Sample();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
