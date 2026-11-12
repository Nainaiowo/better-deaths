using BetterDeaths.PartyCooldowns;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private readonly PartyCooldownEditorState cooldownEditor = new();
    private uint cooldownPreviewJob => cooldownEditor.FocusedJob;
    private bool cooldownPreviewInitialized;
    private uint? cooldownRequestedJob;
    private const string PartyCooldownDragPayload = "BETTER_DEATHS_PARTY_COOLDOWN";
    private readonly Dictionary<uint, ISharedImmediateTexture> partyCooldownTextures = [];
    private readonly Dictionary<uint, int> partyCooldownPreviewCharges = [];
    private readonly Dictionary<uint, int> partyCooldownTargetRows = [];
    private readonly PartyCooldownDisplayEntry[] partyCooldownDrawEntries = new PartyCooldownDisplayEntry[PartyCooldownRows.MaximumAbilities];
    private readonly PartyCooldownNameMeasurement?[] partyCooldownNameMeasurements = new PartyCooldownNameMeasurement?[8];
    private readonly Dictionary<string, Vector2> partyCooldownTextMeasurements = new(StringComparer.Ordinal);
    private ImFontPtr partyCooldownDrawFont;
    private float partyCooldownDrawFontSize;

    private void DrawPartyCooldownSettings()
    {
        DrawModernSectionTitle("Party Cooldowns", string.Empty);
        DrawSubtleSeparator();
        ImGui.Spacing();
        var visible = configuration.ShowPartyCooldowns;
        if (DrawThemedCheckbox("Show party cooldowns", ref visible))
            plugin.SetPartyCooldownVisibility(visible);
        const string collapseLabel = "Auto-collapse other jobs";
        var checkboxWidth = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(collapseLabel).X;
        if (ImGui.GetContentRegionAvail().X - ImGui.GetItemRectSize().X - ImGui.GetStyle().ItemSpacing.X * 2 >= checkboxWidth)
            ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X * 2);
        var autoCollapse = configuration.PartyCooldownAutoCollapseJobs;
        if (DrawThemedCheckbox(collapseLabel, ref autoCollapse))
        {
            configuration.PartyCooldownAutoCollapseJobs = autoCollapse;
            if (autoCollapse)
                FocusPartyCooldownJob(cooldownPreviewJob);
            plugin.SaveConfiguration();
        }
        DrawSettingsTooltip("Focusing a job closes the other job and role sections. Applies to job headers and the preview dropdown. Turn off to keep multiple jobs expanded.");
        ImGui.Spacing();
        if (!cooldownPreviewInitialized)
        {
            cooldownPreviewInitialized = true;
            var ownJob = PartyCooldownCatalog.NormalizeJob(Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0);
            if (PartyCooldownCatalog.Jobs.Any(job => job.Id == ownJob))
                FocusPartyCooldownJob(ownJob);
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
            if (guideActive && OnboardingGuide.Steps[guideStepIndex].Target == GuideTarget.PartyCooldownLayout)
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
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
        var start = ImGui.GetCursorScreenPos();
        FocusGuideTarget(GuideTarget.PartyCooldownLayout);
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
        var placement = configuration.GetPartyCooldownPlacement();
        var size = placement.IconSize;
        DrawCooldownSlider("Icon size", "PartyCooldownSize", ref size,
            PartyCooldownLayout.MinimumIconSize, PartyCooldownLayout.MaximumIconSize, "%.0f px");
        placement.IconSize = size;
        var gap = placement.Spacing;
        DrawCooldownSlider("Spacing", "PartyCooldownGap", ref gap, 0, 12, "%.0f px");
        placement.Spacing = gap;
        var offsetX = placement.OffsetX;
        DrawCooldownSlider("Horizontal offset", "PartyCooldownOffsetX", ref offsetX, -300, 300, "%.0f px");
        placement.OffsetX = offsetX;
        var offsetY = placement.OffsetY;
        DrawCooldownSlider("Vertical offset", "PartyCooldownOffsetY", ref offsetY, -300, 300, "%.0f px");
        placement.OffsetY = offsetY;
        var layoutButtonsWidth = ImGui.GetContentRegionAvail().X;
        if (DrawTransparentIconButton("ResetPartyCooldownPlacement", FontAwesomeIcon.Undo))
        {
            placement.OffsetX = 0;
            placement.OffsetY = 0;
            plugin.SaveConfiguration();
        }
        var resetButtonWidth = ImGui.GetItemRectSize().X;
        if (ImGui.IsItemHovered())
            SetThemedTooltip("Reset this side's offsets. Keeps icon size, spacing, and per-job abilities and custom rows.");
        if (layoutButtonsWidth >= resetButtonWidth + ImGui.GetStyle().ItemSpacing.X +
            GetThemedIconButtonWidth(FontAwesomeIcon.Home, "Default"))
            ImGui.SameLine();
        if (DrawThemedActionButton(FontAwesomeIcon.Home, "Default", "DefaultPartyCooldownPlacement"))
        {
            configuration.PartyCooldownPlacements!.ApplyDefault(configuration.PartyCooldownSide);
            plugin.SaveConfiguration();
        }
        if (ImGui.IsItemHovered())
            SetThemedTooltip("Restore this side's default icon size, spacing, and position. Keeps the other side, per-job abilities, and custom rows.");
        RegisterGuideTarget(GuideTarget.PartyCooldownLayout, start,
            new Vector2(start.X + ImGui.GetContentRegionAvail().X, ImGui.GetCursorScreenPos().Y));
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
        cooldownRequestedJob = null;
        if (requested is { } requestedId)
            cooldownEditor.Focus(requestedId, configuration.PartyCooldownAutoCollapseJobs);
        foreach (var role in Enum.GetValues<PartyCooldownRole>())
        {
            var label = role switch
            {
                PartyCooldownRole.Tank => "Tanks",
                PartyCooldownRole.Healer => "Healers",
                PartyCooldownRole.Melee => "Melee",
                _ => "Ranged",
            };
            ImGui.SetNextItemOpen(cooldownEditor.IsRoleOpen(role), ImGuiCond.Always);
            var roleOpen = ImGui.CollapsingHeader($"{label}##CooldownRole{role}");
            cooldownEditor.SetRoleOpen(role, roleOpen);
            if (!roleOpen)
                continue;
            foreach (var job in PartyCooldownCatalog.Jobs.Where(job => job.Role == role))
            {
                var selected = plugin.GetPartyCooldownSelection(job.Id);
                var wasOpen = cooldownEditor.IsJobOpen(job.Id);
                ImGui.SetNextItemOpen(wasOpen, ImGuiCond.Always);
                var flags = ImGuiTreeNodeFlags.SpanAvailWidth |
                    (job.Id == cooldownPreviewJob ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None);
                var jobOpen = ImGui.TreeNodeEx($"{job.Name}  ({selected.Count}/{PartyCooldownRows.MaximumAbilities})###CooldownJob{job.Id}", flags);
                if (jobOpen != wasOpen)
                {
                    cooldownEditor.SetJobOpen(job.Id, jobOpen, configuration.PartyCooldownAutoCollapseJobs);
                    if (jobOpen)
                        cooldownRequestedJob = job.Id;
                }
                if (!jobOpen)
                    continue;
                if (requested == job.Id)
                    ImGui.SetScrollHereY(.2f);
                DrawPartyCooldownJobEditor(catalog, job, selected);
                ImGui.TreePop();
            }
        }
        RegisterGuideTarget(GuideTarget.PartyCooldowns, start,
            new Vector2(start.X + ImGui.GetContentRegionAvail().X, ImGui.GetCursorScreenPos().Y));
        DrawReviewPaneBottomPadding();
    }

    private void FocusPartyCooldownJob(uint jobId)
    {
        if (cooldownEditor.Focus(jobId, configuration.PartyCooldownAutoCollapseJobs))
            cooldownRequestedJob = cooldownPreviewJob;
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
        var rows = plugin.GetPartyCooldownRows(job.Id);
        var custom = rows.Enabled;
        if (DrawThemedCheckbox($"Custom rows##PartyCooldownRows{job.Id}", ref custom))
        {
            rows.Enabled = custom;
            SavePartyCooldownRows(job.Id, rows, selected);
        }
        DrawSettingsTooltip("Choose up to three rows for this job. Off shows one row and remembers your custom arrangement. Shared between left and right of name.");
        ImGui.TextColored(ModernMutedTextColor, "Selected cooldowns");
        if (rows.Enabled)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(rows.Rows.Count >= PartyCooldownRows.MaximumRows);
            if (DrawTransparentIconButton($"AddPartyCooldownRow{job.Id}", FontAwesomeIcon.Plus) && rows.AddRow())
            {
                partyCooldownTargetRows[job.Id] = rows.Rows.Count - 1;
                SavePartyCooldownRows(job.Id, rows, selected);
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                SetThemedTooltip(rows.Rows.Count >= PartyCooldownRows.MaximumRows ? "Maximum of three rows." : "Add a row.");
            var targetRow = Math.Clamp(partyCooldownTargetRows.GetValueOrDefault(job.Id), 0, rows.Rows.Count - 1);
            partyCooldownTargetRows[job.Id] = targetRow;
            for (var index = 0; index < rows.Rows.Count; index++)
            {
                if (ImGui.RadioButton($"Row {index + 1}##PartyCooldownTarget{job.Id}_{index}", targetRow == index))
                    partyCooldownTargetRows[job.Id] = targetRow = index;
                DrawSettingsTooltip("Click + on an available ability to add it to this row. Drag abilities into any row or between rows.");
                if (index > 0)
                {
                    ImGui.SameLine();
                    ImGui.BeginDisabled(rows.Rows[index].Count != 0);
                    var remove = DrawTransparentIconButton($"RemovePartyCooldownRow{job.Id}_{index}", FontAwesomeIcon.Trash);
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        SetThemedTooltip(rows.Rows[index].Count == 0 ? "Remove this empty row." : "Move or remove this row's abilities first.");
                    if (remove && rows.RemoveEmptyRow(index))
                    {
                        if (targetRow >= index)
                            partyCooldownTargetRows[job.Id] = targetRow = Math.Max(0, targetRow - 1);
                        SavePartyCooldownRows(job.Id, rows, selected);
                        break;
                    }
                }
                DrawPartyCooldownTiles(catalog, job.Id, rows.Rows[index], selected, true, rows, index);
                ImGui.Spacing();
            }
        }
        else
            DrawPartyCooldownTiles(catalog, job.Id, selected, selected, true);
        ImGui.Spacing();
        foreach (var mitigation in new[] { true, false })
        {
            var available = catalog.ForJob(job.Id).Where(action => !selected.Contains(action.Id) &&
                    PartyCooldownSelectionPolicy.IsMitigation(action.Id) == mitigation)
                .Select(action => action.Id).ToArray();
            if (available.Length == 0)
                continue;
            ImGui.PushTextWrapPos();
            ImGui.TextColored(ModernMutedTextColor, mitigation ? "Available defensives" : "Available damage and support cooldowns");
            ImGui.PopTextWrapPos();
            DrawPartyCooldownTiles(catalog, job.Id, available, selected, false, rows.Enabled ? rows : null,
                partyCooldownTargetRows.GetValueOrDefault(job.Id));
            ImGui.Spacing();
        }
        ImGui.Spacing();
        if (DrawThemedActionButton(FontAwesomeIcon.Undo, $"Reset {job.Abbreviation}", $"ResetPartyCooldownJob{job.Id}"))
        {
            plugin.SetPartyCooldownSelection(job.Id, null);
            partyCooldownTargetRows.Remove(job.Id);
            FocusPartyCooldownJob(job.Id);
        }
        DrawSettingsTooltip("Restore this job's default abilities and one-row layout. Does not reset cooldown timers or placement settings.");
        ImGui.Spacing();
    }

    private void SavePartyCooldownRows(uint jobId, PartyCooldownRows rows, List<uint> selected)
    {
        plugin.SetPartyCooldownRows(jobId, rows);
        selected.Clear();
        selected.AddRange(plugin.GetPartyCooldownSelection(jobId));
        FocusPartyCooldownJob(jobId);
    }

    private void DrawPartyCooldownTiles(PartyCooldownCatalog catalog, uint jobId, IReadOnlyList<uint> ids, List<uint> selected,
        bool active, PartyCooldownRows? rows = null, int targetRow = 0)
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
            if (DrawPartyCooldownTile(jobId, action, selected, active, tileWidth, rows, targetRow))
            {
                if (rows is not null)
                {
                    if (active ? rows.Remove(id) : rows.Place(id, targetRow))
                        SavePartyCooldownRows(jobId, rows, selected);
                }
                else
                {
                    if (active)
                        selected.Remove(id);
                    else if (selected.Count < PartyCooldownRows.MaximumAbilities)
                        selected.Add(id);
                    plugin.SetPartyCooldownSelection(jobId, selected);
                    FocusPartyCooldownJob(jobId);
                }
            }
            rowWidth += tileWidth;
        }
        if (active)
            DrawPartyCooldownDropZone(jobId, selected, rowWidth, width, rows, targetRow);
    }

    private unsafe bool DrawPartyCooldownTile(uint jobId, PartyCooldownAction action, List<uint> selected, bool active,
        float width, PartyCooldownRows? rows, int targetRow)
    {
        var enabled = action.UnsupportedReason is null && (active || selected.Count < PartyCooldownRows.MaximumAbilities);
        var start = ImGui.GetCursorScreenPos();
        var height = MathF.Max(30, ImGui.GetFrameHeight() + 6);
        var size = new Vector2(MathF.Max(1, width), height);
        ImGui.BeginDisabled(!enabled);
        var clicked = ImGui.InvisibleButton($"##PartyCooldownTile{jobId}_{action.Id}_{active}_{targetRow}", size);
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
                ImGui.GetColorU32(color with { W = color.W * .6f }), FontAwesomeIcon.GripVertical.ToIconString());
            var glyph = (active ? FontAwesomeIcon.Times : FontAwesomeIcon.Plus).ToIconString();
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), start + new Vector2(width - 23, (height - ImGui.GetTextLineHeight()) * .5f),
                ImGui.GetColorU32(color), glyph);
        }
        if (hovered)
            SetThemedTooltip(action.UnsupportedReason ?? (!active && selected.Count >= PartyCooldownRows.MaximumAbilities
                ? $"Up to {PartyCooldownRows.MaximumAbilities} abilities can be shown for each job."
                : $"{action.Name}\n{action.RecastSeconds:0.#}s base recast | Level {action.Level}"));
        if (enabled && ImGui.BeginDragDropSource())
        {
            uint* data = stackalloc uint[2] { jobId, action.Id };
            ImGui.SetDragDropPayload(PartyCooldownDragPayload, new ReadOnlySpan<byte>(data, 8));
            ImGui.TextUnformatted(action.Name);
            ImGui.EndDragDropSource();
        }
        if (active && ImGui.BeginDragDropTarget())
        {
            AcceptPartyCooldownDrop(jobId, selected, action.Id, ImGui.GetIO().MousePos.X >= start.X + size.X * .5f,
                start, size, rows, targetRow);
            ImGui.EndDragDropTarget();
        }
        ImGui.EndDisabled();
        return enabled && clicked && (!active || ImGui.GetIO().MousePos.X >= removeMin);
    }

    private void DrawPartyCooldownDropZone(uint jobId, List<uint> selected, float rowWidth, float availableWidth,
        PartyCooldownRows? rows, int targetRow)
    {
        var width = availableWidth;
        var count = rows is null ? selected.Count : rows.Rows[targetRow].Count;
        if (count > 0)
        {
            width -= rowWidth + ImGui.GetStyle().ItemSpacing.X;
            if (width < 12)
                return;
            ImGui.SameLine();
        }
        var start = ImGui.GetCursorScreenPos();
        var size = new Vector2(MathF.Max(1, width), MathF.Max(30, ImGui.GetFrameHeight() + 6));
        ImGui.InvisibleButton($"##PartyCooldownAppend{jobId}_{targetRow}", size);
        if (count == 0)
            ImGui.GetWindowDrawList().AddText(start + new Vector2(4, (size.Y - ImGui.GetTextLineHeight()) * .5f),
                ImGui.GetColorU32(ModernMutedTextColor), "None");
        if (ImGui.BeginDragDropTarget())
        {
            AcceptPartyCooldownDrop(jobId, selected, null, false, start, size, rows, targetRow);
            ImGui.EndDragDropTarget();
        }
    }

    private unsafe void AcceptPartyCooldownDrop(uint jobId, List<uint> selected, uint? target, bool after, Vector2 start,
        Vector2 size, PartyCooldownRows? rows, int targetRow)
    {
        var payload = ImGui.AcceptDragDropPayload(PartyCooldownDragPayload,
            ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
        if (payload.IsNull || payload.Data is null || payload.DataSize != 8)
            return;
        var data = (uint*)payload.Data;
        if (data[0] != jobId || plugin.GetPartyCooldownCatalog()?.Find(data[1]) is not { UnsupportedReason: null } action ||
            !action.Jobs.Contains(jobId) || data[1] == target || selected.Count >= PartyCooldownRows.MaximumAbilities && !selected.Contains(data[1]))
            return;
        if (target is null)
            ImGui.GetWindowDrawList().AddRectFilled(start, start + size, ImGui.GetColorU32(ModernAccentColor with { W = .12f }), 2);
        DrawTileInsertionMarker(start, start + size, after);
        if (payload.Delivery)
        {
            if (rows is not null)
            {
                if (rows.Place(data[1], targetRow, target, after))
                    SavePartyCooldownRows(jobId, rows, selected);
                return;
            }
            var changed = after && target is { } anchor ? PartyCooldownCatalog.PlaceAfter(selected, data[1], anchor)
                : PartyCooldownCatalog.PlaceBefore(selected, data[1], target);
            if (changed)
            {
                plugin.SetPartyCooldownSelection(jobId, selected);
                FocusPartyCooldownJob(jobId);
            }
        }
    }

    private void DrawPartyCooldownPreview()
    {
        var start = ImGui.GetCursorScreenPos();
        PreparePartyCooldownFont();
        ImGui.TextColored(ModernAccentColor, "Preview");
        FocusGuideTarget(GuideTarget.PartyCooldownPreview);
        var job = PartyCooldownCatalog.Jobs.First(candidate => candidate.Id == cooldownPreviewJob);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##PartyCooldownPreviewJob", job.Name))
        {
            foreach (var candidate in PartyCooldownCatalog.Jobs)
                if (ImGui.Selectable(candidate.Name, candidate.Id == cooldownPreviewJob))
                {
                    FocusPartyCooldownJob(candidate.Id);
                }
            ImGui.EndCombo();
        }
        job = PartyCooldownCatalog.Jobs.First(candidate => candidate.Id == cooldownPreviewJob);
        var actions = plugin.GetPartyCooldownSelection(job.Id);
        var catalog = plugin.GetPartyCooldownCatalog();
        var placement = configuration.GetPartyCooldownPlacement();
        var iconSize = PartyCooldownLayout.IconSize(placement.IconSize);
        var gap = PartyCooldownLayout.Gap(placement.Spacing);
        configuration.PartyCooldownRowLayouts.TryGetValue(job.Id, out var rows);
        var previewHeight = Math.Clamp((rows?.Enabled == true ? rows.Rows.Count : 1) * (iconSize + gap) + 40, 112, 260);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ModernPanelAltColor);
        if (ImGui.BeginChild("##PartyCooldownPreviewFrame", new Vector2(0, previewHeight), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.SetScrollX(0);
            ImGui.SetScrollY(0);
            var frame = TryReadPartyCooldownPreviewFrame(out var native) ? native : DefaultPartyCooldownPreviewFrame();
            var hoveredAction = DrawPartyCooldownPreviewContents(catalog, actions, rows, placement, frame);
            if (hoveredAction is { } action && ImGui.IsWindowHovered() && !ImGui.IsAnyItemActive())
                SetThemedTooltip($"{action.Name}\n{PartyCooldownCatalog.RecastAtLevel(action.Id, 100, action.RecastSeconds):0.#}s base recast | Level {action.Level}");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        RegisterGuideTarget(GuideTarget.PartyCooldownPreview, start,
            new Vector2(start.X + ImGui.GetContentRegionAvail().X, ImGui.GetCursorScreenPos().Y));
    }

    private bool partyCooldownOverlayFailureReported;
    private DateTime? partyCooldownOverlayBlockedAt;
    private bool partyCooldownOverlayBlockedReported;

    internal void DrawPartyCooldownOverlay()
    {
        using var sample = plugin.MeasurePartyCooldown(PartyCooldownPhase.Draw);
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
        {
            ResetPartyCooldownOverlayWarning();
            return;
        }
        var addon = Plugin.GameGui.GetAddonByName("_PartyList");
        if (addon.IsNull || !addon.IsVisible)
        {
            ResetPartyCooldownOverlayWarning();
            return;
        }
        if (!addon.IsReady)
        {
            ReportPartyCooldownOverlayBlocked("party list not ready", 0, 0, 0);
            return;
        }
        var native = (AddonPartyList*)addon.Address;
        var hud = AgentHUD.Instance();
        if (hud is null)
        {
            ReportPartyCooldownOverlayBlocked("HUD agent unavailable", 0, 0, 0);
            return;
        }
        // AgentHUD explicitly maps each entity to its native party-list slot.
        // Its member-array order is not the party-list order, including for self.
        Span<PartyCooldownHudMember> members = stackalloc PartyCooldownHudMember[hud->PartyMembers.Length];
        var memberCount = 0;
        for (var index = 0; index < members.Length; index++)
        {
            ref var member = ref hud->PartyMembers[index];
            members[index] = new(member.EntityId, member.Index);
            if (member.EntityId is > 0 and not 0xE0000000 && member.Index < native->PartyMembers.Length)
                memberCount++;
        }
        var matchedRows = 0;
        var selectedIcons = 0;
        var drawnIcons = 0;
        var placement = configuration.GetPartyCooldownPlacement();
        Span<int> rowCounts = stackalloc int[PartyCooldownLayout.MaximumRows];
        Span<int> columns = stackalloc int[PartyCooldownLayout.MaximumRows];
        PreparePartyCooldownFont();
        var draw = ImGui.GetForegroundDrawList();
        var viewport = ImGui.GetMainViewport();
        draw.PushClipRect(viewport.Pos, viewport.Pos + viewport.Size, true);
        try
        {
            for (var rowIndex = 0; rowIndex < native->PartyMembers.Length; rowIndex++)
            {
                var memberIndex = PartyCooldownLayout.FindUniqueHudMember(members, rowIndex);
                if (memberIndex < 0)
                    continue;
                var entityId = members[memberIndex].EntityId;
                ref var partyRow = ref native->PartyMembers[rowIndex];
                var node = partyRow.Name;
                var component = partyRow.PartyMemberComponent;
                // Casting hides the name, not the member row or its cooldowns.
                if (node is null || component is null || component->OwnerNode is null || !component->OwnerNode->IsVisible())
                    continue;
                matchedRows++;
                var actionCount = plugin.CopyPartyCooldownDisplay(entityId, partyCooldownDrawEntries);
                selectedIcons += actionCount;
                if (actionCount == 0)
                    continue;
                rowCounts.Clear();
                columns.Clear();
                for (var index = 0; index < actionCount; index++)
                    rowCounts[partyCooldownDrawEntries[index].Row]++;
                var scale = node->GetScaleX();
                if (!float.IsFinite(scale) || scale <= 0)
                    continue;
                var textWidth = 0f;
                if (configuration.PartyCooldownSide == PartyCooldownSide.Right)
                {
                    var cache = partyCooldownNameMeasurements[rowIndex] ??= new();
                    var key = new PartyCooldownNameKey((nint)node, node->Width, node->Height, node->FontSize,
                        node->AlignmentFontType, node->CharSpacing, node->LineSpacing, (ushort)node->TextFlags, node->FontCacheHandle);
                    var name = node->NodeText.AsSpan();
                    if (!cache.TryGet(key, name, out var measured))
                    {
                        ushort width = 0;
                        ushort height = 0;
                        node->GetTextDrawSize(&width, &height);
                        measured = new(width, height);
                        cache.Store(key, name, measured);
                    }
                    textWidth = measured.X;
                }
                var nameMin = viewport.Pos + new Vector2(node->ScreenX, node->ScreenY);
                var nameMax = nameMin + new Vector2(MathF.Max(1, textWidth) * scale, Math.Max(1, (int)node->Height) * scale);
                var row = new PartyCooldownRow(entityId, rowIndex, nameMin, nameMax, scale);
                var origin = PartyCooldownLayout.GetOrigin(row, rowCounts, configuration.PartyCooldownSide,
                    placement.IconSize, placement.Spacing, new(placement.OffsetX, placement.OffsetY));
                var size = PartyCooldownLayout.IconSize(placement.IconSize) * scale;
                for (var actionIndex = 0; actionIndex < actionCount; actionIndex++)
                {
                    ref var entry = ref partyCooldownDrawEntries[actionIndex];
                    var position = origin + PartyCooldownLayout.GetIconOffset(entry.Row, columns[entry.Row]++, rowCounts,
                        configuration.PartyCooldownSide, placement.IconSize, placement.Spacing) * scale;
                    if (position.X + size <= viewport.Pos.X || position.X >= viewport.Pos.X + viewport.Size.X ||
                        position.Y + size <= viewport.Pos.Y || position.Y >= viewport.Pos.Y + viewport.Size.Y)
                        continue;
                    DrawPartyCooldownStatus(draw, position, size, entry.Action, entry.Status, entry.TimerLabel, entry.ChargeLabel);
                    drawnIcons++;
                }
            }
        }
        finally
        {
            draw.PopClipRect();
        }
        if (drawnIcons > 0 || matchedRows > 0 && selectedIcons == 0 && plugin.PartyCooldownActorCount > 0)
            ResetPartyCooldownOverlayWarning();
        else
            ReportPartyCooldownOverlayBlocked("no on-screen cooldowns", memberCount, matchedRows, selectedIcons);
    }

    private void ResetPartyCooldownOverlayWarning()
    {
        partyCooldownOverlayBlockedAt = null;
        partyCooldownOverlayBlockedReported = false;
    }

    private void ReportPartyCooldownOverlayBlocked(string reason, int members, int rows, int icons)
    {
        var now = DateTime.UtcNow;
        partyCooldownOverlayBlockedAt ??= now;
        if (partyCooldownOverlayBlockedReported || now - partyCooldownOverlayBlockedAt.Value < TimeSpan.FromSeconds(5))
            return;
        partyCooldownOverlayBlockedReported = true;
        Plugin.Log.Warning("Party cooldown overlay blocked: {Reason}; HUD members {Members}, name rows {Rows}, " +
            "tracked members {TrackedMembers}, selected icons {Icons}, catalog loaded {CatalogLoaded}.",
            reason, members, rows, plugin.PartyCooldownActorCount, icons, plugin.GetPartyCooldownCatalog() is not null);
    }

    private void DrawPartyCooldownStatus(ImDrawListPtr draw, Vector2 position, float size, PartyCooldownAction action,
        PartyCooldownStatus status, string? timerLabel = null, string? chargeLabel = null, float textScale = 1)
    {
        var tint = new Vector4(1, 1, 1, status.IsOnCooldown ? .45f : 1);
        DrawPartyCooldownIcon(draw, position, size, action.IconId, tint);
        var label = timerLabel ?? PartyCooldownLabels.Timer(status);
        if (label.Length > 0)
            DrawPartyCooldownText(draw, position + new Vector2(size * .5f, size * .5f), label,
                PartyCooldownLayout.TimerTextSize(size, textScale), textScale);
        if (status.MaxCharges > 1 && status.IsKnown)
            DrawPartyCooldownText(draw, position + new Vector2(size - 5 * textScale), chargeLabel ?? PartyCooldownLabels.Charges(status),
                PartyCooldownLayout.ChargeTextSize(size, textScale), textScale);
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

    private unsafe void PreparePartyCooldownFont()
    {
        var font = ImGui.GetFont();
        var size = ImGui.GetFontSize();
        if (font.Handle != partyCooldownDrawFont.Handle || size != partyCooldownDrawFontSize)
            partyCooldownTextMeasurements.Clear();
        partyCooldownDrawFont = font;
        partyCooldownDrawFontSize = size;
    }

    private void DrawPartyCooldownText(ImDrawListPtr draw, Vector2 center, string value, float fontSize, float outlineScale = 1)
    {
        if (!partyCooldownTextMeasurements.TryGetValue(value, out var baseSize))
        {
            baseSize = ImGui.CalcTextSize(value);
            partyCooldownTextMeasurements[value] = baseSize;
        }
        var measured = baseSize * (fontSize / partyCooldownDrawFontSize);
        var position = center - measured * .5f;
        var outline = PartyCooldownLayout.TextOutlineWidth(fontSize, outlineScale);
        for (var y = -1; y <= 1; y++)
            for (var x = -1; x <= 1; x++)
                if (x != 0 || y != 0)
                    draw.AddText(partyCooldownDrawFont, fontSize, position + new Vector2(x, y) * outline, 0xFF000000, value);
        draw.AddText(partyCooldownDrawFont, fontSize, position, 0xFFFFFFFF, value);
    }
}
