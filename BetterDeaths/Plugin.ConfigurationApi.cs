using BetterDeaths.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Game.Chat;
using Dalamud.Game.NativeWrapper;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Game.DutyState;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace BetterDeaths;

public sealed partial class Plugin
{
    public void SetShowWindowByDefault(bool open)
    {
        Configuration.ShowWindow = open;
        SaveConfiguration();
    }

    public void SetShowCurrentPullWidget(bool open)
    {
        Configuration.ShowCurrentPullWidget = open;
        currentPullWidgetWindow.IsOpen = open;
        SaveConfiguration();
    }

    public void SetCurrentPullWidgetBackgroundOpacity(float opacity)
    {
        Configuration.CurrentPullWidgetBackgroundOpacity = Math.Clamp(
            opacity,
            CurrentPullWidgetMinBackgroundOpacity,
            CurrentPullWidgetMaxBackgroundOpacity);
        SaveConfiguration();
    }

    public void SetMainWindowBackgroundOpacity(float opacity)
    {
        Configuration.MainWindowBackgroundOpacity = Math.Clamp(
            opacity,
            MainWindowMinBackgroundOpacity,
            MainWindowMaxBackgroundOpacity);
        SaveConfiguration();
    }

    public void SetShowScrollbars(bool show)
    {
        Configuration.ShowScrollbars = show;
        SaveConfiguration();
    }

    public void SetTheme(BetterDeathsTheme theme)
    {
        if (!Enum.IsDefined(theme) || Configuration.Theme == theme)
        {
            return;
        }

        Configuration.Theme = theme;
        Configuration.HasChangedTheme = true;
        SaveConfiguration();
    }

    public void NotifyCurrentPullWidgetClosed()
    {
        if (disposing || !Configuration.ShowCurrentPullWidget)
        {
            return;
        }

        Configuration.ShowCurrentPullWidget = false;
        SaveConfiguration();
    }

    public void MarkChangelogVersionSeen(string version)
    {
        if (string.Equals(Configuration.LastSeenChangelogVersion, version, StringComparison.Ordinal))
        {
            return;
        }

        Configuration.LastSeenChangelogVersion = version;
        SaveConfiguration();
    }

    public void MarkWideDefaultWindowSizeApplied()
    {
        if (!Configuration.ApplyWideDefaultWindowSizeOnNextOpen)
        {
            return;
        }

        Configuration.ApplyWideDefaultWindowSizeOnNextOpen = false;
        SaveConfiguration();
    }

    public void SaveDeathRecapPopupPosition(Vector2 position)
    {
        if (Configuration.HasDeathRecapPopupPosition &&
            MathF.Abs(Configuration.DeathRecapPopupPositionX - position.X) <= 0.5f &&
            MathF.Abs(Configuration.DeathRecapPopupPositionY - position.Y) <= 0.5f)
        {
            return;
        }

        Configuration.HasDeathRecapPopupPosition = true;
        Configuration.DeathRecapPopupPositionX = position.X;
        Configuration.DeathRecapPopupPositionY = position.Y;
        SaveConfiguration();
    }

    public void SetRemoveChatBranding(bool remove)
    {
        Configuration.RemoveChatBranding = remove;
        SaveConfiguration();
    }

    public void SetPostDeathRecapLinksOnDeath(bool enabled)
    {
        Configuration.PostDeathRecapLinksOnDeath = enabled;
        SaveConfiguration();
    }

    public void SetDeathRecapLinkChannel(DeathRecapLinkChannel channel)
    {
        Configuration.DeathRecapLinkChannel = GetDeathRecapLinkChannelOption(channel).Channel;
        SaveConfiguration();
    }

    public void SetRedactPlayerNames(bool enabled)
    {
        Configuration.RedactPlayerNames = enabled;
        SaveConfiguration();
    }

    public void SetShowDeathRecapPopup(bool enabled)
    {
        Configuration.ShowDeathRecapPopup = enabled;
        SaveConfiguration();
    }

    public void SetDeathRecapPopupTestActive(bool enabled)
    {
        if (enabled)
        {
            deathRecapPopupWindow.DisplayTest();
            return;
        }

        deathRecapPopupWindow.CloseTest();
    }

    public void SetCapturePartyDeaths(bool enabled)
    {
        Configuration.CapturePartyDeaths = enabled;
        SaveConfiguration();
    }

