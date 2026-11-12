namespace BetterDeaths.PartyCooldowns;

public sealed record PartyCooldownPlacement
{
    public float IconSize { get; set; } = 24;
    public float Spacing { get; set; } = 3;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }

    internal bool Normalize()
    {
        var size = PartyCooldownLayout.IconSize(IconSize);
        var spacing = PartyCooldownLayout.Gap(Spacing);
        var x = PartyCooldownLayout.Offset(OffsetX);
        var y = PartyCooldownLayout.Offset(OffsetY);
        var changed = size != IconSize || spacing != Spacing || x != OffsetX || y != OffsetY;
        (IconSize, Spacing, OffsetX, OffsetY) = (size, spacing, x, y);
        return changed;
    }
}

public sealed class PartyCooldownPlacements
{
    internal const PartyCooldownSide DefaultSide = PartyCooldownSide.Left;

    internal static PartyCooldownPlacements CreateDefault() => new()
    {
        Left = new() { IconSize = 48, Spacing = 3, OffsetX = -77, OffsetY = 9 },
        Right = new(),
    };

    public PartyCooldownPlacement? Left { get; set; }
    public PartyCooldownPlacement? Right { get; set; }

    internal bool Normalize(PartyCooldownPlacement legacy)
    {
        var changed = false;
        if (Left is null)
        {
            Left = legacy with { };
            changed = true;
        }
        if (Right is null || ReferenceEquals(Left, Right))
        {
            Right = (Right ?? legacy) with { };
            changed = true;
        }
        changed |= Left.Normalize();
        changed |= Right.Normalize();
        return changed;
    }

    internal PartyCooldownPlacement Get(PartyCooldownSide side) => side == PartyCooldownSide.Left ? Left! : Right!;

    internal void ApplyDefault(PartyCooldownSide side)
    {
        var defaults = CreateDefault().Get(side);
        var placement = Get(side);
        (placement.IconSize, placement.Spacing, placement.OffsetX, placement.OffsetY) =
            (defaults.IconSize, defaults.Spacing, defaults.OffsetX, defaults.OffsetY);
    }
}
