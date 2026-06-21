using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
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
    private RecordedPullSort recordedPullSort = RecordedPullSort.DutyNewestFirst;
    private uint recordedPullDutyFilter = AllRecordedPullDuties;
    private bool clearPendingDeathSelection;
    private bool collapseRecordedPullsRequested;
    private bool showDebugTab;
    private int? pendingMaxRecordedPulls;
    private static readonly Vector4 DamageColor = new(1.0f, 0.35f, 0.25f, 1.0f);
    private static readonly Vector4 HealColor = new(0.25f, 1.0f, 0.45f, 1.0f);
    private static readonly Vector4 WarningColor = new(1.0f, 0.7f, 0.25f, 1.0f);
    private static readonly Vector4 LeadUpGoldColor = new(1.0f, 0.78f, 0.22f, 1.0f);
    private static readonly Vector4 SpamWarningColor = new(1.0f, 0.12f, 0.12f, 1.0f);
    private static readonly Vector4 DisabledColor = new(0.65f, 0.65f, 0.65f, 1.0f);
    private static readonly Vector4 UpdateBannerBgColor = new(0.16f, 0.24f, 0.12f, 0.95f);
    private static readonly Vector4 UpdateBannerTextColor = new(0.35f, 1.0f, 0.45f, 1.0f);
    private static readonly Vector4 HpBarColor = new(0.2f, 0.75f, 0.35f, 1.0f);
    private static readonly Vector4 ShieldBarColor = new(1.0f, 0.82f, 0.16f, 1.0f);
    private static readonly Vector4 BarBackgroundColor = new(0.18f, 0.18f, 0.18f, 1.0f);
    private static readonly Vector4 BarBorderColor = new(0.45f, 0.45f, 0.45f, 1.0f);
    private static readonly Vector4 OverkillColor = new(1.0f, 0.05f, 0.05f, 1.0f);
    private static readonly DateTime ExamplePullStartedAtUtc = new(2026, 6, 19, 0, 0, 0, DateTimeKind.Utc);
    private const string LikelyAutoAttackTooltip = "Likely auto attack. Better Deaths could not resolve a named action here; named spells and abilities usually show their action name.";
    private const uint AllRecordedPullDuties = uint.MaxValue;
    private const string CurrentChangelogVersion = "0.1.0.73";
    private const float LeadUpHistorySeconds = 10.0f;
    private const float PullBodyIndent = 8.0f;
    private const float DeathDetailIndent = 8.0f;
    private const float SectionBodyIndent = 8.0f;
    private const float MinimumHpShieldBarWidth = 24.0f;
    private const uint ClearlyUnsurvivableOverMaxHp = 300_000;

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
        IReadOnlyList<CombatEventRecord> Events);

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

        Size = new Vector2(780, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        DrawPluginUpdateBanner();

        if (!ImGui.BeginTabBar("##BetterDeathsTabs"))
        {
            return;
        }

        var selectExampleTab = pendingDeathSelection is not null && ContainsDeath(GetExampleDeaths(), pendingDeathSelection);
        var selectDeathRecapTab = pendingDeathSelection is not null && !selectExampleTab;
        if (ImGui.BeginTabItem("Death Recap", selectDeathRecapTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            DrawDeathRecapTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Example Pull", selectExampleTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            DrawExamplePullTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Settings"))
        {
            DrawSettingsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Widget"))
        {
            DrawWidgetTab();
            ImGui.EndTabItem();
        }

        if (showDebugTab && ImGui.BeginTabItem("Debug"))
        {
            DrawDebugTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Notes"))
        {
            DrawNotesTab();
            ImGui.EndTabItem();
        }

        DrawChangelogTabItem();

        ImGui.EndTabBar();

        if (clearPendingDeathSelection)
        {
            pendingDeathSelection = null;
            clearPendingDeathSelection = false;
        }
    }

    public bool FocusDeath(long deathSeenAtTicks, uint memberKeyHash)
    {
        var target = new DeathSelectionTarget(deathSeenAtTicks, memberKeyHash);
        if (!ContainsDeath(GetExampleDeaths(), target) &&
            !ContainsDeath(plugin.CurrentDeaths, target) &&
            !plugin.RecordedPulls.Any(snapshot => ContainsDeath(snapshot.Deaths, target)))
        {
            return false;
        }

        pendingDeathSelection = target;
        clearPendingDeathSelection = false;
        IsOpen = true;
        return true;
    }

    private void DrawDeathRecapTab()
    {
        DrawCurrentPull();
        DrawRecordedPulls();
    }

    private void DrawExamplePullTab()
    {
        var deaths = GetExampleDeaths();
        ImGui.TextUnformatted("Example pull - Dancing Mad - Timer 11:54");
        ImGui.TextDisabled("Names are redacted into static party-role labels.");
        using var examplePullIndent = new ImGuiIndentScope(PullBodyIndent);
        DrawDeathTimeline(deaths, "ExamplePull");
        ImGui.Separator();
        DrawDeathDetails(deaths, "ExamplePull");
    }

    private void DrawCurrentPull()
    {
        var header = $"{BuildCurrentPullTitle()}###CurrentPullDeaths";
        if (HasPendingDeathSelection(plugin.CurrentDeaths))
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
        DrawCurrentPullWidgetContent(
            plugin.CurrentDeaths,
            BuildCurrentPullTitle(),
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
        DrawDeathDetails(deaths, idSuffix);
    }

    private void DrawCurrentPullWidgetContent(IReadOnlyList<PartyDeathRecord> deaths, string title, string idSuffix)
    {
        ImGui.TextUnformatted(title);
        if (deaths.Count == 0)
        {
            ImGui.TextDisabled("No deaths recorded this pull.");
            return;
        }

        if (ImGui.BeginChild($"##CurrentPullWidgetScroll{idSuffix}", Vector2.Zero, false))
        {
            DrawCurrentPullWidgetDeathTable(deaths, idSuffix);
        }

        ImGui.EndChild();
    }

    private void DrawCurrentPullWidgetDeathTable(IReadOnlyList<PartyDeathRecord> deaths, string idSuffix)
    {
        if (!ImGui.BeginTable($"##CurrentPullWidgetDeaths{idSuffix}", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 1.35f);
        ImGui.TableSetupColumn("Cause", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableSetupColumn("Overkill", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableSetupColumn("Mits", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        DrawCenteredTableHeader("Time", "Player", "Cause", "Overkill", "Mits");

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
            DrawWidgetCauseSummary(causeEvents);
            ImGui.TableNextColumn();
            DrawWidgetOverkillSummary(selection);
            ImGui.TableNextColumn();
            DrawWidgetMitsCell(death, selection);
        }

        ImGui.EndTable();
    }

    private void DrawWidgetPlayerCell(PartyDeathRecord death)
    {
        var iconId = GetClassJobIconId(death.ClassJobId);
        var displayName = FormatWidgetPlayerName(death.MemberName);
        var tooltip = $"Full name: {death.MemberName}\nInitials: {FormatPlayerInitials(death.MemberName)}";
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
            ImGui.SetTooltip(tooltip);
        }
    }

    private string FormatWidgetPlayerName(string memberName)
    {
        return configuration.WidgetPlayerNameDisplayMode == WidgetPlayerNameDisplayMode.Initials
            ? FormatPlayerInitials(memberName)
            : memberName;
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
            ImGui.TextDisabled("No recorded pulls kept yet.");
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

        foreach (var (snapshot, pullNumber) in visiblePulls)
        {
            var pullId = $"{snapshot.PullNumber}:{snapshot.CapturedAtUtc.Ticks}";
            var header = $"Pull {pullNumber} - {snapshot.TerritoryName} - Timer {FormatCombatTimer(snapshot.PullElapsedSeconds)}###RecordedPull{pullId}";
            if (HasPendingDeathSelection(snapshot.Deaths))
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
            ImGui.TextDisabled($"{snapshot.Reason} - {FormatLocalClockTime(snapshot.CapturedAtUtc)}");
            DrawDeathTimeline(snapshot.Deaths, $"Pull{pullId}");
            DrawDeathDetails(snapshot.Deaths, $"Pull{pullId}");
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

        if (ImGuiComponents.IconButton("ClearRecordedPulls", FontAwesomeIcon.Trash) &&
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
            ImGui.SetTooltip("Ctrl+click to delete stored death recaps");
        }

        ImGui.SetCursorPosX(buttonX + buttonWidth - collapseButtonWidth);
        if (ImGui.SmallButton(collapseLabel))
        {
            collapseRecordedPullsRequested = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Collapse all pulls");
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
        ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.34f, 0.24f, 0.07f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.66f, 0.45f, 0.12f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.50f, 0.35f, 0.10f, 1.0f));
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
        DrawRecordedPullSortControl();
        ImGui.SameLine();
        DrawRecordedPullDutyFilterControl();
    }

    private void DrawRecordedPullSortControl()
    {
        ImGui.SetNextItemWidth(180.0f);
        if (!ImGui.BeginCombo("Sort##RecordedPullSort", GetRecordedPullSortLabel(recordedPullSort)))
        {
            return;
        }

        DrawRecordedPullSortOption(RecordedPullSort.NewestFirst);
        DrawRecordedPullSortOption(RecordedPullSort.OldestFirst);
        DrawRecordedPullSortOption(RecordedPullSort.DutyNewestFirst);
        ImGui.EndCombo();
    }

    private void DrawRecordedPullSortOption(RecordedPullSort sort)
    {
        var selected = recordedPullSort == sort;
        if (ImGui.Selectable(GetRecordedPullSortLabel(sort), selected))
        {
            recordedPullSort = sort;
        }

        if (selected)
        {
            ImGui.SetItemDefaultFocus();
        }
    }

    private void DrawRecordedPullDutyFilterControl()
    {
        var dutyOptions = GetRecordedPullDutyOptions().ToList();
        if (recordedPullDutyFilter != AllRecordedPullDuties &&
            !dutyOptions.Any(option => option.TerritoryId == recordedPullDutyFilter))
        {
            recordedPullDutyFilter = AllRecordedPullDuties;
        }

        ImGui.SetNextItemWidth(260.0f);
        if (!ImGui.BeginCombo("Duty##RecordedPullDutyFilter", GetRecordedPullDutyFilterLabel(dutyOptions)))
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

    private IEnumerable<(PullDeathSnapshot Snapshot, long PullNumber)> GetVisibleRecordedPulls()
    {
        var pulls = plugin.RecordedPulls
            .Select(snapshot => (Snapshot: snapshot, PullNumber: snapshot.PullNumber));

        if (recordedPullDutyFilter != AllRecordedPullDuties)
        {
            pulls = pulls.Where(entry => entry.Snapshot.TerritoryId == recordedPullDutyFilter);
        }

        return recordedPullSort switch
        {
            RecordedPullSort.OldestFirst => pulls.OrderBy(entry => entry.PullNumber),
            RecordedPullSort.DutyNewestFirst => pulls
                .OrderBy(entry => entry.Snapshot.TerritoryName, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(entry => entry.PullNumber),
            _ => pulls.OrderByDescending(entry => entry.PullNumber),
        };
    }

    private static string GetRecordedPullSortLabel(RecordedPullSort sort)
    {
        return sort switch
        {
            RecordedPullSort.OldestFirst => "Oldest first",
            RecordedPullSort.DutyNewestFirst => "Duty, newest first",
            _ => "Newest first",
        };
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
        ImGui.TableSetupColumn("Likely cause", ImGuiTableColumnFlags.WidthStretch, 2.8f);
        DrawCenteredTableHeader("#", "Time", "Player", "Job", "Likely cause");

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
            DrawCenteredText(death.MemberName);
            ImGui.TableNextColumn();
            DrawJobCell(death);

            ImGui.TableNextColumn();
            DrawTimelineCauseText(causeEvents);
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

    private static void DrawLeadUpEventsTableHeader()
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        DrawCenteredHeaderCell("Before death");
        DrawCenteredHeaderCell("Type");
        DrawCenteredHeaderCell("Source");
        DrawCenteredHeaderCell("Action");
        DrawCenteredHeaderCell("Amount");
        DrawCenteredHeaderCell("HP + shields before");
        DrawCenteredHeaderCell("Player mits/debuffs");
        DrawCenteredHeaderCell("Boss damage-downs");
    }

    private static void DrawCenteredHeaderCell(string label)
    {
        ImGui.TableNextColumn();
        DrawCenteredOrWrappedText(label);
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
            .Where(IsLikelyDeathCauseEvent)
            .ToList();
    }

    private static void DrawTimelineCauseText(IReadOnlyList<CombatEventRecord> causeEvents)
    {
        if (causeEvents.Count == 0)
        {
            DrawCenteredOrWrappedText("Likely walled/non-hit KO", WarningColor);
            return;
        }

        foreach (var causeEvent in causeEvents)
        {
            DrawCenteredOrWrappedText(FormatLikelyCauseLine(causeEvent), GetEventColor(causeEvent.Kind));
            DrawLikelyAutoAttackTooltip(causeEvent);
        }
    }

    private static void DrawWidgetCauseSummary(IReadOnlyList<CombatEventRecord> causeEvents)
    {
        var text = FormatWidgetCauseSummary(causeEvents);
        DrawCenteredOrWrappedText(text, GetWidgetCauseColor(causeEvents));

        if (ImGui.IsItemHovered())
        {
            if (causeEvents.Count == 0)
            {
                ImGui.SetTooltip("Likely walled/non-hit KO.");
                return;
            }

            var tooltipLines = causeEvents.Select(FormatLikelyCauseLine).ToList();
            if (causeEvents.Any(IsLikelyAutoAttack))
            {
                tooltipLines.Add(string.Empty);
                tooltipLines.Add(LikelyAutoAttackTooltip);
            }

            ImGui.SetTooltip(string.Join(Environment.NewLine, tooltipLines));
        }
    }

    private static string FormatWidgetCauseSummary(IReadOnlyList<CombatEventRecord> causeEvents)
    {
        if (causeEvents.Count == 0)
        {
            return "Likely walled/non-hit KO";
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
            ? $"{cause.ActionName} from {cause.SourceName}"
            : $"{FormatWidgetAmount(cause.Amount)} {cause.ActionName}";
    }

    private static void DrawWidgetOverkillSummary(DeathDisplaySelection selection)
    {
        var incomingDamage = GetIncomingDamageAmount(selection.Events);
        if (incomingDamage is null || selection.Snapshot is null)
        {
            DrawCenteredText("-", DisabledColor);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("No incoming damage and pre-hit HP snapshot were available for this death.");
            }

            return;
        }

        var snapshot = selection.Snapshot;
        var overkillAmount = CalculateOverkillAmount(snapshot.CurrentHp, snapshot.ShieldHp, incomingDamage);
        DrawCenteredText(
            FormatWidgetAmount(overkillAmount),
            overkillAmount > 0 ? OverkillColor : DisabledColor);

        if (ImGui.IsItemHovered())
        {
            var effectiveHp = (ulong)snapshot.CurrentHp + snapshot.ShieldHp;
            ImGui.SetTooltip(
                $"Incoming damage: {incomingDamage.Value:N0}\n" +
                $"HP plus shields before hit: {effectiveHp:N0}\n" +
                $"Overkill: {overkillAmount:N0}");
        }
    }

    private void DrawWidgetMitsCell(PartyDeathRecord death, DeathDisplaySelection selection)
    {
        var statuses = GetWidgetMitStatuses(death, selection);
        if (statuses.Count == 0)
        {
            DrawCenteredText("-", DisabledColor);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("No player mitigations/debuffs or boss damage-down debuffs were captured for this death.");
            }

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
            .SelectMany(combatEvent => Plugin.GetBossMitigationStatusesForDisplay(combatEvent.SourceStatuses)
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

                var status = row[statusIndex];
                var tooltipPrefix = status.Category == "Boss" && !string.IsNullOrWhiteSpace(status.SourceName)
                    ? $"{status.Category} ({status.SourceName})"
                    : status.Category;
                DrawGameIcon(
                    status.Status.IconId,
                    iconSize,
                    $"{tooltipPrefix}: {FormatStatusCompact(status.Status)}");
            }
        }
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

    private void DrawDeathDetails(IReadOnlyList<PartyDeathRecord> deaths, string idSuffix, bool useCollapsers = true)
    {
        var orderedDeaths = GetDeathsInTimelineOrder(deaths);
        for (var i = 0; i < orderedDeaths.Count; i++)
        {
            var death = orderedDeaths[i];
            var deathNumber = i + 1;
            var isSelectedDeath = IsPendingDeathSelection(death);
            if (!HasDeathDetails(death))
            {
                if (isSelectedDeath)
                {
                    clearPendingDeathSelection = true;
                }

                continue;
            }

            var header = $"#{deathNumber} - {FormatCombatTimer(death.PullElapsedSeconds)} - {death.MemberName} ({death.ClassJobName})###DeathDetail{idSuffix}{death.MemberKey}{death.SeenAtUtc.Ticks}";
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
                ImGui.TextUnformatted($"#{deathNumber} - {FormatCombatTimer(death.PullElapsedSeconds)} - {death.MemberName} ({death.ClassJobName})");
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
            ImGui.BulletText(cause.Kind == DeathEventKind.Status
                ? "HP + shields before likely KO"
                : "HP + shields before likely hit");
            DrawHpShieldBar(
                cause.CurrentHp,
                cause.ShieldHp,
                cause.MaxHp,
                $"CauseHp{death.MemberKey}{death.SeenAtUtc.Ticks}",
                GetIncomingDamageAmount(cause),
                true);
        }
        else
        {
            ImGui.BulletText("HP + shields before likely hit");
            ImGui.TextColored(WarningColor, "Unavailable. No likely hit was captured inside the configured cause window.");
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
        ImGui.TextUnformatted(causeEvents.Count > 1 ? "Likely causes" : "Likely cause");
        if (causeEvents.Count == 0)
        {
            ImGui.TextColored(WarningColor, "Likely walled/non-hit KO. Possible death wall, reconnect spawn KO, or scripted KO.");
            DrawFatalSequenceSummary(death);
            return;
        }

        DrawLikelyCauseDetails(causeEvents);
        DrawFatalSequenceSummary(death);
    }

    private static void DrawLikelyCauseDetails(IReadOnlyList<CombatEventRecord> causeEvents)
    {
        for (var i = 0; i < causeEvents.Count; i++)
        {
            var cause = causeEvents[i];
            if (causeEvents.Count > 1)
            {
                ImGui.BulletText($"Likely cause {i + 1}/{causeEvents.Count}");
                ImGui.Indent();
            }

            DrawActionBullet(cause);
            ImGui.BulletText($"Source: {cause.SourceName}");
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
        ImGui.TableSetupColumn("Captured player mits/debuffs", ImGuiTableColumnFlags.WidthStretch, 1.7f);
        DrawCenteredTableHeader("HP + shields", "Captured hits/events", "Captured player mits/debuffs");

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
        DrawStatusSummaryCell(row, true, Plugin.ShouldShowPlayerStatusTimerForDisplay, true);

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
                    $"{FormatRelativeToDeath(GetLeadUpAnchorSeenAtUtc(death), combatEvent.SeenAtUtc)} {FormatLikelyCauseLine(combatEvent)}");
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
                    $"{FormatRelativeToDeath(GetLeadUpAnchorSeenAtUtc(death), logEvent.SeenAtUtc)} {logEvent.SourceName}: {logEvent.ActionName} {FormatAmount(logEvent.Amount)}");
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

        var row = selection.Snapshot is null
            ? rows[^1]
            : rows.LastOrDefault(row => row.LastSnapshot.SeenAtUtc == selection.Snapshot.SeenAtUtc) ?? rows[^1];
        var events = selection.Events.Count > 0 ? selection.Events : row.Events;
        return new LeadUpSummaryRow(anchorSeenAtUtc, row, events);
    }

    private void DrawExtraMitigationContext(PartyDeathRecord death, string idSuffix)
    {
        ImGui.TextUnformatted("Extra mitigation context");
        using var sectionIndent = new ImGuiIndentScope(SectionBodyIndent);
        DrawStatusSnapshot(plugin.GetRelevantPlayerStatusesForDisplay(GetSelectedPlayerStatuses(death)), $"{idSuffix}AtDeath");
        ImGui.Separator();
        DrawEarlierBossDebuffsNotOnLikelyHit(death, idSuffix);
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

        using var sectionIndent = new ImGuiIndentScope(SectionBodyIndent);
        ImGui.TextColored(LeadUpGoldColor, "Captured lead-up data. HP history is sampled about every half second while alive; event rows show captured actions at their actual timestamps.");
        ImGui.TextColored(LeadUpGoldColor, "Event-row HP uses the HP captured with that event when available; otherwise it falls back to the latest prior HP sample.");
        ImGui.TextColored(LeadUpGoldColor, "If a following HP sample is stale after a captured hit, Better Deaths shows the derived post-hit HP with a tooltip.");
        ImGui.TextColored(LeadUpGoldColor, "Player cells show active defensives, shields, mitigations, and encounter debuffs. Source cells show boss damage-down debuffs.");
        ImGui.TextDisabled("Older saved pulls may show less history if that data was not captured at the time.");
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
        ImGui.TableSetupColumn("Captured hit/event", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Player mits/debuffs", ImGuiTableColumnFlags.WidthStretch, 1.9f);
        DrawCenteredTableHeader("Before KO", "Timer", "HP + shields", "Captured hit/event", "Player mits/debuffs");

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredText(FormatRelativeToDeath(displayAnchorSeenAtUtc, row.SeenAtUtc));
            ImGui.TableNextColumn();
            DrawCenteredText(FormatCombatTimer(row.PullElapsedSeconds));
            ImGui.TableNextColumn();
            DrawHpShieldBar(
                row.CurrentHp,
                row.ShieldHp,
                row.MaxHp,
                $"HpHistoryBar{idSuffix}{row.SeenAtUtc.Ticks}{i}",
                row.Event is not null ? GetIncomingDamageAmount(row.Event) : null,
                tooltipDetail: row.HpTooltipDetail);
            ImGui.TableNextColumn();
            DrawTimelineEventCell(row.Event);
            ImGui.TableNextColumn();
            DrawStatusSummaryCell(plugin.GetRelevantPlayerStatusesForDisplay(row.Statuses), true, Plugin.ShouldShowPlayerStatusTimerForDisplay, true);
        }

        ImGui.EndTable();
    }

    private static IReadOnlyList<LeadUpTimelineRow> GetLeadUpTimelineRows(
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

        var rows = new List<LeadUpTimelineRow>();
        var historyIndex = 0;
        var eventIndex = 0;
        DerivedHpState? pendingDerivedHp = null;

        while (historyIndex < history.Count || eventIndex < events.Count)
        {
            var shouldTakeHistory = historyIndex < history.Count &&
                (eventIndex >= events.Count || history[historyIndex].SeenAtUtc <= events[eventIndex].SeenAtUtc);
            if (shouldTakeHistory)
            {
                var snapshot = history[historyIndex++];
                var timelineRow = CreateHpSampleTimelineRow(snapshot, pendingDerivedHp, displayAnchorSeenAtUtc);
                rows.Add(timelineRow);

                if (pendingDerivedHp is not null && snapshot.SeenAtUtc > pendingDerivedHp.EventSeenAtUtc &&
                    !IsStalePostHitSample(snapshot, pendingDerivedHp))
                {
                    pendingDerivedHp = null;
                }

                continue;
            }

            var combatEvent = events[eventIndex++];
            var hpDisplay = GetEventHpDisplay(death, combatEvent);
            rows.Add(new LeadUpTimelineRow(
                combatEvent.SeenAtUtc,
                combatEvent.PullElapsedSeconds,
                hpDisplay.CurrentHp,
                hpDisplay.ShieldHp,
                hpDisplay.MaxHp,
                combatEvent.Statuses,
                combatEvent,
                hpDisplay.TooltipDetail));

            pendingDerivedHp = TryCreateDerivedHpState(combatEvent, hpDisplay) ?? pendingDerivedHp;
        }

        return rows;
    }

    private static LeadUpTimelineRow CreateHpSampleTimelineRow(
        HpHistorySnapshot snapshot,
        DerivedHpState? pendingDerivedHp,
        DateTime displayAnchorSeenAtUtc)
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
            var tooltip = $"Derived HP after {pendingDerivedHp.SourceName}: {pendingDerivedHp.ActionName} {FormatAmount(pendingDerivedHp.Amount)} at {FormatRelativeToDeath(displayAnchorSeenAtUtc, pendingDerivedHp.EventSeenAtUtc)}; {shieldSourceText}. Raw captured sample was {FormatHp(snapshot.CurrentHp, snapshot.ShieldHp, snapshot.MaxHp)}.";
            return new LeadUpTimelineRow(
                snapshot.SeenAtUtc,
                snapshot.PullElapsedSeconds,
                pendingDerivedHp.DerivedCurrentHp,
                displayShieldHp,
                snapshot.MaxHp > 0 ? snapshot.MaxHp : pendingDerivedHp.SourceMaxHp,
                snapshot.Statuses,
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

    private static void DrawTimelineEventCell(CombatEventRecord? combatEvent)
    {
        if (combatEvent is null)
        {
            DrawCenteredText("-", DisabledColor);
            return;
        }

        var text = combatEvent.Amount > 0
            ? $"{combatEvent.SourceName}: {combatEvent.ActionName} {FormatAmount(combatEvent.Amount)}"
            : $"{combatEvent.SourceName}: {combatEvent.ActionName}";
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

    private static string FormatHpHistoryRelativeRange(DateTime deathSeenAtUtc, HpHistoryDisplayRow row)
    {
        var first = FormatRelativeToDeath(deathSeenAtUtc, row.FirstSnapshot.SeenAtUtc);
        if (row.SampleCount <= 1 || row.FirstSnapshot.SeenAtUtc == row.LastSnapshot.SeenAtUtc)
        {
            return first;
        }

        var last = FormatRelativeToDeath(deathSeenAtUtc, row.LastSnapshot.SeenAtUtc);
        return $"{first} to {last} ({row.SampleCount} samples)";
    }

    private static string FormatHpHistoryTimerRange(HpHistoryDisplayRow row)
    {
        var first = FormatCombatTimer(row.FirstSnapshot.PullElapsedSeconds);
        if (row.SampleCount <= 1 || row.FirstSnapshot.PullElapsedSeconds == row.LastSnapshot.PullElapsedSeconds)
        {
            return first;
        }

        var last = FormatCombatTimer(row.LastSnapshot.PullElapsedSeconds);
        return first == last ? first : $"{first}-{last}";
    }

    private void DrawStatusSnapshot(IReadOnlyList<StatusSnapshot> statuses, string idSuffix)
    {
        DrawLeadUpLabel("Active player mitigations/debuffs at death");
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
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 2.0f);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthStretch, 0.6f);
        ImGui.TableSetupColumn("Stacks", ImGuiTableColumnFlags.WidthStretch, 0.6f);
        ImGui.TableSetupColumn("Remaining", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        DrawCenteredTableHeader("Icon", "Status", "ID", "Stacks", "Remaining");

        foreach (var status in statuses.OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredGameIcon(status.IconId, configuration.StatusIconSize, status.Name);
            ImGui.TableNextColumn();
            DrawCenteredOrWrappedText(status.Name);
            ImGui.TableNextColumn();
            DrawCenteredText(status.Id.ToString());
            ImGui.TableNextColumn();
            DrawCenteredText(status.StackCount == 0 ? "-" : status.StackCount.ToString());
            ImGui.TableNextColumn();
            DrawCenteredText(FormatStatusDuration(status, true, Plugin.ShouldShowPlayerStatusTimerForDisplay(status), "-"));
        }

        ImGui.EndTable();
    }

    private static IReadOnlyList<StatusSnapshot> GetSelectedPlayerStatuses(PartyDeathRecord death)
    {
        return DeathDisplaySelector.Select(death).Snapshot?.Statuses ?? death.StatusesAtDeath;
    }

    private void DrawEarlierBossDebuffsNotOnLikelyHit(PartyDeathRecord death, string idSuffix)
    {
        DrawLeadUpLabel("Earlier boss debuffs, not on likely hit");
        var selection = DeathDisplaySelector.Select(death);
        if (selection.Events.Count == 0)
        {
            ImGui.TextDisabled("No likely hit was captured to compare against.");
            return;
        }

        var rows = GetEarlierBossDebuffsNotOnLikelyHit(death, selection);
        if (rows.Count == 0)
        {
            ImGui.TextDisabled("No earlier boss damage-down debuffs were captured outside the likely hit.");
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
            DrawCenteredOrWrappedText(row.SourceName);
            ImGui.TableNextColumn();
            DrawCenteredOrWrappedText(row.ActionName);
            ImGui.TableNextColumn();
            DrawCenteredIconText(row.Status.IconId, configuration.StatusIconSize, row.Status.Name, row.Status.Name);
        }

        ImGui.EndTable();
    }

    private static IReadOnlyList<EarlierBossDebuffRow> GetEarlierBossDebuffsNotOnLikelyHit(
        PartyDeathRecord death,
        DeathDisplaySelection selection)
    {
        var firstLikelyHitAtUtc = selection.Events
            .Select(combatEvent => combatEvent.SeenAtUtc)
            .OrderBy(seenAtUtc => seenAtUtc)
            .FirstOrDefault();
        var likelyKeys = selection.Events
            .SelectMany(combatEvent => Plugin.GetBossMitigationStatusesForDisplay(combatEvent.SourceStatuses)
                .Select(status => BuildBossDebuffKey(GetSourceKey(combatEvent), status.Id)))
            .ToHashSet(StringComparer.Ordinal);

        return GetLeadUpEvents(death)
            .Where(combatEvent => combatEvent.SeenAtUtc < firstLikelyHitAtUtc)
            .SelectMany(combatEvent =>
            {
                var sourceKey = GetSourceKey(combatEvent);
                return Plugin.GetBossMitigationStatusesForDisplay(combatEvent.SourceStatuses)
                    .Where(status => !likelyKeys.Contains(BuildBossDebuffKey(sourceKey, status.Id)))
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

        if (!ImGui.BeginTable($"##LeadUpEvents{idSuffix}", 8, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Before death", ImGuiTableColumnFlags.WidthStretch, 0.75f);
        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch, 1.15f);
        ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthStretch, 0.85f);
        ImGui.TableSetupColumn("HP + shields before", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableSetupColumn("Player mits/debuffs", ImGuiTableColumnFlags.WidthStretch, 1.55f);
        ImGui.TableSetupColumn("Boss damage-downs", ImGuiTableColumnFlags.WidthStretch, 1.55f);
        DrawLeadUpEventsTableHeader();

        foreach (var combatEvent in events)
        {
            var hpDisplay = GetEventHpDisplay(death, combatEvent);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCenteredText(FormatRelativeToDeath(displayAnchorSeenAtUtc, combatEvent.SeenAtUtc));
            ImGui.TableNextColumn();
            DrawEventTypeText(combatEvent);
            ImGui.TableNextColumn();
            DrawCenteredOrWrappedText(combatEvent.SourceName);
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
            DrawStatusSummaryCell(plugin.GetRelevantPlayerStatusesForDisplay(combatEvent.Statuses), true, Plugin.ShouldShowPlayerStatusTimerForDisplay, true);
            ImGui.TableNextColumn();
            DrawStatusSummaryCell(Plugin.GetBossMitigationStatusesForDisplay(combatEvent.SourceStatuses), true, null, true);
        }

        ImGui.EndTable();
    }

    private static void DrawLeadUpLabel(string label)
    {
        ImGui.TextColored(LeadUpGoldColor, label);
    }

    private static IReadOnlyList<CombatEventRecord> GetLeadUpEvents(PartyDeathRecord death)
    {
        return DeathDisplaySelector.GetLeadUpEvents(death);
    }

    private static bool IsLikelyDeathCauseEvent(CombatEventRecord combatEvent)
    {
        return combatEvent.Kind == DeathEventKind.Status ||
            (combatEvent.Kind == DeathEventKind.Damage && combatEvent.Amount > 0);
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
        if (combatEvent.MaxHp > 0 && (combatEvent.CurrentHp > 0 || combatEvent.ShieldHp > 0))
        {
            return new EventHpDisplay(
                combatEvent.CurrentHp,
                combatEvent.ShieldHp,
                combatEvent.MaxHp,
                "HP captured with this combat event.");
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
            combatEvent.CurrentHp,
            combatEvent.ShieldHp,
            combatEvent.MaxHp > 0 ? combatEvent.MaxHp : death.MaxHp,
            "No HP sample before this event was available.");
    }

    private static void DrawEventSummaryCell(IReadOnlyList<CombatEventRecord> events, int maxEvents = 2)
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
            DrawAmountTooltip();
        }
        else
        {
            var shownEvents = events.Take(maxEvents).ToList();
            foreach (var combatEvent in shownEvents)
            {
                DrawCenteredOrWrappedText(FormatLikelyCauseLine(combatEvent));
                DrawLikelyAutoAttackTooltip(combatEvent);
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
            var tooltipLines = events.Select(FormatLikelyCauseLine).ToList();
            if (totalDamage is not null)
            {
                tooltipLines.Insert(0, $"Total damage: {FormatAmount(totalDamage.Value)}");
                tooltipLines.Insert(1, string.Empty);
            }

            if (events.Any(IsLikelyAutoAttack))
            {
                tooltipLines.Add(string.Empty);
                tooltipLines.Add(LikelyAutoAttackTooltip);
            }

            ImGui.SetTooltip(string.Join(Environment.NewLine, tooltipLines));
        }
    }

    private static string FormatLikelyCauseLine(CombatEventRecord combatEvent)
    {
        if (combatEvent.Kind == DeathEventKind.Status)
        {
            return $"{combatEvent.SourceName}: {combatEvent.ActionName} | Flags: {FormatEventFlags(combatEvent)}";
        }

        return $"{combatEvent.SourceName}: {combatEvent.ActionName} | Amount: {FormatAmount(combatEvent.Amount)} | Flags: {FormatEventFlags(combatEvent)}";
    }

    private void DrawStatusSummaryCell(
        HpHistoryDisplayRow row,
        bool showTenthsOverTenSeconds = false,
        Func<StatusSnapshot, bool>? shouldShowTimer = null,
        bool centerContent = false)
    {
        var statuses = plugin.GetRelevantPlayerStatusesForDisplay(row.LastSnapshot.Statuses);
        DrawStatusSummaryCell(statuses, showTenthsOverTenSeconds, shouldShowTimer, centerContent);
    }

    private void DrawStatusSummaryCell(
        IReadOnlyList<StatusSnapshot> statuses,
        bool showTenthsOverTenSeconds = false,
        Func<StatusSnapshot, bool>? shouldShowTimer = null,
        bool centerContent = false)
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
            if (currentRow.Count > 0 && currentRowWidth + spacing + stackWidth > availableWidth)
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
            ImGui.SetTooltip(tooltip);
        }
    }

    private void DrawSettingsTab()
    {
        DrawPluginUpdateSettings();
        ImGui.Separator();

        var showWindow = configuration.ShowWindow;
        if (ImGui.Checkbox("Show Better Deaths window on plugin load", ref showWindow))
        {
            plugin.SetShowWindowByDefault(showWindow);
        }

        var removeChatBranding = configuration.RemoveChatBranding;
        if (ImGui.Checkbox("Remove Better Deaths branding from chat posts", ref removeChatBranding))
        {
            plugin.SetRemoveChatBranding(removeChatBranding);
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Capture Settings");

        var captureParty = configuration.CapturePartyDeaths;
        if (ImGui.Checkbox("Capture party", ref captureParty))
        {
            plugin.SetCapturePartyDeaths(captureParty);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Includes your own character.");
        }

        var captureOthers = configuration.CaptureOtherDeaths;
        if (ImGui.Checkbox("Capture others", ref captureOthers))
        {
            plugin.SetCaptureOtherDeaths(captureOthers);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Tracks non-party player characters visible to your client.");
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
        DrawSettingsInfoLine("KO state", "A captured character has transitioned into death.");
        DrawSettingsInfoLine("Likely cause", "The selected event from the fatal sequence, or a Doom-like status context inside the configured window.");
        DrawSettingsInfoLine("Fatal sequence", "A compact set of captured hits and combat-log confirmations around the HP transition into KO.");
        DrawSettingsInfoLine("Likely walled/non-hit KO", "Kept in the death timeline, but no player detail panel is shown because no likely hit or KO status context was captured.");
        DrawSettingsInfoLine("Recorded pulls", "Created on duty reset, wipe, recommence, and territory changes when the pull had at least one death.");
        DrawSettingsInfoLine("Recorded pull sort", "Controls the order of visible pull groups.");
        DrawSettingsInfoLine("Duty dropdown", "A filter: All duties shows everything, while a selected duty only shows pulls from that duty.");
        ImGui.TextColored(SpamWarningColor, "Only functions in duties, not overworld or PvP.");
        DrawDebugTabAccessButton();
    }

    private void DrawDebugTabAccessButton()
    {
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("Developer tools");

        var buttonLabel = showDebugTab ? "Hide debug tab" : "Show debug tab";
        var buttonWidth = ImGui.CalcTextSize(buttonLabel).X + (ImGui.GetStyle().FramePadding.X * 2.0f);
        var availableWidth = ImGui.GetContentRegionAvail().X;
        if (availableWidth > buttonWidth)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availableWidth - buttonWidth);
        }

        if (ImGui.Button(buttonLabel))
        {
            showDebugTab = !showDebugTab;
            if (!showDebugTab && configuration.DebugLogEnabled)
            {
                plugin.SetDebugLogEnabled(false);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(showDebugTab
                ? "Hides the developer debug tab and turns debug logging off."
                : "Shows the developer debug tab. Debug logging still requires its own checkbox inside that tab.");
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
            ImGui.SetTooltip("Keeps the current pull death recap in its own window. Closing the widget turns this setting off.");
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

        var widgetNameMode = configuration.WidgetPlayerNameDisplayMode;
        ImGui.SetNextItemWidth(185.0f);
        if (ImGui.BeginCombo("Naming Options", GetWidgetPlayerNameDisplayModeLabel(widgetNameMode)))
        {
            foreach (var mode in Enum.GetValues<WidgetPlayerNameDisplayMode>())
            {
                var selected = widgetNameMode == mode;
                if (ImGui.Selectable(GetWidgetPlayerNameDisplayModeLabel(mode), selected))
                {
                    plugin.SetWidgetPlayerNameDisplayMode(mode);
                    widgetNameMode = mode;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        DrawSettingsTooltip("Controls the widget Player column only. Hover a player in the widget to see full-name and initials examples from that player's in-game name.");

        ImGui.Separator();
        ImGui.TextUnformatted("Widget preview");
        ImGui.TextDisabled("Uses static example pull data so the preview stays available outside combat.");
        DrawCurrentPullWidgetPreview();
    }

    private static string GetWidgetPlayerNameDisplayModeLabel(WidgetPlayerNameDisplayMode mode)
    {
        return mode switch
        {
            WidgetPlayerNameDisplayMode.Initials => "Initials",
            _ => "Full name",
        };
    }

    private void DrawCurrentPullWidgetPreview()
    {
        var previewHeight = MathF.Min(420.0f, MathF.Max(260.0f, ImGui.GetContentRegionAvail().Y));
        var opacity = GetCurrentPullWidgetBackgroundOpacity();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        if (ImGui.BeginChild("##CurrentPullWidgetPreview", new Vector2(0.0f, previewHeight), false))
        {
            DrawWidgetPreviewBackground(opacity);
            DrawCurrentPullWidgetContent(GetExampleDeaths(), "Dancing Mad - Timer 11:54", "WidgetPreview");
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private static void DrawWidgetPreviewBackground(float opacity)
    {
        var drawList = ImGui.GetWindowDrawList();
        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var end = position + size;
        drawList.AddRectFilled(position, end, ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.06f, opacity)), 4.0f);
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

    private float GetWidgetIconSize()
    {
        return Math.Clamp(
            configuration.WidgetIconSize <= 0.0f ? 20.0f : configuration.WidgetIconSize,
            Plugin.MinWidgetIconSize,
            Plugin.MaxWidgetIconSize);
    }

    private static void DrawSettingsInfoLine(string term, string explanation)
    {
        ImGui.TextColored(LeadUpGoldColor, $"{term}:");
        ImGui.SameLine();
        ImGui.TextWrapped(explanation);
    }

    private static void DrawSettingsTooltip(string tooltip)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
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
        if (ImGui.BeginChild("##BetterDeathsUpdateBanner", new Vector2(0.0f, 52.0f), true))
        {
            ImGui.TextColored(UpdateBannerTextColor, GetPluginUpdateStatusText(status));
            ImGui.TextDisabled("Open the Dalamud plugin installer to update Better Deaths.");
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawPluginUpdateSettings()
    {
        var status = plugin.PluginUpdateStatus;
        if (ShouldDrawPluginUpdateBanner(status))
        {
            ImGui.TextColored(UpdateBannerTextColor, GetPluginUpdateStatusText(status));
        }
        else if (status.State == PluginUpdateCheckState.Error)
        {
            ImGui.TextColored(SpamWarningColor, GetPluginUpdateStatusText(status));
        }
        else
        {
            ImGui.TextDisabled(GetPluginUpdateStatusText(status));
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

    private void DrawDebugTab()
    {
        ImGui.TextWrapped("Debug records the same internal capture messages that the old debug chat option printed: tracked action effects and deaths as Better Deaths detects them.");
        ImGui.TextColored(SpamWarningColor, "Warning: this WILL spam while combat events are happening.");

        var debugEnabled = configuration.DebugLogEnabled;
        if (ImGui.Checkbox("Enable debug log", ref debugEnabled))
        {
            plugin.SetDebugLogEnabled(debugEnabled);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear log"))
        {
            plugin.ClearDebugLog();
        }

        ImGui.Separator();
        var entries = plugin.DebugLogEntries;
        ImGui.TextDisabled($"{entries.Count:N0} debug rows kept. The newest 500 rows are retained.");
        if (entries.Count == 0)
        {
            ImGui.TextDisabled("No debug entries captured.");
            return;
        }

        if (!ImGui.BeginTable("##BetterDeathsDebugLog", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
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
            ImGui.TextWrapped(entry.Message);
        }

        ImGui.EndTable();
    }

    private bool HasPendingDeathSelection(IReadOnlyList<PartyDeathRecord> deaths)
    {
        return pendingDeathSelection is { } target && ContainsDeath(deaths, target);
    }

    private bool IsPendingDeathSelection(PartyDeathRecord death)
    {
        return pendingDeathSelection is { } target && IsDeathTarget(death, target);
    }

    private static bool ContainsDeath(IReadOnlyList<PartyDeathRecord> deaths, DeathSelectionTarget target)
    {
        return deaths.Any(death => IsDeathTarget(death, target));
    }

    private static bool IsDeathTarget(PartyDeathRecord death, DeathSelectionTarget target)
    {
        return death.SeenAtUtc.Ticks == target.DeathSeenAtTicks &&
            Plugin.GetMemberKeyHash(death.MemberKey) == target.MemberKeyHash;
    }

    private enum RecordedPullSort
    {
        NewestFirst,
        OldestFirst,
        DutyNewestFirst,
    }

    private sealed record RecordedPullDutyOption(uint TerritoryId, string TerritoryName, int PullCount);

    private sealed record DeathSelectionTarget(long DeathSeenAtTicks, uint MemberKeyHash);

    private static void DrawNotesTab()
    {
        ImGui.TextUnformatted("What Better Deaths adds");
        ImGui.BulletText("Duty-only death review built around raid pulls, wipes, recommences, and resets.");
        ImGui.BulletText("Current Pull and the optional widget show live death order while combat is happening.");
        ImGui.BulletText("Last Pull Review keeps the most recent wiped/reset pull visible until the next duty pull starts.");
        ImGui.BulletText("Recorded pull groups save immediately after wipes or resets, then restore when the plugin loads.");
        ImGui.BulletText("Timeline-first recap shows who died, when they died, and the likely causes before opening player details.");
        ImGui.BulletText("Fatal sequence summaries include source, action, amount, damage type, blocks, parries, crits, direct hits, and combat-log confirmations.");
        ImGui.BulletText("HP plus shields before the likely hit is shown as a clear bar with overkill context.");
        ImGui.BulletText("Nested 10-second lead-up under each death shows HP, shields, player mitigations, encounter debuffs, and captured hits before KO.");
        ImGui.BulletText("Active player mitigations and boss damage-down debuffs are grouped so Reprisal, Addle, Feint, and similar effects are easier to audit.");
        ImGui.BulletText("Chat-posted death summaries can include clickable recap links for other Better Deaths users with the same captured pull.");
        ImGui.Separator();
        ImGui.TextWrapped("The goal is to make wipe review fast: see who died, see why, see what was active, and keep the pull context intact between attempts.");
        ImGui.Separator();
        DrawCreatorNote();
    }

    private static void DrawChangelogTab()
    {
        ImGui.TextUnformatted("v0.1.0.73");
        ImGui.TextDisabled("Improved Current Pull widget readability.");
        DrawBreathingGoldBullet("Current Pull widget now includes an Overkill column.");
        DrawBreathingGoldBullet("Current Pull widget now includes a Mits column with player mitigation/debuff and boss damage-down icons.");
        DrawBreathingGoldBullet("Widget cause and overkill numbers now use compact values like 3k, 186.9k, and 1.3m.");
        ImGui.BulletText("Removed the widget # column to make room for mitigation icons while keeping deaths ordered by time.");
        ImGui.BulletText("Widget mitigation icons wrap inside the Mits column and have their own icon-size slider.");
        ImGui.BulletText("Multi-hit chat posts now summarize total damage in one recap line.");
        ImGui.BulletText("Shared recap recognition now supports the new multi-hit chat summary format.");
        ImGui.BulletText("Duplicate mitigation/debuff status snapshots are now collapsed in recap details, lead-up tables, widgets, and chat posts.");
        ImGui.BulletText("Older saved pulls also benefit from the duplicate status cleanup when they are displayed.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.72");
        ImGui.TextDisabled("Improved responsive layout and reduced background overhead.");
        ImGui.TextColored(LeadUpGoldColor, "Preparation for public beta testing. Lots of behind-the-scenes optimizations were made.");
        ImGui.BulletText("HP + shields bars now scale to the available table cell width instead of using a fixed maximum size.");
        ImGui.BulletText("HP bar text now shortens automatically in narrow columns and clips inside the bar.");
        ImGui.BulletText("Recap, lead-up, status, and debug tables now use weighted responsive columns for cleaner resizing.");
        ImGui.BulletText("Captured hits/events summary text is centered in Player Death Information.");
        ImGui.BulletText("Reduced memory usage by 56% through code cleanup and saved JSON cleanup.");
        ImGui.BulletText("Live capture cleanup now runs on a steady interval instead of every framework tick or combat event.");
        ImGui.BulletText("Recorded pull history now skips disk writes when the saved data has not changed.");
        ImGui.BulletText("Recorded pulls kept now saves after the slider edit is released instead of while dragging.");
        ImGui.BulletText("Reduced small hot-path allocations in party refresh and reset-state tracking.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.71");
        ImGui.TextDisabled("Improved lead-up event accuracy and HP history readability.");
        DrawBreathingGoldBullet("10-second HP history now inserts captured hit/event rows at the actual event timestamp between HP samples.");
        DrawBreathingGoldBullet("Stale post-hit HP samples now show derived post-hit HP, with tooltips showing the raw captured sample.");
        ImGui.BulletText("Captured event rows use the HP captured with that event when available, with tooltip fallback details.");
        ImGui.BulletText("The detailed captured hits/events table remains available for full source, action, amount, and status review.");
        ImGui.BulletText("Lead-up timing now displays relative to the actual KO time to avoid the hidden fatal-sequence buffer offset.");
        ImGui.BulletText("Lead-up table layout was cleaned up so HP bars, captured events, and mitigation/debuff columns have clearer spacing and alignment.");
        ImGui.BulletText("Widget player-name display setting is now labeled Naming Options.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.70");
        ImGui.TextDisabled("Improved recap readability and widget controls.");
        DrawBreathingGoldBullet("Visible overkill amount now appears beneath the HP + shields bar.");
        DrawBreathingGoldBullet("Current Pull widget now shows job icons next to player names and can switch between full names and initials.");
        ImGui.BulletText("Recap tables now center headers and compact values while keeping long cause/status text readable.");
        ImGui.BulletText("Debug tab is now hidden by default and can be revealed from the bottom of Settings under Developer tools.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.69");
        ImGui.TextDisabled("Word fixing and recap table cleanup.");
        ImGui.BulletText("Cleaned up captured hits/events so action IDs no longer crowd the 10-second lead-up table.");
        ImGui.BulletText("Moved hit flags into the Type column and removed the extra Flags column.");
        ImGui.BulletText("Centered mitigation and debuff status cells in the lead-up table.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.68");
        ImGui.TextDisabled("Polished widget and Notes wording.");
        ImGui.BulletText("Renamed the widget window to Better Deaths Widget to avoid repeating Current Pull in the title bar.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.67");
        ImGui.TextDisabled("Improved last-pull review persistence and duty-only capture.");
        DrawBreathingGoldBullet("Last Pull Review keeps the most recent wiped/reset pull visible until the next duty pull starts.");
        DrawBreathingGoldBullet("Review copies now show the saved Recorded Pull number.");
        DrawBreathingGoldBullet("Better Deaths now only captures inside active duties, so overworld combat will not clear review data or create recaps.");
        ImGui.BulletText("Recorded pulls still save immediately on wipe/reset/territory changes without duplicating the same pull.");
        ImGui.BulletText("Removed mitigation timers from chat-posted active status lines to keep chat summaries cleaner.");
        ImGui.BulletText("Updated the Notes tab feature summary to better describe the current review tools.");
        ImGui.BulletText("Updated the Settings warning to: Only functions in duties, not overworld or PvP.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.66");
        ImGui.TextDisabled("Improved Current Pull widget readability.");
        DrawBreathingGoldBullet("Current Pull widget now uses a compact widget-only death table instead of the full recap layout.");
        ImGui.BulletText("Multi-hit deaths are summarized into one total line in the widget, with full hit details kept in the tooltip.");
        ImGui.BulletText("Widget content now clips and scrolls inside the widget instead of overflowing during busy pulls.");
        ImGui.BulletText("Widget preview now uses the same compact renderer as the live widget.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.65");
        ImGui.TextDisabled("Improved death context consistency.");
        ImGui.BulletText("Moved extra mitigation context above the 10-second lead-up so important mitigation review is easier to see.");
        ImGui.BulletText("Player mitigation and debuff context now uses the selected pre-hit snapshot before falling back to post-death statuses.");
        ImGui.BulletText("Earlier boss debuff review now compares against the selected likely-hit group instead of an older single-cause fallback.");
        ImGui.BulletText("Cleaned up old duplicate death-cause paths so the recap UI, debug log, and chat systems stay aligned.");
        ImGui.BulletText("Improved captured hits/events rendering and clarified the empty-state wording.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.64");
        ImGui.TextDisabled("Improved captured hit readability.");
        ImGui.BulletText("Captured hits/events summaries now show the combined damage total as a single hit value.");
        ImGui.BulletText("Likely cause details still keep the full source, action, amount, and flags breakdown.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.63");
        ImGui.TextDisabled("Improved chat recap consistency.");
        ImGui.BulletText("Changed chat-posted death recaps to use the same selected death-display data as Player Death Information.");
        ImGui.BulletText("Chat HP before hit now uses the mathematically selected pre-hit HP plus shield snapshot when available.");
        ImGui.BulletText("Chat-posted active mits and player debuffs now come from the same selected snapshot shown in the UI.");
        ImGui.BulletText("Centralized death display selection so the recap window, timeline cause, shared recap matching, and chat posts stay aligned.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.62");
        ImGui.TextDisabled("Improved HP and hit display consistency.");
        ImGui.BulletText("Changed player death HP selection to prefer the latest pre-hit snapshot that mathematically fits the captured killing damage.");
        ImGui.BulletText("Merged fatal sequence events into the 10-second lead-up so captured hits/events do not appear empty when fatal sequence data exists.");
        ImGui.BulletText("Kept the displayed HP bar and captured hit list tied to the same selected death-cause events.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.61");
        ImGui.TextDisabled("Improved likely cause accuracy.");
        ImGui.BulletText("Stopped heals from being captured as death lead-up events.");
        ImGui.BulletText("Limited displayed likely causes to positive damage or status KO events.");
        ImGui.BulletText("Kept miss and invulnerability events as lead-up context without promoting them to the timeline, player details, or chat-posted cause.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.60");
        ImGui.TextDisabled("Improved recap readability and chat-posted death details.");
        DrawBreathingGoldBullet("Added multiple likely causes to player death details when several captured hits/events contributed to the KO.");
        DrawBreathingGoldBullet("Added player debuffs to chat-posted death summaries.");
        ImGui.BulletText("Removed boss ability icon columns from recaps and lead-up tables because those actions usually do not have useful icons.");
        ImGui.BulletText("Changed likely cause details to show Action, Source, Amount, and Flags in a consistent bullet format.");
        ImGui.BulletText("Cleaned up the Widget tab preview so the opacity slider is not fighting an extra container background.");

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
        ImGui.BulletText("Updated death timeline likely causes to match the captured hits/events shown in player death details.");
        ImGui.BulletText("Centered death timeline headers and key columns for cleaner reading.");
        ImGui.BulletText("Changed recorded pull reset timestamps to display in local time.");
        ImGui.BulletText("Removed misleading combat event and likely cause window sliders from Settings.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.57");
        ImGui.TextDisabled("Improved death timeline job display.");
        ImGui.BulletText("Centered job abbreviations beside job icons in the death timeline.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.56");
        ImGui.TextDisabled("Fixed report rendering with job icons.");
        ImGui.BulletText("Fixed a crash when opening reports that tried to draw an unavailable job icon.");
        ImGui.BulletText("Job icons now fall back safely if the game icon asset cannot be loaded.");

        ImGui.Separator();
        ImGui.TextUnformatted("v0.1.0.54");
        ImGui.TextDisabled("Improved death recap capture and review clarity.");
        ImGui.BulletText("Added compact fatal sequence tracking around deaths.");
        ImGui.BulletText("Added filtered combat-log confirmations for boss and enemy damage to players.");
        ImGui.BulletText("Limited live capture to combat, with a short grace window for delayed KO detection.");
        ImGui.BulletText("Reduced misleading HP display by using the closest alive HP and shield sample before KO.");
        ImGui.BulletText("Added job icons next to job abbreviations in the death timeline.");
        ImGui.BulletText("Changed 8-player death link text to Party wipe detected.");
        ImGui.BulletText("Reduced settings stutter by saving Recorded pulls kept after releasing the slider.");
        ImGui.BulletText("Added safer bounded in-memory capture so Better Deaths does not behave like a debug logger.");
    }

    private static void DrawBreathingGoldBullet(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, GetBreathingGoldColor());
        ImGui.BulletText(text);
        ImGui.PopStyleColor();
    }

    private static Vector4 GetBreathingGoldColor()
    {
        var pulse = (MathF.Sin((float)ImGui.GetTime() * 2.2f) + 1.0f) * 0.5f;
        return new Vector4(
            1.0f,
            0.68f + (pulse * 0.18f),
            0.10f + (pulse * 0.18f),
            1.0f);
    }

    private static void DrawCreatorNote()
    {
        var textColor = new Vector4(1.0f, 0.88f, 0.58f, 1.0f);

        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.TextWrapped("Hi! Nainai here~ I really appreciate you using Better Deaths. I made this as a personal passion project because I always needed and wanted a little more from the tools available..");
        ImGui.TextWrapped("It's not perfect, and it is definitely still growing and getting better every day, but I can promise I am putting a lot of love and care into it every day until it's perfect!");
        ImGui.TextWrapped("Thank you for trying it out, and I hope it helps your prog even a little <3");
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
        ["Tank 1"] = new("example-tank-1", "Tank 1", 0, 19, "PLD", 258000),
        ["Tank 2"] = new("example-tank-2", "Tank 2", 1, 32, "DRK", 252000),
        ["Healer 1"] = new("example-healer-1", "Healer 1", 2, 33, "AST", 139500),
        ["Healer 2"] = new("example-healer-2", "Healer 2", 3, 28, "SCH", 136800),
        ["DPS 1"] = new("example-dps-1", "DPS 1", 4, 41, "VPR", 164000),
        ["DPS 2"] = new("example-dps-2", "DPS 2", 5, 22, "DRG", 171000),
        ["DPS 3"] = new("example-dps-3", "DPS 3", 6, 23, "BRD", 156000),
        ["DPS 4"] = new("example-dps-4", "DPS 4", 7, 35, "RDM", 151000),
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
            CreateExampleDeath(519.6f, "Tank 1", 517.0f, "Kefka", 47843, "Ultima Blaster", 3099, DamageType.Magic, TankStatuses()),
            CreateExampleDeath(534.8f, "DPS 1", 532.8f, "Kefka", 47844, "Ultima Blaster", 186998, DamageType.Magic, DpsStatuses()),
            CreateExampleDeath(542.5f, "Tank 2", 540.5f, "Chaos", 49746, "Attack", 23352, DamageType.Physical, TankStatuses()),
            CreateExampleDeath(637.9f, "DPS 4", 635.9f, "Kefka", 47866, "Earthquake", 63603, DamageType.Magic, CasterStatuses()),
            CreateExampleDeath(654.2f, "DPS 2", 652.2f, "black hole", 47868, "Nothingness", 1, DamageType.Magic, DpsStatuses()),
            CreateExampleDeath(664.3f, "DPS 1", 662.3f, "black hole", 47868, "Nothingness", 166206, DamageType.Magic, DpsStatuses()),
            CreateExampleDeath(680.5f, "DPS 2", 678.5f, "Kefka", 47851, "Shockwave", 176062, DamageType.Physical, DpsStatuses()),
            CreateExampleDeath(694.1f, "DPS 3", 692.1f, "Kefka", 47854, "Look upon Me and Despair", 108455, DamageType.Magic, RangedStatuses()),
            CreateExampleDeath(694.2f, "DPS 4", 692.2f, "Kefka", 47854, "Look upon Me and Despair", 197891, DamageType.Magic, CasterStatuses()),
            CreateExampleDeath(694.8f, "Healer 1", 692.8f, "black hole", 47868, "Nothingness", 1, DamageType.Magic, PureHealerStatuses()),
            CreateExampleDeath(708.9f, "Tank 2", 706.9f, "Kefka", 47856, "Stomp-a-Mole", 315080, DamageType.Physical, TankStatuses()),
            CreateExampleDeath(709.1f, "DPS 1", 707.1f, "Chaos", 47875, "Knock Down", 145170, DamageType.Physical, DpsStatuses()),
            CreateExampleDeath(709.2f, "DPS 4", 707.2f, "Chaos", 47875, "Knock Down", 65144, DamageType.Physical, CasterStatuses()),
            CreateExampleDeath(710.2f, "Healer 2", 708.2f, "Kefka", 47856, "Stomp-a-Mole", 38708, DamageType.Physical, ShieldHealerStatuses()),
            CreateExampleDeath(712.6f, "Tank 1", 710.6f, "Chaos", 49746, "Attack", 48219, DamageType.Physical, TankStatuses()),
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
            "Kefka",
            47843,
            "Ultima Blaster",
            DeathEventKind.Damage,
            (uint)Math.Round(player.MaxHp * 0.18),
            (uint)Math.Round(player.MaxHp * 0.62),
            (uint)Math.Round(player.MaxHp * 0.10),
            player.MaxHp,
            DamageType.Magic,
            string.Empty,
            AdjustExampleStatuses(statusesAtLikelyHit, causeElapsed, setupElapsed),
            ExampleSourceStatuses("Kefka", causeElapsed, setupElapsed));
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
            ExampleSourceStatuses(sourceName, causeElapsed, causeElapsed));
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

    private static IReadOnlyList<StatusSnapshot> TankStatuses()
    {
        return new[]
        {
            Status(1191, "Rampart", 4.2f),
            Status(638, "Vulnerability Up", 18.7f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> PureHealerStatuses()
    {
        return new[]
        {
            Status(2941, "Magic Vulnerability Up", 11.4f),
            Status(638, "Vulnerability Up", 15.2f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> ShieldHealerStatuses()
    {
        return new[]
        {
            Status(297, "Galvanize", 1.9f),
            Status(299, "Sacred Soil", 5.6f),
            Status(2941, "Magic Vulnerability Up", 8.5f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> DpsStatuses()
    {
        return new[]
        {
            Status(1826, "Shield Samba", 3.4f),
            Status(638, "Vulnerability Up", 12.6f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> RangedStatuses()
    {
        return new[]
        {
            Status(1934, "Troubadour", 2.7f),
            Status(638, "Vulnerability Up", 12.6f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> CasterStatuses()
    {
        return new[]
        {
            Status(2941, "Magic Vulnerability Up", 9.2f),
        };
    }

    private static IReadOnlyList<StatusSnapshot> ExampleSourceStatuses(string sourceName, float anchorElapsed, float elapsed)
    {
        if (sourceName is not ("Chaos" or "Kefka"))
        {
            return Array.Empty<StatusSnapshot>();
        }

        return AdjustExampleStatuses(new[]
        {
            Status(1193, "Reprisal", 4.0f),
            Status(1195, "Feint", 2.5f),
        }, anchorElapsed, elapsed);
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
        var shouldCenter = textWidth <= ImGui.GetContentRegionAvail().X;
        if (shouldCenter)
        {
            CenterNextItem(textWidth);
        }

        if (color is { } textColor)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        }

        if (shouldCenter)
        {
            ImGui.TextUnformatted(text);
        }
        else
        {
            ImGui.TextWrapped(text);
        }

        if (color is not null)
        {
            ImGui.PopStyleColor();
        }
    }

    private static void DrawCauseText(CombatEventRecord? cause)
    {
        if (cause is null)
        {
            ImGui.TextColored(WarningColor, "Likely walled/non-hit KO");
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, GetEventColor(cause.Kind));
        ImGui.TextUnformatted(cause.Kind == DeathEventKind.Status
            ? $"{cause.ActionName} from {cause.SourceName}"
            : $"{cause.ActionName} - {cause.Amount:N0} from {cause.SourceName}");
        DrawLikelyAutoAttackTooltip(cause);
        ImGui.PopStyleColor();
    }

    private static void DrawEventKind(DeathEventKind kind)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, GetEventColor(kind));
        ImGui.TextUnformatted(kind.ToString());
        ImGui.PopStyleColor();
    }

    private static void DrawEventTypeText(CombatEventRecord combatEvent)
    {
        DrawCenteredText(FormatEventType(combatEvent), GetEventColor(combatEvent.Kind));
    }

    private static string FormatEventType(CombatEventRecord combatEvent)
    {
        var flags = FormatEventFlags(combatEvent);
        if (!string.IsNullOrWhiteSpace(flags) && flags != "-")
        {
            return flags;
        }

        return combatEvent.Kind switch
        {
            DeathEventKind.Damage => "Hit",
            DeathEventKind.Invulnerable => "Invuln",
            _ => combatEvent.Kind.ToString(),
        };
    }

    private static Vector4 GetEventColor(DeathEventKind kind)
    {
        return kind switch
        {
            DeathEventKind.Damage => DamageColor,
            DeathEventKind.Heal => HealColor,
            DeathEventKind.Status => WarningColor,
            DeathEventKind.Miss or DeathEventKind.Invulnerable => DisabledColor,
            _ => Vector4.One,
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
            ImGui.SetTooltip(LikelyAutoAttackTooltip);
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
            ImGui.SetTooltip(tooltip);
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
            ImGui.SetTooltip("Actual damage taken after mitigation, shields, blocks, parries, and other damage reductions are applied.");
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

    private static void DrawHpShieldBar(uint currentHp, uint shieldHp, uint maxHp, string id, ulong? incomingDamage = null, bool showOverkillLine = false, bool centerLabel = false, string? tooltipDetail = null)
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
        var overkillAmount = incomingDamage is not null && damageAmount > effectiveHp
            ? (ulong)damageAmount - effectiveHp
            : 0UL;
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
        drawList.AddText(textPosition, ImGui.GetColorU32(clearlyUnsurvivable ? OverkillColor : Vector4.One), label);
        ImGui.PopClipRect();

        if (ImGui.IsItemHovered())
        {
            var tooltip = $"{FormatHp(currentHp, shieldHp, maxHp)}\nGreen is HP. Yellow is shield. Shield above max HP wraps from the left.";
            if (!string.IsNullOrWhiteSpace(tooltipDetail))
            {
                tooltip += $"\n{tooltipDetail}";
            }

            if (overkillAmount > 0)
            {
                tooltip += $"\nOverkilled by {overkillAmount:N0}.";
            }

            if (clearlyUnsurvivable)
            {
                tooltip += "\nRed text means the hit was a very large amount and likely came from a failed mechanic or insufficient mitigation.";
            }

            ImGui.SetTooltip(tooltip);
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
        var overkillAmount = CalculateOverkillAmount(currentHp, shieldHp, incomingDamage);
        var text = incomingDamage is null
            ? "Overkill: -"
            : $"Overkill: {overkillAmount:N0}";

        if (overkillAmount > 0)
        {
            DrawCenteredText(text, OverkillColor);
        }
        else
        {
            DrawCenteredText(text, DisabledColor);
        }

        if (ImGui.IsItemHovered())
        {
            var tooltip = incomingDamage is null
                ? "No incoming damage amount was captured for this selected event."
                : $"Incoming damage: {damageAmount:N0}\nHP plus shields before hit: {(ulong)currentHp + shieldHp:N0}";
            if (maxHp > 0)
            {
                tooltip += $"\nMax HP: {maxHp:N0}";
            }

            ImGui.SetTooltip(tooltip);
        }
    }

    private static ulong CalculateOverkillAmount(uint currentHp, uint shieldHp, ulong? incomingDamage)
    {
        var damageAmount = incomingDamage.GetValueOrDefault();
        var effectiveHp = (ulong)currentHp + shieldHp;
        return incomingDamage is not null && damageAmount > effectiveHp
            ? damageAmount - effectiveHp
            : 0UL;
    }

    private static string FormatHp(uint currentHp, uint shieldHp, uint maxHp)
    {
        var effectiveHp = (ulong)currentHp + shieldHp;
        return maxHp == 0
            ? $"{currentHp:N0} + {shieldHp:N0} shield"
            : $"{currentHp:N0} + {shieldHp:N0} shield / {maxHp:N0} ({(double)effectiveHp / maxHp:P0})";
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

    private static string FormatLocalClockTime(DateTime utcDateTime)
    {
        var localDateTime = utcDateTime.Kind switch
        {
            DateTimeKind.Local => utcDateTime,
            DateTimeKind.Utc => utcDateTime.ToLocalTime(),
            _ => DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc).ToLocalTime(),
        };

        return $"{localDateTime:HH:mm:ss} local";
    }

    private static string FormatRelativeToDeath(DateTime deathSeenAtUtc, DateTime eventSeenAtUtc)
    {
        var deltaSeconds = (deathSeenAtUtc - eventSeenAtUtc).TotalSeconds;
        return deltaSeconds >= 0
            ? $"-{deltaSeconds:0.00}s"
            : $"+{Math.Abs(deltaSeconds):0.00}s";
    }
}
