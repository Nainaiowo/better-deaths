namespace BetterDeaths.Tests;

public sealed class ReplayDebuffTimelineTests
{
    [Fact]
    public void TimedDebuffCountsDownFromLatestChange()
    {
        var active = ReplayDebuffTimeline.GetActiveStates(
            [CreateChange(5.0f, 12.0f, active: true)],
            9.5f);

        var state = Assert.Single(active);
        Assert.Equal(7.5f, state.RemainingSeconds);
    }

    [Fact]
    public void HiddenDurationStaysActiveUntilRemoval()
    {
        var changes = new[]
        {
            CreateChange(1.0f, 0.0f, active: true),
            CreateChange(18.0f, 0.0f, active: false),
        };

        var beforeRemoval = Assert.Single(ReplayDebuffTimeline.GetActiveStates(changes, 17.9f));
        Assert.Null(beforeRemoval.RemainingSeconds);
        Assert.Empty(ReplayDebuffTimeline.GetActiveStates(changes, 18.0f));
    }

    [Fact]
    public void RefreshUpdatesExistingDebuffTimer()
    {
        var changes = new[]
        {
            CreateChange(2.0f, 10.0f, active: true),
            CreateChange(8.0f, 10.0f, active: true),
        };

        var beforeRefresh = Assert.Single(ReplayDebuffTimeline.GetActiveStates(changes, 7.0f));
        var afterRefresh = Assert.Single(ReplayDebuffTimeline.GetActiveStates(changes, 9.0f));

        Assert.Equal(5.0f, beforeRefresh.RemainingSeconds);
        Assert.Equal(9.0f, afterRefresh.RemainingSeconds);
    }

    [Fact]
    public void ShortDebuffGainAndLossRemainQueryable()
    {
        var changes = new[]
        {
            CreateChange(3.00f, 0.25f, active: true),
            CreateChange(3.20f, 0.0f, active: false),
        };

        Assert.Single(ReplayDebuffTimeline.GetActiveStates(changes, 3.10f));
        Assert.Empty(ReplayDebuffTimeline.GetActiveStates(changes, 3.20f));
    }

    [Fact]
    public void TimedDebuffWaitsForCapturedRemoval()
    {
        var changes = new[]
        {
            CreateChange(2.0f, 3.0f, active: true),
            CreateChange(6.0f, 0.0f, active: false),
        };

        var beforeRemoval = Assert.Single(ReplayDebuffTimeline.GetActiveStates(changes, 5.5f));
        Assert.Equal(0.0f, beforeRemoval.RemainingSeconds);
        Assert.Empty(ReplayDebuffTimeline.GetActiveStates(changes, 6.0f));
    }

    [Fact]
    public void SameStatusFromDifferentSourcesRemainsDistinct()
    {
        var changes = new[]
        {
            CreateChange(4.0f, 15.0f, active: true, sourceId: 100),
            CreateChange(4.0f, 15.0f, active: true, sourceId: 200),
        };

        Assert.Equal(2, ReplayDebuffTimeline.GetActiveStates(changes, 5.0f).Count);
    }

    [Fact]
    public void OlderPullWithoutDebuffStreamLoadsWithEmptyHistory()
    {
        const string json = """
            {
              "CapturedAtUtc": "2026-01-01T00:00:00Z",
              "Reason": "Test",
              "TerritoryId": 1,
              "TerritoryName": "Test Duty",
              "PullElapsedSeconds": 10,
              "Deaths": []
            }
            """;

        var pull = System.Text.Json.JsonSerializer.Deserialize<PullDeathSnapshot>(json);

        Assert.NotNull(pull);
        Assert.Empty(pull.ReplayDebuffs);
        Assert.False(pull.ReplayDebuffsCaptured);
        Assert.Empty(pull.ReplayAnalyzerEvents);
    }

    private static ReplayDebuffSnapshot CreateChange(
        float elapsed,
        float remainingTime,
        bool active,
        uint sourceId = 100)
    {
        return new ReplayDebuffSnapshot(
            DateTime.UnixEpoch.AddSeconds(elapsed),
            elapsed,
            "member-1",
            "Player One",
            0,
            21,
            "WAR",
            new StatusSnapshot(500, "Example Debuff", 1500, sourceId, 0, remainingTime),
            active);
    }
}
