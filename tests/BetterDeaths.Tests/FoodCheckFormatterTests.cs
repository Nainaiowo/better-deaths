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
    [InlineData(true, true, true, false, false, true)]
    [InlineData(false, true, true, false, false, false)]
    [InlineData(true, false, true, false, false, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, true, true, true, false, false)]
    [InlineData(true, true, true, false, true, false)]
    public void PollsOnlyWhenEnabledInAnInstanceAndOutOfCombat(bool enabled, bool loggedIn, bool inInstance,
        bool inCombat, bool isPvP, bool expected)
    {
        Assert.Equal(expected, FoodCheckPollingPolicy.ShouldPoll(enabled, loggedIn, inInstance, inCombat, isPvP));
    }
}
