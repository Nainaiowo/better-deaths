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
            DamageMeterColumn.Rank,
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.TotalDamage,
        ];

        var changed = DamageMeterColumnPolicy.Move(
            columns,
            DamageMeterColumn.Rank,
            DamageMeterColumn.TotalDamage);

        Assert.True(changed);
        Assert.Equal(
        [
            DamageMeterColumn.PlayerName,
            DamageMeterColumn.TotalDamage,
            DamageMeterColumn.Rank,
        ], columns);
    }
}
