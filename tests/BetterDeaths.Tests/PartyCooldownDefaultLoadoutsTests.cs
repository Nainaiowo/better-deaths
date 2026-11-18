using System.Text.Json;
using BetterDeaths.PartyCooldowns;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownDefaultLoadoutsTests
{
    private static readonly GameRow[] Rows = JsonSerializer.Deserialize<GameRow[]>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "PartyCooldownActions.json")))!;
    private static readonly Dictionary<uint, GameRow> ById = Rows.ToDictionary(row => row.Id);
    private sealed record GameRow(uint Id, string Name, uint Icon, byte Level, byte Group, float Recast,
        byte Charges, uint[] Jobs, bool TargetsOtherPlayers);

    private static PartyCooldownCatalog Catalog()
    {
        return new(Rows.Select(row => new PartyCooldownAction(row.Id, row.Name, row.Icon, row.Level,
            row.Group, row.Recast, row.Charges, row.Jobs, PartyCooldownDefaultLoadouts.WasLegacyDefault(row.Id), row.TargetsOtherPlayers,
            PartyCooldownSelectionPolicy.UnsupportedReason(row.Id),
            !PartyCooldownSelectionPolicy.Includes(row.Id) || PartyCooldownSelectionPolicy.IsExecutionVariant(row.Id))));
    }

    public static IEnumerable<object[]> Jobs() => PartyCooldownCatalog.Jobs.Select(job => new object[] { job.Id });

    [Fact]
    public void ReviewedDefaultsCoverEveryJobExactlyOnce()
    {
        Assert.Equal(24, PartyCooldownRows.MaximumAbilities);
        Assert.Equal(PartyCooldownCatalog.Jobs.Select(job => job.Id).Order(), PartyCooldownDefaultLoadouts.Jobs.Keys.Order());
    }

    [Theory]
    [MemberData(nameof(Jobs))]
    public void EveryReviewedAbilitySurvivesRealCatalogFiltersAndKeepsCategoryOrder(uint job)
    {
        var plan = PartyCooldownDefaultLoadouts.Jobs[job];
        var expected = plan.Actions.ToArray();
        Assert.InRange(expected.Length, 1, PartyCooldownRows.MaximumAbilities);
        Assert.Equal(expected.Length, expected.Distinct().Count());
        var catalog = Catalog();
        Assert.Equal(expected, catalog.DefaultSelection(job));
        Assert.Equal(expected, catalog.NormalizeSelection(job, null));
        foreach (var id in expected)
        {
            var action = Assert.Single(catalog.ForJob(job), value => value.Id == id);
            Assert.Null(action.UnsupportedReason);
            Assert.False(action.CaptureOnly);
            Assert.Contains(job, action.Jobs);
        }
        foreach (var id in plan.TwoMinute)
            Assert.InRange(PartyCooldownCatalog.RecastAtLevel(id, 100, ById[id].Recast), 100, 120);
        foreach (var id in plan.OneMinute)
            Assert.InRange(PartyCooldownCatalog.RecastAtLevel(id, 100, ById[id].Recast), 55, 60);
    }

    [Fact]
    public void PureHealingOptionalControlRoleUtilityAndGrantedExecutionsAreNotDefaults()
    {
        uint[] omitted = [7541, 7542, 3552, 16151, 3570, 3571, 7433, 3583, 7434, 16537, 16543,
            3614, 16553, 16557, 24288, 24299, 24301, 24302, 16015, 36997, 140, 3569, 25862,
            7538, 7540, 7551, 7553, 7554, 7863, 7546, 7561, 7562, 7430, 3606, 3612,
            37031, 37011, 16546, 34686, 7399];
        var all = PartyCooldownDefaultLoadouts.Jobs.Values.SelectMany(plan => plan.Actions).ToArray();
        Assert.DoesNotContain(all, omitted.Contains);
        Assert.Contains(16559u, PartyCooldownDefaultLoadouts.Jobs[33].Defensives);
        Assert.Contains(16536u, PartyCooldownDefaultLoadouts.Jobs[24].Defensives);
        Assert.Contains(16545u, PartyCooldownDefaultLoadouts.Jobs[28].Defensives);
        Assert.Contains(34685u, PartyCooldownDefaultLoadouts.Jobs[42].Defensives);
        Assert.Contains(24318u, PartyCooldownDefaultLoadouts.Jobs[40].TwoMinute);
    }

    [Fact]
    public void LegacyDefaultMigrationPreservesExplicitOrderEmptySelectionsAndUserListReferences()
    {
        var catalog = Catalog();
        var custom = new List<uint> { 27, 7531 };
        var hidden = new List<uint>();
        var selected = new Dictionary<uint, List<uint>> { [19] = custom, [21] = hidden };
        Assert.True(catalog.PreserveLegacyDefaults(selected));
        Assert.Same(custom, selected[19]);
        Assert.Equal(new uint[] { 27, 7531 }, selected[19]);
        Assert.Same(hidden, selected[21]);
        Assert.Empty(selected[21]);
        Assert.Equal(new uint[] { 3613, 25873 }, selected[33].Order());
        Assert.DoesNotContain(16559u, selected[33]);
        Assert.False(catalog.PreserveLegacyDefaults(selected));
        Assert.Equal(PartyCooldownCatalog.Jobs.Count, selected.Count);
    }

    [Fact]
    public void ExplicitResetOptsIntoNewDefaultsWithoutChangingOtherSavedJobs()
    {
        var catalog = Catalog();
        var selected = new Dictionary<uint, List<uint>>();
        catalog.PreserveLegacyDefaults(selected);
        var warrior = selected[21].ToArray();
        selected[19] = catalog.NormalizeSelection(19, null);
        Assert.Equal(PartyCooldownDefaultLoadouts.Jobs[19].Actions, selected[19]);
        Assert.Equal(warrior, selected[21]);
        Assert.True(selected[19].Count > 12);
        Assert.Empty(catalog.NormalizeSelection(19, []));
    }

    [Theory]
    [MemberData(nameof(Jobs))]
    public void ConfirmedAllJobDefaultsReplaceSavedSelectionsAndRemoveCustomRows(uint job)
    {
        var catalog = Catalog();
        var previous = catalog.Defaults(job);
        previous.Reverse();
        var rows = new PartyCooldownRows
        {
            Enabled = true,
            Rows = [previous.Take(1).ToList(), previous.Skip(1).ToList(), []]
        };
        var selections = new Dictionary<uint, List<uint>> { [job] = previous };
        var layouts = new Dictionary<uint, PartyCooldownRows> { [job] = rows };

        catalog.RestoreDefaultSelections(selections, layouts);

        Assert.Equal(PartyCooldownCatalog.Jobs.Count, selections.Count);
        Assert.Equal(PartyCooldownDefaultLoadouts.Jobs[job].Actions, selections[job]);
        Assert.NotSame(previous, selections[job]);
        Assert.Empty(layouts);
        var defaultRows = new PartyCooldownRows();
        defaultRows.Normalize(selections[job]);
        Assert.False(defaultRows.Enabled);
        Assert.Equal(selections[job], Assert.Single(defaultRows.Rows));
    }

    [Fact]
    public void ConfirmedAllJobDefaultsUpgradeLegacyAndExplicitlyEmptySelectionsTogether()
    {
        var catalog = Catalog();
        var selections = new Dictionary<uint, List<uint>> { [19] = [], [40] = [7561, 24303] };
        catalog.PreserveLegacyDefaults(selections);
        var layouts = selections.ToDictionary(pair => pair.Key, pair => new PartyCooldownRows
        {
            Enabled = true,
            Rows = [[], [], pair.Value.ToList()]
        });

        catalog.RestoreDefaultSelections(selections, layouts);

        foreach (var job in PartyCooldownCatalog.Jobs)
            Assert.Equal(PartyCooldownDefaultLoadouts.Jobs[job.Id].Actions, selections[job.Id]);
        Assert.Empty(layouts);
        Assert.True(selections[19].Count > 12);
        Assert.Equal(selections[40], catalog.NormalizeSelection(40, selections[40]));
        Assert.False(catalog.PreserveLegacyDefaults(selections));
        Assert.Equal(PartyCooldownDefaultLoadouts.Jobs[33].Actions, selections[33]);
    }

    [Fact]
    public void ConfirmedAllJobDefaultsSaveReloadAndReapplyWithoutSharingMutableDefaultLists()
    {
        var catalog = Catalog();
        var selections = new Dictionary<uint, List<uint>>();
        var layouts = new Dictionary<uint, PartyCooldownRows>();
        catalog.RestoreDefaultSelections(selections, layouts);
        var restored = JsonSerializer.Deserialize<Dictionary<uint, List<uint>>>(JsonSerializer.Serialize(selections))!;
        foreach (var job in PartyCooldownCatalog.Jobs)
            Assert.Equal(catalog.DefaultSelection(job.Id), catalog.NormalizeSelection(job.Id, restored[job.Id]));

        restored[19].Clear();
        var original = selections[19];
        catalog.RestoreDefaultSelections(restored, layouts);
        Assert.Equal(catalog.DefaultSelection(19), restored[19]);
        Assert.Equal(catalog.DefaultSelection(19), original);
        Assert.NotSame(original, restored[19]);
        var firstApply = restored[19];
        catalog.RestoreDefaultSelections(restored, layouts);
        Assert.Equal(firstApply, restored[19]);
        Assert.NotSame(firstApply, restored[19]);
    }

    [Fact]
    public void ConfirmedAllJobDefaultsDoNotChangeUnrecognizedJobsOrLiveCooldownHistory()
    {
        var catalog = Catalog();
        var unrelatedSelection = new List<uint> { 999999 };
        var unrelatedRows = new PartyCooldownRows { Enabled = true, Rows = [unrelatedSelection] };
        var selections = new Dictionary<uint, List<uint>> { [999] = unrelatedSelection };
        var layouts = new Dictionary<uint, PartyCooldownRows> { [999] = unrelatedRows };
        var tracker = new PartyCooldownTracker();
        var profile = new PartyCooldownProfile(7531, 47, 90, 1);
        Assert.True(tracker.Observe(1, profile, 100, 1));
        var before = tracker.Get(1, profile, 110);

        catalog.RestoreDefaultSelections(selections, layouts);

        Assert.Same(unrelatedSelection, selections[999]);
        Assert.Same(unrelatedRows, layouts[999]);
        Assert.Equal(before, tracker.Get(1, profile, 110));
        Assert.Equal(80, before.RemainingSeconds);
    }

    [Fact]
    public void HistoricalDefaultCountsAreFrozenForEveryJobRatherThanFollowingWhatIfChanges()
    {
        var expected = new Dictionary<uint, int>
        {
            [19] = 3,
            [21] = 5,
            [32] = 6,
            [37] = 6,
            [24] = 2,
            [28] = 2,
            [33] = 2,
            [40] = 3,
            [20] = 2,
            [22] = 1,
            [30] = 1,
            [34] = 2,
            [39] = 1,
            [41] = 1,
            [23] = 1,
            [31] = 2,
            [38] = 1,
            [25] = 1,
            [27] = 1,
            [35] = 2,
            [42] = 1,
            [36] = 1,
            [43] = 0,
        };
        var selections = new Dictionary<uint, List<uint>>();
        Catalog().PreserveLegacyDefaults(selections);
        Assert.Equal(expected.Count, selections.Count);
        foreach (var pair in expected)
            Assert.Equal(pair.Value, selections[pair.Key].Count);
    }

    [Fact]
    public void DefaultListsAreNotMutableSharedUserSelections()
    {
        var catalog = Catalog();
        var first = catalog.Defaults(19);
        first.Clear();
        Assert.NotEmpty(catalog.Defaults(19));
        var selected = new Dictionary<uint, List<uint>>();
        catalog.PreserveLegacyDefaults(selected);
        selected[19].Clear();
        Assert.NotEmpty(catalog.DefaultSelection(19));
    }

    [Fact]
    public void TwentyFourSelectedAbilitiesSurviveNormalizationAndThreeRowSaveReload()
    {
        var actions = Enumerable.Range(1, 25).Select(id => new PartyCooldownAction((uint)id, $"Ability {id}",
            1, 1, (byte)id, 60, 0, [19])).ToArray();
        var catalog = new PartyCooldownCatalog(actions);
        var ids = actions.Select(action => action.Id).ToArray();
        var selection = catalog.NormalizeSelection(19, ids);
        Assert.Equal(ids[..24], selection);
        var rows = new PartyCooldownRows
        {
            Enabled = true,
            Rows = [selection[..8], selection[8..16], selection[16..24]]
        };
        Assert.False(rows.Normalize(selection));
        var restored = JsonSerializer.Deserialize<PartyCooldownRows>(JsonSerializer.Serialize(rows))!;
        Assert.Equal(selection, restored.Flatten());
        Assert.Equal(3, restored.Rows.Count);
        Assert.True(PartyCooldownCatalog.PlaceAfter(selection, 1, 24));
        Assert.Equal(1u, selection[^1]);
        Assert.False(PartyCooldownCatalog.PlaceAfter(selection, 25, 24));
    }

    [Fact]
    public void SyncedNinjaDefaultUsesAssassinateBeforeDreamWithinADreamIsLearned()
    {
        var catalog = Catalog();
        Assert.Equal(2246u, catalog.AtLevel(30, 3566, 40)?.Id);
        Assert.Equal(3566u, catalog.AtLevel(30, 3566, 56)?.Id);
        Assert.Null(catalog.AtLevel(30, 3566, 39));
    }

    [Fact]
    public void AllTwentyFourRefreshBitsAndCopiedEntriesWorkWithoutPerDrawAllocations()
    {
        var actions = Enumerable.Range(1, 24).Select(id => new PartyCooldownAction((uint)id, $"Ability {id}",
            1, 1, (byte)id, 60, 0, [19])).ToArray();
        var display = new PartyCooldownDisplayRow(actions);
        var mask = (1u << 24) - 1;
        Assert.Equal(mask, display.TimerRefreshMask);
        for (var index = 0; index < 24; index++)
            display.Update(index, new(1, 1, 0, true));
        Assert.Equal(0u, display.TimerRefreshMask);
        display.Update(23, new(0, 1, 60, true));
        Assert.Equal(1u << 23, display.TimerRefreshMask);
        var buffer = new PartyCooldownDisplayEntry[24];
        Assert.Equal(24, display.CopyTo(buffer));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 10000; frame++)
            display.CopyTo(buffer);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(24u, buffer[23].Action.Id);
        Assert.Equal("60", buffer[23].TimerLabel);
        display.Invalidate();
        Assert.Equal(mask, display.TimerRefreshMask);
    }
}
