using System.Numerics;
using BetterDeaths.PartyCooldowns;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownPreviewLayoutTests
{
    [Theory]
    [InlineData(PartyCooldownSide.Left)]
    [InlineData(PartyCooldownSide.Right)]
    public void PreviewUsesTheSameIconOriginAndSpacingAsTheLiveHud(PartyCooldownSide side)
    {
        var row = new PartyCooldownRow(1, 0, new(44, 0), new(164, 20), 1.5f);
        int[] counts = [2, 4, 1];
        var offset = new Vector2(17, -9);
        var preview = PartyCooldownPreviewLayout.Create(row, counts, side, 32, 7, offset,
            new(0, 0), new(224, 44), new(float.PositiveInfinity));
        Assert.Equal(1, preview.Zoom);
        Assert.Equal(PartyCooldownLayout.GetOrigin(row, counts, side, 32, 7, offset), preview.IconOrigin);
        for (var index = 0; index < counts.Length; index++)
            for (var column = 0; column < counts[index]; column++)
            {
                var position = preview.Shift + preview.IconOrigin +
                    PartyCooldownLayout.GetIconOffset(index, column, counts, side, 32, 7) * row.Scale;
                Assert.InRange(position.X, 12, preview.Size.X - 12 - 32 * row.Scale);
                Assert.InRange(position.Y, 12, preview.Size.Y - 12 - 32 * row.Scale);
            }
    }

    [Theory]
    [InlineData(PartyCooldownSide.Left)]
    [InlineData(PartyCooldownSide.Right)]
    public void EveryIconAndTheWholeFrameFitOnSmallAndLargeScreensAtMaximumSettings(PartyCooldownSide side)
    {
        foreach (var screen in new[] { new Vector2(320, 240), new(800, 400), new(1800, 900) })
            foreach (var scale in new[] { 1f, 1.5f, 4f })
                foreach (var offset in new[] { new Vector2(-300, -300), new(300, 300) })
                {
                    var row = new PartyCooldownRow(1, 0, new(44, 0), new(244, 20), scale);
                    int[] counts = [1, 9, 14];
                    var frameMin = new Vector2(-15, -5);
                    var frameMax = new Vector2(244, 44);
                    var preview = PartyCooldownPreviewLayout.Create(row, counts, side, 72, 12, offset, frameMin, frameMax, screen);
                    Assert.InRange(preview.Zoom, .001f, 1);
                    Assert.InRange(preview.Size.X, 24, screen.X + .001f);
                    Assert.InRange(preview.Size.Y, 24, screen.Y + .001f);
                    AssertInside(preview, frameMin, frameMax - frameMin);
                    for (var index = 0; index < counts.Length; index++)
                        for (var column = 0; column < counts[index]; column++)
                        {
                            var position = preview.IconOrigin +
                                PartyCooldownLayout.GetIconOffset(index, column, counts, side, 72, 12) * scale;
                            AssertInside(preview, position, new Vector2(72 * scale));
                        }
                }
    }

    [Fact]
    public void AnEmptySelectionDoesNotCreateAnIconRegionFromTheSavedOffsets()
    {
        var row = new PartyCooldownRow(1, 0, new(44, 0), new(164, 20), 1);
        var preview = PartyCooldownPreviewLayout.Create(row, [0, 0, 0], PartyCooldownSide.Left, 72, 12,
            new(-300, 300), new(0, 0), new(224, 40), new(float.PositiveInfinity));
        Assert.Equal(new Vector2(248, 64), preview.Size);
        Assert.Equal(new Vector2(12), preview.Shift);
        Assert.Equal(1, preview.Zoom);
    }

    [Fact]
    public void FittingKeepsShrinkingBelowOnePercentAndRestoresFullSizeWhenSpaceReturns()
    {
        var row = new PartyCooldownRow(1, 0, new(44, 0), new(164, 20), 4);
        int[] counts = [PartyCooldownRows.MaximumAbilities];
        var large = PartyCooldownPreviewLayout.Create(row, counts, PartyCooldownSide.Left, 72, 12, new(-300, 300),
            Vector2.Zero, new(224, 40), new(float.PositiveInfinity));
        Assert.Equal(1, large.Zoom);
        var previous = 1f;
        foreach (var maximum in new[] { new Vector2(320, 200), new(80, 60), new(35, 28), new(20, 16), new(1, 1) })
        {
            var small = PartyCooldownPreviewLayout.Create(row, counts, PartyCooldownSide.Left, 72, 12, new(-300, 300),
                Vector2.Zero, new(224, 40), maximum);
            Assert.InRange(small.Size.X, .001f, maximum.X + .001f);
            Assert.InRange(small.Size.Y, .001f, maximum.Y + .001f);
            Assert.True(small.Zoom > 0 && small.Zoom < previous);
            previous = small.Zoom;
        }
        Assert.True(previous < .01f);
        var restored = PartyCooldownPreviewLayout.Create(row, counts, PartyCooldownSide.Left, 72, 12, new(-300, 300),
            Vector2.Zero, new(224, 40), new(float.PositiveInfinity));
        Assert.Equal(large, restored);
    }

    [Theory]
    [InlineData(16, 1)]
    [InlineData(16, .5f)]
    [InlineData(24, .1f)]
    [InlineData(72, .01f)]
    [InlineData(288, .001f)]
    public void TextAndItsBlackOutlineShrinkWithTheIconsWithoutAMinimumPreviewSize(float size, float zoom)
    {
        var timer = PartyCooldownLayout.TimerTextSize(size);
        var charges = PartyCooldownLayout.ChargeTextSize(size);
        Assert.Equal(timer * zoom, PartyCooldownLayout.TimerTextSize(size * zoom, zoom), 5);
        Assert.Equal(charges * zoom, PartyCooldownLayout.ChargeTextSize(size * zoom, zoom), 5);
        Assert.Equal(PartyCooldownLayout.TextOutlineWidth(timer) * zoom,
            PartyCooldownLayout.TextOutlineWidth(timer * zoom, zoom), 5);
        Assert.Equal(PartyCooldownLayout.TextOutlineWidth(charges) * zoom,
            PartyCooldownLayout.TextOutlineWidth(charges * zoom, zoom), 5);
    }

    [Fact]
    public void CompactPreviewLayoutDoesNotAllocateManagedMemory()
    {
        var row = new PartyCooldownRow(1, 0, new(44, 0), new(164, 20), 1);
        Span<int> counts = stackalloc int[] { 3, 4, 5 };
        for (var index = 0; index < 1000; index++)
            _ = PartyCooldownPreviewLayout.Create(row, counts, PartyCooldownSide.Left, 24, 3,
                Vector2.Zero, Vector2.Zero, new(224, 40), new(float.PositiveInfinity));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = Vector2.Zero;
        for (var index = 0; index < 10000; index++)
            checksum += PartyCooldownPreviewLayout.Create(row, counts, PartyCooldownSide.Left, 24, 3,
                Vector2.Zero, Vector2.Zero, new(224, 40), new(float.PositiveInfinity)).Size;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(checksum.X > 0);
        Assert.Equal(0, allocated);
    }

    private static void AssertInside(PartyCooldownPreviewLayout preview, Vector2 min, Vector2 size)
    {
        var scaled = preview.Shift + min * preview.Zoom;
        Assert.InRange(scaled.X, -.001f, preview.Size.X - size.X * preview.Zoom + .001f);
        Assert.InRange(scaled.Y, -.001f, preview.Size.Y - size.Y * preview.Zoom + .001f);
    }
}
