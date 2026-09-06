using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class DebugStatusCaptureTests
{
    private static readonly StatusSnapshot First = new(1, "First", 0, 100, 0, 30);
    private static readonly StatusSnapshot Second = new(2, "Second", 0, 200, 1, 20);

    [Fact]
    public void StatusRemovalIsPersistedWithoutRepeatingHistoryOnHpChanges()
    {
        var capture = new DebugStatusCapture();
        capture.Observe(Snapshot([First, Second]));
        var current = capture.Observe(Snapshot([Second]))!;
        Assert.Equal(Second, Assert.Single(current.Statuses));
        var damaged = capture.Observe(Snapshot([Second]) with { CurrentHp = 50 })!;
        Assert.Equal(Second, Assert.Single(damaged.Statuses));
        Assert.Equal(2, Assert.Single(capture.GetHistory()).Statuses.Count);
        var empty = capture.Observe(Snapshot([]))!;
        Assert.Empty(empty.Statuses);
        Assert.Equal(2, Assert.Single(capture.GetHistory()).Statuses.Count);
    }

    [Fact]
    public void DurationCountdownAndOrderingDoNotEmitUnchangedRows()
    {
        var capture = new DebugStatusCapture();
        capture.Observe(Snapshot([First, Second]));
        Assert.Null(capture.Observe(Snapshot([Second, First with { RemainingTime = 29 }])));
        Assert.NotNull(capture.Observe(Snapshot([Second with { StackCount = 2 }, First])));
    }

    [Fact]
    public void QueuedSnapshotOwnsItsCollectionAndSurvivesHistoryClear()
    {
        var capture = new DebugStatusCapture();
        var statuses = new List<StatusSnapshot> { First };
        var queued = capture.Observe(Snapshot(statuses))!;
        statuses.Clear();
        capture.Clear();
        Assert.Empty(capture.GetHistory());
        Assert.Equal(First, Assert.Single(queued.Statuses));
        Assert.NotNull(capture.Observe(Snapshot([First])));
    }

    [Fact]
    public void StatusSourcesAndPlayersHaveSeparateHistory()
    {
        var capture = new DebugStatusCapture();
        capture.Observe(Snapshot([First, First with { SourceId = 200 }]));
        capture.Observe(Snapshot([Second]) with { MemberKey = "other", PartyIndex = 2 });
        Assert.Equal(2, capture.Count);
        Assert.Equal(2, capture.GetHistory()[0].Statuses.Count);
        Assert.Single(capture.GetHistory()[1].Statuses);
    }

    [Fact]
    public async Task DeferredSerializationKeepsTheExistingJsonShapeAndOriginalTimestamp()
    {
        var data = new DebugStatusCapture().Observe(Snapshot([First]))!;
        var record = new DebugCaptureFileRecord(data.SeenAtUtc, 12.5f, 1363, "Duty", "StatusSnapshot", data);
        var options = new JsonSerializerOptions();
        var expected = JsonSerializer.Serialize(new
        {
            record.SeenAtUtc, record.PullElapsedSeconds, record.TerritoryId, record.TerritoryName, record.Kind,
            Data = JsonSerializer.SerializeToElement(data, options),
        }, options);
        Assert.Equal(expected, await Task.Run(() => record.Serialize(options)));
    }

    private static DebugStatusSnapshot Snapshot(IReadOnlyList<StatusSnapshot> statuses) => new(
        new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc), 0, "player", "Player", 1, 24, "WHM",
        100, 0, 100, false, true, statuses) { HasWorldObject = true };
}
