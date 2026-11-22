using System.Numerics;

namespace BetterDeaths.Tests;

public sealed class FoodCheckFormatterTests
{
    [Fact]
    public void FindsWellFedAmongOtherStatuses()
    {
        var statuses = new[]
        {
            new StatusSnapshot(1203, "Addle", 1203, 1, 0, 12),
            new StatusSnapshot(FoodCheckFormatter.WellFedStatusId, "Well Fed", 48, 1, 0, 901),
        };

        Assert.Equal(48u, FoodCheckFormatter.FindWellFedStatus(statuses)?.Id);
        Assert.Equal("15:01", FoodCheckFormatter.FormatRemaining(FoodCheckFormatter.FindWellFedStatus(statuses)));
    }

    [Fact]
    public void ReportsWhenFoodIsNotActive()
    {
        Assert.Null(FoodCheckFormatter.FindWellFedStatus([]));
        Assert.Equal("Not active", FoodCheckFormatter.FormatRemaining(null));
    }

    [Theory]
    [InlineData(89.2f, "1:30")]
    [InlineData(3660.1f, "1:01:01")]
    public void RoundsRemainingTimeUpWithoutDroppingHours(float remaining, string expected)
    {
        var status = new StatusSnapshot(FoodCheckFormatter.WellFedStatusId, "Well Fed", 48, 1, 0, remaining);

        Assert.Equal(expected, FoodCheckFormatter.FormatRemaining(status));
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-1.0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void DoesNotInventATimerForInvalidDurations(float remaining)
    {
        var status = new StatusSnapshot(FoodCheckFormatter.WellFedStatusId, "Well Fed", 48, 1, 0, remaining);

        Assert.Equal("Time unavailable", FoodCheckFormatter.FormatRemaining(status));
    }

    [Fact]
    public void HidesFoodMarkerWhenStatusDataIsUnavailable()
    {
        Assert.Equal(FoodCheckIndicatorState.Hidden,
            FoodCheckIndicatorPolicy.Classify(false, false, 0, 600));
    }

    [Fact]
    public void ShowsMissingOnlyWhenStatusDataWasRead()
    {
        Assert.Equal(FoodCheckIndicatorState.Missing,
            FoodCheckIndicatorPolicy.Classify(true, false, 0, 600));
    }

    [Fact]
    public void WarningsOnlyHidesHealthyFoodButKeepsWarningsAndUnavailableDistinct()
    {
        Assert.Equal(FoodCheckIndicatorState.Hidden,
            FoodCheckIndicatorPolicy.ForDisplay(FoodCheckIndicatorState.Active, true));
        Assert.Equal(FoodCheckIndicatorState.Expiring,
            FoodCheckIndicatorPolicy.ForDisplay(FoodCheckIndicatorState.Expiring, true));
        Assert.Equal(FoodCheckIndicatorState.Missing,
            FoodCheckIndicatorPolicy.ForDisplay(FoodCheckIndicatorState.Missing, true));
        Assert.Equal(FoodCheckIndicatorState.Hidden,
            FoodCheckIndicatorPolicy.ForDisplay(FoodCheckIndicatorState.Hidden, true));
    }

    [Theory]
    [InlineData(601, (int)FoodCheckIndicatorState.Active)]
    [InlineData(600, (int)FoodCheckIndicatorState.Expiring)]
    [InlineData(75, (int)FoodCheckIndicatorState.Expiring)]
    [InlineData(0, (int)FoodCheckIndicatorState.Expiring)]
    public void MarksFoodAsExpiringAtOrBelowConfiguredThreshold(float remaining, int expected)
    {
        Assert.Equal((FoodCheckIndicatorState)expected,
            FoodCheckIndicatorPolicy.Classify(true, true, remaining, 600));
    }

}

public sealed class FoodCheckPollingPolicyTests
{
    [Fact]
    public void SamplesOncePerSecond()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), FoodCheckPollingPolicy.Interval);
    }

    [Theory]
    [InlineData(true, true, true, false, false, false, true)]
    [InlineData(true, true, false, false, false, false, false)]
    [InlineData(true, true, false, true, false, false, true)]
    [InlineData(false, true, true, true, false, false, false)]
    [InlineData(true, false, true, true, false, false, false)]
    [InlineData(true, true, true, false, true, false, false)]
    [InlineData(true, true, false, true, true, false, false)]
    [InlineData(true, true, true, true, false, true, false)]
    public void PollsInInstancesOrWhenOverworldIsEnabledAndOnlyOutOfCombat(bool enabled, bool loggedIn,
        bool inInstance, bool showOutsideInstances, bool inCombat, bool isPvP, bool expected)
    {
        Assert.Equal(expected, FoodCheckPollingPolicy.ShouldPoll(enabled, loggedIn, inInstance,
            showOutsideInstances, inCombat, isPvP));
    }
}

