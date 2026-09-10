namespace BetterDeaths.Tests;

using System.Reflection;
using BetterDeaths.DamageParsing;

public sealed class DamageCalibrationTimingTests
{
    private static readonly DateTime Start = new(2026, 9, 10, 2, 12, 10, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x10000001, "Player", 0, "", true, 37) { Level = 100 };
    private static readonly DamageActorIdentity Provider = new(0x10000002, "Provider", 0, "", true, 23);
    private static readonly DamageActorIdentity Target = new(0x40000001, "Target", 0, "", false, 0);

    [Fact]
    public void CapturedSourceBuffBoundaryUsesAcceptedTimingWithoutChangingTheHit()
    {
        var tracker = new PeriodicDamageTracker();
        var noMercy = Status(1831, 0, 7, 20);
        tracker.ObserveStatusTiming(Update(noMercy with { RemainingTime = 20 }, Start.AddSeconds(-19.88)));
        var packet = Packet(Player, Start, [noMercy, Status(2964, 1.6791668f, 35, 6), Status(2599, 3.8661742f, 36, 3)],
            action: 0x9049, amount: 64991, potency: 800);
        var hit = Assert.Single(new DirectDamageParser().Parse(packet));
        tracker.ObserveActionCalibration(packet, [hit]);

        Assert.Equal(64991.0 / 800 / 1.29, Calibration(tracker, Player), 10);
        Assert.Equal(64991, hit.RawMeterAmount);
        Assert.Equal(noMercy, hit.SourceStatuses[0]);
        Assert.Equal(0, noMercy.RemainingTime);
        Assert.False(hit.Critical);
        Assert.False(hit.DirectHit);
        Assert.Equal(64991.0 / 800 / (1.06 * 1.03), Calibration(tracker, Player, physical: true), 10);
    }

    public static IEnumerable<object[]> Jobs => Enumerable.Range(19, 24)
        .Where(job => job is not (26 or 29)).Select(job => new object[] { (uint)job });

    [Theory]
    [MemberData(nameof(Jobs))]
    public void SourceModifierTimingIsSharedAcrossAllCombatJobs(uint job)
    {
        var source = Player with { ClassJobId = job };
        var tracker = new PeriodicDamageTracker();
        var buff = Status(1878, 0, 10, 6);
        tracker.ObserveStatusTiming(Update(buff with { RemainingTime = 3 }, Start, source));
        Observe(tracker, source, Start.AddSeconds(2.9), [buff]);
        Assert.Equal(22000.0 / 220 / 1.06, Calibration(tracker, source), 10);
    }

    [Theory]
    [InlineData(1831u, 20, 1.20)]
    [InlineData(1878u, 6, 1.06)]
    [InlineData(2964u, 6, 1.06)]
    [InlineData(2599u, 3, 1.03)]
    public void PersonalAndPartyModifiersUseTheirAcceptedLifetime(uint id, byte strength, double multiplier)
    {
        var tracker = new PeriodicDamageTracker();
        var buff = Status(id, 0, 10, strength);
        tracker.ObserveStatusTiming(Update(buff with { RemainingTime = 3 }, Start));
        Observe(tracker, Player, Start.AddSeconds(2.9), [buff]);
        Assert.Equal(100 / multiplier, Calibration(tracker, Player), 10);
    }

    [Theory]
    [InlineData(4.001)]
    [InlineData(5.0)]
    public void ExpiredWireTimingCannotBeExtendedByAStalePositiveCountdown(double seconds)
    {
        var tracker = new PeriodicDamageTracker();
        var buff = Status(1831, 20, 7, 20);
        tracker.ObserveStatusTiming(Update(buff with { RemainingTime = 3 }, Start));
        Observe(tracker, Player, Start.AddSeconds(seconds), [buff]);
        Assert.Equal(100, Calibration(tracker, Player));
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(3.546)]
    [InlineData(3.677)]
    [InlineData(3.722)]
    [InlineData(3.772)]
    [InlineData(4.0)]
    public void SourceModifierCalibrationRetainsTheInclusiveExpiryWindow(double seconds)
    {
        var tracker = new PeriodicDamageTracker();
        var buff = Status(3685, 0, 7, 5);
        tracker.ObserveStatusTiming(Update(buff with { RemainingTime = 3 }, Start));
        Observe(tracker, Player, Start.AddSeconds(seconds), [buff]);
        Assert.Equal(100 / 1.05, Calibration(tracker, Player), 10);
    }

