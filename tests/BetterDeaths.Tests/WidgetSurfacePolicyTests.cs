using System.Numerics;

namespace BetterDeaths;

public sealed class WidgetSurfacePolicyTests
{
    [Fact]
    public void StandardOrUnknownStylesLeaveTheOriginalColorsAlone()
    {
        var panel = new Vector4(0.1f, 0.2f, 0.3f, 0.6f);
        var text = new Vector4(0.7f, 0.8f, 0.9f, 0.8f);
        foreach (var style in new[] { WidgetSurfaceStyle.Standard, (WidgetSurfaceStyle)99 })
        {
            Assert.False(WidgetSurfacePolicy.IsExpressive(style));
            Assert.Equal(new WidgetRowColors(panel, text, panel), WidgetSurfacePolicy.GetRowColors(style, 1, panel, text));
        }
    }

    [Fact]
    public void ExpressiveRowsShareOneSurfaceAndUseSmallRoleAccents()
    {
        foreach (var style in Enum.GetValues<WidgetSurfaceStyle>().Where(WidgetSurfacePolicy.IsExpressive))
        {
            var panel = WidgetSurfacePolicy.IsSoft(style) ? new Vector4(0.93f, 0.97f, 0.94f, 1) : new Vector4(0.075f, 0.11f, 0.125f, 1);
            var text = WidgetSurfacePolicy.IsSoft(style) ? new Vector4(0.12f, 0.20f, 0.19f, 1) : Vector4.One;
            var roles = new byte[] { 0, 1, 2, 4 };
            var accents = new HashSet<Vector4>();
            foreach (var role in roles)
            {
                var row = WidgetSurfacePolicy.GetRowColors(style, role, panel, text);
                var ability = WidgetSurfacePolicy.GetRowColors(style, role, panel, text, detail: true);
                Assert.True(accents.Add(row.Accent));
                Assert.Equal(panel, row.Background);
                Assert.Equal(panel, ability.Background);
                Assert.Equal(ability.Text, ability.Accent);
                Assert.Equal(1, row.Background.W);
                Assert.True(WidgetSurfacePolicy.Contrast(row.Text, row.Background) >= 4.5f);
                Assert.True(WidgetSurfacePolicy.Contrast(ability.Text, ability.Background) >= 4.5f);
                Assert.True(WidgetSurfacePolicy.Contrast(row.Accent, row.Background) >= 4.5f);
            }
            Assert.Equal(
                WidgetSurfacePolicy.GetRowColors(style, 2, panel, text),
                WidgetSurfacePolicy.GetRowColors(style, 3, panel, text));
        }
    }

    [Fact]
    public void CustomPanelsAndTextKeepReadablePlayerAndAbilityRows()
    {
        foreach (var style in Enum.GetValues<WidgetSurfaceStyle>().Where(WidgetSurfacePolicy.IsExpressive))
        foreach (var role in new byte[] { 0, 1, 2, 3, 4, 255 })
        foreach (var detail in new[] { false, true })
        foreach (var preferred in new[] { Vector4.Zero, Vector4.One, new Vector4(0.5f, 0.4f, 0.8f, 0.2f) })
        {
            for (var i = 0; i <= 20; i++)
            {
                var panel = new Vector4(i / 20.0f, i / 20.0f, i / 20.0f, 0.35f);
                var row = WidgetSurfacePolicy.GetRowColors(style, role, panel, preferred, detail);
                Assert.True(WidgetSurfacePolicy.Contrast(row.Text, row.Background) >= 4.5f);
                Assert.Equal(1, row.Text.W);
            }
        }
    }

    [Fact]
    public void UnknownRoleUsesNeutralColorWithoutGuessingACombatRole()
    {
        var panel = new Vector4(0.93f, 0.97f, 0.94f, 1);
        var text = new Vector4(0.12f, 0.2f, 0.19f, 1);
        Assert.Equal(
            WidgetSurfacePolicy.GetRowColors(WidgetSurfaceStyle.Bloom, 0, panel, text),
            WidgetSurfacePolicy.GetRowColors(WidgetSurfaceStyle.Bloom, 255, panel, text));
    }

