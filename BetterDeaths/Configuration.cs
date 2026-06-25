using Dalamud.Configuration;
using System;

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
}

public sealed record ChatChannelOption(DeathChatChannel Channel, string Label, string Command);

public enum WidgetDisplayMode
{
    Normal,
    Concise,
}

public enum ClockDisplayMode
{
    TwentyFourHour,
    TwelveHour,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    public bool ShowWindow { get; set; } = true;

    public bool ApplyWideDefaultWindowSizeOnNextOpen { get; set; }

    public float MainWindowBackgroundOpacity { get; set; } = 0.85f;

    public bool ShowCurrentPullWidget { get; set; }

    public float CurrentPullWidgetBackgroundOpacity { get; set; } = 1.0f;

    public float WidgetIconSize { get; set; } = 20.0f;

    public WidgetDisplayMode WidgetDisplayMode { get; set; } = WidgetDisplayMode.Normal;

    public bool RemoveChatBranding { get; set; }

    public bool PostDeathRecapLinksOnDeath { get; set; }

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

    public bool ShowDebugTab { get; set; }

    public bool DebugLogEnabled { get; set; }

    public bool DebugSaveToFileEnabled { get; set; } = true;

    public string LastAnnouncedUpdateNoticeKey { get; set; } = string.Empty;

    public string LastSeenChangelogVersion { get; set; } = string.Empty;

    public string LastAcknowledgedNoticeId { get; set; } = string.Empty;

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