    [Fact]
    public void ModifierWindowDoesNotChangeTheRegularTimingView()
    {
        var ledger = new DamageStatusTimingLedger();
        var buff = Status(3685, 5, 7, 5);
        ledger.Observe(Update(buff with { RemainingTime = 3 }, Start));
        Assert.Equal(.25f, Assert.Single(ledger.ResolveSourceModifiers(Player.EntityId, [buff], Start.AddSeconds(3.75))).RemainingTime);
        Assert.Equal(-.75f, Assert.Single(ledger.Resolve(Player.EntityId, [buff], Start.AddSeconds(3.75))).RemainingTime);
        Assert.Equal(5, buff.RemainingTime);
    }

    [Fact]
    public void ExplicitRemovalWinsOverTheCapturedBuff()
    {
        var tracker = new PeriodicDamageTracker();
        var buff = Status(1831, 20, 7, 20);
        tracker.ObserveStatusTiming(Update(buff, Start));
        tracker.ObserveStatusTiming(Update(Status(0, 0, 7), Start.AddSeconds(1)));
        Observe(tracker, Player, Start.AddSeconds(1), [buff]);
        Assert.Equal(100, Calibration(tracker, Player));
    }

    [Fact]
    public void SlotReplacementDoesNotReviveThePreviousModifier()
    {
        var tracker = new PeriodicDamageTracker();
        var buff = Status(1831, 20, 7, 20);
        tracker.ObserveStatusTiming(Update(buff, Start));
        tracker.ObserveStatusTiming(Update(Status(1878, 15, 7, 6), Start.AddSeconds(1)));
        Observe(tracker, Player, Start.AddSeconds(2), [buff]);
        Assert.Equal(100 / 1.06, Calibration(tracker, Player), 10);
    }

    [Fact]
    public void OutOfOrderAttackUsesTheStatusVersionAtItsOwnTimestamp()
    {
        var tracker = new PeriodicDamageTracker();
        var buff = Status(1831, 20, 7, 20);
        tracker.ObserveStatusTiming(Update(buff, Start));
        tracker.ObserveStatusTiming(Update(Status(0, 0, 7), Start.AddSeconds(2)));
        Observe(tracker, Player, Start.AddSeconds(1), [buff with { RemainingTime = 0 }]);
        Assert.Equal(100 / 1.20, Calibration(tracker, Player), 10);
    }

    [Fact]
    public void ConfirmedRefreshReplacesTheOldExpiry()
    {
        var tracker = new PeriodicDamageTracker();
        var buff = Status(1831, 3, 7, 20);
        tracker.ObserveStatusTiming(Update(buff, Start));
        tracker.ObserveStatusTiming(Update(buff with { RemainingTime = 20 }, Start.AddSeconds(2)));
        Observe(tracker, Player, Start.AddSeconds(4), [buff with { RemainingTime = 0 }]);
        Assert.Equal(100 / 1.20, Calibration(tracker, Player), 10);
    }

    [Fact]
    public void TimingForAnotherActorCannotChangeTheRecipientsCalibration()
    {
        var tracker = new PeriodicDamageTracker();
        var buff = Status(1831, 0, 7, 20);
        tracker.ObserveStatusTiming(Update(buff with { RemainingTime = 20 }, Start, Provider));
        Observe(tracker, Player, Start.AddSeconds(1), [buff]);
        Assert.Equal(100, Calibration(tracker, Player));
    }

    [Fact]
    public void CapturedStrengthIsNotReplacedByAnOlderWireParameter()
    {
        var tracker = new PeriodicDamageTracker();
        var buff = Status(2964, 20, 7, 2);
        tracker.ObserveStatusTiming(Update(buff, Start));
        Observe(tracker, Player, Start.AddSeconds(1), [buff with { RemainingTime = 0, AppliedParameter = 6 }]);
        Assert.Equal(100 / 1.06, Calibration(tracker, Player), 10);
    }

    [Fact]
    public void ConfirmedSourceModifierDoesNotRequireAPositiveMemoryEntry()
    {
        var tracker = new PeriodicDamageTracker();
        tracker.ObserveStatusTiming(Update(Status(1831, 20, 7, 20), Start));
        Observe(tracker, Player, Start.AddSeconds(1), []);
        Assert.Equal(100 / 1.20, Calibration(tracker, Player), 10);
    }

