using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace BetterDeaths.Windows;

public sealed class RecapWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private IReadOnlyList<PartyDeathRecord>? exampleDeaths;
    private DeathSelectionTarget? pendingDeathSelection;
    private uint recordedPullDutyFilter = AllRecordedPullDuties;
    private bool clearPendingDeathSelection;
    private bool collapseRecordedPullsRequested;
    private bool showDebugTab;
    private bool windowStylePushed;
    private bool reviewTimelineSplitterDragging;
    private string? stackedReviewSplitterDraggingId;
    private bool deathTimelineLeadUpResizeDragging;
    private string debugTextFilter = string.Empty;
    private string addonInspectorName = string.Empty;
    private string addonInspectorEventFilter = string.Empty;
    private string pullBrowserPlayerSearch = string.Empty;
    private bool addonInspectorHideCommonNoise = true;
    private int debugActorControlCategoryFilterIndex;
    private int? pendingMaxRecordedPulls;
    private float currentMainWindowBackgroundOpacity = Plugin.DefaultMainWindowBackgroundOpacity;
    private DataPageSnapshot dataPageSnapshot = DataPageSnapshot.Empty;
    private DateTime dataPageSnapshotRefreshedAtUtc = DateTime.MinValue;
    private readonly HashSet<string> expandedTimelineCauseRows = new(StringComparer.Ordinal);
    private readonly HashSet<string> selectedPossibleMitigationKeys = new(StringComparer.Ordinal);
    private string? collapsedSelectedTimelineLeadUpRowId;
    private readonly Dictionary<string, float> replayScrubSecondsByDeathId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> replayPlayingByDeathId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> replaySpeedByDeathId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> replayLastFrameAtUtcByDeathId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> replayZoomByDeathId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector2> replayPanByDeathId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> replayFocusedActorKeyByDeathId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayStaticDisplayCache> replayStaticDisplayCacheByDeathId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayFrameDisplayCache> replayFrameDisplayCacheByDeathId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayPullTimingCache> replayPullTimingCacheByDeathId = new(StringComparer.Ordinal);
    private readonly HashSet<string> replayCanvasPressedByDeathId = new(StringComparer.Ordinal);
    private readonly HashSet<string> replayCanvasDraggedByDeathId = new(StringComparer.Ordinal);
    private readonly HashSet<string> replayScrubStartedOnDeathMarkerByDeathId = new(StringComparer.Ordinal);
    private string? selectedReplayPullKey;
    private DeathSelectionTarget? replayFocusDeathSelection;
    private readonly ReviewSelectionState recapReviewSelection = new();
    private readonly ReviewSelectionState exampleReviewSelection = new();
    private MainPage currentMainPage = MainPage.Review;
    private DeathDetailPage selectedDeathDetailPage = DeathDetailPage.Summary;
    private static BetterDeathsUiTheme activeTheme = BetterDeathsThemeCatalog.GetTheme(BetterDeathsTheme.Classic);
    private static Vector4 DamageColor => activeTheme.DamageColor;
    private static Vector4 HealColor => activeTheme.HealColor;
    private static Vector4 WarningColor => activeTheme.WarningColor;
    private static Vector4 LeadUpGoldColor => activeTheme.LeadUpGoldColor;
    private static Vector4 SpamWarningColor => activeTheme.SpamWarningColor;
    private static Vector4 DisabledColor => activeTheme.DisabledColor;
    private static Vector4 UpdateBannerBgColor => activeTheme.UpdateBannerBgColor;
    private static Vector4 UpdateBannerTextColor => activeTheme.UpdateBannerTextColor;
    private static Vector4 NoticeBorderColor => activeTheme.NoticeBorderColor;
    private static Vector4 NoticeTextColor => activeTheme.NoticeTextColor;
    private static Vector4 NoticeButtonColor => activeTheme.NoticeButtonColor;
    private static Vector4 NoticeButtonHoveredColor => activeTheme.NoticeButtonHoveredColor;
    private static Vector4 HpBarColor => activeTheme.HpBarColor;
    private static Vector4 ShieldBarColor => activeTheme.ShieldBarColor;
    private static Vector4 BarBackgroundColor => activeTheme.BarBackgroundColor;
    private static Vector4 BarBorderColor => activeTheme.BarBorderColor;
    private static Vector4 OverkillColor => activeTheme.OverkillColor;
    private static Vector4 ModernShellColor => activeTheme.ModernShellColor;
    private static Vector4 ModernPanelColor => activeTheme.ModernPanelColor;
    private static Vector4 ModernPanelAltColor => activeTheme.ModernPanelAltColor;
    private static Vector4 ModernPanelBorderColor => activeTheme.ModernPanelBorderColor;
    private static Vector4 ModernAccentColor => activeTheme.ModernAccentColor;
    private static Vector4 ModernAccentSoftColor => activeTheme.ModernAccentSoftColor;
    private static Vector4 ModernMutedTextColor => activeTheme.ModernMutedTextColor;
    private static Vector4 ModernTextColor => activeTheme.ModernTextColor;
    private static Vector4 ModernDividerColor => activeTheme.ModernDividerColor;
    private static Vector4 ModernFrameColor => activeTheme.ModernFrameColor;
    private static Vector4 ModernFrameHoveredColor => activeTheme.ModernFrameHoveredColor;
    private static Vector4 ModernButtonHoveredColor => activeTheme.ModernButtonHoveredColor;
    private static Vector4 ModernNavButtonColor => activeTheme.ModernNavButtonColor;
    private static Vector4 ModernNavButtonHoveredColor => activeTheme.ModernNavButtonHoveredColor;
    private static Vector4 ModernNavButtonSelectedColor => activeTheme.ModernNavButtonSelectedColor;
    private static Vector4 ModernNavButtonSelectedHoveredColor => activeTheme.ModernNavButtonSelectedHoveredColor;
    private static Vector4 ModernNavButtonActiveColor => activeTheme.ModernNavButtonActiveColor;
    private static Vector4 ModernButtonTextColor => activeTheme.ModernButtonTextColor;
    private static Vector4 ModernSelectedButtonTextColor => activeTheme.ModernSelectedButtonTextColor;
    private static Vector4 ModernPopupBgColor => activeTheme.ModernPopupBgColor;
    private static Vector4 ModernCheckMarkColor => activeTheme.ModernCheckMarkColor;
    private static Vector4 ModernSliderGrabColor => activeTheme.ModernSliderGrabColor;
    private static Vector4 ModernSliderGrabActiveColor => activeTheme.ModernSliderGrabActiveColor;
    private static Vector4 ModernHeaderColor => activeTheme.ModernHeaderColor;
    private static Vector4 ModernHeaderHoveredColor => activeTheme.ModernHeaderHoveredColor;
    private static Vector4 ModernHeaderActiveColor => activeTheme.ModernHeaderActiveColor;
    private static Vector4 TimelineSelectedRowColor => activeTheme.TimelineSelectedRowColor;
    private static Vector4 TimelinePressedRowColor => activeTheme.TimelinePressedRowColor;
    private static readonly Vector2 DefaultWindowSize = new(1180.0f, 650.0f);
    private static readonly Vector2 TooltipWindowPadding = new(8.0f, 6.0f);
    private static readonly DateTime ExamplePullStartedAtUtc = new(2026, 6, 19, 0, 0, 0, DateTimeKind.Utc);
    private const string LikelyAutoAttackTooltip = "Possible auto attack. Better Deaths could not resolve a named action here; named spells and abilities usually show their action name.";
    private const string AutoActionDisplayName = "Auto";
    private const uint AllRecordedPullDuties = uint.MaxValue;
    private const string CurrentChangelogVersion = "0.1.0.249";
    private const string FeedbackDiscordUrl = "https://discord.com/invite/Zzrcc8kmvy";
    private const string FeedbackConfirmPopupId = "Open Punish Discord?##BetterDeathsFeedbackConfirm";
    private const string KofiUrl = "https://ko-fi.com/nainaiowo";
    private const string KofiConfirmPopupId = "Open Ko-fi?##BetterDeathsKofiConfirm";
    private const string ReplayBetaBadgeText = "beta";
    private const float LeadUpHistorySeconds = 10.0f;
    private const float DeathReplayLeadUpSeconds = 30.0f;
    private const float DeathReplayMaxPostDeathSeconds = 10.0f;
    private const float ReplayMarkerBadgeStackWindowSeconds = 1.25f;
    private const float ReplayTrailSeconds = 6.0f;
    private const float ReplayTrailMaxSegmentSeconds = 0.8f;
    private const float ReplayP4AssignmentSharedResolveWindowSeconds = 0.75f;
    private const float DmuReplayArenaCenterX = 100.0f;
    private const float DmuReplayArenaCenterZ = 100.0f;
    private const float ReplayPathOfLightTowerDistance = 8.0f;
    private const float ReplayPathOfLightFallbackDurationSeconds = 10.2f;
    private const float ReplayActionEffectSampleSnapWindowSeconds = 0.075f;
    private const float ReplayUntargetableStationaryHideSeconds = 15.0f;
    private const float ReplayPositionMovementEpsilon = 0.05f;
    private const float ReplayStationaryMaxSampleGapSeconds = 2.0f;
    private const float ReplayCanvasMinSide = 180.0f;
    private const float ReplayCanvasMaxSide = 820.0f;
    private const float ReplayMinZoom = 1.0f;
    private const float ReplayMaxZoom = 4.0f;
    private const float ReplayCanvasHorizontalGutter = 20.0f;
    private const float ReplayZoomSliderWidth = 220.0f;
    private const float ReplayZoomOverlayPadding = 10.0f;
    private const float ReplayZoomOverlayHeight = 30.0f;
    private const float ReplayWheelScrollSinkHeight = 1.0f;
    private const float ReplayWorldMarkerRadius = 9.0f;
    private const float ReplayWorldMarkerSquareHalfSize = 8.0f;
    private const float ReplayFacingChevronLength = 7.0f;
    private const float ReplayFacingChevronHalfWidth = 3.4f;
    private const float ReplayTimelineBarHeight = 28.0f;
    private const float ReplayDeathMarkerHoverRadius = 10.0f;
    private const float ReplayDeathMarkerOverlapRadius = 18.0f;
    private const float LeadUpTableRightPadding = 10.0f;
    private const float TimelineLeadUpDropdownMinHeight = 64.0f;
    private const float TimelineLeadUpDropdownMaxHeight = 560.0f;
    private const float TimelineLeadUpResizeHandleHeight = 14.0f;
    private const float TimelineLeadUpResizeHandleWidth = 72.0f;
    private const float TimelineLeadUpScrollBoundaryEpsilon = 0.5f;
    private const float TimelineLeadUpMouseWheelScrollLines = 5.0f;
    private const float MainNavigationButtonWidth = 118.0f;
    private const float MainNavigationButtonMinWidth = 92.0f;
    private const float MainNavigationCompactWidthThreshold = 300.0f;
    private const float DeathDetailButtonWidth = 112.0f;
    private const float DeathDetailCompactWidthThreshold = 270.0f;
    private const float FocusedLeadUpHpChangePercentThreshold = 5.0f;
    private const float FocusedDataRowPaddingX = 8.0f;
    private const float FocusedDataRowPaddingY = 5.0f;
    private const float FocusedDataRowGap = 3.0f;
    private const ImGuiWindowFlags ReplayCanvasChildFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground;
    private const int MaxReplayTrailPointsPerActor = 24;
    private const int MaxReplayMarkerBadgesPerActor = 3;
    private const string ReplayPathOfLightRawEventKind = "dmu-p2-path-of-light";
    private const string ReplayDmuP1FlagrantFireRawEventKind = "dmu-p1-flagrant-fire";
    private const float PullBodyIndent = 8.0f;
    private const float DeathDetailIndent = 8.0f;
    private const float SectionBodyIndent = 8.0f;
    private const float ReviewPaneContentIndent = 8.0f;
    private const float ReviewPaneHorizontalPadding = 9.0f;
    private const float ReviewPaneBottomPadding = 14.0f;
    private const float SectionHelpMarkerRightInset = 12.0f;
    private const float ReplayPageOuterPadding = 8.0f;
    private const float ReplayPaneHorizontalPadding = 14.0f;
    private const float ReplayPaneVerticalPadding = 8.0f;
    private const float ReplayPaneGap = 8.0f;
    private const float PullBrowserCollapsedWidth = 60.0f;
    private const float RecordedPullDutyFilterComboWidth = 260.0f;
    private const float PullBrowserExpandedWidth = RecordedPullDutyFilterComboWidth + (ReviewPaneHorizontalPadding * 2.0f);
    private const float PullBrowserHeaderButtonInset = 6.0f;
    private const float PullCellHeight = 58.0f;
    private const float CollapsedPullCellHeight = 30.0f;
    private const float PullGroupIndicatorThickness = 3.0f;
    private const float PullGroupTopDashWidth = 24.0f;
    private const float StackedPullCellRightPadding = 8.0f;
    private const float StackedCollapsedPullBrowserHeight = 88.0f;
    private const float StackedReviewSplitterHeight = 9.0f;
    private const float StackedExpandedPullBrowserMinHeight = 150.0f;
    private const float StackedExpandedPullBrowserMaxHeight = 460.0f;
    private const float StackedCollapsedPullBrowserMinHeight = 64.0f;
    private const float StackedCollapsedPullBrowserMaxHeight = 180.0f;
    private const float StackedTimelineMinHeight = 170.0f;
    private const float StackedTimelineMaxHeight = 620.0f;
    private const float StackedDeathDetailsMinHeight = 180.0f;
    private const float MinimumTimelinePaneWidth = 360.0f;
    private const float MinimumDeathDetailsPaneWidth = 430.0f;
    private const float ReviewPaneDividerWidth = 1.0f;
    private const float ReviewPaneSplitterWidth = 8.0f;
    private const float MinimumHpShieldBarWidth = 24.0f;
    private const string ThemeNewBadgeText = "New";
    private const uint ClearlyUnsurvivableOverMaxHp = 300_000;
    private const string CompactInfoSeparator = " \u00B7 ";
    private static readonly BetterDeathsTheme[] NewThemeBadges =
    [
        BetterDeathsTheme.Abyss,
        BetterDeathsTheme.Graphite,
        BetterDeathsTheme.Grape,
        BetterDeathsTheme.Soda,
        BetterDeathsTheme.Callus,
        BetterDeathsTheme.Lemonade,
        BetterDeathsTheme.Cotton,
        BetterDeathsTheme.Banana,
        BetterDeathsTheme.Hamtaro,
        BetterDeathsTheme.DrPepper,
        BetterDeathsTheme.Sprite,
        BetterDeathsTheme.MountainDew,
        BetterDeathsTheme.Coke,
        BetterDeathsTheme.Fanta,
        BetterDeathsTheme.GingerAle,
        BetterDeathsTheme.Pepsi,
    ];
    private static readonly Vector4[] BasePullGroupPalette =
    [
        new(0.20f, 0.74f, 0.88f, 1.0f),
        new(0.34f, 0.52f, 0.96f, 1.0f),
        new(0.62f, 0.46f, 0.94f, 1.0f),
        new(0.92f, 0.40f, 0.74f, 1.0f),
        new(0.88f, 0.34f, 0.44f, 1.0f),
        new(0.26f, 0.78f, 0.50f, 1.0f),
        new(0.24f, 0.66f, 0.62f, 1.0f),
        new(0.76f, 0.82f, 0.92f, 1.0f),
    ];
    private static readonly TimeSpan LeadUpStatusMergeWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LeadUpEventHpSampleWindow = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan LeadUpEventDuplicateWindow = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan LeadUpHpDuplicateWindow = TimeSpan.FromMilliseconds(50);
    private static readonly string[] DebugActorControlCategoryFilters =
    [
        "All",
        "Death",
        "DoT",
        "HoT",
        "Status gain/update/loss",
        "Tether/target",
        "Other",
    ];

    private sealed record HpHistoryDisplayRow(
        HpHistorySnapshot FirstSnapshot,
        HpHistorySnapshot LastSnapshot,
        IReadOnlyList<CombatEventRecord> Events,
        int SampleCount);

    private sealed record EarlierBossDebuffRow(
        DateTime SeenAtUtc,
        float PullElapsedSeconds,
        string SourceKey,
        string SourceName,
        string ActionName,
        StatusSnapshot Status);

    private sealed record WidgetMitStatus(StatusSnapshot Status, string Category, string SourceName);

    private readonly record struct ReplayMarkerBadgeDrawEntry(
        ReplayMarkerSnapshot Marker,
        float Alpha);

    private readonly record struct ReplayMarkerBadgeDisplay(
        string Text,
        Vector4 Color);

    private sealed record ReplayActorScreenState(
        ReplayPositionSnapshot Actor,
        IReadOnlyList<ReplayMarkerSnapshot> Markers,
        Vector2 ScreenPosition,
        float Radius,
        float InteractionRadius);

    private readonly record struct ReplayCanvasInputState(bool Hovered, bool Active);

    private readonly record struct ReplayDisplayDataSignature(
        IReadOnlyList<ReplayPositionSnapshot> PositionReference,
        int PositionCount,
        long FirstPositionTicks,
        long LastPositionTicks,
        IReadOnlyList<ReplayMarkerSnapshot> MarkerReference,
        int MarkerCount,
        long FirstMarkerTicks,
        long LastMarkerTicks,
        IReadOnlyList<ReplayMechanicSnapshot> MechanicReference,
        int MechanicCount,
        long FirstMechanicTicks,
        long LastMechanicTicks,
        IReadOnlyList<ReplayWorldMarkerSnapshot> WorldMarkerReference,
        int WorldMarkerCount,
        long FirstWorldMarkerTicks,
        long LastWorldMarkerTicks);

    private readonly record struct ReplayStaticDisplayCacheKey(
        long DeathSeenAtTicks,
        uint TerritoryId,
        bool ShowEarlierMarkers,
        ReplayDisplayDataSignature DataSignature);

    private readonly record struct ReplayFrameDisplayCacheKey(
        ReplayStaticDisplayCacheKey StaticKey,
        long SelectedAtTicks);

    private sealed record ReplayPositionTrack(
        string ActorKey,
        IReadOnlyList<ReplayPositionSnapshot> Positions);

    private sealed record ReplayStaticDisplayCache(
        ReplayStaticDisplayCacheKey Key,
        DateTime ReplayStartAtUtc,
        IReadOnlyList<ReplayPositionTrack> PositionTracks,
        IReadOnlyList<ReplayMechanicSnapshot> Mechanics,
        float MinOffset,
        float MaxOffset);

    private sealed record ReplayPullTimingCache(
        ReplayDisplayDataSignature DataSignature,
        DateTime ReferenceAtUtc,
        float MaxVisibleOffset);

    private sealed record ReplayFrameDisplayCache(
        ReplayFrameDisplayCacheKey Key,
        IReadOnlyList<ReplayPositionSnapshot> ActorStates,
        IReadOnlyList<ReplayMarkerSnapshot> MarkerStates,
        IReadOnlyList<ReplayMechanicSnapshot> MechanicStates,
        IReadOnlyList<ReplayWorldMarkerSnapshot> WorldMarkerStates);

    private sealed record ReplayDeathMarkerDisplayGroup(
        IReadOnlyList<PartyDeathRecord> Deaths,
        float X);

    private readonly record struct StackedReviewLayout(
        float PullBrowserHeight,
        float PullBrowserMinHeight,
        float PullBrowserMaxHeight,
        float TimelineHeight,
        float TimelineMinHeight,
        float TimelineMaxHeight,
        float DeathDetailsHeight);

    private enum StackedReviewResizeTarget
    {
        PullBrowser,
        CollapsedPullBrowser,
        Timeline,
    }

    private sealed record DmuP4AssignmentSummaryStatus(
        StatusSnapshot Status,
        string? RealityLabel,
        bool? IsReal,
        string? Resolution);

    private readonly record struct StatusDisplayKey(uint Id, uint IconId, uint SourceId, string Name);

    private sealed record BossStatusDisplayEntry(StatusSnapshot Status, string SourceName);

    private sealed record LeadUpSummaryRow(
        DateTime AnchorSeenAtUtc,
        HpHistoryDisplayRow Row,
        HpHistorySnapshot HpSnapshot,
        IReadOnlyList<CombatEventRecord> Events,
        IReadOnlyList<StatusSnapshot> SourceStatuses,
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> SourceStatusNames);

    private sealed record ResolvedDeathDisplay(
        uint TerritoryId,
        PartyDeathRecord Death,
        DeathDisplaySelection Selection,
        IReadOnlyList<CombatEventRecord> CauseEvents,
        IReadOnlyList<CombatEventRecord> LeadUpEvents,
        IReadOnlyList<LeadUpTimelineRow> TimelineRows,
        LeadUpSummaryRow? SummaryRow,
        IReadOnlyList<StatusSnapshot> SummaryMitigationDebuffStatuses,
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> SummaryMitigationDebuffStatusSources,
        IReadOnlyList<StatusSnapshot> SelectedMitigationDebuffStatuses,
        IReadOnlyList<ReplayMarkerSnapshot> PullReplayMarkers);

    private sealed record EventHpDisplay(
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        string TooltipDetail);

    private sealed record HpBarHealChange(
        uint PreviousCurrentHp,
        uint PreviousShieldHp);

    private sealed record HpBarDamageChange(
        uint ResultCurrentHp,
        uint ResultShieldHp);

    private sealed record LeadUpTimelineRow(
        DateTime SeenAtUtc,
        float PullElapsedSeconds,
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        IReadOnlyList<StatusSnapshot> Statuses,
        IReadOnlyList<StatusSnapshot> NearbyHpStatuses,
        IReadOnlyList<StatusSnapshot> SourceStatuses,
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> SourceStatusNames,
        CombatEventRecord? Event,
        string? HpTooltipDetail,
        HpBarHealChange? HealChange,
        HpBarDamageChange? DamageChange);

    private sealed record DerivedHpState(
        DateTime EventSeenAtUtc,
        string SourceName,
        string ActionName,
        uint Amount,
        uint SourceCurrentHp,
        uint SourceShieldHp,
        uint SourceMaxHp,
        uint DerivedCurrentHp,
        uint DerivedShieldHp,
        bool UsesCapturedResult);

    private sealed record OverkillDisplay(
        string Text,
        string CompactText,
        Vector4 Color,
        string TooltipLine);

    private sealed record MitigationTotalDisplay(
        double AllReduction,
        double PhysicalReduction,
        double MagicReduction,
        bool HasTypedReduction,
        bool AllVariable,
        bool PhysicalVariable,
        bool MagicVariable);

    private sealed record ReviewPull(
        string Key,
        string Title,
        string Subtitle,
        long? PullNumber,
        uint TerritoryId,
        string TerritoryName,
        float PullElapsedSeconds,
        int DeathCount,
        string PullGroupId,
        int PullGroupColorIndex,
        IReadOnlyList<PartyDeathRecord> Deaths,
        IReadOnlyList<ReplayMarkerSnapshot> ReplayMarkers,
        DeathSelectionSource Source,
        RecordedPullSummary? RecordedPull);

    private readonly record struct MainNavigationItem(
        string Label,
        MainPage Page,
        bool Highlight = false,
        string? BadgeText = null);

    private readonly record struct DeathDetailNavigationItem(
        string Label,
        DeathDetailPage Page,
        string? BadgeText = null);

    private sealed class ReviewSelectionState
    {
        public string? PullKey { get; set; }

        public long? DeathSeenAtTicks { get; set; }

        public uint? DeathMemberKeyHash { get; set; }
    }

    private readonly struct ModernStyleScope : IDisposable
    {
        public ModernStyleScope(float backgroundOpacity)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, WithBackgroundOpacity(ModernShellColor, backgroundOpacity));
            ImGui.PushStyleColor(ImGuiCol.Border, ModernPanelBorderColor);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, WithBackgroundOpacity(ModernFrameColor, backgroundOpacity));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ModernFrameHoveredColor);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ModernAccentSoftColor);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, ModernPopupBgColor);
            ImGui.PushStyleColor(ImGuiCol.CheckMark, ModernCheckMarkColor);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, ModernSliderGrabColor);
            ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, ModernSliderGrabActiveColor);
            ImGui.PushStyleColor(ImGuiCol.Header, ModernHeaderColor);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ModernHeaderHoveredColor);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, ModernHeaderActiveColor);
            ImGui.PushStyleColor(ImGuiCol.Text, ModernTextColor);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, ModernMutedTextColor);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, GetScrollbarBackgroundColor());
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, GetScrollbarGrabColor());
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, GetScrollbarGrabHoveredColor());
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, GetScrollbarGrabActiveColor());
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8.0f, 6.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(7.0f, 6.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, activeTheme.ModernFrameBorderSize);
        }

        public void Dispose()
        {
            ImGui.PopStyleVar(5);
            ImGui.PopStyleColor(18);
        }
    }

    private readonly struct ModernPanelScope : IDisposable
    {
        public ModernPanelScope()
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ModernPanelColor);
            ImGui.PushStyleColor(ImGuiCol.Border, ModernPanelBorderColor);
            ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, ModernPanelAltColor);
            ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, GetTableRowAltColor());
            ImGui.PushStyleColor(ImGuiCol.FrameBg, ModernFrameColor);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ModernFrameHoveredColor);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ModernAccentSoftColor);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, ModernPopupBgColor);
            ImGui.PushStyleColor(ImGuiCol.CheckMark, ModernCheckMarkColor);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, ModernSliderGrabColor);
            ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, ModernSliderGrabActiveColor);
            ImGui.PushStyleColor(ImGuiCol.Header, ModernHeaderColor);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ModernHeaderHoveredColor);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, ModernHeaderActiveColor);
            ImGui.PushStyleColor(ImGuiCol.Text, ModernTextColor);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, ModernMutedTextColor);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, GetScrollbarBackgroundColor());
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, GetScrollbarGrabColor());
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, GetScrollbarGrabHoveredColor());
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, GetScrollbarGrabActiveColor());
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 9.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8.0f, 7.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, activeTheme.ModernFrameBorderSize);
        }

        public void Dispose()
        {
            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(20);
        }
    }

    private readonly struct ModernWidgetScope : IDisposable
    {
        public ModernWidgetScope()
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, ModernPanelAltColor);
            ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, GetTableRowAltColor());
            ImGui.PushStyleColor(ImGuiCol.PopupBg, ModernPopupBgColor);
            ImGui.PushStyleColor(ImGuiCol.Text, ModernTextColor);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, ModernMutedTextColor);
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, GetScrollbarBackgroundColor());
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, GetScrollbarGrabColor());
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, GetScrollbarGrabHoveredColor());
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, GetScrollbarGrabActiveColor());
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6.0f, 4.0f));
        }

        public void Dispose()
        {
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(10);
        }
    }

    private readonly struct ImGuiIndentScope : IDisposable
    {
        private readonly float width;

        public ImGuiIndentScope(float width)
        {
            this.width = width;
            ImGui.Indent(width);
        }

        public void Dispose()
        {
            ImGui.Unindent(width);
        }
    }

    public RecapWindow(Plugin plugin) : base("Better Deaths###BetterDeaths")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
        showDebugTab = configuration.ShowDebugTab;

        Size = DefaultWindowSize;
        SizeCondition = ImGuiCond.FirstUseEver;
        ApplyScrollbarWindowFlag();
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        ApplyConfiguredTheme();
        ApplyScrollbarWindowFlag();
        if (configuration.ApplyWideDefaultWindowSizeOnNextOpen)
        {
            ImGui.SetNextWindowSize(DefaultWindowSize, ImGuiCond.Always);
            plugin.MarkWideDefaultWindowSizeApplied();
        }

        currentMainWindowBackgroundOpacity = GetMainWindowBackgroundOpacity();
        ImGui.SetNextWindowBgAlpha(currentMainWindowBackgroundOpacity);
        PushWindowStyle();
    }

    public override void PostDraw()
    {
        PopWindowStyle();
    }

    private void ApplyConfiguredTheme()
    {
        activeTheme = BetterDeathsThemeCatalog.GetTheme(configuration);
    }

    private ImGuiWindowFlags OptionalScrollbarFlags => configuration.ShowScrollbars
        ? ImGuiWindowFlags.None
        : ImGuiWindowFlags.NoScrollbar;

    private void ApplyScrollbarWindowFlag()
    {
        if (configuration.ShowScrollbars)
        {
            Flags &= ~ImGuiWindowFlags.NoScrollbar;
            return;
        }

        Flags |= ImGuiWindowFlags.NoScrollbar;
    }

    public override void Draw()
    {
        DrawPluginUpdateBanner();

        ApplyPendingSelectionPage();
        DrawModernShell();

        if (clearPendingDeathSelection)
        {
            pendingDeathSelection = null;
            clearPendingDeathSelection = false;
        }
    }

    public bool FocusDeath(long deathSeenAtTicks, uint memberKeyHash)
    {
        var target = ResolveDeathSelectionTarget(deathSeenAtTicks, memberKeyHash);
        if (target is null)
        {
            return false;
        }

        EnsureDeathSelectionTargetVisible(target);
        pendingDeathSelection = target;
        currentMainPage = target.Source == DeathSelectionSource.Example
            ? GetExampleSelectionPage()
            : MainPage.Review;
        clearPendingDeathSelection = false;
        IsOpen = true;
        return true;
    }

    private void ApplyPendingSelectionPage()
    {
        if (pendingDeathSelection is not { } target)
        {
            return;
        }

        currentMainPage = target.Source == DeathSelectionSource.Example
            ? GetExampleSelectionPage()
            : MainPage.Review;
    }

    private MainPage GetExampleSelectionPage()
    {
        return ShouldShowExamplePage()
            ? MainPage.Example
            : MainPage.Review;
    }

    private bool ShouldShowExamplePage()
    {
        return !plugin.RecordedPullHistoryLoading && plugin.RecordedPulls.Count == 0;
    }

    private void DrawModernShell()
    {
        using var shellStyle = new ModernStyleScope(currentMainWindowBackgroundOpacity);
        if (ImGui.BeginChild("##BetterDeathsModernShell", Vector2.Zero, false, OptionalScrollbarFlags))
        {
            using var shellIndent = new ImGuiIndentScope(ReviewPaneContentIndent);
            DrawModernHeader();
            DrawModernNavigation();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawModernPageContent();
        }

        ImGui.EndChild();
    }

    private void PushWindowStyle()
    {
        if (windowStylePushed)
        {
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.WindowBg, WithBackgroundOpacity(ModernShellColor, currentMainWindowBackgroundOpacity));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        windowStylePushed = true;
    }

    private void PopWindowStyle()
    {
        if (!windowStylePushed)
        {
            return;
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        windowStylePushed = false;
    }

    private void DrawModernHeader()
    {
        ImGui.TextColored(ModernAccentColor, "Better Deaths");
        ImGui.SameLine();
        ImGui.TextDisabled("Pull review that starts simple and opens detail only when needed.");
        DrawModernHeaderCredit();
    }

    private static void DrawModernHeaderCredit()
    {
        const float rightPadding = 12.0f;
        const string prefix = "Powered by ";
        const string nai = "Nai";
        const string middle = " and ";
        const string you = "You";

        var prefixSize = ImGui.CalcTextSize(prefix);
        var naiSize = ImGui.CalcTextSize(nai);
        var middleSize = ImGui.CalcTextSize(middle);
        var youSize = ImGui.CalcTextSize(you);
        var signatureWidth = prefixSize.X + naiSize.X + middleSize.X + youSize.X;
        var contentRight = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X - rightPadding;
        var subtitleEnd = ImGui.GetItemRectMax();
        var signatureStartX = contentRight - signatureWidth;
        var minimumGap = ImGui.GetStyle().ItemSpacing.X * 2.0f;
        if (signatureStartX <= subtitleEnd.X + minimumGap)
        {
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var mutedColor = ImGui.GetColorU32(ModernMutedTextColor);
        var highlightColor = ImGui.GetColorU32(ModernAccentColor);
        var position = new Vector2(signatureStartX, ImGui.GetItemRectMin().Y);

        drawList.AddText(position, mutedColor, prefix);
        position.X += prefixSize.X;
        drawList.AddText(position, highlightColor, nai);
        position.X += naiSize.X;
        drawList.AddText(position, mutedColor, middle);
        position.X += middleSize.X;
        drawList.AddText(position, highlightColor, you);
    }

    private void DrawModernNavigation()
    {
        var showExamplePage = ShouldShowExamplePage();
        if (!showExamplePage && currentMainPage == MainPage.Example)
        {
            currentMainPage = MainPage.Review;
        }

        var items = new List<MainNavigationItem>
        {
            new("Review", MainPage.Review),
            new("Replay", MainPage.Replay, BadgeText: ReplayBetaBadgeText),
            new("Customize", MainPage.Customize, HasUnseenNewThemeBadges()),
            new("Data", MainPage.Data),
            new("Feedback", MainPage.Feedback),
            new("Updates", MainPage.Updates, ShouldHighlightChangelogTab()),
        };
        if (showExamplePage)
        {
            items.Insert(1, new MainNavigationItem("Example", MainPage.Example));
        }

        if (showDebugTab)
        {
            items.Add(new MainNavigationItem("Debug", MainPage.Debug));
        }

        if (ImGui.GetContentRegionAvail().X < MainNavigationCompactWidthThreshold)
        {
            DrawMainNavigationCombo(items);
            return;
        }

        DrawResponsiveMainNavigationButtons(items);
    }

    private void DrawResponsiveMainNavigationButtons(IReadOnlyList<MainNavigationItem> items)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var fullWidth = (items.Count * MainNavigationButtonWidth) + (Math.Max(0, items.Count - 1) * spacing);
        var useCompactWidths = fullWidth > availableWidth;
        var rowWidth = 0.0f;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var buttonWidth = useCompactWidths
                ? Math.Clamp(GetResponsiveButtonWidth(item.Label, 30.0f), MainNavigationButtonMinWidth, MainNavigationButtonWidth)
                : MainNavigationButtonWidth;
            var nextWidth = rowWidth <= 0.0f
                ? buttonWidth
                : rowWidth + spacing + buttonWidth;
            if (rowWidth > 0.0f && nextWidth > availableWidth)
            {
                rowWidth = 0.0f;
            }
            else if (rowWidth > 0.0f)
            {
                ImGui.SameLine(0.0f, spacing);
            }

            DrawModernNavButton(item.Label, item.Page, item.Highlight, item.BadgeText, buttonWidth);
            if (item.Page == MainPage.Customize && item.Highlight && !useCompactWidths)
            {
                DrawFloatingNewBadgeOverLastItem();
            }

            rowWidth = rowWidth <= 0.0f
                ? buttonWidth
                : rowWidth + spacing + buttonWidth;
        }
    }

    private void DrawMainNavigationCombo(IReadOnlyList<MainNavigationItem> items)
    {
        var selectedItem = items.FirstOrDefault(item => item.Page == currentMainPage);
        var selectedLabel = string.IsNullOrWhiteSpace(selectedItem.Label)
            ? "Review"
            : selectedItem.Label;
        var width = MathF.Min(260.0f, MathF.Max(140.0f, ImGui.GetContentRegionAvail().X));
        ImGui.SetNextItemWidth(width);
        if (!ImGui.BeginCombo("##BetterDeathsMainNavigationCompact", selectedLabel))
        {
            return;
        }

        foreach (var item in items)
        {
            var selected = currentMainPage == item.Page;
            var label = item.BadgeText is not null
                ? $"{item.Label} ({item.BadgeText})"
                : item.Highlight
                    ? $"{item.Label}  new"
                    : item.Label;
            if (ImGui.Selectable(label, selected))
            {
                currentMainPage = item.Page;
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawModernPageContent()
    {
        switch (currentMainPage)
        {
            case MainPage.Replay:
                DrawReplayPage();
                break;
            case MainPage.Example:
                DrawExamplePullTab();
                break;
            case MainPage.Customize:
                DrawCustomizePage();
                break;
            case MainPage.Data:
                DrawReviewPanel("##DataModern", Vector2.Zero, DrawDataPage);
                break;
            case MainPage.Feedback:
                DrawReviewPanel("##FeedbackModern", Vector2.Zero, DrawFeedbackPage);
                break;
            case MainPage.Updates:
                plugin.MarkChangelogVersionSeen(CurrentChangelogVersion);
                DrawUpdatesPage();
                break;
            case MainPage.Debug:
                DrawReviewPanel("##DebugModern", Vector2.Zero, DrawDebugTab);
                break;
            default:
                DrawDeathRecapTab();
                break;
        }
    }

    private void DrawCustomizePage()
    {
        var available = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        if (available.X >= 980.0f)
        {
            var leftWidth = MathF.Max(420.0f, (available.X - spacing) * 0.48f);
            DrawReviewPanel(
                "##CustomizeSettings",
                new Vector2(leftWidth, available.Y),
                DrawSettingsTab);
            ImGui.SameLine();
            DrawReviewPanel(
                "##CustomizeWidget",
                Vector2.Zero,
                DrawWidgetTab);
            return;
        }

        DrawReviewPanel("##CustomizeSettingsStacked", new Vector2(0.0f, MathF.Min(360.0f, MathF.Max(240.0f, available.Y * 0.48f))), DrawSettingsTab);
        DrawReviewPanel("##CustomizeWidgetStacked", Vector2.Zero, DrawWidgetTab);
    }

    private void DrawUpdatesPage()
    {
        var available = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        if (available.X >= 980.0f)
        {
            var leftWidth = MathF.Max(390.0f, (available.X - spacing) * 0.42f);
            DrawReviewPanel(
                "##UpdatesNotes",
                new Vector2(leftWidth, available.Y),
                DrawNotesTab);
            ImGui.SameLine();
            DrawReviewPanel(
                "##UpdatesChangelog",
                Vector2.Zero,
                DrawChangelogTab);
            return;
        }

        DrawReviewPanel("##UpdatesChangelogStacked", new Vector2(0.0f, MathF.Min(420.0f, MathF.Max(260.0f, available.Y * 0.55f))), DrawChangelogTab);
        DrawReviewPanel("##UpdatesNotesStacked", Vector2.Zero, DrawNotesTab);
    }

    private void DrawModernNavButton(
        string label,
        MainPage page,
        bool highlight = false,
        string? badgeText = null,
        float width = MainNavigationButtonWidth)
    {
        var selected = currentMainPage == page;
        var buttonColor = selected
            ? ModernNavButtonSelectedColor
            : ModernNavButtonColor;
        var hoveredColor = selected
            ? ModernNavButtonSelectedHoveredColor
            : ModernNavButtonHoveredColor;
        var textColor = highlight
            ? LeadUpGoldColor
            : GetButtonTextColor(buttonColor, selected);

        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ModernNavButtonActiveColor);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        if (ImGui.Button($"{label}##MainNav{page}", new Vector2(width, 30.0f)))
        {
            currentMainPage = page;
        }
        var buttonMin = ImGui.GetItemRectMin();
        var buttonMax = ImGui.GetItemRectMax();
        if (!string.IsNullOrWhiteSpace(badgeText))
        {
            DrawFloatingTabPill(buttonMin, buttonMax, badgeText);
        }

        if (highlight)
        {
            DrawChangelogTabHighlightBorder();
        }

        ImGui.PopStyleColor(4);
    }

    private static void DrawFloatingNewBadgeOverLastItem()
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        if (max.X <= min.X || max.Y <= min.Y)
        {
            return;
        }

        var size = GetNewBadgeSize();
        var bounce = MathF.Abs(MathF.Sin((float)ImGui.GetTime() * 5.8f)) * 4.0f;
        var position = new Vector2(
            min.X + ((max.X - min.X - size.X) * 0.5f),
            min.Y - 11.0f - bounce);
        DrawNewBadge(position, size);
    }

    private static void DrawInlineNewBadge()
    {
        var size = GetNewBadgeSize();
        var cursor = ImGui.GetCursorScreenPos();
        var textLineHeight = ImGui.GetTextLineHeight();
        var position = new Vector2(
            cursor.X,
            cursor.Y + MathF.Max(0.0f, (textLineHeight - size.Y) * 0.5f) - 1.0f);

        DrawNewBadge(position, size);
        ImGui.Dummy(size);
    }

    private static Vector2 GetNewBadgeSize()
    {
        return ImGui.CalcTextSize(ThemeNewBadgeText) + new Vector2(14.0f, 5.0f);
    }

    private static void DrawNewBadge(Vector2 position, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var end = position + size;
        var rounding = MathF.Min(9.0f, size.Y * 0.5f);
        var textSize = ImGui.CalcTextSize(ThemeNewBadgeText);
        var textPosition = position + ((size - textSize) * 0.5f);
        var pulse = (MathF.Sin((float)ImGui.GetTime() * 3.6f) + 1.0f) * 0.5f;
        var fill = new Vector4(1.0f, 0.30f + (pulse * 0.05f), 0.48f + (pulse * 0.05f), 1.0f);
        var dark = new Vector4(0.035f, 0.025f, 0.035f, 0.94f);
        var shadow = new Vector4(0.0f, 0.0f, 0.0f, 0.58f);
        var textColor = new Vector4(1.0f, 0.96f, 0.88f, 1.0f);

        drawList.AddRectFilled(position + new Vector2(2.0f), end + new Vector2(2.0f), ImGui.GetColorU32(shadow), rounding);
        drawList.AddRectFilled(position, end, ImGui.GetColorU32(fill), rounding);
        drawList.AddRect(position, end, ImGui.GetColorU32(dark), rounding);
        drawList.AddRect(
            position + new Vector2(1.0f),
            end - new Vector2(1.0f),
            ImGui.GetColorU32(LeadUpGoldColor with { W = 0.50f }),
            MathF.Max(1.0f, rounding - 1.0f));
        drawList.AddText(textPosition + new Vector2(1.0f), ImGui.GetColorU32(dark), ThemeNewBadgeText);
        drawList.AddText(textPosition, ImGui.GetColorU32(textColor), ThemeNewBadgeText);
    }

    private static Vector4 GetModernNavSelectedTextColor()
    {
        return ActiveThemeUsesLightPanels()
            ? ModernTextColor
            : ModernAccentColor;
    }

    private static Vector4 GetTableRowAltColor()
    {
        return activeTheme.TableRowAltColor;
    }

    private void DrawDeathRecapTab()
    {
        var pulls = BuildDeathRecapReviewPulls();
        DrawReviewWorkspace(
            pulls,
            "DeathRecap",
            showPullBrowser: true,
            recapReviewSelection);
    }

    private void DrawReplayPage()
    {
        var visiblePulls = GetVisibleRecordedPulls()
            .Where(entry => entry.Summary.DeathCount > 0)
            .ToList();

        DrawReviewPanel(
            "##ReplayPage",
            Vector2.Zero,
            () =>
            {
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(ReplayPageOuterPadding, ReplayPageOuterPadding));
                var replayPageVisible = ImGui.BeginChild("##ReplayPagePaddedContent", Vector2.Zero, false, ImGuiWindowFlags.NoScrollbar);
                ImGui.PopStyleVar();
                if (!replayPageVisible)
                {
                    ImGui.EndChild();
                    return;
                }

                if (visiblePulls.Count == 0)
                {
                    ImGui.TextDisabled(plugin.RecordedPullHistoryLoading
                        ? "Loading saved pulls..."
                        : "No saved death pulls have replay data yet.");
                    DrawReviewPaneBottomPadding();
                    ImGui.EndChild();
                    return;
                }

                EnsureReplayPullSelection(visiblePulls);
                var selectedEntry = visiblePulls.FirstOrDefault(entry =>
                    string.Equals(BuildRecordedPullKey(entry.Summary), selectedReplayPullKey, StringComparison.Ordinal));
                if (selectedEntry.Summary is null)
                {
                    selectedEntry = visiblePulls[0];
                }

                var available = ImGui.GetContentRegionAvail();
                if (available.X >= 860.0f)
                {
                    var browserWidth = MathF.Min(300.0f, MathF.Max(230.0f, available.X * 0.28f));
                    DrawReplayPane(
                        "##ReplayPullBrowser",
                        new Vector2(browserWidth, available.Y),
                        () => DrawReplayPullBrowser(visiblePulls));
                    DrawVerticalReplayDivider("ReplayPullBrowserDivider", available.Y);
                    DrawReplayPane(
                        "##ReplayViewer",
                        new Vector2(0.0f, available.Y),
                        () => DrawReplayViewer(selectedEntry.Summary));
                    ImGui.EndChild();
                    return;
                }

                var browserHeight = MathF.Min(230.0f, MathF.Max(150.0f, available.Y * 0.30f));
                DrawReplayPane(
                    "##ReplayPullBrowserStacked",
                    new Vector2(0.0f, browserHeight),
                    () => DrawReplayPullBrowser(visiblePulls, useCells: true));
                ImGui.Dummy(new Vector2(1.0f, ReplayPaneGap));
                DrawReplayPane(
                    "##ReplayViewerStacked",
                    Vector2.Zero,
                    () => DrawReplayViewer(selectedEntry.Summary));
                ImGui.EndChild();
            },
            indentContent: false);
    }

    private void EnsureReplayPullSelection(IReadOnlyList<(RecordedPullSummary Summary, long PullNumber)> visiblePulls)
    {
        if (selectedReplayPullKey is not null &&
            visiblePulls.Any(entry => string.Equals(BuildRecordedPullKey(entry.Summary), selectedReplayPullKey, StringComparison.Ordinal)))
        {
            return;
        }

        selectedReplayPullKey = BuildRecordedPullKey(visiblePulls[0].Summary);
        replayFocusDeathSelection = null;
    }

    private void DrawReplayPullBrowser(
        IReadOnlyList<(RecordedPullSummary Summary, long PullNumber)> visiblePulls,
        bool useCells = false)
    {
        using var paneIndent = new ImGuiIndentScope(ReviewPaneContentIndent);
        DrawModernSectionTitle("Replays");
        DrawRecordedPullControls();
        ImGui.Separator();

        if (ImGui.BeginChild("##ReplayPullRows", Vector2.Zero, false, OptionalScrollbarFlags))
        {
            foreach (var (summary, _) in visiblePulls)
            {
                var key = BuildRecordedPullKey(summary);
                var selected = string.Equals(selectedReplayPullKey, key, StringComparison.Ordinal);
                var pull = CreateReplayReviewPull(summary);
                var rowId = $"ReplayPullRow{key}";
                var clicked = useCells
                    ? DrawExpandedPullCell(pull, rowId, selected, StackedPullCellRightPadding)
                    : DrawWidePullListItem(pull, rowId, selected);
                if (clicked)
                {
                    selectedReplayPullKey = key;
                    replayFocusDeathSelection = null;
                }

                if (ImGui.IsItemHovered())
                {
                    SetThemedTooltip(FormatExpandedPullTooltip(pull));
                }

                if (useCells)
                {
                    ImGui.Dummy(new Vector2(1.0f, 6.0f));
                }
            }

            DrawReviewPaneBottomPadding();
        }

        ImGui.EndChild();
    }

    private static ReviewPull CreateReplayReviewPull(RecordedPullSummary summary)
    {
        return new ReviewPull(
            BuildRecordedPullKey(summary),
            $"Pull {summary.PullNumber}",
            string.Empty,
            summary.PullNumber,
            summary.TerritoryId,
            summary.TerritoryName,
            summary.PullElapsedSeconds,
            summary.DeathCount,
            summary.PullGroupId,
            summary.PullGroupColorIndex,
            [],
            [],
            DeathSelectionSource.Recorded,
            summary);
    }

    private void DrawReplayViewer(RecordedPullSummary summary)
    {
        var detail = plugin.GetRecordedPullDetails(summary);
        if (detail is null)
        {
            DrawModernSectionTitle($"Pull {summary.PullNumber} Replay");
            ImGui.TextDisabled(plugin.RecordedPullHistoryLoading
                ? "Loading replay..."
                : "Replay details could not be loaded.");
            DrawReviewPaneBottomPadding();
            return;
        }

        DrawModernSectionTitle(
            $"Pull {summary.PullNumber} Replay",
            $"{summary.TerritoryName} - {FormatCombatTimer(summary.PullElapsedSeconds)}");
        ImGui.TextDisabled(FormatRecordedPullCapturedTime(summary));
        ImGui.Spacing();

        var focusDeath = GetReplayFocusDeath(detail, summary) ?? GetDeathsInTimelineOrder(detail.Deaths).FirstOrDefault();
        if (!HasFullPullReplayData(detail))
        {
            DrawMutedWrappedText("This pull was captured before full-pull replay data was saved.");
            DrawReviewPaneBottomPadding();
            return;
        }

        DrawFullPullReplayContext(detail, summary, focusDeath);
        DrawReviewPaneBottomPadding();
    }

    private void DrawFullPullReplayContext(PullDeathSnapshot pull, RecordedPullSummary summary, PartyDeathRecord? focusDeath)
    {
        var idSuffix = BuildReplayViewerId(summary);
        var positions = pull.ReplayPositions;
        var markers = pull.ReplayMarkers;
        var rawMechanics = pull.ReplayMechanics;
        var worldMarkers = pull.ReplayWorldMarkers;
        var replayModule = ReplayEncounterModules.Get(pull.TerritoryId);
        if (positions.Count == 0 && markers.Count == 0 && rawMechanics.Count == 0 && worldMarkers.Count == 0)
        {
            DrawMutedWrappedText("No replay position data was captured for this pull.");
            return;
        }

        KeepOnlyActiveReplayDisplayCache(idSuffix);
        var timing = GetReplayPullTimingCache(idSuffix, pull, positions, markers, rawMechanics, worldMarkers);
        var referenceAtUtc = timing.ReferenceAtUtc;
        var maxVisibleOffset = timing.MaxVisibleOffset;
        var replayDisplay = GetReplayStaticDisplayCache(
            idSuffix,
            pull.CapturedAtUtc.Ticks,
            pull.TerritoryId,
            referenceAtUtc,
            referenceAtUtc,
            0.0f,
            maxVisibleOffset,
            positions,
            markers,
            rawMechanics,
            worldMarkers,
            replayModule,
            showEarlierMarkers: true);
        var minOffset = replayDisplay.MinOffset;
        var maxOffset = replayDisplay.MaxOffset;

        if (!replayScrubSecondsByDeathId.TryGetValue(idSuffix, out var scrubSeconds))
        {
            scrubSeconds = focusDeath is null
                ? minOffset
                : MathF.Max(minOffset, focusDeath.PullElapsedSeconds - DeathReplayLeadUpSeconds);
            replayScrubSecondsByDeathId[idSuffix] = scrubSeconds;
        }

        scrubSeconds = Math.Clamp(scrubSeconds, minOffset, maxOffset);
        DrawDeathReplayControls(
            idSuffix,
            minOffset,
            maxOffset,
            ref scrubSeconds,
            focusDeath?.PullElapsedSeconds,
            "Death");
        DrawFullReplayTimelineScrubber(pull, summary, minOffset, maxOffset, ref scrubSeconds, focusDeath, idSuffix);

        var focusText = focusDeath is null
            ? string.Empty
            : $" | Death {FormatReplayOffset(scrubSeconds - focusDeath.PullElapsedSeconds)}";
        DrawMutedWrappedText($"Pull {FormatCombatTimer(scrubSeconds)}{focusText}");
        var selectedAtUtc = referenceAtUtc.AddSeconds(scrubSeconds);
        var replayFrame = GetReplayFrameDisplayCache(
            idSuffix,
            positions,
            markers,
            worldMarkers,
            replayDisplay,
            selectedAtUtc,
            showEarlierMarkers: true,
            replayModule);
        var showTrails = GetReplayShowTrails();
        DrawDeathReplayCanvas(
            focusDeath,
            positions,
            replayDisplay.PositionTracks,
            replayDisplay.Mechanics,
            markers,
            replayFrame.ActorStates,
            replayFrame.MarkerStates,
            replayFrame.MechanicStates,
            worldMarkers,
            replayFrame.WorldMarkerStates,
            selectedAtUtc,
            idSuffix,
            replayModule,
            showTrails);

        DrawDeathReplayDisplaySettings(idSuffix);
    }

    private static string BuildReplayViewerId(RecordedPullSummary summary)
    {
        return $"FullReplay:{summary.PullNumber}:{summary.CapturedAtUtc.Ticks}";
    }

    private static bool HasFullPullReplayData(PullDeathSnapshot pull)
    {
        return pull.ReplayPositions.Count > 0 ||
            pull.ReplayMarkers.Count > 0 ||
            pull.ReplayMechanics.Count > 0 ||
            pull.ReplayWorldMarkers.Count > 0;
    }

    private PartyDeathRecord? GetReplayFocusDeath(PullDeathSnapshot pull, RecordedPullSummary summary)
    {
        if (replayFocusDeathSelection is not { } target ||
            !DeathSelectionSourceMatches(target, DeathSelectionSource.Recorded, summary))
        {
            return null;
        }

        return pull.Deaths.FirstOrDefault(death => IsDeathTarget(death, target.DeathSeenAtTicks, target.MemberKeyHash));
    }

    private static DateTime GetPullReplayReferenceAtUtc(PullDeathSnapshot pull)
    {
        if (pull.ReplayPositions.Count > 0)
        {
            var firstPosition = pull.ReplayPositions
                .OrderBy(position => position.SeenAtUtc)
                .First();
            return firstPosition.SeenAtUtc.AddSeconds(-Math.Max(0.0f, firstPosition.PullElapsedSeconds));
        }

        if (pull.ReplayMechanics.Count > 0)
        {
            var firstMechanic = pull.ReplayMechanics
                .OrderBy(mechanic => mechanic.SeenAtUtc)
                .First();
            return firstMechanic.SeenAtUtc.AddSeconds(-Math.Max(0.0f, firstMechanic.PullElapsedSeconds));
        }

        if (pull.ReplayMarkers.Count > 0)
        {
            var firstMarker = pull.ReplayMarkers
                .OrderBy(marker => marker.SeenAtUtc)
                .First();
            return firstMarker.SeenAtUtc.AddSeconds(-Math.Max(0.0f, firstMarker.PullElapsedSeconds));
        }

        if (pull.ReplayWorldMarkers.Count > 0)
        {
            var firstWorldMarker = pull.ReplayWorldMarkers
                .OrderBy(marker => marker.SeenAtUtc)
                .First();
            return firstWorldMarker.SeenAtUtc.AddSeconds(-Math.Max(0.0f, firstWorldMarker.PullElapsedSeconds));
        }

        return pull.CapturedAtUtc.AddSeconds(-Math.Max(0.0f, pull.PullElapsedSeconds));
    }

    private static float GetFullPullReplayMaxOffset(
        DateTime referenceAtUtc,
        PullDeathSnapshot pull,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayMechanicSnapshot> mechanics,
        IReadOnlyList<ReplayWorldMarkerSnapshot> worldMarkers)
    {
        var maxOffset = MathF.Max(0.5f, pull.PullElapsedSeconds);
        foreach (var position in positions)
        {
            maxOffset = MathF.Max(maxOffset, (float)(position.SeenAtUtc - referenceAtUtc).TotalSeconds);
        }

        foreach (var mechanic in mechanics)
        {
            maxOffset = MathF.Max(
                maxOffset,
                (float)(mechanic.SeenAtUtc - referenceAtUtc).TotalSeconds + MathF.Max(0.0f, mechanic.DurationSeconds));
        }

        foreach (var marker in markers)
        {
            maxOffset = MathF.Max(maxOffset, (float)(marker.SeenAtUtc - referenceAtUtc).TotalSeconds);
        }

        foreach (var marker in worldMarkers)
        {
            maxOffset = MathF.Max(maxOffset, (float)(marker.SeenAtUtc - referenceAtUtc).TotalSeconds);
        }

        return MathF.Max(0.5f, maxOffset);
    }

    private ReplayPullTimingCache GetReplayPullTimingCache(
        string idSuffix,
        PullDeathSnapshot pull,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayMechanicSnapshot> mechanics,
        IReadOnlyList<ReplayWorldMarkerSnapshot> worldMarkers)
    {
        var dataSignature = CreateReplayDisplayDataSignature(positions, markers, mechanics, worldMarkers);
        if (replayPullTimingCacheByDeathId.TryGetValue(idSuffix, out var cached) &&
            cached.DataSignature.Equals(dataSignature))
        {
            return cached;
        }

        var referenceAtUtc = GetPullReplayReferenceAtUtc(pull);
        var maxVisibleOffset = GetFullPullReplayMaxOffset(referenceAtUtc, pull, positions, markers, mechanics, worldMarkers);
        var updated = new ReplayPullTimingCache(dataSignature, referenceAtUtc, maxVisibleOffset);
        replayPullTimingCacheByDeathId[idSuffix] = updated;
        return updated;
    }

    private void DrawFullReplayTimelineScrubber(
        PullDeathSnapshot pull,
        RecordedPullSummary summary,
        float minOffset,
        float maxOffset,
        ref float scrubSeconds,
        PartyDeathRecord? focusDeath,
        string idSuffix)
    {
        var availableWidth = MathF.Max(180.0f, ImGui.GetContentRegionAvail().X);
        const float markerRadius = 6.0f;
        const float focusedMarkerRadius = 7.0f;
        var start = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##FullReplayScrubTimeline{idSuffix}", new Vector2(availableWidth, ReplayTimelineBarHeight));
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        var active = ImGui.IsItemActive();
        var activated = ImGui.IsItemActivated();
        var drawList = ImGui.GetWindowDrawList();
        var end = start + new Vector2(availableWidth, ReplayTimelineBarHeight);
        var centerY = start.Y + (ReplayTimelineBarHeight * 0.5f);
        var range = MathF.Max(0.001f, maxOffset - minOffset);
        var mouse = ImGui.GetIO().MousePos;
        var markerGroups = BuildReplayDeathMarkerGroups(pull.Deaths, start.X, availableWidth, minOffset, maxOffset, range);
        var hoveredMarkerGroup = hovered
            ? FindHoveredReplayDeathMarkerGroup(markerGroups, mouse.X, scrubSeconds)
            : null;
        var pressStartedOnMarker = hoveredMarkerGroup is not null;

        if (activated)
        {
            replayPlayingByDeathId[idSuffix] = false;
            if (pressStartedOnMarker)
            {
                replayScrubStartedOnDeathMarkerByDeathId.Add(idSuffix);
            }
            else
            {
                replayScrubStartedOnDeathMarkerByDeathId.Remove(idSuffix);
            }
        }

        var markerPressActive = replayScrubStartedOnDeathMarkerByDeathId.Contains(idSuffix);
        if (!active)
        {
            replayScrubStartedOnDeathMarkerByDeathId.Remove(idSuffix);
            markerPressActive = false;
        }

        if (clicked && hoveredMarkerGroup is not null)
        {
            var clickedDeath = hoveredMarkerGroup.Deaths
                .OrderBy(death => death.PullElapsedSeconds)
                .ThenBy(death => FormatPlayerName(death, pull.Deaths), StringComparer.OrdinalIgnoreCase)
                .First();
            replayFocusDeathSelection = BuildRecordedDeathSelectionTarget(
                clickedDeath.SeenAtUtc.Ticks,
                Plugin.GetMemberKeyHash(clickedDeath.MemberKey),
                summary);
            scrubSeconds = Math.Clamp(
                clickedDeath.PullElapsedSeconds - DeathReplayLeadUpSeconds,
                minOffset,
                maxOffset);
            replayScrubSecondsByDeathId[idSuffix] = scrubSeconds;
            replayPlayingByDeathId[idSuffix] = false;
        }
        else if (active && !markerPressActive)
        {
            var newScrubSeconds = Math.Clamp(
                minOffset + (((mouse.X - start.X) / availableWidth) * range),
                minOffset,
                maxOffset);
            if (MathF.Abs(newScrubSeconds - scrubSeconds) > 0.001f)
            {
                scrubSeconds = newScrubSeconds;
                replayScrubSecondsByDeathId[idSuffix] = scrubSeconds;
            }

            replayPlayingByDeathId[idSuffix] = false;
        }

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        drawList.AddRectFilled(start, end, ImGui.GetColorU32(ModernPanelAltColor with { W = 0.42f }), 4.0f);
        drawList.AddRect(start, end, ImGui.GetColorU32(ModernPanelBorderColor with { W = 0.70f }), 4.0f);

        if (focusDeath is not null)
        {
            var highlightStart = Math.Clamp(focusDeath.PullElapsedSeconds - DeathReplayLeadUpSeconds, minOffset, maxOffset);
            var highlightEnd = Math.Clamp(focusDeath.PullElapsedSeconds + DeathReplayMaxPostDeathSeconds, minOffset, maxOffset);
            var highlightX1 = start.X + ((highlightStart - minOffset) / range * availableWidth);
            var highlightX2 = start.X + ((highlightEnd - minOffset) / range * availableWidth);
            drawList.AddRectFilled(
                new Vector2(highlightX1, start.Y + 4.0f),
                new Vector2(MathF.Max(highlightX1 + 2.0f, highlightX2), end.Y - 4.0f),
                ImGui.GetColorU32(LeadUpGoldColor with { W = 0.20f }),
                3.0f);
        }

        var scrubX = start.X + ((Math.Clamp(scrubSeconds, minOffset, maxOffset) - minOffset) / range * availableWidth);
        drawList.AddLine(
            new Vector2(scrubX, start.Y + 2.0f),
            new Vector2(scrubX, end.Y - 2.0f),
            ImGui.GetColorU32(ModernAccentColor),
            2.0f);

        var timeText = FormatCombatTimer(scrubSeconds);
        var timeTextSize = ImGui.CalcTextSize(timeText);
        drawList.AddText(
            new Vector2(start.X + ((availableWidth - timeTextSize.X) * 0.5f), centerY - (timeTextSize.Y * 0.5f)),
            ImGui.GetColorU32(ModernTextColor),
            timeText);

        var skullIcon = FontAwesomeIcon.Skull.ToIconString();
        var skullSize = ImGui.CalcTextSize(skullIcon);
        foreach (var group in markerGroups)
        {
            var isFocused = focusDeath is not null && group.Deaths.Any(death =>
                IsDeathTarget(death, focusDeath.SeenAtUtc.Ticks, Plugin.GetMemberKeyHash(focusDeath.MemberKey)));
            var markerColor = isFocused ? LeadUpGoldColor : DamageColor;
            var radius = isFocused ? focusedMarkerRadius : markerRadius;
            var markerCenter = new Vector2(group.X, centerY);
            drawList.AddLine(
                new Vector2(group.X, start.Y + 4.0f),
                new Vector2(group.X, end.Y - 4.0f),
                ImGui.GetColorU32(markerColor with { W = isFocused ? 0.92f : 0.62f }),
                isFocused ? 2.0f : 1.2f);
            drawList.AddCircleFilled(markerCenter, radius + 2.0f, ImGui.GetColorU32(ModernPanelColor with { W = 0.92f }), 16);
            drawList.AddCircleFilled(markerCenter, radius, ImGui.GetColorU32(markerColor), 16);
            drawList.AddText(
                markerCenter - (skullSize * 0.5f),
                ImGui.GetColorU32(ModernPanelColor),
                skullIcon);

            if (group.Deaths.Count > 1)
            {
                var countText = $"x{group.Deaths.Count}";
                var countSize = ImGui.CalcTextSize(countText);
                var countPos = new Vector2(
                    MathF.Min(end.X - countSize.X - 4.0f, markerCenter.X + radius + 3.0f),
                    markerCenter.Y - (countSize.Y * 0.5f));
                drawList.AddText(countPos, ImGui.GetColorU32(markerColor), countText);
            }
        }

        if (hoveredMarkerGroup is not null)
        {
            SetThemedTooltip(string.Join("\n", hoveredMarkerGroup.Deaths
                .OrderBy(death => death.PullElapsedSeconds)
                .ThenBy(death => FormatPlayerName(death, pull.Deaths), StringComparer.OrdinalIgnoreCase)
                .Select(death => FormatPlayerName(death, pull.Deaths))));
        }
    }

    private static IReadOnlyList<ReplayDeathMarkerDisplayGroup> BuildReplayDeathMarkerGroups(
        IReadOnlyList<PartyDeathRecord> deaths,
        float startX,
        float availableWidth,
        float minOffset,
        float maxOffset,
        float range)
    {
        var markers = GetDeathsInTimelineOrder(deaths)
            .Select(death => (
                Death: death,
                X: startX + ((Math.Clamp(death.PullElapsedSeconds, minOffset, maxOffset) - minOffset) / range * availableWidth)))
            .OrderBy(marker => marker.X)
            .ThenBy(marker => marker.Death.PullElapsedSeconds)
            .ToList();
        var groups = new List<(List<PartyDeathRecord> Deaths, float X)>();
        foreach (var marker in markers)
        {
            if (groups.Count > 0 && MathF.Abs(marker.X - groups[^1].X) <= ReplayDeathMarkerOverlapRadius)
            {
                var group = groups[^1];
                group.Deaths.Add(marker.Death);
                group.X = (group.X * (group.Deaths.Count - 1) + marker.X) / group.Deaths.Count;
                groups[^1] = group;
                continue;
            }

            groups.Add((new List<PartyDeathRecord> { marker.Death }, marker.X));
        }

        return groups
            .Select(group => new ReplayDeathMarkerDisplayGroup(group.Deaths, group.X))
            .ToList();
    }

    private static ReplayDeathMarkerDisplayGroup? FindHoveredReplayDeathMarkerGroup(
        IReadOnlyList<ReplayDeathMarkerDisplayGroup> groups,
        float mouseX,
        float scrubSeconds)
    {
        return groups
            .Where(group => MathF.Abs(mouseX - group.X) <= ReplayDeathMarkerHoverRadius)
            .OrderBy(group => MathF.Abs(mouseX - group.X))
            .ThenBy(group => group.Deaths.Min(death => MathF.Abs(death.PullElapsedSeconds - scrubSeconds)))
            .FirstOrDefault();
    }

    private void DrawExamplePullTab()
    {
        var deaths = GetExampleDeaths();
        var pulls = new List<ReviewPull>
        {
            new(
                "ExamplePull",
                "Example pull",
                "Sigmascape V4.0 - Timer 04:53",
                null,
                0,
                "Sigmascape V4.0",
                293.0f,
                deaths.Count,
                string.Empty,
                -1,
                deaths,
                deaths.SelectMany(death => death.ReplayMarkers).ToList(),
                DeathSelectionSource.Example,
                null),
        };

        DrawReviewWorkspace(
            pulls,
            "ExamplePull",
            showPullBrowser: false,
            exampleReviewSelection);
    }

    private List<ReviewPull> BuildDeathRecapReviewPulls()
    {
        var pulls = new List<ReviewPull>();
        if (!plugin.CurrentPullClosedForReview)
        {
            pulls.Add(new ReviewPull(
                "Current",
                "Current pull",
                $"{plugin.CurrentPullTerritoryName} - Timer {FormatCombatTimer(plugin.CurrentPullElapsedSeconds)}",
                null,
                plugin.CurrentTerritoryId,
                plugin.CurrentPullTerritoryName,
                plugin.CurrentPullElapsedSeconds,
                plugin.CurrentDeaths.Count,
                plugin.CurrentDutyInstancePullGroupId,
                plugin.CurrentDutyInstancePullGroupColorIndex,
                plugin.CurrentDeaths,
                plugin.GetCurrentPullReplayMarkersForReview(),
                DeathSelectionSource.Current,
                null));
        }

        var hasCurrentPull = pulls.Count > 0;
        var visibleRecordedPulls = GetVisibleRecordedPulls().ToList();
        for (var i = 0; i < visibleRecordedPulls.Count; i++)
        {
            var summary = visibleRecordedPulls[i].Summary;
            if (summary.DeathCount == 0 && !PendingDeathSelectionMatchesRecordedPull(summary))
            {
                continue;
            }

            var key = BuildRecordedPullKey(summary);
            var shouldLoadDetails =
                string.Equals(recapReviewSelection.PullKey, key, StringComparison.Ordinal) ||
                (!hasCurrentPull && recapReviewSelection.PullKey is null && i == 0) ||
                PendingDeathSelectionMatchesRecordedPull(summary);
            var detail = shouldLoadDetails
                ? plugin.GetRecordedPullDetails(summary)
                : plugin.GetLoadedRecordedPullDetails(summary);
            var deaths = detail?.Deaths ?? [];
            pulls.Add(new ReviewPull(
                key,
                $"Pull {summary.PullNumber}",
                FormatRecordedPullCapturedTime(summary),
                summary.PullNumber,
                summary.TerritoryId,
                summary.TerritoryName,
                summary.PullElapsedSeconds,
                detail?.Deaths.Count ?? summary.DeathCount,
                summary.PullGroupId,
                summary.PullGroupColorIndex,
                deaths,
                detail?.ReplayMarkers ?? [],
                DeathSelectionSource.Recorded,
                summary));
        }

        return pulls;
    }

    private static string BuildRecordedPullKey(RecordedPullSummary summary)
    {
        return $"Recorded:{summary.PullNumber}:{summary.CapturedAtUtc.Ticks}";
    }

    private string FormatRecordedPullCapturedTime(RecordedPullSummary summary)
    {
        return FormatLocalClockTime(summary.CapturedAtUtc);
    }

    private bool PendingDeathSelectionMatchesRecordedPull(RecordedPullSummary summary)
    {
        return pendingDeathSelection is { } target &&
            DeathSelectionSourceMatches(target, DeathSelectionSource.Recorded, summary);
    }

    private void DrawReviewWorkspace(
        IReadOnlyList<ReviewPull> pulls,
        string idPrefix,
        bool showPullBrowser,
        ReviewSelectionState selection)
    {
        ApplyPendingSelectionToReviewWorkspace(pulls, selection);
        EnsureReviewSelection(pulls, selection);

        if (pulls.Count == 0)
        {
            using var emptyStateIndent = new ImGuiIndentScope(ReviewPaneContentIndent);
            ImGui.TextDisabled("No pull data is available yet.");
            DrawReviewPaneBottomPadding();
            return;
        }

        var selectedPull = GetSelectedReviewPull(pulls, selection.PullKey) ?? pulls[0];
        var selectedDeath = GetSelectedReviewDeath(selectedPull, selection);
        var available = ImGui.GetContentRegionAvail();
        var wideLayout = available.X >= (showPullBrowser ? 1120.0f : 860.0f);

        if (!wideLayout)
        {
            DrawStackedReviewWorkspace(
                pulls,
                selectedPull,
                selectedDeath,
                idPrefix,
                showPullBrowser,
                selection);
            return;
        }

        DrawReviewPanel(
            $"##{idPrefix}UnifiedReview",
            available,
            () => DrawWideUnifiedReviewWorkspace(
                pulls,
                selectedPull,
                selectedDeath,
                idPrefix,
                showPullBrowser,
                selection),
            indentContent: false);
    }

    private void DrawWideUnifiedReviewWorkspace(
        IReadOnlyList<ReviewPull> pulls,
        ReviewPull selectedPull,
        PartyDeathRecord? selectedDeath,
        string idPrefix,
        bool showPullBrowser,
        ReviewSelectionState selection)
    {
        var available = ImGui.GetContentRegionAvail();
        var pullBrowserCollapsed = showPullBrowser && configuration.PullBrowserCollapsed;
        var pullBrowserWidth = 0.0f;
        var pullBrowserControlWidth = 0.0f;
        if (showPullBrowser)
        {
            if (pullBrowserCollapsed)
            {
                pullBrowserControlWidth = PullBrowserCollapsedWidth;
            }
            else
            {
                pullBrowserWidth = PullBrowserExpandedWidth;
                pullBrowserControlWidth = pullBrowserWidth;
            }
        }

        var pullBrowserDividerWidth = showPullBrowser && !pullBrowserCollapsed
            ? ReviewPaneDividerWidth
            : 0.0f;
        var reviewWidth = available.X - pullBrowserControlWidth - pullBrowserDividerWidth - ReviewPaneSplitterWidth;
        if (reviewWidth < MinimumTimelinePaneWidth + MinimumDeathDetailsPaneWidth)
        {
            DrawStackedReviewWorkspace(
                pulls,
                selectedPull,
                selectedDeath,
                idPrefix,
                showPullBrowser,
                selection);
            return;
        }

        var defaultRightWidth = Math.Clamp(available.X * 0.34f, MinimumDeathDetailsPaneWidth, 640.0f);
        var defaultTimelineWidth = pullBrowserCollapsed
            ? reviewWidth * 0.5f
            : reviewWidth - defaultRightWidth;
        var maxTimelineWidth = MathF.Max(MinimumTimelinePaneWidth, reviewWidth - MinimumDeathDetailsPaneWidth);
        var configuredTimelineWidth = IsUsableReviewTimelineWidth(configuration.ReviewTimelineWidth)
            ? configuration.ReviewTimelineWidth
            : defaultTimelineWidth;
        var centerWidth = Math.Clamp(configuredTimelineWidth, MinimumTimelinePaneWidth, maxTimelineWidth);
        var rightWidth = MathF.Max(MinimumDeathDetailsPaneWidth, reviewWidth - centerWidth);

        if (rightWidth + centerWidth > reviewWidth)
        {
            rightWidth = reviewWidth - centerWidth;
        }

        if (centerWidth < MinimumTimelinePaneWidth || rightWidth < MinimumDeathDetailsPaneWidth)
        {
            DrawStackedReviewWorkspace(
                pulls,
                selectedPull,
                selectedDeath,
                idPrefix,
                showPullBrowser,
                selection);
            return;
        }

        if (showPullBrowser)
        {
            if (pullBrowserCollapsed)
            {
                DrawCollapsedPullBrowserDivider(
                    $"{idPrefix}PullBrowserDivider",
                    available.Y,
                    pulls,
                    selection);
            }
            else
            {
                DrawReviewPane(
                    $"##{idPrefix}PullBrowser",
                    new Vector2(pullBrowserWidth, available.Y),
                    () => DrawPullBrowser(
                        pulls,
                        idPrefix,
                        selection));
                DrawVerticalReviewDivider($"{idPrefix}PullBrowserDivider", available.Y);
            }
        }

        DrawReviewPane(
            $"##{idPrefix}Timeline",
            new Vector2(centerWidth, available.Y),
            () => DrawSelectedPullTimeline(
                selectedPull,
                idPrefix,
                selection,
                allowLeadUpScrollHandoff: true));
        DrawResizableTimelineDivider($"{idPrefix}TimelineDivider", available.Y, reviewWidth, centerWidth);
        DrawReviewPane(
            $"##{idPrefix}DeathDetails",
            new Vector2(rightWidth, available.Y),
            () => DrawSelectedDeathPanel(selectedPull, selectedDeath, idPrefix));
    }

    private void DrawStackedReviewWorkspace(
        IReadOnlyList<ReviewPull> pulls,
        ReviewPull selectedPull,
        PartyDeathRecord? selectedDeath,
        string idPrefix,
        bool showPullBrowser,
        ReviewSelectionState selection)
    {
        var available = ImGui.GetContentRegionAvail();
        DrawReviewPanel(
            $"##{idPrefix}UnifiedReviewStacked",
            available,
            () =>
            {
                var innerAvailable = ImGui.GetContentRegionAvail();
                var stackedLayout = GetStackedReviewLayout(innerAvailable.Y, showPullBrowser, configuration.PullBrowserCollapsed);
                if (showPullBrowser)
                {
                    if (configuration.PullBrowserCollapsed)
                    {
                        DrawReviewPane(
                            $"##{idPrefix}PullBrowserStackedCollapsed",
                            new Vector2(0.0f, stackedLayout.PullBrowserHeight),
                            () => DrawCollapsedPullBrowser(idPrefix, pulls, selection));
                    }
                    else
                    {
                        DrawReviewPane(
                            $"##{idPrefix}PullBrowserStacked",
                            new Vector2(0.0f, stackedLayout.PullBrowserHeight),
                            () => DrawPullBrowser(
                                pulls,
                                idPrefix,
                                selection,
                                useVerticalDrawerControls: true,
                                usePullCells: true));
                    }

                    DrawResizableStackedReviewDivider(
                        $"{idPrefix}PullBrowserStackedResize",
                        innerAvailable.X,
                        configuration.PullBrowserCollapsed
                            ? StackedReviewResizeTarget.CollapsedPullBrowser
                            : StackedReviewResizeTarget.PullBrowser,
                        stackedLayout.PullBrowserHeight,
                        stackedLayout.PullBrowserMinHeight,
                        stackedLayout.PullBrowserMaxHeight,
                        "Drag to resize Pulls.");
                }

                DrawReviewPane(
                    $"##{idPrefix}TimelineStacked",
                    new Vector2(0.0f, stackedLayout.TimelineHeight),
                    () => DrawSelectedPullTimeline(
                        selectedPull,
                        idPrefix,
                        selection,
                        allowLeadUpScrollHandoff: true));
                DrawResizableStackedReviewDivider(
                    $"{idPrefix}TimelineStackedResize",
                    innerAvailable.X,
                    StackedReviewResizeTarget.Timeline,
                    stackedLayout.TimelineHeight,
                    stackedLayout.TimelineMinHeight,
                    stackedLayout.TimelineMaxHeight,
                    "Drag to resize the death timeline.");
                DrawReviewPane(
                    $"##{idPrefix}DeathDetailsStacked",
                    new Vector2(0.0f, stackedLayout.DeathDetailsHeight),
                    () => DrawSelectedDeathPanel(selectedPull, selectedDeath, idPrefix));
            },
            indentContent: false);
    }

    private StackedReviewLayout GetStackedReviewLayout(float availableHeight, bool showPullBrowser, bool pullBrowserCollapsed)
    {
        var dividerCount = showPullBrowser ? 2 : 1;
        var availableForPanes = MathF.Max(1.0f, availableHeight - (dividerCount * StackedReviewSplitterHeight));
        var pullTarget = pullBrowserCollapsed
            ? StackedReviewResizeTarget.CollapsedPullBrowser
            : StackedReviewResizeTarget.PullBrowser;
        var pullMinHeight = showPullBrowser
            ? pullBrowserCollapsed ? StackedCollapsedPullBrowserMinHeight : StackedExpandedPullBrowserMinHeight
            : 0.0f;
        var pullMaxHeight = showPullBrowser
            ? MathF.Min(
                pullBrowserCollapsed ? StackedCollapsedPullBrowserMaxHeight : StackedExpandedPullBrowserMaxHeight,
                MathF.Max(pullMinHeight, availableForPanes - StackedTimelineMinHeight - StackedDeathDetailsMinHeight))
            : 0.0f;
        var defaultPullHeight = pullBrowserCollapsed
            ? StackedCollapsedPullBrowserHeight
            : MathF.Min(260.0f, MathF.Max(170.0f, availableHeight * 0.28f));
        var pullHeight = showPullBrowser
            ? GetStackedReviewPaneHeight(pullTarget, defaultPullHeight, pullMinHeight, pullMaxHeight)
            : 0.0f;

        var timelineMinHeight = StackedTimelineMinHeight;
        var timelineMaxHeight = MathF.Min(
            StackedTimelineMaxHeight,
            MathF.Max(timelineMinHeight, availableForPanes - pullHeight - StackedDeathDetailsMinHeight));
        var defaultTimelineHeight = MathF.Min(330.0f, MathF.Max(210.0f, availableHeight * 0.35f));
        var timelineHeight = GetStackedReviewPaneHeight(
            StackedReviewResizeTarget.Timeline,
            defaultTimelineHeight,
            timelineMinHeight,
            timelineMaxHeight);
        var deathDetailsHeight = MathF.Max(
            StackedDeathDetailsMinHeight,
            availableForPanes - pullHeight - timelineHeight);

        return new StackedReviewLayout(
            pullHeight,
            pullMinHeight,
            pullMaxHeight,
            timelineHeight,
            timelineMinHeight,
            timelineMaxHeight,
            deathDetailsHeight);
    }

    private float GetStackedReviewPaneHeight(
        StackedReviewResizeTarget target,
        float defaultHeight,
        float minHeight,
        float maxHeight)
    {
        var configuredHeight = GetConfiguredStackedReviewPaneHeight(target);
        var height = IsUsableStackedReviewPaneHeight(configuredHeight)
            ? configuredHeight
            : defaultHeight;
        return ClampStackedReviewPaneHeight(height, minHeight, maxHeight);
    }

    private float GetConfiguredStackedReviewPaneHeight(StackedReviewResizeTarget target)
    {
        return target switch
        {
            StackedReviewResizeTarget.PullBrowser => configuration.StackedPullBrowserHeight,
            StackedReviewResizeTarget.CollapsedPullBrowser => configuration.StackedCollapsedPullBrowserHeight,
            StackedReviewResizeTarget.Timeline => configuration.StackedTimelineHeight,
            _ => 0.0f,
        };
    }

    private void SetConfiguredStackedReviewPaneHeight(StackedReviewResizeTarget target, float height)
    {
        switch (target)
        {
            case StackedReviewResizeTarget.PullBrowser:
                configuration.StackedPullBrowserHeight = height;
                break;
            case StackedReviewResizeTarget.CollapsedPullBrowser:
                configuration.StackedCollapsedPullBrowserHeight = height;
                break;
            case StackedReviewResizeTarget.Timeline:
                configuration.StackedTimelineHeight = height;
                break;
        }
    }

    private static bool IsUsableStackedReviewPaneHeight(float height)
    {
        return !float.IsNaN(height) && !float.IsInfinity(height) && height > 0.0f;
    }

    private static float ClampStackedReviewPaneHeight(float height, float minHeight, float maxHeight)
    {
        var safeMaxHeight = MathF.Max(minHeight, maxHeight);
        return Math.Clamp(height, minHeight, safeMaxHeight);
    }

    private void DrawReviewPanel(string id, Vector2 size, Action draw, bool indentContent = true)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, ModernPanelAltColor);
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, GetTableRowAltColor());
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, indentContent ? new Vector2(ReviewPaneContentIndent, 6.0f) : Vector2.Zero);
        if (ImGui.BeginChild(id, size, false, OptionalScrollbarFlags))
        {
            if (indentContent)
            {
                using var panelIndent = new ImGuiIndentScope(ReviewPaneContentIndent);
                draw();
            }
            else
            {
                draw();
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    private void DrawReviewPane(string id, Vector2 size, Action draw)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(ReviewPaneHorizontalPadding, 6.0f));
        if (ImGui.BeginChild(id, size, false, OptionalScrollbarFlags))
        {
            draw();
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawReplayPane(string id, Vector2 size, Action draw)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(ReplayPaneHorizontalPadding, ReplayPaneVerticalPadding));
        if (ImGui.BeginChild(id, size, false, OptionalScrollbarFlags))
        {
            draw();
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private static void DrawVerticalReviewDivider(string id, float height)
    {
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ModernDividerColor);
        ImGui.BeginChild($"##{id}", new Vector2(1.0f, height), false, ImGuiWindowFlags.NoScrollbar);
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.SameLine(0.0f, 0.0f);
    }

    private static void DrawVerticalReplayDivider(string id, float height)
    {
        ImGui.SameLine(0.0f, ReplayPaneGap);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ModernDividerColor);
        ImGui.BeginChild($"##{id}", new Vector2(1.0f, height), false, ImGuiWindowFlags.NoScrollbar);
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.SameLine(0.0f, ReplayPaneGap);
    }

    private void DrawResizableTimelineDivider(string id, float height, float reviewWidth, float currentTimelineWidth)
    {
        ImGui.SameLine(0.0f, 0.0f);
        var position = ImGui.GetCursorScreenPos();
        var size = new Vector2(ReviewPaneSplitterWidth, height);
        var drawList = ImGui.GetWindowDrawList();
        var lineX = position.X + MathF.Floor(ReviewPaneSplitterWidth * 0.5f);
        drawList.AddLine(
            new Vector2(lineX, position.Y),
            new Vector2(lineX, position.Y + MathF.Max(0.0f, height)),
            ImGui.GetColorU32(ModernDividerColor),
            1.0f);

        ImGui.InvisibleButton($"##{id}", size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        if (hovered || active)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        }

        if (active)
        {
            reviewTimelineSplitterDragging = true;
            var deltaX = ImGui.GetIO().MouseDelta.X;
            if (MathF.Abs(deltaX) > 0.0f)
            {
                var currentWidth = IsUsableReviewTimelineWidth(configuration.ReviewTimelineWidth)
                    ? configuration.ReviewTimelineWidth
                    : currentTimelineWidth;
                var newWidth = Math.Clamp(
                    currentWidth + deltaX,
                    MinimumTimelinePaneWidth,
                    reviewWidth - MinimumDeathDetailsPaneWidth);
                if (MathF.Abs(configuration.ReviewTimelineWidth - newWidth) > 0.1f)
                {
                    configuration.ReviewTimelineWidth = newWidth;
                }
            }
        }
        else if (reviewTimelineSplitterDragging)
        {
            reviewTimelineSplitterDragging = false;
            plugin.SaveConfiguration();
        }

        if (hovered)
        {
            SetThemedTooltip("Drag to resize the death timeline.");
        }

        ImGui.SameLine(0.0f, 0.0f);
    }

    private static bool IsUsableReviewTimelineWidth(float width)
    {
        return !float.IsNaN(width) && !float.IsInfinity(width) && width > 0.0f;
    }

    private void DrawResizableStackedReviewDivider(
        string id,
        float width,
        StackedReviewResizeTarget target,
        float currentHeight,
        float minHeight,
        float maxHeight,
        string tooltip)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var safeWidth = MathF.Max(1.0f, width);
        var safeMaxHeight = MathF.Max(minHeight, maxHeight);
        var drawList = ImGui.GetWindowDrawList();
        var y = cursor.Y + MathF.Floor(StackedReviewSplitterHeight * 0.5f);
        drawList.AddLine(
            new Vector2(cursor.X, y),
            new Vector2(cursor.X + safeWidth, y),
            ImGui.GetColorU32(ModernDividerColor),
            1.0f);

        ImGui.InvisibleButton($"##{id}", new Vector2(safeWidth, StackedReviewSplitterHeight));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        if (hovered || active)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
        }

        if (active)
        {
            stackedReviewSplitterDraggingId = id;
            var deltaY = ImGui.GetIO().MouseDelta.Y;
            if (MathF.Abs(deltaY) > 0.0f)
            {
                var configuredHeight = GetConfiguredStackedReviewPaneHeight(target);
                var baseHeight = IsUsableStackedReviewPaneHeight(configuredHeight)
                    ? configuredHeight
                    : currentHeight;
                var newHeight = Math.Clamp(baseHeight + deltaY, minHeight, safeMaxHeight);
                if (MathF.Abs(configuredHeight - newHeight) > 0.1f)
                {
                    SetConfiguredStackedReviewPaneHeight(target, newHeight);
                }
            }
        }
        else if (string.Equals(stackedReviewSplitterDraggingId, id, StringComparison.Ordinal))
        {
            stackedReviewSplitterDraggingId = null;
            plugin.SaveConfiguration();
        }

        if (hovered)
        {
            SetThemedTooltip(tooltip);
        }
    }

    private void DrawPullBrowser(
        IReadOnlyList<ReviewPull> pulls,
        string idPrefix,
        ReviewSelectionState selection,
        bool useVerticalDrawerControls = false,
        bool usePullCells = false)
    {
        using var paneIndent = new ImGuiIndentScope(ReviewPaneContentIndent);
        DrawPullBrowserHeader(idPrefix, useVerticalDrawerControls);
        DrawRecordedPullControls();
        ImGui.Separator();

        if (pulls.Count == 0)
        {
            ImGui.TextDisabled("No pull data available.");
            DrawReviewPaneBottomPadding();
            return;
        }

        var visiblePulls = GetPullBrowserVisiblePulls(pulls);
        if (visiblePulls.Count == 0)
        {
            ImGui.TextDisabled("No pulls match that player name.");
            DrawReviewPaneBottomPadding();
            return;
        }

        if (ImGui.BeginChild($"##{idPrefix}PullRows", Vector2.Zero, false, OptionalScrollbarFlags))
        {
            foreach (var pull in visiblePulls)
            {
                var selected = string.Equals(selection.PullKey, pull.Key, StringComparison.Ordinal);
                var rowId = $"PullRow{idPrefix}{pull.Key}";
                var clicked = usePullCells
                    ? DrawExpandedPullCell(pull, rowId, selected, StackedPullCellRightPadding)
                    : DrawWidePullListItem(pull, rowId, selected);
                if (clicked)
                {
                    selection.PullKey = pull.Key;
                    ClearSelectedDeath(selection);
                }

                if (usePullCells && ImGui.IsItemHovered())
                {
                    SetThemedTooltip(FormatExpandedPullTooltip(pull));
                }

                if (usePullCells)
                {
                    ImGui.Dummy(new Vector2(1.0f, 6.0f));
                }
            }

            DrawReviewPaneBottomPadding();
        }

        ImGui.EndChild();
    }

    private void DrawPullBrowserHeader(string idPrefix, bool useVerticalDrawerControls)
    {
        var startCursor = ImGui.GetCursorPos();
        ImGui.TextColored(LeadUpGoldColor, "Pulls");

        var style = ImGui.GetStyle();
        var iconButtonWidth = ImGui.GetFrameHeight();
        var buttonWidth = (iconButtonWidth * 2.0f) + style.ItemSpacing.X;
        var buttonX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - buttonWidth - PullBrowserHeaderButtonInset;

        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX() + style.ItemSpacing.X, buttonX));
        ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
        DrawClearRecordedPullsButton($"ClearRecordedPullsModern{idPrefix}", clearSelection: true);
        ImGui.SameLine(0.0f, style.ItemSpacing.X);
        var collapseIcon = useVerticalDrawerControls
            ? FontAwesomeIcon.ChevronUp
            : FontAwesomeIcon.ChevronLeft;
        if (DrawTransparentIconButton($"CollapsePullBrowser{idPrefix}", collapseIcon))
        {
            plugin.SetPullBrowserCollapsed(true);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Collapse pulls");
        }

        ImGui.PopStyleColor();

        ImGui.SetCursorPos(new Vector2(startCursor.X, startCursor.Y + ImGui.GetTextLineHeightWithSpacing()));
        DrawPullBrowserSearchInput(idPrefix);
    }

    private void DrawPullBrowserSearchInput(string idPrefix)
    {
        ImGui.SetNextItemWidth(MathF.Max(1.0f, ImGui.GetContentRegionAvail().X));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ModernFrameColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ModernFrameHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ModernAccentSoftColor);
        ImGui.PushStyleColor(ImGuiCol.Text, ModernTextColor);
        ImGui.InputTextWithHint(
            $"##PullBrowserPlayerSearch{idPrefix}",
            "Search Player name",
            ref pullBrowserPlayerSearch,
            64);
        ImGui.PopStyleColor(4);

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Filters pulls by captured death player name.");
        }
    }

    private IReadOnlyList<ReviewPull> GetPullBrowserVisiblePulls(IReadOnlyList<ReviewPull> pulls)
    {
        var search = NormalizePullBrowserPlayerSearch(pullBrowserPlayerSearch);
        if (search.Length == 0)
        {
            return pulls;
        }

        var matches = new List<ReviewPull>();
        foreach (var pull in pulls)
        {
            if (TryGetPullBrowserSearchMatch(pull, search) is { } match)
            {
                matches.Add(match);
            }
        }

        return matches;
    }

    private ReviewPull? TryGetPullBrowserSearchMatch(ReviewPull pull, string search)
    {
        var deaths = pull.Deaths;
        var hydratedDetails = false;
        if (deaths.Count == 0 &&
            pull.RecordedPull is not null &&
            pull.DeathCount > 0 &&
            plugin.GetRecordedPullDetails(pull.RecordedPull) is { } detail)
        {
            deaths = detail.Deaths;
            hydratedDetails = true;
        }

        if (!deaths.Any(death => PullDeathMatchesPlayerSearch(death, search)))
        {
            return null;
        }

        return hydratedDetails
            ? pull with
            {
                Deaths = deaths,
                DeathCount = deaths.Count,
            }
            : pull;
    }

    private static bool PullDeathMatchesPlayerSearch(PartyDeathRecord death, string search)
    {
        return !string.IsNullOrWhiteSpace(death.MemberName) &&
            death.MemberName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePullBrowserPlayerSearch(string search)
    {
        return string.Join(' ', search.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private bool DrawWidePullListItem(ReviewPull pull, string id, bool selected)
    {
        var clicked = ImGui.Selectable($"{GetPullCellTitle(pull)}###{id}", selected);
        DrawPullGroupRightDashForLastItem(pull);
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(FormatExpandedPullTooltip(pull));
        }

        ImGui.TextDisabled(FormatPullDutyInfo(pull));
        if (pull.Source == DeathSelectionSource.Recorded &&
            !string.IsNullOrWhiteSpace(pull.Subtitle))
        {
            ImGui.TextDisabled(pull.Subtitle);
        }

        ImGui.Spacing();
        return clicked;
    }

    private Vector4? GetPullGroupColor(ReviewPull pull)
    {
        if (string.IsNullOrWhiteSpace(pull.PullGroupId) || pull.PullGroupColorIndex < 0)
        {
            return null;
        }

        return GetPullGroupColor(pull.PullGroupColorIndex);
    }

    private Vector4 GetPullGroupColor(int colorIndex)
    {
        var normalizedIndex = Mod(colorIndex, Plugin.PullGroupColorPaletteSize);
        if (configuration.UseCustomPullGroupColors &&
            configuration.PullGroupColors is { Count: > 0 } customColors &&
            normalizedIndex < customColors.Count &&
            customColors[normalizedIndex] is { } customColor)
        {
            return ToVector4(customColor) with { W = Math.Clamp(customColor.A <= 0.0f ? 0.86f : customColor.A, 0.25f, 1.0f) };
        }

        return GetDefaultPullGroupColor(normalizedIndex);
    }

    private static Vector4 GetDefaultPullGroupColor(int colorIndex)
    {
        var baseColor = BasePullGroupPalette[Mod(colorIndex, BasePullGroupPalette.Length)];
        var color = ActiveThemeUsesLightPanels()
            ? BlendColors(baseColor, ModernTextColor, 0.20f)
            : BlendColors(baseColor, ModernAccentColor, 0.12f);
        return color with { W = ActiveThemeUsesLightPanels() ? 0.82f : 0.88f };
    }

    private void DrawPullGroupRightDashForLastItem(ReviewPull pull)
    {
        if (GetPullGroupColor(pull) is not { } color)
        {
            return;
        }

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var centerY = (min.Y + max.Y) * 0.5f;
        var dashWidth = MathF.Min(26.0f, MathF.Max(12.0f, (max.X - min.X) * 0.18f));
        var dashMin = new Vector2(max.X - dashWidth - 4.0f, centerY - (PullGroupIndicatorThickness * 0.5f));
        var dashMax = new Vector2(max.X - 4.0f, centerY + (PullGroupIndicatorThickness * 0.5f));
        ImGui.GetWindowDrawList().AddRectFilled(dashMin, dashMax, ImGui.GetColorU32(color), 2.0f);
    }

    private static void DrawPullGroupTopDash(ImDrawListPtr drawList, Vector2 start, Vector2 end, Vector4 color)
    {
        var width = MathF.Min(PullGroupTopDashWidth, MathF.Max(8.0f, (end.X - start.X) - 18.0f));
        var dashMin = new Vector2(start.X + MathF.Max(0.0f, ((end.X - start.X) - width) * 0.5f), start.Y + 5.0f);
        var dashMax = new Vector2(dashMin.X + width, dashMin.Y + PullGroupIndicatorThickness);
        drawList.AddRectFilled(dashMin, dashMax, ImGui.GetColorU32(color), 2.0f);
    }

    private static int Mod(int value, int modulus)
    {
        if (modulus <= 0)
        {
            return 0;
        }

        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private bool DrawExpandedPullCell(ReviewPull pull, string id, bool selected, float rightPadding = 0.0f)
    {
        var start = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1.0f, ImGui.GetContentRegionAvail().X - MathF.Max(0.0f, rightPadding));
        var size = new Vector2(width, PullCellHeight);
        var clicked = ImGui.InvisibleButton($"##{id}", size);
        var hovered = ImGui.IsItemHovered();
        var end = start + size;
        var drawList = ImGui.GetWindowDrawList();
        var groupColor = GetPullGroupColor(pull);
        var fill = selected
            ? BlendColors(ModernAccentSoftColor, ModernPanelAltColor, 0.34f)
            : hovered
                ? BlendColors(ModernPanelAltColor, ModernButtonHoveredColor, 0.32f)
                : ModernPanelAltColor with { W = 0.78f };
        var border = selected
            ? ModernAccentColor with { W = 0.95f }
            : ModernPanelBorderColor with { W = hovered ? 0.82f : 0.50f };
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(fill), 6.0f);
        drawList.AddRect(start, end, ImGui.GetColorU32(border), 6.0f, ImDrawFlags.None, selected ? 1.4f : 1.0f);
        if (selected)
        {
            drawList.AddRectFilled(
                start + new Vector2(0.0f, 5.0f),
                start + new Vector2(3.0f, size.Y - 5.0f),
                ImGui.GetColorU32(LeadUpGoldColor),
                3.0f);
        }

        if (groupColor is { } color)
        {
            DrawPullGroupTopDash(drawList, start, end, color);
        }

        var textStart = start + new Vector2(12.0f, groupColor is null ? 8.0f : 15.0f);
        var clipMin = start + new Vector2(8.0f, 4.0f);
        var clipMax = end - new Vector2(8.0f, 4.0f);
        ImGui.PushClipRect(clipMin, clipMax, true);
        drawList.AddText(
            textStart,
            ImGui.GetColorU32(selected ? LeadUpGoldColor : ModernTextColor),
            GetPullCellTitle(pull));
        drawList.AddText(
            textStart + new Vector2(0.0f, ImGui.GetTextLineHeightWithSpacing()),
            ImGui.GetColorU32(ModernMutedTextColor),
            FormatPullCellSubtitle(pull));
        ImGui.PopClipRect();
        return clicked;
    }

    private void DrawStackedCollapsedPullCells(
        string idPrefix,
        IReadOnlyList<ReviewPull> pulls,
        ReviewSelectionState selection)
    {
        if (pulls.Count == 0)
        {
            return;
        }

        ImGui.Dummy(new Vector2(1.0f, 4.0f));
        var rowsHeight = MathF.Max(0.0f, ImGui.GetContentRegionAvail().Y);
        if (rowsHeight <= 0.0f)
        {
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (!ImGui.BeginChild($"##StackedCollapsedPullCells{idPrefix}", new Vector2(0.0f, rowsHeight), false, OptionalScrollbarFlags))
        {
            ImGui.EndChild();
            ImGui.PopStyleVar();
            return;
        }

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var rowWidth = 0.0f;
        foreach (var pull in pulls)
        {
            var selected = string.Equals(selection.PullKey, pull.Key, StringComparison.Ordinal);
            var label = GetCollapsedPullLabel(pull);
            var width = Math.Clamp(ImGui.CalcTextSize(label).X + 26.0f, 44.0f, 72.0f);
            var nextWidth = rowWidth <= 0.0f
                ? width
                : rowWidth + spacing + width;
            if (rowWidth > 0.0f && nextWidth > availableWidth)
            {
                rowWidth = 0.0f;
            }
            else if (rowWidth > 0.0f)
            {
                ImGui.SameLine(0.0f, spacing);
            }

            if (DrawCollapsedPullButton(label, $"StackedCollapsedPull{idPrefix}{pull.Key}", selected, width, GetPullGroupColor(pull)))
            {
                selection.PullKey = pull.Key;
                ClearSelectedDeath(selection);
            }

            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip(FormatCollapsedPullTooltip(pull));
            }

            rowWidth = rowWidth <= 0.0f
                ? width
                : rowWidth + spacing + width;
        }

        DrawReviewPaneBottomPadding();

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawCollapsedPullBrowser(
        string idPrefix,
        IReadOnlyList<ReviewPull> pulls,
        ReviewSelectionState selection)
    {
        var startCursor = ImGui.GetCursorPos();
        ImGui.TextColored(LeadUpGoldColor, "Pulls");
        var iconText = FontAwesomeIcon.ChevronDown.ToIconString();
        var buttonWidth = ImGui.CalcTextSize(iconText).X + (ImGui.GetStyle().FramePadding.X * 2.0f);
        var buttonX = startCursor.X + MathF.Max(0.0f, ImGui.GetContentRegionAvail().X - buttonWidth - PullBrowserHeaderButtonInset);
        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX() + ImGui.GetStyle().ItemSpacing.X, buttonX));
        ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
        if (DrawTransparentIconButton($"ExpandPullBrowser{idPrefix}", FontAwesomeIcon.ChevronDown))
        {
            plugin.SetPullBrowserCollapsed(false);
        }

        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Expand pulls");
        }

        ImGui.SetCursorPos(new Vector2(startCursor.X, startCursor.Y + ImGui.GetTextLineHeightWithSpacing()));
        DrawStackedCollapsedPullCells(idPrefix, pulls, selection);
    }

    private void DrawCollapsedPullBrowserDivider(
        string id,
        float height,
        IReadOnlyList<ReviewPull> pulls,
        ReviewSelectionState selection)
    {
        var position = ImGui.GetCursorScreenPos();
        var size = new Vector2(PullBrowserCollapsedWidth, height);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.BeginChild($"##{id}", size, false, OptionalScrollbarFlags))
        {
            ImGui.SetCursorPosY(4.0f);

            ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
            if (DrawCenteredTransparentIconButton($"ExpandPullBrowser{id}", FontAwesomeIcon.ChevronRight))
            {
                plugin.SetPullBrowserCollapsed(false);
            }

            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip("Expand pulls");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var rowsHeight = MathF.Max(0.0f, ImGui.GetContentRegionAvail().Y);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            if (ImGui.BeginChild($"##{id}Rows", new Vector2(0.0f, rowsHeight), false, OptionalScrollbarFlags))
            {
                foreach (var pull in pulls)
                {
                    var selected = string.Equals(selection.PullKey, pull.Key, StringComparison.Ordinal);
                    var pullLabel = GetCollapsedPullLabel(pull);
                    if (DrawCollapsedPullRailItem(pullLabel, $"CollapsedPullRail{id}{pull.Key}", selected, GetPullGroupColor(pull)))
                    {
                        selection.PullKey = pull.Key;
                        ClearSelectedDeath(selection);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        SetThemedTooltip(FormatCollapsedPullTooltip(pull));
                    }
                }

                DrawReviewPaneBottomPadding();
            }

            ImGui.EndChild();
            ImGui.PopStyleVar();
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        var lineX = position.X + size.X - 1.0f;
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(lineX, position.Y),
            new Vector2(lineX, position.Y + size.Y),
            ImGui.GetColorU32(ModernDividerColor),
            1.0f);
        ImGui.SameLine(0.0f, 0.0f);
    }

    private bool DrawCollapsedPullButton(
        string label,
        string id,
        bool selected,
        float widthOverride = 0.0f,
        Vector4? groupColor = null)
    {
        var start = ImGui.GetCursorScreenPos();
        var width = widthOverride > 0.0f
            ? MathF.Min(widthOverride, MathF.Max(1.0f, ImGui.GetContentRegionAvail().X))
            : MathF.Max(1.0f, ImGui.GetContentRegionAvail().X);
        var height = CollapsedPullCellHeight;
        var clicked = ImGui.InvisibleButton($"##{id}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var end = start + new Vector2(width, height);
        var fill = selected
            ? BlendColors(ModernAccentSoftColor, ModernPanelAltColor, 0.22f)
            : hovered
                ? BlendColors(ModernPanelAltColor, ModernButtonHoveredColor, 0.26f)
                : ModernPanelAltColor with { W = 0.70f };
        var border = selected
            ? LeadUpGoldColor
            : ModernPanelBorderColor with { W = hovered ? 0.80f : 0.46f };
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(fill), 5.0f);
        drawList.AddRect(start, end, ImGui.GetColorU32(border), 5.0f, ImDrawFlags.None, selected ? 1.3f : 1.0f);
        if (groupColor is { } color)
        {
            DrawPullGroupTopDash(drawList, start, end, color);
        }

        var textSize = ImGui.CalcTextSize(label);
        var textPosition = new Vector2(
            start.X + MathF.Max(0.0f, (width - textSize.X) * 0.5f),
            groupColor is null
                ? start.Y + MathF.Max(0.0f, (height - textSize.Y) * 0.5f)
                : start.Y + MathF.Max(0.0f, height - textSize.Y - 3.0f));
        drawList.AddText(
            textPosition,
            selected ? ImGui.GetColorU32(LeadUpGoldColor) : ImGui.GetColorU32(ModernTextColor),
            label);

        return clicked;
    }

    private static bool DrawCollapsedPullRailItem(string label, string id, bool selected, Vector4? groupColor)
    {
        var start = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1.0f, ImGui.GetContentRegionAvail().X);
        var height = CollapsedPullCellHeight;
        var clicked = ImGui.InvisibleButton($"##{id}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var end = start + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        var accent = selected
            ? LeadUpGoldColor
            : hovered
                ? ModernMutedTextColor
                : ModernDividerColor;
        if (selected || hovered)
        {
            var lineX = start.X + 5.0f;
            drawList.AddLine(
                new Vector2(lineX, start.Y + 5.0f),
                new Vector2(lineX, end.Y - 5.0f),
                ImGui.GetColorU32(accent),
                selected ? 2.0f : 1.0f);
        }

        if (groupColor is { } color)
        {
            var stripMin = new Vector2(end.X - PullGroupIndicatorThickness - 5.0f, start.Y + 5.0f);
            var stripMax = new Vector2(end.X - 5.0f, end.Y - 5.0f);
            drawList.AddRectFilled(stripMin, stripMax, ImGui.GetColorU32(color), 2.0f);
        }

        var textSize = ImGui.CalcTextSize(label);
        var textPosition = new Vector2(
            start.X + MathF.Max(0.0f, (width - textSize.X) * 0.5f),
            start.Y + MathF.Max(0.0f, (height - textSize.Y) * 0.5f));
        drawList.AddText(
            textPosition,
            selected
                ? ImGui.GetColorU32(LeadUpGoldColor)
                : ImGui.GetColorU32(hovered ? ModernTextColor : ModernMutedTextColor),
            label);

        return clicked;
    }

    private static string GetCollapsedPullLabel(ReviewPull pull)
    {
        if (pull.PullNumber is { } pullNumber && pullNumber > 0)
        {
            return pullNumber.ToString(CultureInfo.InvariantCulture);
        }

        return pull.Source == DeathSelectionSource.Current ? "Now" : pull.Title;
    }

    private static string GetPullCellTitle(ReviewPull pull)
    {
        if (pull.PullNumber is { } pullNumber && pullNumber > 0)
        {
            return $"Pull {pullNumber}";
        }

        return pull.Source == DeathSelectionSource.Current
            ? "Current pull"
            : pull.Title;
    }

    private static string FormatPullCellSubtitle(ReviewPull pull)
    {
        var dutyName = string.IsNullOrWhiteSpace(pull.TerritoryName)
            ? "Unknown duty"
            : pull.TerritoryName;
        return $"{dutyName} - {FormatCombatTimer(pull.PullElapsedSeconds)}";
    }

    private string FormatExpandedPullTooltip(ReviewPull pull)
    {
        var detail = $"{FormatPullCellSubtitle(pull)}{CompactInfoSeparator}{FormatDeathCount(pull.DeathCount)}";
        return pull.Source == DeathSelectionSource.Recorded && !string.IsNullOrWhiteSpace(pull.Subtitle)
            ? $"{detail}{CompactInfoSeparator}{pull.Subtitle}"
            : detail;
    }

    private string FormatCollapsedPullTooltip(ReviewPull pull)
    {
        return pull.Source == DeathSelectionSource.Recorded && !string.IsNullOrWhiteSpace(pull.Subtitle)
            ? $"{FormatPullDutyInfo(pull)}{CompactInfoSeparator}{pull.Subtitle}"
            : FormatPullDutyInfo(pull);
    }

    private static string FormatDeathCount(int deathCount)
    {
        return deathCount == 1 ? "1 death" : $"{deathCount} deaths";
    }

    private static string FormatPullDutyInfo(ReviewPull pull)
    {
        return $"{pull.TerritoryName} ({FormatCombatTimer(pull.PullElapsedSeconds)}){CompactInfoSeparator}{FormatDeathCount(pull.DeathCount)}";
    }

    private void DrawClearRecordedPullsButton(string id, bool clearSelection)
    {
        var hasRecordedPulls = plugin.RecordedPulls.Count > 0;
        if (!hasRecordedPulls)
        {
            ImGui.BeginDisabled();
        }

        if (DrawTransparentIconButton(id, FontAwesomeIcon.Trash) &&
            ImGui.GetIO().KeyCtrl)
        {
            plugin.ClearRecordedPulls();
            if (clearSelection)
            {
                recapReviewSelection.PullKey = null;
                recapReviewSelection.DeathSeenAtTicks = null;
                recapReviewSelection.DeathMemberKeyHash = null;
            }

            pendingDeathSelection = null;
        }

        if (!hasRecordedPulls)
        {
            ImGui.EndDisabled();
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Ctrl+click to delete stored death recaps");
        }
    }

    private void DrawSelectedPullTimeline(
        ReviewPull pull,
        string idPrefix,
        ReviewSelectionState selection,
        bool allowLeadUpScrollHandoff = false)
    {
        using var paneIndent = new ImGuiIndentScope(ReviewPaneContentIndent);
        DrawTimelineSectionTitle(GetPullDeathTimelineTitle(pull), pull.Subtitle, idPrefix);
        if (pull.Deaths.Count == 0)
        {
            ImGui.TextDisabled("No deaths recorded for this pull.");
            DrawTimelineKofiLink(idPrefix);
            DrawKofiConfirmationPopup();
            DrawReviewPaneBottomPadding();
            return;
        }

        DrawTimelineLeadUpControls($"{idPrefix}{pull.Key}");
        DrawSelectableDeathTimeline(pull, idPrefix, selection, allowLeadUpScrollHandoff);
        DrawTimelineKofiLink(idPrefix);
        DrawKofiConfirmationPopup();
        DrawReviewPaneBottomPadding();
    }

    private static string GetPullDeathTimelineTitle(ReviewPull pull)
    {
        if (pull.PullNumber is { } pullNumber && pullNumber > 0)
        {
            return $"Pull {pullNumber} Death Timeline";
        }

        return pull.Source switch
        {
            DeathSelectionSource.Current => "Current Pull Death Timeline",
            DeathSelectionSource.Example => "Example Pull Death Timeline",
            _ => $"{pull.Title} Death Timeline",
        };
    }

    private void DrawSelectableDeathTimeline(
        ReviewPull pull,
        string idPrefix,
        ReviewSelectionState selection,
        bool allowLeadUpScrollHandoff)
    {
        var itemSpacing = ImGui.GetStyle().ItemSpacing;
        var pendingLeadUpScrollHandoff = 0.0f;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(itemSpacing.X, 0.0f));
        DrawSelectableDeathTimelineHeader($"{idPrefix}{pull.Key}");
        var orderedDeaths = GetDeathsInTimelineOrder(pull.Deaths);
        for (var i = 0; i < orderedDeaths.Count; i++)
        {
            var death = orderedDeaths[i];
            var rowSelected = IsSelectedReviewDeath(death, selection);
            var causeEvents = GetTimelineCauseEvents(death);
            var causeId = $"ModernCause{idPrefix}{pull.Key}{death.MemberKey}{death.SeenAtUtc.Ticks}";
            var leadUpId = $"ModernLeadUp{idPrefix}{pull.Key}{death.MemberKey}{death.SeenAtUtc.Ticks}";
            var leadUpExpanded = IsTimelineLeadUpExpanded(leadUpId, rowSelected);
            var rowHeight = GetTimelineRowHeight(causeEvents, causeId);
            var previousPullKey = selection.PullKey;
            var previousDeathSeenAtTicks = selection.DeathSeenAtTicks;
            var previousDeathMemberKeyHash = selection.DeathMemberKeyHash;
            var rowPressed = false;
            if (!ImGui.BeginTable(
                $"##ModernDeathTimelineRow{leadUpId}",
                5,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV,
                GetRightPaddedTableSize(LeadUpTableRightPadding)))
            {
                continue;
            }

            SetupDeathTimelineColumns();
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            if (rowSelected)
            {
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(TimelineSelectedRowColor));
            }

            ImGui.TableNextColumn();
            if (DrawTimelineIndexCell(
                (i + 1).ToString(),
                $"SelectDeath{idPrefix}{pull.Key}{death.MemberKey}{death.SeenAtUtc.Ticks}",
                rowSelected,
                rowHeight,
                out var rowSelectablePressed))
            {
                rowPressed = true;
                if (rowSelected)
                {
                    ToggleSelectedTimelineLeadUp(leadUpId, ref leadUpExpanded);
                }
                else
                {
                    SelectTimelineDeathAndOpenLeadUp(death, selection);
                    leadUpExpanded = true;
                }
            }
            else if (rowSelectablePressed)
            {
                rowPressed = true;
            }

            ImGui.TableNextColumn();
            DrawCenteredText(FormatCombatTimer(death.PullElapsedSeconds));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatPlayerName(death, pull.Deaths));
            ImGui.TableNextColumn();
            DrawJobCell(death);
            ImGui.TableNextColumn();
            if (DrawTimelineCauseText(
                    death,
                    causeEvents,
                    causeId,
                    () =>
                    {
                        rowPressed = true;
                        if (rowSelected)
                        {
                            ToggleSelectedTimelineLeadUp(leadUpId, ref leadUpExpanded);
                        }
                        else
                        {
                            SelectTimelineDeathAndOpenLeadUp(death, selection);
                            leadUpExpanded = true;
                        }
                    },
                    () => rowPressed = true))
            {
                rowPressed = false;
                selection.PullKey = previousPullKey;
                selection.DeathSeenAtTicks = previousDeathSeenAtTicks;
                selection.DeathMemberKeyHash = previousDeathMemberKeyHash;
            }

            if (rowPressed)
            {
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(TimelinePressedRowColor));
            }

            ImGui.EndTable();
            if (leadUpExpanded)
            {
                DrawTimelineLeadUpDropdown(
                    death,
                    pull.TerritoryId,
                    pull.ReplayMarkers,
                    leadUpId,
                    allowLeadUpScrollHandoff,
                    ref pendingLeadUpScrollHandoff);
            }
        }

        ImGui.PopStyleVar();
        ApplyTimelineLeadUpScrollHandoff(pendingLeadUpScrollHandoff);
    }

    private bool IsTimelineLeadUpExpanded(string leadUpId, bool rowSelected)
    {
        return rowSelected && !string.Equals(collapsedSelectedTimelineLeadUpRowId, leadUpId, StringComparison.Ordinal);
    }

    private void SelectTimelineDeathAndOpenLeadUp(PartyDeathRecord death, ReviewSelectionState selection)
    {
        collapsedSelectedTimelineLeadUpRowId = null;
        SelectDeath(death, selection);
    }

    private void ToggleSelectedTimelineLeadUp(string leadUpId, ref bool leadUpExpanded)
    {
        if (leadUpExpanded)
        {
            collapsedSelectedTimelineLeadUpRowId = leadUpId;
            leadUpExpanded = false;
            return;
        }

        collapsedSelectedTimelineLeadUpRowId = null;
        leadUpExpanded = true;
    }

    private void DrawTimelineLeadUpControls(string idSuffix)
    {
        var availableWidth = MathF.Max(1.0f, GetRightPaddedTableSize(LeadUpTableRightPadding).X);
        var toggleWidth = GetThemedSwitchWidth("Timers");
        if (availableWidth > toggleWidth)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0.0f, availableWidth - toggleWidth));
        }

        DrawLeadUpTimelineTimerToggle($"DeathTimeline{idSuffix}");
        ImGui.Spacing();
    }

    private void DrawSelectableDeathTimelineHeader(string id)
    {
        if (!ImGui.BeginTable(
            $"##ModernDeathTimelineHeader{id}",
            5,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV,
            GetRightPaddedTableSize(LeadUpTableRightPadding)))
        {
            return;
        }

        SetupDeathTimelineColumns();
        DrawCenteredTableHeader("#", "Time", "Player", "Job", "Fatal event");
        ImGui.EndTable();
    }

    private static void SetupDeathTimelineColumns()
    {
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthStretch, 0.32f);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.62f);
        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthStretch, 0.72f);
        ImGui.TableSetupColumn("Fatal event", ImGuiTableColumnFlags.WidthStretch, 2.4f);
    }

    private static bool DrawTimelineIndexCell(
        string text,
        string id,
        bool selected,
        float rowHeight,
        out bool pressed)
    {
        var cellStart = ImGui.GetCursorScreenPos();
        var cellWidth = ImGui.GetContentRegionAvail().X;
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, TimelinePressedRowColor);
        var clicked = ImGui.Selectable(
            $"##{id}",
            selected,
            ImGuiSelectableFlags.SpanAllColumns,
            new Vector2(0.0f, rowHeight));
        pressed = ImGui.IsItemActive();
        ImGui.SetItemAllowOverlap();
        ImGui.PopStyleColor();

        var textSize = ImGui.CalcTextSize(text);
        var textPosition = new Vector2(
            cellStart.X + MathF.Max(0.0f, (cellWidth - textSize.X) * 0.5f),
            cellStart.Y);
        ImGui.GetWindowDrawList().AddText(
            textPosition,
            ImGui.GetColorU32(selected ? LeadUpGoldColor : ModernTextColor),
            text);

        return clicked;
    }

    private void DrawTimelineLeadUpDropdown(
        PartyDeathRecord death,
        uint territoryId,
        IReadOnlyList<ReplayMarkerSnapshot> pullReplayMarkers,
        string idSuffix,
        bool allowScrollHandoff,
        ref float pendingScrollHandoff)
    {
        var resolved = ResolveDeathDisplay(death, territoryId, pullReplayMarkers);
        var rows = GetDisplayLeadUpTimelineRows(resolved.TimelineRows);
        var panelHeight = GetTimelineLeadUpDropdownHeight(rows.Count);
        var panelWidth = MathF.Max(1.0f, GetRightPaddedTableSize(LeadUpTableRightPadding).X);
        var itemSpacing = ImGui.GetStyle().ItemSpacing;
        var parentScrollYBefore = ImGui.GetScrollY();

        ImGui.PushStyleColor(ImGuiCol.ChildBg, WithBackgroundOpacity(ModernPanelAltColor, currentMainWindowBackgroundOpacity));
        ImGui.PushStyleColor(ImGuiCol.Border, ModernPanelBorderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8.0f, 7.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(itemSpacing.X, 4.0f));
        var childHovered = false;
        var childMouseWheel = 0.0f;
        var childScrollY = 0.0f;
        var childScrollMaxY = 0.0f;
        if (ImGui.BeginChild($"##TimelineLeadUpDropdown{idSuffix}", new Vector2(panelWidth, panelHeight), true, OptionalScrollbarFlags))
        {
            DrawHpHistory(resolved, idSuffix, showLabel: false, showHeader: false, rightPadding: 0.0f);
            childHovered = ImGui.IsWindowHovered();
            childMouseWheel = ImGui.GetIO().MouseWheel;
            childScrollY = ImGui.GetScrollY();
            childScrollMaxY = ImGui.GetScrollMaxY();
        }

        ImGui.EndChild();
        QueueTimelineLeadUpScrollHandoff(
            allowScrollHandoff,
            parentScrollYBefore,
            childHovered,
            childMouseWheel,
            childScrollY,
            childScrollMaxY,
            ref pendingScrollHandoff);
        DrawTimelineLeadUpResizeHandle(idSuffix, panelWidth, panelHeight);
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
        ImGui.Dummy(new Vector2(1.0f, 4.0f));
    }

    private static void QueueTimelineLeadUpScrollHandoff(
        bool enabled,
        float parentScrollYBefore,
        bool childHovered,
        float mouseWheel,
        float childScrollY,
        float childScrollMaxY,
        ref float pendingScrollHandoff)
    {
        if (!enabled ||
            !childHovered ||
            MathF.Abs(mouseWheel) <= 0.001f ||
            MathF.Abs(ImGui.GetScrollY() - parentScrollYBefore) > TimelineLeadUpScrollBoundaryEpsilon)
        {
            return;
        }

        var childHasScrollableContent = childScrollMaxY > TimelineLeadUpScrollBoundaryEpsilon;
        var childAtTop = childScrollY <= TimelineLeadUpScrollBoundaryEpsilon;
        var childAtBottom = childScrollY >= childScrollMaxY - TimelineLeadUpScrollBoundaryEpsilon;
        if (childHasScrollableContent &&
            (mouseWheel > 0.0f && !childAtTop ||
                mouseWheel < 0.0f && !childAtBottom))
        {
            return;
        }

        pendingScrollHandoff += -mouseWheel * ImGui.GetTextLineHeightWithSpacing() * TimelineLeadUpMouseWheelScrollLines;
    }

    private static void ApplyTimelineLeadUpScrollHandoff(float scrollDelta)
    {
        if (MathF.Abs(scrollDelta) <= 0.001f)
        {
            return;
        }

        var scrollMaxY = ImGui.GetScrollMaxY();
        if (scrollMaxY <= TimelineLeadUpScrollBoundaryEpsilon)
        {
            return;
        }

        var scrollY = ImGui.GetScrollY();
        var updatedScrollY = Math.Clamp(scrollY + scrollDelta, 0.0f, scrollMaxY);
        if (MathF.Abs(updatedScrollY - scrollY) > TimelineLeadUpScrollBoundaryEpsilon)
        {
            ImGui.SetScrollY(updatedScrollY);
        }
    }

    private float GetTimelineLeadUpDropdownHeight(int rowCount)
    {
        var defaultHeight = GetDefaultTimelineLeadUpDropdownHeight(rowCount);
        return IsUsableTimelineLeadUpDropdownHeight(configuration.DeathTimelineLeadUpHeight)
            ? Math.Clamp(configuration.DeathTimelineLeadUpHeight, TimelineLeadUpDropdownMinHeight, TimelineLeadUpDropdownMaxHeight)
            : defaultHeight;
    }

    private static float GetDefaultTimelineLeadUpDropdownHeight(int rowCount)
    {
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var rowHeight = MathF.Max(30.0f, lineHeight + 13.0f);
        return Math.Clamp((rowCount * rowHeight) + 18.0f, TimelineLeadUpDropdownMinHeight, 300.0f);
    }

    private void DrawTimelineLeadUpResizeHandle(string idSuffix, float panelWidth, float panelHeight)
    {
        var start = ImGui.GetCursorScreenPos();
        var size = new Vector2(MathF.Max(1.0f, panelWidth), TimelineLeadUpResizeHandleHeight);
        ImGui.InvisibleButton(
            $"##TimelineLeadUpResize{idSuffix}",
            size);

        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        if (hovered || active)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
        }

        if (active)
        {
            deathTimelineLeadUpResizeDragging = true;
            var deltaY = ImGui.GetIO().MouseDelta.Y;
            if (MathF.Abs(deltaY) > 0.0f)
            {
                var currentHeight = IsUsableTimelineLeadUpDropdownHeight(configuration.DeathTimelineLeadUpHeight)
                    ? configuration.DeathTimelineLeadUpHeight
                    : panelHeight;
                var newHeight = Math.Clamp(
                    currentHeight + deltaY,
                    TimelineLeadUpDropdownMinHeight,
                    TimelineLeadUpDropdownMaxHeight);
                if (MathF.Abs(configuration.DeathTimelineLeadUpHeight - newHeight) > 0.1f)
                {
                    configuration.DeathTimelineLeadUpHeight = newHeight;
                }
            }
        }
        else if (deathTimelineLeadUpResizeDragging)
        {
            deathTimelineLeadUpResizeDragging = false;
            plugin.SaveConfiguration();
        }

        var drawList = ImGui.GetWindowDrawList();
        var end = start + size;
        var backgroundColor = active
            ? ModernAccentSoftColor with { W = ActiveThemeUsesLightPanels() ? 0.34f : 0.28f }
            : hovered
                ? BlendColors(ModernPanelAltColor, ModernAccentSoftColor, 0.45f) with { W = ActiveThemeUsesLightPanels() ? 0.42f : 0.34f }
                : ModernPanelAltColor with { W = ActiveThemeUsesLightPanels() ? 0.28f : 0.22f };
        drawList.AddRectFilled(start, end, ImGui.GetColorU32(backgroundColor), 0.0f);
        drawList.AddLine(
            start,
            new Vector2(end.X, start.Y),
            ImGui.GetColorU32(ModernPanelBorderColor with { W = hovered || active ? 0.78f : 0.50f }),
            1.0f);

        var gripColor = active
            ? LeadUpGoldColor
            : hovered
                ? ModernAccentColor
                : ModernMutedTextColor with { W = ActiveThemeUsesLightPanels() ? 0.72f : 0.58f };
        var center = new Vector2(
            start.X + (size.X * 0.5f),
            start.Y + (size.Y * 0.5f));
        var halfWidth = MathF.Min(TimelineLeadUpResizeHandleWidth * 0.5f, MathF.Max(0.0f, panelWidth * 0.25f));
        drawList.AddLine(
            new Vector2(center.X - halfWidth, center.Y - 2.0f),
            new Vector2(center.X + halfWidth, center.Y - 2.0f),
            ImGui.GetColorU32(gripColor),
            hovered || active ? 2.0f : 1.0f);
        drawList.AddLine(
            new Vector2(center.X - halfWidth, center.Y + 2.0f),
            new Vector2(center.X + halfWidth, center.Y + 2.0f),
            ImGui.GetColorU32(gripColor),
            hovered || active ? 2.0f : 1.0f);

        if (hovered)
        {
            SetThemedTooltip("Drag to resize the 10s lead-up.");
        }
    }

    private static bool IsUsableTimelineLeadUpDropdownHeight(float height)
    {
        return !float.IsNaN(height) && !float.IsInfinity(height) && height > 0.0f;
    }

    private static float GetThemedSwitchWidth(string label)
    {
        return 18.0f + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(label).X;
    }

    private void DrawSelectedDeathPanel(ReviewPull pull, PartyDeathRecord? death, string idPrefix)
    {
        using var paneIndent = new ImGuiIndentScope(ReviewPaneContentIndent);
        DrawSelectedDeathSectionTitle(pull.Title);
        if (death is null)
        {
            ImGui.TextDisabled(pull.Deaths.Count == 0
                ? "This pull has no recorded deaths."
                : "Select a death from the timeline to inspect details.");
            DrawReviewPaneBottomPadding();
            return;
        }

        var deathId = $"{idPrefix}{pull.Key}{death.MemberKey}{death.SeenAtUtc.Ticks}";
        DrawSelectedDeathHeader(pull, death);
        DrawSelectedDeathReplayLink(pull, death, deathId);
        var resolved = ResolveDeathDisplay(death, pull.TerritoryId, pull.ReplayMarkers);
        DrawDeathDetailSwitcher(deathId);
        ImGui.Spacing();

        switch (selectedDeathDetailPage)
        {
            case DeathDetailPage.WhatIf:
                DrawPossibleMitigationContext(resolved, deathId);
                break;
            default:
                DrawCauseSummary(resolved);
                break;
        }

        DrawReviewPaneBottomPadding();
    }

    private static void DrawReviewPaneBottomPadding()
    {
        ImGui.Dummy(new Vector2(1.0f, ReviewPaneBottomPadding));
    }

    private void DrawDeathDetailSwitcher(string deathId)
    {
        var items = new DeathDetailNavigationItem[]
        {
            new("Summary", DeathDetailPage.Summary),
            new("What-if", DeathDetailPage.WhatIf),
        };
        if (ImGui.GetContentRegionAvail().X < DeathDetailCompactWidthThreshold)
        {
            DrawDeathDetailCombo(items, deathId);
            return;
        }

        DrawResponsiveDeathDetailButtons(items, deathId);
    }

    private void DrawResponsiveDeathDetailButtons(IReadOnlyList<DeathDetailNavigationItem> items, string deathId)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var fullWidth = (items.Count * DeathDetailButtonWidth) + (Math.Max(0, items.Count - 1) * spacing);
        var rowWidth = 0.0f;
        foreach (var item in items)
        {
            var buttonWidth = DeathDetailButtonWidth;
            var nextWidth = rowWidth <= 0.0f
                ? buttonWidth
                : rowWidth + spacing + buttonWidth;
            if (rowWidth > 0.0f && nextWidth > availableWidth)
            {
                rowWidth = 0.0f;
            }
            else if (rowWidth > 0.0f)
            {
                ImGui.SameLine(0.0f, spacing);
            }

            DrawDeathDetailButton(
                item.Label,
                item.Page,
                deathId,
                badgeText: item.BadgeText,
                width: buttonWidth);
            rowWidth = rowWidth <= 0.0f
                ? buttonWidth
                : rowWidth + spacing + buttonWidth;
        }
    }

    private void DrawDeathDetailCombo(IReadOnlyList<DeathDetailNavigationItem> items, string deathId)
    {
        var selectedItem = items.FirstOrDefault(item => item.Page == selectedDeathDetailPage);
        var selectedLabel = string.IsNullOrWhiteSpace(selectedItem.Label)
            ? "Summary"
            : selectedItem.BadgeText is null
                ? selectedItem.Label
                : $"{selectedItem.Label} ({selectedItem.BadgeText})";
        var width = MathF.Min(240.0f, MathF.Max(140.0f, ImGui.GetContentRegionAvail().X));
        ImGui.SetNextItemWidth(width);
        if (!ImGui.BeginCombo($"##DeathDetailCompact{deathId}", selectedLabel))
        {
            return;
        }

        foreach (var item in items)
        {
            var selected = selectedDeathDetailPage == item.Page;
            var label = item.BadgeText is null
                ? item.Label
                : $"{item.Label} ({item.BadgeText})";
            if (ImGui.Selectable(label, selected))
            {
                selectedDeathDetailPage = item.Page;
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawDeathDetailButton(
        string label,
        DeathDetailPage page,
        string deathId,
        bool disabled = false,
        string? tooltip = null,
        string? badgeText = null,
        float width = DeathDetailButtonWidth)
    {
        var selected = !disabled && selectedDeathDetailPage == page;
        var buttonColor = selected ? ModernAccentSoftColor : ModernPanelAltColor;
        var hoveredColor = selected
            ? ModernAccentSoftColor with { W = 1.0f }
            : ModernButtonHoveredColor;

        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ModernAccentSoftColor);
        ImGui.PushStyleColor(ImGuiCol.Text, GetButtonTextColor(buttonColor, selected));
        if (disabled)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button($"{label}##DeathDetail{deathId}{page}", new Vector2(width, 28.0f)))
        {
            selectedDeathDetailPage = page;
        }
        var buttonMin = ImGui.GetItemRectMin();
        var buttonMax = ImGui.GetItemRectMax();
        if (!string.IsNullOrWhiteSpace(badgeText))
        {
            DrawFloatingTabPill(buttonMin, buttonMax, badgeText);
        }

        if (disabled)
        {
            ImGui.EndDisabled();
        }

        if (!string.IsNullOrWhiteSpace(tooltip) &&
            ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            SetThemedTooltip(tooltip);
        }

        ImGui.PopStyleColor(4);
    }

    private static void DrawFloatingTabPill(Vector2 buttonMin, Vector2 buttonMax, string text)
    {
        var drawList = ImGui.GetWindowDrawList();
        var textSize = ImGui.CalcTextSize(text);
        var padding = new Vector2(7.0f, 2.0f);
        var size = textSize + (padding * 2.0f);
        var position = new Vector2(
            buttonMax.X - size.X - 6.0f,
            buttonMin.Y - 5.0f);
        var pillMin = position;
        var pillMax = position + size;
        var shadow = ModernShellColor with { W = ActiveThemeUsesLightPanels() ? 0.22f : 0.48f };
        var fill = LeadUpGoldColor with { W = ActiveThemeUsesLightPanels() ? 0.92f : 0.88f };
        var border = ActiveThemeUsesLightPanels()
            ? ModernPanelBorderColor with { W = 0.92f }
            : ModernTextColor with { W = 0.22f };
        var textColor = ActiveThemeUsesLightPanels()
            ? ModernShellColor
            : new Vector4(0.08f, 0.08f, 0.08f, 1.0f);

        drawList.AddRectFilled(pillMin + new Vector2(1.0f, 1.0f), pillMax + new Vector2(1.0f, 1.0f), ImGui.GetColorU32(shadow), size.Y * 0.5f);
        drawList.AddRectFilled(pillMin, pillMax, ImGui.GetColorU32(fill), size.Y * 0.5f);
        drawList.AddRect(pillMin, pillMax, ImGui.GetColorU32(border), size.Y * 0.5f);
        drawList.AddText(pillMin + padding, ImGui.GetColorU32(textColor), text);
    }

    private static bool DrawSegmentedButton(string label, string id, bool selected, float width)
    {
        var buttonColor = selected ? ModernNavButtonSelectedColor : ModernNavButtonColor;
        var hoveredColor = selected ? ModernNavButtonSelectedHoveredColor : ModernNavButtonHoveredColor;
        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ModernNavButtonActiveColor);
        ImGui.PushStyleColor(ImGuiCol.Text, GetButtonTextColor(buttonColor, selected));
        var clicked = ImGui.Button($"{label}##{id}", new Vector2(width, 24.0f));
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private static bool DrawTextSelectorOption(string label, string id, bool selected)
    {
        var textSize = ImGui.CalcTextSize(label);
        var size = new Vector2(textSize.X, ImGui.GetTextLineHeight());
        var start = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##{id}", size);
        var hovered = ImGui.IsItemHovered();
        var color = GetTextSelectorColor(selected, hovered);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(start, ImGui.GetColorU32(color), label);

        if (selected || hovered)
        {
            var underlineY = start.Y + textSize.Y + 1.0f;
            drawList.AddLine(
                new Vector2(start.X, underlineY),
                new Vector2(start.X + textSize.X, underlineY),
                ImGui.GetColorU32(color with { W = selected ? 0.95f : 0.72f }),
                selected ? 1.4f : 1.0f);
        }

        return clicked;
    }

    private static void DrawTextSelectorSeparator()
    {
        ImGui.SameLine(0.0f, 4.0f);
        ImGui.TextColored(ModernMutedTextColor, "|");
        ImGui.SameLine(0.0f, 4.0f);
    }

    private static float GetTextSelectorWidth(params string[] labels)
    {
        var width = 0.0f;
        foreach (var label in labels)
        {
            width += ImGui.CalcTextSize(label).X;
        }

        if (labels.Length > 1)
        {
            width += ImGui.CalcTextSize("|").X * (labels.Length - 1);
            width += 8.0f * (labels.Length - 1);
        }

        return width;
    }

    private static Vector4 GetTextSelectorColor(bool selected, bool hovered)
    {
        var color = selected
            ? LeadUpGoldColor
            : ModernAccentColor;

        if (hovered)
        {
            color = BlendColors(color, ModernTextColor, selected ? 0.12f : 0.26f);
        }

        return GetColorContrast(ModernPanelColor, color) >= 2.1f
            ? color with { W = 1.0f }
            : GetReadableTextColorForBackground(ModernPanelColor);
    }

    private static bool DrawThemedActionButton(string label, string id, float width = 0.0f)
    {
        var buttonWidth = width <= 0.0f
            ? GetThemedActionButtonWidth(label)
            : MathF.Max(0.0f, width);
        var buttonHeight = ImGui.GetFrameHeight();

        ImGui.PushStyleColor(ImGuiCol.Button, ModernNavButtonSelectedColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ModernNavButtonSelectedHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ModernNavButtonActiveColor);
        ImGui.PushStyleColor(ImGuiCol.Text, GetButtonTextColor(ModernNavButtonSelectedColor, selected: true));
        var clicked = ImGui.Button($"{label}##{id}", new Vector2(buttonWidth, buttonHeight));
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private static float GetThemedActionButtonWidth(string label)
    {
        var style = ImGui.GetStyle();
        return ImGui.CalcTextSize(label).X + (style.FramePadding.X * 2.0f) + 12.0f;
    }

    private static float GetResponsiveButtonWidth(string label, float extraPadding)
    {
        var style = ImGui.GetStyle();
        return ImGui.CalcTextSize(label).X + (style.FramePadding.X * 2.0f) + extraPadding;
    }

    private void DrawSelectedDeathHeader(ReviewPull pull, PartyDeathRecord death)
    {
        var iconId = GetClassJobIconId(death.ClassJobId);
        if (iconId != 0)
        {
            DrawGameIcon(iconId, Math.Clamp(configuration.ActionIconSize, 16.0f, 32.0f), death.ClassJobName);
            ImGui.SameLine();
        }

        ImGui.TextUnformatted($"{FormatPlayerName(death, pull.Deaths)} ({death.ClassJobName})");
        ImGui.TextDisabled($"Death at {FormatCombatTimer(death.PullElapsedSeconds)}");
        ImGui.Separator();
    }

    private void DrawSelectedDeathReplayLink(ReviewPull pull, PartyDeathRecord death, string idSuffix)
    {
        var canOpenReplay = TryGetReplayRecordedPullSummary(pull, out var summary);
        if (!canOpenReplay)
        {
            ImGui.BeginDisabled();
        }

        var width = MathF.Min(GetThemedActionButtonWidth("Open in Replay") + 42.0f, ImGui.GetContentRegionAvail().X);
        var clicked = DrawThemedActionButton("Open in Replay", $"OpenDeathInReplay{idSuffix}", width);
        DrawFloatingTabPill(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ReplayBetaBadgeText);
        if (clicked && canOpenReplay)
        {
            OpenReplayForDeath(summary, death);
        }

        if (!canOpenReplay)
        {
            ImGui.EndDisabled();
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(canOpenReplay
                ? "Opens the saved pull replay around this death."
                : "Full replay is available after the pull is saved.");
        }

        ImGui.Spacing();
    }

    private bool TryGetReplayRecordedPullSummary(ReviewPull pull, out RecordedPullSummary summary)
    {
        if (pull.RecordedPull is { } recordedPull)
        {
            summary = recordedPull;
            return true;
        }

        if (pull.Source == DeathSelectionSource.Current &&
            plugin.CurrentPullClosedForReview &&
            plugin.CurrentPullRecordedPullNumber > 0)
        {
            foreach (var candidate in plugin.RecordedPulls)
            {
                if (candidate.PullNumber == plugin.CurrentPullRecordedPullNumber)
                {
                    summary = candidate;
                    return true;
                }
            }
        }

        summary = default!;
        return false;
    }

    private void OpenReplayForDeath(RecordedPullSummary summary, PartyDeathRecord death)
    {
        selectedReplayPullKey = BuildRecordedPullKey(summary);
        replayFocusDeathSelection = BuildRecordedDeathSelectionTarget(
            death.SeenAtUtc.Ticks,
            Plugin.GetMemberKeyHash(death.MemberKey),
            summary);
        currentMainPage = MainPage.Replay;

        var replayId = BuildReplayViewerId(summary);
        var focusStart = MathF.Max(0.0f, death.PullElapsedSeconds - DeathReplayLeadUpSeconds);
        replayScrubSecondsByDeathId[replayId] = focusStart;
        replayPlayingByDeathId[replayId] = false;
    }

    private static void DrawSelectedDeathSectionTitle(string? subtitle)
    {
        var startCursor = ImGui.GetCursorPos();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        ImGui.TextColored(LeadUpGoldColor, "Selected Death");
        var afterTitleCursor = ImGui.GetCursorPos();
        DrawRightAlignedQuestionTooltipMarker(
            "SelectedDeathSnapshotInfo",
            startCursor,
            availableWidth,
            () => SetThemedTooltip(string.Join(
                Environment.NewLine,
                "Events are shown as accurately as possible while working from snapshotted data.",
                "Better Deaths uses hooks to keep the replay as precise as we can.",
                "Sometimes HP and data fields may not update immediately.",
                "This can cause an invalid HP bar to appear at the end of the history.")));

        ImGui.SetCursorPos(afterTitleCursor);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            ImGui.TextDisabled(subtitle);
        }
    }

    private static void DrawModernSectionTitle(string title, string? subtitle = null)
    {
        ImGui.TextColored(LeadUpGoldColor, title);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            ImGui.TextDisabled(subtitle);
        }
    }

    private void DrawTimelineSectionTitle(string title, string? subtitle, string idSuffix)
    {
        var startCursor = ImGui.GetCursorPos();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var style = ImGui.GetStyle();

        ImGui.TextColored(LeadUpGoldColor, title);
        var afterTitleCursor = ImGui.GetCursorPos();
        var titleWidth = ImGui.CalcTextSize(title).X;
        var displayModeToggleWidth = GetReviewDisplayModeToggleWidth();
        var orderToggleWidth = GetLeadUpTimelineOrderToggleWidth();
        var controlWidth = MathF.Max(displayModeToggleWidth, orderToggleWidth);
        var helpMarkerWidth = 24.0f;
        var toggleX = startCursor.X + MathF.Max(
            0.0f,
            availableWidth - SectionHelpMarkerRightInset - helpMarkerWidth - style.ItemSpacing.X - controlWidth);
        var subtitleWidth = string.IsNullOrWhiteSpace(subtitle)
            ? 0.0f
            : ImGui.CalcTextSize(subtitle).X;
        var hasSubtitleRowSpace = subtitleWidth <= MathF.Max(0.0f, toggleX - startCursor.X - style.ItemSpacing.X);
        var toggleDrawn = false;
        if (toggleX >= startCursor.X + titleWidth + style.ItemSpacing.X &&
            hasSubtitleRowSpace)
        {
            var restoreCursor = ImGui.GetCursorPos();
            ImGui.SetCursorPos(new Vector2(toggleX, startCursor.Y - 2.0f));
            DrawReviewDisplayModeToggle(idSuffix);
            ImGui.SetCursorPos(new Vector2(toggleX, afterTitleCursor.Y));
            DrawLeadUpTimelineOrderToggle(idSuffix);
            ImGui.SetCursorPos(restoreCursor);
            toggleDrawn = true;
        }

        DrawRightAlignedQuestionTooltipMarker(
            $"ReviewLegendHelp{idSuffix}",
            startCursor,
            availableWidth,
            DrawReviewLegendTooltip);

        ImGui.SetCursorPos(afterTitleCursor);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            ImGui.TextDisabled(subtitle);
        }
        else if (toggleDrawn)
        {
            ImGui.Dummy(new Vector2(0.0f, ImGui.GetTextLineHeight()));
        }

        if (!toggleDrawn)
        {
            DrawReviewDisplayModeToggle(idSuffix);
            DrawLeadUpTimelineOrderToggle(idSuffix);
        }
    }

    private void DrawReviewDisplayModeToggle(string idSuffix)
    {
        DrawReviewDisplayModeButton(ReviewDisplayMode.Focused, idSuffix);
        DrawTextSelectorSeparator();
        DrawReviewDisplayModeButton(ReviewDisplayMode.Detailed, idSuffix);
    }

    private void DrawReviewDisplayModeButton(ReviewDisplayMode mode, string idSuffix)
    {
        var selected = configuration.ReviewDisplayMode == mode;
        if (DrawTextSelectorOption(GetReviewDisplayModeLabel(mode), $"ReviewDisplayMode{mode}{idSuffix}", selected) &&
            !selected)
        {
            plugin.SetReviewDisplayMode(mode);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(mode == ReviewDisplayMode.Focused
                ? "Shows a cleaner review view with less table detail."
                : "Shows the full review details.");
        }
    }

    private static float GetReviewDisplayModeToggleWidth()
    {
        return GetTextSelectorWidth("Focused", "Detailed");
    }

    private static string GetReviewDisplayModeLabel(ReviewDisplayMode mode)
    {
        return mode switch
        {
            ReviewDisplayMode.Focused => "Focused",
            _ => "Detailed",
        };
    }

    private void DrawLeadUpTimelineOrderToggle(string idSuffix)
    {
        DrawLeadUpTimelineOrderButton(LeadUpTimelineOrder.Newest, idSuffix);
        DrawTextSelectorSeparator();
        DrawLeadUpTimelineOrderButton(LeadUpTimelineOrder.Oldest, idSuffix);
    }

    private void DrawLeadUpTimelineOrderButton(LeadUpTimelineOrder order, string idSuffix)
    {
        var selected = configuration.LeadUpTimelineOrder == order;
        if (DrawTextSelectorOption(GetLeadUpTimelineOrderLabel(order), $"LeadUpTimelineOrder{order}{idSuffix}", selected) &&
            !selected)
        {
            plugin.SetLeadUpTimelineOrder(order);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(order == LeadUpTimelineOrder.Newest
                ? "Shows the closest events to death first."
                : "Shows the start of the 10-second lead-up first.");
        }
    }

    private static float GetLeadUpTimelineOrderToggleWidth()
    {
        return GetTextSelectorWidth("Newest", "Oldest");
    }

    private static string GetLeadUpTimelineOrderLabel(LeadUpTimelineOrder order)
    {
        return order switch
        {
            LeadUpTimelineOrder.Newest => "Newest",
            _ => "Oldest",
        };
    }

    private static void DrawRightAlignedQuestionTooltipMarker(
        string id,
        Vector2 rowStartCursor,
        float availableWidth,
        Action drawTooltip)
    {
        var restoreCursor = ImGui.GetCursorPos();
        var textSize = ImGui.CalcTextSize("?");
        var hitSize = new Vector2(
            MathF.Max(18.0f, textSize.X + 10.0f),
            MathF.Max(18.0f, textSize.Y + 8.0f));
        var markerX = rowStartCursor.X + MathF.Max(
            0.0f,
            availableWidth - SectionHelpMarkerRightInset - ((hitSize.X + textSize.X) * 0.5f));

        ImGui.SetCursorPos(new Vector2(markerX, rowStartCursor.Y));
        DrawQuestionTooltipMarker(id, drawTooltip);
        ImGui.SetCursorPos(restoreCursor);
    }

    private static void DrawQuestionTooltipMarker(string id, Action drawTooltip)
    {
        ImGui.TextColored(LeadUpGoldColor, "?");
        var textMin = ImGui.GetItemRectMin();
        var textMax = ImGui.GetItemRectMax();
        var restoreCursor = ImGui.GetCursorPos();
        var textSize = textMax - textMin;
        var hitSize = new Vector2(
            MathF.Max(18.0f, textSize.X + 10.0f),
            MathF.Max(18.0f, textSize.Y + 8.0f));
        var hitPosition = new Vector2(
            textMin.X - MathF.Max(0.0f, (hitSize.X - textSize.X) * 0.5f),
            textMin.Y - MathF.Max(0.0f, (hitSize.Y - textSize.Y) * 0.5f));

        ImGui.SetCursorScreenPos(hitPosition);
        ImGui.InvisibleButton($"##{id}", hitSize);
        if (ImGui.IsItemHovered())
        {
            drawTooltip();
        }

        ImGui.SetCursorPos(restoreCursor);
    }

    private static void DrawReviewLegendTooltip()
    {
        BeginThemedTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 34.0f);
        ImGui.TextColored(LeadUpGoldColor, "Review legend");
        ImGui.Separator();
        DrawReviewLegendTooltipLine("KO state", "A captured character has transitioned into death.");
        DrawReviewLegendTooltipLine("Fatal event", "The fatal hit group, fatal status, or selected event around the HP transition into KO.");
        DrawReviewLegendTooltipLine("Non-hit KO", "Kept in the death timeline. A detail panel is shown only when Better Deaths captured status, event, or environmental context.");
        DrawReviewLegendTooltipLine("Recorded pulls", "Created on duty reset, wipe, recommence, and territory changes when the pull had at least one death.");
        DrawReviewLegendTooltipLine("Recorded pull order", "Recorded pulls are grouped by duty, with the duty containing the newest pull shown first.");
        DrawReviewLegendTooltipLine("Duty dropdown", "All duties shows everything, while a selected duty only shows pulls from that duty.");
        ImGui.PopTextWrapPos();
        EndThemedTooltip();
    }

    private static void DrawReviewLegendTooltipLine(string term, string explanation)
    {
        ImGui.TextColored(LeadUpGoldColor, $"{term}:");
        ImGui.SameLine();
        ImGui.TextWrapped(explanation);
    }

    private void ApplyPendingSelectionToReviewWorkspace(
        IReadOnlyList<ReviewPull> pulls,
        ReviewSelectionState selection)
    {
        if (pendingDeathSelection is not { } target)
        {
            return;
        }

        var matchingPull = pulls.FirstOrDefault(pull =>
            DeathSelectionSourceMatches(target, pull.Source, pull.RecordedPull) &&
            ContainsDeath(pull.Deaths, target.DeathSeenAtTicks, target.MemberKeyHash));
        matchingPull ??= pulls.FirstOrDefault(pull =>
            ContainsDeath(pull.Deaths, target.DeathSeenAtTicks, target.MemberKeyHash));
        if (matchingPull is null)
        {
            return;
        }

        selection.PullKey = matchingPull.Key;
        selection.DeathSeenAtTicks = target.DeathSeenAtTicks;
        selection.DeathMemberKeyHash = target.MemberKeyHash;
        clearPendingDeathSelection = true;
    }

    private static void EnsureReviewSelection(
        IReadOnlyList<ReviewPull> pulls,
        ReviewSelectionState selection)
    {
        if (pulls.Count == 0)
        {
            selection.PullKey = null;
            selection.DeathSeenAtTicks = null;
            selection.DeathMemberKeyHash = null;
            return;
        }

        var selectedPull = GetSelectedReviewPull(pulls, selection.PullKey);
        if (selectedPull is null)
        {
            selectedPull = pulls.FirstOrDefault(pull => pull.Deaths.Count > 0) ?? pulls[0];
            selection.PullKey = selectedPull.Key;
            ClearSelectedDeath(selection);
            return;
        }

        if (GetSelectedReviewDeath(selectedPull, selection) is null)
        {
            ClearSelectedDeath(selection);
        }
    }

    private static ReviewPull? GetSelectedReviewPull(IReadOnlyList<ReviewPull> pulls, string? selectedPullKey)
    {
        return selectedPullKey is null
            ? null
            : pulls.FirstOrDefault(pull => string.Equals(pull.Key, selectedPullKey, StringComparison.Ordinal));
    }

    private static PartyDeathRecord? GetSelectedReviewDeath(
        ReviewPull pull,
        ReviewSelectionState selection)
    {
        if (selection.DeathSeenAtTicks is null || selection.DeathMemberKeyHash is null)
        {
            return null;
        }

        return pull.Deaths.FirstOrDefault(death =>
            IsDeathTarget(death, selection.DeathSeenAtTicks.Value, selection.DeathMemberKeyHash.Value));
    }

    private static void SelectDeath(
        PartyDeathRecord death,
        ReviewSelectionState selection)
    {
        selection.DeathSeenAtTicks = death.SeenAtUtc.Ticks;
        selection.DeathMemberKeyHash = Plugin.GetMemberKeyHash(death.MemberKey);
    }

    private static void ClearSelectedDeath(ReviewSelectionState selection)
    {
        selection.DeathSeenAtTicks = null;
        selection.DeathMemberKeyHash = null;
    }

    private static bool IsSelectedReviewDeath(
        PartyDeathRecord death,
        ReviewSelectionState selection)
    {
        return selection.DeathSeenAtTicks is not null &&
            selection.DeathMemberKeyHash is not null &&
            IsDeathTarget(death, selection.DeathSeenAtTicks.Value, selection.DeathMemberKeyHash.Value);
    }

    private void DrawCurrentPull()
    {
        var header = $"{BuildCurrentPullTitle()}###CurrentPullDeaths";
        if (HasPendingDeathSelection(plugin.CurrentDeaths, DeathSelectionSource.Current))
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        }

        if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (plugin.CurrentPullClosedForReview)
        {
            ImGui.TextDisabled("This pull is saved in Recorded pulls and will stay here until the next pull starts.");
        }

        using var currentPullIndent = new ImGuiIndentScope(PullBodyIndent);
        DrawCurrentPullContent("Current");
    }

    internal void DrawCurrentPullWidgetContent()
    {
        ApplyConfiguredTheme();
        DrawCurrentPullWidgetContent(
            plugin.CurrentDeaths,
            BuildCurrentPullWidgetTitle(),
            "CurrentWidget");
    }

    private string BuildCurrentPullTitle()
    {
        var label = plugin.CurrentPullClosedForReview ? "Last Pull Review" : "Current pull";
        var savedText = plugin.CurrentPullClosedForReview && plugin.CurrentPullRecordedPullNumber > 0
            ? $" - saved as Pull {plugin.CurrentPullRecordedPullNumber}"
            : string.Empty;
        return $"{label} - {plugin.CurrentPullTerritoryName} - Timer {FormatCombatTimer(plugin.CurrentPullElapsedSeconds)}{savedText}";
    }

    private string BuildCurrentPullWidgetTitle()
    {
        var label = plugin.CurrentPullClosedForReview ? "Last Pull Review" : "Current pull";
        var savedText = plugin.CurrentPullClosedForReview && plugin.CurrentPullRecordedPullNumber > 0
            ? $" - saved as Pull {plugin.CurrentPullRecordedPullNumber}"
            : string.Empty;
        return $"{label} - {plugin.CurrentPullTerritoryName} - {FormatCombatTimer(plugin.CurrentPullElapsedSeconds)}{savedText}";
    }

    private void DrawCurrentPullContent(string idSuffix)
    {
        DrawCurrentPullContent(plugin.CurrentDeaths, idSuffix);
    }

    private void DrawCurrentPullContent(IReadOnlyList<PartyDeathRecord> deaths, string idSuffix)
    {
        if (deaths.Count == 0)
        {
            ImGui.TextDisabled("No deaths recorded this pull.");
            DrawReviewPaneBottomPadding();
            return;
        }

        var replayMarkers = plugin.GetCurrentPullReplayMarkersForReview();
        DrawDeathTimeline(deaths, idSuffix);
        DrawDeathDetails(deaths, idSuffix, selectionSource: DeathSelectionSource.Current, pullReplayMarkers: replayMarkers);
    }

    private void DrawCurrentPullWidgetContent(IReadOnlyList<PartyDeathRecord> deaths, string title, string idSuffix)
    {
        using var widgetStyle = new ModernWidgetScope();
        using (new ImGuiIndentScope(ReviewPaneHorizontalPadding))
        {
            DrawModernWidgetTitle(title);
            ImGui.Spacing();

            if (deaths.Count == 0)
            {
                ImGui.TextDisabled("No deaths recorded this pull.");
                DrawReviewPaneBottomPadding();
                return;
            }
        }

        if (ImGui.BeginChild($"##CurrentPullWidgetScroll{idSuffix}", Vector2.Zero, false, OptionalScrollbarFlags))
        {
            DrawCurrentPullWidgetDeathTable(deaths, idSuffix);
            DrawReviewPaneBottomPadding();
        }

        ImGui.EndChild();
    }

    private static void DrawModernWidgetTitle(string title)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
        ImGui.TextWrapped(title);
        ImGui.PopStyleColor();
    }

    private void DrawCurrentPullWidgetDeathTable(IReadOnlyList<PartyDeathRecord> deaths, string idSuffix)
    {
        if (!ImGui.BeginTable($"##CurrentPullWidgetDeaths{idSuffix}", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        var conciseMode = IsWidgetConciseMode();
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, conciseMode ? 1.05f : 1.35f);
        ImGui.TableSetupColumn("Event", ImGuiTableColumnFlags.WidthStretch, conciseMode ? 0.75f : 0.95f);
        ImGui.TableSetupColumn("Overkill", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Mits/Debuffs", ImGuiTableColumnFlags.WidthStretch, conciseMode ? 1.85f : 2.35f);
        DrawCenteredTableHeader("Time", "Player", "Event", "Overkill", "Mits/Debuffs");

        var orderedDeaths = GetDeathsInTimelineOrder(deaths);
        for (var i = 0; i < orderedDeaths.Count; i++)
        {
            var death = orderedDeaths[i];
            var selection = DeathDisplaySelector.Select(death);
            var causeEvents = GetTimelineCauseEvents(selection);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredText(FormatCombatTimer(death.PullElapsedSeconds));
            ImGui.TableNextColumn();
            DrawWidgetPlayerCell(death);
            ImGui.TableNextColumn();
            DrawWidgetCauseSummary(death, causeEvents, conciseMode);
            ImGui.TableNextColumn();
            DrawWidgetOverkillSummary(selection);
            ImGui.TableNextColumn();
            DrawWidgetMitsCell(death, selection, conciseMode);
        }

        ImGui.EndTable();
    }

    private void DrawWidgetPlayerCell(PartyDeathRecord death)
    {
        var iconId = GetClassJobIconId(death.ClassJobId);
        var fullDisplayName = FormatPlayerName(death);
        var displayName = FormatWidgetPlayerName(fullDisplayName);
        var tooltip = configuration.RedactPlayerNames
            ? $"{fullDisplayName}\nInitials: {FormatPlayerInitials(fullDisplayName)}"
            : $"Full name: {death.MemberName}\nInitials: {FormatPlayerInitials(death.MemberName)}";
        var iconSize = GetWidgetIconSize();
        var textWidth = ImGui.CalcTextSize(displayName).X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var groupWidth = iconId == 0
            ? textWidth
            : iconSize + spacing + textWidth;
        var shouldCenter = groupWidth <= ImGui.GetContentRegionAvail().X;
        if (shouldCenter)
        {
            CenterNextItem(groupWidth);
        }

        ImGui.BeginGroup();
        if (iconId != 0)
        {
            var iconTop = ImGui.GetCursorPosY();
            DrawGameIcon(iconId, iconSize, death.ClassJobName);
            ImGui.SameLine();
            var textOffset = MathF.Max(0.0f, (iconSize - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.SetCursorPosY(iconTop + textOffset);
        }

        if (shouldCenter)
        {
            ImGui.TextUnformatted(displayName);
        }
        else
        {
            ImGui.TextWrapped(displayName);
        }

        ImGui.EndGroup();
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(tooltip);
        }
    }

    private string FormatWidgetPlayerName(string memberName)
    {
        return IsWidgetConciseMode()
            ? FormatPlayerInitials(memberName)
            : memberName;
    }

    private bool IsWidgetConciseMode()
    {
        return configuration.WidgetDisplayMode == WidgetDisplayMode.Concise;
    }

    private static string FormatPlayerInitials(string memberName)
    {
        var parts = memberName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0)
            .Select(part => $"{part[0]}.");
        var initials = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(initials) ? memberName : initials;
    }

    private void DrawRecordedPulls()
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Recorded pulls");
        DrawRecordedPullHeaderButtons();
        DrawRecordedPullControls();
        if (plugin.RecordedPulls.Count == 0)
        {
            ImGui.TextDisabled(plugin.RecordedPullHistoryLoading
                ? "Loading saved pulls..."
                : "No recorded pulls kept yet.");
            collapseRecordedPullsRequested = false;
            return;
        }

        var visiblePulls = GetVisibleRecordedPulls().ToList();
        if (visiblePulls.Count == 0)
        {
            ImGui.TextDisabled("No recorded pulls match the selected duty.");
            collapseRecordedPullsRequested = false;
            return;
        }

        foreach (var (summary, pullNumber) in visiblePulls)
        {
            var pullId = $"{summary.PullNumber}:{summary.CapturedAtUtc.Ticks}";
            var header = $"Pull {pullNumber} - {summary.TerritoryName} - Timer {FormatCombatTimer(summary.PullElapsedSeconds)}###RecordedPull{pullId}";
            if (PendingDeathSelectionMatchesRecordedPull(summary))
            {
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            }
            else if (collapseRecordedPullsRequested)
            {
                ImGui.SetNextItemOpen(false, ImGuiCond.Always);
            }

            if (!ImGui.CollapsingHeader(header))
            {
                continue;
            }

            using var recordedPullIndent = new ImGuiIndentScope(PullBodyIndent);
            ImGui.TextDisabled(FormatRecordedPullCapturedTime(summary));
            var detail = plugin.GetRecordedPullDetails(summary);
            if (detail is null)
            {
                ImGui.TextDisabled(plugin.RecordedPullHistoryLoading
                    ? "Loading pull details..."
                    : "Pull details could not be loaded.");
                continue;
            }

            DrawDeathTimeline(detail.Deaths, $"Pull{pullId}");
            DrawDeathDetails(
                detail.Deaths,
                $"Pull{pullId}",
                selectionSource: DeathSelectionSource.Recorded,
                recordedPull: summary,
                pullReplayMarkers: detail.ReplayMarkers);
        }

        collapseRecordedPullsRequested = false;
    }

    private void DrawRecordedPullHeaderButtons()
    {
        const string collapseIcon = "▲";
        const string collapseLabel = $"{collapseIcon}##CollapseRecordedPulls";
        var trashIcon = FontAwesomeIcon.Trash.ToIconString();
        var style = ImGui.GetStyle();
        var collapseButtonWidth = ImGui.CalcTextSize(collapseIcon).X + (style.FramePadding.X * 2.0f);
        var clearButtonWidth = ImGui.CalcTextSize(trashIcon).X + (style.FramePadding.X * 2.0f);
        var buttonWidth = MathF.Max(collapseButtonWidth, clearButtonWidth);
        var buttonX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - buttonWidth;

        var hasRecordedPulls = plugin.RecordedPulls.Count > 0;
        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX(), buttonX + buttonWidth - clearButtonWidth));
        if (!hasRecordedPulls)
        {
            ImGui.BeginDisabled();
        }

        if (DrawTransparentIconButton("ClearRecordedPulls", FontAwesomeIcon.Trash) &&
            ImGui.GetIO().KeyCtrl)
        {
            plugin.ClearRecordedPulls();
            pendingDeathSelection = null;
            collapseRecordedPullsRequested = false;
        }

        if (!hasRecordedPulls)
        {
            ImGui.EndDisabled();
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Ctrl+click to delete stored death recaps");
        }

        ImGui.SetCursorPosX(buttonX + buttonWidth - collapseButtonWidth);
        using (new TransparentButtonScope())
        {
            if (ImGui.SmallButton(collapseLabel))
            {
                collapseRecordedPullsRequested = true;
            }
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Collapse all pulls");
        }
    }

    private void DrawChangelogTabItem()
    {
        var highlight = ShouldHighlightChangelogTab();
        if (highlight)
        {
            PushChangelogTabHighlightStyle();
        }

        var isOpen = ImGui.BeginTabItem("Changelog");
        if (highlight)
        {
            DrawChangelogTabHighlightBorder();
            ImGui.PopStyleColor(4);
        }

        if (!isOpen)
        {
            return;
        }

        if (highlight)
        {
            plugin.MarkChangelogVersionSeen(CurrentChangelogVersion);
        }

        DrawChangelogTab();
        ImGui.EndTabItem();
    }

    private bool ShouldHighlightChangelogTab()
    {
        return !string.Equals(configuration.LastSeenChangelogVersion, CurrentChangelogVersion, StringComparison.Ordinal);
    }

    private static void PushChangelogTabHighlightStyle()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
        ImGui.PushStyleColor(ImGuiCol.Tab, activeTheme.ChangelogTabColor);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, activeTheme.ChangelogTabHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.TabActive, activeTheme.ChangelogTabActiveColor);
    }

    private static void DrawChangelogTabHighlightBorder()
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        if (max.X <= min.X || max.Y <= min.Y)
        {
            return;
        }

        var pulse = (MathF.Sin((float)ImGui.GetTime() * 5.0f) + 1.0f) * 0.5f;
        var color = LeadUpGoldColor with { W = 0.45f + (pulse * 0.45f) };
        var padding = 1.0f + pulse;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(
            new Vector2(min.X - padding, min.Y - padding),
            new Vector2(max.X + padding, max.Y + padding),
            ImGui.GetColorU32(color),
            3.0f);
    }

    private void DrawRecordedPullControls()
    {
        DrawRecordedPullDutyFilterControl();
    }

    private static float GetRecordedPullComboWidth(float preferredWidth)
    {
        var availableWidth = MathF.Max(120.0f, ImGui.GetContentRegionAvail().X);
        return MathF.Min(preferredWidth, availableWidth);
    }

    private void DrawRecordedPullDutyFilterControl()
    {
        var dutyOptions = GetRecordedPullDutyOptions().ToList();
        if (recordedPullDutyFilter != AllRecordedPullDuties &&
            !dutyOptions.Any(option => option.TerritoryId == recordedPullDutyFilter))
        {
            recordedPullDutyFilter = AllRecordedPullDuties;
        }

        var comboWidth = GetRecordedPullComboWidth(RecordedPullDutyFilterComboWidth);
        ImGui.SetNextItemWidth(comboWidth);
        if (!ImGui.BeginCombo("##RecordedPullDutyFilter", GetRecordedPullDutyFilterLabel(dutyOptions)))
        {
            return;
        }

        var allSelected = recordedPullDutyFilter == AllRecordedPullDuties;
        if (ImGui.Selectable("All duties", allSelected))
        {
            recordedPullDutyFilter = AllRecordedPullDuties;
        }

        if (allSelected)
        {
            ImGui.SetItemDefaultFocus();
        }

        foreach (var option in dutyOptions)
        {
            var selected = recordedPullDutyFilter == option.TerritoryId;
            if (ImGui.Selectable($"{option.TerritoryName} ({option.PullCount})", selected))
            {
                recordedPullDutyFilter = option.TerritoryId;
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private IEnumerable<RecordedPullDutyOption> GetRecordedPullDutyOptions()
    {
        return plugin.RecordedPulls
            .GroupBy(snapshot => snapshot.TerritoryId)
            .Select(group =>
            {
                var territoryName = group
                    .Select(snapshot => snapshot.TerritoryName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Unknown territory";
                return new RecordedPullDutyOption(group.Key, territoryName, group.Count());
            })
            .OrderBy(option => option.TerritoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.TerritoryId);
    }

    private string GetRecordedPullDutyFilterLabel(IReadOnlyList<RecordedPullDutyOption> dutyOptions)
    {
        if (recordedPullDutyFilter == AllRecordedPullDuties)
        {
            return "All duties";
        }

        var option = dutyOptions.FirstOrDefault(option => option.TerritoryId == recordedPullDutyFilter);
        return option is null
            ? "All duties"
            : $"{option.TerritoryName} ({option.PullCount})";
    }

    private IEnumerable<(RecordedPullSummary Summary, long PullNumber)> GetVisibleRecordedPulls()
    {
        var pulls = plugin.RecordedPulls
            .Select(summary => (Summary: summary, PullNumber: summary.PullNumber));

        if (recordedPullDutyFilter != AllRecordedPullDuties)
        {
            pulls = pulls.Where(entry => entry.Summary.TerritoryId == recordedPullDutyFilter);
        }

        return pulls
            .OrderByDescending(entry => entry.PullNumber)
            .ThenByDescending(entry => entry.Summary.CapturedAtUtc)
            .ThenBy(entry => entry.Summary.TerritoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Summary.TerritoryId);
    }

    private void DrawDeathTimeline(IReadOnlyList<PartyDeathRecord> deaths, string idSuffix)
    {
        ImGui.TextUnformatted("Death timeline");
        if (!ImGui.BeginTable(
            $"##DeathTimeline{idSuffix}",
            5,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg,
            GetRightPaddedTableSize(LeadUpTableRightPadding)))
        {
            return;
        }

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthStretch, 0.35f);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Fatal event", ImGuiTableColumnFlags.WidthStretch, 2.8f);
        DrawCenteredTableHeader("#", "Time", "Player", "Job", "Fatal event");

        var orderedDeaths = GetDeathsInTimelineOrder(deaths);
        for (var i = 0; i < orderedDeaths.Count; i++)
        {
            var death = orderedDeaths[i];
            var causeEvents = GetTimelineCauseEvents(death);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredText((i + 1).ToString());
            ImGui.TableNextColumn();
            DrawCenteredText(FormatCombatTimer(death.PullElapsedSeconds));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatPlayerName(death, deaths));
            ImGui.TableNextColumn();
            DrawJobCell(death);

            ImGui.TableNextColumn();
            DrawTimelineCauseText(death, causeEvents, $"Cause{idSuffix}{death.MemberKey}{death.SeenAtUtc.Ticks}");
        }

        ImGui.EndTable();
    }

    private void DrawCenteredTableHeader(params string[] labels)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        foreach (var label in labels)
        {
            ImGui.TableNextColumn();
            DrawCenteredOrWrappedText(label);
        }
    }

    private static void DrawHpHistoryTableHeader()
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        DrawCenteredIconHeaderCell(FontAwesomeIcon.Clock);
        DrawCenteredHeaderCell("Source");
        DrawCenteredHeaderCell("HP + shields");
        DrawCenteredHeaderCell("Events");
        DrawCenteredHeaderCell("Mits/Debuffs");
    }

    private static void DrawCenteredHeaderCell(string label)
    {
        ImGui.TableNextColumn();
        DrawCenteredOrWrappedText(label);
    }

    private static void DrawCenteredIconHeaderCell(FontAwesomeIcon icon)
    {
        ImGui.TableNextColumn();
        var iconText = icon.ToIconString();
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            CenterNextItem(ImGui.CalcTextSize(iconText).X);
            ImGui.TextUnformatted(iconText);
        }
    }

    private static void DrawCenteredHpShieldBar(
        uint currentHp,
        uint shieldHp,
        uint maxHp,
        string id,
        ulong? incomingDamage = null,
        string? tooltipDetail = null,
        HpBarHealChange? healChange = null,
        HpBarDamageChange? damageChange = null)
    {
        var width = GetHpShieldBarWidth(maxHp);
        CenterNextItem(width);
        DrawHpShieldBar(
            currentHp,
            shieldHp,
            maxHp,
            id,
            incomingDamage,
            centerLabel: true,
            tooltipDetail: tooltipDetail,
            healChange: healChange,
            damageChange: damageChange);
    }

    private IReadOnlyList<CombatEventRecord> GetTimelineCauseEvents(PartyDeathRecord death)
    {
        return GetRawTimelineCauseEvents(DeathDisplaySelector.Select(death));
    }

    private static IReadOnlyList<CombatEventRecord> GetTimelineCauseEvents(DeathDisplaySelection selection)
    {
        return selection.Events
            .Where(IsFatalEvent)
            .ToList();
    }

    private static IReadOnlyList<CombatEventRecord> GetRawTimelineCauseEvents(DeathDisplaySelection selection)
    {
        return selection.FatalEvents
            .SelectMany(group => group.Events)
            .Where(IsFatalEvent)
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ThenBy(combatEvent => combatEvent.EventOrdinal)
            .ToList();
    }

    private float GetTimelineRowHeight(IReadOnlyList<CombatEventRecord> causeEvents, string causeId)
    {
        var baseRowHeight = MathF.Max(ImGui.GetTextLineHeightWithSpacing(), ImGui.GetFrameHeight());
        if (causeEvents.Count <= 1 || !expandedTimelineCauseRows.Contains(causeId))
        {
            return baseRowHeight;
        }

        var style = ImGui.GetStyle();
        return baseRowHeight +
            MathF.Max(1.0f, style.ItemInnerSpacing.Y * 0.5f) +
            style.ItemSpacing.Y +
            (causeEvents.Count * ImGui.GetTextLineHeightWithSpacing());
    }

    private bool DrawTimelineCauseText(
        PartyDeathRecord? death,
        IReadOnlyList<CombatEventRecord> causeEvents,
        string id,
        Action? selectDeath = null,
        Action? markDeathPressed = null)
    {
        if (causeEvents.Count == 0)
        {
            DrawCenteredOrWrappedText(GetNonHitKoLabel(death), WarningColor);
            return false;
        }

        if (causeEvents.Count > 1)
        {
            return DrawCollapsedTimelineCauseText(causeEvents, id, selectDeath, markDeathPressed);
        }

        foreach (var causeEvent in causeEvents)
        {
            var line = FormatTimelineCauseLine(causeEvent);
            if (selectDeath is not null)
            {
                DrawSelectableCenteredOrWrappedTimelineText(
                    $"TimelineCause{id}",
                    line,
                    GetEventColor(causeEvent.Kind),
                    selectDeath,
                    markDeathPressed);
            }
            else
            {
                DrawCenteredOrWrappedText(line, GetEventColor(causeEvent.Kind));
            }

            DrawLikelyAutoAttackTooltip(causeEvent);
        }

        return false;
    }

    private static string GetNonHitKoLabel(PartyDeathRecord? death)
    {
        return IsEnvironmentSourceDeath(death) ? "Walled" : "Non-hit KO";
    }

    private static string GetNonHitKoTooltip(PartyDeathRecord? death)
    {
        return IsEnvironmentSourceDeath(death)
            ? "Walled."
            : "Non-hit KO.";
    }

    private static bool IsEnvironmentSourceDeath(PartyDeathRecord? death)
    {
        return death?.EnvironmentalAssessment is { EnvironmentSourceDeath: true };
    }

    private bool DrawCollapsedTimelineCauseText(
        IReadOnlyList<CombatEventRecord> causeEvents,
        string id,
        Action? selectDeath,
        Action? markDeathPressed)
    {
        var summary = BuildTimelineCauseSummary(causeEvents);
        var textColor = GetWidgetCauseColor(causeEvents);
        var style = ImGui.GetStyle();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var causeLines = causeEvents
            .Select(FormatTimelineCauseLine)
            .ToList();
        var isExpanded = expandedTimelineCauseRows.Contains(id);
        var wasExpanded = isExpanded;
        var summaryTextSize = ImGui.CalcTextSize(summary);
        var controlWidth = MathF.Max(0.0f, availableWidth);
        var controlSize = new Vector2(controlWidth, MathF.Max(ImGui.GetFrameHeight(), summaryTextSize.Y));

        CenterNextItem(controlWidth);
        var controlPosition = ImGui.GetCursorScreenPos();
        var arrowClicked = DrawDisclosureArrowButton(
            $"##TimelineCauseArrow{id}",
            isExpanded,
            controlPosition,
            controlSize.Y,
            summaryTextSize.Y,
            textColor,
            out var arrowHovered,
            out var arrowSize,
            out _);
        var afterArrowCursor = ImGui.GetCursorPos();
        var drawList = ImGui.GetWindowDrawList();
        var textY = controlPosition.Y + MathF.Max(0.0f, (controlSize.Y - summaryTextSize.Y) * 0.5f);
        var summaryMinX = controlPosition.X + style.FramePadding.X + arrowSize + style.ItemInnerSpacing.X;
        var summaryPosition = new Vector2(
            MathF.Max(summaryMinX, controlPosition.X + MathF.Max(0.0f, (controlSize.X - summaryTextSize.X) * 0.5f)),
            textY);

        if (selectDeath is not null)
        {
            ImGui.SetCursorScreenPos(new Vector2(summaryPosition.X, controlPosition.Y));
            if (ImGui.InvisibleButton($"##TimelineCauseSummary{id}", new Vector2(summaryTextSize.X, controlSize.Y)))
            {
                selectDeath();
            }

            if (ImGui.IsItemActive())
            {
                markDeathPressed?.Invoke();
            }
        }

        drawList.AddText(summaryPosition, ImGui.GetColorU32(textColor), summary);
        ImGui.SetCursorPos(afterArrowCursor);

        if (arrowClicked)
        {
            if (isExpanded)
            {
                expandedTimelineCauseRows.Remove(id);
                isExpanded = false;
            }
            else
            {
                expandedTimelineCauseRows.Add(id);
                isExpanded = true;
            }
        }

        if (arrowHovered)
        {
            SetThemedTooltip(isExpanded ? "Collapse fatal events." : "Expand fatal events.");
        }

        if (!isExpanded)
        {
            return arrowClicked;
        }

        if (arrowClicked && !wasExpanded)
        {
            return true;
        }

        ImGui.Dummy(new Vector2(0.0f, MathF.Max(1.0f, style.ItemInnerSpacing.Y * 0.5f)));
        for (var causeIndex = 0; causeIndex < causeEvents.Count; causeIndex++)
        {
            var causeEvent = causeEvents[causeIndex];
            var line = causeLines[causeIndex];
            if (selectDeath is not null)
            {
                DrawSelectableCenteredOrWrappedTimelineText(
                    $"TimelineCause{id}{causeIndex}",
                    line,
                    GetEventColor(causeEvent.Kind),
                    selectDeath,
                    markDeathPressed);
            }
            else
            {
                DrawCenteredOrWrappedText(line, GetEventColor(causeEvent.Kind));
            }

            DrawLikelyAutoAttackTooltip(causeEvent);
        }

        return arrowClicked;
    }

    private static bool DrawDisclosureArrowButton(
        string id,
        bool isExpanded,
        Vector2 controlPosition,
        float controlHeight,
        float textHeight,
        Vector4 textColor,
        out bool hovered,
        out float arrowSize,
        out float arrowHitWidth)
    {
        var style = ImGui.GetStyle();
        arrowSize = MathF.Min(8.0f, MathF.Max(5.0f, textHeight * 0.55f));
        arrowHitWidth = arrowSize + (style.FramePadding.X * 2.0f);
        var clicked = ImGui.InvisibleButton(id, new Vector2(arrowHitWidth, controlHeight));
        hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        var arrowX = controlPosition.X + style.FramePadding.X;
        var arrowCenterY = controlPosition.Y + (controlHeight * 0.5f);
        var arrowHalfSize = arrowSize * 0.5f;
        var arrowColor = ImGui.GetColorU32(textColor);
        if (isExpanded)
        {
            drawList.AddTriangleFilled(
                new Vector2(arrowX, arrowCenterY - arrowHalfSize),
                new Vector2(arrowX + arrowSize, arrowCenterY - arrowHalfSize),
                new Vector2(arrowX + arrowHalfSize, arrowCenterY + arrowHalfSize),
                arrowColor);
        }
        else
        {
            drawList.AddTriangleFilled(
                new Vector2(arrowX, arrowCenterY - arrowHalfSize),
                new Vector2(arrowX, arrowCenterY + arrowHalfSize),
                new Vector2(arrowX + arrowSize, arrowCenterY),
                arrowColor);
        }

        return clicked;
    }

    private static void DrawSelectableCenteredOrWrappedTimelineText(
        string id,
        string text,
        Vector4 color,
        Action selectDeath,
        Action? markDeathPressed)
    {
        var textWidth = ImGui.CalcTextSize(text).X;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var lines = textWidth <= availableWidth
            ? [text]
            : WrapTextForWidth(text, availableWidth).ToList();
        var drawList = ImGui.GetWindowDrawList();

        ImGui.BeginGroup();
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var lineSize = ImGui.CalcTextSize(line);
            CenterNextItem(lineSize.X);
            var linePosition = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton($"##{id}{lineIndex}", new Vector2(lineSize.X, MathF.Max(lineSize.Y, ImGui.GetTextLineHeight()))))
            {
                selectDeath();
            }

            if (ImGui.IsItemActive())
            {
                markDeathPressed?.Invoke();
            }

            drawList.AddText(linePosition, ImGui.GetColorU32(color), line);
        }

        ImGui.EndGroup();
    }

    private static string BuildTimelineCauseSummary(IReadOnlyList<CombatEventRecord> causeEvents)
    {
        return $"{causeEvents.Count} fatal events";
    }

    private static string FormatTimelineCauseLine(CombatEventRecord combatEvent)
    {
        return EventHasSignedAmount(combatEvent)
            ? $"{FormatActionNameForDisplay(combatEvent)}: {FormatSignedEventAmount(combatEvent)}"
            : FormatActionNameForDisplay(combatEvent);
    }

    private void DrawWidgetCauseSummary(
        PartyDeathRecord death,
        IReadOnlyList<CombatEventRecord> causeEvents,
        bool conciseMode)
    {
        var text = conciseMode
            ? FormatConciseWidgetCauseSummary(death, causeEvents)
            : FormatWidgetCauseSummary(death, causeEvents);
        DrawCenteredOrWrappedText(text, GetWidgetCauseColor(causeEvents));

        if (ImGui.IsItemHovered())
        {
            if (causeEvents.Count == 0)
            {
                SetThemedTooltip(GetNonHitKoTooltip(death));
                return;
            }

            var tooltipLines = causeEvents.Select(FormatFatalEventLine).ToList();
            if (causeEvents.Any(IsLikelyAutoAttack))
            {
                tooltipLines.Add(string.Empty);
                tooltipLines.Add(LikelyAutoAttackTooltip);
            }

            SetThemedTooltip(string.Join(Environment.NewLine, tooltipLines));
        }
    }

    private string FormatWidgetCauseSummary(PartyDeathRecord death, IReadOnlyList<CombatEventRecord> causeEvents)
    {
        if (causeEvents.Count == 0)
        {
            return GetNonHitKoLabel(death);
        }

        var damageEvents = causeEvents
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .ToList();
        var totalDamage = damageEvents.Aggregate(0UL, (sum, cause) => sum + cause.Amount);
        if (damageEvents.Count > 1)
        {
            return $"{damageEvents.Count} hits | {FormatSignedDamageAmount(totalDamage)} total";
        }

        if (causeEvents.Count > 1 && totalDamage > 0)
        {
            return $"{causeEvents.Count} events | {FormatSignedDamageAmount(totalDamage)} damage";
        }

        var cause = causeEvents[0];
        return cause.Kind == DeathEventKind.Status
            ? $"{FormatActionNameForDisplay(cause)} from {FormatKnownPlayerName(cause.SourceName)}"
            : $"{FormatSignedEventAmount(cause)} {FormatActionNameForDisplay(cause)}";
    }

    private static string FormatConciseWidgetCauseSummary(PartyDeathRecord death, IReadOnlyList<CombatEventRecord> causeEvents)
    {
        if (causeEvents.Count == 0)
        {
            return IsEnvironmentSourceDeath(death) ? "Walled" : "-";
        }

        var totalDamage = causeEvents
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .Aggregate(0UL, (sum, cause) => sum + cause.Amount);

        return totalDamage > 0 ? FormatSignedDamageAmount(totalDamage) : "-";
    }

    private static void DrawWidgetOverkillSummary(DeathDisplaySelection selection)
    {
        var incomingDamage = GetIncomingDamageAmount(selection.Events);
        if (incomingDamage is null || selection.Snapshot is null)
        {
            DrawCenteredText("-", DisabledColor);
            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip("No incoming damage and pre-hit HP snapshot were available for this death.");
            }

            return;
        }

        var snapshot = selection.Snapshot;
        var overkillDisplay = GetOverkillDisplay(snapshot.CurrentHp, incomingDamage);
        DrawCenteredText(overkillDisplay.CompactText, overkillDisplay.Color);

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(
                $"Incoming damage: {incomingDamage.Value:N0}\n" +
                $"HP before hit: {snapshot.CurrentHp:N0}\n" +
                $"Captured shield: {snapshot.ShieldHp:N0}\n" +
                overkillDisplay.TooltipLine);
        }
    }

    private void DrawWidgetMitsCell(PartyDeathRecord death, DeathDisplaySelection selection, bool conciseMode)
    {
        var statuses = GetWidgetMitStatuses(death, selection);
        if (statuses.Count == 0)
        {
            DrawCenteredText("-", DisabledColor);
            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip("No player mitigations/debuffs or boss damage-down debuffs were captured for this death.");
            }

            return;
        }

        if (conciseMode)
        {
            DrawConciseWidgetMitIcons(statuses);
            return;
        }

        DrawWidgetMitIcons(statuses);
    }

    private IReadOnlyList<WidgetMitStatus> GetWidgetMitStatuses(PartyDeathRecord death, DeathDisplaySelection selection)
    {
        var playerStatuses = plugin
            .GetRelevantPlayerStatusesForDisplay(selection.Snapshot?.Statuses ?? death.StatusesAtDeath)
            .Select(status => new WidgetMitStatus(status, "Player", string.Empty));
        var bossStatuses = selection.Events
            .SelectMany(combatEvent => Plugin.GetBossMitigationStatusesForDisplay(GetEventSourceMitigationStatuses(death, combatEvent))
                .Select(status => new WidgetMitStatus(status, "Boss", combatEvent.SourceName)));

        return playerStatuses
            .Concat(bossStatuses)
            .GroupBy(status => (status.Category, status.SourceName, status.Status.Id, status.Status.IconId, status.Status.SourceId))
            .Select(group => group
                .OrderBy(status => status.Status.RemainingTime <= 0.0f ? float.MaxValue : status.Status.RemainingTime)
                .First())
            .OrderBy(status => status.Category == "Boss" ? 1 : 0)
            .ThenBy(status => status.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Status.Id)
            .ToList();
    }

    private void DrawWidgetMitIcons(IReadOnlyList<WidgetMitStatus> statuses)
    {
        var iconSize = GetWidgetIconSize();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var availableWidth = MathF.Max(iconSize, ImGui.GetContentRegionAvail().X);
        var rows = new List<List<WidgetMitStatus>>();
        var rowWidths = new List<float>();
        var currentRow = new List<WidgetMitStatus>();
        var currentRowWidth = 0.0f;

        foreach (var status in statuses)
        {
            if (currentRow.Count > 0 && currentRowWidth + spacing + iconSize > availableWidth)
            {
                rows.Add(currentRow);
                rowWidths.Add(currentRowWidth);
                currentRow = [];
                currentRowWidth = 0.0f;
            }

            if (currentRow.Count > 0)
            {
                currentRowWidth += spacing;
            }

            currentRow.Add(status);
            currentRowWidth += iconSize;
        }

        if (currentRow.Count > 0)
        {
            rows.Add(currentRow);
            rowWidths.Add(currentRowWidth);
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            CenterNextItem(rowWidths[rowIndex]);
            var row = rows[rowIndex];
            for (var statusIndex = 0; statusIndex < row.Count; statusIndex++)
            {
                if (statusIndex > 0)
                {
                    ImGui.SameLine();
                }

                DrawWidgetMitIcon(row[statusIndex], iconSize);
            }
        }
    }

    private void DrawConciseWidgetMitIcons(IReadOnlyList<WidgetMitStatus> statuses)
    {
        var iconSize = GetWidgetIconSize();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var visibleCount = GetVisibleWidgetMitIconCount(
            statuses.Count,
            iconSize,
            spacing,
            ImGui.GetContentRegionAvail().X);
        var visibleStatuses = statuses.Take(visibleCount).ToList();
        var extraCount = Math.Max(0, statuses.Count - visibleStatuses.Count);
        var extraText = extraCount > 0 ? $"+{extraCount}" : string.Empty;
        var groupWidth = GetWidgetMitOverflowGroupWidth(visibleStatuses.Count, extraText, iconSize, spacing);

        CenterNextItem(groupWidth);
        ImGui.BeginGroup();
        for (var statusIndex = 0; statusIndex < visibleStatuses.Count; statusIndex++)
        {
            if (statusIndex > 0)
            {
                ImGui.SameLine();
            }

            DrawWidgetMitIcon(visibleStatuses[statusIndex], iconSize);
        }

        if (!string.IsNullOrEmpty(extraText))
        {
            if (visibleStatuses.Count > 0)
            {
                ImGui.SameLine();
            }

            ImGui.TextUnformatted(extraText);
            if (ImGui.IsItemHovered())
            {
                DrawWidgetHiddenMitIconsTooltip(statuses.Skip(visibleStatuses.Count).ToList(), iconSize);
            }
        }

        ImGui.EndGroup();
    }

    private static int GetVisibleWidgetMitIconCount(int statusCount, float iconSize, float spacing, float availableWidth)
    {
        if (statusCount <= 0)
        {
            return 0;
        }

        var maxIconsWithoutOverflow = GetWidgetMitIconFitCount(statusCount, iconSize, spacing, availableWidth);
        if (statusCount <= maxIconsWithoutOverflow)
        {
            return statusCount;
        }

        var startingVisibleCount = Math.Min(statusCount - 1, maxIconsWithoutOverflow);
        for (var visibleCount = startingVisibleCount; visibleCount >= 0; visibleCount--)
        {
            var extraText = $"+{statusCount - visibleCount}";
            var groupWidth = GetWidgetMitOverflowGroupWidth(visibleCount, extraText, iconSize, spacing);
            if (groupWidth <= availableWidth || visibleCount == 0)
            {
                return visibleCount;
            }
        }

        return 0;
    }

    private static int GetWidgetMitIconFitCount(int statusCount, float iconSize, float spacing, float availableWidth)
    {
        if (statusCount <= 0 || availableWidth < iconSize)
        {
            return 0;
        }

        return Math.Min(statusCount, (int)MathF.Floor((availableWidth + spacing) / (iconSize + spacing)));
    }

    private static float GetWidgetMitOverflowGroupWidth(int visibleCount, string extraText, float iconSize, float spacing)
    {
        var groupWidth = visibleCount * iconSize;
        if (visibleCount > 1)
        {
            groupWidth += (visibleCount - 1) * spacing;
        }

        if (!string.IsNullOrEmpty(extraText))
        {
            groupWidth += (visibleCount > 0 ? spacing : 0.0f) + ImGui.CalcTextSize(extraText).X;
        }

        return groupWidth;
    }

    private static void DrawWidgetHiddenMitIconsTooltip(IReadOnlyList<WidgetMitStatus> statuses, float configuredIconSize)
    {
        if (statuses.Count == 0)
        {
            return;
        }

        var iconSize = Math.Clamp(configuredIconSize, 12.0f, 48.0f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var maxWidth = MathF.Max(iconSize, iconSize * 6.0f + spacing * 5.0f);
        var rowWidth = 0.0f;

        BeginThemedTooltip();
        for (var statusIndex = 0; statusIndex < statuses.Count; statusIndex++)
        {
            var needsSameLine = rowWidth > 0.0f && rowWidth + spacing + iconSize <= maxWidth;
            if (needsSameLine)
            {
                ImGui.SameLine();
                rowWidth += spacing;
            }
            else
            {
                rowWidth = 0.0f;
            }

            DrawTooltipStatusIcon(statuses[statusIndex].Status.IconId, iconSize);
            rowWidth += iconSize;
        }

        EndThemedTooltip();
    }

    private static void DrawTooltipStatusIcon(uint iconId, float iconSize)
    {
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(iconSize));
            return;
        }

        try
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            var wrap = texture.GetWrapOrDefault();
            if (wrap is not null)
            {
                ImGui.Image(wrap.Handle, new Vector2(iconSize));
                return;
            }
        }
        catch
        {
            // Fall through to the placeholder.
        }

        ImGui.Dummy(new Vector2(iconSize));
    }

    private void DrawWidgetMitIcon(WidgetMitStatus status, float iconSize)
    {
        DrawGameIcon(status.Status.IconId, iconSize, FormatWidgetMitTooltip(status));
    }

    private string FormatWidgetMitTooltip(WidgetMitStatus status)
    {
        var tooltipPrefix = status.Category == "Boss" && !string.IsNullOrWhiteSpace(status.SourceName)
            ? $"{status.Category} ({FormatKnownPlayerName(status.SourceName)})"
            : status.Category;
        return $"{tooltipPrefix}: {FormatStatusCompact(status.Status)}";
    }

    private static string FormatWidgetAmount(ulong amount)
    {
        if (amount >= 1_000_000)
        {
            return $"{TruncateToOneDecimal(amount / 1_000_000.0):0.#}m";
        }

        return amount >= 1_000
            ? $"{TruncateToOneDecimal(amount / 1_000.0):0.#}k"
            : amount.ToString("N0");
    }

    private static double TruncateToOneDecimal(double value)
    {
        return Math.Truncate(value * 10.0) / 10.0;
    }

    private static Vector4 GetWidgetCauseColor(IReadOnlyList<CombatEventRecord> causeEvents)
    {
        if (causeEvents.Count == 0)
        {
            return WarningColor;
        }

        if (causeEvents.Any(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0))
        {
            return DamageColor;
        }

        if (causeEvents.Any(cause => cause.Kind == DeathEventKind.Heal && cause.Amount > 0))
        {
            return HealColor;
        }

        if (causeEvents.Any(cause => cause.Kind == DeathEventKind.Status))
        {
            return WarningColor;
        }

        return DisabledColor;
    }

    private void DrawDeathDetails(
        IReadOnlyList<PartyDeathRecord> deaths,
        string idSuffix,
        bool useCollapsers = true,
        DeathSelectionSource selectionSource = DeathSelectionSource.Current,
        RecordedPullSummary? recordedPull = null,
        IReadOnlyList<ReplayMarkerSnapshot>? pullReplayMarkers = null)
    {
        var orderedDeaths = GetDeathsInTimelineOrder(deaths);
        var replayMarkers = pullReplayMarkers ?? [];
        for (var i = 0; i < orderedDeaths.Count; i++)
        {
            var death = orderedDeaths[i];
            var deathNumber = i + 1;
            var isSelectedDeath = IsPendingDeathSelection(death, selectionSource, recordedPull);
            if (!HasDeathDetails(death))
            {
                if (isSelectedDeath)
                {
                    clearPendingDeathSelection = true;
                }

                continue;
            }

            var playerName = FormatPlayerName(death);
            var header = $"#{deathNumber} - {FormatCombatTimer(death.PullElapsedSeconds)} - {playerName} ({death.ClassJobName})###DeathDetail{idSuffix}{death.MemberKey}{death.SeenAtUtc.Ticks}";
            if (useCollapsers)
            {
                if (isSelectedDeath)
                {
                    ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                }

                if (!ImGui.CollapsingHeader(header))
                {
                    continue;
                }
            }
            else
            {
                ImGui.Separator();
                ImGui.TextUnformatted($"#{deathNumber} - {FormatCombatTimer(death.PullElapsedSeconds)} - {playerName} ({death.ClassJobName})");
            }

            if (isSelectedDeath)
            {
                ImGui.SetScrollHereY(0.1f);
                clearPendingDeathSelection = true;
            }

            using var deathDetailIndent = new ImGuiIndentScope(DeathDetailIndent);
            var resolved = ResolveDeathDisplay(death, recordedPull?.TerritoryId ?? plugin.CurrentTerritoryId, replayMarkers);
            DrawCauseSummary(resolved);
            var deathId = $"{idSuffix}{death.MemberKey}{death.SeenAtUtc.Ticks}";
            ImGui.Separator();
            DrawExtraMitigationContext(resolved, deathId);
            ImGui.Separator();
            DrawBetterDeathsInformation(resolved, deathId);
            DrawReviewPaneBottomPadding();
        }
    }

    private static bool HasDeathDetails(PartyDeathRecord death)
    {
        return DeathDisplaySelector.Select(death).Events.Count > 0 ||
            death.EnvironmentalAssessment is { Confidence: > 0.0f };
    }

    private static IReadOnlyList<PartyDeathRecord> GetDeathsInTimelineOrder(IReadOnlyList<PartyDeathRecord> deaths)
    {
        return deaths
            .OrderBy(death => death.SeenAtUtc)
            .ThenBy(death => death.PartyIndex)
            .ToList();
    }

    private ResolvedDeathDisplay ResolveDeathDisplay(
        PartyDeathRecord death,
        uint territoryId,
        IReadOnlyList<ReplayMarkerSnapshot>? pullReplayMarkers = null)
    {
        var selection = DeathDisplaySelector.Select(death);
        var causeEvents = GetTimelineCauseEvents(selection);
        var leadUpEvents = GetLeadUpEvents(death);
        var timelineRows = GetLeadUpTimelineRows(death, GetLeadUpDisplayAnchorSeenAtUtc(death), leadUpEvents);
        var summaryRow = GetLeadUpSummaryRow(death, selection, leadUpEvents, timelineRows);
        var summaryMitigationDebuffStatuses = summaryRow is not null
            ? GetLeadUpSummaryMitigationDebuffStatuses(summaryRow, out _)
            : GetSelectedMitigationDebuffStatuses(death, selection, leadUpEvents);
        var summaryMitigationDebuffStatusSources = summaryRow?.SourceStatusNames ??
            GetEventSourceMitigationStatusSourceNames(death, selection.Events, leadUpEvents);
        var selectedMitigationDebuffStatuses = GetSelectedMitigationDebuffStatuses(death, selection, leadUpEvents);

        return new ResolvedDeathDisplay(
            territoryId,
            death,
            selection,
            causeEvents,
            leadUpEvents,
            timelineRows,
            summaryRow,
            summaryMitigationDebuffStatuses,
            summaryMitigationDebuffStatusSources,
            selectedMitigationDebuffStatuses,
            pullReplayMarkers is { Count: > 0 } ? pullReplayMarkers : death.ReplayMarkers);
    }

    private void DrawCauseSummary(ResolvedDeathDisplay resolved)
    {
        var death = resolved.Death;
        ImGui.TextUnformatted("Player death information");
        using var sectionIndent = new ImGuiIndentScope(SectionBodyIndent);
        var causeEvents = resolved.CauseEvents;
        var summaryRow = resolved.SummaryRow;
        if (summaryRow is not null)
        {
            DrawLeadUpDeathSummary(death, summaryRow);
        }
        else if (causeEvents.Count > 0)
        {
            var cause = causeEvents[^1];
            var hpDisplay = GetEventHpDisplay(death, cause);
            ImGui.BulletText(cause.Kind == DeathEventKind.Status
                ? "HP + shields before fatal KO"
                : "HP + shields before fatal hit");
            if (hpDisplay.MaxHp > 0)
            {
                DrawHpShieldBar(
                    hpDisplay.CurrentHp,
                    hpDisplay.ShieldHp,
                    hpDisplay.MaxHp,
                    $"CauseHp{death.MemberKey}{death.SeenAtUtc.Ticks}",
                    GetIncomingDamageAmount(cause),
                    true,
                    tooltipDetail: hpDisplay.TooltipDetail);
            }
            else
            {
                ImGui.TextColored(WarningColor, "No HP sample was captured before the fatal hit.");
            }
        }
        else
        {
            ImGui.BulletText("HP + shields before fatal hit");
            ImGui.TextColored(
                WarningColor,
                IsEnvironmentSourceDeath(death)
                    ? "The death packet was environmental; no boss/player fatal hit was assigned."
                    : "No fatal hit was captured inside the configured event window.");
        }

        DrawDmuP4AssignmentSummary(resolved);
        DrawEnvironmentalDeathContext(death);

        var buttonId = $"{death.MemberKey}{death.SeenAtUtc.Ticks}";
        const string postInformationLabel = "Post information to chat";
        var channelButtonMax = DrawDeathChatChannelCombo(buttonId);
        var contentRight = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        var postInformationWidth = GetThemedActionButtonWidth(postInformationLabel);
        var canFitPostInformationInline = channelButtonMax.X + ImGui.GetStyle().ItemSpacing.X + postInformationWidth <= contentRight;
        if (canFitPostInformationInline)
        {
            ImGui.SameLine();
        }

        if (DrawThemedActionButton(
                postInformationLabel,
                $"PostInfo{buttonId}",
                canFitPostInformationInline ? postInformationWidth : MathF.Min(postInformationWidth, ImGui.GetContentRegionAvail().X)) &&
            ImGui.GetIO().KeyCtrl)
        {
            plugin.PrintDeathInformationToChat(death);
        }
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Ctrl+click to post. Other Better Deaths users present will see a link to this pull.");
        }

        DrawFatalEventRow(death, causeEvents);
        ImGui.Separator();
        DrawStatusSnapshot(
            resolved.SummaryMitigationDebuffStatuses,
            $"{death.MemberKey}{death.SeenAtUtc.Ticks}SummaryAtDeath",
            resolved.SummaryMitigationDebuffStatusSources);
        ImGui.Separator();
        DrawEarlierBossDebuffsNotOnFatalHit(resolved, $"{death.MemberKey}{death.SeenAtUtc.Ticks}Summary");
    }

    private void DrawEnvironmentalDeathContext(PartyDeathRecord death)
    {
        if (death.EnvironmentalAssessment is not { Confidence: > 0.0f } assessment)
        {
            return;
        }

        ImGui.Spacing();
        DrawLeadUpLabel("Environmental context");
        var assessmentText = assessment.EnvironmentSourceDeath
            ? "Walled"
            : $"{assessment.Summary} - {FormatEnvironmentalConfidence(assessment.Confidence)}";
        ImGui.TextColored(
            GetEnvironmentalDeathContextColor(assessment.Confidence),
            assessmentText);
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(assessment.EnvironmentSourceDeath
                ? "The death packet reported environment/no actor as the source. Better Deaths treats death walls and jump-offs as walling."
                : "Confidence-based only. Better Deaths does not have exact arena bounds for every fight.");
        }

        if (assessment.EnvironmentSourceDeath)
        {
            return;
        }

        foreach (var evidence in assessment.Evidence.Take(3))
        {
            DrawMutedWrappedText($"- {evidence}");
        }
    }

    private static Vector4 GetEnvironmentalDeathContextColor(float confidence)
    {
        return confidence >= 0.75f
            ? WarningColor
            : confidence >= 0.55f
                ? LeadUpGoldColor
                : ModernMutedTextColor;
    }

    private static string FormatEnvironmentalConfidence(float confidence)
    {
        return $"{(Math.Clamp(confidence, 0.0f, 1.0f) * 100.0f).ToString("0", CultureInfo.InvariantCulture)}%";
    }

    private void DrawDmuP4AssignmentSummary(ResolvedDeathDisplay resolved)
    {
        var assignments = GetDmuP4AssignmentSummaryStatuses(resolved);
        if (assignments.Count == 0)
        {
            return;
        }

        ImGui.Spacing();
        DrawLeadUpLabel("P4 Grand Cross Debuffs");

        var availableWidth = MathF.Max(1.0f, ImGui.GetContentRegionAvail().X);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var rowWidth = 0.0f;
        foreach (var assignment in assignments)
        {
            var itemWidth = GetDmuP4AssignmentDisplayWidth(assignment);
            if (rowWidth > 0.0f && rowWidth + spacing + itemWidth > availableWidth)
            {
                rowWidth = 0.0f;
            }
            else if (rowWidth > 0.0f)
            {
                ImGui.SameLine(0.0f, spacing);
                rowWidth += spacing;
            }

            DrawDmuP4AssignmentDisplay(assignment);
            rowWidth += itemWidth;
        }
    }

    private IReadOnlyList<DmuP4AssignmentSummaryStatus> GetDmuP4AssignmentSummaryStatuses(ResolvedDeathDisplay resolved)
    {
        var death = resolved.Death;
        var statusCandidates = resolved.Death.StatusesAtDeath
            .Concat(resolved.Selection.Snapshot?.Statuses ?? [])
            .Concat(resolved.SummaryRow?.HpSnapshot.Statuses ?? [])
            .Concat(resolved.CauseEvents.SelectMany(combatEvent => combatEvent.Statuses))
            .Where(status => ReplayEncounterModules.IsDmuP4AssignmentMarker(status.Id))
            .GroupBy(status => status.Id)
            .Select(group => group
                .OrderBy(status => status.RemainingTime <= 0.0f ? float.MaxValue : status.RemainingTime)
                .ThenBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(GetDmuP4AssignmentSortKey)
            .ThenBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (statusCandidates.Count == 0)
        {
            return [];
        }

        var markers = resolved.PullReplayMarkers;
        var assignmentMarkers = markers
            .Where(marker => marker.ActorKind == ReplayActorKind.Player &&
                ReplayEncounterModules.IsDmuP4AssignmentMarker(marker.MarkerId) &&
                ReplayMarkerMatchesDeathPlayer(death, marker))
            .ToList();
        return statusCandidates
            .Select(status => CreateDmuP4AssignmentSummaryStatus(death, status, assignmentMarkers, markers))
            .ToList();
    }

    private static DmuP4AssignmentSummaryStatus CreateDmuP4AssignmentSummaryStatus(
        PartyDeathRecord death,
        StatusSnapshot status,
        IReadOnlyList<ReplayMarkerSnapshot> assignmentMarkers,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers)
    {
        var marker = assignmentMarkers
            .Where(candidate => candidate.MarkerId == status.Id)
            .OrderBy(candidate => Math.Abs((candidate.SeenAtUtc - death.SeenAtUtc).TotalSeconds))
            .FirstOrDefault();
        if (marker is not null &&
            TryFindNearestDmuP4RealityTell(marker, allMarkers, out var realityLabel, out var isReal) &&
            ReplayEncounterModules.TryGetDmuP4StatusResolution(status.Id, isReal, out var resolution))
        {
            return new DmuP4AssignmentSummaryStatus(status, realityLabel, isReal, resolution);
        }

        return new DmuP4AssignmentSummaryStatus(status, null, null, null);
    }

    private static bool ReplayMarkerMatchesDeathPlayer(PartyDeathRecord death, ReplayMarkerSnapshot marker)
    {
        return string.Equals(marker.ActorKey, $"player:{death.MemberKey}", StringComparison.Ordinal) ||
            marker.PartyIndex == death.PartyIndex &&
            string.Equals(marker.ActorName, death.MemberName, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetDmuP4AssignmentSortKey(StatusSnapshot status)
    {
        return status.Id switch
        {
            5545 => 0,
            5544 => 1,
            5543 => 2,
            5546 => 3,
            5547 => 4,
            5548 => 5,
            _ => 99,
        };
    }

    private float GetDmuP4AssignmentDisplayWidth(DmuP4AssignmentSummaryStatus assignment)
    {
        var iconSize = Math.Clamp(configuration.StatusIconSize, 12.0f, 32.0f);
        var timerText = FormatStatusDuration(assignment.Status, true, true, "-");
        var timerWidth = ImGui.CalcTextSize(timerText).X;
        var textWidth = GetDmuP4AssignmentRealityText(assignment) is { Length: > 0 } realityText
            ? ImGui.CalcTextSize(realityText).X
            : 0.0f;
        return MathF.Max(MathF.Max(iconSize, timerWidth), textWidth);
    }

    private void DrawDmuP4AssignmentDisplay(DmuP4AssignmentSummaryStatus assignment)
    {
        var status = assignment.Status;
        var iconSize = Math.Clamp(configuration.StatusIconSize, 12.0f, 32.0f);
        var timerText = FormatStatusDuration(status, true, true, "-");
        var isFake = assignment.IsReal == false;
        var borderColor = isFake
            ? OverkillColor
            : ModernPanelBorderColor;
        var tooltip = FormatDmuP4AssignmentTooltip(assignment);
        var realityText = GetDmuP4AssignmentRealityText(assignment);

        ImGui.BeginGroup();
        var stackStartX = ImGui.GetCursorPosX();
        var timerWidth = ImGui.CalcTextSize(timerText).X;
        var realityWidth = realityText.Length > 0
            ? ImGui.CalcTextSize(realityText).X
            : 0.0f;
        var stackWidth = MathF.Max(MathF.Max(iconSize, timerWidth), realityWidth);
        if (realityText.Length > 0)
        {
            ImGui.SetCursorPosX(stackStartX + MathF.Max(0.0f, (stackWidth - realityWidth) * 0.5f));
            ImGui.TextColored(isFake ? OverkillColor : LeadUpGoldColor, realityText);
        }

        ImGui.SetCursorPosX(stackStartX + MathF.Max(0.0f, (stackWidth - iconSize) * 0.5f));
        DrawStatusIconWithBorder(status.IconId, iconSize, borderColor, tooltip);
        ImGui.SetCursorPosX(stackStartX + MathF.Max(0.0f, (stackWidth - timerWidth) * 0.5f));
        ImGui.TextDisabled(timerText);

        ImGui.EndGroup();
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(tooltip);
        }
    }

    private static string GetDmuP4AssignmentRealityText(DmuP4AssignmentSummaryStatus assignment)
    {
        return !string.IsNullOrWhiteSpace(assignment.RealityLabel) &&
            !string.IsNullOrWhiteSpace(assignment.Resolution)
            ? $"{assignment.RealityLabel}: {assignment.Resolution}"
            : string.Empty;
    }

    private static string FormatDmuP4AssignmentTooltip(DmuP4AssignmentSummaryStatus assignment)
    {
        var status = assignment.Status;
        var timerText = FormatStatusDuration(status, true, true, "-");
        if (!string.IsNullOrWhiteSpace(assignment.RealityLabel) &&
            !string.IsNullOrWhiteSpace(assignment.Resolution))
        {
            return $"{status.Name}\nTimer: {timerText}\n{assignment.RealityLabel}: {assignment.Resolution}";
        }

        return $"{status.Name}\nTimer: {timerText}\nTell not captured.";
    }

    private static void DrawStatusIconWithBorder(uint iconId, float iconSize, Vector4 borderColor, string tooltip)
    {
        var size = new Vector2(Math.Clamp(iconSize, 12.0f, 48.0f));
        var start = ImGui.GetCursorScreenPos();
        var hovered = false;
        if (iconId == 0)
        {
            ImGui.Dummy(size);
            hovered = ImGui.IsItemHovered();
        }
        else
        {
            try
            {
                var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                var wrap = texture.GetWrapOrDefault();
                if (wrap is null)
                {
                    ImGui.Dummy(size);
                }
                else
                {
                    ImGui.Image(wrap.Handle, size);
                }
            }
            catch
            {
                ImGui.Dummy(size);
            }

            hovered = ImGui.IsItemHovered();
        }

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(
            start - new Vector2(1.0f),
            start + size + new Vector2(1.0f),
            ImGui.GetColorU32(borderColor),
            3.0f,
            ImDrawFlags.None,
            1.8f);

        if (hovered)
        {
            SetThemedTooltip(tooltip);
        }
    }

    private Vector2 DrawDeathChatChannelCombo(string id, float width = 185.0f)
    {
        var effectiveChannel = Plugin.GetEffectiveChatChannel(configuration.DeathChatChannel);
        var buttonWidth = MathF.Min(width, MathF.Max(92.0f, ImGui.GetContentRegionAvail().X));
        var popupId = $"DeathChatChannelPopup{id}";
        var popupWidth = GetDeathChatChannelPopupWidth(buttonWidth);
        var buttonLabel = Plugin.GetChatChannelLabel(effectiveChannel);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8.0f);
        ImGui.PushStyleColor(ImGuiCol.Button, GetChatChannelSelectorColor());
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, GetChatChannelSelectorHoveredColor());
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, GetChatChannelSelectorActiveColor());
        ImGui.PushStyleColor(ImGuiCol.Text, ModernTextColor);
        if (ImGui.Button($"{buttonLabel}##DeathChatChannel{id}", new Vector2(buttonWidth, ImGui.GetFrameHeight())))
        {
            ImGui.OpenPopup(popupId);
        }

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar();

        var buttonMin = ImGui.GetItemRectMin();
        var buttonMax = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddRect(
            buttonMin,
            buttonMax,
            ImGui.GetColorU32(GetChatChannelSelectorBorderColor()),
            8.0f,
            ImDrawFlags.None,
            1.0f);
        ImGui.SetNextWindowPos(new Vector2(buttonMin.X, buttonMax.Y + 2.0f), ImGuiCond.Appearing);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6.0f, 6.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4.0f, 4.0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, GetChatChannelPopupColor());
        ImGui.PushStyleColor(ImGuiCol.Border, GetChatChannelSelectorBorderColor());
        ImGui.PushStyleColor(ImGuiCol.Header, ModernHeaderColor);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ModernHeaderHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, ModernHeaderActiveColor);
        ImGui.PushStyleColor(ImGuiCol.Text, ModernTextColor);

        if (!ImGui.BeginPopup(popupId))
        {
            ImGui.PopStyleColor(6);
            ImGui.PopStyleVar(2);
            return buttonMax;
        }

        foreach (var option in Plugin.ChatChannelOptions)
        {
            var selected = effectiveChannel == option.Channel;
            var label = selected
                ? $"> {option.Label}"
                : $"  {option.Label}";
            if (ImGui.Selectable($"{label}##DeathChatChannel{id}{option.Channel}", selected, ImGuiSelectableFlags.None, new Vector2(popupWidth, 0.0f)))
            {
                plugin.SetDeathChatChannel(option.Channel);
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndPopup();
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(2);
        return buttonMax;
    }

    private void DrawDeathRecapLinkChannelSetting()
    {
        ImGui.Indent(ImGui.GetFrameHeightWithSpacing());
        const string label = "Death Link channel";
        var style = ImGui.GetStyle();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var labelWidth = ImGui.CalcTextSize(label).X;
        var canFitInline = availableWidth - labelWidth - style.ItemInnerSpacing.X >= 120.0f;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (canFitInline)
        {
            ImGui.SameLine();
        }

        DrawDeathRecapLinkChannelCombo("PostDeathRecapLink", 180.0f);
        ImGui.Unindent(ImGui.GetFrameHeightWithSpacing());

        DrawSettingsTooltip("Local-only. This does not post to party, say, linkshells, or other public chat.");
    }

    private Vector2 DrawDeathRecapLinkChannelCombo(string id, float width = 180.0f)
    {
        var effectiveChannel = Plugin.GetEffectiveDeathRecapLinkChannel(configuration.DeathRecapLinkChannel);
        var buttonWidth = MathF.Min(width, MathF.Max(92.0f, ImGui.GetContentRegionAvail().X));
        var popupId = $"DeathRecapLinkChannelPopup{id}";
        var popupWidth = GetDeathRecapLinkChannelPopupWidth(buttonWidth);
        var buttonLabel = Plugin.GetDeathRecapLinkChannelLabel(effectiveChannel);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8.0f);
        ImGui.PushStyleColor(ImGuiCol.Button, GetChatChannelSelectorColor());
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, GetChatChannelSelectorHoveredColor());
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, GetChatChannelSelectorActiveColor());
        ImGui.PushStyleColor(ImGuiCol.Text, ModernTextColor);
        if (ImGui.Button($"{buttonLabel}##DeathRecapLinkChannel{id}", new Vector2(buttonWidth, ImGui.GetFrameHeight())))
        {
            ImGui.OpenPopup(popupId);
        }

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar();

        var buttonMin = ImGui.GetItemRectMin();
        var buttonMax = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddRect(
            buttonMin,
            buttonMax,
            ImGui.GetColorU32(GetChatChannelSelectorBorderColor()),
            8.0f,
            ImDrawFlags.None,
            1.0f);
        ImGui.SetNextWindowPos(new Vector2(buttonMin.X, buttonMax.Y + 2.0f), ImGuiCond.Appearing);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6.0f, 6.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4.0f, 4.0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, GetChatChannelPopupColor());
        ImGui.PushStyleColor(ImGuiCol.Border, GetChatChannelSelectorBorderColor());
        ImGui.PushStyleColor(ImGuiCol.Header, ModernHeaderColor);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ModernHeaderHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, ModernHeaderActiveColor);
        ImGui.PushStyleColor(ImGuiCol.Text, ModernTextColor);

        if (!ImGui.BeginPopup(popupId))
        {
            ImGui.PopStyleColor(6);
            ImGui.PopStyleVar(2);
            return buttonMax;
        }

        foreach (var option in Plugin.DeathRecapLinkChannelOptions)
        {
            var selected = effectiveChannel == option.Channel;
            var label = selected
                ? $"> {option.Label}"
                : $"  {option.Label}";
            if (ImGui.Selectable($"{label}##DeathRecapLinkChannel{id}{option.Channel}", selected, ImGuiSelectableFlags.None, new Vector2(popupWidth, 0.0f)))
            {
                plugin.SetDeathRecapLinkChannel(option.Channel);
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndPopup();
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(2);
        return buttonMax;
    }

    private static float GetDeathChatChannelPopupWidth(float minimumWidth)
    {
        var style = ImGui.GetStyle();
        var optionWidth = Plugin.ChatChannelOptions
            .Select(option => ImGui.CalcTextSize($"> {option.Label}").X)
            .DefaultIfEmpty(0.0f)
            .Max();
        return MathF.Max(minimumWidth, optionWidth + (style.FramePadding.X * 2.0f) + 8.0f);
    }

    private static float GetDeathRecapLinkChannelPopupWidth(float minimumWidth)
    {
        var style = ImGui.GetStyle();
        var optionWidth = Plugin.DeathRecapLinkChannelOptions
            .Select(option => ImGui.CalcTextSize($"> {option.Label}").X)
            .DefaultIfEmpty(0.0f)
            .Max();
        return MathF.Max(minimumWidth, optionWidth + (style.FramePadding.X * 2.0f) + 8.0f);
    }

    private static Vector4 GetChatChannelSelectorColor()
    {
        return ActiveThemeUsesLightPanels()
            ? BlendColors(ModernPanelAltColor, ModernAccentSoftColor, 0.22f) with { W = 1.0f }
            : BlendColors(ModernFrameColor, ModernAccentSoftColor, 0.36f) with { W = 0.96f };
    }

    private static Vector4 GetChatChannelSelectorHoveredColor()
    {
        return ActiveThemeUsesLightPanels()
            ? BlendColors(ModernPanelAltColor, ModernAccentColor, 0.18f) with { W = 1.0f }
            : BlendColors(ModernFrameHoveredColor, ModernAccentSoftColor, 0.48f) with { W = 1.0f };
    }

    private static Vector4 GetChatChannelSelectorActiveColor()
    {
        return ActiveThemeUsesLightPanels()
            ? BlendColors(ModernAccentSoftColor, ModernPanelBorderColor, 0.20f) with { W = 1.0f }
            : ModernAccentSoftColor with { W = 1.0f };
    }

    private static Vector4 GetChatChannelSelectorBorderColor()
    {
        return ActiveThemeUsesLightPanels()
            ? BlendColors(ModernPanelBorderColor, ModernAccentColor, 0.45f) with { W = 1.0f }
            : BlendColors(ModernPanelBorderColor, ModernAccentColor, 0.55f) with { W = 0.95f };
    }

    private static Vector4 GetChatChannelPopupColor()
    {
        return ActiveThemeUsesLightPanels()
            ? BlendColors(ModernPopupBgColor, ModernPanelAltColor, 0.22f) with { W = 1.0f }
            : BlendColors(ModernPopupBgColor, ModernAccentSoftColor, 0.18f) with { W = 0.98f };
    }

    private void DrawFatalEventRow(PartyDeathRecord death, IReadOnlyList<CombatEventRecord> causeEvents)
    {
        ImGui.Separator();
        var enemyHpAtDeath = GetEnemyHpAtDeathForDisplay(death);
        if (enemyHpAtDeath.Count == 0)
        {
            DrawFatalEventHeaderAndDetails(death, causeEvents);
            return;
        }

        if (IsFocusedReviewMode())
        {
            DrawFocusedFatalEventColumns(death, causeEvents, enemyHpAtDeath);
            return;
        }

        if (!ImGui.BeginTable($"##FatalEventHealthsAtDeath{death.MemberKey}{death.SeenAtUtc.Ticks}", 2, ImGuiTableFlags.SizingStretchProp))
        {
            DrawFatalEventHeaderAndDetails(death, causeEvents);
            DrawEnemyHpAtDeathHeaderAndBullets(enemyHpAtDeath);
            return;
        }

        ImGui.TableSetupColumn("Fatal event", ImGuiTableColumnFlags.WidthStretch, 1.35f);
        ImGui.TableSetupColumn("Enemy HP at death", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawFatalEventHeaderAndDetails(death, causeEvents);
        ImGui.TableNextColumn();
        DrawEnemyHpAtDeathHeaderAndBullets(enemyHpAtDeath);
        ImGui.EndTable();
    }

    private static void DrawTimelineKofiLink(string idPrefix)
    {
        const string label = "Ko-fi";
        var textSize = ImGui.CalcTextSize(label);
        var style = ImGui.GetStyle();
        var currentPosition = ImGui.GetCursorScreenPos();
        var contentMax = ImGui.GetWindowPos() + ImGui.GetWindowContentRegionMax();
        var linkPosition = new Vector2(
            MathF.Max(currentPosition.X, contentMax.X - textSize.X - style.ItemSpacing.X),
            MathF.Max(currentPosition.Y + style.ItemSpacing.Y, contentMax.Y - textSize.Y - ReviewPaneBottomPadding));

        ImGui.SetCursorScreenPos(linkPosition);
        if (DrawInlineTextLink(label, $"TimelineKofi{idPrefix}"))
        {
            ImGui.OpenPopup(KofiConfirmPopupId);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Support Better Deaths on Ko-fi.");
        }
    }

    private static bool DrawInlineTextLink(string label, string id)
    {
        var textSize = ImGui.CalcTextSize(label);
        var position = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##{id}", new Vector2(textSize.X, MathF.Max(textSize.Y, ImGui.GetTextLineHeight())));
        var hovered = ImGui.IsItemHovered();
        var color = hovered
            ? BlendColors(LeadUpGoldColor, ModernTextColor, 0.22f)
            : LeadUpGoldColor;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(position, ImGui.GetColorU32(color), label);
        if (hovered)
        {
            var underlineY = position.Y + textSize.Y + 1.0f;
            drawList.AddLine(
                new Vector2(position.X, underlineY),
                new Vector2(position.X + textSize.X, underlineY),
                ImGui.GetColorU32(color with { W = 0.78f }),
                1.0f);
        }

        return clicked;
    }

    private void DrawFocusedFatalEventColumns(
        PartyDeathRecord death,
        IReadOnlyList<CombatEventRecord> causeEvents,
        IReadOnlyList<EnemyHpSnapshot> enemyHpAtDeath)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X * 2.0f;
        if (availableWidth < 420.0f)
        {
            DrawFatalEventHeaderAndDetails(death, causeEvents);
            ImGui.Spacing();
            DrawEnemyHpAtDeathHeaderAndBullets(enemyHpAtDeath);
            return;
        }

        var leftWidth = MathF.Max(160.0f, (availableWidth - spacing) * 0.58f);
        var rightWidth = MathF.Max(140.0f, availableWidth - leftWidth - spacing);
        var rowStart = ImGui.GetCursorScreenPos();
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(rowStart.X + leftWidth);
        DrawFatalEventHeaderAndDetails(death, causeEvents);
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var leftEndY = ImGui.GetCursorScreenPos().Y;

        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + leftWidth + spacing, rowStart.Y));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(rowStart.X + leftWidth + spacing + rightWidth);
        DrawEnemyHpAtDeathHeaderAndBullets(enemyHpAtDeath);
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var rightEndY = ImGui.GetCursorScreenPos().Y;
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X, MathF.Max(leftEndY, rightEndY)));
    }

    private void DrawFatalEventHeaderAndDetails(
        PartyDeathRecord death,
        IReadOnlyList<CombatEventRecord> causeEvents)
    {
        ImGui.TextUnformatted(causeEvents.Count > 1 ? "Fatal events" : "Fatal event");
        if (causeEvents.Count == 0)
        {
            ImGui.TextColored(
                WarningColor,
                IsEnvironmentSourceDeath(death)
                    ? "Walled."
                    : "Non-hit KO. Possible death wall, reconnect spawn KO, or scripted KO.");
            return;
        }

        DrawFatalEventDetails(causeEvents);
    }

    private void DrawFatalEventDetails(IReadOnlyList<CombatEventRecord> causeEvents)
    {
        for (var i = 0; i < causeEvents.Count; i++)
        {
            var cause = causeEvents[i];
            if (causeEvents.Count > 1)
            {
                ImGui.BulletText($"Fatal event {i + 1}/{causeEvents.Count}");
                ImGui.Indent();
            }

            if (cause.Kind == DeathEventKind.Status)
            {
                DrawActionBullet(cause);
                ImGui.BulletText($"Source: {FormatKnownPlayerName(cause.SourceName)}");
                ImGui.BulletText(cause.Detail);
            }
            else
            {
                ImGui.Bullet();
                ImGui.SameLine();
                DrawCombatEventLine(cause);
            }

            if (causeEvents.Count > 1)
            {
                ImGui.Unindent();
            }
        }
    }

    private void DrawLeadUpDeathSummary(PartyDeathRecord death, LeadUpSummaryRow summary)
    {
        var row = summary.Row;
        ImGui.BulletText("HP + shields");

        if (!ImGui.BeginTable($"##LeadUpDeathSummary{death.MemberKey}{death.SeenAtUtc.Ticks}", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("HP + shields", ImGuiTableColumnFlags.WidthStretch, 1.05f);
        ImGui.TableSetupColumn("Captured hits/events", ImGuiTableColumnFlags.WidthStretch, 1.45f);
        ImGui.TableSetupColumn("Captured mitigations/debuffs", ImGuiTableColumnFlags.WidthStretch, 1.7f);
        DrawCenteredTableHeader("HP + shields", "Captured hits/events", "Captured mitigations/debuffs");

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var hpSnapshot = summary.HpSnapshot;
        DrawHpShieldBar(
            hpSnapshot.CurrentHp,
            hpSnapshot.ShieldHp,
            hpSnapshot.MaxHp,
            $"LeadUpSummaryHp{death.MemberKey}{death.SeenAtUtc.Ticks}{hpSnapshot.SeenAtUtc.Ticks}",
            GetIncomingDamageAmount(summary.Events),
            true);
        ImGui.TableNextColumn();
        DrawEventSummaryCell(summary.Events, int.MaxValue);
        ImGui.TableNextColumn();
        DrawLeadUpSummaryMitigationDebuffCell(summary);

        ImGui.EndTable();
    }

    private void DrawJobCell(PartyDeathRecord death)
    {
        var iconId = GetClassJobIconId(death.ClassJobId);
        var textWidth = ImGui.CalcTextSize(death.ClassJobName).X;
        if (iconId != 0)
        {
            var iconSize = Math.Clamp(configuration.ActionIconSize, 12.0f, 48.0f);
            var groupWidth = iconSize + ImGui.GetStyle().ItemSpacing.X + textWidth;
            CenterNextItem(groupWidth);
            var iconTop = ImGui.GetCursorPosY();
            DrawGameIcon(iconId, configuration.ActionIconSize, death.ClassJobName);
            ImGui.SameLine();
            var textOffset = MathF.Max(0.0f, (iconSize - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.SetCursorPosY(iconTop + textOffset);
            ImGui.TextUnformatted(death.ClassJobName);
            return;
        }

        DrawCenteredText(death.ClassJobName);
    }

    private LeadUpSummaryRow? GetLeadUpSummaryRow(
        PartyDeathRecord death,
        DeathDisplaySelection selection,
        IReadOnlyList<CombatEventRecord> leadUpEvents,
        IReadOnlyList<LeadUpTimelineRow> timelineRows)
    {
        var anchorSeenAtUtc = selection.AnchorSeenAtUtc;
        if (selection.Snapshot is { } snapshot)
        {
            var exactSnapshotRow = timelineRows.LastOrDefault(row =>
                row.Event is null &&
                row.SeenAtUtc == snapshot.SeenAtUtc);
            if (exactSnapshotRow is not null)
            {
                IReadOnlyList<CombatEventRecord> events = selection.Events.Count > 0 ? selection.Events : [];
                return CreateLeadUpSummaryRowFromTimelineRow(
                    death,
                    anchorSeenAtUtc,
                    exactSnapshotRow,
                    events,
                    leadUpEvents,
                    [exactSnapshotRow],
                    snapshot);
            }
        }

        var matchedFatalRows = GetTimelineRowsForFatalEvents(selection, timelineRows);
        if (matchedFatalRows.Count > 0)
        {
            IReadOnlyList<CombatEventRecord> events = selection.Events.Count > 0
                ? selection.Events
                : matchedFatalRows
                    .Select(row => row.Event)
                    .OfType<CombatEventRecord>()
                    .ToList();
            return CreateLeadUpSummaryRowFromTimelineRow(
                death,
                anchorSeenAtUtc,
                matchedFatalRows[0],
                events,
                leadUpEvents,
                matchedFatalRows,
                selection.Snapshot);
        }

        return GetLeadUpSummaryRowFallback(death, selection, leadUpEvents);
    }

    private LeadUpSummaryRow? GetLeadUpSummaryRowFallback(
        PartyDeathRecord death,
        DeathDisplaySelection selection,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        var anchorSeenAtUtc = selection.AnchorSeenAtUtc;
        var rows = GetLeadUpHpHistoryRows(death, anchorSeenAtUtc, leadUpEvents);
        if (rows.Count == 0 || selection.Snapshot is null)
        {
            return null;
        }

        var row = rows.LastOrDefault(row => row.LastSnapshot.SeenAtUtc == selection.Snapshot.SeenAtUtc);
        if (row is null)
        {
            return null;
        }

        var events = selection.Events.Count > 0 ? selection.Events : row.Events;
        var sourceStatuses = events.Count > 0
            ? events.SelectMany(combatEvent => GetEventSourceMitigationStatuses(death, combatEvent, leadUpEvents)).ToList()
            : GetActiveSourceMitigationStatuses(death, row.LastSnapshot.SeenAtUtc, null, leadUpEvents);
        var sourceStatusNames = events.Count > 0
            ? GetEventSourceMitigationStatusSourceNames(death, events, leadUpEvents)
            : GetActiveSourceMitigationStatusSourceNames(death, row.LastSnapshot.SeenAtUtc, null, leadUpEvents);
        return new LeadUpSummaryRow(anchorSeenAtUtc, row, selection.Snapshot, events, sourceStatuses, sourceStatusNames);
    }

    private LeadUpSummaryRow CreateLeadUpSummaryRowFromTimelineRow(
        PartyDeathRecord death,
        DateTime anchorSeenAtUtc,
        LeadUpTimelineRow timelineRow,
        IReadOnlyList<CombatEventRecord> events,
        IReadOnlyList<CombatEventRecord> leadUpEvents,
        IReadOnlyList<LeadUpTimelineRow> matchedRows,
        HpHistorySnapshot? summarySnapshot = null)
    {
        var statuses = timelineRow.Statuses
            .Concat(timelineRow.NearbyHpStatuses)
            .ToList();
        var snapshot = new HpHistorySnapshot(
            timelineRow.SeenAtUtc,
            timelineRow.PullElapsedSeconds,
            timelineRow.CurrentHp,
            timelineRow.ShieldHp,
            timelineRow.MaxHp,
            statuses);
        IReadOnlyList<CombatEventRecord> rowEvents = timelineRow.Event is null ? [] : [timelineRow.Event];
        var row = new HpHistoryDisplayRow(snapshot, snapshot, rowEvents, 1);
        var matchedSourceStatuses = matchedRows
            .SelectMany(row => row.SourceStatuses)
            .ToList();
        var matchedSourceStatusNames = MergeBossStatusSourceNames(matchedRows.Select(row => row.SourceStatusNames));
        var sourceStatuses = matchedSourceStatuses.Count > 0
            ? matchedSourceStatuses
            : events.Count > 0
                ? events.SelectMany(combatEvent => GetEventSourceMitigationStatuses(death, combatEvent, leadUpEvents)).ToList()
                : GetActiveSourceMitigationStatuses(death, timelineRow.SeenAtUtc, null, leadUpEvents);
        var sourceStatusNames = matchedSourceStatuses.Count > 0
            ? matchedSourceStatusNames
            : events.Count > 0
                ? GetEventSourceMitigationStatusSourceNames(death, events, leadUpEvents)
                : GetActiveSourceMitigationStatusSourceNames(death, timelineRow.SeenAtUtc, null, leadUpEvents);
        return new LeadUpSummaryRow(anchorSeenAtUtc, row, summarySnapshot ?? snapshot, events, sourceStatuses, sourceStatusNames);
    }

    private static IReadOnlyList<LeadUpTimelineRow> GetTimelineRowsForFatalEvents(
        DeathDisplaySelection selection,
        IReadOnlyList<LeadUpTimelineRow> timelineRows)
    {
        var rawFatalEvents = selection.FatalEvents
            .SelectMany(group => group.Events)
            .Where(IsFatalEvent)
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ThenBy(combatEvent => combatEvent.EventOrdinal)
            .ToList();
        if (rawFatalEvents.Count == 0)
        {
            return [];
        }

        foreach (var fatalEvent in rawFatalEvents)
        {
            var matchingRows = timelineRows
                .Where(row => row.Event is not null && CombatEventsMatchForSummary(row.Event, fatalEvent))
                .OrderBy(row => row.SeenAtUtc)
                .ThenBy(row => row.Event?.EventOrdinal ?? 0)
                .ToList();
            if (matchingRows.Count > 0)
            {
                return matchingRows;
            }
        }

        return [];
    }

    private static bool CombatEventsMatchForSummary(CombatEventRecord left, CombatEventRecord right)
    {
        if (!string.IsNullOrWhiteSpace(left.EventIdentity) &&
            !string.IsNullOrWhiteSpace(right.EventIdentity))
        {
            return string.Equals(left.EventIdentity, right.EventIdentity, StringComparison.Ordinal);
        }

        return left.SeenAtUtc == right.SeenAtUtc &&
            left.MemberKey == right.MemberKey &&
            left.SourceEntityId == right.SourceEntityId &&
            left.ActionId == right.ActionId &&
            left.Kind == right.Kind &&
            left.Amount == right.Amount;
    }

    private void DrawExtraMitigationContext(ResolvedDeathDisplay resolved, string idSuffix)
    {
        ImGui.TextUnformatted("Extra mitigation context");
        using var sectionIndent = new ImGuiIndentScope(SectionBodyIndent);
        DrawStatusSnapshot(
            resolved.SummaryMitigationDebuffStatuses,
            $"{idSuffix}AtDeath",
            resolved.SummaryMitigationDebuffStatusSources);
        ImGui.Separator();
        DrawEarlierBossDebuffsNotOnFatalHit(resolved, idSuffix);
    }

    private void DrawPossibleMitigationContext(ResolvedDeathDisplay resolved, string idSuffix)
    {
        var death = resolved.Death;
        ImGui.TextUnformatted("Mitigation what-if");
        using var sectionIndent = new ImGuiIndentScope(SectionBodyIndent);
        var damageEvents = resolved.CauseEvents
            .Where(combatEvent => combatEvent.Kind == DeathEventKind.Damage && combatEvent.Amount > 0)
            .ToList();
        var observedDamage = GetIncomingDamageAmount(damageEvents);
        var hpDisplay = GetWhatIfHpDisplay(resolved, damageEvents);
        var activeStatuses = resolved.SelectedMitigationDebuffStatuses;
        var options = GetPossibleMitigationOptions(death, activeStatuses).ToList();
        var selectedOptions = options
            .Where(option => selectedPossibleMitigationKeys.Contains(BuildPossibleMitigationSelectionKey(idSuffix, option)))
            .ToList();

        DrawLeadUpLabel("Current outcome");
        if (observedDamage is null)
        {
            ImGui.TextColored(WarningColor, "No captured damage amount is available for this selected death.");
        }
        else
        {
            ImGui.BulletText($"Captured damage: {observedDamage.Value:N0}");
            if (hpDisplay.MaxHp > 0)
            {
                ImGui.BulletText($"HP + shields before hit: {(ulong)hpDisplay.CurrentHp + hpDisplay.ShieldHp:N0}");
            }
        }

        DrawMitigationTotal(activeStatuses);
        ImGui.Separator();

        DrawLeadUpLabelWithInlineMutedText("Available mitigation", "Shows all available abilities at the time.");
        DrawWhatIfChatControls(
            death,
            idSuffix,
            options,
            selectedOptions,
            damageEvents,
            observedDamage,
            hpDisplay);
        if (death.PossibleMitigations.Count == 0)
        {
            ImGui.TextDisabled("No available mitigation data was captured for this death.");
        }
        else if (options.Count == 0)
        {
            ImGui.TextDisabled("No tracked extra damage reductions looked available for this death.");
        }
        else
        {
            DrawPossibleMitigationOptionsTable(options, idSuffix);
        }

        ImGui.Separator();
        DrawPossibleMitigationResult(
            damageEvents,
            observedDamage,
            hpDisplay,
            activeStatuses,
            selectedOptions,
            options.Count > 0);
    }

    private void DrawWhatIfChatControls(
        PartyDeathRecord death,
        string idSuffix,
        IReadOnlyList<PossibleMitigationSnapshot> options,
        IReadOnlyList<PossibleMitigationSnapshot> selectedOptions,
        IReadOnlyList<CombatEventRecord> damageEvents,
        ulong? observedDamage,
        EventHpDisplay hpDisplay)
    {
        var controlId = $"WhatIf{idSuffix}";
        var comboWidth = MathF.Min(185.0f, MathF.Max(120.0f, ImGui.GetContentRegionAvail().X));
        DrawDeathChatChannelCombo(controlId, comboWidth);
        ImGui.Spacing();

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var availableButtonWidth = MathF.Min(GetThemedActionButtonWidth("Send available mits"), availableWidth);
        var outcomeButtonWidth = MathF.Min(GetThemedActionButtonWidth("Send outcome"), availableWidth);
        var canFitTwoButtons = availableWidth >= availableButtonWidth + outcomeButtonWidth + spacing;

        ImGui.BeginDisabled(options.Count == 0);
        if (DrawThemedActionButton("Send available mits", $"WhatIfAvailableMits{controlId}", canFitTwoButtons ? availableButtonWidth : availableWidth) &&
            ImGui.GetIO().KeyCtrl)
        {
            QueueWhatIfMitigationChat("Available mits", options);
        }
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Ctrl+click to send available mits to chat.");
        }

        ImGui.EndDisabled();

        if (canFitTwoButtons)
        {
            ImGui.SameLine();
        }

        ImGui.BeginDisabled(selectedOptions.Count == 0 || observedDamage is null || damageEvents.Count == 0);
        if (DrawThemedActionButton("Send outcome", $"WhatIfSelectedMits{controlId}", canFitTwoButtons ? outcomeButtonWidth : availableWidth) &&
            ImGui.GetIO().KeyCtrl)
        {
            QueueWhatIfSelectedMitigationOutcomeChat(
                selectedOptions,
                damageEvents,
                observedDamage,
                hpDisplay);
        }
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Ctrl+click to send the selected outcome to chat.");
        }

        ImGui.EndDisabled();
        ImGui.Spacing();
    }

    private void QueueWhatIfMitigationChat(
        string label,
        IReadOnlyList<PossibleMitigationSnapshot> options)
    {
        plugin.QueueBetterDeathsChatMessage(
            $"{label}: {FormatPossibleMitigationChatOptions(options)}.");
    }

    private void QueueWhatIfSelectedMitigationOutcomeChat(
        IReadOnlyList<PossibleMitigationSnapshot> selectedOptions,
        IReadOnlyList<CombatEventRecord> damageEvents,
        ulong? observedDamage,
        EventHpDisplay hpDisplay)
    {
        plugin.QueueBetterDeathsChatMessage(
            $"Outcome with selected mits: {FormatPossibleMitigationChatOptions(selectedOptions)}.");

        if (BuildWhatIfOutcomeChatLine(damageEvents, observedDamage, hpDisplay, selectedOptions) is { } outcomeLine)
        {
            plugin.QueuePlainChatMessage(outcomeLine);
        }
    }

    private string FormatPossibleMitigationChatOptions(IReadOnlyList<PossibleMitigationSnapshot> options)
    {
        return options.Count == 0
            ? "none"
            : string.Join("; ", options.Select(FormatPossibleMitigationChatOption));
    }

    private string FormatPossibleMitigationChatOption(PossibleMitigationSnapshot option)
    {
        return $"{option.ActionName} ({option.ClassJobName})";
    }

    private static string? BuildWhatIfOutcomeChatLine(
        IReadOnlyList<CombatEventRecord> damageEvents,
        ulong? observedDamage,
        EventHpDisplay hpDisplay,
        IReadOnlyList<PossibleMitigationSnapshot> selectedOptions)
    {
        if (observedDamage is null || damageEvents.Count == 0 || selectedOptions.Count == 0)
        {
            return null;
        }

        var selectedStatuses = selectedOptions
            .SelectMany(option => option.Statuses)
            .ToList();
        var potentialDamage = CalculateDamageWithAdditionalMitigation(damageEvents, selectedStatuses);
        var preventedDamage = observedDamage.Value > potentialDamage
            ? observedDamage.Value - potentialDamage
            : 0UL;
        var resultText = "HP before hit was not captured.";
        if (hpDisplay.MaxHp > 0)
        {
            var effectiveHp = (ulong)hpDisplay.CurrentHp + hpDisplay.ShieldHp;
            resultText = potentialDamage < effectiveHp
                ? $"survives by {effectiveHp - potentialDamage:N0}"
                : potentialDamage == effectiveHp
                    ? "exactly lethal"
                    : $"still short by {potentialDamage - effectiveHp:N0}";
        }

        return $"{observedDamage.Value:N0} -> {potentialDamage:N0}; reduced by {preventedDamage:N0}; {resultText}.";
    }

    private EventHpDisplay GetWhatIfHpDisplay(ResolvedDeathDisplay resolved, IReadOnlyList<CombatEventRecord> damageEvents)
    {
        var death = resolved.Death;
        if (resolved.SummaryRow is not null && resolved.SummaryRow.Row.LastSnapshot.MaxHp > 0)
        {
            return new EventHpDisplay(
                resolved.SummaryRow.Row.LastSnapshot.CurrentHp,
                resolved.SummaryRow.Row.LastSnapshot.ShieldHp,
                resolved.SummaryRow.Row.LastSnapshot.MaxHp,
                "HP from the selected death lead-up row.");
        }

        var eventForHp = damageEvents.LastOrDefault() ?? resolved.CauseEvents.LastOrDefault();
        if (eventForHp is not null)
        {
            return GetEventHpDisplay(death, eventForHp);
        }

        return new EventHpDisplay(
            death.CurrentHp,
            death.ShieldHp,
            death.MaxHp,
            "HP from the captured death record.");
    }

    private IReadOnlyList<PossibleMitigationSnapshot> GetPossibleMitigationOptions(
        PartyDeathRecord death,
        IReadOnlyList<StatusSnapshot> activeStatuses)
    {
        return death.PossibleMitigations
            .Where(option => option.Statuses.Any(HasCalculableMitigationPercent))
            .Where(option => !option.Statuses.Any(status => StatusAlreadyActive(status, activeStatuses)))
            .OrderBy(option => GetPossibleMitigationRoleOrder(option.ClassJobId))
            .ThenBy(option => GetPossibleMitigationClassOrder(option.ClassJobId))
            .ThenBy(option => option.PartyIndex)
            .ThenBy(option => option.MemberName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => GetPossibleMitigationScopeOrder(option.Scope))
            .ThenBy(option => option.ActionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void DrawPossibleMitigationOptionsTable(IReadOnlyList<PossibleMitigationSnapshot> options, string idSuffix)
    {
        var focused = IsFocusedReviewMode();
        if (focused)
        {
            DrawFocusedPossibleMitigationOptionsList(options, idSuffix);
            return;
        }

        if (!ImGui.BeginTable($"##PossibleMitigationOptions{idSuffix}", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Ability", ImGuiTableColumnFlags.WidthStretch, 1.85f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 2.25f);
        ImGui.TableSetupColumn("Mit%", ImGuiTableColumnFlags.WidthStretch, 1.9f);
        DrawCenteredTableHeader("Ability", "Source", "Mit%");

        foreach (var option in options)
        {
            var selectionKey = BuildPossibleMitigationSelectionKey(idSuffix, option);
            var selected = selectedPossibleMitigationKeys.Contains(selectionKey);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var checkboxValue = selected;
            if (DrawThemedCheckbox($"##PossibleMitigation{selectionKey}", ref checkboxValue))
            {
                if (checkboxValue)
                {
                    selectedPossibleMitigationKeys.Add(selectionKey);
                }
                else
                {
                    selectedPossibleMitigationKeys.Remove(selectionKey);
                }
            }

            ImGui.SameLine(0.0f, MathF.Max(2.0f, ImGui.GetStyle().ItemInnerSpacing.X * 0.5f));
            DrawPossibleMitigationAbility(option, ImGui.GetContentRegionAvail().X);

            ImGui.TableNextColumn();
            DrawPossibleMitigationSource(option, ImGui.GetContentRegionAvail().X);

            ImGui.TableNextColumn();
            DrawPossibleMitigationPercentCell(option.Statuses);
        }

        ImGui.EndTable();
    }

    private void DrawFocusedPossibleMitigationOptionsList(
        IReadOnlyList<PossibleMitigationSnapshot> options,
        string idSuffix)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var iconSize = Math.Clamp(configuration.StatusIconSize, 14.0f, 22.0f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var checkboxColumnWidth = ImGui.GetFrameHeight() + 6.0f;
        var innerWidth = MathF.Max(0.0f, availableWidth - (FocusedDataRowPaddingX * 2.0f));
        var remainingWidth = MathF.Max(90.0f, innerWidth - checkboxColumnWidth - (spacing * 2.0f));
        var abilityMinimumWidth = MathF.Min(84.0f, remainingWidth * 0.45f);
        var sourceMinimumWidth = MathF.Min(72.0f, MathF.Max(0.0f, remainingWidth - abilityMinimumWidth));
        var abilityMaximumWidth = MathF.Max(abilityMinimumWidth, remainingWidth - sourceMinimumWidth);
        var abilityColumnWidth = Math.Clamp(remainingWidth * 0.44f, abilityMinimumWidth, abilityMaximumWidth);
        var sourceColumnWidth = MathF.Max(24.0f, remainingWidth - abilityColumnWidth);

        DrawFocusedColumnLabels(
            (FocusedDataRowPaddingX + checkboxColumnWidth + spacing, "Ability"),
            (FocusedDataRowPaddingX + checkboxColumnWidth + spacing + abilityColumnWidth + spacing, "Source"));

        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var selectionKey = BuildPossibleMitigationSelectionKey(idSuffix, option);
            var selected = selectedPossibleMitigationKeys.Contains(selectionKey);
            var sourceLabel = FormatPossibleMitigationSourceLabel(option);
            var abilityLabel = FormatPossibleMitigationAbilityLabel(option.ActionName);
            var abilityHeight = GetIconTextWrappedHeight(abilityLabel, abilityColumnWidth, option.ActionIconId, iconSize);
            var sourceHeight = GetIconTextWrappedHeight(sourceLabel, sourceColumnWidth, option.ClassJobId == 0 ? 0U : GetClassJobIconId(option.ClassJobId), iconSize);
            var rowHeight = MathF.Max(ImGui.GetFrameHeight(), MathF.Max(sourceHeight, abilityHeight)) + (FocusedDataRowPaddingY * 2.0f);
            var rowStart = ImGui.GetCursorScreenPos();
            DrawFocusedDataRowBackground(rowStart, availableWidth, rowHeight, index);

            var contentY = rowStart.Y + FocusedDataRowPaddingY;
            ImGui.SetCursorScreenPos(new Vector2(rowStart.X + FocusedDataRowPaddingX, contentY));
            var checkboxValue = selected;
            if (DrawThemedCheckbox($"##PossibleMitigation{selectionKey}", ref checkboxValue))
            {
                if (checkboxValue)
                {
                    selectedPossibleMitigationKeys.Add(selectionKey);
                }
                else
                {
                    selectedPossibleMitigationKeys.Remove(selectionKey);
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(
                rowStart.X + FocusedDataRowPaddingX + checkboxColumnWidth + spacing,
                contentY));
            DrawIconTextWrapped(option.ActionIconId, iconSize, option.ActionName, abilityLabel, abilityColumnWidth);

            ImGui.SetCursorScreenPos(new Vector2(
                rowStart.X + FocusedDataRowPaddingX + checkboxColumnWidth + spacing + abilityColumnWidth + spacing,
                contentY));
            DrawIconTextWrapped(GetClassJobIconId(option.ClassJobId), iconSize, option.ClassJobName, sourceLabel, sourceColumnWidth);

            ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowStart.Y + rowHeight + FocusedDataRowGap));
        }
    }

    private string FormatPossibleMitigationSourceLabel(PossibleMitigationSnapshot option)
    {
        var playerName = plugin.FormatPlayerDisplayName(
            option.MemberName,
            option.MemberKey,
            option.PartyIndex,
            option.ClassJobId,
            option.ClassJobName);
        return $"{playerName} ({option.ClassJobName})";
    }

    private void DrawPossibleMitigationSource(PossibleMitigationSnapshot option, float width)
    {
        var iconId = GetClassJobIconId(option.ClassJobId);
        var label = FormatPossibleMitigationSourceLabel(option);
        var iconSize = Math.Clamp(configuration.StatusIconSize, 14.0f, 22.0f);
        DrawIconTextWrapped(iconId, iconSize, option.ClassJobName, label, MathF.Max(24.0f, width));
    }

    private void DrawPossibleMitigationAbility(PossibleMitigationSnapshot option, float width)
    {
        var iconSize = Math.Clamp(configuration.StatusIconSize, 14.0f, 22.0f);
        DrawIconTextWrapped(
            option.ActionIconId,
            iconSize,
            option.ActionName,
            FormatPossibleMitigationAbilityLabel(option.ActionName),
            MathF.Max(24.0f, width));
    }

    private static string FormatPossibleMitigationAbilityLabel(string actionName)
    {
        return actionName switch
        {
            "Shadowed Vigil" => "SV",
            "Great Nebula" => "GN",
            "Riddle of Earth" => "RoE",
            "Third Eye / Tengentsu" => "Third Eye",
            "Bloodwhetting" => "BW",
            "Nascent Flash" => "NF",
            "Heart of Corundum" => "HoC",
            "Exaltation" => "Exalt",
            "Taurochole" => "Tauro",
            "Dark Missionary" => "DM",
            "Heart of Light" => "HoL",
            "Temperance" => "Temp",
            "Sacred Soil" => "Soil",
            "Collective Unconscious" => "CU",
            "Troubadour" => "Troub",
            "Shield Samba" => "Samba",
            "Magick Barrier" => "MB",
            _ => actionName,
        };
    }

    private static void DrawPossibleMitigationPercentCell(IReadOnlyList<StatusSnapshot> statuses)
    {
        var parts = statuses
            .SelectMany(status => Plugin.GetMitigationDisplayInfo(status).MitigationPercents)
            .DistinctBy(part => (part.Scope, part.Text, part.IconId))
            .ToList();
        if (parts.Count == 0)
        {
            DrawCenteredText("-", DisabledColor);
            return;
        }

        var iconSize = Math.Clamp(ImGui.GetTextLineHeight(), 12.0f, 18.0f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var separatorWidth = ImGui.CalcTextSize("/").X + (spacing * 2.0f);
        var availableWidth = MathF.Max(1.0f, ImGui.GetContentRegionAvail().X);
        var rows = BuildPossibleMitigationPercentRows(parts, iconSize, spacing, separatorWidth, availableWidth);

        foreach (var row in rows)
        {
            var rowWidth = GetPossibleMitigationPercentRowWidth(row, iconSize, spacing, separatorWidth);
            if (row.Count == 1 && rowWidth > availableWidth)
            {
                DrawPossibleMitigationPercentPartStacked(row[0], iconSize);
                continue;
            }

            CenterNextItem(rowWidth);
            ImGui.BeginGroup();
            for (var index = 0; index < row.Count; index++)
            {
                if (index > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextUnformatted("/");
                    ImGui.SameLine();
                }

                DrawPossibleMitigationPercentPart(row[index], iconSize);
            }

            ImGui.EndGroup();
        }
    }

    private static void DrawPossibleMitigationPercentPart(Plugin.MitigationPercentDisplay part, float iconSize)
    {
        if (part.IconId != 0)
        {
            DrawGameIcon(part.IconId, iconSize, part.Tooltip ?? part.Text);
            ImGui.SameLine();
        }

        ImGui.TextUnformatted(part.Text);
    }

    private static void DrawPossibleMitigationPercentPartStacked(Plugin.MitigationPercentDisplay part, float iconSize)
    {
        if (part.IconId != 0)
        {
            DrawCenteredGameIcon(part.IconId, iconSize, part.Tooltip ?? part.Text);
        }

        DrawCenteredOrWrappedText(part.Text);
    }

    private static IReadOnlyList<IReadOnlyList<Plugin.MitigationPercentDisplay>> BuildPossibleMitigationPercentRows(
        IReadOnlyList<Plugin.MitigationPercentDisplay> parts,
        float iconSize,
        float spacing,
        float separatorWidth,
        float availableWidth)
    {
        var rows = new List<IReadOnlyList<Plugin.MitigationPercentDisplay>>();
        var currentRow = new List<Plugin.MitigationPercentDisplay>();
        var currentWidth = 0.0f;

        foreach (var part in parts)
        {
            var partWidth = GetPossibleMitigationPercentPartWidth(part, iconSize, spacing);
            var nextWidth = currentRow.Count == 0
                ? partWidth
                : currentWidth + separatorWidth + partWidth;
            if (currentRow.Count > 0 && nextWidth > availableWidth)
            {
                rows.Add(currentRow);
                currentRow = [];
                currentWidth = 0.0f;
                nextWidth = partWidth;
            }

            currentRow.Add(part);
            currentWidth = nextWidth;
        }

        if (currentRow.Count > 0)
        {
            rows.Add(currentRow);
        }

        return rows;
    }

    private static float GetPossibleMitigationPercentRowWidth(
        IReadOnlyList<Plugin.MitigationPercentDisplay> parts,
        float iconSize,
        float spacing,
        float separatorWidth)
    {
        return parts.Sum(part => GetPossibleMitigationPercentPartWidth(part, iconSize, spacing)) +
            (Math.Max(0, parts.Count - 1) * separatorWidth);
    }

    private static float GetPossibleMitigationPercentPartWidth(
        Plugin.MitigationPercentDisplay part,
        float iconSize,
        float spacing)
    {
        return (part.IconId == 0 ? 0.0f : iconSize + spacing) + ImGui.CalcTextSize(part.Text).X;
    }

    private void DrawPossibleMitigationResult(
        IReadOnlyList<CombatEventRecord> damageEvents,
        ulong? observedDamage,
        EventHpDisplay hpDisplay,
        IReadOnlyList<StatusSnapshot> activeStatuses,
        IReadOnlyList<PossibleMitigationSnapshot> selectedOptions,
        bool hasAvailableOptions)
    {
        DrawLeadUpLabel("Mitigation result");
        if (observedDamage is null || damageEvents.Count == 0)
        {
            ImGui.TextDisabled("Select a death with captured damage to calculate a mitigation result.");
            return;
        }

        if (!hasAvailableOptions)
        {
            ImGui.TextDisabled("No selectable mitigation was available to calculate.");
            return;
        }

        if (selectedOptions.Count == 0)
        {
            ImGui.TextDisabled("Select one or more mitigations above to calculate a possible result.");
            return;
        }

        var selectedStatuses = selectedOptions
            .SelectMany(option => option.Statuses)
            .ToList();
        var combinedStatuses = DeduplicateWhatIfStatuses(activeStatuses.Concat(selectedStatuses));
        var potentialDamage = CalculateDamageWithAdditionalMitigation(damageEvents, selectedStatuses);
        var preventedDamage = observedDamage.Value > potentialDamage
            ? observedDamage.Value - potentialDamage
            : 0UL;

        ImGui.BulletText($"Potential damage: {potentialDamage:N0}");
        ImGui.BulletText($"Reduced by: {preventedDamage:N0}");
        if (hpDisplay.MaxHp > 0)
        {
            var effectiveHp = (ulong)hpDisplay.CurrentHp + hpDisplay.ShieldHp;
            if (potentialDamage < effectiveHp)
            {
                ImGui.TextColored(HealColor, $"Would survive by {effectiveHp - potentialDamage:N0}.");
            }
            else if (potentialDamage == effectiveHp)
            {
                ImGui.TextColored(WarningColor, "Would be exactly lethal.");
            }
            else
            {
                ImGui.TextColored(OverkillColor, $"Still short by {potentialDamage - effectiveHp:N0}.");
            }
        }

        DrawMitigationTotal(combinedStatuses);
        ImGui.Dummy(new Vector2(0.0f, ImGui.GetStyle().ItemSpacing.Y * 1.5f));
    }

    private static ulong CalculateDamageWithAdditionalMitigation(
        IReadOnlyList<CombatEventRecord> damageEvents,
        IReadOnlyList<StatusSnapshot> selectedStatuses)
    {
        var total = 0UL;
        foreach (var damageEvent in damageEvents)
        {
            total += ApplyAdditionalMitigation(damageEvent.Amount, damageEvent.DamageType, selectedStatuses);
        }

        return total;
    }

    private static ulong ApplyAdditionalMitigation(
        uint amount,
        DamageType damageType,
        IReadOnlyList<StatusSnapshot> selectedStatuses)
    {
        var remaining = 1.0;
        foreach (var status in selectedStatuses)
        {
            foreach (var part in Plugin.GetMitigationDisplayInfo(status).MitigationPercents)
            {
                if (part.Percent <= 0.0f || !MitigationPartAppliesToDamageType(part.Scope, damageType))
                {
                    continue;
                }

                remaining *= 1.0 - (Math.Clamp(part.Percent, 0.0f, 100.0f) / 100.0);
            }
        }

        return (ulong)Math.Ceiling(amount * remaining);
    }

    private static bool MitigationPartAppliesToDamageType(Plugin.MitigationPercentScope scope, DamageType damageType)
    {
        return scope switch
        {
            Plugin.MitigationPercentScope.Physical => IsPhysicalDamageType(damageType),
            Plugin.MitigationPercentScope.Magic => IsMagicDamageType(damageType),
            _ => true,
        };
    }

    private static bool IsPhysicalDamageType(DamageType damageType)
    {
        return damageType is DamageType.Slashing or DamageType.Piercing or DamageType.Blunt or DamageType.Shot or DamageType.Physical;
    }

    private static bool IsMagicDamageType(DamageType damageType)
    {
        return damageType is DamageType.Magic or DamageType.Breath;
    }

    private static bool HasCalculableMitigationPercent(StatusSnapshot status)
    {
        return Plugin.GetMitigationDisplayInfo(status).MitigationPercents.Count > 0;
    }

    private static bool StatusAlreadyActive(StatusSnapshot status, IReadOnlyList<StatusSnapshot> activeStatuses)
    {
        return activeStatuses.Any(activeStatus =>
            status.Id != 0 && activeStatus.Id == status.Id ||
            string.Equals(activeStatus.Name, status.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<StatusSnapshot> DeduplicateWhatIfStatuses(IEnumerable<StatusSnapshot> statuses)
    {
        var deduplicated = new List<StatusSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var status in statuses)
        {
            var key = status.Id == 0 ? status.Name : status.Id.ToString(CultureInfo.InvariantCulture);
            if (seen.Add(key))
            {
                deduplicated.Add(status);
            }
        }

        return deduplicated;
    }

    private static string BuildPossibleMitigationSelectionKey(string idSuffix, PossibleMitigationSnapshot option)
    {
        return $"{idSuffix}:{option.Key}:{option.ActionId}";
    }

    private static int GetPossibleMitigationScopeOrder(PossibleMitigationScope scope)
    {
        return scope switch
        {
            PossibleMitigationScope.Personal => 0,
            PossibleMitigationScope.Targeted => 1,
            PossibleMitigationScope.Party => 2,
            PossibleMitigationScope.Boss => 3,
            _ => 4,
        };
    }

    private static int GetPossibleMitigationRoleOrder(uint classJobId)
    {
        return classJobId switch
        {
            1 or 3 or 19 or 21 or 32 or 37 => 0,
            6 or 24 or 28 or 33 or 40 => 1,
            2 or 4 or 5 or 7 or 26 or 29 or 20 or 22 or 23 or 25 or 27 or 30 or 31 or 34 or 35 or 36 or 38 or 39 or 41 or 42 => 2,
            _ => 3,
        };
    }

    private static int GetPossibleMitigationClassOrder(uint classJobId)
    {
        return classJobId switch
        {
            1 or 19 => 0,
            3 or 21 => 1,
            32 => 2,
            37 => 3,
            6 or 24 => 10,
            28 => 11,
            33 => 12,
            40 => 13,
            2 or 20 => 20,
            4 or 22 => 21,
            29 or 30 => 22,
            34 => 23,
            39 => 24,
            41 => 25,
            5 or 23 => 30,
            31 => 31,
            38 => 32,
            7 or 25 => 40,
            26 or 27 => 41,
            35 => 42,
            42 => 43,
            36 => 44,
            _ => 100,
        };
    }

    private void DrawBetterDeathsInformation(ResolvedDeathDisplay resolved, string idSuffix)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
        var isOpen = ImGui.CollapsingHeader($"Better Deaths information - {LeadUpHistorySeconds:0}s lead-up###BetterDeathsInfo{idSuffix}");
        ImGui.PopStyleColor();
        if (!isOpen)
        {
            return;
        }

        DrawBetterDeathsInformationContent(resolved, idSuffix);
    }

    private void DrawBetterDeathsInformationContent(ResolvedDeathDisplay resolved, string idSuffix)
    {
        using var sectionIndent = new ImGuiIndentScope(SectionBodyIndent);
        ImGui.TextDisabled("Older saved pulls may show less detail if that data was not captured at the time.");
        DrawHpHistory(resolved, idSuffix);
    }

    private IReadOnlyList<EnemyHpSnapshot> GetEnemyHpAtDeathForDisplay(PartyDeathRecord death)
    {
        return death.EnemyHpAtDeath
            .Where(enemy => enemy.IsTargetable)
            .ToList();
    }

    private void DrawEnemyHpAtDeathHeaderAndBullets(IReadOnlyList<EnemyHpSnapshot> enemies)
    {
        if (enemies.Count == 0)
        {
            return;
        }

        ImGui.TextUnformatted("Enemy HP at death");
        foreach (var enemy in enemies)
        {
            var enemyHpText = $"{enemy.Name}: {FormatEnemyHpPercent(enemy)}";
            ImGui.BulletText(enemyHpText);

            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip($"{enemy.Name}\nHP: {enemy.CurrentHp:N0} / {enemy.MaxHp:N0}\nEntity: {enemy.EntityId:X8}");
            }
        }
    }

    private void DrawDeathReplayDisplaySettings(string idSuffix)
    {
        ImGui.Spacing();
        var showReplayPlayerNames = configuration.ShowReplayPlayerNames;
        if (DrawThemedCheckbox($"Show names##DeathReplayShowNames{idSuffix}", ref showReplayPlayerNames))
        {
            plugin.SetShowReplayPlayerNames(showReplayPlayerNames);
        }

        DrawSettingsTooltip("Shows player names on the replay canvas. Name Redaction still hides real names.");

        var style = ImGui.GetStyle();
        if (CanFitInlineReplayCheckbox("Show classes", style))
        {
            ImGui.SameLine();
        }

        var showReplayPlayerJobs = configuration.ShowReplayPlayerJobs;
        if (DrawThemedCheckbox($"Show classes##DeathReplayShowJobs{idSuffix}", ref showReplayPlayerJobs))
        {
            plugin.SetShowReplayPlayerJobs(showReplayPlayerJobs);
        }

        DrawSettingsTooltip("Shows class/job abbreviations on the replay canvas.");

        if (CanFitInlineReplayCheckbox("Show HP", style))
        {
            ImGui.SameLine();
        }

        var showReplayPlayerHp = configuration.ShowReplayPlayerHp;
        if (DrawThemedCheckbox($"Show HP##DeathReplayShowHp{idSuffix}", ref showReplayPlayerHp))
        {
            plugin.SetShowReplayPlayerHp(showReplayPlayerHp);
        }

        DrawSettingsTooltip("Shows compact HP and shield percentages on the replay canvas.");
        DrawDeathReplayWorldMarkerOpacitySlider(idSuffix);
    }

    private static bool CanFitInlineReplayCheckbox(string label, ImGuiStylePtr style)
    {
        var labelWidth = ImGui.CalcTextSize(label).X + ImGui.GetFrameHeight() + (style.ItemInnerSpacing.X * 2.0f);
        return ImGui.GetContentRegionAvail().X >= labelWidth + style.ItemSpacing.X;
    }

    private void DrawDeathReplayWorldMarkerOpacitySlider(string idSuffix)
    {
        ImGui.Spacing();
        const string label = "World marker opacity";
        var style = ImGui.GetStyle();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var labelWidth = ImGui.CalcTextSize(label).X;
        var canFitInline = availableWidth - labelWidth - style.ItemInnerSpacing.X >= 160.0f;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (canFitInline)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(MathF.Min(360.0f, availableWidth - labelWidth - style.ItemInnerSpacing.X));
        }
        else
        {
            ImGui.SetNextItemWidth(MathF.Max(1.0f, availableWidth));
        }

        var replayWorldMarkerOpacity = GetReplayWorldMarkerOpacity();
        if (ImGui.SliderFloat(
            $"##DeathReplayWorldMarkerOpacity{idSuffix}",
            ref replayWorldMarkerOpacity,
            Plugin.MinReplayWorldMarkerOpacity,
            Plugin.MaxReplayWorldMarkerOpacity,
            "%.2f"))
        {
            plugin.SetReplayWorldMarkerOpacity(replayWorldMarkerOpacity);
        }

        DrawSettingsTooltip("Controls how visible A/B/C/D and 1/2/3/4 waymarks are inside Death Replay.");
    }

    private ReplayStaticDisplayCache GetReplayStaticDisplayCache(
        string idSuffix,
        long referenceTicks,
        uint territoryId,
        DateTime replayStartAtUtc,
        DateTime referenceAtUtc,
        float minimumVisibleOffset,
        float maximumVisibleOffset,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayMechanicSnapshot> rawMechanics,
        IReadOnlyList<ReplayWorldMarkerSnapshot> worldMarkers,
        IReplayEncounterModule replayModule,
        bool showEarlierMarkers)
    {
        var dataSignature = CreateReplayDisplayDataSignature(positions, markers, rawMechanics, worldMarkers);
        var key = new ReplayStaticDisplayCacheKey(referenceTicks, territoryId, showEarlierMarkers, dataSignature);
        if (replayStaticDisplayCacheByDeathId.TryGetValue(idSuffix, out var cached) && cached.Key.Equals(key))
        {
            return cached;
        }

        var positionTracks = BuildReplayPositionTracks(positions);
        var mechanics = GetReplayMechanicsForDisplay(positions, markers, rawMechanics, replayModule, replayStartAtUtc, showEarlierMarkers);
        GetReplayTimeBounds(referenceAtUtc, minimumVisibleOffset, maximumVisibleOffset, positions, mechanics, worldMarkers, out var minOffset, out var maxOffset);
        var updated = new ReplayStaticDisplayCache(key, replayStartAtUtc, positionTracks, mechanics, minOffset, maxOffset);
        replayStaticDisplayCacheByDeathId[idSuffix] = updated;
        replayFrameDisplayCacheByDeathId.Remove(idSuffix);
        return updated;
    }

    private ReplayFrameDisplayCache GetReplayFrameDisplayCache(
        string idSuffix,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayWorldMarkerSnapshot> worldMarkers,
        ReplayStaticDisplayCache replayDisplay,
        DateTime selectedAtUtc,
        bool showEarlierMarkers,
        IReplayEncounterModule replayModule)
    {
        var key = new ReplayFrameDisplayCacheKey(replayDisplay.Key, selectedAtUtc.Ticks);
        if (replayFrameDisplayCacheByDeathId.TryGetValue(idSuffix, out var cached) && cached.Key.Equals(key))
        {
            return cached;
        }

        var actorStates = SelectReplayActorStates(replayDisplay.PositionTracks, selectedAtUtc);
        var markerStates = SelectReplayMarkerStates(markers, positions, selectedAtUtc, replayDisplay.ReplayStartAtUtc, showEarlierMarkers, replayModule);
        var mechanicStates = SelectReplayMechanicStates(replayDisplay.Mechanics, markers, positions, actorStates, selectedAtUtc, replayModule);
        var worldMarkerStates = SelectReplayWorldMarkerStates(worldMarkers, selectedAtUtc);
        var updated = new ReplayFrameDisplayCache(key, actorStates, markerStates, mechanicStates, worldMarkerStates);
        replayFrameDisplayCacheByDeathId[idSuffix] = updated;
        return updated;
    }

    private void KeepOnlyActiveReplayDisplayCache(string activeDeathId)
    {
        KeepOnlyActiveReplayDisplayCacheEntry(replayStaticDisplayCacheByDeathId, activeDeathId);
        KeepOnlyActiveReplayDisplayCacheEntry(replayFrameDisplayCacheByDeathId, activeDeathId);
        KeepOnlyActiveReplayDisplayCacheEntry(replayPullTimingCacheByDeathId, activeDeathId);
    }

    private static void KeepOnlyActiveReplayDisplayCacheEntry<T>(Dictionary<string, T> cache, string activeDeathId)
    {
        if (cache.Count == 0)
        {
            return;
        }

        if (cache.Count == 1)
        {
            string? staleKey = null;
            foreach (var key in cache.Keys)
            {
                if (!string.Equals(key, activeDeathId, StringComparison.Ordinal))
                {
                    staleKey = key;
                }
            }

            if (staleKey is not null)
            {
                cache.Remove(staleKey);
            }

            return;
        }

        foreach (var key in cache.Keys.Where(key => !string.Equals(key, activeDeathId, StringComparison.Ordinal)).ToList())
        {
            cache.Remove(key);
        }
    }

    private static ReplayDisplayDataSignature CreateReplayDisplayDataSignature(
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayMechanicSnapshot> mechanics,
        IReadOnlyList<ReplayWorldMarkerSnapshot> worldMarkers)
    {
        GetReplaySeenAtRange(positions, out var firstPositionTicks, out var lastPositionTicks);
        GetReplaySeenAtRange(markers, out var firstMarkerTicks, out var lastMarkerTicks);
        GetReplaySeenAtRange(mechanics, out var firstMechanicTicks, out var lastMechanicTicks);
        GetReplaySeenAtRange(worldMarkers, out var firstWorldMarkerTicks, out var lastWorldMarkerTicks);
        return new ReplayDisplayDataSignature(
            positions,
            positions.Count,
            firstPositionTicks,
            lastPositionTicks,
            markers,
            markers.Count,
            firstMarkerTicks,
            lastMarkerTicks,
            mechanics,
            mechanics.Count,
            firstMechanicTicks,
            lastMechanicTicks,
            worldMarkers,
            worldMarkers.Count,
            firstWorldMarkerTicks,
            lastWorldMarkerTicks);
    }

    private static void GetReplaySeenAtRange(IReadOnlyList<ReplayPositionSnapshot> snapshots, out long firstTicks, out long lastTicks)
    {
        if (snapshots.Count == 0)
        {
            firstTicks = 0L;
            lastTicks = 0L;
            return;
        }

        firstTicks = snapshots[0].SeenAtUtc.Ticks;
        lastTicks = snapshots[^1].SeenAtUtc.Ticks;
    }

    private static void GetReplaySeenAtRange(IReadOnlyList<ReplayMarkerSnapshot> snapshots, out long firstTicks, out long lastTicks)
    {
        if (snapshots.Count == 0)
        {
            firstTicks = 0L;
            lastTicks = 0L;
            return;
        }

        firstTicks = snapshots[0].SeenAtUtc.Ticks;
        lastTicks = snapshots[^1].SeenAtUtc.Ticks;
    }

    private static void GetReplaySeenAtRange(IReadOnlyList<ReplayMechanicSnapshot> snapshots, out long firstTicks, out long lastTicks)
    {
        if (snapshots.Count == 0)
        {
            firstTicks = 0L;
            lastTicks = 0L;
            return;
        }

        firstTicks = snapshots[0].SeenAtUtc.Ticks;
        lastTicks = snapshots[^1].SeenAtUtc.Ticks;
    }

    private static void GetReplaySeenAtRange(IReadOnlyList<ReplayWorldMarkerSnapshot> snapshots, out long firstTicks, out long lastTicks)
    {
        if (snapshots.Count == 0)
        {
            firstTicks = 0L;
            lastTicks = 0L;
            return;
        }

        firstTicks = snapshots[0].SeenAtUtc.Ticks;
        lastTicks = snapshots[^1].SeenAtUtc.Ticks;
    }

    private static IReadOnlyList<ReplayPositionTrack> BuildReplayPositionTracks(IReadOnlyList<ReplayPositionSnapshot> positions)
    {
        if (positions.Count == 0)
        {
            return [];
        }

        var tracksByActor = new Dictionary<string, List<ReplayPositionSnapshot>>(StringComparer.Ordinal);
        foreach (var position in positions)
        {
            if (!tracksByActor.TryGetValue(position.ActorKey, out var track))
            {
                track = [];
                tracksByActor[position.ActorKey] = track;
            }

            track.Add(position);
        }

        var tracks = new List<ReplayPositionTrack>(tracksByActor.Count);
        foreach (var (actorKey, track) in tracksByActor)
        {
            track.Sort(static (left, right) => left.SeenAtUtc.CompareTo(right.SeenAtUtc));
            tracks.Add(new ReplayPositionTrack(actorKey, track));
        }

        return tracks;
    }

    private static IReadOnlyList<ReplayMechanicSnapshot> GetReplayMechanicsForDisplay(
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayMechanicSnapshot> rawMechanics,
        IReplayEncounterModule replayModule,
        DateTime replayStartAtUtc,
        bool showEarlierMarkers)
    {
        var mechanics = NormalizeReplayPathOfLightTowerTimeline(rawMechanics
            .Select(NormalizeReplayMechanicForDisplay)
            .ToList())
            .ToList();
        if (markers.Count == 0 || positions.Count == 0)
        {
            return mechanics
                .OrderBy(mechanic => mechanic.SeenAtUtc)
                .ThenBy(mechanic => mechanic.SourceName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var displayStartAtUtc = positions.Min(position => position.SeenAtUtc);
        var displayEndAtUtc = positions.Max(position => position.SeenAtUtc);
        var resolvedDmuP1FireMarkerSourceKeys = AddDmuP1FireMarkerMechanics(
            mechanics,
            positions,
            markers,
            displayStartAtUtc,
            displayEndAtUtc,
            replayStartAtUtc,
            showEarlierMarkers);
        foreach (var marker in markers)
        {
            if (!showEarlierMarkers && marker.SeenAtUtc < replayStartAtUtc ||
                marker.SeenAtUtc > displayEndAtUtc ||
                resolvedDmuP1FireMarkerSourceKeys.Contains(GetReplayMarkerMechanicSourceKey(marker)) ||
                !replayModule.ShouldCreateReplayMarkerMechanic(marker, markers) ||
                !replayModule.TryGetMarkerInfo(marker.MarkerId, out var markerInfo))
            {
                continue;
            }

            markerInfo = ResolveReplayMarkerMechanicInfo(marker, markers, markerInfo);
            if (markerInfo.Shape is not { } shape)
            {
                continue;
            }

            var actorPosition = FindReplayMarkerActorPosition(positions, marker.ActorKey, marker.SeenAtUtc, displayStartAtUtc, displayEndAtUtc);
            if (actorPosition is null)
            {
                continue;
            }

            var nextMarkerAtUtc = markers
                .Where(candidate => string.Equals(candidate.ActorKey, marker.ActorKey, StringComparison.Ordinal) &&
                    candidate.SeenAtUtc > marker.SeenAtUtc)
                .OrderBy(candidate => candidate.SeenAtUtc)
                .Select(candidate => (DateTime?)candidate.SeenAtUtc)
                .FirstOrDefault();
            if (nextMarkerAtUtc is { } overwrittenAtUtc && overwrittenAtUtc <= displayStartAtUtc)
            {
                continue;
            }

            var mechanicSeenAtUtc = MaxDateTime(marker.SeenAtUtc, displayStartAtUtc);
            var mechanicEndAtUtc = MinDateTime(GetReplayMarkerMechanicEndAtUtc(marker, markerInfo), displayEndAtUtc);
            if (nextMarkerAtUtc is { } next)
            {
                mechanicEndAtUtc = MinDateTime(mechanicEndAtUtc, next);
            }

            if (mechanicEndAtUtc <= mechanicSeenAtUtc)
            {
                continue;
            }

            mechanics.Add(new ReplayMechanicSnapshot(
                mechanicSeenAtUtc,
                actorPosition.PullElapsedSeconds,
                Math.Max(0.05f, (float)(mechanicEndAtUtc - mechanicSeenAtUtc).TotalSeconds),
                GetReplayMarkerMechanicSourceKey(marker),
                marker.ActorName,
                shape,
                actorPosition.X,
                actorPosition.Y,
                actorPosition.Z,
                actorPosition.Rotation,
                markerInfo.Radius,
                markerInfo.Length,
                markerInfo.Width,
                markerInfo.AngleDegrees,
                markerInfo.Description,
                "target-icon",
                marker.RawMarkerId,
                marker.EntityId,
                true));
        }

        return mechanics
            .OrderBy(mechanic => mechanic.SeenAtUtc)
            .ThenBy(mechanic => mechanic.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mechanic => mechanic.RawEventId)
            .ToList();
    }

    private static HashSet<string> AddDmuP1FireMarkerMechanics(
        List<ReplayMechanicSnapshot> mechanics,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        DateTime displayStartAtUtc,
        DateTime displayEndAtUtc,
        DateTime replayStartAtUtc,
        bool showEarlierMarkers)
    {
        var resolvedSourceKeys = new HashSet<string>(StringComparer.Ordinal);
        var fireMarkers = markers
            .Where(marker => ReplayEncounterModules.IsDmuP1FireMarker(marker.MarkerId))
            .Where(marker => showEarlierMarkers || marker.SeenAtUtc >= replayStartAtUtc)
            .Where(marker => marker.SeenAtUtc <= displayEndAtUtc)
            .OrderBy(marker => marker.SeenAtUtc)
            .ThenBy(marker => marker.ActorKey, StringComparer.Ordinal)
            .ToList();
        if (fireMarkers.Count == 0)
        {
            return resolvedSourceKeys;
        }

        foreach (var fireRound in BuildReplayMarkerBatches(fireMarkers, TimeSpan.FromSeconds(1.0)))
        {
            if (AddDmuP1FireMarkerMechanicRound(
                mechanics,
                positions,
                markers,
                fireRound,
                displayStartAtUtc,
                displayEndAtUtc))
            {
                foreach (var marker in fireRound)
                {
                    resolvedSourceKeys.Add(GetReplayMarkerMechanicSourceKey(marker));
                }
            }
        }

        return resolvedSourceKeys;
    }

    private static bool AddDmuP1FireMarkerMechanicRound(
        List<ReplayMechanicSnapshot> mechanics,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayMarkerSnapshot> fireRound,
        DateTime displayStartAtUtc,
        DateTime displayEndAtUtc)
    {
        if (fireRound.Count == 0 ||
            !ReplayEncounterModules.TryResolveDmuP1FireMarkerInfo(fireRound[0], markers, default, out var fireInfo) ||
            fireInfo.Shape is not { } shape)
        {
            return false;
        }

        var roundSeenAtUtc = fireRound.Min(marker => marker.SeenAtUtc);
        var mechanicSeenAtUtc = MaxDateTime(roundSeenAtUtc, displayStartAtUtc);
        var mechanicEndAtUtc = MinDateTime(roundSeenAtUtc.AddSeconds(Math.Max(0.05f, fireInfo.DurationSeconds)), displayEndAtUtc);
        if (mechanicEndAtUtc <= mechanicSeenAtUtc)
        {
            return false;
        }

        var targets = shape == ReplayMechanicShape.Spread
            ? GetReplayPlayerActorsAt(positions, roundSeenAtUtc, displayStartAtUtc, displayEndAtUtc)
            : GetDmuP1FireStackTargets(positions, fireRound, roundSeenAtUtc, displayStartAtUtc, displayEndAtUtc);
        if (targets.Count == 0)
        {
            return false;
        }

        foreach (var target in targets)
        {
            mechanics.Add(new ReplayMechanicSnapshot(
                mechanicSeenAtUtc,
                target.PullElapsedSeconds,
                Math.Max(0.05f, (float)(mechanicEndAtUtc - mechanicSeenAtUtc).TotalSeconds),
                $"{ReplayDmuP1FlagrantFireRawEventKind}:{roundSeenAtUtc.Ticks}:{target.ActorKey}",
                target.ActorName,
                shape,
                target.X,
                target.Y,
                target.Z,
                target.Rotation,
                fireInfo.Radius,
                fireInfo.Length,
                fireInfo.Width,
                fireInfo.AngleDegrees,
                fireInfo.Description,
                ReplayDmuP1FlagrantFireRawEventKind,
                fireRound[0].RawMarkerId,
                fireRound[0].MarkerId,
                true));
        }

        return true;
    }

    private static IReadOnlyList<IReadOnlyList<ReplayMarkerSnapshot>> BuildReplayMarkerBatches(
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        TimeSpan maxGap)
    {
        var batches = new List<IReadOnlyList<ReplayMarkerSnapshot>>();
        var current = new List<ReplayMarkerSnapshot>();
        foreach (var marker in markers)
        {
            if (current.Count > 0 &&
                marker.SeenAtUtc - current[^1].SeenAtUtc > maxGap)
            {
                batches.Add(current);
                current = [];
            }

            current.Add(marker);
        }

        if (current.Count > 0)
        {
            batches.Add(current);
        }

        return batches;
    }

    private static IReadOnlyList<ReplayPositionSnapshot> GetDmuP1FireStackTargets(
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMarkerSnapshot> fireRound,
        DateTime roundSeenAtUtc,
        DateTime displayStartAtUtc,
        DateTime displayEndAtUtc)
    {
        var displayedStackTargets = fireRound
            .Where(marker => marker.MarkerId == 128)
            .Select(marker => FindReplayMarkerActorPosition(positions, marker.ActorKey, marker.SeenAtUtc, displayStartAtUtc, displayEndAtUtc))
            .Where(position => position is not null)
            .Select(position => position!)
            .GroupBy(position => position.ActorKey, StringComparer.Ordinal)
            .Select(group => group.OrderBy(position => Math.Abs((position.SeenAtUtc - roundSeenAtUtc).TotalSeconds)).First())
            .OrderBy(position => position.PartyIndex)
            .ToList();
        if (displayedStackTargets.Count > 0)
        {
            return displayedStackTargets;
        }

        var players = GetReplayPlayerActorsAt(positions, roundSeenAtUtc, displayStartAtUtc, displayEndAtUtc);
        if (players.Count <= 2)
        {
            return players;
        }

        var partySorted = players
            .OrderBy(position => IsReplayTank(position) || IsReplayHealer(position) ? 0 : 1)
            .ThenBy(position => position.PartyIndex)
            .ThenBy(position => position.ActorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return [partySorted[0], partySorted[^1]];
    }

    private static IReadOnlyList<ReplayPositionSnapshot> GetReplayPlayerActorsAt(
        IReadOnlyList<ReplayPositionSnapshot> positions,
        DateTime selectedAtUtc,
        DateTime displayStartAtUtc,
        DateTime displayEndAtUtc)
    {
        return positions
            .Where(position => position.ActorKind == ReplayActorKind.Player)
            .GroupBy(position => position.ActorKey, StringComparer.Ordinal)
            .Select(group => FindReplayMarkerActorPosition(positions, group.Key, selectedAtUtc, displayStartAtUtc, displayEndAtUtc))
            .Where(position => position is not null)
            .Select(position => position!)
            .OrderBy(position => position.PartyIndex)
            .ThenBy(position => position.ActorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ReplayMechanicSnapshot NormalizeReplayMechanicForDisplay(ReplayMechanicSnapshot mechanic)
    {
        if (IsReplayPathOfLightMechanic(mechanic) &&
            TryGetReplayPathOfLightTowerIndex(mechanic.SourceKey, out var towerIndex))
        {
            var angleDegrees = 180.0f - ((towerIndex - 1) * 45.0f);
            var angleRadians = angleDegrees * MathF.PI / 180.0f;
            var x = DmuReplayArenaCenterX + MathF.Sin(angleRadians) * ReplayPathOfLightTowerDistance;
            var z = DmuReplayArenaCenterZ + MathF.Cos(angleRadians) * ReplayPathOfLightTowerDistance;

            return mechanic with
            {
                DurationSeconds = MathF.Min(mechanic.DurationSeconds, ReplayPathOfLightFallbackDurationSeconds),
                X = x,
                Z = z,
            };
        }

        return mechanic;
    }

    private static IReadOnlyList<ReplayMechanicSnapshot> NormalizeReplayPathOfLightTowerTimeline(
        IReadOnlyList<ReplayMechanicSnapshot> mechanics)
    {
        var passthroughMechanics = new List<ReplayMechanicSnapshot>();
        var pathOfLightTowers = new List<ReplayMechanicSnapshot>();
        foreach (var mechanic in mechanics)
        {
            if (IsReplayPathOfLightMechanic(mechanic))
            {
                pathOfLightTowers.Add(mechanic);
            }
            else
            {
                passthroughMechanics.Add(mechanic);
            }
        }

        if (pathOfLightTowers.Count == 0)
        {
            return passthroughMechanics;
        }

        var adjustedTowers = new List<ReplayMechanicSnapshot>(pathOfLightTowers.Count);
        var activeTowers = new List<(ReplayMechanicSnapshot Mechanic, DateTime EndAtUtc)>();
        var storedTowers = new List<ReplayMechanicSnapshot>();

        foreach (var tower in pathOfLightTowers
            .OrderBy(mechanic => mechanic.SeenAtUtc)
            .ThenBy(mechanic => mechanic.SourceKey, StringComparer.Ordinal))
        {
            ReleaseStoredPathOfLightTowers(tower.SeenAtUtc);
            if (activeTowers.Count >= 2)
            {
                storedTowers.Add(tower);
                continue;
            }

            AddActivePathOfLightTower(tower, tower.SeenAtUtc);
        }

        ReleaseStoredPathOfLightTowers(DateTime.MaxValue);
        passthroughMechanics.AddRange(adjustedTowers);
        return passthroughMechanics;

        void AddActivePathOfLightTower(ReplayMechanicSnapshot tower, DateTime displayStartAtUtc)
        {
            var adjustedTower = AdjustReplayPathOfLightTowerStart(tower, displayStartAtUtc);
            if (adjustedTower is null)
            {
                return;
            }

            adjustedTowers.Add(adjustedTower);
            activeTowers.Add((adjustedTower, adjustedTower.SeenAtUtc.AddSeconds(Math.Max(0.05f, adjustedTower.DurationSeconds))));
        }

        void ReleaseStoredPathOfLightTowers(DateTime selectedAtUtc)
        {
            while (activeTowers.Any(entry => entry.EndAtUtc <= selectedAtUtc))
            {
                var releaseAtUtc = activeTowers
                    .Where(entry => entry.EndAtUtc <= selectedAtUtc)
                    .Select(entry => entry.EndAtUtc)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();
                activeTowers.RemoveAll(entry => entry.EndAtUtc <= selectedAtUtc);
                if (activeTowers.Count != 0 || storedTowers.Count == 0)
                {
                    continue;
                }

                var releaseBatch = storedTowers
                    .OrderBy(mechanic => mechanic.SeenAtUtc)
                    .ThenBy(mechanic => mechanic.SourceKey, StringComparer.Ordinal)
                    .ToList();
                storedTowers.Clear();
                foreach (var storedTower in releaseBatch)
                {
                    AddActivePathOfLightTower(storedTower, releaseAtUtc);
                }
            }
        }
    }

    private static ReplayMechanicSnapshot? AdjustReplayPathOfLightTowerStart(
        ReplayMechanicSnapshot tower,
        DateTime displayStartAtUtc)
    {
        if (displayStartAtUtc <= tower.SeenAtUtc)
        {
            return tower;
        }

        var originalEndAtUtc = tower.SeenAtUtc.AddSeconds(Math.Max(0.05f, tower.DurationSeconds));
        if (displayStartAtUtc >= originalEndAtUtc)
        {
            return null;
        }

        var startDelaySeconds = (float)(displayStartAtUtc - tower.SeenAtUtc).TotalSeconds;
        return tower with
        {
            SeenAtUtc = displayStartAtUtc,
            PullElapsedSeconds = tower.PullElapsedSeconds + startDelaySeconds,
            DurationSeconds = Math.Max(0.05f, (float)(originalEndAtUtc - displayStartAtUtc).TotalSeconds),
        };
    }

    private static ReplayMarkerInfo ResolveReplayMarkerMechanicInfo(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        ReplayMarkerInfo markerInfo)
    {
        return ReplayEncounterModules.TryResolveDmuP1FireMarkerInfo(marker, markers, markerInfo, out var resolvedInfo)
            ? resolvedInfo
            : markerInfo;
    }

    private static DateTime GetReplayMarkerMechanicEndAtUtc(ReplayMarkerSnapshot marker, ReplayMarkerInfo markerInfo)
    {
        if (GetReplayMarkerExpiresAtUtc(marker) is { } statusExpiresAtUtc &&
            statusExpiresAtUtc > marker.SeenAtUtc)
        {
            return statusExpiresAtUtc;
        }

        return marker.SeenAtUtc.AddSeconds(Math.Max(0.05f, markerInfo.DurationSeconds));
    }

    private static bool IsReplayPathOfLightMechanic(ReplayMechanicSnapshot mechanic)
    {
        return string.Equals(mechanic.RawEventKind, ReplayPathOfLightRawEventKind, StringComparison.Ordinal) &&
            mechanic.Shape == ReplayMechanicShape.Tower;
    }

    private static bool TryGetReplayPathOfLightTowerIndex(string sourceKey, out int towerIndex)
    {
        const string prefix = "dmu-p2-path-of-light:";
        towerIndex = 0;
        if (!sourceKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var indexEnd = sourceKey.IndexOf(':', prefix.Length);
        if (indexEnd <= prefix.Length)
        {
            return false;
        }

        return int.TryParse(sourceKey[prefix.Length..indexEnd], CultureInfo.InvariantCulture, out towerIndex) &&
            towerIndex is >= 1 and <= 8;
    }

    private static ReplayPositionSnapshot? FindReplayMarkerActorPosition(
        IReadOnlyList<ReplayPositionSnapshot> positions,
        string actorKey,
        DateTime markerSeenAtUtc,
        DateTime displayStartAtUtc,
        DateTime displayEndAtUtc)
    {
        var clampedAtUtc = markerSeenAtUtc < displayStartAtUtc
            ? displayStartAtUtc
            : markerSeenAtUtc > displayEndAtUtc
                ? displayEndAtUtc
                : markerSeenAtUtc;
        ReplayPositionSnapshot? bestPosition = null;
        var bestDistance = TimeSpan.MaxValue;
        foreach (var position in positions)
        {
            if (!string.Equals(position.ActorKey, actorKey, StringComparison.Ordinal))
            {
                continue;
            }

            var distance = position.SeenAtUtc >= clampedAtUtc
                ? position.SeenAtUtc - clampedAtUtc
                : clampedAtUtc - position.SeenAtUtc;
            if (distance >= bestDistance)
            {
                continue;
            }

            bestPosition = position;
            bestDistance = distance;
        }

        return bestPosition;
    }

    private static DateTime MinDateTime(DateTime left, DateTime right)
    {
        return left <= right ? left : right;
    }

    private static DateTime MaxDateTime(DateTime left, DateTime right)
    {
        return left >= right ? left : right;
    }

    private static void GetReplayTimeBounds(
        DateTime referenceAtUtc,
        float minimumVisibleOffset,
        float maximumVisibleOffset,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMechanicSnapshot> mechanics,
        IReadOnlyList<ReplayWorldMarkerSnapshot> worldMarkers,
        out float minOffset,
        out float maxOffset)
    {
        maximumVisibleOffset = MathF.Max(minimumVisibleOffset + 0.05f, maximumVisibleOffset);
        if (positions.Count > 0)
        {
            var latestReplayOffset = positions
                .Select(position => (float)(position.SeenAtUtc - referenceAtUtc).TotalSeconds)
                .DefaultIfEmpty(0.0f)
                .Max();
            foreach (var mechanic in mechanics)
            {
                var mechanicEndOffset = (float)(mechanic.SeenAtUtc - referenceAtUtc).TotalSeconds +
                    MathF.Max(0.0f, mechanic.DurationSeconds);
                latestReplayOffset = MathF.Max(latestReplayOffset, mechanicEndOffset);
            }

            foreach (var marker in worldMarkers)
            {
                var markerOffset = (float)(marker.SeenAtUtc - referenceAtUtc).TotalSeconds;
                latestReplayOffset = MathF.Max(latestReplayOffset, markerOffset);
            }

            minOffset = minimumVisibleOffset;
            maxOffset = MathF.Min(MathF.Max(minimumVisibleOffset, latestReplayOffset), maximumVisibleOffset);
            if (maxOffset - minOffset < 0.05f)
            {
                maxOffset = MathF.Min(maximumVisibleOffset, minOffset + 0.5f);
            }

            return;
        }

        var hasOffset = false;
        minOffset = 0.0f;
        maxOffset = 0.0f;
        foreach (var mechanic in mechanics)
        {
            var startOffset = (float)(mechanic.SeenAtUtc - referenceAtUtc).TotalSeconds;
            IncludeReplayTimeOffset(startOffset, ref hasOffset, ref minOffset, ref maxOffset);
            IncludeReplayTimeOffset(startOffset + MathF.Max(0.0f, mechanic.DurationSeconds), ref hasOffset, ref minOffset, ref maxOffset);
        }

        foreach (var marker in worldMarkers)
        {
            var markerOffset = (float)(marker.SeenAtUtc - referenceAtUtc).TotalSeconds;
            IncludeReplayTimeOffset(markerOffset, ref hasOffset, ref minOffset, ref maxOffset);
        }

        if (!hasOffset)
        {
            minOffset = minimumVisibleOffset;
            maxOffset = MathF.Min(maximumVisibleOffset, minimumVisibleOffset + 0.5f);
            return;
        }

        minOffset = MathF.Max(minOffset, minimumVisibleOffset);
        maxOffset = MathF.Min(maxOffset, maximumVisibleOffset);
        if (maxOffset - minOffset < 0.05f)
        {
            maxOffset = MathF.Min(maximumVisibleOffset, minOffset + 0.5f);
        }
    }

    private static void IncludeReplayTimeOffset(float offset, ref bool hasOffset, ref float minOffset, ref float maxOffset)
    {
        if (!hasOffset)
        {
            minOffset = offset;
            maxOffset = offset;
            hasOffset = true;
            return;
        }

        minOffset = MathF.Min(minOffset, offset);
        maxOffset = MathF.Max(maxOffset, offset);
    }

    private void DrawDeathReplayControls(
        string idSuffix,
        float minOffset,
        float maxOffset,
        ref float scrubSeconds,
        float? focusOffset = 0.0f,
        string focusLabel = "Death")
    {
        UpdateReplayPlayback(idSuffix, minOffset, maxOffset, ref scrubSeconds);
        var compactControls = ImGui.GetContentRegionAvail().X < 480.0f;
        var isPlaying = replayPlayingByDeathId.TryGetValue(idSuffix, out var playing) && playing;
        var playLabel = isPlaying ? "Pause" : "Play";
        if (DrawThemedActionButton(playLabel, $"ReplayPlayPause{idSuffix}", 64.0f))
        {
            if (isPlaying)
            {
                replayPlayingByDeathId[idSuffix] = false;
            }
            else
            {
                if (scrubSeconds >= maxOffset - 0.001f)
                {
                    scrubSeconds = minOffset;
                    replayScrubSecondsByDeathId[idSuffix] = scrubSeconds;
                }

                replayPlayingByDeathId[idSuffix] = true;
                replayLastFrameAtUtcByDeathId[idSuffix] = DateTime.UtcNow;
            }
        }

        ImGui.SameLine(0.0f, 4.0f);
        if (DrawThemedActionButton("Reset", $"ReplayReset{idSuffix}", 64.0f))
        {
            scrubSeconds = minOffset;
            replayScrubSecondsByDeathId[idSuffix] = scrubSeconds;
            replayPlayingByDeathId[idSuffix] = false;
        }

        ImGui.SameLine(0.0f, 4.0f);
        if (focusOffset is null)
        {
            ImGui.BeginDisabled();
        }

        if (DrawThemedActionButton(focusLabel, $"ReplayDeath{idSuffix}", 64.0f) && focusOffset is { } targetOffset)
        {
            scrubSeconds = Math.Clamp(targetOffset, minOffset, maxOffset);
            replayScrubSecondsByDeathId[idSuffix] = scrubSeconds;
            replayPlayingByDeathId[idSuffix] = false;
        }

        if (focusOffset is null)
        {
            ImGui.EndDisabled();
        }

        if (!compactControls)
        {
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemSpacing.X * 2.0f);
        }

        DrawReplaySpeedButton(idSuffix, "0.5x", 0.5f);
        ImGui.SameLine(0.0f, 4.0f);
        DrawReplaySpeedButton(idSuffix, "1x", 1.0f);
        ImGui.SameLine(0.0f, 4.0f);
        DrawReplaySpeedButton(idSuffix, "2x", 2.0f);

        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemSpacing.X * 2.0f);
        var showTrails = GetReplayShowTrails();
        if (DrawSegmentedButton("Trails", $"ReplayTrails{idSuffix}", showTrails, 70.0f))
        {
            plugin.SetShowReplayTrails(!showTrails);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Shows recent movement trails behind actors.");
        }
    }

    private void UpdateReplayPlayback(string idSuffix, float minOffset, float maxOffset, ref float scrubSeconds)
    {
        if (!replayPlayingByDeathId.TryGetValue(idSuffix, out var isPlaying) || !isPlaying)
        {
            replayLastFrameAtUtcByDeathId[idSuffix] = DateTime.UtcNow;
            return;
        }

        var now = DateTime.UtcNow;
        var lastFrameAtUtc = replayLastFrameAtUtcByDeathId.TryGetValue(idSuffix, out var last)
            ? last
            : now;
        replayLastFrameAtUtcByDeathId[idSuffix] = now;
        var deltaSeconds = Math.Clamp((float)(now - lastFrameAtUtc).TotalSeconds, 0.0f, 0.25f);
        if (deltaSeconds <= 0.0f)
        {
            return;
        }

        scrubSeconds = Math.Clamp(scrubSeconds + (deltaSeconds * GetReplaySpeed(idSuffix)), minOffset, maxOffset);
        replayScrubSecondsByDeathId[idSuffix] = scrubSeconds;
        if (scrubSeconds >= maxOffset - 0.001f)
        {
            replayPlayingByDeathId[idSuffix] = false;
        }
    }

    private void DrawReplaySpeedButton(string idSuffix, string label, float speed)
    {
        var selected = MathF.Abs(GetReplaySpeed(idSuffix) - speed) < 0.001f;
        if (DrawSegmentedButton(label, $"ReplaySpeed{label}{idSuffix}", selected, 46.0f))
        {
            replaySpeedByDeathId[idSuffix] = speed;
        }
    }

    private float GetReplaySpeed(string idSuffix)
    {
        return replaySpeedByDeathId.TryGetValue(idSuffix, out var speed)
            ? Math.Clamp(speed, 0.25f, 4.0f)
            : 1.0f;
    }

    private bool GetReplayShowTrails()
    {
        return configuration.ShowReplayTrails;
    }

    private float GetReplayZoom(string idSuffix)
    {
        return replayZoomByDeathId.TryGetValue(idSuffix, out var zoom)
            ? Math.Clamp(zoom, ReplayMinZoom, ReplayMaxZoom)
            : ReplayMinZoom;
    }

    private void SetReplayZoom(string idSuffix, float zoom)
    {
        var clampedZoom = Math.Clamp(zoom, ReplayMinZoom, ReplayMaxZoom);
        replayZoomByDeathId[idSuffix] = clampedZoom;
        if (clampedZoom <= ReplayMinZoom + 0.001f)
        {
            replayPanByDeathId[idSuffix] = Vector2.Zero;
        }
    }

    private Vector2 GetReplayPan(string idSuffix, float zoom, Vector2 canvasSize)
    {
        var pan = replayPanByDeathId.TryGetValue(idSuffix, out var storedPan)
            ? storedPan
            : Vector2.Zero;
        return ClampReplayPan(pan, zoom, canvasSize);
    }

    private static Vector2 ClampReplayPan(Vector2 pan, float zoom, Vector2 canvasSize)
    {
        if (zoom <= ReplayMinZoom + 0.001f)
        {
            return Vector2.Zero;
        }

        var maxPan = canvasSize * ((zoom - ReplayMinZoom) * 0.5f);
        return new Vector2(
            Math.Clamp(pan.X, -maxPan.X, maxPan.X),
            Math.Clamp(pan.Y, -maxPan.Y, maxPan.Y));
    }

    private static IReadOnlyList<ReplayPositionSnapshot> SelectReplayActorStates(
        IReadOnlyList<ReplayPositionTrack> positionTracks,
        DateTime selectedAtUtc)
    {
        var actorStates = new List<ReplayPositionSnapshot>(positionTracks.Count);
        foreach (var track in positionTracks)
        {
            var actorState = CreateReplayActorState(track.Positions, selectedAtUtc);
            if (actorState is null ||
                ShouldHideStationaryUntargetableReplayActor(track.Positions, actorState, selectedAtUtc))
            {
                continue;
            }

            actorStates.Add(actorState);
        }

        return actorStates
            .OrderBy(position => position.ActorKind)
            .ThenBy(position => position.PartyIndex)
            .ThenBy(position => position.ActorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ReplayWorldMarkerSnapshot> SelectReplayWorldMarkerStates(
        IReadOnlyList<ReplayWorldMarkerSnapshot> worldMarkers,
        DateTime selectedAtUtc)
    {
        if (worldMarkers.Count == 0)
        {
            return [];
        }

        var latestByMarker = new Dictionary<int, ReplayWorldMarkerSnapshot>();
        foreach (var marker in worldMarkers.OrderBy(marker => marker.SeenAtUtc).ThenBy(marker => marker.MarkerIndex))
        {
            if (marker.SeenAtUtc > selectedAtUtc)
            {
                break;
            }

            latestByMarker[marker.MarkerIndex] = marker;
        }

        return latestByMarker.Values
            .Where(marker => marker.Active)
            .Where(marker => float.IsFinite(marker.X) && float.IsFinite(marker.Z))
            .OrderBy(marker => marker.MarkerIndex)
            .ToList();
    }

    private static bool ShouldHideStationaryUntargetableReplayActor(
        IReadOnlyList<ReplayPositionSnapshot> actorPositions,
        ReplayPositionSnapshot actor,
        DateTime selectedAtUtc)
    {
        return actor.ActorKind == ReplayActorKind.Enemy &&
            !actor.IsTargetable &&
            !HasReplayActorMovedRecently(actorPositions, selectedAtUtc);
    }

    private static bool HasReplayActorMovedRecently(
        IReadOnlyList<ReplayPositionSnapshot> actorPositions,
        DateTime selectedAtUtc)
    {
        var endIndex = FindReplayPositionIndexAtOrBefore(actorPositions, selectedAtUtc);
        if (endIndex <= 0)
        {
            return true;
        }

        var windowStartAtUtc = selectedAtUtc.AddSeconds(-ReplayUntargetableStationaryHideSeconds);
        var latest = actorPositions[endIndex];
        if (latest.SeenAtUtc <= windowStartAtUtc)
        {
            return true;
        }

        for (var index = endIndex; index > 0; index--)
        {
            var current = actorPositions[index];
            var previous = actorPositions[index - 1];
            if ((current.SeenAtUtc - previous.SeenAtUtc).TotalSeconds > ReplayStationaryMaxSampleGapSeconds)
            {
                return true;
            }

            if (ReplayPositionsMoved(previous, current))
            {
                return true;
            }

            if (previous.SeenAtUtc <= windowStartAtUtc)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReplayPositionsMoved(ReplayPositionSnapshot previous, ReplayPositionSnapshot current)
    {
        var dx = current.X - previous.X;
        var dy = current.Y - previous.Y;
        var dz = current.Z - previous.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) > ReplayPositionMovementEpsilon * ReplayPositionMovementEpsilon;
    }

    private static ReplayPositionSnapshot? CreateReplayActorState(
        IReadOnlyList<ReplayPositionSnapshot> actorPositions,
        DateTime selectedAtUtc)
    {
        var previousIndex = FindReplayPositionIndexAtOrBefore(actorPositions, selectedAtUtc);
        var nextIndex = previousIndex >= 0 && actorPositions[previousIndex].SeenAtUtc == selectedAtUtc
            ? previousIndex
            : previousIndex + 1;
        var previous = previousIndex >= 0
            ? actorPositions[previousIndex]
            : null;
        var next = nextIndex >= 0 && nextIndex < actorPositions.Count
            ? actorPositions[nextIndex]
            : null;
        if (TrySelectNearbyActionEffectReplayPosition(actorPositions, selectedAtUtc, previousIndex, nextIndex, out var actionEffectPosition))
        {
            return actionEffectPosition;
        }

        if (previous is null)
        {
            return next is not null && next.SeenAtUtc - selectedAtUtc <= TimeSpan.FromSeconds(0.75)
                ? next
                : null;
        }

        if (next is null || next.SeenAtUtc == previous.SeenAtUtc)
        {
            return selectedAtUtc - previous.SeenAtUtc <= TimeSpan.FromSeconds(1.5)
                ? previous
                : null;
        }

        if (selectedAtUtc - previous.SeenAtUtc > TimeSpan.FromSeconds(1.5) ||
            next.SeenAtUtc - selectedAtUtc > TimeSpan.FromSeconds(1.5))
        {
            return previous;
        }

        var spanSeconds = Math.Max(0.001f, (float)(next.SeenAtUtc - previous.SeenAtUtc).TotalSeconds);
        var t = Math.Clamp((float)(selectedAtUtc - previous.SeenAtUtc).TotalSeconds / spanSeconds, 0.0f, 1.0f);
        return previous with
        {
            SeenAtUtc = selectedAtUtc,
            PullElapsedSeconds = previous.PullElapsedSeconds + ((next.PullElapsedSeconds - previous.PullElapsedSeconds) * t),
            X = previous.X + ((next.X - previous.X) * t),
            Y = previous.Y + ((next.Y - previous.Y) * t),
            Z = previous.Z + ((next.Z - previous.Z) * t),
            Rotation = LerpAngle(previous.Rotation, next.Rotation, t),
        };
    }

    private static bool TrySelectNearbyActionEffectReplayPosition(
        IReadOnlyList<ReplayPositionSnapshot> actorPositions,
        DateTime selectedAtUtc,
        int previousIndex,
        int nextIndex,
        out ReplayPositionSnapshot position)
    {
        position = default!;
        ReplayPositionSnapshot? best = null;
        var bestDistance = TimeSpan.MaxValue;
        var startIndex = Math.Max(0, Math.Min(previousIndex, nextIndex) - 2);
        var endIndex = Math.Min(actorPositions.Count - 1, Math.Max(previousIndex, nextIndex) + 2);
        for (var index = startIndex; index <= endIndex; index++)
        {
            var candidate = actorPositions[index];
            if (candidate.ActorKind == ReplayActorKind.Player ||
                !IsActionEffectReplayPositionSample(candidate))
            {
                continue;
            }

            var distance = Duration(candidate.SeenAtUtc, selectedAtUtc);
            if (distance.TotalSeconds > ReplayActionEffectSampleSnapWindowSeconds ||
                distance >= bestDistance)
            {
                continue;
            }

            best = candidate;
            bestDistance = distance;
        }

        if (best is null)
        {
            return false;
        }

        position = best;
        return true;
    }

    private static bool IsActionEffectReplayPositionSample(ReplayPositionSnapshot position)
    {
        return position.SampleSource is ReplayPositionSampleSource.ActionEffectSource or
            ReplayPositionSampleSource.ActionEffectTarget;
    }

    private static float LerpAngle(float from, float to, float t)
    {
        var delta = MathF.Atan2(MathF.Sin(to - from), MathF.Cos(to - from));
        return from + (delta * t);
    }

    private static Vector2 ReplayDirectionFromRotation(float rotation)
    {
        return new Vector2(MathF.Sin(rotation), MathF.Cos(rotation));
    }

    private static float ReplayRotationFromDirection(float x, float z)
    {
        return MathF.Atan2(x, z);
    }

    private static IReadOnlyList<ReplayMarkerSnapshot> SelectReplayMarkerStates(
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        DateTime selectedAtUtc,
        DateTime replayStartAtUtc,
        bool showEarlierMarkers,
        IReplayEncounterModule replayModule)
    {
        var eligibleMarkers = markers
            .Where(marker => marker.SeenAtUtc <= selectedAtUtc)
            .Where(marker => showEarlierMarkers || marker.SeenAtUtc >= replayStartAtUtc)
            .ToList();
        var timedP4Assignments = SelectDmuP4AssignmentMarkerStates(eligibleMarkers, selectedAtUtc);
        var normalMarkers = SelectNormalReplayMarkerStates(eligibleMarkers, markers, positions, selectedAtUtc, replayModule);

        return normalMarkers
            .Concat(timedP4Assignments)
            .Where(marker => IsReplayMarkerDisplayableAt(marker, markers, positions, selectedAtUtc, replayModule))
            .OrderBy(marker => marker.ActorKind)
            .ThenBy(marker => marker.PartyIndex)
            .ThenBy(marker => marker.ActorName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(marker => marker.SeenAtUtc)
            .ThenBy(marker => GetReplayMarkerDisplayExpiresAtUtc(marker, replayModule) ?? DateTime.MaxValue)
            .ThenBy(marker => marker.MarkerId)
            .ToList();
    }

    private static IReadOnlyList<ReplayMarkerSnapshot> SelectNormalReplayMarkerStates(
        IReadOnlyList<ReplayMarkerSnapshot> eligibleMarkers,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        DateTime selectedAtUtc,
        IReplayEncounterModule replayModule)
    {
        var selected = new List<ReplayMarkerSnapshot>();
        foreach (var actorMarkers in eligibleMarkers
            .Where(marker => !ReplayEncounterModules.IsDmuP4AssignmentMarker(marker.MarkerId))
            .GroupBy(marker => marker.ActorKey, StringComparer.Ordinal))
        {
            var displayableCluster = SelectLatestReplayMarkerCluster(actorMarkers)
                .Where(marker => IsReplayMarkerDisplayableAt(marker, allMarkers, positions, selectedAtUtc, replayModule))
                .OrderByDescending(marker => marker.SeenAtUtc)
                .ThenBy(marker => GetReplayMarkerDisplayExpiresAtUtc(marker, replayModule) ?? DateTime.MaxValue)
                .ThenBy(marker => marker.MarkerId)
                .Take(MaxReplayMarkerBadgesPerActor)
                .ToList();
            selected.AddRange(displayableCluster);
        }

        return selected;
    }

    private static IReadOnlyList<ReplayMarkerSnapshot> SelectLatestReplayMarkerCluster(IEnumerable<ReplayMarkerSnapshot> actorMarkers)
    {
        var ordered = actorMarkers
            .OrderByDescending(marker => marker.SeenAtUtc)
            .ThenBy(marker => marker.MarkerId)
            .ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        var newestSeenAtUtc = ordered[0].SeenAtUtc;
        var clusterStartAtUtc = newestSeenAtUtc.AddSeconds(-ReplayMarkerBadgeStackWindowSeconds);
        return ordered
            .Where(marker => marker.SeenAtUtc >= clusterStartAtUtc)
            .GroupBy(GetReplayMarkerStackKey, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(marker => marker.SeenAtUtc).First())
            .OrderByDescending(marker => marker.SeenAtUtc)
            .ThenBy(marker => marker.MarkerId)
            .ToList();
    }

    private static string GetReplayMarkerStackKey(ReplayMarkerSnapshot marker)
    {
        return $"{marker.MarkerId}:{marker.RawMarkerId}";
    }

    private static IReadOnlyList<ReplayMarkerSnapshot> SelectDmuP4AssignmentMarkerStates(
        IReadOnlyList<ReplayMarkerSnapshot> eligibleMarkers,
        DateTime selectedAtUtc)
    {
        var selected = new List<ReplayMarkerSnapshot>();
        foreach (var actorMarkers in eligibleMarkers
            .Where(marker => ReplayEncounterModules.IsDmuP4AssignmentMarker(marker.MarkerId))
            .GroupBy(marker => marker.ActorKey, StringComparer.Ordinal))
        {
            var latestByStatus = actorMarkers
                .GroupBy(marker => marker.MarkerId)
                .Select(group => group.OrderByDescending(marker => marker.SeenAtUtc).First())
                .ToList();
            var activeTimedMarkers = latestByStatus
                .Select(marker => (Marker: marker, ExpiresAtUtc: GetReplayMarkerExpiresAtUtc(marker)))
                .Where(entry => entry.ExpiresAtUtc is { } expiresAtUtc && selectedAtUtc <= expiresAtUtc)
                .OrderBy(entry => entry.ExpiresAtUtc)
                .ThenBy(entry => entry.Marker.SeenAtUtc)
                .ThenBy(entry => entry.Marker.MarkerId)
                .ToList();
            if (activeTimedMarkers.Count > 0)
            {
                var soonestExpireAtUtc = activeTimedMarkers[0].ExpiresAtUtc!.Value;
                selected.AddRange(activeTimedMarkers
                    .Where(entry => Math.Abs((entry.ExpiresAtUtc!.Value - soonestExpireAtUtc).TotalSeconds) <= ReplayP4AssignmentSharedResolveWindowSeconds)
                    .Select(entry => entry.Marker));
                continue;
            }

            var legacyFallback = actorMarkers
                .OrderByDescending(marker => marker.SeenAtUtc)
                .ThenBy(marker => marker.MarkerId)
                .FirstOrDefault();
            if (legacyFallback is not null)
            {
                selected.Add(legacyFallback);
            }
        }

        return selected;
    }

    private static DateTime? GetReplayMarkerExpiresAtUtc(ReplayMarkerSnapshot marker)
    {
        return marker.RemainingTime > 0.01f
            ? marker.SeenAtUtc.AddSeconds(marker.RemainingTime)
            : null;
    }

    private static DateTime? GetReplayMarkerDisplayExpiresAtUtc(
        ReplayMarkerSnapshot marker,
        IReplayEncounterModule replayModule)
    {
        if (GetReplayMarkerExpiresAtUtc(marker) is { } expiresAtUtc)
        {
            return expiresAtUtc;
        }

        if (!replayModule.TryGetMarkerInfo(marker.MarkerId, out var markerInfo))
        {
            return null;
        }

        if (ReplayEncounterModules.IsDmuP1FireMarker(marker.MarkerId) ||
            ReplayEncounterModules.IsDmuP1MysteryMagicMarker(marker.MarkerId) ||
            markerInfo.Shape is not null)
        {
            return marker.SeenAtUtc.AddSeconds(Math.Max(0.05f, markerInfo.DurationSeconds));
        }

        return null;
    }

    private static bool IsReplayMarkerDisplayableAt(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        DateTime selectedAtUtc,
        IReplayEncounterModule replayModule)
    {
        if (marker.SeenAtUtc > selectedAtUtc ||
            IsReplayMarkerExpiredAt(marker, selectedAtUtc, replayModule))
        {
            return false;
        }

        if (ReplayEncounterModules.IsDmuP1FireMarker(marker.MarkerId) &&
            ReplayEncounterModules.TryResolveDmuP1FireMarkerInfo(marker, markers, default, out _))
        {
            return false;
        }

        return replayModule.ShouldDisplayReplayMarker(marker, markers, positions, selectedAtUtc);
    }

    private static bool IsReplayMarkerExpiredAt(
        ReplayMarkerSnapshot marker,
        DateTime selectedAtUtc,
        IReplayEncounterModule replayModule)
    {
        return GetReplayMarkerDisplayExpiresAtUtc(marker, replayModule) is { } expiresAtUtc &&
            selectedAtUtc > expiresAtUtc;
    }

    private static IReadOnlyList<ReplayMechanicSnapshot> SelectReplayMechanicStates(
        IReadOnlyList<ReplayMechanicSnapshot> mechanics,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayPositionSnapshot> actorStates,
        DateTime selectedAtUtc,
        IReplayEncounterModule replayModule)
    {
        return mechanics
            .Where(mechanic => mechanic.SeenAtUtc <= selectedAtUtc)
            .Where(mechanic => selectedAtUtc <= mechanic.SeenAtUtc.AddSeconds(Math.Max(0.05f, mechanic.DurationSeconds)))
            .Where(mechanic => ShouldDisplayReplayMechanic(mechanic, markers, positions, selectedAtUtc, replayModule))
            .Select(mechanic => ProjectReplayMechanicToActorState(mechanic, actorStates, replayModule))
            .OrderBy(mechanic => mechanic.SeenAtUtc)
            .ThenBy(mechanic => mechanic.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mechanic => mechanic.RawEventKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mechanic => mechanic.RawEventId)
            .ToList();
    }

    private static bool ShouldDisplayReplayMechanic(
        ReplayMechanicSnapshot mechanic,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        DateTime selectedAtUtc,
        IReplayEncounterModule replayModule)
    {
        if (!IsReplayMarkerMechanic(mechanic))
        {
            return true;
        }

        var marker = FindReplayMarkerForMechanic(mechanic, markers);
        return marker is null ||
            replayModule.ShouldDisplayReplayMarker(marker, markers, positions, selectedAtUtc);
    }

    private static ReplayMarkerSnapshot? FindReplayMarkerForMechanic(
        ReplayMechanicSnapshot mechanic,
        IReadOnlyList<ReplayMarkerSnapshot> markers)
    {
        return TryGetReplayMarkerMechanicIdentity(mechanic, out var actorKey, out var rawMarkerId, out var seenAtTicks)
            ? markers.FirstOrDefault(marker =>
                string.Equals(marker.ActorKey, actorKey, StringComparison.Ordinal) &&
                marker.RawMarkerId == rawMarkerId &&
                marker.SeenAtUtc.Ticks == seenAtTicks)
            : null;
    }

    private static ReplayMechanicSnapshot ProjectReplayMechanicToActorState(
        ReplayMechanicSnapshot mechanic,
        IReadOnlyList<ReplayPositionSnapshot> actorStates,
        IReplayEncounterModule replayModule)
    {
        if (IsReplayTetherMechanic(mechanic))
        {
            return ProjectReplayTetherMechanicToActorState(mechanic, actorStates);
        }

        if (IsReplayDmuP1FlagrantFireMechanic(mechanic) &&
            TryGetReplayDmuP1FlagrantFireActorKey(mechanic, out var fireActorKey) &&
            actorStates.FirstOrDefault(actor => string.Equals(actor.ActorKey, fireActorKey, StringComparison.Ordinal)) is { } fireActor)
        {
            return mechanic with
            {
                X = fireActor.X,
                Y = fireActor.Y,
                Z = fireActor.Z,
                Rotation = fireActor.Rotation,
            };
        }

        if (!IsReplayMarkerMechanic(mechanic) ||
            !replayModule.TryGetMarkerInfo(mechanic.RawEventId, out var markerInfo) ||
            !markerInfo.AnchorsToActor ||
            !TryFindReplayMechanicSourceActor(mechanic, actorStates, out var sourceActor))
        {
            return mechanic;
        }

        var rotation = sourceActor.Rotation;
        if (mechanic.Shape == ReplayMechanicShape.Cone &&
            markerInfo.ConeBaitsClosestPlayer &&
            TryFindClosestReplayPlayer(sourceActor, actorStates, out var targetActor))
        {
            rotation = ReplayRotationFromDirection(targetActor.X - sourceActor.X, targetActor.Z - sourceActor.Z);
        }

        return mechanic with
        {
            X = sourceActor.X,
            Y = sourceActor.Y,
            Z = sourceActor.Z,
            Rotation = rotation,
        };
    }

    private static ReplayMechanicSnapshot ProjectReplayTetherMechanicToActorState(
        ReplayMechanicSnapshot mechanic,
        IReadOnlyList<ReplayPositionSnapshot> actorStates)
    {
        if (!TryGetReplayTetherEntityIds(mechanic, out var sourceEntityId, out var targetEntityId))
        {
            return mechanic;
        }

        var capturedSource = GetReplayTetherStart(mechanic);
        var source = actorStates.FirstOrDefault(actor => actor.EntityId == sourceEntityId) is { } sourceActor
            ? new Vector3(sourceActor.X, sourceActor.Y, sourceActor.Z)
            : capturedSource;
        var target = actorStates.FirstOrDefault(actor => actor.EntityId == targetEntityId) is { } targetActor
            ? new Vector3(targetActor.X, targetActor.Y, targetActor.Z)
            : GetReplayTetherEnd(mechanic);
        if (!float.IsFinite(source.X) ||
            !float.IsFinite(source.Z) ||
            !float.IsFinite(target.X) ||
            !float.IsFinite(target.Z))
        {
            return mechanic;
        }

        var dx = target.X - source.X;
        var dz = target.Z - source.Z;
        var distance = MathF.Sqrt((dx * dx) + (dz * dz));
        if (distance <= 0.05f)
        {
            return mechanic;
        }

        return mechanic with
        {
            X = source.X + (dx * 0.5f),
            Y = (source.Y + target.Y) * 0.5f,
            Z = source.Z + (dz * 0.5f),
            Rotation = ReplayRotationFromDirection(dx, dz),
            Length = distance,
        };
    }

    private static bool TryGetReplayTetherEntityIds(
        ReplayMechanicSnapshot mechanic,
        out uint sourceEntityId,
        out uint targetEntityId)
    {
        sourceEntityId = 0;
        targetEntityId = mechanic.RawState;
        var parts = mechanic.SourceKey.Split(':');
        if (parts.Length >= 2 &&
            uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedSourceEntityId))
        {
            sourceEntityId = parsedSourceEntityId;
        }

        if (targetEntityId == 0 &&
            parts.Length >= 3 &&
            uint.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedTargetEntityId))
        {
            targetEntityId = parsedTargetEntityId;
        }

        return sourceEntityId != 0 || targetEntityId != 0;
    }

    private static Vector3 GetReplayTetherStart(ReplayMechanicSnapshot mechanic)
    {
        var halfLength = Math.Max(0.1f, mechanic.Length) * 0.5f;
        var direction = ReplayDirectionFromRotation(mechanic.Rotation);
        return new Vector3(
            mechanic.X - (direction.X * halfLength),
            mechanic.Y,
            mechanic.Z - (direction.Y * halfLength));
    }

    private static Vector3 GetReplayTetherEnd(ReplayMechanicSnapshot mechanic)
    {
        var halfLength = Math.Max(0.1f, mechanic.Length) * 0.5f;
        var direction = ReplayDirectionFromRotation(mechanic.Rotation);
        return new Vector3(
            mechanic.X + (direction.X * halfLength),
            mechanic.Y,
            mechanic.Z + (direction.Y * halfLength));
    }

    private static bool IsReplayTetherMechanic(ReplayMechanicSnapshot mechanic)
    {
        return mechanic.Shape == ReplayMechanicShape.Tether;
    }

    private static bool IsReplayMarkerMechanic(ReplayMechanicSnapshot mechanic)
    {
        return string.Equals(mechanic.RawEventKind, "target-icon", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReplayDmuP1FlagrantFireMechanic(ReplayMechanicSnapshot mechanic)
    {
        return string.Equals(mechanic.RawEventKind, ReplayDmuP1FlagrantFireRawEventKind, StringComparison.Ordinal);
    }

    private static bool TryGetReplayDmuP1FlagrantFireActorKey(
        ReplayMechanicSnapshot mechanic,
        out string actorKey)
    {
        actorKey = string.Empty;
        const string separator = ":";
        var prefix = ReplayDmuP1FlagrantFireRawEventKind + separator;
        if (!mechanic.SourceKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = mechanic.SourceKey[prefix.Length..];
        var ticksSeparator = payload.IndexOf(':');
        if (ticksSeparator <= 0 ||
            !long.TryParse(payload[..ticksSeparator], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        actorKey = payload[(ticksSeparator + 1)..];
        return !string.IsNullOrWhiteSpace(actorKey);
    }

    private static string GetReplayMarkerMechanicSourceKey(ReplayMarkerSnapshot marker)
    {
        return $"marker:{marker.ActorKey}:{marker.RawMarkerId}:{marker.SeenAtUtc.Ticks}";
    }

    private static HashSet<string> GetReplayMarkerMechanicSourceKeysWithVisibleBadges(
        IReadOnlyList<ReplayMarkerSnapshot> markerStates,
        IReadOnlyList<ReplayPositionSnapshot> actorStates)
    {
        if (markerStates.Count == 0 || actorStates.Count == 0)
        {
            return [];
        }

        var visibleActorKeys = actorStates
            .Select(actor => actor.ActorKey)
            .ToHashSet(StringComparer.Ordinal);
        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var marker in markerStates)
        {
            if (visibleActorKeys.Contains(marker.ActorKey))
            {
                sourceKeys.Add(GetReplayMarkerMechanicSourceKey(marker));
            }
        }

        return sourceKeys;
    }

    private static bool TryFindReplayMechanicSourceActor(
        ReplayMechanicSnapshot mechanic,
        IReadOnlyList<ReplayPositionSnapshot> actorStates,
        out ReplayPositionSnapshot sourceActor)
    {
        var actorKey = TryGetReplayMarkerMechanicActorKey(mechanic);
        var resolvedSource = actorKey is null
            ? actorStates.FirstOrDefault(actor => actor.EntityId == mechanic.RawState)
            : actorStates.FirstOrDefault(actor => string.Equals(actor.ActorKey, actorKey, StringComparison.Ordinal)) ??
                actorStates.FirstOrDefault(actor => actor.EntityId == mechanic.RawState);

        if (resolvedSource is null)
        {
            sourceActor = null!;
            return false;
        }

        sourceActor = resolvedSource;
        return true;
    }

    private static string? TryGetReplayMarkerMechanicActorKey(ReplayMechanicSnapshot mechanic)
    {
        return TryGetReplayMarkerMechanicIdentity(mechanic, out var actorKey, out _, out _)
            ? actorKey
            : null;
    }

    private static bool TryGetReplayMarkerMechanicIdentity(
        ReplayMechanicSnapshot mechanic,
        out string actorKey,
        out uint rawMarkerId,
        out long seenAtTicks)
    {
        actorKey = string.Empty;
        rawMarkerId = 0;
        seenAtTicks = 0;
        const string prefix = "marker:";
        if (!mechanic.SourceKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = mechanic.SourceKey[prefix.Length..];
        var lastSeparator = payload.LastIndexOf(':');
        if (lastSeparator <= 0)
        {
            return false;
        }

        var previousSeparator = payload.LastIndexOf(':', lastSeparator - 1);
        if (previousSeparator <= 0 ||
            !uint.TryParse(payload[(previousSeparator + 1)..lastSeparator], out rawMarkerId) ||
            !long.TryParse(payload[(lastSeparator + 1)..], out seenAtTicks))
        {
            return false;
        }

        actorKey = payload[..previousSeparator];
        return true;
    }

    private static bool TryFindClosestReplayPlayer(
        ReplayPositionSnapshot sourceActor,
        IReadOnlyList<ReplayPositionSnapshot> actorStates,
        out ReplayPositionSnapshot targetActor)
    {
        var resolvedTarget = actorStates
            .Where(actor => actor.ActorKind == ReplayActorKind.Player)
            .Where(actor => !string.Equals(actor.ActorKey, sourceActor.ActorKey, StringComparison.Ordinal))
            .Where(actor => float.IsFinite(actor.X) && float.IsFinite(actor.Z))
            .OrderBy(actor => DistanceSquared(sourceActor.X, sourceActor.Z, actor.X, actor.Z))
            .FirstOrDefault();

        if (resolvedTarget is null)
        {
            targetActor = null!;
            return false;
        }

        targetActor = resolvedTarget;
        return true;
    }

    private static float DistanceSquared(float leftX, float leftZ, float rightX, float rightZ)
    {
        var x = leftX - rightX;
        var z = leftZ - rightZ;
        return (x * x) + (z * z);
    }

    private static Vector2 GetRightPaddedTableSize(float rightPadding)
    {
        var width = ImGui.GetContentRegionAvail().X;
        return new Vector2(MathF.Max(1.0f, width - MathF.Min(rightPadding, MathF.Max(0.0f, width - 1.0f))), 0.0f);
    }

    private static void DrawTableText(int column, string text, bool wrap = true, bool disabled = false)
    {
        ImGui.TableSetColumnIndex(column);
        if (disabled)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, DisabledColor);
            if (wrap)
            {
                ImGui.TextWrapped(text);
            }
            else
            {
                ImGui.TextUnformatted(text);
            }

            ImGui.PopStyleColor();
        }
        else if (wrap)
        {
            ImGui.TextWrapped(text);
        }
        else
        {
            ImGui.TextUnformatted(text);
        }
    }

    private void DrawDeathReplayCanvas(
        PartyDeathRecord? focusDeath,
        IReadOnlyList<ReplayPositionSnapshot> allPositions,
        IReadOnlyList<ReplayPositionTrack> positionTracks,
        IReadOnlyList<ReplayMechanicSnapshot> allMechanics,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers,
        IReadOnlyList<ReplayPositionSnapshot> actorStates,
        IReadOnlyList<ReplayMarkerSnapshot> markerStates,
        IReadOnlyList<ReplayMechanicSnapshot> mechanicStates,
        IReadOnlyList<ReplayWorldMarkerSnapshot> allWorldMarkers,
        IReadOnlyList<ReplayWorldMarkerSnapshot> worldMarkerStates,
        DateTime selectedAtUtc,
        string idSuffix,
        IReplayEncounterModule replayModule,
        bool showTrails)
    {
        var cursorStart = ImGui.GetCursorScreenPos();
        var availableWidth = MathF.Max(ReplayCanvasMinSide, ImGui.GetContentRegionAvail().X);
        var horizontalGutter = MathF.Min(
            ReplayCanvasHorizontalGutter,
            MathF.Max(0.0f, availableWidth - ReplayCanvasMinSide) * 0.5f);
        var canvasSide = MathF.Min(availableWidth - (horizontalGutter * 2.0f), ReplayCanvasMaxSide);
        var canvasX = cursorStart.X + MathF.Max(0.0f, (availableWidth - canvasSide) * 0.5f);

        var canvasSize = new Vector2(canvasSide, canvasSide);
        ImGui.SetCursorScreenPos(new Vector2(canvasX, cursorStart.Y));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var canvasChildVisible = ImGui.BeginChild($"##DeathReplayCanvasScrollBlock{idSuffix}", canvasSize, false, ReplayCanvasChildFlags);
        ImGui.PopStyleVar();
        if (!canvasChildVisible)
        {
            ImGui.EndChild();
            RestoreCursorAfterReplayCanvas(cursorStart);
            return;
        }

        ImGui.SetScrollY(0.0f);
        var canvasStart = ImGui.GetCursorScreenPos();
        var zoom = GetReplayZoom(idSuffix);
        var pan = GetReplayPan(idSuffix, zoom, canvasSize);
        var zoomOverlayRect = GetReplayZoomOverlayRect(canvasStart, canvasSize);
        var canvasWindowHovered = ImGui.IsWindowHovered();
        var zoomOverlayHovered = canvasWindowHovered && IsMouseInsideRect(zoomOverlayRect.Start, zoomOverlayRect.Size);
        var canvasHovered = canvasWindowHovered && IsMouseInsideRect(canvasStart, canvasSize);
        var canvasInputState = DrawReplayCanvasInputRegions(idSuffix, canvasStart, canvasSize, zoomOverlayRect);
        var canvasInputHovered = canvasInputState.Hovered && !zoomOverlayHovered;
        HandleReplayCanvasWheelZoom(idSuffix, canvasHovered, canvasStart, canvasSize, ref zoom, ref pan);
        HandleReplayCanvasPan(idSuffix, canvasInputState.Active, zoom, canvasSize, ref pan);

        var drawList = ImGui.GetWindowDrawList();
        var canvasEnd = canvasStart + canvasSize;
        drawList.AddRectFilled(canvasStart, canvasEnd, ImGui.GetColorU32(ModernPanelAltColor with { W = 0.62f }), 5.0f);
        drawList.AddRect(canvasStart, canvasEnd, ImGui.GetColorU32(ModernPanelBorderColor), 5.0f);

        var boundsMechanics = allPositions.Count == 0
            ? allMechanics
            : mechanicStates;
        if (!TryGetReplayBounds(replayModule, allPositions, boundsMechanics, allWorldMarkers, out var minX, out var maxX, out var minZ, out var maxZ))
        {
            DrawCenteredCanvasText(drawList, canvasStart, canvasSize, "Replay positions could not be mapped.");
            AddReplayCanvasWheelScrollSink(canvasStart, canvasSize);
            ImGui.EndChild();
            RestoreCursorAfterReplayCanvas(cursorStart);
            return;
        }

        var focusedActorKey = replayFocusedActorKeyByDeathId.TryGetValue(idSuffix, out var focusedKey)
            ? focusedKey
            : null;
        ImGui.PushClipRect(canvasStart, canvasEnd, true);
        DrawReplayGrid(drawList, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan);
        DrawReplayWorldMarkers(drawList, worldMarkerStates, GetReplayWorldMarkerOpacity(), canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan);
        var markerMechanicSourceKeysWithVisibleBadges = GetReplayMarkerMechanicSourceKeysWithVisibleBadges(markerStates, actorStates);
        DrawReplayMechanics(drawList, mechanicStates, markerMechanicSourceKeysWithVisibleBadges, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan);
        if (showTrails)
        {
            var visibleActorKeys = actorStates
                .Select(actor => actor.ActorKey)
                .ToHashSet(StringComparer.Ordinal);
            DrawReplayTrails(drawList, focusDeath, positionTracks, visibleActorKeys, selectedAtUtc, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan);
        }

        var markersByActor = markerStates
            .GroupBy(marker => marker.ActorKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ReplayMarkerSnapshot>)group.ToList(), StringComparer.Ordinal);
        var actorScreenPositions = new List<ReplayActorScreenState>();
        foreach (var actor in actorStates.Where(actor => actor.ActorKind == ReplayActorKind.Enemy && !IsFocusedReplayActor(actor, focusedActorKey)))
        {
            markersByActor.TryGetValue(actor.ActorKey, out var markers);
            DrawReplayActor(drawList, focusDeath, actor, markers ?? [], allMarkers, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan, actorScreenPositions, canvasInputHovered, replayModule, focused: false);
        }

        foreach (var actor in actorStates.Where(actor => actor.ActorKind == ReplayActorKind.Player && !IsFocusedReplayActor(actor, focusedActorKey)))
        {
            markersByActor.TryGetValue(actor.ActorKey, out var markers);
            DrawReplayActor(drawList, focusDeath, actor, markers ?? [], allMarkers, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan, actorScreenPositions, canvasInputHovered, replayModule, focused: false);
        }

        var focusedActor = actorStates.FirstOrDefault(actor => IsFocusedReplayActor(actor, focusedActorKey));
        if (focusedActor is not null)
        {
            markersByActor.TryGetValue(focusedActor.ActorKey, out var focusedMarkers);
            DrawReplayActor(drawList, focusDeath, focusedActor, focusedMarkers ?? [], allMarkers, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan, actorScreenPositions, canvasInputHovered, replayModule, focused: true);
        }

        ImGui.PopClipRect();
        DrawDeathReplayZoomOverlay(idSuffix, canvasStart, canvasSize);
        HandleReplayCanvasFocus(idSuffix, canvasInputHovered, actorScreenPositions);

        AddReplayCanvasWheelScrollSink(canvasStart, canvasSize);
        ImGui.EndChild();

        RestoreCursorAfterReplayCanvas(cursorStart);
    }

    private static void AddReplayCanvasWheelScrollSink(Vector2 canvasStart, Vector2 canvasSize)
    {
        var cursorBefore = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(canvasStart);
        ImGui.Dummy(new Vector2(canvasSize.X, canvasSize.Y + ReplayWheelScrollSinkHeight));
        ImGui.SetCursorScreenPos(cursorBefore);
    }

    private ReplayCanvasInputState DrawReplayCanvasInputRegions(
        string idSuffix,
        Vector2 canvasStart,
        Vector2 canvasSize,
        (Vector2 Start, Vector2 Size) excludedRect)
    {
        var cursorBefore = ImGui.GetCursorScreenPos();
        var canvasEnd = canvasStart + canvasSize;
        var excludedStart = new Vector2(
            Math.Clamp(excludedRect.Start.X, canvasStart.X, canvasEnd.X),
            Math.Clamp(excludedRect.Start.Y, canvasStart.Y, canvasEnd.Y));
        var excludedEnd = new Vector2(
            Math.Clamp(excludedRect.Start.X + excludedRect.Size.X, canvasStart.X, canvasEnd.X),
            Math.Clamp(excludedRect.Start.Y + excludedRect.Size.Y, canvasStart.Y, canvasEnd.Y));
        var hovered = false;
        var active = false;
        var regionIndex = 0;

        SubmitReplayCanvasInputRegion(idSuffix, regionIndex++, canvasStart, new Vector2(canvasEnd.X, excludedStart.Y), ref hovered, ref active);
        SubmitReplayCanvasInputRegion(idSuffix, regionIndex++, new Vector2(canvasStart.X, excludedStart.Y), new Vector2(excludedStart.X, excludedEnd.Y), ref hovered, ref active);
        SubmitReplayCanvasInputRegion(idSuffix, regionIndex++, new Vector2(excludedEnd.X, excludedStart.Y), new Vector2(canvasEnd.X, excludedEnd.Y), ref hovered, ref active);
        SubmitReplayCanvasInputRegion(idSuffix, regionIndex, new Vector2(canvasStart.X, excludedEnd.Y), canvasEnd, ref hovered, ref active);

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            replayCanvasPressedByDeathId.Remove(idSuffix);
        }

        ImGui.SetCursorScreenPos(cursorBefore);
        return new ReplayCanvasInputState(hovered, active);
    }

    private void SubmitReplayCanvasInputRegion(
        string idSuffix,
        int regionIndex,
        Vector2 regionStart,
        Vector2 regionEnd,
        ref bool hovered,
        ref bool active)
    {
        var regionSize = regionEnd - regionStart;
        if (regionSize.X <= 1.0f || regionSize.Y <= 1.0f)
        {
            return;
        }

        ImGui.SetCursorScreenPos(regionStart);
        var clicked = ImGui.InvisibleButton($"##DeathReplayCanvasInput{idSuffix}{regionIndex}", regionSize);
        hovered |= ImGui.IsItemHovered();
        active |= ImGui.IsItemActive();
        if (clicked || ImGui.IsItemActivated())
        {
            replayCanvasPressedByDeathId.Add(idSuffix);
        }
    }

    private void DrawDeathReplayZoomOverlay(string idSuffix, Vector2 canvasStart, Vector2 canvasSize)
    {
        var (overlayStart, overlaySize) = GetReplayZoomOverlayRect(canvasStart, canvasSize);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(overlayStart, overlayStart + overlaySize, ImGui.GetColorU32(ModernPanelColor with { W = 0.82f }), 6.0f);
        drawList.AddRect(overlayStart, overlayStart + overlaySize, ImGui.GetColorU32(ModernPanelBorderColor), 6.0f);

        var cursorBefore = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(overlayStart + new Vector2(8.0f, 5.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6.0f, 2.0f));
        ImGui.PushStyleColor(ImGuiCol.Text, ModernTextColor);
        var zoom = GetReplayZoom(idSuffix);
        ImGui.TextUnformatted("Zoom");
        ImGui.SameLine(0.0f, 7.0f);
        ImGui.SetNextItemWidth(MathF.Max(72.0f, overlaySize.X - 62.0f));
        if (ImGui.SliderFloat($"##DeathReplayZoom{idSuffix}", ref zoom, ReplayMinZoom, ReplayMaxZoom, $"{zoom:0.0}x"))
        {
            SetReplayZoom(idSuffix, zoom);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Zooms the replay view. You can also use the mouse wheel over the replay square.");
        }

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        ImGui.SetCursorScreenPos(cursorBefore);
    }

    private static (Vector2 Start, Vector2 Size) GetReplayZoomOverlayRect(Vector2 canvasStart, Vector2 canvasSize)
    {
        var maxWidth = MathF.Max(128.0f, canvasSize.X - (ReplayZoomOverlayPadding * 2.0f));
        var width = MathF.Min(Math.Clamp(canvasSize.X * 0.42f, 158.0f, ReplayZoomSliderWidth + 58.0f), maxWidth);
        return (canvasStart + new Vector2(ReplayZoomOverlayPadding, ReplayZoomOverlayPadding), new Vector2(width, ReplayZoomOverlayHeight));
    }

    private static bool IsMouseInsideRect(Vector2 start, Vector2 size)
    {
        var mouse = ImGui.GetIO().MousePos;
        return mouse.X >= start.X &&
            mouse.X <= start.X + size.X &&
            mouse.Y >= start.Y &&
            mouse.Y <= start.Y + size.Y;
    }

    private void HandleReplayCanvasWheelZoom(
        string idSuffix,
        bool canvasHovered,
        Vector2 canvasStart,
        Vector2 canvasSize,
        ref float zoom,
        ref Vector2 pan)
    {
        if (!canvasHovered)
        {
            return;
        }

        var wheel = ImGui.GetIO().MouseWheel;
        if (MathF.Abs(wheel) <= 0.001f)
        {
            return;
        }

        var oldZoom = Math.Clamp(zoom, ReplayMinZoom, ReplayMaxZoom);
        var newZoom = Math.Clamp(oldZoom + (wheel * 0.18f), ReplayMinZoom, ReplayMaxZoom);
        if (MathF.Abs(newZoom - oldZoom) <= 0.001f)
        {
            return;
        }

        var center = canvasStart + (canvasSize * 0.5f);
        var mouse = ImGui.GetIO().MousePos;
        var zoomRatio = newZoom / oldZoom;
        pan = newZoom <= ReplayMinZoom + 0.001f
            ? Vector2.Zero
            : ClampReplayPan(((mouse - center) * (1.0f - zoomRatio)) + (pan * zoomRatio), newZoom, canvasSize);
        replayZoomByDeathId[idSuffix] = newZoom;
        replayPanByDeathId[idSuffix] = pan;
        zoom = newZoom;
    }

    private void HandleReplayCanvasPan(string idSuffix, bool canvasActive, float zoom, Vector2 canvasSize, ref Vector2 pan)
    {
        if (!canvasActive || zoom <= ReplayMinZoom + 0.001f || !ImGui.IsMouseDragging(ImGuiMouseButton.Left, 3.0f))
        {
            return;
        }

        pan = ClampReplayPan(pan + ImGui.GetIO().MouseDelta, zoom, canvasSize);
        replayPanByDeathId[idSuffix] = pan;
        replayCanvasDraggedByDeathId.Add(idSuffix);
    }

    private void HandleReplayCanvasFocus(
        string idSuffix,
        bool canvasHovered,
        IReadOnlyList<ReplayActorScreenState> actorScreenPositions)
    {
        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            return;
        }

        var pressStartedOnCanvas = replayCanvasPressedByDeathId.Remove(idSuffix);
        if (!pressStartedOnCanvas ||
            replayCanvasDraggedByDeathId.Remove(idSuffix) ||
            !canvasHovered)
        {
            return;
        }

        var dragDelta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left);
        if (dragDelta.LengthSquared() > 16.0f)
        {
            return;
        }

        var mouse = ImGui.GetIO().MousePos;
        var clickedPlayer = actorScreenPositions
            .Where(entry => entry.Actor.ActorKind == ReplayActorKind.Player)
            .Where(entry => Vector2.Distance(mouse, entry.ScreenPosition) <= entry.InteractionRadius)
            .OrderBy(entry => Vector2.Distance(mouse, entry.ScreenPosition))
            .FirstOrDefault();
        if (clickedPlayer is not null)
        {
            replayFocusedActorKeyByDeathId[idSuffix] = clickedPlayer.Actor.ActorKey;
            return;
        }

        replayFocusedActorKeyByDeathId.Remove(idSuffix);
    }

    private static bool IsFocusedReplayActor(ReplayPositionSnapshot actor, string? focusedActorKey)
    {
        return !string.IsNullOrWhiteSpace(focusedActorKey) &&
            string.Equals(actor.ActorKey, focusedActorKey, StringComparison.Ordinal);
    }

    private static void RestoreCursorAfterReplayCanvas(Vector2 cursorStart)
    {
        var cursorAfterCanvas = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(cursorStart.X, cursorAfterCanvas.Y));
    }

    private static bool TryGetReplayBounds(
        IReplayEncounterModule replayModule,
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMechanicSnapshot> mechanics,
        IReadOnlyList<ReplayWorldMarkerSnapshot> worldMarkers,
        out float minX,
        out float maxX,
        out float minZ,
        out float maxZ)
    {
        if (replayModule.TryGetReplayArena(out var arena) &&
            TryGetKnownReplayArenaBounds(arena, out minX, out maxX, out minZ, out maxZ))
        {
            return true;
        }

        return TryGetInferredReplayBounds(positions, mechanics, worldMarkers, out minX, out maxX, out minZ, out maxZ);
    }

    private static bool TryGetKnownReplayArenaBounds(
        ReplayArenaInfo arena,
        out float minX,
        out float maxX,
        out float minZ,
        out float maxZ)
    {
        minX = maxX = minZ = maxZ = 0.0f;
        if (!float.IsFinite(arena.CenterX) ||
            !float.IsFinite(arena.CenterZ) ||
            !float.IsFinite(arena.Radius) ||
            arena.Radius <= 0.1f)
        {
            return false;
        }

        minX = arena.CenterX - arena.Radius;
        maxX = arena.CenterX + arena.Radius;
        minZ = arena.CenterZ - arena.Radius;
        maxZ = arena.CenterZ + arena.Radius;
        return true;
    }

    private static bool TryGetInferredReplayBounds(
        IReadOnlyList<ReplayPositionSnapshot> positions,
        IReadOnlyList<ReplayMechanicSnapshot> mechanics,
        IReadOnlyList<ReplayWorldMarkerSnapshot> worldMarkers,
        out float minX,
        out float maxX,
        out float minZ,
        out float maxZ)
    {
        var hasBounds = false;
        minX = maxX = minZ = maxZ = 0.0f;
        foreach (var position in positions)
        {
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Z))
            {
                continue;
            }

            IncludeReplayBounds(position.X, position.X, position.Z, position.Z, ref hasBounds, ref minX, ref maxX, ref minZ, ref maxZ);
        }

        foreach (var mechanic in mechanics)
        {
            if (!float.IsFinite(mechanic.X) || !float.IsFinite(mechanic.Z))
            {
                continue;
            }

            var radius = GetReplayMechanicBoundsRadius(mechanic);
            IncludeReplayBounds(mechanic.X - radius, mechanic.X + radius, mechanic.Z - radius, mechanic.Z + radius, ref hasBounds, ref minX, ref maxX, ref minZ, ref maxZ);
        }

        foreach (var marker in worldMarkers)
        {
            if (!marker.Active ||
                !float.IsFinite(marker.X) ||
                !float.IsFinite(marker.Z))
            {
                continue;
            }

            IncludeReplayBounds(marker.X, marker.X, marker.Z, marker.Z, ref hasBounds, ref minX, ref maxX, ref minZ, ref maxZ);
        }

        if (!hasBounds)
        {
            minX = maxX = minZ = maxZ = 0.0f;
            return false;
        }

        if (maxX - minX < 1.0f)
        {
            minX -= 1.0f;
            maxX += 1.0f;
        }

        if (maxZ - minZ < 1.0f)
        {
            minZ -= 1.0f;
            maxZ += 1.0f;
        }

        const float inferredMinimumRange = 30.0f;
        const float inferredPaddingScale = 1.18f;
        var xRange = maxX - minX;
        var zRange = maxZ - minZ;
        var evenRange = MathF.Max(MathF.Max(xRange, zRange), inferredMinimumRange) * inferredPaddingScale;
        var centerX = (minX + maxX) * 0.5f;
        var centerZ = (minZ + maxZ) * 0.5f;
        minX = centerX - (evenRange * 0.5f);
        maxX = centerX + (evenRange * 0.5f);
        minZ = centerZ - (evenRange * 0.5f);
        maxZ = centerZ + (evenRange * 0.5f);

        return true;
    }

    private static void IncludeReplayBounds(
        float candidateMinX,
        float candidateMaxX,
        float candidateMinZ,
        float candidateMaxZ,
        ref bool hasBounds,
        ref float minX,
        ref float maxX,
        ref float minZ,
        ref float maxZ)
    {
        if (!hasBounds)
        {
            minX = candidateMinX;
            maxX = candidateMaxX;
            minZ = candidateMinZ;
            maxZ = candidateMaxZ;
            hasBounds = true;
            return;
        }

        minX = MathF.Min(minX, candidateMinX);
        maxX = MathF.Max(maxX, candidateMaxX);
        minZ = MathF.Min(minZ, candidateMinZ);
        maxZ = MathF.Max(maxZ, candidateMaxZ);
    }

    private static void DrawReplayGrid(
        ImDrawListPtr drawList,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan)
    {
        var gridColor = ImGui.GetColorU32(ModernDividerColor with { W = 0.32f });
        var centerX = (minX + maxX) * 0.5f;
        var centerZ = (minZ + maxZ) * 0.5f;
        var arenaRadius = MathF.Max(maxX - minX, maxZ - minZ) * 0.5f;
        var arenaCenter = ReplayWorldPointToScreen(centerX, centerZ, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan);
        var screenRadius = ReplayWorldLengthToScreenRadius(arenaRadius, canvasSize, minX, maxX, minZ, maxZ, zoom);

        drawList.AddCircle(arenaCenter, screenRadius, ImGui.GetColorU32(ModernPanelBorderColor with { W = 0.9f }), 96, 1.6f);
        drawList.AddCircle(arenaCenter, screenRadius * 0.5f, ImGui.GetColorU32(ModernDividerColor with { W = 0.22f }), 72, 1.0f);
        drawList.AddLine(
            ReplayWorldPointToScreen(centerX - arenaRadius, centerZ, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan),
            ReplayWorldPointToScreen(centerX + arenaRadius, centerZ, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan),
            ImGui.GetColorU32(ModernDividerColor with { W = 0.26f }),
            1.0f);
        drawList.AddLine(
            ReplayWorldPointToScreen(centerX, centerZ - arenaRadius, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan),
            ReplayWorldPointToScreen(centerX, centerZ + arenaRadius, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan),
            ImGui.GetColorU32(ModernDividerColor with { W = 0.26f }),
            1.0f);

        const int gridLines = 4;
        for (var i = 1; i < gridLines; i++)
        {
            var x = minX + ((maxX - minX) * i / gridLines);
            var z = minZ + ((maxZ - minZ) * i / gridLines);
            drawList.AddLine(
                ReplayWorldPointToScreen(x, minZ, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan),
                ReplayWorldPointToScreen(x, maxZ, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan),
                gridColor,
                1.0f);
            drawList.AddLine(
                ReplayWorldPointToScreen(minX, z, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan),
                ReplayWorldPointToScreen(maxX, z, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan),
                gridColor,
                1.0f);
        }

        DrawReplayCardinalLabels(drawList, arenaCenter, screenRadius);
    }

    private static void DrawReplayCardinalLabels(ImDrawListPtr drawList, Vector2 arenaCenter, float screenRadius)
    {
        if (!float.IsFinite(screenRadius) || screenRadius <= 16.0f)
        {
            return;
        }

        var inset = Math.Clamp(screenRadius * 0.08f, 8.0f, 18.0f);
        var color = ActiveThemeUsesLightPanels()
            ? ModernMutedTextColor with { W = 0.82f }
            : ModernTextColor with { W = 0.80f };
        var shadow = ActiveThemeUsesLightPanels()
            ? ModernPanelColor with { W = 0.80f }
            : ModernShellColor with { W = 0.78f };

        DrawReplayCardinalLabel(drawList, "N", arenaCenter + new Vector2(0.0f, -screenRadius + inset), color, shadow);
        DrawReplayCardinalLabel(drawList, "E", arenaCenter + new Vector2(screenRadius - inset, 0.0f), color, shadow);
        DrawReplayCardinalLabel(drawList, "S", arenaCenter + new Vector2(0.0f, screenRadius - inset), color, shadow);
        DrawReplayCardinalLabel(drawList, "W", arenaCenter + new Vector2(-screenRadius + inset, 0.0f), color, shadow);
    }

    private static void DrawReplayCardinalLabel(
        ImDrawListPtr drawList,
        string label,
        Vector2 center,
        Vector4 color,
        Vector4 shadow)
    {
        var textSize = ImGui.CalcTextSize(label);
        var position = center - (textSize * 0.5f);
        drawList.AddText(position + new Vector2(1.0f, 1.0f), ImGui.GetColorU32(shadow), label);
        drawList.AddText(position, ImGui.GetColorU32(color), label);
    }

    private static void DrawReplayWorldMarkers(
        ImDrawListPtr drawList,
        IReadOnlyList<ReplayWorldMarkerSnapshot> worldMarkers,
        float opacity,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan)
    {
        opacity = Math.Clamp(opacity, Plugin.MinReplayWorldMarkerOpacity, Plugin.MaxReplayWorldMarkerOpacity);
        foreach (var marker in worldMarkers)
        {
            var center = ReplayWorldPointToScreen(marker.X, marker.Z, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan);
            var color = GetReplayWorldMarkerColor(marker.MarkerIndex);
            var fill = color with { W = (ActiveThemeUsesLightPanels() ? 0.24f : 0.16f) * opacity };
            var border = color with { W = (ActiveThemeUsesLightPanels() ? 0.82f : 0.72f) * opacity };
            var textColor = BlendColors(color, ModernTextColor, ActiveThemeUsesLightPanels() ? 0.28f : 0.16f) with { W = 0.96f * opacity };

            if (marker.MarkerIndex < 4)
            {
                drawList.AddCircleFilled(center, ReplayWorldMarkerRadius, ImGui.GetColorU32(fill), 24);
                drawList.AddCircle(center, ReplayWorldMarkerRadius, ImGui.GetColorU32(border), 24, 1.4f);
            }
            else
            {
                var start = center - new Vector2(ReplayWorldMarkerSquareHalfSize);
                var end = center + new Vector2(ReplayWorldMarkerSquareHalfSize);
                drawList.AddRectFilled(start, end, ImGui.GetColorU32(fill), 2.0f);
                drawList.AddRect(start, end, ImGui.GetColorU32(border), 2.0f, ImDrawFlags.None, 1.4f);
            }

            DrawReplayWorldMarkerLabel(drawList, marker.Label, center, textColor);
        }
    }

    private static void DrawReplayWorldMarkerLabel(ImDrawListPtr drawList, string label, Vector2 center, Vector4 color)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        var textSize = ImGui.CalcTextSize(label);
        drawList.AddText(center - (textSize * 0.5f), ImGui.GetColorU32(color), label);
    }

    private static Vector4 GetReplayWorldMarkerColor(int markerIndex)
    {
        return markerIndex switch
        {
            0 or 4 => new Vector4(0.43f, 0.43f, 1.0f, 1.0f),
            1 or 5 => new Vector4(0.45f, 0.95f, 0.92f, 1.0f),
            2 or 6 => new Vector4(1.0f, 0.86f, 0.38f, 1.0f),
            3 or 7 => new Vector4(1.0f, 0.46f, 0.78f, 1.0f),
            _ => ModernAccentColor,
        };
    }

    private static void DrawReplayMechanics(
        ImDrawListPtr drawList,
        IReadOnlyList<ReplayMechanicSnapshot> mechanics,
        IReadOnlySet<string> hiddenLabelMechanicSourceKeys,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan)
    {
        foreach (var mechanic in mechanics)
        {
            var center = ReplayWorldPointToScreen(mechanic.X, mechanic.Z, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan);
            DrawReplayMechanic(drawList, mechanic, center, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan, hideLabel: hiddenLabelMechanicSourceKeys.Contains(mechanic.SourceKey));
        }
    }

    private static void DrawReplayMechanic(
        ImDrawListPtr drawList,
        ReplayMechanicSnapshot mechanic,
        Vector2 center,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan,
        float alpha = 1.0f,
        bool hideLabel = false)
    {
        alpha = Math.Clamp(alpha, 0.0f, 1.0f);
        var color = GetReplayMechanicColor(mechanic);
        var fill = color with { W = (mechanic.IsKnown ? 0.16f : 0.10f) * alpha };
        var border = color with { W = (mechanic.IsKnown ? 0.85f : 0.65f) * alpha };
        var radius = Math.Clamp(ReplayWorldLengthToScreenRadius(Math.Max(1.0f, mechanic.Radius), canvasSize, minX, maxX, minZ, maxZ, zoom), 10.0f, 160.0f);

        switch (mechanic.Shape)
        {
            case ReplayMechanicShape.Donut:
                DrawReplayDonutMechanic(drawList, mechanic, center, canvasSize, minX, maxX, minZ, maxZ, zoom, fill, border);
                break;
            case ReplayMechanicShape.Cone:
                DrawReplayConeMechanic(drawList, mechanic, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan, fill, border);
                break;
            case ReplayMechanicShape.Line:
                DrawReplayLineMechanic(drawList, mechanic, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan, border);
                break;
            case ReplayMechanicShape.Tether:
                DrawReplayTetherMechanic(drawList, mechanic, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan, border);
                break;
            case ReplayMechanicShape.Tower:
                drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(fill), 40);
                drawList.AddCircle(center, radius, ImGui.GetColorU32(border), 40, 2.2f);
                drawList.AddCircle(center, MathF.Max(4.0f, radius * 0.45f), ImGui.GetColorU32(border), 32, 1.5f);
                drawList.AddLine(center + new Vector2(-radius * 0.55f, 0.0f), center + new Vector2(radius * 0.55f, 0.0f), ImGui.GetColorU32(border), 1.2f);
                drawList.AddLine(center + new Vector2(0.0f, -radius * 0.55f), center + new Vector2(0.0f, radius * 0.55f), ImGui.GetColorU32(border), 1.2f);
                break;
            case ReplayMechanicShape.Stack:
                drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(fill), 40);
                drawList.AddCircle(center, radius, ImGui.GetColorU32(border), 40, 2.0f);
                drawList.AddCircle(center, MathF.Max(5.0f, radius * 0.68f), ImGui.GetColorU32(border), 32, 1.2f);
                break;
            case ReplayMechanicShape.Spread:
                drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(fill), 40);
                drawList.AddCircle(center, radius, ImGui.GetColorU32(border), 40, 1.8f);
                drawList.AddCircle(center, MathF.Max(5.0f, radius * 0.28f), ImGui.GetColorU32(border), 20, 1.2f);
                break;
            case ReplayMechanicShape.Label:
                break;
            default:
                drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(fill), 40);
                drawList.AddCircle(center, radius, ImGui.GetColorU32(border), 40, 1.8f);
                break;
        }

        if (ShouldDrawReplayMechanicLabel(mechanic, hideLabel))
        {
            DrawReplayMechanicLabel(drawList, mechanic, center, border, alpha);
        }
    }

    private static bool ShouldDrawReplayMechanicLabel(ReplayMechanicSnapshot mechanic, bool hideLabel)
    {
        if (hideLabel ||
            mechanic.Shape == ReplayMechanicShape.Tether)
        {
            return false;
        }

        return true;
    }

    private static void DrawReplayDonutMechanic(
        ImDrawListPtr drawList,
        ReplayMechanicSnapshot mechanic,
        Vector2 center,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector4 fill,
        Vector4 border)
    {
        var outerRadius = Math.Max(2.0f, mechanic.Radius);
        var innerRadius = Math.Clamp(mechanic.Width > 0.0f ? mechanic.Width : mechanic.Length, 0.0f, outerRadius - 0.5f);
        var middleRadius = (outerRadius + innerRadius) * 0.5f;
        var ringWidth = Math.Max(0.5f, outerRadius - innerRadius);
        var middleScreenRadius = ReplayWorldLengthToScreenRadius(middleRadius, canvasSize, minX, maxX, minZ, maxZ, zoom);
        var ringScreenWidth = ReplayWorldLengthToScreenRadius(ringWidth, canvasSize, minX, maxX, minZ, maxZ, zoom);
        var outerScreenRadius = ReplayWorldLengthToScreenRadius(outerRadius, canvasSize, minX, maxX, minZ, maxZ, zoom);
        var innerScreenRadius = ReplayWorldLengthToScreenRadius(innerRadius, canvasSize, minX, maxX, minZ, maxZ, zoom);

        drawList.AddCircle(center, middleScreenRadius, ImGui.GetColorU32(fill), 64, Math.Clamp(ringScreenWidth, 4.0f, 80.0f));
        drawList.AddCircle(center, outerScreenRadius, ImGui.GetColorU32(border), 64, 1.8f);
        if (innerScreenRadius > 2.0f)
        {
            drawList.AddCircle(center, innerScreenRadius, ImGui.GetColorU32(border), 64, 1.4f);
        }
    }

    private static void DrawReplayConeMechanic(
        ImDrawListPtr drawList,
        ReplayMechanicSnapshot mechanic,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan,
        Vector4 fill,
        Vector4 border)
    {
        var length = Math.Max(2.0f, mechanic.Length > 0.0f ? mechanic.Length : mechanic.Radius);
        var halfAngle = MathF.Max(5.0f, mechanic.AngleDegrees <= 0.0f ? 45.0f : mechanic.AngleDegrees * 0.5f) * MathF.PI / 180.0f;
        var center = ReplayWorldPointToScreen(mechanic.X, mechanic.Z, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan);
        var leftDirection = ReplayDirectionFromRotation(mechanic.Rotation - halfAngle);
        var rightDirection = ReplayDirectionFromRotation(mechanic.Rotation + halfAngle);
        var left = ReplayWorldPointToScreen(
            mechanic.X + (leftDirection.X * length),
            mechanic.Z + (leftDirection.Y * length),
            canvasStart,
            canvasSize,
            minX,
            maxX,
            minZ,
            maxZ,
            zoom,
            pan);
        var right = ReplayWorldPointToScreen(
            mechanic.X + (rightDirection.X * length),
            mechanic.Z + (rightDirection.Y * length),
            canvasStart,
            canvasSize,
            minX,
            maxX,
            minZ,
            maxZ,
            zoom,
            pan);

        drawList.AddTriangleFilled(center, left, right, ImGui.GetColorU32(fill));
        drawList.AddTriangle(center, left, right, ImGui.GetColorU32(border), 1.7f);
    }

    private static void DrawReplayLineMechanic(
        ImDrawListPtr drawList,
        ReplayMechanicSnapshot mechanic,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan,
        Vector4 border)
    {
        var length = Math.Max(2.0f, mechanic.Length);
        var halfLength = length * 0.5f;
        var direction = ReplayDirectionFromRotation(mechanic.Rotation);
        var start = ReplayWorldPointToScreen(
            mechanic.X - (direction.X * halfLength),
            mechanic.Z - (direction.Y * halfLength),
            canvasStart,
            canvasSize,
            minX,
            maxX,
            minZ,
            maxZ,
            zoom,
            pan);
        var end = ReplayWorldPointToScreen(
            mechanic.X + (direction.X * halfLength),
            mechanic.Z + (direction.Y * halfLength),
            canvasStart,
            canvasSize,
            minX,
            maxX,
            minZ,
            maxZ,
            zoom,
            pan);
        var thickness = Math.Clamp(ReplayWorldLengthToScreenRadius(Math.Max(1.0f, mechanic.Width), canvasSize, minX, maxX, minZ, maxZ, zoom), 3.0f, 34.0f);
        drawList.AddLine(start, end, ImGui.GetColorU32(border with { W = 0.26f }), thickness);
        drawList.AddLine(start, end, ImGui.GetColorU32(border), MathF.Min(3.0f, thickness));
    }

    private static void DrawReplayTetherMechanic(
        ImDrawListPtr drawList,
        ReplayMechanicSnapshot mechanic,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan,
        Vector4 border)
    {
        var length = Math.Max(0.1f, mechanic.Length);
        var halfLength = length * 0.5f;
        var direction = ReplayDirectionFromRotation(mechanic.Rotation);
        var start = ReplayWorldPointToScreen(
            mechanic.X - (direction.X * halfLength),
            mechanic.Z - (direction.Y * halfLength),
            canvasStart,
            canvasSize,
            minX,
            maxX,
            minZ,
            maxZ,
            zoom,
            pan);
        var end = ReplayWorldPointToScreen(
            mechanic.X + (direction.X * halfLength),
            mechanic.Z + (direction.Y * halfLength),
            canvasStart,
            canvasSize,
            minX,
            maxX,
            minZ,
            maxZ,
            zoom,
            pan);

        drawList.AddLine(start, end, ImGui.GetColorU32(border with { W = 0.20f }), 6.0f);
        drawList.AddLine(start, end, ImGui.GetColorU32(border with { W = 0.92f }), 2.1f);
        drawList.AddCircleFilled(start, 3.0f, ImGui.GetColorU32(border with { W = 0.72f }), 16);
        drawList.AddCircleFilled(end, 3.8f, ImGui.GetColorU32(border), 16);
    }

    private static void DrawReplayMechanicLabel(ImDrawListPtr drawList, ReplayMechanicSnapshot mechanic, Vector2 center, Vector4 color, float alpha = 1.0f)
    {
        alpha = Math.Clamp(alpha, 0.0f, 1.0f);
        var label = string.IsNullOrWhiteSpace(mechanic.Label)
            ? mechanic.Shape.ToString()
            : mechanic.Label;
        var textSize = ImGui.CalcTextSize(label);
        var padding = new Vector2(5.0f, 2.0f);
        var labelStart = center - new Vector2(textSize.X * 0.5f, textSize.Y * 0.5f);
        labelStart -= padding;
        var labelEnd = labelStart + textSize + (padding * 2.0f);
        drawList.AddRectFilled(labelStart, labelEnd, ImGui.GetColorU32(ModernPanelColor with { W = 0.82f * alpha }), 4.0f);
        drawList.AddRect(labelStart, labelEnd, ImGui.GetColorU32(color), 4.0f, ImDrawFlags.None, 1.0f);
        drawList.AddText(labelStart + padding, ImGui.GetColorU32(color), label);
    }

    private static void DrawReplayTrails(
        ImDrawListPtr drawList,
        PartyDeathRecord? focusDeath,
        IReadOnlyList<ReplayPositionTrack> positionTracks,
        IReadOnlySet<string> visibleActorKeys,
        DateTime selectedAtUtc,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan)
    {
        var trailStartAtUtc = selectedAtUtc.AddSeconds(-ReplayTrailSeconds);
        foreach (var track in positionTracks)
        {
            if (!visibleActorKeys.Contains(track.ActorKey))
            {
                continue;
            }

            var positions = track.Positions;
            var endIndex = FindReplayPositionIndexAtOrBefore(positions, selectedAtUtc);
            if (endIndex < 0 ||
                positions[endIndex].SeenAtUtc < trailStartAtUtc)
            {
                continue;
            }

            var startIndex = endIndex;
            var minIndex = Math.Max(0, endIndex - MaxReplayTrailPointsPerActor + 1);
            while (startIndex > minIndex &&
                positions[startIndex - 1].SeenAtUtc >= trailStartAtUtc)
            {
                startIndex--;
            }

            var pointCount = endIndex - startIndex + 1;
            if (pointCount <= 0)
            {
                continue;
            }

            for (var i = startIndex + 1; i <= endIndex; i++)
            {
                var previous = positions[i - 1];
                var current = positions[i];
                if ((current.SeenAtUtc - previous.SeenAtUtc).TotalSeconds > ReplayTrailMaxSegmentSeconds)
                {
                    continue;
                }

                var alpha = Math.Clamp(0.12f + (0.38f * (i - startIndex) / Math.Max(1, pointCount - 1)), 0.12f, 0.50f);
                var color = GetReplayActorColor(focusDeath, current) with { W = alpha };
                drawList.AddLine(
                    ReplayWorldToScreen(previous, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan),
                    ReplayWorldToScreen(current, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan),
                    ImGui.GetColorU32(color),
                    current.ActorKind == ReplayActorKind.Enemy ? 1.8f : 1.4f);
            }

            for (var i = startIndex; i <= endIndex; i++)
            {
                var current = positions[i];
                var alpha = Math.Clamp(0.16f + (0.34f * (i - startIndex) / Math.Max(1, pointCount - 1)), 0.16f, 0.50f);
                var color = GetReplayActorColor(focusDeath, current) with { W = alpha };
                drawList.AddCircleFilled(
                    ReplayWorldToScreen(current, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan),
                    current.ActorKind == ReplayActorKind.Enemy ? 2.2f : 1.8f,
                    ImGui.GetColorU32(color),
                    10);
            }
        }
    }

    private static int FindReplayPositionIndexAtOrBefore(
        IReadOnlyList<ReplayPositionSnapshot> positions,
        DateTime selectedAtUtc)
    {
        var low = 0;
        var high = positions.Count - 1;
        var result = -1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (positions[mid].SeenAtUtc <= selectedAtUtc)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }

    private void DrawReplayActor(
        ImDrawListPtr drawList,
        PartyDeathRecord? focusDeath,
        ReplayPositionSnapshot actor,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan,
        List<ReplayActorScreenState> actorScreenPositions,
        bool canvasHovered,
        IReplayEncounterModule replayModule,
        bool focused)
    {
        var screenPosition = ReplayWorldToScreen(actor, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan);
        var isDeathTarget = IsReplayDeathTarget(focusDeath, actor);
        var baseRadius = isDeathTarget ? 7.0f : actor.ActorKind == ReplayActorKind.Enemy ? 6.0f : 5.0f;
        var radius = focused ? baseRadius + 3.5f : baseRadius;
        var interactionRadius = GetReplayActorInteractionRadius(radius, markers.Count);
        var hovered = canvasHovered && IsReplayActorHovered(screenPosition, interactionRadius);
        var color = GetReplayActorColor(focusDeath, actor);
        if (focused)
        {
            drawList.AddCircle(screenPosition, radius + 7.0f, ImGui.GetColorU32(LeadUpGoldColor with { W = 0.34f }), 28, 2.4f);
            drawList.AddCircle(screenPosition, radius + 12.0f, ImGui.GetColorU32(LeadUpGoldColor with { W = 0.14f }), 28, 4.0f);
        }

        drawList.AddCircleFilled(screenPosition, radius, ImGui.GetColorU32(color), 22);
        drawList.AddCircle(screenPosition, radius + 1.5f, ImGui.GetColorU32(focused || isDeathTarget ? LeadUpGoldColor : ModernPanelBorderColor), 22, focused ? 2.0f : 1.3f);
        if (actor.ActorKind == ReplayActorKind.Player)
        {
            DrawReplayFacingChevron(drawList, actor, screenPosition, radius, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan);
        }

        if (actor.ActorKind == ReplayActorKind.Player && actor.IsDead)
        {
            DrawReplayDeadActorMarker(drawList, screenPosition, radius);
        }

        if (!actor.IsTargetable && actor.ActorKind == ReplayActorKind.Enemy)
        {
            drawList.AddCircle(screenPosition, radius + 4.0f, ImGui.GetColorU32(ModernMutedTextColor with { W = 0.55f }), 18, 1.0f);
        }

        var hoverRevealsPlayerName = hovered && !configuration.ShowReplayPlayerNames;
        var forcePlayerName = focused || isDeathTarget || hoverRevealsPlayerName;
        var forcePlayerHp = focused || isDeathTarget || hoverRevealsPlayerName;
        var shouldDrawPlayerInfoLabel = actor.ActorKind == ReplayActorKind.Player &&
            (configuration.ShowReplayPlayerNames || configuration.ShowReplayPlayerJobs || configuration.ShowReplayPlayerHp);
        var shouldDrawFullLabel = actor.ActorKind == ReplayActorKind.Enemy || forcePlayerName || shouldDrawPlayerInfoLabel;
        if (shouldDrawFullLabel)
        {
            var label = FormatReplayActorLabel(actor, forcePlayerName, forcePlayerHp);
            if (!string.IsNullOrWhiteSpace(label))
            {
                var labelYOffset = actor.ActorKind == ReplayActorKind.Player
                    ? radius + 6.0f
                    : -ImGui.GetTextLineHeight() * 0.5f;
                var textPosition = screenPosition + new Vector2(radius + 5.0f, labelYOffset);
                var labelColor = actor.ActorKind == ReplayActorKind.Player && actor.IsDead
                    ? DamageColor
                    : focused || isDeathTarget
                        ? LeadUpGoldColor
                        : ModernTextColor;
                if (focused)
                {
                    DrawFocusedReplayActorLabel(drawList, label, textPosition, labelColor);
                }
                else
                {
                    drawList.AddText(textPosition, ImGui.GetColorU32(labelColor), label);
                }
            }
        }

        if (markers.Count > 0)
        {
            if (focused || isDeathTarget || hoverRevealsPlayerName)
            {
                DrawReplayMarkerBadges(drawList, markers, allMarkers, screenPosition, radius, canvasStart, canvasSize, replayModule);
            }
            else
            {
                DrawReplayMarkerIndicators(
                    drawList,
                    markers.Select(marker => new ReplayMarkerBadgeDrawEntry(marker, 1.0f)),
                    allMarkers,
                    screenPosition,
                    radius,
                    canvasStart,
                    canvasSize,
                    replayModule);
            }
        }

        actorScreenPositions.Add(new ReplayActorScreenState(actor, markers, screenPosition, radius, interactionRadius));
    }

    private static void DrawReplayFacingChevron(
        ImDrawListPtr drawList,
        ReplayPositionSnapshot actor,
        Vector2 screenPosition,
        float actorRadius,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan)
    {
        if (!float.IsFinite(actor.Rotation))
        {
            return;
        }

        var direction = ReplayWorldDirectionToScreenDirection(
            ReplayDirectionFromRotation(actor.Rotation),
            canvasSize,
            minX,
            maxX,
            minZ,
            maxZ,
            zoom);
        if (direction.LengthSquared() <= 0.001f)
        {
            return;
        }

        direction = Vector2.Normalize(direction);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var tip = screenPosition + (direction * (actorRadius + ReplayFacingChevronLength));
        var baseCenter = screenPosition + (direction * MathF.Max(0.0f, actorRadius - 1.0f));
        var left = baseCenter + (perpendicular * ReplayFacingChevronHalfWidth);
        var right = baseCenter - (perpendicular * ReplayFacingChevronHalfWidth);
        var shellColor = ActiveThemeUsesLightPanels()
            ? ModernPanelColor with { W = 0.95f }
            : ModernShellColor with { W = 0.95f };
        var chevronColor = ActiveThemeUsesLightPanels()
            ? ModernTextColor with { W = 0.96f }
            : new Vector4(1.0f, 1.0f, 0.96f, 0.96f);

        drawList.AddLine(tip, left, ImGui.GetColorU32(shellColor), 3.3f);
        drawList.AddLine(tip, right, ImGui.GetColorU32(shellColor), 3.3f);
        drawList.AddLine(tip, left, ImGui.GetColorU32(chevronColor), 1.7f);
        drawList.AddLine(tip, right, ImGui.GetColorU32(chevronColor), 1.7f);
    }

    private static float GetReplayActorInteractionRadius(float actorRadius, int markerCount)
    {
        return markerCount > 0
            ? actorRadius + 24.0f
            : actorRadius + 7.0f;
    }

    private static bool IsReplayActorHovered(Vector2 screenPosition, float interactionRadius)
    {
        return Vector2.Distance(ImGui.GetIO().MousePos, screenPosition) <= interactionRadius;
    }

    private static void DrawReplayDeadActorMarker(ImDrawListPtr drawList, Vector2 center, float actorRadius)
    {
        var halfSize = MathF.Max(3.8f, actorRadius * 0.68f);
        var strokeWidth = Math.Clamp(actorRadius * 0.36f, 2.4f, 4.2f);
        var outlineColor = ActiveThemeUsesLightPanels()
            ? ModernPanelColor with { W = 0.92f }
            : ModernShellColor with { W = 0.96f };
        var markerColor = ActiveThemeUsesLightPanels()
            ? new Vector4(0.98f, 0.98f, 0.94f, 1.0f)
            : new Vector4(1.0f, 1.0f, 0.96f, 1.0f);
        var firstStart = center + new Vector2(-halfSize, -halfSize);
        var firstEnd = center + new Vector2(halfSize, halfSize);
        var secondStart = center + new Vector2(halfSize, -halfSize);
        var secondEnd = center + new Vector2(-halfSize, halfSize);

        drawList.AddLine(firstStart, firstEnd, ImGui.GetColorU32(outlineColor), strokeWidth + 2.0f);
        drawList.AddLine(secondStart, secondEnd, ImGui.GetColorU32(outlineColor), strokeWidth + 2.0f);
        drawList.AddLine(firstStart, firstEnd, ImGui.GetColorU32(markerColor), strokeWidth);
        drawList.AddLine(secondStart, secondEnd, ImGui.GetColorU32(markerColor), strokeWidth);
    }

    private static void DrawFocusedReplayActorLabel(ImDrawListPtr drawList, string label, Vector2 textPosition, Vector4 textColor)
    {
        var padding = new Vector2(6.0f, 3.0f);
        var textSize = ImGui.CalcTextSize(label);
        var labelStart = textPosition - padding;
        var labelEnd = textPosition + textSize + padding;
        drawList.AddRectFilled(labelStart, labelEnd, ImGui.GetColorU32(ModernPanelColor with { W = 0.90f }), 5.0f);
        drawList.AddRect(labelStart, labelEnd, ImGui.GetColorU32(textColor with { W = 0.95f }), 5.0f, ImDrawFlags.None, 1.4f);
        drawList.AddText(textPosition, ImGui.GetColorU32(textColor), label);
    }

    private static void DrawReplayMarkerBadges(
        ImDrawListPtr drawList,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers,
        Vector2 actorScreenPosition,
        float actorRadius,
        Vector2 canvasStart,
        Vector2 canvasSize,
        IReplayEncounterModule replayModule)
    {
        DrawReplayMarkerBadgeColumn(
            drawList,
            markers.Select(marker => new ReplayMarkerBadgeDrawEntry(marker, 1.0f)),
            allMarkers,
            actorScreenPosition,
            actorRadius,
            canvasStart,
            canvasSize,
            replayModule);
    }

    private static void DrawReplayMarkerIndicators(
        ImDrawListPtr drawList,
        IEnumerable<ReplayMarkerBadgeDrawEntry> badgeEntries,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers,
        Vector2 actorScreenPosition,
        float actorRadius,
        Vector2 canvasStart,
        Vector2 canvasSize,
        IReplayEncounterModule replayModule)
    {
        const float indicatorSize = 7.0f;
        const float indicatorGap = 3.0f;
        var entries = badgeEntries
            .Select(entry => (
                Entry: entry,
                Display: GetReplayMarkerBadgeDisplay(entry.Marker, allMarkers, replayModule),
                Shape: GetReplayMarkerIndicatorShape(entry.Marker, allMarkers, replayModule)))
            .Where(entry => entry.Entry.Alpha > 0.0f && !string.IsNullOrWhiteSpace(entry.Display.Text))
            .Take(MaxReplayMarkerBadgesPerActor)
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var canvasEnd = canvasStart + canvasSize;
        var rowWidth = (entries.Count * indicatorSize) + (Math.Max(0, entries.Count - 1) * indicatorGap);
        var rowHeight = indicatorSize;
        var minX = canvasStart.X + 4.0f;
        var maxX = Math.Max(minX, canvasEnd.X - rowWidth - 4.0f);
        var minY = canvasStart.Y + 4.0f;
        var maxY = Math.Max(minY, canvasEnd.Y - rowHeight - 4.0f);
        var rowStart = new Vector2(
            Math.Clamp(actorScreenPosition.X - (rowWidth * 0.5f), minX, maxX),
            Math.Clamp(actorScreenPosition.Y - actorRadius - 10.0f - rowHeight, minY, maxY));

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var center = rowStart + new Vector2(
                (indicatorSize * 0.5f) + (index * (indicatorSize + indicatorGap)),
                indicatorSize * 0.5f);
            DrawReplayMarkerIndicatorAt(drawList, entry.Entry, entry.Display, entry.Shape, center, indicatorSize);
        }
    }

    private static ReplayMechanicShape? GetReplayMarkerIndicatorShape(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers,
        IReplayEncounterModule replayModule)
    {
        return TryGetResolvedReplayMarkerInfo(marker, allMarkers, replayModule, out var info)
            ? info.Shape
            : null;
    }

    private static void DrawReplayMarkerIndicatorAt(
        ImDrawListPtr drawList,
        ReplayMarkerBadgeDrawEntry entry,
        ReplayMarkerBadgeDisplay display,
        ReplayMechanicShape? shape,
        Vector2 center,
        float size)
    {
        var alpha = Math.Clamp(entry.Alpha, 0.0f, 1.0f);
        var color = display.Color with { W = display.Color.W * alpha };
        var shellColor = ActiveThemeUsesLightPanels()
            ? ModernPanelColor with { W = 0.92f * alpha }
            : ModernShellColor with { W = 0.92f * alpha };
        var borderColor = color with { W = Math.Clamp(color.W, 0.0f, 1.0f) };
        var half = size * 0.5f;

        drawList.AddCircleFilled(center, half + 2.0f, ImGui.GetColorU32(shellColor), 16);

        switch (shape)
        {
            case ReplayMechanicShape.Cone:
                drawList.AddTriangleFilled(
                    center + new Vector2(0.0f, -half),
                    center + new Vector2(half, half),
                    center + new Vector2(-half, half),
                    ImGui.GetColorU32(color));
                drawList.AddTriangle(
                    center + new Vector2(0.0f, -half),
                    center + new Vector2(half, half),
                    center + new Vector2(-half, half),
                    ImGui.GetColorU32(borderColor),
                    1.0f);
                break;
            case ReplayMechanicShape.Tower:
                drawList.AddRectFilled(center - new Vector2(half), center + new Vector2(half), ImGui.GetColorU32(color), 1.0f);
                drawList.AddRect(center - new Vector2(half), center + new Vector2(half), ImGui.GetColorU32(borderColor), 1.0f);
                break;
            case ReplayMechanicShape.Line:
                drawList.AddLine(center - new Vector2(half, 0.0f), center + new Vector2(half, 0.0f), ImGui.GetColorU32(color), 3.0f);
                drawList.AddLine(center - new Vector2(half, 0.0f), center + new Vector2(half, 0.0f), ImGui.GetColorU32(borderColor), 1.0f);
                break;
            case ReplayMechanicShape.Donut:
                drawList.AddCircle(center, half, ImGui.GetColorU32(color), 16, 2.0f);
                break;
            default:
                drawList.AddCircleFilled(center, half, ImGui.GetColorU32(color), 16);
                drawList.AddCircle(center, half, ImGui.GetColorU32(borderColor), 16, 1.0f);
                break;
        }
    }

    private static void DrawReplayMarkerBadgeColumn(
        ImDrawListPtr drawList,
        IEnumerable<ReplayMarkerBadgeDrawEntry> badgeEntries,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers,
        Vector2 actorScreenPosition,
        float actorRadius,
        Vector2 canvasStart,
        Vector2 canvasSize,
        IReplayEncounterModule replayModule)
    {
        var padding = new Vector2(5.0f, 2.0f);
        const float badgeGap = 3.0f;
        var entries = badgeEntries
            .Select(entry => (Entry: entry, Display: GetReplayMarkerBadgeDisplay(entry.Marker, allMarkers, replayModule)))
            .Where(entry => entry.Entry.Alpha > 0.0f && !string.IsNullOrWhiteSpace(entry.Display.Text))
            .Select(entry => (
                entry.Entry,
                entry.Display,
                Size: ImGui.CalcTextSize(entry.Display.Text) + (padding * 2.0f)))
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var canvasEnd = canvasStart + canvasSize;
        var columnHeight = entries.Sum(entry => entry.Size.Y) + (badgeGap * Math.Max(0, entries.Count - 1));
        var minY = canvasStart.Y + 4.0f;
        var maxY = Math.Max(minY, canvasEnd.Y - columnHeight - 4.0f);
        var desiredY = actorScreenPosition.Y - actorRadius - 6.0f - columnHeight;
        var columnStartY = Math.Clamp(desiredY, minY, maxY);
        var currentY = columnStartY + columnHeight;
        foreach (var entry in entries)
        {
            currentY -= entry.Size.Y;
            var minX = canvasStart.X + 4.0f;
            var maxX = Math.Max(minX, canvasEnd.X - entry.Size.X - 4.0f);
            var badgeStart = new Vector2(
                Math.Clamp(actorScreenPosition.X - (entry.Size.X * 0.5f), minX, maxX),
                currentY);
            DrawReplayMarkerBadgeAt(
                drawList,
                entry.Entry,
                entry.Display,
                badgeStart,
                entry.Size);
            currentY -= badgeGap;
        }
    }

    private static void DrawReplayMarkerBadgeAt(
        ImDrawListPtr drawList,
        ReplayMarkerBadgeDrawEntry entry,
        ReplayMarkerBadgeDisplay display,
        Vector2 badgeStart,
        Vector2 badgeSize)
    {
        var alpha = Math.Clamp(entry.Alpha, 0.0f, 1.0f);
        var badgeEnd = badgeStart + badgeSize;
        var borderColor = display.Color with { W = display.Color.W * alpha };
        drawList.AddRectFilled(badgeStart, badgeEnd, ImGui.GetColorU32(ModernPanelColor with { W = 0.92f * alpha }), 4.0f);
        drawList.AddRect(badgeStart, badgeEnd, ImGui.GetColorU32(borderColor), 4.0f, ImDrawFlags.None, 1.2f);
        drawList.AddText(badgeStart + new Vector2(5.0f, 2.0f), ImGui.GetColorU32(borderColor), display.Text);
    }

    private static Vector2 ReplayWorldToScreen(
        ReplayPositionSnapshot actor,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan)
    {
        return ReplayWorldPointToScreen(actor.X, actor.Z, canvasStart, canvasSize, minX, maxX, minZ, maxZ, zoom, pan);
    }

    private static Vector2 ReplayWorldPointToScreen(
        float worldX,
        float worldZ,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom = ReplayMinZoom,
        Vector2 pan = default)
    {
        const float padding = 30.0f;
        var innerWidth = MathF.Max(1.0f, canvasSize.X - (padding * 2.0f));
        var innerHeight = MathF.Max(1.0f, canvasSize.Y - (padding * 2.0f));
        var xRatio = Math.Clamp((worldX - minX) / MathF.Max(1.0f, maxX - minX), 0.0f, 1.0f);
        var zRatio = Math.Clamp((worldZ - minZ) / MathF.Max(1.0f, maxZ - minZ), 0.0f, 1.0f);
        var basePoint = new Vector2(
            canvasStart.X + padding + (innerWidth * xRatio),
            canvasStart.Y + padding + (innerHeight * zRatio));
        var center = canvasStart + (canvasSize * 0.5f);
        return center + ((basePoint - center) * Math.Clamp(zoom, ReplayMinZoom, ReplayMaxZoom)) + pan;
    }

    private static float ReplayWorldLengthToScreenRadius(
        float worldLength,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom = ReplayMinZoom)
    {
        const float padding = 30.0f;
        var innerWidth = MathF.Max(1.0f, canvasSize.X - (padding * 2.0f));
        var innerHeight = MathF.Max(1.0f, canvasSize.Y - (padding * 2.0f));
        var xScale = innerWidth / MathF.Max(1.0f, maxX - minX);
        var zScale = innerHeight / MathF.Max(1.0f, maxZ - minZ);
        return MathF.Max(1.0f, worldLength) * MathF.Min(xScale, zScale) * Math.Clamp(zoom, ReplayMinZoom, ReplayMaxZoom);
    }

    private static Vector2 ReplayWorldDirectionToScreenDirection(
        Vector2 worldDirection,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom = ReplayMinZoom)
    {
        const float padding = 30.0f;
        var innerWidth = MathF.Max(1.0f, canvasSize.X - (padding * 2.0f));
        var innerHeight = MathF.Max(1.0f, canvasSize.Y - (padding * 2.0f));
        var xScale = innerWidth / MathF.Max(1.0f, maxX - minX);
        var zScale = innerHeight / MathF.Max(1.0f, maxZ - minZ);
        var clampedZoom = Math.Clamp(zoom, ReplayMinZoom, ReplayMaxZoom);
        return new Vector2(worldDirection.X * xScale * clampedZoom, worldDirection.Y * zScale * clampedZoom);
    }

    private static float GetReplayMechanicBoundsRadius(ReplayMechanicSnapshot mechanic)
    {
        return mechanic.Shape switch
        {
            ReplayMechanicShape.Donut => Math.Max(2.0f, mechanic.Radius),
            ReplayMechanicShape.Cone => Math.Max(mechanic.Radius, mechanic.Length),
            ReplayMechanicShape.Line => Math.Max(mechanic.Radius, mechanic.Length * 0.5f) + Math.Max(0.0f, mechanic.Width * 0.5f),
            ReplayMechanicShape.Tether => Math.Max(2.0f, mechanic.Length * 0.5f),
            ReplayMechanicShape.Label => 2.0f,
            _ => Math.Max(2.0f, mechanic.Radius),
        };
    }

    private static Vector4 GetReplayMechanicColor(ReplayMechanicSnapshot mechanic)
    {
        if (!mechanic.IsKnown)
        {
            return WarningColor;
        }

        if (string.Equals(mechanic.RawEventKind, "black-hole-blast", StringComparison.OrdinalIgnoreCase))
        {
            return OverkillColor;
        }

        if (string.Equals(mechanic.RawEventKind, "black-hole-tether", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mechanic.RawEventKind, "graven-image-tether", StringComparison.OrdinalIgnoreCase))
        {
            return ModernAccentColor;
        }

        return GetReplayMechanicShapeColor(mechanic.Shape);
    }

    private static Vector4 GetReplayMechanicShapeColor(ReplayMechanicShape shape)
    {
        return shape switch
        {
            ReplayMechanicShape.Stack => ModernAccentColor,
            ReplayMechanicShape.Spread => LeadUpGoldColor,
            ReplayMechanicShape.Tower => NoticeTextColor,
            ReplayMechanicShape.Donut => LeadUpGoldColor,
            ReplayMechanicShape.Cone => DamageColor,
            ReplayMechanicShape.Line => OverkillColor,
            ReplayMechanicShape.Label => ModernTextColor,
            ReplayMechanicShape.Tether => ModernAccentColor,
            _ => LeadUpGoldColor,
        };
    }

    private string FormatReplayActorLabel(ReplayPositionSnapshot actor, bool forcePlayerName = false, bool forcePlayerHp = false)
    {
        if (actor.ActorKind == ReplayActorKind.Enemy)
        {
            return actor.ActorName;
        }

        var showName = forcePlayerName || configuration.ShowReplayPlayerNames;
        var showJob = configuration.ShowReplayPlayerJobs && !string.IsNullOrWhiteSpace(actor.ClassJobName);
        var showHp = forcePlayerHp || configuration.ShowReplayPlayerHp;
        if (!showName && !showJob && !showHp)
        {
            return string.Empty;
        }

        string? name = null;
        if (configuration.RedactPlayerNames)
        {
            var slot = actor.PartyIndex >= 0 && actor.PartyIndex < 1000
                ? actor.PartyIndex + 1
                : actor.PartyIndex;
            name = $"Party {slot}";
        }
        else
        {
            name = FormatKnownPlayerName(actor.ActorName);
        }

        var identityText = (showName, showJob) switch
        {
            (true, true) => $"{name} ({actor.ClassJobName})",
            (true, false) => name,
            (false, true) => actor.ClassJobName,
            _ => string.Empty,
        };
        var hpText = showHp
            ? FormatReplayActorHpLabel(actor)
            : string.Empty;

        if (string.IsNullOrWhiteSpace(identityText))
        {
            return hpText;
        }

        return string.IsNullOrWhiteSpace(hpText)
            ? identityText
            : $"{identityText} {hpText}";
    }

    private static string FormatReplayActorHpLabel(ReplayPositionSnapshot actor)
    {
        if (actor.MaxHp == 0)
        {
            return string.Empty;
        }

        var currentPercent = actor.IsDead
            ? 0
            : FormatReplayHpPercent(actor.CurrentHp, actor.MaxHp);
        var shieldText = actor.ShieldHp > 0
            ? $" +{FormatReplayHpPercent(actor.ShieldHp, actor.MaxHp)}%"
            : string.Empty;
        return $"{currentPercent}%{shieldText}";
    }

    private static int FormatReplayHpPercent(uint amount, uint maxHp)
    {
        if (maxHp == 0 || amount == 0)
        {
            return 0;
        }

        var percent = (int)MathF.Round((float)amount / maxHp * 100.0f);
        return Math.Clamp(Math.Max(1, percent), 0, 999);
    }

    private string FormatReplayMarkerActorLabel(ReplayMarkerSnapshot marker)
    {
        if (marker.ActorKind == ReplayActorKind.Enemy)
        {
            return marker.ActorName;
        }

        if (configuration.RedactPlayerNames)
        {
            var slot = marker.PartyIndex >= 0 && marker.PartyIndex < 1000
                ? marker.PartyIndex + 1
                : marker.PartyIndex;
            return string.IsNullOrWhiteSpace(marker.ClassJobName)
                ? $"Party {slot}"
                : $"Party {slot} ({marker.ClassJobName})";
        }

        var name = FormatKnownPlayerName(marker.ActorName);
        return string.IsNullOrWhiteSpace(marker.ClassJobName)
            ? name
            : $"{name} ({marker.ClassJobName})";
    }

    private static bool IsReplayDeathTarget(PartyDeathRecord? death, ReplayPositionSnapshot actor)
    {
        return death is not null &&
            actor.ActorKind == ReplayActorKind.Player &&
            string.Equals(actor.ActorKey, $"player:{death.MemberKey}", StringComparison.Ordinal);
    }

    private static Vector4 GetReplayActorColor(PartyDeathRecord? death, ReplayPositionSnapshot actor)
    {
        return actor.ActorKind switch
        {
            ReplayActorKind.Enemy => actor.IsTargetable
                ? DamageColor
                : ModernMutedTextColor with { W = 0.78f },
            _ => GetReplayPlayerRoleColor(actor),
        };
    }

    private static Vector4 GetReplayPlayerRoleColor(ReplayPositionSnapshot actor)
    {
        if (IsReplayTank(actor))
        {
            return ActiveThemeUsesLightPanels()
                ? new Vector4(0.12f, 0.38f, 0.86f, 1.0f)
                : new Vector4(0.30f, 0.58f, 1.0f, 1.0f);
        }

        if (IsReplayHealer(actor))
        {
            return ActiveThemeUsesLightPanels()
                ? new Vector4(0.08f, 0.56f, 0.24f, 1.0f)
                : new Vector4(0.20f, 0.82f, 0.38f, 1.0f);
        }

        return OverkillColor;
    }

    private static bool IsReplayTank(ReplayPositionSnapshot actor)
    {
        return actor.ClassJobId is 19 or 21 or 32 or 37 ||
            NormalizeReplayJob(actor.ClassJobName) is "GLA" or "PLD" or "GLADIATOR" or "PALADIN" or
                "MRD" or "WAR" or "MARAUDER" or "WARRIOR" or
                "DRK" or "DARKKNIGHT" or
                "GNB" or "GUNBREAKER";
    }

    private static bool IsReplayHealer(ReplayPositionSnapshot actor)
    {
        return actor.ClassJobId is 6 or 24 or 28 or 33 or 40 ||
            NormalizeReplayJob(actor.ClassJobName) is "CNJ" or "CONJURER" or
                "WHM" or "WHITEMAGE" or
                "SCH" or "SCHOLAR" or
                "AST" or "ASTROLOGIAN" or
                "SGE" or "SAGE";
    }

    private static string NormalizeReplayJob(string classJobName)
    {
        return new string(classJobName.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private static ReplayMarkerBadgeDisplay GetReplayMarkerBadgeDisplay(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers,
        IReplayEncounterModule replayModule)
    {
        return new ReplayMarkerBadgeDisplay(
            FormatReplayMarkerBadgeText(marker, allMarkers, replayModule),
            GetReplayMarkerBadgeColor(marker, allMarkers, replayModule));
    }

    private static string FormatReplayMarkerBadgeText(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers,
        IReplayEncounterModule replayModule)
    {
        if (ReplayEncounterModules.IsDmuP4RealityTellMarker(marker.MarkerId) &&
            ReplayEncounterModules.TryGetDmuP4RealityTell(marker.RawMarkerId, out var realityLabel, out _))
        {
            return realityLabel;
        }

        if (TryGetResolvedReplayMarkerInfo(marker, allMarkers, replayModule, out var info))
        {
            var text = GetReplayMarkerPreferredBadgeText(info);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return $"#{marker.MarkerId}";
    }

    private static Vector4 GetReplayMarkerBadgeColor(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers,
        IReplayEncounterModule replayModule)
    {
        return TryGetResolvedReplayMarkerInfo(marker, allMarkers, replayModule, out var info) &&
            info.Shape is { } shape
            ? GetReplayMechanicShapeColor(shape)
            : LeadUpGoldColor;
    }

    private static bool TryGetResolvedReplayMarkerInfo(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> allMarkers,
        IReplayEncounterModule replayModule,
        out ReplayMarkerInfo info)
    {
        if (!replayModule.TryGetMarkerInfo(marker.MarkerId, out info))
        {
            return false;
        }

        if (ReplayEncounterModules.TryResolveDmuP1FireMarkerInfo(marker, allMarkers, info, out var resolvedInfo))
        {
            info = resolvedInfo;
        }

        return true;
    }

    private static string GetReplayMarkerPreferredBadgeText(ReplayMarkerInfo info)
    {
        var description = info.Description.Trim();
        var shortLabel = info.ShortLabel.Trim();
        if (!string.IsNullOrWhiteSpace(description) &&
            !string.Equals(description, "Unknown marker", StringComparison.OrdinalIgnoreCase) &&
            !IsGenericReplayMarkerLabel(description, info.Shape))
        {
            return description;
        }

        if (!string.IsNullOrWhiteSpace(shortLabel))
        {
            return shortLabel;
        }

        return description;
    }

    private static bool IsGenericReplayMarkerLabel(string label, ReplayMechanicShape? shape)
    {
        if (shape is { } knownShape &&
            string.Equals(label, knownShape.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return label.Trim() switch
        {
            var value when string.Equals(value, "Stack", StringComparison.OrdinalIgnoreCase) => true,
            var value when string.Equals(value, "Spread", StringComparison.OrdinalIgnoreCase) => true,
            var value when string.Equals(value, "Cone", StringComparison.OrdinalIgnoreCase) => true,
            var value when string.Equals(value, "Fan", StringComparison.OrdinalIgnoreCase) => true,
            var value when string.Equals(value, "Circle", StringComparison.OrdinalIgnoreCase) => true,
            var value when string.Equals(value, "Tower", StringComparison.OrdinalIgnoreCase) => true,
            var value when string.Equals(value, "Donut", StringComparison.OrdinalIgnoreCase) => true,
            var value when string.Equals(value, "Line", StringComparison.OrdinalIgnoreCase) => true,
            var value when string.Equals(value, "Tether", StringComparison.OrdinalIgnoreCase) => true,
            _ => false,
        };
    }

    private static string FormatReplayMarkerSummaryLabel(ReplayMarkerSnapshot marker, IReplayEncounterModule replayModule)
    {
        var idText = FormatReplayMarkerIdText(marker);
        return replayModule.TryGetMarkerInfo(marker.MarkerId, out var info)
            ? $"{info.Description} ({idText})"
            : idText;
    }

    private static string FormatReplayMarkerSummaryLabel(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReplayEncounterModule replayModule)
    {
        var idText = FormatReplayMarkerIdText(marker);
        if (ReplayEncounterModules.IsDmuP4RealityTellMarker(marker.MarkerId) &&
            ReplayEncounterModules.TryGetDmuP4RealityTell(marker.RawMarkerId, out var realityLabel, out _))
        {
            return $"{realityLabel} P4 tell ({idText})";
        }

        if (TryFormatDmuP4AssignmentMarker(marker, markers, replayModule, out var assignmentText))
        {
            return assignmentText;
        }

        return FormatReplayMarkerSummaryLabel(marker, replayModule);
    }

    private static bool TryFormatDmuP4AssignmentMarker(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        IReplayEncounterModule replayModule,
        out string text)
    {
        if (!ReplayEncounterModules.IsDmuP4AssignmentMarker(marker.MarkerId) ||
            !replayModule.TryGetMarkerInfo(marker.MarkerId, out var info))
        {
            text = string.Empty;
            return false;
        }

        if (TryFindNearestDmuP4RealityTell(marker, markers, out var realityLabel, out var isReal) &&
            ReplayEncounterModules.TryGetDmuP4StatusResolution(marker.MarkerId, isReal, out var resolution))
        {
            text = $"{info.Description} - {realityLabel}: {resolution} ({FormatReplayMarkerIdText(marker)})";
            return true;
        }

        text = $"{info.Description} ({FormatReplayMarkerIdText(marker)})";
        return true;
    }

    private static bool TryFindNearestDmuP4RealityTell(
        ReplayMarkerSnapshot marker,
        IReadOnlyList<ReplayMarkerSnapshot> markers,
        out string realityLabel,
        out bool isReal)
    {
        var tell = markers
            .Where(candidate => candidate.ActorKind == ReplayActorKind.Enemy &&
                ReplayEncounterModules.IsDmuP4RealityTellMarker(candidate.MarkerId) &&
                candidate.SeenAtUtc >= marker.SeenAtUtc.AddSeconds(-8.0) &&
                candidate.SeenAtUtc <= marker.SeenAtUtc.AddSeconds(25.0) &&
                ReplayEncounterModules.TryGetDmuP4RealityTell(candidate.RawMarkerId, out _, out _))
            .OrderBy(candidate => Math.Abs((candidate.SeenAtUtc - marker.SeenAtUtc).TotalSeconds))
            .FirstOrDefault();
        if (tell is not null &&
            ReplayEncounterModules.TryGetDmuP4RealityTell(tell.RawMarkerId, out realityLabel, out isReal))
        {
            return true;
        }

        realityLabel = string.Empty;
        isReal = false;
        return false;
    }

    private static string FormatReplayMarkerIdText(ReplayMarkerSnapshot marker)
    {
        return marker.RawMarkerId != 0 && marker.RawMarkerId != marker.MarkerId
            ? $"ID {marker.MarkerId}, raw {marker.RawMarkerId}"
            : $"ID {marker.MarkerId}";
    }

    private static string FormatReplayOffset(float seconds)
    {
        if (MathF.Abs(seconds) < 0.005f)
        {
            return "Death";
        }

        return seconds < 0.0f
            ? $"{seconds:0.00}s"
            : $"+{seconds:0.00}s";
    }

    private static void DrawCenteredCanvasText(ImDrawListPtr drawList, Vector2 canvasStart, Vector2 canvasSize, string text)
    {
        var textSize = ImGui.CalcTextSize(text);
        drawList.AddText(
            canvasStart + ((canvasSize - textSize) * 0.5f),
            ImGui.GetColorU32(ModernMutedTextColor),
            text);
    }

    private void DrawHpHistory(
        ResolvedDeathDisplay resolved,
        string idSuffix,
        bool showLabel = true,
        bool showHeader = true,
        float rightPadding = LeadUpTableRightPadding)
    {
        var death = resolved.Death;
        if (showLabel)
        {
            DrawLeadUpLabel("10 second HP history");
        }

        var displayAnchorSeenAtUtc = GetLeadUpDisplayAnchorSeenAtUtc(death);
        var rows = GetDisplayLeadUpTimelineRows(resolved.TimelineRows);

        if (rows.Count == 0)
        {
            DrawMutedWrappedText("No HP samples or combat events captured in the last 10 seconds before KO.");
            return;
        }

        if (!ImGui.BeginTable(
            $"##HpHistory{idSuffix}",
            5,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg,
            GetRightPaddedTableSize(rightPadding)))
        {
            return;
        }

        ImGui.TableSetupColumn("Before KO", ImGuiTableColumnFlags.WidthStretch, 0.75f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("HP + shields", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("Events", ImGuiTableColumnFlags.WidthStretch, 1.45f);
        ImGui.TableSetupColumn("Mits/Debuffs", ImGuiTableColumnFlags.WidthStretch, 2.0f);
        if (showHeader)
        {
            DrawHpHistoryTableHeader();
        }

        string? previousSourceKey = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var sourceKey = row.Event is null ? null : GetSourceKey(row.Event);
            var sourceChanged = previousSourceKey is not null &&
                sourceKey is not null &&
                !string.Equals(previousSourceKey, sourceKey, StringComparison.Ordinal);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredText(FormatRelativeToDeath(displayAnchorSeenAtUtc, row.SeenAtUtc));
            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip(FormatPreciseRelativeToDeath(displayAnchorSeenAtUtc, row.SeenAtUtc));
            }

            ImGui.TableNextColumn();
            DrawLeadUpTimelineSourceCell(row, sourceChanged);
            ImGui.TableNextColumn();
            DrawHpShieldBar(
                row.CurrentHp,
                row.ShieldHp,
                row.MaxHp,
                $"HpHistoryBar{idSuffix}{row.SeenAtUtc.Ticks}{i}",
                row.Event is not null ? GetIncomingDamageAmount(row.Event) : null,
                valueOnlyTooltip: true,
                healChange: row.HealChange,
                damageChange: row.DamageChange);
            ImGui.TableNextColumn();
            DrawTimelineEventCell(row.Event);
            ImGui.TableNextColumn();
            DrawMitigationDebuffSummaryCell(row, configuration.ShowLeadUpTimelineMitigationTimers);
            if (sourceKey is not null)
            {
                previousSourceKey = sourceKey;
            }
        }

        ImGui.EndTable();
    }

    private bool IsFocusedReviewMode()
    {
        return configuration.ReviewDisplayMode == ReviewDisplayMode.Focused;
    }

    private IReadOnlyList<LeadUpTimelineRow> GetDisplayLeadUpTimelineRows(IReadOnlyList<LeadUpTimelineRow> rows)
    {
        var displayRows = IsFocusedReviewMode()
            ? GetFocusedLeadUpTimelineRows(rows)
            : rows;
        return configuration.LeadUpTimelineOrder == LeadUpTimelineOrder.Newest
            ? displayRows.OrderByDescending(row => row.SeenAtUtc).ToList()
            : displayRows;
    }

    private static IReadOnlyList<LeadUpTimelineRow> GetFocusedLeadUpTimelineRows(IReadOnlyList<LeadUpTimelineRow> rows)
    {
        if (rows.Count <= 2)
        {
            return rows;
        }

        var focusedRows = new List<LeadUpTimelineRow>();
        LeadUpTimelineRow? previousKept = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!ShouldKeepFocusedLeadUpTimelineRow(row, previousKept))
            {
                continue;
            }

            focusedRows.Add(row);
            previousKept = row;
        }

        return focusedRows.Count == 0 ? rows : focusedRows;
    }

    private static bool ShouldKeepFocusedLeadUpTimelineRow(LeadUpTimelineRow row, LeadUpTimelineRow? previousKept)
    {
        if (row.Event is not null ||
            row.HealChange is not null ||
            row.DamageChange is not null ||
            !string.IsNullOrWhiteSpace(row.HpTooltipDetail))
        {
            return true;
        }

        return previousKept is null ||
            HasMeaningfulFocusedLeadUpHpChange(previousKept, row);
    }

    private static bool HasMeaningfulFocusedLeadUpHpChange(LeadUpTimelineRow previous, LeadUpTimelineRow row)
    {
        var maxHp = row.MaxHp > 0 ? row.MaxHp : previous.MaxHp;
        if (maxHp == 0)
        {
            return previous.CurrentHp != row.CurrentHp ||
                previous.ShieldHp != row.ShieldHp;
        }

        var previousHpPercent = previous.CurrentHp * 100.0f / maxHp;
        var currentHpPercent = row.CurrentHp * 100.0f / maxHp;
        var previousShieldPercent = previous.ShieldHp * 100.0f / maxHp;
        var currentShieldPercent = row.ShieldHp * 100.0f / maxHp;
        return MathF.Abs(currentHpPercent - previousHpPercent) >= FocusedLeadUpHpChangePercentThreshold ||
            MathF.Abs(currentShieldPercent - previousShieldPercent) >= FocusedLeadUpHpChangePercentThreshold ||
            (previous.ShieldHp == 0) != (row.ShieldHp == 0);
    }

    private void DrawLeadUpTimelineSourceCell(LeadUpTimelineRow row, bool sourceChanged)
    {
        if (row.Event is null)
        {
            DrawCenteredText("-", DisabledColor);
            return;
        }

        DrawCenteredOrWrappedText(
            FormatKnownPlayerName(row.Event.SourceName),
            sourceChanged ? LeadUpGoldColor : null);
    }

    private IReadOnlyList<LeadUpTimelineRow> GetLeadUpTimelineRows(
        PartyDeathRecord death,
        DateTime displayAnchorSeenAtUtc)
    {
        return GetLeadUpTimelineRows(death, displayAnchorSeenAtUtc, GetLeadUpEvents(death));
    }

    private IReadOnlyList<LeadUpTimelineRow> GetLeadUpTimelineRows(
        PartyDeathRecord death,
        DateTime displayAnchorSeenAtUtc,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        var cutoff = displayAnchorSeenAtUtc - TimeSpan.FromSeconds(LeadUpHistorySeconds);
        var history = death.HpHistory
            .Where(snapshot => snapshot.SeenAtUtc >= cutoff && snapshot.SeenAtUtc <= displayAnchorSeenAtUtc)
            .Where(snapshot => snapshot.CurrentHp > 0 || snapshot.ShieldHp > 0)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ToList();
        var events = leadUpEvents
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ToList();
        var displayHistory = GetDisplayLeadUpHpHistory(history, events);

        var rows = new List<LeadUpTimelineRow>();
        var historyIndex = 0;
        var eventIndex = 0;
        DerivedHpState? pendingDerivedHp = null;

        while (historyIndex < displayHistory.Count || eventIndex < events.Count)
        {
            var shouldTakeHistory = historyIndex < displayHistory.Count &&
                (eventIndex >= events.Count || displayHistory[historyIndex].SeenAtUtc <= events[eventIndex].SeenAtUtc);
            if (shouldTakeHistory)
            {
                var snapshot = displayHistory[historyIndex++];
                var activeSourceStatuses = GetActiveSourceMitigationStatuses(death, snapshot.SeenAtUtc, null, events);
                var activeSourceStatusNames = GetActiveSourceMitigationStatusSourceNames(death, snapshot.SeenAtUtc, null, events);
                var timelineRow = CreateHpSampleTimelineRow(
                    snapshot,
                    pendingDerivedHp,
                    displayAnchorSeenAtUtc,
                    activeSourceStatuses,
                    activeSourceStatusNames);
                AddLeadUpTimelineRow(rows, timelineRow);

                if (pendingDerivedHp is not null &&
                    snapshot.SeenAtUtc > pendingDerivedHp.EventSeenAtUtc &&
                    !IsStalePostHitSample(snapshot, pendingDerivedHp))
                {
                    pendingDerivedHp = null;
                }

                continue;
            }

            var combatEvent = events[eventIndex++];
            var (hpDisplay, healChange, damageChange) = GetTimelineEventHpDisplay(death, combatEvent, history, events);
            var eventSourceStatuses = GetEventSourceMitigationStatuses(death, combatEvent, events);
            var eventSourceStatusNames = GetEventSourceMitigationStatusSourceNames(death, combatEvent, events);
            if (ShouldKeepLethalDerivedHp(combatEvent.SeenAtUtc, pendingDerivedHp))
            {
                hpDisplay = CreateLethalDerivedHpDisplay(pendingDerivedHp!, hpDisplay.MaxHp);
                healChange = null;
                damageChange = null;
            }

            AddLeadUpTimelineRow(rows, new LeadUpTimelineRow(
                combatEvent.SeenAtUtc,
                combatEvent.PullElapsedSeconds,
                hpDisplay.CurrentHp,
                hpDisplay.ShieldHp,
                hpDisplay.MaxHp,
                combatEvent.Statuses,
                GetNearbyHpHistoryStatuses(history, combatEvent.SeenAtUtc),
                eventSourceStatuses,
                eventSourceStatusNames,
                combatEvent,
                hpDisplay.TooltipDetail,
                healChange,
                damageChange));

            pendingDerivedHp = TryCreateDerivedHpState(combatEvent, hpDisplay) ?? pendingDerivedHp;
        }

        return rows;
    }

    private static IReadOnlyList<HpHistorySnapshot> GetDisplayLeadUpHpHistory(
        IReadOnlyList<HpHistorySnapshot> history,
        IReadOnlyList<CombatEventRecord> events)
    {
        if (history.Count == 0)
        {
            return [];
        }

        var displayHistory = new List<HpHistorySnapshot>(history.Count);
        foreach (var snapshot in history)
        {
            if (IsHpSampleCoveredByNearbyEvent(snapshot, events, history))
            {
                continue;
            }

            if (displayHistory.Count > 0 &&
                CanMergeDisplayHpHistorySnapshot(displayHistory[^1], snapshot))
            {
                displayHistory[^1] = SelectPreferredDisplayHpHistorySnapshot(displayHistory[^1], snapshot);
                continue;
            }

            displayHistory.Add(snapshot);
        }

        return displayHistory;
    }

    private static bool IsHpSampleCoveredByNearbyEvent(
        HpHistorySnapshot snapshot,
        IReadOnlyList<CombatEventRecord> events,
        IReadOnlyList<HpHistorySnapshot> history)
    {
        foreach (var combatEvent in events)
        {
            if (IsIntermediateEffectResultHpSample(snapshot, combatEvent, history))
            {
                return true;
            }

            if (IsPostHealHpSampleCoveredByHealEvent(snapshot, combatEvent, events))
            {
                return true;
            }

            if (!IsWithinLeadUpEventHpSampleWindow(snapshot.SeenAtUtc, combatEvent.SeenAtUtc))
            {
                continue;
            }

            if (EventHasCapturedHp(combatEvent) ||
                snapshot.SeenAtUtc <= combatEvent.SeenAtUtc)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPostHealHpSampleCoveredByHealEvent(
        HpHistorySnapshot snapshot,
        CombatEventRecord combatEvent,
        IReadOnlyList<CombatEventRecord> events)
    {
        if (combatEvent.Kind != DeathEventKind.Heal ||
            combatEvent.Amount == 0 ||
            snapshot.SeenAtUtc < combatEvent.SeenAtUtc ||
            snapshot.SeenAtUtc - combatEvent.SeenAtUtc > LeadUpStatusMergeWindow ||
            HasInterveningHpChangingEvent(combatEvent, snapshot.SeenAtUtc, events))
        {
            return false;
        }

        if (CombatEventHasResultHp(combatEvent))
        {
            return HpSampleMatchesEffectResult(snapshot, combatEvent);
        }

        if (!EventHasCapturedHp(combatEvent))
        {
            return false;
        }

        var preHealDisplay = new EventHpDisplay(
            combatEvent.CurrentHp,
            combatEvent.ShieldHp,
            combatEvent.MaxHp,
            string.Empty);
        return PostHealSnapshotMatches(combatEvent, preHealDisplay, snapshot);
    }

    private static bool IsIntermediateEffectResultHpSample(
        HpHistorySnapshot snapshot,
        CombatEventRecord combatEvent,
        IReadOnlyList<HpHistorySnapshot> history)
    {
        if (!CombatEventHasResultHp(combatEvent) ||
            combatEvent.ResultSeenAtUtc is not { } resultSeenAtUtc ||
            snapshot.SeenAtUtc < combatEvent.SeenAtUtc ||
            !IsNearEffectResultHpSample(snapshot.SeenAtUtc, resultSeenAtUtc) ||
            HpSampleMatchesEffectResult(snapshot, combatEvent) ||
            !HasAuthoritativeEffectResultHpSample(history, combatEvent))
        {
            return false;
        }

        return true;
    }

    private static bool HistoryContainsEffectResultHpSample(
        IReadOnlyList<HpHistorySnapshot> history,
        CombatEventRecord combatEvent)
    {
        return history.Any(snapshot =>
            snapshot.SeenAtUtc >= combatEvent.SeenAtUtc &&
            combatEvent.ResultSeenAtUtc is { } resultSeenAtUtc &&
            IsWithinLeadUpEventHpSampleWindow(snapshot.SeenAtUtc, resultSeenAtUtc) &&
            HpSampleMatchesEffectResult(snapshot, combatEvent));
    }

    private static bool HasAuthoritativeEffectResultHpSample(
        IReadOnlyList<HpHistorySnapshot> history,
        CombatEventRecord combatEvent)
    {
        return EffectResultIsZeroHp(combatEvent) ||
            HistoryContainsEffectResultHpSample(history, combatEvent);
    }

    private static bool IsNearEffectResultHpSample(DateTime snapshotSeenAtUtc, DateTime resultSeenAtUtc)
    {
        if (snapshotSeenAtUtc <= resultSeenAtUtc)
        {
            return IsWithinLeadUpEventHpSampleWindow(snapshotSeenAtUtc, resultSeenAtUtc);
        }

        return Duration(snapshotSeenAtUtc, resultSeenAtUtc) <= LeadUpEventDuplicateWindow;
    }

    private static bool HpSampleMatchesEffectResult(HpHistorySnapshot snapshot, CombatEventRecord combatEvent)
    {
        return snapshot.CurrentHp == combatEvent.ResultCurrentHp &&
            snapshot.ShieldHp == combatEvent.ResultShieldHp &&
            snapshot.MaxHp == combatEvent.ResultMaxHp;
    }

    private static bool EventHasCapturedHp(CombatEventRecord combatEvent)
    {
        return combatEvent.HpSource != CombatEventHpSource.NoPreHitSample &&
            combatEvent.MaxHp > 0 &&
            (combatEvent.CurrentHp > 0 || combatEvent.ShieldHp > 0);
    }

    private static bool IsWithinLeadUpEventHpSampleWindow(DateTime first, DateTime second)
    {
        return Duration(first, second) <= LeadUpEventHpSampleWindow;
    }

    private static bool CanMergeDisplayHpHistorySnapshot(HpHistorySnapshot existing, HpHistorySnapshot snapshot)
    {
        return Duration(existing.SeenAtUtc, snapshot.SeenAtUtc) <= LeadUpHpDuplicateWindow &&
            existing.CurrentHp == snapshot.CurrentHp &&
            existing.ShieldHp == snapshot.ShieldHp &&
            existing.MaxHp == snapshot.MaxHp &&
            StatusListsMatchForHistoryMerge(existing.Statuses, snapshot.Statuses);
    }

    private static HpHistorySnapshot SelectPreferredDisplayHpHistorySnapshot(HpHistorySnapshot existing, HpHistorySnapshot snapshot)
    {
        if (snapshot.Statuses.Count > existing.Statuses.Count)
        {
            return snapshot;
        }

        if (existing.Statuses.Count > snapshot.Statuses.Count)
        {
            return existing;
        }

        return snapshot.SeenAtUtc >= existing.SeenAtUtc ? snapshot : existing;
    }

    private static TimeSpan Duration(DateTime first, DateTime second)
    {
        return first >= second ? first - second : second - first;
    }

    private static void AddLeadUpTimelineRow(List<LeadUpTimelineRow> rows, LeadUpTimelineRow row)
    {
        if (rows.Count > 0 && CanMergeLeadUpTimelineRow(rows[^1], row))
        {
            rows[^1] = row;
            return;
        }

        rows.Add(row);
    }

    private static bool CanMergeLeadUpTimelineRow(LeadUpTimelineRow previous, LeadUpTimelineRow next)
    {
        return previous.Event is null &&
            next.Event is null &&
            previous.CurrentHp == next.CurrentHp &&
            previous.ShieldHp == next.ShieldHp &&
            previous.MaxHp == next.MaxHp &&
            string.Equals(previous.HpTooltipDetail, next.HpTooltipDetail, StringComparison.Ordinal) &&
            StatusListsMatchForHistoryMerge(previous.Statuses, next.Statuses) &&
            StatusListsMatchForHistoryMerge(previous.NearbyHpStatuses, next.NearbyHpStatuses) &&
            StatusListsMatchForHistoryMerge(previous.SourceStatuses, next.SourceStatuses) &&
            BossStatusSourceNamesMatch(previous.SourceStatusNames, next.SourceStatusNames);
    }

    private LeadUpTimelineRow CreateHpSampleTimelineRow(
        HpHistorySnapshot snapshot,
        DerivedHpState? pendingDerivedHp,
        DateTime displayAnchorSeenAtUtc,
        IReadOnlyList<StatusSnapshot> sourceStatuses,
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> sourceStatusNames)
    {
        if (pendingDerivedHp is not null &&
            snapshot.SeenAtUtc > pendingDerivedHp.EventSeenAtUtc &&
            IsStalePostHitSample(snapshot, pendingDerivedHp))
        {
            var isLethalDerivedHit = IsLethalDerivedHpState(pendingDerivedHp);
            var displayShieldHp = isLethalDerivedHit || snapshot.ShieldHp == pendingDerivedHp.SourceShieldHp
                ? pendingDerivedHp.DerivedShieldHp
                : snapshot.ShieldHp;
            var shieldSourceText = isLethalDerivedHit || snapshot.ShieldHp == pendingDerivedHp.SourceShieldHp
                ? "shield was also derived from the hit"
                : "shield came from the captured sample";
            var resultSourceText = pendingDerivedHp.UsesCapturedResult ? "Captured HP after" : "Derived HP after";
            var tooltip = $"{resultSourceText} {FormatKnownPlayerName(pendingDerivedHp.SourceName)}: {FormatActionNameForDisplay(pendingDerivedHp.ActionName)} {FormatAmount(pendingDerivedHp.Amount)} at {FormatRelativeToDeath(displayAnchorSeenAtUtc, pendingDerivedHp.EventSeenAtUtc)}; {shieldSourceText}. Raw captured sample was {FormatHp(snapshot.CurrentHp, snapshot.ShieldHp, snapshot.MaxHp)}.";
            return new LeadUpTimelineRow(
                snapshot.SeenAtUtc,
                snapshot.PullElapsedSeconds,
                pendingDerivedHp.DerivedCurrentHp,
                displayShieldHp,
                snapshot.MaxHp > 0 ? snapshot.MaxHp : pendingDerivedHp.SourceMaxHp,
                snapshot.Statuses,
                snapshot.Statuses,
                sourceStatuses,
                sourceStatusNames,
                null,
                tooltip,
                null,
                null);
        }

        return new LeadUpTimelineRow(
            snapshot.SeenAtUtc,
            snapshot.PullElapsedSeconds,
            snapshot.CurrentHp,
            snapshot.ShieldHp,
            snapshot.MaxHp,
            snapshot.Statuses,
            snapshot.Statuses,
            sourceStatuses,
            sourceStatusNames,
            null,
            null,
            null,
            null);
    }

    private static bool IsStalePostHitSample(HpHistorySnapshot snapshot, DerivedHpState pendingDerivedHp)
    {
        if (snapshot.MaxHp != 0 &&
            pendingDerivedHp.SourceMaxHp != 0 &&
            snapshot.MaxHp != pendingDerivedHp.SourceMaxHp)
        {
            return false;
        }

        return snapshot.CurrentHp == pendingDerivedHp.SourceCurrentHp ||
            (IsLethalDerivedHpState(pendingDerivedHp) && (snapshot.CurrentHp > 0 || snapshot.ShieldHp > 0));
    }

    private static bool ShouldKeepLethalDerivedHp(DateTime seenAtUtc, DerivedHpState? pendingDerivedHp)
    {
        return pendingDerivedHp is not null &&
            seenAtUtc > pendingDerivedHp.EventSeenAtUtc &&
            IsLethalDerivedHpState(pendingDerivedHp);
    }

    private EventHpDisplay CreateLethalDerivedHpDisplay(DerivedHpState pendingDerivedHp, uint fallbackMaxHp)
    {
        return new EventHpDisplay(
            0,
            0,
            pendingDerivedHp.SourceMaxHp > 0 ? pendingDerivedHp.SourceMaxHp : fallbackMaxHp,
            $"HP stayed at zero after {FormatKnownPlayerName(pendingDerivedHp.SourceName)}: {FormatActionNameForDisplay(pendingDerivedHp.ActionName)}.");
    }

    private static bool IsLethalDerivedHpState(DerivedHpState pendingDerivedHp)
    {
        return pendingDerivedHp.DerivedCurrentHp == 0 &&
            pendingDerivedHp.DerivedShieldHp == 0;
    }

    private static DerivedHpState? TryCreateDerivedHpState(CombatEventRecord combatEvent, EventHpDisplay hpDisplay)
    {
        if (combatEvent.Kind != DeathEventKind.Damage || combatEvent.Amount == 0 || hpDisplay.MaxHp == 0)
        {
            return null;
        }

        if (CombatEventHasResultHp(combatEvent))
        {
            return new DerivedHpState(
                combatEvent.SeenAtUtc,
                combatEvent.SourceName,
                combatEvent.ActionName,
                combatEvent.Amount,
                hpDisplay.CurrentHp,
                hpDisplay.ShieldHp,
                hpDisplay.MaxHp,
                combatEvent.ResultCurrentHp,
                combatEvent.ResultShieldHp,
                true);
        }

        var remainingDamage = (ulong)combatEvent.Amount;
        var derivedShieldHp = (ulong)hpDisplay.ShieldHp;
        var shieldDamage = Math.Min(derivedShieldHp, remainingDamage);
        derivedShieldHp -= shieldDamage;
        remainingDamage -= shieldDamage;

        var derivedCurrentHp = (ulong)hpDisplay.CurrentHp;
        var hpDamage = Math.Min(derivedCurrentHp, remainingDamage);
        derivedCurrentHp -= hpDamage;

        return new DerivedHpState(
            combatEvent.SeenAtUtc,
            combatEvent.SourceName,
            combatEvent.ActionName,
            combatEvent.Amount,
            hpDisplay.CurrentHp,
            hpDisplay.ShieldHp,
            hpDisplay.MaxHp,
            (uint)derivedCurrentHp,
            (uint)derivedShieldHp,
            false);
    }

    private static bool CombatEventHasResultHp(CombatEventRecord combatEvent)
    {
        return combatEvent.ResultSeenAtUtc is not null &&
            combatEvent.ResultMaxHp > 0;
    }

    private static bool EffectResultIsZeroHp(CombatEventRecord combatEvent)
    {
        return CombatEventHasResultHp(combatEvent) &&
            combatEvent.ResultCurrentHp == 0 &&
            combatEvent.ResultShieldHp == 0;
    }

    private static IReadOnlyList<StatusSnapshot> GetNearbyHpHistoryStatuses(
        IReadOnlyList<HpHistorySnapshot> history,
        DateTime seenAtUtc)
    {
        var statuses = new List<StatusSnapshot>();
        var priorSnapshot = history
            .Where(snapshot => snapshot.SeenAtUtc <= seenAtUtc)
            .OrderByDescending(snapshot => snapshot.SeenAtUtc)
            .FirstOrDefault();
        if (priorSnapshot is not null && seenAtUtc - priorSnapshot.SeenAtUtc <= LeadUpStatusMergeWindow)
        {
            statuses.AddRange(priorSnapshot.Statuses);
        }

        var nextSnapshot = history
            .Where(snapshot => snapshot.SeenAtUtc > seenAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .FirstOrDefault();
        if (nextSnapshot is not null && nextSnapshot.SeenAtUtc - seenAtUtc <= LeadUpStatusMergeWindow)
        {
            statuses.AddRange(nextSnapshot.Statuses);
        }

        return statuses;
    }

    private void DrawTimelineEventCell(CombatEventRecord? combatEvent)
    {
        if (combatEvent is null)
        {
            DrawCenteredText("-", DisabledColor);
            return;
        }

        DrawCenteredWrappedCombatEventLine(combatEvent);
    }

    private IReadOnlyList<HpHistoryDisplayRow> GetLeadUpHpHistoryRows(PartyDeathRecord death, DateTime anchorSeenAtUtc)
    {
        return GetLeadUpHpHistoryRows(death, anchorSeenAtUtc, GetLeadUpEvents(death));
    }

    private IReadOnlyList<HpHistoryDisplayRow> GetLeadUpHpHistoryRows(
        PartyDeathRecord death,
        DateTime anchorSeenAtUtc,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        var cutoff = anchorSeenAtUtc - TimeSpan.FromSeconds(LeadUpHistorySeconds);
        var history = death.HpHistory
            .Where(snapshot => snapshot.SeenAtUtc >= cutoff && snapshot.SeenAtUtc <= anchorSeenAtUtc)
            .Where(snapshot => snapshot.CurrentHp > 0 || snapshot.ShieldHp > 0)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ToList();
        return history.Count == 0
            ? []
            : BuildHpHistoryDisplayRows(anchorSeenAtUtc, history, leadUpEvents);
    }

    private static IReadOnlyList<HpHistoryDisplayRow> BuildHpHistoryDisplayRows(
        DateTime anchorSeenAtUtc,
        IReadOnlyList<HpHistorySnapshot> history,
        IReadOnlyList<CombatEventRecord> events)
    {
        var rows = new List<HpHistoryDisplayRow>();
        for (var i = 0; i < history.Count; i++)
        {
            var snapshot = history[i];
            var isLastSample = i + 1 >= history.Count;
            var nextSampleAt = i + 1 < history.Count
                ? history[i + 1].SeenAtUtc
                : anchorSeenAtUtc;
            var nextEvents = events
                .Where(combatEvent => combatEvent.SeenAtUtc >= snapshot.SeenAtUtc &&
                    (isLastSample ? combatEvent.SeenAtUtc <= nextSampleAt : combatEvent.SeenAtUtc < nextSampleAt))
                .ToList();

            if (rows.Count > 0 && CanMergeHpHistoryRow(rows[^1], snapshot, nextEvents))
            {
                rows[^1] = rows[^1] with
                {
                    LastSnapshot = snapshot,
                    SampleCount = rows[^1].SampleCount + 1,
                };
                continue;
            }

            rows.Add(new HpHistoryDisplayRow(snapshot, snapshot, nextEvents, 1));
        }

        return rows;
    }

    private static bool CanMergeHpHistoryRow(
        HpHistoryDisplayRow previousRow,
        HpHistorySnapshot snapshot,
        IReadOnlyList<CombatEventRecord> nextEvents)
    {
        return previousRow.Events.Count == 0 &&
            nextEvents.Count == 0 &&
            previousRow.LastSnapshot.CurrentHp == snapshot.CurrentHp &&
            previousRow.LastSnapshot.ShieldHp == snapshot.ShieldHp &&
            previousRow.LastSnapshot.MaxHp == snapshot.MaxHp &&
            StatusListsMatchForHistoryMerge(previousRow.LastSnapshot.Statuses, snapshot.Statuses);
    }

    private static bool StatusListsMatchForHistoryMerge(
        IReadOnlyList<StatusSnapshot> first,
        IReadOnlyList<StatusSnapshot> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        var firstOrdered = first
            .OrderBy(status => status.Id)
            .ThenBy(status => status.SourceId)
            .ThenBy(status => status.StackCount)
            .ThenBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var secondOrdered = second
            .OrderBy(status => status.Id)
            .ThenBy(status => status.SourceId)
            .ThenBy(status => status.StackCount)
            .ThenBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return firstOrdered.Zip(secondOrdered).All(pair =>
            pair.First.Id == pair.Second.Id &&
            pair.First.IconId == pair.Second.IconId &&
            pair.First.SourceId == pair.Second.SourceId &&
            pair.First.StackCount == pair.Second.StackCount &&
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal));
    }

    private void DrawStatusSnapshot(
        IReadOnlyList<StatusSnapshot> statuses,
        string idSuffix,
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>>? bossSourceNamesByStatus = null)
    {
        DrawLeadUpLabel("Active mitigations/debuffs at death");
        if (statuses.Count == 0)
        {
            ImGui.TextDisabled("No defensive, mitigation, shield, or encounter debuff statuses captured.");
            return;
        }

        var focused = IsFocusedReviewMode();
        if (focused)
        {
            DrawFocusedStatusSnapshotList(statuses, bossSourceNamesByStatus);
            DrawMitigationTotal(statuses);
            return;
        }

        if (!ImGui.BeginTable($"##DeathStatusSnapshot{idSuffix}", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Ability", ImGuiTableColumnFlags.WidthStretch, 2.25f);
        ImGui.TableSetupColumn("Mit Type", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("Mit%", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("Linked Effects", ImGuiTableColumnFlags.WidthStretch, 0.95f);
        DrawCenteredTableHeader("Ability", "Mit Type", "Mit%", "Linked Effects");

        foreach (var status in statuses.OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase))
        {
            var displayInfo = Plugin.GetMitigationDisplayInfo(status);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawStatusSnapshotAbility(status, ImGui.GetContentRegionAvail().X, bossSourceNamesByStatus);
            ImGui.TableNextColumn();
            DrawMitigationTypeCell(displayInfo.Types);
            ImGui.TableNextColumn();
            DrawMitigationPercentCell(displayInfo);
            ImGui.TableNextColumn();
            DrawInducedStatusesCell(displayInfo.InducedStatuses);
        }

        ImGui.EndTable();
        DrawMitigationTotal(statuses);
    }

    private void DrawStatusSnapshotAbility(
        StatusSnapshot status,
        float width,
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>>? bossSourceNamesByStatus)
    {
        var iconSize = Math.Clamp(configuration.StatusIconSize, 14.0f, 22.0f);
        var bossSourceNames = GetBossSourceNamesForStatus(status, bossSourceNamesByStatus);
        DrawIconTextWrapped(
            status.IconId,
            iconSize,
            FormatStatusSourceTooltip(status.Name, bossSourceNames),
            status.Name,
            MathF.Max(24.0f, width),
            bossSourceNames is { Count: > 0 } ? ModernAccentColor : null);
    }

    private void DrawFocusedStatusSnapshotList(
        IReadOnlyList<StatusSnapshot> statuses,
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>>? bossSourceNamesByStatus)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var iconSize = Math.Clamp(configuration.StatusIconSize, 14.0f, 22.0f);
        var innerWidth = MathF.Max(0.0f, availableWidth - (FocusedDataRowPaddingX * 2.0f));
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var iconColumnWidth = iconSize + 8.0f;
        var remainingWidth = MathF.Max(80.0f, innerWidth - iconColumnWidth - (spacing * 2.0f));
        var abilityMinimumWidth = MathF.Min(44.0f, remainingWidth * 0.42f);
        var typeMaximumWidth = MathF.Max(48.0f, MathF.Min(210.0f, remainingWidth - abilityMinimumWidth));
        var typeMinimumWidth = MathF.Min(104.0f, typeMaximumWidth);
        var typeColumnWidth = Math.Clamp(remainingWidth * 0.46f, typeMinimumWidth, typeMaximumWidth);
        var abilityColumnWidth = MathF.Max(abilityMinimumWidth, remainingWidth - typeColumnWidth);

        DrawFocusedColumnLabels(
            (FocusedDataRowPaddingX + iconColumnWidth + spacing, "Ability"),
            (FocusedDataRowPaddingX + iconColumnWidth + spacing + abilityColumnWidth + spacing, "Type"));

        var rowIndex = 0;
        foreach (var status in statuses.OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase))
        {
            var displayInfo = Plugin.GetMitigationDisplayInfo(status);
            var abilityHeight = GetWrappedTextHeight(status.Name, abilityColumnWidth);
            var typeHeight = GetMitigationTypeInlineHeight(displayInfo.Types, typeColumnWidth, iconSize);
            var rowHeight = MathF.Max(iconSize, MathF.Max(abilityHeight, typeHeight)) + (FocusedDataRowPaddingY * 2.0f);
            var rowStart = ImGui.GetCursorScreenPos();
            DrawFocusedDataRowBackground(rowStart, availableWidth, rowHeight, rowIndex);
            var bossSourceNames = GetBossSourceNamesForStatus(status, bossSourceNamesByStatus);

            var contentY = rowStart.Y + FocusedDataRowPaddingY;
            ImGui.SetCursorScreenPos(new Vector2(rowStart.X + FocusedDataRowPaddingX, contentY));
            DrawGameIcon(status.IconId, iconSize, FormatStatusSourceTooltip(status.Name, bossSourceNames));
            DrawBossStatusIconBorderIfNeeded(bossSourceNames);

            ImGui.SetCursorScreenPos(new Vector2(rowStart.X + FocusedDataRowPaddingX + iconColumnWidth + spacing, contentY));
            DrawWrappedTextLines(status.Name, abilityColumnWidth);

            ImGui.SetCursorScreenPos(new Vector2(
                rowStart.X + FocusedDataRowPaddingX + iconColumnWidth + spacing + abilityColumnWidth + spacing,
                contentY));
            DrawMitigationTypeInline(displayInfo.Types, typeColumnWidth, iconSize);

            ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowStart.Y + rowHeight + FocusedDataRowGap));
            rowIndex++;
        }
    }

    private void DrawMitigationTypeCell(IReadOnlyList<Plugin.MitigationTypeDisplay> types)
    {
        if (types.Count == 0)
        {
            DrawCenteredText("-", DisabledColor);
            return;
        }

        var iconSize = Math.Clamp(configuration.StatusIconSize, 12.0f, 22.0f);
        foreach (var type in types)
        {
            DrawMitigationTypeLine(type, iconSize);
        }
    }

    private static void DrawMitigationTypeLine(Plugin.MitigationTypeDisplay type, float iconSize)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var lineWidth = (type.IconId == 0 ? 0.0f : iconSize + spacing) + ImGui.CalcTextSize(type.Label).X;
        CenterNextItem(lineWidth);

        ImGui.BeginGroup();
        if (type.IconId != 0)
        {
            DrawGameIcon(type.IconId, iconSize, type.Tooltip ?? type.Label);
            ImGui.SameLine();
        }

        ImGui.TextUnformatted(type.Label);
        if (ImGui.IsItemHovered() && type.Tooltip is not null)
        {
            SetThemedTooltip(type.Tooltip);
        }

        ImGui.EndGroup();
    }

    private void DrawMitigationTypeInline(IReadOnlyList<Plugin.MitigationTypeDisplay> types, float width, float iconSize)
    {
        if (types.Count == 0)
        {
            ImGui.TextColored(DisabledColor, "-");
            return;
        }

        var columnStart = ImGui.GetCursorScreenPos();
        var currentY = columnStart.Y;
        var lineGap = ImGui.GetStyle().ItemSpacing.Y * 0.35f;
        foreach (var type in types)
        {
            ImGui.SetCursorScreenPos(new Vector2(columnStart.X, currentY));
            DrawIconTextWrapped(type.IconId, iconSize, type.Tooltip ?? type.Label, type.Label, width);
            currentY += GetIconTextWrappedHeight(type.Label, width, type.IconId, iconSize) + lineGap;
        }

        ImGui.SetCursorScreenPos(new Vector2(columnStart.X, currentY - lineGap));
    }

    private static float GetMitigationTypeInlineHeight(
        IReadOnlyList<Plugin.MitigationTypeDisplay> types,
        float width,
        float iconSize)
    {
        if (types.Count == 0)
        {
            return ImGui.GetTextLineHeight();
        }

        var lineGap = ImGui.GetStyle().ItemSpacing.Y * 0.35f;
        return types.Sum(type => GetIconTextWrappedHeight(type.Label, width, type.IconId, iconSize)) +
            MathF.Max(0, types.Count - 1) * lineGap;
    }

    private static void DrawMitigationPercentCell(Plugin.MitigationDisplayInfo displayInfo)
    {
        if (displayInfo.MitigationPercents.Count == 0)
        {
            DrawCenteredText("-", DisabledColor);
            return;
        }

        var iconSize = Math.Clamp(ImGui.GetTextLineHeight(), 12.0f, 18.0f);
        if (displayInfo.MitigationPercents.Count > 1)
        {
            foreach (var part in displayInfo.MitigationPercents)
            {
                DrawMitigationPercentLine(part, iconSize, displayInfo);
            }

            return;
        }

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var percentPart = displayInfo.MitigationPercents[0];
        var groupWidth = (percentPart.IconId == 0 ? 0.0f : iconSize + spacing) + ImGui.CalcTextSize(percentPart.Text).X;
        CenterNextItem(groupWidth);

        ImGui.BeginGroup();
        DrawMitigationPercentPart(percentPart, iconSize, displayInfo);
        ImGui.EndGroup();
    }

    private static void DrawMitigationPercentLine(
        Plugin.MitigationPercentDisplay part,
        float iconSize,
        Plugin.MitigationDisplayInfo displayInfo)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var lineWidth = (part.IconId == 0 ? 0.0f : iconSize + spacing) + ImGui.CalcTextSize(part.Text).X;
        CenterNextItem(lineWidth);

        ImGui.BeginGroup();
        DrawMitigationPercentPart(part, iconSize, displayInfo);
        ImGui.EndGroup();
    }

    private static void DrawMitigationPercentPart(
        Plugin.MitigationPercentDisplay part,
        float iconSize,
        Plugin.MitigationDisplayInfo displayInfo)
    {
        var tooltip = CombineTooltips(part.Tooltip, displayInfo.HasVariableMitigationPercent ? displayInfo.MitigationPercentTooltip : null);
        if (part.IconId != 0)
        {
            DrawGameIcon(part.IconId, iconSize, tooltip ?? part.Text);
            ImGui.SameLine();
        }

        if (displayInfo.HasVariableMitigationPercent)
        {
            ImGui.TextColored(GetBreathingGoldColor(), part.Text);
        }
        else
        {
            ImGui.TextUnformatted(part.Text);
        }

        if (ImGui.IsItemHovered() && tooltip is not null)
        {
            SetThemedTooltip(tooltip);
        }
    }

    private static string? CombineTooltips(string? first, string? second)
    {
        return (first, second) switch
        {
            (null, null) => null,
            (not null, null) => first,
            (null, not null) => second,
            _ => $"{first}\n{second}",
        };
    }

    private static void DrawMitigationTotal(IReadOnlyList<StatusSnapshot> statuses)
    {
        var total = CalculateMitigationTotal(statuses);
        if (total is null)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.TextColored(LeadUpGoldColor, "Mit total:");
        ImGui.SameLine();
        if (total.HasTypedReduction)
        {
            DrawTypedMitigationTotal(total);
        }
        else
        {
            ImGui.TextUnformatted(FormatMitigationTotalPercent(total.AllReduction, total.AllVariable));
            if (ImGui.IsItemHovered())
            {
                DrawMitigationTotalTooltip();
            }
        }
    }

    private static void DrawTypedMitigationTotal(MitigationTotalDisplay total)
    {
        ImGui.BeginGroup();
        DrawMitigationTotalPart(
            Plugin.PhysicalDamageReductionIconId,
            FormatMitigationTotalPercent(total.PhysicalReduction, total.PhysicalVariable),
            "Physical damage reduction total");
        ImGui.SameLine();
        ImGui.TextUnformatted("/");
        ImGui.SameLine();
        DrawMitigationTotalPart(
            Plugin.MagicDamageReductionIconId,
            FormatMitigationTotalPercent(total.MagicReduction, total.MagicVariable),
            "Magic damage reduction total");
        ImGui.EndGroup();

        if (ImGui.IsItemHovered())
        {
            DrawMitigationTotalTooltip();
        }
    }

    private static void DrawMitigationTotalPart(uint iconId, string text, string tooltip)
    {
        var iconSize = Math.Clamp(ImGui.GetTextLineHeight(), 12.0f, 18.0f);
        DrawGameIcon(iconId, iconSize, tooltip);
        ImGui.SameLine();
        ImGui.TextUnformatted(text);
    }

    private static void DrawMitigationTotalTooltip()
    {
        SetThemedTooltip("Calculated Multiplicatively.");
    }

    private static MitigationTotalDisplay? CalculateMitigationTotal(IReadOnlyList<StatusSnapshot> statuses)
    {
        var allRemaining = 1.0;
        var physicalRemaining = 1.0;
        var magicRemaining = 1.0;
        var hasAnyReduction = false;
        var hasPhysicalReduction = false;
        var hasMagicReduction = false;
        var allVariable = false;
        var physicalVariable = false;
        var magicVariable = false;

        foreach (var status in statuses)
        {
            var displayInfo = Plugin.GetMitigationDisplayInfo(status);
            foreach (var part in displayInfo.MitigationPercents)
            {
                if (part.Percent <= 0.0f)
                {
                    continue;
                }

                var remaining = 1.0 - (Math.Clamp(part.Percent, 0.0f, 100.0f) / 100.0);
                hasAnyReduction = true;

                switch (part.Scope)
                {
                    case Plugin.MitigationPercentScope.Physical:
                        physicalRemaining *= remaining;
                        hasPhysicalReduction = true;
                        physicalVariable |= displayInfo.HasVariableMitigationPercent;
                        break;
                    case Plugin.MitigationPercentScope.Magic:
                        magicRemaining *= remaining;
                        hasMagicReduction = true;
                        magicVariable |= displayInfo.HasVariableMitigationPercent;
                        break;
                    default:
                        allRemaining *= remaining;
                        allVariable |= displayInfo.HasVariableMitigationPercent;
                        break;
                }
            }
        }

        if (!hasAnyReduction)
        {
            return null;
        }

        return new MitigationTotalDisplay(
            1.0 - allRemaining,
            1.0 - (allRemaining * physicalRemaining),
            1.0 - (allRemaining * magicRemaining),
            hasPhysicalReduction || hasMagicReduction,
            allVariable,
            allVariable || physicalVariable,
            allVariable || magicVariable);
    }

    private static string FormatMitigationTotalPercent(double reduction, bool variable)
    {
        var clampedPercent = Math.Clamp(reduction, 0.0, 1.0) * 100.0;
        return $"{clampedPercent:0.#}%{(variable ? "+" : string.Empty)}";
    }

    private void DrawInducedStatusesCell(IReadOnlyList<Plugin.InducedMitigationDisplay> inducedStatuses)
    {
        if (inducedStatuses.Count == 0)
        {
            DrawCenteredText("-", DisabledColor);
            return;
        }

        var iconSize = Math.Clamp(configuration.StatusIconSize, 12.0f, 24.0f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var groupWidth = (inducedStatuses.Count * iconSize) + ((inducedStatuses.Count - 1) * spacing);
        CenterNextItem(groupWidth);

        ImGui.BeginGroup();
        for (var i = 0; i < inducedStatuses.Count; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            var inducedStatus = inducedStatuses[i];
            DrawGameIcon(GetStatusIconId(inducedStatus.StatusId), iconSize, inducedStatus.Name);
        }

        ImGui.EndGroup();
    }

    private static IReadOnlyList<StatusSnapshot> GetSelectedPlayerStatuses(PartyDeathRecord death)
    {
        return DeathDisplaySelector.Select(death).Snapshot?.Statuses ?? death.StatusesAtDeath;
    }

    private void DrawEarlierBossDebuffsNotOnFatalHit(ResolvedDeathDisplay resolved, string idSuffix)
    {
        var death = resolved.Death;
        DrawLeadUpLabel("Expired Mits");
        var selection = resolved.Selection;
        if (selection.Events.Count == 0)
        {
            ImGui.TextDisabled("No fatal hit was captured to compare against.");
            return;
        }

        var rows = GetEarlierBossDebuffsNotOnFatalHit(death, selection, resolved.LeadUpEvents);
        if (rows.Count == 0)
        {
            ImGui.TextDisabled("No earlier boss damage-down debuffs were captured outside the fatal hit.");
            return;
        }

        if (!ImGui.BeginTable($"##EarlierBossDebuffs{idSuffix}", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Seen", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Timer", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("Earlier hit", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("Debuff", ImGuiTableColumnFlags.WidthStretch, 1.7f);
        DrawCenteredTableHeader("Seen", "Timer", "Source", "Earlier hit", "Debuff");

        foreach (var row in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredText(FormatRelativeToDeath(selection.AnchorSeenAtUtc, row.SeenAtUtc));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatCombatTimer(row.PullElapsedSeconds));
            ImGui.TableNextColumn();
            DrawCenteredOrWrappedText(FormatKnownPlayerName(row.SourceName));
            ImGui.TableNextColumn();
            DrawCenteredOrWrappedText(FormatActionNameForDisplay(row.ActionName));
            ImGui.TableNextColumn();
            DrawCenteredIconText(row.Status.IconId, configuration.StatusIconSize, row.Status.Name, row.Status.Name);
        }

        ImGui.EndTable();
    }

    private static IReadOnlyList<EarlierBossDebuffRow> GetEarlierBossDebuffsNotOnFatalHit(
        PartyDeathRecord death,
        DeathDisplaySelection selection,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        var firstFatalHitAtUtc = selection.Events
            .Select(combatEvent => combatEvent.SeenAtUtc)
            .OrderBy(seenAtUtc => seenAtUtc)
            .FirstOrDefault();
        var fatalEventKeys = selection.Events
            .SelectMany(combatEvent => Plugin.GetBossMitigationStatusesForDisplay(GetEventSourceMitigationStatuses(death, combatEvent, leadUpEvents))
                .Select(status => BuildBossDebuffKey(GetSourceKey(combatEvent), status.Id)))
            .ToHashSet(StringComparer.Ordinal);

        return leadUpEvents
            .Where(combatEvent => combatEvent.SeenAtUtc < firstFatalHitAtUtc)
            .SelectMany(combatEvent =>
            {
                var sourceKey = GetSourceKey(combatEvent);
                return Plugin.GetBossMitigationStatusesForDisplay(GetEventSourceMitigationStatuses(death, combatEvent, leadUpEvents))
                    .Where(status => !fatalEventKeys.Contains(BuildBossDebuffKey(sourceKey, status.Id)))
                    .Select(status => new EarlierBossDebuffRow(
                        combatEvent.SeenAtUtc,
                        combatEvent.PullElapsedSeconds,
                        sourceKey,
                        combatEvent.SourceName,
                        combatEvent.ActionName,
                        status));
            })
            .GroupBy(row => BuildBossDebuffKey(row.SourceKey, row.Status.Id))
            .Select(group => group.OrderByDescending(row => row.SeenAtUtc).First())
            .OrderByDescending(row => row.SeenAtUtc)
            .ThenBy(row => row.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Status.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildBossDebuffKey(string sourceKey, uint statusId)
    {
        return $"{sourceKey}:{statusId}";
    }

    private static string GetSourceKey(CombatEventRecord combatEvent)
    {
        return combatEvent.SourceEntityId == 0
            ? combatEvent.SourceName
            : combatEvent.SourceEntityId.ToString("X8");
    }

    private static void DrawLeadUpLabel(string label)
    {
        ImGui.TextColored(LeadUpGoldColor, label);
    }

    private static void DrawLeadUpLabelWithInlineMutedText(string label, string mutedText)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var labelWidth = ImGui.CalcTextSize(label).X;
        var mutedWidth = ImGui.CalcTextSize(mutedText).X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.TextColored(LeadUpGoldColor, label);
        if (availableWidth - labelWidth - spacing >= mutedWidth)
        {
            ImGui.SameLine(0.0f, spacing);
            ImGui.TextColored(ModernMutedTextColor, mutedText);
        }
        else
        {
            ImGui.TextColored(ModernMutedTextColor, mutedText);
        }
    }

    private static void DrawFocusedColumnLabels(params (float OffsetX, string Label)[] labels)
    {
        var rowStart = ImGui.GetCursorScreenPos();
        foreach (var (offsetX, label) in labels)
        {
            ImGui.SetCursorScreenPos(new Vector2(rowStart.X + offsetX, rowStart.Y));
            ImGui.TextColored(ModernMutedTextColor, label);
        }

        ImGui.SetCursorScreenPos(new Vector2(
            rowStart.X,
            rowStart.Y + ImGui.GetTextLineHeight() + (ImGui.GetStyle().ItemSpacing.Y * 0.4f)));
    }

    private static void DrawFocusedDataRowBackground(Vector2 rowStart, float width, float height, int rowIndex)
    {
        var drawList = ImGui.GetWindowDrawList();
        var baseColor = rowIndex % 2 == 0
            ? activeTheme.FocusedRowColor
            : BlendColors(activeTheme.FocusedRowColor, ModernPanelAltColor, 0.18f);
        var accentColor = activeTheme.FocusedRowAccentColor;
        var rowEnd = rowStart + new Vector2(width, height);
        drawList.AddRectFilled(rowStart, rowEnd, ImGui.GetColorU32(baseColor), 4.0f);
        drawList.AddRectFilled(
            rowStart,
            rowStart + new Vector2(2.0f, height),
            ImGui.GetColorU32(accentColor),
            4.0f);
    }

    private static float GetWrappedTextHeight(string text, float width)
    {
        return WrapTextForWidth(text, width).Count * ImGui.GetTextLineHeight();
    }

    private static void DrawWrappedTextLines(string text, float width, Vector4? color = null)
    {
        if (color is { } textColor)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        }

        foreach (var line in WrapTextForWidth(text, width))
        {
            ImGui.TextUnformatted(line);
        }

        if (color is not null)
        {
            ImGui.PopStyleColor();
        }
    }

    private static float GetIconTextWrappedHeight(string text, float width, uint iconId, float iconSize)
    {
        if (iconId == 0)
        {
            return GetWrappedTextHeight(text, width);
        }

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var textWidth = MathF.Max(24.0f, width - iconSize - spacing);
        return MathF.Max(iconSize, GetWrappedTextHeight(text, textWidth));
    }

    private static void DrawIconTextWrapped(
        uint iconId,
        float iconSize,
        string tooltip,
        string text,
        float width,
        Vector4? iconBorderColor = null)
    {
        if (iconId == 0)
        {
            DrawWrappedTextLines(text, width);
            return;
        }

        var rowStart = ImGui.GetCursorScreenPos();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var clampedIconSize = Math.Clamp(iconSize, 12.0f, 48.0f);
        DrawGameIcon(iconId, clampedIconSize, tooltip);
        DrawIconBorderIfNeeded(iconBorderColor);
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + clampedIconSize + spacing, rowStart.Y));
        DrawWrappedTextLines(text, MathF.Max(24.0f, width - clampedIconSize - spacing));
    }

    private static void DrawBossStatusIconBorderIfNeeded(IReadOnlyList<string>? bossSourceNames)
    {
        DrawIconBorderIfNeeded(bossSourceNames is { Count: > 0 } ? ModernAccentColor : null);
    }

    private static void DrawIconBorderIfNeeded(Vector4? borderColor)
    {
        if (borderColor is not { } color)
        {
            return;
        }

        ImGui.GetWindowDrawList().AddRect(
            ImGui.GetItemRectMin() - new Vector2(1.0f),
            ImGui.GetItemRectMax() + new Vector2(1.0f),
            ImGui.GetColorU32(color),
            3.0f,
            ImDrawFlags.None,
            1.8f);
    }

    private static void DrawMutedWrappedText(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ModernMutedTextColor);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private void DrawLeadUpTimelineTimerToggle(string idSuffix)
    {
        var showTimers = configuration.ShowLeadUpTimelineMitigationTimers;
        if (DrawThemedSwitch("Timers", $"LeadUpTimelineMitigationTimers{idSuffix}", ref showTimers))
        {
            configuration.ShowLeadUpTimelineMitigationTimers = showTimers;
            plugin.SaveConfiguration();
        }
    }

    private static bool DrawThemedSwitch(string label, string id, ref bool value)
    {
        var style = ImGui.GetStyle();
        var textSize = ImGui.CalcTextSize(label);
        var switchSize = new Vector2(18.0f, 28.0f);
        var labelOffsetY = MathF.Max(0.0f, (switchSize.Y - textSize.Y) * 0.5f);
        var cursor = ImGui.GetCursorScreenPos();

        var switchPosition = cursor;
        var clicked = ImGui.InvisibleButton($"##{id}", switchSize);
        if (clicked)
        {
            value = !value;
        }

        var hovered = ImGui.IsItemHovered();
        var end = switchPosition + switchSize;
        var rounding = switchSize.X * 0.5f;
        var fill = value
            ? ModernAccentSoftColor with { W = ActiveThemeUsesLightPanels() ? 0.96f : 0.92f }
            : GetCheckboxFrameColor();
        var border = hovered
            ? ModernAccentColor
            : GetCheckboxBorderColor();
        var knobColor = value
            ? ModernAccentColor
            : BlendColors(ModernMutedTextColor, ModernPanelColor, ActiveThemeUsesLightPanels() ? 0.18f : 0.10f) with { W = 1.0f };
        var knobRadius = 5.6f;
        var knobCenterY = value
            ? switchPosition.Y + 7.5f
            : end.Y - 7.5f;
        var knobCenter = new Vector2(switchPosition.X + (switchSize.X * 0.5f), knobCenterY);
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(switchPosition, end, ImGui.GetColorU32(fill), rounding);
        drawList.AddRect(switchPosition, end, ImGui.GetColorU32(border), rounding, ImDrawFlags.None, 1.0f);
        drawList.AddCircleFilled(knobCenter, knobRadius, ImGui.GetColorU32(knobColor), 18);

        ImGui.SameLine(0.0f, style.ItemInnerSpacing.X);
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, cursor.Y + labelOffsetY));
        ImGui.TextUnformatted(label);

        return clicked;
    }

    private static IReadOnlyList<CombatEventRecord> GetLeadUpEvents(PartyDeathRecord death)
    {
        return DeduplicateLeadUpDisplayEvents(DeathDisplaySelector.GetLeadUpEvents(death));
    }

    private static IReadOnlyList<CombatEventRecord> DeduplicateLeadUpDisplayEvents(IReadOnlyList<CombatEventRecord> events)
    {
        if (events.Count < 2)
        {
            return events;
        }

        var deduplicated = new List<CombatEventRecord>(events.Count);
        foreach (var combatEvent in events)
        {
            if (deduplicated.Count > 0 &&
                CanMergeLeadUpDisplayEvent(deduplicated[^1], combatEvent))
            {
                continue;
            }

            deduplicated.Add(combatEvent);
        }

        return deduplicated;
    }

    private static bool CanMergeLeadUpDisplayEvent(CombatEventRecord previous, CombatEventRecord next)
    {
        return Duration(previous.SeenAtUtc, next.SeenAtUtc) <= LeadUpEventDuplicateWindow &&
            previous.MemberKey == next.MemberKey &&
            SourceMatchesForLeadUpDisplay(previous, next) &&
            previous.ActionId == next.ActionId &&
            previous.Kind == next.Kind &&
            previous.Amount == next.Amount &&
            previous.CurrentHp == next.CurrentHp &&
            previous.ShieldHp == next.ShieldHp &&
            previous.MaxHp == next.MaxHp &&
            previous.DamageType == next.DamageType &&
            previous.Critical == next.Critical &&
            previous.DirectHit == next.DirectHit &&
            previous.Blocked == next.Blocked &&
            previous.Parried == next.Parried &&
            string.Equals(previous.ActionName, next.ActionName, StringComparison.Ordinal) &&
            string.Equals(previous.SourceName, next.SourceName, StringComparison.Ordinal) &&
            string.Equals(previous.Detail, next.Detail, StringComparison.Ordinal) &&
            StatusListsMatchForHistoryMerge(previous.Statuses, next.Statuses) &&
            StatusListsMatchForHistoryMerge(previous.SourceStatuses, next.SourceStatuses);
    }

    private static bool SourceMatchesForLeadUpDisplay(CombatEventRecord previous, CombatEventRecord next)
    {
        return previous.SourceEntityId == next.SourceEntityId ||
            string.Equals(previous.SourceName, next.SourceName, StringComparison.Ordinal);
    }

    private static bool IsFatalEvent(CombatEventRecord combatEvent)
    {
        return DeathDisplaySelector.IsFatalEvent(combatEvent);
    }

    private static DateTime GetLeadUpAnchorSeenAtUtc(PartyDeathRecord death)
    {
        return DeathDisplaySelector.Select(death).AnchorSeenAtUtc;
    }

    private static DateTime GetLeadUpDisplayAnchorSeenAtUtc(PartyDeathRecord death)
    {
        return death.SeenAtUtc;
    }

    private static (EventHpDisplay HpDisplay, HpBarHealChange? HealChange, HpBarDamageChange? DamageChange) GetTimelineEventHpDisplay(
        PartyDeathRecord death,
        CombatEventRecord combatEvent,
        IReadOnlyList<HpHistorySnapshot> history,
        IReadOnlyList<CombatEventRecord> events)
    {
        var hpDisplay = GetEventHpDisplay(death, combatEvent);
        var damageChange = TryGetPostDamageHpChange(combatEvent, hpDisplay);
        var postHealDisplay = TryGetPostHealHpDisplay(combatEvent, hpDisplay, history, events);
        if (postHealDisplay is null)
        {
            return (hpDisplay, null, damageChange);
        }

        return (postHealDisplay, new HpBarHealChange(hpDisplay.CurrentHp, hpDisplay.ShieldHp), null);
    }

    private static HpBarDamageChange? TryGetPostDamageHpChange(CombatEventRecord combatEvent, EventHpDisplay preDamageDisplay)
    {
        if (combatEvent.Kind != DeathEventKind.Damage ||
            combatEvent.Amount == 0 ||
            preDamageDisplay.MaxHp == 0 ||
            (preDamageDisplay.CurrentHp == 0 && preDamageDisplay.ShieldHp == 0))
        {
            return null;
        }

        if (CombatEventHasResultHp(combatEvent))
        {
            var resultMaxHp = combatEvent.ResultMaxHp == 0 ? preDamageDisplay.MaxHp : combatEvent.ResultMaxHp;
            var resultCurrentHp = Math.Min(combatEvent.ResultCurrentHp, resultMaxHp);
            var resultShieldHp = combatEvent.ResultShieldHp;
            return HpOrShieldDecreased(preDamageDisplay, resultCurrentHp, resultShieldHp)
                ? new HpBarDamageChange(resultCurrentHp, resultShieldHp)
                : null;
        }

        var remainingDamage = (ulong)combatEvent.Amount;
        var derivedResultShieldHp = (ulong)preDamageDisplay.ShieldHp;
        var shieldDamage = Math.Min(derivedResultShieldHp, remainingDamage);
        derivedResultShieldHp -= shieldDamage;
        remainingDamage -= shieldDamage;

        var derivedResultCurrentHp = (ulong)preDamageDisplay.CurrentHp;
        var hpDamage = Math.Min(derivedResultCurrentHp, remainingDamage);
        derivedResultCurrentHp -= hpDamage;

        return HpOrShieldDecreased(preDamageDisplay, (uint)derivedResultCurrentHp, (uint)derivedResultShieldHp)
            ? new HpBarDamageChange((uint)derivedResultCurrentHp, (uint)derivedResultShieldHp)
            : null;
    }

    private static EventHpDisplay? TryGetPostHealHpDisplay(
        CombatEventRecord combatEvent,
        EventHpDisplay preHealDisplay,
        IReadOnlyList<HpHistorySnapshot> history,
        IReadOnlyList<CombatEventRecord> events)
    {
        if (combatEvent.Kind != DeathEventKind.Heal || combatEvent.Amount == 0 || preHealDisplay.MaxHp == 0)
        {
            return null;
        }

        if (CombatEventHasResultHp(combatEvent))
        {
            var resultMaxHp = combatEvent.ResultMaxHp == 0 ? preHealDisplay.MaxHp : combatEvent.ResultMaxHp;
            var resultCurrentHp = Math.Min(combatEvent.ResultCurrentHp, resultMaxHp);
            if (HpOrShieldIncreased(preHealDisplay, resultCurrentHp, combatEvent.ResultShieldHp))
            {
                return new EventHpDisplay(
                    resultCurrentHp,
                    combatEvent.ResultShieldHp,
                    resultMaxHp,
                    preHealDisplay.TooltipDetail);
            }
        }

        var postHealSnapshot = FindPostHealHpSnapshot(combatEvent, preHealDisplay, history, events);
        if (postHealSnapshot is not null &&
            HpOrShieldIncreased(preHealDisplay, postHealSnapshot.CurrentHp, postHealSnapshot.ShieldHp))
        {
            var snapshotCurrentHp = Math.Min(postHealSnapshot.CurrentHp, postHealSnapshot.MaxHp);
            return new EventHpDisplay(
                snapshotCurrentHp,
                postHealSnapshot.ShieldHp,
                postHealSnapshot.MaxHp,
                preHealDisplay.TooltipDetail);
        }

        var restoredCurrentHp = (ulong)preHealDisplay.CurrentHp + combatEvent.Amount;
        var derivedCurrentHp = (uint)Math.Min((ulong)preHealDisplay.MaxHp, restoredCurrentHp);
        return derivedCurrentHp > preHealDisplay.CurrentHp
            ? new EventHpDisplay(
                derivedCurrentHp,
                preHealDisplay.ShieldHp,
                preHealDisplay.MaxHp,
                preHealDisplay.TooltipDetail)
            : null;
    }

    private static HpHistorySnapshot? FindPostHealHpSnapshot(
        CombatEventRecord combatEvent,
        EventHpDisplay preHealDisplay,
        IReadOnlyList<HpHistorySnapshot> history,
        IReadOnlyList<CombatEventRecord> events)
    {
        return history
            .Where(snapshot => snapshot.SeenAtUtc >= combatEvent.SeenAtUtc)
            .Where(snapshot => snapshot.SeenAtUtc - combatEvent.SeenAtUtc <= LeadUpStatusMergeWindow)
            .Where(snapshot => !HasInterveningHpChangingEvent(combatEvent, snapshot.SeenAtUtc, events))
            .Where(snapshot => PostHealSnapshotMatches(combatEvent, preHealDisplay, snapshot))
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .FirstOrDefault();
    }

    private static bool HasInterveningHpChangingEvent(
        CombatEventRecord combatEvent,
        DateTime snapshotSeenAtUtc,
        IReadOnlyList<CombatEventRecord> events)
    {
        return events.Any(candidate =>
            candidate.SeenAtUtc > combatEvent.SeenAtUtc &&
            candidate.SeenAtUtc < snapshotSeenAtUtc &&
            candidate.Amount > 0 &&
            candidate.Kind is DeathEventKind.Damage or DeathEventKind.Heal);
    }

    private static bool PostHealSnapshotMatches(
        CombatEventRecord combatEvent,
        EventHpDisplay preHealDisplay,
        HpHistorySnapshot snapshot)
    {
        if (CombatEventHasResultHp(combatEvent))
        {
            return HpSampleMatchesEffectResult(snapshot, combatEvent);
        }

        if (snapshot.MaxHp != preHealDisplay.MaxHp)
        {
            return false;
        }

        var restoredCurrentHp = (ulong)preHealDisplay.CurrentHp + combatEvent.Amount;
        var expectedCurrentHp = (uint)Math.Min((ulong)preHealDisplay.MaxHp, restoredCurrentHp);
        return snapshot.CurrentHp == expectedCurrentHp &&
            snapshot.ShieldHp >= preHealDisplay.ShieldHp;
    }

    private static bool HpOrShieldIncreased(EventHpDisplay previous, uint currentHp, uint shieldHp)
    {
        return currentHp > previous.CurrentHp || shieldHp > previous.ShieldHp;
    }

    private static bool HpOrShieldDecreased(EventHpDisplay previous, uint currentHp, uint shieldHp)
    {
        return currentHp < previous.CurrentHp || shieldHp < previous.ShieldHp;
    }

    private static EventHpDisplay GetEventHpDisplay(PartyDeathRecord death, CombatEventRecord combatEvent)
    {
        if (combatEvent.HpSource != CombatEventHpSource.NoPreHitSample &&
            combatEvent.MaxHp > 0 &&
            (combatEvent.CurrentHp > 0 || combatEvent.ShieldHp > 0))
        {
            var tooltip = combatEvent.HpSource switch
            {
                CombatEventHpSource.DirectCombatEventSnapshot => "HP captured directly with this combat event.",
                CombatEventHpSource.LatestPriorSample => "HP from the latest captured sample before this combat event.",
                _ => "HP captured with this combat event by the legacy capture path.",
            };
            return new EventHpDisplay(
                combatEvent.CurrentHp,
                combatEvent.ShieldHp,
                combatEvent.MaxHp,
                tooltip);
        }

        var priorSample = death.HpHistory
            .Where(snapshot => snapshot.SeenAtUtc <= combatEvent.SeenAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .LastOrDefault();
        if (priorSample is not null)
        {
            var deltaSeconds = Math.Max(0.0, (combatEvent.SeenAtUtc - priorSample.SeenAtUtc).TotalSeconds);
            return new EventHpDisplay(
                priorSample.CurrentHp,
                priorSample.ShieldHp,
                priorSample.MaxHp,
                $"HP fallback from latest captured sample {deltaSeconds:0.00}s before this event.");
        }

        return new EventHpDisplay(
            0,
            0,
            0,
            "No HP sample before this event was available.");
    }

    private void DrawEventSummaryCell(IReadOnlyList<CombatEventRecord> events, int maxEvents = 2)
    {
        if (events.Count == 0)
        {
            DrawCenteredText("-", DisabledColor);
            return;
        }

        ImGui.BeginGroup();
        var totalDamage = GetIncomingDamageAmount(events);
        if (totalDamage is not null)
        {
            DrawCenteredDamageSummary(events, FormatSignedDamageAmount(totalDamage.Value));
            DrawPostMitigationHitTooltip();
        }
        else
        {
            var shownEvents = events.Take(maxEvents).ToList();
            foreach (var combatEvent in shownEvents)
            {
                DrawCenteredOrWrappedText(FormatFatalEventLine(combatEvent), GetEventColor(combatEvent.Kind));
            }

            var hiddenCount = events.Count - shownEvents.Count;
            if (hiddenCount > 0)
            {
                DrawCenteredText($"+{hiddenCount} more", DisabledColor);
            }
        }
        ImGui.EndGroup();

        if (ImGui.IsItemHovered())
        {
            if (totalDamage is not null)
            {
                SetThemedTooltip("The value presented is the calculated hit post-mitigations.");
            }
        }
    }

    private static void DrawPostMitigationHitTooltip()
    {
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("The value presented is the calculated hit post-mitigations.");
        }
    }

    private string FormatFatalEventLine(CombatEventRecord combatEvent)
    {
        if (combatEvent.Kind == DeathEventKind.Status)
        {
            return $"{FormatKnownPlayerName(combatEvent.SourceName)}: {FormatActionNameForDisplay(combatEvent)} | Flags: {FormatEventFlags(combatEvent)}";
        }

        return $"{FormatKnownPlayerName(combatEvent.SourceName)}: {FormatActionNameForDisplay(combatEvent)} | Amount: {FormatSignedEventAmount(combatEvent)} | Flags: {FormatEventFlags(combatEvent)}";
    }

    private void DrawCombatEventLine(
        CombatEventRecord combatEvent,
        string? prefix = null,
        bool includeFlags = false)
    {
        if (!string.IsNullOrEmpty(prefix))
        {
            ImGui.TextUnformatted(prefix);
            ImGui.SameLine(0.0f, 0.0f);
        }

        DrawCombatEventLineCore(combatEvent, centered: false, includeFlags);
    }

    private void DrawCenteredCombatEventLine(CombatEventRecord combatEvent)
    {
        DrawCombatEventLineCore(combatEvent, centered: true, includeFlags: false);
    }

    private void DrawCenteredWrappedCombatEventLine(CombatEventRecord combatEvent)
    {
        var actionText = FormatActionNameForDisplay(combatEvent);
        var amountText = FormatSignedEventAmountSuffix(combatEvent);
        var text = $"{actionText}{amountText}";
        var iconId = GetDamageTypeIconId(combatEvent);
        var iconSize = GetInlineDamageTypeIconSize(iconId);
        var spacing = 4.0f;
        var fullWidth = (iconId == 0 ? 0.0f : iconSize + spacing) + ImGui.CalcTextSize(text).X;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var hovered = false;
        if (fullWidth <= availableWidth)
        {
            CenterNextItem(fullWidth);
            ImGui.BeginGroup();
            if (iconId != 0)
            {
                DrawGameIcon(iconId, iconSize, GetDamageTypeIconTooltip(combatEvent.DamageType));
                hovered |= ImGui.IsItemHovered();
                ImGui.SameLine(0.0f, spacing);
            }

            ImGui.PushStyleColor(ImGuiCol.Text, GetEventColor(combatEvent.Kind));
            ImGui.TextUnformatted(text);
            hovered |= ImGui.IsItemHovered();
            ImGui.PopStyleColor();
            ImGui.EndGroup();
            hovered |= ImGui.IsItemHovered();
        }
        else
        {
            ImGui.BeginGroup();
            DrawCenteredWrappedActionTextWithIcon(combatEvent, text);
            ImGui.EndGroup();
            hovered |= ImGui.IsItemHovered();
        }

        if (hovered)
        {
            SetThemedTooltip($"Source: {FormatKnownPlayerName(combatEvent.SourceName)}");
        }
    }

    private static void DrawCenteredWrappedActionTextWithIcon(CombatEventRecord combatEvent, string text)
    {
        var iconId = GetDamageTypeIconId(combatEvent);
        if (iconId == 0)
        {
            DrawCenteredOrWrappedText(text, GetEventColor(combatEvent.Kind));
            return;
        }

        var iconSize = GetInlineDamageTypeIconSize(iconId);
        var spacing = 4.0f;
        var availableTextWidth = MathF.Max(1.0f, ImGui.GetContentRegionAvail().X - iconSize - spacing);
        var lines = WrapTextForWidth(text, availableTextWidth);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (i == 0)
            {
                CenterNextItem(iconSize + spacing + ImGui.CalcTextSize(line).X);
                DrawDamageTypeIconInline(combatEvent, iconSize);
                ImGui.PushStyleColor(ImGuiCol.Text, GetEventColor(combatEvent.Kind));
                ImGui.TextUnformatted(line);
                ImGui.PopStyleColor();
                continue;
            }

            DrawCenteredText(line, GetEventColor(combatEvent.Kind));
        }
    }

    private void DrawCombatEventLineCore(
        CombatEventRecord combatEvent,
        bool centered,
        bool includeFlags)
    {
        var sourceText = $"{FormatKnownPlayerName(combatEvent.SourceName)}:";
        var actionText = FormatActionNameForDisplay(combatEvent);
        var amountText = FormatSignedEventAmountSuffix(combatEvent);
        var flagsText = includeFlags ? $" | Flags: {FormatEventFlags(combatEvent)}" : string.Empty;
        var textAfterIcon = $"{actionText}{amountText}{flagsText}";
        var iconId = GetDamageTypeIconId(combatEvent);
        var iconSize = GetInlineDamageTypeIconSize(iconId);
        var spacing = 4.0f;
        var groupWidth = ImGui.CalcTextSize(sourceText).X + spacing +
            (iconId == 0 ? 0.0f : iconSize + spacing) +
            ImGui.CalcTextSize(textAfterIcon).X;
        if (centered)
        {
            CenterNextItem(groupWidth);
        }

        ImGui.PushStyleColor(ImGuiCol.Text, GetEventColor(combatEvent.Kind));
        ImGui.TextUnformatted(sourceText);
        ImGui.SameLine(0.0f, spacing);
        ImGui.PopStyleColor();

        DrawDamageTypeIconInline(combatEvent, iconSize);

        ImGui.PushStyleColor(ImGuiCol.Text, GetEventColor(combatEvent.Kind));
        ImGui.TextUnformatted(textAfterIcon);
        ImGui.PopStyleColor();
        DrawLikelyAutoAttackTooltip(combatEvent);
    }

    private static void DrawCenteredDamageSummary(IReadOnlyList<CombatEventRecord> events, string text)
    {
        var iconId = GetSharedDamageTypeIconId(events);
        var iconSize = GetInlineDamageTypeIconSize(iconId);
        if (iconId == 0)
        {
            DrawCenteredOrWrappedText(text, DamageColor);
            return;
        }

        var spacing = 4.0f;
        var groupWidth = iconSize + spacing + ImGui.CalcTextSize(text).X;
        var tooltipDamageType = events
            .First(combatEvent => GetDamageTypeIconId(combatEvent) == iconId)
            .DamageType;
        CenterNextItem(groupWidth);
        DrawGameIcon(iconId, iconSize, GetDamageTypeIconTooltip(tooltipDamageType));
        ImGui.SameLine(0.0f, spacing);
        ImGui.PushStyleColor(ImGuiCol.Text, DamageColor);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static uint GetSharedDamageTypeIconId(IReadOnlyList<CombatEventRecord> events)
    {
        uint? iconId = null;
        foreach (var combatEvent in events.Where(combatEvent => combatEvent.Kind == DeathEventKind.Damage && combatEvent.Amount > 0))
        {
            var eventIconId = GetDamageTypeIconId(combatEvent);
            if (eventIconId == 0)
            {
                return 0;
            }

            if (iconId is null)
            {
                iconId = eventIconId;
                continue;
            }

            if (iconId.Value != eventIconId)
            {
                return 0;
            }
        }

        return iconId ?? 0;
    }

    private static void DrawFlagsBullet(CombatEventRecord combatEvent)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextUnformatted("Flags:");
        ImGui.SameLine(0.0f, 4.0f);
        DrawDamageTypeIconInline(combatEvent);
        ImGui.TextUnformatted(FormatEventFlags(combatEvent));
    }

    private void DrawLeadUpSummaryMitigationDebuffCell(LeadUpSummaryRow summary)
    {
        var statuses = GetLeadUpSummaryMitigationDebuffStatuses(summary, out var bossStatusKeys);
        DrawStatusSummaryCell(
            statuses,
            true,
            status => bossStatusKeys.Contains(GetStatusKey(status)) ||
                Plugin.ShouldShowPlayerStatusTimerForDisplay(status),
            true,
            bossSourceNamesByStatus: summary.SourceStatusNames);
    }

    private void DrawMitigationDebuffSummaryCell(LeadUpTimelineRow row, bool showTimers)
    {
        DrawCombinedMitigationDebuffCell(
            row.Statuses.Concat(row.NearbyHpStatuses),
            row.SourceStatuses,
            row.SourceStatusNames,
            showTimers,
            maxStatusesPerRow: 4);
    }

    private IReadOnlyList<StatusSnapshot> GetMergedPlayerStatusesForEvent(
        PartyDeathRecord death,
        CombatEventRecord combatEvent)
    {
        return plugin.GetRelevantPlayerStatusesForDisplay(
            combatEvent.Statuses.Concat(GetNearbyHpHistoryStatuses(death.HpHistory, combatEvent.SeenAtUtc)));
    }

    private void DrawCombinedMitigationDebuffCell(
        IEnumerable<StatusSnapshot> playerStatusSource,
        IEnumerable<StatusSnapshot> bossStatusSource,
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> bossSourceNamesByStatus,
        bool showTimers = true,
        int? maxStatusesPerRow = null)
    {
        var statuses = GetCombinedMitigationDebuffStatuses(playerStatusSource, bossStatusSource, out var bossStatusKeys);

        DrawStatusSummaryCell(
            statuses,
            true,
            showTimers
                ? status => bossStatusKeys.Contains(GetStatusKey(status)) ||
                    Plugin.ShouldShowPlayerStatusTimerForDisplay(status)
                : _ => false,
            true,
            maxStatusesPerRow,
            bossSourceNamesByStatus);
    }

    private IReadOnlyList<StatusSnapshot> GetLeadUpSummaryMitigationDebuffStatuses(
        LeadUpSummaryRow summary,
        out HashSet<StatusDisplayKey> bossStatusKeys)
    {
        return GetCombinedMitigationDebuffStatuses(
            GetLeadUpSummaryPlayerStatusSource(summary),
            GetLeadUpSummaryBossStatusSource(summary),
            out bossStatusKeys);
    }

    private static IEnumerable<StatusSnapshot> GetLeadUpSummaryPlayerStatusSource(LeadUpSummaryRow summary)
    {
        return summary.Row.LastSnapshot.Statuses
            .Concat(summary.Events.SelectMany(combatEvent => combatEvent.Statuses));
    }

    private static IEnumerable<StatusSnapshot> GetLeadUpSummaryBossStatusSource(LeadUpSummaryRow summary)
    {
        return summary.SourceStatuses;
    }

    private static IReadOnlyList<StatusSnapshot> GetEventSourceMitigationStatuses(
        PartyDeathRecord death,
        CombatEventRecord combatEvent)
    {
        return GetEventSourceMitigationStatuses(death, combatEvent, GetLeadUpEvents(death));
    }

    private static IReadOnlyList<StatusSnapshot> GetEventSourceMitigationStatuses(
        PartyDeathRecord death,
        CombatEventRecord combatEvent,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        return GetEventSourceMitigationStatusEntries(death, combatEvent, leadUpEvents)
            .Select(entry => entry.Status)
            .ToList();
    }

    private static IReadOnlyList<BossStatusDisplayEntry> GetEventSourceMitigationStatusEntries(
        PartyDeathRecord death,
        CombatEventRecord combatEvent,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        return SelectBossStatusDisplayEntries(combatEvent.SourceStatuses
            .Select(status => new BossStatusDisplayEntry(status, combatEvent.SourceName))
            .Concat(GetActiveSourceMitigationStatusEntries(death, combatEvent.SeenAtUtc, combatEvent.SourceEntityId, leadUpEvents)));
    }

    private static IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> GetEventSourceMitigationStatusSourceNames(
        PartyDeathRecord death,
        CombatEventRecord combatEvent,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        return BuildBossStatusSourceNames(GetEventSourceMitigationStatusEntries(death, combatEvent, leadUpEvents));
    }

    private static IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> GetEventSourceMitigationStatusSourceNames(
        PartyDeathRecord death,
        IEnumerable<CombatEventRecord> combatEvents,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        return MergeBossStatusSourceNames(combatEvents
            .Select(combatEvent => GetEventSourceMitigationStatusSourceNames(death, combatEvent, leadUpEvents)));
    }

    private static IReadOnlyList<BossStatusDisplayEntry> SelectBossStatusDisplayEntries(IEnumerable<BossStatusDisplayEntry> entries)
    {
        var entryList = entries.ToList();
        if (entryList.Count == 0)
        {
            return [];
        }

        var bossStatusKeys = Plugin
            .GetBossMitigationStatusesForDisplay(entryList.Select(entry => entry.Status))
            .Select(GetStatusKey)
            .ToHashSet();
        return entryList
            .Where(entry => bossStatusKeys.Contains(GetStatusKey(entry.Status)))
            .GroupBy(entry => (entry.SourceName, StatusKey: GetStatusKey(entry.Status)))
            .Select(group => group
                .OrderBy(entry => entry.Status.RemainingTime <= 0.0f ? float.MaxValue : entry.Status.RemainingTime)
                .ThenBy(entry => entry.Status.StackCount)
                .First())
            .ToList();
    }

    private static IReadOnlyList<StatusSnapshot> GetActiveSourceMitigationStatuses(
        PartyDeathRecord death,
        DateTime seenAtUtc,
        uint? sourceEntityId = null)
    {
        return GetActiveSourceMitigationStatuses(death, seenAtUtc, sourceEntityId, GetLeadUpEvents(death));
    }

    private static IReadOnlyList<StatusSnapshot> GetActiveSourceMitigationStatuses(
        PartyDeathRecord death,
        DateTime seenAtUtc,
        uint? sourceEntityId,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        return GetActiveSourceMitigationStatusEntries(death, seenAtUtc, sourceEntityId, leadUpEvents)
            .Select(entry => entry.Status)
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();
    }

    private static IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> GetActiveSourceMitigationStatusSourceNames(
        PartyDeathRecord death,
        DateTime seenAtUtc,
        uint? sourceEntityId,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        return BuildBossStatusSourceNames(GetActiveSourceMitigationStatusEntries(death, seenAtUtc, sourceEntityId, leadUpEvents));
    }

    private static IReadOnlyList<BossStatusDisplayEntry> GetActiveSourceMitigationStatusEntries(
        PartyDeathRecord death,
        DateTime seenAtUtc,
        uint? sourceEntityId,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        var sourceMitigationHistory = GetSourceMitigationHistoryForDisplay(death, leadUpEvents);
        if (sourceMitigationHistory.Count == 0)
        {
            return [];
        }

        var activeEntries = sourceMitigationHistory
            .Where(snapshot => snapshot.SeenAtUtc <= seenAtUtc)
            .Where(snapshot => sourceEntityId is null || snapshot.SourceEntityId == sourceEntityId.Value)
            .SelectMany(snapshot => GetActiveSourceMitigationStatuses(snapshot, seenAtUtc))
            .GroupBy(entry => (entry.SourceEntityId, entry.Status.Id, entry.Status.IconId, entry.Status.SourceId))
            .Select(group => group
                .OrderByDescending(entry => entry.SeenAtUtc)
                .ThenBy(entry => entry.Status.RemainingTime)
                .First())
            .ToList();
        return activeEntries
            .Select(entry => new BossStatusDisplayEntry(entry.Status, entry.SourceName))
            .ToList();
    }

    private static IReadOnlyList<SourceMitigationSnapshot> GetSourceMitigationHistoryForDisplay(PartyDeathRecord death)
    {
        return GetSourceMitigationHistoryForDisplay(death, GetLeadUpEvents(death));
    }

    private static IReadOnlyList<SourceMitigationSnapshot> GetSourceMitigationHistoryForDisplay(
        PartyDeathRecord death,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        var sourceMitigationHistory = death.SourceMitigationHistory?.ToList() ?? [];
        sourceMitigationHistory.AddRange(leadUpEvents
            .Where(combatEvent => combatEvent.SourceEntityId != 0)
            .Select(combatEvent => new SourceMitigationSnapshot(
                combatEvent.SeenAtUtc,
                combatEvent.PullElapsedSeconds,
                combatEvent.SourceEntityId,
                combatEvent.SourceName,
                Plugin.GetBossMitigationStatusesForDisplay(combatEvent.SourceStatuses)
                    .Where(status => status.RemainingTime > 0.0f)
                    .ToList()))
            .Where(snapshot => snapshot.Statuses.Count > 0));

        return sourceMitigationHistory
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.SourceEntityId)
            .ToList();
    }

    private static IEnumerable<(uint SourceEntityId, string SourceName, DateTime SeenAtUtc, StatusSnapshot Status)> GetActiveSourceMitigationStatuses(
        SourceMitigationSnapshot snapshot,
        DateTime seenAtUtc)
    {
        var elapsedSeconds = (float)(seenAtUtc - snapshot.SeenAtUtc).TotalSeconds;
        foreach (var status in snapshot.Statuses)
        {
            var remainingTime = status.RemainingTime - elapsedSeconds;
            if (remainingTime <= 0.0f)
            {
                continue;
            }

            yield return (
                snapshot.SourceEntityId,
                snapshot.SourceName,
                snapshot.SeenAtUtc,
                status with { RemainingTime = remainingTime });
        }
    }

    private static IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> BuildBossStatusSourceNames(
        IEnumerable<BossStatusDisplayEntry> entries)
    {
        return entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.SourceName))
            .GroupBy(entry => GetStatusKey(entry.Status))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(entry => entry.SourceName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(sourceName => sourceName, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    private static IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> MergeBossStatusSourceNames(
        IEnumerable<IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>>> sourceMaps)
    {
        var merged = new Dictionary<StatusDisplayKey, List<string>>();
        foreach (var sourceMap in sourceMaps)
        {
            foreach (var (key, sourceNames) in sourceMap)
            {
                if (!merged.TryGetValue(key, out var mergedNames))
                {
                    mergedNames = [];
                    merged[key] = mergedNames;
                }

                foreach (var sourceName in sourceNames)
                {
                    if (!string.IsNullOrWhiteSpace(sourceName) &&
                        !mergedNames.Contains(sourceName, StringComparer.OrdinalIgnoreCase))
                    {
                        mergedNames.Add(sourceName);
                    }
                }
            }
        }

        return merged.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value
                .OrderBy(sourceName => sourceName, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static bool BossStatusSourceNamesMatch(
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> first,
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        foreach (var (key, firstNames) in first)
        {
            if (!second.TryGetValue(key, out var secondNames) ||
                firstNames.Count != secondNames.Count ||
                !firstNames.SequenceEqual(secondNames, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<StatusSnapshot> GetSelectedMitigationDebuffStatuses(PartyDeathRecord death)
    {
        var selection = DeathDisplaySelector.Select(death);
        return GetSelectedMitigationDebuffStatuses(death, selection, GetLeadUpEvents(death));
    }

    private IReadOnlyList<StatusSnapshot> GetSelectedMitigationDebuffStatuses(
        PartyDeathRecord death,
        DeathDisplaySelection selection,
        IReadOnlyList<CombatEventRecord> leadUpEvents)
    {
        return GetCombinedMitigationDebuffStatuses(
            GetSelectedPlayerStatuses(death),
            selection.Events.SelectMany(combatEvent => GetEventSourceMitigationStatuses(death, combatEvent, leadUpEvents)),
            out _);
    }

    private IReadOnlyList<StatusSnapshot> GetCombinedMitigationDebuffStatuses(
        IEnumerable<StatusSnapshot> playerStatusSource,
        IEnumerable<StatusSnapshot> bossStatusSource,
        out HashSet<StatusDisplayKey> bossStatusKeys)
    {
        var playerStatuses = plugin
            .GetRelevantPlayerStatusesForDisplay(playerStatusSource)
            .ToList();
        var bossStatuses = Plugin
            .GetBossMitigationStatusesForDisplay(bossStatusSource)
            .ToList();

        bossStatusKeys = bossStatuses
            .Select(GetStatusKey)
            .ToHashSet();
        return playerStatuses
            .Concat(bossStatuses)
            .GroupBy(GetStatusKey)
            .Select(group => group
                .OrderBy(status => status.RemainingTime <= 0.0f ? float.MaxValue : status.RemainingTime)
                .ThenBy(status => status.StackCount)
                .First())
            .ToList();
    }

    private static StatusDisplayKey GetStatusKey(StatusSnapshot status)
    {
        return new StatusDisplayKey(status.Id, status.IconId, status.SourceId, status.Name);
    }

    private void DrawStatusSummaryCell(
        IReadOnlyList<StatusSnapshot> statuses,
        bool showTenthsOverTenSeconds = false,
        Func<StatusSnapshot, bool>? shouldShowTimer = null,
        bool centerContent = false,
        int? maxStatusesPerRow = null,
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>>? bossSourceNamesByStatus = null)
    {
        if (statuses.Count == 0)
        {
            if (centerContent)
            {
                DrawCenteredText("-", DisabledColor);
            }
            else
            {
                ImGui.TextDisabled("-");
            }

            return;
        }

        var orderedStatuses = statuses
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();
        var availableWidth = MathF.Max(
            ImGui.GetContentRegionAvail().X,
            GetStatusIconStackWidth(
                orderedStatuses[0],
                configuration.StatusIconSize,
                showTenthsOverTenSeconds,
                ShouldShowStatusTimer(orderedStatuses[0], shouldShowTimer)));
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var rows = new List<List<(StatusSnapshot Status, float StackWidth, bool ShowTimer)>>();
        var rowWidths = new List<float>();
        var currentRow = new List<(StatusSnapshot Status, float StackWidth, bool ShowTimer)>();
        var currentRowWidth = 0.0f;

        foreach (var status in orderedStatuses)
        {
            var showTimer = ShouldShowStatusTimer(status, shouldShowTimer);
            var stackWidth = GetStatusIconStackWidth(status, configuration.StatusIconSize, showTenthsOverTenSeconds, showTimer);
            var reachedRowLimit = maxStatusesPerRow.HasValue && currentRow.Count >= maxStatusesPerRow.Value;
            if (currentRow.Count > 0 && (reachedRowLimit || currentRowWidth + spacing + stackWidth > availableWidth))
            {
                rows.Add(currentRow);
                rowWidths.Add(currentRowWidth);
                currentRow = [];
                currentRowWidth = 0.0f;
            }

            if (currentRow.Count > 0)
            {
                currentRowWidth += spacing;
            }

            currentRow.Add((status, stackWidth, showTimer));
            currentRowWidth += stackWidth;
        }

        if (currentRow.Count > 0)
        {
            rows.Add(currentRow);
            rowWidths.Add(currentRowWidth);
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (centerContent)
            {
                CenterNextItem(rowWidths[rowIndex]);
            }

            var row = rows[rowIndex];
            for (var statusIndex = 0; statusIndex < row.Count; statusIndex++)
            {
                if (statusIndex > 0)
                {
                    ImGui.SameLine();
                }

                var status = row[statusIndex].Status;
                IReadOnlyList<string>? bossSourceNames = null;
                bossSourceNamesByStatus?.TryGetValue(GetStatusKey(status), out bossSourceNames);
                DrawStatusIconStack(
                    status,
                    configuration.StatusIconSize,
                    showTenthsOverTenSeconds,
                    row[statusIndex].ShowTimer,
                    bossSourceNames);
            }
        }
    }

    private static bool ShouldShowStatusTimer(StatusSnapshot status, Func<StatusSnapshot, bool>? shouldShowTimer)
    {
        return shouldShowTimer?.Invoke(status) ?? true;
    }

    private static float GetStatusIconStackWidth(
        StatusSnapshot status,
        float configuredIconSize,
        bool showTenthsOverTenSeconds = false,
        bool showTimer = true)
    {
        var iconSize = Math.Clamp(configuredIconSize, 12.0f, 32.0f);
        var timerText = FormatStatusDuration(status, showTenthsOverTenSeconds, showTimer);
        var timerWidth = string.IsNullOrEmpty(timerText) ? 0.0f : ImGui.CalcTextSize(timerText).X;
        return MathF.Max(iconSize, timerWidth);
    }

    private static void DrawStatusIconStack(
        StatusSnapshot status,
        float configuredIconSize,
        bool showTenthsOverTenSeconds = false,
        bool showTimer = true,
        IReadOnlyList<string>? bossSourceNames = null)
    {
        var iconSize = Math.Clamp(configuredIconSize, 12.0f, 32.0f);
        var timerText = FormatStatusDuration(status, showTenthsOverTenSeconds, showTimer);
        var timerWidth = string.IsNullOrEmpty(timerText) ? 0.0f : ImGui.CalcTextSize(timerText).X;
        var groupWidth = GetStatusIconStackWidth(status, configuredIconSize, showTenthsOverTenSeconds, showTimer);
        var startX = ImGui.GetCursorPosX();
        var hasBossSourceNames = bossSourceNames is { Count: > 0 };
        var tooltip = FormatStatusSourceTooltip(
            FormatStatusCompact(status, showTenthsOverTenSeconds, showTimer),
            bossSourceNames);
        var hovered = false;

        ImGui.BeginGroup();
        ImGui.SetCursorPosX(startX + MathF.Max(0.0f, (groupWidth - iconSize) * 0.5f));
        var iconStart = ImGui.GetCursorScreenPos();
        if (status.IconId == 0)
        {
            ImGui.Dummy(new Vector2(iconSize));
            hovered |= ImGui.IsItemHovered();
        }
        else
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(status.IconId));
            var wrap = texture.GetWrapOrDefault();
            if (wrap is null)
            {
                ImGui.Dummy(new Vector2(iconSize));
                hovered |= ImGui.IsItemHovered();
            }
            else
            {
                ImGui.Image(wrap.Handle, new Vector2(iconSize));
                hovered |= ImGui.IsItemHovered();
            }
        }

        if (hasBossSourceNames)
        {
            ImGui.GetWindowDrawList().AddRect(
                iconStart - new Vector2(1.0f),
                iconStart + new Vector2(iconSize) + new Vector2(1.0f),
                ImGui.GetColorU32(ModernAccentColor),
                3.0f,
                ImDrawFlags.None,
                1.8f);
        }

        if (!string.IsNullOrEmpty(timerText))
        {
            ImGui.SetCursorPosX(startX + MathF.Max(0.0f, (groupWidth - timerWidth) * 0.5f));
            ImGui.TextDisabled(timerText);
            hovered |= ImGui.IsItemHovered();
        }

        ImGui.EndGroup();
        hovered |= ImGui.IsItemHovered();
        if (hovered)
        {
            SetThemedTooltip(tooltip);
        }
    }

    private static string FormatBossStatusSourceTooltip(IReadOnlyList<string> sourceNames)
    {
        return string.Join(", ", sourceNames);
    }

    private static IReadOnlyList<string>? GetBossSourceNamesForStatus(
        StatusSnapshot status,
        IReadOnlyDictionary<StatusDisplayKey, IReadOnlyList<string>>? bossSourceNamesByStatus)
    {
        return bossSourceNamesByStatus is not null &&
            bossSourceNamesByStatus.TryGetValue(GetStatusKey(status), out var sourceNames) &&
            sourceNames.Count > 0
            ? sourceNames
            : null;
    }

    private static string FormatStatusSourceTooltip(string statusTooltip, IReadOnlyList<string>? bossSourceNames)
    {
        return bossSourceNames is { Count: > 0 }
            ? $"{statusTooltip}\n{FormatBossStatusSourceTooltip(bossSourceNames)}"
            : statusTooltip;
    }

    private void DrawSettingsTab()
    {
        DrawSettingsGroupHeader("General", "Window behavior and saved review defaults.", first: true);
        DrawGeneralSettingsGroup();

        DrawSettingsGroupHeader("Death Popup", "Quick local access after your own death.");
        DrawDeathPopupSettingsGroup();

        DrawSettingsGroupHeader("Privacy & Chat", "Local links, redaction, and shared chat cleanup.");
        DrawPrivacyChatSettingsGroup();

        DrawSettingsGroupHeader("Capture", "Who gets recorded and how timestamps display.");
        DrawCaptureSettingsGroup();

        DrawSettingsGroupHeader("Appearance", "Window, icon, and theme styling.");
        DrawAppearanceSettingsGroup();

        DrawReviewPaneBottomPadding();
    }

    private static void DrawSettingsGroupHeader(string title, string subtitle, bool first = false)
    {
        if (!first)
        {
            ImGui.Spacing();
            DrawSubtleSeparator();
        }

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var titleWidth = ImGui.CalcTextSize(title).X;
        var subtitleWidth = ImGui.CalcTextSize(subtitle).X;
        var canFitInline = availableWidth - titleWidth - ImGui.GetStyle().ItemInnerSpacing.X >= subtitleWidth;

        ImGui.TextUnformatted(title);
        if (canFitInline)
        {
            ImGui.SameLine();
        }

        ImGui.TextDisabled(subtitle);
    }

    private void DrawGeneralSettingsGroup()
    {
        var showWindow = configuration.ShowWindow;
        if (DrawThemedCheckbox("Show Better Deaths window on plugin load", ref showWindow))
        {
            plugin.SetShowWindowByDefault(showWindow);
        }

        DrawInlineDebugTabButton();

        var showScrollbars = configuration.ShowScrollbars;
        if (DrawThemedCheckbox("Enable scrollbars", ref showScrollbars))
        {
            plugin.SetShowScrollbars(showScrollbars);
        }

        DrawSettingsTooltip("Mouse-wheel scrolling still works when this is off. This option is for users whose mouse scrolling is broken or malfunctioning.");

        var maxPulls = pendingMaxRecordedPulls ?? configuration.MaxRecordedPulls;
        if (ImGui.SliderInt("Recorded pulls kept", ref maxPulls, 1, 100))
        {
            pendingMaxRecordedPulls = maxPulls;
        }

        if (ImGui.IsItemDeactivatedAfterEdit() && pendingMaxRecordedPulls is { } committedMaxPulls)
        {
            maxPulls = committedMaxPulls;
            pendingMaxRecordedPulls = null;
            plugin.SetMaxRecordedPulls(maxPulls);
        }

        DrawSettingsTooltip("Controls how many completed pull recaps are saved locally and shown in Recorded pulls. Older pulls are removed after this limit.");
    }

    private void DrawDeathPopupSettingsGroup()
    {
        var showDeathRecapPopup = configuration.ShowDeathRecapPopup;
        var testButtonWidth = GetThemedActionButtonWidth("Test");
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var toggleWidth = ImGui.CalcTextSize("Show recap popup when you die").X +
            ImGui.GetFrameHeight() +
            (ImGui.GetStyle().ItemInnerSpacing.X * 2.0f);
        var canFitTestInline = availableWidth - toggleWidth - ImGui.GetStyle().ItemSpacing.X >= testButtonWidth;

        if (DrawThemedCheckbox("Show recap popup when you die", ref showDeathRecapPopup))
        {
            plugin.SetShowDeathRecapPopup(showDeathRecapPopup);
        }

        var popupSettingHovered = ImGui.IsItemHovered();
        if (canFitTestInline)
        {
            ImGui.SameLine();
        }

        if (DrawThemedActionButton("Test", "TestDeathRecapPopup", testButtonWidth))
        {
            plugin.SetDeathRecapPopupTestActive(true);
        }

        var testHovered = ImGui.IsItemHovered();
        if (popupSettingHovered)
        {
            SetThemedTooltip("Shows a small local-only button for 30 seconds after your own death. The button opens that exact death in Review.");
        }
        else if (testHovered)
        {
            SetThemedTooltip("Shows the same movable popup button for 30 seconds. The Test button does nothing and turns off when it disappears.");
        }
    }

    private void DrawPrivacyChatSettingsGroup()
    {
        var postDeathRecapLinksOnDeath = configuration.PostDeathRecapLinksOnDeath;
        var postDeathRecapLinksChanged = DrawThemedCheckbox("##PostDeathRecapLinksOnDeath", ref postDeathRecapLinksOnDeath);
        var postDeathRecapLinksHovered = ImGui.IsItemHovered();
        var postDeathRecapLinksLabelClicked = false;
        ImGui.SameLine();
        ImGui.TextUnformatted("Post");
        postDeathRecapLinksHovered |= ImGui.IsItemHovered();
        postDeathRecapLinksLabelClicked |= ImGui.IsItemClicked();
        ImGui.SameLine();
        ImGui.TextColored(LeadUpGoldColor, "[ Death Link ]");
        postDeathRecapLinksHovered |= ImGui.IsItemHovered();
        postDeathRecapLinksLabelClicked |= ImGui.IsItemClicked();
        ImGui.SameLine();
        ImGui.TextUnformatted("on captured death(s).");
        postDeathRecapLinksHovered |= ImGui.IsItemHovered();
        postDeathRecapLinksLabelClicked |= ImGui.IsItemClicked();
        if (postDeathRecapLinksLabelClicked)
        {
            postDeathRecapLinksOnDeath = !postDeathRecapLinksOnDeath;
            postDeathRecapLinksChanged = true;
        }

        if (postDeathRecapLinksChanged)
        {
            plugin.SetPostDeathRecapLinksOnDeath(postDeathRecapLinksOnDeath);
        }

        if (postDeathRecapLinksHovered)
        {
            DrawPostDeathRecapLinksTooltip();
        }

        DrawDeathRecapLinkChannelSetting();

        var redactPlayerNames = configuration.RedactPlayerNames;
        if (DrawThemedCheckbox("Name Redaction", ref redactPlayerNames))
        {
            plugin.SetRedactPlayerNames(redactPlayerNames);
        }

        DrawSettingsTooltip("A way to show information to others without doxxing your party");

        var removeChatBranding = configuration.RemoveChatBranding;
        if (DrawThemedCheckbox("Remove Better Deaths branding from chat posts", ref removeChatBranding))
        {
            plugin.SetRemoveChatBranding(removeChatBranding);
        }

        DrawSettingsTooltip(";( sadge, you hate me..");
    }

    private void DrawCaptureSettingsGroup()
    {
        if (ImGui.BeginTable("##CaptureClockSettings", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Capture", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("Clock", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var captureParty = configuration.CapturePartyDeaths;
            if (DrawThemedCheckbox("Capture party", ref captureParty))
            {
                plugin.SetCapturePartyDeaths(captureParty);
            }

            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip("Includes your own character.");
            }

            var captureOthers = configuration.CaptureOtherDeaths;
            if (DrawThemedCheckbox("Capture others", ref captureOthers))
            {
                plugin.SetCaptureOtherDeaths(captureOthers);
            }

            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip("Tracks non-party player characters visible to your client.");
            }

            ImGui.TableNextColumn();
            DrawClockDisplaySetting();
            ImGui.EndTable();
        }
    }

    private void DrawAppearanceSettingsGroup()
    {
        var mainWindowBackgroundOpacity = GetMainWindowBackgroundOpacity();
        if (ImGui.SliderFloat(
            "Better Deaths window opacity",
            ref mainWindowBackgroundOpacity,
            Plugin.MainWindowMinBackgroundOpacity,
            Plugin.MainWindowMaxBackgroundOpacity,
            "%.2f"))
        {
            plugin.SetMainWindowBackgroundOpacity(mainWindowBackgroundOpacity);
        }

        DrawSettingsTooltip("Controls the main Better Deaths window background opacity. Lower values make it easier to see combat behind the review window.");

        var iconSize = MathF.Max(configuration.ActionIconSize, configuration.StatusIconSize);
        if (ImGui.SliderFloat("Icon size", ref iconSize, 12.0f, 48.0f, "%.0f px"))
        {
            plugin.SetIconSize(iconSize);
        }

        DrawSettingsTooltip("Controls non-widget action and status icons in death timelines, details, examples, and Better Deaths lead-up tables. Use the Widget tab for Current Pull widget icons.");

        DrawPullGroupColorSetting();
        DrawThemeSetting();
    }

    private void DrawPullGroupColorSetting()
    {
        var useCustomPullGroupColors = configuration.UseCustomPullGroupColors;
        if (DrawThemedCheckbox("Customize pull group colors", ref useCustomPullGroupColors))
        {
            configuration.UseCustomPullGroupColors = useCustomPullGroupColors;
            if (useCustomPullGroupColors)
            {
                EnsureCustomPullGroupColorsInitialized();
            }

            plugin.SaveConfiguration();
        }

        DrawSettingsTooltip("Controls the same-duty-instance color markers shown in the Pulls list.");

        if (!useCustomPullGroupColors)
        {
            return;
        }

        if (EnsureCustomPullGroupColorsInitialized())
        {
            plugin.SaveConfiguration();
        }

        var resetLabel = "Reset pull colors";
        var resetWidth = GetThemedActionButtonWidth(resetLabel);
        if (DrawThemedActionButton(resetLabel, "ResetPullGroupColors", MathF.Min(resetWidth, ImGui.GetContentRegionAvail().X)))
        {
            ResetCustomPullGroupColorsFromTheme();
        }

        ImGui.TextColored(ModernMutedTextColor, "Pull group palette");
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var columnCount = availableWidth >= 560.0f ? 2 : 1;
        if (!ImGui.BeginTable("##PullGroupColorPalette", columnCount, ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        for (var i = 0; i < configuration.PullGroupColors.Count; i++)
        {
            ImGui.TableNextColumn();
            DrawPullGroupColorEdit(i);
        }

        ImGui.EndTable();
        ImGui.Spacing();
    }

    private void DrawPullGroupColorEdit(int index)
    {
        var color = configuration.PullGroupColors[index];
        var vector = ToVector4(color);
        ImGui.SetNextItemWidth(MathF.Min(150.0f, ImGui.GetContentRegionAvail().X * 0.55f));
        if (ImGui.ColorEdit4($"##PullGroupColor{index}", ref vector, ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            SetThemeColorValue(color, vector);
        }

        var hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        ImGui.TextWrapped($"Group {index + 1}");
        hovered |= ImGui.IsItemHovered();
        if (hovered)
        {
            SetThemedTooltip("Used for pulls captured in the same duty instance.");
        }
    }

    private bool EnsureCustomPullGroupColorsInitialized()
    {
        var changed = false;
        configuration.PullGroupColors ??= [];
        while (configuration.PullGroupColors.Count < Plugin.PullGroupColorPaletteSize)
        {
            configuration.PullGroupColors.Add(ToThemeColorValue(GetDefaultPullGroupColor(configuration.PullGroupColors.Count)));
            changed = true;
        }

        for (var i = 0; i < configuration.PullGroupColors.Count; i++)
        {
            if (configuration.PullGroupColors[i] is null)
            {
                configuration.PullGroupColors[i] = ToThemeColorValue(GetDefaultPullGroupColor(i));
                changed = true;
            }
        }

        while (configuration.PullGroupColors.Count > Plugin.PullGroupColorPaletteSize)
        {
            configuration.PullGroupColors.RemoveAt(configuration.PullGroupColors.Count - 1);
            changed = true;
        }

        return changed;
    }

    private void ResetCustomPullGroupColorsFromTheme()
    {
        configuration.PullGroupColors = Enumerable
            .Range(0, Plugin.PullGroupColorPaletteSize)
            .Select(index => ToThemeColorValue(GetDefaultPullGroupColor(index)))
            .ToList();
        plugin.SaveConfiguration();
    }

    private void DrawThemeSetting()
    {
        ImGui.TextColored(LeadUpGoldColor, "Theme");
        if (HasUnseenNewThemeBadges())
        {
            ImGui.SameLine();
            DrawInlineNewBadge();
        }

        ImGui.Spacing();

        var darkThemes = BetterDeathsThemeCatalog.All
            .Where(theme => !IsLightPanelTheme(theme))
            .ToList();
        var lightThemes = BetterDeathsThemeCatalog.All
            .Where(IsLightPanelTheme)
            .ToList();

        DrawThemeGroup("Dark", "Dark", darkThemes);
        ImGui.Spacing();
        DrawSubtleSeparator();
        ImGui.Spacing();
        DrawThemeGroup("Light", "Light", lightThemes);
        ImGui.Spacing();
        DrawSubtleSeparator();
        ImGui.Spacing();
        DrawCustomThemeBuilder();
    }

    private void DrawCustomThemeBuilder()
    {
        ImGui.TextColored(LeadUpGoldColor, "Custom theme");
        var customTheme = configuration.CustomTheme ??= new CustomThemeConfiguration();
        var enabled = customTheme.Enabled;
        if (DrawThemedCheckbox("Use custom theme", ref enabled))
        {
            if (enabled && !customTheme.Initialized)
            {
                configuration.CustomTheme = BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(BetterDeathsThemeCatalog.GetTheme(configuration.Theme));
                customTheme = configuration.CustomTheme;
            }

            customTheme.Enabled = enabled;
            activeTheme = BetterDeathsThemeCatalog.GetTheme(configuration);
            plugin.SaveConfiguration();
        }

        var checkboxMax = ImGui.GetItemRectMax();
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Starts from the selected theme, then applies the custom colors below.");
        }

        var resetLabel = "Reset from selected theme";
        var resetWidth = GetThemedActionButtonWidth(resetLabel);
        var contentRight = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        if (checkboxMax.X + ImGui.GetStyle().ItemSpacing.X + resetWidth <= contentRight)
        {
            ImGui.SameLine();
        }
        else
        {
            ImGui.Spacing();
        }

        ImGui.BeginDisabled(!customTheme.Initialized);
        if (DrawThemedActionButton(resetLabel, "ResetCustomThemeFromSelectedTheme", MathF.Min(resetWidth, ImGui.GetContentRegionAvail().X)))
        {
            configuration.CustomTheme = BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(BetterDeathsThemeCatalog.GetTheme(configuration.Theme));
            configuration.CustomTheme.Enabled = enabled;
            activeTheme = BetterDeathsThemeCatalog.GetTheme(configuration);
            plugin.SaveConfiguration();
        }

        ImGui.EndDisabled();

        if (!enabled)
        {
            DrawMutedWrappedText("Enable this to edit colors without replacing the built-in theme list.");
            return;
        }

        if (!customTheme.Initialized)
        {
            configuration.CustomTheme = BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(BetterDeathsThemeCatalog.GetTheme(configuration.Theme));
            customTheme = configuration.CustomTheme;
            activeTheme = BetterDeathsThemeCatalog.GetTheme(configuration);
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        DrawCustomThemePreview(BetterDeathsThemeCatalog.GetTheme(configuration));
        ImGui.Spacing();

        DrawCustomThemeColorGroup(
            "Text",
            ("Regular text", customTheme.RegularText, "Normal readable text used through the window."),
            ("Muted / label text", customTheme.MutedText, "Subtext and quiet labels."),
            ("Gold / section text", customTheme.GoldText, "Section labels, highlights, shields, and gold callouts."),
            ("Disabled text", customTheme.DisabledText, "Disabled controls and unavailable text."));
        DrawCustomThemeColorGroup(
            "Combat",
            ("Damage", customTheme.DamageText, "Damage numbers and harmful event text."),
            ("Overkill", customTheme.OverkillText, "Overkill and death-severity text."),
            ("Healing", customTheme.HealText, "Healing text and positive result text."),
            ("Warning", customTheme.WarningText, "Warnings and neutral caution text."),
            ("Spam warning", customTheme.SpamWarningText, "Spam or repeated-event warning text."));
        DrawCustomThemeColorGroup(
            "Surfaces",
            ("Window background", customTheme.WindowBackground, "Main Better Deaths window background color."),
            ("Content background", customTheme.ContentBackground, "Main content area background."),
            ("Raised background", customTheme.RaisedBackground, "Rows, frames, and raised surfaces."),
            ("Frame background", customTheme.FrameBackground, "Inputs, dropdowns, sliders, and checkbox backgrounds."),
            ("Frame hover", customTheme.FrameHoverBackground, "Hovered inputs, dropdowns, sliders, and checkbox backgrounds."),
            ("Popup background", customTheme.PopupBackground, "Dropdown and tooltip background color."),
            ("Border", customTheme.Border, "Main borders and table accents."),
            ("Divider", customTheme.Divider, "Separators between sections."));
        DrawCustomThemeColorGroup(
            "Accents",
            ("Accent", customTheme.Accent, "Primary accent color."),
            ("Soft accent", customTheme.AccentSoft, "Secondary accent color used for softer states."),
            ("Header", customTheme.HeaderBackground, "Table headers and header-style controls."),
            ("Header hover", customTheme.HeaderHoverBackground, "Hovered table headers and header-style controls."),
            ("Header active", customTheme.HeaderActiveBackground, "Active table headers and header-style controls."),
            ("Table row alt", customTheme.TableRowAlt, "Alternating table row background."),
            ("Focused row", customTheme.FocusedRow, "Focused-mode row background."),
            ("Focused row accent", customTheme.FocusedRowAccent, "Focused-mode row side accent."),
            ("Selected row", customTheme.TimelineSelectedRow, "Selected timeline row background."),
            ("Pressed row", customTheme.TimelinePressedRow, "Pressed timeline row feedback."));
        DrawCustomThemeColorGroup(
            "Buttons",
            ("Button color", customTheme.ButtonColor, "Normal tab and segmented button fill."),
            ("Button hover", customTheme.ButtonHoverColor, "Hovered tab and segmented button fill."),
            ("Selected button", customTheme.SelectedButtonColor, "Selected tabs and primary action buttons."),
            ("Selected hover", customTheme.SelectedButtonHoverColor, "Hovered selected tabs and primary action buttons."),
            ("Button active", customTheme.ButtonActiveColor, "Pressed button feedback."),
            ("Button text", customTheme.ButtonText, "Normal button label text."),
            ("Selected text", customTheme.SelectedButtonText, "Selected button label text."));
        DrawCustomThemeColorGroup(
            "Controls",
            ("Checkbox background", customTheme.CheckboxBackground, "Unchecked checkbox background."),
            ("Checkbox hover", customTheme.CheckboxHoverBackground, "Hovered checkbox background."),
            ("Checkbox active", customTheme.CheckboxActiveBackground, "Checked checkbox background."),
            ("Checkbox checkmark", customTheme.CheckboxCheckMark, "Checkbox checkmark color."),
            ("Checkbox border", customTheme.CheckboxBorder, "Checkbox border color."),
            ("Slider grab", customTheme.SliderGrab, "Slider handle color."),
            ("Slider active", customTheme.SliderGrabActive, "Active slider handle color."),
            ("Scrollbar background", customTheme.ScrollbarBackground, "Scrollbar track color."),
            ("Scrollbar grab", customTheme.ScrollbarGrab, "Scrollbar handle color."),
            ("Scrollbar hover", customTheme.ScrollbarGrabHover, "Hovered scrollbar handle color."),
            ("Scrollbar active", customTheme.ScrollbarGrabActive, "Active scrollbar handle color."));
        DrawCustomThemeColorGroup(
            "Bars",
            ("HP bar", customTheme.HpBar, "Health bar fill."),
            ("Shield bar", customTheme.ShieldBar, "Shield bar fill."),
            ("Bar background", customTheme.BarBackground, "Health and shield bar empty background."),
            ("Bar border", customTheme.BarBorder, "Health and shield bar outline."));
        DrawCustomThemeColorGroup(
            "Widget",
            ("Widget background", customTheme.WidgetWindowBackground, "Current pull widget window background."),
            ("Widget title", customTheme.WidgetTitleBackground, "Current pull widget title bar."),
            ("Widget title active", customTheme.WidgetTitleActiveBackground, "Current pull widget active title bar."),
            ("Widget border", customTheme.WidgetBorder, "Current pull widget border."),
            ("Resize grip", customTheme.WidgetResizeGrip, "Current pull widget resize grip."),
            ("Resize grip hover", customTheme.WidgetResizeGripHover, "Hovered current pull widget resize grip."),
            ("Resize grip active", customTheme.WidgetResizeGripActive, "Active current pull widget resize grip."));
        DrawCustomThemeColorGroup(
            "Updates / notices",
            ("Update banner", customTheme.UpdateBannerBackground, "Update banner background."),
            ("Update banner text", customTheme.UpdateBannerText, "Update banner text."),
            ("Notice border", customTheme.NoticeBorder, "Creator note and notice border / highlight."),
            ("Notice text", customTheme.NoticeText, "Creator note and notice text."),
            ("Notice button", customTheme.NoticeButton, "Notice action button color."),
            ("Notice button hover", customTheme.NoticeButtonHover, "Hovered notice action button color."),
            ("Changelog tab", customTheme.ChangelogTab, "Changelog tab fill."),
            ("Changelog tab hover", customTheme.ChangelogTabHover, "Hovered changelog tab fill."),
            ("Changelog tab active", customTheme.ChangelogTabActive, "Active changelog tab fill."));
    }

    private void DrawCustomThemeColorGroup(
        string label,
        params (string Label, ThemeColorValue Color, string Tooltip)[] colors)
    {
        ImGui.TextColored(ModernMutedTextColor, label);
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var columnCount = availableWidth >= 560.0f ? 2 : 1;
        if (!ImGui.BeginTable($"##CustomTheme{label}", columnCount, ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        foreach (var color in colors)
        {
            ImGui.TableNextColumn();
            DrawCustomThemeColorEdit($"{label}{color.Label}", color.Label, color.Color, color.Tooltip);
        }

        ImGui.EndTable();
        ImGui.Spacing();
    }

    private void DrawCustomThemeColorEdit(string id, string label, ThemeColorValue color, string tooltip)
    {
        var vector = ToVector4(color);
        ImGui.SetNextItemWidth(MathF.Min(150.0f, ImGui.GetContentRegionAvail().X * 0.55f));
        if (ImGui.ColorEdit4($"##CustomThemeColor{id}", ref vector, ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            SetThemeColorValue(color, vector);
            activeTheme = BetterDeathsThemeCatalog.GetTheme(configuration);
        }

        var hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        ImGui.TextWrapped(label);
        hovered |= ImGui.IsItemHovered();
        if (hovered)
        {
            SetThemedTooltip(tooltip);
        }
    }

    private static Vector4 ToVector4(ThemeColorValue color)
    {
        return new Vector4(
            Math.Clamp(color.R, 0.0f, 1.0f),
            Math.Clamp(color.G, 0.0f, 1.0f),
            Math.Clamp(color.B, 0.0f, 1.0f),
            Math.Clamp(color.A, 0.0f, 1.0f));
    }

    private static ThemeColorValue ToThemeColorValue(Vector4 color)
    {
        return new ThemeColorValue(
            Math.Clamp(color.X, 0.0f, 1.0f),
            Math.Clamp(color.Y, 0.0f, 1.0f),
            Math.Clamp(color.Z, 0.0f, 1.0f),
            Math.Clamp(color.W, 0.0f, 1.0f));
    }

    private static void SetThemeColorValue(ThemeColorValue color, Vector4 value)
    {
        color.R = Math.Clamp(value.X, 0.0f, 1.0f);
        color.G = Math.Clamp(value.Y, 0.0f, 1.0f);
        color.B = Math.Clamp(value.Z, 0.0f, 1.0f);
        color.A = Math.Clamp(value.W, 0.0f, 1.0f);
    }

    private void DrawCustomThemePreview(BetterDeathsUiTheme theme)
    {
        var previewSize = new Vector2(ImGui.GetContentRegionAvail().X, 202.0f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, theme.ModernShellColor with { W = 0.96f });
        ImGui.PushStyleColor(ImGuiCol.Border, theme.ModernPanelBorderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10.0f, 9.0f));
        if (ImGui.BeginChild("##CustomThemePreview", previewSize, true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.TextColored(theme.LeadUpGoldColor, "Rendered preview");
            ImGui.TextColored(theme.ModernMutedTextColor, "Text, controls, rows, bars");
            ImGui.Spacing();

            ImGui.TextColored(theme.ModernTextColor, "Regular text");
            ImGui.SameLine();
            ImGui.TextColored(theme.ModernMutedTextColor, "Muted label");
            ImGui.TextColored(theme.LeadUpGoldColor, "Gold label");
            ImGui.SameLine();
            ImGui.TextColored(theme.DamageColor, "-81,937");
            ImGui.SameLine();
            ImGui.TextColored(theme.HealColor, "+34,682");
            ImGui.SameLine();
            ImGui.TextColored(theme.WarningColor, "Warning");

            DrawCustomThemePreviewButtons(theme);
            DrawCustomThemePreviewRow(theme);
            DrawCustomThemePreviewHpBar(theme);
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static void DrawCustomThemePreviewButtons(BetterDeathsUiTheme theme)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, theme.ModernNavButtonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.ModernNavButtonHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.ModernNavButtonActiveColor);
        ImGui.PushStyleColor(ImGuiCol.Text, theme.ModernButtonTextColor);
        ImGui.Button("Button", new Vector2(92.0f, 26.0f));
        ImGui.PopStyleColor(4);
        if (ImGui.GetContentRegionAvail().X >= 92.0f + ImGui.GetStyle().ItemSpacing.X)
        {
            ImGui.SameLine();
        }

        ImGui.PushStyleColor(ImGuiCol.Button, theme.ModernNavButtonSelectedColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.ModernNavButtonSelectedHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.ModernNavButtonActiveColor);
        ImGui.PushStyleColor(ImGuiCol.Text, theme.ModernSelectedButtonTextColor);
        ImGui.Button("Selected", new Vector2(92.0f, 26.0f));
        ImGui.PopStyleColor(4);

        if (ImGui.GetContentRegionAvail().X >= 132.0f + ImGui.GetStyle().ItemSpacing.X)
        {
            ImGui.SameLine();
        }

        var drawList = ImGui.GetWindowDrawList();
        var checkboxStart = ImGui.GetCursorScreenPos() + new Vector2(0.0f, 4.0f);
        var checkboxEnd = checkboxStart + new Vector2(18.0f, 18.0f);
        drawList.AddRectFilled(checkboxStart, checkboxEnd, ImGui.GetColorU32(theme.CheckboxFrameActiveColor), 4.0f);
        drawList.AddRect(checkboxStart, checkboxEnd, ImGui.GetColorU32(theme.CheckboxBorderColor), 4.0f, ImDrawFlags.None, 1.2f);
        drawList.AddLine(checkboxStart + new Vector2(4.0f, 9.0f), checkboxStart + new Vector2(8.0f, 13.0f), ImGui.GetColorU32(theme.ModernCheckMarkColor), 2.0f);
        drawList.AddLine(checkboxStart + new Vector2(8.0f, 13.0f), checkboxStart + new Vector2(14.0f, 5.0f), ImGui.GetColorU32(theme.ModernCheckMarkColor), 2.0f);
        drawList.AddText(checkboxStart + new Vector2(25.0f, 0.0f), ImGui.GetColorU32(theme.ModernTextColor), "Checkbox");
        ImGui.Dummy(new Vector2(MathF.Min(132.0f, ImGui.GetContentRegionAvail().X), 26.0f));
    }

    private static void DrawCustomThemePreviewRow(BetterDeathsUiTheme theme)
    {
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var narrow = width < 360.0f;
        var height = narrow ? 64.0f : 48.0f;
        var rowHeight = height * 0.46f;
        drawList.AddRectFilled(start, start + new Vector2(width, rowHeight), ImGui.GetColorU32(theme.TableRowAltColor), 4.0f);
        drawList.AddRect(start, start + new Vector2(width, rowHeight), ImGui.GetColorU32(theme.ModernPanelBorderColor), 4.0f);
        drawList.AddText(start + new Vector2(9.0f, 4.0f), ImGui.GetColorU32(theme.ModernTextColor), "Table row");
        var focusedStart = start + new Vector2(0.0f, rowHeight + 4.0f);
        drawList.AddRectFilled(focusedStart, focusedStart + new Vector2(width, rowHeight), ImGui.GetColorU32(theme.FocusedRowColor), 4.0f);
        drawList.AddRectFilled(focusedStart, focusedStart + new Vector2(2.0f, rowHeight), ImGui.GetColorU32(theme.FocusedRowAccentColor), 4.0f);
        drawList.AddText(focusedStart + new Vector2(9.0f, 4.0f), ImGui.GetColorU32(theme.ModernTextColor), "Focused row");
        if (narrow)
        {
            drawList.AddText(focusedStart + new Vector2(9.0f, 24.0f), ImGui.GetColorU32(theme.LeadUpGoldColor), "Gold label");
            drawList.AddText(focusedStart + new Vector2(width * 0.52f, 24.0f), ImGui.GetColorU32(theme.DamageColor), "Damage text");
        }
        else
        {
            drawList.AddText(focusedStart + new Vector2(width * 0.45f, 4.0f), ImGui.GetColorU32(theme.LeadUpGoldColor), "Gold label");
            drawList.AddText(focusedStart + new Vector2(width * 0.70f, 4.0f), ImGui.GetColorU32(theme.DamageColor), "Damage text");
        }

        ImGui.Dummy(new Vector2(width, height + 4.0f));
    }

    private static void DrawCustomThemePreviewHpBar(BetterDeathsUiTheme theme)
    {
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = MathF.Min(260.0f, ImGui.GetContentRegionAvail().X);
        var height = 18.0f;
        drawList.AddRectFilled(start, start + new Vector2(width, height), ImGui.GetColorU32(theme.BarBackgroundColor), 4.0f);
        drawList.AddRectFilled(start, start + new Vector2(width * 0.68f, height), ImGui.GetColorU32(theme.HpBarColor), 4.0f);
        drawList.AddRectFilled(start + new Vector2(width * 0.68f, 0.0f), start + new Vector2(width * 0.84f, height), ImGui.GetColorU32(theme.ShieldBarColor), 4.0f);
        drawList.AddRect(start, start + new Vector2(width, height), ImGui.GetColorU32(theme.BarBorderColor), 4.0f);
        drawList.AddText(start + new Vector2(7.0f, 1.0f), ImGui.GetColorU32(theme.ModernTextColor), "HP + shields");
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawThemeGroup(string label, string id, IReadOnlyList<BetterDeathsUiTheme> themes)
    {
        if (themes.Count == 0)
        {
            return;
        }

        ImGui.TextDisabled(label);
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var style = ImGui.GetStyle();
        const float minimumTileWidth = 76.0f;
        var columnCount = Math.Clamp(
            (int)MathF.Floor((availableWidth + style.ItemSpacing.X) / (minimumTileWidth + style.ItemSpacing.X)),
            1,
            Math.Min(8, themes.Count));

        if (!ImGui.BeginTable($"##ThemePicker{id}", columnCount, ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        foreach (var theme in themes)
        {
            ImGui.TableNextColumn();
            DrawThemeTile(theme);
        }

        ImGui.EndTable();
    }

    private static bool IsLightPanelTheme(BetterDeathsUiTheme theme)
    {
        return GetColorLuminance(theme.ModernPanelColor) >= 0.55f;
    }

    private static void DrawSubtleSeparator()
    {
        var cursor = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddLine(
            cursor,
            cursor + new Vector2(MathF.Max(0.0f, width), 0.0f),
            ImGui.GetColorU32(ModernDividerColor),
            1.0f);
        ImGui.Dummy(new Vector2(width, 1.0f));
    }

    private void DrawThemeTile(BetterDeathsUiTheme theme)
    {
        var selected = configuration.Theme == theme.Id;
        var cellWidth = ImGui.GetContentRegionAvail().X;
        var swatchSize = Math.Clamp(cellWidth - 18.0f, 34.0f, 44.0f);

        CenterNextItem(swatchSize);
        var position = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##ThemeTile{theme.Id}", new Vector2(swatchSize, swatchSize));
        var hovered = ImGui.IsItemHovered();
        var end = position + new Vector2(swatchSize, swatchSize);
        var drawList = ImGui.GetWindowDrawList();
        const float rounding = 7.0f;
        var innerPadding = MathF.Max(5.0f, swatchSize * 0.13f);
        var accentHeight = MathF.Max(7.0f, swatchSize * 0.18f);

        drawList.AddRectFilled(position, end, ImGui.GetColorU32(theme.ModernShellColor with { W = 1.0f }), rounding);
        drawList.AddRectFilled(
            position + new Vector2(innerPadding, innerPadding),
            end - new Vector2(innerPadding, innerPadding),
            ImGui.GetColorU32(theme.ModernPanelColor with { W = 1.0f }),
            MathF.Max(3.0f, rounding - 2.0f));
        drawList.AddRectFilled(
            new Vector2(position.X + innerPadding, end.Y - innerPadding - accentHeight),
            new Vector2(end.X - innerPadding, end.Y - innerPadding),
            ImGui.GetColorU32(theme.ModernAccentColor),
            3.0f);
        drawList.AddRect(
            position,
            end,
            ImGui.GetColorU32(selected ? LeadUpGoldColor : theme.ModernPanelBorderColor),
            rounding);

        if (hovered)
        {
            drawList.AddRect(position + new Vector2(1.0f), end - new Vector2(1.0f), ImGui.GetColorU32(theme.ModernAccentColor), rounding - 1.0f);
        }

        if (clicked)
        {
            plugin.SetTheme(theme.Id);
            MarkNewThemeBadgeSeen(theme.Id);
            activeTheme = BetterDeathsThemeCatalog.GetTheme(configuration);
        }

        if (ShouldShowNewThemeBadge(theme.Id))
        {
            var badgeSize = GetNewBadgeSize();
            var badgePosition = new Vector2(
                position.X + ((swatchSize - badgeSize.X) * 0.5f),
                position.Y - MathF.Min(5.0f, badgeSize.Y * 0.25f));
            DrawNewBadge(badgePosition, badgeSize);
        }

        var labelColor = selected ? LeadUpGoldColor : ModernTextColor;
        DrawCenteredOrWrappedText(theme.Label, labelColor);
    }

    private bool HasUnseenNewThemeBadges()
    {
        return NewThemeBadges.Any(ShouldShowNewThemeBadge);
    }

    private bool ShouldShowNewThemeBadge(BetterDeathsTheme theme)
    {
        return NewThemeBadges.Contains(theme) && !GetSeenNewThemeBadges().Contains(theme);
    }

    private void MarkNewThemeBadgeSeen(BetterDeathsTheme theme)
    {
        var seenThemes = GetSeenNewThemeBadges();
        if (!NewThemeBadges.Contains(theme) || seenThemes.Contains(theme))
        {
            return;
        }

        seenThemes.Add(theme);
        plugin.SaveConfiguration();
    }

    private List<BetterDeathsTheme> GetSeenNewThemeBadges()
    {
        configuration.SeenNewThemeBadges ??= [];
        return configuration.SeenNewThemeBadges;
    }

    private void DrawInlineDebugTabButton()
    {
        const string buttonLabel = "debug";
        var buttonWidth = ImGui.CalcTextSize(buttonLabel).X + (ImGui.GetStyle().FramePadding.X * 2.0f);
        var buttonX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - buttonWidth;
        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX(), buttonX));
        if (ImGui.Button(buttonLabel))
        {
            showDebugTab = !showDebugTab;
            plugin.SetShowDebugTab(showDebugTab);
            if (!showDebugTab && configuration.DebugLogEnabled)
            {
                plugin.SetDebugLogEnabled(false);
            }
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(showDebugTab
                ? "Hide Debug and turn debug logging off."
                : "Show Debug. Debug logging still has its own checkbox inside that page.");
        }
    }

    private void DrawWidgetTab()
    {
        var showCurrentPullWidget = configuration.ShowCurrentPullWidget;
        if (DrawThemedCheckbox("Show current pull widget", ref showCurrentPullWidget))
        {
            plugin.SetShowCurrentPullWidget(showCurrentPullWidget);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Keeps the current pull death recap in its own window. Closing the widget turns this setting off.");
        }

        var widgetBackgroundOpacity = GetCurrentPullWidgetBackgroundOpacity();
        if (ImGui.SliderFloat(
            "Widget background opacity",
            ref widgetBackgroundOpacity,
            Plugin.CurrentPullWidgetMinBackgroundOpacity,
            Plugin.CurrentPullWidgetMaxBackgroundOpacity,
            "%.2f"))
        {
            plugin.SetCurrentPullWidgetBackgroundOpacity(widgetBackgroundOpacity);
        }

        DrawSettingsTooltip("Controls only the Current Pull widget background. Text, icons, tables, and HP bars stay fully visible. The lower limit keeps enough contrast over gameplay.");

        var widgetIconSize = GetWidgetIconSize();
        if (ImGui.SliderFloat(
            "Widget icon size",
            ref widgetIconSize,
            Plugin.MinWidgetIconSize,
            Plugin.MaxWidgetIconSize,
            "%.0f px"))
        {
            plugin.SetWidgetIconSize(widgetIconSize);
        }

        DrawSettingsTooltip("Controls only the Current Pull widget job and mitigation/debuff icon sizes.");

        DrawWidgetDisplayModeSetting();

        DrawSettingsTooltip("Normal keeps the full widget detail. Concise uses player initials, damage-only events, and fits mitigation/debuff icons to available space with a +x count.");

        ImGui.Separator();
        ImGui.TextUnformatted("Widget preview");
        ImGui.TextDisabled("Uses static example pull data so the preview stays available outside combat.");
        DrawCurrentPullWidgetPreview();
        DrawReviewPaneBottomPadding();
    }

    private static string GetWidgetDisplayModeLabel(WidgetDisplayMode mode)
    {
        return mode switch
        {
            WidgetDisplayMode.Concise => "Concise",
            _ => "Normal",
        };
    }

    private void DrawCurrentPullWidgetPreview()
    {
        var previewHeight = MathF.Min(420.0f, MathF.Max(260.0f, ImGui.GetContentRegionAvail().Y));
        var opacity = GetCurrentPullWidgetBackgroundOpacity();
        var theme = BetterDeathsThemeCatalog.GetTheme(configuration);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.BeginChild("##CurrentPullWidgetPreview", new Vector2(0.0f, previewHeight), false, OptionalScrollbarFlags))
        {
            var titleHeight = DrawWidgetPreviewBackground(theme, opacity);
            ImGui.SetCursorPos(new Vector2(0.0f, titleHeight));
            DrawCurrentPullWidgetContent(GetExampleDeaths(), "Sigmascape V4.0 - 04:53", "WidgetPreview");
            DrawWidgetPreviewChrome(theme, titleHeight);
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private static float DrawWidgetPreviewBackground(BetterDeathsUiTheme theme, float opacity)
    {
        var drawList = ImGui.GetWindowDrawList();
        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var end = position + size;
        var titleHeight = MathF.Max(ImGui.GetFrameHeight(), ImGui.GetTextLineHeight() + 8.0f);
        const float tileSize = 28.0f;
        var darkTileColor = ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.12f, 1.0f));
        var lightTileColor = ImGui.GetColorU32(new Vector4(0.28f, 0.28f, 0.28f, 1.0f));
        var rowIndex = 0;

        for (var y = position.Y; y < end.Y; y += tileSize)
        {
            var columnIndex = 0;
            for (var x = position.X; x < end.X; x += tileSize)
            {
                var tileEnd = new Vector2(
                    MathF.Min(x + tileSize, end.X),
                    MathF.Min(y + tileSize, end.Y));
                drawList.AddRectFilled(
                    new Vector2(x, y),
                    tileEnd,
                    ((rowIndex + columnIndex) & 1) == 0 ? darkTileColor : lightTileColor);
                columnIndex++;
            }

            rowIndex++;
        }

        drawList.AddRectFilled(
            position,
            end,
            ImGui.GetColorU32(theme.WidgetWindowBackgroundColor with { W = Math.Clamp(opacity, 0.0f, 1.0f) }),
            8.0f);
        drawList.AddRectFilled(
            position,
            new Vector2(end.X, MathF.Min(end.Y, position.Y + titleHeight)),
            ImGui.GetColorU32(theme.WidgetTitleBackgroundColor));

        return titleHeight;
    }

    private static void DrawWidgetPreviewChrome(BetterDeathsUiTheme theme, float titleHeight)
    {
        var drawList = ImGui.GetWindowDrawList();
        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var end = position + size;
        var textPosition = position + new Vector2(8.0f, MathF.Max(0.0f, (titleHeight - ImGui.GetTextLineHeight()) * 0.5f));

        drawList.AddText(textPosition, ImGui.GetColorU32(theme.ModernTextColor), "Better Deaths Widget");
        DrawWidgetPreviewTitleControls(theme, position, size, titleHeight);
        DrawWidgetPreviewResizeGrip(theme, position, size, left: true);
        DrawWidgetPreviewResizeGrip(theme, position, size, left: false);
        drawList.AddRect(
            position + new Vector2(0.5f),
            end - new Vector2(0.5f),
            ImGui.GetColorU32(theme.WidgetBorderColor),
            8.0f);
    }

    private static void DrawWidgetPreviewTitleControls(BetterDeathsUiTheme theme, Vector2 position, Vector2 size, float titleHeight)
    {
        var drawList = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(theme.ModernTextColor);
        var centerY = position.Y + (titleHeight * 0.5f);
        var closeCenterX = position.X + size.X - 10.0f;
        var arrowCenterX = closeCenterX - 18.0f;
        var menuCenterX = arrowCenterX - 18.0f;

        for (var lineIndex = 0; lineIndex < 3; lineIndex++)
        {
            var y = centerY - 5.0f + (lineIndex * 4.0f);
            drawList.AddLine(new Vector2(menuCenterX - 5.0f, y), new Vector2(menuCenterX + 5.0f, y), color, 1.4f);
        }

        drawList.AddLine(new Vector2(arrowCenterX - 4.0f, centerY - 2.0f), new Vector2(arrowCenterX, centerY + 3.0f), color, 1.4f);
        drawList.AddLine(new Vector2(arrowCenterX + 4.0f, centerY - 2.0f), new Vector2(arrowCenterX, centerY + 3.0f), color, 1.4f);
        drawList.AddLine(new Vector2(closeCenterX - 4.0f, centerY - 4.0f), new Vector2(closeCenterX + 4.0f, centerY + 4.0f), color, 1.4f);
        drawList.AddLine(new Vector2(closeCenterX + 4.0f, centerY - 4.0f), new Vector2(closeCenterX - 4.0f, centerY + 4.0f), color, 1.4f);
    }

    private static void DrawWidgetPreviewResizeGrip(BetterDeathsUiTheme theme, Vector2 position, Vector2 size, bool left)
    {
        const float inset = 5.0f;
        const float lineSpacing = 4.0f;
        const float thickness = 1.3f;

        var drawList = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(theme.WidgetResizeGripColor);
        var origin = left
            ? new Vector2(position.X + inset, position.Y + size.Y - inset)
            : new Vector2(position.X + size.X - inset, position.Y + size.Y - inset);

        for (var lineIndex = 0; lineIndex < 3; lineIndex++)
        {
            var offset = lineIndex * lineSpacing;
            if (left)
            {
                drawList.AddLine(
                    new Vector2(origin.X + offset, origin.Y),
                    new Vector2(origin.X, origin.Y - offset),
                    color,
                    thickness);
            }
            else
            {
                drawList.AddLine(
                    new Vector2(origin.X - offset, origin.Y),
                    new Vector2(origin.X, origin.Y - offset),
                    color,
                    thickness);
            }
        }
    }

    private float GetCurrentPullWidgetBackgroundOpacity()
    {
        return Math.Clamp(
            configuration.CurrentPullWidgetBackgroundOpacity <= 0.0f
                ? Plugin.CurrentPullWidgetMaxBackgroundOpacity
                : configuration.CurrentPullWidgetBackgroundOpacity,
            Plugin.CurrentPullWidgetMinBackgroundOpacity,
            Plugin.CurrentPullWidgetMaxBackgroundOpacity);
    }

    private float GetMainWindowBackgroundOpacity()
    {
        return Math.Clamp(
            configuration.MainWindowBackgroundOpacity <= 0.0f
                ? Plugin.DefaultMainWindowBackgroundOpacity
                : configuration.MainWindowBackgroundOpacity,
            Plugin.MainWindowMinBackgroundOpacity,
            Plugin.MainWindowMaxBackgroundOpacity);
    }

    private float GetReplayWorldMarkerOpacity()
    {
        return Math.Clamp(
            configuration.ReplayWorldMarkerOpacity <= 0.0f
                ? Plugin.DefaultReplayWorldMarkerOpacity
                : configuration.ReplayWorldMarkerOpacity,
            Plugin.MinReplayWorldMarkerOpacity,
            Plugin.MaxReplayWorldMarkerOpacity);
    }

    private static Vector4 WithBackgroundOpacity(Vector4 color, float opacity)
    {
        return color with
        {
            W = Math.Clamp(opacity, Plugin.MainWindowMinBackgroundOpacity, Plugin.MainWindowMaxBackgroundOpacity),
        };
    }

    private string FormatPlayerName(PartyDeathRecord death)
    {
        return plugin.FormatPlayerDisplayName(death);
    }

    private string FormatPlayerName(PartyDeathRecord death, IReadOnlyList<PartyDeathRecord>? context)
    {
        return plugin.FormatPlayerDisplayName(death, context);
    }

    private string FormatPlayerName(DebugStatusSnapshot snapshot)
    {
        return plugin.FormatPlayerDisplayName(
            snapshot.MemberName,
            snapshot.MemberKey,
            snapshot.PartyIndex,
            snapshot.ClassJobId,
            snapshot.ClassJobName);
    }

    private string FormatKnownPlayerName(string name)
    {
        return plugin.FormatKnownPlayerName(name);
    }

    private string RedactKnownPlayerNamesInText(string text)
    {
        return plugin.RedactKnownPlayerNamesInText(text);
    }

    private float GetWidgetIconSize()
    {
        return Math.Clamp(
            configuration.WidgetIconSize <= 0.0f ? 20.0f : configuration.WidgetIconSize,
            Plugin.MinWidgetIconSize,
            Plugin.MaxWidgetIconSize);
    }

    private static void DrawSettingsTooltip(string tooltip)
    {
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(tooltip);
        }
    }

    private void DrawPluginUpdateBanner()
    {
        var status = plugin.PluginUpdateStatus;
        if (!ShouldDrawPluginUpdateBanner(status))
        {
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, UpdateBannerBgColor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(ReviewPaneContentIndent, 6.0f));
        if (ImGui.BeginChild("##BetterDeathsUpdateBanner", new Vector2(0.0f, 52.0f), true, OptionalScrollbarFlags))
        {
            ImGui.TextColored(UpdateBannerTextColor, GetPluginUpdateStatusText(status));
            ImGui.TextDisabled("Open the Dalamud plugin installer to update Better Deaths.");
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawClockDisplaySetting()
    {
        ImGui.TextUnformatted("Clock Display");
        DrawClockDisplayModeSegment("24-hour", ClockDisplayMode.TwentyFourHour);
        DrawTextSelectorSeparator();
        DrawClockDisplayModeSegment("12-hour", ClockDisplayMode.TwelveHour);

        DrawSettingsTooltip("Controls local clock times shown in recorded pull descriptions.");
    }

    private void DrawClockDisplayModeSegment(string label, ClockDisplayMode mode)
    {
        var selected = configuration.ClockDisplayMode == mode;
        if (DrawTextSelectorOption(label, $"ClockDisplayMode{mode}", selected))
        {
            plugin.SetClockDisplayMode(mode);
        }
    }

    private void DrawWidgetDisplayModeSetting()
    {
        ImGui.TextUnformatted("Display");
        DrawWidgetDisplayModeSegment(WidgetDisplayMode.Normal);
        DrawTextSelectorSeparator();
        DrawWidgetDisplayModeSegment(WidgetDisplayMode.Concise);
    }

    private void DrawWidgetDisplayModeSegment(WidgetDisplayMode mode)
    {
        var selected = configuration.WidgetDisplayMode == mode;
        if (DrawTextSelectorOption(GetWidgetDisplayModeLabel(mode), $"WidgetDisplayMode{mode}", selected))
        {
            plugin.SetWidgetDisplayMode(mode);
        }
    }

    private static bool ShouldDrawPluginUpdateBanner(PluginUpdateStatus status)
    {
        return status.State is PluginUpdateCheckState.UpdateAvailable or PluginUpdateCheckState.VersionMismatch;
    }

    private static string GetPluginUpdateStatusText(PluginUpdateStatus status)
    {
        return status.State switch
        {
            PluginUpdateCheckState.WaitingForDalamud => "Waiting for Dalamud to finish plugin update checks.",
            PluginUpdateCheckState.Checking => "Checking for Better Deaths updates...",
            PluginUpdateCheckState.UpToDate => "No Better Deaths update detected.",
            PluginUpdateCheckState.UpdateAvailable => $"Better Deaths update available: v{status.AvailableVersion}{(status.AvailableVersionIsTesting ? " testing" : string.Empty)}.",
            PluginUpdateCheckState.VersionMismatch => "Better Deaths version mismatch detected. Restart or update through Dalamud.",
            PluginUpdateCheckState.Error => $"Could not check for Better Deaths updates{(string.IsNullOrWhiteSpace(status.Error) ? "." : $": {status.Error}")}",
            _ => "Better Deaths has not checked for updates yet.",
        };
    }

    private static void SetThemedTooltip(string text)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, TooltipWindowPadding);
        ImGui.SetTooltip(text);
        ImGui.PopStyleVar();
    }

    private static void DrawPostDeathRecapLinksTooltip()
    {
        BeginThemedTooltip();
        ImGui.TextUnformatted("Posts a clickable ");
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.TextColored(LeadUpGoldColor, "[ Death Link ]");
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.TextUnformatted(" in the selected local chat channel after captured deaths.");
        ImGui.TextUnformatted("Party members who share death information will still show a ");
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.TextColored(LeadUpGoldColor, "[ Pull Link ]");
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.TextUnformatted(" regardless of this toggle.");
        EndThemedTooltip();
    }

    private static void BeginThemedTooltip()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, TooltipWindowPadding);
        ImGui.BeginTooltip();
    }

    private static void EndThemedTooltip()
    {
        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    private static void DrawWrappedText(string text, Vector4? color = null)
    {
        if (color is { } textColor)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        }

        ImGui.TextWrapped(text);

        if (color is not null)
        {
            ImGui.PopStyleColor();
        }
    }

    private void DrawDebugTab()
    {
        ImGui.TextWrapped("Debug shows raw statuses and packet/control captures for tracked characters in the current duty, plus the internal capture log. Use this to verify whether Dalamud exposes data before Better Deaths filters it for recaps.");
        ImGui.TextColored(SpamWarningColor, "Warning: this is for troubleshooting only and can get noisy while combat events are happening.");
        ImGui.TextDisabled("Debug data stays until duty enter or manual clear. New pulls inside the same duty will append to the same history.");

        var debugEnabled = configuration.DebugLogEnabled;
        if (DrawThemedCheckbox("Enable debug capture", ref debugEnabled))
        {
            plugin.SetDebugLogEnabled(debugEnabled);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear debug data"))
        {
            plugin.ClearDebugLog();
        }

        ImGui.SameLine();
        var freezeOnDeath = plugin.DebugFreezeOnDeathEnabled;
        if (DrawThemedCheckbox("Freeze on death", ref freezeOnDeath))
        {
            plugin.SetDebugFreezeOnDeathEnabled(freezeOnDeath);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("When enabled, Debug records the death control event and then stops accepting new debug rows until resumed or cleared.");
        }

        if (plugin.DebugCaptureFrozen)
        {
            ImGui.SameLine();
            ImGui.TextColored(WarningColor, "Frozen");
            ImGui.SameLine();
            if (ImGui.Button("Resume debug capture"))
            {
                plugin.SetDebugCaptureFrozen(false);
            }
        }

        var saveDebugFile = configuration.DebugSaveToFileEnabled;
        if (DrawThemedCheckbox("Save debug file", ref saveDebugFile))
        {
            plugin.SetDebugSaveToFileEnabled(saveDebugFile);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("When enabled, Debug writes captured rows to a local JSONL file. The newest rows are kept and the file is capped at 25 MB.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear saved debug file"))
        {
            plugin.ClearSavedDebugCaptureFile();
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Deletes the saved debug JSONL file and clears pending debug-file rows. This does not clear the visible in-memory Debug tables.");
        }

        ImGui.TextDisabled($"Saved debug file: {FormatByteSize(plugin.DebugCaptureFileSizeBytes)} / {FormatByteSize(plugin.DebugCaptureMaxFileSizeBytes)}");
        if (plugin.DebugCaptureQueuedLineCount > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"Queued rows: {plugin.DebugCaptureQueuedLineCount:N0}");
        }

        ImGui.TextDisabled(plugin.DebugCaptureFilePath);

        DrawDebugFilters();

        ImGui.TextDisabled(
            $"Capture state: Duty {FormatDebugBool(plugin.DebugIsDutyCaptureActive)} | Combat {FormatDebugBool(plugin.DebugIsInCombat)} | Live capture {FormatDebugBool(plugin.DebugShouldCaptureLiveCombat)} | EffectResult hook {FormatDebugBool(plugin.DebugEffectResultHookEnabled)} | ActorControl hook {FormatDebugBool(plugin.DebugActorControlHookEnabled)} | PvP blocked {FormatDebugBool(plugin.DebugIsPvPCaptureBlocked)} | Tracked {plugin.CurrentMembers.Count:N0}");

        ImGui.Separator();
        DrawDebugCaptureTab();
        DrawReviewPaneBottomPadding();
    }

    private void DrawDebugCaptureTab()
    {
        DrawAddonInspector();
        ImGui.Separator();
        DrawDebugStatusSnapshots();
        ImGui.Separator();
        DrawDebugEffectResultSnapshots();
        ImGui.Separator();
        DrawDebugActorControlEvents();

        ImGui.Separator();
        DrawDebugLog();
    }

    private void DrawDebugFilters()
    {
        ImGui.SetNextItemWidth(MathF.Max(180.0f, ImGui.GetContentRegionAvail().X * 0.35f));
        ImGui.InputText("Player/text filter##DebugTextFilter", ref debugTextFilter, 128);
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Filters Debug rows by player, target, source, category, action/status text, or raw IDs shown in the row.");
        }

        if (!string.IsNullOrWhiteSpace(debugTextFilter))
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear filter"))
            {
                debugTextFilter = string.Empty;
            }
        }

        ImGui.SetNextItemWidth(MathF.Max(180.0f, ImGui.GetContentRegionAvail().X * 0.35f));
        var currentLabel = DebugActorControlCategoryFilters[Math.Clamp(debugActorControlCategoryFilterIndex, 0, DebugActorControlCategoryFilters.Length - 1)];
        if (ImGui.BeginCombo("Control category##DebugActorControlCategory", currentLabel))
        {
            for (var i = 0; i < DebugActorControlCategoryFilters.Length; i++)
            {
                var selected = debugActorControlCategoryFilterIndex == i;
                if (ImGui.Selectable(DebugActorControlCategoryFilters[i], selected))
                {
                    debugActorControlCategoryFilterIndex = i;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawAddonInspector()
    {
        ImGui.TextColored(LeadUpGoldColor, "Addon inspector");
        ImGui.TextDisabled("Read-only. Open the game window you want to inspect, then snapshot its addon name.");

        ImGui.SetNextItemWidth(MathF.Max(220.0f, ImGui.GetContentRegionAvail().X * 0.35f));
        ImGui.InputText("Addon name##AddonInspectorName", ref addonInspectorName, 128);
        ImGui.SameLine();
        if (ImGui.Button("Snapshot addon"))
        {
            plugin.CaptureAddonInspectorSnapshot(addonInspectorName);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear addon inspector"))
        {
            plugin.ClearAddonInspector();
        }

        ImGui.SetNextItemWidth(MathF.Max(220.0f, ImGui.GetContentRegionAvail().X * 0.35f));
        ImGui.InputText("Addon event filter##AddonInspectorEventFilter", ref addonInspectorEventFilter, 128);
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Filters addon lifecycle rows by addon name, event name, or address. Separate words are treated as separate search terms.");
        }

        ImGui.SameLine();
        if (ImGui.Button("board/strat"))
        {
            addonInspectorEventFilter = "board strat strategy";
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear addon filter"))
        {
            addonInspectorEventFilter = string.Empty;
        }

        ImGui.SameLine();
        DrawThemedCheckbox("Hide common UI noise", ref addonInspectorHideCommonNoise);
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Hides very common HUD addons like nameplates, cast bars, and minimap from the inspector event list.");
        }

        DrawAddonInspectorEvents();
        DrawAddonInspectorSnapshot();
    }

    private void DrawAddonInspectorEvents()
    {
        var allEvents = plugin.AddonInspectorEvents;
        var events = allEvents
            .Where(MatchesAddonInspectorEvent)
            .Take(80)
            .ToList();
        if (allEvents.Count == 0)
        {
            ImGui.TextDisabled("No addon lifecycle events captured yet. Open a game UI window.");
            return;
        }

        if (events.Count == 0)
        {
            ImGui.TextDisabled("No addon lifecycle events match the current filter.");
            return;
        }

        ImGui.TextDisabled($"Showing {events.Count:N0} of {allEvents.Count:N0} latest addon lifecycle events.");
        if (!ImGui.BeginTable("##AddonInspectorEvents", 7, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("Use", ImGuiTableColumnFlags.WidthFixed, 44.0f);
        ImGui.TableSetupColumn("UTC", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("Event", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("Addon", ImGuiTableColumnFlags.WidthStretch, 2.0f);
        ImGui.TableSetupColumn("Address", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("Ready", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Visible", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableHeadersRow();

        foreach (var entry in events)
        {
            ImGui.PushID($"AddonInspectorEvent{entry.SeenAtUtc.Ticks}{entry.Address}{entry.AddonName}");
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            if (ImGui.SmallButton("Use"))
            {
                addonInspectorName = entry.AddonName;
            }

            ImGui.TableSetColumnIndex(1);
            DrawCenteredText(entry.SeenAtUtc.ToString("HH:mm:ss"));

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(entry.EventName);

            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(entry.AddonName);

            ImGui.TableSetColumnIndex(4);
            DrawCenteredText(FormatDebugAddress(entry.Address));

            ImGui.TableSetColumnIndex(5);
            DrawCenteredText(FormatDebugBool(entry.IsReady));

            ImGui.TableSetColumnIndex(6);
            DrawCenteredText(FormatDebugBool(entry.IsVisible));

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawAddonInspectorSnapshot()
    {
        var snapshot = plugin.AddonInspectorSnapshot;
        if (snapshot is null)
        {
            ImGui.TextDisabled("No addon snapshot yet.");
            return;
        }

        ImGui.Spacing();
        ImGui.TextColored(LeadUpGoldColor, "Latest addon snapshot");
        ImGui.TextDisabled(
            $"{snapshot.AddonName} | {snapshot.SeenAtUtc:HH:mm:ss} UTC | {FormatDebugAddress(snapshot.Address)} | Ready {FormatDebugBool(snapshot.IsReady)} | Visible {FormatDebugBool(snapshot.IsVisible)} | Pos {snapshot.X:N0}, {snapshot.Y:N0} | Size {snapshot.Width:N0} x {snapshot.Height:N0}");

        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            ImGui.TextColored(WarningColor, snapshot.Error);
            return;
        }

        DrawAddonInspectorAtkValues(snapshot);
        DrawAddonInspectorNodes(snapshot);
    }

    private void DrawAddonInspectorAtkValues(AddonInspectorSnapshot snapshot)
    {
        var values = snapshot.AtkValues
            .Where(MatchesAddonInspectorValue)
            .ToList();
        if (snapshot.AtkValues.Count == 0)
        {
            ImGui.TextDisabled("No AtkValues exposed on this snapshot.");
            return;
        }

        if (values.Count == 0)
        {
            ImGui.TextDisabled("No AtkValues match the current filter.");
            return;
        }

        if (!ImGui.TreeNode($"AtkValues ({values.Count:N0}/{snapshot.AtkValues.Count:N0})###AddonInspectorAtkValues"))
        {
            return;
        }

        if (ImGui.BeginTable("##AddonInspectorAtkValuesTable", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthStretch, 0.35f);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthStretch, 0.8f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 3.0f);
            ImGui.TableHeadersRow();

            foreach (var value in values)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                DrawCenteredText(value.Index.ToString(CultureInfo.InvariantCulture));

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(value.Type);

                ImGui.TableSetColumnIndex(2);
                ImGui.TextWrapped(value.Value);
            }

            ImGui.EndTable();
        }

        ImGui.TreePop();
    }

    private void DrawAddonInspectorNodes(AddonInspectorSnapshot snapshot)
    {
        var nodes = snapshot.Nodes
            .Where(MatchesAddonInspectorNode)
            .ToList();
        if (snapshot.Nodes.Count == 0)
        {
            ImGui.TextDisabled("No nodes exposed on this snapshot.");
            return;
        }

        if (nodes.Count == 0)
        {
            ImGui.TextDisabled("No nodes match the current filter.");
            return;
        }

        if (!ImGui.TreeNode($"Nodes ({nodes.Count:N0}/{snapshot.NodeCount:N0})###AddonInspectorNodes"))
        {
            return;
        }

        if (ImGui.BeginTable("##AddonInspectorNodesTable", 8, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthStretch, 0.35f);
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthStretch, 0.65f);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("Visible", ImGuiTableColumnFlags.WidthStretch, 0.65f);
            ImGui.TableSetupColumn("X/Y", ImGuiTableColumnFlags.WidthStretch, 0.75f);
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthStretch, 0.75f);
            ImGui.TableSetupColumn("Text", ImGuiTableColumnFlags.WidthStretch, 2.4f);
            ImGui.TableSetupColumn("Raw", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableHeadersRow();

            foreach (var node in nodes)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                DrawCenteredText(node.Index.ToString(CultureInfo.InvariantCulture));

                ImGui.TableSetColumnIndex(1);
                DrawCenteredText(node.NodeId.ToString(CultureInfo.InvariantCulture));

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(node.NodeType);

                ImGui.TableSetColumnIndex(3);
                DrawCenteredText(FormatDebugBool(node.IsVisible));

                ImGui.TableSetColumnIndex(4);
                DrawCenteredText($"{node.X:N0}, {node.Y:N0}");

                ImGui.TableSetColumnIndex(5);
                DrawCenteredText($"{node.Width:N0} x {node.Height:N0}");

                ImGui.TableSetColumnIndex(6);
                if (string.IsNullOrWhiteSpace(node.Text))
                {
                    ImGui.TextDisabled("-");
                }
                else
                {
                    ImGui.TextWrapped(node.Text);
                }

                ImGui.TableSetColumnIndex(7);
                ImGui.TextDisabled(node.Text is null ? "-" : "Text");
            }

            ImGui.EndTable();
        }

        ImGui.TreePop();
    }

    private bool MatchesAddonInspectorEvent(AddonInspectorEvent entry)
    {
        if (addonInspectorHideCommonNoise && IsCommonAddonInspectorNoise(entry.AddonName))
        {
            return false;
        }

        if (!MatchesAddonInspectorEventFilter(entry))
        {
            return false;
        }

        if (!TryGetDebugTextFilter(out var filter))
        {
            return true;
        }

        return MatchesDebugTextFilter(
            filter,
            entry.EventName,
            entry.AddonName,
            FormatDebugAddress(entry.Address),
            FormatDebugBool(entry.IsReady),
            FormatDebugBool(entry.IsVisible));
    }

    private bool MatchesAddonInspectorEventFilter(AddonInspectorEvent entry)
    {
        var filter = addonInspectorEventFilter.Trim();
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var terms = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Any(term => MatchesDebugTextFilter(
            term,
            entry.EventName,
            entry.AddonName,
            FormatDebugAddress(entry.Address)));
    }

    private static bool IsCommonAddonInspectorNoise(string addonName)
    {
        return addonName is "NamePlate" or "CastBarEnemy" or "_NaviMap" or "_ParameterWidget" or "_DTR" or "_TargetInfo" or "_FocusTargetInfo" or "_EnemyList" or "_PartyList" ||
            addonName.StartsWith("_ActionBar", StringComparison.Ordinal) ||
            addonName.StartsWith("_Status", StringComparison.Ordinal) ||
            addonName.StartsWith("_CastBar", StringComparison.Ordinal);
    }

    private bool MatchesAddonInspectorValue(AddonInspectorValue value)
    {
        if (!TryGetDebugTextFilter(out var filter))
        {
            return true;
        }

        return MatchesDebugTextFilter(
            filter,
            value.Index.ToString(CultureInfo.InvariantCulture),
            value.Type,
            value.Value);
    }

    private bool MatchesAddonInspectorNode(AddonInspectorNode node)
    {
        if (!TryGetDebugTextFilter(out var filter))
        {
            return true;
        }

        return MatchesDebugTextFilter(
            filter,
            node.Index.ToString(CultureInfo.InvariantCulture),
            node.NodeId.ToString(CultureInfo.InvariantCulture),
            node.NodeType,
            FormatDebugBool(node.IsVisible),
            node.Text);
    }

    private bool MatchesDebugStatusSnapshot(DebugStatusSnapshot snapshot)
    {
        if (!TryGetDebugTextFilter(out var filter))
        {
            return true;
        }

        return MatchesDebugTextFilter(
            filter,
            snapshot.MemberName,
            FormatPlayerName(snapshot),
            snapshot.ClassJobName,
            snapshot.PartyIndex.ToString(),
            FormatDebugStatusSource(snapshot.ClassJobId)) ||
            MatchesDebugTextFilter(
                filter,
                string.Join(" ", snapshot.Statuses.Select(status => $"{status.Name} {status.Id} {FormatDebugStatusSource(status.SourceId)}")));
    }

    private bool MatchesDebugEffectResultSnapshot(DebugEffectResultSnapshot snapshot)
    {
        if (!TryGetDebugTextFilter(out var filter))
        {
            return true;
        }

        return MatchesDebugTextFilter(
            filter,
            snapshot.TargetName,
            FormatKnownPlayerName(snapshot.TargetName),
            FormatDebugStatusSource(snapshot.TargetId),
            FormatDebugStatusSource(snapshot.ActorId),
            snapshot.RelatedActionSequence.ToString(CultureInfo.InvariantCulture)) ||
            MatchesDebugTextFilter(
                filter,
                string.Join(" ", snapshot.Statuses.Select(status => $"{status.Name} {status.EffectId} {FormatKnownPlayerName(status.SourceName)} {FormatDebugStatusSource(status.SourceActorId)}")));
    }

    private bool MatchesDebugActorControlEvent(DebugActorControlEvent entry)
    {
        if (!MatchesDebugActorControlCategory(entry))
        {
            return false;
        }

        if (!TryGetDebugTextFilter(out var filter))
        {
            return true;
        }

        return MatchesDebugTextFilter(
            filter,
            entry.EntityName,
            FormatKnownPlayerName(entry.EntityName),
            entry.TargetName,
            FormatKnownPlayerName(entry.TargetName),
            entry.CategoryName,
            entry.Category.ToString(CultureInfo.InvariantCulture),
            FormatDebugStatusSource(entry.EntityId),
            FormatDebugActorControlTarget(entry.TargetId),
            entry.Param1.ToString(CultureInfo.InvariantCulture),
            entry.Param2.ToString(CultureInfo.InvariantCulture),
            entry.Param3.ToString(CultureInfo.InvariantCulture),
            entry.Param4.ToString(CultureInfo.InvariantCulture),
            entry.Param5.ToString(CultureInfo.InvariantCulture),
            entry.Param6.ToString(CultureInfo.InvariantCulture),
            entry.Param7.ToString(CultureInfo.InvariantCulture),
            entry.Param8.ToString(CultureInfo.InvariantCulture),
            entry.Param9.ToString(CultureInfo.InvariantCulture));
    }

    private bool MatchesDebugLogEntry(DebugLogEntry entry)
    {
        if (!TryGetDebugTextFilter(out var filter))
        {
            return true;
        }

        return MatchesDebugTextFilter(
            filter,
            entry.SeenAtUtc.ToString("HH:mm:ss"),
            FormatCombatTimer(entry.PullElapsedSeconds),
            entry.Message,
            RedactKnownPlayerNamesInText(entry.Message));
    }

    private bool TryGetDebugTextFilter(out string filter)
    {
        filter = debugTextFilter.Trim();
        return !string.IsNullOrWhiteSpace(filter);
    }

    private static bool MatchesDebugTextFilter(string filter, params string?[] values)
    {
        return values.Any(value =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesDebugActorControlCategory(DebugActorControlEvent entry)
    {
        return debugActorControlCategoryFilterIndex switch
        {
            1 => entry.Category == 0x6,
            2 => entry.Category == 0x605,
            3 => entry.Category == 0x604,
            4 => entry.Category is 0x14 or 0x15 or 0x16,
            5 => entry.Category is 0x22 or 0x23 or 0x36 or 0x1F6,
            6 => entry.Category is not (0x6 or 0x604 or 0x605 or 0x14 or 0x15 or 0x16 or 0x22 or 0x23 or 0x36 or 0x1F6),
            _ => true,
        };
    }

    private void DrawDebugStatusSnapshots()
    {
        ImGui.TextColored(LeadUpGoldColor, "Captured raw status table");
        ImGui.TextDisabled("Memory-only. Respects Capture Party and Capture Others settings. Statuses stay listed until duty enter or manual clear.");

        if (!configuration.DebugLogEnabled)
        {
            ImGui.TextDisabled("Enable debug capture to see raw statuses.");
            return;
        }

        var allSnapshots = plugin.DebugStatusSnapshots;
        var snapshots = allSnapshots
            .Where(MatchesDebugStatusSnapshot)
            .ToList();
        if (allSnapshots.Count == 0)
        {
            ImGui.TextDisabled("No tracked status snapshots yet. Enter an active duty with tracked characters visible.");
            return;
        }

        if (snapshots.Count == 0)
        {
            ImGui.TextDisabled("No raw status rows match the current filter.");
            return;
        }

        foreach (var snapshot in snapshots)
        {
            ImGui.PushID($"DebugStatus{snapshot.MemberKey}");
            var deadText = snapshot.IsDead ? " dead" : string.Empty;
            var label = $"{FormatPlayerName(snapshot)} ({snapshot.ClassJobName}) - {snapshot.Statuses.Count:N0} captured statuses{deadText}###DebugStatusSnapshot";
            if (ImGui.TreeNode(label))
            {
                var shieldText = snapshot.ShieldHp > 0 ? $" + {snapshot.ShieldHp:N0} shield" : string.Empty;
                ImGui.TextDisabled(
                    $"Last seen {snapshot.SeenAtUtc:HH:mm:ss} UTC | Pull {FormatCombatTimer(snapshot.PullElapsedSeconds)} | HP {snapshot.CurrentHp:N0}{shieldText} / {snapshot.MaxHp:N0}");

                if (snapshot.Statuses.Count == 0)
                {
                    ImGui.TextDisabled("No raw statuses captured for this character.");
                }
                else
                {
                    DrawDebugStatusTable(snapshot);
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }
    }

    private void DrawDebugStatusTable(DebugStatusSnapshot snapshot)
    {
        if (!ImGui.BeginTable("##DebugRawStatuses", 6, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, Math.Clamp(configuration.StatusIconSize, 16.0f, 32.0f) + 10.0f);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 2.0f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 0.85f);
        ImGui.TableSetupColumn("Stacks", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Remaining", ImGuiTableColumnFlags.WidthStretch, 0.75f);
        DrawCenteredTableHeader("Icon", "ID", "Name", "Source", "Stacks", "Last remaining");

        foreach (var status in snapshot.Statuses
                     .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(status => status.Id)
                     .ThenBy(status => status.SourceId))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredGameIcon(status.IconId, Math.Clamp(configuration.StatusIconSize, 16.0f, 32.0f), FormatDebugStatusTooltip(status));
            ImGui.TableNextColumn();
            DrawCenteredText(status.Id.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped(status.Name);
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugStatusSource(status.SourceId));
            ImGui.TableNextColumn();
            DrawCenteredText(status.StackCount == 0 ? "-" : status.StackCount.ToString());
            ImGui.TableNextColumn();
            DrawCenteredText(FormatStatusDuration(status, true, true, "-"));
        }

        ImGui.EndTable();
    }

    private void DrawDebugEffectResultSnapshots()
    {
        ImGui.TextColored(LeadUpGoldColor, "EffectResult packet table");
        ImGui.TextDisabled("Memory-only. Shows latest-per-target packet data and a rolling packet history for the current duty.");

        if (!configuration.DebugLogEnabled)
        {
            ImGui.TextDisabled("Enable debug capture to see EffectResult packets.");
            return;
        }

        if (!plugin.DebugEffectResultHookEnabled)
        {
            ImGui.TextColored(WarningColor, "EffectResult hook is not enabled. The signature may be unavailable on this client build.");
            return;
        }

        var allSnapshots = plugin.DebugEffectResultSnapshots;
        var snapshots = allSnapshots
            .Where(MatchesDebugEffectResultSnapshot)
            .ToList();
        var allHistory = plugin.DebugEffectResultHistory;
        var history = allHistory
            .Where(MatchesDebugEffectResultSnapshot)
            .ToList();

        if (allSnapshots.Count == 0 && allHistory.Count == 0)
        {
            ImGui.TextDisabled("No EffectResult packets captured yet. Enter combat in a duty with tracked characters visible.");
            return;
        }

        ImGui.TextDisabled($"Latest per target: {snapshots.Count:N0}/{allSnapshots.Count:N0} visible. Rolling history: {history.Count:N0}/{allHistory.Count:N0} visible.");

        if (snapshots.Count == 0)
        {
            ImGui.TextDisabled("No latest-per-target packet rows match the current filter.");
        }

        foreach (var snapshot in snapshots)
        {
            ImGui.PushID($"DebugEffectResult{snapshot.TargetId}{snapshot.ActorId}{snapshot.TargetName}");
            var label = $"{FormatKnownPlayerName(snapshot.TargetName)} - {snapshot.Statuses.Count:N0}/{snapshot.EffectCount:N0} packet statuses###DebugEffectResultSnapshot";
            if (ImGui.TreeNode(label))
            {
                var shieldText = snapshot.ShieldHp > 0
                    ? $" ({snapshot.ShieldHp:N0})"
                    : string.Empty;
                ImGui.TextDisabled(
                    $"Last packet {snapshot.SeenAtUtc:HH:mm:ss} UTC | Pull {FormatCombatTimer(snapshot.PullElapsedSeconds)} | HP {snapshot.CurrentHp:N0} / {snapshot.MaxHp:N0} | Shield {snapshot.ShieldPercent:N0}%{shieldText} | MP {snapshot.CurrentMp:N0} | Seq {snapshot.RelatedActionSequence} | Replay {FormatDebugBool(snapshot.IsReplay)}");
                ImGui.TextDisabled(
                    $"Target {FormatDebugStatusSource(snapshot.TargetId)} | Actor {FormatDebugStatusSource(snapshot.ActorId)}");

                if (snapshot.Statuses.Count == 0)
                {
                    ImGui.TextDisabled("Packet carried no status entries.");
                }
                else
                {
                    DrawDebugEffectResultStatusTable(snapshot);
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        ImGui.Spacing();
        DrawDebugEffectResultHistoryTable(history, allHistory.Count);
    }

    private void DrawDebugEffectResultHistoryTable(IReadOnlyList<DebugEffectResultSnapshot> history, int totalHistoryCount)
    {
        ImGui.TextColored(LeadUpGoldColor, "EffectResult rolling history");
        if (totalHistoryCount == 0)
        {
            ImGui.TextDisabled("No packet history captured yet.");
            return;
        }

        if (history.Count == 0)
        {
            ImGui.TextDisabled("No packet history rows match the current filter.");
            return;
        }

        if (!ImGui.BeginTable("##DebugEffectResultHistory", 9, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("UTC", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("Pull", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch, 1.35f);
        ImGui.TableSetupColumn("HP", ImGuiTableColumnFlags.WidthStretch, 1.05f);
        ImGui.TableSetupColumn("Shield", ImGuiTableColumnFlags.WidthStretch, 0.75f);
        ImGui.TableSetupColumn("MP", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("Seq", ImGuiTableColumnFlags.WidthStretch, 0.75f);
        ImGui.TableSetupColumn("Replay", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Statuses", ImGuiTableColumnFlags.WidthStretch, 2.2f);
        DrawCenteredTableHeader("UTC", "Pull", "Target", "HP", "Shield", "MP", "Seq", "Replay", "Statuses");

        foreach (var snapshot in history)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredText(snapshot.SeenAtUtc.ToString("HH:mm:ss"));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatCombatTimer(snapshot.PullElapsedSeconds));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(FormatKnownPlayerName(snapshot.TargetName));
            ImGui.TableNextColumn();
            DrawCenteredText($"{snapshot.CurrentHp:N0}/{snapshot.MaxHp:N0}");
            ImGui.TableNextColumn();
            DrawCenteredText(snapshot.ShieldHp > 0 ? $"{snapshot.ShieldPercent:N0}% ({snapshot.ShieldHp:N0})" : $"{snapshot.ShieldPercent:N0}%");
            ImGui.TableNextColumn();
            DrawCenteredText(snapshot.CurrentMp.ToString("N0"));
            ImGui.TableNextColumn();
            DrawCenteredText(snapshot.RelatedActionSequence.ToString());
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugBool(snapshot.IsReplay));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(FormatDebugEffectResultStatusSummary(snapshot));
        }

        ImGui.EndTable();
    }

    private void DrawDebugEffectResultStatusTable(DebugEffectResultSnapshot snapshot)
    {
        if (!ImGui.BeginTable("##DebugEffectResultStatuses", 8, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, Math.Clamp(configuration.StatusIconSize, 16.0f, 32.0f) + 10.0f);
        ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthStretch, 0.45f);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1.7f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Source name", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableSetupColumn("Stacks", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthStretch, 0.75f);
        DrawCenteredTableHeader("Icon", "Index", "ID", "Name", "Source", "Source name", "Stacks", "Duration");

        foreach (var status in snapshot.Statuses)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredGameIcon(status.IconId, Math.Clamp(configuration.StatusIconSize, 16.0f, 32.0f), FormatDebugEffectResultStatusTooltip(status));
            ImGui.TableNextColumn();
            DrawCenteredText(status.EffectIndex.ToString());
            ImGui.TableNextColumn();
            DrawCenteredText(status.EffectId.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped(status.Name);
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugStatusSource(status.SourceActorId));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(FormatKnownPlayerName(status.SourceName));
            ImGui.TableNextColumn();
            DrawCenteredText(status.StackCount == 0 ? "-" : status.StackCount.ToString());
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugEffectResultDuration(status.Duration));
        }

        ImGui.EndTable();
    }

    private void DrawDebugActorControlEvents()
    {
        ImGui.TextColored(LeadUpGoldColor, "ActorControl packet table");
        ImGui.TextDisabled("Memory-only. Shows the latest raw ActorControl events, including death, DoT, HoT, status, tether, and target/control categories when the client exposes them.");

        if (!configuration.DebugLogEnabled)
        {
            ImGui.TextDisabled("Enable debug capture to see ActorControl packets.");
            return;
        }

        if (!plugin.DebugActorControlHookEnabled)
        {
            ImGui.TextColored(WarningColor, "ActorControl hook is not enabled. The signature may be unavailable on this client build.");
            return;
        }

        var allEvents = plugin.DebugActorControlEvents;
        var events = allEvents
            .Where(MatchesDebugActorControlEvent)
            .ToList();
        if (allEvents.Count == 0)
        {
            ImGui.TextDisabled("No ActorControl packets captured yet. Enter combat in a duty with tracked characters visible.");
            return;
        }

        ImGui.TextDisabled($"{events.Count:N0}/{allEvents.Count:N0} control event rows visible.");
        if (events.Count == 0)
        {
            ImGui.TextDisabled("No ActorControl rows match the current filter.");
            return;
        }

        if (!ImGui.BeginTable("##DebugActorControlEvents", 16, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("UTC", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("Pull", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Entity", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("p1", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("p2", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("p3", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("p4", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("p5", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("p6", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("p7", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("p8", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("Param9", ImGuiTableColumnFlags.WidthStretch, 0.55f);
        ImGui.TableSetupColumn("Entity ID", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Target ID", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        DrawCenteredTableHeader("UTC", "Pull", "Entity", "Category", "p1", "p2", "p3", "p4", "p5", "p6", "p7", "p8", "Target", "Param9", "Entity ID", "Target ID");

        foreach (var entry in events)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredText(entry.SeenAtUtc.ToString("HH:mm:ss"));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatCombatTimer(entry.PullElapsedSeconds));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(FormatKnownPlayerName(entry.EntityName));
            ImGui.TableNextColumn();
            DrawCenteredText($"{entry.CategoryName} ({entry.Category})");
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugActorControlParam(entry.Param1));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugActorControlParam(entry.Param2));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugActorControlParam(entry.Param3));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugActorControlParam(entry.Param4));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugActorControlParam(entry.Param5));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugActorControlParam(entry.Param6));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugActorControlParam(entry.Param7));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugActorControlParam(entry.Param8));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(FormatKnownPlayerName(entry.TargetName));
            ImGui.TableNextColumn();
            DrawCenteredText(entry.Param9.ToString());
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugStatusSource(entry.EntityId));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatDebugActorControlTarget(entry.TargetId));
        }

        ImGui.EndTable();
    }

    private void DrawDebugLog()
    {
        var allEntries = plugin.DebugLogEntries;
        var entries = allEntries
            .Where(MatchesDebugLogEntry)
            .ToList();
        ImGui.TextColored(LeadUpGoldColor, "Internal capture log");
        ImGui.TextDisabled($"{entries.Count:N0}/{allEntries.Count:N0} debug rows visible. The newest 1,000 rows are retained.");
        if (allEntries.Count == 0)
        {
            ImGui.TextDisabled("No debug entries captured.");
            return;
        }

        if (entries.Count == 0)
        {
            ImGui.TextDisabled("No debug log rows match the current filter.");
            return;
        }

        if (!ImGui.BeginTable("##BetterDeathsDebugLog", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("UTC", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Pull", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch, 3.0f);
        DrawCenteredTableHeader("UTC", "Pull", "Message");

        foreach (var entry in entries.OrderBy(entry => entry.SeenAtUtc))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredText(entry.SeenAtUtc.ToString("HH:mm:ss"));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatCombatTimer(entry.PullElapsedSeconds));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(RedactKnownPlayerNamesInText(entry.Message));
        }

        ImGui.EndTable();
    }

    private static string FormatDebugStatusSource(uint sourceId)
    {
        return sourceId == 0
            ? "-"
            : $"0x{sourceId:X8}";
    }

    private static string FormatDebugActorControlTarget(ulong targetId)
    {
        return targetId == 0
            ? "-"
            : $"0x{targetId:X16}";
    }

    private static string FormatDebugActorControlParam(uint value)
    {
        return value == 0
            ? "-"
            : value.ToString();
    }

    private static string FormatDebugBool(bool value)
    {
        return value ? "yes" : "no";
    }

    private static string FormatDebugAddress(nint address)
    {
        return address == 0 ? "-" : $"0x{(long)address:X}";
    }

    private static string FormatDebugStatusTooltip(StatusSnapshot status)
    {
        return $"{status.Name} ({status.Id})\nSource: {FormatDebugStatusSource(status.SourceId)}\nStacks: {(status.StackCount == 0 ? "-" : status.StackCount.ToString())}\nRemaining: {FormatStatusDuration(status, true, true, "-")}";
    }

    private string FormatDebugEffectResultStatusTooltip(DebugEffectResultStatus status)
    {
        return $"{status.Name} ({status.EffectId})\nEffect index: {status.EffectIndex}\nSource: {FormatKnownPlayerName(status.SourceName)} ({FormatDebugStatusSource(status.SourceActorId)})\nStacks: {(status.StackCount == 0 ? "-" : status.StackCount.ToString())}\nDuration: {FormatDebugEffectResultDuration(status.Duration)}";
    }

    private string FormatDebugEffectResultStatusSummary(DebugEffectResultSnapshot snapshot)
    {
        if (snapshot.Statuses.Count == 0)
        {
            return "-";
        }

        return string.Join("; ", snapshot.Statuses.Select(status =>
            $"{status.Name} ({status.EffectId}) {FormatDebugEffectResultDuration(status.Duration)} from {FormatKnownPlayerName(status.SourceName)}"));
    }

    private static string FormatDebugEffectResultDuration(float duration)
    {
        return duration < 0.0f
            ? "remove"
            : duration <= 0.0f
                ? "-"
                : $"{duration:0.0}s";
    }

    private static string FormatByteSize(long bytes)
    {
        const double oneKb = 1024.0;
        const double oneMb = oneKb * 1024.0;
        if (bytes >= oneMb)
        {
            return $"{bytes / oneMb:0.0} MB";
        }

        return bytes >= oneKb
            ? $"{bytes / oneKb:0.0} KB"
            : $"{bytes:N0} B";
    }

    private DeathSelectionTarget? ResolveDeathSelectionTarget(long deathSeenAtTicks, uint memberKeyHash)
    {
        if (plugin.CurrentPullClosedForReview)
        {
            foreach (var summary in plugin.RecordedPulls.AsEnumerable().Reverse())
            {
                if (summary.PullNumber != plugin.CurrentPullRecordedPullNumber)
                {
                    continue;
                }

                var detail = plugin.GetRecordedPullDetails(summary);
                if (detail is not null && ContainsDeath(detail.Deaths, deathSeenAtTicks, memberKeyHash))
                {
                    return BuildRecordedDeathSelectionTarget(deathSeenAtTicks, memberKeyHash, summary);
                }
            }
        }
        else if (ContainsDeath(plugin.CurrentDeaths, deathSeenAtTicks, memberKeyHash))
        {
            return new DeathSelectionTarget(deathSeenAtTicks, memberKeyHash, DeathSelectionSource.Current, null, null, null);
        }

        foreach (var summary in plugin.RecordedPulls.AsEnumerable().Reverse())
        {
            var detail = plugin.GetRecordedPullDetails(summary);
            if (detail is not null && ContainsDeath(detail.Deaths, deathSeenAtTicks, memberKeyHash))
            {
                return BuildRecordedDeathSelectionTarget(deathSeenAtTicks, memberKeyHash, summary);
            }
        }

        return ShouldShowExamplePage() && ContainsDeath(GetExampleDeaths(), deathSeenAtTicks, memberKeyHash)
            ? new DeathSelectionTarget(deathSeenAtTicks, memberKeyHash, DeathSelectionSource.Example, null, null, null)
            : null;
    }

    private void EnsureDeathSelectionTargetVisible(DeathSelectionTarget target)
    {
        if (target.Source != DeathSelectionSource.Recorded ||
            target.RecordedPullTerritoryId is not { } territoryId ||
            recordedPullDutyFilter == AllRecordedPullDuties ||
            recordedPullDutyFilter == territoryId)
        {
            return;
        }

        recordedPullDutyFilter = AllRecordedPullDuties;
    }

    private static DeathSelectionTarget BuildRecordedDeathSelectionTarget(
        long deathSeenAtTicks,
        uint memberKeyHash,
        RecordedPullSummary recordedPull)
    {
        return new DeathSelectionTarget(
            deathSeenAtTicks,
            memberKeyHash,
            DeathSelectionSource.Recorded,
            recordedPull.PullNumber,
            recordedPull.CapturedAtUtc.Ticks,
            recordedPull.TerritoryId);
    }

    private bool HasPendingDeathSelection(
        IReadOnlyList<PartyDeathRecord> deaths,
        DeathSelectionSource source,
        RecordedPullSummary? recordedPull = null)
    {
        return pendingDeathSelection is { } target &&
            DeathSelectionSourceMatches(target, source, recordedPull) &&
            ContainsDeath(deaths, target.DeathSeenAtTicks, target.MemberKeyHash);
    }

    private bool IsPendingDeathSelection(
        PartyDeathRecord death,
        DeathSelectionSource source,
        RecordedPullSummary? recordedPull = null)
    {
        return pendingDeathSelection is { } target &&
            DeathSelectionSourceMatches(target, source, recordedPull) &&
            IsDeathTarget(death, target.DeathSeenAtTicks, target.MemberKeyHash);
    }

    private static bool DeathSelectionSourceMatches(
        DeathSelectionTarget target,
        DeathSelectionSource source,
        RecordedPullSummary? recordedPull)
    {
        if (target.Source != source)
        {
            return false;
        }

        return source != DeathSelectionSource.Recorded ||
            (recordedPull is not null &&
                target.RecordedPullNumber == recordedPull.PullNumber &&
                target.RecordedPullCapturedAtTicks == recordedPull.CapturedAtUtc.Ticks);
    }

    private static bool ContainsDeath(IReadOnlyList<PartyDeathRecord> deaths, long deathSeenAtTicks, uint memberKeyHash)
    {
        return deaths.Any(death => IsDeathTarget(death, deathSeenAtTicks, memberKeyHash));
    }

    private static bool IsDeathTarget(PartyDeathRecord death, long deathSeenAtTicks, uint memberKeyHash)
    {
        return death.SeenAtUtc.Ticks == deathSeenAtTicks &&
            Plugin.GetMemberKeyHash(death.MemberKey) == memberKeyHash;
    }

    private enum DeathSelectionSource
    {
        Current,
        Recorded,
        Example,
    }

    private enum MainPage
    {
        Review,
        Replay,
        Example,
        Customize,
        Data,
        Feedback,
        Updates,
        Debug,
    }

    private enum DeathDetailPage
    {
        Summary,
        WhatIf,
    }

    private sealed record RecordedPullDutyOption(uint TerritoryId, string TerritoryName, int PullCount);

    private sealed record DataPageSnapshot(
        int SavedPullCount,
        int MaxRecordedPulls,
        long RecordedPullStorageSizeBytes,
        int RecordedPullDetailFileCount,
        long DebugCaptureFileSizeBytes,
        long DebugCaptureMaxFileSizeBytes,
        long LocalDataDirectorySizeBytes,
        string LocalDataDirectoryPath)
    {
        public static readonly DataPageSnapshot Empty = new(0, 0, 0, 0, 0, 0, 0, string.Empty);
    }

    private sealed record DeathSelectionTarget(
        long DeathSeenAtTicks,
        uint MemberKeyHash,
        DeathSelectionSource Source,
        long? RecordedPullNumber,
        long? RecordedPullCapturedAtTicks,
        uint? RecordedPullTerritoryId);

    private void DrawNotesTab()
    {
        ImGui.TextUnformatted("What Better Deaths adds");
        DrawWrappedBullet("Pull-based death review for wipes, recommences, resets, and saved pull history.");
        DrawWrappedBullet("Current Pull and the optional widget show live death order while combat is happening.");
        DrawWrappedBullet("Timeline-first recap shows who died, when they died, and the fatal events before opening player details.");
        DrawWrappedBullet("Summary details tie the fatal event to source, action, amount, damage type, HP plus shields, overkill context, and enemy HP at death.");
        DrawWrappedBullet("10-second lead-up shows HP and shield movement, sources, heals, hits, status timers, and captured events before KO.");
        DrawWrappedBullet("Mitigation review groups player mitigation, shields, debuffs, target mitigation, expired context, and calculated mitigation total.");
        DrawWrappedBullet("What-if mitigation lets you select available tools and see how the damage result would have changed.");
        DrawWrappedBullet("Themes, widget opacity controls, optional scrollbars, and name redaction help shape the review view around how you share and play.");
        DrawWrappedBullet("The Data tab explains what is saved locally and makes clear that Better Deaths does not upload your data.");
        DrawWrappedBullet("Chat-posted death summaries can include clickable recap links for other Better Deaths users with the same captured pull.");
        ImGui.Separator();
        ImGui.TextWrapped("The goal is to make wipe review fast: see who died, see why, see what was active, and keep the pull context intact between attempts.");
        ImGui.Separator();
        DrawCreatorNote();
        DrawReviewPaneBottomPadding();
    }

    private void DrawDataPage()
    {
        var data = GetDataPageSnapshot();

        ImGui.TextColored(LeadUpGoldColor, "Privacy & Data");
        ImGui.TextWrapped("Better Deaths does not upload your data. It does not have any upload functions, telemetry, analytics, webhooks, or hidden network reporting built into the plugin in any way, shape, or form.");
        ImGui.TextWrapped("That is intentional, and it will remain that way.");

        ImGui.Separator();
        ImGui.TextColored(LeadUpGoldColor, "Local data");
        DrawWrappedBullet("Recorded pulls are saved locally so you can review pulls after wipes, resets, reloads, or plugin updates.");
        DrawWrappedBullet("Saved pull data can include player names, jobs, duty names, death timing, replay positions, waymarks, player and enemy HP, shields, damage events, actions, statuses, and mitigation context.");
        DrawWrappedBullet("Name Redaction helps with screenshots and shared display, but local saved pull files may still contain the original captured names.");
        DrawWrappedBullet("Debug capture is local and optional. It can contain raw troubleshooting data, so leave it off unless you are testing or debugging.");

        ImGui.Spacing();
        DrawDataStat("Saved pulls", $"{data.SavedPullCount:N0} / {data.MaxRecordedPulls:N0}");
        DrawDataStat("Recorded pull files", $"{FormatByteSize(data.RecordedPullStorageSizeBytes)} across {data.RecordedPullDetailFileCount:N0} detail file(s)");
        DrawDataStat("Debug file", $"{FormatByteSize(data.DebugCaptureFileSizeBytes)} / {FormatByteSize(data.DebugCaptureMaxFileSizeBytes)}");
        DrawDataStat("Total local folder", FormatByteSize(data.LocalDataDirectorySizeBytes));
        DrawDataStat("Local folder", data.LocalDataDirectoryPath);

        ImGui.Separator();
        ImGui.TextColored(LeadUpGoldColor, "What Better Deaths reads");
        DrawWrappedBullet("While capture is enabled in supported duties, Better Deaths reads combat, party, HP, shield, status, action, death, and timing data that your client can already see.");
        DrawWrappedBullet("Better Deaths reads its own local configuration and recorded pull files so your settings and saved recaps persist.");
        DrawWrappedBullet("Better Deaths listens for Better Deaths recap chat posts so clickable recap links can open a matching local pull review.");

        ImGui.Separator();
        ImGui.TextColored(LeadUpGoldColor, "Sharing");
        DrawWrappedBullet("Chat posting is opt-in. If you post recap information to chat, that information is shared through the selected in-game chat channel.");
        DrawWrappedBullet("Recap links are not web links and do not send data to a Better Deaths server. They are local Dalamud chat payloads used to find a matching recap.");
        DrawWrappedBullet("The Feedback tab only opens the Punish Discord invite in your browser after you confirm. Better Deaths does not attach or upload plugin data to it.");
        DrawReviewPaneBottomPadding();
    }

    private void DrawFeedbackPage()
    {
        ImGui.TextColored(LeadUpGoldColor, "Feedback");
        ImGui.TextWrapped("Feedback is now taken on the Punish Discord server.");
        ImGui.TextWrapped("The button opens the Punish Discord invite in your browser after you confirm.");

        ImGui.Spacing();
        DrawDataStat("URL", FeedbackDiscordUrl);

        ImGui.Spacing();
        var discordButtonLabel = $"{FontAwesomeIcon.Comments.ToIconString()} Open Punish Discord";
        if (DrawThemedActionButton(discordButtonLabel, "OpenBetterDeathsFeedbackDiscord"))
        {
            ImGui.OpenPopup(FeedbackConfirmPopupId);
        }

        DrawFeedbackConfirmationPopup();
        DrawReviewPaneBottomPadding();
    }

    private static void DrawFeedbackConfirmationPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(460.0f, 0.0f), ImGuiCond.Appearing);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12.0f, 10.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8.0f, 8.0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, ModernPopupBgColor);
        ImGui.PushStyleColor(ImGuiCol.Border, ModernPanelBorderColor);
        if (!ImGui.BeginPopup(FeedbackConfirmPopupId, ImGuiWindowFlags.NoMove))
        {
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);
            return;
        }

        ImGui.TextColored(LeadUpGoldColor, "Open Punish Discord?");
        ImGui.TextWrapped("This will open the Punish Discord invite in your browser.");
        ImGui.TextWrapped(FeedbackDiscordUrl);
        ImGui.Separator();

        if (DrawThemedActionButton("OK", "ConfirmOpenBetterDeathsFeedbackDiscord", 92.0f))
        {
            OpenFeedbackDiscordUrl();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (DrawThemedActionButton("Cancel", "CancelOpenBetterDeathsFeedbackDiscord", 92.0f))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(2);
    }

    private static void DrawKofiConfirmationPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(460.0f, 0.0f), ImGuiCond.Appearing);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12.0f, 10.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8.0f, 8.0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, ModernPopupBgColor);
        ImGui.PushStyleColor(ImGuiCol.Border, ModernPanelBorderColor);
        if (!ImGui.BeginPopup(KofiConfirmPopupId, ImGuiWindowFlags.NoMove))
        {
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);
            return;
        }

        ImGui.TextColored(LeadUpGoldColor, "Open Ko-fi?");
        ImGui.TextWrapped("This will open a Ko-fi link in your browser.");
        ImGui.TextWrapped(KofiUrl);
        ImGui.Separator();

        if (DrawThemedActionButton("OK", "ConfirmOpenBetterDeathsKofi", 92.0f))
        {
            OpenKofiUrl();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (DrawThemedActionButton("Cancel", "CancelOpenBetterDeathsKofi", 92.0f))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(2);
    }

    private static void OpenFeedbackDiscordUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = FeedbackDiscordUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not open Better Deaths feedback Discord invite.");
            Plugin.ChatGui.Print($"[Better Deaths] Could not open the Punish Discord invite. URL: {FeedbackDiscordUrl}");
        }
    }

    private static void OpenKofiUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = KofiUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not open Better Deaths Ko-fi link.");
            Plugin.ChatGui.Print($"[Better Deaths] Could not open Ko-fi. URL: {KofiUrl}");
        }
    }

    private DataPageSnapshot GetDataPageSnapshot()
    {
        var now = DateTime.UtcNow;
        if ((now - dataPageSnapshotRefreshedAtUtc).TotalSeconds < 1.0)
        {
            return dataPageSnapshot;
        }

        dataPageSnapshot = new DataPageSnapshot(
            plugin.RecordedPulls.Count,
            configuration.MaxRecordedPulls,
            plugin.RecordedPullStorageSizeBytes,
            plugin.RecordedPullDetailFileCount,
            plugin.DebugCaptureFileSizeBytes,
            plugin.DebugCaptureMaxFileSizeBytes,
            plugin.LocalDataDirectorySizeBytes,
            plugin.LocalDataDirectoryPath);
        dataPageSnapshotRefreshedAtUtc = now;
        return dataPageSnapshot;
    }

    private static void DrawDataStat(string label, string value)
    {
        var startX = ImGui.GetCursorPosX();
        var labelText = $"{label}:";
        ImGui.TextColored(LeadUpGoldColor, labelText);
        var style = ImGui.GetStyle();
        var remainingWidth = ImGui.GetContentRegionAvail().X;
        var minimumInlineValueWidth = MathF.Min(180.0f, MathF.Max(80.0f, ImGui.CalcTextSize(value).X));
        if (remainingWidth >= style.ItemSpacing.X + minimumInlineValueWidth)
        {
            ImGui.SameLine();
        }
        else
        {
            ImGui.SetCursorPosX(startX + ReviewPaneContentIndent);
        }

        ImGui.TextWrapped(value);
    }

    private static void DrawWrappedBullet(string text)
    {
        DrawWrappedBullet(text, null);
    }

    private static void DrawWrappedBullet(string text, Vector4? color)
    {
        if (color is { } textColor)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        }

        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextWrapped(text);
        if (color is not null)
        {
            ImGui.PopStyleColor();
        }
    }

    private static void DrawChangelogTab()
    {
        ImGui.TextUnformatted("v0.1.0.249");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Reduced stale untargetable boss clutter in new Death Replay captures.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.248");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Fixed Death Replay facing indicators and rotation-based mechanic draws.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.247");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Improved Death Replay mechanic cleanup and moved death markers onto the replay timeline.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.246");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Added DMU P4 and P5 replay draw support with cleaner mechanic cleanup.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.245");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Added new soda-inspired themes: Dr Pepper, Sprite, Mountain Dew, Coke, Fanta, Ginger Ale, and Pepsi.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.243");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Moved Death Replay into its own Replay tab with full-pull replay review for saved death pulls.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.242");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Improved boss-side mitigation/debuff display clarity.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.241");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Improved multi-boss mitigation/debuff context in the 10s lead-up.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.240");
        ImGui.TextDisabled("Stable update.");
        DrawHighlightedChangelogBullet("Added N/E/S/W labels to Death Replay for clearer arena orientation.");
        DrawHighlightedChangelogBullet("Added facing indicators to party member replay markers.");
        DrawHighlightedChangelogBullet("Improved Death Replay position syncing around captured action effects going forward. This will reflect in facing indicators as well");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.235");
        ImGui.TextDisabled("Stable update.");
        DrawWrappedBullet("Fixed current pull grouping colors.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.234");
        ImGui.TextDisabled("Testing update.");
        DrawWrappedBullet("Fixed current pull grouping colors.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.233");
        ImGui.TextDisabled("Stable update.");
        DrawWrappedBullet("error in grouping pulls");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.232");
        ImGui.TextDisabled("Stable update.");
        DrawHighlightedChangelogBullet("Added same-duty pull grouping to the Pulls list, with colored indicators across collapsed, expanded, and stacked views.");
        DrawHighlightedChangelogBullet("Added customizable pull group colors in Customize.");
        DrawHighlightedChangelogBullet("Improved theme readability across all built-in themes, including button text, status text, warning/gold text, and HP/shield labels.");
        DrawWrappedBullet("Added saved pull group data so recorded pulls keep their duty-instance color after restart.");
        DrawWrappedBullet("Added extra padding to short-width expanded pull cells so the content does not sit against the window edge.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.231");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Added same-duty pull grouping to the Pulls list, with colored indicators across collapsed, expanded, and stacked views.");
        DrawHighlightedChangelogBullet("Added customizable pull group colors in Customize.");
        DrawHighlightedChangelogBullet("Improved theme readability across all built-in themes, including button text, status text, warning/gold text, and HP/shield labels.");
        DrawWrappedBullet("Added saved pull group data so recorded pulls keep their duty-instance color after restart.");
        DrawWrappedBullet("Added extra padding to short-width expanded pull cells so the content does not sit against the window edge.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.230");
        ImGui.TextDisabled("Stable update.");
        DrawHighlightedChangelogBullet("Improved Death Replay timing, waymark capture, and mechanic cleanup reliability.");
        DrawHighlightedChangelogBullet("Death Replay now captures and shows placed world markers.");
        DrawHighlightedChangelogBullet("Death Replay now pauses when you scrub the playback timeline.");
        DrawHighlightedChangelogBullet("Added replay display toggles for names, classes, and HP, with cleaner hover behavior.");
        DrawHighlightedChangelogBullet("Removed replay resolve fade effects so mechanics transition more cleanly.");
        DrawHighlightedChangelogBullet("Reorganized Customize for less clutter.");
        DrawWrappedBullet("Added a world marker opacity slider under Death Replay.");
        DrawWrappedBullet("Added local-only channel choices for automatic Death Links, including System Message.");
        DrawWrappedBullet("Cleaned up death-chat HP wording and Death Link / Pull Link labels.");
        DrawWrappedBullet("Added a compact Test button beside the recap popup toggle.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.229");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Improved Death Replay timing, waymark capture, and mechanic cleanup reliability.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.226");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Death Replay now pauses when you scrub the playback timeline.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.225");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Death Replay now captures and shows placed world markers.");
        DrawWrappedBullet("Added a world marker opacity slider for Death Replay.");
        DrawWrappedBullet("Added local-channel choices for automatic Death Links.");
        DrawHighlightedChangelogBullet("Reorganized Customize for less clutter.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.224");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Added System Message as a chat posting option.");
        DrawWrappedBullet("Recap links now post as system messages instead of echo messages.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.223");
        ImGui.TextDisabled("Testing update.");
        DrawHighlightedChangelogBullet("Removed replay resolve fade effects so mechanics transition more cleanly.");
        DrawWrappedBullet("Cleaned up death-chat HP wording and Death Link / Pull Link labels.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.222");
        ImGui.TextDisabled("Stable update.");
        DrawHighlightedChangelogBullet("Reduced replay label clutter with compact mechanic indicators and hover/focus details.");
        DrawWrappedBullet("Cleaned up active mitigation and What-if review tables for easier scanning.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.220");
        ImGui.TextDisabled("Stable update.");
        DrawHighlightedChangelogBullet("Simplified What-if information.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.219");
        ImGui.TextDisabled("Stable update.");
        DrawHighlightedChangelogBullet("Removed duplicate mitigation tab, apologies.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.218");
        ImGui.TextDisabled("Stable update.");
        DrawHighlightedChangelogBullet("Stacked review sections can now be resized by dragging the dividers between Pulls, Death Timeline, and Selected Death.");
        DrawHighlightedChangelogBullet("The 10s lead-up now hands off mouse-wheel scrolling to the timeline when it reaches the top or bottom.");
        DrawHighlightedChangelogBullet("Death Replay now hides stale untargetable enemies after they sit still for 15 seconds.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.209");
        ImGui.TextDisabled("Stable update.");
        DrawHighlightedChangelogBullet("Added Walled detection for environment-source deaths such as death walls and jump-offs.");
        DrawHighlightedChangelogBullet("Added broader mitigation tracking for secondary and granted effects, including Desperate Measures and other shield, regen, and healing-received effects.");
        DrawHighlightedChangelogBullet("Moved the 10-second lead-up into the selected death row.");
        DrawHighlightedChangelogBullet("The 10s lead-up container can now be resized by dragging its bottom edge.");
        DrawWrappedBullet("Summary now keeps active mitigations/debuffs at death and Expired Mits directly with the death context.");
        DrawWrappedBullet("Added a Customize option to hide the Example tab.");
        DrawHighlightedChangelogBullet("Forsaken Towers should draw more consistently (hopefully).");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.208");
        ImGui.TextDisabled("Stable update.");
        DrawWrappedBullet("Updated the Updates creator note.");
        DrawWrappedBullet("Removed the old acknowledgement popup and its reopen button.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.207");
        ImGui.TextDisabled("Stable update.");
        DrawHighlightedChangelogBullet("Added environmental death context to help identify likely falls, death walls, and out-of-bounds KOs.");
        DrawHighlightedChangelogBullet("Added DMU arena-boundary detection so deaths near or outside the known arena edge can be flagged as likely death walls when no lethal event was captured.");
        DrawHighlightedChangelogBullet("Added a Pulls search bar so recorded pulls can be filtered by player name.");
        DrawWrappedBullet("Updated Review legend wording so non-hit KOs with environmental context are described correctly.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.206");
        ImGui.TextDisabled("Stable update.");
        DrawHighlightedChangelogBullet("Fixed death recap popups opening off-screen on smaller or lower-resolution displays.");
        DrawHighlightedChangelogBullet("Extended replay lead-up time from 20 seconds to 30 seconds before death.");
        DrawHighlightedChangelogBullet("Fixed replay arena orientation so player positions no longer appear flipped east/west.");
        DrawHighlightedChangelogBullet("Fixed tether jumping in replays by sampling player positions faster when live tethers are active.");
        DrawHighlightedChangelogBullet("Added more DMU replay draw support for P2 and P3 mechanics, including Path of Light towers, Forsaken actions, P3 status markers, cast predictions, donuts, cones, lines, towers, and resolved mechanic timing.");
        DrawHighlightedChangelogBullet("Improved replay marker labels so known mechanics use their real mechanic names and colors, while unknown markers still fall back to generic labels.");
        DrawHighlightedChangelogBullet("Improved replay marker timeline behavior so the replay shows the marker state that was active at the selected timestamp instead of only the newest marker.");
        DrawHighlightedChangelogBullet("Reworked Review window tabs so the main tabs and selected-death tabs wrap or compact cleanly instead of running off-screen.");
        DrawHighlightedChangelogBullet("Redesigned the Pulls display with cleaner pull cells, small-window number cells, drawer controls, and better trash-button placement.");
        DrawWrappedBullet("Reordered the What-if mitigation view to show ability names first, making the list more compact and easier to scan.");
        DrawWrappedBullet("Made both the real recap popup and test recap popup movable by dragging from the popup button, while keeping normal click behavior intact.");
        DrawWrappedBullet("Improved replay movement trails so they show captured breadcrumb points and avoid misleading long straight-line jumps across missing samples.");
        DrawWrappedBullet("Improved Forsaken replay handling so marker visibility follows the active resolve timing more reliably and uses cached data for better performance.");
        DrawWrappedBullet("Added stacked marker badges for closely timed mechanics, with capped display so the replay does not become unreadable.");
        DrawWrappedBullet("Improved resolved marker fading so one marker can resolve while another marker on the same player remains visible.");
        DrawWrappedBullet("Fixed mitigation tab layout so mitigation type text no longer overflows into the ability column.");
        DrawWrappedBullet("Improved replay performance by caching actor position tracks and Forsaken marker grouping instead of rebuilding them every frame.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.202");
        ImGui.TextDisabled("Stable update.");
        DrawWrappedBullet("Fixed mitigation type labels overflowing into the ability column.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.201");
        ImGui.TextDisabled("Stable update.");
        DrawWrappedBullet("Fixed tether jumping in replays.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.200");
        ImGui.TextDisabled("Stable update.");
        DrawBreathingGoldBullet("Recap popup buttons can now be dragged directly to move the popup.");
        DrawWrappedBullet("Added a Test recap popup preview so you can place the popup without needing to die.");
        DrawWrappedBullet("P4 replay labels now follow the next resolving debuff and can show paired debuffs.");
        DrawWrappedBullet("Fatal sequence now starts collapsed.");
        DrawWrappedBullet("Cleaned up focused Summary spacing.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.199");
        ImGui.TextDisabled("Stable update.");
        DrawWrappedBullet("P4 replay labels now follow the next resolving debuff and can show paired debuffs.");
        DrawWrappedBullet("Fatal sequence now starts collapsed.");
        DrawWrappedBullet("Cleaned up focused Summary spacing.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.198");
        ImGui.TextDisabled("Stable update.");
        DrawWrappedBullet("Cleaned up P4 Grand Cross Debuffs layout.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.197");
        ImGui.TextDisabled("Stable update.");
        DrawWrappedBullet("Cleaned up P4 Grand Cross Debuffs display.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.196");
        ImGui.TextDisabled("Stable update.");
        DrawBreathingGoldBullet("Death Replay beta now captures DMU P4 real/fake boss tells and labels related assignments.");
        DrawBreathingGoldBullet("Added P4 Grand Cross Debuffs to Summary.");
        DrawWrappedBullet("Cleaned up focused mitigation layout.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.193");
        ImGui.TextDisabled("Stable update.");
        DrawLargeHighlightedChangelogCallout("We are now on the Punish repo, no change or edits will be required on the user's end. The transition will be seamless.");
        DrawBreathingGoldBullet("Death Replay beta is available with positions, overhead markers, zooming, panning, and DMU replay improvements.");
        DrawBreathingGoldBullet("Added Focused and Detailed review modes, plus a custom theme builder.");
        DrawWrappedBullet("Death timeline width can now be resized.");
        DrawWrappedBullet("Plugin distribution support was cleaned up.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.173");
        ImGui.TextDisabled("Stable update.");
        DrawBreathingGoldBullet("Added a Feedback tab with a confirmation before opening the feedback link.");
        DrawWrappedBullet("Replay now stays visible as a disabled Coming soon tab while replay work continues.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.172");
        ImGui.TextDisabled("Testing update.");
        DrawBreathingGoldBullet("Added an early Death Replay tab with captured player and enemy positions around the selected death.");
        DrawWrappedBullet("Replay data starts with new pulls; older saved pulls will not have position replay data.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.171");
        ImGui.TextDisabled("Stable update.");
        DrawBreathingGoldBullet("10s lead-up HP bars now show damage loss and healing growth more clearly.");
        DrawWrappedBullet("10s lead-up Timeline now shows the event source.");
        DrawWrappedBullet("Help markers were cleaned up visually.");
        DrawWrappedBullet("The in-plugin information page was updated to match the current Better Deaths features.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.167");
        ImGui.TextDisabled("Stable update.");
        DrawBreathingGoldBullet("Death HP capture now uses the hit's own snapshot instead of searching older HP rows.");
        DrawBreathingGoldBullet("Overkill should now stay tied to the actual fatal hit instead of stale HP or shield data.");
        DrawWrappedBullet("Player-side mitigation timers now show in the 10s lead-up when Timers is enabled.");
        DrawWrappedBullet("Source and target statuses are captured closer to the hit, so mitigation and debuff timing should be more reliable.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.166");
        ImGui.TextDisabled("Stable update.");
        DrawBreathingGoldBullet("Fixed a lethal-hit edge case where a stale HP snapshot could make the 10s lead-up look like the player still had HP after they should have died.");
        DrawWrappedBullet("Summary overkill now stays tied to the selected fatal hit instead of falling onto a later zero-HP row.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.165");
        ImGui.TextDisabled("Stable update.");
        DrawBreathingGoldBullet("Added a What-if mitigation review, so you can check how extra mitigation would have changed a death.");
        DrawBreathingGoldBullet("Reworked the 10s lead-up so hits, HP, shields, and heals line up more cleanly.");
        DrawBreathingGoldBullet("Added more themes, with better readability across the app.");
        DrawBreathingGoldBullet("Added optional visible scrollbars for the scroll wheel-challenged.");
        DrawWrappedBullet("Added a Timers switch for the 10s lead-up Mits/Debuffs column.");
        DrawWrappedBullet("Added available mitigation review, including chat options for sharing it.");
        DrawWrappedBullet("Cleaned up 10s lead-up tables so Timeline and Events are easier to read.");
        DrawWrappedBullet("Cleaned up chat buttons and dropdown styling.");
        DrawWrappedBullet("Grouped death follow-up settings together.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.151");
        ImGui.TextDisabled("Stable update.");
        DrawWrappedBullet("Improved enemy HP at death readability.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.150");
        ImGui.TextDisabled("Stable update.");
        DrawBreathingGoldBullet("Added enemy HP at death to fatal event details.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.149");
        ImGui.TextDisabled("Testing update.");
        DrawBreathingGoldBullet("Fatal hit capture has been improved so death data should line up more cleanly with the 10s lead-up.");
        DrawBreathingGoldBullet("Added Moonlit, Wisteria, and Blush themes.");
        DrawWrappedBullet("Added a Timeline / Events toggle in the 10s lead-up.");
        DrawWrappedBullet("Added physical and magic icons to hit information.");
        DrawWrappedBullet("Auto-attacks now show as Auto instead of Action or Attack.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.136");
        ImGui.TextDisabled("Privacy and data.");
        DrawBreathingGoldBullet("Added a Data page with privacy and local storage information.");
        DrawWrappedBullet("Better Deaths now shows local saved pull storage, debug file size, and local data folder size.");
        DrawWrappedBullet("Added clearer wording that Better Deaths does not upload data and has no upload functions built into the plugin.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.135");
        ImGui.TextDisabled("Stable update.");
        DrawBreathingGoldBullet("Added more theme options.");
        DrawBreathingGoldBullet("Startup impact was causing issues, and we've refactored how the local data gets read by the plugin on launch. Loads and updates should now be smooth after the new changes go into effect.");
        DrawWrappedBullet("Theme choices are now split into dark and light sections.");
        DrawWrappedBullet("Theme highlights and table rows are easier to read across themes.");
        DrawWrappedBullet("Cleaned up settings buttons.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.132");
        ImGui.TextDisabled("Testing update.");
        DrawBreathingGoldBullet("Improved fatal event selection for multi-hit deaths.");
        DrawBreathingGoldBullet("Added a variety of theme options.");
        DrawWrappedBullet("Fixed the clock icon in lead-up table headers.");
        DrawWrappedBullet("Improved widget preview and recap popup theming.");
        DrawWrappedBullet("Improved theme contrast and window spacing.");
        DrawWrappedBullet("Cleaned up window spacing and header text.");
        DrawWrappedBullet("Cleaned up settings and review table layout.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.123");
        ImGui.TextDisabled("Data table cleanup.");
        DrawWrappedBullet("Data tables were refined and cleaned.");
        DrawWrappedBullet("Adjusted the captured hits/events columns so Source, Action, Amount, HP, and Mits/Debuffs have better spacing.");
        DrawWrappedBullet("Fixed download count metadata so installer counts can update from the feed instead of being stuck inside the package.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.121");
        ImGui.TextDisabled("Settings privacy.");
        DrawWrappedBullet("Added a name redaction option in settings");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.118");
        ImGui.TextDisabled("Testing polish.");
        DrawWrappedBullet("Widget Mits/Debuffs icons now fit the available space before showing +x.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.117");
        ImGui.TextDisabled("Testing widget polish.");
        DrawBreathingGoldBullet("Current pull widget text is indented while the table stays full width.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.116");
        ImGui.TextDisabled("Widget adjustments.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.115");
        ImGui.TextDisabled("Testing UI polish.");
        DrawWrappedBullet("10s lead-up timer hover now shows the exact timer.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.114");
        ImGui.TextDisabled("Lead-up cleanup and resource pass.");
        DrawBreathingGoldBullet("10 second HP history shows fewer duplicate-looking rows.");
        DrawWrappedBullet("Removed extra lead-up explanation text.");
        DrawWrappedBullet("HP history mouseovers now only show HP plus shield over max HP.");
        DrawWrappedBullet("Mitigation and debuff icons in HP history now wrap at 4 per line.");
        DrawWrappedBullet("Fixed unresolved issues in the 10s lead-up.");
        DrawWrappedBullet("Runtime capture paths now do less avoidable work without changing tracked-player capture.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.112");
        ImGui.TextDisabled("Testing review, widget, mitigation, and recap polish.");
        DrawBreathingGoldBullet("Boss-targeted debuffs now stay visible through the lead-up while they are still active.");
        DrawBreathingGoldBullet("A movable death recap popup can appear when you die.");
        DrawWrappedBullet("Recorded deaths now save source mitigation history.");
        DrawWrappedBullet("Mitigation now shows a calculated Mit total.");
        DrawWrappedBullet("Split physical/magic mitigation now has room to show cleanly.");
        DrawWrappedBullet("Multi-cause rows now separate the expand arrow from row selection.");
        DrawWrappedBullet("Expanded timeline rows now keep hover and click highlights across the full row.");
        DrawWrappedBullet("Now no longer duplicates a saved last pull, while the widget still keeps the quick view.");
        DrawWrappedBullet("Current pull widget now uses the newer UI, and removes redundant wording.");
        DrawWrappedBullet("Recap popup opacity can now be adjusted in Customize.");
        DrawWrappedBullet("Health and hit tooltips are shorter.");
        DrawWrappedBullet("Chat recap messages send faster, with the link posted last.");
        DrawWrappedBullet("The recap link setting now shows Post [ Death Link ] as a system message on captured death(s).");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.110");
        ImGui.TextDisabled("Timeline arrow polish.");
        DrawWrappedBullet("Multi-cause timeline rows use the original disclosure arrow shape again: right when closed, down when open.");
        DrawWrappedBullet("The arrow stays on the left and still has no background, so it reads like part of the row instead of another button.");
        DrawWrappedBullet("Tiny visual details can scrape at you forever when they are wrong. This one should finally sit where it belongs.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.109");
        ImGui.TextDisabled("Pulls starts tucked away.");
        DrawWrappedBullet("Review now starts with the Pulls drawer collapsed, so the timeline and selected death have room first.");
        DrawWrappedBullet("The duty filter stays on All duties by default unless you choose a different duty yourself.");
        DrawWrappedBullet("It is a small default, but it matters. The window should get out of the way before the pull has even finished hurting.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.108");
        ImGui.TextDisabled("Timeline cause rows expand inline.");
        DrawWrappedBullet("Multi-cause death timeline rows now expand in place instead of opening a floating popup.");
        DrawWrappedBullet("The toggle is transparent again, with the arrow back on the left where it belongs.");
        DrawWrappedBullet("Small UI details hurt when they are wrong. This should feel calmer and clearer when the pull already did enough damage.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.107");
        ImGui.TextDisabled("Timeline cause dropdown polish.");
        DrawWrappedBullet("Multi-cause death timeline rows now open as actual dropdowns instead of pretending to be one.");
        DrawWrappedBullet("The dropdown stays centered, transparent, and compact while showing each captured likely cause cleanly.");
        DrawWrappedBullet("Small UI details like this matter. Review should feel precise, especially when the pull already hurt enough.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.106");
        ImGui.TextDisabled("Pulls drawer fit and header polish.");
        DrawWrappedBullet("Tightened the Pulls drawer so the duty filter defines the width instead of leaving extra empty space.");
        DrawWrappedBullet("Pulled the trash and collapse buttons back from the edge so they stop getting clipped by the window border.");
        DrawWrappedBullet("This one is small, but it matters: Review should feel intentional and usable in the middle of prog, not like the UI is fighting for every pixel.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.105");
        ImGui.TextDisabled("Testing Pulls drawer spacing refinements.");
        DrawWrappedBullet("Pulls drawer now uses a fixed 300px width and no longer has a resize handle.");
        DrawWrappedBullet("Collapsed Pulls now shows selectable pull numbers with compact duty tooltips.");
        DrawWrappedBullet("Pull summaries use compact duty, timer, and death-count text with local times shown without seconds.");
        DrawWrappedBullet("Duty filter now starts directly at the left side of the Pulls controls.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.104");
        ImGui.TextDisabled("Testing Pulls drawer cleanup.");
        DrawWrappedBullet("Removed the pull order dropdown while keeping the Duty filter.");
        DrawWrappedBullet("Recorded pulls now always use duty-grouped newest-first ordering.");
        DrawWrappedBullet("Pulls header controls now sit beside the title with delete before collapse.");
        DrawWrappedBullet("Reduced the Pulls drawer maximum width for a tighter review layout.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.103");
        ImGui.TextDisabled("Testing review drawer and scrollbar refinements.");
        DrawBreathingGoldBullet("Collapsed Pulls now expands from the separator edge instead of taking its own mini panel.");
        DrawWrappedBullet("Review splits the remaining space evenly between the death timeline and selected death details while Pulls is collapsed.");
        DrawWrappedBullet("Visible scrollbars are hidden across the recap UI while content remains mouse-wheel scrollable.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.102");
        ImGui.TextDisabled("Testing review drawer and persistence refinements.");
        DrawBreathingGoldBullet("Pulls now slide in and out from the left side of Review with matching expand and collapse arrows.");
        DrawBreathingGoldBullet("Pull drawer width and Customize settings are saved so user choices persist across plugin updates.");
        DrawWrappedBullet("Pull drawer can be resized horizontally and keeps the trash button in the Pulls header.");
        DrawWrappedBullet("Pull filters now show Duty first, then Sort, and wrap cleanly when the drawer is narrow.");
        DrawWrappedBullet("Timeline likely causes now use compact action and damage lines, with multi-cause rows collapsed by default.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.101");
        ImGui.TextDisabled("Testing layout and settings refinements.");
        DrawBreathingGoldBullet("Added a Clock Display setting for 24-hour or 12-hour local pull times.");
        DrawWrappedBullet("Recorded pull subtitles now avoid repeating duty name and timer, showing only the reset/capture time.");
        DrawWrappedBullet("Timeline number cells and selected death details have cleaner indentation away from separators.");
        DrawWrappedBullet("Moved the debug button onto the Developer tools row to reduce settings clutter.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.100");
        ImGui.TextDisabled("Cleaner testing review surface.");
        DrawBreathingGoldBullet("Review and Example now use one continuous review surface instead of separate boxed containers.");
        DrawBreathingGoldBullet("Pulls, timeline, and death details are separated by thin translucent dividers to recover space.");
        DrawWrappedBullet("Reduced the outer shell padding so the recap content sits closer to the window edges.");
        DrawWrappedBullet("Removed the visible shell border so the custom UI blends more naturally into the window.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.99");
        ImGui.TextDisabled("Cleaner testing UI direction.");
        DrawBreathingGoldBullet("Replaced the default tab strip with a cleaner Review / Example / Customize / Updates shell.");
        DrawBreathingGoldBullet("Selected death review now uses guided detail controls instead of nested tabs.");
        DrawWrappedBullet("Customize combines Settings and Widget controls into one responsive page.");
        DrawWrappedBullet("Updates combines Notes and Changelog into one support page.");
        DrawWrappedBullet("Review panels now use a custom visual style for a cleaner testing concept.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.98");
        ImGui.TextDisabled("Testing UI overhaul concept.");
        DrawBreathingGoldBullet("Death Recap now uses a master-detail review workspace instead of stacked pull collapsers.");
        DrawBreathingGoldBullet("Selected death details are split into Summary, Mitigation, and 10s Lead-up tabs.");
        DrawWrappedBullet("Recorded pulls now live in a pull browser with the existing duty filter and sort controls.");
        DrawWrappedBullet("Example Pull uses the same review workspace as real recorded pulls so the preview matches the active recap flow.");
        DrawWrappedBullet("The recap layout responds to available width by switching between side-by-side panes and stacked sections.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.97");
        ImGui.TextDisabled("Improved widget readability, chat summaries, and mitigation display.");
        DrawBreathingGoldBullet("Current Pull widget now has Normal and Concise display options.");
        DrawBreathingGoldBullet("Active mitigations/debuffs Mit% now uses physical and magic icons instead of P/M letter prefixes.");
        DrawWrappedBullet("Current Pull widget now labels the status column as Mits/Debuffs and concise view caps visible status icons at three with a +x count.");
        DrawWrappedBullet("Chat-posted recaps now shorten HP before hit/KO by removing the max HP value while keeping the percentage.");
        DrawWrappedBullet("Example Pull and Widget preview now use a smaller redacted Sigmascape V4.0 Pull 127-style example.");
        DrawWrappedBullet("Current Pull widget hides the visual scrollbar while keeping mouse-wheel scrolling available.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.90");
        ImGui.TextDisabled("Reworked live combat capture for more accurate death review.");
        DrawBreathingGoldBullet("Reworked death capture to use hook-based live combat data instead of relying mostly on periodic FFXIV HP/status snapshots.");
        DrawWrappedBullet("10-second lead-up history now receives packet-timed HP, shield, and status updates when the game exposes them.");
        DrawWrappedBullet("Death confirmation now uses live combat events to anchor KO timing sooner and more consistently.");
        DrawWrappedBullet("DoT tick damage can now appear as a captured death event when it is the relevant hit.");
        DrawWrappedBullet("Player mitigation and debuff context now merges nearby live combat data with existing snapshots for better pre-hit review.");
        DrawWrappedBullet("Likely wall/non-hit KO fallback is cleaner when a death is confirmed but no lethal hit is captured.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.89");
        ImGui.TextDisabled("Debug updates for future release testing.");
        DrawWrappedBullet("Updated Debug for future release testing.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.88");
        ImGui.TextDisabled("Debug updates for future release testing.");
        DrawWrappedBullet("Updated Debug for future release testing.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.87");
        ImGui.TextDisabled("Debug updates for future release testing.");
        DrawWrappedBullet("Updated Debug for future release testing.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.86");
        ImGui.TextDisabled("Debug updates for future release testing.");
        DrawWrappedBullet("Updated Debug for future release testing.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.85");
        ImGui.TextDisabled("Debug updates for future release testing.");
        DrawWrappedBullet("Updated Debug for future release testing.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.84");
        ImGui.TextDisabled("Debug updates for future release testing.");
        DrawWrappedBullet("Updated Debug for future release testing.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.82");
        ImGui.TextDisabled("Debug updates for future release testing.");
        DrawWrappedBullet("Updated Debug for future release testing.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.81");
        ImGui.TextDisabled("Debug updates for future release testing.");
        DrawWrappedBullet("Updated Debug for future release testing.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.79");
        ImGui.TextDisabled("Patched debug and self-capture tracking.");
        DrawWrappedBullet("Patched an issue where debug capture could fail to show data when your own character was not being tracked.");
        DrawWrappedBullet("Better Deaths now falls back to your local player when the party list does not expose your character.");
        DrawWrappedBullet("Added a Debug tab capture-state line showing duty, combat, live capture, PvP block, and tracked character count.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.78");
        ImGui.TextDisabled("Improved debug capture for status troubleshooting.");
        DrawWrappedBullet("Reworked the Debug tab to show live raw status snapshots for tracked characters, which helps verify data needed for implementing additional features.");
        DrawWrappedBullet("Raw debug status snapshots now show status icon, ID, name, source, stacks, and remaining time.");
        DrawWrappedBullet("Debug data is memory-only and clears when debug capture is disabled or cleared.");
        DrawWrappedBullet("Shortened the 10-second lead-up explanation text.");
        DrawWrappedBullet("Renamed the expired mitigation section to better describe what it shows.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.77");
        ImGui.TextDisabled("Clarified mitigation type labels.");
        DrawWrappedBullet("Mit Type entries now show tooltips explaining All, Physical, Magic, Shield, Regen, and other displayed categories.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.76");
        ImGui.TextDisabled("Cleaned up mitigation review and table wiring.");
        DrawBreathingGoldBullet("Extra mitigation context now shows review-focused mitigation details instead of low-value raw status fields.");
        DrawBreathingGoldBullet("Renamed Status to Ability, Spell ID to Mit Type, Stacks to Mit%, and Remaining to Linked Effects.");
        DrawWrappedBullet("Mit Type and Mit% now use stored mitigation metadata, including physical and magic reduction types when applicable.");
        DrawWrappedBullet("Linked Effects now shows related mitigation effects such as Bloodwhetting granting Stem the Flow and Stem the Tide.");
        DrawWrappedBullet("Variable mitigation values, such as Intervention, are highlighted with a tooltip explaining why the value may change.");
        DrawWrappedBullet("Captured hits/events now clarify that displayed damage values are calculated post-mitigation.");
        DrawWrappedBullet("Overkill now distinguishes exact lethal hits from non-lethal captured hits followed by likely non-hit KOs.");
        DrawWrappedBullet("Cleaned up redundant backend display paths and removed abandoned code that no longer contributed to recap review.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.75");
        ImGui.TextDisabled("Improved mitigation and lead-up event review.");
        DrawBreathingGoldBullet("Mitigation now displays more accurately in Player Death Information.");
        DrawWrappedBullet("Boss-targeted mitigations are now included in Player Death Information and Extra mitigation context when needed.");
        DrawWrappedBullet("Fixed a bug that prevented boss-targeted mitigations from appearing in the 10-second HP history.");
        DrawWrappedBullet("Captured hits/events below the 10-second HP history now correctly show player mits/debuffs and boss damage-downs.");
        DrawWrappedBullet("Fixed the 10-second captured hits/events table so wall/non-hit KOs cannot pull in events older than 10 seconds before the actual KO.");
        DrawWrappedBullet("Renamed some columns to better reflect what they capture and display.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.74");
        ImGui.TextDisabled("Polished responsive text and shared recap link wording.");
        DrawWrappedBullet("Long centered table text now wraps into centered lines instead of falling back to left-aligned wrapping.");
        DrawWrappedBullet("Notes feature bullets now wrap when the window is narrowed.");
        DrawWrappedBullet("Changelog bullets now wrap when the window is narrowed.");
        DrawWrappedBullet("Detected shared recap links now show the player name with a compact Pull Link.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.73");
        ImGui.TextDisabled("Improved Current Pull widget readability.");
        DrawBreathingGoldBullet("Current Pull widget now includes an Overkill column.");
        DrawBreathingGoldBullet("Current Pull widget now includes a Mits column with player mitigation/debuff and boss damage-down icons.");
        DrawBreathingGoldBullet("Widget cause and overkill numbers now use compact values like 3k, 186.9k, and 1.3m.");
        DrawWrappedBullet("Removed the widget # column to make room for mitigation icons while keeping deaths ordered by time.");
        DrawWrappedBullet("Widget mitigation icons wrap inside the Mits column and have their own icon-size slider.");
        DrawWrappedBullet("Multi-hit chat posts now summarize total damage in one recap line.");
        DrawWrappedBullet("Shared recap recognition now supports the new multi-hit chat summary format.");
        DrawWrappedBullet("Duplicate mitigation/debuff status snapshots are now collapsed in recap details, lead-up tables, widgets, and chat posts.");
        DrawWrappedBullet("Older saved pulls also benefit from the duplicate status cleanup when they are displayed.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.72");
        ImGui.TextDisabled("Improved responsive layout and reduced background overhead.");
        DrawHighlightedChangelogBullet("Preparation for public beta testing. Lots of behind-the-scenes optimizations were made.");
        DrawWrappedBullet("HP + shields bars now scale to the available table cell width instead of using a fixed maximum size.");
        DrawWrappedBullet("HP bar text now shortens automatically in narrow columns and clips inside the bar.");
        DrawWrappedBullet("Recap, lead-up, status, and debug tables now use weighted responsive columns for cleaner resizing.");
        DrawWrappedBullet("Captured hits/events summary text is centered in Player Death Information.");
        DrawWrappedBullet("Reduced memory usage by 56% through code cleanup and saved JSON cleanup.");
        DrawWrappedBullet("Live capture cleanup now runs on a steady interval instead of every framework tick or combat event.");
        DrawWrappedBullet("Recorded pull history now skips disk writes when the saved data has not changed.");
        DrawWrappedBullet("Recorded pulls kept now saves after the slider edit is released instead of while dragging.");
        DrawWrappedBullet("Reduced small hot-path allocations in party refresh and reset-state tracking.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.71");
        ImGui.TextDisabled("Improved lead-up event accuracy and HP history readability.");
        DrawBreathingGoldBullet("10-second HP history now inserts captured hit/event rows at the actual event timestamp between HP samples.");
        DrawBreathingGoldBullet("Stale post-hit HP samples now show derived post-hit HP, with tooltips showing the raw captured sample.");
        DrawWrappedBullet("Captured event rows use the HP captured with that event when available, with tooltip fallback details.");
        DrawWrappedBullet("The detailed captured hits/events table remains available for full source, action, amount, and status review.");
        DrawWrappedBullet("Lead-up timing now displays relative to the actual KO time to avoid the hidden fatal-sequence buffer offset.");
        DrawWrappedBullet("Lead-up table layout was cleaned up so HP bars, captured events, and mitigation/debuff columns have clearer spacing and alignment.");
        DrawWrappedBullet("Widget player-name display setting is now labeled Naming Options.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.70");
        ImGui.TextDisabled("Improved recap readability and widget controls.");
        DrawBreathingGoldBullet("Visible overkill amount now appears beneath the HP + shields bar.");
        DrawBreathingGoldBullet("Current Pull widget now shows job icons next to player names and can switch between full names and initials.");
        DrawWrappedBullet("Recap tables now center headers and compact values while keeping long cause/status text readable.");
        DrawWrappedBullet("Debug tab is now hidden by default and can be revealed from the bottom of Settings under Developer tools.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.69");
        ImGui.TextDisabled("Word fixing and recap table cleanup.");
        DrawWrappedBullet("Cleaned up captured hits/events so action IDs no longer crowd the 10-second lead-up table.");
        DrawWrappedBullet("Moved hit flags into the Type column and removed the extra Flags column.");
        DrawWrappedBullet("Centered mitigation and debuff status cells in the lead-up table.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.68");
        ImGui.TextDisabled("Polished widget and Notes wording.");
        DrawWrappedBullet("Renamed the widget window to Better Deaths Widget to avoid repeating Current Pull in the title bar.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.67");
        ImGui.TextDisabled("Improved last-pull review persistence and duty-only capture.");
        DrawBreathingGoldBullet("Last Pull Review keeps the most recent wiped/reset pull visible until the next duty pull starts.");
        DrawBreathingGoldBullet("Review copies now show the saved Recorded Pull number.");
        DrawBreathingGoldBullet("Better Deaths now only captures inside active duties, so overworld combat will not clear review data or create recaps.");
        DrawWrappedBullet("Recorded pulls still save immediately on wipe/reset/territory changes without duplicating the same pull.");
        DrawWrappedBullet("Removed mitigation timers from chat-posted active status lines to keep chat summaries cleaner.");
        DrawWrappedBullet("Updated the Notes tab feature summary to better describe the current review tools.");
        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.66");
        ImGui.TextDisabled("Improved Current Pull widget readability.");
        DrawBreathingGoldBullet("Current Pull widget now uses a compact widget-only death table instead of the full recap layout.");
        DrawWrappedBullet("Multi-hit deaths are summarized into one total line in the widget, with full hit details kept in the tooltip.");
        DrawWrappedBullet("Widget content now clips and scrolls inside the widget instead of overflowing during busy pulls.");
        DrawWrappedBullet("Widget preview now uses the same compact renderer as the live widget.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.65");
        ImGui.TextDisabled("Improved death context consistency.");
        DrawWrappedBullet("Moved extra mitigation context above the 10-second lead-up so important mitigation review is easier to see.");
        DrawWrappedBullet("Player mitigation and debuff context now uses the selected pre-hit snapshot before falling back to post-death statuses.");
        DrawWrappedBullet("Earlier boss debuff review now compares against the selected likely-hit group instead of an older single-cause fallback.");
        DrawWrappedBullet("Cleaned up old duplicate death-cause paths so the recap UI, debug log, and chat systems stay aligned.");
        DrawWrappedBullet("Improved captured hits/events rendering and clarified the empty-state wording.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.64");
        ImGui.TextDisabled("Improved captured hit readability.");
        DrawWrappedBullet("Captured hits/events summaries now show the combined damage total as a single hit value.");
        DrawWrappedBullet("Likely cause details still keep the full source, action, amount, and flags breakdown.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.63");
        ImGui.TextDisabled("Improved chat recap consistency.");
        DrawWrappedBullet("Changed chat-posted death recaps to use the same selected death-display data as Player Death Information.");
        DrawWrappedBullet("Chat HP before hit now uses the mathematically selected pre-hit HP plus shield snapshot when available.");
        DrawWrappedBullet("Chat-posted active mits and player debuffs now come from the same selected snapshot shown in the UI.");
        DrawWrappedBullet("Centralized death display selection so the recap window, timeline cause, shared recap matching, and chat posts stay aligned.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.62");
        ImGui.TextDisabled("Improved HP and hit display consistency.");
        DrawWrappedBullet("Changed player death HP selection to prefer the latest pre-hit snapshot that mathematically fits the captured killing damage.");
        DrawWrappedBullet("Merged fatal sequence events into the 10-second lead-up so captured hits/events do not appear empty when fatal sequence data exists.");
        DrawWrappedBullet("Kept the displayed HP bar and captured hit list tied to the same selected death-cause events.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.61");
        ImGui.TextDisabled("Improved likely cause accuracy.");
        DrawWrappedBullet("Stopped heals from being captured as death lead-up events.");
        DrawWrappedBullet("Limited displayed likely causes to positive damage or status KO events.");
        DrawWrappedBullet("Kept miss and invulnerability events as lead-up context without promoting them to the timeline, player details, or chat-posted cause.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.60");
        ImGui.TextDisabled("Improved recap readability and chat-posted death details.");
        DrawBreathingGoldBullet("Added multiple likely causes to player death details when several captured hits/events contributed to the KO.");
        DrawBreathingGoldBullet("Added player debuffs to chat-posted death summaries.");
        DrawWrappedBullet("Removed boss ability icon columns from recaps and lead-up tables because those actions usually do not have useful icons.");
        DrawWrappedBullet("Changed likely cause details to show Action, Source, Amount, and Flags in a consistent bullet format.");
        DrawWrappedBullet("Cleaned up the Widget tab preview so the opacity slider is not fighting an extra container background.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.59");
        ImGui.TextDisabled("Improved Current Pull widget customization.");
        DrawBreathingGoldBullet("Added a Widget tab with settings and an example preview.");
        DrawBreathingGoldBullet("Added a background opacity slider for the Current Pull widget.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.58");
        ImGui.TextDisabled("Improved live pull visibility and death timeline consistency.");
        DrawBreathingGoldBullet("Added an optional Current Pull widget for watching deaths during live combat.");
        DrawBreathingGoldBullet("Added /bdwidget and /betterdeathswidget to toggle the Current Pull widget.");
        DrawWrappedBullet("Updated death timeline likely causes to match the captured hits/events shown in player death details.");
        DrawWrappedBullet("Centered death timeline headers and key columns for cleaner reading.");
        DrawWrappedBullet("Changed recorded pull reset timestamps to display in local time.");
        DrawWrappedBullet("Removed misleading combat event and likely cause window sliders from Settings.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.57");
        ImGui.TextDisabled("Improved death timeline job display.");
        DrawWrappedBullet("Centered job abbreviations beside job icons in the death timeline.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.56");
        ImGui.TextDisabled("Fixed report rendering with job icons.");
        DrawWrappedBullet("Fixed a crash when opening reports that tried to draw an unavailable job icon.");
        DrawWrappedBullet("Job icons now fall back safely if the game icon asset cannot be loaded.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.54");
        ImGui.TextDisabled("Improved death recap capture and review clarity.");
        DrawWrappedBullet("Added compact fatal sequence tracking around deaths.");
        DrawWrappedBullet("Added filtered combat-log confirmations for boss and enemy damage to players.");
        DrawWrappedBullet("Limited live capture to combat, with a short grace window for delayed KO detection.");
        DrawWrappedBullet("Reduced misleading HP display by using the closest alive HP and shield sample before KO.");
        DrawWrappedBullet("Added job icons next to job abbreviations in the death timeline.");
        DrawWrappedBullet("Changed 8-player death link text to Party wipe detected.");
        DrawWrappedBullet("Reduced settings stutter by saving Recorded pulls kept after releasing the slider.");
        DrawWrappedBullet("Added safer bounded in-memory capture so Better Deaths does not behave like a debug logger.");
        DrawReviewPaneBottomPadding();
    }

    private static void DrawBreathingGoldBullet(string text)
    {
        DrawHighlightedChangelogBullet(text);
    }

    private static void DrawLargeHighlightedChangelogCallout(string text)
    {
        var style = ImGui.GetStyle();
        var start = ImGui.GetCursorScreenPos();
        var width = MathF.Max(ImGui.GetContentRegionAvail().X, ImGui.GetFontSize() * 14.0f);
        var markerWidth = 4.0f;
        var paddingX = 10.0f;
        var paddingY = MathF.Max(5.0f, style.ItemSpacing.Y * 0.75f);
        var fontSize = ImGui.GetFontSize() * 2.0f;
        var fontScale = fontSize / ImGui.GetFontSize();
        var textOffsetX = markerWidth + paddingX;
        var textWidth = MathF.Max(ImGui.GetFontSize() * 10.0f, width - textOffsetX - paddingX);
        var lines = WrapTextForWidth(text, textWidth, fontScale);
        var lineHeight = ImGui.GetTextLineHeight() * fontScale;
        var height = MathF.Max(lineHeight + (paddingY * 2.0f), (lineHeight * lines.Count) + (paddingY * 2.0f));
        var end = start + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(start, end, ImGui.GetColorU32(GetChangelogHighlightBackgroundColor()), 4.0f);
        drawList.AddRectFilled(
            start + new Vector2(0.0f, 3.0f),
            new Vector2(start.X + markerWidth, end.Y - 3.0f),
            ImGui.GetColorU32(GetChangelogHighlightAccentColor()),
            2.0f);

        var textColor = ImGui.GetColorU32(GetChangelogHighlightTextColor());
        var textPosition = start + new Vector2(textOffsetX, paddingY);
        var font = ImGui.GetFont();
        foreach (var line in lines)
        {
            drawList.AddText(font, fontSize, textPosition, textColor, line);
            textPosition.Y += lineHeight;
        }

        ImGui.Dummy(new Vector2(width, height));
        ImGui.Spacing();
    }

    private static void DrawHighlightedChangelogBullet(string text)
    {
        var style = ImGui.GetStyle();
        var start = ImGui.GetCursorScreenPos();
        var width = MathF.Max(ImGui.GetContentRegionAvail().X, ImGui.GetFontSize() * 12.0f);
        var markerWidth = 3.0f;
        var paddingX = 8.0f;
        var paddingY = MathF.Max(2.0f, style.ItemSpacing.Y * 0.35f);
        var textOffsetX = markerWidth + paddingX;
        var textWidth = MathF.Max(ImGui.GetFontSize() * 8.0f, width - textOffsetX - paddingX);
        var lines = WrapTextForWidth(text, textWidth);
        var lineHeight = ImGui.GetTextLineHeight();
        var height = MathF.Max(lineHeight + (paddingY * 2.0f), (lineHeight * lines.Count) + (paddingY * 2.0f));
        var end = start + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(start, end, ImGui.GetColorU32(GetChangelogHighlightBackgroundColor()), 3.0f);
        drawList.AddRectFilled(
            start + new Vector2(0.0f, 2.0f),
            new Vector2(start.X + markerWidth, end.Y - 2.0f),
            ImGui.GetColorU32(GetChangelogHighlightAccentColor()),
            1.5f);

        var textColor = ImGui.GetColorU32(GetChangelogHighlightTextColor());
        var textPosition = start + new Vector2(textOffsetX, paddingY);
        foreach (var line in lines)
        {
            drawList.AddText(textPosition, textColor, line);
            textPosition.Y += lineHeight;
        }

        ImGui.Dummy(new Vector2(width, height));
    }

    private static Vector4 GetBreathingGoldColor()
    {
        var pulse = (MathF.Sin((float)ImGui.GetTime() * 2.2f) + 1.0f) * 0.5f;
        var baseColor = LeadUpGoldColor;
        var pulseAmount = ActiveThemeUsesLightPanels() ? 0.07f : 0.18f;

        return new Vector4(
            MathF.Min(1.0f, baseColor.X + (pulse * 0.04f)),
            MathF.Min(1.0f, baseColor.Y + (pulse * pulseAmount)),
            MathF.Min(1.0f, baseColor.Z + (pulse * pulseAmount)),
            baseColor.W);
    }

    private static Vector4 GetChangelogHighlightTextColor()
    {
        var color = GetBreathingGoldColor();
        if (ActiveThemeUsesLightPanels())
        {
            color = BlendColors(color, ModernTextColor, 0.32f);
        }
        else
        {
            color = BlendColors(color, new Vector4(1.0f, 1.0f, 1.0f, 1.0f), 0.08f);
        }

        return color with { W = 1.0f };
    }

    private static Vector4 GetChangelogHighlightAccentColor()
    {
        var pulse = GetChangelogHighlightPulse();
        var color = ActiveThemeUsesLightPanels()
            ? BlendColors(ModernAccentColor, ModernTextColor, 0.12f)
            : ModernAccentColor;
        var pulseTarget = ActiveThemeUsesLightPanels() ? ModernTextColor : LeadUpGoldColor;
        var pulseAmount = ActiveThemeUsesLightPanels() ? 0.08f : 0.16f;
        return BlendColors(color, pulseTarget, pulse * pulseAmount) with { W = 1.0f };
    }

    private static Vector4 GetChangelogHighlightBackgroundColor()
    {
        var pulse = GetChangelogHighlightPulse();
        var color = ActiveThemeUsesLightPanels()
            ? BlendColors(ModernPanelAltColor, ModernAccentSoftColor, 0.42f)
            : BlendColors(ModernPanelAltColor, ModernAccentSoftColor, 0.55f);

        return BlendColors(color, ModernAccentColor, pulse * 0.08f) with { W = ActiveThemeUsesLightPanels() ? 0.70f : 0.36f };
    }

    private static float GetChangelogHighlightPulse()
    {
        return (MathF.Sin((float)ImGui.GetTime() * 2.2f) + 1.0f) * 0.5f;
    }

    private static bool ActiveThemeUsesLightPanels()
    {
        return GetColorLuminance(ModernPanelColor) >= 0.55f;
    }

    private static Vector4 GetCheckboxFrameColor()
    {
        return activeTheme.CheckboxFrameColor;
    }

    private static Vector4 GetCheckboxFrameHoveredColor()
    {
        return activeTheme.CheckboxFrameHoveredColor;
    }

    private static Vector4 GetCheckboxFrameActiveColor()
    {
        return activeTheme.CheckboxFrameActiveColor;
    }

    private static Vector4 GetCheckboxCheckMarkColor()
    {
        return activeTheme.ModernCheckMarkColor;
    }

    private static Vector4 GetCheckboxBorderColor()
    {
        return activeTheme.CheckboxBorderColor;
    }

    private static Vector4 GetScrollbarBackgroundColor()
    {
        return activeTheme.ScrollbarBackgroundColor;
    }

    private static Vector4 GetScrollbarGrabColor()
    {
        return activeTheme.ScrollbarGrabColor;
    }

    private static Vector4 GetScrollbarGrabHoveredColor()
    {
        return activeTheme.ScrollbarGrabHoveredColor;
    }

    private static Vector4 GetScrollbarGrabActiveColor()
    {
        return activeTheme.ScrollbarGrabActiveColor;
    }

    private static Vector4 GetButtonTextColor(Vector4 background, bool selected)
    {
        var desired = selected
            ? ModernSelectedButtonTextColor
            : ModernButtonTextColor;
        return GetColorContrast(background, desired) >= 4.5f
            ? desired
            : GetReadableTextColorForBackground(background, 4.5f);
    }

    private static Vector4 GetReadableTextColorForBackground(Vector4 background, float minimumContrast = 3.0f)
    {
        var darkText = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
        var lightText = new Vector4(0.98f, 0.98f, 0.96f, 1.0f);
        var themeText = ModernTextColor with { W = 1.0f };

        if (GetColorContrast(background, themeText) >= minimumContrast)
        {
            return themeText;
        }

        return GetColorContrast(background, lightText) >= GetColorContrast(background, darkText)
            ? lightText
            : darkText;
    }

    private static float GetColorContrast(Vector4 first, Vector4 second)
    {
        var firstLuminance = GetContrastLuminance(first);
        var secondLuminance = GetContrastLuminance(second);
        return (MathF.Max(firstLuminance, secondLuminance) + 0.05f) /
            (MathF.Min(firstLuminance, secondLuminance) + 0.05f);
    }

    private static float GetContrastChannel(float channel)
    {
        return channel <= 0.03928f
            ? channel / 12.92f
            : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }

    private static float GetContrastLuminance(Vector4 color)
    {
        return (GetContrastChannel(color.X) * 0.2126f) +
            (GetContrastChannel(color.Y) * 0.7152f) +
            (GetContrastChannel(color.Z) * 0.0722f);
    }

    private static float GetColorLuminance(Vector4 color)
    {
        return (color.X * 0.2126f) + (color.Y * 0.7152f) + (color.Z * 0.0722f);
    }

    private static Vector4 BlendColors(Vector4 first, Vector4 second, float amount)
    {
        var clampedAmount = Math.Clamp(amount, 0.0f, 1.0f);
        return new Vector4(
            first.X + ((second.X - first.X) * clampedAmount),
            first.Y + ((second.Y - first.Y) * clampedAmount),
            first.Z + ((second.Z - first.Z) * clampedAmount),
            first.W + ((second.W - first.W) * clampedAmount));
    }

    private static Vector4 GetCreatorNoteTextColor()
    {
        if (!ActiveThemeUsesLightPanels())
        {
            return new Vector4(
                MathF.Max(LeadUpGoldColor.X, 0.94f),
                MathF.Max(LeadUpGoldColor.Y, 0.76f),
                MathF.Max(LeadUpGoldColor.Z, 0.38f),
                1.0f);
        }

        return new Vector4(
            MathF.Min(LeadUpGoldColor.X * 0.68f, 0.55f),
            MathF.Min(LeadUpGoldColor.Y * 0.68f, 0.38f),
            MathF.Min(LeadUpGoldColor.Z * 0.68f, 0.20f),
            1.0f);
    }

    private static void DrawCreatorNote()
    {
        var textColor = GetCreatorNoteTextColor();

        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.TextWrapped("Hiya Nai here!");
        ImGui.Spacing();
        ImGui.TextWrapped("Seeing this grow from a little idea into something people actually use and care about means more to me than I can really put into words.. Every bit of feedback, every bug report, and every tiny detail you all helped me chase down has made this feel less like something I've been building alone.");
        ImGui.Spacing();
        ImGui.TextWrapped("I'm happy to say that the project is nearing the end and will be moving more into maintenance mode for Replays; I hope it can remain a plugin worthy of your prog!");
        ImGui.Spacing();
        ImGui.TextWrapped("I appreciate every single one of you lovely individuals.");
        ImGui.PopStyleColor();
    }

    private sealed record ExamplePlayer(
        string Key,
        string Name,
        int PartyIndex,
        uint ClassJobId,
        string Job,
        uint MaxHp);

    private static readonly IReadOnlyDictionary<string, ExamplePlayer> ExamplePlayers = new Dictionary<string, ExamplePlayer>
    {
        ["Tank 1"] = new("example-tank-1", "Tank 1", 0, 32, "DRK", 325090),
        ["Tank 2"] = new("example-tank-2", "Tank 2", 1, 19, "PLD", 325090),
        ["Healer 1"] = new("example-healer-1", "Healer 1", 2, 24, "WHM", 205237),
        ["Healer 2"] = new("example-healer-2", "Healer 2", 3, 28, "SCH", 205177),
        ["DPS 1"] = new("example-dps-1", "DPS 1", 4, 23, "BRD", 226428),
        ["DPS 2"] = new("example-dps-2", "DPS 2", 5, 22, "DRG", 227550),
        ["DPS 3"] = new("example-dps-3", "DPS 3", 6, 25, "BLM", 205177),
        ["DPS 4"] = new("example-dps-4", "DPS 4", 7, 34, "SAM", 226618),
    };

    private IReadOnlyList<PartyDeathRecord> GetExampleDeaths()
    {
        exampleDeaths ??= CreateExampleDeaths();
        return exampleDeaths;
    }

    private IReadOnlyList<PartyDeathRecord> CreateExampleDeaths()
    {
        return new List<PartyDeathRecord>
        {
            CreateExampleDeath(280.9f, "DPS 1", 280.3f, "Kefka", 47810, "Spellwave", 1831599, DamageType.Magic, DpsSpellwaveStatuses()),
            CreateExampleDeath(280.9f, "DPS 2", 280.3f, "Kefka", 47810, "Spellwave", 1834025, DamageType.Magic, DpsGalvanizeSpellwaveStatuses()),
            CreateExampleDeath(281.0f, "DPS 3", 280.3f, "Kefka", 47810, "Spellwave", 1662458, DamageType.Magic, CasterSpellwaveStatuses()),
            CreateExampleDeath(281.0f, "Tank 1", 280.3f, "Kefka", 47810, "Spellwave", 1118760, DamageType.Magic, TankSpellwaveStatuses()),
            CreateExampleDeath(281.1f, "DPS 4", 280.3f, "Kefka", 47810, "Spellwave", 1633114, DamageType.Magic, DpsSpellwaveStatuses()),
            CreateExampleDeath(286.0f, "Tank 2", 279.3f, "Kefka", 47833, "Past's End", 22550, DamageType.Magic, PaladinPastEndStatuses()),
            CreateExampleNonHitDeath(286.6f, "Healer 1", TroubadourOnlyStatuses()),
            CreateExampleDeath(292.3f, "Healer 2", 291.7f, "Kefka", 47807, "the River of Light", 7257, DamageType.Magic, ShieldHealerRiverStatuses()),
        };
    }

    private PartyDeathRecord CreateExampleDeath(
        float deathElapsed,
        string playerRole,
        float causeElapsed,
        string sourceName,
        uint actionId,
        string actionName,
        uint amount,
        DamageType damageType,
        IReadOnlyList<StatusSnapshot> statusesAtLikelyHit)
    {
        var player = ExamplePlayers[playerRole];
        var setupElapsed = MathF.Max(0.0f, causeElapsed - 8.0f);
        var setupEvent = CreateExampleEvent(
            player,
            setupElapsed,
            sourceName,
            actionId,
            actionName,
            DeathEventKind.Damage,
            (uint)Math.Round(player.MaxHp * 0.12),
            (uint)Math.Round(player.MaxHp * 0.72),
            0,
            player.MaxHp,
            damageType,
            string.Empty,
            AdjustExampleStatuses(statusesAtLikelyHit, causeElapsed, setupElapsed),
            Array.Empty<StatusSnapshot>());
        var likelyCauseShieldHp = EstimateExampleShieldBeforeHit(player, statusesAtLikelyHit);
        var likelyCauseHp = EstimateExampleHpBeforeHit(player, amount, sourceName, likelyCauseShieldHp);
        var likelyCause = CreateExampleEvent(
            player,
            causeElapsed,
            sourceName,
            actionId,
            actionName,
            DeathEventKind.Damage,
            amount,
            likelyCauseHp,
            likelyCauseShieldHp,
            player.MaxHp,
            damageType,
            string.Empty,
            statusesAtLikelyHit,
            Array.Empty<StatusSnapshot>());
        var statusesAtDeath = AdjustExampleStatuses(statusesAtLikelyHit, causeElapsed, deathElapsed);

        return new PartyDeathRecord(
            ExamplePullStartedAtUtc.AddSeconds(deathElapsed),
            deathElapsed,
            player.Key,
            player.Name,
            player.PartyIndex,
            player.ClassJobId,
            player.Job,
            0,
            0,
            player.MaxHp,
            likelyCause,
            new List<CombatEventRecord> { setupEvent, likelyCause },
            CreateExampleHpHistory(player, deathElapsed, likelyCause, statusesAtLikelyHit),
            statusesAtDeath)
        {
            EnemyHpAtDeath = CreateExampleEnemyHpAtDeath(deathElapsed),
            ReplayPositions = CreateExampleReplayPositions(player, deathElapsed),
            ReplayMarkers = CreateExampleReplayMarkers(player, deathElapsed),
            ReplayMechanics = CreateExampleReplayMechanics(deathElapsed),
        };
    }

    private PartyDeathRecord CreateExampleNonHitDeath(
        float deathElapsed,
        string playerRole,
        IReadOnlyList<StatusSnapshot> statusesAtDeath)
    {
        var player = ExamplePlayers[playerRole];

        return new PartyDeathRecord(
            ExamplePullStartedAtUtc.AddSeconds(deathElapsed),
            deathElapsed,
            player.Key,
            player.Name,
            player.PartyIndex,
            player.ClassJobId,
            player.Job,
            0,
            0,
            player.MaxHp,
            null,
            Array.Empty<CombatEventRecord>(),
            CreateExampleNonHitHpHistory(player, deathElapsed, statusesAtDeath),
            statusesAtDeath)
        {
            EnemyHpAtDeath = CreateExampleEnemyHpAtDeath(deathElapsed),
            ReplayPositions = CreateExampleReplayPositions(player, deathElapsed),
            ReplayMarkers = CreateExampleReplayMarkers(player, deathElapsed),
            ReplayMechanics = CreateExampleReplayMechanics(deathElapsed),
        };
    }

    private static IReadOnlyList<ReplayPositionSnapshot> CreateExampleReplayPositions(ExamplePlayer focusPlayer, float deathElapsed)
    {
        var snapshots = new List<ReplayPositionSnapshot>();
        var startElapsed = MathF.Max(0.0f, deathElapsed - DeathReplayLeadUpSeconds);
        var endElapsed = deathElapsed + 10.0f;
        var players = ExamplePlayers.Values
            .OrderBy(player => player.PartyIndex)
            .ToList();

        for (var elapsed = startElapsed; elapsed <= endElapsed + 0.001f; elapsed += 1.0f)
        {
            var phase = elapsed - startElapsed;
            foreach (var player in players)
            {
                var angle = ((MathF.PI * 2.0f) / players.Count) * player.PartyIndex + (phase * 0.07f);
                var spread = player.PartyIndex < 4 ? 13.0f : 18.0f;
                var direction = ReplayDirectionFromRotation(angle);
                var isFocus = string.Equals(player.Key, focusPlayer.Key, StringComparison.Ordinal);
                var isDead = isFocus && elapsed >= deathElapsed;
                var currentHp = isDead
                    ? 0u
                    : (uint)Math.Round(player.MaxHp * Math.Clamp(0.72f + (MathF.Sin(phase * 0.23f + player.PartyIndex) * 0.12f), 0.35f, 1.0f));

                snapshots.Add(new ReplayPositionSnapshot(
                    ExamplePullStartedAtUtc.AddSeconds(elapsed),
                    elapsed,
                    $"player:{player.Key}",
                    player.Name,
                    ReplayActorKind.Player,
                    player.PartyIndex,
                    (uint)(10_000 + player.PartyIndex),
                    player.ClassJobId,
                    player.Job,
                    direction.X * spread,
                    0.0f,
                    direction.Y * spread,
                    angle,
                    currentHp,
                    0,
                    player.MaxHp,
                    isDead,
                    true));
            }

            const uint kefkaMaxHp = 58_000_000;
            var kefkaHpRatio = Math.Clamp(0.88 - (elapsed / 400.0f), 0.08f, 0.95f);
            snapshots.Add(new ReplayPositionSnapshot(
                ExamplePullStartedAtUtc.AddSeconds(elapsed),
                elapsed,
                $"enemy:{GetExampleSourceId("Kefka"):X8}",
                "Kefka",
                ReplayActorKind.Enemy,
                2000,
                GetExampleSourceId("Kefka"),
                0,
                string.Empty,
                0.0f,
                0.0f,
                -4.0f + (MathF.Sin(phase * 0.12f) * 1.5f),
                0.0f,
                (uint)Math.Round(kefkaMaxHp * kefkaHpRatio),
                0,
                kefkaMaxHp,
                false,
                true));
        }

        return snapshots;
    }

    private static IReadOnlyList<ReplayMarkerSnapshot> CreateExampleReplayMarkers(ExamplePlayer focusPlayer, float deathElapsed)
    {
        var firstMarkerElapsed = MathF.Max(0.0f, deathElapsed - DeathReplayLeadUpSeconds - 5.0f);
        var secondMarkerElapsed = MathF.Max(0.0f, deathElapsed - 8.0f);
        return
        [
            CreateExampleReplayMarker(focusPlayer, firstMarkerElapsed, 336),
            CreateExampleReplayMarker(focusPlayer, secondMarkerElapsed, 337),
        ];
    }

    private static ReplayMarkerSnapshot CreateExampleReplayMarker(ExamplePlayer player, float elapsed, uint markerId)
    {
        return new ReplayMarkerSnapshot(
            ExamplePullStartedAtUtc.AddSeconds(elapsed),
            elapsed,
            $"player:{player.Key}",
            player.Name,
            ReplayActorKind.Player,
            player.PartyIndex,
            (uint)(10_000 + player.PartyIndex),
            player.ClassJobId,
            player.Job,
            markerId,
            markerId);
    }

    private static IReadOnlyList<ReplayMechanicSnapshot> CreateExampleReplayMechanics(float deathElapsed)
    {
        return
        [
            CreateExampleReplayMechanic(
                deathElapsed - 13.5f,
                4.5f,
                ReplayMechanicShape.Circle,
                -10.0f,
                7.5f,
                0.0f,
                6.0f,
                0.0f,
                0.0f,
                360.0f,
                "Circle",
                "MapEffect",
                1001,
                1,
                true),
            CreateExampleReplayMechanic(
                deathElapsed - 8.5f,
                4.0f,
                ReplayMechanicShape.Cone,
                0.0f,
                -4.0f,
                MathF.PI * 0.15f,
                16.0f,
                16.0f,
                0.0f,
                70.0f,
                "Cone",
                "VFX",
                1002,
                1,
                true),
            CreateExampleReplayMechanic(
                deathElapsed - 5.2f,
                5.0f,
                ReplayMechanicShape.Tower,
                11.0f,
                -2.0f,
                0.0f,
                4.8f,
                0.0f,
                0.0f,
                360.0f,
                "Tower",
                "MapEffect",
                1003,
                1,
                true),
            CreateExampleReplayMechanic(
                deathElapsed - 2.8f,
                3.5f,
                ReplayMechanicShape.Line,
                -1.0f,
                1.0f,
                MathF.PI * 0.62f,
                2.0f,
                30.0f,
                3.0f,
                0.0f,
                "Line",
                "Cast",
                1004,
                1,
                true),
            CreateExampleReplayMechanic(
                deathElapsed + 1.2f,
                4.0f,
                ReplayMechanicShape.Circle,
                8.0f,
                8.0f,
                0.0f,
                5.0f,
                0.0f,
                0.0f,
                360.0f,
                "Unknown",
                "RawEvent",
                9001,
                2,
                false),
        ];
    }

    private static ReplayMechanicSnapshot CreateExampleReplayMechanic(
        float elapsed,
        float durationSeconds,
        ReplayMechanicShape shape,
        float x,
        float z,
        float rotation,
        float radius,
        float length,
        float width,
        float angleDegrees,
        string label,
        string rawEventKind,
        uint rawEventId,
        uint rawState,
        bool isKnown)
    {
        elapsed = MathF.Max(0.0f, elapsed);
        return new ReplayMechanicSnapshot(
            ExamplePullStartedAtUtc.AddSeconds(elapsed),
            elapsed,
            MathF.Max(0.1f, durationSeconds),
            "example-mechanic-source",
            "Example mechanic",
            shape,
            x,
            0.0f,
            z,
            rotation,
            radius,
            length,
            width,
            angleDegrees,
            label,
            rawEventKind,
            rawEventId,
            rawState,
            isKnown);
    }

    private static IReadOnlyList<EnemyHpSnapshot> CreateExampleEnemyHpAtDeath(float deathElapsed)
    {
        const uint kefkaMaxHp = 58_000_000;
        var hpRatio = Math.Clamp(0.88 - (deathElapsed / 400.0), 0.08, 0.95);
        return
        [
            new EnemyHpSnapshot(
                ExamplePullStartedAtUtc.AddSeconds(deathElapsed),
                deathElapsed,
                GetExampleSourceId("Kefka"),
                "Kefka",
                (uint)Math.Round(kefkaMaxHp * hpRatio),
                kefkaMaxHp,
                true),
        ];
    }

    private static IReadOnlyList<HpHistorySnapshot> CreateExampleNonHitHpHistory(
        ExamplePlayer player,
        float deathElapsed,
        IReadOnlyList<StatusSnapshot> statusesAtDeath)
    {
        var sampleTimes = new[]
        {
            MathF.Max(0.0f, deathElapsed - LeadUpHistorySeconds),
            MathF.Max(0.0f, deathElapsed - 2.0f),
            MathF.Max(0.0f, deathElapsed - 1.0f),
        };

        return sampleTimes
            .Distinct()
            .Where(elapsed => elapsed <= deathElapsed)
            .OrderBy(elapsed => elapsed)
            .Select(elapsed =>
            {
                var secondsBeforeDeath = MathF.Max(0.0f, deathElapsed - elapsed);
                var currentHp = (uint)Math.Round(Math.Min(player.MaxHp, player.MaxHp * (0.26f + (secondsBeforeDeath * 0.03f))));
                return new HpHistorySnapshot(
                    ExamplePullStartedAtUtc.AddSeconds(elapsed),
                    elapsed,
                    currentHp,
                    0,
                    player.MaxHp,
                    AdjustExampleStatuses(statusesAtDeath, deathElapsed, elapsed));
            })
            .ToList();
    }

    private static IReadOnlyList<HpHistorySnapshot> CreateExampleHpHistory(
        ExamplePlayer player,
        float deathElapsed,
        CombatEventRecord likelyCause,
        IReadOnlyList<StatusSnapshot> statusesAtLikelyHit)
    {
        var sampleTimes = new[]
        {
            MathF.Max(0.0f, deathElapsed - LeadUpHistorySeconds),
            MathF.Max(0.0f, likelyCause.PullElapsedSeconds - 2.0f),
            MathF.Max(0.0f, likelyCause.PullElapsedSeconds - 1.0f),
            likelyCause.PullElapsedSeconds,
        };

        return sampleTimes
            .Distinct()
            .Where(elapsed => elapsed <= deathElapsed)
            .OrderBy(elapsed => elapsed)
            .Select(elapsed =>
            {
                var secondsBeforeCause = MathF.Max(0.0f, likelyCause.PullElapsedSeconds - elapsed);
                var currentHp = elapsed >= likelyCause.PullElapsedSeconds
                    ? likelyCause.CurrentHp
                    : (uint)Math.Round(Math.Min(player.MaxHp, likelyCause.CurrentHp + (player.MaxHp * 0.05f * secondsBeforeCause)));
                return new HpHistorySnapshot(
                    ExamplePullStartedAtUtc.AddSeconds(elapsed),
                    elapsed,
                    currentHp,
                    likelyCause.ShieldHp,
                    player.MaxHp,
                    AdjustExampleStatuses(statusesAtLikelyHit, likelyCause.PullElapsedSeconds, elapsed));
            })
            .ToList();
    }

    private static uint EstimateExampleHpBeforeHit(ExamplePlayer player, uint amount, string sourceName, uint shieldHp)
    {
        if (sourceName == "black hole")
        {
            return (uint)Math.Round(player.MaxHp * 0.78);
        }

        var damageAvailableForHp = Math.Max(1.0, amount > shieldHp ? amount - shieldHp : amount);
        var targetHp = Math.Min(player.MaxHp * 0.82, damageAvailableForHp * 0.72);
        var minimumHp = Math.Min(player.MaxHp * 0.22, Math.Max(1.0, damageAvailableForHp * 0.35));
        targetHp = Math.Max(minimumHp, targetHp);
        return (uint)Math.Round(targetHp);
    }

    private static uint EstimateExampleShieldBeforeHit(ExamplePlayer player, IReadOnlyList<StatusSnapshot> statusesAtLikelyHit)
    {
        if (statusesAtLikelyHit.Any(status => status.Name.Contains("Galvanize", StringComparison.OrdinalIgnoreCase)))
        {
            return (uint)Math.Round(player.MaxHp * 0.16);
        }

        return 0U;
    }

    private CombatEventRecord CreateExampleEvent(
        ExamplePlayer player,
        float elapsed,
        string sourceName,
        uint actionId,
        string actionName,
        DeathEventKind kind,
        uint amount,
        uint currentHp,
        uint shieldHp,
        uint maxHp,
        DamageType damageType,
        string detail,
        IReadOnlyList<StatusSnapshot> statuses,
        IReadOnlyList<StatusSnapshot> sourceStatuses)
    {
        return new CombatEventRecord(
            ExamplePullStartedAtUtc.AddSeconds(elapsed),
            elapsed,
            player.Key,
            player.Name,
            player.PartyIndex,
            GetExampleSourceId(sourceName),
            sourceName,
            actionId,
            actionName,
            GetActionIconId(actionId),
            kind,
            amount,
            currentHp,
            shieldHp,
            maxHp,
            damageType,
            false,
            false,
            false,
            false,
            detail,
            statuses,
            sourceStatuses);
    }

    private static uint GetExampleSourceId(string sourceName)
    {
        return sourceName switch
        {
            "Chaos" => 28,
            "Kefka" => 32,
            "black hole" => 34,
            _ => 0,
        };
    }

    private static IReadOnlyList<StatusSnapshot> DpsSpellwaveStatuses()
    {
        return new[]
        {
            Status(2941, "Magic Vulnerability Up", 2.0f),
            Status(1934, "Troubadour", 15.0f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> DpsGalvanizeSpellwaveStatuses()
    {
        return new[]
        {
            Status(297, "Galvanize", 22.1f),
            Status(2941, "Magic Vulnerability Up", 2.0f),
            Status(1934, "Troubadour", 15.0f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> CasterSpellwaveStatuses()
    {
        return new[]
        {
            Status(1219, "Confession", 10.0f),
            Status(297, "Galvanize", 22.1f),
            Status(2941, "Magic Vulnerability Up", 2.0f),
            Status(1934, "Troubadour", 15.0f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> TankSpellwaveStatuses()
    {
        return new[]
        {
            Status(317, "Fey Illumination", 20.0f),
            Status(2941, "Magic Vulnerability Up", 2.0f),
            Status(1934, "Troubadour", 15.0f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> PaladinPastEndStatuses()
    {
        return new[]
        {
            Status(1219, "Confession", 10.0f),
            Status(297, "Galvanize", 23.1f),
            Status(2674, "Holy Sheltron", 7.7f),
            Status(2675, "Knight's Resolve", 3.7f),
            Status(2941, "Magic Vulnerability Up", 2.0f),
            Status(1175, "Passage of Arms", 18.0f),
            Status(1934, "Troubadour", 15.0f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> TroubadourOnlyStatuses()
    {
        return new[]
        {
            Status(1934, "Troubadour", 15.0f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> ShieldHealerRiverStatuses()
    {
        return new[]
        {
            Status(297, "Galvanize", 20.6f),
            Status(299, "Sacred Soil", 5.0f),
            Status(1944, "Sacred Soil", 15.0f),
            Status(1934, "Troubadour", 15.0f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> AdjustExampleStatuses(
        IReadOnlyList<StatusSnapshot> statusesAtAnchor,
        float anchorElapsed,
        float elapsed)
    {
        var deltaSeconds = anchorElapsed - elapsed;
        return statusesAtAnchor
            .Select(status => status with
            {
                RemainingTime = MathF.Max(0.0f, status.RemainingTime + deltaSeconds),
            })
            .Where(status => status.RemainingTime > 0.05f)
            .ToList();
    }

    private static StatusSnapshot Status(uint id, string name, float remainingTime, ushort stackCount = 0)
    {
        return new StatusSnapshot(id, name, GetStatusIconId(id), 0, stackCount, remainingTime);
    }

    private static uint GetActionIconId(uint actionId)
    {
        try
        {
            var action = Plugin.DataManager.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(actionId);
            return action?.Icon ?? 0u;
        }
        catch
        {
            return 0;
        }
    }

    private static uint GetStatusIconId(uint statusId)
    {
        if (statusId == 0)
        {
            return 0;
        }

        try
        {
            var status = Plugin.DataManager.GetExcelSheet<LuminaStatus>()?.GetRowOrDefault(statusId);
            return status?.Icon ?? 0u;
        }
        catch
        {
            return 0;
        }
    }

    private static uint GetClassJobIconId(uint classJobId)
    {
        return classJobId == 0 ? 0 : 62100 + classJobId;
    }

    private static void CenterNextItem(float itemWidth)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        if (availableWidth > itemWidth)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((availableWidth - itemWidth) * 0.5f));
        }
    }

    private static bool DrawTransparentIconButton(string id, FontAwesomeIcon icon)
    {
        using var transparentButton = new TransparentButtonScope();
        return ImGuiComponents.IconButton(id, icon);
    }

    private static bool DrawCenteredTransparentIconButton(string id, FontAwesomeIcon icon)
    {
        var iconText = icon.ToIconString();
        var buttonWidth = ImGui.CalcTextSize(iconText).X + (ImGui.GetStyle().FramePadding.X * 2.0f);
        CenterNextItem(buttonWidth);
        return DrawTransparentIconButton(id, icon);
    }

    private readonly struct TransparentButtonScope : IDisposable
    {
        public TransparentButtonScope()
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f);
        }

        public void Dispose()
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }
    }

    private readonly struct CheckboxStyleScope : IDisposable
    {
        public CheckboxStyleScope()
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, GetCheckboxFrameColor());
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, GetCheckboxFrameHoveredColor());
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, GetCheckboxFrameActiveColor());
            ImGui.PushStyleColor(ImGuiCol.CheckMark, GetCheckboxCheckMarkColor());
            ImGui.PushStyleColor(ImGuiCol.Border, GetCheckboxBorderColor());
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.0f);
        }

        public void Dispose()
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(5);
        }
    }

    private static bool DrawThemedCheckbox(string label, ref bool value)
    {
        using var checkboxStyle = new CheckboxStyleScope();
        return ImGui.Checkbox(label, ref value);
    }

    private static void DrawCenteredText(string text)
    {
        CenterNextItem(ImGui.CalcTextSize(text).X);
        ImGui.TextUnformatted(text);
    }

    private static void DrawCenteredText(string text, Vector4 color)
    {
        CenterNextItem(ImGui.CalcTextSize(text).X);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void DrawCenteredOrWrappedText(string text)
    {
        DrawCenteredOrWrappedText(text, null);
    }

    private static void DrawCenteredOrWrappedText(string text, Vector4? color)
    {
        var textWidth = ImGui.CalcTextSize(text).X;
        var availableWidth = ImGui.GetContentRegionAvail().X;

        if (color is { } textColor)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        }

        ImGui.BeginGroup();
        if (textWidth <= availableWidth)
        {
            CenterNextItem(textWidth);
            ImGui.TextUnformatted(text);
        }
        else
        {
            foreach (var line in WrapTextForWidth(text, availableWidth))
            {
                CenterNextItem(ImGui.CalcTextSize(line).X);
                ImGui.TextUnformatted(line);
            }
        }

        ImGui.EndGroup();
        if (color is not null)
        {
            ImGui.PopStyleColor();
        }
    }

    private static IReadOnlyList<string> WrapTextForWidth(string text, float maxWidth)
    {
        return WrapTextForWidth(text, maxWidth, 1.0f);
    }

    private static IReadOnlyList<string> WrapTextForWidth(string text, float maxWidth, float fontScale)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0.0f)
        {
            return [text];
        }

        var lines = new List<string>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentLine = string.Empty;
        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
            if ((ImGui.CalcTextSize(candidate).X * fontScale) <= maxWidth || string.IsNullOrEmpty(currentLine))
            {
                currentLine = candidate;
                continue;
            }

            lines.Add(currentLine);
            currentLine = word;
        }

        if (!string.IsNullOrEmpty(currentLine))
        {
            lines.Add(currentLine);
        }

        return lines.Count == 0 ? [text] : lines;
    }

    private static Vector4 GetEventColor(DeathEventKind kind)
    {
        return kind switch
        {
            DeathEventKind.Damage => DamageColor,
            DeathEventKind.Heal => HealColor,
            DeathEventKind.Status => WarningColor,
            DeathEventKind.Miss or DeathEventKind.Invulnerable => DisabledColor,
            _ => ModernTextColor,
        };
    }

    private static string FormatAction(CombatEventRecord combatEvent)
    {
        if (IsLikelyAutoAttack(combatEvent))
        {
            return AutoActionDisplayName;
        }

        var actionName = FormatActionNameForDisplay(combatEvent);
        return combatEvent.Kind == DeathEventKind.Status
            ? $"{actionName} (status {combatEvent.ActionId})"
            : $"{actionName} ({combatEvent.ActionId})";
    }

    private static void DrawActionText(CombatEventRecord combatEvent, bool includeId = true)
    {
        DrawCenteredWrappedActionTextWithIcon(combatEvent, includeId ? FormatAction(combatEvent) : FormatActionNameForDisplay(combatEvent));
        DrawLikelyAutoAttackTooltip(combatEvent);
    }

    private static void DrawCenteredActionText(CombatEventRecord combatEvent, string text)
    {
        var iconId = GetDamageTypeIconId(combatEvent);
        var iconSize = GetInlineDamageTypeIconSize(iconId);
        var spacing = 4.0f;
        var groupWidth = (iconId == 0 ? 0.0f : iconSize + spacing) + ImGui.CalcTextSize(text).X;
        CenterNextItem(groupWidth);
        DrawDamageTypeIconInline(combatEvent, iconSize);
        ImGui.TextUnformatted(text);
    }

    private static void DrawActionBullet(CombatEventRecord combatEvent)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextUnformatted("Action:");
        ImGui.SameLine(0.0f, 4.0f);
        DrawDamageTypeIconInline(combatEvent);
        ImGui.TextUnformatted(FormatAction(combatEvent));
        DrawLikelyAutoAttackTooltip(combatEvent);
    }

    private static void DrawLikelyAutoAttackTooltip(CombatEventRecord combatEvent)
    {
        if (ImGui.IsItemHovered() && IsLikelyAutoAttack(combatEvent))
        {
            SetThemedTooltip(LikelyAutoAttackTooltip);
        }
    }

    private static bool IsLikelyAutoAttack(CombatEventRecord combatEvent)
    {
        return combatEvent.Kind == DeathEventKind.Damage &&
            IsAutoAttackActionName(combatEvent.ActionName, combatEvent.ActionId);
    }

    private static string FormatActionNameForDisplay(CombatEventRecord combatEvent)
    {
        return IsLikelyAutoAttack(combatEvent)
            ? AutoActionDisplayName
            : FormatActionNameForDisplay(combatEvent.ActionName);
    }

    private static string FormatActionNameForDisplay(string actionName)
    {
        return IsAutoAttackActionName(actionName, 0)
            ? AutoActionDisplayName
            : actionName;
    }

    private static bool IsAutoAttackActionName(string actionName, uint actionId)
    {
        if (string.Equals(actionName, AutoActionDisplayName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actionName, "Attack", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (actionId != 0 && string.Equals(actionName, $"Action {actionId}", StringComparison.Ordinal))
        {
            return true;
        }

        const string actionPrefix = "Action ";
        return actionName.StartsWith(actionPrefix, StringComparison.Ordinal) &&
            actionName[actionPrefix.Length..].All(char.IsDigit);
    }

    private static bool DrawDamageTypeIconInline(CombatEventRecord combatEvent, float iconSize = 14.0f)
    {
        var iconId = GetDamageTypeIconId(combatEvent);
        if (iconId == 0)
        {
            return false;
        }

        DrawGameIcon(iconId, iconSize, GetDamageTypeIconTooltip(combatEvent.DamageType));
        ImGui.SameLine(0.0f, 4.0f);
        return true;
    }

    private static uint GetDamageTypeIconId(CombatEventRecord combatEvent)
    {
        return combatEvent.Kind == DeathEventKind.Damage && combatEvent.Amount > 0
            ? GetDamageTypeIconId(combatEvent.DamageType)
            : 0;
    }

    private static uint GetDamageTypeIconId(DamageType damageType)
    {
        return damageType switch
        {
            DamageType.Magic => Plugin.MagicDamageReductionIconId,
            DamageType.Slashing or DamageType.Piercing or DamageType.Blunt or DamageType.Shot or DamageType.Physical =>
                Plugin.PhysicalDamageReductionIconId,
            _ => 0,
        };
    }

    private static float GetInlineDamageTypeIconSize(uint iconId)
    {
        return iconId == 0 ? 0.0f : 14.0f;
    }

    private static string GetDamageTypeIconTooltip(DamageType damageType)
    {
        return damageType switch
        {
            DamageType.Magic => "Magic damage",
            DamageType.Slashing or DamageType.Piercing or DamageType.Blunt or DamageType.Shot or DamageType.Physical =>
                "Physical damage",
            _ => $"{damageType} damage",
        };
    }

    private static string FormatStatusSummary(IReadOnlyList<StatusSnapshot> statuses, int maxStatuses)
    {
        if (statuses.Count == 0)
        {
            return "-";
        }

        var orderedStatuses = statuses
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();
        var shownStatuses = orderedStatuses
            .Take(maxStatuses)
            .Select(FormatStatusCompact);
        var summary = string.Join(", ", shownStatuses);
        var hiddenCount = orderedStatuses.Count - Math.Min(orderedStatuses.Count, maxStatuses);
        return hiddenCount > 0
            ? $"{summary}, +{hiddenCount} more"
            : summary;
    }

    private static string FormatStatusCompact(StatusSnapshot status)
    {
        var stackText = status.StackCount > 0 ? $" x{status.StackCount}" : string.Empty;
        var remainingText = status.RemainingTime > 0 ? $" {status.RemainingTime:0.0}s" : string.Empty;
        return $"{status.Name}{stackText}{remainingText}";
    }

    private static string FormatStatusCompact(
        StatusSnapshot status,
        bool showTenthsOverTenSeconds,
        bool showTimer = true)
    {
        var stackText = status.StackCount > 0 ? $" x{status.StackCount}" : string.Empty;
        var timerText = FormatStatusDuration(status, showTenthsOverTenSeconds, showTimer);
        var remainingText = string.IsNullOrEmpty(timerText) ? string.Empty : $" {timerText}";
        return $"{status.Name}{stackText}{remainingText}";
    }

    private static string FormatStatusDuration(
        StatusSnapshot status,
        bool showTenthsOverTenSeconds = false,
        bool showTimer = true,
        string emptyText = "")
    {
        if (!showTimer)
        {
            return emptyText;
        }

        if (status.RemainingTime <= 0.0f)
        {
            return emptyText;
        }

        return $"{FormatStatusDurationNumber(status.RemainingTime, showTenthsOverTenSeconds)}s";
    }

    private static string FormatStatusDurationNumber(float remainingTime, bool showTenthsOverTenSeconds)
    {
        return remainingTime >= 10.0f && !showTenthsOverTenSeconds
            ? $"{remainingTime:0}"
            : $"{remainingTime:0.0}";
    }

    private static void DrawGameIcon(uint iconId, float iconSize, string tooltip)
    {
        if (iconId == 0)
        {
            ImGui.TextDisabled("-");
            return;
        }

        var size = new Vector2(Math.Clamp(iconSize, 12.0f, 48.0f));
        ISharedImmediateTexture? texture;
        try
        {
            texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
        }
        catch
        {
            ImGui.TextDisabled("-");
            return;
        }

        var wrap = texture.GetWrapOrDefault();
        if (wrap is null)
        {
            ImGui.TextDisabled("-");
            return;
        }

        ImGui.Image(wrap.Handle, size);
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(tooltip);
        }
    }

    private static void DrawCenteredGameIcon(uint iconId, float iconSize, string tooltip)
    {
        CenterNextItem(Math.Clamp(iconSize, 12.0f, 48.0f));
        DrawGameIcon(iconId, iconSize, tooltip);
    }

    private static void DrawCenteredIconText(uint iconId, float iconSize, string text, string tooltip)
    {
        var clampedIconSize = Math.Clamp(iconSize, 12.0f, 48.0f);
        var textWidth = ImGui.CalcTextSize(text).X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var groupWidth = clampedIconSize + spacing + textWidth;
        var shouldCenter = groupWidth <= ImGui.GetContentRegionAvail().X;
        if (shouldCenter)
        {
            CenterNextItem(groupWidth);
        }

        var iconTop = ImGui.GetCursorPosY();
        DrawGameIcon(iconId, clampedIconSize, tooltip);
        ImGui.SameLine();
        var textOffset = MathF.Max(0.0f, (clampedIconSize - ImGui.GetTextLineHeight()) * 0.5f);
        ImGui.SetCursorPosY(iconTop + textOffset);

        if (shouldCenter)
        {
            ImGui.TextUnformatted(text);
        }
        else
        {
            ImGui.TextWrapped(text);
        }
    }

    private static void DrawAmountBullet(uint amount)
    {
        ImGui.BulletText($"Amount: {FormatAmount(amount)}");
        DrawAmountTooltip();
    }

    private static void DrawAmountValue(CombatEventRecord combatEvent)
    {
        DrawCenteredText(FormatSignedEventAmount(combatEvent), GetEventAmountColor(combatEvent));
        DrawAmountTooltip(combatEvent.Kind);
    }

    private static void DrawAmountTooltip(DeathEventKind kind = DeathEventKind.Damage)
    {
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip(kind == DeathEventKind.Heal
                ? "Actual HP restored by this healing event."
                : "Actual damage taken after mitigation, shields, blocks, parries, and other damage reductions are applied.");
        }
    }

    private static string FormatAmount(uint amount)
    {
        return FormatAmount((ulong)amount);
    }

    private static string FormatAmount(ulong amount)
    {
        return amount > 0 ? amount.ToString("N0") : "-";
    }

    private static bool EventHasSignedAmount(CombatEventRecord combatEvent)
    {
        return combatEvent.Amount > 0 &&
            combatEvent.Kind is DeathEventKind.Damage or DeathEventKind.Heal;
    }

    private static string FormatSignedEventAmountSuffix(CombatEventRecord combatEvent)
    {
        return EventHasSignedAmount(combatEvent)
            ? $" {FormatSignedEventAmount(combatEvent)}"
            : string.Empty;
    }

    private static string FormatSignedEventAmount(CombatEventRecord combatEvent)
    {
        if (combatEvent.Amount == 0)
        {
            return "-";
        }

        return combatEvent.Kind switch
        {
            DeathEventKind.Damage => FormatSignedDamageAmount(combatEvent.Amount),
            DeathEventKind.Heal => $"+{FormatAmount(combatEvent.Amount)}",
            _ => FormatAmount(combatEvent.Amount),
        };
    }

    private static string FormatSignedDamageAmount(ulong amount)
    {
        return amount > 0 ? $"-{FormatAmount(amount)}" : "-";
    }

    private static Vector4 GetEventAmountColor(CombatEventRecord combatEvent)
    {
        return combatEvent.Kind switch
        {
            DeathEventKind.Damage when combatEvent.Amount > 0 => DamageColor,
            DeathEventKind.Heal when combatEvent.Amount > 0 => HealColor,
            _ => DisabledColor,
        };
    }

    private static Vector4 GetHealIncreaseBarColor()
    {
        var lightGreen = ActiveThemeUsesLightPanels()
            ? new Vector4(0.50f, 0.92f, 0.52f, 1.0f)
            : new Vector4(0.68f, 1.0f, 0.62f, 1.0f);
        return BlendColors(HpBarColor, lightGreen, 0.62f) with { W = 1.0f };
    }

    private static Vector4 GetHpBarLabelColor(
        float labelCenterOffset,
        float hpWidth,
        float shieldWidth,
        float overflowShieldWidth)
    {
        var backdrop = GetHpBarLabelBackdropColor(labelCenterOffset, hpWidth, shieldWidth, overflowShieldWidth);
        return GetReadableTextColorForBackground(backdrop, 4.5f);
    }

    private static Vector4 GetHpBarLabelBackdropColor(
        float labelCenterOffset,
        float hpWidth,
        float shieldWidth,
        float overflowShieldWidth)
    {
        if (overflowShieldWidth > 0.0f && labelCenterOffset <= overflowShieldWidth)
        {
            return ShieldBarColor;
        }

        if (hpWidth > 0.0f && labelCenterOffset <= hpWidth)
        {
            return HpBarColor;
        }

        if (shieldWidth > 0.0f && labelCenterOffset <= hpWidth + shieldWidth)
        {
            return ShieldBarColor;
        }

        return BarBackgroundColor;
    }

    private static Vector4 GetHpBarLabelShadowColor(Vector4 labelColor)
    {
        return GetColorLuminance(labelColor) >= 0.50f
            ? new Vector4(0.02f, 0.025f, 0.03f, 0.62f)
            : new Vector4(1.0f, 1.0f, 0.96f, 0.34f);
    }

    private static string FormatEnemyHpPercent(EnemyHpSnapshot enemy)
    {
        if (enemy.MaxHp == 0)
        {
            return "-";
        }

        var ratio = Math.Clamp((double)enemy.CurrentHp / enemy.MaxHp, 0.0, 1.0);
        return $"{ratio:P1}";
    }

    private static ulong? GetIncomingDamageAmount(CombatEventRecord combatEvent)
    {
        return combatEvent.Kind == DeathEventKind.Damage && combatEvent.Amount > 0
            ? combatEvent.Amount
            : null;
    }

    private static ulong? GetIncomingDamageAmount(IReadOnlyList<CombatEventRecord> events)
    {
        var total = events
            .Where(combatEvent => combatEvent.Kind == DeathEventKind.Damage && combatEvent.Amount > 0)
            .Aggregate(0UL, (sum, combatEvent) => sum + combatEvent.Amount);
        return total == 0 ? null : total;
    }

    private static void DrawHpShieldBar(
        uint currentHp,
        uint shieldHp,
        uint maxHp,
        string id,
        ulong? incomingDamage = null,
        bool showOverkillLine = false,
        bool centerLabel = false,
        string? tooltipDetail = null,
        bool valueOnlyTooltip = false,
        HpBarHealChange? healChange = null,
        HpBarDamageChange? damageChange = null)
    {
        if (maxHp == 0)
        {
            ImGui.TextDisabled(FormatHp(currentHp, shieldHp, maxHp));
            if (showOverkillLine)
            {
                DrawOverkillLine(currentHp, shieldHp, maxHp, incomingDamage);
            }

            return;
        }

        var width = GetHpShieldBarWidth(maxHp);
        var height = MathF.Max(ImGui.GetTextLineHeight() + 4.0f, 20.0f);
        var position = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, height);

        ImGui.InvisibleButton($"##{id}", size);
        var drawList = ImGui.GetWindowDrawList();
        var barEnd = position + size;
        var rounding = 3.0f;
        drawList.AddRectFilled(position, barEnd, ImGui.GetColorU32(BarBackgroundColor), rounding);

        var hpRatio = Math.Clamp((double)currentHp / maxHp, 0.0, 1.0);
        var rawShieldRatio = Math.Clamp((double)shieldHp / maxHp, 0.0, double.PositiveInfinity);
        var missingHpRatio = Math.Max(0.0, 1.0 - hpRatio);
        var shieldRatio = Math.Min(rawShieldRatio, missingHpRatio);
        var overflowShieldRatio = Math.Clamp(rawShieldRatio - shieldRatio, 0.0, 1.0);
        var hpWidth = (float)(size.X * hpRatio);
        var shieldWidth = (float)(size.X * shieldRatio);
        var overflowShieldWidth = (float)(size.X * overflowShieldRatio);
        var damageAmount = incomingDamage.GetValueOrDefault();
        var clearlyUnsurvivable = incomingDamage is not null &&
            damageAmount >= (ulong)maxHp + ClearlyUnsurvivableOverMaxHp;

        var healStartRatio = 0.0;
        var healEndRatio = 0.0;
        if (healChange is not null && currentHp > healChange.PreviousCurrentHp)
        {
            healStartRatio = Math.Clamp((double)healChange.PreviousCurrentHp / maxHp, 0.0, 1.0);
            healEndRatio = Math.Clamp((double)currentHp / maxHp, 0.0, 1.0);
        }

        if (healEndRatio > healStartRatio)
        {
            var preHealHpWidth = (float)(size.X * healStartRatio);
            var postHealHpWidth = (float)(size.X * healEndRatio);
            drawList.AddRectFilled(position, new Vector2(position.X + postHealHpWidth, barEnd.Y), ImGui.GetColorU32(HpBarColor), rounding);
            DrawHealGrowthOverlay(drawList, position, size, preHealHpWidth, postHealHpWidth);
        }
        else if (hpWidth > 0.0f)
        {
            drawList.AddRectFilled(position, new Vector2(position.X + hpWidth, barEnd.Y), ImGui.GetColorU32(HpBarColor), rounding);
        }

        if (shieldWidth > 0.0f)
        {
            var shieldStart = new Vector2(position.X + hpWidth, position.Y);
            var shieldEnd = new Vector2(position.X + hpWidth + shieldWidth, barEnd.Y);
            drawList.AddRectFilled(shieldStart, shieldEnd, ImGui.GetColorU32(ShieldBarColor), rounding);
        }

        if (overflowShieldWidth > 0.0f)
        {
            drawList.AddRectFilled(position, new Vector2(position.X + overflowShieldWidth, barEnd.Y), ImGui.GetColorU32(ShieldBarColor), rounding);
        }

        if (damageChange is not null && healEndRatio <= healStartRatio)
        {
            DrawDamageLossOverlay(drawList, position, size, currentHp, shieldHp, maxHp, damageChange);
        }

        drawList.AddRect(position, barEnd, ImGui.GetColorU32(BarBorderColor), rounding);

        var label = FormatHpForBar(currentHp, shieldHp, maxHp, size.X);
        var textSize = ImGui.CalcTextSize(label);
        var textPosition = new Vector2(
            centerLabel ? position.X + MathF.Max(4.0f, (size.X - textSize.X) * 0.5f) : position.X + 4.0f,
            position.Y + MathF.Max(1.0f, (size.Y - textSize.Y) * 0.5f));
        var labelCenterOffset = Math.Clamp((textPosition.X - position.X) + (textSize.X * 0.5f), 0.0f, size.X);
        var labelColor = GetHpBarLabelColor(labelCenterOffset, hpWidth, shieldWidth, overflowShieldWidth);
        var shadowColor = GetHpBarLabelShadowColor(labelColor);
        ImGui.PushClipRect(position, barEnd, true);
        drawList.AddText(textPosition + new Vector2(1.0f, 1.0f), ImGui.GetColorU32(shadowColor), label);
        drawList.AddText(textPosition, ImGui.GetColorU32(labelColor), label);
        ImGui.PopClipRect();

        if (ImGui.IsItemHovered())
        {
            var tooltip = valueOnlyTooltip
                ? FormatHpValueOnly(currentHp, shieldHp, maxHp)
                : FormatHp(currentHp, shieldHp, maxHp);
            if (!valueOnlyTooltip && !string.IsNullOrWhiteSpace(tooltipDetail))
            {
                tooltip += $"\n{tooltipDetail}";
            }

            if (!valueOnlyTooltip && clearlyUnsurvivable)
            {
                tooltip += "\nLikely failed mechanic or vastly insufficient mitigation related death.";
            }

            SetThemedTooltip(tooltip);
        }

        if (showOverkillLine)
        {
            DrawOverkillLine(currentHp, shieldHp, maxHp, incomingDamage);
        }
    }

    private static void DrawHealGrowthOverlay(ImDrawListPtr drawList, Vector2 position, Vector2 size, float preHealHpWidth, float postHealHpWidth)
    {
        var growthWidth = postHealHpWidth - preHealHpWidth;
        if (growthWidth <= 0.0f)
        {
            return;
        }

        var startX = position.X + preHealHpWidth;
        var endX = position.X + MathF.Max(postHealHpWidth - 1.0f, preHealHpWidth + 1.0f);
        if (endX <= startX)
        {
            return;
        }

        var top = position.Y + 3.0f;
        var bottom = position.Y + MathF.Max(4.0f, size.Y - 3.0f);
        if (bottom <= top)
        {
            return;
        }

        var overlayColor = GetHealIncreaseBarColor();
        drawList.AddRectFilled(
            new Vector2(startX, top),
            new Vector2(endX, bottom),
            ImGui.GetColorU32(overlayColor with { W = ActiveThemeUsesLightPanels() ? 0.72f : 0.82f }),
            1.5f);
    }

    private static void DrawDamageLossOverlay(
        ImDrawListPtr drawList,
        Vector2 position,
        Vector2 size,
        uint currentHp,
        uint shieldHp,
        uint maxHp,
        HpBarDamageChange damageChange)
    {
        if (maxHp == 0)
        {
            return;
        }

        var top = position.Y + 2.0f;
        var bottom = position.Y + MathF.Max(3.0f, size.Y - 2.0f);
        if (bottom <= top)
        {
            return;
        }

        var currentHpRatio = Math.Clamp((double)currentHp / maxHp, 0.0, 1.0);
        var resultHpRatio = Math.Clamp((double)damageChange.ResultCurrentHp / maxHp, 0.0, 1.0);
        DrawDamageLossSegment(
            drawList,
            position,
            top,
            bottom,
            (float)(size.X * resultHpRatio),
            (float)(size.X * currentHpRatio),
            GetHpDamageLossBarColor());

        var missingHpRatio = Math.Max(0.0, 1.0 - currentHpRatio);
        var shieldRatio = Math.Min(Math.Clamp((double)shieldHp / maxHp, 0.0, double.PositiveInfinity), missingHpRatio);
        var resultShieldRatio = Math.Min(Math.Clamp((double)damageChange.ResultShieldHp / maxHp, 0.0, double.PositiveInfinity), missingHpRatio);
        DrawDamageLossSegment(
            drawList,
            position,
            top,
            bottom,
            (float)(size.X * (currentHpRatio + resultShieldRatio)),
            (float)(size.X * (currentHpRatio + shieldRatio)),
            GetShieldDamageLossBarColor());
    }

    private static void DrawDamageLossSegment(
        ImDrawListPtr drawList,
        Vector2 position,
        float top,
        float bottom,
        float startOffset,
        float endOffset,
        Vector4 color)
    {
        if (endOffset - startOffset <= 0.5f)
        {
            return;
        }

        var startX = position.X + startOffset;
        var endX = position.X + endOffset;
        drawList.AddRectFilled(
            new Vector2(startX, top),
            new Vector2(endX, bottom),
            ImGui.GetColorU32(color),
            1.0f);

        var edgeWidth = MathF.Min(2.0f, MathF.Max(1.0f, (endX - startX) * 0.12f));
        drawList.AddRectFilled(
            new Vector2(startX, top + 1.0f),
            new Vector2(startX + edgeWidth, bottom - 1.0f),
            ImGui.GetColorU32(color with { W = MathF.Min(1.0f, color.W + 0.14f) }),
            1.0f);
    }

    private static Vector4 GetHpDamageLossBarColor()
    {
        var lossColor = ActiveThemeUsesLightPanels()
            ? new Vector4(0.04f, 0.16f, 0.08f, 0.64f)
            : new Vector4(0.01f, 0.08f, 0.04f, 0.72f);
        return BlendColors(HpBarColor, lossColor, ActiveThemeUsesLightPanels() ? 0.76f : 0.70f) with { W = lossColor.W };
    }

    private static Vector4 GetShieldDamageLossBarColor()
    {
        var lossColor = ActiveThemeUsesLightPanels()
            ? new Vector4(0.04f, 0.12f, 0.18f, 0.62f)
            : new Vector4(0.01f, 0.05f, 0.09f, 0.70f);
        return BlendColors(ShieldBarColor, lossColor, ActiveThemeUsesLightPanels() ? 0.74f : 0.68f) with { W = lossColor.W };
    }

    private static float GetHpShieldBarWidth(uint maxHp)
    {
        if (maxHp == 0)
        {
            return MathF.Max(MinimumHpShieldBarWidth, ImGui.GetContentRegionAvail().X);
        }

        var availableWidth = ImGui.GetContentRegionAvail().X;
        return MathF.Max(MinimumHpShieldBarWidth, availableWidth);
    }

    private static string FormatHpForBar(uint currentHp, uint shieldHp, uint maxHp, float width)
    {
        var availableTextWidth = MathF.Max(0.0f, width - 8.0f);
        var effectiveHp = (ulong)currentHp + shieldHp;
        var candidates = maxHp == 0
            ? new[]
            {
                FormatHp(currentHp, shieldHp, maxHp),
                $"{FormatCompactAmount(currentHp)} + {FormatCompactAmount(shieldHp)} shield",
                $"{FormatCompactAmount(effectiveHp)} total",
            }
            : new[]
            {
                FormatHp(currentHp, shieldHp, maxHp),
                $"{currentHp:N0} + {shieldHp:N0} / {maxHp:N0} ({(double)effectiveHp / maxHp:P0})",
                $"{FormatCompactAmount(currentHp)} + {FormatCompactAmount(shieldHp)} / {FormatCompactAmount(maxHp)} ({(double)effectiveHp / maxHp:P0})",
                $"{(double)effectiveHp / maxHp:P0}",
            };

        foreach (var candidate in candidates)
        {
            if (ImGui.CalcTextSize(candidate).X <= availableTextWidth)
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static string FormatCompactAmount(ulong amount)
    {
        if (amount >= 1_000_000)
        {
            return $"{amount / 1_000_000.0:0.#}m";
        }

        return amount >= 1_000
            ? $"{amount / 1_000.0:0.#}k"
            : amount.ToString("N0");
    }

    private static void DrawOverkillLine(uint currentHp, uint shieldHp, uint maxHp, ulong? incomingDamage)
    {
        var damageAmount = incomingDamage.GetValueOrDefault();
        var overkillDisplay = GetOverkillDisplay(currentHp, incomingDamage);
        DrawCenteredText(overkillDisplay.Text, overkillDisplay.Color);

        if (ImGui.IsItemHovered())
        {
            var tooltip = incomingDamage is null
                ? "No incoming damage amount was captured for this selected event."
                : $"Incoming damage: {damageAmount:N0}\nHP before hit: {currentHp:N0}\nCaptured shield: {shieldHp:N0}\n{overkillDisplay.TooltipLine}";
            if (maxHp > 0)
            {
                tooltip += $"\nMax HP: {maxHp:N0}";
            }

            SetThemedTooltip(tooltip);
        }
    }

    private static OverkillDisplay GetOverkillDisplay(uint currentHp, ulong? incomingDamage)
    {
        if (incomingDamage is null)
        {
            return new OverkillDisplay(
                "Overkill: -",
                "-",
                DisabledColor,
                "No incoming damage amount was captured for this selected event.");
        }

        var damageAmount = incomingDamage.Value;
        if (damageAmount > currentHp)
        {
            var overkillAmount = damageAmount - currentHp;
            return new OverkillDisplay(
                $"Overkill: {overkillAmount:N0}",
                FormatWidgetAmount(overkillAmount),
                OverkillColor,
                $"Overkilled by {overkillAmount:N0}.");
        }

        if (damageAmount == currentHp)
        {
            return new OverkillDisplay(
                "Exact lethal hit",
                "Exact",
                DisabledColor,
                "Captured hit exactly matched HP before hit.");
        }

        return new OverkillDisplay(
            "No overkill. Follow-up non-hit KO.",
            "Non-hit KO",
            WarningColor,
            "Captured hit was non-lethal based on HP before hit. The KO came from a follow-up non-hit event.");
    }

    private static string FormatHp(uint currentHp, uint shieldHp, uint maxHp)
    {
        var effectiveHp = (ulong)currentHp + shieldHp;
        return maxHp == 0
            ? $"{currentHp:N0} + {shieldHp:N0} shield"
            : $"{currentHp:N0} + {shieldHp:N0} shield / {maxHp:N0} ({(double)effectiveHp / maxHp:P0})";
    }

    private static string FormatHpValueOnly(uint currentHp, uint shieldHp, uint maxHp)
    {
        return maxHp == 0
            ? $"{currentHp:N0} + {shieldHp:N0} shield"
            : $"{currentHp:N0} + {shieldHp:N0} shield / {maxHp:N0}";
    }

    private static string FormatEventFlags(CombatEventRecord combatEvent)
    {
        var flags = new List<string>();
        if (combatEvent.DamageType != DamageType.Unknown)
        {
            flags.Add(combatEvent.DamageType.ToString());
        }

        if (combatEvent.Critical)
        {
            flags.Add("Crit");
        }

        if (combatEvent.DirectHit)
        {
            flags.Add("Direct");
        }

        if (combatEvent.Blocked)
        {
            flags.Add("Blocked");
        }

        if (combatEvent.Parried)
        {
            flags.Add("Parried");
        }

        if (!string.IsNullOrWhiteSpace(combatEvent.Detail) &&
            !flags.Any(flag => string.Equals(flag, combatEvent.Detail, StringComparison.OrdinalIgnoreCase)))
        {
            flags.Add(combatEvent.Detail);
        }

        return flags.Count == 0 ? "-" : string.Join(", ", flags);
    }

    private static string FormatCombatTimer(float elapsedSeconds)
    {
        var totalSeconds = (int)MathF.Max(0.0f, elapsedSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private string FormatLocalClockTime(DateTime utcDateTime)
    {
        var localDateTime = utcDateTime.Kind switch
        {
            DateTimeKind.Local => utcDateTime,
            DateTimeKind.Utc => utcDateTime.ToLocalTime(),
            _ => DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc).ToLocalTime(),
        };

        return configuration.ClockDisplayMode == ClockDisplayMode.TwelveHour
            ? $"{localDateTime:h:mm tt} local"
            : $"{localDateTime:HH:mm} local";
    }

    private static string FormatRelativeToDeath(DateTime deathSeenAtUtc, DateTime eventSeenAtUtc)
    {
        var deltaSeconds = (deathSeenAtUtc - eventSeenAtUtc).TotalSeconds;
        return deltaSeconds >= 0
            ? $"-{deltaSeconds:0.00}s"
            : $"+{Math.Abs(deltaSeconds):0.00}s";
    }

    private static string FormatPreciseRelativeToDeath(DateTime deathSeenAtUtc, DateTime eventSeenAtUtc)
    {
        var deltaSeconds = (deathSeenAtUtc - eventSeenAtUtc).TotalSeconds;
        return deltaSeconds >= 0
            ? $"-{deltaSeconds:0.000}s"
            : $"+{Math.Abs(deltaSeconds):0.000}s";
    }
}
