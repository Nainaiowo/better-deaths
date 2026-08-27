namespace BetterDeaths.Windows;

using BetterDeaths.DamageParsing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

public sealed partial class RecapWindow
{
    private const string DamageMeterColumnDragPayload = "BETTER_DEATHS_METER_COLUMN";
    private static readonly DamageMeterColumn[] AvailableDamageMeterColumns = Enum.GetValues<DamageMeterColumn>();
    private readonly HashSet<string> expandedDamageMeterSources = new(StringComparer.Ordinal);
    private DamageMeterColumn? draggingDamageMeterColumn;
    private long selectedDamageEncounterNumber = -1;

    private void DrawWidgetsPage()
    {
        var available = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        if (available.X >= 900.0f)
        {
            var leftWidth = MathF.Max(480.0f, (available.X - spacing) * 0.5f);
            DrawReviewPanel(
                "##DeathWidgetSettings",
                new Vector2(leftWidth, available.Y),
                DrawWidgetTab);
            ImGui.SameLine();
            DrawDamageMeterWorkspace();
            return;
        }

        var deathWidgetHeight = MathF.Min(460.0f, MathF.Max(300.0f, available.Y * 0.44f));
        DrawReviewPanel(
            "##DeathWidgetSettingsStacked",
            new Vector2(0.0f, deathWidgetHeight),
            DrawWidgetTab);
        ImGui.Dummy(new Vector2(1.0f, WorkspacePaneGap));
        DrawDamageMeterWorkspace();
    }

    private void DrawDamageMeterWorkspace()
    {
        var available = ImGui.GetContentRegionAvail();
        var collapsed = configuration.DamageMeterBrowserCollapsed;
        var stacked = available.X < 860.0f;
        if (stacked)
        {
            var expandedBrowserMinimum = available.Y < 430.0f ? 110.0f : 150.0f;
            var browserHeight = collapsed
                ? 54.0f
                : MathF.Min(210.0f, MathF.Max(expandedBrowserMinimum, available.Y * 0.28f));
            DrawReviewPanel("##DamageMeterEncountersStacked", new Vector2(0.0f, browserHeight), DrawDamageMeterBrowser);
            ImGui.Dummy(new Vector2(1.0f, WorkspacePaneGap));
            DrawReviewPanel("##DamageMeter", Vector2.Zero, DrawDamageMeterContent);
            return;
        }

        var browserWidth = collapsed ? PullBrowserCollapsedWidth : PullBrowserExpandedWidth;
        DrawReviewPanel("##DamageMeterEncounters", new Vector2(browserWidth, available.Y), DrawDamageMeterBrowser);
        DrawVerticalReviewDivider("DamageMeterBrowserDivider", available.Y);
        DrawReviewPanel("##DamageMeter", Vector2.Zero, DrawDamageMeterContent);
    }

    private void DrawDamageMeterContent()
    {
        ImGui.TextColored(LeadUpGoldColor, "DPS Meter");
        ImGui.SameLine();
        ImGui.TextColored(ModernMutedTextColor, "Widget display and columns");
        ImGui.Dummy(new Vector2(1.0f, 5.0f));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(1.0f, 8.0f));

