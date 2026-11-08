using System;
using System.Numerics;

namespace BetterDeaths.PartyCooldowns;

internal readonly record struct PartyCooldownPreviewLayout(Vector2 IconOrigin, Vector2 Shift, Vector2 Size, float Zoom)
{
    public static PartyCooldownPreviewLayout Create(PartyCooldownRow row, ReadOnlySpan<int> rowCounts, PartyCooldownSide side,
        float iconSize, float gap, Vector2 offset, Vector2 frameMin, Vector2 frameMax, Vector2 maximumSize)
    {
        var padding = MathF.Min(12, MathF.Min(maximumSize.X, maximumSize.Y) * .125f);
        var scale = float.IsFinite(row.Scale) && row.Scale > 0 ? row.Scale : 1;
        var origin = PartyCooldownLayout.GetOrigin(row, rowCounts, side, iconSize, gap, offset);
        var gridSize = PartyCooldownLayout.GetSize(rowCounts, iconSize, gap) * scale;
        var min = gridSize == Vector2.Zero ? frameMin : Vector2.Min(frameMin, origin);
        var max = gridSize == Vector2.Zero ? frameMax : Vector2.Max(frameMax, origin + gridSize);
        var content = Vector2.Max(max - min, Vector2.One);
        var available = Vector2.Max(maximumSize - new Vector2(padding * 2), new Vector2(float.Epsilon));
        var zoom = MathF.Min(1, MathF.Min(available.X / content.X, available.Y / content.Y));
        return new(origin, new Vector2(padding) - min * zoom, content * zoom + new Vector2(padding * 2), zoom);
    }
}
