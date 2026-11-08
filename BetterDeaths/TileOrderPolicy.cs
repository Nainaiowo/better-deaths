using System.Collections.Generic;

namespace BetterDeaths;

internal static class TileOrderPolicy
{
    public static bool Place<T>(IList<T> items, T source, T? target, bool after = false) where T : struct
    {
        if (target is { } self && EqualityComparer<T>.Default.Equals(source, self))
            return false;
        var insertion = target is { } anchor ? items.IndexOf(anchor) : items.Count;
        if (insertion < 0)
            return false;
        if (after && target.HasValue)
            insertion++;
        var sourceIndex = items.IndexOf(source);
        if (sourceIndex >= 0 && sourceIndex < insertion)
            insertion--;
        if (sourceIndex == insertion)
            return false;
        if (sourceIndex >= 0)
            items.RemoveAt(sourceIndex);
        items.Insert(insertion, source);
        return true;
    }
}
