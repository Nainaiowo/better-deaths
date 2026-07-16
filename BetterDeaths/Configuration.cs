using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace BetterDeaths;

public enum DeathChatChannel
{
    Echo,
    Say,
    Party,
    Alliance,
    FreeCompany,
    Yell,
    Shout,
    CrossWorldLinkshell1,
    CrossWorldLinkshell2,
    CrossWorldLinkshell3,
    CrossWorldLinkshell4,
    CrossWorldLinkshell5,
    CrossWorldLinkshell6,
    CrossWorldLinkshell7,
    CrossWorldLinkshell8,
    SystemMessage,
}

public sealed record ChatChannelOption(DeathChatChannel Channel, string Label, string Command);

public enum DeathRecapLinkChannel
{
    SystemMessage,
    Echo,
    Notice,
    Urgent,
    ErrorMessage,
    SystemError,
}

public sealed record DeathRecapLinkChannelOption(DeathRecapLinkChannel Channel, string Label);

public enum WidgetDisplayMode
{
    Normal,
    Concise,
}

public enum ReviewDisplayMode
{
    Detailed,
    Focused,
}

public enum LeadUpTimelineOrder
{
    Oldest,
    Newest,
}

public enum ClockDisplayMode
{
    TwentyFourHour,
    TwelveHour,
}

[Serializable]
public sealed class ThemeColorValue
{
    public float R { get; set; }

    public float G { get; set; }

    public float B { get; set; }

    public float A { get; set; } = 1.0f;

    public ThemeColorValue()
    {
    }

    public ThemeColorValue(float r, float g, float b, float a = 1.0f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }
}

[Serializable]
public sealed class CustomThemeConfiguration
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; }

    public bool Enabled { get; set; }

    public bool Initialized { get; set; }

    public ThemeColorValue WindowBackground { get; set; } = new();

    public ThemeColorValue ContentBackground { get; set; } = new();

    public ThemeColorValue RaisedBackground { get; set; } = new();

    public ThemeColorValue Border { get; set; } = new();

    public ThemeColorValue Divider { get; set; } = new();

    public ThemeColorValue Accent { get; set; } = new();

    public ThemeColorValue AccentSoft { get; set; } = new();

    public ThemeColorValue RegularText { get; set; } = new();

    public ThemeColorValue MutedText { get; set; } = new();

    public ThemeColorValue GoldText { get; set; } = new();

    public ThemeColorValue DisabledText { get; set; } = new();

    public ThemeColorValue DamageText { get; set; } = new();

    public ThemeColorValue HealText { get; set; } = new();

    public ThemeColorValue WarningText { get; set; } = new();

    public ThemeColorValue SpamWarningText { get; set; } = new();

    public ThemeColorValue OverkillText { get; set; } = new();

    public ThemeColorValue FrameBackground { get; set; } = new();

    public ThemeColorValue FrameHoverBackground { get; set; } = new();

    public ThemeColorValue PopupBackground { get; set; } = new();

    public ThemeColorValue ButtonColor { get; set; } = new();

    public ThemeColorValue ButtonHoverColor { get; set; } = new();

    public ThemeColorValue SelectedButtonColor { get; set; } = new();

    public ThemeColorValue SelectedButtonHoverColor { get; set; } = new();

    public ThemeColorValue ButtonActiveColor { get; set; } = new();

    public ThemeColorValue ButtonText { get; set; } = new();

    public ThemeColorValue SelectedButtonText { get; set; } = new();

    public ThemeColorValue CheckboxBackground { get; set; } = new();

    public ThemeColorValue CheckboxHoverBackground { get; set; } = new();

    public ThemeColorValue CheckboxActiveBackground { get; set; } = new();

    public ThemeColorValue CheckboxCheckMark { get; set; } = new();

    public ThemeColorValue CheckboxBorder { get; set; } = new();

    public ThemeColorValue SliderGrab { get; set; } = new();

    public ThemeColorValue SliderGrabActive { get; set; } = new();

    public ThemeColorValue HeaderBackground { get; set; } = new();

    public ThemeColorValue HeaderHoverBackground { get; set; } = new();

    public ThemeColorValue HeaderActiveBackground { get; set; } = new();

    public ThemeColorValue TableRowAlt { get; set; } = new();

    public ThemeColorValue FocusedRow { get; set; } = new();

    public ThemeColorValue FocusedRowAccent { get; set; } = new();

    public ThemeColorValue TimelineSelectedRow { get; set; } = new();

    public ThemeColorValue TimelinePressedRow { get; set; } = new();

    public ThemeColorValue ScrollbarBackground { get; set; } = new();

    public ThemeColorValue ScrollbarGrab { get; set; } = new();

    public ThemeColorValue ScrollbarGrabHover { get; set; } = new();

    public ThemeColorValue ScrollbarGrabActive { get; set; } = new();

    public ThemeColorValue ChangelogTab { get; set; } = new();

    public ThemeColorValue ChangelogTabHover { get; set; } = new();

    public ThemeColorValue ChangelogTabActive { get; set; } = new();

    public ThemeColorValue HpBar { get; set; } = new();

    public ThemeColorValue ShieldBar { get; set; } = new();

    public ThemeColorValue BarBackground { get; set; } = new();

    public ThemeColorValue BarBorder { get; set; } = new();

    public ThemeColorValue WidgetWindowBackground { get; set; } = new();

    public ThemeColorValue WidgetTitleBackground { get; set; } = new();

    public ThemeColorValue WidgetTitleActiveBackground { get; set; } = new();

    public ThemeColorValue WidgetBorder { get; set; } = new();

    public ThemeColorValue WidgetResizeGrip { get; set; } = new();

    public ThemeColorValue WidgetResizeGripHover { get; set; } = new();

    public ThemeColorValue WidgetResizeGripActive { get; set; } = new();

    public ThemeColorValue UpdateBannerBackground { get; set; } = new();

    public ThemeColorValue UpdateBannerText { get; set; } = new();

    public ThemeColorValue NoticeBorder { get; set; } = new();

    public ThemeColorValue NoticeText { get; set; } = new();

    public ThemeColorValue NoticeButton { get; set; } = new();

    public ThemeColorValue NoticeButtonHover { get; set; } = new();
}

