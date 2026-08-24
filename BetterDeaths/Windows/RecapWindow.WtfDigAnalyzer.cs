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
    private const float WtfDigMapDefaultSide = 680.0f;
    private const float WtfDigMapMinSide = 280.0f;
    private const float WtfDigMapMaxSide = 820.0f;
    private const float WtfDigTableMinWidth = 260.0f;
    private static readonly Vector4 LimitCutKefkaRotationColor = new(0.94f, 0.53f, 0.03f, 1.0f);
    private static readonly Vector4 LimitCutPlayerRotationColor = new(0.50f, 0.84f, 0.88f, 1.0f);
    private static readonly Vector2[] WtfDigTextOutlineOffsets =
    [
        new(-1.0f, -1.0f),
        new(0.0f, -1.0f),
        new(1.0f, -1.0f),
        new(-1.0f, 0.0f),
        new(1.0f, 0.0f),
        new(-1.0f, 1.0f),
        new(0.0f, 1.0f),
        new(1.0f, 1.0f),
    ];

    private readonly WtfDigAnalyzerController wtfDigAnalyzer = new();
    private long? selectedWtfDigLocalPullNumber;
    private long? selectedWtfDigLocalPullCapturedAtTicks;
    private int selectedForsakenResolution;
    private int? selectedForsakenCleave;
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
    private bool wtfDigMapResizeDragging;

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
            selectedForsakenCleave = null;
            forsakenViewIndex = 1;
            selectedArrowWave = -1;
            selectedBlackHoleTether = 0;
        }
        ImGui.TextColored(ModernAccentColor, "WTF.DIG Analyzer");
        ImGui.TextColored(LeadUpGoldColor with { W = 0.88f }, "Powered by Mczub");
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

        DrawWtfDigLocalDataQuality(state);

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
            if (state.Source == WtfDigAnalyzerSource.LocalPull && state.Analysis is not null)
            {
                ImGui.Dummy(new Vector2(1.0f, 5.0f));
                DrawWtfDigReplayJump(state.Analysis);
            }
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

    private void DrawWtfDigReplayJump(object analysis)
    {
        var targetSeconds = analysis switch
        {
            ArrowsAnalysis arrows when arrows.Waves.Count > 0 =>
                arrows.Waves[Math.Clamp(selectedArrowWave < 0 ? 0 : selectedArrowWave, 0, arrows.Waves.Count - 1)].Time,
            ForsakenAnalysis forsaken when forsaken.Resolutions.Count > 0 =>
                forsaken.Resolutions[Math.Clamp(selectedForsakenResolution, 0, forsaken.Resolutions.Count - 1)].SnapshotTime,
            LimitCutAnalysis { FinalBlastTime: { } finalBlastTime } => finalBlastTime,
            BlackHoleAnalysis blackHole when blackHole.Tethers.Count > 0 =>
                blackHole.Tethers[Math.Clamp(selectedBlackHoleTether, 0, blackHole.Tethers.Count - 1)].Time,
            P4Analysis { KefkaSaysTime: { } kefkaSaysTime } => kefkaSaysTime,
            _ => 0.0,
        };
        if (DrawThemedActionButton("Open this moment in Replay", "WtfDigOpenReplay"))
        {
            OpenWtfDigReplayAt((float)targetSeconds);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Opens the full Better Deaths replay near the mechanic currently shown here.");
        }
    }

    private void OpenWtfDigReplayAt(float pullElapsedSeconds)
    {
        var summary = plugin.RecordedPulls.FirstOrDefault(candidate =>
            candidate.PullNumber == selectedWtfDigLocalPullNumber &&
            candidate.CapturedAtUtc.Ticks == selectedWtfDigLocalPullCapturedAtTicks);
        if (summary is null)
        {
            return;
        }

        selectedReplayPullKey = BuildRecordedPullKey(summary);
        replayFocusDeathSelection = null;
        currentMainPage = MainPage.Replay;
        var replayId = BuildReplayViewerId(summary);
        replayScrubSecondsByDeathId[replayId] = MathF.Max(0.0f, pullElapsedSeconds - 2.0f);
        replayPlayingByDeathId[replayId] = false;
    }

    private static void DrawWtfDigLocalDataQuality(WtfDigAnalyzerViewState state)
    {
        if (state.Source != WtfDigAnalyzerSource.LocalPull ||
            !state.LocalAvailability.TryGetValue(state.Analyzer.Key, out var availability) ||
            availability.Quality == WtfDigLocalDataQuality.Unavailable)
        {
            return;
        }

        ImGui.Dummy(new Vector2(1.0f, 4.0f));
        var label = availability.Quality == WtfDigLocalDataQuality.Exact
            ? "Recorded mechanic data"
            : "Estimated timing from nearby fight data";
        var color = availability.Quality == WtfDigLocalDataQuality.Exact
            ? HealColor
            : LeadUpGoldColor;
        ImGui.TextColored(color, label);
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(availability.Summary);
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
                selectedForsakenCleave = null;
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
                            selectedForsakenCleave = null;
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
                selectedForsakenCleave = null;
                forsakenViewIndex = 1;
                resolution = candidate;
            }
        }

        ImGui.Dummy(new Vector2(1.0f, 6.0f));
        DrawForsakenSummary(resolution, analysis.Rotation);
        DrawForsakenControls(resolution);

        DrawWtfDigMapTableLayout(
            "Forsaken",
            MeasureForsakenTableWidth(resolution),
            mapSize => DrawForsakenArena(resolution, mapSize),
            tableSize => DrawForsakenPlayerTable(resolution, tableSize));

        DrawWtfDigInlineLegend(
        [
            ("Stack", ForsakenEffectColor(ForsakenEffect.Stack)),
            ("Spread", ForsakenEffectColor(ForsakenEffect.Spread)),
            ("Cone", ForsakenEffectColor(ForsakenEffect.Cone)),
            ("Clone", ForsakenEffectColor(ForsakenEffect.Clone)),
            ("Cleave", ForsakenEffectColor(ForsakenEffect.Cleave)),
        ]);
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
        DrawWtfDigMapTableLayout(
            "Arrows",
            MeasureArrowsTableWidth(shown, slots),
            mapSize => DrawArrowsArena(shown, showArrowStarts ? analysis.Starts : [], slots, mapSize),
            tableSize => DrawArrowsTable(shown, slots, tableSize));

        DrawWtfDigInlineLegend(
        [
            ("Up / N", ArrowDirectionColor(0)),
            ("Right / E", ArrowDirectionColor(1)),
            ("Down / S", ArrowDirectionColor(2)),
            ("Left / W", ArrowDirectionColor(3)),
            ("Sleep start", new Vector4(1.0f, 0.84f, 0.0f, 1.0f)),
            ("Confused start", new Vector4(1.0f, 0.21f, 0.71f, 1.0f)),
        ]);
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
        ImGui.TextColored(LimitCutKefkaRotationColor, kefka.StartName);
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.TextUnformatted(" rotating ");
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.TextColored(LimitCutKefkaRotationColor, FormatLimitCutRotation(kefka.Rotation));
        ImGui.SameLine();
        ImGui.TextColored(LimitCutPlayerRotationColor, $"Players rotate {FormatLimitCutRotation(analysis.PlayerRotation)}");
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
        DrawWtfDigMapTableLayout(
            "LimitCut",
            MeasureLimitCutTableWidth(analysis.Players),
            mapSize => DrawLimitCutArena(analysis, mapSize),
            tableSize => DrawLimitCutTable(analysis.Players, tableSize));

        ImGui.Dummy(new Vector2(1.0f, 4.0f));
        DrawWtfDigInlineLegend(
        [
            ("Kefka start and rotation", LimitCutKefkaRotationColor),
            ("Expected player positions", LimitCutPlayerRotationColor),
            ("Final clones", DamageColor),
        ]);
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
        DrawBlackHoleTetherSelector(analysis, ref tether);

        ImGui.Dummy(new Vector2(1.0f, 4.0f));
        var actualSoaks = tether.States.Sum(state => state.HitsThisTether);
        var expectedSoaks = BlackHoleAnalyzer.ExpectedSoaks(tether.Set, tether.Tether);
        ImGui.TextUnformatted($"{tether.Label} - {FormatWtfDigTime(tether.Time)}");
        if (actualSoaks > expectedSoaks)
        {
            ImGui.SameLine();
            ImGui.TextColored(LeadUpGoldColor, $"{actualSoaks} soaks (expected {expectedSoaks})");
        }

        DrawWtfDigMapTableLayout(
            "BlackHole",
            MeasureBlackHoleTableWidth(tether, analysis.Players),
            mapSize => DrawBlackHoleArena(tether, analysis.Players, mapSize),
            tableSize => DrawBlackHoleTable(tether, analysis.Players, tableSize));

        DrawWtfDigInlineLegend(
        [
            ("Unbecoming: hit once", new Vector4(0.36f, 0.61f, 1.0f, 1.0f)),
            ("Meanest: hit twice or more", new Vector4(0.77f, 0.80f, 1.0f, 1.0f)),
            ("Primordial Crust", LeadUpGoldColor),
            ("Black hole", new Vector4(0.63f, 0.42f, 1.0f, 1.0f)),
            ("Tether holder", ModernTextColor),
        ]);
    }

    private void DrawBlackHoleTetherSelector(BlackHoleAnalysis analysis, ref BlackHoleTether tether)
    {
        const float groupPadding = 4.0f;
        const float buttonSpacing = 2.0f;
        var groups = analysis.Tethers
            .Select((candidate, index) => (Candidate: candidate, Index: index))
            .GroupBy(item => item.Candidate.Set)
            .OrderBy(group => group.Key)
            .ToArray();
        var available = ImGui.GetContentRegionAvail().X;
        var outerSpacing = ImGui.GetStyle().ItemSpacing.X;
        var rowWidth = 0.0f;

        foreach (var group in groups)
        {
            var items = group.OrderBy(item => item.Candidate.Tether).ToArray();
            var labels = items.Select(item =>
            {
                var candidate = item.Candidate;
                var soaks = candidate.States.Sum(state => state.HitsThisTether);
                var expected = BlackHoleAnalyzer.ExpectedSoaks(candidate.Set, candidate.Tether);
                var warning = candidate.States.Any(state => state.DiedThisTether) || soaks > expected;
                return $"Tether {candidate.Tether}{(warning ? " !" : string.Empty)}";
            }).ToArray();
            var widths = labels.Select(GetThemedActionButtonWidth).ToArray();
            var selectorWidth = (groupPadding * 2.0f) + widths.Sum() +
                (buttonSpacing * Math.Max(0, items.Length - 1));
            var setLabel = items[0].Candidate.Label.Split(',', 2)[0].Trim().ToUpperInvariant();
            var groupWidth = MathF.Max(selectorWidth, ImGui.CalcTextSize(setLabel).X);

            if (rowWidth > 0.0f && rowWidth + outerSpacing + groupWidth <= available)
            {
                ImGui.SameLine(0.0f, outerSpacing);
                rowWidth += outerSpacing + groupWidth;
            }
            else
            {
                rowWidth = groupWidth;
            }

            ImGui.BeginGroup();
            ImGui.TextColored(ModernMutedTextColor, setLabel);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, WithBackgroundOpacity(ModernPanelAltColor, currentMainWindowBackgroundOpacity));
            ImGui.PushStyleColor(ImGuiCol.Border, ModernPanelBorderColor);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(groupPadding));
            var selectorHeight = ImGui.GetFrameHeight() + (groupPadding * 2.0f) + 2.0f;
            if (ImGui.BeginChild(
                    $"##BlackHoleSet{group.Key}",
                    new Vector2(groupWidth, selectorHeight),
                    true,
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
                {
                    if (itemIndex > 0)
                    {
                        ImGui.SameLine(0.0f, buttonSpacing);
                    }

                    var item = items[itemIndex];
                    var candidate = item.Candidate;
                    var soaks = candidate.States.Sum(state => state.HitsThisTether);
                    var expected = BlackHoleAnalyzer.ExpectedSoaks(candidate.Set, candidate.Tether);
                    var death = candidate.States.Any(state => state.DiedThisTether);
                    var over = soaks > expected;
                    if (DrawBlackHoleTetherButton(
                            labels[itemIndex],
                            $"BlackHoleTether{item.Index}",
                            item.Index == selectedBlackHoleTether,
                            widths[itemIndex]))
                    {
                        selectedBlackHoleTether = item.Index;
                        tether = candidate;
                    }

                    if (ImGui.IsItemHovered() && (death || over))
                    {
                        var details = new List<string>();
                        if (death)
                        {
                            details.Add("Death on this tether");
                        }

                        if (over)
                        {
                            details.Add($"{soaks} soaks (expected {expected})");
                        }

                        SetThemedTooltip(string.Join("\n", details));
                    }
                }
            }

            ImGui.EndChild();
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
            ImGui.EndGroup();
        }
    }

    private static bool DrawBlackHoleTetherButton(string label, string id, bool selected, float width)
    {
        var buttonColor = selected ? ModernNavButtonSelectedColor : Vector4.Zero;
        var hoveredColor = selected
            ? ModernNavButtonSelectedHoveredColor
            : ModernNavButtonHoveredColor;
        var textColor = selected
            ? GetButtonTextColor(ModernNavButtonSelectedColor, selected: true)
            : ModernMutedTextColor;

        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ModernNavButtonActiveColor);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f);
        var clicked = ImGui.Button($"{label}##{id}", new Vector2(width, ImGui.GetFrameHeight()));
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);
        return clicked;
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

        DrawWtfDigMutedWrapped("Shows casts, debuffs, and each player's assignment. Check the original log or recording when exact timing matters.");
    }

    private static void DrawWtfDigInlineLegend(IReadOnlyList<(string Label, Vector4 Color)> items)
    {
        ImGui.Dummy(new Vector2(1.0f, 5.0f));
        var available = MathF.Max(1.0f, ImGui.GetContentRegionAvail().X);
        var rowWidth = 0.0f;
        foreach (var (label, color) in items)
        {
            var width = ImGui.CalcTextSize(label).X;
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            if (rowWidth > 0.0f && rowWidth + spacing + width <= available)
            {
                ImGui.SameLine();
                rowWidth += spacing + width;
            }
            else
            {
                rowWidth = width;
            }

            ImGui.TextColored(color, label);
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

        ImGui.TextDisabled(label);
        foreach (var target in targets)
        {
            ImGui.SameLine();
            DrawWtfDigJobBadge(target.Job, target.Job.Abbreviation);
        }
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
            DrawWtfDigJobBadge(wound.Job, wound.Job.Abbreviation);
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
                var available = ImGui.GetContentRegionAvail().X;
                var label = $"{group.Key}:";
                var rowWidth = ImGui.CalcTextSize(label).X;
                ImGui.TextColored(group.Any(player => player.Danger) ? LeadUpGoldColor : ModernMutedTextColor,
                    label);
                foreach (var player in group)
                {
                    var width = ImGui.CalcTextSize(player.Job.Abbreviation).X + 10.0f;
                    var spacing = ImGui.GetStyle().ItemSpacing.X;
                    if (rowWidth + spacing + width <= available)
                    {
                        ImGui.SameLine();
                        rowWidth += spacing + width;
                    }
                    else
                    {
                        rowWidth = width;
                    }

                    DrawWtfDigJobBadge(player.Job, player.Job.Abbreviation);
                }

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
        var positionById = tether.States
            .Where(state => state.Position is not null)
            .ToDictionary(state => state.ActorId, state => state.Position!.Value);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##BlackHoleArena", new Vector2(size, size));
        ImGui.SetItemAllowOverlap();
        var drawList = ImGui.GetWindowDrawList();
        var end = origin + new Vector2(size, size);
        var center = origin + new Vector2(size * 0.5f);
        const float extent = BlackHoleAnalyzer.ArenaRadius + 6.0f;
        var scale = (size * 0.5f) / extent;
        Vector2 ToScreen(Vector2 point) => center + point * scale;
        ImGui.PushClipRect(origin, end, true);
        drawList.AddRectFilled(origin, end, ImGui.GetColorU32(new Vector4(0.067f, 0.078f, 0.106f, 1.0f)), 4.0f);
        drawList.AddRect(origin, end, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.10f)), 4.0f);
        drawList.AddCircleFilled(
            center,
            BlackHoleAnalyzer.ArenaRadius * scale,
            ImGui.GetColorU32(new Vector4(0.051f, 0.063f, 0.086f, 1.0f)),
            96);
        drawList.AddCircle(
            center,
            BlackHoleAnalyzer.ArenaRadius * scale,
            ImGui.GetColorU32(new Vector4(0.165f, 0.184f, 0.227f, 1.0f)),
            96,
            1.5f);
        drawList.AddLine(
            center - new Vector2(0.0f, BlackHoleAnalyzer.ArenaRadius * scale),
            center + new Vector2(0.0f, BlackHoleAnalyzer.ArenaRadius * scale),
            ImGui.GetColorU32(new Vector4(0.110f, 0.125f, 0.161f, 1.0f)));
        drawList.AddLine(
            center - new Vector2(BlackHoleAnalyzer.ArenaRadius * scale, 0.0f),
            center + new Vector2(BlackHoleAnalyzer.ArenaRadius * scale, 0.0f),
            ImGui.GetColorU32(new Vector4(0.110f, 0.125f, 0.161f, 1.0f)));
        drawList.AddCircleFilled(
            center,
            3.0f,
            ImGui.GetColorU32(new Vector4(0.227f, 0.255f, 0.314f, 1.0f)),
            16);
        DrawForsakenWaymarks(drawList, ToScreen, scale);

        if (tether.BigKefka is { } bigKefka)
        {
            var point = ToScreen(bigKefka);
            var fill = new Vector4(0.10f, 0.04f, 0.16f, 1.0f);
            var stroke = new Vector4(0.78f, 0.36f, 1.0f, 1.0f);
            var text = new Vector4(0.84f, 0.61f, 1.0f, 1.0f);
            drawList.AddCircleFilled(point, 14.0f, ImGui.GetColorU32(fill), 28);
            drawList.AddCircle(point, 14.0f, ImGui.GetColorU32(stroke), 28, 2.5f);
            DrawWtfDigCenteredText(drawList, point, "K", text, 0.86f);
        }

        if (showBlackHoleBosses)
        {
            if (tether.Chaos is { } chaos)
            {
                DrawBlackHoleBossMarker(
                    drawList,
                    ToScreen(chaos),
                    BlackHoleAnalyzer.ChaosRadius * scale,
                    new Vector4(1.0f, 0.48f, 0.30f, 1.0f),
                    "C");
            }

            if (tether.Exdeath is { } exdeath)
            {
                DrawBlackHoleBossMarker(
                    drawList,
                    ToScreen(exdeath),
                    BlackHoleAnalyzer.ExdeathRadius * scale,
                    new Vector4(0.37f, 0.83f, 0.55f, 1.0f),
                    "X");
            }
        }

        var beamColor = new Vector4(0.63f, 0.42f, 1.0f, 1.0f);
        foreach (var beam in tether.Beams)
        {
            var beamOrigin = ToScreen(beam.Origin);
            var direction = new Vector2((float)Math.Sin(beam.FacingRadians), (float)Math.Cos(beam.FacingRadians));
            var perpendicular = new Vector2(-direction.Y, direction.X);
            var beamStart = ToScreen(beam.Origin - (direction * 12.0f));
            var beamEnd = ToScreen(beam.Origin + (direction * ((BlackHoleAnalyzer.ArenaRadius * 2.0f) + 6.0f)));
            var halfWidth = 3.0f * scale;
            var first = beamStart + perpendicular * halfWidth;
            var second = beamEnd + perpendicular * halfWidth;
            var third = beamEnd - perpendicular * halfWidth;
            var fourth = beamStart - perpendicular * halfWidth;
            drawList.AddQuadFilled(
                first,
                second,
                third,
                fourth,
                ImGui.GetColorU32(beamColor with { W = 0.11f }));
            drawList.AddQuad(
                first,
                second,
                third,
                fourth,
                ImGui.GetColorU32(beamColor with { W = 0.27f }),
                1.0f);
            drawList.AddCircleFilled(beamOrigin, 8.0f, ImGui.GetColorU32(new Vector4(0.09f, 0.04f, 0.15f, 1.0f)), 24);
            drawList.AddCircle(beamOrigin, 8.0f, ImGui.GetColorU32(beamColor), 24, 2.0f);
            drawList.AddCircleFilled(beamOrigin, 3.0f, ImGui.GetColorU32(beamColor), 16);
        }

        foreach (var beam in tether.Beams)
        {
            var beamOrigin = ToScreen(beam.Origin);
            foreach (var hit in beam.Hits)
            {
                if (!positionById.TryGetValue(hit.ActorId, out var position))
                {
                    continue;
                }

                var playerPosition = ToScreen(position);
                if (hit.ActorId == beam.TetherHolder)
                {
                    drawList.AddLine(
                        beamOrigin,
                        playerPosition,
                        ImGui.GetColorU32(hit.Lethal ? new Vector4(1.0f, 0.42f, 0.42f, 1.0f) : Vector4.One),
                        2.5f);
                }
                else
                {
                    DrawWtfDigDashedLine(
                        drawList,
                        beamOrigin,
                        playerPosition,
                        new Vector4(0.54f, 0.58f, 0.65f, 0.33f),
                        1.0f,
                        3.0f,
                        3.0f);
                }
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

            DrawBlackHolePlayerToken(drawList, point, player, state);
        }

        ImGui.PopClipRect();
    }

    private static void DrawBlackHoleBossMarker(
        ImDrawListPtr drawList,
        Vector2 point,
        float radius,
        Vector4 color,
        string label)
    {
        DrawWtfDigDashedCircle(drawList, point, radius, color with { W = 0.80f }, 1.5f, 4, 3);
        drawList.AddCircleFilled(point, 3.5f, ImGui.GetColorU32(color), 16);
        DrawWtfDigCenteredText(
            drawList,
            point - new Vector2(0.0f, radius + 8.0f),
            label,
            color,
            0.86f);
    }

    private static void DrawBlackHolePlayerToken(
        ImDrawListPtr drawList,
        Vector2 point,
        BlackHolePlayerInfo player,
        BlackHolePlayerState state)
    {
        var alpha = state.Dead ? 0.50f : 1.0f;
        if (state.HitsThisTether > 0)
        {
            var emphasis = state.TetherThisTether
                ? Vector4.One
                : new Vector4(0.96f, 0.77f, 0.32f, 1.0f);
            drawList.AddCircle(point, 16.0f, ImGui.GetColorU32(emphasis with { W = alpha }), 28, 2.0f);
        }

        if (state.Crust)
        {
            DrawWtfDigDashedCircle(
                drawList,
                point,
                14.0f,
                new Vector4(0.89f, 0.76f, 0.31f, alpha),
                1.5f,
                3,
                2);
        }

        var levelColor = state.Level switch
        {
            NothingLevel.Unbecoming => new Vector4(0.36f, 0.61f, 1.0f, alpha),
            NothingLevel.Meanest => new Vector4(0.77f, 0.80f, 1.0f, alpha),
            _ => new Vector4(0.36f, 0.39f, 0.45f, alpha),
        };
        drawList.AddCircle(point, 12.0f, ImGui.GetColorU32(levelColor), 28, 2.5f);

        var jobColor = ParseWtfDigColor(player.Job.Color) with { W = alpha };
        drawList.AddCircleFilled(point, 9.5f, ImGui.GetColorU32(jobColor), 24);
        drawList.AddCircle(point, 9.5f, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, alpha)), 24, 1.0f);
        DrawWtfDigCenteredText(
            drawList,
            point,
            BlackHoleRoleTag(player.Role),
            new Vector4(0.04f, 0.05f, 0.07f, alpha),
            0.72f);
        DrawWtfDigCenteredText(
            drawList,
            point - new Vector2(0.0f, 18.0f),
            player.Job.Abbreviation,
            jobColor,
            0.72f);
        if (state.Dead)
        {
            DrawWtfDigCenteredText(
                drawList,
                point + new Vector2(11.0f, -8.0f),
                "X",
                DamageColor with { W = alpha },
                0.64f);
        }
    }

    private static void DrawWtfDigCenteredText(
        ImDrawListPtr drawList,
        Vector2 center,
        string text,
        Vector4 color,
        float scale)
    {
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize() * scale;
        var textSize = ImGui.CalcTextSize(text) * scale;
        drawList.AddText(font, fontSize, center - (textSize * 0.5f), ImGui.GetColorU32(color), text);
    }

    private static void DrawWtfDigDashedLine(
        ImDrawListPtr drawList,
        Vector2 start,
        Vector2 end,
        Vector4 color,
        float thickness,
        float dashLength,
        float gapLength)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length <= 0.01f)
        {
            return;
        }

        var direction = delta / length;
        for (var distance = 0.0f; distance < length; distance += dashLength + gapLength)
        {
            var segmentEnd = MathF.Min(length, distance + dashLength);
            drawList.AddLine(
                start + direction * distance,
                start + direction * segmentEnd,
                ImGui.GetColorU32(color),
                thickness);
        }
    }

    private static void DrawWtfDigDashedCircle(
        ImDrawListPtr drawList,
        Vector2 center,
        float radius,
        Vector4 color,
        float thickness,
        int drawnSegments,
        int gapSegments)
    {
        const int segments = 96;
        var patternLength = Math.Max(1, drawnSegments + gapSegments);
        for (var index = 0; index < segments; index++)
        {
            if (index % patternLength >= drawnSegments)
            {
                continue;
            }

            var firstAngle = MathF.Tau * index / segments;
            var secondAngle = MathF.Tau * (index + 1) / segments;
            drawList.AddLine(
                center + new Vector2(MathF.Cos(firstAngle), MathF.Sin(firstAngle)) * radius,
                center + new Vector2(MathF.Cos(secondAngle), MathF.Sin(secondAngle)) * radius,
                ImGui.GetColorU32(color),
                thickness);
        }
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
            ImGui.BeginTable("##BlackHoleTable", 5, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Soaked", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Crust", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableHeadersRow();
            foreach (var state in states)
            {
                var player = info.GetValueOrDefault(state.ActorId);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                DrawWtfDigJobBadge(
                    player?.Job,
                    player is null ? "?" : state.Dead ? $"{player.Job.Abbreviation} X" : player.Job.Abbreviation);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(player?.Role.Label ?? "-");
                ImGui.TableNextColumn();
                var soakLabel = FormatBlackHoleSoakLabel(state);
                ImGui.TextColored(state.LethalThisTether ? DamageColor : state.HitsThisTether > 0 ? LeadUpGoldColor : ModernMutedTextColor, soakLabel);
                ImGui.TableNextColumn();
                var stateLabel = FormatBlackHoleStateLabel(state);
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
        ImGui.SetItemAllowOverlap();
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

        if (analysis.Kefka is { } kefka)
        {
            DrawLimitCutRotationArrow(
                drawList,
                ToScreen,
                kefka.StartAngle,
                kefka.Rotation,
                (float)analysis.WallRadius + 1.5f,
                LimitCutKefkaRotationColor);
            var point = ToScreen(LimitCutAnalyzer.PositionAtAngle(kefka.StartAngle, analysis.WallRadius));
            var north = point + new Vector2(0, -9);
            var east = point + new Vector2(9, 0);
            var south = point + new Vector2(0, 9);
            var west = point + new Vector2(-9, 0);
            drawList.AddQuadFilled(
                north,
                east,
                south,
                west,
                ImGui.GetColorU32(new Vector4(0.04f, 0.05f, 0.07f, 0.90f)));
            drawList.AddQuad(
                north,
                east,
                south,
                west,
                ImGui.GetColorU32(LimitCutKefkaRotationColor), 2.5f);
            const string kefkaLabel = "K";
            var kefkaLabelSize = ImGui.CalcTextSize(kefkaLabel);
            drawList.AddText(
                point - (kefkaLabelSize * 0.5f),
                ImGui.GetColorU32(LimitCutKefkaRotationColor),
                kefkaLabel);
        }

        if (analysis.PlayerStartAngle is { } playerStartAngle && analysis.PlayerRotation is { } playerRotation)
        {
            DrawLimitCutRotationArrow(
                drawList,
                ToScreen,
                playerStartAngle,
                playerRotation,
                (float)analysis.WallRadius - 2.5f,
                LimitCutPlayerRotationColor);
        }

        foreach (var gap in analysis.Gaps)
        {
            var point = ToScreen(gap.Position);
            drawList.AddCircle(point, 10.0f, ImGui.GetColorU32(LimitCutPlayerRotationColor with { W = 0.74f }), 24, 2.0f);
            var label = gap.Number.ToString(CultureInfo.InvariantCulture);
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(point - textSize * 0.5f, ImGui.GetColorU32(LimitCutPlayerRotationColor), label);
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

    private static void DrawLimitCutRotationArrow(
        ImDrawListPtr drawList,
        Func<Vector2, Vector2> toScreen,
        double startAngle,
        LimitCutRotation rotation,
        float radius,
        Vector4 color)
    {
        const int arcDegrees = 55;
        const int stepDegrees = 6;
        const float arrowHeadLength = 9.0f;
        const float arrowHeadAngle = 0.5f;
        var direction = rotation == LimitCutRotation.Cw ? 1.0 : -1.0;
        var points = new List<Vector2>();
        for (var degrees = 0; degrees <= arcDegrees; degrees += stepDegrees)
        {
            points.Add(toScreen(LimitCutAnalyzer.PositionAtAngle(
                startAngle + (direction * degrees),
                radius)));
        }

        var outline = new Vector4(0.04f, 0.05f, 0.07f, 0.78f);
        for (var index = 1; index < points.Count; index++)
        {
            drawList.AddLine(points[index - 1], points[index], ImGui.GetColorU32(outline), 4.0f);
            drawList.AddLine(points[index - 1], points[index], ImGui.GetColorU32(color), 2.0f);
        }

        var tip = points[^1];
        var previous = points[^2];
        var angle = MathF.Atan2(tip.Y - previous.Y, tip.X - previous.X);
        var headOne = tip - new Vector2(
            arrowHeadLength * MathF.Cos(angle - arrowHeadAngle),
            arrowHeadLength * MathF.Sin(angle - arrowHeadAngle));
        var headTwo = tip - new Vector2(
            arrowHeadLength * MathF.Cos(angle + arrowHeadAngle),
            arrowHeadLength * MathF.Sin(angle + arrowHeadAngle));
        drawList.AddTriangle(tip, headOne, headTwo, ImGui.GetColorU32(outline), 3.5f);
        drawList.AddTriangleFilled(tip, headOne, headTwo, ImGui.GetColorU32(color));
    }

    private void DrawLimitCutTable(IReadOnlyList<LimitCutPlayer> players, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, WithBackgroundOpacity(ModernPanelAltColor, currentMainWindowBackgroundOpacity));
        if (ImGui.BeginChild("##LimitCutTablePanel", size, true, OptionalScrollbarFlags) &&
            ImGui.BeginTable("##LimitCutTable", 5, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Stood", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Off by", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableHeadersRow();
            foreach (var player in players)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(player.Number?.ToString(CultureInfo.InvariantCulture) ?? "?");
                ImGui.TableNextColumn();
                DrawWtfDigJobBadge(
                    player.Job,
                    player.Dead ? $"{player.Job.Abbreviation} X" : player.Job.Abbreviation);
                ImGui.TableNextColumn();
                ImGui.TextDisabled(FormatLimitCutAngle(player.Angle));
                ImGui.TableNextColumn();
                ImGui.TextDisabled(FormatLimitCutAngle(player.ExpectedAngle));
                ImGui.TableNextColumn();
                var color = player.AngleError switch
                {
                    null => ModernMutedTextColor,
                    < 11.25 => HealColor,
                    < 22.5 => LeadUpGoldColor,
                    _ => DamageColor,
                };
                ImGui.TextColored(color, FormatLimitCutAngle(player.AngleError));
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
        ImGui.SetItemAllowOverlap();
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
            DrawWtfDigOutlinedText(
                drawList,
                point + new Vector2(7.0f, 6.0f) - labelSize * 0.5f,
                label,
                ParseWtfDigColor(arrow.Job.Color));
        }

        ImGui.PopClipRect();
    }

    private void DrawArrowsTable(IReadOnlyList<ArrowDrop> arrows, IReadOnlyList<Vector2> expectedSlots, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, WithBackgroundOpacity(ModernPanelAltColor, currentMainWindowBackgroundOpacity));
        if (ImGui.BeginChild("##ArrowsTablePanel", size, true, OptionalScrollbarFlags) &&
            ImGui.BeginTable("##ArrowsTable", 5, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Wave", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Direction", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Position", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Error", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableHeadersRow();
            foreach (var arrow in arrows.OrderBy(entry => entry.Wave).ThenBy(entry => entry.Job.Role).ThenBy(entry => entry.Job.Abbreviation))
            {
                var error = ArrowsAnalyzer.Error(arrow.Position, expectedSlots);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                DrawWtfDigJobBadge(arrow.Job, arrow.Job.Abbreviation);
                ImGui.TableNextColumn();
                ImGui.TextDisabled(arrow.Wave.ToString(CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                ImGui.TextColored(ArrowDirectionColor(arrow.DirectionIndex), ArrowDirectionLabel(arrow.DirectionIndex));
                ImGui.TableNextColumn();
                ImGui.TextDisabled(FormatArrowPosition(arrow.Position));
                ImGui.TableNextColumn();
                var errorColor = error switch
                {
                    < 1.0f => HealColor,
                    < 2.0f => LeadUpGoldColor,
                    null => ModernMutedTextColor,
                    _ => DamageColor,
                };
                ImGui.TextColored(errorColor, FormatArrowError(error));
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
                    selectedForsakenCleave = null;
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
                    ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Assign", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Tower", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Took", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Stacks", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableHeadersRow();
                foreach (var player in resolution.Players)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    DrawWtfDigJobBadge(
                        player.Job,
                        player.Died ? $"{player.Job.Abbreviation} X" : player.Job.Abbreviation);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(FormatForsakenAssignmentLabel(player));
                    ImGui.TableNextColumn();
                    ImGui.TextColored(player.SoakedTower ? HealColor : ModernMutedTextColor, player.SoakedTower ? "Yes" : "-");
                    ImGui.TableNextColumn();
                    var taken = FormatForsakenTaken(player);
                    if (taken == "-")
                    {
                        ImGui.TextDisabled(taken);
                    }
                    else
                    {
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
        ImGui.SetItemAllowOverlap();
        var arenaHovered = ImGui.IsItemHovered();
        var mousePosition = ImGui.GetIO().MousePos;
        var arenaClicked = ImGui.IsItemClicked(ImGuiMouseButton.Left) &&
                           !IsWtfDigMapResizeCorner(mousePosition, start, size);
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
        if (selectedForsakenCleave is { } selectedIndex && selectedIndex >= resolution.Cleaves.Count)
        {
            selectedForsakenCleave = null;
        }

        int? cleaveUnderMouse = null;
        if (arenaHovered && showTowers && showForsakenCleaveSnapshot)
        {
            cleaveUnderMouse = resolution.Cleaves
                .Select((cleave, index) => (Index: index, Distance: Vector2.Distance(mousePosition, ToScreen(cleave.Origin))))
                .Where(candidate => candidate.Distance <= 12.0f)
                .OrderBy(candidate => candidate.Distance)
                .Select(candidate => (int?)candidate.Index)
                .FirstOrDefault();
            if (cleaveUnderMouse is not null)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
        }

        if (arenaClicked && cleaveUnderMouse is { } clickedIndex)
        {
            selectedForsakenCleave = selectedForsakenCleave == clickedIndex ? null : clickedIndex;
        }

        if (showTowers)
        {
            for (var index = 0; index < resolution.Cleaves.Count; index++)
            {
                var cleave = resolution.Cleaves[index];
                var heading = cleave.Frontal ? cleave.FacingRadians : cleave.FacingRadians + Math.PI;
                DrawForsakenSector(drawList, ToScreen, cleave.Origin, heading,
                    Math.PI / 2, extent + 3.0f, DamageColor with { W = 0.08f }, DamageColor with { W = 0.34f });
                if (selectedForsakenCleave == index)
                {
                    DrawForsakenCleaveBoundary(drawList, ToScreen, cleave.Origin, heading, extent + 3.0f);
                }
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

                for (var index = 0; index < resolution.Cleaves.Count; index++)
                {
                    var cleave = resolution.Cleaves[index];
                    var point = ToScreen(cleave.Origin);
                    var selected = selectedForsakenCleave == index;
                    drawList.AddQuadFilled(
                        point + new Vector2(0.0f, -6.0f),
                        point + new Vector2(6.0f, 0.0f),
                        point + new Vector2(0.0f, 6.0f),
                        point + new Vector2(-6.0f, 0.0f),
                        ImGui.GetColorU32(new Vector4(0.10f, 0.05f, 0.05f, 0.92f)));
                    var diamondColor = ImGui.GetColorU32(selected ? LimitCutPlayerRotationColor : DamageColor);
                    var diamondThickness = selected ? 2.5f : 1.5f;
                    var top = point + new Vector2(0.0f, -6.0f);
                    var right = point + new Vector2(6.0f, 0.0f);
                    var bottom = point + new Vector2(0.0f, 6.0f);
                    var left = point + new Vector2(-6.0f, 0.0f);
                    drawList.AddLine(top, right, diamondColor, diamondThickness);
                    drawList.AddLine(right, bottom, diamondColor, diamondThickness);
                    drawList.AddLine(bottom, left, diamondColor, diamondThickness);
                    drawList.AddLine(left, top, diamondColor, diamondThickness);
                }
            }

            foreach (var player in resolution.Players.Where(player => player.Position is not null))
            {
                DrawForsakenPlayerToken(drawList, ToScreen(player.Position!.Value), player.Job, player, player.DiedThisSet || player.DoubleHit, false);
            }

            if (showForsakenCleaveSnapshot)
            {
                DrawSelectedForsakenCleaveBait(drawList, ToScreen, resolution);
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

    private void DrawSelectedForsakenCleaveBait(
        ImDrawListPtr drawList,
        Func<Vector2, Vector2> toScreen,
        ForsakenResolution resolution)
    {
        if (selectedForsakenCleave is not { } index ||
            index < 0 ||
            index >= resolution.Cleaves.Count)
        {
            return;
        }

        var cleave = resolution.Cleaves[index];
        var origin = toScreen(cleave.Origin);
        const float reach = 28.0f;
        var facingEnd = toScreen(cleave.Origin + new Vector2(
            (float)(reach * Math.Sin(cleave.FacingRadians)),
            (float)(reach * Math.Cos(cleave.FacingRadians))));
        DrawDashedLine(drawList, origin, facingEnd, LimitCutPlayerRotationColor, 5.0f, 4.0f, 1.75f);

        if (cleave.Bait is not { } bait)
        {
            return;
        }

        var baitPoint = toScreen(bait.Position);
        DrawDashedCircle(drawList, baitPoint, 11.5f, LimitCutPlayerRotationColor, 24, 2.0f);
        DrawForsakenPlayerToken(drawList, baitPoint, bait.Job, null, false, false);
    }

    private static void DrawForsakenCleaveBoundary(
        ImDrawListPtr drawList,
        Func<Vector2, Vector2> toScreen,
        Vector2 origin,
        double heading,
        float radius)
    {
        var first = heading - (Math.PI / 2.0);
        var second = heading + (Math.PI / 2.0);
        drawList.AddLine(
            toScreen(origin + new Vector2((float)(radius * Math.Sin(first)), (float)(radius * Math.Cos(first)))),
            toScreen(origin + new Vector2((float)(radius * Math.Sin(second)), (float)(radius * Math.Cos(second)))),
            ImGui.GetColorU32(DamageColor with { W = 0.88f }),
            2.0f);
    }

    private static void DrawDashedLine(
        ImDrawListPtr drawList,
        Vector2 start,
        Vector2 end,
        Vector4 color,
        float dashLength,
        float gapLength,
        float thickness)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length <= 0.01f)
        {
            return;
        }

        var direction = delta / length;
        for (var offset = 0.0f; offset < length; offset += dashLength + gapLength)
        {
            drawList.AddLine(
                start + (direction * offset),
                start + (direction * MathF.Min(length, offset + dashLength)),
                ImGui.GetColorU32(color),
                thickness);
        }
    }

    private static void DrawDashedCircle(
        ImDrawListPtr drawList,
        Vector2 center,
        float radius,
        Vector4 color,
        int segments,
        float thickness)
    {
        for (var index = 0; index < segments; index += 2)
        {
            var start = (MathF.Tau * index) / segments;
            var end = (MathF.Tau * (index + 1)) / segments;
            drawList.PathArcTo(center, radius, start, end, 2);
            drawList.PathStroke(ImGui.GetColorU32(color), ImDrawFlags.None, thickness);
        }
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

    private static void DrawWtfDigJobBadge(WtfDigJobInfo? job, string label)
    {
        var textSize = ImGui.CalcTextSize(label);
        var size = new Vector2(textSize.X + 10.0f, MathF.Max(20.0f, textSize.Y + 4.0f));
        var start = ImGui.GetCursorScreenPos();
        var end = start + size;
        var fill = job is null
            ? ModernPanelBorderColor
            : ParseWtfDigColor(job.Color);
        var textColor = GetReadableTextColorForBackground(fill, 4.5f);
        var borderColor = BlendColors(
            fill,
            textColor,
            ActiveThemeUsesLightPanels() ? 0.42f : 0.30f) with
        {
            W = 0.90f,
        };
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(start, end, ImGui.GetColorU32(fill), 4.0f);
        drawList.AddRect(start, end, ImGui.GetColorU32(borderColor), 4.0f, ImDrawFlags.None, 1.2f);
        drawList.AddText(
            start + ((size - textSize) * 0.5f),
            ImGui.GetColorU32(textColor),
            label);
        ImGui.Dummy(size);
    }

    private static void DrawWtfDigOutlinedText(
        ImDrawListPtr drawList,
        Vector2 position,
        string text,
        Vector4 color)
    {
        var outline = GetReadableTextColorForBackground(ModernPanelAltColor, 4.5f) with { W = 0.92f };
        foreach (var offset in WtfDigTextOutlineOffsets)
        {
            drawList.AddText(position + offset, ImGui.GetColorU32(outline), text);
        }

        drawList.AddText(position, ImGui.GetColorU32(color), text);
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

    private void DrawWtfDigMapTableLayout(
        string idSuffix,
        float preferredTableWidth,
        Action<float> drawMap,
        Action<Vector2> drawTable)
    {
        var contentWidth = MathF.Max(1.0f, ImGui.GetContentRegionAvail().X);
        var style = ImGui.GetStyle();
        var gap = MathF.Max(style.ItemSpacing.X, style.FramePadding.X * 2.0f);
        var tableWidth = MathF.Min(contentWidth, MathF.Max(WtfDigTableMinWidth, preferredTableWidth));
        var sideBySide = contentWidth >= WtfDigMapMinSide + gap + tableWidth;
        var maxVisibleMapSide = sideBySide
            ? MathF.Min(WtfDigMapMaxSide, contentWidth - gap - tableWidth)
            : MathF.Min(WtfDigMapMaxSide, contentWidth);
        var preferredMapSide = float.IsFinite(configuration.WtfDigAnalyzerMapSide) &&
                               configuration.WtfDigAnalyzerMapSide >= WtfDigMapMinSide
            ? configuration.WtfDigAnalyzerMapSide
            : WtfDigMapDefaultSide;
        var mapSide = MathF.Min(preferredMapSide, maxVisibleMapSide);
        var mapStart = ImGui.GetCursorScreenPos();

        drawMap(mapSide);
        if (sideBySide)
        {
            ImGui.SameLine(0.0f, gap);
            drawTable(new Vector2(tableWidth, mapSide));
        }
        else
        {
            ImGui.Dummy(new Vector2(1.0f, MathF.Max(1.0f, style.ItemSpacing.Y)));
            drawTable(new Vector2(tableWidth, 0.0f));
        }

        var resizeState = SubmitWtfDigMapResizeHandles(idSuffix, mapStart, mapSide, maxVisibleMapSide);
        DrawReplayCanvasResizeHandleVisuals(
            ImGui.GetWindowDrawList(),
            mapStart,
            new Vector2(mapSide),
            resizeState);
    }

    private ReplayCanvasResizeState SubmitWtfDigMapResizeHandles(
        string idSuffix,
        Vector2 mapStart,
        float mapSide,
        float maxSide)
    {
        var cursorBefore = ImGui.GetCursorScreenPos();
        var mapSize = new Vector2(mapSide);
        var hovered = false;
        var active = false;

        SubmitWtfDigMapResizeHandle(idSuffix, ReplayCanvasResizeCorner.TopLeft, mapStart, mapSize, maxSide, ref hovered, ref active);
        SubmitWtfDigMapResizeHandle(idSuffix, ReplayCanvasResizeCorner.TopRight, mapStart, mapSize, maxSide, ref hovered, ref active);
        SubmitWtfDigMapResizeHandle(idSuffix, ReplayCanvasResizeCorner.BottomLeft, mapStart, mapSize, maxSide, ref hovered, ref active);
        SubmitWtfDigMapResizeHandle(idSuffix, ReplayCanvasResizeCorner.BottomRight, mapStart, mapSize, maxSide, ref hovered, ref active);

        if (!active && wtfDigMapResizeDragging)
        {
            wtfDigMapResizeDragging = false;
            plugin.SaveConfiguration();
        }

        ImGui.SetCursorScreenPos(cursorBefore);
        return new ReplayCanvasResizeState(hovered, active);
    }

    private void SubmitWtfDigMapResizeHandle(
        string idSuffix,
        ReplayCanvasResizeCorner corner,
        Vector2 mapStart,
        Vector2 mapSize,
        float maxSide,
        ref bool hovered,
        ref bool active)
    {
        var handleSize = new Vector2(ReplayCanvasResizeHandleSize);
        var handleStart = corner switch
        {
            ReplayCanvasResizeCorner.TopLeft => mapStart,
            ReplayCanvasResizeCorner.TopRight => new Vector2(mapStart.X + mapSize.X - handleSize.X, mapStart.Y),
            ReplayCanvasResizeCorner.BottomLeft => new Vector2(mapStart.X, mapStart.Y + mapSize.Y - handleSize.Y),
            ReplayCanvasResizeCorner.BottomRight => mapStart + mapSize - handleSize,
            _ => mapStart,
        };

        ImGui.SetCursorScreenPos(handleStart);
        ImGui.InvisibleButton($"##WtfDigMapResize{idSuffix}{corner}", handleSize);
        var handleHovered = ImGui.IsItemHovered();
        var handleActive = ImGui.IsItemActive();
        hovered |= handleHovered;
        active |= handleActive;

        if (handleHovered || handleActive)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }

        if (!handleActive)
        {
            return;
        }

        wtfDigMapResizeDragging = true;
        var resizeDelta = GetReplayCanvasResizeDelta(corner, ImGui.GetIO().MouseDelta);
        if (MathF.Abs(resizeDelta) <= 0.001f)
        {
            return;
        }

        var minimumSide = MathF.Min(WtfDigMapMinSide, maxSide);
        var newSide = Math.Clamp(mapSize.X + resizeDelta, minimumSide, maxSide);
        if (MathF.Abs(configuration.WtfDigAnalyzerMapSide - newSide) > 0.1f)
        {
            configuration.WtfDigAnalyzerMapSide = newSide;
        }
    }

    private static float MeasureBlackHoleTableWidth(
        BlackHoleTether tether,
        IReadOnlyList<BlackHolePlayerInfo> players)
    {
        var info = players.ToDictionary(player => player.ActorId);
        return MeasureWtfDigTableWidth(
            ("Job", tether.States.Select(state => info.TryGetValue(state.ActorId, out var player)
                ? state.Dead ? $"{player.Job.Abbreviation} X" : player.Job.Abbreviation
                : "?"), 10.0f),
            ("Role", tether.States.Select(state => info.GetValueOrDefault(state.ActorId)?.Role.Label ?? "-"), 0.0f),
            ("Soaked", tether.States.Select(FormatBlackHoleSoakLabel), 0.0f),
            ("State", tether.States.Select(FormatBlackHoleStateLabel), 0.0f),
            ("Crust", tether.States.Select(state => state.Crust ? "Yes" : "Cleansed"), 0.0f));
    }

    private static float MeasureLimitCutTableWidth(IReadOnlyList<LimitCutPlayer> players) =>
        MeasureWtfDigTableWidth(
            ("#", players.Select(player => player.Number?.ToString(CultureInfo.InvariantCulture) ?? "?"), 0.0f),
            ("Job", players.Select(player => player.Dead ? $"{player.Job.Abbreviation} X" : player.Job.Abbreviation), 10.0f),
            ("Stood", players.Select(player => FormatLimitCutAngle(player.Angle)), 0.0f),
            ("Target", players.Select(player => FormatLimitCutAngle(player.ExpectedAngle)), 0.0f),
            ("Off by", players.Select(player => FormatLimitCutAngle(player.AngleError)), 0.0f));

    private static float MeasureArrowsTableWidth(
        IReadOnlyList<ArrowDrop> arrows,
        IReadOnlyList<Vector2> expectedSlots) =>
        MeasureWtfDigTableWidth(
            ("Job", arrows.Select(arrow => arrow.Job.Abbreviation), 10.0f),
            ("Wave", arrows.Select(arrow => arrow.Wave.ToString(CultureInfo.InvariantCulture)), 0.0f),
            ("Direction", arrows.Select(arrow => ArrowDirectionLabel(arrow.DirectionIndex)), 0.0f),
            ("Position", arrows.Select(arrow => FormatArrowPosition(arrow.Position)), 0.0f),
            ("Error", arrows.Select(arrow => FormatArrowError(ArrowsAnalyzer.Error(arrow.Position, expectedSlots))), 0.0f));

    private static float MeasureForsakenTableWidth(ForsakenResolution resolution) =>
        MeasureWtfDigTableWidth(
            ("Job", resolution.Players.Select(player => player.Died ? $"{player.Job.Abbreviation} X" : player.Job.Abbreviation), 10.0f),
            ("Assign", resolution.Players.Select(FormatForsakenAssignmentLabel), 0.0f),
            ("Tower", resolution.Players.Select(player => player.SoakedTower ? "Yes" : "-"), 0.0f),
            ("Took", resolution.Players.Select(FormatForsakenTaken), 0.0f),
            ("Stacks", resolution.Players.Select(player => player.TroubleStacks?.ToString(CultureInfo.InvariantCulture) ?? "-"), 0.0f));

    private static float MeasureWtfDigTableWidth(
        params (string Header, IEnumerable<string> Values, float ExtraWidth)[] columns)
    {
        var style = ImGui.GetStyle();
        var width = 2.0f + (style.WindowPadding.X * 2.0f) + style.ScrollbarSize;
        foreach (var column in columns)
        {
            var columnWidth = ImGui.CalcTextSize(column.Header).X;
            foreach (var value in column.Values)
            {
                columnWidth = MathF.Max(columnWidth, ImGui.CalcTextSize(value).X + column.ExtraWidth);
            }

            width += MathF.Ceiling(columnWidth) + (style.CellPadding.X * 2.0f);
        }

        return MathF.Max(WtfDigTableMinWidth, width);
    }

    private static bool IsWtfDigMapResizeCorner(Vector2 position, Vector2 mapStart, float mapSide)
    {
        var insetX = position.X - mapStart.X;
        var insetY = position.Y - mapStart.Y;
        var nearHorizontalEdge = insetX <= ReplayCanvasResizeHandleSize ||
                                 insetX >= mapSide - ReplayCanvasResizeHandleSize;
        var nearVerticalEdge = insetY <= ReplayCanvasResizeHandleSize ||
                               insetY >= mapSide - ReplayCanvasResizeHandleSize;
        return nearHorizontalEdge && nearVerticalEdge;
    }

    private static string FormatBlackHoleSoakLabel(BlackHolePlayerState state) => state.SoakCount == 0
        ? "-"
        : $"{state.SoakCount}{(state.HitsThisTether > 0 ? state.TetherThisTether ? " held" : " clipped" : string.Empty)}";

    private static string FormatBlackHoleStateLabel(BlackHolePlayerState state) => state.Level switch
    {
        NothingLevel.Unbecoming => "Unbecoming",
        NothingLevel.Meanest => "Meanest",
        _ => "-",
    };

    private static string FormatLimitCutAngle(double? angle) => angle is { } value
        ? $"{Math.Round(value * 2) / 2:0.#} deg"
        : "-";

    private static string FormatArrowPosition(Vector2? position) => position is { } point
        ? $"{point.X:F1}, {point.Y:F1}"
        : "-";

    private static string FormatArrowError(float? error) => error is { } distance
        ? $"{distance:F1}y"
        : "-";

    private static string FormatForsakenAssignmentLabel(ForsakenPlayerSnapshot player)
    {
        var assignment = FormatForsakenAssignment(player.Assignment);
        return player.ReassignedTo is { } reassigned
            ? $"{assignment} -> {FormatForsakenAssignment(reassigned)}"
            : assignment;
    }

    private static string FormatForsakenTaken(ForsakenPlayerSnapshot player) => player.Taken.Count == 0
        ? "-"
        : string.Join(", ", player.Taken.Select(hit =>
            $"{hit.Label ?? FormatForsakenEffect(hit.Effect)} {Math.Round(hit.Damage / 1000.0):N0}k"));

    private static string FormatWtfDigTime(double seconds) => $"{Math.Max(0, (int)seconds) / 60}:{Math.Max(0, (int)seconds) % 60:00}";
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
                LocalAvailability = new Dictionary<string, WtfDigLocalAnalyzerAvailability>(StringComparer.Ordinal),
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
                LocalAvailability = new Dictionary<string, WtfDigLocalAnalyzerAvailability>(StringComparer.Ordinal),
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
                LocalAvailability = new Dictionary<string, WtfDigLocalAnalyzerAvailability>(StringComparer.Ordinal),
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
                LocalAvailability = new Dictionary<string, WtfDigLocalAnalyzerAvailability>(StringComparer.Ordinal),
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
        var availability = eventSource is LocalPullEventSource localSource
            ? localSource.AnalyzerAvailability
            : new Dictionary<string, WtfDigLocalAnalyzerAvailability>(StringComparer.Ordinal);
        if (availability.TryGetValue(analyzer.Key, out var localAvailability) &&
            localAvailability.Quality == WtfDigLocalDataQuality.Unavailable)
        {
            return new WtfDigOperationResult(
                report,
                report.Fights,
                fight,
                null,
                localAvailability.Summary,
                eventSource)
            {
                LocalAvailability = availability,
            };
        }

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
        return new WtfDigOperationResult(report, eligible, fight, analysis, error, eventSource)
        {
            LocalAvailability = availability,
        };
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
                    LocalAvailability = result.LocalAvailability,
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
    string? Error)
{
    internal IReadOnlyDictionary<string, WtfDigLocalAnalyzerAvailability> LocalAvailability { get; init; } =
        new Dictionary<string, WtfDigLocalAnalyzerAvailability>(StringComparer.Ordinal);
}

internal sealed record WtfDigOperationResult(
    FflogsReportSummary Report,
    IReadOnlyList<FflogsFight> EligibleFights,
    FflogsFight? SelectedFight,
    object? Analysis,
    string? Error,
    IWtfDigEventSource? EventSource = null)
{
    internal IReadOnlyDictionary<string, WtfDigLocalAnalyzerAvailability> LocalAvailability { get; init; } =
        new Dictionary<string, WtfDigLocalAnalyzerAvailability>(StringComparer.Ordinal);
}
