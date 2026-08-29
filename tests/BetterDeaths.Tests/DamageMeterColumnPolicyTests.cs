namespace BetterDeaths;

public sealed class DamageMeterColumnPolicyTests
{
    [Fact]
    public void NormalizeRemovesDuplicatesAndUnknownValuesWithoutChangingOrder()
    {
        var normalized = DamageMeterColumnPolicy.Normalize(
        [
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.TotalDamage,
            DamageMeterColumn.PlayerName,
            (DamageMeterColumn)999,
            DamageMeterColumn.DamagePerSecond,
        ]);

        Assert.Equal(
        [
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.TotalDamage,
            DamageMeterColumn.DamagePerSecond,
        ], normalized);
    }

    [Fact]
    public void NormalizeRestoresDefaultsWhenNoUsableColumnsRemain()
    {
        var normalized = DamageMeterColumnPolicy.Normalize([(DamageMeterColumn)999]);

        Assert.Equal(DamageMeterColumnPolicy.CreateDefault(), normalized);
    }

    [Fact]
    public void NormalizeRemovesTheLegacyRankColumnWithoutRenumberingSavedColumns()
    {
        var normalized = DamageMeterColumnPolicy.Normalize(
        [
            (DamageMeterColumn)0,
            DamageMeterColumn.JobIcon,
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.DamagePerSecond,
        ]);

        Assert.Equal(
        [
            DamageMeterColumn.JobIcon,
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.DamagePerSecond,
        ], normalized);
        Assert.Equal(1, (int)DamageMeterColumn.JobIcon);
        Assert.Equal(2, (int)DamageMeterColumn.PlayerName);
        Assert.Equal(4, (int)DamageMeterColumn.DamagePerSecond);
    }

    [Fact]
    public void MoveReordersAnExistingColumn()
    {
        var columns = new List<DamageMeterColumn>
        {
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.TotalDamage,
            DamageMeterColumn.DamagePerSecond,
        };

        var changed = DamageMeterColumnPolicy.Move(
            columns,
            DamageMeterColumn.DamagePerSecond,
            DamageMeterColumn.PlayerName);

        Assert.True(changed);
        Assert.Equal(
        [
            DamageMeterColumn.DamagePerSecond,
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.TotalDamage,
        ], columns);
    }

    [Fact]
    public void MovingAColumnForwardUsesTheTargetSlot()
    {
        List<DamageMeterColumn> columns =
        [
            DamageMeterColumn.JobIcon,
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.TotalDamage,
        ];

        var changed = DamageMeterColumnPolicy.Move(
            columns,
            DamageMeterColumn.JobIcon,
            DamageMeterColumn.TotalDamage);

        Assert.True(changed);
        Assert.Equal(
        [
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.TotalDamage,
            DamageMeterColumn.JobIcon,
        ], columns);
    }

    [Fact]
    public void PlaceBeforeAddsAnInactiveColumnAtTheDropTarget()
    {
        List<DamageMeterColumn> columns =
        [
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.TotalDamage,
        ];

        var changed = DamageMeterColumnPolicy.PlaceBefore(
            columns,
            DamageMeterColumn.CriticalHitPercent,
            DamageMeterColumn.TotalDamage);

        Assert.True(changed);
        Assert.Equal(
        [
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.CriticalHitPercent,
            DamageMeterColumn.TotalDamage,
        ], columns);
    }
}
