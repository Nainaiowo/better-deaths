using BetterDeaths.PartyCooldowns;
using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownPlacementsTests
{
    [Fact]
    public void NewInstallUsesLeftSideWithTheCapturedLayoutDefaults()
    {
        var placements = PartyCooldownPlacements.CreateDefault();
        Assert.Equal(PartyCooldownSide.Left, PartyCooldownPlacements.DefaultSide);
        Assert.Equal(new PartyCooldownPlacement { IconSize = 48, Spacing = 3, OffsetX = -77, OffsetY = 9 },
            placements.Left);
        Assert.Equal(new PartyCooldownPlacement(), placements.Right);
        Assert.False(placements.Normalize(new()));
    }

    [Fact]
    public void DefaultProfilesAreIndependentOfEachOtherAndOfOtherInstallations()
    {
        var first = PartyCooldownPlacements.CreateDefault();
        var second = PartyCooldownPlacements.CreateDefault();
        Assert.NotSame(first.Left, first.Right);
        Assert.NotSame(first.Left, second.Left);
        Assert.NotSame(first.Right, second.Right);
        first.Left!.IconSize = 72;
        first.Left.OffsetX = -200;
        first.Right!.Spacing = 10;
        Assert.Equal(48, second.Left!.IconSize);
        Assert.Equal(-77, second.Left.OffsetX);
        Assert.Equal(3, second.Right!.Spacing);
        Assert.Equal(24, first.Right.IconSize);
    }

    [Fact]
    public void SavingAndReloadingTheNewDefaultsKeepsBothSides()
    {
        var placements = PartyCooldownPlacements.CreateDefault();
        var restored = JsonSerializer.Deserialize<PartyCooldownPlacements>(JsonSerializer.Serialize(placements))!;
        Assert.False(restored.Normalize(new()));
        Assert.Equal(placements.Left, restored.Left);
        Assert.Equal(placements.Right, restored.Right);
    }

    [Fact]
    public void PreviouslySavedOldDefaultsAreNotReplacedWithTheNewDefaults()
    {
        var saved = JsonSerializer.Deserialize<PartyCooldownPlacements>(
            """{"Left":{"IconSize":24,"Spacing":3,"OffsetX":0,"OffsetY":0},"Right":{"IconSize":24,"Spacing":3,"OffsetX":0,"OffsetY":0}}""")!;
        var left = saved.Left;
        var right = saved.Right;
        Assert.False(saved.Normalize(new()));
        Assert.Same(left, saved.Left);
        Assert.Same(right, saved.Right);
        Assert.Equal(new PartyCooldownPlacement(), saved.Left);
        Assert.NotEqual(PartyCooldownPlacements.CreateDefault().Left, saved.Left);
    }

    [Fact]
    public void LegacySharedDefaultsStillMigrateWithoutRepositioningEitherSide()
    {
        var legacy = new PartyCooldownPlacement();
        var placements = new PartyCooldownPlacements();
        Assert.True(placements.Normalize(legacy));
        Assert.Equal(legacy, placements.Left);
        Assert.Equal(legacy, placements.Right);
        Assert.NotEqual(PartyCooldownPlacements.CreateDefault().Left, placements.Left);
    }

    [Fact]
    public void ExistingSharedLayoutMigratesToTwoIndependentProfiles()
    {
        var legacy = new PartyCooldownPlacement { IconSize = 48, Spacing = 7, OffsetX = -42, OffsetY = 18 };
        var placements = new PartyCooldownPlacements();
        Assert.True(placements.Normalize(legacy));
        Assert.Equal(legacy, placements.Get(PartyCooldownSide.Left));
        Assert.Equal(legacy, placements.Get(PartyCooldownSide.Right));
        Assert.NotSame(legacy, placements.Left);
        Assert.NotSame(placements.Left, placements.Right);
        placements.Get(PartyCooldownSide.Left).IconSize = 72;
        placements.Get(PartyCooldownSide.Left).Spacing = 12;
        placements.Get(PartyCooldownSide.Left).OffsetX = -100;
        placements.Get(PartyCooldownSide.Left).OffsetY = 50;
        Assert.Equal(legacy, placements.Get(PartyCooldownSide.Right));
        Assert.False(placements.Normalize(legacy));
        Assert.Equal(72, placements.Get(PartyCooldownSide.Left).IconSize);
    }

    [Theory]
    [InlineData(PartyCooldownSide.Left)]
    [InlineData(PartyCooldownSide.Right)]
    public void ApplyingDefaultsOnlyChangesTheSelectedSidesLayout(PartyCooldownSide side)
    {
        var placements = new PartyCooldownPlacements
        {
            Left = new() { IconSize = 72, Spacing = 10, OffsetX = -200, OffsetY = 25 },
            Right = new() { IconSize = 36, Spacing = 6, OffsetX = 63, OffsetY = 16 },
        };
        var otherSide = side == PartyCooldownSide.Left ? PartyCooldownSide.Right : PartyCooldownSide.Left;
        var selected = placements.Get(side);
        var other = placements.Get(otherSide);
        var originalOther = other with { };
        placements.ApplyDefault(side);
        Assert.Same(selected, placements.Get(side));
        Assert.Equal(PartyCooldownPlacements.CreateDefault().Get(side), selected);
        Assert.Same(other, placements.Get(otherSide));
        Assert.Equal(originalOther, other);
    }

    [Fact]
    public void AppliedDefaultsCanBeRepeatedAndSavedWithoutRepositioningTheOtherSide()
    {
        var placements = new PartyCooldownPlacements();
        placements.Normalize(new() { IconSize = 40, Spacing = 7, OffsetX = 33, OffsetY = -12 });
        var right = placements.Right! with { };
        placements.ApplyDefault(PartyCooldownSide.Left);
        placements.Left!.IconSize = 64;
        placements.Left.OffsetY = 100;
        placements.ApplyDefault(PartyCooldownSide.Left);
        var restored = JsonSerializer.Deserialize<PartyCooldownPlacements>(JsonSerializer.Serialize(placements))!;
        Assert.Equal(PartyCooldownPlacements.CreateDefault().Left, restored.Left);
        Assert.Equal(right, restored.Right);
    }

    [Fact]
    public void SwitchingSidesAndReloadingPreservesTheirOwnSizeSpacingAndOffsets()
    {
        var placements = new PartyCooldownPlacements
        {
            Left = new() { IconSize = 72, Spacing = 12, OffsetX = -100, OffsetY = 50 },
            Right = new() { IconSize = 24, Spacing = 3, OffsetX = 15, OffsetY = -20 },
        };
        Assert.False(placements.Normalize(new()));
        var restored = JsonSerializer.Deserialize<PartyCooldownPlacements>(JsonSerializer.Serialize(placements))!;
        Assert.False(restored.Normalize(new()));
        foreach (var side in new[] { PartyCooldownSide.Left, PartyCooldownSide.Right, PartyCooldownSide.Left, PartyCooldownSide.Right })
            Assert.Equal(placements.Get(side), restored.Get(side));
        Assert.NotSame(restored.Left, restored.Right);
    }

    [Fact]
    public void MissingProfileDoesNotOverwriteAnExistingSide()
    {
        var left = new PartyCooldownPlacement { IconSize = 64, Spacing = 8, OffsetX = -200, OffsetY = 12 };
        var placements = new PartyCooldownPlacements { Left = left };
        var legacy = new PartyCooldownPlacement { IconSize = 32, OffsetX = 30 };
        Assert.True(placements.Normalize(legacy));
        Assert.Same(left, placements.Left);
        Assert.Equal(legacy, placements.Right);
    }

    [Fact]
    public void ResettingSelectedOffsetsLeavesBothSizesAndOtherSideUnchanged()
    {
        var placements = new PartyCooldownPlacements();
        placements.Normalize(new() { IconSize = 48, Spacing = 8, OffsetX = 100, OffsetY = -50 });
        var selected = placements.Get(PartyCooldownSide.Left);
        selected.OffsetX = 0;
        selected.OffsetY = 0;
        Assert.Equal(48, selected.IconSize);
        Assert.Equal(8, selected.Spacing);
        Assert.Equal(100, placements.Get(PartyCooldownSide.Right).OffsetX);
        Assert.Equal(-50, placements.Get(PartyCooldownSide.Right).OffsetY);
    }

    [Fact]
    public void EachSideIsNormalizedAndSharedReferencesAreSeparated()
    {
        var shared = new PartyCooldownPlacement { IconSize = 99, Spacing = -1, OffsetX = 500, OffsetY = -500 };
        var placements = new PartyCooldownPlacements { Left = shared, Right = shared };
        Assert.True(placements.Normalize(new()));
        Assert.NotSame(placements.Left, placements.Right);
        foreach (var side in new[] { PartyCooldownSide.Left, PartyCooldownSide.Right })
        {
            var layout = placements.Get(side);
            Assert.Equal(72, layout.IconSize);
            Assert.Equal(0, layout.Spacing);
            Assert.Equal(300, layout.OffsetX);
            Assert.Equal(-300, layout.OffsetY);
        }
        placements.Left!.IconSize = 16;
        Assert.Equal(72, placements.Right!.IconSize);
    }

    [Fact]
    public void InvalidNumbersAreRepairedWithoutChangingTheOtherSide()
    {
        var right = new PartyCooldownPlacement { IconSize = 40, Spacing = 9, OffsetX = 20, OffsetY = 30 };
        var placements = new PartyCooldownPlacements
        {
            Left = new() { IconSize = float.NaN, Spacing = float.PositiveInfinity, OffsetX = float.NaN, OffsetY = float.NegativeInfinity },
            Right = right,
        };
        Assert.True(placements.Normalize(new()));
        Assert.Equal(new PartyCooldownPlacement(), placements.Left);
        Assert.Same(right, placements.Right);
        Assert.Equal(40, right.IconSize);
    }

    [Fact]
    public void ReadingTheActiveLayoutAllocatesNoManagedMemory()
    {
        var placements = new PartyCooldownPlacements();
        placements.Normalize(new());
        _ = placements.Get(PartyCooldownSide.Left);
        var before = GC.GetAllocatedBytesForCurrentThread();
        float checksum = 0;
        for (var index = 0; index < 10000; index++)
            checksum += placements.Get(index % 2 == 0 ? PartyCooldownSide.Left : PartyCooldownSide.Right).IconSize;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(240000, checksum);
        Assert.Equal(0, allocated);
    }
}