        DrawDamageMeterWidgetSettings();
        ImGui.Dummy(new Vector2(1.0f, 10.0f));
        DrawDamageMeterDataSettings();
        ImGui.Dummy(new Vector2(1.0f, 10.0f));
        DrawDamageMeterColumnSettings();
        ImGui.Dummy(new Vector2(1.0f, 10.0f));
        DrawDamageMeterPreview();
        DrawReviewPaneBottomPadding();
    }

    private void DrawDamageMeterWidgetSettings()
    {
        ImGui.TextColored(ModernAccentColor, "Widget");
        var showWidget = configuration.ShowDamageMeterWidget;
        if (DrawThemedSwitch("Show DPS meter widget", "DamageMeterWidgetVisible", ref showWidget))
        {
            plugin.SetShowDamageMeterWidget(showWidget);
        }

        ImGui.Dummy(new Vector2(1.0f, 3.0f));
        ImGui.TextUnformatted("Display");
        DrawDamageMeterDisplayModeSegment(WidgetDisplayMode.Normal);
        DrawTextSelectorSeparator();
        DrawDamageMeterDisplayModeSegment(WidgetDisplayMode.Concise);
        DrawSettingsTooltip("Normal shows full names and labels. Concise uses player initials and tighter columns.");
    }

    private void DrawDamageMeterDataSettings()
    {
        ImGui.TextColored(ModernAccentColor, "Data");
        ImGui.TextUnformatted("Combatants");
        ImGui.SameLine();
        var allCombatants = configuration.DamageMeterShowAllCombatants;
        var partyWidth = GetThemedActionButtonWidth("Party");
        if (DrawThemedToggleButton("Party", "DamageMeterParty", !allCombatants, partyWidth))
        {
            configuration.DamageMeterShowAllCombatants = false;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var allWidth = GetThemedActionButtonWidth("All");
        if (DrawThemedToggleButton("All", "DamageMeterAll", allCombatants, allWidth))
        {
            configuration.DamageMeterShowAllCombatants = true;
            plugin.SaveConfiguration();
        }
    }

    private void DrawDamageMeterDisplayModeSegment(WidgetDisplayMode mode)
    {
        var selected = configuration.DamageMeterWidgetDisplayMode == mode;
        if (DrawTextSelectorOption(GetWidgetDisplayModeLabel(mode), $"DamageMeterDisplayMode{mode}", selected))
        {
            plugin.SetDamageMeterWidgetDisplayMode(mode);
        }
    }

    private void DrawDamageMeterColumnSettings()
    {
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            draggingDamageMeterColumn = null;
        }

        configuration.DamageMeterColumns = DamageMeterColumnPolicy.Normalize(configuration.DamageMeterColumns);
        ImGui.TextColored(ModernAccentColor, "Widget columns");
        ImGui.TextColored(ModernMutedTextColor, "Drag to reorder. Select X to remove a column.");
        ImGui.Dummy(new Vector2(1.0f, 3.0f));

        var columns = configuration.DamageMeterColumns;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var rowWidth = 0.0f;
        DamageMeterColumn? removeColumn = null;
        foreach (var column in columns.ToList())
        {
            var tileWidth = GetDamageMeterColumnTileWidth(column);
            if (rowWidth > 0.0f && rowWidth + spacing + tileWidth <= availableWidth)
            {
                ImGui.SameLine(0.0f, spacing);
                rowWidth += spacing;
            }
            else
            {
                rowWidth = 0.0f;
            }

            if (DrawDamageMeterColumnTile(column, isActiveColumn: true, columns.Count > 1))
            {
                removeColumn = column;
            }

            rowWidth += tileWidth;
        }

        if (removeColumn is { } removed && columns.Remove(removed))
        {
            draggingDamageMeterColumn = null;
            plugin.SaveConfiguration();
        }

        ImGui.Dummy(new Vector2(1.0f, 8.0f));
        ImGui.TextColored(ModernMutedTextColor, "Available columns");
        rowWidth = 0.0f;
        foreach (var column in AvailableDamageMeterColumns.Where(column => !columns.Contains(column)))
        {
            var tileWidth = GetDamageMeterColumnTileWidth(column);
            if (rowWidth > 0.0f && rowWidth + spacing + tileWidth <= availableWidth)
            {
                ImGui.SameLine(0.0f, spacing);
                rowWidth += spacing;
            }
            else
            {
                rowWidth = 0.0f;
            }

            if (DrawDamageMeterColumnTile(column, isActiveColumn: false, canRemove: false))
            {
                columns.Add(column);
                draggingDamageMeterColumn = null;
                plugin.SaveConfiguration();
            }

            rowWidth += tileWidth;
        }

        if (columns.Count == AvailableDamageMeterColumns.Length)
        {
            ImGui.TextDisabled("All columns are active.");
        }

        ImGui.Dummy(new Vector2(1.0f, 6.0f));
        var resetLabel = $"{FontAwesomeIcon.Undo.ToIconString()}  Reset";
        var resetWidth = GetThemedActionButtonWidth(resetLabel);
        if (DrawThemedActionButton(resetLabel, "DamageMeterResetColumns", resetWidth))
        {
            configuration.DamageMeterColumns = DamageMeterColumnPolicy.CreateDefault();
            draggingDamageMeterColumn = null;
            plugin.SaveConfiguration();
        }
    }

    private bool DrawDamageMeterColumnTile(DamageMeterColumn column, bool isActiveColumn, bool canRemove)
    {
        var label = GetDamageMeterColumnLabel(column);
        var size = new Vector2(GetDamageMeterColumnTileWidth(column), ImGui.GetFrameHeight() + 6.0f);
        var start = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##DamageMeterColumn{(isActiveColumn ? "Active" : "Available")}{column}", size);
        var hovered = ImGui.IsItemHovered();
        var pressed = ImGui.IsItemActive();
        var end = start + size;
        var closeSize = size.Y;
        var closeStart = new Vector2(end.X - closeSize, start.Y);
        var mouse = ImGui.GetIO().MousePos;
        var closeHovered = hovered && mouse.X >= closeStart.X && mouse.X <= end.X &&
            mouse.Y >= start.Y && mouse.Y <= end.Y;

        var drawList = ImGui.GetWindowDrawList();
        var fill = pressed
            ? ModernNavButtonActiveColor with { W = 0.70f }
            : hovered
                ? ModernNavButtonSelectedColor with { W = 0.55f }
                : ModernPanelAltColor with { W = ActiveThemeUsesLightPanels() ? 0.72f : 0.55f };
        var border = draggingDamageMeterColumn == column
            ? ModernAccentColor
            : hovered
                ? ModernAccentColor with { W = 0.72f }
                : ModernPanelBorderColor with { W = 0.70f };
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(fill), 4.0f);
        drawList.AddRect(start, end, ImGui.GetColorU32(border), 4.0f);

        var grip = FontAwesomeIcon.GripLines.ToIconString();
        var textSize = ImGui.CalcTextSize(label);
        var close = (isActiveColumn ? FontAwesomeIcon.Times : FontAwesomeIcon.Plus).ToIconString();
        ImFontPtr iconFont;
        float iconFontSize;
        Vector2 gripSize;
        Vector2 closeTextSize;
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            iconFont = ImGui.GetFont();
            iconFontSize = ImGui.GetFontSize();
            gripSize = ImGui.CalcTextSize(grip);
            closeTextSize = ImGui.CalcTextSize(close);
        }

        var centerY = start.Y + (size.Y * 0.5f);
        drawList.AddText(
            iconFont,
            iconFontSize,
            new Vector2(start.X + 9.0f, centerY - (gripSize.Y * 0.5f)),
            ImGui.GetColorU32(ModernMutedTextColor),
            grip);
        drawList.AddText(
            new Vector2(start.X + 9.0f + gripSize.X + 8.0f, centerY - (textSize.Y * 0.5f)),
            ImGui.GetColorU32(ModernTextColor),
            label);
        drawList.AddText(
            iconFont,
            iconFontSize,
            new Vector2(closeStart.X + ((closeSize - closeTextSize.X) * 0.5f), centerY - (closeTextSize.Y * 0.5f)),
            ImGui.GetColorU32(isActiveColumn && !canRemove
                ? ModernMutedTextColor with { W = 0.45f }
                : canRemove || !isActiveColumn
                    ? ModernMutedTextColor
                    : ModernMutedTextColor with { W = 0.45f }),
            close);

        if (hovered)
        {
            ImGui.SetMouseCursor(closeHovered ? ImGuiMouseCursor.Hand : ImGuiMouseCursor.ResizeAll);
        }

        if (isActiveColumn && closeHovered && !canRemove)
        {
            SetThemedTooltip("At least one column must remain.");
        }

        if (ImGui.BeginDragDropSource())
        {
            draggingDamageMeterColumn = column;
            ImGui.SetDragDropPayload(DamageMeterColumnDragPayload, [1]);
            ImGui.TextUnformatted(label);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(DamageMeterColumnDragPayload);
            if (isActiveColumn && !payload.IsNull && draggingDamageMeterColumn is { } source &&
                DamageMeterColumnPolicy.PlaceBefore(configuration.DamageMeterColumns, source, column))
            {
                draggingDamageMeterColumn = null;
                plugin.SaveConfiguration();
            }

            ImGui.EndDragDropTarget();
        }

        return clicked && (!isActiveColumn || closeHovered && canRemove);
    }

    private static float GetDamageMeterColumnTileWidth(DamageMeterColumn column)
    {
        float gripWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            gripWidth = ImGui.CalcTextSize(FontAwesomeIcon.GripLines.ToIconString()).X;
        }

        return 9.0f + gripWidth + 8.0f + ImGui.CalcTextSize(GetDamageMeterColumnLabel(column)).X +
            9.0f + ImGui.GetFrameHeight() + 2.0f;
    }

    private static string GetDamageMeterColumnLabel(DamageMeterColumn column)
    {
        return column switch
        {
            DamageMeterColumn.Rank => "Rank",
            DamageMeterColumn.JobIcon => "Job icon",
            DamageMeterColumn.PlayerName => "Player",
            DamageMeterColumn.DamagePercent => "Damage %",
            DamageMeterColumn.DamagePerSecond => "DPS",
            DamageMeterColumn.RaidDamagePerSecond => "rDPS",
            DamageMeterColumn.CriticalHitPercent => "Critical hit %",
            DamageMeterColumn.DirectHitPercent => "Direct hit %",
            DamageMeterColumn.CriticalDirectHitPercent => "Critical + direct %",
            DamageMeterColumn.MaxHitAmount => "Max hit",
            DamageMeterColumn.MaxHitName => "Max hit name",
            DamageMeterColumn.TotalDamage => "Damage",
            DamageMeterColumn.Deaths => "Deaths",
            DamageMeterColumn.HitCount => "Hits",
            _ => column.ToString(),
        };
    }

    private void DrawDamageMeterBrowser()
    {
        var collapsed = configuration.DamageMeterBrowserCollapsed;
        var history = plugin.RecordedDamageEncounters;
        var current = plugin.CurrentDamageEncounter;
        if (collapsed)
        {
            if (ImGui.GetContentRegionAvail().X > PullBrowserCollapsedWidth + 100.0f)
            {
                ImGui.TextColored(LeadUpGoldColor, "Encounters");
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
                if (DrawTransparentIconButton("ExpandDamageMeterBrowserStacked", FontAwesomeIcon.ChevronDown))
                {
                    plugin.SetDamageMeterBrowserCollapsed(false);
                }

                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    SetThemedTooltip("Expand encounters");
                }

                return;
            }

            ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
            if (DrawCenteredTransparentIconButton("ExpandDamageMeterBrowser", FontAwesomeIcon.ChevronRight))
            {
                plugin.SetDamageMeterBrowserCollapsed(false);
            }

            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip("Expand encounters");
            }

            ImGui.Separator();
            DrawDamageMeterBrowserRailItem("EX", -1, selectedDamageEncounterNumber == -1, "Example preview");
            if (current is not null)
            {
                DrawDamageMeterBrowserRailItem("LIVE", 0, selectedDamageEncounterNumber == 0, "Live encounter");
            }

            foreach (var encounter in history.Reverse())
            {
                DrawDamageMeterBrowserRailItem(
                    encounter.EncounterNumber.ToString(),
                    encounter.EncounterNumber,
                    selectedDamageEncounterNumber == encounter.EncounterNumber,
                    $"Encounter {encounter.EncounterNumber}: {encounter.TerritoryName}");
            }

            return;
        }

        var headerStart = ImGui.GetCursorPos();
        ImGui.TextColored(LeadUpGoldColor, "Encounters");
        var style = ImGui.GetStyle();
        var buttonWidth = (ImGui.GetFrameHeight() * 2.0f) + style.ItemSpacing.X;
        var buttonX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - buttonWidth - PullBrowserHeaderButtonInset;
        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX() + style.ItemSpacing.X, buttonX));
        ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
        if (history.Count == 0)
        {
            ImGui.BeginDisabled();
        }

        if (DrawTransparentIconButton("ClearDamageMeterEncounters", FontAwesomeIcon.Trash) &&
            ImGui.GetIO().KeyCtrl)
        {
            plugin.ClearRecordedDamageEncounters();
            selectedDamageEncounterNumber = -1;
        }

        if (history.Count == 0)
        {
            ImGui.EndDisabled();
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Ctrl+click to delete recorded damage encounters");
        }

        ImGui.SameLine(0.0f, style.ItemSpacing.X);
        if (DrawTransparentIconButton("CollapseDamageMeterBrowser", FontAwesomeIcon.ChevronLeft))
        {
            plugin.SetDamageMeterBrowserCollapsed(true);
        }

        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Collapse encounters");
        }

        ImGui.SetCursorPosY(headerStart.Y + ImGui.GetTextLineHeightWithSpacing());
        ImGui.Separator();
        if (ImGui.BeginChild("##DamageMeterEncounterRows", Vector2.Zero, false, OptionalScrollbarFlags))
        {
            DrawDamageMeterBrowserItem(
                "Example",
                "Redacted report preview",
                -1,
                selectedDamageEncounterNumber == -1);
            if (current is not null)
            {
                DrawDamageMeterBrowserItem(
                    "Live encounter",
                    FormatDamageMeterDuration(current.DurationSeconds),
                    0,
                    selectedDamageEncounterNumber == 0);
            }

            foreach (var encounter in history.Reverse())
            {
                var detail = $"{FormatDamageMeterDuration(encounter.Snapshot.DurationSeconds)}  {FormatLocalClockTime(encounter.CapturedAtUtc)}";
                DrawDamageMeterBrowserItem(
                    $"Encounter {encounter.EncounterNumber}",
                    $"{encounter.TerritoryName}\n{detail}",
                    encounter.EncounterNumber,
                    selectedDamageEncounterNumber == encounter.EncounterNumber);
            }

            DrawReviewPaneBottomPadding();
        }

        ImGui.EndChild();
    }

    private void DrawDamageMeterBrowserRailItem(string label, long selection, bool selected, string tooltip)
    {
        if (ImGui.Selectable($"{label}##DamageMeterRail{selection}", selected, ImGuiSelectableFlags.None, new Vector2(0.0f, 30.0f)))
        {
            selectedDamageEncounterNumber = selection;
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(tooltip);
        }
    }

    private void DrawDamageMeterBrowserItem(
        string title,
        string detail,
        long selection,
        bool selected)
    {
        var height = detail.Contains('\n') ? 58.0f : 48.0f;
        var start = ImGui.GetCursorScreenPos();
        if (ImGui.Selectable($"##DamageMeterEncounter{selection}", selected, ImGuiSelectableFlags.None, new Vector2(0.0f, height)))
        {
            selectedDamageEncounterNumber = selection;
        }

        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(start + new Vector2(8.0f, 6.0f), ImGui.GetColorU32(selected ? ModernAccentColor : ModernTextColor), title);
        drawList.AddText(start + new Vector2(8.0f, 25.0f), ImGui.GetColorU32(ModernMutedTextColor), detail);
        if (hovered)
        {
            SetThemedTooltip($"{title}\n{detail}");
        }

        ImGui.Dummy(new Vector2(1.0f, 3.0f));
    }

    private void DrawDamageMeterPreview()
    {
        var (snapshot, label) = GetSelectedDamageMeterPreview();
        ImGui.TextColored(ModernAccentColor, "Preview");
        ImGui.TextColored(ModernMutedTextColor, label);
        if (selectedDamageEncounterNumber == -1)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("?");
            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip(
                    "The report provides the damage and hit details used here, but not its adjusted rDPS table. The example mirrors DPS in the rDPS column only to preview the layout. Live encounters calculate rDPS normally.");
            }
        }

        var previewHeight = MathF.Min(440.0f, MathF.Max(280.0f, ImGui.GetContentRegionAvail().Y));
        var theme = BetterDeathsThemeCatalog.GetTheme(configuration);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.BeginChild("##DamageMeterPreview", new Vector2(0.0f, previewHeight), false, OptionalScrollbarFlags))
        {
            var titleHeight = DrawWidgetPreviewBackground(theme, GetCurrentPullWidgetBackgroundOpacity());
            ImGui.SetCursorPos(new Vector2(0.0f, titleHeight));
            using (new ModernWidgetScope())
            {
                DrawDamageMeterWidgetSnapshot(snapshot, label, "Preview");
            }

            DrawWidgetPreviewChrome(theme, titleHeight, "Better Deaths DPS Meter");
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private (DamageEncounterSnapshot Snapshot, string Label) GetSelectedDamageMeterPreview()
    {
        if (selectedDamageEncounterNumber == -1)
        {
            return (DamageMeterPreviewData.Create(), "Dancing Mad | redacted report example");
        }

        var current = plugin.CurrentDamageEncounter;
        if (selectedDamageEncounterNumber == 0 && current is not null)
        {
            return (current, "Live encounter");
        }

        var history = plugin.RecordedDamageEncounters;
        var recorded = selectedDamageEncounterNumber > 0
            ? history.FirstOrDefault(encounter => encounter.EncounterNumber == selectedDamageEncounterNumber)
            : history.LastOrDefault();
        if (recorded is null)
        {
            selectedDamageEncounterNumber = -1;
            return (DamageMeterPreviewData.Create(), "Dancing Mad | redacted report example");
        }

        selectedDamageEncounterNumber = recorded.EncounterNumber;
        return (recorded.Snapshot, $"Encounter {recorded.EncounterNumber} | {recorded.TerritoryName}");
    }

    internal void DrawDamageMeterWidgetContent()
    {
        ApplyConfiguredTheme();
        using var widgetStyle = new ModernWidgetScope();

        var current = plugin.CurrentDamageEncounter;
        var last = plugin.LastDamageEncounter ?? plugin.RecordedDamageEncounters.LastOrDefault()?.Snapshot;
        var snapshot = current ?? last;
        if (snapshot is null)
        {
            using (new ImGuiIndentScope(ReviewPaneHorizontalPadding))
            {
                DrawModernWidgetTitle("Waiting for combat");
                ImGui.Spacing();
                ImGui.TextDisabled("No damage recorded yet.");
            }

            return;
        }

        DrawDamageMeterWidgetSnapshot(
            snapshot,
            ReferenceEquals(snapshot, current) ? "Live" : "Last encounter",
            "LiveWidget");
    }

    private void DrawDamageMeterWidgetSnapshot(
        DamageEncounterSnapshot snapshot,
        string state,
        string idSuffix)
    {
        var sources = GetVisibleDamageMeterSources(snapshot);
        var visibleTotal = sources.Aggregate(0UL, (total, source) => total + source.TotalDamage);
        using (new ImGuiIndentScope(ReviewPaneHorizontalPadding))
        {
            var visibleDps = snapshot.DurationSeconds > 0.0
                ? visibleTotal / snapshot.DurationSeconds
                : 0.0;
            var scope = configuration.DamageMeterShowAllCombatants ? "All" : "Party";
            var title = configuration.DamageMeterWidgetDisplayMode == WidgetDisplayMode.Concise
                ? $"{state} | {FormatDamageMeterDuration(snapshot.DurationSeconds)} | {FormatDamageMeterNumber(visibleDps)} DPS"
                : $"{state} | {scope} | {FormatDamageMeterDuration(snapshot.DurationSeconds)} | DPS {FormatDamageMeterNumber(visibleDps)}";
            DrawModernWidgetTitle(title);
            ImGui.Spacing();
        }

        if (ImGui.BeginChild($"##DamageMeterWidgetScroll{idSuffix}", Vector2.Zero, false, OptionalScrollbarFlags))
        {
            DrawDamageMeterWidgetTable(snapshot, sources, visibleTotal, idSuffix);
            DrawReviewPaneBottomPadding();
        }

        ImGui.EndChild();
    }

    private List<DamageSourceSummary> GetVisibleDamageMeterSources(DamageEncounterSnapshot snapshot)
    {
        return snapshot.Sources
            .Where(source => configuration.DamageMeterShowAllCombatants ||
                source.Source.IsPartyMember ||
                source.Source.IsLimitBreak)
            .OrderByDescending(source => source.TotalDamage)
            .ThenBy(source => source.Source.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void DrawDamageMeterWidgetTable(
        DamageEncounterSnapshot snapshot,
        IReadOnlyList<DamageSourceSummary> sources,
        ulong visibleTotal,
        string idSuffix)
    {
        if (sources.Count == 0)
        {
            ImGui.TextDisabled(configuration.DamageMeterShowAllCombatants
                ? "No damage sources were recorded."
                : "No party damage was recorded.");
            return;
        }

        var columns = DamageMeterColumnPolicy.Normalize(configuration.DamageMeterColumns);
        const ImGuiTableFlags flags = ImGuiTableFlags.SizingFixedFit |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.ScrollX;
        if (!ImGui.BeginTable($"##DamageMeterWidgetRows{idSuffix}", columns.Count + 1, flags))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableSetupColumn("##Expand", ImGuiTableColumnFlags.WidthFixed, 30.0f);
        foreach (var column in columns)
        {
            ImGui.TableSetupColumn(
                $"{GetDamageMeterColumnHeader(column)}##DamageMeterWidget{idSuffix}{column}",
                ImGuiTableColumnFlags.WidthFixed,
                GetDamageMeterColumnWidth(column));
        }

        DrawCenteredTableHeader(["", .. columns.Select(GetDamageMeterColumnHeader)]);

        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var sourceKey = GetDamageMeterSourceKey(source.Source);
            var expansionKey = $"{idSuffix}:{sourceKey}";
            var expanded = expandedDamageMeterSources.Contains(expansionKey);
            DrawDamageMeterWidgetSourceRow(
                snapshot,
                source,
                visibleTotal,
                index + 1,
                expansionKey,
                expanded,
                columns);
            if (!expanded)
            {
                continue;
            }

            foreach (var action in source.Actions)
            {
                DrawDamageMeterWidgetActionRow(snapshot, action, source.TotalDamage, columns);
            }
        }

        ImGui.EndTable();
    }

    private void DrawDamageMeterWidgetSourceRow(
        DamageEncounterSnapshot snapshot,
        DamageSourceSummary source,
        ulong visibleTotal,
        int rank,
        string expansionKey,
        bool expanded,
        IReadOnlyList<DamageMeterColumn> columns)
    {
        const float iconSize = 20.0f;
        ImGui.TableNextRow(ImGuiTableRowFlags.None, iconSize + 8.0f);
        ImGui.TableNextColumn();
        var expandIcon = expanded ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight;
        if (DrawTransparentIconButton($"DamageMeterWidgetExpand{expansionKey}", expandIcon))
        {
            if (!expandedDamageMeterSources.Remove(expansionKey))
            {
                expandedDamageMeterSources.Add(expansionKey);
            }
        }

        foreach (var column in columns)
        {
            ImGui.TableNextColumn();
            DrawDamageMeterSourceColumn(snapshot, source, visibleTotal, rank, column, iconSize);
        }
    }

    private static void DrawDamageMeterWidgetActionRow(
        DamageEncounterSnapshot snapshot,
        DamageActionSummary action,
        ulong sourceTotal,
        IReadOnlyList<DamageMeterColumn> columns)
    {
        const float iconSize = 20.0f;
        ImGui.TableNextRow(ImGuiTableRowFlags.None, iconSize + 6.0f);
        ImGui.TableNextColumn();

        foreach (var column in columns)
        {
            ImGui.TableNextColumn();
            DrawDamageMeterActionColumn(snapshot, action, sourceTotal, column, iconSize);
        }
    }

    private void DrawDamageMeterSourceColumn(
        DamageEncounterSnapshot snapshot,
        DamageSourceSummary source,
        ulong visibleTotal,
        int rank,
        DamageMeterColumn column,
        float iconSize)
    {
        var directDamageHits = Math.Max(0, source.Hits - source.PeriodicHits);
        switch (column)
        {
            case DamageMeterColumn.Rank:
                DrawCenteredText(rank.ToString());
                break;
            case DamageMeterColumn.JobIcon:
                var iconId = GetClassJobIconId(source.Source.ClassJobId);
                if (iconId != 0)
                {
                    CenterNextItem(iconSize);
                    DrawGameIcon(iconId, iconSize, source.Source.Name);
                }

                break;
            case DamageMeterColumn.PlayerName:
                var displayName = source.Source.IsPlayer
                    ? FormatKnownPlayerName(source.Source.Name)
                    : source.Source.Name;
                ImGui.TextUnformatted(
                    configuration.DamageMeterWidgetDisplayMode == WidgetDisplayMode.Concise && source.Source.IsPlayer
                        ? FormatPlayerInitials(displayName)
                        : displayName);
                break;
            case DamageMeterColumn.DamagePercent:
                DrawDamageMeterShareBar(source.TotalDamage, visibleTotal);
                break;
            case DamageMeterColumn.DamagePerSecond:
                DrawCenteredText(FormatDamageMeterNumber(
                    snapshot.DurationSeconds > 0.0 ? source.TotalDamage / snapshot.DurationSeconds : 0.0));
                break;
            case DamageMeterColumn.RaidDamagePerSecond:
                DrawCenteredText(FormatDamageMeterNumber(
                    snapshot.DurationSeconds > 0.0 ? source.RaidAdjustedDamage / snapshot.DurationSeconds : 0.0));
                DrawRaidDamageTooltip(source);
                break;
            case DamageMeterColumn.CriticalHitPercent:
                DrawDamageMeterHitPercent(source.CriticalHits, directDamageHits, "critical hits");
                break;
            case DamageMeterColumn.DirectHitPercent:
                DrawDamageMeterHitPercent(source.DirectHits, directDamageHits, "direct hits");
                break;
            case DamageMeterColumn.CriticalDirectHitPercent:
                DrawDamageMeterHitPercent(source.CriticalDirectHits, directDamageHits, "critical direct hits");
                break;
            case DamageMeterColumn.MaxHitAmount:
                DrawCenteredText(source.MaxHitAmount == 0 ? "-" : FormatAmount(source.MaxHitAmount));
                break;
            case DamageMeterColumn.MaxHitName:
                ImGui.TextUnformatted(string.IsNullOrWhiteSpace(source.MaxHitActionName)
                    ? "-"
                    : source.MaxHitActionName);
                break;
            case DamageMeterColumn.TotalDamage:
                DrawCenteredText(FormatAmount(source.TotalDamage));
                break;
            case DamageMeterColumn.Deaths:
                DrawCenteredText(source.Deaths == 0 ? "-" : source.Deaths.ToString("N0"));
                break;
            case DamageMeterColumn.HitCount:
                DrawCenteredText(source.Hits == 0 ? "-" : source.Hits.ToString("N0"));
                break;
        }
    }

    private static void DrawDamageMeterActionColumn(
        DamageEncounterSnapshot snapshot,
        DamageActionSummary action,
        ulong sourceTotal,
        DamageMeterColumn column,
        float iconSize)
    {
        var directDamageHits = Math.Max(0, action.Hits - action.PeriodicHits);
        var muted = ModernMutedTextColor;
        switch (column)
        {
            case DamageMeterColumn.Rank:
            case DamageMeterColumn.Deaths:
                DrawCenteredText("-", muted);
                break;
            case DamageMeterColumn.JobIcon:
                var iconId = action.PeriodicDamage == action.TotalDamage && action.ActionId != 0
                    ? GetStatusIconId(action.ActionId)
                    : GetActionIconId(action.ActionId);
                if (iconId != 0)
                {
                    CenterNextItem(iconSize);
                    DrawGameIcon(iconId, iconSize, action.ActionName);
                }

                break;
            case DamageMeterColumn.PlayerName:
                ImGui.TextColored(muted, action.ActionName);
                break;
            case DamageMeterColumn.DamagePercent:
                DrawCenteredText(
                    sourceTotal == 0 ? "-" : $"{action.TotalDamage * 100.0 / sourceTotal:F1}%",
                    muted);
                break;
            case DamageMeterColumn.DamagePerSecond:
                DrawCenteredText(FormatDamageMeterNumber(
                    snapshot.DurationSeconds > 0.0 ? action.TotalDamage / snapshot.DurationSeconds : 0.0), muted);
                break;
            case DamageMeterColumn.RaidDamagePerSecond:
                DrawCenteredText("-", muted);
                if (ImGui.IsItemHovered())
                {
                    SetThemedTooltip(
                        "rDPS is shown on player rows because raid-buff contribution comes from damage dealt by the whole party.");
                }

                break;
            case DamageMeterColumn.CriticalHitPercent:
                DrawDamageMeterHitPercent(action.CriticalHits, directDamageHits, "critical hits", muted);
                break;
            case DamageMeterColumn.DirectHitPercent:
                DrawDamageMeterHitPercent(action.DirectHits, directDamageHits, "direct hits", muted);
                break;
            case DamageMeterColumn.CriticalDirectHitPercent:
                DrawDamageMeterHitPercent(action.CriticalDirectHits, directDamageHits, "critical direct hits", muted);
                break;
            case DamageMeterColumn.MaxHitAmount:
                DrawCenteredText(action.MaxHitAmount == 0 ? "-" : FormatAmount(action.MaxHitAmount), muted);
                break;
            case DamageMeterColumn.MaxHitName:
                ImGui.TextColored(muted, action.ActionName);
                break;
            case DamageMeterColumn.TotalDamage:
                DrawCenteredText(FormatAmount(action.TotalDamage), muted);
                break;
            case DamageMeterColumn.HitCount:
                DrawCenteredText(action.Hits == 0 ? "-" : action.Hits.ToString("N0"), muted);
                break;
        }
    }

    private static void DrawDamageMeterHitPercent(
        int count,
        int eligibleHits,
        string label,
        Vector4? color = null)
    {
        DrawCenteredText(
            eligibleHits == 0 ? "-" : $"{count * 100.0 / eligibleHits:F1}%",
            color ?? ModernTextColor);
        if (ImGui.IsItemHovered() && eligibleHits > 0)
        {
            SetThemedTooltip($"{count:N0} {label} out of {eligibleHits:N0} eligible hits.");
        }
    }

    private string GetDamageMeterColumnHeader(DamageMeterColumn column)
    {
        if (configuration.DamageMeterWidgetDisplayMode == WidgetDisplayMode.Concise)
        {
            return column switch
            {
                DamageMeterColumn.PlayerName => "Name",
                DamageMeterColumn.DamagePercent => "Share",
                DamageMeterColumn.CriticalHitPercent => "Crit",
                DamageMeterColumn.DirectHitPercent => "DH",
                DamageMeterColumn.CriticalDirectHitPercent => "CDH",
                DamageMeterColumn.MaxHitAmount => "Max",
                DamageMeterColumn.MaxHitName => "Max skill",
                DamageMeterColumn.TotalDamage => "Damage",
                DamageMeterColumn.HitCount => "Hits",
                _ => GetDamageMeterColumnLabel(column),
            };
        }

        return column switch
        {
            DamageMeterColumn.JobIcon => "Job",
            DamageMeterColumn.CriticalHitPercent => "Crit %",
            DamageMeterColumn.DirectHitPercent => "Direct %",
            DamageMeterColumn.CriticalDirectHitPercent => "Crit + direct %",
            _ => GetDamageMeterColumnLabel(column),
        };
    }

    private float GetDamageMeterColumnWidth(DamageMeterColumn column)
    {
        var concise = configuration.DamageMeterWidgetDisplayMode == WidgetDisplayMode.Concise;
        return column switch
        {
            DamageMeterColumn.Rank => concise ? 34.0f : 44.0f,
            DamageMeterColumn.JobIcon => concise ? 38.0f : 44.0f,
            DamageMeterColumn.PlayerName => concise ? 92.0f : 145.0f,
            DamageMeterColumn.DamagePercent => concise ? 82.0f : 94.0f,
            DamageMeterColumn.DamagePerSecond => concise ? 70.0f : 84.0f,
            DamageMeterColumn.RaidDamagePerSecond => concise ? 70.0f : 84.0f,
            DamageMeterColumn.CriticalHitPercent => concise ? 66.0f : 86.0f,
            DamageMeterColumn.DirectHitPercent => concise ? 62.0f : 90.0f,
            DamageMeterColumn.CriticalDirectHitPercent => concise ? 66.0f : 116.0f,
            DamageMeterColumn.MaxHitAmount => concise ? 78.0f : 94.0f,
            DamageMeterColumn.MaxHitName => concise ? 110.0f : 145.0f,
            DamageMeterColumn.TotalDamage => concise ? 86.0f : 102.0f,
            DamageMeterColumn.Deaths => concise ? 50.0f : 62.0f,
            DamageMeterColumn.HitCount => concise ? 54.0f : 62.0f,
            _ => 90.0f,
        };
    }

    private static void DrawDamageMeterShareBar(ulong damage, ulong total)
    {
        var fraction = total == 0 ? 0.0f : (float)Math.Clamp(damage / (double)total, 0.0, 1.0);
        var label = total == 0 ? "-" : $"{damage * 100.0 / total:F1}%";
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ModernFrameColor);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, ModernAccentColor);
        ImGui.PushStyleColor(ImGuiCol.Text, GetReadableTextColorForBackground(
            fraction >= 0.45f ? ModernAccentColor : ModernFrameColor));
        ImGui.ProgressBar(fraction, new Vector2(-1.0f, ImGui.GetFrameHeight()), label);
        ImGui.PopStyleColor(3);
    }

    private static void DrawRaidDamageTooltip(DamageSourceSummary source)
    {
        if (!ImGui.IsItemHovered())
        {
            return;
        }

        SetThemedTooltip(
            "Raid-contributing DPS moves damage gained from another player's raid buffs back to the player who provided them.\n" +
            $"Received from others: {FormatDamageMeterNumber(source.ExternalBuffDamageReceived)} damage\n" +
            $"Given through buffs: {FormatDamageMeterNumber(source.RaidBuffDamageGiven)} damage");
    }

    private static string GetDamageMeterSourceKey(DamageActorIdentity source)
    {
        return source.EntityId != 0
            ? source.EntityId.ToString("X8")
            : source.Name;
    }

    private static string FormatDamageMeterDuration(double durationSeconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0.0, durationSeconds));
        return duration.TotalHours >= 1.0
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string FormatDamageMeterNumber(double value)
    {
        return value > 0.0 ? value.ToString("N0") : "-";
    }
}
