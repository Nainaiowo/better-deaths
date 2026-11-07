using BetterDeaths.PartyCooldowns;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private uint cooldownPreviewJob = 19;
    private bool cooldownPreviewInitialized;
    private uint? cooldownRequestedJob;
    private const string PartyCooldownDragPayload = "BETTER_DEATHS_PARTY_COOLDOWN";
    private readonly Dictionary<uint, ISharedImmediateTexture> partyCooldownTextures = [];
    private readonly Dictionary<uint, int> partyCooldownPreviewCharges = [];

    private void DrawPartyCooldownSettings()
    {
        DrawModernSectionTitle("Party Cooldowns", string.Empty);
        DrawSubtleSeparator();
        ImGui.Spacing();
        var visible = configuration.ShowPartyCooldowns;
        if (DrawThemedCheckbox("Show party cooldowns", ref visible))
            plugin.SetPartyCooldownVisibility(visible);
        ImGui.Spacing();
        if (!cooldownPreviewInitialized)
        {
            cooldownPreviewInitialized = true;
            var ownJob = PartyCooldownCatalog.NormalizeJob(Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0);
            if (PartyCooldownCatalog.Jobs.Any(job => job.Id == ownJob))
                cooldownRequestedJob = cooldownPreviewJob = ownJob;
        }
        var available = ImGui.GetContentRegionAvail();
        var gap = ImGui.GetStyle().ItemSpacing.X * 2;
        var layout = HudLayoutPolicy.PartyCooldownColumns(available.X, ImGui.GetTextLineHeight(), gap);
        if (layout.Split)
        {
            if (ImGui.BeginChild("##PartyCooldownJobEditor", new Vector2(layout.EditorWidth, available.Y), false, OptionalScrollbarFlags))
                DrawPartyCooldownAbilities();
            ImGui.EndChild();
            ImGui.SameLine(0, gap);
            if (ImGui.BeginChild("##PartyCooldownPreviewSettings", new Vector2(0, available.Y), false, OptionalScrollbarFlags))
            {
                DrawPartyCooldownPreview();
                ImGui.Spacing();
                DrawSubtleSeparator();
                ImGui.Spacing();
                DrawPartyCooldownPlacement();
            }
            ImGui.EndChild();
        }
        else
        {
            DrawPartyCooldownPreview();
            ImGui.Spacing();
            if (ImGui.CollapsingHeader("Layout (all jobs)##PartyCooldownPlacementCompact"))
                DrawPartyCooldownPlacement(showTitle: false);
            ImGui.Spacing();
            DrawPartyCooldownAbilities();
        }
        if (cooldownAppearanceSavePending)
        {
            cooldownAppearanceSavePending = false;
            plugin.SaveConfiguration();
        }
    }

    private void DrawPartyCooldownPlacement(bool showTitle = true)
    {
        if (showTitle)
        {
            ImGui.TextColored(ModernAccentColor, "Layout (all jobs)");
            DrawSettingsTooltip("Position, size, and spacing apply to every party-list row. Ability selections are separate for each job.");
        }
        ImGui.TextUnformatted("Position");
        var sideWidth = GetResponsiveSegmentWidth(2, GetThemedActionButtonWidth("Right of name"));
        foreach (var side in Enum.GetValues<PartyCooldownSide>())
        {
            if (side == PartyCooldownSide.Right)
                ImGui.SameLine();
            var label = side == PartyCooldownSide.Left ? "Left of name" : "Right of name";
            if (GetThemedActionButtonWidth(label) > sideWidth)
                label = side.ToString();
            if (DrawSegmentedButton(label,
                $"PartyCooldownSide{side}", configuration.PartyCooldownSide == side, sideWidth, MathF.Max(24, ImGui.GetFrameHeight())))
            {
                configuration.PartyCooldownSide = side;
                plugin.SaveConfiguration();
            }
            DrawSettingsTooltip(side == PartyCooldownSide.Left ? "Left of each player's name." : "Right of each player's name.");
        }
        var size = configuration.PartyCooldownIconSize;
        DrawCooldownSlider("Icon size", "PartyCooldownSize", ref size, 16, 36, "%.0f px");
        configuration.PartyCooldownIconSize = size;
        var gap = configuration.PartyCooldownIconGap;
        DrawCooldownSlider("Spacing", "PartyCooldownGap", ref gap, 0, 12, "%.0f px");
        configuration.PartyCooldownIconGap = gap;
        var offsetX = configuration.PartyCooldownOffsetX;
        DrawCooldownSlider("Horizontal offset", "PartyCooldownOffsetX", ref offsetX, -300, 300, "%.0f px");
        configuration.PartyCooldownOffsetX = offsetX;
        var offsetY = configuration.PartyCooldownOffsetY;
        DrawCooldownSlider("Vertical offset", "PartyCooldownOffsetY", ref offsetY, -300, 300, "%.0f px");
        configuration.PartyCooldownOffsetY = offsetY;
        if (DrawTransparentIconButton("ResetPartyCooldownPlacement", FontAwesomeIcon.Undo))
        {
            configuration.PartyCooldownSide = PartyCooldownSide.Right;
            configuration.PartyCooldownOffsetX = 0;
            configuration.PartyCooldownOffsetY = 0;
            plugin.SaveConfiguration();
        }
        if (ImGui.IsItemHovered())
            SetThemedTooltip("Reset placement. Keeps icon size, spacing, and per-job selections.");
    }

    private void DrawPartyCooldownAbilities()
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.Spacing();
        ImGui.TextColored(ModernAccentColor, "Abilities by job");
        FocusGuideTarget(GuideTarget.PartyCooldowns);
        if (plugin.GetPartyCooldownCatalog() is not { } catalog)
        {
            ImGui.TextDisabled(plugin.PartyCooldownCatalogFailed ? "Ability data could not be loaded." : "Loading abilities...");
            return;
        }
        var requested = cooldownRequestedJob;
        foreach (var role in Enum.GetValues<PartyCooldownRole>())
        {
            var label = role switch
            {
                PartyCooldownRole.Tank => "Tanks",
                PartyCooldownRole.Healer => "Healers",
                PartyCooldownRole.Melee => "Melee",
                _ => "Ranged",
            };
            if (requested is { } requestedId && PartyCooldownCatalog.Jobs.Any(job => job.Id == requestedId && job.Role == role))
                ImGui.SetNextItemOpen(true);
            if (!ImGui.CollapsingHeader($"{label}##CooldownRole{role}"))
                continue;
            foreach (var job in PartyCooldownCatalog.Jobs.Where(job => job.Role == role))
            {
                var selected = plugin.GetPartyCooldownSelection(job.Id);
                if (requested == job.Id)
                    ImGui.SetNextItemOpen(true);
                var flags = ImGuiTreeNodeFlags.SpanAvailWidth |
                    (job.Id == cooldownPreviewJob ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None);
                if (!ImGui.TreeNodeEx($"{job.Name}  ({selected.Count}/12)###CooldownJob{job.Id}", flags))
                    continue;
                if (requested == job.Id)
                    ImGui.SetScrollHereY(.2f);
                if (ImGui.IsItemToggledOpen())
                    cooldownPreviewJob = job.Id;
                DrawPartyCooldownJobEditor(catalog, job, selected);
                ImGui.TreePop();
            }
        }
        cooldownRequestedJob = null;
        RegisterGuideTarget(GuideTarget.PartyCooldowns, start,
            new Vector2(start.X + ImGui.GetContentRegionAvail().X, ImGui.GetCursorScreenPos().Y));
        DrawReviewPaneBottomPadding();
    }

    private void DrawCooldownSlider(string label, string id, ref float value, float minimum, float maximum, string format)
    {
        ImGui.TextUnformatted(label);
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderFloat($"##{id}", ref value, minimum, maximum, format);
        // Save after the caller writes the changed value back to Configuration.
        if (ImGui.IsItemDeactivatedAfterEdit())
            cooldownAppearanceSavePending = true;
    }

    private bool cooldownAppearanceSavePending;

    private void DrawPartyCooldownJobEditor(PartyCooldownCatalog catalog, PartyCooldownJob job, IReadOnlyList<uint> current)
    {
        var selected = catalog.NormalizeSelection(job.Id, current);
        ImGui.TextColored(ModernMutedTextColor, "Selected cooldowns");
        DrawPartyCooldownTiles(catalog, job.Id, selected, selected, true);
        if (selected.Count == 0)
            ImGui.TextDisabled("None");
        DrawPartyCooldownDropZone(job.Id, selected);
        ImGui.Spacing();
        foreach (var mitigation in new[] { true, false })
        {
            var available = catalog.ForJob(job.Id).Where(action => !selected.Contains(action.Id) &&
                    PartyCooldownSelectionPolicy.IsMitigation(action.Id) == mitigation)
                .Select(action => action.Id).ToArray();
            if (available.Length == 0)
                continue;
            ImGui.TextColored(ModernMutedTextColor, mitigation ? "Available mitigations" : "Available major cooldowns");
            DrawPartyCooldownTiles(catalog, job.Id, available, selected, false);
            ImGui.Spacing();
        }
        ImGui.Spacing();
        if (DrawThemedActionButton($"{FontAwesomeIcon.Undo.ToIconString()}  Reset {job.Abbreviation}", $"ResetPartyCooldownJob{job.Id}"))
            plugin.SetPartyCooldownSelection(job.Id, null);
        ImGui.Spacing();
    }

    private void DrawPartyCooldownTiles(PartyCooldownCatalog catalog, uint jobId, IReadOnlyList<uint> ids, List<uint> selected, bool active)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var rowWidth = 0f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        foreach (var id in ids.ToArray())
        {
            if (catalog.Find(id) is not { } action)
                continue;
            var tileWidth = MathF.Min(width, MathF.Min(280, ImGui.CalcTextSize(action.Name).X + 92));
            if (rowWidth > 0 && rowWidth + spacing + tileWidth <= width)
            {
                ImGui.SameLine();
                rowWidth += spacing;
            }
            else
                rowWidth = 0;
            if (DrawPartyCooldownTile(jobId, action, selected, active, tileWidth))
            {
                if (active)
                    selected.Remove(id);
                else if (selected.Count < 12)
                    selected.Add(id);
                plugin.SetPartyCooldownSelection(jobId, selected);
            }
            rowWidth += tileWidth;
        }
    }

    private unsafe bool DrawPartyCooldownTile(uint jobId, PartyCooldownAction action, List<uint> selected, bool active, float width)
    {
        var enabled = action.UnsupportedReason is null && (active || selected.Count < 12);
        var start = ImGui.GetCursorScreenPos();
        var height = MathF.Max(30, ImGui.GetFrameHeight() + 6);
        var size = new Vector2(MathF.Max(1, width), height);
        ImGui.BeginDisabled(!enabled);
        var clicked = ImGui.InvisibleButton($"##PartyCooldownTile{jobId}_{action.Id}_{active}", size);
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        var draw = ImGui.GetWindowDrawList();
        var color = enabled ? ModernTextColor : ModernMutedTextColor with { W = .5f };
        draw.AddRectFilled(start, start + size, ImGui.GetColorU32(hovered ? ModernNavButtonHoveredColor : ModernPanelAltColor), 4);
        draw.AddRect(start, start + size, ImGui.GetColorU32(hovered ? ModernAccentColor : ModernPanelBorderColor), 4);
        DrawPartyCooldownIcon(draw, start + new Vector2(22, (height - 20) * .5f), 20, action.IconId, new(1, 1, 1, enabled ? 1 : .35f));
        var label = ClipTextToWidth(action.Name, MathF.Max(0, width - 83));
        draw.AddText(start + new Vector2(48, (height - ImGui.GetTextLineHeight()) * .5f), ImGui.GetColorU32(color), label);
        var removeMin = start.X + width - 30;
        if (hovered && enabled)
            ImGui.SetMouseCursor(ImGui.GetIO().MousePos.X >= removeMin ? ImGuiMouseCursor.Hand : ImGuiMouseCursor.ResizeAll);
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), start + new Vector2(5, (height - ImGui.GetTextLineHeight()) * .5f),
                ImGui.GetColorU32(color with { W = color.W * .6f }), FontAwesomeIcon.GripLines.ToIconString());
            var glyph = (active ? FontAwesomeIcon.Times : FontAwesomeIcon.Plus).ToIconString();
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), start + new Vector2(width - 23, (height - ImGui.GetTextLineHeight()) * .5f),
                ImGui.GetColorU32(color), glyph);
        }
        if (hovered)
            SetThemedTooltip(action.UnsupportedReason ?? (!active && selected.Count >= 12
                ? "Up to 12 abilities can be shown for each job."
                : $"{action.Name}\n{action.RecastSeconds:0.#}s base recast | Level {action.Level}"));
        if (enabled && ImGui.BeginDragDropSource())
        {
            var payload = new uint[] { jobId, action.Id };
            fixed (uint* data = payload)
                ImGui.SetDragDropPayload(PartyCooldownDragPayload, new ReadOnlySpan<byte>(data, 8));
            ImGui.TextUnformatted(action.Name);
            ImGui.EndDragDropSource();
        }
        if (active && ImGui.BeginDragDropTarget())
        {
            AcceptPartyCooldownDrop(jobId, selected, action.Id);
            ImGui.EndDragDropTarget();
        }
        ImGui.EndDisabled();
        return enabled && clicked && (!active || ImGui.GetIO().MousePos.X >= removeMin);
    }

    private void DrawPartyCooldownDropZone(uint jobId, List<uint> selected)
    {
        var start = ImGui.GetCursorScreenPos();
        var size = new Vector2(MathF.Max(1, ImGui.GetContentRegionAvail().X), selected.Count == 0 ? 30 : 12);
        ImGui.InvisibleButton($"##PartyCooldownAppend{jobId}", size);
        if (ImGui.BeginDragDropTarget())
        {
            ImGui.GetWindowDrawList().AddRectFilled(start, start + size, ImGui.GetColorU32(ModernAccentColor with { W = .35f }), 2);
            AcceptPartyCooldownDrop(jobId, selected, null);
            ImGui.EndDragDropTarget();
        }
    }

    private unsafe void AcceptPartyCooldownDrop(uint jobId, List<uint> selected, uint? before)
    {
        var payload = ImGui.AcceptDragDropPayload(PartyCooldownDragPayload);
        if (payload.IsNull || payload.Data is null || payload.DataSize != 8)
            return;
        var data = (uint*)payload.Data;
        if (data[0] == jobId && plugin.GetPartyCooldownCatalog()?.Find(data[1]) is { UnsupportedReason: null } action &&
            action.Jobs.Contains(jobId) && PartyCooldownCatalog.PlaceBefore(selected, data[1], before))
            plugin.SetPartyCooldownSelection(jobId, selected);
    }

    private unsafe void DrawPartyCooldownPreview()
    {
        ImGui.TextColored(ModernAccentColor, "Preview");
        var job = PartyCooldownCatalog.Jobs.First(candidate => candidate.Id == cooldownPreviewJob);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##PartyCooldownPreviewJob", job.Name))
        {
            foreach (var candidate in PartyCooldownCatalog.Jobs)
                if (ImGui.Selectable(candidate.Name, candidate.Id == cooldownPreviewJob))
                {
                    cooldownPreviewJob = candidate.Id;
                    cooldownRequestedJob = candidate.Id;
                }
            ImGui.EndCombo();
        }
        job = PartyCooldownCatalog.Jobs.First(candidate => candidate.Id == cooldownPreviewJob);
        var actions = plugin.GetPartyCooldownSelection(job.Id);
        var catalog = plugin.GetPartyCooldownCatalog();
        var iconSize = PartyCooldownLayout.IconSize(configuration.PartyCooldownIconSize);
        var gap = PartyCooldownLayout.Gap(configuration.PartyCooldownIconGap);
        var stripWidth = actions.Count * iconSize + Math.Max(0, actions.Count - 1) * gap;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ModernPanelAltColor);
        if (ImGui.BeginChild("##PartyCooldownPreviewCanvas", new Vector2(0, 112), false, ImGuiWindowFlags.HorizontalScrollbar))
        {
            var position = ImGui.GetCursorScreenPos();
            var draw = ImGui.GetWindowDrawList();
            const string label = "Player 1";
            var nameWidth = ImGui.CalcTextSize(label).X;
            var name = new Vector2(configuration.PartyCooldownSide == PartyCooldownSide.Left ? stripWidth + 20 : 12, 20);
            var row = new PartyCooldownRow(0, 0, name, name + new Vector2(nameWidth, ImGui.GetTextLineHeight()), 1);
            var origin = PartyCooldownLayout.GetOrigin(row, actions.Count, configuration.PartyCooldownSide, iconSize, gap,
                new(configuration.PartyCooldownOffsetX, configuration.PartyCooldownOffsetY));
            var shift = Vector2.Max(new Vector2(12) - Vector2.Min(name, origin), Vector2.Zero);
            var extent = Vector2.Max(name + new Vector2(nameWidth, ImGui.GetTextLineHeight()),
                origin + new Vector2(stripWidth, iconSize)) + shift + new Vector2(12);
            draw.AddText(position + name + shift, ImGui.GetColorU32(ModernTextColor), label);
            for (var index = 0; index < actions.Count; index++)
                if (catalog?.Find(actions[index]) is { } action)
                {
                    if (!partyCooldownPreviewCharges.TryGetValue(action.Id, out var maximum))
                    {
                        maximum = Math.Clamp((int)ActionManager.GetMaxCharges(action.Id, 100), 1, 5);
                        partyCooldownPreviewCharges[action.Id] = maximum;
                    }
                    DrawPartyCooldownStatus(draw, position + origin + shift + new Vector2(index * (iconSize + gap), 0), iconSize,
                        action, PartyCooldownStatus.Preview(maximum, action.RecastSeconds, index));
                }
            ImGui.Dummy(extent);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private bool partyCooldownOverlayFailureReported;

    internal void DrawPartyCooldownOverlay()
    {
        try
        {
            DrawNativePartyCooldownOverlay();
        }
        catch (Exception error)
        {
            if (!partyCooldownOverlayFailureReported)
            {
                partyCooldownOverlayFailureReported = true;
                Plugin.Log.Warning(error, "Could not draw the party cooldown overlay.");
            }
        }
    }

    private unsafe void DrawNativePartyCooldownOverlay()
    {
        if (!configuration.ShowPartyCooldowns || Plugin.GameGui.GameUiHidden || Plugin.ClientState.IsPvP ||
            !Plugin.ClientState.IsLoggedIn || Plugin.PluginInterface.UiBuilder.CutsceneActive)
            return;
        var addon = Plugin.GameGui.GetAddonByName("_PartyList");
        if (addon.IsNull || !addon.IsReady || !addon.IsVisible)
            return;
        var native = (AddonPartyList*)addon.Address;
        var numbers = PartyListNumberArray.Instance();
        if (numbers is null || !numbers->IsVisible)
            return;
        var count = Math.Clamp(numbers->PartyListCount, 0, 8);
        var uiCount = Math.Clamp(native->MemberCount, 0, 8);
        Span<int> displayRows = stackalloc int[uiCount];
        for (var index = 0; index < uiCount; index++)
            displayRows[index] = native->PartyMembers[index].DisplayRow;
        var draw = ImGui.GetBackgroundDrawList();
        var viewport = ImGui.GetMainViewport();
        draw.PushClipRect(viewport.Pos, viewport.Pos + viewport.Size, true);
        try
        {
            for (var index = 0; index < count; index++)
            {
                ref var data = ref numbers->PartyMembers[index];
                if (data.EntityId is 0 or 0xE0000000 || data.DisplayRow < 0 || data.DisplayRow >= 8)
                    continue;
                var uiIndex = PartyCooldownLayout.FindUniqueRow(displayRows, data.DisplayRow);
                if (uiIndex < 0)
                    continue;
                var node = native->PartyMembers[uiIndex].Name;
                if (node is null || !node->IsVisible())
                    continue;
                var actions = plugin.GetVisiblePartyCooldowns(data.EntityId);
                if (actions.Count == 0)
                    continue;
                var scale = node->GetScaleX();
                if (!float.IsFinite(scale) || scale <= 0)
                    continue;
                ushort textWidth = 0;
                ushort textHeight = 0;
                node->GetTextDrawSize(&textWidth, &textHeight);
                var nameMin = viewport.Pos + new Vector2(node->ScreenX, node->ScreenY);
                var nameMax = nameMin + new Vector2(Math.Max(1, (int)textWidth) * scale, Math.Max(1, (int)node->Height) * scale);
                var row = new PartyCooldownRow(data.EntityId, data.DisplayRow, nameMin, nameMax, scale);
                var origin = PartyCooldownLayout.GetOrigin(row, actions.Count, configuration.PartyCooldownSide,
                    configuration.PartyCooldownIconSize, configuration.PartyCooldownIconGap,
                    new(configuration.PartyCooldownOffsetX, configuration.PartyCooldownOffsetY));
                var size = PartyCooldownLayout.IconSize(configuration.PartyCooldownIconSize) * scale;
                var step = size + PartyCooldownLayout.Gap(configuration.PartyCooldownIconGap) * scale;
                for (var actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                    DrawPartyCooldownStatus(draw, origin + new Vector2(actionIndex * step, 0), size,
                        actions[actionIndex], plugin.GetPartyCooldownStatus(data.EntityId, actions[actionIndex]));
            }
        }
        finally
        {
            draw.PopClipRect();
        }
    }

    private void DrawPartyCooldownStatus(ImDrawListPtr draw, Vector2 position, float size, PartyCooldownAction action, PartyCooldownStatus status)
    {
        var tint = new Vector4(1, 1, 1, status.IsReady ? 1 : .45f);
        DrawPartyCooldownIcon(draw, position, size, action.IconId, tint);
        var label = !status.IsKnown ? "?" : status.RemainingSeconds > 0 && status.Charges < status.MaxCharges
            ? Math.Ceiling(status.RemainingSeconds).ToString("0") : string.Empty;
        if (label.Length > 0)
            DrawPartyCooldownText(draw, position + new Vector2(size * .5f, size * .5f), label, MathF.Max(11, size * .48f));
        if (status.MaxCharges > 1 && status.IsKnown)
            DrawPartyCooldownText(draw, position + new Vector2(size - 5, size - 5), status.Charges.ToString(), MathF.Max(10, size * .36f));
    }

    private void DrawPartyCooldownIcon(ImDrawListPtr draw, Vector2 position, float size, uint iconId, Vector4 tint)
    {
        if (!partyCooldownTextures.TryGetValue(iconId, out var texture))
        {
            texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            partyCooldownTextures[iconId] = texture;
        }
        if (texture.GetWrapOrDefault() is { } wrap)
            draw.AddImage(wrap.Handle, position, position + new Vector2(size), Vector2.Zero, Vector2.One, ImGui.GetColorU32(tint));
    }

    private static void DrawPartyCooldownText(ImDrawListPtr draw, Vector2 center, string value, float fontSize)
    {
        var font = ImGui.GetFont();
        var measured = ImGui.CalcTextSize(value) * (fontSize / ImGui.GetFontSize());
        var position = center - measured * .5f;
        draw.AddText(font, fontSize, position + Vector2.One, 0xFF000000, value);
        draw.AddText(font, fontSize, position, 0xFFFFFFFF, value);
    }
}
