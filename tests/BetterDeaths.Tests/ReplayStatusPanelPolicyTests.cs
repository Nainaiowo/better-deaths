using System.Numerics;

namespace BetterDeaths;

public sealed class ReplayStatusPanelPolicyTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(-1, false)]
    [InlineData(1016, false)]
    public void PartyHpOnlyIncludesTheLocalParty(int partyIndex, bool expected)
    {
        Assert.Equal(expected, ReplayStatusPanelPolicy.IsLocalPartyIndex(partyIndex));
    }

    [Fact]
    public void StackedHeightReservesMitigationSpace()
    {
        var height = ReplayStatusPanelPolicy.GetStackedHeight(
            hasPartyHp: true,
            partyHpHeight: 333.0f,
            hasMitigation: true,
            mitigationHeight: 444.0f,
            panelGap: 8.0f);

        Assert.Equal(785.0f, height);
    }

    [Theory]
    [InlineData(180, 180, true)]
    [InlineData(320, 280, true)]
    [InlineData(407, 180, true)]
    [InlineData(408, 180, false)]
    [InlineData(1000, 600, false)]
    [InlineData(1080, 820, false)]
    public void NarrowLayoutsUseSpaceBelowTheCanvasInsteadOfCoveringIt(float available, float canvas, bool below)
    {
        Assert.Equal(below, ReplayStatusPanelPolicy.PlaceBelowCanvas(available, canvas, 218, 10));
    }

    [Theory]
    [InlineData(1000, 600, 330, 160)]
    [InlineData(1000, 600, 480, 480)]
    [InlineData(320, 280, 330, 160)]
    [InlineData(180, 180, 480, 480)]
    public void HpAndEffectsStaySeparateAndOutsideTheArenaRegardlessOfCombinedHeight(
        float available, float canvas, float hpHeight, float effectsHeight)
    {
        var cursor = new Vector2(15, 25);
        var below = ReplayStatusPanelPolicy.PlaceBelowCanvas(available, canvas, 218, 10);
        var width = below ? available : 300;
        var group = ReplayStatusPanelPolicy.GetGroupStart(cursor, cursor.X, canvas, available, width, 10, below);
        var effects = group + new Vector2(0, hpHeight + 8);

        Assert.True(group.X >= cursor.X + canvas || group.Y >= cursor.Y + canvas);
        Assert.True(effects.X >= cursor.X + canvas || effects.Y >= cursor.Y + canvas);
        Assert.True(effects.Y >= group.Y + hpHeight + 8);
        Assert.Equal(hpHeight + effectsHeight + 8,
            ReplayStatusPanelPolicy.GetStackedHeight(true, hpHeight, true, effectsHeight, 8));
    }

    [Fact]
    public void TurningOnHpDoesNotChangeWhetherEffectsUseTheOutsideSidebar()
    {
        Assert.False(ReplayStatusPanelPolicy.PlaceBelowCanvas(900, 500, 218, 10));
        var effectsOnly = ReplayStatusPanelPolicy.GetGroupStart(new(10, 20), 10, 500, 900, 300, 10, false);
        var hpAndEffects = effectsOnly + new Vector2(0, 480 + 8);
        Assert.Equal(effectsOnly.X, hpAndEffects.X);
        Assert.True(hpAndEffects.X > 10 + 500);
    }

    [Fact]
    public void MoreIndicatorCountsOnlyEntriesStillBelowTheScrolledViewport()
    {
        float[] heights = [24, 38, 24, 38, 24];
        const float viewportHeight = 70;
        var maximumScroll = heights.Sum() + (heights.Length - 1) * 5 - viewportHeight;

        Assert.Equal(3, HiddenBelow(heights, 0, viewportHeight));
        Assert.Equal(2, HiddenBelow(heights, 50, viewportHeight));
        Assert.Equal(0, HiddenBelow(heights, maximumScroll, viewportHeight));
        Assert.Equal(3, HiddenBelow(heights, 0, viewportHeight));
        Assert.Equal(0, HiddenBelow([], 0, viewportHeight));
    }

    [Theory]
    [InlineData(-40, 24, false, false)]
    [InlineData(-10, 24, true, false)]
    [InlineData(0, 24, true, false)]
    [InlineData(60, 24, true, true)]
    [InlineData(70, 24, false, true)]
    [InlineData(100, 38, false, true)]
    public void PartlyVisibleRowsCanBeReadWithoutCountingRowsAlreadyAboveAsHiddenBelow(
        float top, float height, bool visible, bool below)
    {
        Assert.Equal(visible, ReplayStatusPanelPolicy.IsInScrollViewport(top, height, 0, 70));
        Assert.Equal(below, ReplayStatusPanelPolicy.IsBelowScrollViewport(top, height, 70));
    }

    [Theory]
    [InlineData(100, 1000, 20, 80)]
    [InlineData(100, 100, 20, 100)]
    [InlineData(100, 0, 20, 100)]
    [InlineData(40, 1000, 20, 40)]
    [InlineData(44, 1000, 20, 24)]
    public void OverflowFooterKeepsAFixedBodyHeightAndDoesNotCollapseTinyLists(
        float viewport, float content, float footer, float expected)
    {
        Assert.Equal(expected, ReplayStatusPanelPolicy.GetScrollBodyHeight(viewport, content, footer));
    }

    private static int HiddenBelow(float[] heights, float scroll, float viewportHeight)
    {
        var y = -scroll;
        var count = 0;
        foreach (var height in heights)
        {
            if (ReplayStatusPanelPolicy.IsBelowScrollViewport(y, height, viewportHeight))
                count++;
            y += height + 5;
        }
        return count;
    }
}
