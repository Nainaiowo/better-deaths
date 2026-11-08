using System.Text.Json;
using BetterDeaths.PartyCooldowns;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownCoverageTests
{
    private static readonly (string Reason, uint[] Ids)[] ReviewedExclusions =
    [
        ("Movement without independent damage or protection", [94, 112, 155, 2262, 7419, 16010, 24295, 24401, 24402,
            25762, 34646, 34684, 36926, 36934, 36951, 36988, 37008]),
        ("Resource spender or gauge command, not an independent recharge", [167, 181, 189, 2864, 3547, 3578, 7401, 7402, 7404,
            7437, 7490, 7491, 7495, 16007, 16008, 16466, 16467, 16469, 16470, 16474, 16487, 16501, 17209, 24296,
            24389, 24392, 24394, 25761, 25763, 25773, 34683]),
        ("Granted follow-up or release; parent cooldown already offered", [7399, 7400, 7415, 16009, 16155, 16502,
            25755, 25791, 25875, 34686, 35920, 35921, 35922, 36944, 36953, 36956, 36964, 36990, 36991,
            37005, 37007, 37011, 37012, 37019, 37020, 37021, 37022, 37029, 37031, 37037]),
        ("Stance, enmity, or utility outside damage/defensive cooldown scope", [28, 48, 149, 2245, 3629, 7533, 7537, 7557,
            7571, 16006, 16142, 24285, 44881, 44882, 44892, 44894, 47093]),
        ("Limited-job combo actions, not independent burst buttons", [44884, 44887, 44888, 44889, 44930, 44931, 44932, 44933]),
    ];

    public static IEnumerable<object[]> JobDamageCoverage =>
    [
        [19u, new uint[] { 20, 23, 29, 7383, 16461, 25747, 36921 }],
        [21u, new uint[] { 38, 52, 7386, 7387, 7389, 25752 }],
        [32u, new uint[] { 3625, 3639, 3643, 7390, 16472, 25757 }],
        [37u, new uint[] { 16138, 16144, 16146, 16159, 16164, 16165, 25760 }],
        [24u, new uint[] { 136, 7430, 7561, 7562 }],
        [28u, new uint[] { 166, 3587, 7436, 16542, 37014, 7561, 7562 }],
        [33u, new uint[] { 3606, 7439, 16552, 25874, 37017, 7561, 7562 }],
        [40u, new uint[] { 24289, 24307, 24313, 24318, 37033, 7561, 7562 }],
        [20u, new uint[] { 69, 7395, 7396, 25766, 7546 }],
        [22u, new uint[] { 83, 85, 92, 96, 3555, 3557, 16478, 16480, 7546 }],
        [30u, new uint[] { 2246, 2248, 2258, 2259, 2261, 2263, 2264, 3566, 7403, 16489, 16493, 36957, 36958, 7546 }],
        [34u, new uint[] { 7492, 7493, 7496, 7497, 7499, 16481, 16482, 7546 }],
        [39u, new uint[] { 24380, 24381, 24393, 24405, 7546 }],
        [41u, new uint[] { 34620, 34623, 34647, 7546 }],
        [23u, new uint[] { 101, 107, 110, 114, 116, 117, 118, 3558, 3559, 3562, 25785, 36975 }],
        [31u, new uint[] { 2872, 2874, 2876, 2878, 2890, 7414, 7418, 16498, 16499, 16500, 25788, 36979, 36980 }],
        [38u, new uint[] { 15997, 15998, 16011, 16013 }],
        [25u, new uint[] { 158, 3573, 7421, 25796, 7561, 7562 }],
        [27u, new uint[] { 3581, 7427, 7429, 16508, 16510, 25800, 25801, 7561, 7562 }],
        [35u, new uint[] { 7506, 7515, 7517, 7518, 7519, 7520, 7521, 16527, 7561, 7562 }],
        [42u, new uint[] { 34676, 35347, 35348, 35349, 7561, 7562 }],
        [36u, new uint[] { 11411, 11421, 11426, 11427, 11428, 11429, 11430, 18305, 18312, 18323, 18324, 18325,
            23264, 23275, 23277, 23285, 23287, 23288, 23290, 34571, 34576, 34578, 34580, 34581, 34582, 7561, 7562 }],
        [43u, new uint[] { 44890, 44891, 44893, 44895, 44902, 44904, 44905 }],
    ];

    [Fact]
    public void DamageAndSupportMatrixCoversEverySupportedJob()
    {
        Assert.Equal(PartyCooldownCatalog.Jobs.Select(job => job.Id).Order(),
            JobDamageCoverage.Select(row => (uint)row[0]).Order());
    }

    [Theory]
    [MemberData(nameof(JobDamageCoverage))]
    public void EveryJobsReviewedCooldownChoicesSurviveTheActualCatalogFilters(uint job, uint[] ids)
    {
        var catalog = CreateCatalog();
        foreach (var id in ids)
        {
            Assert.True(PartyCooldownSelectionPolicy.IsMajorCooldown(id), $"Missing damage/support choice {id} for {job}.");
            Assert.Contains(catalog.ForJob(job), action => action.Id == id);
        }
    }

    [Theory]
    [MemberData(nameof(PartyCooldownSelectionPolicyTests.MitigationCoverage), MemberType = typeof(PartyCooldownSelectionPolicyTests))]
    public void EveryJobsReviewedDefensiveChoicesSurviveTheActualCatalogFilters(uint job, uint[] ids)
    {
        var catalog = CreateCatalog();
        foreach (var id in ids)
            Assert.Contains(catalog.ForJob(job), action => action.Id == id);
    }

    [Fact]
    public void ReviewedGameRowsKeepTheirSelectionAndConditionalClassification()
    {
        var rows = ReadRows();
        Assert.Equal(rows.Length, rows.DistinctBy(row => row.Id).Count());
        Assert.Equal(PartyCooldownCatalog.Jobs.Select(job => job.Id).Order(), rows.SelectMany(row => row.Jobs).Distinct().Order());
        foreach (var row in rows)
        {
            Assert.Equal(row.Included, PartyCooldownSelectionPolicy.Includes(row.Id));
            Assert.Equal(row.Defensive, PartyCooldownSelectionPolicy.IsMitigation(row.Id));
            Assert.Equal(row.Variant, PartyCooldownSelectionPolicy.IsExecutionVariant(row.Id));
            Assert.Equal(row.Unsupported, PartyCooldownSelectionPolicy.UnsupportedReason(row.Id) is not null);
        }
    }

    [Fact]
    public void EveryCurrentCandidateIsIncludedOrHasAnExplicitReviewedExclusion()
    {
        var rows = ReadRows();
        var excluded = ReviewedExclusions.SelectMany(group => group.Ids.Select(id => (Id: id, group.Reason))).ToArray();
        Assert.Equal(excluded.Length, excluded.DistinctBy(entry => entry.Id).Count());
        foreach (var row in rows)
            Assert.Equal(!row.Included, excluded.Any(entry => entry.Id == row.Id));
        foreach (var entry in excluded)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
            Assert.Contains(rows, row => row.Id == entry.Id && !row.Included);
        }
    }

    [Theory]
    [InlineData(19, 7538)]
    [InlineData(21, 7540)]
    [InlineData(32, 7538)]
    [InlineData(37, 7540)]
    [InlineData(20, 7863)]
    [InlineData(22, 7863)]
    [InlineData(30, 7863)]
    [InlineData(34, 7863)]
    [InlineData(39, 7863)]
    [InlineData(41, 7863)]
    [InlineData(23, 7551)]
    [InlineData(31, 7551)]
    [InlineData(38, 7551)]
    [InlineData(23, 7553)]
    [InlineData(31, 7554)]
    public void ActualRoleControlActionsCanBeSelectedAndStartTheirOwnTimer(uint job, uint actionId)
    {
        var catalog = CreateCatalog();
        var action = Assert.Single(catalog.ForJob(job), candidate => candidate.Id == actionId);
        Assert.Null(action.UnsupportedReason);
        Assert.Equal([actionId], catalog.NormalizeSelection(job, [actionId]));
        var profile = new PartyCooldownProfile(actionId, action.Group, action.RecastSeconds, 1);
        var tracker = new PartyCooldownTracker();
        tracker.ConfirmReady(1, profile, 0);
        Assert.True(tracker.Observe(1, profile, 10, 1));
        Assert.Equal(action.RecastSeconds - 1, tracker.Get(1, profile, 11).RemainingSeconds);
    }

    [Fact]
    public void ConditionalChoicesStayVisibleButCannotEnterSavedSelections()
    {
        var catalog = CreateCatalog();
        foreach (var job in PartyCooldownCatalog.Jobs)
        {
            var conditional = catalog.ForJob(job.Id).Where(action => action.UnsupportedReason is not null).ToArray();
            Assert.Empty(catalog.NormalizeSelection(job.Id, conditional.Select(action => action.Id)));
            Assert.All(conditional, action => Assert.False(string.IsNullOrWhiteSpace(action.UnsupportedReason)));
        }
    }

    [Theory]
    [InlineData(42, 35347, 34670)]
    [InlineData(42, 35347, 34671)]
    [InlineData(42, 35347, 34672)]
    [InlineData(42, 35347, 34673)]
    [InlineData(42, 34676, 34677)]
    [InlineData(38, 15997, 36984)]
    [InlineData(27, 7429, 16516)]
    [InlineData(27, 7429, 36998)]
    public void ActualTransformedExecutionsUpdateTheirParentTimerWithoutDuplicateTiles(uint job, uint parent, uint execution)
    {
        var catalog = CreateCatalog();
        var choice = Assert.Single(catalog.ForJob(job), action => action.Id == parent);
        var captured = Assert.Single(catalog.CaptureActionsForJob(job), action => action.Id == execution);
        Assert.DoesNotContain(catalog.ForJob(job), action => action.Id == execution);
        Assert.Equal(choice.Group, captured.Group);
        Assert.Equal(choice.RecastSeconds, captured.RecastSeconds);
        var maximum = Math.Max(1, (int)choice.MaxCharges);
        var profile = new PartyCooldownProfile(parent, choice.Group, choice.RecastSeconds, maximum);
        var tracker = new PartyCooldownTracker();
        tracker.ConfirmReady(1, profile, 0);
        Assert.True(tracker.Observe(1, profile with { ActionId = execution }, 1, 123));
        var status = tracker.Get(1, profile, 2);
        Assert.True(status.IsKnown);
        Assert.Equal(maximum - 1, status.Charges);
        Assert.Equal(choice.RecastSeconds - 1, status.RemainingSeconds);
    }

    private static PartyCooldownCatalog CreateCatalog() => new(ReadRows().Select(row =>
        new PartyCooldownAction(row.Id, row.Name, row.Icon, row.Level, row.Group, row.Recast, row.Charges, row.Jobs,
            TargetsOtherPlayers: row.TargetsOtherPlayers, UnsupportedReason: PartyCooldownSelectionPolicy.UnsupportedReason(row.Id),
            CaptureOnly: !PartyCooldownSelectionPolicy.Includes(row.Id) || PartyCooldownSelectionPolicy.IsExecutionVariant(row.Id))));

    private static GameRow[] ReadRows() => JsonSerializer.Deserialize<GameRow[]>(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "PartyCooldownActions.json")))!;

    private sealed record GameRow(uint Id, string Name, uint Icon, byte Level, byte Group, float Recast,
        byte Charges, uint[] Jobs, bool Included, bool Defensive, bool Variant, bool TargetsOtherPlayers, bool Unsupported);
}
