using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal enum GuideSection
{
    Review,
    Replay,
    Widgets,
    Analyzer,
    Options,
    Data,
    Updates,
}

internal enum GuideTarget
{
    Navigation,
    ReviewPulls,
    ReviewTimeline,
    ReviewDetails,
    WhatIf,
    ReplayPulls,
    ReplayViewer,
    DeathWidget,
    MeterWidget,
    MeterPreview,
    MeterColumns,
    MeterEncounters,
    AnalyzerSource,
    AnalyzerResults,
    OptionsSettings,
    OptionsCustomize,
    Data,
    Updates,
}

internal readonly record struct GuideStep(
    GuideSection Section,
    GuideTarget Target,
    string Title,
    string Description,
    string TryThis);

internal static class OnboardingGuide
{
    public const string ButtonLabel = "Walkthrough";

    public static RecordedPullSummary? SelectRecordedPull(IEnumerable<RecordedPullSummary> visiblePulls) =>
        visiblePulls.FirstOrDefault(pull => pull.DeathCount > 0);

    public static IReadOnlyList<GuideStep> Steps { get; } =
    [
        new(GuideSection.Review, GuideTarget.Navigation, "Start with a pull",
            "Review connects each death to its pull, its place in the death timeline, and the evidence behind it. The walkthrough selects a saved pull with deaths, or a redacted tutorial sample when none is available.",
            "Explore the selected saved pull, or the tutorial sample if none is available."),
        new(GuideSection.Review, GuideTarget.ReviewPulls, "Choose the right pull",
            "The pull rail lists the current pull and saved attempts. Its duty filter and player search help narrow the history; collapsing the rail leaves more room for the review.",
            "Select a pull in the left rail. Example has one sample pull instead."),
        new(GuideSection.Review, GuideTarget.ReviewTimeline, "Read the death timeline",
            "Each row shows when a player died and the captured fatal event. Selecting a row opens the lead-up beneath it, including damage, healing, HP, shields, and nearby status effects.",
            "Select a death row, then hover an HP bar for its exact values."),
        new(GuideSection.Review, GuideTarget.ReviewDetails, "Inspect the selected death",
            "Summary separates the fatal event from active and expired mitigation. The HP and shield bars show the state around the hit; expired effects are context, not protection that applied.",
            "Check the fatal event, then compare active effects with expired ones."),
        new(GuideSection.Review, GuideTarget.WhatIf, "Test a mitigation idea",
            "What-if estimates how selected mitigation could have changed a captured damaging death. It is a planning aid, not a claim that the effect was actually present.",
            "Open What-if beside Summary when a death has a damaging fatal event."),
        new(GuideSection.Replay, GuideTarget.ReplayPulls, "Replay the attempt",
            "Replay places captured players and waymarks on an arena timeline. Play, pause, or scrub; skull markers identify recorded deaths. Open in Replay from a death to jump near that moment.",
            "Choose a saved or tutorial pull, then drag the timeline or select a skull marker."),
        new(GuideSection.Replay, GuideTarget.ReplayViewer, "Read synchronized context",
            "Party HP and Active Effects follow replay time. Effects can show mitigations, debuffs, or both. Missing capture data can leave a replay incomplete, so use it alongside Review.",
            "Move the timeline and watch HP, shields, and effect timers change."),
        new(GuideSection.Widgets, GuideTarget.DeathWidget, "Set up the Death Widget",
            "The Death Widget is a compact view of the current pull's death information. Its visibility, display mode, background opacity, and icon size are independent of the DPS Meter.",
            "Adjust the Death Widget controls and inspect its preview."),
        new(GuideSection.Widgets, GuideTarget.MeterPreview, "Read the DPS Meter",
            "The floating meter shows personal damage and DPS over the captured encounter duration. Damage % is the player's share of the displayed total; expanding a player shows ability details.",
            "Use the preview when you are not in combat; live results stay in the floating widget."),
        new(GuideSection.Widgets, GuideTarget.MeterColumns, "Shape the meter",
            "Add available columns with + or drag them into Widget columns. Drag active columns to reorder, or use X to remove one. Concise shortens names and numbers without changing recorded data.",
            "Try a column and switch between Normal and Concise in the preview."),
        new(GuideSection.Widgets, GuideTarget.MeterEncounters, "Revisit an encounter",
            "The Encounters list keeps saved combat results separately from death pulls. Selecting one updates the settings preview, not the floating widget's live or latest encounter.",
            "Select a saved encounter, or explore the tutorial encounter when none is available."),
        new(GuideSection.Analyzer, GuideTarget.AnalyzerSource, "Choose an analysis source",
            "Analyzer supports eligible saved Better Deaths pulls and FFLogs links for supported mechanics. A local pull stays local; a report link uses the external report service.",
            "Choose the source, mechanic, and matching pull or report."),
        new(GuideSection.Analyzer, GuideTarget.AnalyzerResults, "Check evidence and assignments",
            "Compare the arena map with the assignment table. The data-quality label distinguishes recorded mechanic evidence from timing estimated from nearby fight data.",
            "Read the quality label before treating an assignment as exact."),
        new(GuideSection.Options, GuideTarget.OptionsSettings, "Set capture and privacy",
            "The left side controls death capture, popup behavior, chat sharing, and on-screen name redaction. Redaction changes display, not names already stored in local pull files.",
            "Review Capture and Privacy & Chat before sharing screenshots or chat posts."),
        new(GuideSection.Options, GuideTarget.OptionsCustomize, "Customize the workspace",
            "The right side sets Review detail, timeline order and duration, icons, opacity, and themes. Style-theme animation and strength affect decoration, not combat results.",
            "Change a theme or Review layout, then return to Review to see it."),
        new(GuideSection.Data, GuideTarget.Data, "Know what is saved",
            "Data shows local storage and pull history. Recorded pulls and encounters stay on your computer. Optional chat sharing sends a local link; FFLogs analysis sends a report code to its service.",
            "Check storage details here; use the section Help button for the full privacy explanation."),
        new(GuideSection.Updates, GuideTarget.Updates, "Keep track of changes",
            "Updates contains the plugin changelog and release notes. You can reopen Walkthrough from the header at any time, and Help opens detailed instructions for the current section.",
            "You are ready to explore your own pulls."),
    ];

    public static int FirstStepFor(GuideSection section)
    {
        for (var index = 0; index < Steps.Count; index++)
        {
            if (Steps[index].Section == section)
                return index;
        }

        return 0;
    }
}
