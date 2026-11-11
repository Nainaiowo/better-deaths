using System.Text.Json;
using System.Reflection;
using BetterDeaths.PartyCooldowns;

namespace BetterDeaths.Tests;

public sealed class PossibleMitigationCooldownTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);
    private const uint Player = 0x10000001, Enemy = 0x40000001;
    private static DateTime At(double seconds) => Start.AddSeconds(seconds);
    private static readonly Dictionary<uint, ActionData> Actions = JsonSerializer.Deserialize<ActionData[]>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PartyCooldownActions.json")))!
        .ToDictionary(action => action.Id);

    private sealed record ActionData(uint Id, string Name, byte Group, float Recast, int Charges);

    private static PossibleMitigationSnapshot Candidate(uint action, string member = "player")
    {
        var data = Actions[action];
        return new($"{member}:{action}", member, member, 0, 32, "DRK", action, data.Name, 0,
            PossibleMitigationScope.Targeted, data.Recast, null, null, "Ready", [])
        { CooldownGroup = data.Group, MaxCharges = Math.Max(1, data.Charges) };
    }

    private static MitigationUseEvidence Use(uint action, double time, uint sequence = 1, string member = "player")
    {
        var data = Actions[action];
        return new(sequence, $"{member}:{action}", member, Player, sequence, At(time), (float)time,
            action, data.Name, 0, [Player])
        { CooldownGroup = data.Group, CooldownSeconds = data.Recast };
    }

    private static PartyDeathRecord Death(double time)
    {
        var hit = new CombatEventRecord(At(time), (float)time, "victim", "Victim", 0, Enemy, "Boss", 500, "Hit", 0,
            DeathEventKind.Damage, 100000, 100000, 0, 100000, DamageType.Magic, false, false, false, false, "", [], [])
        { HasPlayerStatusSnapshot = true, HasSourceStatusSnapshot = true };
        return new(At(time + .2), (float)time + .2f, "victim", "Victim", 0, 32, "DRK", 0, 0, 100000, hit, [hit], [], [])
        { EntityId = Player };
    }

    private static PossibleMitigationSnapshot? Resolve(MitigationUseTracker tracker, uint action, double time,
        string member = "player") => PossibleMitigationTimingPolicy.Resolve(Candidate(action, member), Death(time), tracker, At(time + .5));

    [Theory]
    [InlineData(25751, 16464)]
    [InlineData(16464, 25751)]
    [InlineData(3551, 16464)]
    public void SharedAndSyncedActionsSpendTheSameCooldown(uint spent, uint candidate)
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(spent, 0));
        Assert.Null(Resolve(tracker, candidate, 10));
        Assert.NotNull(Resolve(tracker, candidate, 25));
    }

    [Theory]
    [InlineData(1, 10, true)]
    [InlineData(2, 10, false)]
    [InlineData(2, 59.99, false)]
    [InlineData(2, 60, true)]
    [InlineData(2, 120, true)]
    public void ChargedMitigationUsesRemainingChargesAndSequentialRecharge(int spent, double hitAt, bool available)
    {
        var tracker = new MitigationUseTracker();
        for (uint index = 0; index < spent; index++)
            tracker.ObserveUse(Use(25754, index, index + 1));
        Assert.Equal(available, Resolve(tracker, 25754, hitAt) is not null);
    }

    [Fact]
    public void LateUseOfSecondChargeIsStillMarkedLate()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(25754, 0));
        tracker.ObserveUse(Use(25754, 10.1, 2));
        Assert.True(Resolve(tracker, 25754, 10)!.WasUsedTooLate);
    }

    [Fact]
    public void LaterRechargeCannotMakeALateUseAvailableAtAnEarlierHit()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(25751, -15));
        tracker.ObserveUse(Use(16464, 10.1, 2));
        Assert.Null(Resolve(tracker, 16464, 9.9));
    }

    [Fact]
    public void SharedCooldownUsesDoNotChangeTheCandidatesNameOrProveItsLateUse()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(25751, 10.1));
        var option = Assert.IsType<PossibleMitigationSnapshot>(Resolve(tracker, 16464, 10));
        Assert.False(option.WasUsedTooLate);
        Assert.Equal(16464u, option.ActionId);
        Assert.Equal("Nascent Flash", option.ActionName);
    }

    [Fact]
    public void HistoriesArePlayerSpecificAndDoNotSpendDuplicatePacketsOrDependOnArrivalOrder()
    {
        var tracker = new MitigationUseTracker();
        Assert.True(tracker.ObserveUse(Use(25754, 5, 2)));
        Assert.False(tracker.ObserveUse(Use(25754, 5, 2)));
        Assert.NotNull(Resolve(tracker, 25754, 10));
        tracker.ObserveUse(Use(25754, 0));
        Assert.Null(Resolve(tracker, 25754, 10));
        Assert.NotNull(Resolve(tracker, 25754, 10, "other"));
        tracker.Clear();
        Assert.NotNull(Resolve(tracker, 25754, 10));
    }

    [Fact]
    public void DuplicateActionSequenceDoesNotConsumeAnotherChargeOrHideProvenLateUse()
    {
        var tracker = new MitigationUseTracker();
        var use = Use(25754, 9.9);
        Assert.True(tracker.ObserveUse(use));
        Assert.False(tracker.ObserveUse(use with { CaptureId = 2, SeenAtUtc = At(9.91) }));
        tracker.ObserveApplication(new(3, 1, At(10.1), Player, "player", new(2682, "Oblation", 0, Player, 0, 10)));
        var candidate = Candidate(25754) with { EffectStatusIds = [2682] };
        Assert.True(PossibleMitigationTimingPolicy.Resolve(candidate, Death(10), tracker, At(10.5))!.WasUsedTooLate);
    }

    [Fact]
    public void SpendingTheRecoveredChargeDoesNotRestartTheOtherChargesRecharge()
    {
        var tracker = new MitigationUseTracker();
        tracker.ObserveUse(Use(25754, 0));
        tracker.ObserveUse(Use(25754, 1, 2));
        tracker.ObserveUse(Use(25754, 60, 3));
        Assert.Null(Resolve(tracker, 25754, 119.99));
        Assert.NotNull(Resolve(tracker, 25754, 120));
    }

    [Fact]
    public void NewCooldownMetadataRoundTripsAndLegacyOptionsKeepTheirOriginalDefaults()
    {
        var candidate = Candidate(25754);
        var copy = JsonSerializer.Deserialize<PossibleMitigationSnapshot>(JsonSerializer.Serialize(candidate))!;
        Assert.Equal(candidate.CooldownGroup, copy.CooldownGroup);
        Assert.Equal(2, copy.MaxCharges);
        var old = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(candidate))!.AsObject();
        old.Remove("CooldownGroup");
        old.Remove("MaxCharges");
        var legacy = old.Deserialize<PossibleMitigationSnapshot>()!;
        Assert.Equal(0, legacy.CooldownGroup);
        Assert.Equal(1, legacy.MaxCharges);
    }

    public static IEnumerable<object[]> IndependentWhatIfActions()
    {
        var definitions = (Array)typeof(Plugin).GetField("PossibleMitigationDefinitions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (var definition in definitions)
            if ((uint)definition!.GetType().GetProperty("GrantedByActionId")!.GetValue(definition)! == 0)
                yield return [(uint)definition.GetType().GetProperty("ActionId")!.GetValue(definition)!];
    }

    [Theory]
    [MemberData(nameof(IndependentWhatIfActions))]
    public void EveryIndependentWhatIfActionUsesChargesAndExactRechargeBoundaries(uint actionId)
    {
        var candidate = Candidate(actionId);
        Assert.NotEqual(0, candidate.CooldownGroup);
        var recast = (float)PartyCooldownCatalog.RecastAtLevel(actionId, 100, candidate.CooldownSeconds);
        candidate = candidate with { CooldownSeconds = recast };
        var tracker = new MitigationUseTracker();
        for (uint charge = 0; charge < candidate.MaxCharges; charge++)
        {
            tracker.ObserveUse(Use(actionId, charge * .01, charge + 1) with { CooldownSeconds = recast });
            Assert.Equal(charge + 1 < candidate.MaxCharges,
                PossibleMitigationTimingPolicy.Resolve(candidate, Death(1), tracker, At(1.5)) is not null);
        }
        Assert.Null(PossibleMitigationTimingPolicy.Resolve(candidate, Death(recast - .001), tracker, At(recast - .001)));
        Assert.NotNull(PossibleMitigationTimingPolicy.Resolve(candidate, Death(recast), tracker, At(recast)));
    }
}
