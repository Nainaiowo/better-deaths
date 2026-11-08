using BetterDeaths.PartyCooldowns;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownSelectionPolicyTests
{
    public static IEnumerable<object[]> MitigationCoverage =>
    [
        [19u, new uint[] { 7531, 7535, 17, 22, 27, 30, 3540, 3542, 7382, 7385, 25746, 36920 }],
        [21u, new uint[] { 7531, 7535, 40, 43, 44, 3551, 3552, 7388, 16464, 25751, 36923 }],
        [32u, new uint[] { 7531, 7535, 3634, 3636, 3638, 3641, 7393, 16471, 25754, 36927 }],
        [37u, new uint[] { 7531, 7535, 16140, 16148, 16151, 16152, 16160, 16161, 25758, 36935 }],
        [24u, new uint[] { 3570, 3571, 7432, 7433, 16536, 25861 }],
        [28u, new uint[] { 188, 3583, 3585, 3586, 7434, 16537, 16538, 16543, 16545, 16546, 25867, 25868 }],
        [33u, new uint[] { 3613, 3614, 16553, 16556, 16559, 25873 }],
        [40u, new uint[] { 24288, 24298, 24299, 24300, 24301, 24302, 24303, 24305, 24310, 24311, 24317 }],
        [20u, new uint[] { 7549, 65, 7394 }],
        [22u, new uint[] { 7549, 7541, 7542 }],
        [30u, new uint[] { 7549, 2241 }],
        [34u, new uint[] { 7549, 7498, 36962 }],
        [39u, new uint[] { 7549, 24404 }],
        [41u, new uint[] { 7549, 7541, 7542 }],
        [23u, new uint[] { 3561, 7405, 7408 }],
        [31u, new uint[] { 2887, 16889 }],
        [38u, new uint[] { 16012, 16014, 16015 }],
        [25u, new uint[] { 7560, 157 }],
        [27u, new uint[] { 7560, 25799, 36997 }],
        [35u, new uint[] { 7560, 25857 }],
        [42u, new uint[] { 7560, 34685 }],
        [36u, new uint[] { 18306, 18320, 23273, 23280, 34575 }],
        [43u, new uint[] { 44896, 44897, 44898, 44899, 44901, 44903 }],
    ];

    [Theory]
    [MemberData(nameof(MitigationCoverage))]
    public void EveryJobKeepsItsIndependentMitigationCooldowns(uint jobId, uint[] actions)
    {
        Assert.Contains(PartyCooldownCatalog.Jobs, job => job.Id == jobId);
        Assert.All(actions, id => Assert.True(PartyCooldownSelectionPolicy.IsMitigation(id), $"Missing mitigation {id} for job {jobId}."));
    }

    [Fact]
    public void MitigationMatrixCoversEverySupportedJob()
    {
        Assert.Equal(PartyCooldownCatalog.Jobs.Select(job => job.Id).Order(),
            MitigationCoverage.Select(row => (uint)row[0]).Order());
    }

    [Theory]
    [InlineData(7538)] // Interject.
    [InlineData(7540)] // Low Blow.
    [InlineData(7551)] // Head Graze.
    [InlineData(7553)] // Foot Graze.
    [InlineData(7554)] // Leg Graze.
    [InlineData(7863)] // Leg Sweep.
    public void RoleCrowdControlIsAvailableAsOptionalDefensiveUtility(uint id) =>
        Assert.True(PartyCooldownSelectionPolicy.IsMitigation(id));

    [Theory]
    [InlineData(20)] // Fight or Flight.
    [InlineData(7389)] // Inner Release.
    [InlineData(7390)] // Delirium.
    [InlineData(16138)] // No Mercy.
    [InlineData(7395)] // Riddle of Fire.
    [InlineData(85)] // Lance Charge.
    [InlineData(2258)] // Trick Attack.
    [InlineData(7499)] // Meikyo Shisui (55-second recast).
    [InlineData(24393)] // Gluttony.
    [InlineData(2876)] // Reassemble (55-second recast).
    [InlineData(16013)] // Flourish.
    [InlineData(7427)] // Summon Bahamut.
    [InlineData(35348)] // Steel Muse.
    public void MajorShorterBurstButtonsRemainInScope(uint id) => Assert.True(PartyCooldownSelectionPolicy.IsMajorCooldown(id));

    [Theory]
    [InlineData(7533)] // Provoke.
    [InlineData(7537)] // Shirk.
    [InlineData(2262)] // Shukuchi.
    [InlineData(36934)] // Trajectory.
    [InlineData(16470)] // Edge of Shadow: MP, not an independent reusable cooldown.
    [InlineData(16501)] // Automaton Queen: battery, not a six-second summon cycle.
    [InlineData(25755)] // Salt and Darkness: consumed Salted Earth follow-up.
    [InlineData(34686)] // Tempera Grassa: conversion, tracked through Coat.
    public void ResourceSpendersMovementAndEnmityAreNotOfferedAsIndependentCooldowns(uint id) => Assert.False(PartyCooldownSelectionPolicy.Includes(id));

    [Theory]
    [InlineData(34675, 35349, 42)]
    [InlineData(34674, 35348, 42)]
    [InlineData(25831, 7427, 27)]
    [InlineData(36992, 7427, 27)]
    [InlineData(37018, 37017, 33)]
    [InlineData(36984, 15997, 38)]
    [InlineData(34670, 35347, 42)]
    [InlineData(34671, 35347, 42)]
    [InlineData(34672, 35347, 42)]
    [InlineData(34673, 35347, 42)]
    [InlineData(34677, 34676, 42)]
    [InlineData(16516, 7429, 27)]
    [InlineData(36998, 7429, 27)]
    public void TransformedExecutionsStayCapturableWithoutDuplicateChoices(uint variant, uint canonical, uint job)
    {
        var original = new PartyCooldownAction(canonical, "Cooldown", 1, 1, 1, 60, 1, [job]);
        var execution = original with { Id = variant, CaptureOnly = true };
        var catalog = new PartyCooldownCatalog([original, execution]);
        Assert.True(PartyCooldownSelectionPolicy.Includes(variant));
        Assert.Equal(canonical, PartyCooldownSelectionPolicy.CanonicalAction(variant));
        Assert.Single(catalog.ForJob(job));
        Assert.Contains(catalog.CaptureActionsForJob(job), action => action.Id == variant);
        Assert.Equal(new[] { canonical }, catalog.NormalizeSelection(job, [variant, canonical]));
        var tracker = new PartyCooldownTracker();
        tracker.Observe(1, new(variant, 1, 60, 1), 10, 1);
        Assert.Equal(50, tracker.Get(1, new(canonical, 1, 60, 1), 20).RemainingSeconds);
    }

    [Fact]
    public void HiddenSharedRecastActionsStillUpdateTheVisibleMitigation()
    {
        var mitigation = new PartyCooldownAction(23273, "Chelonian Gate", 1, 1, 4, 30, 1, [36]);
        var shared = mitigation with { Id = 99991, Name = "Shared attack", CaptureOnly = true };
        var unrelated = shared with { Id = 99992, Group = 5 };
        var wrongJob = shared with { Id = 99993, Jobs = new uint[] { 19 } };
        var catalog = new PartyCooldownCatalog([mitigation, shared, unrelated, wrongJob]);
        Assert.Single(catalog.ForJob(36));
        Assert.Equal(2, catalog.CaptureActionsForJob(36).Count);
        Assert.Empty(catalog.CaptureActionsForJob(19));
        Assert.NotNull(catalog.Find(shared.Id));
        Assert.Null(catalog.Find(unrelated.Id));
        Assert.Null(catalog.Find(wrongJob.Id));
        Assert.Empty(catalog.NormalizeSelection(36, [shared.Id]));
    }

    [Fact]
    public void StarryMuseUsesItsActualPresentationWithoutBreakingSavedScenicMuseSelections()
    {
        Assert.Equal(34675u, PartyCooldownSelectionPolicy.DisplayAction(35349));
        Assert.Equal(34675u, PartyCooldownSelectionPolicy.DisplayAction(34675));
        Assert.Equal(34685u, PartyCooldownSelectionPolicy.DisplayAction(34685));
        var choice = new PartyCooldownAction(35349, "Starry Muse", 3826, 70, 21, 120, 1, [42]);
        var execution = choice with { Id = 34675, CaptureOnly = true };
        var catalog = new PartyCooldownCatalog([choice, execution]);
        var displayed = Assert.Single(catalog.ForJob(42));
        Assert.Equal("Starry Muse", displayed.Name);
        Assert.Equal(3826u, displayed.IconId);
        Assert.Equal(new uint[] { 35349 }, catalog.NormalizeSelection(42, [35349, 34675]));
        Assert.Contains(catalog.CaptureActionsForJob(42), action => action.Id == 34675);
        var tracker = new PartyCooldownTracker();
        Assert.True(tracker.Observe(1, new(34675, 21, 120, 1), 10, 42));
        Assert.Equal(120, tracker.Get(1, new(35349, 21, 120, 1), 10).RemainingSeconds);
        Assert.Equal(119, tracker.Get(1, new(35349, 21, 120, 1), 11).RemainingSeconds);
    }

    [Theory]
    [InlineData(3542)]
    [InlineData(25746)]
    public void FiveSecondMitigationsAreNotRejectedByTheOldMinimumDuration(uint id)
    {
        var catalog = new PartyCooldownCatalog([new(id, "Sheltron", 1, 1, 3, 5, 1, new uint[] { 19 })]);
        Assert.Single(catalog.ForJob(19));
    }

    [Fact]
    public void ThreeSecondDefensiveCooldownIsNotConfusedWithAGlobalCooldown()
    {
        var catalog = new PartyCooldownCatalog([new(44898, "Cloud Skim", 1, 22, 9, 3, 0, new uint[] { 43 })]);
        Assert.Single(catalog.ForJob(43));
    }

    [Fact]
    public void ShortDamagingMovementCooldownIsStillAvailableLikeOtherDamagingGapClosers()
    {
        var catalog = new PartyCooldownCatalog([new(7492, "Hissatsu: Gyoten", 1, 54, 5, 5, 0, new uint[] { 34 })]);
        Assert.Single(catalog.ForJob(34));
    }

    [Theory]
    [InlineData(52)]
    [InlineData(110)]
    [InlineData(2874)]
    [InlineData(36980)]
    [InlineData(2259)]
    [InlineData(3586)]
    [InlineData(34576)]
    [InlineData(44895)]
    public void ConditionalChoicesExplainWhyTheyCannotBeSelected(uint id)
    {
        Assert.True(PartyCooldownSelectionPolicy.Includes(id));
        Assert.False(string.IsNullOrWhiteSpace(PartyCooldownSelectionPolicy.UnsupportedReason(id)));
    }
}
