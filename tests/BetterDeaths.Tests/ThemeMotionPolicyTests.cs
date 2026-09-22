using System.Numerics;

namespace BetterDeaths;

public sealed class ThemeMotionPolicyTests
{
    [Fact]
    public void EveryStyleHasDistinctBoundedCachedGeometry()
    {
        var fingerprints = new HashSet<string>();
        foreach (var decoration in Enum.GetValues<ThemeDecoration>().Where(d => d != ThemeDecoration.None))
        {
            var paths = ThemeOrnamentLibrary.GetGlyph(decoration);
            Assert.InRange(paths.Length, 1, 8);
            Assert.InRange(paths.ToArray().Sum(p => p.Points.Length), 1, 40);
            var fingerprint = string.Join(";", paths.ToArray().SelectMany(p => p.Points.ToArray()).Select(p => $"{p.X}:{p.Y}"));
            Assert.True(fingerprints.Add(fingerprint), $"Duplicate geometry: {decoration}");
            foreach (var path in paths)
            {
                var points = path.Points.Span;
                foreach (var point in points)
                {
                    Assert.InRange(point.X, 0, 32);
                    Assert.InRange(point.Y, 0, 32);
                }
                if (!path.Filled) continue;
                for (var i = 0; i < points.Length; i++)
                {
                    var a = points[(i + 1) % points.Length] - points[i];
                    var b = points[(i + 2) % points.Length] - points[(i + 1) % points.Length];
                    Assert.True(a.X * b.Y - a.Y * b.X > 0, $"Non-convex or reversed: {decoration}");
                }
            }
        }
    }

    [Fact]
    public void EveryMotionRemainsFiniteInsideSmallAndLargeWindows()
    {
        Vector2[] sizes = [new(32, 24), new(160, 80), new(700, 320), new(3840, 2160)];
        double[] times = [0, 0.1, 10, 38, 64, 3600, 86_400, 31_536_000];
        foreach (var decoration in Enum.GetValues<ThemeDecoration>().Where(d => d != ThemeDecoration.None))
        foreach (var size in sizes)
        foreach (var time in times)
        foreach (var scale in new[] { 0.5f, 1f, 3f })
        for (var index = 0; index < ThemeMotionPolicy.MaxParticles; index++)
        {
            var pose = ThemeMotionPolicy.Sample(decoration, index, size, time, true, scale);
            var radius = pose.Size * 0.70711f;
            Assert.InRange(pose.Center.X - radius, -0.001f, size.X);
            Assert.InRange(pose.Center.Y - radius, -0.001f, size.Y);
            Assert.InRange(pose.Center.X + radius, 0, size.X + 0.001f);
            Assert.InRange(pose.Center.Y + radius, 0, size.Y + 0.001f);
            Assert.InRange(pose.Opacity, 0, 1);
            Assert.True(float.IsFinite(pose.Rotation));
        }
    }

    [Fact]
    public void ReducedMotionIsStationaryAndAnimationActuallyMoves()
    {
        foreach (var decoration in Enum.GetValues<ThemeDecoration>().Where(d => d != ThemeDecoration.None))
        {
            var size = new Vector2(700, 300);
            var still = ThemeMotionPolicy.Sample(decoration, 2, size, 0, false);
            Assert.Equal(still, ThemeMotionPolicy.Sample(decoration, 2, size, 60, false));
            Assert.Equal(still, ThemeMotionPolicy.Sample(decoration, 2, size, double.NaN, true));
            Assert.NotEqual(ThemeMotionPolicy.Sample(decoration, 2, size, 2, true),
                ThemeMotionPolicy.Sample(decoration, 2, size, 8, true));
            Assert.Equal(1, still.Opacity);
        }
    }

    [Fact]
    public void ZeroStrengthPlainThemesAndInvalidSizesProduceNoEffects()
    {
        var valid = new Vector2(700, 400);
        Assert.Equal(0, ThemeMotionPolicy.GetCount(ThemeDecoration.Sweets, valid, 0, false));
        Assert.Equal(0, ThemeMotionPolicy.GetCount(ThemeDecoration.None, valid, 1, false));
        Assert.Equal(0, ThemeMotionPolicy.GetCount((ThemeDecoration)999, valid, 1, false));
        foreach (var size in new[] { Vector2.Zero, new Vector2(float.NaN, 400), new Vector2(400, float.PositiveInfinity), new Vector2(-1, 20) })
        {
            Assert.Equal(0, ThemeMotionPolicy.GetCount(ThemeDecoration.Sweets, size, 1, false));
            Assert.Equal(default, ThemeMotionPolicy.Sample(ThemeDecoration.Sweets, 0, size, 1, true));
        }
        Assert.Equal(default, ThemeMotionPolicy.Sample(ThemeDecoration.Sweets, -1, valid, 1, true));
        Assert.Equal(default, ThemeMotionPolicy.Sample(ThemeDecoration.Sweets, ThemeMotionPolicy.MaxParticles, valid, 1, true));
    }

