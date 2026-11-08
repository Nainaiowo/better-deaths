using System.Numerics;
using BetterDeaths.PartyCooldowns;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownGridLayoutTests
{
    [Theory]
    [InlineData(PartyCooldownSide.Left)]
    [InlineData(PartyCooldownSide.Right)]
    public void DisabledCustomRowsKeepTheExistingSingleRow(PartyCooldownSide side)
    {
        var row = new PartyCooldownRow(1, 0, new(200, 100), new(300, 120), 1);
        Assert.Equal(new Vector2(188, 20), PartyCooldownLayout.GetSize(8, 20, 4));
        Assert.Equal(new Vector2(side == PartyCooldownSide.Left ? 6 : 306, 100),
            PartyCooldownLayout.GetOrigin(row, 8, side, 20, 4, Vector2.Zero));
        for (var index = 0; index < 8; index++)
            Assert.Equal(new Vector2(index * 24, 0), PartyCooldownLayout.GetIconOffset(0, index, [8], side, 20, 4));
    }

    [Theory]
    [InlineData(PartyCooldownSide.Left)]
    [InlineData(PartyCooldownSide.Right)]
    public void CustomRowsExtendDownwardAndShortRowsAlignTowardTheName(PartyCooldownSide side)
    {
        var row = new PartyCooldownRow(1, 0, new(200, 100), new(300, 120), 1);
        int[] counts = [3, 1, 2];
        Assert.Equal(new Vector2(68, 68), PartyCooldownLayout.GetSize(counts, 20, 4));
        Assert.Equal(new Vector2(side == PartyCooldownSide.Left ? 126 : 306, 100),
            PartyCooldownLayout.GetOrigin(row, counts, side, 20, 4, Vector2.Zero));
        Assert.Equal(new Vector2(side == PartyCooldownSide.Left ? 48 : 0, 24),
            PartyCooldownLayout.GetIconOffset(1, 0, counts, side, 20, 4));
        Assert.Equal(new Vector2(side == PartyCooldownSide.Left ? 24 : 0, 48),
            PartyCooldownLayout.GetIconOffset(2, 0, counts, side, 20, 4));
        Assert.Equal(new Vector2(side == PartyCooldownSide.Left ? 48 : 24, 48),
            PartyCooldownLayout.GetIconOffset(2, 1, counts, side, 20, 4));
    }

    [Fact]
    public void EmptyMiddleRowsArePreservedButEmptyTrailingRowsDoNotAddBlankHeight()
    {
        Assert.Equal(Vector2.Zero, PartyCooldownLayout.GetSize([0, 0, 0], 72, 3));
        Assert.Equal(new Vector2(72, 72), PartyCooldownLayout.GetSize([1, 0, 0], 72, 3));
        Assert.Equal(new Vector2(147, 222), PartyCooldownLayout.GetSize([1, 0, 2], 72, 3));
        Assert.Equal(new Vector2(147, 147), PartyCooldownLayout.GetSize([0, 2, 0], 72, 3));
        Assert.Equal(new Vector2(147, 222), PartyCooldownLayout.GetSize([2, 2, 2, 12], 72, 3));
        Assert.Equal(new Vector2(897, 72), PartyCooldownLayout.GetSize([12], 72, 3));
        Assert.Equal(Vector2.Zero, PartyCooldownLayout.GetIconOffset(-1, 0, [3], PartyCooldownSide.Left, 20, 4));
        Assert.Equal(Vector2.Zero, PartyCooldownLayout.GetIconOffset(0, 3, [3], PartyCooldownSide.Left, 20, 4));
        Assert.Equal(Vector2.Zero, PartyCooldownLayout.GetIconOffset(1, 0, [3, 0], PartyCooldownSide.Left, 20, 4));
    }

    [Fact]
    public void EveryTwelveAbilityDistributionFitsItsBoundsWithoutOverlap()
    {
        foreach (var side in Enum.GetValues<PartyCooldownSide>())
            foreach (var size in new[] { 16f, 24, 72 })
                foreach (var gap in new[] { 0f, 3, 12 })
                    for (var first = 0; first <= 12; first++)
                        for (var second = 0; second <= 12 - first; second++)
                            for (var third = 0; third <= 12 - first - second; third++)
                            {
                                int[] counts = [first, second, third];
                                var bounds = PartyCooldownLayout.GetSize(counts, size, gap);
                                Assert.True(bounds.Y <= 3 * size + 2 * gap);
                                var positions = new List<Vector2>();
                                for (var row = 0; row < 3; row++)
                                    for (var column = 0; column < counts[row]; column++)
                                    {
                                        var point = PartyCooldownLayout.GetIconOffset(row, column, counts, side, size, gap);
                                        Assert.InRange(point.X, 0, bounds.X - size);
                                        Assert.InRange(point.Y, 0, bounds.Y - size);
                                        foreach (var previous in positions)
                                            Assert.True(MathF.Abs(previous.X - point.X) >= size || MathF.Abs(previous.Y - point.Y) >= size);
                                        positions.Add(point);
                                    }
                            }
    }

    [Theory]
    [InlineData(PartyCooldownSide.Left)]
    [InlineData(PartyCooldownSide.Right)]
    public void NativeHudScalingMatchesThePreviewGrid(PartyCooldownSide side)
    {
        var preview = new PartyCooldownRow(1, 0, new(100, 50), new(150, 60), 1);
        var native = preview with { NameMin = preview.NameMin * 2, NameMax = preview.NameMax * 2, Scale = 2 };
        var offset = new Vector2(10, -5);
        int[] counts = [2, 4, 1];
        var previewOrigin = PartyCooldownLayout.GetOrigin(preview, counts, side, 20, 4, offset);
        var nativeOrigin = PartyCooldownLayout.GetOrigin(native, counts, side, 20, 4, offset);
        for (var row = 0; row < counts.Length; row++)
            for (var column = 0; column < counts[row]; column++)
            {
                var icon = PartyCooldownLayout.GetIconOffset(row, column, counts, side, 20, 4);
                Assert.Equal((previewOrigin + icon) * 2, nativeOrigin + icon * 2);
            }
    }

    [Fact]
    public void LayoutMathDoesNotAllocateManagedMemory()
    {
        var checksum = Vector2.Zero;
        Span<int> counts = stackalloc int[] { 5, 4, 3 };
        var row = new PartyCooldownRow(1, 0, new(100, 50), new(150, 60), 1);
        for (var index = 0; index < 1000; index++)
            checksum += PartyCooldownLayout.GetIconOffset(2, index % 3, counts, PartyCooldownSide.Left, 24, 3);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10000; index++)
        {
            checksum += PartyCooldownLayout.GetIconOffset(2, index % 3, counts, PartyCooldownSide.Left, 24, 3);
            checksum += PartyCooldownLayout.GetSize(counts, 24, 3);
            checksum += PartyCooldownLayout.GetOrigin(row, counts, PartyCooldownSide.Left, 24, 3, Vector2.Zero);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(checksum.Y > 0);
        Assert.Equal(0, allocated);
    }
}
