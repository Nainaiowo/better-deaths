using BetterDeaths.DamageParsing;

namespace BetterDeaths.Tests;

public sealed class PeriodicModifierAdmissionTests
{
    private static readonly DateTime Start = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Source = new(0x1001, "Source", 0, "", true, 23) { Level = 100 };
    private static readonly DamageActorIdentity Provider = new(0x1002, "Provider", 0, "", true, 33);
    private static readonly DamageActorIdentity Target = new(0x4001, "Target", 0, "", false, 0);

    public static IEnumerable<object?[]> Strengths =>
        from status in new uint[] { 0x75A, 0x75D, 0xF2F, 0xF31, 0xB94, 0x71D, 0x71E, 0x839 }
        from applied in new byte?[] { null, 0, 1, 2, 3, 4, 5, 6 }
        from stacks in new ushort[] { 0, 1, 3 }
        select new object?[] { status, applied, stacks, status is 0x75A or 0xB94 or 0x71D or 0x71E or 0x839 };

    [Theory]
    [MemberData(nameof(Strengths))]
    public void VariableModifiersFollowTheModelCatalogWithoutChangingCapturedEffects(
        uint status, byte? applied, ushort stacks, bool supported)
    {
        var tracker = new PeriodicDamageTracker();
        var buff = new DamageStatusSnapshot(status, Provider, stacks, 30) { HasParameter = false, AppliedParameter = applied };
        var packet = new DamageActionPacket(1, Start, 1, Source, 16495, "Burst Shot",
            [new(0, Target, [new(0, 3, 0, 5, 0, 0, 42400)]) { HasTargetStatusSnapshot = true }])
            { HasSourceStatusSnapshot = true, SourceStatuses = [buff], ActionCategoryId = 3 };
        var direct = Assert.Single(new DirectDamageParser().Parse(packet));
        Assert.Equal(42400, direct.RawMeterAmount);
        tracker.ObserveActionCalibration(packet, [direct]);

        tracker.Observe(new(Target, Source, 1200, "Caustic Bite", 0, 7406, "Caustic Bite",
            Start.AddSeconds(1), 30, true, false, false)
        {
            PeriodicPotency = 20, ActionCategoryId = 3, DamageType = 3,
            HasSourceStatusSnapshot = true, SourceStatuses = [buff],
        });
        var tick = Assert.Single(tracker.Process(new(2, Start.AddSeconds(4), Target, 0, "", 0, 6000, null)));
        var estimate = tick.PeriodicCompatibilityEstimate!;
        var expected = 1.0 + (supported ? applied.GetValueOrDefault() / 100.0 : 0.0);
        Assert.Equal(supported, PeriodicCalibrationCatalog.SupportsVariableDamageStatus(status));
        Assert.Equal(expected, estimate.Inputs!.DamageMultiplier, 9);
        Assert.Equal(42400 / (220 * expected), estimate.Inputs.DamagePerPotency, 9);
        Assert.Equal(supported && applied is null, estimate.Limitation == "Unknown application-time buff strength");

        // Capture and raid attribution retain effects even when this estimator does not model them.
        Assert.Equal(applied.GetValueOrDefault() / 100.0, Assert.Single(RaidBuffPolicy.GetEffects(buff, false, Source)).Amount, 9);
        Assert.Equal(buff, Assert.Single(direct.SourceStatuses));
    }
}
