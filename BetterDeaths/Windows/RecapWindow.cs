using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
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
    private bool showThankYouNoticeOnDemand;
    private bool windowStylePushed;
    private string debugTextFilter = string.Empty;
    private int debugActorControlCategoryFilterIndex;
    private int? pendingMaxRecordedPulls;
    private float currentMainWindowBackgroundOpacity = Plugin.DefaultMainWindowBackgroundOpacity;
    private DataPageSnapshot dataPageSnapshot = DataPageSnapshot.Empty;
    private DateTime dataPageSnapshotRefreshedAtUtc = DateTime.MinValue;
    private readonly HashSet<string> expandedTimelineCauseRows = new(StringComparer.Ordinal);
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
    private const uint AllRecordedPullDuties = uint.MaxValue;
    private const string CurrentChangelogVersion = "0.1.0.136";
    private const float LeadUpHistorySeconds = 10.0f;
    private const float PullBodyIndent = 8.0f;
    private const float DeathDetailIndent = 8.0f;
    private const float SectionBodyIndent = 8.0f;
    private const float ReviewPaneContentIndent = 8.0f;
    private const float ReviewPaneHorizontalPadding = 9.0f;
    private const float PullBrowserCollapsedWidth = 60.0f;
    private const float RecordedPullDutyFilterComboWidth = 260.0f;
    private const float PullBrowserExpandedWidth = RecordedPullDutyFilterComboWidth + (ReviewPaneHorizontalPadding * 2.0f);
    private const float PullBrowserHeaderButtonInset = 6.0f;
    private const float MinimumTimelinePaneWidth = 360.0f;
    private const float MinimumHpShieldBarWidth = 24.0f;
    private const string ThemeNewBadgeText = "New";
    private const uint ClearlyUnsurvivableOverMaxHp = 300_000;
    private const string CompactInfoSeparator = " \u00B7 ";
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

    private sealed record LeadUpSummaryRow(
        DateTime AnchorSeenAtUtc,
        HpHistoryDisplayRow Row,
        IReadOnlyList<CombatEventRecord> Events,
        IReadOnlyList<StatusSnapshot> SourceStatuses);

    private sealed record EventHpDisplay(
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        string TooltipDetail);

    private sealed record LeadUpTimelineRow(
        DateTime SeenAtUtc,
        float PullElapsedSeconds,
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        IReadOnlyList<StatusSnapshot> Statuses,
        IReadOnlyList<StatusSnapshot> NearbyHpStatuses,
        IReadOnlyList<StatusSnapshot> SourceStatuses,
        CombatEventRecord? Event,
        string? HpTooltipDetail);

    private sealed record DerivedHpState(
        DateTime EventSeenAtUtc,
        string SourceName,
        string ActionName,
        uint Amount,
        uint SourceCurrentHp,
        uint SourceShieldHp,
        uint SourceMaxHp,
        uint DerivedCurrentHp,
        uint DerivedShieldHp);

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
        IReadOnlyList<PartyDeathRecord> Deaths,
        DeathSelectionSource Source,
        RecordedPullSummary? RecordedPull);

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
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8.0f, 6.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(7.0f, 6.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, activeTheme.ModernFrameBorderSize);
        }

        public void Dispose()
        {
            ImGui.PopStyleVar(5);
            ImGui.PopStyleColor(14);
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
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 9.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8.0f, 7.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, activeTheme.ModernFrameBorderSize);
        }

        public void Dispose()
        {
            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(16);
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
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6.0f, 4.0f));
        }

        public void Dispose()
        {
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(6);
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
        Flags |= ImGuiWindowFlags.NoScrollbar;
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        ApplyConfiguredTheme();
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
        activeTheme = BetterDeathsThemeCatalog.GetTheme(configuration.Theme);
    }

    public override void Draw()
    {
        DrawPluginUpdateBanner();

        if (showThankYouNoticeOnDemand || plugin.ShouldShowThankYouNotice())
        {
            DrawOneTimeThankYouNotice(showThankYouNoticeOnDemand);
            return;
        }

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
            ? MainPage.Example
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
            ? MainPage.Example
            : MainPage.Review;
    }

    private void DrawModernShell()
    {
        using var shellStyle = new ModernStyleScope(currentMainWindowBackgroundOpacity);
        if (ImGui.BeginChild("##BetterDeathsModernShell", Vector2.Zero, false, ImGuiWindowFlags.NoScrollbar))
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
    }

    private void DrawModernNavigation()
    {
        DrawModernNavButton("Review", MainPage.Review);
        ImGui.SameLine();
        DrawModernNavButton("Example", MainPage.Example);
        ImGui.SameLine();
        DrawModernNavButton("Customize", MainPage.Customize);
        if (!configuration.HasChangedTheme)
        {
            DrawFloatingNewBadgeOverLastItem();
        }

        ImGui.SameLine();
        DrawModernNavButton("Data", MainPage.Data);
        ImGui.SameLine();
        DrawModernNavButton("Updates", MainPage.Updates, ShouldHighlightChangelogTab());
        if (showDebugTab)
        {
            ImGui.SameLine();
            DrawModernNavButton("Debug", MainPage.Debug);
        }
    }

    private void DrawModernPageContent()
    {
        switch (currentMainPage)
        {
            case MainPage.Example:
                DrawExamplePullTab();
                break;
            case MainPage.Customize:
                DrawCustomizePage();
                break;
            case MainPage.Data:
                DrawReviewPanel("##DataModern", Vector2.Zero, DrawDataPage);
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

    private void DrawModernNavButton(string label, MainPage page, bool highlight = false)
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
            : selected ? GetModernNavSelectedTextColor() : ModernTextColor;

        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ModernNavButtonActiveColor);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        if (ImGui.Button($"{label}##MainNav{page}", new Vector2(118.0f, 30.0f)))
        {
            currentMainPage = page;
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
        return ActiveThemeUsesLightPanels()
            ? new Vector4(0.15f, 0.23f, 0.28f, 0.105f)
            : new Vector4(1.0f, 1.0f, 1.0f, 0.065f);
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
                deaths,
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
                plugin.CurrentDeaths,
                DeathSelectionSource.Current,
                null));
        }

        var hasCurrentPull = pulls.Count > 0;
        var visibleRecordedPulls = GetVisibleRecordedPulls().ToList();
        for (var i = 0; i < visibleRecordedPulls.Count; i++)
        {
            var summary = visibleRecordedPulls[i].Summary;
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
                deaths,
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
        const float dividerWidth = 1.0f;
        var pullBrowserCollapsed = showPullBrowser && configuration.PullBrowserCollapsed;
        var rightWidth = pullBrowserCollapsed
            ? 0.0f
            : Math.Clamp(available.X * 0.34f, 430.0f, 640.0f);
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
                if (available.X - rightWidth - MinimumTimelinePaneWidth - PullBrowserExpandedWidth - dividerWidth < 0.0f)
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

                pullBrowserWidth = PullBrowserExpandedWidth;
                pullBrowserControlWidth = pullBrowserWidth;
            }
        }

        var centerWidth = 0.0f;
        if (pullBrowserCollapsed)
        {
            var reviewWidth = available.X - pullBrowserControlWidth - dividerWidth;
            centerWidth = MathF.Max(0.0f, reviewWidth * 0.5f);
            rightWidth = MathF.Max(0.0f, reviewWidth - centerWidth);
        }
        else
        {
            centerWidth = available.X - rightWidth - pullBrowserControlWidth - dividerWidth;
        }

        if (centerWidth < MinimumTimelinePaneWidth)
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
                selection));
        DrawVerticalReviewDivider($"{idPrefix}TimelineDivider", available.Y);
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
                if (showPullBrowser)
                {
                    if (configuration.PullBrowserCollapsed)
                    {
                        DrawReviewPane(
                            $"##{idPrefix}PullBrowserStackedCollapsed",
                            new Vector2(0.0f, 38.0f),
                            () => DrawCollapsedPullBrowser(idPrefix));
                    }
                    else
                    {
                        DrawReviewPane(
                            $"##{idPrefix}PullBrowserStacked",
                            new Vector2(0.0f, MathF.Min(260.0f, MathF.Max(170.0f, innerAvailable.Y * 0.28f))),
                            () => DrawPullBrowser(
                                pulls,
                                idPrefix,
                                selection));
                    }

                    DrawHorizontalReviewDivider(innerAvailable.X);
                }

                DrawReviewPane(
                    $"##{idPrefix}TimelineStacked",
                    new Vector2(0.0f, MathF.Min(330.0f, MathF.Max(210.0f, innerAvailable.Y * 0.35f))),
                    () => DrawSelectedPullTimeline(
                        selectedPull,
                        idPrefix,
                        selection));
                DrawHorizontalReviewDivider(innerAvailable.X);
                DrawReviewPane(
                    $"##{idPrefix}DeathDetailsStacked",
                    Vector2.Zero,
                    () => DrawSelectedDeathPanel(selectedPull, selectedDeath, idPrefix));
            },
            indentContent: false);
    }

    private static void DrawReviewPanel(string id, Vector2 size, Action draw, bool indentContent = true)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, ModernPanelAltColor);
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, GetTableRowAltColor());
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, indentContent ? new Vector2(ReviewPaneContentIndent, 6.0f) : Vector2.Zero);
        if (ImGui.BeginChild(id, size, false, ImGuiWindowFlags.NoScrollbar))
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

    private static void DrawReviewPane(string id, Vector2 size, Action draw)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(ReviewPaneHorizontalPadding, 6.0f));
        if (ImGui.BeginChild(id, size, false, ImGuiWindowFlags.NoScrollbar))
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

    private static void DrawHorizontalReviewDivider(float width)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var y = cursor.Y + 3.0f;
        drawList.AddLine(
            new Vector2(cursor.X, y),
            new Vector2(cursor.X + MathF.Max(0.0f, width), y),
            ImGui.GetColorU32(ModernDividerColor),
            1.0f);
        ImGui.Dummy(new Vector2(width, 7.0f));
    }

    private void DrawPullBrowser(
        IReadOnlyList<ReviewPull> pulls,
        string idPrefix,
        ReviewSelectionState selection)
    {
        using var paneIndent = new ImGuiIndentScope(ReviewPaneContentIndent);
        DrawPullBrowserHeader(idPrefix);
        DrawRecordedPullControls();
        ImGui.Separator();

        if (pulls.Count == 0)
        {
            ImGui.TextDisabled("No pull data available.");
            return;
        }

        if (ImGui.BeginChild($"##{idPrefix}PullRows", Vector2.Zero, false, ImGuiWindowFlags.NoScrollbar))
        {
            foreach (var pull in pulls)
            {
                var selected = string.Equals(selection.PullKey, pull.Key, StringComparison.Ordinal);
                var rowLabel = $"{pull.Title}###PullRow{idPrefix}{pull.Key}";
                if (ImGui.Selectable(rowLabel, selected))
                {
                    selection.PullKey = pull.Key;
                    SelectDefaultDeathForPull(pull, selection);
                }

                ImGui.TextDisabled(FormatPullDutyInfo(pull));
                if (pull.Source == DeathSelectionSource.Recorded &&
                    !string.IsNullOrWhiteSpace(pull.Subtitle))
                {
                    ImGui.TextDisabled(pull.Subtitle);
                }

                ImGui.Spacing();
            }
        }

        ImGui.EndChild();
    }

    private void DrawPullBrowserHeader(string idPrefix)
    {
        ImGui.TextColored(LeadUpGoldColor, "Pulls");

        var style = ImGui.GetStyle();
        var iconButtonWidth = ImGui.GetFrameHeight();
        var buttonWidth = (iconButtonWidth * 2.0f) + style.ItemSpacing.X;
        var buttonX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - buttonWidth - PullBrowserHeaderButtonInset;

        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX() + style.ItemSpacing.X, buttonX));
        ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
        DrawClearRecordedPullsButton($"ClearRecordedPullsModern{idPrefix}", clearSelection: true);
        ImGui.SameLine(0.0f, style.ItemSpacing.X);
        if (DrawTransparentIconButton($"CollapsePullBrowser{idPrefix}", FontAwesomeIcon.ChevronLeft))
        {
            plugin.SetPullBrowserCollapsed(true);
        }

        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Collapse pulls");
        }

        ImGui.PopStyleColor();

        ImGui.TextDisabled("Choose a pull to review.");
    }

    private void DrawCollapsedPullBrowser(string idPrefix)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
        if (DrawCenteredTransparentIconButton($"ExpandPullBrowser{idPrefix}", FontAwesomeIcon.ChevronRight))
        {
            plugin.SetPullBrowserCollapsed(false);
        }

        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Expand pulls");
        }
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
        if (ImGui.BeginChild($"##{id}", size, false, ImGuiWindowFlags.NoScrollbar))
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
            if (ImGui.BeginChild($"##{id}Rows", new Vector2(0.0f, rowsHeight), false, ImGuiWindowFlags.NoScrollbar))
            {
                foreach (var pull in pulls)
                {
                    var selected = string.Equals(selection.PullKey, pull.Key, StringComparison.Ordinal);
                    var label = GetCollapsedPullLabel(pull);
                    if (DrawCollapsedPullButton(label, $"CollapsedPull{id}{pull.Key}", selected))
                    {
                        selection.PullKey = pull.Key;
                        SelectDefaultDeathForPull(pull, selection);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        SetThemedTooltip(FormatCollapsedPullTooltip(pull));
                    }
                }
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

    private bool DrawCollapsedPullButton(string label, string id, bool selected)
    {
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetTextLineHeightWithSpacing();
        var clicked = ImGui.Selectable(
            $"##{id}",
            selected,
            default,
            new Vector2(width, height));

        var textSize = ImGui.CalcTextSize(label);
        var textPosition = new Vector2(
            start.X + MathF.Max(0.0f, (width - textSize.X) * 0.5f),
            start.Y + MathF.Max(0.0f, (height - textSize.Y) * 0.5f));
        ImGui.GetWindowDrawList().AddText(
            textPosition,
            selected ? ImGui.GetColorU32(LeadUpGoldColor) : ImGui.GetColorU32(ImGuiCol.Text),
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
        ReviewSelectionState selection)
    {
        using var paneIndent = new ImGuiIndentScope(ReviewPaneContentIndent);
        DrawTimelineSectionTitle(GetPullDeathTimelineTitle(pull), pull.Subtitle);
        if (pull.Deaths.Count == 0)
        {
            ImGui.TextDisabled("No deaths recorded for this pull.");
            return;
        }

        DrawSelectableDeathTimeline(pull, idPrefix, selection);
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
        ReviewSelectionState selection)
    {
        if (!ImGui.BeginTable($"##ModernDeathTimeline{idPrefix}{pull.Key}", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthStretch, 0.32f);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.62f);
        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthStretch, 0.72f);
        ImGui.TableSetupColumn("Fatal event", ImGuiTableColumnFlags.WidthStretch, 2.4f);
        DrawCenteredTableHeader("#", "Time", "Player", "Job", "Fatal event");

        var orderedDeaths = GetDeathsInTimelineOrder(pull.Deaths);
        for (var i = 0; i < orderedDeaths.Count; i++)
        {
            var death = orderedDeaths[i];
            var rowSelected = IsSelectedReviewDeath(death, selection);
            var causeEvents = GetTimelineCauseEvents(death);
            var causeId = $"ModernCause{idPrefix}{pull.Key}{death.MemberKey}{death.SeenAtUtc.Ticks}";
            var rowHeight = GetTimelineRowHeight(causeEvents, causeId);
            var previousPullKey = selection.PullKey;
            var previousDeathSeenAtTicks = selection.DeathSeenAtTicks;
            var previousDeathMemberKeyHash = selection.DeathMemberKeyHash;
            var rowPressed = false;
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            if (rowSelected)
            {
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(TimelineSelectedRowColor));
            }

            ImGui.TableNextColumn();
            if (DrawCenteredRowSelectable(
                (i + 1).ToString(),
                $"SelectDeath{idPrefix}{pull.Key}{death.MemberKey}{death.SeenAtUtc.Ticks}",
                rowSelected,
                rowHeight,
                out var rowSelectablePressed))
            {
                rowPressed = true;
                SelectDeath(death, selection);
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
                    causeEvents,
                    causeId,
                    () =>
                    {
                        rowPressed = true;
                        SelectDeath(death, selection);
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
        }

        ImGui.EndTable();
    }

    private void DrawSelectedDeathPanel(ReviewPull pull, PartyDeathRecord? death, string idPrefix)
    {
        using var paneIndent = new ImGuiIndentScope(ReviewPaneContentIndent);
        DrawModernSectionTitle("Selected Death", pull.Title);
        if (death is null)
        {
            ImGui.TextDisabled(pull.Deaths.Count == 0
                ? "This pull has no recorded deaths."
                : "Select a death from the timeline to inspect details.");
            return;
        }

        DrawSelectedDeathHeader(pull, death);
        var deathId = $"{idPrefix}{pull.Key}{death.MemberKey}{death.SeenAtUtc.Ticks}";
        DrawDeathDetailSwitcher(deathId);
        ImGui.Spacing();

        switch (selectedDeathDetailPage)
        {
            case DeathDetailPage.Mitigation:
                DrawExtraMitigationContext(death, deathId);
                break;
            case DeathDetailPage.LeadUp:
                DrawBetterDeathsInformationContent(death, deathId);
                break;
            default:
                DrawCauseSummary(death);
                break;
        }
    }

    private static bool DrawCenteredRowSelectable(string text, string id, bool selected, float rowHeight, out bool pressed)
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
            ImGui.GetColorU32(ImGuiCol.Text),
            text);

        return clicked;
    }

    private void DrawDeathDetailSwitcher(string deathId)
    {
        DrawDeathDetailButton("Summary", DeathDetailPage.Summary, deathId);
        ImGui.SameLine();
        DrawDeathDetailButton("Mitigation", DeathDetailPage.Mitigation, deathId);
        ImGui.SameLine();
        DrawDeathDetailButton("10s Lead-up", DeathDetailPage.LeadUp, deathId);
    }

    private void DrawDeathDetailButton(string label, DeathDetailPage page, string deathId)
    {
        var selected = selectedDeathDetailPage == page;
        var buttonColor = selected ? ModernAccentSoftColor : ModernPanelAltColor;
        var hoveredColor = selected
            ? ModernAccentSoftColor with { W = 1.0f }
            : ModernButtonHoveredColor;

        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ModernAccentSoftColor);
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? ModernAccentColor : ModernTextColor);
        if (ImGui.Button($"{label}##DeathDetail{deathId}{page}", new Vector2(112.0f, 28.0f)))
        {
            selectedDeathDetailPage = page;
        }

        ImGui.PopStyleColor(4);
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

    private static void DrawModernSectionTitle(string title, string? subtitle = null)
    {
        ImGui.TextColored(LeadUpGoldColor, title);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            ImGui.TextDisabled(subtitle);
        }
    }

    private static void DrawTimelineSectionTitle(string title, string? subtitle = null)
    {
        var startCursor = ImGui.GetCursorPos();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var helpText = "?";
        var helpSize = ImGui.CalcTextSize(helpText);

        ImGui.TextColored(LeadUpGoldColor, title);
        var afterTitleCursor = ImGui.GetCursorPos();

        ImGui.SetCursorPos(new Vector2(
            startCursor.X + MathF.Max(0.0f, availableWidth - helpSize.X - 2.0f),
            startCursor.Y));
        ImGui.TextColored(LeadUpGoldColor, helpText);
        if (ImGui.IsItemHovered())
        {
            DrawReviewLegendTooltip();
        }

        ImGui.SetCursorPos(afterTitleCursor);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            ImGui.TextDisabled(subtitle);
        }
    }

    private static void DrawReviewLegendTooltip()
    {
        BeginThemedTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 34.0f);
        ImGui.TextColored(LeadUpGoldColor, "Review legend");
        ImGui.Separator();
        DrawReviewLegendTooltipLine("KO state", "A captured character has transitioned into death.");
        DrawReviewLegendTooltipLine("Fatal event", "The fatal hit group, fatal status, or selected event around the HP transition into KO.");
        DrawReviewLegendTooltipLine("Fatal sequence", "A compact set of captured hits and combat-log confirmations around the HP transition into KO.");
        DrawReviewLegendTooltipLine("Non-hit KO", "Kept in the death timeline, but no player detail panel is shown because no fatal hit or KO status context was captured.");
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
            SelectDefaultDeathForPull(selectedPull, selection);
            return;
        }

        if (GetSelectedReviewDeath(selectedPull, selection) is null)
        {
            SelectDefaultDeathForPull(selectedPull, selection);
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

    private static void SelectDefaultDeathForPull(
        ReviewPull pull,
        ReviewSelectionState selection)
    {
        var death = GetDeathsInTimelineOrder(pull.Deaths)
            .FirstOrDefault(HasDeathDetails) ??
            GetDeathsInTimelineOrder(pull.Deaths).FirstOrDefault();
        if (death is null)
        {
            selection.DeathSeenAtTicks = null;
            selection.DeathMemberKeyHash = null;
            return;
        }

        SelectDeath(death, selection);
    }

    private static void SelectDeath(
        PartyDeathRecord death,
        ReviewSelectionState selection)
    {
        selection.DeathSeenAtTicks = death.SeenAtUtc.Ticks;
        selection.DeathMemberKeyHash = Plugin.GetMemberKeyHash(death.MemberKey);
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
            return;
        }

        DrawDeathTimeline(deaths, idSuffix);
        DrawDeathDetails(deaths, idSuffix, selectionSource: DeathSelectionSource.Current);
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
                return;
            }
        }

        if (ImGui.BeginChild($"##CurrentPullWidgetScroll{idSuffix}", Vector2.Zero, false, ImGuiWindowFlags.NoScrollbar))
        {
            DrawCurrentPullWidgetDeathTable(deaths, idSuffix);
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
            DrawWidgetCauseSummary(causeEvents, conciseMode);
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
            DrawDeathDetails(detail.Deaths, $"Pull{pullId}", selectionSource: DeathSelectionSource.Recorded, recordedPull: summary);
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
            .GroupBy(entry => entry.Summary.TerritoryId)
            .OrderByDescending(group => group.Max(entry => entry.PullNumber))
            .ThenBy(group => group
                .Select(entry => entry.Summary.TerritoryName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.OrderByDescending(entry => entry.PullNumber));
    }

    private void DrawDeathTimeline(IReadOnlyList<PartyDeathRecord> deaths, string idSuffix)
    {
        ImGui.TextUnformatted("Death timeline");
        if (!ImGui.BeginTable($"##DeathTimeline{idSuffix}", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
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
            DrawTimelineCauseText(causeEvents, $"Cause{idSuffix}{death.MemberKey}{death.SeenAtUtc.Ticks}");
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
        DrawCenteredHeaderCell("Timer");
        DrawCenteredHeaderCell("HP + shields");
        DrawCenteredHeaderCell("Events");
        DrawCenteredHeaderCell("Mits/Debuffs");
    }

    private static void DrawLeadUpEventsTableHeader()
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        DrawCenteredIconHeaderCell(FontAwesomeIcon.Clock);
        DrawCenteredHeaderCell("Source");
        DrawCenteredHeaderCell("Action");
        DrawCenteredHeaderCell("Amount");
        DrawCenteredHeaderCell("HP + shields");
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

    private static void DrawCenteredHpShieldBar(uint currentHp, uint shieldHp, uint maxHp, string id, ulong? incomingDamage = null, string? tooltipDetail = null)
    {
        var width = GetHpShieldBarWidth(maxHp);
        CenterNextItem(width);
        DrawHpShieldBar(currentHp, shieldHp, maxHp, id, incomingDamage, centerLabel: true, tooltipDetail: tooltipDetail);
    }

    private IReadOnlyList<CombatEventRecord> GetTimelineCauseEvents(PartyDeathRecord death)
    {
        return GetTimelineCauseEvents(DeathDisplaySelector.Select(death));
    }

    private static IReadOnlyList<CombatEventRecord> GetTimelineCauseEvents(DeathDisplaySelection selection)
    {
        return selection.Events
            .Where(IsFatalEvent)
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
        IReadOnlyList<CombatEventRecord> causeEvents,
        string id,
        Action? selectDeath = null,
        Action? markDeathPressed = null)
    {
        if (causeEvents.Count == 0)
        {
            DrawCenteredOrWrappedText("Non-hit KO", WarningColor);
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
        var summaryTextSize = ImGui.CalcTextSize(summary);
        var controlWidth = MathF.Max(0.0f, availableWidth);
        var controlSize = new Vector2(controlWidth, MathF.Max(ImGui.GetFrameHeight(), summaryTextSize.Y));

        CenterNextItem(controlWidth);
        var controlPosition = ImGui.GetCursorScreenPos();
        var arrowSize = MathF.Min(8.0f, MathF.Max(5.0f, summaryTextSize.Y * 0.55f));
        var arrowHitWidth = arrowSize + (style.FramePadding.X * 2.0f);
        var arrowClicked = ImGui.InvisibleButton($"##TimelineCauseArrow{id}", new Vector2(arrowHitWidth, controlSize.Y));
        var arrowHovered = ImGui.IsItemHovered();
        var afterArrowCursor = ImGui.GetCursorPos();
        var drawList = ImGui.GetWindowDrawList();
        var textY = controlPosition.Y + MathF.Max(0.0f, (controlSize.Y - summaryTextSize.Y) * 0.5f);
        var arrowX = controlPosition.X + style.FramePadding.X;
        var arrowCenterY = controlPosition.Y + (controlSize.Y * 0.5f);
        var arrowHalfSize = arrowSize * 0.5f;
        var summaryMinX = arrowX + arrowSize + style.ItemInnerSpacing.X;
        var summaryPosition = new Vector2(
            MathF.Max(summaryMinX, controlPosition.X + MathF.Max(0.0f, (controlSize.X - summaryTextSize.X) * 0.5f)),
            textY);
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
        return combatEvent.Kind == DeathEventKind.Damage && combatEvent.Amount > 0
            ? $"{combatEvent.ActionName}: {FormatAmount(combatEvent.Amount)}"
            : combatEvent.ActionName;
    }

    private void DrawWidgetCauseSummary(IReadOnlyList<CombatEventRecord> causeEvents, bool conciseMode)
    {
        var text = conciseMode
            ? FormatConciseWidgetCauseSummary(causeEvents)
            : FormatWidgetCauseSummary(causeEvents);
        DrawCenteredOrWrappedText(text, GetWidgetCauseColor(causeEvents));

        if (ImGui.IsItemHovered())
        {
            if (causeEvents.Count == 0)
            {
                SetThemedTooltip("Non-hit KO.");
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

    private string FormatWidgetCauseSummary(IReadOnlyList<CombatEventRecord> causeEvents)
    {
        if (causeEvents.Count == 0)
        {
            return "Non-hit KO";
        }

        var damageEvents = causeEvents
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .ToList();
        var totalDamage = damageEvents.Aggregate(0UL, (sum, cause) => sum + cause.Amount);
        if (damageEvents.Count > 1)
        {
            return $"{damageEvents.Count} hits | {FormatWidgetAmount(totalDamage)} total";
        }

        if (causeEvents.Count > 1 && totalDamage > 0)
        {
            return $"{causeEvents.Count} events | {FormatWidgetAmount(totalDamage)} damage";
        }

        var cause = causeEvents[0];
        return cause.Kind == DeathEventKind.Status
            ? $"{cause.ActionName} from {FormatKnownPlayerName(cause.SourceName)}"
            : $"{FormatWidgetAmount(cause.Amount)} {cause.ActionName}";
    }

    private static string FormatConciseWidgetCauseSummary(IReadOnlyList<CombatEventRecord> causeEvents)
    {
        if (causeEvents.Count == 0)
        {
            return "-";
        }

        var totalDamage = causeEvents
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .Aggregate(0UL, (sum, cause) => sum + cause.Amount);

        return totalDamage > 0 ? FormatWidgetAmount(totalDamage) : "-";
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
        var overkillDisplay = GetOverkillDisplay(snapshot.CurrentHp, snapshot.ShieldHp, incomingDamage);
        DrawCenteredText(overkillDisplay.CompactText, overkillDisplay.Color);

        if (ImGui.IsItemHovered())
        {
            var effectiveHp = (ulong)snapshot.CurrentHp + snapshot.ShieldHp;
            SetThemedTooltip(
                $"Incoming damage: {incomingDamage.Value:N0}\n" +
                $"HP plus shields before hit: {effectiveHp:N0}\n" +
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
        RecordedPullSummary? recordedPull = null)
    {
        var orderedDeaths = GetDeathsInTimelineOrder(deaths);
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
            DrawCauseSummary(death);
            var deathId = $"{idSuffix}{death.MemberKey}{death.SeenAtUtc.Ticks}";
            ImGui.Separator();
            DrawExtraMitigationContext(death, deathId);
            ImGui.Separator();
            DrawBetterDeathsInformation(death, deathId);
        }
    }

    private static bool HasDeathDetails(PartyDeathRecord death)
    {
        return DeathDisplaySelector.Select(death).Events.Count > 0 ||
            death.FatalSequence is { Events.Count: > 0 } ||
            death.FatalSequence is { LogEvents.Count: > 0 };
    }

    private static IReadOnlyList<PartyDeathRecord> GetDeathsInTimelineOrder(IReadOnlyList<PartyDeathRecord> deaths)
    {
        return deaths
            .OrderBy(death => death.SeenAtUtc)
            .ThenBy(death => death.PartyIndex)
            .ToList();
    }

    private void DrawCauseSummary(PartyDeathRecord death)
    {
        ImGui.TextUnformatted("Player death information");
        using var sectionIndent = new ImGuiIndentScope(SectionBodyIndent);
        var causeEvents = GetTimelineCauseEvents(death);
        var summaryRow = GetLeadUpSummaryRow(death);
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
            ImGui.TextColored(WarningColor, "No fatal hit was captured inside the configured event window.");
        }

        var buttonId = $"{death.MemberKey}{death.SeenAtUtc.Ticks}";
        var effectiveChannel = Plugin.GetEffectiveChatChannel(configuration.DeathChatChannel);
        ImGui.SetNextItemWidth(185.0f);
        if (ImGui.BeginCombo($"##DeathChatChannel{buttonId}", Plugin.GetChatChannelLabel(effectiveChannel)))
        {
            foreach (var option in Plugin.ChatChannelOptions)
            {
                var selected = effectiveChannel == option.Channel;
                if (ImGui.Selectable(option.Label, selected))
                {
                    plugin.SetDeathChatChannel(option.Channel);
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button($"Post information to chat##PostInfo{buttonId}"))
        {
            plugin.PrintDeathInformationToChat(death);
        }

        ImGui.Separator();
        ImGui.TextUnformatted(causeEvents.Count > 1 ? "Fatal events" : "Fatal event");
        if (causeEvents.Count == 0)
        {
            ImGui.TextColored(WarningColor, "Non-hit KO. Possible death wall, reconnect spawn KO, or scripted KO.");
            DrawFatalSequenceSummary(death);
            return;
        }

        DrawFatalEventDetails(causeEvents);
        DrawFatalSequenceSummary(death);
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

            DrawActionBullet(cause);
            ImGui.BulletText($"Source: {FormatKnownPlayerName(cause.SourceName)}");
            if (cause.Kind == DeathEventKind.Status)
            {
                ImGui.BulletText(cause.Detail);
            }
            else
            {
                DrawAmountBullet(cause.Amount);
                ImGui.BulletText($"Flags: {FormatEventFlags(cause)}");
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
        DrawHpShieldBar(
            row.LastSnapshot.CurrentHp,
            row.LastSnapshot.ShieldHp,
            row.LastSnapshot.MaxHp,
            $"LeadUpSummaryHp{death.MemberKey}{death.SeenAtUtc.Ticks}{row.LastSnapshot.SeenAtUtc.Ticks}",
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

    private void DrawFatalSequenceSummary(PartyDeathRecord death)
    {
        if (death.FatalSequence is not { } sequence)
        {
            return;
        }

        var hasEvents = sequence.Events.Count > 0;
        var hasLogEvents = sequence.LogEvents.Count > 0;
        if (!hasEvents && !hasLogEvents)
        {
            return;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Fatal sequence");
        ImGui.TextDisabled("Compact damage context around the HP transition into KO.");

        if (hasEvents)
        {
            ImGui.BulletText("Captured events");
            foreach (var combatEvent in sequence.Events.OrderBy(combatEvent => combatEvent.SeenAtUtc))
            {
                ImGui.Indent();
                ImGui.TextWrapped(
                    $"{FormatRelativeToDeath(GetLeadUpAnchorSeenAtUtc(death), combatEvent.SeenAtUtc)} {FormatFatalEventLine(combatEvent)}");
                DrawLikelyAutoAttackTooltip(combatEvent);
                ImGui.Unindent();
            }
        }

        if (hasLogEvents)
        {
            ImGui.BulletText("Combat log confirmations");
            foreach (var logEvent in sequence.LogEvents.OrderBy(logEvent => logEvent.SeenAtUtc))
            {
                ImGui.Indent();
                ImGui.TextWrapped(
                    $"{FormatRelativeToDeath(GetLeadUpAnchorSeenAtUtc(death), logEvent.SeenAtUtc)} {FormatKnownPlayerName(logEvent.SourceName)}: {logEvent.ActionName} {FormatAmount(logEvent.Amount)}");
                ImGui.Unindent();
            }
        }
    }

    private LeadUpSummaryRow? GetLeadUpSummaryRow(PartyDeathRecord death)
    {
        var selection = DeathDisplaySelector.Select(death);
        var anchorSeenAtUtc = selection.AnchorSeenAtUtc;
        var rows = GetLeadUpHpHistoryRows(death, anchorSeenAtUtc);
        if (rows.Count == 0)
        {
            return null;
        }

        if (selection.Snapshot is null)
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
            ? events.SelectMany(combatEvent => GetEventSourceMitigationStatuses(death, combatEvent)).ToList()
            : GetActiveSourceMitigationStatuses(death, row.LastSnapshot.SeenAtUtc);
        return new LeadUpSummaryRow(anchorSeenAtUtc, row, events, sourceStatuses);
    }

    private void DrawExtraMitigationContext(PartyDeathRecord death, string idSuffix)
    {
        ImGui.TextUnformatted("Extra mitigation context");
        using var sectionIndent = new ImGuiIndentScope(SectionBodyIndent);
        var summary = GetLeadUpSummaryRow(death);
        var statuses = summary is not null
            ? GetLeadUpSummaryMitigationDebuffStatuses(summary, out _)
            : GetSelectedMitigationDebuffStatuses(death);
        DrawStatusSnapshot(
            statuses,
            $"{idSuffix}AtDeath");
        ImGui.Separator();
        DrawEarlierBossDebuffsNotOnFatalHit(death, idSuffix);
    }

    private void DrawBetterDeathsInformation(PartyDeathRecord death, string idSuffix)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
        var isOpen = ImGui.CollapsingHeader($"Better Deaths information - {LeadUpHistorySeconds:0}s lead-up###BetterDeathsInfo{idSuffix}");
        ImGui.PopStyleColor();
        if (!isOpen)
        {
            return;
        }

        DrawBetterDeathsInformationContent(death, idSuffix);
    }

    private void DrawBetterDeathsInformationContent(PartyDeathRecord death, string idSuffix)
    {
        using var sectionIndent = new ImGuiIndentScope(SectionBodyIndent);
        ImGui.TextDisabled("Older saved pulls may show less detail if that data was not captured at the time.");
        DrawHpHistory(death, idSuffix);
        ImGui.Separator();
        DrawLeadUpEvents(death, idSuffix);
    }

    private void DrawHpHistory(PartyDeathRecord death, string idSuffix)
    {
        DrawLeadUpLabel("10 second HP history");
        var anchorSeenAtUtc = GetLeadUpAnchorSeenAtUtc(death);
        var displayAnchorSeenAtUtc = GetLeadUpDisplayAnchorSeenAtUtc(death);
        var rows = GetLeadUpTimelineRows(death, anchorSeenAtUtc, displayAnchorSeenAtUtc);

        if (rows.Count == 0)
        {
            ImGui.TextDisabled("No HP samples or combat events captured in the last 10 seconds before KO.");
            return;
        }

        if (!ImGui.BeginTable($"##HpHistory{idSuffix}", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Before KO", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Timer", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("HP + shields", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("Events", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Mits/Debuffs", ImGuiTableColumnFlags.WidthStretch, 1.9f);
        DrawHpHistoryTableHeader();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredText(FormatRelativeToDeath(displayAnchorSeenAtUtc, row.SeenAtUtc));
            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip(FormatPreciseRelativeToDeath(displayAnchorSeenAtUtc, row.SeenAtUtc));
            }

            ImGui.TableNextColumn();
            DrawCenteredText(FormatCombatTimer(row.PullElapsedSeconds));
            ImGui.TableNextColumn();
            DrawHpShieldBar(
                row.CurrentHp,
                row.ShieldHp,
                row.MaxHp,
                $"HpHistoryBar{idSuffix}{row.SeenAtUtc.Ticks}{i}",
                row.Event is not null ? GetIncomingDamageAmount(row.Event) : null,
                valueOnlyTooltip: true);
            ImGui.TableNextColumn();
            DrawTimelineEventCell(row.Event);
            ImGui.TableNextColumn();
            DrawMitigationDebuffSummaryCell(row);
        }

        ImGui.EndTable();
    }

    private IReadOnlyList<LeadUpTimelineRow> GetLeadUpTimelineRows(
        PartyDeathRecord death,
        DateTime anchorSeenAtUtc,
        DateTime displayAnchorSeenAtUtc)
    {
        var cutoff = displayAnchorSeenAtUtc - TimeSpan.FromSeconds(LeadUpHistorySeconds);
        var history = death.HpHistory
            .Where(snapshot => snapshot.SeenAtUtc >= cutoff && snapshot.SeenAtUtc <= displayAnchorSeenAtUtc)
            .Where(snapshot => snapshot.CurrentHp > 0 || snapshot.ShieldHp > 0)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ToList();
        var events = GetLeadUpEvents(death)
            .Where(combatEvent => combatEvent.SeenAtUtc >= cutoff && combatEvent.SeenAtUtc <= anchorSeenAtUtc)
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
                var timelineRow = CreateHpSampleTimelineRow(
                    snapshot,
                    pendingDerivedHp,
                    displayAnchorSeenAtUtc,
                    GetActiveSourceMitigationStatuses(death, snapshot.SeenAtUtc));
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
            var hpDisplay = GetEventHpDisplay(death, combatEvent);
            AddLeadUpTimelineRow(rows, new LeadUpTimelineRow(
                combatEvent.SeenAtUtc,
                combatEvent.PullElapsedSeconds,
                hpDisplay.CurrentHp,
                hpDisplay.ShieldHp,
                hpDisplay.MaxHp,
                combatEvent.Statuses,
                GetNearbyHpHistoryStatuses(history, combatEvent.SeenAtUtc),
                GetEventSourceMitigationStatuses(death, combatEvent),
                combatEvent,
                hpDisplay.TooltipDetail));

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
            if (IsHpSampleCoveredByNearbyEvent(snapshot, events))
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
        IReadOnlyList<CombatEventRecord> events)
    {
        foreach (var combatEvent in events)
        {
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
            StatusListsMatchForHistoryMerge(previous.SourceStatuses, next.SourceStatuses);
    }

    private LeadUpTimelineRow CreateHpSampleTimelineRow(
        HpHistorySnapshot snapshot,
        DerivedHpState? pendingDerivedHp,
        DateTime displayAnchorSeenAtUtc,
        IReadOnlyList<StatusSnapshot> sourceStatuses)
    {
        if (pendingDerivedHp is not null &&
            snapshot.SeenAtUtc > pendingDerivedHp.EventSeenAtUtc &&
            IsStalePostHitSample(snapshot, pendingDerivedHp))
        {
            var displayShieldHp = snapshot.ShieldHp == pendingDerivedHp.SourceShieldHp
                ? pendingDerivedHp.DerivedShieldHp
                : snapshot.ShieldHp;
            var shieldSourceText = snapshot.ShieldHp == pendingDerivedHp.SourceShieldHp
                ? "shield was also derived from the hit"
                : "shield came from the captured sample";
            var tooltip = $"Derived HP after {FormatKnownPlayerName(pendingDerivedHp.SourceName)}: {pendingDerivedHp.ActionName} {FormatAmount(pendingDerivedHp.Amount)} at {FormatRelativeToDeath(displayAnchorSeenAtUtc, pendingDerivedHp.EventSeenAtUtc)}; {shieldSourceText}. Raw captured sample was {FormatHp(snapshot.CurrentHp, snapshot.ShieldHp, snapshot.MaxHp)}.";
            return new LeadUpTimelineRow(
                snapshot.SeenAtUtc,
                snapshot.PullElapsedSeconds,
                pendingDerivedHp.DerivedCurrentHp,
                displayShieldHp,
                snapshot.MaxHp > 0 ? snapshot.MaxHp : pendingDerivedHp.SourceMaxHp,
                snapshot.Statuses,
                snapshot.Statuses,
                sourceStatuses,
                null,
                tooltip);
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
            null,
            null);
    }

    private static bool IsStalePostHitSample(HpHistorySnapshot snapshot, DerivedHpState pendingDerivedHp)
    {
        return snapshot.CurrentHp == pendingDerivedHp.SourceCurrentHp &&
            (snapshot.MaxHp == 0 || pendingDerivedHp.SourceMaxHp == 0 || snapshot.MaxHp == pendingDerivedHp.SourceMaxHp);
    }

    private static DerivedHpState? TryCreateDerivedHpState(CombatEventRecord combatEvent, EventHpDisplay hpDisplay)
    {
        if (combatEvent.Kind != DeathEventKind.Damage || combatEvent.Amount == 0 || hpDisplay.MaxHp == 0)
        {
            return null;
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
            (uint)derivedShieldHp);
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

        var text = combatEvent.Amount > 0
            ? $"{FormatKnownPlayerName(combatEvent.SourceName)}: {combatEvent.ActionName} {FormatAmount(combatEvent.Amount)}"
            : $"{FormatKnownPlayerName(combatEvent.SourceName)}: {combatEvent.ActionName}";
        DrawCenteredOrWrappedText(text, GetEventColor(combatEvent.Kind));
        DrawLikelyAutoAttackTooltip(combatEvent);
    }

    private IReadOnlyList<HpHistoryDisplayRow> GetLeadUpHpHistoryRows(PartyDeathRecord death, DateTime anchorSeenAtUtc)
    {
        var cutoff = anchorSeenAtUtc - TimeSpan.FromSeconds(LeadUpHistorySeconds);
        var history = death.HpHistory
            .Where(snapshot => snapshot.SeenAtUtc >= cutoff && snapshot.SeenAtUtc <= anchorSeenAtUtc)
            .Where(snapshot => snapshot.CurrentHp > 0 || snapshot.ShieldHp > 0)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ToList();
        return history.Count == 0
            ? []
            : BuildHpHistoryDisplayRows(anchorSeenAtUtc, history, GetLeadUpEvents(death));
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
        string idSuffix)
    {
        DrawLeadUpLabel("Active mitigations/debuffs at death");
        if (statuses.Count == 0)
        {
            ImGui.TextDisabled("No defensive, mitigation, shield, or encounter debuff statuses captured.");
            return;
        }

        if (!ImGui.BeginTable($"##DeathStatusSnapshot{idSuffix}", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthStretch, 0.45f);
        ImGui.TableSetupColumn("Ability", ImGuiTableColumnFlags.WidthStretch, 1.7f);
        ImGui.TableSetupColumn("Mit Type", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("Mit%", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Linked Effects", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        DrawCenteredTableHeader("Icon", "Ability", "Mit Type", "Mit%", "Linked Effects");

        foreach (var status in statuses.OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase))
        {
            var displayInfo = Plugin.GetMitigationDisplayInfo(status);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredGameIcon(status.IconId, configuration.StatusIconSize, status.Name);
            ImGui.TableNextColumn();
            DrawCenteredOrWrappedText(status.Name);
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

    private void DrawEarlierBossDebuffsNotOnFatalHit(PartyDeathRecord death, string idSuffix)
    {
        DrawLeadUpLabel("Mitigations that expired on the leadup to the hit");
        var selection = DeathDisplaySelector.Select(death);
        if (selection.Events.Count == 0)
        {
            ImGui.TextDisabled("No fatal hit was captured to compare against.");
            return;
        }

        var rows = GetEarlierBossDebuffsNotOnFatalHit(death, selection);
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
            DrawCenteredOrWrappedText(row.ActionName);
            ImGui.TableNextColumn();
            DrawCenteredIconText(row.Status.IconId, configuration.StatusIconSize, row.Status.Name, row.Status.Name);
        }

        ImGui.EndTable();
    }

    private static IReadOnlyList<EarlierBossDebuffRow> GetEarlierBossDebuffsNotOnFatalHit(
        PartyDeathRecord death,
        DeathDisplaySelection selection)
    {
        var firstFatalHitAtUtc = selection.Events
            .Select(combatEvent => combatEvent.SeenAtUtc)
            .OrderBy(seenAtUtc => seenAtUtc)
            .FirstOrDefault();
        var fatalEventKeys = selection.Events
            .SelectMany(combatEvent => Plugin.GetBossMitigationStatusesForDisplay(GetEventSourceMitigationStatuses(death, combatEvent))
                .Select(status => BuildBossDebuffKey(GetSourceKey(combatEvent), status.Id)))
            .ToHashSet(StringComparer.Ordinal);

        return GetLeadUpEvents(death)
            .Where(combatEvent => combatEvent.SeenAtUtc < firstFatalHitAtUtc)
            .SelectMany(combatEvent =>
            {
                var sourceKey = GetSourceKey(combatEvent);
                return Plugin.GetBossMitigationStatusesForDisplay(GetEventSourceMitigationStatuses(death, combatEvent))
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

    private void DrawLeadUpEvents(PartyDeathRecord death, string idSuffix)
    {
        DrawLeadUpLabel("Captured hits/events in last 10 seconds");
        var events = GetLeadUpEvents(death);
        var displayAnchorSeenAtUtc = GetLeadUpDisplayAnchorSeenAtUtc(death);

        if (events.Count == 0)
        {
            ImGui.TextDisabled("No damage, miss, invulnerability, or status events captured in the last 10 seconds before KO.");
            return;
        }

        if (!ImGui.BeginTable($"##LeadUpEvents{idSuffix}", 6, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Before death", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1.35f);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch, 1.55f);
        ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("HP + shields", ImGuiTableColumnFlags.WidthStretch, 1.55f);
        ImGui.TableSetupColumn("Mits/Debuffs", ImGuiTableColumnFlags.WidthStretch, 2.55f);
        DrawLeadUpEventsTableHeader();

        foreach (var combatEvent in events)
        {
            var hpDisplay = GetEventHpDisplay(death, combatEvent);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredText(FormatRelativeToDeath(displayAnchorSeenAtUtc, combatEvent.SeenAtUtc));
            ImGui.TableNextColumn();
            DrawCenteredOrWrappedText(FormatKnownPlayerName(combatEvent.SourceName));
            ImGui.TableNextColumn();
            DrawActionText(combatEvent, false);
            ImGui.TableNextColumn();
            DrawAmountValue(combatEvent.Amount);
            ImGui.TableNextColumn();
            DrawCenteredHpShieldBar(
                hpDisplay.CurrentHp,
                hpDisplay.ShieldHp,
                hpDisplay.MaxHp,
                $"TimelineHp{idSuffix}{combatEvent.MemberKey}{combatEvent.SeenAtUtc.Ticks}",
                GetIncomingDamageAmount(combatEvent),
                hpDisplay.TooltipDetail);
            ImGui.TableNextColumn();
            DrawCombinedMitigationDebuffCell(
                GetMergedPlayerStatusesForEvent(death, combatEvent),
                GetEventSourceMitigationStatuses(death, combatEvent));
        }

        ImGui.EndTable();
    }

    private static void DrawLeadUpLabel(string label)
    {
        ImGui.TextColored(LeadUpGoldColor, label);
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

    private static EventHpDisplay GetEventHpDisplay(PartyDeathRecord death, CombatEventRecord combatEvent)
    {
        if (combatEvent.HpSource != CombatEventHpSource.NoPreHitSample &&
            combatEvent.MaxHp > 0 &&
            (combatEvent.CurrentHp > 0 || combatEvent.ShieldHp > 0))
        {
            var tooltip = combatEvent.HpSource == CombatEventHpSource.LatestPriorSample
                ? "HP from the latest captured sample before this combat event."
                : "HP captured with this combat event by the legacy capture path.";
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
            DrawCenteredOrWrappedText($"Hit for {FormatAmount(totalDamage.Value)}");
            DrawPostMitigationHitTooltip();
        }
        else
        {
            var shownEvents = events.Take(maxEvents).ToList();
            foreach (var combatEvent in shownEvents)
            {
                DrawCenteredOrWrappedText(FormatFatalEventLine(combatEvent));
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
            return $"{FormatKnownPlayerName(combatEvent.SourceName)}: {combatEvent.ActionName} | Flags: {FormatEventFlags(combatEvent)}";
        }

        return $"{FormatKnownPlayerName(combatEvent.SourceName)}: {combatEvent.ActionName} | Amount: {FormatAmount(combatEvent.Amount)} | Flags: {FormatEventFlags(combatEvent)}";
    }

    private void DrawLeadUpSummaryMitigationDebuffCell(LeadUpSummaryRow summary)
    {
        var statuses = GetLeadUpSummaryMitigationDebuffStatuses(summary, out var bossStatusKeys);
        DrawStatusSummaryCell(
            statuses,
            true,
            status => bossStatusKeys.Contains(GetStatusKey(status)) ||
                Plugin.ShouldShowPlayerStatusTimerForDisplay(status),
            true);
    }

    private void DrawMitigationDebuffSummaryCell(LeadUpTimelineRow row)
    {
        DrawCombinedMitigationDebuffCell(
            row.Statuses.Concat(row.NearbyHpStatuses),
            row.SourceStatuses,
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
        int? maxStatusesPerRow = null)
    {
        var statuses = GetCombinedMitigationDebuffStatuses(playerStatusSource, bossStatusSource, out var bossStatusKeys);

        DrawStatusSummaryCell(
            statuses,
            true,
            status => bossStatusKeys.Contains(GetStatusKey(status)) ||
                Plugin.ShouldShowPlayerStatusTimerForDisplay(status),
            true,
            maxStatusesPerRow);
    }

    private IReadOnlyList<StatusSnapshot> GetLeadUpSummaryMitigationDebuffStatuses(
        LeadUpSummaryRow summary,
        out HashSet<(uint Id, uint IconId, uint SourceId)> bossStatusKeys)
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
        return combatEvent.SourceStatuses
            .Concat(GetActiveSourceMitigationStatuses(death, combatEvent.SeenAtUtc, combatEvent.SourceEntityId))
            .GroupBy(GetStatusKey)
            .Select(group => group
                .OrderBy(status => status.RemainingTime <= 0.0f ? float.MaxValue : status.RemainingTime)
                .ThenBy(status => status.StackCount)
                .First())
            .ToList();
    }

    private static IReadOnlyList<StatusSnapshot> GetActiveSourceMitigationStatuses(
        PartyDeathRecord death,
        DateTime seenAtUtc,
        uint? sourceEntityId = null)
    {
        var sourceMitigationHistory = GetSourceMitigationHistoryForDisplay(death);
        if (sourceMitigationHistory.Count == 0)
        {
            return [];
        }

        return sourceMitigationHistory
            .Where(snapshot => snapshot.SeenAtUtc <= seenAtUtc)
            .Where(snapshot => sourceEntityId is null || snapshot.SourceEntityId == sourceEntityId.Value)
            .SelectMany(snapshot => GetActiveSourceMitigationStatuses(snapshot, seenAtUtc))
            .GroupBy(entry => (entry.SourceEntityId, entry.Status.Id, entry.Status.IconId, entry.Status.SourceId))
            .Select(group => group
                .OrderByDescending(entry => entry.SeenAtUtc)
                .ThenBy(entry => entry.Status.RemainingTime)
                .First()
                .Status)
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();
    }

    private static IReadOnlyList<SourceMitigationSnapshot> GetSourceMitigationHistoryForDisplay(PartyDeathRecord death)
    {
        var sourceMitigationHistory = death.SourceMitigationHistory?.ToList() ?? [];
        sourceMitigationHistory.AddRange(GetLeadUpEvents(death)
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

    private static IEnumerable<(uint SourceEntityId, DateTime SeenAtUtc, StatusSnapshot Status)> GetActiveSourceMitigationStatuses(
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
                snapshot.SeenAtUtc,
                status with { RemainingTime = remainingTime });
        }
    }

    private IReadOnlyList<StatusSnapshot> GetSelectedMitigationDebuffStatuses(PartyDeathRecord death)
    {
        var selection = DeathDisplaySelector.Select(death);
        return GetCombinedMitigationDebuffStatuses(
            GetSelectedPlayerStatuses(death),
            selection.Events.SelectMany(combatEvent => GetEventSourceMitigationStatuses(death, combatEvent)),
            out _);
    }

    private IReadOnlyList<StatusSnapshot> GetCombinedMitigationDebuffStatuses(
        IEnumerable<StatusSnapshot> playerStatusSource,
        IEnumerable<StatusSnapshot> bossStatusSource,
        out HashSet<(uint Id, uint IconId, uint SourceId)> bossStatusKeys)
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

    private static (uint Id, uint IconId, uint SourceId) GetStatusKey(StatusSnapshot status)
    {
        return (status.Id, status.IconId, status.SourceId);
    }

    private void DrawStatusSummaryCell(
        IReadOnlyList<StatusSnapshot> statuses,
        bool showTenthsOverTenSeconds = false,
        Func<StatusSnapshot, bool>? shouldShowTimer = null,
        bool centerContent = false,
        int? maxStatusesPerRow = null)
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

                DrawStatusIconStack(row[statusIndex].Status, configuration.StatusIconSize, showTenthsOverTenSeconds, row[statusIndex].ShowTimer);
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
        bool showTimer = true)
    {
        var iconSize = Math.Clamp(configuredIconSize, 12.0f, 32.0f);
        var timerText = FormatStatusDuration(status, showTenthsOverTenSeconds, showTimer);
        var timerWidth = string.IsNullOrEmpty(timerText) ? 0.0f : ImGui.CalcTextSize(timerText).X;
        var groupWidth = GetStatusIconStackWidth(status, configuredIconSize, showTenthsOverTenSeconds, showTimer);
        var startX = ImGui.GetCursorPosX();
        var tooltip = FormatStatusCompact(status, showTenthsOverTenSeconds, showTimer);
        var hovered = false;

        ImGui.BeginGroup();
        ImGui.SetCursorPosX(startX + MathF.Max(0.0f, (groupWidth - iconSize) * 0.5f));
        if (status.IconId == 0)
        {
            ImGui.Dummy(new Vector2(iconSize));
        }
        else
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(status.IconId));
            var wrap = texture.GetWrapOrDefault();
            if (wrap is null)
            {
                ImGui.Dummy(new Vector2(iconSize));
            }
            else
            {
                ImGui.Image(wrap.Handle, new Vector2(iconSize));
                hovered |= ImGui.IsItemHovered();
            }
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

    private void DrawSettingsTab()
    {
        var showWindow = configuration.ShowWindow;
        if (ImGui.Checkbox("Show Better Deaths window on plugin load", ref showWindow))
        {
            plugin.SetShowWindowByDefault(showWindow);
        }

        DrawInlineDebugTabButton();

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

        var showDeathRecapPopup = configuration.ShowDeathRecapPopup;
        if (ImGui.Checkbox("Show recap popup when you die", ref showDeathRecapPopup))
        {
            plugin.SetShowDeathRecapPopup(showDeathRecapPopup);
        }

        DrawSettingsTooltip("Shows a small local-only button for 30 seconds after your own death. The button opens that exact death in Review.");

        var redactPlayerNames = configuration.RedactPlayerNames;
        if (ImGui.Checkbox("Name Redaction", ref redactPlayerNames))
        {
            plugin.SetRedactPlayerNames(redactPlayerNames);
        }

        DrawSettingsTooltip("A way to show information to others without doxxing your party");


        var removeChatBranding = configuration.RemoveChatBranding;
        if (ImGui.Checkbox("Remove Better Deaths branding from chat posts", ref removeChatBranding))
        {
            plugin.SetRemoveChatBranding(removeChatBranding);
        }

        DrawSettingsTooltip(";( sadge, you hate me..");

        var postDeathRecapLinksOnDeath = configuration.PostDeathRecapLinksOnDeath;
        var postDeathRecapLinksChanged = ImGui.Checkbox("##PostDeathRecapLinksOnDeath", ref postDeathRecapLinksOnDeath);
        var postDeathRecapLinksHovered = ImGui.IsItemHovered();
        var postDeathRecapLinksLabelClicked = false;
        ImGui.SameLine();
        ImGui.TextUnformatted("Post");
        postDeathRecapLinksHovered |= ImGui.IsItemHovered();
        postDeathRecapLinksLabelClicked |= ImGui.IsItemClicked();
        ImGui.SameLine();
        ImGui.TextColored(LeadUpGoldColor, "[Recap Link]");
        postDeathRecapLinksHovered |= ImGui.IsItemHovered();
        postDeathRecapLinksLabelClicked |= ImGui.IsItemClicked();
        ImGui.SameLine();
        ImGui.TextUnformatted("when deaths are captured");
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
            SetThemedTooltip("Opt-in. When enabled, Better Deaths posts a clickable recap link to chat after captured deaths. Manual chat posts still include their own recap link.");
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Capture Settings");

        if (ImGui.BeginTable("##CaptureClockSettings", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Capture", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("Clock", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var captureParty = configuration.CapturePartyDeaths;
            if (ImGui.Checkbox("Capture party", ref captureParty))
            {
                plugin.SetCapturePartyDeaths(captureParty);
            }

            if (ImGui.IsItemHovered())
            {
                SetThemedTooltip("Includes your own character.");
            }

            var captureOthers = configuration.CaptureOtherDeaths;
            if (ImGui.Checkbox("Capture others", ref captureOthers))
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

        var iconSize = MathF.Max(configuration.ActionIconSize, configuration.StatusIconSize);
        if (ImGui.SliderFloat("Icon size", ref iconSize, 12.0f, 48.0f, "%.0f px"))
        {
            plugin.SetIconSize(iconSize);
        }

        DrawSettingsTooltip("Controls non-widget action and status icons in death timelines, details, examples, and Better Deaths lead-up tables. Use the Widget tab for Current Pull widget icons.");

        ImGui.Separator();
        DrawThemeSetting();
    }

    private void DrawThemeSetting()
    {
        ImGui.TextColored(LeadUpGoldColor, "Theme");
        if (!configuration.HasChangedTheme)
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
            themes.Count);

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
            activeTheme = theme;
        }

        var labelColor = selected ? LeadUpGoldColor : ModernTextColor;
        DrawCenteredOrWrappedText(theme.Label, labelColor);
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
        if (ImGui.Checkbox("Show current pull widget", ref showCurrentPullWidget))
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

        var widgetMode = configuration.WidgetDisplayMode;
        ImGui.SetNextItemWidth(185.0f);
        if (ImGui.BeginCombo("Display", GetWidgetDisplayModeLabel(widgetMode)))
        {
            foreach (var mode in Enum.GetValues<WidgetDisplayMode>())
            {
                var selected = widgetMode == mode;
                if (ImGui.Selectable(GetWidgetDisplayModeLabel(mode), selected))
                {
                    plugin.SetWidgetDisplayMode(mode);
                    widgetMode = mode;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        DrawSettingsTooltip("Normal keeps the full widget detail. Concise uses player initials, damage-only events, and fits mitigation/debuff icons to available space with a +x count.");

        ImGui.Separator();
        ImGui.TextUnformatted("Widget preview");
        ImGui.TextDisabled("Uses static example pull data so the preview stays available outside combat.");
        DrawCurrentPullWidgetPreview();
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
        var theme = BetterDeathsThemeCatalog.GetTheme(configuration.Theme);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.BeginChild("##CurrentPullWidgetPreview", new Vector2(0.0f, previewHeight), false, ImGuiWindowFlags.NoScrollbar))
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
        if (ImGui.BeginChild("##BetterDeathsUpdateBanner", new Vector2(0.0f, 52.0f), true, ImGuiWindowFlags.NoScrollbar))
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
        var currentMode = configuration.ClockDisplayMode;
        var preview = currentMode == ClockDisplayMode.TwelveHour
            ? "12-hour"
            : "24-hour";
        if (ImGui.BeginCombo("##ClockDisplayMode", preview))
        {
            DrawClockDisplayModeOption("24-hour", ClockDisplayMode.TwentyFourHour);
            DrawClockDisplayModeOption("12-hour", ClockDisplayMode.TwelveHour);
            ImGui.EndCombo();
        }

        DrawSettingsTooltip("Controls local clock times shown in recorded pull descriptions.");
    }

    private void DrawClockDisplayModeOption(string label, ClockDisplayMode mode)
    {
        var selected = configuration.ClockDisplayMode == mode;
        if (ImGui.Selectable(label, selected))
        {
            plugin.SetClockDisplayMode(mode);
        }

        if (selected)
        {
            ImGui.SetItemDefaultFocus();
        }
    }

    private void DrawOneTimeThankYouNotice(bool onDemand)
    {
        ImGui.PushStyleColor(ImGuiCol.Border, NoticeBorderColor);
        ImGui.PushStyleColor(ImGuiCol.Text, NoticeTextColor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(ReviewPaneContentIndent, 6.0f));

        if (ImGui.BeginChild("##BetterDeathsThankYouNotice", Vector2.Zero, true))
        {
            DrawWrappedText("Hiya! NaiLa here again!");
            ImGui.Spacing();
            DrawWrappedText("The second and final major push is here: UI POLISH");
            ImGui.Spacing();
            DrawWrappedText("We've completely revamped the look and flow as we get ready for v1.0. I'm so proud of how far we've come, and I'm genuinely grateful for all the effort everyone has put into testing and giving me feedback.");
            ImGui.Spacing();
            DrawWrappedText("Seeing this grow from a little idea into something people actually use and care about means more to me than I can really put into words.. Every bit of feedback, every bug report, and every tiny detail you all helped me chase down has made this feel less like something I've been building alone \u2665");
            ImGui.Spacing();
            DrawWrappedText("I'm happy to say that this project is likely nearing the end. Once we're all set and ready, Better Deaths should move more into maintenance as we go through future patches and expansions.");
            ImGui.Spacing();
            DrawWrappedText("Currently on the docket, we have some remaining items that I want to add before I'm comfortable with a release.");
            DrawWrappedBullet("The first being themes so you aren't stuck with the current color scheme. I'll try my best to add a variety, but no promises on deliverables.");
            DrawWrappedBullet("Second, I want to go through and fix a lot of hard-coded values. Currently, we have some hard-coded values that we can change to be dynamically grabbed or resolved.");
            DrawWrappedText("There's still a lot of work to be done, but I hope this UI refinement makes you all happy. It definitely is making me happy! Thank you all again so much for your hard work and for allowing me to do this for you \u2665", NoticeBorderColor);
            ImGui.Spacing();
            DrawWrappedText("This will be the final message that I send here. I appreciate every single one of you very much!", NoticeBorderColor);
            ImGui.Spacing();
            DrawWrappedText("Signing off with love and deep appreciation, NaiLa", NoticeBorderColor);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Button, NoticeButtonColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, NoticeButtonHoveredColor);
            if (ImGui.Button("Continue to Better Deaths"))
            {
                if (onDemand)
                {
                    showThankYouNoticeOnDemand = false;
                }
                else
                {
                    plugin.MarkThankYouNoticeAcknowledged();
                }
            }

            ImGui.PopStyleColor();
            ImGui.PopStyleColor();
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();
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
        if (ImGui.Checkbox("Enable debug capture", ref debugEnabled))
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
        if (ImGui.Checkbox("Freeze on death", ref freezeOnDeath))
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
        if (ImGui.Checkbox("Save debug file", ref saveDebugFile))
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

        return ContainsDeath(GetExampleDeaths(), deathSeenAtTicks, memberKeyHash)
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
        Example,
        Customize,
        Data,
        Updates,
        Debug,
    }

    private enum DeathDetailPage
    {
        Summary,
        Mitigation,
        LeadUp,
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
        DrawWrappedBullet("Duty-only death review built around raid pulls, wipes, recommences, and resets.");
        DrawWrappedBullet("Current Pull and the optional widget show live death order while combat is happening.");
        DrawWrappedBullet("Last Pull Review keeps the most recent wiped/reset pull visible until the next duty pull starts.");
        DrawWrappedBullet("Recorded pull groups save immediately after wipes or resets, then restore when the plugin loads.");
        DrawWrappedBullet("Timeline-first recap shows who died, when they died, and the fatal events before opening player details.");
        DrawWrappedBullet("Fatal sequence summaries include source, action, amount, damage type, blocks, parries, crits, direct hits, and combat-log confirmations.");
        DrawWrappedBullet("HP plus shields before the fatal hit is shown as a clear bar with overkill context.");
        DrawWrappedBullet("Nested 10-second lead-up under each death shows HP, shields, player mitigations, encounter debuffs, and captured hits before KO.");
        DrawWrappedBullet("Active player mitigations and boss damage-down debuffs are grouped so Reprisal, Addle, Feint, and similar effects are easier to audit.");
        DrawWrappedBullet("Chat-posted death summaries can include clickable recap links for other Better Deaths users with the same captured pull.");
        ImGui.Separator();
        ImGui.TextWrapped("The goal is to make wipe review fast: see who died, see why, see what was active, and keep the pull context intact between attempts.");
        ImGui.Separator();
        DrawCreatorNote();
        DrawAcknowledgementNoticeButton();
    }

    private void DrawDataPage()
    {
        var data = GetDataPageSnapshot();

        ImGui.TextColored(LeadUpGoldColor, "Privacy & Data");
        ImGui.TextWrapped("Better Deaths does not upload your data. It does not have any upload functions, telemetry, analytics, webhooks, feedback endpoints, or hidden network reporting built into the plugin in any way, shape, or form.");
        ImGui.TextWrapped("That is intentional, and it will remain that way.");

        ImGui.Separator();
        ImGui.TextColored(LeadUpGoldColor, "Local data");
        DrawWrappedBullet("Recorded pulls are saved locally so you can review pulls after wipes, resets, reloads, or plugin updates.");
        DrawWrappedBullet("Saved pull data can include player names, jobs, duty names, death timing, HP and shields, damage events, actions, statuses, and mitigation context.");
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
        ImGui.TextColored(LeadUpGoldColor, $"{label}:");
        ImGui.SameLine();
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

        ImGui.TextUnformatted("v0.1.0.119");
        ImGui.TextDisabled("Acknowledgement fix.");
        DrawWrappedBullet("Acknowledgement text now wraps correctly.");

        ImGui.Separator();

        ImGui.TextUnformatted("v0.1.0.118");
        ImGui.TextDisabled("Testing polish.");
        DrawBreathingGoldBullet("Added the final UI polish acknowledgement message.");
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
        DrawWrappedBullet("The recap link setting now shows Post [Recap Link] when deaths are captured.");

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
        DrawBreathingGoldBullet("Added a star button under Developer tools to reopen the acknowledgement message on demand.");
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
        DrawWrappedBullet("Detected shared recap links now show Pull link detected with a compact Open Recap link.");

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
        ImGui.TextColored(LeadUpGoldColor, "Preparation for public beta testing. Lots of behind-the-scenes optimizations were made.");
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
    }

    private static void DrawBreathingGoldBullet(string text)
    {
        DrawWrappedBullet(text, GetBreathingGoldColor());
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

    private static bool ActiveThemeUsesLightPanels()
    {
        return GetColorLuminance(ModernPanelColor) >= 0.55f;
    }

    private static float GetColorLuminance(Vector4 color)
    {
        return (color.X * 0.2126f) + (color.Y * 0.7152f) + (color.Z * 0.0722f);
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
        ImGui.TextWrapped("Hi! NaiLa here~ I really appreciate you using Better Deaths. I made this as a personal passion project because I always needed and wanted a little more from the tools available..");
        ImGui.TextWrapped("It's not perfect, and it is definitely still growing and getting better every day, but I can promise I am putting a lot of love and care into it every day until it's perfect!");
        ImGui.TextWrapped("Thank you for trying it out, and I hope it helps your prog even a little <3");
        ImGui.PopStyleColor();
    }

    private void DrawAcknowledgementNoticeButton()
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, LeadUpGoldColor);
        if (DrawTransparentIconButton("ShowAcknowledgementNotice", FontAwesomeIcon.Star))
        {
            showThankYouNoticeOnDemand = true;
        }

        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Show the Better Deaths acknowledgement message again.");
        }
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
            statusesAtDeath);
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
            statusesAtDeath);
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
            if (ImGui.CalcTextSize(candidate).X <= maxWidth || string.IsNullOrEmpty(currentLine))
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
        return combatEvent.Kind == DeathEventKind.Status
            ? $"{combatEvent.ActionName} (status {combatEvent.ActionId})"
            : $"{combatEvent.ActionName} ({combatEvent.ActionId})";
    }

    private static void DrawActionText(CombatEventRecord combatEvent, bool includeId = true)
    {
        DrawCenteredOrWrappedText(includeId ? FormatAction(combatEvent) : combatEvent.ActionName);
        DrawLikelyAutoAttackTooltip(combatEvent);
    }

    private static void DrawActionBullet(CombatEventRecord combatEvent)
    {
        ImGui.BulletText($"Action: {FormatAction(combatEvent)}");
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
            combatEvent.ActionId != 0 &&
            string.Equals(combatEvent.ActionName, $"Action {combatEvent.ActionId}", StringComparison.Ordinal);
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

    private static void DrawAmountValue(uint amount)
    {
        DrawCenteredText(FormatAmount(amount));
        DrawAmountTooltip();
    }

    private static void DrawAmountTooltip()
    {
        if (ImGui.IsItemHovered())
        {
            SetThemedTooltip("Actual damage taken after mitigation, shields, blocks, parries, and other damage reductions are applied.");
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

    private static void DrawHpShieldBar(uint currentHp, uint shieldHp, uint maxHp, string id, ulong? incomingDamage = null, bool showOverkillLine = false, bool centerLabel = false, string? tooltipDetail = null, bool valueOnlyTooltip = false)
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
        var effectiveHp = (ulong)currentHp + shieldHp;
        var damageAmount = incomingDamage.GetValueOrDefault();
        var clearlyUnsurvivable = incomingDamage is not null &&
            damageAmount >= (ulong)maxHp + ClearlyUnsurvivableOverMaxHp;

        if (hpWidth > 0.0f)
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

        drawList.AddRect(position, barEnd, ImGui.GetColorU32(BarBorderColor), rounding);

        var label = FormatHpForBar(currentHp, shieldHp, maxHp, size.X);
        var textSize = ImGui.CalcTextSize(label);
        var textPosition = new Vector2(
            centerLabel ? position.X + MathF.Max(4.0f, (size.X - textSize.X) * 0.5f) : position.X + 4.0f,
            position.Y + MathF.Max(1.0f, (size.Y - textSize.Y) * 0.5f));
        ImGui.PushClipRect(position, barEnd, true);
        drawList.AddText(textPosition, ImGui.GetColorU32(clearlyUnsurvivable ? OverkillColor : ModernTextColor), label);
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
                tooltip += "\nRed text means a likely failed mechanic or vastly insufficient mitigation related death.";
            }

            SetThemedTooltip(tooltip);
        }

        if (showOverkillLine)
        {
            DrawOverkillLine(currentHp, shieldHp, maxHp, incomingDamage);
        }
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
        var overkillDisplay = GetOverkillDisplay(currentHp, shieldHp, incomingDamage);
        DrawCenteredText(overkillDisplay.Text, overkillDisplay.Color);

        if (ImGui.IsItemHovered())
        {
            var tooltip = incomingDamage is null
                ? "No incoming damage amount was captured for this selected event."
                : $"Incoming damage: {damageAmount:N0}\nHP plus shields before hit: {(ulong)currentHp + shieldHp:N0}\n{overkillDisplay.TooltipLine}";
            if (maxHp > 0)
            {
                tooltip += $"\nMax HP: {maxHp:N0}";
            }

            SetThemedTooltip(tooltip);
        }
    }

    private static OverkillDisplay GetOverkillDisplay(uint currentHp, uint shieldHp, ulong? incomingDamage)
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
        var effectiveHp = (ulong)currentHp + shieldHp;
        if (damageAmount > effectiveHp)
        {
            var overkillAmount = damageAmount - effectiveHp;
            return new OverkillDisplay(
                $"Overkill: {overkillAmount:N0}",
                FormatWidgetAmount(overkillAmount),
                OverkillColor,
                $"Overkilled by {overkillAmount:N0}.");
        }

        if (damageAmount == effectiveHp)
        {
            return new OverkillDisplay(
                "Exact lethal hit",
                "Exact",
                DisabledColor,
                "Captured hit exactly matched HP plus shields before hit.");
        }

        return new OverkillDisplay(
            "No overkill. Follow-up non-hit KO.",
            "Non-hit KO",
            WarningColor,
            "Captured hit was non-lethal based on HP plus shields before hit. The KO came from a follow-up non-hit event.");
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