    public void SetCaptureOtherDeaths(bool enabled)
    {
        Configuration.CaptureOtherDeaths = enabled;
        SaveConfiguration();
    }

    public void SetClockDisplayMode(ClockDisplayMode mode)
    {
        Configuration.ClockDisplayMode = mode;
        SaveConfiguration();
    }

    public void SetPullBrowserCollapsed(bool collapsed)
    {
        if (Configuration.PullBrowserCollapsed == collapsed)
        {
            return;
        }

        Configuration.PullBrowserCollapsed = collapsed;
        SaveConfiguration();
    }

    public void SetDeathChatChannel(DeathChatChannel channel)
    {
        Configuration.DeathChatChannel = GetChatChannelOption(channel).Channel;
        SaveConfiguration();
    }

    public void SetWidgetDisplayMode(WidgetDisplayMode mode)
    {
        Configuration.WidgetDisplayMode = Enum.IsDefined(mode)
            ? mode
            : WidgetDisplayMode.Normal;
        SaveConfiguration();
    }

    public void SetReviewDisplayMode(ReviewDisplayMode mode)
    {
        Configuration.ReviewDisplayMode = Enum.IsDefined(mode)
            ? mode
            : ReviewDisplayMode.Detailed;
        SaveConfiguration();
    }

    public void SetLeadUpTimelineOrder(LeadUpTimelineOrder order)
    {
        Configuration.LeadUpTimelineOrder = Enum.IsDefined(order)
            ? order
            : LeadUpTimelineOrder.Newest;
        SaveConfiguration();
    }

    public void SetShowReplayTrails(bool show)
    {
        if (Configuration.ShowReplayTrails == show)
        {
            return;
        }

        Configuration.ShowReplayTrails = show;
        SaveConfiguration();
    }

    public void SetShowReplayPlayerNames(bool show)
    {
        if (Configuration.ShowReplayPlayerNames == show)
        {
            return;
        }

        Configuration.ShowReplayPlayerNames = show;
        SaveConfiguration();
    }

    public void SetShowReplayPlayerJobs(bool show)
    {
        if (Configuration.ShowReplayPlayerJobs == show)
        {
            return;
        }

        Configuration.ShowReplayPlayerJobs = show;
        SaveConfiguration();
    }

    public void SetShowReplayPlayerHp(bool show)
    {
        if (Configuration.ShowReplayPlayerHp == show)
        {
            return;
        }

        Configuration.ShowReplayPlayerHp = show;
        SaveConfiguration();
    }

    public void SetReplayWorldMarkerOpacity(float opacity)
    {
        Configuration.ReplayWorldMarkerOpacity = Math.Clamp(
            opacity,
            MinReplayWorldMarkerOpacity,
            MaxReplayWorldMarkerOpacity);
        SaveConfiguration();
    }

    public void SetWidgetIconSize(float size)
    {
        Configuration.WidgetIconSize = Math.Clamp(size, MinWidgetIconSize, MaxWidgetIconSize);
        SaveConfiguration();
    }

    public void SetRecentEventSeconds(int seconds)
    {
        Configuration.RecentEventSeconds = Math.Clamp(seconds, 5, 60);
        SaveConfiguration();
    }

    public void SetDeathCauseSeconds(int seconds)
    {
        Configuration.DeathCauseSeconds = Math.Clamp(seconds, 5, 60);
        SaveConfiguration();
    }

    public void SetMaxRecordedPulls(int pulls)
    {
        var maxRecordedPulls = Math.Clamp(pulls, 1, 100);
        if (Configuration.MaxRecordedPulls == maxRecordedPulls)
        {
            return;
        }

        Configuration.MaxRecordedPulls = maxRecordedPulls;
        SaveConfiguration();
        WaitForRecordedPullHistoryLoadForMutation();
        lock (recordedPullLock)
        {
            TrimRecordedPullsLocked();
            UpdateRecordedPullSummariesLocked();
        }

        SaveRecordedPullHistory();
    }

    public void SetActionIconSize(float size)
    {
        Configuration.ActionIconSize = Math.Clamp(size, 12.0f, 48.0f);
        SaveConfiguration();
    }

    public void SetStatusIconSize(float size)
    {
        Configuration.StatusIconSize = Math.Clamp(size, 12.0f, 48.0f);
        SaveConfiguration();
    }