public enum BetterDeathsTheme
{
    Classic,
    Rose,
    Verdant,
    Ember,
    Pink,
    Lavender,
    PastelBlue,
    Wisteria,
    Blush,
    Bright,
    Marble,
    Tabasco,
    Mint,
    Sky,
    Peach,
    Cloud,
    Abyss,
    Graphite,
    Grape,
    Soda,
    Callus,
    Lemonade,
    Cotton,
    Banana,
    Hamtaro,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    public bool ShowWindow { get; set; } = true;

    public bool ApplyWideDefaultWindowSizeOnNextOpen { get; set; }

    public float MainWindowBackgroundOpacity { get; set; } = 0.85f;

    public bool ShowScrollbars { get; set; }

    public BetterDeathsTheme Theme { get; set; } = BetterDeathsTheme.Classic;

    public CustomThemeConfiguration CustomTheme { get; set; } = new();

    public bool HasChangedTheme { get; set; }

    public List<BetterDeathsTheme> SeenNewThemeBadges { get; set; } = [];

    public bool ShowCurrentPullWidget { get; set; }

    public float CurrentPullWidgetBackgroundOpacity { get; set; } = 1.0f;

    public float WidgetIconSize { get; set; } = 20.0f;

    public WidgetDisplayMode WidgetDisplayMode { get; set; } = WidgetDisplayMode.Normal;

    public ReviewDisplayMode ReviewDisplayMode { get; set; } = ReviewDisplayMode.Detailed;

    public LeadUpTimelineOrder LeadUpTimelineOrder { get; set; } = LeadUpTimelineOrder.Newest;

    public bool RemoveChatBranding { get; set; }

    public bool PostDeathRecapLinksOnDeath { get; set; }

    public DeathRecapLinkChannel DeathRecapLinkChannel { get; set; } = DeathRecapLinkChannel.SystemMessage;

    public bool RedactPlayerNames { get; set; }

    public bool ShowDeathRecapPopup { get; set; } = true;

    // Legacy setting name kept so older configs can migrate the saved value to MainWindowBackgroundOpacity.
    public float DeathRecapPopupBackgroundOpacity { get; set; } = 0.85f;

    public bool HasDeathRecapPopupPosition { get; set; }

    public float DeathRecapPopupPositionX { get; set; }

    public float DeathRecapPopupPositionY { get; set; }

    public bool CapturePartyDeaths { get; set; } = true;

    public bool CaptureOtherDeaths { get; set; }

    public ClockDisplayMode ClockDisplayMode { get; set; } = ClockDisplayMode.TwentyFourHour;

    public bool PullBrowserCollapsed { get; set; } = true;

    public float PullBrowserWidth { get; set; } = 300.0f;

    public float ReviewTimelineWidth { get; set; }

    public float StackedPullBrowserHeight { get; set; }

    public float StackedCollapsedPullBrowserHeight { get; set; }

    public float StackedTimelineHeight { get; set; }

    public float DeathTimelineLeadUpHeight { get; set; }

    public bool ShowLeadUpTimelineMitigationTimers { get; set; } = true;

    public bool ShowReplayTrails { get; set; } = true;

    public bool ShowReplayPlayerNames { get; set; }

    public bool ShowReplayPlayerJobs { get; set; } = true;

    public bool ShowReplayPlayerHp { get; set; }

    public float ReplayWorldMarkerOpacity { get; set; } = 0.75f;

    public bool UseCustomPullGroupColors { get; set; }

    public List<ThemeColorValue> PullGroupColors { get; set; } = [];

    public string ActiveDutyInstancePullGroupId { get; set; } = string.Empty;

    public int ActiveDutyInstancePullGroupColorIndex { get; set; } = -1;

    public uint ActiveDutyInstancePullGroupTerritoryId { get; set; }

    public DateTime ActiveDutyInstancePullGroupClearedAtUtc { get; set; } = DateTime.MinValue;

    public bool ShowDebugTab { get; set; }

    public bool DebugLogEnabled { get; set; }

    public bool DebugSaveToFileEnabled { get; set; } = true;

    public string LastAnnouncedUpdateNoticeKey { get; set; } = string.Empty;

    public string LastSeenChangelogVersion { get; set; } = string.Empty;

    public bool PuniRepositoryMigrationComplete { get; set; }

    public DeathChatChannel DeathChatChannel { get; set; } = DeathChatChannel.Party;

    public int RecentEventSeconds { get; set; } = 20;

    public int DeathCauseSeconds { get; set; } = 15;

    public int MaxRecordedPulls { get; set; } = 20;

    public float ActionIconSize { get; set; } = 24.0f;

    public float StatusIconSize { get; set; } = 24.0f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
