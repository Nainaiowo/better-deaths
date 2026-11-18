using BetterDeaths.PartyCooldowns;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private readonly record struct CooldownPreviewRect(Vector2 Min, Vector2 Size)
    {
        public Vector2 Max => Min + Size;
    }

    private readonly record struct CooldownPreviewFrame(PartyCooldownRow Name, CooldownPreviewRect Job,
        CooldownPreviewRect Slot, CooldownPreviewRect Hp, CooldownPreviewRect Mp, float FontSize)
    {
        public Vector2 Min => Vector2.Min(Vector2.Min(Name.NameMin, Job.Min), Vector2.Min(Slot.Min, Vector2.Min(Hp.Min, Mp.Min)));
        public Vector2 Max => Vector2.Max(Vector2.Max(Name.NameMax, Job.Max), Vector2.Max(Slot.Max, Vector2.Max(Hp.Max, Mp.Max)));
    }

    private static CooldownPreviewFrame DefaultPartyCooldownPreviewFrame()
    {
        var name = new Vector2(44, 0);
        return new(new(0, 0, name, name + new Vector2(ImGui.CalcTextSize("Player 1").X, 20), 1),
            new(new(12, 5), new(28)), new(new(0, 6), new(10, 18)),
            new(new(44, 22), new(180, 10)), new(new(44, 35), new(180, 5)), 16);
    }

    private unsafe PartyCooldownAction? DrawPartyCooldownPreviewContents(PartyCooldownCatalog? catalog, IReadOnlyList<uint> actions,
        PartyCooldownRows? rows, PartyCooldownPlacement placement, CooldownPreviewFrame frame)
    {
        Span<int> rowCounts = stackalloc int[PartyCooldownLayout.MaximumRows];
        Span<int> columns = stackalloc int[PartyCooldownLayout.MaximumRows];
        rowCounts.Clear();
        columns.Clear();
        foreach (var id in actions)
            rowCounts[rows?.RowFor(id) ?? 0]++;
        var maximum = Vector2.Max(Vector2.One, ImGui.GetContentRegionAvail() - new Vector2(0, ImGui.GetTextLineHeightWithSpacing()));
        var layout = PartyCooldownPreviewLayout.Create(frame.Name, rowCounts, configuration.PartyCooldownSide,
            placement.IconSize, placement.Spacing, new(placement.OffsetX, placement.OffsetY),
            frame.Min, frame.Max, maximum);
        ImGui.TextDisabled($"Preview: {layout.Zoom * 100:0.##}%");
        var start = ImGui.GetCursorScreenPos() + Vector2.Max(Vector2.Zero, (maximum - layout.Size) * .5f) + layout.Shift;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(start + frame.Name.NameMin * layout.Zoom, start + frame.Name.NameMax * layout.Zoom, true);
        draw.AddText(ImGui.GetFont(), frame.FontSize * layout.Zoom, start + frame.Name.NameMin * layout.Zoom,
            0xFFFFFFFF, "Player 1");
        draw.PopClipRect();
        DrawPartyCooldownIcon(draw, start + frame.Job.Min * layout.Zoom, frame.Job.Size.X * layout.Zoom,
            GetClassJobIconId(cooldownPreviewJob), Vector4.One);
        DrawPartyCooldownText(draw, start + (frame.Slot.Min + frame.Slot.Size * .5f) * layout.Zoom,
            "1", frame.FontSize * layout.Zoom, layout.Zoom);
        DrawPartyCooldownPreviewGauge(draw, start, frame.Hp, layout.Zoom, .86f, new(.55f, .84f, .43f, 1));
        DrawPartyCooldownPreviewGauge(draw, start, frame.Mp, layout.Zoom, .72f, new(.68f, .61f, .88f, 1));
        PartyCooldownAction? hovered = null;
        for (var index = 0; index < actions.Count; index++)
            if (catalog?.Find(actions[index]) is { } action)
            {
                if (!partyCooldownPreviewCharges.TryGetValue(action.Id, out var charges))
                {
                    charges = Math.Clamp((int)ActionManager.GetMaxCharges(action.Id, 100), 1, 5);
                    partyCooldownPreviewCharges[action.Id] = charges;
                }
                var rowIndex = rows?.RowFor(actions[index]) ?? 0;
                var offset = PartyCooldownLayout.GetIconOffset(rowIndex, columns[rowIndex]++, rowCounts,
                    configuration.PartyCooldownSide, placement.IconSize, placement.Spacing) * frame.Name.Scale;
                var position = start + (layout.IconOrigin + offset) * layout.Zoom;
                var size = PartyCooldownLayout.IconSize(placement.IconSize) * frame.Name.Scale * layout.Zoom;
                DrawPartyCooldownStatus(draw, position, size, action,
                    PartyCooldownStatus.Preview(charges, PartyCooldownCatalog.RecastAtLevel(action.Id, 100, action.RecastSeconds), index),
                    textScale: layout.Zoom);
                if (ImGui.IsMouseHoveringRect(position, position + new Vector2(size)))
                    hovered = action;
            }
        ImGui.Dummy(maximum);
        return hovered;
    }

    private static void DrawPartyCooldownPreviewGauge(ImDrawListPtr draw, Vector2 origin, CooldownPreviewRect rect,
        float zoom, float fraction, Vector4 color)
    {
        if (rect.Size.X <= 0 || rect.Size.Y <= 0)
            return;
        var min = origin + rect.Min * zoom;
        var max = min + rect.Size * zoom;
        draw.AddRectFilled(min, max, 0xE6222222, zoom);
        var fill = new Vector2(min.X + rect.Size.X * fraction * zoom, max.Y);
        draw.AddRectFilledMultiColor(min, fill, ImGui.GetColorU32(color), ImGui.GetColorU32(color),
            ImGui.GetColorU32(color * new Vector4(.65f, .65f, .65f, 1)), ImGui.GetColorU32(color * new Vector4(.65f, .65f, .65f, 1)));
        draw.AddRect(min, max, 0xFF929292, zoom, ImDrawFlags.None, zoom);
    }

    private unsafe bool TryReadPartyCooldownPreviewFrame(out CooldownPreviewFrame frame)
    {
        frame = default;
        var addon = Plugin.GameGui.GetAddonByName("_PartyList");
        var hud = AgentHUD.Instance();
        var local = Plugin.ObjectTable.LocalPlayer;
        if (addon.IsNull || !addon.IsReady || !addon.IsVisible || hud is null || local is null)
            return false;
        var native = (AddonPartyList*)addon.Address;
        Span<PartyCooldownHudMember> members = stackalloc PartyCooldownHudMember[hud->PartyMembers.Length];
        for (var index = 0; index < members.Length; index++)
            members[index] = new(hud->PartyMembers[index].EntityId, hud->PartyMembers[index].Index);
        for (var index = 0; index < native->PartyMembers.Length; index++)
        {
            var match = PartyCooldownLayout.FindUniqueHudMember(members, index);
            if (match < 0 || members[match].EntityId != local.EntityId)
                continue;
            ref var row = ref native->PartyMembers[index];
            if (row.PartyMemberComponent is null || row.PartyMemberComponent->OwnerNode is null || row.Name is null ||
                row.ClassJobIcon is null || row.HPGaugeBar is null)
                return false;
            var scale = row.Name->GetScaleX();
            if (!float.IsFinite(scale) || scale <= 0 || scale > 4)
                return false;
            var origin = new Vector2(row.Name->ScreenX, row.Name->ScreenY);
            if (!TryReadPartyCooldownPreviewRect((AtkResNode*)row.ClassJobIcon, origin, out var icon) ||
                !TryReadPartyCooldownPreviewRect((AtkResNode*)row.HPGaugeBar->BackdropImageNode, origin, out var hp))
                return false;
            var slot = new CooldownPreviewRect(icon.Min - new Vector2(12 * scale, 0), new(10 * scale, 18 * scale));
            if (TryReadPartyCooldownPreviewRect((AtkResNode*)row.GroupSlotIndicator, origin, out var nativeSlot))
                slot = nativeSlot;
            var mp = new CooldownPreviewRect(hp.Min, Vector2.Zero);
            if (row.MPGaugeBar is not null &&
                TryReadPartyCooldownPreviewRect((AtkResNode*)row.MPGaugeBar->BackdropImageNode, origin, out var nativeMp))
                mp = nativeMp;
            ushort width = 0;
            ushort height = 0;
            row.Name->GetTextDrawSize(&width, &height);
            var name = new PartyCooldownRow(0, 0, Vector2.Zero,
                new(MathF.Max(1, width) * scale, Math.Max(1, (int)row.Name->Height) * scale), scale);
            frame = new(name, icon, slot, hp, mp, Math.Clamp(row.Name->FontSize * scale, 8, 72));
            return true;
        }
        return false;
    }

    private static unsafe bool TryReadPartyCooldownPreviewRect(AtkResNode* node, Vector2 origin, out CooldownPreviewRect rect)
    {
        rect = default;
        if (node is null)
            return false;
        var min = new Vector2(node->ScreenX, node->ScreenY) - origin;
        var size = new Vector2(node->Width * node->GetScaleX(), node->Height * node->GetScaleY());
        if (!float.IsFinite(min.X) || !float.IsFinite(min.Y) || !float.IsFinite(size.X) || !float.IsFinite(size.Y) ||
            size.X <= 0 || size.Y <= 0 || size.X > 2000 || size.Y > 2000 || MathF.Abs(min.X) > 2000 || MathF.Abs(min.Y) > 2000)
            return false;
        rect = new(min, size);
        return true;
    }
}
