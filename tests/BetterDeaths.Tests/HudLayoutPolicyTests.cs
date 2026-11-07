namespace BetterDeaths.Tests;

public sealed class HudLayoutPolicyTests
{
    [Theory]
    [InlineData(1000, 160, 8, 3)]
    [InlineData(496, 160, 8, 3)]
    [InlineData(495, 160, 8, 2)]
    [InlineData(328, 160, 8, 2)]
    [InlineData(327, 160, 8, 1)]
    [InlineData(140, 160, 8, 1)]
    public void ViewButtonsWrapInsteadOfShrinkingAllThreeLabels(float width, float button, float gap, int columns)
    {
        Assert.Equal(columns, HudLayoutPolicy.NavigationColumns(width, button, gap, 3));
    }

    [Theory]
    [InlineData(320, 17, false)]
    [InlineData(640, 17, false)]
    [InlineData(775, 17, false)]
    [InlineData(776, 17, true)]
    [InlineData(1180, 17, true)]
    [InlineData(900, 24, false)]
    [InlineData(1200, 24, true)]
    public void PreviewIsPinnedOnlyWhenBothPanesHaveEnoughRoom(float width, float line, bool split)
    {
        Assert.Equal(split, HudLayoutPolicy.PartyCooldownColumns(width, line, 16).Split);
    }

    [Fact]
    public void WidePanesFitWithoutSqueezingTheEditorOrPreview()
    {
        foreach (var line in new[] { 12, 17, 20, 24, 32 })
            for (var width = 240; width <= 2560; width += 7)
            {
                var layout = HudLayoutPolicy.PartyCooldownColumns(width, line, 16);
                if (!layout.Split)
                {
                    Assert.Equal(width, layout.EditorWidth);
                    continue;
                }
                Assert.True(layout.EditorWidth >= Math.Max(420, line * 24) - .01f);
                Assert.True(layout.PreviewWidth >= Math.Max(300, line * 20) - .01f);
                Assert.Equal(width, layout.EditorWidth + layout.PreviewWidth + 16, 2);
            }
    }

    [Fact]
    public void EmptyAndInvalidMeasurementsCannotCreateInvalidPaneDimensions()
    {
        Assert.Equal(0, HudLayoutPolicy.NavigationColumns(1000, 160, 8, 0));
        Assert.Equal(1, HudLayoutPolicy.NavigationColumns(float.NaN, 160, 8, 3));
        var layout = HudLayoutPolicy.PartyCooldownColumns(float.NaN, float.NaN, float.NaN);
        Assert.False(layout.Split);
        Assert.Equal(1, layout.EditorWidth);
        Assert.Equal(1, layout.PreviewWidth);
    }
}
