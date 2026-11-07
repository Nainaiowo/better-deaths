using BetterDeaths.PartyCooldowns;
using System.Numerics;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownCatalogTests
{
    [Fact]
    public void JobsAreUniqueAndAllFourRoleCategoriesArePresent()
    {
        Assert.Equal(PartyCooldownCatalog.Jobs.Count, PartyCooldownCatalog.Jobs.DistinctBy(job => job.Id).Count());
        Assert.Equal(Enum.GetValues<PartyCooldownRole>(), PartyCooldownCatalog.Jobs.Select(job => job.Role).Distinct());
        Assert.Contains(PartyCooldownCatalog.Jobs, job => job.Id == 42 && job.Role == PartyCooldownRole.Ranged);
        Assert.Contains(PartyCooldownCatalog.Jobs, job => job.Id == 36 && job.Role == PartyCooldownRole.Ranged);
    }

    [Fact]
    public void RoleActionsAppearForEachEligibleJobWithoutCrossRoleLeakage()
    {
        uint[] tanks = [19, 21, 32, 37];
        var catalog = new PartyCooldownCatalog([Action(7531, tanks, true), Action(7560, [25, 27, 35, 42])]);
        foreach (var job in PartyCooldownCatalog.Jobs)
            Assert.Equal(tanks.Contains(job.Id), catalog.ForJob(job.Id).Any(action => action.Id == 7531));
    }

    [Fact]
    public void DefaultsAreIndependentOfExplicitEmptySelection()
    {
        var catalog = new PartyCooldownCatalog([Action(7531, [19], true)]);
        Assert.Equal(new uint[] { 7531 }, catalog.NormalizeSelection(19, null));
        Assert.Empty(catalog.NormalizeSelection(19, []));
    }

    [Fact]
    public void SelectionKeepsOrderAndRejectsDuplicatesWrongJobAndUnsupportedAbilities()
    {
        var catalog = new PartyCooldownCatalog([Action(1, [19]), Action(2, [19]), Action(3, [21]),
            Action(4, [19]) with { UnsupportedReason = "Conditional cooldown" }]);
        Assert.Equal(new uint[] { 2, 1 }, catalog.NormalizeSelection(19, [2, 1, 2, 0, 999, 3, 4]));
    }

    [Fact]
    public void InvalidAndGcdDefinitionsCannotBecomeTrackable()
    {
        var catalog = new PartyCooldownCatalog([Action(1, [19]) with { Group = 58 },
            Action(2, [19]) with { RecastSeconds = float.NaN }, Action(3, [19]) with { Group = 0 },
            Action(4, [19]) with { RecastSeconds = 2.5f }, Action(5, [19])]);
        Assert.Equal(new uint[] { 5 }, catalog.ForJob(19).Select(action => action.Id));
    }

    [Fact]
    public void SyncedUpgradedAbilityUsesEligiblePredecessorButNotDifferentTargetedAbility()
    {
        var guardian = Action(36920, [19]) with { Level = 92, Group = 5 };
        var sentinel = Action(17, [19]) with { Level = 38, Group = 5 };
        var targeted = Action(999, [19]) with { Level = 70, Group = 5, TargetsOtherPlayers = true };
        var catalog = new PartyCooldownCatalog([guardian, sentinel, targeted]);
        Assert.Equal(17u, catalog.AtLevel(19, 36920, 80)?.Id);
        Assert.Equal(36920u, catalog.AtLevel(19, 36920, 100)?.Id);
        Assert.Null(catalog.AtLevel(19, 36920, 30));
    }

    [Theory]
    [InlineData(7405, 87, 120, 120)]
    [InlineData(7405, 88, 120, 90)]
    [InlineData(16889, 88, 120, 90)]
    [InlineData(16012, 88, 120, 90)]
    [InlineData(7561, 93, 60, 60)]
    [InlineData(7561, 94, 60, 40)]
    [InlineData(3606, 68, 90, 60)]
    [InlineData(7519, 74, 45, 35)]
    [InlineData(24300, 88, 120, 90)]
    [InlineData(158, 84, 120, 100)]
    [InlineData(3585, 88, 120, 90)]
    [InlineData(7496, 94, 120, 60)]
    [InlineData(16481, 94, 120, 60)]
    [InlineData(16542, 97, 90, 90)]
    [InlineData(16542, 98, 90, 60)]
    [InlineData(24294, 94, 90, 60)]
    [InlineData(44905, 42, 120, 90)]
    [InlineData(44904, 48, 120, 90)]
    public void RecastTraitsRespectTheActualSyncedLevel(uint action, byte level, double original, double expected)
    {
        Assert.Equal(expected, PartyCooldownCatalog.RecastAtLevel(action, level, original));
    }

    [Fact]
    public void DraggingCanAddReorderAndAppendWithoutDuplicating()
    {
        List<uint> selection = [1, 2, 3];
        Assert.True(PartyCooldownCatalog.PlaceBefore(selection, 4, 2));
        Assert.Equal(new uint[] { 1, 4, 2, 3 }, selection);
        Assert.True(PartyCooldownCatalog.PlaceBefore(selection, 3, 1));
        Assert.Equal(new uint[] { 3, 1, 4, 2 }, selection);
        Assert.True(PartyCooldownCatalog.PlaceBefore(selection, 3, null));
        Assert.Equal(new uint[] { 1, 4, 2, 3 }, selection);
    }

    [Fact]
    public void InvalidDropDoesNotLoseTheSourceOrExceedTheLimit()
    {
        List<uint> selection = Enumerable.Range(1, 12).Select(value => (uint)value).ToList();
        Assert.False(PartyCooldownCatalog.PlaceBefore(selection, 2, 999));
        Assert.False(PartyCooldownCatalog.PlaceBefore(selection, 13, 1));
        Assert.False(PartyCooldownCatalog.PlaceBefore(selection, 0, null));
        Assert.True(PartyCooldownCatalog.PlaceBefore(selection, 12, 1));
        Assert.Equal(12, selection.Count);
    }

    [Fact]
    public void DuplicateNamedActionVariantsStayCapturableWithoutDuplicatingTheChooser()
    {
        var catalog = new PartyCooldownCatalog([Action(1, [19]) with { Name = "Ability" },
            Action(2, [19]) with { Name = "Ability" }]);
        Assert.Single(catalog.ForJob(19));
        Assert.Equal(2, catalog.CaptureActionsForJob(19).Count);
        Assert.NotNull(catalog.Find(2));
    }

    [Fact]
    public void PartyReorderMatchesDisplayRowsNotArrayIndices()
    {
        int[] rows = [0, 3, 1, 2];
        Assert.Equal(2, PartyCooldownLayout.FindUniqueRow(rows, 1));
        Assert.Equal(1, PartyCooldownLayout.FindUniqueRow(rows, 3));
        Assert.Equal(-1, PartyCooldownLayout.FindUniqueRow(rows, 7));
        Assert.Equal(-1, PartyCooldownLayout.FindUniqueRow([0, 0, 1], 0));
        Assert.Equal(-1, PartyCooldownLayout.FindUniqueRow(rows, -1));
    }

    [Fact]
    public void LeftAndRightPlacementFollowNameBoundsAndHudScaling()
    {
        var row = new PartyCooldownRow(1, 0, new(200, 100), new(300, 120), 1);
        Assert.Equal(new Vector2(102, 100), PartyCooldownLayout.GetOrigin(row, 4, PartyCooldownSide.Left, 20, 4, Vector2.Zero));
        Assert.Equal(new Vector2(306, 100), PartyCooldownLayout.GetOrigin(row, 4, PartyCooldownSide.Right, 20, 4, Vector2.Zero));
        Assert.Equal(new Vector2(306 + 12, 98), PartyCooldownLayout.GetOrigin(row, 4, PartyCooldownSide.Right, 20, 4, new(12, -2)));
        var scaled = row with { Scale = 2, NameMax = new(400, 140) };
        Assert.Equal(new Vector2(412, 100), PartyCooldownLayout.GetOrigin(scaled, 4, PartyCooldownSide.Right, 20, 4, Vector2.Zero));
    }

    [Fact]
    public void CorruptNumericSettingsCannotProduceNonFiniteCoordinates()
    {
        Assert.Equal(24, PartyCooldownLayout.IconSize(float.NaN));
        Assert.Equal(3, PartyCooldownLayout.Gap(float.PositiveInfinity));
        Assert.Equal(0, PartyCooldownLayout.Offset(float.NegativeInfinity));
        Assert.Equal(36, PartyCooldownLayout.IconSize(900));
        Assert.Equal(-300, PartyCooldownLayout.Offset(-1000));
    }

    [Fact]
    public void CombinedHudAndGlobalScalingDoesNotApplyADifferentScaleToPlacement()
    {
        var row = new PartyCooldownRow(1, 0, new(200, 100), new(600, 180), 4);
        Assert.Equal(new Vector2(624, 100), PartyCooldownLayout.GetOrigin(row, 4, PartyCooldownSide.Right, 20, 4, Vector2.Zero));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PreviewNeverInventsExtraChargesOrAnOverlongCooldown(int maximum)
    {
        for (var index = 0; index < 12; index++)
        {
            var status = PartyCooldownStatus.Preview(maximum, 5, index);
            Assert.Equal(maximum, status.MaxCharges);
            Assert.InRange(status.Charges, 0, maximum);
            Assert.InRange(status.RemainingSeconds, 0, 5);
        }
    }

    private static PartyCooldownAction Action(uint id, uint[] jobs, bool defaultSelected = false) =>
        new(id, $"Ability {id}", 1, 1, 1, 60, 1, jobs, defaultSelected);
}
