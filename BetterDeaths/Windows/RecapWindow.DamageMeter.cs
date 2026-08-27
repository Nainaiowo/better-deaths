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
    private const float DamageMeterIconSize = 24.0f;
    private const string DamageMeterColumnDragPayload = "BETTER_DEATHS_METER_COLUMN";
    private static readonly DamageMeterColumn[] AvailableDamageMeterColumns = Enum.GetValues<DamageMeterColumn>();
    private readonly HashSet<string> expandedDamageMeterSources = new(StringComparer.Ordinal);
    private DamageMeterColumn? draggingDamageMeterColumn;

    private void DrawDamageMeterPage()
    {
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
    }

    private void DrawDamageMeterWidgetSettings()
    {
        ImGui.TextColored(ModernAccentColor, "Widget");
        var showWidget = configuration.ShowDamageMeterWidget;
        if (DrawThemedSwitch("Show DPS meter widget", "DamageMeterWidgetVisible", ref showWidget))
        {
            plugin.SetShowDamageMeterWidget(showWidget);
        }
    }

    private void DrawDamageMeterDataSettings()
    {
        ImGui.TextColored(ModernAccentColor, "Data");
        ImGui.TextUnformatted("Encounter");
        ImGui.SameLine();
        var liveWidth = GetThemedActionButtonWidth("Live");
        if (DrawThemedToggleButton(
                "Live",
                "DamageMeterLive",
                !configuration.DamageMeterShowLastEncounter,
                liveWidth))
        {
            configuration.DamageMeterShowLastEncounter = false;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var lastWidth = GetThemedActionButtonWidth("Last pull");
        if (DrawThemedToggleButton(
                "Last pull",
                "DamageMeterLast",
                configuration.DamageMeterShowLastEncounter,
                lastWidth))
        {
            configuration.DamageMeterShowLastEncounter = true;
            plugin.SaveConfiguration();
        }

        ImGui.Dummy(new Vector2(1.0f, 3.0f));
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

    private void DrawDamageMeterColumnSettings()
    {
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            draggingDamageMeterColumn = null;
        }

        configuration.DamageMeterColumns = DamageMeterColumnPolicy.Normalize(configuration.DamageMeterColumns);
        ImGui.TextColored(ModernAccentColor, "Widget columns");
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

            if (DrawDamageMeterColumnTile(column, columns.Count > 1))
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
        var addLabel = $"{FontAwesomeIcon.Plus.ToIconString()}  Add column";
        var addWidth = GetThemedActionButtonWidth(addLabel);
        if (DrawThemedActionButton(addLabel, "DamageMeterAddColumn", addWidth))
        {
            ImGui.OpenPopup("##DamageMeterAddColumnPopup");
        }

        DrawDamageMeterAddColumnPopup();
        ImGui.SameLine();
        var resetLabel = $"{FontAwesomeIcon.Undo.ToIconString()}  Reset";
        var resetWidth = GetThemedActionButtonWidth(resetLabel);
        if (DrawThemedActionButton(resetLabel, "DamageMeterResetColumns", resetWidth))
        {
            configuration.DamageMeterColumns = DamageMeterColumnPolicy.CreateDefault();
            draggingDamageMeterColumn = null;
            plugin.SaveConfiguration();
        }
    }

    private bool DrawDamageMeterColumnTile(DamageMeterColumn column, bool canRemove)
    {
        var label = GetDamageMeterColumnLabel(column);
        var size = new Vector2(GetDamageMeterColumnTileWidth(column), ImGui.GetFrameHeight() + 6.0f);
        var start = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##DamageMeterColumn{column}", size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var end = start + size;
        var closeSize = size.Y;
        var closeStart = new Vector2(end.X - closeSize, start.Y);
        var mouse = ImGui.GetIO().MousePos;
        var closeHovered = hovered && mouse.X >= closeStart.X && mouse.X <= end.X &&
            mouse.Y >= start.Y && mouse.Y <= end.Y;

        var drawList = ImGui.GetWindowDrawList();
        var fill = active
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
        var close = FontAwesomeIcon.Times.ToIconString();
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
            ImGui.GetColorU32(canRemove
                ? ModernMutedTextColor
                : ModernMutedTextColor with { W = 0.45f }),
            close);

        if (hovered)
        {
            ImGui.SetMouseCursor(closeHovered ? ImGuiMouseCursor.Hand : ImGuiMouseCursor.ResizeAll);
        }

        if (closeHovered && !canRemove)
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
            if (!payload.IsNull && draggingDamageMeterColumn is { } source &&
                DamageMeterColumnPolicy.Move(configuration.DamageMeterColumns, source, column))
            {
                draggingDamageMeterColumn = null;
                plugin.SaveConfiguration();
            }

            ImGui.EndDragDropTarget();
        }

        return clicked && closeHovered && canRemove;
    }

    private void DrawDamageMeterAddColumnPopup()
    {
        if (!ImGui.BeginPopup("##DamageMeterAddColumnPopup"))
        {
            return;
        }

        ImGui.TextColored(LeadUpGoldColor, "Add column");
        ImGui.Separator();
        var addedAny = false;
        foreach (var column in AvailableDamageMeterColumns.Where(column =>
                     !configuration.DamageMeterColumns.Contains(column)))
        {
            if (!ImGui.Selectable($"{GetDamageMeterColumnLabel(column)}##AddDamageMeterColumn{column}"))
            {
                continue;
            }

            configuration.DamageMeterColumns.Add(column);
            plugin.SaveConfiguration();
            addedAny = true;
        }

        if (!addedAny && configuration.DamageMeterColumns.Count == AvailableDamageMeterColumns.Length)
        {
            ImGui.TextDisabled("All columns are active.");
        }

        ImGui.EndPopup();
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

    internal void DrawDamageMeterWidgetContent()
    {
        ApplyConfiguredTheme();
        using var widgetStyle = new ModernWidgetScope();

        var current = plugin.CurrentDamageEncounter;
        var last = plugin.LastDamageEncounter;
        var useLast = configuration.DamageMeterShowLastEncounter && last is not null;
        var snapshot = useLast ? last : current ?? last;
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

        var sources = GetVisibleDamageMeterSources(snapshot);
        var visibleTotal = sources.Aggregate(0UL, (total, source) => total + source.TotalDamage);
        using (new ImGuiIndentScope(ReviewPaneHorizontalPadding))
        {
            var visibleDps = snapshot.DurationSeconds > 0.0
                ? visibleTotal / snapshot.DurationSeconds
                : 0.0;
            var scope = configuration.DamageMeterShowAllCombatants ? "All" : "Party";
            var state = ReferenceEquals(snapshot, current) ? "Live" : "Last pull";
            DrawModernWidgetTitle(
                $"{state} | {scope} | {FormatDamageMeterDuration(snapshot.DurationSeconds)} | DPS {FormatDamageMeterNumber(visibleDps)}");
            ImGui.Spacing();
        }

        if (ImGui.BeginChild("##DamageMeterWidgetScroll", Vector2.Zero, false, OptionalScrollbarFlags))
        {
            DrawDamageMeterWidgetTable(snapshot, sources, visibleTotal);
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
        ulong visibleTotal)
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
        if (!ImGui.BeginTable("##DamageMeterWidgetRows", columns.Count + 1, flags))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableSetupColumn("##Expand", ImGuiTableColumnFlags.WidthFixed, 30.0f);
        foreach (var column in columns)
        {
            ImGui.TableSetupColumn(
                $"{GetDamageMeterColumnHeader(column)}##DamageMeterWidget{column}",
                ImGuiTableColumnFlags.WidthFixed,
                GetDamageMeterColumnWidth(column));
        }

        DrawCenteredTableHeader(["", .. columns.Select(GetDamageMeterColumnHeader)]);

        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var sourceKey = GetDamageMeterSourceKey(source.Source);
            var expanded = expandedDamageMeterSources.Contains(sourceKey);
            DrawDamageMeterWidgetSourceRow(
                snapshot,
                source,
                visibleTotal,
                index + 1,
                sourceKey,
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
        string sourceKey,
        bool expanded,
        IReadOnlyList<DamageMeterColumn> columns)
    {
        const float iconSize = 20.0f;
        ImGui.TableNextRow(ImGuiTableRowFlags.None, iconSize + 8.0f);
        ImGui.TableNextColumn();
        var expandIcon = expanded ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight;
        if (DrawTransparentIconButton($"DamageMeterWidgetExpand{sourceKey}", expandIcon))
        {
            if (!expandedDamageMeterSources.Remove(sourceKey))
            {
                expandedDamageMeterSources.Add(sourceKey);
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
                ImGui.TextUnformatted(source.Source.IsPlayer
                    ? FormatKnownPlayerName(source.Source.Name)
                    : source.Source.Name);
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

    private static string GetDamageMeterColumnHeader(DamageMeterColumn column)
    {
        return column switch
        {
            DamageMeterColumn.JobIcon => "Job",
            DamageMeterColumn.CriticalHitPercent => "Crit %",
            DamageMeterColumn.DirectHitPercent => "Direct %",
            DamageMeterColumn.CriticalDirectHitPercent => "Crit + direct %",
            _ => GetDamageMeterColumnLabel(column),
        };
    }

    private static float GetDamageMeterColumnWidth(DamageMeterColumn column)
    {
        return column switch
        {
            DamageMeterColumn.Rank => 44.0f,
            DamageMeterColumn.JobIcon => 44.0f,
            DamageMeterColumn.PlayerName => 145.0f,
            DamageMeterColumn.DamagePercent => 94.0f,
            DamageMeterColumn.DamagePerSecond => 84.0f,
            DamageMeterColumn.RaidDamagePerSecond => 84.0f,
            DamageMeterColumn.CriticalHitPercent => 86.0f,
            DamageMeterColumn.DirectHitPercent => 90.0f,
            DamageMeterColumn.CriticalDirectHitPercent => 116.0f,
            DamageMeterColumn.MaxHitAmount => 94.0f,
            DamageMeterColumn.MaxHitName => 145.0f,
            DamageMeterColumn.TotalDamage => 102.0f,
            DamageMeterColumn.Deaths => 62.0f,
            DamageMeterColumn.HitCount => 62.0f,
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

    private static void DrawDamageMeterSummary(
        DamageEncounterSnapshot snapshot,
        IReadOnlyList<DamageSourceSummary> sources,
        ulong visibleTotal,
        bool useLast)
    {
        var duration = snapshot.DurationSeconds;
        var visibleDps = duration > 0.0 ? visibleTotal / duration : 0.0;
        ImGui.TextColored(ModernMutedTextColor, useLast ? "Last pull" : "Live");
        ImGui.SameLine();
        ImGui.TextUnformatted(FormatDamageMeterDuration(duration));
        ImGui.SameLine();
        ImGui.TextColored(ModernMutedTextColor, "Damage");
        ImGui.SameLine();
        ImGui.TextUnformatted(FormatAmount(visibleTotal));
        ImGui.SameLine();
        ImGui.TextColored(ModernMutedTextColor, "DPS");
        ImGui.SameLine();
        ImGui.TextUnformatted(FormatDamageMeterNumber(visibleDps));

        var estimatedDamage = sources.Aggregate(0UL, (total, source) => total + source.EstimatedDamage);
        var unattributedDamage = sources.Aggregate(0UL, (total, source) => total + source.UnattributedDamage);
        if (estimatedDamage == 0 && unattributedDamage == 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(HealColor, "Exact source totals");
            return;
        }

        ImGui.SameLine();
        var uncertain = estimatedDamage + unattributedDamage;
        var uncertainPercent = visibleTotal == 0 ? 0.0 : uncertain * 100.0 / visibleTotal;
        ImGui.TextColored(WarningColor, $"Estimated split {uncertainPercent:F1}%");
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(
                "The encounter total is exact. Ordinary DoTs arrive as one combined tick, so Better Deaths estimates the split between active DoTs. Damage with no reliable owner remains unattributed.");
        }
    }

    private void DrawDamageMeterTable(
        DamageEncounterSnapshot snapshot,
        IReadOnlyList<DamageSourceSummary> sources,
        ulong visibleTotal)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerV |
            ImGuiTableFlags.BordersOuterH;
        if (!ImGui.BeginTable("##DamageMeterRows", 8, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 34.0f);
        ImGui.TableSetupColumn("Player / ability", ImGuiTableColumnFlags.WidthStretch, 2.7f);
        ImGui.TableSetupColumn("Damage", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("DPS", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("rDPS", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Share", ImGuiTableColumnFlags.WidthStretch, 0.85f);
        ImGui.TableSetupColumn("Crit", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Direct", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        DrawCenteredTableHeader("#", "Player / ability", "Damage", "DPS", "rDPS", "Share", "Crit", "Direct");

        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var sourceKey = GetDamageMeterSourceKey(source.Source);
            var expanded = expandedDamageMeterSources.Contains(sourceKey);
            DrawDamageMeterSourceRow(snapshot, source, visibleTotal, index + 1, sourceKey, expanded);
            if (!expanded)
            {
                continue;
            }

            foreach (var action in source.Actions)
            {
                DrawDamageMeterActionRow(snapshot, action, source.TotalDamage);
            }
        }

        ImGui.EndTable();
    }

    private void DrawDamageMeterSourceRow(
        DamageEncounterSnapshot snapshot,
        DamageSourceSummary source,
        ulong visibleTotal,
        int rank,
        string sourceKey,
        bool expanded)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.None, DamageMeterIconSize + 8.0f);
        ImGui.TableNextColumn();
        DrawCenteredText(rank.ToString());

        ImGui.TableNextColumn();
        var expandIcon = expanded ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight;
        if (DrawTransparentIconButton($"DamageMeterExpand{sourceKey}", expandIcon))
        {
            if (!expandedDamageMeterSources.Remove(sourceKey))
            {
                expandedDamageMeterSources.Add(sourceKey);
            }
        }

        ImGui.SameLine();
        var iconId = GetClassJobIconId(source.Source.ClassJobId);
        if (iconId != 0)
        {
            DrawGameIcon(iconId, DamageMeterIconSize, source.Source.Name);
            ImGui.SameLine();
        }

        var displayName = source.Source.IsPlayer
            ? FormatKnownPlayerName(source.Source.Name)
            : source.Source.Name;
        ImGui.TextUnformatted(displayName);

        DrawDamageMeterMetricCells(
            source.TotalDamage,
            source.RaidAdjustedDamage,
            snapshot.DurationSeconds,
            visibleTotal,
            source.Hits,
            source.PeriodicHits,
            source.CriticalHits,
            source.DirectHits);
    }

    private static void DrawDamageMeterActionRow(
        DamageEncounterSnapshot snapshot,
        DamageActionSummary action,
        ulong sourceTotal)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextDisabled("-");

        ImGui.TableNextColumn();
        ImGui.Indent(30.0f);
        var iconId = action.PeriodicDamage == action.TotalDamage && action.ActionId != 0
            ? GetStatusIconId(action.ActionId)
            : GetActionIconId(action.ActionId);
        if (iconId != 0)
        {
            DrawGameIcon(iconId, 20.0f, action.ActionName);
            ImGui.SameLine();
        }

        ImGui.TextColored(ModernMutedTextColor, action.ActionName);
        ImGui.Unindent(30.0f);

        DrawDamageMeterMetricCells(
            action.TotalDamage,
            null,
            snapshot.DurationSeconds,
            sourceTotal,
            action.Hits,
            action.PeriodicHits,
            action.CriticalHits,
            action.DirectHits,
            muted: true);
    }

    private static void DrawDamageMeterMetricCells(
        ulong damage,
        double? raidAdjustedDamage,
        double durationSeconds,
        ulong shareTotal,
        int hits,
        int periodicHits,
        int criticalHits,
        int directHits,
        bool muted = false)
    {
        var color = muted ? ModernMutedTextColor : ModernTextColor;
        ImGui.TableNextColumn();
        DrawCenteredText(FormatAmount(damage), color);
        ImGui.TableNextColumn();
        DrawCenteredText(FormatDamageMeterNumber(durationSeconds > 0.0 ? damage / durationSeconds : 0.0), color);
        ImGui.TableNextColumn();
        DrawCenteredText(
            raidAdjustedDamage is null
                ? "-"
                : FormatDamageMeterNumber(durationSeconds > 0.0 ? raidAdjustedDamage.Value / durationSeconds : 0.0),
            color);
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(raidAdjustedDamage is null
                ? "rDPS is shown on player rows because raid-buff contribution comes from damage dealt by the whole party."
                : "Raid-contributing DPS moves damage gained from another player's raid buffs back to the player who provided them.");
        }

        ImGui.TableNextColumn();
        DrawCenteredText(shareTotal == 0 ? "-" : $"{damage * 100.0 / shareTotal:F1}%", color);

        var directDamageHits = Math.Max(0, hits - periodicHits);
        ImGui.TableNextColumn();
        DrawCenteredText(directDamageHits == 0 ? "-" : $"{criticalHits * 100.0 / directDamageHits:F1}%", color);
        if (ImGui.IsItemHovered() && directDamageHits > 0)
        {
            SetThemedTooltip($"{criticalHits:N0} critical hits out of {directDamageHits:N0} eligible hits.");
        }

        ImGui.TableNextColumn();
        DrawCenteredText(directDamageHits == 0 ? "-" : $"{directHits * 100.0 / directDamageHits:F1}%", color);
        if (ImGui.IsItemHovered() && directDamageHits > 0)
        {
            SetThemedTooltip($"{directHits:N0} direct hits out of {directDamageHits:N0} eligible hits.");
        }
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
