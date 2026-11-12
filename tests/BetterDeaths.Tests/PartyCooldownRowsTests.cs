using System.Text.Json;
using BetterDeaths.PartyCooldowns;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownRowsTests
{
    [Fact]
    public void ExistingSelectionsStartInOneRowWithoutEnablingCustomRows()
    {
        var rows = new PartyCooldownRows();
        Assert.True(rows.Normalize([10, 20, 30]));
        Assert.False(rows.Enabled);
        Assert.Equal([10u, 20, 30], rows.Rows.Single());
        Assert.False(rows.Normalize([10, 20, 30]));
    }

    [Fact]
    public void TogglingOffAndReloadingRemembersTheCustomArrangement()
    {
        var rows = new PartyCooldownRows { Enabled = true, Rows = [[20], [30, 10], []] };
        Assert.False(rows.Normalize([10, 20, 30]));
        Assert.Equal(1, rows.RowFor(10));
        rows.Enabled = false;
        var restored = JsonSerializer.Deserialize<PartyCooldownRows>(JsonSerializer.Serialize(rows))!;
        Assert.False(restored.Normalize([10, 20, 30]));
        Assert.False(restored.Enabled);
        Assert.Equal(0, restored.RowFor(10));
        Assert.Equal([20u, 30, 10], restored.Flatten());
        restored.Enabled = true;
        Assert.Equal(0, restored.RowFor(20));
        Assert.Equal(1, restored.RowFor(30));
        Assert.Equal(1, restored.RowFor(10));
        Assert.Equal(3, restored.Rows.Count);
    }

    [Fact]
    public void FlatListEditsKeepRememberedGroupsRemoveDeselectedIdsAndAppendNewIdsToRowOne()
    {
        var rows = new PartyCooldownRows { Rows = [[20], [30, 10]] };
        Assert.True(rows.Normalize([10, 40, 20]));
        Assert.Equal([20u, 40], rows.Rows[0]);
        Assert.Equal([10u], rows.Rows[1]);
        rows.Enabled = true;
        Assert.Equal([20u, 40, 10], rows.Flatten());
    }

    [Fact]
    public void NormalizationRepairsMalformedRowsWithoutLosingValidSelections()
    {
        var rows = new PartyCooldownRows { Enabled = true, Rows = [[0, 2, 2, 99], null!, [1, 4], [3, 5]] };
        Assert.True(rows.Normalize([1, 2, 3, 4, 5, 6]));
        Assert.Equal([2u, 3, 5, 6], rows.Rows[0]);
        Assert.Empty(rows.Rows[1]);
        Assert.Equal([1u, 4], rows.Rows[2]);
        Assert.Equal(6, rows.Flatten().Count);
        Assert.False(rows.Normalize([1, 2, 3, 4, 5, 6]));
        Assert.True(rows.Enabled);
    }

    [Theory]
    [InlineData("{\"Rows\":null}")]
    [InlineData("{\"Rows\":[]}")]
    [InlineData("{\"Rows\":[null]}")]
    public void MissingOrEmptyRowsRecoverAsRowOne(string json)
    {
        var rows = JsonSerializer.Deserialize<PartyCooldownRows>(json)!;
        Assert.True(rows.Normalize([10, 20]));
        Assert.Equal([10u, 20], rows.Rows.Single());
    }

    [Fact]
    public void NormalizationCapsTotalAbilitiesButNotEachIndividualRow()
    {
        var limit = PartyCooldownRows.MaximumAbilities;
        var selected = Enumerable.Range(1, limit + 1).Select(id => (uint)id).ToArray();
        var rows = new PartyCooldownRows { Rows = [[], [], selected.ToList()] };
        rows.Normalize(selected);
        Assert.Equal(selected[..limit], rows.Rows[2]);
        Assert.Equal(limit, rows.Flatten().Count);
        Assert.Equal(3, rows.Rows.Count);
    }

    [Fact]
    public void RowsAreAddedUpToThreeAndOnlyEmptyAdditionalRowsCanBeRemoved()
    {
        var rows = new PartyCooldownRows();
        rows.Normalize([1, 2]);
        Assert.True(rows.AddRow());
        Assert.True(rows.AddRow());
        Assert.False(rows.AddRow());
        Assert.False(rows.RemoveEmptyRow(0));
        Assert.False(rows.RemoveEmptyRow(-1));
        Assert.False(rows.RemoveEmptyRow(3));
        Assert.True(rows.Place(1, 1));
        Assert.False(rows.RemoveEmptyRow(1));
        Assert.True(rows.Place(1, 2));
        Assert.True(rows.RemoveEmptyRow(1));
        Assert.Equal([1u], rows.Rows[1]);
        Assert.Equal([2u], rows.Rows[0]);
    }

    [Fact]
    public void MovesBetweenRowsAndWithinRowsAllowEveryInsertionPosition()
    {
        var rows = new PartyCooldownRows { Rows = [[1, 2, 3], [4], []] };
        Assert.True(rows.Place(2, 1, 4));
        Assert.Equal([2u, 4], rows.Rows[1]);
        Assert.Equal([1u, 3], rows.Rows[0]);
        Assert.True(rows.Place(1, 1, 4, after: true));
        Assert.Equal([2u, 4, 1], rows.Rows[1]);
        Assert.True(rows.Place(2, 1));
        Assert.Equal([4u, 1, 2], rows.Rows[1]);
        Assert.False(rows.Place(2, 1));
        Assert.True(rows.Place(3, 2));
        Assert.Empty(rows.Rows[0]);
        Assert.Equal([3u], rows.Rows[2]);
        Assert.Equal(4, rows.Flatten().Count);
    }

    [Fact]
    public void MovingAnExistingAbilityAtCapacityWorksButAddingAnotherDoesNot()
    {
        var limit = PartyCooldownRows.MaximumAbilities;
        var last = (uint)limit;
        var next = last + 1;
        var rows = new PartyCooldownRows { Rows = [Enumerable.Range(1, limit).Select(id => (uint)id).ToList(), []] };
        Assert.False(rows.Place(next, 0));
        Assert.False(rows.Place(next, 1));
        Assert.True(rows.Place(last, 1));
        Assert.True(rows.Place(1, 1, last, after: true));
        Assert.Equal([last, 1], rows.Rows[1]);
        Assert.Equal(limit, rows.Flatten().Count);
        Assert.True(rows.Remove(5));
        Assert.False(rows.Remove(5));
        Assert.True(rows.Place(next, 0));
        Assert.Equal(limit, rows.Flatten().Count);
    }

    [Fact]
    public void RejectedMovesLeaveTheOriginalArrangementUntouched()
    {
        var rows = new PartyCooldownRows { Rows = [[1, 2], [3]] };
        var before = JsonSerializer.Serialize(rows);
        Assert.False(rows.Place(0, 0));
        Assert.False(rows.Place(1, -1));
        Assert.False(rows.Place(1, 3));
        Assert.False(rows.Place(1, 1, 99));
        Assert.False(rows.Place(1, 1, 1));
        Assert.Equal(before, JsonSerializer.Serialize(rows));
    }

    [Fact]
    public void JobLayoutsDoNotShareRowsOrToggleState()
    {
        var jobs = new Dictionary<uint, PartyCooldownRows>
        {
            [19] = new() { Enabled = true, Rows = [[7531], [17]] },
            [21] = new() { Rows = [[7531, 40]] },
        };
        var restored = JsonSerializer.Deserialize<Dictionary<uint, PartyCooldownRows>>(JsonSerializer.Serialize(jobs))!;
        Assert.True(restored[19].Place(7531, 1));
        Assert.True(restored[19].Enabled);
        Assert.False(restored[21].Enabled);
        Assert.Equal([7531u, 40], restored[21].Rows.Single());
    }
}
