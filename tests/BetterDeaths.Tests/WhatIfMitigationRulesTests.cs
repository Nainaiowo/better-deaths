namespace BetterDeaths.Tests;

public sealed class WhatIfMitigationRulesTests
{
    [Fact]
    public void DisplayAndWhatIfShareMultiplicativeMathWithoutDoubleCountingProviders()
    {
        var reprisal = CreateStatus(1193, "Reprisal", 100);
        var addle = CreateStatus(1203, "Addle", 200);
        var statuses = new[] { reprisal, reprisal with { SourceId = 300 }, addle };
        var total = WhatIfMitigationRules.CalculateTotal(statuses)!;
        Assert.Equal(.1, total.AllReduction, 8);
        Assert.Equal(.145, total.PhysicalReduction, 8);
        Assert.Equal(.19, total.MagicReduction, 8);
        Assert.Equal(.81, WhatIfMitigationRules.DamageMultiplier(statuses, DamageType.Magic), 8);
        Assert.Equal(.855, WhatIfMitigationRules.DamageMultiplier(statuses, DamageType.Physical), 8);
        Assert.Equal(.9, WhatIfMitigationRules.DamageMultiplier(statuses, DamageType.Unknown), 8);
    }

    [Theory]
    [InlineData(DamageType.Slashing, .855)]
    [InlineData(DamageType.Piercing, .855)]
    [InlineData(DamageType.Blunt, .855)]
    [InlineData(DamageType.Shot, .855)]
    [InlineData(DamageType.Physical, .855)]
    [InlineData(DamageType.Magic, .81)]
    [InlineData(DamageType.Breath, .81)]
    [InlineData(DamageType.Unknown, .9)]
    public void SharedMathPreservesEveryExistingDamageTypeRule(DamageType type, double expected)
    {
        Assert.Equal(expected, WhatIfMitigationRules.DamageMultiplier(
            [CreateStatus(1193, "Reprisal", 100), CreateStatus(1203, "Addle", 200)], type), 8);
    }

    [Fact]
    public void ShieldsHealingAndNameReusedOffensiveBuffsDoNotInventPercentReduction()
    {
        var statuses = new[] { CreateStatus(3892, "The Spire", 100), CreateStatus(1892, "Neutral Sect", 100),
            CreateStatus(1883, "The Bole", 100), CreateStatus(1197, "Tactician", 100) };
        Assert.Null(WhatIfMitigationRules.CalculateTotal(statuses));
        Assert.Equal(1, WhatIfMitigationRules.DamageMultiplier(statuses, DamageType.Magic));
    }

    [Fact]
    public void DeduplicateStatuses_CountsDuplicateBossDebuffOnce()
    {
        var firstFeint = CreateStatus(1195, "Feint", 100);
        var secondFeint = CreateStatus(1195, "Feint", 200);

        var result = WhatIfMitigationRules.DeduplicateStatuses([firstFeint, secondFeint]);

        Assert.Equal([firstFeint], result);
    }

    [Fact]
    public void ShareStatus_MatchesSameTargetedMitigationFromDifferentSources()
    {
        var firstAquaveil = CreateStatus(2708, "Aquaveil", 100);
        var secondAquaveil = CreateStatus(2708, "Aquaveil", 200);

        Assert.True(WhatIfMitigationRules.ShareStatus([firstAquaveil], [secondAquaveil]));
    }

    [Fact]
    public void ShareStatus_DoesNotMatchDifferentMitigationEffects()
    {
        var feint = CreateStatus(1195, "Feint", 100);
        var reprisal = CreateStatus(1193, "Reprisal", 200);

        Assert.False(WhatIfMitigationRules.ShareStatus([feint], [reprisal]));
    }

    [Fact]
    public void DeduplicateStatuses_UsesNameForStatusesWithoutIds()
    {
        var first = CreateStatus(0, "Dark Missionary", 100);
        var second = CreateStatus(0, "dark missionary", 200);

        var result = WhatIfMitigationRules.DeduplicateStatuses([first, second]);

        Assert.Equal([first], result);
    }

    private static StatusSnapshot CreateStatus(uint id, string name, uint sourceId)
    {
        return new StatusSnapshot(id, name, 0, sourceId, 0, 10.0f);
    }
}
