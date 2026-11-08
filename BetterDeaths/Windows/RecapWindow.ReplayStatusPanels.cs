using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private readonly record struct ReplayStatusScrollState(
        bool Opened, bool Visible, Vector2 ParentCursor, Vector2 Origin, float Width,
        float ViewportTop, float ViewportBottom, float ContentHeight, Vector2 FooterStart, bool HasFooter);

    private ReplayStatusScrollState BeginReplayStatusScroll(string id, Vector2 start, Vector2 end, float contentHeight)
    {
        var size = end - start;
        if (size.X <= 0 || size.Y <= 0)
            return default;

        var parentCursor = ImGui.GetCursorScreenPos();
        var footerHeight = ImGui.GetTextLineHeight() + 4;
        var bodyHeight = ReplayStatusPanelPolicy.GetScrollBodyHeight(size.Y, contentHeight, footerHeight);
        var hasFooter = bodyHeight < size.Y;
        ImGui.SetCursorScreenPos(start);
        ImGui.SetNextWindowContentSize(new Vector2(0, MathF.Max(1, contentHeight)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var visible = ImGui.BeginChild(id, new Vector2(size.X, bodyHeight), false,
            ImGuiWindowFlags.NoBackground | OptionalScrollbarFlags);
        ImGui.PopStyleVar();
        var origin = ImGui.GetCursorScreenPos();
        var viewportTop = ImGui.GetWindowPos().Y;
        return new(true, visible, parentCursor, origin, ImGui.GetContentRegionAvail().X,
            viewportTop, viewportTop + ImGui.GetWindowSize().Y, contentHeight,
            new Vector2(start.X, end.Y - footerHeight + 2), hasFooter);
    }

    private static void EndReplayStatusScroll(ReplayStatusScrollState state, int hiddenBelow, float alpha)
    {
        if (!state.Opened)
            return;

        ImGui.SetCursorScreenPos(state.Origin);
        ImGui.Dummy(new Vector2(1, state.ContentHeight));
        ImGui.EndChild();
        if (state.HasFooter && hiddenBelow > 0)
            ImGui.GetWindowDrawList().AddText(state.FooterStart,
                ImGui.GetColorU32(ScaleAlpha(ModernMutedTextColor, alpha)), $"+{hiddenBelow} more");
        ImGui.SetCursorScreenPos(state.ParentCursor);
    }

    private void DrawReplayPartyHpScrollableRows(IReadOnlyList<ReplayPositionSnapshot> entries,
        Vector2 panelStart, Vector2 panelSize, string idSuffix, float alpha)
    {
        var start = panelStart + new Vector2(ReplayMitigationOverlayPadding,
            ReplayMitigationOverlayHeaderHeight + ReplayMitigationOverlayPadding);
        var end = panelStart + panelSize - new Vector2(ReplayMitigationOverlayPadding,
            ReplayMitigationOverlayResizeCornerSize + ReplayMitigationOverlayPadding);
        var contentHeight = entries.Count * ReplayPartyHpRowHeight + Math.Max(0, entries.Count - 1) * ReplayMitigationOverlayRowGap;
        var scroll = BeginReplayStatusScroll($"##ReplayPartyHpScroll{idSuffix}", start, end, contentHeight);
        var hiddenBelow = 0;
        if (scroll.Visible)
        {
            var drawList = ImGui.GetWindowDrawList();
            var y = scroll.Origin.Y;
            foreach (var entry in entries)
            {
                if (ReplayStatusPanelPolicy.IsBelowScrollViewport(y, ReplayPartyHpRowHeight, scroll.ViewportBottom))
                    hiddenBelow++;
                if (ReplayStatusPanelPolicy.IsInScrollViewport(y, ReplayPartyHpRowHeight, scroll.ViewportTop, scroll.ViewportBottom))
                    DrawReplayPartyHpRow(drawList, entry, scroll.Origin.X, y, scroll.Width, alpha);
                y += ReplayPartyHpRowHeight + ReplayMitigationOverlayRowGap;
            }
        }
        EndReplayStatusScroll(scroll, hiddenBelow, alpha);
    }
}
