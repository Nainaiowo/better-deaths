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
    private readonly HashSet<string> expandedDamageMeterSources = new(StringComparer.Ordinal);

    private void DrawDamageMeterPage()
    {
        DrawReviewPanel("##DamageMeter", Vector2.Zero, DrawDamageMeterContent);
    }

    private void DrawDamageMeterContent()
    {
        var current = plugin.CurrentDamageEncounter;
        var last = plugin.LastDamageEncounter;
        var useLast = last is not null && (configuration.DamageMeterShowLastEncounter || current is null);
        var snapshot = useLast ? last ?? current : current ?? last;

        DrawDamageMeterHeader(current is not null, last is not null, useLast);
        ImGui.Dummy(new Vector2(1.0f, 4.0f));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(1.0f, 4.0f));

        if (snapshot is null)
        {
            ImGui.TextDisabled("No damage recorded yet.");
            return;
        }

        var sources = GetVisibleDamageMeterSources(snapshot);
        var visibleTotal = sources.Aggregate(0UL, (total, source) => total + source.TotalDamage);
        DrawDamageMeterSummary(snapshot, sources, visibleTotal, useLast);
        ImGui.Dummy(new Vector2(1.0f, 7.0f));

        if (sources.Count == 0)
        {
            ImGui.TextDisabled(configuration.DamageMeterShowAllCombatants
                ? "No damage sources were recorded."
                : "No party damage was recorded.");
            return;
        }

        DrawDamageMeterTable(snapshot, sources, visibleTotal);
    }

    private void DrawDamageMeterHeader(bool hasCurrent, bool hasLast, bool useLast)
    {
        ImGui.TextColored(LeadUpGoldColor, "Damage Meter");
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine();

        var liveWidth = GetThemedActionButtonWidth("Live");
        if (DrawThemedToggleButton("Live", "DamageMeterLive", !useLast, liveWidth) && hasCurrent)
        {
            configuration.DamageMeterShowLastEncounter = false;
            plugin.SaveConfiguration();
        }

        if (!hasCurrent && ImGui.IsItemHovered())
        {
            SetThemedTooltip("No active encounter is available.");
        }

        ImGui.SameLine(0.0f, spacing);
        var lastWidth = GetThemedActionButtonWidth("Last pull");
        if (DrawThemedToggleButton("Last pull", "DamageMeterLast", useLast, lastWidth) && hasLast)
        {
            configuration.DamageMeterShowLastEncounter = true;
            plugin.SaveConfiguration();
        }

        if (!hasLast && ImGui.IsItemHovered())
        {
            SetThemedTooltip("No completed encounter is available.");
        }

        ImGui.SameLine(0.0f, spacing);
        var widgetWidth = GetThemedActionButtonWidth("Widget");
        if (DrawThemedToggleButton(
                "Widget",
                "DamageMeterWidget",
                configuration.ShowDamageMeterWidget,
                widgetWidth))
        {
            plugin.SetShowDamageMeterWidget(!configuration.ShowDamageMeterWidget);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Show or hide the compact damage meter window.");
        }

        var allCombatants = configuration.DamageMeterShowAllCombatants;
        var partyWidth = GetThemedActionButtonWidth("Party");
        var allWidth = GetThemedActionButtonWidth("All");
        var selectorWidth = partyWidth + spacing + allWidth;
        var selectorX = ImGui.GetWindowContentRegionMax().X - selectorWidth;
        if (selectorX > ImGui.GetCursorPosX() + spacing)
        {
            ImGui.SameLine(selectorX);
        }
        else
        {
            ImGui.NewLine();
        }

        if (DrawThemedToggleButton("Party", "DamageMeterParty", !allCombatants, partyWidth))
        {
            configuration.DamageMeterShowAllCombatants = false;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine(0.0f, spacing);
        if (DrawThemedToggleButton("All", "DamageMeterAll", allCombatants, allWidth))
        {
            configuration.DamageMeterShowAllCombatants = true;
            plugin.SaveConfiguration();
        }
    }

    internal void DrawDamageMeterWidgetContent()
    {
        ApplyConfiguredTheme();
        using var widgetStyle = new ModernWidgetScope();

        var current = plugin.CurrentDamageEncounter;
        var snapshot = current ?? plugin.LastDamageEncounter;
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
            var state = current is null ? "Last pull" : "Live";
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

        const ImGuiTableFlags flags = ImGuiTableFlags.SizingStretchProp |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.BordersInnerV;
        if (!ImGui.BeginTable("##DamageMeterWidgetRows", 5, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 30.0f);
        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 2.0f);
        ImGui.TableSetupColumn("Damage", ImGuiTableColumnFlags.WidthStretch, 1.05f);
        ImGui.TableSetupColumn("DPS", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Share", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        DrawCenteredTableHeader("#", "Player", "Damage", "DPS", "Share");

        for (var index = 0; index < sources.Count; index++)
        {
            DrawDamageMeterWidgetRow(snapshot, sources[index], visibleTotal, index + 1);
        }

        ImGui.EndTable();
    }

    private void DrawDamageMeterWidgetRow(
        DamageEncounterSnapshot snapshot,
        DamageSourceSummary source,
        ulong visibleTotal,
        int rank)
    {
        const float iconSize = 20.0f;
        ImGui.TableNextRow(ImGuiTableRowFlags.None, iconSize + 8.0f);
        ImGui.TableNextColumn();
        DrawCenteredText(rank.ToString());

        ImGui.TableNextColumn();
        var iconId = GetClassJobIconId(source.Source.ClassJobId);
        if (iconId != 0)
        {
            DrawGameIcon(iconId, iconSize, source.Source.Name);
            ImGui.SameLine();
        }

        var displayName = source.Source.IsPlayer
            ? FormatKnownPlayerName(source.Source.Name)
            : source.Source.Name;
        ImGui.TextUnformatted(displayName);

        ImGui.TableNextColumn();
        DrawCenteredText(FormatAmount(source.TotalDamage));
        ImGui.TableNextColumn();
        DrawCenteredText(FormatDamageMeterNumber(
            snapshot.DurationSeconds > 0.0 ? source.TotalDamage / snapshot.DurationSeconds : 0.0));
        ImGui.TableNextColumn();
        DrawDamageMeterShareBar(source.TotalDamage, visibleTotal);
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
        if (!ImGui.BeginTable("##DamageMeterRows", 7, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 34.0f);
        ImGui.TableSetupColumn("Player / ability", ImGuiTableColumnFlags.WidthStretch, 2.7f);
        ImGui.TableSetupColumn("Damage", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("DPS", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Share", ImGuiTableColumnFlags.WidthStretch, 0.85f);
        ImGui.TableSetupColumn("Crit", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Direct", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        DrawCenteredTableHeader("#", "Player / ability", "Damage", "DPS", "Share", "Crit", "Direct");

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