public sealed class FoodCheckTintPolicyTests
{
    [Theory]
    [InlineData(0.0f, 0.20f)]
    [InlineData(0.65f, 0.65f)]
    [InlineData(1.0f, 1.0f)]
    [InlineData(1.5f, 1.0f)]
    [InlineData(-0.5f, 0.20f)]
    public void ClampsHeightToTheSupportedRange(float value, float expected)
    {
        Assert.Equal(expected, FoodCheckTintPolicy.NormalizeHeightFraction(value));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void UsesDefaultHeightForInvalidValues(float value)
    {
        Assert.Equal(FoodCheckTintPolicy.DefaultHeightFraction,
            FoodCheckTintPolicy.NormalizeHeightFraction(value));
    }

    [Fact]
    public void CentersTintVerticallyWithoutChangingItsHeightOrWidth()
    {
        var (topInset, height) = FoodCheckTintPolicy.GetVerticalBounds(40, 0.5f);

        Assert.Equal(10, topInset);
        Assert.Equal(20, height);
    }

    [Theory]
    [InlineData(-10.0f, 0.0f)]
    [InlineData(0.0f, 10.0f)]
    [InlineData(10.0f, 20.0f)]
    public void AppliesYOffsetWithoutChangingTintHeight(float offsetY, float expectedTopInset)
    {
        var (topInset, height) = FoodCheckTintPolicy.GetVerticalBounds(40, 0.5f, offsetY);

        Assert.Equal(expectedTopInset, topInset);
        Assert.Equal(20, height);
    }

    [Theory]
    [InlineData(-100.0f, -50.0f)]
    [InlineData(0.0f, 0.0f)]
    [InlineData(100.0f, 50.0f)]
    [InlineData(float.NaN, 0.0f)]
    public void ClampsOrResetsYOffset(float value, float expected)
    {
        Assert.Equal(expected, FoodCheckTintPolicy.NormalizeOffsetY(value));
    }
}

public sealed class FoodCheckMarkerPolicyTests
{
    [Theory]
    [InlineData(8.0f, 24.0f, 16.0f)]
    [InlineData(48.0f, 24.0f, 48.0f)]
    [InlineData(80.0f, 24.0f, 72.0f)]
    [InlineData(float.NaN, 48.0f, 48.0f)]
    public void NormalizesIconSizeAndUsesCurrentLayoutAsFallback(float value, float fallback, float expected)
    {
        Assert.Equal(expected, FoodCheckMarkerPolicy.NormalizeIconSize(value, fallback));
    }

    [Theory]
    [InlineData(-400.0f, -300.0f)]
    [InlineData(0.0f, 0.0f)]
    [InlineData(400.0f, 300.0f)]
    [InlineData(float.NaN, 0.0f)]
    public void ClampsIconOffsets(float value, float expected)
    {
        Assert.Equal(expected, FoodCheckMarkerPolicy.NormalizeOffset(value));
    }

    [Fact]
    public void KeepsExistingIconSlotPositionWhenSizeAndOffsetsMatch()
    {
        var position = FoodCheckMarkerPolicy.PositionInCooldownSlot(new(100, 200), 48, 48, 1, 0, 0);

        Assert.Equal(new Vector2(100, 200), position);
    }

    [Fact]
    public void CentersCustomIconInSlotThenAppliesScaledOffsets()
    {
        var position = FoodCheckMarkerPolicy.PositionInCooldownSlot(new(100, 200), 48, 24, 2, 3, -2);

        Assert.Equal(new Vector2(130, 220), position);
    }
}
