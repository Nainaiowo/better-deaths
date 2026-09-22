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
    public void MeleeAndRangedMatchWhileRolesAndAdjacentShadesStayDistinct(BetterDeathsTheme theme)
    {
        foreach (var canvas in new[] { new Vector4(.08f, .10f, .09f, 1), Vector4.One })
        foreach (var detail in new[] { false, true })
        for (var index = 0; index < 8; index++)
        {
            var melee = MeterThemeStyle.Row(theme, canvas, Vector4.One, 2, index, detail);
            Assert.Equal(melee, MeterThemeStyle.Row(theme, canvas, Vector4.One, 3, index, detail));
            foreach (byte role in new byte[] { 1, 4 })
                Assert.InRange(Vector4.Distance(melee.Background,
                    MeterThemeStyle.Row(theme, canvas, Vector4.One, role, index, detail).Background),
                    detail ? .04f : .10f, .35f);
            var nextShade = MeterThemeStyle.Row(theme, canvas, Vector4.One, 2, index + 1, detail);
            Assert.True(Vector4.Distance(melee.Background, nextShade.Background) >= (detail ? .02f : .05f));
            Assert.Equal(melee, MeterThemeStyle.Row(theme, canvas, Vector4.One, 2, index + 2, detail));
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
    [InlineData(780, 1, 180, 50, 90, 0, true, false)]
    [InlineData(300, 1, 180, 50, 90, 0, false, false)]
    [InlineData(240, 1, 180, 170, 70, 0, false, true)]
    [InlineData(780, 2, 360, 100, 180, 0, true, false)]
    [InlineData(500, 1, 180, 400, 100, 0, false, true)]
    [InlineData(356, 1, 180, 50, 90, 0, true, false)]
    [InlineData(355, 1, 180, 50, 90, 0, false, false)]
    [InlineData(380, 1, 180, 50, 90, 30, false, false)]
    [InlineData(386, 1, 180, 50, 90, 30, true, false)]
    public void CompactHeaderOnlyWrapsWhenMeasuredContentNeedsIt(float width, float scale, float heading,
        float first, float second, float controls, bool inline, bool stack)
    {
        var layout = MeterThemeStyle.Layout(width, scale, heading, first, second, controls);
        Assert.Equal(width, layout.TextWidth);
        Assert.Equal(inline, layout.InlineHeading);
        Assert.Equal(stack, layout.StackMetrics);
        Assert.InRange(layout.MetricsWidth, 0, width);
        if (inline)
            Assert.True(heading + controls + layout.MetricsWidth + 24 * scale <= width);
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
                var kind = MeterThemeStyle.Profile(theme).Background;
                var bubbles = kind is MeterBackgroundKind.Fizz or MeterBackgroundKind.Citrus or MeterBackgroundKind.Liquid or
                    MeterBackgroundKind.Bubblegum or MeterBackgroundKind.Depth;
                Assert.InRange(p.Center.X, 0, size.X);
                Assert.InRange(p.Center.Y, 0, size.Y);
                Assert.InRange(p.Radius, bubbles ? 5 : 2, bubbles ? 9 : 6);
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
    public void EffectStrengthIncreasesDetailDensityWithinExistingCaps()
    {
        foreach (var maximum in new[] { 1, 2, 3, 5, 6, MeterHeaderMotionPolicy.ParticleCount })
        {
            Assert.Equal(0, MeterHeaderMotionPolicy.DensityCount(maximum, 0));
            Assert.Equal(maximum, MeterHeaderMotionPolicy.DensityCount(maximum, 1));
            var previous = 0;
            for (var step = 1; step <= 20; step++)
            {
                var count = MeterHeaderMotionPolicy.DensityCount(maximum, step / 20f);
                Assert.InRange(count, previous, maximum);
                previous = count;
            }
        }

        Assert.Equal(1, MeterHeaderMotionPolicy.DensityCount(8, .1f));
        Assert.Equal(5, MeterHeaderMotionPolicy.DensityCount(8, .55f));
        Assert.Equal(8, MeterHeaderMotionPolicy.DensityCount(100, 1));
        Assert.Equal(0, MeterHeaderMotionPolicy.DensityCount(-1, 1));
        Assert.Equal(1, MeterHeaderMotionPolicy.DensityWeight(0, 8, .55f));
        Assert.InRange(MeterHeaderMotionPolicy.DensityWeight(4, 8, .55f), .39f, .41f);
        Assert.Equal(0, MeterHeaderMotionPolicy.DensityWeight(5, 8, .55f));
        Assert.Equal(0, MeterHeaderMotionPolicy.DensityWeight(-1, 8, 1));
        Assert.Equal(0, MeterHeaderMotionPolicy.DensityWeight(8, 8, 1));
    }

    [Fact]
    public void BubblesRiseOnDifferentCurvedPathsAndPetalsFall()
    {
        var size = new Vector2(780, 60);
        var bubbleStart = MeterHeaderMotionPolicy.Particle(BetterDeathsTheme.Soda, 0, size, .1f, 1);
        var bubbleEnd = MeterHeaderMotionPolicy.Particle(BetterDeathsTheme.Soda, 0, size, .13f, 1);
        var otherStart = MeterHeaderMotionPolicy.Particle(BetterDeathsTheme.Soda, 1, size, .1f, 1);
        var otherEnd = MeterHeaderMotionPolicy.Particle(BetterDeathsTheme.Soda, 1, size, .13f, 1);
        Assert.True(bubbleEnd.Center.Y < bubbleStart.Center.Y);
        Assert.True(MathF.Abs(bubbleEnd.Center.X - bubbleStart.Center.X) > 1);
        Assert.True(MathF.Abs((bubbleEnd.Center.Y - bubbleStart.Center.Y) -
            (otherEnd.Center.Y - otherStart.Center.Y)) > 1);
        Assert.InRange(bubbleStart.Radius, 5, 9);

        var petalStart = MeterHeaderMotionPolicy.Particle(BetterDeathsTheme.Sakura, 0, size, .1f, 1);
        var petalEnd = MeterHeaderMotionPolicy.Particle(BetterDeathsTheme.Sakura, 0, size, .13f, 1);
        Assert.True(petalEnd.Center.Y > petalStart.Center.Y);
        Assert.True(MathF.Abs(petalEnd.Center.X - petalStart.Center.X) > 0);
    }

    [Fact]
    public void CloudsDriftHorizontallyAndRibbonLanesMoveIndependently()
    {
        var size = new Vector2(780, 60);
        var cloudStart = MeterHeaderMotionPolicy.Particle(BetterDeathsTheme.Sky, 0, size, .1f, 1);
        var cloudEnd = MeterHeaderMotionPolicy.Particle(BetterDeathsTheme.Sky, 0, size, .2f, 1);
        Assert.True(cloudEnd.Center.X > cloudStart.Center.X);
        Assert.True(MathF.Abs(cloudEnd.Center.Y - cloudStart.Center.Y) < size.Y * .1f);

        var first = MeterHeaderMotionPolicy.Contour(BetterDeathsTheme.Confection, size, 0, 10, .1f);
        var firstLater = MeterHeaderMotionPolicy.Contour(BetterDeathsTheme.Confection, size, 0, 10, .2f);
        var second = MeterHeaderMotionPolicy.Contour(BetterDeathsTheme.Confection, size, 1, 10, .1f);
        var secondLater = MeterHeaderMotionPolicy.Contour(BetterDeathsTheme.Confection, size, 1, 10, .2f);
        Assert.True(MathF.Abs((firstLater.Y - first.Y) - (secondLater.Y - second.Y)) > .1f);
        Assert.Equal(first, MeterHeaderMotionPolicy.Contour(BetterDeathsTheme.Confection, size, 0, 10, 1.1f));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ParticleThemesDoNotShareOneTrajectory(BetterDeathsTheme theme)
    {
        var kind = MeterThemeStyle.Profile(theme).Background;
        if (kind is MeterBackgroundKind.Satin or MeterBackgroundKind.Ribbons or MeterBackgroundKind.Fabric or
            MeterBackgroundKind.Marble or MeterBackgroundKind.Brushed or MeterBackgroundKind.Daylight)
            return;

        var size = new Vector2(780, 60);
        var movement = new List<Vector2>();
        for (var index = 0; index < MeterHeaderMotionPolicy.ParticleCount; index++)
        {
            var before = MeterHeaderMotionPolicy.Particle(theme, index, size, .2f, 1);
            var after = MeterHeaderMotionPolicy.Particle(theme, index, size, .205f, 1);
            var delta = after.Center - before.Center;
            if (delta.Length() < size.Y * .2f)
                movement.Add(delta);
        }

        Assert.True(movement.Count >= 3);
        Assert.True(movement.Max(p => p.X) - movement.Min(p => p.X) > .1f ||
            movement.Max(p => p.Y) - movement.Min(p => p.Y) > .1f);
        if (kind is MeterBackgroundKind.Clouds or MeterBackgroundKind.Cotton)
            Assert.All(movement, p => Assert.True(p.X > 0));
        else if (kind != MeterBackgroundKind.Moonlight)
        {
            var rising = kind is MeterBackgroundKind.Fizz or MeterBackgroundKind.Citrus or MeterBackgroundKind.Liquid or
                MeterBackgroundKind.Bubblegum or MeterBackgroundKind.Depth or MeterBackgroundKind.Heat or MeterBackgroundKind.Spice;
            Assert.All(movement, p => Assert.True(rising ? p.Y < 0 : p.Y > 0));
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
            Assert.Equal(0, MeterHeaderMotionPolicy.DetailVisibility(Vector2.Zero, new Vector2(value, 110), false));
            var layout = MeterThemeStyle.Layout(value, value, value, value, value, value);
            Assert.True(float.IsFinite(layout.TextWidth));
            Assert.True(float.IsFinite(layout.MetricsWidth));
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
    public void SmallDetailsStayAwayFromHeaderText(BetterDeathsTheme theme)
    {
        foreach (var size in new[] { new Vector2(300, 60), new Vector2(780, 40), new Vector2(3840, 220) })
        foreach (var scale in new[] { .5f, 1, 3 })
        for (var i = 0; i < MeterHeaderMotionPolicy.ParticleCount; i++)
        {
            var p = MeterHeaderMotionPolicy.Particle(theme, i, size, .25f, scale);
            Assert.Equal(p, MeterHeaderMotionPolicy.Particle(theme, i, size, 1.25f, scale));
            Assert.InRange(p.Center.X / size.X, size.X < 7 * size.Y ? .72f : .26f,
                size.X < 7 * size.Y ? .96f : .8f);
            Assert.InRange(p.Opacity, 0, 1);
            Assert.InRange(MeterHeaderMotionPolicy.DetailVisibility(p.Center, size, false), 0, 1);
        }
    }

    [Fact]
    public void HeadingAndTimerHaveLessBackgroundDetail()
    {
        var size = new Vector2(780, 110);
        var middle = MeterHeaderMotionPolicy.DetailVisibility(new(450, 55), size, false);
        Assert.Equal(0, MeterHeaderMotionPolicy.DetailVisibility(new(50, 55), size, false));
        Assert.Equal(0, MeterHeaderMotionPolicy.DetailVisibility(new(750, 55), size, false));
        Assert.True(middle > 0);
        Assert.Equal(0, MeterHeaderMotionPolicy.DetailVisibility(new(150, 30), new(300, 60), false));
        Assert.True(MeterHeaderMotionPolicy.DetailVisibility(new(255, 30), new(300, 60), false) > 0);
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
                for (var index = 0; index < MeterHeaderMotionPolicy.ParticleCount; index++)
                {
                    var particle = MeterHeaderMotionPolicy.Particle(theme, index, new Vector2(780, 110), phase, 1);
                    _ = MeterHeaderMotionPolicy.DetailVisibility(particle.Center, new(780, 110), false);
                }
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