    [Theory]
    [InlineData(300, 80, 150, 24, false)]
    [InlineData(254, 80, 150, 24, false)]
    [InlineData(253, 80, 150, 24, true)]
    [InlineData(180, 90, 90, 24, true)]
    [InlineData(400, 150, 250, 24, true)]
    public void SummaryStacksWhenMetricsCannotFit(float width, float time, float dps, float spacing, bool stack)
    {
        Assert.Equal(stack, WidgetSurfacePolicy.StackSummary(width, time, dps, spacing));
    }

    [Fact]
    public void RowColorCalculationDoesNotAllocate()
    {
        var panel = new Vector4(0.1f, 0.2f, 0.3f, 1);
        _ = WidgetSurfacePolicy.GetRowColors(WidgetSurfaceStyle.Circuit, 1, panel, Vector4.One);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = WidgetSurfacePolicy.GetRowColors(WidgetSurfaceStyle.Circuit, (byte)(i % 5), panel, Vector4.One);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void QuietSurfacesPreserveOpacityAndKeepOnlyASmallColorTint()
    {
        foreach (var original in new[] { new Vector4(1, 0.85f, 0.92f, 0.55f), new Vector4(0.12f, 0.08f, 0.17f, 0.92f) })
        {
            var surface = WidgetSurfacePolicy.GetQuietSurface(original);
            Assert.Equal(original.W, surface.W);
            Assert.True(MathF.Abs(surface.X - surface.Y) < MathF.Abs(original.X - original.Y));
            Assert.True(MathF.Abs(surface.X - surface.Z) < MathF.Abs(original.X - original.Z));
        }
    }

    [Theory]
    [InlineData(420, 1, 20)]
    [InlineData(180, 1, 14.4f)]
    [InlineData(420, 2, 33.6f)]
    [InlineData(float.NaN, 1, 0)]
    [InlineData(-1, 1, 0)]
    public void DecorativeMarginIsBoundedAndLeavesMostWidthForData(float width, float scale, float expected)
    {
        Assert.Equal(expected, WidgetSurfacePolicy.GetEdgeGutter(width, scale), precision: 3);
    }

    [Theory]
    [InlineData(420, 1, 58)]
    [InlineData(279, 1, 0)]
    [InlineData(280, 1, 58)]
    [InlineData(420, 2, 0)]
    [InlineData(600, 2, 116)]
    [InlineData(float.NaN, 1, 0)]
    public void SignatureReservesItsOwnSpaceAndHidesWhenNarrow(float width, float scale, float expected) =>
        Assert.Equal(expected, WidgetSurfacePolicy.GetSignatureWidth(width, scale));

    [Fact]
    public void TonalRowsAddBoundedNeutralDepthWithReadableText()
    {
        foreach (var canvas in new[] { new Vector4(.98f, .94f, .96f, 1), new Vector4(.06f, .08f, .09f, 1) })
        foreach (var style in Enum.GetValues<WidgetSurfaceStyle>().Where(WidgetSurfacePolicy.IsExpressive))
        for (var row = 0; row < 8; row++)
        {
            var overlay = WidgetSurfacePolicy.GetRowOverlay(canvas, row);
            Assert.InRange(overlay.W, .01f, .06f);
            Assert.Equal(overlay.X, overlay.Y);
            Assert.Equal(overlay.X, overlay.Z);
            var surface = WidgetSurfacePolicy.CompositeSurface(canvas, overlay);
            var colors = WidgetSurfacePolicy.GetRowColors(style, 1, surface, Vector4.One);
            Assert.True(WidgetSurfacePolicy.Contrast(colors.Text, surface) >= 4.5f);
            Assert.True(WidgetSurfacePolicy.Contrast(colors.Accent, surface) >= 4.5f);
            Assert.NotEqual(surface, canvas);
        }
    }
}