    [Theory]
    [InlineData(0x31u)]
    [InlineData(0x2Bu)]
    [InlineData(0x2Cu)]
    public void AttributeChangeAdmissionUsesTheSameSourceWindow(uint status)
    {
        var tracker = new PeriodicDamageTracker();
        for (var i = 0; i < 11; i++)
            Observe(tracker, Player, Start.AddSeconds(i * .1), []);
        var buff = Status(status, 3, 7);
        tracker.ObserveStatusTiming(Update(buff, Start.AddSeconds(2)));
        Observe(tracker, Player, Start.AddSeconds(5.75), [buff with { RemainingTime = 0 }]);
        Assert.Equal(11, CalibrationSamples(tracker));
        Observe(tracker, Player, Start.AddSeconds(6.01), [buff with { RemainingTime = 20 }]);
        Assert.Equal(12, CalibrationSamples(tracker));
    }

    [Fact]
    public void UnknownModifierStrengthCannotBecomeAUsableSampleThroughAStaleCountdown()
    {
        var tracker = new PeriodicDamageTracker();
        var buff = Status(0xB5F, 3, 7);
        tracker.ObserveStatusTiming(Update(buff, Start));
        Observe(tracker, Player, Start.AddSeconds(3.75), [buff with { RemainingTime = 0 }]);
        Assert.Equal(0, CalibrationSamples(tracker));
        Observe(tracker, Player, Start.AddSeconds(4.01), [buff with { RemainingTime = 20 }]);
        Assert.Equal(1, CalibrationSamples(tracker));
    }

    [Fact]
    public void SourceModifierWindowIsNotAppliedToTargetDebuffs()
    {
        var tracker = new PeriodicDamageTracker();
        var debuff = Status(0xA1A, 3, 7) with { Source = Player };
        tracker.ObserveStatusTiming(Update(debuff, Start, Target));
        var packet = Packet(Player, Start.AddSeconds(3.75), []) with
        {
            Targets = [new(0, Target, [new(0, 3, 0, 5, 0, 0, 22000)])
                { TargetStatuses = [debuff with { RemainingTime = 0 }], HasTargetStatusSnapshot = true }],
        };
        tracker.ObserveActionCalibration(packet, new DirectDamageParser().Parse(packet));
        Assert.Equal(100, Calibration(tracker, Player));
    }

    [Theory]
    [InlineData(0f, 1.0)]
    [InlineData(5f, 1.2)]
    public void MissingWireEvidencePreservesExistingCaptureBehavior(float remaining, double multiplier)
    {
        var tracker = new PeriodicDamageTracker();
        Observe(tracker, Player, Start, [Status(1831, remaining, 7, 20)]);
        Assert.Equal(100 / multiplier, Calibration(tracker, Player), 10);
    }

    private static DamageStatusSnapshot Status(uint id, float remaining, byte slot, byte? strength = null) =>
        new(id, Provider, 0, remaining) { StatusSlot = slot, AppliedParameter = strength };

    private static DamageStatusTimingUpdate Update(DamageStatusSnapshot status, DateTime time,
        DamageActorIdentity? source = null) => new((source ?? Player).EntityId, status.StatusSlot!.Value, status, time);

    private static DamageActionPacket Packet(DamageActorIdentity source, DateTime time, DamageStatusSnapshot[] statuses,
        uint action = 16495, ushort amount = 22000, double potency = 220) =>
        new(time.Ticks, time, (uint)(time - Start).Ticks, source, action, "Attack",
            [new(0, Target, [new(0, 3, 0, 5, 0, 0, amount)]) { HasTargetStatusSnapshot = true }])
        { SourceStatuses = statuses, HasSourceStatusSnapshot = true, ActionCategoryId = 3,
            DirectPotency = potency, CanCalibratePotency = true };

    private static void Observe(PeriodicDamageTracker tracker, DamageActorIdentity source, DateTime time,
        DamageStatusSnapshot[] statuses)
    {
        var packet = Packet(source, time, statuses);
        tracker.ObserveActionCalibration(packet, new DirectDamageParser().Parse(packet));
    }

    private static double Calibration(PeriodicDamageTracker tracker, DamageActorIdentity source, bool physical = false)
    {
        var snapshot = typeof(PeriodicDamageTracker).GetMethod(physical ? "GetCalibration" : "GetCompatibilityCalibration",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tracker, [source])!;
        return (double)snapshot.GetType().GetProperty("DamagePerPotency")!.GetValue(snapshot)!;
    }

    private static int CalibrationSamples(PeriodicDamageTracker tracker)
    {
        var snapshot = typeof(PeriodicDamageTracker).GetMethod("GetCompatibilityCalibration",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tracker, [Player])!;
        return (int)snapshot.GetType().GetProperty("PotencySamples")!.GetValue(snapshot)!;
    }
}
