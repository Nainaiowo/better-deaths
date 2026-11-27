using BetterDeaths.PartyCooldowns;
using Newtonsoft.Json;
using static BetterDeaths.Tests.PartyCooldownImportFixture;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownImportUndoTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void UndoRestoresExactStoredSetupAcrossReloads(int format, bool allJobs)
    {
        var live = DetailedOriginal(customRowsEnabled: allJobs);
        var before = Json(live);
        string? disk = null;
        Import(live, format, allJobs, staged => disk = Json(staged));
        Assert.Equal(allJobs ? null : 19u, live.LastPartyCooldownImport!.SingleJobId);
        Assert.Equal(format != 1, live.LastPartyCooldownImport.Layout is not null);
        Assert.NotEqual(before, disk);
        live = Reload(disk!);
        Assert.NotNull(live.LastPartyCooldownImport);

        var imported = Json(live);
        string? duringSave = null;
        Undo(live, staged =>
        {
            duringSave = Json(live);
            disk = Json(staged);
        });

        Assert.Equal(imported, duringSave);
        Assert.Equal(before, Json(live));
        Assert.Equal(before, disk);
        live = Reload(disk!);
        Assert.Equal(before, Json(live));
        Assert.Null(live.LastPartyCooldownImport);
        Assert.False(live.TryUndoLastCooldownImport(Catalog(), _ => Assert.Fail("Already undone"), NoLog, out _));
    }

    [Fact]
    public void UndoOfAnOlderLayoutCodeLeavesLaterFontEditsUntouched()
    {
        var live = DetailedOriginal(false);
        Import(live, 3, true);
        live.PartyCooldownPlacements!.Left!.FontSizePercent = 69;
        live.PartyCooldownPlacements.Right!.FontSizePercent = 168;
        live = Reload(Json(live));
        Undo(live);
        Assert.Equal(69, live.PartyCooldownPlacements!.Left!.FontSizePercent);
        Assert.Equal(168, live.PartyCooldownPlacements.Right!.FontSizePercent);
    }

    [Fact]
    public void SingleJobUndoPreservesLaterChangesToOtherJobsAndLocalOptions()
    {
        var live = DetailedOriginal(customRowsEnabled: false);
        var oldRows = Json(live.PartyCooldownRowLayouts[19]);
        var oldPlacements = Json(live.PartyCooldownPlacements!);
        Import(live, 3, false);
        live.PartyCooldownActions[21].Clear();
        live.PartyCooldownRowLayouts[21] = new() { Enabled = true, Rows = [[], []] };
        live.ShowPartyCooldowns = false;
        live.PartyCooldownAutoCollapseJobs = true;
        live.PartyCooldownPlacements!.Left!.Spacing = 7;
        live.PartyCooldownRowLayouts[19].Rows[0].Add(7531);

        Undo(live);
        Assert.Equal(new uint[] { 17, 7531 }, live.PartyCooldownActions[19]);
        Assert.Equal(oldRows, Json(live.PartyCooldownRowLayouts[19]));
        Assert.Equal(oldPlacements, Json(live.PartyCooldownPlacements!));
        Assert.Empty(live.PartyCooldownActions[21]);
        Assert.Equal(2, live.PartyCooldownRowLayouts[21].Rows.Count);
        Assert.False(live.ShowPartyCooldowns);
        Assert.True(live.PartyCooldownAutoCollapseJobs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionOnlyUndoDoesNotRewindLaterPositionChanges(bool allJobs)
    {
        var live = DetailedOriginal(true);
        var oldRows = Json(live.PartyCooldownRowLayouts[19]);
        Import(live, 1, allJobs);
        var placements = live.PartyCooldownPlacements!;
        placements.Right!.OffsetY = -75.5f;
        live.PartyCooldownSide = PartyCooldownSide.Right;
        live.PartyCooldownIconGap = 11;
        Undo(live);
        Assert.Same(placements, live.PartyCooldownPlacements);
        Assert.Equal(-75.5f, live.GetPartyCooldownPlacement().OffsetY);
        Assert.Equal(11, live.PartyCooldownIconGap);
        Assert.Equal(oldRows, Json(live.PartyCooldownRowLayouts[19]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedUndoKeepsImportedSetupAndAllowsRetry(bool allJobs)
    {
        var live = DetailedOriginal(true);
        var original = Json(live);
        Import(live, 3, allJobs);
        var imported = Json(live);
        var checkpoint = live.LastPartyCooldownImport;
        var failure = new IOException("Simulated write failure");
        Exception? logged = null;
        string? attempted = null;
        Assert.False(live.TryUndoLastCooldownImport(Catalog(), staged =>
        {
            attempted = Json(staged);
            throw failure;
        }, ex => logged = ex, out var error));
        Assert.Same(failure, logged);
        Assert.Contains("retry", error);
        Assert.Equal(original, attempted);
        Assert.Equal(imported, Json(live));
        Assert.Same(checkpoint, live.LastPartyCooldownImport);
        Undo(live);
        Assert.Equal(original, Json(live));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyTheLastSuccessfulImportCanBeUndone(bool firstAllJobs)
    {
        var live = DetailedOriginal(false);
        Import(live, 3, firstAllJobs);
        var firstUndo = live.LastPartyCooldownImport!;
        Assert.False(live.TrySaveCooldownImport("not a code", 19, Catalog(), _ => Assert.Fail("Invalid code"), NoLog, out _, out _));
        Assert.False(live.TrySaveCooldownImport(MakeCode(3, !firstAllJobs), firstAllJobs ? 19u : null, Catalog(),
            _ => throw new IOException(), _ => { }, out _, out _));
        Assert.Same(firstUndo, live.LastPartyCooldownImport);

        live.PartyCooldownPlacements!.Right!.OffsetX = 123;
        var beforeSecondActions = Json(live.PartyCooldownActions);
        var beforeSecondRows = Json(live.PartyCooldownRowLayouts);
        Import(live, 3, !firstAllJobs);
        Assert.NotSame(firstUndo, live.LastPartyCooldownImport);
        Assert.Equal(firstAllJobs ? 19u : null, live.LastPartyCooldownImport!.SingleJobId);
        Undo(live);
        Assert.Equal(123, live.PartyCooldownPlacements!.Right!.OffsetX);
        Assert.Equal(beforeSecondActions, Json(live.PartyCooldownActions));
        Assert.Equal(beforeSecondRows, Json(live.PartyCooldownRowLayouts));
        Assert.Null(live.LastPartyCooldownImport);
    }

    [Fact]
    public void AJobThatPreviouslyInheritedDefaultsDoesNotGainAnOverrideOnUndo()
    {
        var live = Original();
        live.PartyCooldownActions.Remove(19);
        live.PartyCooldownRowLayouts.Remove(19);
        Import(live, 3, false);
        Assert.True(live.PartyCooldownActions.ContainsKey(19));
        Assert.True(live.PartyCooldownRowLayouts.ContainsKey(19));
        Undo(live);
        Assert.False(live.PartyCooldownActions.ContainsKey(19));
        Assert.False(live.PartyCooldownRowLayouts.ContainsKey(19));
    }

    [Fact]
    public void SnapshotOwnsItsRowAndPlacementValues()
    {
        var live = DetailedOriginal(false);
        var original = Json(live);
        var actions = live.PartyCooldownActions[19];
        var rows = live.PartyCooldownRowLayouts[19];
        var left = live.PartyCooldownPlacements!.Left!;
        var right = live.PartyCooldownPlacements.Right!;
        Import(live, 3, true);
        actions.Clear();
        rows.Rows[0].Clear();
        rows.Enabled = true;
        left.IconSize = 500;
        right.OffsetX = -275;
        Undo(live);
        Assert.Equal(original, Json(live));
    }

    [Fact]
    public void AnIncompatibleCheckpointNeverPartiallyRestoresJobs()
    {
        var live = DetailedOriginal(true);
        Import(live, 3, true);
        var imported = Json(live);
        // This catalog can no longer provide WAR's Rampart, saved after PLD in the checkpoint.
        var changedCatalog = new PartyCooldownCatalog([
            new(7531, "Rampart", 1, 1, 1, 90, 1, [19]), new(17, "Sentinel", 2, 1, 2, 120, 1, [19])]);
        Assert.False(live.TryUndoLastCooldownImport(changedCatalog, _ => Assert.Fail("Not exact"), NoLog, out var error));
        Assert.Contains("exactly", error);
        Assert.Equal(imported, Json(live));
    }

    [Fact]
    public void ImportCannotOverwriteSettingsWhenAnExactBackupCannotBeMade()
    {
        var live = Original();
        Import(live, 3, false);
        live.PartyCooldownPlacements!.Left!.Spacing = -1; // Simulate a damaged local setting.
        var before = Json(live);
        Assert.False(live.TrySaveCooldownImport(MakeCode(3, true), null, Catalog(),
            _ => Assert.Fail("Must not replace the current setup"), NoLog, out _, out var error));
        Assert.Equal(CooldownImportProblem.BackupFailed, error.Problem);
        Assert.Contains("backed up", error.Reason);
        Assert.Equal(before, Json(live));
    }

    [Fact]
    public void ChangedDefaultsCannotSilentlyRestoreDifferentIconsForAnInheritedJob()
    {
        var live = Original();
        live.PartyCooldownActions.Remove(19);
        live.PartyCooldownRowLayouts.Remove(19);
        Import(live, 3, false);
        var imported = Json(live);
        var newCatalog = new PartyCooldownCatalog([
            new(7531, "Rampart", 1, 1, 1, 90, 1, [19, 21]), new(17, "Sentinel", 2, 1, 2, 120, 1, [19]),
            new(7535, "Reprisal", 3, 1, 3, 60, 1, [19, 21])]);
        Assert.False(Catalog().DefaultSelection(19).SequenceEqual(newCatalog.DefaultSelection(19)));
        Assert.False(live.TryUndoLastCooldownImport(newCatalog, _ => Assert.Fail("Not exact"), NoLog, out _));
        Assert.Equal(imported, Json(live));
    }

    [Fact]
    public void ReloadingSavedRowsNeverInsertsAnotherEmptyFirstRow()
    {
        var rows = new PartyCooldownRows { Enabled = true, Rows = [[17], [], [7531]] };
        var expected = Json(rows);
        for (var i = 0; i < 3; i++)
        {
            rows = JsonConvert.DeserializeObject<PartyCooldownRows>(Json(rows))!;
            Assert.Equal(expected, Json(rows));
        }
    }

    private static Configuration DetailedOriginal(bool customRowsEnabled)
    {
        var live = Original();
        live.PartyCooldownPlacements = new()
        {
            Left = new() { IconSize = 58.25f, FontSizePercent = 121, Spacing = 4.5f, OffsetX = -82.5f, OffsetY = 17.75f },
            Right = new() { IconSize = 36.5f, FontSizePercent = 72, Spacing = 9.75f, OffsetX = 19.5f, OffsetY = -28.25f },
        };
        live.PartyCooldownActions[19] = customRowsEnabled ? [7531, 17] : [17, 7531];
        live.PartyCooldownRowLayouts[19] = new() { Enabled = customRowsEnabled, Rows = [[7531], [], [17]] };
        live.PartyCooldownActions[24] = []; // Explicitly empty, unlike a missing job.
        live.PartyCooldownRowLayouts[24] = new() { Enabled = false, Rows = [[], [], []] };
        return live;
    }

    private static string Json(object value) => JsonConvert.SerializeObject(value);
    private static Configuration Reload(string json) => JsonConvert.DeserializeObject<Configuration>(json)!;
    private static void NoLog(Exception error) => Assert.Fail(error.Message);

    private static void Import(Configuration live, int format, bool allJobs, Action<Configuration>? save = null) =>
        Assert.True(live.TrySaveCooldownImport(MakeCode(format, allJobs), allJobs ? null : 19u, Catalog(),
            save ?? (_ => { }), NoLog, out _, out var error), error.Reason);

    private static void Undo(Configuration live, Action<Configuration>? save = null) =>
        Assert.True(live.TryUndoLastCooldownImport(Catalog(), save ?? (_ => { }), NoLog, out var error), error);
}
