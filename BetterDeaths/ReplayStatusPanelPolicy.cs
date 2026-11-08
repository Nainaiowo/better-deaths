using System;
using System.Numerics;

namespace BetterDeaths;

internal static class ReplayStatusPanelPolicy
{
    private const int AlliancePartyIndexBase = 1000;

    public static bool IsLocalPartyIndex(int partyIndex)
    {
        return partyIndex >= 0 && partyIndex < AlliancePartyIndexBase;
    }

    public static float GetStackedHeight(
        bool hasPartyHp,
        float partyHpHeight,
        bool hasMitigation,
        float mitigationHeight,
        float panelGap)
    {
        return (hasPartyHp ? partyHpHeight : 0.0f) +
            (hasMitigation ? mitigationHeight : 0.0f) +
            (hasPartyHp && hasMitigation ? panelGap : 0.0f);
    }

    public static bool PlaceBelowCanvas(float availableWidth, float canvasSide, float minimumPanelWidth, float gap) =>
        availableWidth - canvasSide - gap < minimumPanelWidth;

    public static Vector2 GetGroupStart(Vector2 cursorStart, float canvasX, float canvasSide,
        float availableWidth, float panelWidth, float gap, bool belowCanvas) => belowCanvas
            ? cursorStart + new Vector2(MathF.Max(0, (availableWidth - panelWidth) * .5f), canvasSide + gap)
            : new Vector2(canvasX + canvasSide + gap, cursorStart.Y);

    public static bool IsBelowScrollViewport(float rowTop, float rowHeight, float viewportBottom) =>
        rowTop + rowHeight > viewportBottom + .5f;

    public static bool IsInScrollViewport(float rowTop, float rowHeight, float viewportTop, float viewportBottom) =>
        rowTop < viewportBottom && rowTop + rowHeight > viewportTop;

    public static float GetScrollBodyHeight(float viewportHeight, float contentHeight, float footerHeight) =>
        MathF.Max(1, viewportHeight - (contentHeight > viewportHeight && viewportHeight >= footerHeight + 24 ? footerHeight : 0));
}