    public void SetIconSize(float size)
    {
        var clampedSize = Math.Clamp(size, 12.0f, 48.0f);
        Configuration.ActionIconSize = clampedSize;
        Configuration.StatusIconSize = clampedSize;
        SaveConfiguration();
    }

    public void SaveConfiguration()
    {
        Configuration.Save();
    }

    private void NormalizeUserConfiguration()
    {
        var changed = false;
        var loadedConfigurationVersion = Configuration.Version;
        var iconSize = Math.Clamp(MathF.Max(Configuration.ActionIconSize, Configuration.StatusIconSize), 12.0f, 48.0f);
        var widgetIconSize = Math.Clamp(
            Configuration.WidgetIconSize <= 0.0f ? 20.0f : Configuration.WidgetIconSize,
            MinWidgetIconSize,
            MaxWidgetIconSize);
        var widgetBackgroundOpacity = Math.Clamp(
            Configuration.CurrentPullWidgetBackgroundOpacity <= 0.0f
                ? CurrentPullWidgetMaxBackgroundOpacity
                : Configuration.CurrentPullWidgetBackgroundOpacity,
            CurrentPullWidgetMinBackgroundOpacity,
            CurrentPullWidgetMaxBackgroundOpacity);
        var legacyPopupBackgroundOpacity = Math.Clamp(
            Configuration.DeathRecapPopupBackgroundOpacity <= 0.0f
                ? DefaultMainWindowBackgroundOpacity
                : Configuration.DeathRecapPopupBackgroundOpacity,
            MainWindowMinBackgroundOpacity,
            MainWindowMaxBackgroundOpacity);
        var mainWindowBackgroundOpacity = Math.Clamp(
            Configuration.MainWindowBackgroundOpacity <= 0.0f
                ? DefaultMainWindowBackgroundOpacity
                : Configuration.MainWindowBackgroundOpacity,
            MainWindowMinBackgroundOpacity,
            MainWindowMaxBackgroundOpacity);
        const float pullBrowserWidth = 300.0f;
        var recentEventSeconds = Math.Clamp(Configuration.RecentEventSeconds, 5, 60);
        var deathCauseSeconds = Math.Clamp(Configuration.DeathCauseSeconds, 5, 60);
        var maxRecordedPulls = Math.Clamp(Configuration.MaxRecordedPulls, 1, 100);

        if (MathF.Abs(Configuration.ActionIconSize - iconSize) > 0.01f)
        {
            Configuration.ActionIconSize = iconSize;
            changed = true;
        }

        if (MathF.Abs(Configuration.StatusIconSize - iconSize) > 0.01f)
        {
            Configuration.StatusIconSize = iconSize;
            changed = true;
        }

        if (MathF.Abs(Configuration.WidgetIconSize - widgetIconSize) > 0.01f)
        {
            Configuration.WidgetIconSize = widgetIconSize;
            changed = true;
        }

        if (MathF.Abs(Configuration.CurrentPullWidgetBackgroundOpacity - widgetBackgroundOpacity) > 0.01f)
        {
            Configuration.CurrentPullWidgetBackgroundOpacity = widgetBackgroundOpacity;
            changed = true;
        }

        if (loadedConfigurationVersion < 4)
        {
            mainWindowBackgroundOpacity = legacyPopupBackgroundOpacity;
            Configuration.ApplyWideDefaultWindowSizeOnNextOpen = true;
            changed = true;
        }

        if (MathF.Abs(Configuration.MainWindowBackgroundOpacity - mainWindowBackgroundOpacity) > 0.01f)
        {
            Configuration.MainWindowBackgroundOpacity = mainWindowBackgroundOpacity;
            changed = true;
        }

        if (MathF.Abs(Configuration.PullBrowserWidth - pullBrowserWidth) > 0.5f)
        {
            Configuration.PullBrowserWidth = pullBrowserWidth;
            changed = true;
        }

        if (float.IsNaN(Configuration.ReviewTimelineWidth) ||
            float.IsInfinity(Configuration.ReviewTimelineWidth) ||
            Configuration.ReviewTimelineWidth < 0.0f)
        {
            Configuration.ReviewTimelineWidth = 0.0f;
            changed = true;
        }

        if (float.IsNaN(Configuration.StackedPullBrowserHeight) ||
            float.IsInfinity(Configuration.StackedPullBrowserHeight) ||
            Configuration.StackedPullBrowserHeight < 0.0f)
        {
            Configuration.StackedPullBrowserHeight = 0.0f;
            changed = true;
        }

        if (float.IsNaN(Configuration.StackedCollapsedPullBrowserHeight) ||
            float.IsInfinity(Configuration.StackedCollapsedPullBrowserHeight) ||
            Configuration.StackedCollapsedPullBrowserHeight < 0.0f)
        {
            Configuration.StackedCollapsedPullBrowserHeight = 0.0f;
            changed = true;
        }

        if (float.IsNaN(Configuration.StackedTimelineHeight) ||
            float.IsInfinity(Configuration.StackedTimelineHeight) ||
            Configuration.StackedTimelineHeight < 0.0f)
        {
            Configuration.StackedTimelineHeight = 0.0f;
            changed = true;
        }

        if (float.IsNaN(Configuration.DeathTimelineLeadUpHeight) ||
            float.IsInfinity(Configuration.DeathTimelineLeadUpHeight) ||
            Configuration.DeathTimelineLeadUpHeight < 0.0f)
        {
            Configuration.DeathTimelineLeadUpHeight = 0.0f;
            changed = true;
        }

        if (loadedConfigurationVersion < 2)
        {
            Configuration.PullBrowserCollapsed = true;
            changed = true;
        }

        if (Configuration.RecentEventSeconds != recentEventSeconds)
        {
            Configuration.RecentEventSeconds = recentEventSeconds;
            changed = true;
        }

        if (Configuration.DeathCauseSeconds != deathCauseSeconds)
        {
            Configuration.DeathCauseSeconds = deathCauseSeconds;
            changed = true;
        }

        if (Configuration.MaxRecordedPulls != maxRecordedPulls)
        {
            Configuration.MaxRecordedPulls = maxRecordedPulls;
            changed = true;
        }

        if (!Enum.IsDefined(Configuration.WidgetDisplayMode))
        {
            Configuration.WidgetDisplayMode = WidgetDisplayMode.Normal;
            changed = true;
        }

        if (!Enum.IsDefined(Configuration.ReviewDisplayMode))
        {
            Configuration.ReviewDisplayMode = ReviewDisplayMode.Detailed;
            changed = true;
        }

        if (!Enum.IsDefined(Configuration.LeadUpTimelineOrder))
        {
            Configuration.LeadUpTimelineOrder = LeadUpTimelineOrder.Newest;
            changed = true;
        }

        if (!Enum.IsDefined(Configuration.ClockDisplayMode))
        {
            Configuration.ClockDisplayMode = ClockDisplayMode.TwentyFourHour;
            changed = true;
        }

        if (!Enum.IsDefined(Configuration.Theme))
        {
            Configuration.Theme = BetterDeathsTheme.Classic;
            changed = true;
        }

        if (Configuration.CustomTheme is null)
        {
            Configuration.CustomTheme = new CustomThemeConfiguration();
            changed = true;
        }

        if (NormalizeCustomTheme(Configuration.CustomTheme, Configuration.Theme))
        {
            changed = true;
        }

        if (Configuration.PullGroupColors is null)
        {
            Configuration.PullGroupColors = [];
            changed = true;
        }
        else
        {
            while (Configuration.PullGroupColors.Count > PullGroupColorPaletteSize)
            {
                Configuration.PullGroupColors.RemoveAt(Configuration.PullGroupColors.Count - 1);
                changed = true;
            }

            for (var i = 0; i < Configuration.PullGroupColors.Count; i++)
            {
                if (Configuration.PullGroupColors[i] is null)
                {
                    Configuration.PullGroupColors[i] = new ThemeColorValue();
                    changed = true;
                }
            }
        }

        if (!Configuration.HasChangedTheme && Configuration.Theme != BetterDeathsTheme.Classic)
        {
            Configuration.HasChangedTheme = true;
            changed = true;
        }

        if (!Enum.IsDefined(Configuration.DeathChatChannel))
        {
            Configuration.DeathChatChannel = DeathChatChannel.Party;
            changed = true;
        }

        if (!Enum.IsDefined(Configuration.DeathRecapLinkChannel))
        {
            Configuration.DeathRecapLinkChannel = DeathRecapLinkChannel.SystemMessage;
            changed = true;
        }

        if (Configuration.Version != CurrentConfigurationVersion)
        {
            Configuration.Version = CurrentConfigurationVersion;
            changed = true;
        }

        if (changed)
        {
            SaveConfiguration();
        }
    }
}