    [Theory]
    [InlineData(float.NaN, 0.55f)]
    [InlineData(float.PositiveInfinity, 0.55f)]
    [InlineData(-10, 0)]
    [InlineData(10, 1)]
    [InlineData(0.35f, 0.35f)]
    public void StrengthIsNormalized(float value, float expected) => Assert.Equal(expected, ThemeMotionPolicy.NormalizeStrength(value));

    [Fact]
    public void DrawBudgetDoesNotGrowUnboundedWithResolution()
    {
        Assert.Equal(6, ThemeMotionPolicy.GetCount(ThemeDecoration.Lanterns, new Vector2(20_000), 1, false));
        Assert.Equal(3, ThemeMotionPolicy.GetCount(ThemeDecoration.Lanterns, new Vector2(20_000), 1, true));
    }

    [Fact]
    public void StyleThemeDensityRisesWithStrengthWithoutExceedingItsBudget()
    {
        var size = new Vector2(20_000);
        Assert.Equal(1, ThemeMotionPolicy.GetCount(ThemeDecoration.Lanterns, size, .1f, false));
        Assert.Equal(4, ThemeMotionPolicy.GetCount(ThemeDecoration.Lanterns, size, .55f, false));
        Assert.Equal(6, ThemeMotionPolicy.GetCount(ThemeDecoration.Lanterns, size, 1, false));
        Assert.Equal(1, ThemeMotionPolicy.GetCount(ThemeDecoration.Lanterns, size, .1f, true));
        Assert.Equal(2, ThemeMotionPolicy.GetCount(ThemeDecoration.Lanterns, size, .55f, true));
        Assert.Equal(3, ThemeMotionPolicy.GetCount(ThemeDecoration.Lanterns, size, 1, true));
    }

    [Fact]
    public void MotionAndGeometryLookupsAllocateNothingPerFrame()
    {
        var size = new Vector2(700, 400);
        _ = ThemeMotionPolicy.Sample(ThemeDecoration.Sweets, 0, size, 1, true);
        _ = ThemeOrnamentLibrary.GetGlyph(ThemeDecoration.Sweets).Length;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sum = 0f;
        for (var frame = 0; frame < 1000; frame++)
        for (var i = 0; i < ThemeMotionPolicy.MaxParticles; i++)
        {
            sum += ThemeMotionPolicy.Sample(ThemeDecoration.Sweets, i, size, frame / 60.0, true).Center.X;
            _ = ThemeOrnamentLibrary.GetGlyph(ThemeDecoration.Sweets).Length;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(sum > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void EdgeMotionKeepsTheCenterClearAndIsNotMirrored()
    {
        var size = new Vector2(700, 400);
        foreach (var decoration in Enum.GetValues<ThemeDecoration>().Where(d => d != ThemeDecoration.None))
        for (var frame = 0; frame < 120; frame++)
        {
            var left = 0;
            var right = 0;
            for (var i = 0; i < ThemeMotionPolicy.MaxParticles; i++)
            {
                var pose = ThemeMotionPolicy.Sample(decoration, i, size, frame * 0.5, true);
                var radius = pose.Size * 0.70711f;
                Assert.True(pose.Center.X + radius <= size.X * 0.18f || pose.Center.X - radius >= size.X * 0.82f);
                if (pose.Center.X < size.X * 0.5f) left++; else right++;
            }
            Assert.True(right > left);
        }
    }

    [Fact]
    public void CompleteSignatureStaysWithinItsReservedRegionWhileMoving()
    {
        foreach (var decoration in Enum.GetValues<ThemeDecoration>().Where(d => d != ThemeDecoration.None))
        foreach (var size in new[] { new Vector2(16), new Vector2(58, 48), new Vector2(116, 96) })
        for (var i = 0; i < 100; i++)
        {
            var pose = ThemeMotionPolicy.SampleSignature(decoration, size, i * .2, true);
            var radius = pose.Size * .5f * (MathF.Abs(MathF.Cos(pose.Rotation)) + MathF.Abs(MathF.Sin(pose.Rotation)));
            Assert.InRange(pose.Center.X - radius, 0, size.X);
            Assert.InRange(pose.Center.Y - radius, 0, size.Y);
            Assert.InRange(pose.Center.X + radius, 0, size.X);
            Assert.InRange(pose.Center.Y + radius, 0, size.Y);
            Assert.Equal(ThemeMotionPolicy.SampleSignature(decoration, size, 0, false),
                ThemeMotionPolicy.SampleSignature(decoration, size, i * .2, false));
        }
        Assert.Equal(default, ThemeMotionPolicy.SampleSignature(ThemeDecoration.None, new Vector2(58), 1, true));
        Assert.Equal(default, ThemeMotionPolicy.SampleSignature(ThemeDecoration.Sweets, Vector2.Zero, 1, true));
    }
}
