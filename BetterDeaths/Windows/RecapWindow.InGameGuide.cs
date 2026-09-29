using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private bool guideActive;
    private bool guideScrollPending;
    private int guideStepIndex;
    private GuideSection? guideDataSelectionPending;
    private (ReviewSelectionState Selection, string PullKey)? guideDeathSelectionPending;
    private bool guideUsesExampleReview;
    private readonly Dictionary<GuideTarget, (Vector2 Min, Vector2 Max)> guideTargets = new();
    private Vector2 guidePanelMin;
    private Vector2 guidePanelMax;

    private void RegisterGuideTarget(GuideTarget target, Vector2 min, Vector2 max)
    {
        if (!guideActive)
            return;

        if (guideScrollPending && OnboardingGuide.Steps[guideStepIndex].Target == target &&
            target is not (GuideTarget.Navigation or GuideTarget.MeterColumns or GuideTarget.MeterPreview))
        {
            ImGui.SetScrollHereY(0.2f);
            guideScrollPending = false;
        }

        var windowMin = ImGui.GetWindowPos();
        var windowMax = windowMin + ImGui.GetWindowSize();
        min = Vector2.Max(min, windowMin);
        max = Vector2.Min(max, windowMax);
        if (max.X - min.X < 8.0f || max.Y - min.Y < 8.0f)
            return;

        guideTargets[target] = (min, max);
    }

    private void RegisterGuideTarget(GuideTarget target)
    {
        RegisterGuideTarget(target, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }

    private void FocusGuideTarget(GuideTarget target)
    {
        if (!guideActive || !guideScrollPending || OnboardingGuide.Steps[guideStepIndex].Target != target)
            return;

        ImGui.SetScrollHereY(0.2f);
        guideScrollPending = false;
    }

    private void DrawGuidePointer()
    {
        if (!guideActive)
            return;

        var target = OnboardingGuide.Steps[guideStepIndex].Target;
        if (!guideTargets.TryGetValue(target, out var rect))
        {
            var fallback = target switch
            {
                GuideTarget.WhatIf => GuideTarget.ReviewDetails,
                GuideTarget.ReviewPulls => GuideTarget.ReviewTimeline,
                GuideTarget.ReplayPulls => GuideTarget.ReplayViewer,
                GuideTarget.MeterPreview => GuideTarget.MeterWidget,
                GuideTarget.MeterColumns => GuideTarget.MeterWidget,
                GuideTarget.AnalyzerSource => GuideTarget.AnalyzerResults,
                _ => GuideTarget.Navigation,
            };
            if (!guideTargets.TryGetValue(fallback, out rect) &&
                !guideTargets.TryGetValue(GuideTarget.Navigation, out rect))
                return;
        }

        var windowMin = ImGui.GetWindowPos();
        var windowMax = windowMin + ImGui.GetWindowSize();
        var min = Vector2.Max(rect.Min, windowMin + new Vector2(2.0f));
        var max = Vector2.Min(rect.Max, windowMax - new Vector2(2.0f));
        if (max.X - min.X < 8.0f || max.Y - min.Y < 8.0f)
            return;

        var draw = ImGui.GetForegroundDrawList();
        draw.PushClipRect(windowMin, windowMax, true);
        draw.AddRect(min, max, ImGui.GetColorU32(ModernShellColor with { W = 0.8f }),
            4.0f, ImDrawFlags.None, 5.0f);
        draw.AddRect(min, max, ImGui.GetColorU32(ModernAccentColor with { W = 1.0f }),
            4.0f, ImDrawFlags.None, 2.0f);

        if (min.Y > guidePanelMax.Y + 8.0f)
        {
            var x = Math.Clamp((min.X + max.X) * 0.5f, guidePanelMin.X + 20.0f, guidePanelMax.X - 20.0f);
            var lineStart = new Vector2(x, guidePanelMax.Y + 2.0f);
            var lineEnd = new Vector2(x, min.Y - 5.0f);
            draw.AddLine(lineStart, lineEnd, ImGui.GetColorU32(ModernAccentColor with { W = 0.65f }), 1.5f);
            draw.AddTriangleFilled(new Vector2(x, min.Y - 2.0f),
                new Vector2(x - 4.0f, min.Y - 9.0f), new Vector2(x + 4.0f, min.Y - 9.0f),
                ImGui.GetColorU32(ModernAccentColor));
        }

        draw.PopClipRect();
    }

    private void DrawGuideButton(string idSuffix, float width)
    {
        if (DrawThemedToggleButton(OnboardingGuide.ButtonLabel, $"InGameGuide{idSuffix}", guideActive, width))
        {
            if (guideActive)
                guideActive = false;
            else
                StartInGameGuide();
        }

        if (ImGui.IsItemHovered())
            SetThemedTooltip(guideActive ? "Close the walkthrough." : "Walk through Better Deaths inside the plugin.");
    }

    private void StartInGameGuide()
    {
        MarkInGameGuideSeen();
        pendingDeathSelection = null;
        clearPendingDeathSelection = false;
        guideDataSelectionPending = GuideSection.Review;
        guideDeathSelectionPending = null;
        guideUsesExampleReview = false;
        guideActive = true;
        MoveInGameGuideTo(0);
    }

    private void MarkInGameGuideSeen()
    {
        if (configuration.HasSeenInGameGuide)
            return;

        configuration.HasSeenInGameGuide = true;
        plugin.SaveConfiguration();
    }

    private void MoveInGameGuideTo(int index)
    {
        var previousSection = OnboardingGuide.Steps[guideStepIndex].Section;
        guideStepIndex = Math.Clamp(index, 0, OnboardingGuide.Steps.Count - 1);
        if (OnboardingGuide.Steps[guideStepIndex].Section != previousSection)
            guideDataSelectionPending = OnboardingGuide.Steps[guideStepIndex].Section;
        guideScrollPending = true;
        currentMainPage = OnboardingGuide.Steps[guideStepIndex].Section switch
        {
            GuideSection.Review when ShouldShowExamplePage() => MainPage.Example,
            GuideSection.Review => MainPage.Review,
            GuideSection.Replay => MainPage.Replay,
            GuideSection.Widgets => MainPage.Widgets,
            GuideSection.Analyzer => MainPage.Analyzer,
            GuideSection.Options => MainPage.Options,
            GuideSection.Data => MainPage.Data,
            GuideSection.Updates => MainPage.Updates,
            _ => MainPage.Review,
        };
    }

    private void PrepareInGameGuideData()
    {
        if (!guideActive || guideDataSelectionPending is not { } section)
            return;

        if ((section is GuideSection.Review or GuideSection.Replay or GuideSection.Analyzer) &&
            plugin.RecordedPullHistoryLoading)
            return;

        switch (section)
        {
            case GuideSection.Review:
                var recorded = OnboardingGuide.SelectRecordedPull(GetVisibleRecordedPulls().Select(entry => entry.Summary));
                guideUsesExampleReview = recorded is null;
                if (guideUsesExampleReview && TutorialSampleData.GetPullIfReady() is null &&
                    !TutorialSampleData.PullLoadFailed)
                    return;
                var selection = recorded is null ? exampleReviewSelection : recapReviewSelection;
                var pullKey = recorded is null ? "ExamplePull" : BuildRecordedPullKey(recorded);
                selection.PullKey = pullKey;
                ClearSelectedDeath(selection);
                selectedDeathDetailPage = DeathDetailPage.Summary;
                guideDeathSelectionPending = (selection, pullKey);
                currentMainPage = recorded is null ? MainPage.Example : MainPage.Review;
                break;
            case GuideSection.Replay:
                var replay = OnboardingGuide.SelectRecordedPull(GetVisibleRecordedPulls().Select(entry => entry.Summary));
                if (replay is null)
                {
                    if (TutorialSampleData.GetPullIfReady() is not { } sample)
                        return;
                    replay = TutorialSampleData.CreatePullSummary(sample);
                }
                SelectReplayPull(BuildRecordedPullKey(replay));
                break;
            case GuideSection.Widgets:
                selectedDamageEncounterNumber = plugin.RecordedDamageEncounters.LastOrDefault()?.EncounterNumber ?? 0;
                break;
            case GuideSection.Analyzer:
                var analysisPull = GetWtfDigLocalPulls().FirstOrDefault();
                if (analysisPull is null)
                    return;
                wtfDigAnalyzer.SetSource(WtfDigAnalyzerSource.LocalPull);
                SelectWtfDigLocalPull(analysisPull);
                break;
        }

        guideDataSelectionPending = null;
    }

    private void SelectInGameGuideDeath(IReadOnlyList<ReviewPull> pulls, ReviewSelectionState selection)
    {
        if (!guideActive || guideDeathSelectionPending is not { } pending ||
            !ReferenceEquals(pending.Selection, selection))
            return;

        if (!string.Equals(selection.PullKey, pending.PullKey, StringComparison.Ordinal))
        {
            guideDeathSelectionPending = null;
            return;
        }

        var pull = GetSelectedReviewPull(pulls, pending.PullKey);
        if (pull is null || GetDeathsInTimelineOrder(pull.Deaths).FirstOrDefault() is not { } death)
            return;

        if (GetSelectedReviewDeath(pull, selection) is null)
            SelectTimelineDeathAndOpenLeadUp(death, selection);
        guideDeathSelectionPending = null;
    }

    private void DrawInGameGuide()
    {
        if (!guideActive && configuration.HasSeenInGameGuide)
            return;

        if (guideActive && OnboardingGuide.Steps[guideStepIndex].Section == GuideSection.Review &&
            currentMainPage == MainPage.Review && ShouldShowExamplePage())
            currentMainPage = MainPage.Example;

        if (guideActive && GuideSectionFor(currentMainPage) is { } section &&
            OnboardingGuide.Steps[guideStepIndex].Section != section)
        {
            guideStepIndex = OnboardingGuide.FirstStepFor(section);
            guideDataSelectionPending = section;
            guideScrollPending = true;
        }

        PrepareInGameGuideData();

        var title = guideActive ? OnboardingGuide.Steps[guideStepIndex].Title : "Get to know Better Deaths";
        var description = guideActive
            ? OnboardingGuide.Steps[guideStepIndex].Description
            : "Take a short tour of Review, Replay, Widgets, Analyzer, Options, and saved data. It uses the real plugin views and can be reopened later.";
        var action = guideActive ? OnboardingGuide.Steps[guideStepIndex].TryThis : string.Empty;
        if (guideActive && plugin.RecordedPulls.Count == 0 &&
            OnboardingGuide.Steps[guideStepIndex].Section is GuideSection.Replay or GuideSection.Analyzer)
            action = "Explore the redacted tutorial pull, then try the same controls on your own captures.";

        var width = MathF.Max(120.0f, ImGui.GetContentRegionAvail().X - 24.0f);
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var buttonWidth = guideActive
            ? (guideStepIndex > 0 ? GetThemedActionButtonWidth("Back") : 0.0f) +
              GetThemedActionButtonWidth("Next") + GetThemedActionButtonWidth("Close") +
              (ImGui.GetStyle().ItemSpacing.X * (guideStepIndex > 0 ? 2.0f : 1.0f))
            : GetThemedActionButtonWidth("Start walkthrough") + GetThemedActionButtonWidth("Not now") +
              ImGui.GetStyle().ItemSpacing.X;
        var wrapButtons = buttonWidth > width;
        var buttonRows = wrapButtons ? guideActive && guideStepIndex > 0 ? 3 : 2 : 1;
        var height = Math.Clamp(
            (lineHeight * 2.0f) +
            ImGui.CalcTextSize(description, false, width).Y +
            (guideActive ? ImGui.CalcTextSize(action, false, width).Y + 6.0f : 0.0f) +
            ImGui.GetFrameHeight() * buttonRows + 37.0f,
            118.0f,
            260.0f);
        height = MathF.Min(height, MathF.Max(80.0f, ImGui.GetContentRegionAvail().Y - 140.0f));

        ImGui.PushStyleColor(ImGuiCol.ChildBg, ModernPanelAltColor);
        ImGui.PushStyleColor(ImGuiCol.Border, ModernPanelBorderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10.0f, 8.0f));
        if (ImGui.BeginChild("##BetterDeathsInGameGuide", new Vector2(0.0f, height), true))
        {
            if (guideActive)
            {
                var sectionName = OnboardingGuide.Steps[guideStepIndex].Section.ToString();
                ImGui.TextColored(ModernAccentColor,
                    $"Walkthrough {guideStepIndex + 1} / {OnboardingGuide.Steps.Count}  |  {sectionName}");
            }
            else
            {
                ImGui.TextColored(ModernAccentColor, "WALKTHROUGH");
            }

            ImGui.TextColored(ModernTextColor, title);
            ImGui.TextWrapped(description);
            if (guideActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ModernMutedTextColor);
                ImGui.TextWrapped(action);
                ImGui.PopStyleColor();
            }

            ImGui.Dummy(new Vector2(1.0f, 2.0f));
            if (!guideActive)
            {
                if (DrawThemedActionButton("Start walkthrough", "StartInGameGuide"))
                    StartInGameGuide();
                if (!wrapButtons)
                    ImGui.SameLine();
                if (DrawThemedToggleButton("Not now", "DismissInGameGuide", false))
                    MarkInGameGuideSeen();
            }
            else
            {
                if (guideStepIndex > 0)
                {
                    if (DrawThemedToggleButton("Back", "GuidePrevious", false))
                        MoveInGameGuideTo(guideStepIndex - 1);
                    if (!wrapButtons)
                        ImGui.SameLine();
                }

                var lastStep = guideStepIndex == OnboardingGuide.Steps.Count - 1;
                if (DrawThemedActionButton(lastStep ? "Finish" : "Next", "GuideNext"))
                {
                    if (lastStep)
                        guideActive = false;
                    else
                        MoveInGameGuideTo(guideStepIndex + 1);
                }

                if (!wrapButtons)
                    ImGui.SameLine();
                if (DrawThemedToggleButton("Close", "GuideClose", false))
                    guideActive = false;
            }
        }

        ImGui.EndChild();
        guidePanelMin = ImGui.GetItemRectMin();
        guidePanelMax = ImGui.GetItemRectMax();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
        ImGui.Dummy(new Vector2(1.0f, ModernHeaderBottomGap));
    }

    private static GuideSection? GuideSectionFor(MainPage page) => page switch
    {
        MainPage.Review or MainPage.Example => GuideSection.Review,
        MainPage.Replay => GuideSection.Replay,
        MainPage.Widgets => GuideSection.Widgets,
        MainPage.Analyzer => GuideSection.Analyzer,
        MainPage.Options => GuideSection.Options,
        MainPage.Data or MainPage.Feedback => GuideSection.Data,
        MainPage.Updates => GuideSection.Updates,
        _ => null,
    };
}
