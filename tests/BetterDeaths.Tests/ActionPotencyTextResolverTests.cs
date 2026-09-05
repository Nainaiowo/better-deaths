namespace BetterDeaths.DamageParsing;

using Lumina.Text.ReadOnly;

public sealed class ActionPotencyTextResolverTests
{
    private const string Dia = "Deals unaspected damage with a potency of <if([gnum68==24],<if([gnum72>=94],85,65)>,65)>." +
        "<br><colortype(504)>Additional Effect: <colortype(0)>Unaspected damage over time" +
        "<br>Potency: <if([gnum68==24],<if([gnum72>=94],85,65)>,65)><br>Duration: 30s";

    [Theory]
    [InlineData(24, 100, 85)]
    [InlineData(24, 94, 85)]
    [InlineData(24, 93, 65)]
    [InlineData(24, 72, 65)]
    [InlineData(25, 100, 65)]
    public void ResolvesCapturedDiaTooltipForTheSource(uint job, byte level, double expected)
    {
        var description = ReadOnlySeString.FromMacroString(Dia);
        Assert.Null(ActionPotencyProfileParser.Parse(description.ExtractText(), true).PeriodicPotency);
        var result = Parse(description, job, level);
        Assert.Equal(expected, result.DirectPotency);
        Assert.Equal(expected, result.PeriodicPotency);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(24, 0)]
    public void MissingActorContextDoesNotGuessATrait(uint job, byte level)
    {
        var result = Parse(ReadOnlySeString.FromMacroString(Dia), job, level);
        Assert.Null(result.DirectPotency);
        Assert.Null(result.PeriodicPotency);
    }

    [Theory]
    [InlineData(100, 350)]
    [InlineData(93, 310)]
    public void ResolvesGlareThreeCalibrationPotency(byte level, double expected)
    {
        var description = ReadOnlySeString.FromMacroString(
            "Deals unaspected damage with a potency of <if([gnum68==24],<if([gnum72>=94],350,310)>,310)>.");
        Assert.Equal(expected, Parse(description, 24, level).DirectPotency);
    }

    [Fact]
    public void LeavesUnknownGameStateUnknown()
    {
        var description = ReadOnlySeString.FromMacroString(
            "Deals damage with a potency of <if([gnum99==1],900,300)>. Additional Effect: Damage over time Potency: <num(gnum99)>");
        var result = Parse(description, 24, 100);
        Assert.Null(result.DirectPotency);
        Assert.Null(result.PeriodicPotency);
    }

    [Fact]
    public void PreservesFixedPotencyAndVariablePotencyRejection()
    {
        var description = ReadOnlySeString.FromMacroString(
            "Deals lightning damage with a potency of 150.<br>Additional Effect: Lightning damage over time<br>Potency: 60<br>Duration: 30s");
        Assert.Equal(new ActionPotencyProfile(150, 60), Parse(description, 25, 100));
        Assert.Null(Parse(ReadOnlySeString.FromMacroString(
            "Delivers an attack with a potency of <num(100)>. Combo Potency: 400"), 21, 100).DirectPotency);
    }

    [Fact]
    public void CapturedOpeningTicksGetPotencyWithoutChangingObservedDamage()
    {
        var start = new DateTime(2026, 9, 4, 23, 14, 6, DateTimeKind.Utc);
        var healer = new DamageActorIdentity(0x1001, "Healer", 0, "", true, 24) { Level = 100 };
        var caster = new DamageActorIdentity(0x1002, "Caster", 0, "", true, 25) { Level = 100 };
        var target = new DamageActorIdentity(0x40000001, "Target", 0, "", false, 0);
        var profile = Parse(ReadOnlySeString.FromMacroString(Dia), healer.ClassJobId, healer.Level);
        var module = new DamageParsingModule();
        var rates = new DamageBaseRateSnapshot(0.251, 0.096);
        var dia = new DamageStatusApplication(target, healer, 1871, "Dia", 0, 16532, "Dia", start,
            30, true, false, false)
        {
            PeriodicPotency = profile.PeriodicPotency,
            BaseDamageLowByte = 80,
            CriticalRateLowByte = 251,
            SourceBaseRates = rates,
            HasSourceStatusSnapshot = true,
        };
        var direct = Assert.Single(module.Process(new DamageActionPacket(1, start, 1, healer, 16532, "Dia",
            [new DamageActionTarget(0, target, [new DamageActionEffect(0, 3, 0, 0x75, 0, 0, 9072)])])
        {
            DirectPotency = profile.DirectPotency,
            CanCalibratePotency = true,
            SourceBaseRates = rates,
            HasSourceStatusSnapshot = true,
            StatusApplications = [dia],
        }));
        module.Process(new DamageActionPacket(2, start.AddMilliseconds(110), 2, caster, 36986, "High Thunder",
            [new DamageActionTarget(0, target, [new DamageActionEffect(0, 3, 0x40, 0x45, 0, 0, 23718)])])
        {
            DirectPotency = 150,
            CanCalibratePotency = true,
            HasSourceStatusSnapshot = true,
            StatusApplications = [new DamageStatusApplication(target, caster, 3871, "High Thunder", 0, 36986,
                "High Thunder", start.AddMilliseconds(110), 30, true, false, false)
            {
                PeriodicPotency = 60,
                BaseDamageLowByte = 188,
                CriticalRateLowByte = 6,
                HasSourceStatusSnapshot = true,
            }],
        });
        module.ProcessPeriodicTick(new PeriodicDamageTick(3, start.AddSeconds(1), target, 0, "", 0, 7930, null));
        var ticks = module.FlushPendingPeriodicTicks(start.AddSeconds(2));
        var healerTick = Assert.Single(ticks, tick => tick.Source.EntityId == healer.EntityId);

        Assert.Equal(9072u, direct.Amount);
        Assert.Equal(2, ticks.Count);
        Assert.Equal(7930.0, ticks.Sum(tick => tick.RawMeterAmount));
        Assert.Equal(85, healerTick.PeriodicEstimateInputs!.Potency);
        Assert.True(healerTick.SimulatedPeriodicAmount > 0);
        Assert.Equal(PeriodicAllocationBasis.PotencyEstimate, healerTick.PeriodicAllocationBasis);
        Assert.Null(healerTick.PeriodicEstimateUnavailableReason);
    }

    [Fact]
    public void ActorLevelSurvivesSerializationAndTemporaryIdentityLoss()
    {
        var source = new DamageActorIdentity(0x1001, "Player", 0, "", true, 24) { Level = 94 };
        var restored = System.Text.Json.JsonSerializer.Deserialize<DamageActorIdentity>(
            System.Text.Json.JsonSerializer.Serialize(source));
        Assert.Equal(source, restored);
        var target = new DamageActorIdentity(0x40000001, "Target", 0, "", false, 0);
        var packet = new DamageActionPacket(1, DateTime.UtcNow, 1, source, 16532, "Dia",
            [new DamageActionTarget(0, target, [new DamageActionEffect(0, 3, 0, 0, 0, 0, 1000)])]);
        var module = new DamageParsingModule();
        module.Process(packet);
        var later = Assert.Single(module.Process(packet with { ActionSequence = 2, Source = source with { Level = 0 } }));
        Assert.Equal(94, later.Source.Level);
        var synced = Assert.Single(module.Process(packet with { ActionSequence = 3, Source = source with { Level = 93 } }));
        Assert.Equal(93, synced.Source.Level);
    }

    private static ActionPotencyProfile Parse(ReadOnlySeString description, uint job, byte level) =>
        ActionPotencyProfileParser.Parse(ActionPotencyTextResolver.Resolve(description, job, level), true);
}
