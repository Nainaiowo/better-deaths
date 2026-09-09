namespace BetterDeaths.Tests;

using System.Reflection;
using BetterDeaths.DamageParsing;

public sealed class CriticalCalibrationTimingTests
{
    private static readonly DateTime Start = new(2026, 9, 9, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x10000001, "Player", 0, "", true, 23) { Level = 100 };
    private static readonly DamageActorIdentity Provider = new(0x10000002, "Provider", 0, "", true, 23);
    private static readonly DamageActorIdentity Target = new(0x40000001, "Target", 0, "", false, 0);

    // Recorded countdowns and independently replayed expiry decisions from the testing-37 capture.
    public static TheoryData<uint, uint, uint, float, float> CapturedExpiryCases => new()
    {
        { 39, 24378, 2216, 2.6136851f, .805f },
        { 23, 36977, 2216, 2.5812697f, .716f },
        { 40, 24312, 2216, 2.0855849f, .269f },
        { 21, 7, 2216, 2.0710144f, .225f },
        { 39, 24375, 2216, 2.7071357f, .995f },
        { 40, 37033, 2216, 2.275853f, .550f },
        { 39, 7, 2216, 2.2829978f, .505f },
        { 21, 7, 2216, 1.9720206f, .726f },
        { 21, 3549, 1177, 1.0128075f, .920f },
        { 23, 3558, 2216, 2.007188f, .775f },
    };

    [Theory]
    [MemberData(nameof(CapturedExpiryCases))]
    public void CapturedExpiryDecisionsUseTheNetworkTimeline(uint job, uint action, uint buff, float memory, float remaining)
    {
        var source = Player with { ClassJobId = job };
        var module = new DamageParsingModule();
        module.ObserveStatusTiming(Update(source, buff, 30, Start.AddSeconds(remaining - 30.0)));
        var packet = Packet(source, action, Start, [Status(buff, memory)]);
        var hit = Assert.Single(module.Process(packet));
        Assert.Equal(1, CriticalSamples(module, source));
        Assert.Equal(1000, hit.RawMeterAmount);
        Assert.Equal(memory, Assert.Single(hit.SourceStatuses).RemainingTime);
    }

    [Theory]
    [InlineData(19u)] [InlineData(20u)] [InlineData(21u)] [InlineData(22u)]
    [InlineData(23u)] [InlineData(24u)] [InlineData(25u)] [InlineData(27u)]
    [InlineData(28u)] [InlineData(30u)] [InlineData(31u)] [InlineData(32u)]
    [InlineData(33u)] [InlineData(34u)] [InlineData(35u)] [InlineData(36u)]
    [InlineData(37u)] [InlineData(38u)] [InlineData(39u)] [InlineData(40u)]
    [InlineData(41u)] [InlineData(42u)]
    public void SharedChanceBuffTimingDoesNotDependOnTheRecipientsJob(uint job)
    {
        var source = Player with { ClassJobId = job };
        var module = new DamageParsingModule();
        module.ObserveStatusTiming(Update(source, 2216, 3, Start));
        module.Process(Packet(source, 7, Start.AddSeconds(1), [Status(2216, .5f)]));
        Assert.Equal(0, CriticalSamples(module, source));
        module.Process(Packet(source, 7, Start.AddSeconds(2.1), [Status(2216, 2)]));
        Assert.Equal(1, CriticalSamples(module, source));
    }

    [Fact]
    public void SignedRefreshKeepsTheWarriorGuaranteedHitExcluded()
    {
        var source = Player with { ClassJobId = 21 };
        var module = new DamageParsingModule();
        module.ObserveStatusTiming(Update(source, 1177, 15, Start));
        module.ObserveStatusTiming(Update(source, 1177, -15, Start.AddSeconds(.715)));
        var hit = Assert.Single(module.Process(Packet(source, 3549, Start.AddSeconds(.715), [Status(1177, 14.2f)])));
        Assert.Equal(0, CriticalSamples(module, source));
        Assert.Equal(1000, hit.RawMeterAmount);
    }

