using System.Numerics;

namespace BetterDeaths;

public sealed class ConfectionWidgetStyleTests
{
    [Fact]
    public void EveryRoleAndCustomCanvasHasReadablePlayerAndAbilityRows()
    {
        foreach (var role in new byte[] { 0, 1, 2, 3, 4, 255 })
        foreach (var detail in new[] { false, true })
        foreach (var text in new[] { Vector4.Zero, Vector4.One, new Vector4(.63f, .18f, .39f, 1) })
        for (var i = 0; i <= 20; i++)
        {
            var canvas = new Vector4(i / 20f, i / 20f, i / 20f, .3f);
            var row = ConfectionWidgetStyle.Row(canvas, text, role, i, detail);
            Assert.True(WidgetSurfacePolicy.Contrast(row.Text, row.Background) >= 4.5);
            Assert.True(WidgetSurfacePolicy.Contrast(row.Accent, row.Background) >= 4.5);
            Assert.Equal(1, row.Text.W);
            Assert.Equal(1, row.Background.W);
            foreach (var header in new[] { ConfectionWidgetStyle.Summary(canvas), ConfectionWidgetStyle.ColumnHeader(canvas) })
                Assert.True(WidgetSurfacePolicy.Contrast(WidgetSurfacePolicy.ReadableText(text, header), header) >= 4.5);
        }
    }

    [Fact]
    public void RowColorsDoNotDependOnDamageAndHaveDistinctRoleTreatments()
    {
        var canvas = new Vector4(.97f, .96f, .97f, 1);
        var colors = new HashSet<Vector4>();
        foreach (byte role in new byte[] { 0, 1, 2, 4 })
            Assert.True(colors.Add(ConfectionWidgetStyle.Row(canvas, Vector4.Zero, role, 1, false).Background));
        Assert.Equal(ConfectionWidgetStyle.Row(canvas, Vector4.Zero, 0, 1, false),
            ConfectionWidgetStyle.Row(canvas, Vector4.Zero, 255, 1, false));
    }

    [Fact]
    public void MeleeAndRangedShareTheSamePaletteOnPlayerAndAbilityRows()
    {
        foreach (var canvas in new[] { new Vector4(.08f, .10f, .09f, 1), Vector4.One })
        foreach (var detail in new[] { false, true })
        for (var index = 0; index < 8; index++)
            Assert.Equal(ConfectionWidgetStyle.Row(canvas, Vector4.Zero, 2, index, detail),
                ConfectionWidgetStyle.Row(canvas, Vector4.Zero, 3, index, detail));
    }

    [Fact]
    public void RoleTintsRemainCloseAcrossLightAndDarkSurfaces()
    {
        foreach (var canvas in new[] { new Vector4(.08f, .10f, .09f, 1), Vector4.One })
        foreach (var detail in new[] { false, true })
        for (var index = 0; index < 2; index++)
        foreach (byte first in new byte[] { 1, 2, 4 })
        foreach (byte second in new byte[] { 1, 2, 4 })
        {
            var a = ConfectionWidgetStyle.Row(canvas, Vector4.Zero, first, index, detail).Background;
            var b = ConfectionWidgetStyle.Row(canvas, Vector4.Zero, second, index, detail).Background;
            Assert.InRange(Vector4.Distance(a, b), 0, .1f);
        }
    }

    [Fact]
    public void ColorPoliciesDoNotAllocate()
    {
        _ = ConfectionWidgetStyle.Row(Vector4.One, Vector4.Zero, 2, 1, false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = ConfectionWidgetStyle.Row(Vector4.One, Vector4.Zero, (byte)(i % 5), i, false);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
