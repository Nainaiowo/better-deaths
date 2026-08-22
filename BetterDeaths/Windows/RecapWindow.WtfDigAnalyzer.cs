using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BetterDeaths.WtfDig;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private readonly WtfDigAnalyzerController wtfDigAnalyzer = new();
    private long? selectedWtfDigLocalPullNumber;
    private long? selectedWtfDigLocalPullCapturedAtTicks;
    private int selectedForsakenResolution;
    private int forsakenViewIndex = 1;
    private bool alignForsakenTowers = true;
    private bool showForsakenCleaveSnapshot = true;
    private int selectedArrowWave = -1;
    private int arrowStrategyIndex;
    private bool showArrowStarts = true;
    private int selectedBlackHoleTether;
    private bool blackHoleSupportFirst;
    private bool showBlackHoleBosses = true;
    private int wtfDigWaymarkPresetIndex;
    private object? displayedWtfDigAnalysis;

    private void DrawWtfDigAnalyzerPage()
    {
        DrawReviewPanel("##WtfDigAnalyzer", Vector2.Zero, DrawWtfDigAnalyzerContent);
    }

    private void DrawWtfDigAnalyzerContent()
    {
        var state = wtfDigAnalyzer.Snapshot();
        if (state.Analysis is not null && !ReferenceEquals(displayedWtfDigAnalysis, state.Analysis))
        {
            displayedWtfDigAnalysis = state.Analysis;
            selectedForsakenResolution = 0;
            forsakenViewIndex = 1;
            selectedArrowWave = -1;
            selectedBlackHoleTether = 0;
        }
        ImGui.TextColored(ModernAccentColor, "WTF.DIG Analyzer");
        ImGui.Dummy(new Vector2(1.0f, 5.0f));

        DrawWtfDigSourceSelector(state);
        state = wtfDigAnalyzer.Snapshot();
        ImGui.Dummy(new Vector2(1.0f, 5.0f));
        DrawWtfDigAnalyzerSelector(state);
        if (state.Analyzer.Key != "kefka-says")
        {
            ImGui.Dummy(new Vector2(1.0f, 4.0f));
            DrawWtfDigWaymarkSelector();
        }

        ImGui.Dummy(new Vector2(1.0f, 5.0f));
        if (state.Source == WtfDigAnalyzerSource.LocalPull)
        {
            DrawWtfDigLocalPullInput(state);
        }
        else
        {
            DrawWtfDigReportInput(state);
        }

        state = wtfDigAnalyzer.Snapshot();

        if (!string.IsNullOrWhiteSpace(state.Error))
        {
            ImGui.Dummy(new Vector2(1.0f, 4.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, DamageColor);
            ImGui.TextWrapped(state.Error);
            ImGui.PopStyleColor();
        }

        if (state.Report is not null)
        {
            ImGui.Dummy(new Vector2(1.0f, 8.0f));
            DrawWtfDigReportBar(state);
        }

        if (state.Loading)
        {
            ImGui.Dummy(new Vector2(1.0f, 8.0f));
            var loadingText = state.Source == WtfDigAnalyzerSource.LocalPull
                ? state.Analysis is null ? "Analyzing Better Deaths pull..." : "Updating analysis..."
                : state.Analysis is null ? "Loading FFLogs data..." : "Updating analysis...";
            ImGui.TextColored(LeadUpGoldColor, loadingText);
        }

        if (state.Analysis is ForsakenAnalysis forsaken && forsaken.Resolutions.Count > 0)
        {
            ImGui.Dummy(new Vector2(1.0f, 10.0f));
            DrawForsakenAnalysis(forsaken);
        }
        else if (state.Analysis is ArrowsAnalysis arrows && arrows.Waves.Count > 0)
        {
            ImGui.Dummy(new Vector2(1.0f, 10.0f));
            DrawArrowsAnalysis(arrows);
        }
        else if (state.Analysis is LimitCutAnalysis limitCut && limitCut.Kefka is not null)
        {
            ImGui.Dummy(new Vector2(1.0f, 10.0f));
            DrawLimitCutAnalysis(limitCut, state.Source);
        }
        else if (state.Analysis is BlackHoleAnalysis blackHole && blackHole.Tethers.Count > 0)
        {
            ImGui.Dummy(new Vector2(1.0f, 10.0f));
            DrawBlackHoleAnalysis(blackHole);
        }
        else if (state.Analysis is P4Analysis p4 && p4.Rounds.Count > 0)
        {
            ImGui.Dummy(new Vector2(1.0f, 10.0f));
            DrawP4Analysis(p4);
        }

        ImGui.Dummy(new Vector2(1.0f, 12.0f));
        ImGui.Separator();
        DrawWtfDigMutedWrapped("Analyzer logic provided with WTF.DIG by mczub.");
        if (state.Source == WtfDigAnalyzerSource.LocalPull)
        {
            DrawWtfDigMutedWrapped("This analysis uses the fight data already recorded by Better Deaths and stays on your computer.");
            DrawWtfDigMutedWrapped("Replay snapshots are estimates. Use a recording as the source of truth when one is available.");
        }
        else
        {
            DrawWtfDigMutedWrapped("Analyzing sends the FFLogs report code to WTF.DIG's service, which retrieves the requested log data from FFLogs.");
            DrawWtfDigMutedWrapped("FFLogs position snapshots are estimates. Use the original log and recording as the source of truth.");
        }
    }

    private static void DrawWtfDigMutedWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ModernMutedTextColor);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private void DrawWtfDigWaymarkSelector()
    {
        ImGui.TextDisabled("Waymarks");
        ImGui.SameLine();
        foreach (var (label, index) in new[] { ("12y Diamond", 0), ("DN/Zenith", 1) })
        {
            if (index > 0)
            {
                ImGui.SameLine();
            }

            if (DrawThemedToggleButton(label, $"WtfDigWaymarks{index}", wtfDigWaymarkPresetIndex == index))
            {
                wtfDigWaymarkPresetIndex = index;
            }
        }
    }

    private void DrawWtfDigSourceSelector(WtfDigAnalyzerViewState state)
    {
        ImGui.TextDisabled("Source");
        ImGui.SameLine();
        if (DrawThemedToggleButton(
                "Better Deaths Pull",
                "WtfDigSourceLocal",
                state.Source == WtfDigAnalyzerSource.LocalPull))
        {
            wtfDigAnalyzer.SetSource(WtfDigAnalyzerSource.LocalPull);
        }

        ImGui.SameLine();
        if (DrawThemedToggleButton(
                "FFLogs Link",
                "WtfDigSourceFflogs",
                state.Source == WtfDigAnalyzerSource.Fflogs))
        {
            wtfDigAnalyzer.SetSource(WtfDigAnalyzerSource.Fflogs);
        }
    }

    private void DrawWtfDigAnalyzerSelector(WtfDigAnalyzerViewState state)
    {
        ImGui.TextDisabled("Mechanic");
        var rowWidth = 0.0f;
        var available = ImGui.GetContentRegionAvail().X;
        foreach (var analyzer in WtfDigAnalyzerCatalog.All)
        {
            var label = $"{analyzer.Phase} {analyzer.Label}";
            var width = GetThemedActionButtonWidth(label);
            if (rowWidth > 0.0f && rowWidth + ImGui.GetStyle().ItemSpacing.X + width <= available)
            {
                ImGui.SameLine();
                rowWidth += ImGui.GetStyle().ItemSpacing.X + width;
            }
            else
            {
                rowWidth = width;
            }

            if (DrawThemedToggleButton(label, $"WtfDigAnalyzer{analyzer.Key}", state.Analyzer.Key == analyzer.Key, width))
            {
                wtfDigAnalyzer.SelectAnalyzer(analyzer);
                selectedForsakenResolution = 0;
            }

            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip(analyzer.Description);
            }
        }
    }

    private void DrawWtfDigReportInput(WtfDigAnalyzerViewState state)
    {
        var input = state.Input;
        var buttonWidth = MathF.Max(86.0f, GetThemedActionButtonWidth(state.Loading ? "Loading..." : "Analyze"));
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var buttonOnSameLine = availableWidth >= buttonWidth + 140.0f + ImGui.GetStyle().ItemSpacing.X;
        var inputWidth = buttonOnSameLine
            ? availableWidth - buttonWidth - ImGui.GetStyle().ItemSpacing.X
            : MathF.Max(1.0f, availableWidth);
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.InputTextWithHint(
                "##WtfDigReportInput",
                "https://www.fflogs.com/reports/... or ...?fight=12",
                ref input,
                512,
                ImGuiInputTextFlags.EnterReturnsTrue))
        {
            wtfDigAnalyzer.SetInput(input);
            if (!state.Loading)
            {
                wtfDigAnalyzer.Load();
            }
        }
        else if (!string.Equals(input, state.Input, StringComparison.Ordinal))
        {
            wtfDigAnalyzer.SetInput(input);
        }

        if (buttonOnSameLine)
        {
            ImGui.SameLine();
        }

        if (state.Loading)
        {
            ImGui.BeginDisabled();
        }

        if (DrawThemedActionButton(state.Loading ? "Loading..." : "Analyze", "WtfDigAnalyze", buttonWidth))
        {
            wtfDigAnalyzer.Load();
        }

        if (state.Loading)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawWtfDigLocalPullInput(WtfDigAnalyzerViewState state)
    {
        var localPulls = plugin.RecordedPulls
            .Where(summary => ReplayEncounterModules.IsDancingMadUltimate(summary.TerritoryId))
            .OrderByDescending(summary => summary.CapturedAtUtc)
            .ThenByDescending(summary => summary.PullNumber)
            .ToArray();
        if (localPulls.Length == 0)
        {
            ImGui.TextDisabled(plugin.RecordedPullHistoryLoading
                ? "Loading Better Deaths pulls..."
                : "No recorded DMU pulls are available yet.");
            return;
        }

        var selected = localPulls.FirstOrDefault(summary =>
            summary.PullNumber == selectedWtfDigLocalPullNumber &&
            summary.CapturedAtUtc.Ticks == selectedWtfDigLocalPullCapturedAtTicks);
        if (selected is null)
        {
            selected = localPulls.FirstOrDefault(summary =>
                string.Equals(BuildRecordedPullKey(summary), recapReviewSelection.PullKey, StringComparison.Ordinal)) ?? localPulls[0];
            selectedWtfDigLocalPullNumber = selected.PullNumber;
            selectedWtfDigLocalPullCapturedAtTicks = selected.CapturedAtUtc.Ticks;
        }

        ImGui.TextDisabled("Recorded pull");
        ImGui.SetNextItemWidth(MathF.Min(470.0f, MathF.Max(220.0f, ImGui.GetContentRegionAvail().X)));
        if (ImGui.BeginCombo("##WtfDigLocalPullPicker", FormatWtfDigLocalPullLabel(selected)))
        {
            foreach (var summary in localPulls)
            {
                var isSelected = summary.PullNumber == selected.PullNumber &&
                    summary.CapturedAtUtc.Ticks == selected.CapturedAtUtc.Ticks;
                if (ImGui.Selectable(FormatWtfDigLocalPullLabel(summary), isSelected))
                {
                    selected = summary;
                    selectedWtfDigLocalPullNumber = summary.PullNumber;
                    selectedWtfDigLocalPullCapturedAtTicks = summary.CapturedAtUtc.Ticks;
                    wtfDigAnalyzer.PrepareLocalPull(summary);
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        var identityMatches = state.Source == WtfDigAnalyzerSource.LocalPull &&
            state.LocalPullNumber == selected.PullNumber &&
            state.LocalPullCapturedAtTicks == selected.CapturedAtUtc.Ticks;
        if (!identityMatches)
        {
            wtfDigAnalyzer.PrepareLocalPull(selected);
            state = wtfDigAnalyzer.Snapshot();
        }

        var detail = plugin.GetRecordedPullDetails(selected);
        if (detail is null)
        {
            ImGui.TextDisabled(plugin.IsRecordedPullDetailsLoading(selected)
                ? "Loading this pull..."
                : "This pull's replay data could not be loaded.");
            return;
        }

        if (state.Report is null && !state.Loading && string.IsNullOrWhiteSpace(state.Error))
        {
            wtfDigAnalyzer.LoadLocalPull(detail);
        }
        else if (state.Report is null && !state.Loading && !string.IsNullOrWhiteSpace(state.Error))
        {
            if (DrawThemedActionButton("Try again", "WtfDigRetryLocal"))
            {
                wtfDigAnalyzer.LoadLocalPull(detail);
            }
        }
    }

    private static string FormatWtfDigLocalPullLabel(RecordedPullSummary pull)
    {
        return $"Pull {pull.PullNumber} - {WtfDigAnalysisHelpers.FormatDuration(pull.PullElapsedSeconds * 1000.0)} - {pull.CapturedAtUtc.ToLocalTime():g}";
    }

    private void DrawWtfDigReportBar(WtfDigAnalyzerViewState state)
    {
        var report = state.Report!;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, WithBackgroundOpacity(ModernPanelAltColor, currentMainWindowBackgroundOpacity));
        ImGui.PushStyleColor(ImGuiCol.Border, ModernPanelBorderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, ModernPanelRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10.0f, 8.0f));
        var height = ImGui.GetFrameHeight() + (ImGui.GetTextLineHeightWithSpacing() * 2.0f) + 12.0f;
        if (ImGui.BeginChild("##WtfDigLoadedReport", new Vector2(0.0f, height), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(report.Title) ? "FFLogs report" : report.Title);
            if (report.Zone is { } zone)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(zone.Name);
            }

            if (state.Source == WtfDigAnalyzerSource.LocalPull)
            {
                var selected = state.SelectedFight;
                ImGui.TextDisabled(selected is null
                    ? "Preparing pull analysis..."
                    : $"Recorded locally - {WtfDigAnalysisHelpers.FormatDuration(selected.DurationMs)}");
            }
            else if (state.EligibleFights.Count == 0)
            {
                ImGui.TextDisabled($"No pulls reached {state.Analyzer.MechanicLabel}.");
            }
            else
            {
                var selected = state.SelectedFight;
                var selectedIndex = selected is null
                    ? -1
                    : state.EligibleFights.ToList().FindIndex(fight => fight.Id == selected.Id);
                DrawWtfDigPullStepButton("<", "Previous", selectedIndex > 0, () =>
                    wtfDigAnalyzer.SelectFight(state.EligibleFights[selectedIndex - 1].Id));
                ImGui.SameLine();
                var pullLabel = selected is null ? "Select a pull" : FormatWtfDigPullLabel(report, selected);
                ImGui.SetNextItemWidth(MathF.Min(390.0f, MathF.Max(180.0f, ImGui.GetContentRegionAvail().X - 42.0f)));
                if (ImGui.BeginCombo("##WtfDigPullPicker", pullLabel))
                {
                    foreach (var fight in state.EligibleFights)
                    {
                        var isSelected = fight.Id == selected?.Id;
                        if (ImGui.Selectable(FormatWtfDigPullLabel(report, fight), isSelected))
                        {
                            wtfDigAnalyzer.SelectFight(fight.Id);
                            selectedForsakenResolution = 0;
                        }

                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }

                    ImGui.EndCombo();
                }

                ImGui.SameLine();
                DrawWtfDigPullStepButton(">", "Next", selectedIndex >= 0 && selectedIndex < state.EligibleFights.Count - 1, () =>
                    wtfDigAnalyzer.SelectFight(state.EligibleFights[selectedIndex + 1].Id));
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static void DrawWtfDigPullStepButton(string label, string tooltip, bool enabled, Action onClick)
    {
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button($"{label}##WtfDig{tooltip}", new Vector2(28.0f, ImGui.GetFrameHeight())))
        {
            onClick();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{tooltip} pull");
        }

        if (!enabled)
        {
            ImGui.EndDisabled();
        }
    }

    private static string FormatWtfDigPullLabel(FflogsReportSummary report, FflogsFight fight)
    {
        var dancingMad = report.Fights.Where(WtfDigAnalyzerCatalog.IsDancingMad).OrderBy(entry => entry.StartTime).ToArray();
        var wipeNumber = dancingMad.Count(entry => entry.Kill != true && entry.StartTime < fight.StartTime) + 1;
        var name = fight.Kill == true ? "Kill" : $"Wipe {wipeNumber}";
        var phase = fight.PhaseTransitions is { Count: > 0 }
            ? $" P{fight.PhaseTransitions[^1].Id}"
            : string.Empty;
        return $"{name}{phase} - {WtfDigAnalysisHelpers.FormatDuration(fight.DurationMs)}";
    }

    private void DrawForsakenAnalysis(ForsakenAnalysis analysis)
    {
        selectedForsakenResolution = Math.Clamp(selectedForsakenResolution, 0, analysis.Resolutions.Count - 1);
        var resolution = analysis.Resolutions[selectedForsakenResolution];
        ImGui.TextColored(ModernAccentColor, "Forsaken tower sets");
        var rowWidth = 0.0f;
        var available = ImGui.GetContentRegionAvail().X;
        for (var index = 0; index < analysis.Resolutions.Count; index++)
        {
            var candidate = analysis.Resolutions[index];
            var warning = candidate.Players.Any(player => player.DiedThisSet || player.DoubleHit) ? " !" : string.Empty;
            var label = candidate.IsRaidwide
                ? $"Raidwide {FormatWtfDigTime(candidate.ResolveTime)}{warning}"
                : $"Set {candidate.SetNumber} {FormatWtfDigTime(candidate.ResolveTime)}{warning}";
            var width = GetThemedActionButtonWidth(label);
            if (rowWidth > 0.0f && rowWidth + ImGui.GetStyle().ItemSpacing.X + width <= available)
            {
                ImGui.SameLine();
                rowWidth += ImGui.GetStyle().ItemSpacing.X + width;
            }
            else
            {
                rowWidth = width;
            }

            if (DrawThemedToggleButton(label, $"ForsakenSet{index}", index == selectedForsakenResolution, width))
            {
                selectedForsakenResolution = index;
                forsakenViewIndex = 1;
                resolution = candidate;
            }
        }

        ImGui.Dummy(new Vector2(1.0f, 6.0f));
        DrawForsakenSummary(resolution, analysis.Rotation);
        DrawForsakenControls(resolution);

        var contentWidth = ImGui.GetContentRegionAvail().X;
        var mapSize = GetWtfDigMapSize(contentWidth);
        if (contentWidth >= 820.0f)
        {
            DrawForsakenArena(resolution, mapSize);
            ImGui.SameLine(0.0f, 12.0f);
            var tableWidth = MathF.Max(260.0f, ImGui.GetContentRegionAvail().X);
            DrawForsakenPlayerTable(resolution, new Vector2(tableWidth, mapSize));
        }
        else
        {
            DrawForsakenArena(resolution, mapSize);
            ImGui.Dummy(new Vector2(1.0f, 8.0f));
            DrawForsakenPlayerTable(resolution, new Vector2(0.0f, 0.0f));
        }
    }

    private void DrawArrowsAnalysis(ArrowsAnalysis analysis)
    {
        if (selectedArrowWave >= analysis.Waves.Count)
        {
            selectedArrowWave = -1;
        }

        ImGui.TextColored(ModernAccentColor, "Tele-Portent arrows");
        if (DrawThemedToggleButton("All", "ArrowWaveAll", selectedArrowWave < 0))
        {
            selectedArrowWave = -1;
        }

        for (var index = 0; index < analysis.Waves.Count; index++)
        {
            ImGui.SameLine();
            var wave = analysis.Waves[index];
            var label = $"Wave {index + 1} {FormatWtfDigTime(wave.Time)}";
            if (DrawThemedToggleButton(label, $"ArrowWave{index}", selectedArrowWave == index))
            {
                selectedArrowWave = index;
            }
        }

        ImGui.Dummy(new Vector2(1.0f, 5.0f));
        ImGui.TextDisabled("Strategy");
        var strategies = new[] { ("Merry-go-round", ArrowStrategy.MerryGoRound), ("Filipino", ArrowStrategy.Filipino), ("Freaky", ArrowStrategy.Freaky) };
        for (var index = 0; index < strategies.Length; index++)
        {
            if (index > 0)
            {
                ImGui.SameLine();
            }

            if (DrawThemedToggleButton(strategies[index].Item1, $"ArrowStrategy{index}", arrowStrategyIndex == index))
            {
                arrowStrategyIndex = index;
            }
        }

        ImGui.SameLine();
        var starts = showArrowStarts;
        if (ImGui.Checkbox("Start positions##ArrowStarts", ref starts))
        {
            showArrowStarts = starts;
        }

        var shown = selectedArrowWave < 0
            ? analysis.Waves.SelectMany(wave => wave.Arrows).ToArray()
            : analysis.Waves[selectedArrowWave].Arrows;
        var slots = ArrowsAnalyzer.ExpectedSlots(strategies[Math.Clamp(arrowStrategyIndex, 0, strategies.Length - 1)].Item2);
        ImGui.Dummy(new Vector2(1.0f, 6.0f));
        var contentWidth = ImGui.GetContentRegionAvail().X;
        var mapSize = GetWtfDigMapSize(contentWidth);
        if (contentWidth >= 820.0f)
        {
            DrawArrowsArena(shown, showArrowStarts ? analysis.Starts : [], slots, mapSize);
            ImGui.SameLine(0.0f, 12.0f);
            DrawArrowsTable(shown, slots, new Vector2(MathF.Max(260.0f, ImGui.GetContentRegionAvail().X), mapSize));
        }
        else
        {
            DrawArrowsArena(shown, showArrowStarts ? analysis.Starts : [], slots, mapSize);
            ImGui.Dummy(new Vector2(1.0f, 8.0f));
            DrawArrowsTable(shown, slots, Vector2.Zero);
        }

    }

    private void DrawLimitCutAnalysis(LimitCutAnalysis analysis, WtfDigAnalyzerSource source)
    {
        var kefka = analysis.Kefka!;
        var wrong = analysis.Players.Count(player => (player.AngleError ?? 0) >= 22.5);
        var dead = analysis.Players.Count(player => player.Dead);
        var unverified = analysis.Players.Count(player => player.AngleError is null);
        ImGui.TextColored(ModernAccentColor, "Limit Cut");
        ImGui.TextUnformatted("Clone started ");
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.TextColored(LeadUpGoldColor, kefka.StartName);
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.TextUnformatted(" rotating ");
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.TextColored(LeadUpGoldColor, FormatLimitCutRotation(kefka.Rotation));
        ImGui.SameLine();
        ImGui.TextDisabled($"Players rotate {FormatLimitCutRotation(analysis.PlayerRotation)}");
        if (wrong > 0)
        {
            ImGui.TextColored(DamageColor, $"{wrong} player{(wrong == 1 ? string.Empty : "s")} in the wrong spot");
        }
        else if (analysis.Players.Count >= 8 && unverified == 0)
        {
            ImGui.TextColored(HealColor, "All players were in their spot.");
        }

        if (dead > 0 || unverified > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"{dead} dead | {unverified} unverified");
        }

        ImGui.Dummy(new Vector2(1.0f, 6.0f));
        var contentWidth = ImGui.GetContentRegionAvail().X;
        var mapSize = GetWtfDigMapSize(contentWidth);
        if (contentWidth >= 820.0f)
        {
            DrawLimitCutArena(analysis, mapSize);
            ImGui.SameLine(0.0f, 12.0f);
            DrawLimitCutTable(analysis.Players, new Vector2(MathF.Max(260.0f, ImGui.GetContentRegionAvail().X), mapSize));
        }
        else
        {
            DrawLimitCutArena(analysis, mapSize);
            ImGui.Dummy(new Vector2(1.0f, 8.0f));
            DrawLimitCutTable(analysis.Players, Vector2.Zero);
        }

        ImGui.TextDisabled(source == WtfDigAnalyzerSource.LocalPull
            ? "Positions use Better Deaths replay snapshots near the final blast."
            : "Positions are estimated from FFLogs snapshots near the final blast.");
    }

    private void DrawBlackHoleAnalysis(BlackHoleAnalysis analysis)
    {
        selectedBlackHoleTether = Math.Clamp(selectedBlackHoleTether, 0, analysis.Tethers.Count - 1);
        var tether = analysis.Tethers[selectedBlackHoleTether];
        ImGui.TextColored(ModernAccentColor, "Black Hole");
        ImGui.TextDisabled("Assignments");
        var orderedPlayers = analysis.Players
            .OrderBy(player => BlackHoleAnalyzer.RoleSortKey(player.Role, blackHoleSupportFirst))
            .ToArray();
        var assignmentText = string.Join("   ", orderedPlayers.Select(player => $"{player.Job.Abbreviation} {player.Role.Label}"));
        ImGui.TextWrapped(assignmentText);
        ImGui.Dummy(new Vector2(1.0f, 4.0f));
        ImGui.TextDisabled("Order");
        if (DrawThemedToggleButton("DSA", "BlackHoleOrderDSA", !blackHoleSupportFirst))
        {
            blackHoleSupportFirst = false;
        }

        ImGui.SameLine();
        if (DrawThemedToggleButton("SDA", "BlackHoleOrderSDA", blackHoleSupportFirst))
        {
            blackHoleSupportFirst = true;
        }

        ImGui.SameLine();
        var bosses = showBlackHoleBosses;
        if (ImGui.Checkbox("Bosses##BlackHoleBosses", ref bosses))
        {
            showBlackHoleBosses = bosses;
        }

        ImGui.Dummy(new Vector2(1.0f, 5.0f));
        var rowWidth = 0.0f;
        var available = ImGui.GetContentRegionAvail().X;
        for (var index = 0; index < analysis.Tethers.Count; index++)
        {
            var candidate = analysis.Tethers[index];
            var soaks = candidate.States.Sum(state => state.HitsThisTether);
            var expected = BlackHoleAnalyzer.ExpectedSoaks(candidate.Set, candidate.Tether);
            var warning = candidate.States.Any(state => state.DiedThisTether) || soaks > expected ? " !" : string.Empty;
            var label = $"S{candidate.Set} T{candidate.Tether}{warning}";
            var width = GetThemedActionButtonWidth(label);
            if (rowWidth > 0 && rowWidth + ImGui.GetStyle().ItemSpacing.X + width <= available)
            {
                ImGui.SameLine();
                rowWidth += ImGui.GetStyle().ItemSpacing.X + width;
            }
            else
            {
                rowWidth = width;
            }

            if (DrawThemedToggleButton(label, $"BlackHoleTether{index}", index == selectedBlackHoleTether, width))
            {
                selectedBlackHoleTether = index;
                tether = candidate;
            }
        }

        var actualSoaks = tether.States.Sum(state => state.HitsThisTether);
        var expectedSoaks = BlackHoleAnalyzer.ExpectedSoaks(tether.Set, tether.Tether);
        ImGui.TextUnformatted($"{tether.Label} - {FormatWtfDigTime(tether.Time)}");
        if (actualSoaks > expectedSoaks)
        {
            ImGui.SameLine();
            ImGui.TextColored(LeadUpGoldColor, $"{actualSoaks} soaks (expected {expectedSoaks})");
        }

        var contentWidth = ImGui.GetContentRegionAvail().X;
        var mapSize = GetWtfDigMapSize(contentWidth);
        if (contentWidth >= 820.0f)
        {
            DrawBlackHoleArena(tether, analysis.Players, mapSize);
            ImGui.SameLine(0.0f, 12.0f);
            DrawBlackHoleTable(tether, analysis.Players, new Vector2(MathF.Max(260.0f, ImGui.GetContentRegionAvail().X), mapSize));
        }
        else
        {
            DrawBlackHoleArena(tether, analysis.Players, mapSize);
            ImGui.Dummy(new Vector2(1.0f, 8.0f));
            DrawBlackHoleTable(tether, analysis.Players, Vector2.Zero);
        }

    }

    private void DrawP4Analysis(P4Analysis analysis)
    {
        ImGui.TextColored(ModernAccentColor, "Kefka Says");
        if (analysis.KefkaSaysTime is { } saysTime)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"cast at {FormatWtfDigTime(saysTime)}");
        }

        for (var index = 0; index < analysis.Rounds.Count; index++)
        {
            if (index > 0)
            {
                ImGui.Dummy(new Vector2(1.0f, 8.0f));
                ImGui.Separator();
            }

            DrawP4Round(analysis.Rounds[index], analysis.FloodOfNaught, analysis.Markers);
        }

        if (analysis.Resolution is { } resolution)
        {
            ImGui.Dummy(new Vector2(1.0f, 10.0f));
            ImGui.Separator();
            ImGui.TextColored(ModernAccentColor, "Resolution order");
            var available = ImGui.GetContentRegionAvail().X;
            if (available >= 820.0f && ImGui.BeginTable("##P4ResolutionBlocks", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableNextColumn();
                DrawP4ResolutionBlock("Short Debuffs", resolution.Block1, resolution.Block1Reached, analysis.Markers, "short");
                ImGui.TableNextColumn();
                DrawP4ResolutionBlock("Long Debuffs", resolution.Block2, resolution.Block2Reached, analysis.Markers, "long");
                ImGui.EndTable();
            }
            else
            {
                DrawP4ResolutionBlock("Short Debuffs", resolution.Block1, resolution.Block1Reached, analysis.Markers, "short");
                ImGui.Dummy(new Vector2(1.0f, 8.0f));
                DrawP4ResolutionBlock("Long Debuffs", resolution.Block2, resolution.Block2Reached, analysis.Markers, "long");
            }
        }

    }

    private void DrawP4Round(P4Round round, P4RealFake? flood, IReadOnlyList<P4DeathMarker> markers)
    {
        ImGui.TextUnformatted($"{P4Ordinal(round.Index)} Debuffs - {FormatWtfDigTime(round.Time)}");
        ImGui.TextDisabled("Kefka - Mystery Magic");
        ImGui.TextUnformatted("Ice ");
        ImGui.SameLine(0.0f, 0.0f);
        DrawP4RealFake(round.Ice);
        ImGui.SameLine();
        ImGui.TextUnformatted("Lightning ");
        ImGui.SameLine(0.0f, 0.0f);
        DrawP4RealFake(round.Lightning);

        DrawP4RoundMarkers(markers, round.Index, "mm");
        ImGui.TextDisabled("Neo Exdeath - Grand Cross");
        if (round.Index != 3)
        {
            ImGui.TextUnformatted("Cast ");
            ImGui.SameLine(0.0f, 0.0f);
            DrawP4RealFake(round.NeoExdeath);
            if (round.MarkTimer is { } timer)
            {
                ImGui.SameLine();
                ImGui.TextColored(timer == P4Timer.Short ? LeadUpGoldColor : ModernAccentColor,
                    timer == P4Timer.Short ? "SHORT" : "LONG");
            }
        }

        DrawP4DebuffLine("Water", round.Water);
        DrawP4DebuffLine("Lightning", round.LightningMarks);
        DrawP4DebuffLine("Gaze", round.Gaze);
        DrawP4DebuffLine("Short accel", round.ShortAccel);
        DrawP4DebuffLine("Long accel", round.LongAccel);

        DrawP4RoundMarkers(markers, round.Index, "gc");

        if (round.Wounds.Count > 0)
        {
            ImGui.TextUnformatted("Flood of Naught ");
            ImGui.SameLine(0.0f, 0.0f);
            DrawP4RealFake(flood);
            DrawP4Wounds(round.Wounds);
        }

        if (!string.IsNullOrWhiteSpace(round.ChaosAbility))
        {
            ImGui.TextDisabled($"Chaos - {round.ChaosAbility}");
            ImGui.SameLine();
            DrawP4RealFake(round.Chaos);
        }

        DrawP4RoundMarkers(markers, round.Index, "third");
    }

    private static void DrawP4RoundMarkers(IReadOnlyList<P4DeathMarker> markers, int round, string substep)
    {
        foreach (var marker in markers.Where(marker => marker.Stage == "round" && marker.Round == round && marker.Substep == substep))
        {
            ImGui.TextColored(marker.Kind == "wipe" ? DamageColor : LeadUpGoldColor, FormatP4Marker(marker));
        }
    }

    private static void DrawP4DebuffLine(string label, IReadOnlyList<P4DebuffTarget> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var jobs = string.Join(" ", targets.Select(target => target.Job.Abbreviation));
        ImGui.TextDisabled(label);
        ImGui.SameLine();
        ImGui.TextUnformatted(jobs);
    }

    private void DrawP4Wounds(IReadOnlyList<P4WoundInfo> wounds)
    {
        if (!ImGui.BeginTable("##P4Wounds", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 50.0f);
        ImGui.TableSetupColumn("Wound", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Final", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        foreach (var wound in wounds)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(ParseWtfDigColor(wound.Job.Color), wound.Job.Abbreviation);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatP4Wound(wound.Wound));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(wound.Field switch { P4WoundField.Swap => "Swap", P4WoundField.Keep => "Keep", _ => "-" });
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatP4Wound(wound.FinalWound));
            ImGui.TableNextColumn();
            var label = wound.Success switch
            {
                true => "Correct",
                false when wound.CaughtMiddle => "Caught in middle",
                false => "Wrong side",
                _ => "Unknown",
            };
            ImGui.TextColored(wound.Success == true ? HealColor : wound.Success == false ? DamageColor : ModernMutedTextColor,
                wound.Died ? $"{label} | died" : label);
        }

        ImGui.EndTable();
    }

    private void DrawP4ResolutionBlock(
        string title,
        IReadOnlyList<P4ResolutionElement> elements,
        bool reached,
        IReadOnlyList<P4DeathMarker> markers,
        string stage)
    {
        ImGui.TextUnformatted(title);
        if (!reached)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("not reached");
        }

        for (var index = 0; index <= elements.Count; index++)
        {
            foreach (var marker in markers.Where(marker => marker.Stage == stage && marker.Index == index))
            {
                ImGui.TextColored(marker.Kind == "wipe" ? DamageColor : LeadUpGoldColor, FormatP4Marker(marker));
            }

            if (index >= elements.Count)
            {
                continue;
            }

            var element = elements[index];
            ImGui.BulletText(element.Title);
            if (element.HasRealFake)
            {
                ImGui.SameLine();
                DrawP4RealFake(element.RealFake);
            }

            if (!string.IsNullOrWhiteSpace(element.Call))
            {
                ImGui.SameLine();
                ImGui.TextColored(element.Call == "Move" ? LeadUpGoldColor : ModernTextColor, element.Call);
            }

            if (element.HasManaResult)
            {
                ImGui.Indent(16.0f);
                ImGui.TextDisabled("Casts");
                ImGui.SameLine();
                ImGui.TextUnformatted("Lightning ");
                ImGui.SameLine(0.0f, 0.0f);
                DrawP4RealFake(element.CastLightning);
                ImGui.SameLine();
                ImGui.TextUnformatted("Ice ");
                ImGui.SameLine(0.0f, 0.0f);
                DrawP4RealFake(element.CastIce);
                ImGui.TextDisabled("Actual");
                ImGui.SameLine();
                ImGui.TextUnformatted("Lightning ");
                ImGui.SameLine(0.0f, 0.0f);
                DrawP4RealFake(element.Lightning);
                ImGui.SameLine();
                ImGui.TextUnformatted("Ice ");
                ImGui.SameLine(0.0f, 0.0f);
                DrawP4RealFake(element.Ice);
                ImGui.Unindent(16.0f);
            }

            foreach (var group in element.Players.GroupBy(player => player.Call))
            {
                ImGui.Indent(16.0f);
                var jobs = string.Join(" ", group.Select(player => player.Job.Abbreviation));
                ImGui.TextColored(group.Any(player => player.Danger) ? LeadUpGoldColor : ModernMutedTextColor,
                    $"{group.Key}: {jobs}");
                ImGui.Unindent(16.0f);
            }
        }
    }

    private static void DrawP4RealFake(P4RealFake? value)
    {
        ImGui.TextColored(value switch
        {
            P4RealFake.Real => HealColor,
            P4RealFake.Fake => DamageColor,
            _ => ModernMutedTextColor,
        }, value switch
        {
            P4RealFake.Real => "REAL",
            P4RealFake.Fake => "FAKE",
            _ => "?",
        });
    }

    private static string FormatP4Marker(P4DeathMarker marker) => marker.Kind == "wipe"
        ? $"Wipe at {FormatWtfDigTime(marker.AtSeconds)}"
        : $"Death{(marker.Count > 1 ? $" x{marker.Count}" : string.Empty)} at {FormatWtfDigTime(marker.AtSeconds)}";
    private static string FormatP4Wound(P4WoundColor? wound) => wound switch { P4WoundColor.White => "White", P4WoundColor.Black => "Black", _ => "-" };
    private static string P4Ordinal(int value) => value switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{value}th" };

    private void DrawBlackHoleArena(BlackHoleTether tether, IReadOnlyList<BlackHolePlayerInfo> players, float size)
    {
        var playerById = players.ToDictionary(player => player.ActorId);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##BlackHoleArena", new Vector2(size, size));
        var drawList = ImGui.GetWindowDrawList();
        var end = origin + new Vector2(size, size);
        var center = origin + new Vector2(size * 0.5f);
        const float extent = 28.0f;
        var scale = (size * 0.5f) / extent;
        Vector2 ToScreen(Vector2 point) => center + point * scale;
        ImGui.PushClipRect(origin, end, true);
        drawList.AddRectFilled(origin, end, ImGui.GetColorU32(ModernPanelAltColor with { W = 0.86f }), 4.0f);
        drawList.AddRect(origin, end, ImGui.GetColorU32(ModernPanelBorderColor), 4.0f);
        drawList.AddCircle(center, BlackHoleAnalyzer.ArenaRadius * scale, ImGui.GetColorU32(ModernPanelBorderColor), 96, 1.5f);
        DrawForsakenWaymarks(drawList, ToScreen, scale);

        foreach (var beam in tether.Beams)
        {
            var beamOrigin = ToScreen(beam.Origin);
            var direction = new Vector2((float)Math.Sin(beam.FacingRadians), (float)Math.Cos(beam.FacingRadians));
            var perpendicular = new Vector2(-direction.Y, direction.X);
            var beamEnd = beamOrigin + direction * (50.0f * scale);
            var halfWidth = 3.0f * scale;
            var color = new Vector4(0.63f, 0.42f, 1.0f, 1.0f);
            drawList.AddQuadFilled(
                beamOrigin + perpendicular * halfWidth,
                beamEnd + perpendicular * halfWidth,
                beamEnd - perpendicular * halfWidth,
                beamOrigin - perpendicular * halfWidth,
                ImGui.GetColorU32(color with { W = 0.12f }));
            drawList.AddLine(beamOrigin, beamEnd, ImGui.GetColorU32(color with { W = 0.60f }), 1.3f);
            drawList.AddCircleFilled(beamOrigin, 7.0f, ImGui.GetColorU32(color), 24);
        }

        if (showBlackHoleBosses)
        {
            if (tether.BigKefka is { } bigKefka)
            {
                var point = ToScreen(bigKefka);
                drawList.AddCircleFilled(point, 11.0f, ImGui.GetColorU32(DamageColor with { W = 0.54f }), 24);
                drawList.AddText(point + new Vector2(8.0f, -8.0f), ImGui.GetColorU32(DamageColor), "Kefka");
            }

            if (tether.Chaos is { } chaos)
            {
                var point = ToScreen(chaos);
                drawList.AddCircle(point, BlackHoleAnalyzer.ChaosRadius * scale, ImGui.GetColorU32(new Vector4(1.0f, 0.48f, 0.30f, 0.72f)), 48, 1.5f);
            }

            if (tether.Exdeath is { } exdeath)
            {
                var point = ToScreen(exdeath);
                drawList.AddCircle(point, BlackHoleAnalyzer.ExdeathRadius * scale, ImGui.GetColorU32(new Vector4(0.37f, 0.83f, 0.55f, 0.72f)), 48, 1.5f);
            }
        }

        foreach (var state in tether.States.Where(state => state.Position is not null))
        {
            var point = ToScreen(state.Position!.Value);
            var player = playerById.GetValueOrDefault(state.ActorId);
            if (player is null)
            {
                continue;
            }

            var levelColor = state.Level switch
            {
                NothingLevel.Unbecoming => new Vector4(0.36f, 0.61f, 1.0f, 1.0f),
                NothingLevel.Meanest => new Vector4(0.77f, 0.80f, 1.0f, 1.0f),
                _ => ModernMutedTextColor,
            };
            if (state.HitsThisTether > 0)
            {
                drawList.AddCircle(point, 13.0f, ImGui.GetColorU32(state.LethalThisTether ? DamageColor : levelColor), 28, 2.5f);
            }

            if (state.TetherThisTether)
            {
                drawList.AddCircle(point, 16.0f, ImGui.GetColorU32(ModernTextColor), 28, 2.0f);
            }
            else if (state.HitsThisTether > 0)
            {
                drawList.AddCircle(point, 16.0f, ImGui.GetColorU32(LeadUpGoldColor), 28, 1.5f);
            }

            DrawForsakenPlayerToken(drawList, point, player.Job, null, false, false);
            drawList.AddText(point + new Vector2(9.0f, 6.0f), ImGui.GetColorU32(ModernTextColor), BlackHoleRoleTag(player.Role));
        }

        ImGui.PopClipRect();
    }

    private void DrawBlackHoleTable(
        BlackHoleTether tether,
        IReadOnlyList<BlackHolePlayerInfo> players,
        Vector2 size)
    {
        var info = players.ToDictionary(player => player.ActorId);
        var states = tether.States
            .OrderBy(state => info.TryGetValue(state.ActorId, out var player)
                ? BlackHoleAnalyzer.RoleSortKey(player.Role, blackHoleSupportFirst)
                : 999)
            .ToArray();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, WithBackgroundOpacity(ModernPanelAltColor, currentMainWindowBackgroundOpacity));
        if (ImGui.BeginChild("##BlackHoleTablePanel", size, true, OptionalScrollbarFlags) &&
            ImGui.BeginTable("##BlackHoleTable", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 52.0f);
            ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("Soaked", ImGuiTableColumnFlags.WidthStretch, 0.8f);
            ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("Crust", ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableHeadersRow();
            foreach (var state in states)
            {
                var player = info.GetValueOrDefault(state.ActorId);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(player is null ? ModernMutedTextColor : ParseWtfDigColor(player.Job.Color),
                    player is null ? "?" : state.Dead ? $"{player.Job.Abbreviation} X" : player.Job.Abbreviation);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(player?.Role.Label ?? "-");
                ImGui.TableNextColumn();
                var soakLabel = state.SoakCount == 0
                    ? "-"
                    : $"{state.SoakCount}{(state.HitsThisTether > 0 ? state.TetherThisTether ? " held" : " clipped" : string.Empty)}";
                ImGui.TextColored(state.LethalThisTether ? DamageColor : state.HitsThisTether > 0 ? LeadUpGoldColor : ModernMutedTextColor, soakLabel);
                ImGui.TableNextColumn();
                var stateLabel = state.Level switch
                {
                    NothingLevel.Unbecoming => "Unbecoming",
                    NothingLevel.Meanest => "Meanest",
                    _ => "-",
                };
                ImGui.TextUnformatted(stateLabel);
                ImGui.TableNextColumn();
                ImGui.TextColored(state.Crust ? LeadUpGoldColor : state.CleansedThisTether ? HealColor : ModernMutedTextColor,
                    state.Crust ? "Yes" : "Cleansed");
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private static string BlackHoleRoleTag(BlackHoleRole role) => $"{role.Order?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}{role.Type switch
    {
        BlackHoleRoleType.Dps => "D",
        BlackHoleRoleType.Support => "S",
        _ => "A",
    }}";

    private void DrawLimitCutArena(LimitCutAnalysis analysis, float size)
    {
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##LimitCutArena", new Vector2(size, size));
        var drawList = ImGui.GetWindowDrawList();
        var end = origin + new Vector2(size, size);
        var center = origin + new Vector2(size * 0.5f);
        var extent = MathF.Max(22.0f, (float)analysis.WallRadius + 3.0f);
        var scale = (size * 0.5f) / extent;
        Vector2 ToScreen(Vector2 point) => center + point * scale;
        ImGui.PushClipRect(origin, end, true);
        drawList.AddRectFilled(origin, end, ImGui.GetColorU32(ModernPanelAltColor with { W = 0.86f }), 4.0f);
        drawList.AddRect(origin, end, ImGui.GetColorU32(ModernPanelBorderColor), 4.0f);
        drawList.AddCircle(center, (float)analysis.WallRadius * scale, ImGui.GetColorU32(ModernPanelBorderColor), 96, 1.4f);
        DrawForsakenWaymarks(drawList, ToScreen, scale);

        foreach (var spot in analysis.BlasterSpots)
        {
            var point = ToScreen(spot);
            drawList.AddCircleFilled(point, 6.0f, ImGui.GetColorU32(DamageColor with { W = 0.82f }), 20);
            drawList.AddCircle(point, 8.0f, ImGui.GetColorU32(DamageColor with { W = 0.36f }), 20, 1.0f);
        }

        foreach (var gap in analysis.Gaps)
        {
            var point = ToScreen(gap.Position);
            drawList.AddCircle(point, 10.0f, ImGui.GetColorU32(new Vector4(0.50f, 0.84f, 0.88f, 0.74f)), 24, 2.0f);
            var label = gap.Number.ToString(CultureInfo.InvariantCulture);
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(point - textSize * 0.5f, ImGui.GetColorU32(new Vector4(0.50f, 0.84f, 0.88f, 1.0f)), label);
        }

        if (analysis.Kefka is { } kefka)
        {
            var point = ToScreen(LimitCutAnalyzer.PositionAtAngle(kefka.StartAngle, analysis.WallRadius));
            drawList.AddQuad(
                point + new Vector2(0, -9), point + new Vector2(9, 0),
                point + new Vector2(0, 9), point + new Vector2(-9, 0),
                ImGui.GetColorU32(LeadUpGoldColor), 2.5f);
        }

        foreach (var player in analysis.Players.Where(player => player.Position is not null))
        {
            var point = ToScreen(player.Position!.Value);
            var danger = (player.AngleError ?? 0) >= 22.5;
            if (danger)
            {
                drawList.AddCircle(point, 12.0f, ImGui.GetColorU32(DamageColor), 24, 2.5f);
            }

            DrawForsakenPlayerToken(drawList, point, player.Job, null, false, false);
            var label = player.Number?.ToString(CultureInfo.InvariantCulture) ?? "?";
            drawList.AddText(point + new Vector2(8.0f, 7.0f), ImGui.GetColorU32(player.Dead ? ModernMutedTextColor : ModernTextColor), label);
        }

        ImGui.PopClipRect();
    }

    private void DrawLimitCutTable(IReadOnlyList<LimitCutPlayer> players, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, WithBackgroundOpacity(ModernPanelAltColor, currentMainWindowBackgroundOpacity));
        if (ImGui.BeginChild("##LimitCutTablePanel", size, true, OptionalScrollbarFlags) &&
            ImGui.BeginTable("##LimitCutTable", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 28.0f);
            ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 52.0f);
            ImGui.TableSetupColumn("Stood", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Off by", ImGuiTableColumnFlags.WidthFixed, 60.0f);
            ImGui.TableHeadersRow();
            foreach (var player in players)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(player.Number?.ToString(CultureInfo.InvariantCulture) ?? "?");
                ImGui.TableNextColumn();
                ImGui.TextColored(ParseWtfDigColor(player.Job.Color), player.Dead ? $"{player.Job.Abbreviation} X" : player.Job.Abbreviation);
                ImGui.TableNextColumn();
                ImGui.TextDisabled(player.Angle is { } stood ? $"{Math.Round(stood * 2) / 2:0.#} deg" : "-");
                ImGui.TableNextColumn();
                ImGui.TextDisabled(player.ExpectedAngle is { } target ? $"{Math.Round(target * 2) / 2:0.#} deg" : "-");
                ImGui.TableNextColumn();
                var color = player.AngleError switch
                {
                    null => ModernMutedTextColor,
                    < 11.25 => HealColor,
                    < 22.5 => LeadUpGoldColor,
                    _ => DamageColor,
                };
                ImGui.TextColored(color, player.AngleError is { } error ? $"{Math.Round(error * 2) / 2:0.#} deg" : "-");
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private static string FormatLimitCutRotation(LimitCutRotation? rotation) => rotation switch
    {
        LimitCutRotation.Cw => "CW",
        LimitCutRotation.Ccw => "CCW",
        _ => "?",
    };

    private void DrawArrowsArena(
        IReadOnlyList<ArrowDrop> arrows,
        IReadOnlyList<ArrowStart> starts,
        IReadOnlyList<Vector2> expectedSlots,
        float size)
    {
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##ArrowsArena", new Vector2(size, size));
        var drawList = ImGui.GetWindowDrawList();
        var end = origin + new Vector2(size, size);
        var center = origin + new Vector2(size * 0.5f);
        var scale = (size * 0.5f) / 20.0f;
        Vector2 ToScreen(Vector2 point) => center + point * scale;
        ImGui.PushClipRect(origin, end, true);
        drawList.AddRectFilled(origin, end, ImGui.GetColorU32(ModernPanelAltColor with { W = 0.86f }), 4.0f);
        drawList.AddRect(origin, end, ImGui.GetColorU32(ModernPanelBorderColor), 4.0f);
        drawList.AddCircle(center, 20.0f * scale - 2.0f, ImGui.GetColorU32(ModernPanelBorderColor), 96, 1.2f);
        drawList.AddLine(new Vector2(center.X, origin.Y + 6.0f), new Vector2(center.X, end.Y - 6.0f), ImGui.GetColorU32(ModernDividerColor with { W = 0.34f }));
        drawList.AddLine(new Vector2(origin.X + 6.0f, center.Y), new Vector2(end.X - 6.0f, center.Y), ImGui.GetColorU32(ModernDividerColor with { W = 0.34f }));
        DrawForsakenWaymarks(drawList, ToScreen, scale);

        foreach (var slot in expectedSlots)
        {
            var point = ToScreen(slot);
            drawList.AddCircleFilled(point, 3.2f, ImGui.GetColorU32(ModernMutedTextColor with { W = 0.24f }), 16);
            drawList.AddCircle(point, 5.0f, ImGui.GetColorU32(ModernMutedTextColor with { W = 0.18f }), 16, 1.0f);
        }

        foreach (var start in starts.Where(entry => entry.Position is not null))
        {
            var point = ToScreen(start.Position!.Value);
            var color = start.Role == ArrowStartRole.Sleep
                ? new Vector4(1.0f, 0.84f, 0.0f, 1.0f)
                : new Vector4(1.0f, 0.21f, 0.71f, 1.0f);
            drawList.AddCircle(point, 11.0f, ImGui.GetColorU32(color with { W = 0.72f }), 24, 2.0f);
            DrawForsakenPlayerToken(drawList, point, start.Job, null, false, true);
        }

        foreach (var arrow in arrows.Where(entry => entry.Position is not null))
        {
            var point = ToScreen(arrow.Position!.Value);
            var color = ArrowDirectionColor(arrow.DirectionIndex);
            drawList.AddCircleFilled(point, ArrowsAnalyzer.ArrowAoeRadius * scale, ImGui.GetColorU32(color with { W = 0.11f }), 28);
            drawList.AddCircle(point, ArrowsAnalyzer.ArrowAoeRadius * scale, ImGui.GetColorU32(color with { W = 0.70f }), 28, 1.4f);
            var angle = arrow.DirectionIndex * MathF.PI / 2.0f;
            var direction = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
            var tip = point + direction * (ArrowsAnalyzer.ArrowAoeRadius * scale * 0.82f);
            var side = new Vector2(-direction.Y, direction.X);
            drawList.AddTriangleFilled(
                tip,
                point - direction * 4.0f + side * 4.0f,
                point - direction * 4.0f - side * 4.0f,
                ImGui.GetColorU32(color));
            var label = arrow.Job.Abbreviation;
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(point + new Vector2(7.0f, 6.0f) - labelSize * 0.5f, ImGui.GetColorU32(ParseWtfDigColor(arrow.Job.Color)), label);
        }

        ImGui.PopClipRect();
    }

    private void DrawArrowsTable(IReadOnlyList<ArrowDrop> arrows, IReadOnlyList<Vector2> expectedSlots, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, WithBackgroundOpacity(ModernPanelAltColor, currentMainWindowBackgroundOpacity));
        if (ImGui.BeginChild("##ArrowsTablePanel", size, true, OptionalScrollbarFlags) &&
            ImGui.BeginTable("##ArrowsTable", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 48.0f);
            ImGui.TableSetupColumn("Wave", ImGuiTableColumnFlags.WidthFixed, 44.0f);
            ImGui.TableSetupColumn("Direction", ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableSetupColumn("Position", ImGuiTableColumnFlags.WidthStretch, 1.1f);
            ImGui.TableSetupColumn("Error", ImGuiTableColumnFlags.WidthFixed, 54.0f);
            ImGui.TableHeadersRow();
            foreach (var arrow in arrows.OrderBy(entry => entry.Wave).ThenBy(entry => entry.Job.Role).ThenBy(entry => entry.Job.Abbreviation))
            {
                var error = ArrowsAnalyzer.Error(arrow.Position, expectedSlots);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(ParseWtfDigColor(arrow.Job.Color), arrow.Job.Abbreviation);
                ImGui.TableNextColumn();
                ImGui.TextDisabled(arrow.Wave.ToString(CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                ImGui.TextColored(ArrowDirectionColor(arrow.DirectionIndex), ArrowDirectionLabel(arrow.DirectionIndex));
                ImGui.TableNextColumn();
                ImGui.TextDisabled(arrow.Position is { } point ? $"{point.X:F1}, {point.Y:F1}" : "-");
                ImGui.TableNextColumn();
                var errorColor = error switch
                {
                    < 1.0f => HealColor,
                    < 2.0f => LeadUpGoldColor,
                    null => ModernMutedTextColor,
                    _ => DamageColor,
                };
                ImGui.TextColored(errorColor, error is { } distance ? $"{distance:F1}y" : "-");
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private static Vector4 ArrowDirectionColor(int direction) => direction switch
    {
        0 => new Vector4(0.36f, 0.61f, 1.0f, 1.0f),
        1 => new Vector4(1.0f, 0.62f, 0.23f, 1.0f),
        2 => new Vector4(0.75f, 0.48f, 1.0f, 1.0f),
        _ => new Vector4(0.37f, 0.83f, 0.55f, 1.0f),
    };

    private static string ArrowDirectionLabel(int direction) => direction switch
    {
        0 => "Up / N",
        1 => "Right / E",
        2 => "Down / S",
        _ => "Left / W",
    };

    private static void DrawForsakenSummary(ForsakenResolution resolution, ForsakenRotation? rotation)
    {
        var deaths = resolution.Players.Count(player => player.Died);
        var doubles = resolution.Players.Count(player => player.DoubleHit);
        ImGui.TextUnformatted(resolution.IsRaidwide
            ? resolution.Label
            : $"{resolution.Label} ({resolution.Parity})");
        if (!resolution.IsRaidwide)
        {
            ImGui.SameLine();
            var towerNames = resolution.Slots is { } pair
                ? $"{ForsakenSlotName(pair.First)} + {ForsakenSlotName(pair.Second)}"
                : "unknown";
            ImGui.TextColored(LeadUpGoldColor, $"towers {towerNames}");
            if (rotation is not null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"rotate {rotation.Direction.ToUpperInvariant()}");
            }
        }

        var shownCounts = resolution.Counts
            .Where(entry => entry.Value > 0 && entry.Key != ForsakenEffect.Other)
            .OrderBy(entry => entry.Key)
            .ToArray();
        for (var index = 0; index < shownCounts.Length; index++)
        {
            if (index > 0)
            {
                ImGui.SameLine();
            }

            var entry = shownCounts[index];
            ImGui.TextColored(ForsakenEffectColor(entry.Key), $"{entry.Value} {FormatForsakenEffect(entry.Key).ToLowerInvariant()}");
        }

        var details = new List<string>();
        if (resolution.Mismatch)
        {
            details.Add("a tower was not soaked by 2");
        }

        if (doubles > 0)
        {
            details.Add($"{doubles} took 2 effects (lethal)");
        }

        if (deaths > 0)
        {
            details.Add($"{deaths} dead");
        }

        if (details.Count == 0)
        {
            ImGui.TextDisabled(resolution.IsRaidwide
                ? "No death warning detected for this raidwide."
                : "No tower-soak, double-hit, or death warning detected for this set.");
        }
        else
        {
            ImGui.TextColored(DamageColor, string.Join(" | ", details));
        }
    }

    private void DrawForsakenControls(ForsakenResolution resolution)
    {
        var align = alignForsakenTowers;
        if (ImGui.Checkbox("Rotate towers to SW/SE##ForsakenAlign", ref align))
        {
            alignForsakenTowers = align;
        }

        if (resolution.CleaveSnapshot.Count > 0)
        {
            ImGui.SameLine();
            var snapshot = showForsakenCleaveSnapshot;
            if (ImGui.Checkbox("Cleave snapshot##ForsakenSnapshot", ref snapshot))
            {
                showForsakenCleaveSnapshot = snapshot;
            }
        }

        if (resolution.Clones.Count > 0)
        {
            ImGui.SameLine();
            foreach (var (label, value) in new[] { ("Clones", 0), ("Towers", 1), ("Both", 2) })
            {
                if (value > 0)
                {
                    ImGui.SameLine();
                }

                if (DrawThemedToggleButton(label, $"ForsakenView{value}", forsakenViewIndex == value))
                {
                    forsakenViewIndex = value;
                }
            }
        }
        else
        {
            forsakenViewIndex = 1;
        }
    }

    private void DrawForsakenPlayerTable(ForsakenResolution resolution, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, WithBackgroundOpacity(ModernPanelAltColor, currentMainWindowBackgroundOpacity));
        if (ImGui.BeginChild("##ForsakenPlayerTablePanel", size, true, OptionalScrollbarFlags))
        {
            if (ImGui.BeginTable(
                    "##ForsakenPlayerTable",
                    5,
                    ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 52.0f);
                ImGui.TableSetupColumn("Assign", ImGuiTableColumnFlags.WidthStretch, 1.15f);
                ImGui.TableSetupColumn("Tower", ImGuiTableColumnFlags.WidthFixed, 54.0f);
                ImGui.TableSetupColumn("Took", ImGuiTableColumnFlags.WidthStretch, 1.6f);
                ImGui.TableSetupColumn("Stacks", ImGuiTableColumnFlags.WidthFixed, 48.0f);
                ImGui.TableHeadersRow();
                foreach (var player in resolution.Players)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(ParseWtfDigColor(player.Job.Color), player.Died ? $"{player.Job.Abbreviation} X" : player.Job.Abbreviation);
                    ImGui.TableNextColumn();
                    var assignment = FormatForsakenAssignment(player.Assignment);
                    if (player.ReassignedTo is { } reassigned)
                    {
                        assignment += $" -> {FormatForsakenAssignment(reassigned)}";
                    }

                    ImGui.TextUnformatted(assignment);
                    ImGui.TableNextColumn();
                    ImGui.TextColored(player.SoakedTower ? HealColor : ModernMutedTextColor, player.SoakedTower ? "Yes" : "-");
                    ImGui.TableNextColumn();
                    if (player.Taken.Count == 0)
                    {
                        ImGui.TextDisabled("-");
                    }
                    else
                    {
                        var taken = string.Join(", ", player.Taken.Select(hit =>
                            $"{hit.Label ?? FormatForsakenEffect(hit.Effect)} {Math.Round(hit.Damage / 1000.0):N0}k"));
                        ImGui.TextColored(player.DoubleHit ? DamageColor : ModernTextColor, taken);
                    }

                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(player.TroubleStacks?.ToString(CultureInfo.InvariantCulture) ?? "-");
                }

                ImGui.EndTable();
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawForsakenArena(ForsakenResolution resolution, float size)
    {
        var start = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##ForsakenArena", new Vector2(size, size));
        var drawList = ImGui.GetWindowDrawList();
        var end = start + new Vector2(size, size);
        var center = start + new Vector2(size * 0.5f);
        const float extent = 14.0f;
        var scale = (size * 0.5f) / extent;
        var rotation = alignForsakenTowers && resolution.Slots is { } pair
            ? (3 - pair.First) * MathF.PI / 4.0f
            : 0.0f;
        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);
        Vector2 ToScreen(Vector2 point) => center + new Vector2(
            (point.X * cos - point.Y * sin) * scale,
            (point.X * sin + point.Y * cos) * scale);

        ImGui.PushClipRect(start, end, true);
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(ModernPanelAltColor with { W = 0.86f }), 4.0f);
        drawList.AddRect(start, end, ImGui.GetColorU32(ModernPanelBorderColor), 4.0f);
        drawList.AddLine(new Vector2(center.X, start.Y + 8.0f), new Vector2(center.X, end.Y - 8.0f), ImGui.GetColorU32(ModernDividerColor with { W = 0.34f }));
        drawList.AddLine(new Vector2(start.X + 8.0f, center.Y), new Vector2(end.X - 8.0f, center.Y), ImGui.GetColorU32(ModernDividerColor with { W = 0.34f }));
        drawList.AddCircle(center, ForsakenAnalyzer.TowerRadius * scale, ImGui.GetColorU32(ModernDividerColor with { W = 0.55f }), 64, 1.0f);
        drawList.AddCircle(center, (extent * scale) - 2.0f, ImGui.GetColorU32(ModernPanelBorderColor), 80, 1.0f);
        drawList.AddCircle(center, 6.0f * scale, ImGui.GetColorU32(DamageColor with { W = 0.30f }), 48, 1.0f);
        drawList.AddCircle(center, 5.0f * scale, ImGui.GetColorU32(DamageColor with { W = 0.30f }), 48, 1.0f);

        var showTowers = forsakenViewIndex is 1 or 2;
        var showClones = forsakenViewIndex is 0 or 2;
        if (showTowers)
        {
            foreach (var cleave in resolution.Cleaves)
            {
                DrawForsakenSector(drawList, ToScreen, cleave.Origin, cleave.Frontal ? cleave.FacingRadians : cleave.FacingRadians + Math.PI,
                    Math.PI / 2, extent + 3.0f, DamageColor with { W = 0.08f }, DamageColor with { W = 0.34f });
            }
        }

        DrawForsakenWaymarks(drawList, ToScreen, scale);
        foreach (var candidate in resolution.Candidates)
        {
            var point = ToScreen(candidate);
            var active = resolution.ActiveTowers.Any(tower => Vector2.Distance(tower, candidate) < 0.1f);
            if (active)
            {
                drawList.AddCircleFilled(point, ForsakenAnalyzer.TowerAoeRadius * scale, ImGui.GetColorU32(LeadUpGoldColor with { W = 0.08f }), 48);
                drawList.AddCircle(point, ForsakenAnalyzer.TowerAoeRadius * scale, ImGui.GetColorU32(LeadUpGoldColor with { W = 0.64f }), 48, 1.5f);
                drawList.AddCircleFilled(point, 2.0f, ImGui.GetColorU32(LeadUpGoldColor), 16);
            }
            else
            {
                drawList.AddCircleFilled(point, 3.0f, ImGui.GetColorU32(ModernMutedTextColor with { W = 0.12f }), 12);
            }
        }

        if (showClones)
        {
            foreach (var clone in resolution.Clones)
            {
                var point = ToScreen(clone.Position);
                var color = new Vector4(1.0f, 0.37f, 0.75f, 1.0f);
                drawList.AddCircleFilled(point, ForsakenAnalyzer.CloneAoeRadius * scale, ImGui.GetColorU32(color with { W = 0.08f }), 48);
                drawList.AddCircle(point, ForsakenAnalyzer.CloneAoeRadius * scale, ImGui.GetColorU32(color with { W = 0.70f }), 48, 1.2f);
            }
        }

        if (showTowers)
        {
            foreach (var circle in resolution.CircleAoes)
            {
                var color = ForsakenEffectColor(circle.Effect);
                var point = ToScreen(circle.Position);
                drawList.AddCircleFilled(point, ForsakenAnalyzer.SoakAoeRadius * scale, ImGui.GetColorU32(color with { W = 0.08f }), 48);
                drawList.AddCircle(point, ForsakenAnalyzer.SoakAoeRadius * scale, ImGui.GetColorU32(color with { W = 0.62f }), 48, 1.2f);
            }

            foreach (var cone in resolution.Cones)
            {
                DrawForsakenSector(drawList, ToScreen, cone.Origin, cone.FacingRadians, ForsakenAnalyzer.ConeHalfRadians,
                    extent + 2.0f, ForsakenEffectColor(ForsakenEffect.Cone) with { W = 0.13f }, ForsakenEffectColor(ForsakenEffect.Cone) with { W = 0.62f });
            }

            if (showForsakenCleaveSnapshot)
            {
                foreach (var ghost in resolution.CleaveSnapshot)
                {
                    DrawForsakenPlayerToken(drawList, ToScreen(ghost.Position), ghost.Job, null, false, true);
                }

                foreach (var cleave in resolution.Cleaves)
                {
                    var point = ToScreen(cleave.Origin);
                    drawList.AddQuadFilled(
                        point + new Vector2(0.0f, -6.0f),
                        point + new Vector2(6.0f, 0.0f),
                        point + new Vector2(0.0f, 6.0f),
                        point + new Vector2(-6.0f, 0.0f),
                        ImGui.GetColorU32(DamageColor with { W = 0.74f }));
                }
            }

            foreach (var player in resolution.Players.Where(player => player.Position is not null))
            {
                DrawForsakenPlayerToken(drawList, ToScreen(player.Position!.Value), player.Job, player, player.DiedThisSet || player.DoubleHit, false);
            }
        }
        else
        {
            foreach (var player in resolution.CloneSnapshot)
            {
                DrawForsakenPlayerToken(drawList, ToScreen(player.Position), player.Job, null, false, false);
            }
        }

        ImGui.PopClipRect();
    }

    private static void DrawForsakenSector(
        ImDrawListPtr drawList,
        Func<Vector2, Vector2> toScreen,
        Vector2 origin,
        double heading,
        double halfAngle,
        float radius,
        Vector4 fill,
        Vector4 border)
    {
        var screenOrigin = toScreen(origin);
        var points = new List<Vector2> { screenOrigin };
        const int segments = 20;
        for (var index = 0; index <= segments; index++)
        {
            var angle = heading - halfAngle + ((halfAngle * 2.0) * index / segments);
            points.Add(toScreen(origin + new Vector2((float)(radius * Math.Sin(angle)), (float)(radius * Math.Cos(angle)))));
        }

        for (var index = 2; index < points.Count; index++)
        {
            drawList.AddTriangleFilled(points[0], points[index - 1], points[index], ImGui.GetColorU32(fill));
        }

        for (var index = 2; index < points.Count; index++)
        {
            drawList.AddLine(points[index - 1], points[index], ImGui.GetColorU32(border), 1.0f);
        }

        drawList.AddLine(points[0], points[1], ImGui.GetColorU32(border), 1.0f);
        drawList.AddLine(points[0], points[^1], ImGui.GetColorU32(border), 1.0f);
    }

    private void DrawForsakenWaymarks(ImDrawListPtr drawList, Func<Vector2, Vector2> toScreen, float scale)
    {
        var waymarks = wtfDigWaymarkPresetIndex == 1
            ? new[]
            {
                ("A", new Vector2(0, -12)), ("B", new Vector2(12, 0)),
                ("C", new Vector2(0, 12)), ("D", new Vector2(-12, 0)),
                ("1", new Vector2(-8.765f, -8.765f)), ("2", new Vector2(8.628f, -8.765f)),
                ("3", new Vector2(8.628f, 8.628f)), ("4", new Vector2(-8.765f, 8.628f)),
            }
            : new[]
            {
                ("A", new Vector2(0, -12)), ("B", new Vector2(12, 0)),
                ("C", new Vector2(0, 12)), ("D", new Vector2(-12, 0)),
                ("1", new Vector2(-6, -6)), ("2", new Vector2(6, -6)),
                ("3", new Vector2(6, 6)), ("4", new Vector2(-6, 6)),
            };
        foreach (var (label, position) in waymarks)
        {
            var point = toScreen(position);
            var letters = char.IsLetter(label[0]);
            var color = label is "A" or "1"
                ? new Vector4(0.94f, 0.35f, 0.32f, 0.72f)
                : label is "B" or "2"
                    ? new Vector4(0.94f, 0.78f, 0.31f, 0.72f)
                    : label is "C" or "3"
                        ? new Vector4(0.31f, 0.64f, 0.94f, 0.72f)
                        : new Vector4(0.76f, 0.40f, 0.94f, 0.72f);
            if (letters)
            {
                var radius = MathF.Max(8.0f, 1.25f * scale);
                drawList.AddCircleFilled(point, radius, ImGui.GetColorU32(color with { W = 0.07f }), 24);
                drawList.AddCircle(point, radius, ImGui.GetColorU32(color with { W = 0.52f }), 24, 1.0f);
            }
            else
            {
                var half = MathF.Max(7.5f, 1.13333f * scale);
                drawList.AddRectFilled(point - new Vector2(half), point + new Vector2(half),
                    ImGui.GetColorU32(color with { W = 0.07f }), 1.0f);
                drawList.AddRect(point - new Vector2(half), point + new Vector2(half),
                    ImGui.GetColorU32(color with { W = 0.52f }), 1.0f);
            }

            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(point - (textSize * 0.5f), ImGui.GetColorU32(color), label);
        }
    }

    private static void DrawForsakenPlayerToken(
        ImDrawListPtr drawList,
        Vector2 point,
        WtfDigJobInfo job,
        ForsakenPlayerSnapshot? player,
        bool danger,
        bool ghost)
    {
        var alpha = ghost ? 0.38f : 1.0f;
        if (player is not null)
        {
            var hit = player.Taken.FirstOrDefault(entry => entry.Effect != ForsakenEffect.Raidwide);
            var ring = danger ? DamageColor : hit is null ? (Vector4?)null : ForsakenEffectColor(hit.Effect);
            if (ring is { } ringColor)
            {
                drawList.AddCircle(point, 12.0f, ImGui.GetColorU32(ringColor with { W = alpha }), 28, danger ? 3.0f : 2.0f);
            }
        }

        var color = ParseWtfDigColor(job.Color) with { W = alpha };
        drawList.AddCircleFilled(point, 8.5f, ImGui.GetColorU32(color), 24);
        drawList.AddCircle(point, 8.5f, ImGui.GetColorU32(new Vector4(0, 0, 0, alpha)), 24, 1.0f);
        var textSize = ImGui.CalcTextSize(job.Abbreviation);
        var textColor = GetReadableTextColorForBackground(color, 4.5f) with { W = alpha };
        drawList.AddText(point - (textSize * 0.5f), ImGui.GetColorU32(textColor), job.Abbreviation);
    }

    private static Vector4 ForsakenEffectColor(ForsakenEffect effect) => effect switch
    {
        ForsakenEffect.Stack => new Vector4(0.36f, 0.61f, 1.0f, 1.0f),
        ForsakenEffect.Spread => new Vector4(0.75f, 0.48f, 1.0f, 1.0f),
        ForsakenEffect.Cone => new Vector4(1.0f, 0.62f, 0.23f, 1.0f),
        ForsakenEffect.Clone => new Vector4(1.0f, 0.37f, 0.75f, 1.0f),
        ForsakenEffect.Cleave => new Vector4(1.0f, 0.30f, 0.30f, 1.0f),
        _ => new Vector4(0.60f, 0.63f, 0.68f, 1.0f),
    };

    private static Vector4 ParseWtfDigColor(string value)
    {
        if (value.Length != 7 || value[0] != '#' ||
            !byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return new Vector4(0.60f, 0.63f, 0.68f, 1.0f);
        }

        return new Vector4(red / 255.0f, green / 255.0f, blue / 255.0f, 1.0f);
    }

    private static string FormatForsakenAssignment(ForsakenAssignment? assignment) => assignment switch
    {
        ForsakenAssignment.Stack => "Stack",
        ForsakenAssignment.Spread => "Spread",
        ForsakenAssignment.Cone => "Cone",
        _ => "-",
    };

    private static string FormatForsakenEffect(ForsakenEffect effect) => effect switch
    {
        ForsakenEffect.Stack => "Stack",
        ForsakenEffect.Spread => "Spread",
        ForsakenEffect.Cone => "Cone",
        ForsakenEffect.Clone => "Clone",
        ForsakenEffect.Cleave => "Cleave",
        ForsakenEffect.Raidwide => "Raidwide",
        _ => "Other",
    };

    private static string FormatWtfDigTime(double seconds) => $"{Math.Max(0, (int)seconds) / 60}:{Math.Max(0, (int)seconds) % 60:00}";
    private static float GetWtfDigMapSize(float contentWidth)
    {
        var desired = contentWidth >= 820.0f ? contentWidth * 0.52f : contentWidth;
        return MathF.Min(560.0f, MathF.Max(1.0f, desired));
    }
    private static string ForsakenSlotName(int index) => new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[Math.Clamp(index, 0, 7)];
}

internal sealed class WtfDigAnalyzerController : IDisposable
{
    private readonly object sync = new();
    private readonly FflogsClient client = new();
    private IWtfDigEventSource? activeEventSource;
    private PullDeathSnapshot? pendingLocalPull;
    private CancellationTokenSource? cancellation;
    private long generation;
    private WtfDigAnalyzerViewState state = new(
        string.Empty,
        WtfDigAnalyzerCatalog.All.First(analyzer => analyzer.Key == "forsaken"),
        WtfDigAnalyzerSource.LocalPull,
        null,
        null,
        null,
        [],
        null,
        null,
        false,
        null);

    internal WtfDigAnalyzerViewState Snapshot()
    {
        lock (sync)
        {
            return state;
        }
    }

    internal void SetInput(string input)
    {
        lock (sync)
        {
            state = state with { Input = input };
        }
    }

    internal void SetSource(WtfDigAnalyzerSource source)
    {
        lock (sync)
        {
            if (state.Source == source)
            {
                return;
            }

            CancelOperationLocked();
            activeEventSource = source == WtfDigAnalyzerSource.Fflogs ? client : null;
            pendingLocalPull = null;
            state = state with
            {
                Source = source,
                LocalPullNumber = null,
                LocalPullCapturedAtTicks = null,
                Report = null,
                EligibleFights = [],
                SelectedFight = null,
                Analysis = null,
                Loading = false,
                Error = null,
            };
        }
    }

    internal void PrepareLocalPull(RecordedPullSummary summary)
    {
        lock (sync)
        {
            if (state.Source == WtfDigAnalyzerSource.LocalPull &&
                state.LocalPullNumber == summary.PullNumber &&
                state.LocalPullCapturedAtTicks == summary.CapturedAtUtc.Ticks)
            {
                return;
            }

            CancelOperationLocked();
            activeEventSource = null;
            pendingLocalPull = null;
            state = state with
            {
                Source = WtfDigAnalyzerSource.LocalPull,
                LocalPullNumber = summary.PullNumber,
                LocalPullCapturedAtTicks = summary.CapturedAtUtc.Ticks,
                Report = null,
                EligibleFights = [],
                SelectedFight = null,
                Analysis = null,
                Loading = false,
                Error = null,
            };
        }
    }

    internal void LoadLocalPull(PullDeathSnapshot pull)
    {
        WtfDigAnalyzerDefinition analyzer;
        lock (sync)
        {
            pendingLocalPull = pull;
            analyzer = state.Analyzer;
            state = state with
            {
                Source = WtfDigAnalyzerSource.LocalPull,
                LocalPullNumber = pull.PullNumber,
                LocalPullCapturedAtTicks = pull.CapturedAtUtc.Ticks,
            };
        }

        StartOperation(token => LoadLocalPullAsync(pull, analyzer, token), clearExisting: true);
    }

    internal void Load()
    {
        string input;
        WtfDigAnalyzerDefinition analyzer;
        lock (sync)
        {
            input = state.Input;
            analyzer = state.Analyzer;
            pendingLocalPull = null;
            activeEventSource = client;
            state = state with
            {
                Source = WtfDigAnalyzerSource.Fflogs,
                LocalPullNumber = null,
                LocalPullCapturedAtTicks = null,
            };
        }

        var parsed = FflogsClient.ParseReportInput(input);
        if (parsed is null)
        {
            lock (sync)
            {
                cancellation?.Cancel();
                cancellation?.Dispose();
                cancellation = null;
                generation++;
                state = state with
                {
                    Loading = false,
                    Error = "That does not look like an FFLogs report link or report code.",
                };
            }

            return;
        }

        StartOperation(token => LoadAsync(parsed, analyzer, token), clearExisting: true);
    }

    internal void SelectAnalyzer(WtfDigAnalyzerDefinition analyzer)
    {
        FflogsReportSummary? report;
        int? selectedFightId;
        FflogsReportInput? pendingInput;
        IWtfDigEventSource? eventSource;
        PullDeathSnapshot? localPull;
        WtfDigAnalyzerSource source;
        lock (sync)
        {
            source = state.Source;
            pendingInput = source == WtfDigAnalyzerSource.Fflogs && state.Loading
                ? FflogsClient.ParseReportInput(state.Input)
                : null;
            localPull = source == WtfDigAnalyzerSource.LocalPull ? pendingLocalPull : null;
            CancelOperationLocked();
            report = state.Report;
            selectedFightId = state.SelectedFight?.Id;
            eventSource = activeEventSource;
            state = state with
            {
                Analyzer = analyzer,
                EligibleFights = report is null ? [] : WtfDigAnalyzerCatalog.EligibleFights(report, analyzer),
                Analysis = null,
                Loading = false,
                Error = null,
            };
        }

        if (report is null)
        {
            if (localPull is not null)
            {
                StartOperation(token => LoadLocalPullAsync(localPull, analyzer, token), clearExisting: true);
                return;
            }

            if (pendingInput is not null)
            {
                StartOperation(token => LoadAsync(pendingInput, analyzer, token), clearExisting: true);
            }

            return;
        }

        if (eventSource is null)
        {
            return;
        }

        var eligible = source == WtfDigAnalyzerSource.LocalPull
            ? report.Fights
            : WtfDigAnalyzerCatalog.EligibleFights(report, analyzer);
        var selected = selectedFightId is { } currentFightId
            ? report.Fights.FirstOrDefault(fight => fight.Id == currentFightId)
            : eligible.LastOrDefault();
        if (selected is null)
        {
            lock (sync)
            {
                state = state with { SelectedFight = null };
            }

            return;
        }

        if (source == WtfDigAnalyzerSource.Fflogs && !eligible.Any(fight => fight.Id == selected.Id))
        {
            lock (sync)
            {
                state = state with
                {
                    SelectedFight = selected,
                    Error = $"This pull ({WtfDigAnalysisHelpers.FormatDuration(selected.DurationMs)}) is too short to reach {analyzer.MechanicLabel}. Pick a longer pull.",
                };
            }

            return;
        }

        StartOperation(token => AnalyzeAsync(eventSource, report, selected, analyzer, token), report, eligible, selected);
    }

    internal void SelectFight(int fightId)
    {
        FflogsReportSummary? report;
        WtfDigAnalyzerDefinition analyzer;
        IReadOnlyList<FflogsFight> eligible;
        IWtfDigEventSource? eventSource;
        lock (sync)
        {
            report = state.Report;
            analyzer = state.Analyzer;
            eligible = state.EligibleFights;
            eventSource = activeEventSource;
        }

        var fight = eligible.FirstOrDefault(candidate => candidate.Id == fightId);
        if (report is null || fight is null || eventSource is null)
        {
            return;
        }

        StartOperation(token => AnalyzeAsync(eventSource, report, fight, analyzer, token), report, eligible, fight);
    }

    public void Dispose()
    {
        lock (sync)
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            generation++;
        }

        client.Dispose();
    }

    private async Task<WtfDigOperationResult> LoadLocalPullAsync(
        PullDeathSnapshot pull,
        WtfDigAnalyzerDefinition analyzer,
        CancellationToken token)
    {
        var localSource = await Task.Run(() => LocalPullEventSource.Create(pull), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return await AnalyzeAsync(localSource, localSource.Report, localSource.Fight, analyzer, token)
            .ConfigureAwait(false);
    }

    private async Task<WtfDigOperationResult> LoadAsync(
        FflogsReportInput input,
        WtfDigAnalyzerDefinition analyzer,
        CancellationToken token)
    {
        var report = await client.FetchReportSummaryAsync(input.Code, token).ConfigureAwait(false);
        var eligible = WtfDigAnalyzerCatalog.EligibleFights(report, analyzer);
        FflogsFight? fight;
        if (input.FightId is { } fightId)
        {
            fight = report.Fights.FirstOrDefault(candidate => candidate.Id == fightId);
            if (fight is null)
            {
                throw new InvalidOperationException("That pull was not found in this report.");
            }

            if (!eligible.Any(candidate => candidate.Id == fight.Id))
            {
                return new WtfDigOperationResult(
                    report,
                    eligible,
                    fight,
                    null,
                    $"This pull ({WtfDigAnalysisHelpers.FormatDuration(fight.DurationMs)}) is too short to reach {analyzer.MechanicLabel}. Pick a longer pull.",
                    client);
            }
        }
        else
        {
            fight = eligible.LastOrDefault();
        }

        if (fight is null)
        {
            return new WtfDigOperationResult(report, eligible, null, null, null, client);
        }

        return await AnalyzeAsync(client, report, fight, analyzer, token).ConfigureAwait(false);
    }

    private async Task<WtfDigOperationResult> AnalyzeAsync(
        IWtfDigEventSource eventSource,
        FflogsReportSummary report,
        FflogsFight fight,
        WtfDigAnalyzerDefinition analyzer,
        CancellationToken token)
    {
        object analysis = analyzer.Key switch
        {
            "arrows" => await new ArrowsAnalyzer(eventSource).AnalyzeAsync(report, fight, token).ConfigureAwait(false),
            "forsaken" => await new ForsakenAnalyzer(eventSource).AnalyzeAsync(report, fight, token).ConfigureAwait(false),
            "kefka-lc" => await new LimitCutAnalyzer(eventSource).AnalyzeAsync(report, fight, token).ConfigureAwait(false),
            "black-hole" => await new BlackHoleAnalyzer(eventSource).AnalyzeAsync(report, fight, token).ConfigureAwait(false),
            "kefka-says" => await new P4Analyzer(eventSource).AnalyzeAsync(report, fight, token).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown analyzer: {analyzer.Label}."),
        };
        var error = analysis switch
        {
            ForsakenAnalysis { Resolutions.Count: 0 } => "No Forsaken mechanic was found in this pull.",
            ArrowsAnalysis { Waves.Count: 0 } => "No Tele-Portent arrows were found in this pull.",
            LimitCutAnalysis { Kefka: null } => "No Limit Cut (Ultima Blaster) was found in this pull.",
            BlackHoleAnalysis { Tethers.Count: 0 } => "No Black Hole (Nothingness) was found in this pull.",
            P4Analysis { Rounds.Count: 0 } => "No Kefka Says was found in this pull.",
            _ => null,
        };
        var eligible = eventSource is LocalPullEventSource ? report.Fights : WtfDigAnalyzerCatalog.EligibleFights(report, analyzer);
        return new WtfDigOperationResult(report, eligible, fight, analysis, error, eventSource);
    }

    private void StartOperation(
        Func<CancellationToken, Task<WtfDigOperationResult>> operation,
        FflogsReportSummary? report = null,
        IReadOnlyList<FflogsFight>? eligible = null,
        FflogsFight? selectedFight = null,
        bool clearExisting = false)
    {
        CancellationToken token;
        long currentGeneration;
        lock (sync)
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            token = cancellation.Token;
            currentGeneration = ++generation;
            state = state with
            {
                Report = clearExisting ? null : report ?? state.Report,
                EligibleFights = clearExisting ? [] : eligible ?? state.EligibleFights,
                SelectedFight = clearExisting ? null : selectedFight ?? state.SelectedFight,
                Loading = true,
                Analysis = null,
                Error = null,
            };
        }

        _ = CompleteOperationAsync(currentGeneration, operation, token);
    }

    private async Task CompleteOperationAsync(
        long operationGeneration,
        Func<CancellationToken, Task<WtfDigOperationResult>> operation,
        CancellationToken token)
    {
        try
        {
            var result = await operation(token).ConfigureAwait(false);
            lock (sync)
            {
                if (generation != operationGeneration || token.IsCancellationRequested)
                {
                    return;
                }

                state = state with
                {
                    Report = result.Report,
                    EligibleFights = result.EligibleFights,
                    SelectedFight = result.SelectedFight,
                    Analysis = result.Analysis,
                    Loading = false,
                    Error = result.Error,
                };
                activeEventSource = result.EventSource;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (sync)
            {
                if (generation == operationGeneration)
                {
                    state = state with { Loading = false, Error = exception.Message };
                }
            }
        }
    }

    private void CancelOperationLocked()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        generation++;
    }
}

internal enum WtfDigAnalyzerSource
{
    LocalPull,
    Fflogs,
}

internal sealed record WtfDigAnalyzerViewState(
    string Input,
    WtfDigAnalyzerDefinition Analyzer,
    WtfDigAnalyzerSource Source,
    long? LocalPullNumber,
    long? LocalPullCapturedAtTicks,
    FflogsReportSummary? Report,
    IReadOnlyList<FflogsFight> EligibleFights,
    FflogsFight? SelectedFight,
    object? Analysis,
    bool Loading,
    string? Error);

internal sealed record WtfDigOperationResult(
    FflogsReportSummary Report,
    IReadOnlyList<FflogsFight> EligibleFights,
    FflogsFight? SelectedFight,
    object? Analysis,
    string? Error,
    IWtfDigEventSource? EventSource = null);