    [Theory]
    [InlineData(24u, 120u)]
    [InlineData(28u, 190u)]
    [InlineData(33u, 3594u)]
    [InlineData(40u, 24284u)]
    public void HealingSamplesUseTheSameChanceBuffTimeline(uint job, uint action)
    {
        var source = Player with { ClassJobId = job };
        var module = new DamageParsingModule();
        module.ObserveStatusTiming(Update(source, 2216, 3, Start));
        DamageActionPacket Heal(double seconds, float memory) => Packet(source, action, Start.AddSeconds(seconds), [Status(2216, memory)]) with
        {
            Targets = [new(0, source, [new(0, 4, 0, 0x20, 0, 0, 1000)]) { HasTargetStatusSnapshot = true }],
            HealingPotency = 500,
        };
        module.Process(Heal(1, .5f));
        Assert.Equal(0, CriticalSamples(module, source));
        module.Process(Heal(2.1, 2));
        Assert.Equal(1, CriticalSamples(module, source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedDamageAndHealingKeepTheirOriginalEffectOrder(bool healFirst)
    {
        var source = Player with { ClassJobId = 40 };
        var module = new DamageParsingModule();
        module.ObserveStatusTiming(Update(source, 2216, 1.5f, Start));
        DamageActionEffect damage = new(healFirst ? 1 : 0, 3, 0x20, 0, 0, 0, 1000);
        DamageActionEffect heal = new(healFirst ? 0 : 1, 4, 0, 0, 0, 0, 1000);
        var packet = Packet(source, 24312, Start.AddSeconds(1), [Status(2216, 2)]) with
        {
            Targets = [new(0, Target, healFirst ? [heal, damage] : [damage, heal]) { HasTargetStatusSnapshot = true }],
            HealingPotency = 170,
        };
        Assert.Equal(1000, Assert.Single(module.Process(packet)).RawMeterAmount);
        Assert.Equal(2, CriticalSamples(module, source));
    }

    [Theory]
    [InlineData(31u, 16498u, 851u)]
    [InlineData(22u, 78u, 116u)]
    public void ConsumedGuaranteedBuffContextStillBelongsToTheHit(uint job, uint action, uint buff)
    {
        var source = Player with { ClassJobId = job };
        var module = new DamageParsingModule();
        module.ObserveStatusTiming(Update(source, buff, 5, Start));
        module.ObserveStatusTiming(Update(source, 0, 0, Start.AddSeconds(1)));
        module.Process(Packet(source, action, Start.AddSeconds(1), [Status(buff, 4)]));
        Assert.Equal(0, CriticalSamples(module, source));
    }

    private static DamageStatusSnapshot Status(uint status, float remaining) =>
        new(status, Provider, 0, remaining) { StatusSlot = 30 };
    private static DamageStatusTimingUpdate Update(DamageActorIdentity source, uint status, float duration, DateTime time) =>
        new(source.EntityId, 30, Status(status, duration), time);
    private static DamageActionPacket Packet(DamageActorIdentity source, uint action, DateTime time, DamageStatusSnapshot[] statuses) =>
        new(time.Ticks, time, (uint)(time - Start).Ticks, source, action, "Attack",
            [new(0, Target, [new(0, 3, 0x20, 0, 0, 0, 1000)])])
        { SourceStatuses = statuses, HasSourceStatusSnapshot = true, ActionCategoryId = 2 };
    private static int CriticalSamples(DamageParsingModule module, DamageActorIdentity source)
    {
        var tracker = typeof(DamageParsingModule).GetField("periodicDamageTracker", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(module)!;
        var snapshot = typeof(PeriodicDamageTracker).GetMethod("GetCompatibilityCalibration", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(tracker, [source])!;
        return (int)snapshot.GetType().GetProperty("CriticalSamples")!.GetValue(snapshot)!;
    }
}
