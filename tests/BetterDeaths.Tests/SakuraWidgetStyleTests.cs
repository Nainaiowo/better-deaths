using System.Numerics;

namespace BetterDeaths;

public sealed class SakuraWidgetStyleTests
{
    [Fact]
    public void DarkSummaryUsesALighterRoseWithoutLosingItsTextAccents()
    {
        var canvas = new Vector4(.08f, .10f, .09f, 1);
        var summary = SakuraWidgetStyle.Summary(canvas);
        Assert.True(summary.X > summary.Z && summary.Z > summary.Y);
        Assert.True(summary.X > canvas.X && summary.Y > canvas.Y && summary.Z > canvas.Z);
        foreach (var text in new[] { new Vector4(1, .64f, .77f, 1), new Vector4(.80f, .73f, .77f, 1), new Vector4(.66f, .90f, .79f, 1) })
            Assert.True(WidgetSurfacePolicy.Contrast(text, summary) >= 4.5);
    }

    [Fact]
    public void RowsAndSummarySupportDarkAndCustomLightCanvases()
    {
        foreach (var role in new byte[] { 0, 1, 2, 3, 4, 255 })
        foreach (var detail in new[] { false, true })
        foreach (var text in new[] { Vector4.Zero, Vector4.One, new Vector4(.66f, .9f, .79f, 1) })
        for (var i = 0; i <= 20; i++)
        {
            var canvas = new Vector4(i / 20f, i / 20f, i / 20f, .3f);
            var row = SakuraWidgetStyle.Row(canvas, text, role, i, detail);
            Assert.True(WidgetSurfacePolicy.Contrast(row.Text, row.Background) >= 4.5);
            Assert.True(WidgetSurfacePolicy.Contrast(row.Accent, row.Background) >= 4.5);
            Assert.Equal(1, row.Text.W);
            foreach (var header in new[] { SakuraWidgetStyle.Summary(canvas), SakuraWidgetStyle.ColumnHeader(canvas) })
                Assert.True(WidgetSurfacePolicy.Contrast(WidgetSurfacePolicy.ReadableText(text, header), header) >= 4.5);
        }
    }

    [Fact]
    public void DistinctRolesDoNotUseDamageDependentFills()
    {
        var canvas = new Vector4(.08f, .10f, .09f, 1);
        var colors = new HashSet<Vector4>();
        foreach (byte role in new byte[] { 0, 1, 2, 4 })
            Assert.True(colors.Add(SakuraWidgetStyle.Row(canvas, Vector4.One, role, 1, false).Background));
        Assert.Equal(SakuraWidgetStyle.Row(canvas, Vector4.One, 0, 1, false),
            SakuraWidgetStyle.Row(canvas, Vector4.One, 255, 1, false));
    }

    [Fact]
    public void MeleeAndRangedShareTheSamePaletteOnPlayerAndAbilityRows()
    {
        foreach (var canvas in new[] { new Vector4(.08f, .10f, .09f, 1), Vector4.One })
        foreach (var detail in new[] { false, true })
        for (var index = 0; index < 8; index++)
            Assert.Equal(SakuraWidgetStyle.Row(canvas, Vector4.One, 2, index, detail),
                SakuraWidgetStyle.Row(canvas, Vector4.One, 3, index, detail));
    }

    [Fact]
    public void RoleTintsStayDistinctAcrossLightAndDarkSurfaces()
    {
        foreach (var canvas in new[] { new Vector4(.08f, .10f, .09f, 1), Vector4.One })
        foreach (var detail in new[] { false, true })
        for (var index = 0; index < 2; index++)
        foreach (byte first in new byte[] { 1, 2, 4 })
        foreach (byte second in new byte[] { 1, 2, 4 })
        {
            var a = SakuraWidgetStyle.Row(canvas, Vector4.One, first, index, detail).Background;
            var b = SakuraWidgetStyle.Row(canvas, Vector4.One, second, index, detail).Background;
            if (first == second)
                Assert.Equal(a, b);
            else
            {
                var distance = Vector4.Distance(a, b);
                Assert.True(distance >= (detail ? .04f : .10f) && distance <= .35f,
                    $"Roles {first}/{second}, light={canvas == Vector4.One}, detail={detail}: {distance}");
            }
        }
    }

    [Fact]
    public void FramePoliciesDoNotAllocate()
    {
        _ = SakuraWidgetStyle.Row(Vector4.One, Vector4.Zero, 2, 1, false);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = SakuraWidgetStyle.Row(Vector4.One, Vector4.Zero, (byte)(i % 5), i, false);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
