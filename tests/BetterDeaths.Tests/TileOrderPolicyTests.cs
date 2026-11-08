namespace BetterDeaths.Tests;

public sealed class TileOrderPolicyTests
{
    [Fact]
    public void EveryExistingSourceAndTargetSupportsBothInsertionSides()
    {
        int[] original = [1, 2, 3, 4, 5];
        foreach (var source in original)
            foreach (var target in original)
                foreach (var after in new[] { false, true })
                {
                    var items = original.ToList();
                    var expected = original.ToList();
                    if (source != target)
                    {
                        expected.Remove(source);
                        expected.Insert(expected.IndexOf(target) + (after ? 1 : 0), source);
                    }
                    var changed = TileOrderPolicy.Place(items, source, target, after);
                    Assert.Equal(expected, items);
                    Assert.Equal(!original.SequenceEqual(expected), changed);
                }
    }

    [Fact]
    public void AppendHandlesExistingAvailableAndEmptyListsWithoutDuplicates()
    {
        List<int> items = [1, 2, 3];
        Assert.True(TileOrderPolicy.Place(items, 1, null));
        Assert.Equal(new[] { 2, 3, 1 }, items);
        Assert.True(TileOrderPolicy.Place(items, 4, null));
        Assert.Equal(new[] { 2, 3, 1, 4 }, items);
        Assert.False(TileOrderPolicy.Place(items, 4, null));
        List<int> empty = [];
        Assert.True(TileOrderPolicy.Place(empty, 1, null));
        Assert.Equal(new[] { 1 }, empty);
    }

    [Fact]
    public void UnrecognizedTargetsDoNotRemoveExistingOrInsertAvailableItems()
    {
        List<int> items = [1, 2, 3];
        Assert.False(TileOrderPolicy.Place(items, 2, 99));
        Assert.False(TileOrderPolicy.Place(items, 4, 99, true));
        Assert.Equal(new[] { 1, 2, 3 }, items);
    }
}
