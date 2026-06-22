using BetterDeaths.Windows;
using Dalamud.Game.Command;
using Dalamud.Game.Chat;
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
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace BetterDeaths;

public sealed class Plugin : IDalamudPlugin
{
    private const string MainCommandName = "/betterdeaths";
    private const string ShortCommandName = "/bd";
    private const string WidgetCommandName = "/betterdeathswidget";
    private const string ShortWidgetCommandName = "/bdwidget";
    private const string SharedRecapPrefix = "Recap:";
    private const float SharedRecapMatchWindowSeconds = 5.0f;
    private const int RecentStatusHistorySeconds = 20;
    private const float StatusDeathRemainingWindowSeconds = 5.0f;
    private const int OwnSharedRecapSuppressionSeconds = 5;
    private const int QueuedChatDelayMs = 750;
    private const int DeathRecapLinkBatchDelaySeconds = 3;
    private const int MaxQueuedChatMessageLength = 450;
    private const int MaxDebugLogEntries = 500;
    private const string RecordedPullHistoryFileName = "recorded-pulls.json";
    private const int RecordedPullHistorySchemaVersion = 2;
    private const int RecordedPullHistoryRollingBackupCount = 5;
    private const string RecordedPullHistoryRollingBackupSearchPattern = "recorded-pulls.backup.*.json";
    private const ushort ChatGreenColorKey = 45;
    private const int BetterDeathsLeadUpSeconds = 10;
    private const int BetterDeathsLeadUpCaptureSeconds = BetterDeathsLeadUpSeconds + 10;
    private const int HpHistoryRetentionSeconds = BetterDeathsLeadUpCaptureSeconds + 5;
    private const int CombatLogEventRetentionSeconds = BetterDeathsLeadUpCaptureSeconds + 5;
    private const int MaxCombatLogEventsPerMember = 80;
    public const float CurrentPullWidgetMinBackgroundOpacity = 0.35f;
    public const float CurrentPullWidgetMaxBackgroundOpacity = 1.0f;
    public const float MinWidgetIconSize = 12.0f;
    public const float MaxWidgetIconSize = 32.0f;
    private static readonly TimeSpan FatalSequenceStartBuffer = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan FatalSequenceEndBuffer = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PostCombatCaptureGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PluginUpdateCheckInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan HpHistorySampleInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LiveCapturePruneInterval = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions RecordedPullHistoryJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };
    private static readonly Regex SharedKnownDeathPostRegex = new(
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\):\s+(?<amount>[\d,]+)\s+from\s+(?<action>.+?)\s+by\s+(?<source>.+?)\.\s+HP before hit:\s+.+\.$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SharedMultiHitDeathPostRegex = new(
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\):\s+(?<amount>[\d,]+)\s+damage\s+by\s+(?<hits>\d+)\s+hits\s+from\s+(?<source>.+?)\.\s+HP before hit:\s+.+\.$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SharedStatusDeathPostRegex = new(
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\):\s+(?<action>.+?)\s+from\s+(?<source>.+?)\.\s+HP before KO:\s+.+\.$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SharedUnknownDeathPostRegex = new(
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\):\s+likely walled/non-hit KO\.\s+HP before hit unavailable\.$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly IReadOnlyList<ChatChannelOption> ChatChannelOptions =
    [
        new(DeathChatChannel.Say, "Say", "/s"),
        new(DeathChatChannel.Party, "Party", "/p"),
        new(DeathChatChannel.Alliance, "Alliance", "/alliance"),
        new(DeathChatChannel.FreeCompany, "Free Company", "/fc"),
        new(DeathChatChannel.CrossWorldLinkshell1, "Cross-world Linkshell 1", "/cwl1"),
        new(DeathChatChannel.CrossWorldLinkshell2, "Cross-world Linkshell 2", "/cwl2"),
        new(DeathChatChannel.CrossWorldLinkshell3, "Cross-world Linkshell 3", "/cwl3"),
        new(DeathChatChannel.CrossWorldLinkshell4, "Cross-world Linkshell 4", "/cwl4"),
        new(DeathChatChannel.CrossWorldLinkshell5, "Cross-world Linkshell 5", "/cwl5"),
        new(DeathChatChannel.CrossWorldLinkshell6, "Cross-world Linkshell 6", "/cwl6"),
        new(DeathChatChannel.CrossWorldLinkshell7, "Cross-world Linkshell 7", "/cwl7"),
        new(DeathChatChannel.CrossWorldLinkshell8, "Cross-world Linkshell 8", "/cwl8"),
    ];

    private sealed record RecordedPullHistoryFile(int SchemaVersion, List<PullDeathSnapshot> Pulls);

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("BetterDeaths");
    private readonly RecapWindow recapWindow;
    private readonly CurrentPullWidgetWindow currentPullWidgetWindow;
    private readonly List<PartyMemberSnapshot> currentMembers = [];
    private readonly List<PartyDeathRecord> currentDeaths = [];
    private readonly List<PullDeathSnapshot> recordedPulls = [];
    private readonly List<DebugLogEntry> debugLogEntries = [];
    private readonly DalamudLinkPayload deathChatLinkPayload;
    private readonly Dictionary<string, List<CombatEventRecord>> recentEventsByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CombatLogEventRecord>> recentCombatLogEventsByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<StatusObservation>> recentStatusesByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<HpHistorySnapshot>> recentHpHistoryByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> lastHpHistorySampleByMember = new(StringComparer.Ordinal);
    private readonly HashSet<string> deadMemberKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> postResetSuppressedDeadMemberKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> currentMemberKeyScratch = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> actionNameCache = new();
    private readonly Dictionary<uint, uint> actionIconCache = new();
    private readonly Dictionary<uint, string> statusNameCache = new();
    private readonly Dictionary<uint, uint> statusIconCache = new();
    private readonly Dictionary<uint, string> classJobNameCache = new();
    private readonly Dictionary<uint, string> territoryNameCache = new();
    private readonly List<RecentOwnSharedDeathPost> recentOwnSharedDeathPosts = [];
    private readonly List<PartyDeathRecord> pendingDeathRecapLinks = [];
    private readonly Queue<QueuedChatMessage> queuedChatMessages = [];
    private DateTime? pendingDeathRecapLinksDueAtUtc;
    private DateTime nextQueuedChatMessageAtUtc = DateTime.MinValue;
    private DateTime nextPluginUpdateCheckAtUtc = DateTime.MinValue;
    private DateTime nextLiveCapturePruneAtUtc = DateTime.MinValue;
    private DateTime? lastPluginUpdateCheckAtUtc;
    private PluginUpdateCheckState pluginUpdateCheckState = PluginUpdateCheckState.NotChecked;
    private string? availablePluginUpdateVersion;
    private bool availablePluginUpdateIsTesting;
    private string? pluginUpdateCheckError;
    private string? pendingUpdateNoticeKey;
    private string? lastSavedRecordedPullHistoryJson;
    private bool updateCheckInProgress;
    private static readonly string[] DefensiveStatusNames =
    [
        "Aquaveil",
        "Bloodwhetting",
        "Bulwark",
        "Camouflage",
        "Catalyze",
        "Collective Unconscious",
        "Dark Force",
        "Dark Mind",
        "Dark Missionary",
        "Divine Benison",
        "Divine Caress",
        "Divine Veil",
        "Eukrasian Diagnosis",
        "Eukrasian Prognosis",
        "Exaltation",
        "Expedient",
        "Galvanize",
        "Gunmetal Soul",
        "Haima",
        "Hallowed Ground",
        "Heart of Corundum",
        "Heart of Light",
        "Heart of Stone",
        "Holos",
        "Holmgang",
        "Intervention",
        "Kerachole",
        "Land Waker",
        "Living Dead",
        "Magick Barrier",
        "Manaward",
        "Nascent Flash",
        "Nebula",
        "Oblation",
        "Panhaima",
        "Passage of Arms",
        "Rampart",
        "Radiant Aegis",
        "Riddle of Earth",
        "Sacred Soil",
        "Sentinel",
        "Shade Shift",
        "Shake It Off",
        "Sheltron",
        "Shield Samba",
        "Shadow Wall",
        "Superbolide",
        "Tactician",
        "Taurochole",
        "Temperance",
        "The Blackest Night",
        "Third Eye",
        "Thrill of Battle",
        "Troubadour",
        "Undead Rebirth",
        "Vengeance",
        "Wheel of Fortune",
    ];

    private static readonly string[] EncounterDebuffNameFragments =
    [
        "accretion",
        "acceleration bomb",
        "allagan field",
        "beyond death",
        "bleeding",
        "burns",
        "compressed water",
        "curse",
        "cursed shriek",
        "damage down",
        "deep freeze",
        "doom",
        "dynamic fluid",
        "earth resistance down",
        "entropy",
        "fire resistance down",
        "forked lightning",
        "heavy",
        "ice resistance down",
        "lightning resistance down",
        "magic vulnerability up",
        "misdirection",
        "physical vulnerability up",
        "poison",
        "prey",
        "pyretic",
        "resistance down",
        "surprise flare",
        "surprise holy",
        "throttle",
        "vulnerability up",
        "water resistance down",
        "wind resistance down",
    ];

    private static readonly uint[] BossMitigationStatusIds =
    [
        1203, // Addle
        1195, // Feint
        1193, // Reprisal
        860, // Dismantled
        1715, // Malodorous
        2115, // Conked
        3642, // Candy Cane
    ];

    private static readonly string[] DoomStatusNameFragments =
    [
        "doom",
    ];

    private static readonly uint[] CombatDamageLogMessageIds =
    [
        448,
        449,
        450,
        451,
        504,
        505,
        509,
        510,
        511,
        518,
        527,
        531,
    ];

    private Hook<ActionEffectHandler.Delegates.Receive>? actionEffectHook;
    private DateTime? pullStartedAtUtc;
    private DateTime? lastInCombatAtUtc;
    private float lastKnownPullElapsedSeconds;
    private bool combatTimerRunning;
    private bool collectingPostResetDeadMembers;
    private bool currentPullClosedForReview;
    private bool currentPullSnapshotCaptured;
    private long currentPullRecordedPullNumber;
    private long nextRecordedPullNumber = 1;
    private uint currentTerritoryId;
    private string currentTerritoryName = "Unknown territory";
    private uint currentPullTerritoryId;
    private string currentPullTerritoryName = "Unknown territory";
    private bool disposing;

    public Configuration Configuration { get; }

    public IReadOnlyList<PartyMemberSnapshot> CurrentMembers => currentMembers;

    public IReadOnlyList<PartyDeathRecord> CurrentDeaths => currentDeaths;

    public IReadOnlyList<PullDeathSnapshot> RecordedPulls => recordedPulls;

    public IReadOnlyList<DebugLogEntry> DebugLogEntries => debugLogEntries;

    public PluginUpdateStatus PluginUpdateStatus => new(
        pluginUpdateCheckState,
        FormatVersionForDisplay(typeof(Plugin).Assembly.GetName().Version),
        FormatVersionForDisplay(PluginInterface.Manifest.AssemblyVersion),
        availablePluginUpdateVersion,
        availablePluginUpdateIsTesting,
        lastPluginUpdateCheckAtUtc,
        nextPluginUpdateCheckAtUtc == DateTime.MinValue ? null : nextPluginUpdateCheckAtUtc,
        pluginUpdateCheckError);

    public uint CurrentTerritoryId => currentTerritoryId;

    public string CurrentTerritoryName => currentTerritoryName;

    public string CurrentPullTerritoryName => currentPullTerritoryId == 0
        ? currentTerritoryName
        : currentPullTerritoryName;

    public bool CurrentPullClosedForReview => currentPullClosedForReview;

    public long CurrentPullRecordedPullNumber => currentPullRecordedPullNumber;

    public float CurrentPullElapsedSeconds => pullStartedAtUtc is not null && combatTimerRunning
        ? CalculatePullElapsed(DateTime.UtcNow)
        : lastKnownPullElapsedSeconds;

    public unsafe Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        NormalizeIconSizeConfiguration();
        LoadRecordedPullHistory();
        deathChatLinkPayload = ChatGui.AddChatLinkHandler(0, OnDeathChatLinkClick);

        recapWindow = new RecapWindow(this)
        {
            IsOpen = Configuration.ShowWindow,
        };
        windowSystem.AddWindow(recapWindow);

        currentPullWidgetWindow = new CurrentPullWidgetWindow(this, recapWindow)
        {
            IsOpen = Configuration.ShowCurrentPullWidget,
        };
        windowSystem.AddWindow(currentPullWidgetWindow);

        CommandManager.AddHandler(MainCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle Better Deaths.",
        });
        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle Better Deaths.",
        });
        CommandManager.AddHandler(WidgetCommandName, new CommandInfo(OnWidgetCommand)
        {
            HelpMessage = "Toggle the Better Deaths current pull widget.",
        });
        CommandManager.AddHandler(ShortWidgetCommandName, new CommandInfo(OnWidgetCommand)
        {
            HelpMessage = "Toggle the Better Deaths current pull widget.",
        });

        actionEffectHook = GameInteropProvider.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
            ActionEffectHandler.MemberFunctionPointers.Receive,
            OnReceiveActionEffect);
        actionEffectHook.Enable();

        DutyState.DutyStarted += OnDutyReset;
        DutyState.DutyWiped += OnDutyReset;
        DutyState.DutyRecommenced += OnDutyReset;
        ChatGui.ChatMessage += OnChatMessage;
        ChatGui.LogMessage += OnLogMessage;
        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenMainUi;
    }

    public void Dispose()
    {
        disposing = true;
        CaptureCurrentPullSnapshot("Plugin unloaded");
        SaveRecordedPullHistory();
        PluginInterface.UiBuilder.OpenConfigUi -= OpenMainUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Framework.Update -= OnFrameworkUpdate;
        ChatGui.LogMessage -= OnLogMessage;
        ChatGui.ChatMessage -= OnChatMessage;
        DutyState.DutyRecommenced -= OnDutyReset;
        DutyState.DutyWiped -= OnDutyReset;
        DutyState.DutyStarted -= OnDutyReset;
        actionEffectHook?.Dispose();
        ChatGui.RemoveChatLinkHandler(0);
        CommandManager.RemoveHandler(ShortWidgetCommandName);
        CommandManager.RemoveHandler(WidgetCommandName);
        CommandManager.RemoveHandler(ShortCommandName);
        CommandManager.RemoveHandler(MainCommandName);
        windowSystem.RemoveAllWindows();
    }

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

    public void SetRemoveChatBranding(bool remove)
    {
        Configuration.RemoveChatBranding = remove;
        SaveConfiguration();
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

    public void SetDebugLogEnabled(bool enabled)
    {
        Configuration.DebugLogEnabled = enabled;
        SaveConfiguration();
    }

    public void ClearDebugLog()
    {
        debugLogEntries.Clear();
    }

    public void ClearRecordedPulls()
    {
        recordedPulls.Clear();
        nextRecordedPullNumber = 1;
        currentPullRecordedPullNumber = 0;
        DeleteRecordedPullHistoryFiles();
        lastSavedRecordedPullHistoryJson = null;
        SaveRecordedPullHistory();
    }

    public void SetDeathChatChannel(DeathChatChannel channel)
    {
        Configuration.DeathChatChannel = GetChatChannelOption(channel).Channel;
        SaveConfiguration();
    }

    public void SetWidgetPlayerNameDisplayMode(WidgetPlayerNameDisplayMode mode)
    {
        Configuration.WidgetPlayerNameDisplayMode = Enum.IsDefined(mode)
            ? mode
            : WidgetPlayerNameDisplayMode.FullName;
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
        TrimRecordedPulls();
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

    public void PrintDeathInformationToChat(PartyDeathRecord death)
    {
        var timer = FormatCombatTimer(death.PullElapsedSeconds);
        var playerLabel = $"{death.MemberName} ({death.ClassJobName})";
        var prefix = GetChatBrandingPrefix();
        var selection = DeathDisplaySelector.Select(death);
        var causeEvents = selection.Events;
        if (causeEvents.Count == 0)
        {
            RememberOwnSharedDeathPost(death);
            QueueChat(Configuration.DeathChatChannel, $"{prefix}{SharedRecapPrefix} {timer} {playerLabel}: likely walled/non-hit KO. HP before hit unavailable.");
            QueueChat(Configuration.DeathChatChannel, $"Active mits: {FormatDeathStatusList(death, selection)}.");
            QueueChat(Configuration.DeathChatChannel, $"Player debuffs: {FormatPlayerDebuffStatusList(death, selection)}.");
            return;
        }

        RememberOwnSharedDeathPost(death);
        var damageEvents = causeEvents
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .ToList();
        if (damageEvents.Count > 1)
        {
            QueueChat(Configuration.DeathChatChannel, $"{prefix}{SharedRecapPrefix} {timer} {playerLabel}: {FormatDeathChatMultiHitLine(damageEvents, selection.Snapshot)}");
        }
        else
        {
            QueueChat(Configuration.DeathChatChannel, $"{prefix}{SharedRecapPrefix} {timer} {playerLabel}: {FormatDeathChatCauseLine(causeEvents[0], selection.Snapshot)}");
        }

        QueueChat(Configuration.DeathChatChannel, $"Active mits: {FormatDeathStatusList(death, selection)}.");
        QueueChat(Configuration.DeathChatChannel, $"Player debuffs: {FormatPlayerDebuffStatusList(death, selection)}.");
        PrintDeathRecapLink(death);
    }

    public static string GetChatChannelLabel(DeathChatChannel channel)
    {
        return GetChatChannelOption(channel).Label;
    }

    public static DeathChatChannel GetEffectiveChatChannel(DeathChatChannel channel)
    {
        return GetChatChannelOption(channel).Channel;
    }

    private static string FormatDeathStatusList(PartyDeathRecord death, DeathDisplaySelection selection)
    {
        var groups = new List<string>();
        var defenses = GetRelevantDeathStatuses(GetPrimaryDisplayStatusSnapshot(death, selection))
            .Where(IsDefensiveStatus)
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();
        if (defenses.Count > 0)
        {
            groups.Add($"Player: {FormatStatusList(defenses)}");
        }

        groups.AddRange(FormatBossMitigationGroups(selection));

        return groups.Count == 0 ? "none captured" : string.Join(" | ", groups);
    }

    private static string FormatPlayerDebuffStatusList(PartyDeathRecord death, DeathDisplaySelection selection)
    {
        var debuffs = GetRelevantDeathStatuses(GetPrimaryDisplayStatusSnapshot(death, selection))
            .Where(IsPlayerDebuffStatus)
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();

        return debuffs.Count == 0 ? "none captured" : FormatStatusList(debuffs);
    }

    private static IReadOnlyList<StatusSnapshot> GetPrimaryDisplayStatusSnapshot(PartyDeathRecord death, DeathDisplaySelection selection)
    {
        return selection.Snapshot?.Statuses ??
            GetPrimaryDisplayCauseEvent(selection.Events)?.Statuses ??
            death.StatusesAtDeath;
    }

    private static IReadOnlyList<string> FormatBossMitigationGroups(DeathDisplaySelection selection)
    {
        return selection.Events
            .SelectMany(combatEvent => combatEvent.SourceStatuses
                .Where(IsBossMitigationStatus)
                .Select(status => new { combatEvent.SourceName, Status = status }))
            .GroupBy(entry => entry.SourceName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var statuses = group
                    .Select(entry => entry.Status)
                    .GroupBy(status => status.Id)
                    .Select(statusGroup => statusGroup.OrderBy(status => status.RemainingTime).First())
                    .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(status => status.Id)
                    .ToList();

                return statuses.Count == 0 ? null : $"{group.Key}: {FormatStatusList(statuses)}";
            })
            .Where(groupText => groupText is not null)
            .Select(groupText => groupText!)
            .ToList();
    }

    public static uint GetMemberKeyHash(string memberKey)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in memberKey)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return hash;
        }
    }

    public void SaveConfiguration()
    {
        Configuration.Save();
    }

    private void NormalizeIconSizeConfiguration()
    {
        var iconSize = Math.Clamp(MathF.Max(Configuration.ActionIconSize, Configuration.StatusIconSize), 12.0f, 48.0f);
        var widgetIconSize = Math.Clamp(
            Configuration.WidgetIconSize <= 0.0f ? 20.0f : Configuration.WidgetIconSize,
            MinWidgetIconSize,
            MaxWidgetIconSize);
        if (MathF.Abs(Configuration.ActionIconSize - iconSize) <= 0.01f &&
            MathF.Abs(Configuration.StatusIconSize - iconSize) <= 0.01f &&
            MathF.Abs(Configuration.WidgetIconSize - widgetIconSize) <= 0.01f)
        {
            return;
        }

        Configuration.ActionIconSize = iconSize;
        Configuration.StatusIconSize = iconSize;
        Configuration.WidgetIconSize = widgetIconSize;
        SaveConfiguration();
    }

    private void OnCommand(string command, string args)
    {
        recapWindow.IsOpen = !recapWindow.IsOpen;
    }

    private void OnWidgetCommand(string command, string args)
    {
        SetShowCurrentPullWidget(!Configuration.ShowCurrentPullWidget);
    }

    private void OpenMainUi()
    {
        recapWindow.IsOpen = true;
    }

    private void OnDeathChatLinkClick(uint commandId, SeString message)
    {
        foreach (var rawPayload in message.Payloads.OfType<RawPayload>())
        {
            if (DeathChatLinkPayload.Decode(rawPayload) is not { } payload)
            {
                continue;
            }

            if (!recapWindow.FocusDeath(payload.DeathSeenAtTicks, payload.MemberKeyHash))
            {
                ChatGui.Print("[Better Deaths] That death recap is no longer available.");
            }

            return;
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            if (!TryParseSharedDeathPost(message.OriginalMessage.ExtractText(), out var post) &&
                !TryParseSharedDeathPost(message.Message.TextValue, out post))
            {
                return;
            }

            if (IsRecentOwnSharedDeathPost(post))
            {
                AddDebugLog("Ignored own shared Better Deaths recap chat post.");
                return;
            }

            if (FindSharedDeathPost(post) is { } death)
            {
                if (!HasDeathRecapDetails(death))
                {
                    AddDebugLog($"Shared Better Deaths recap for {death.MemberName} has no detail panel to link.");
                    return;
                }

                PrintDetectedSharedRecapLink(death);
                AddDebugLog($"Linked shared Better Deaths recap for {death.MemberName}.");
                return;
            }

            AddDebugLog($"Shared Better Deaths recap did not match a captured death for {post.MemberName}.");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process shared Better Deaths chat recap.");
        }
    }

    private void OnLogMessage(ILogMessage message)
    {
        try
        {
            CaptureCombatLogMessage(message);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process Better Deaths log message.");
        }
    }

    private void CaptureCombatLogMessage(ILogMessage message)
    {
        var now = DateTime.UtcNow;
        if (!ShouldCaptureLiveCombat(now) || currentMembers.Count == 0)
        {
            return;
        }

        var logMessageId = (uint)message.LogMessageId;
        if (!CombatDamageLogMessageIds.Contains(logMessageId))
        {
            return;
        }

        var source = message.SourceEntity;
        var target = message.TargetEntity;
        if (source is null || target is null || source.IsPlayer || !target.IsPlayer)
        {
            return;
        }

        var targetName = FormatLogEntityName(target);
        var member = FindCurrentMemberByName(targetName);
        if (member is null)
        {
            return;
        }

        if (!TryGetCombatLogAmount(message, out var amount) || amount == 0)
        {
            return;
        }

        var actionName = TryGetCombatLogActionName(message, out var parsedActionName) &&
            !string.IsNullOrWhiteSpace(parsedActionName)
            ? parsedActionName
            : "Attack";
        var record = new CombatLogEventRecord(
            now,
            CurrentPullElapsedSeconds,
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            FormatLogEntityName(source),
            targetName,
            logMessageId,
            actionName,
            amount);
        AddRecentCombatLogEvent(record);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            var now = DateTime.UtcNow;
            MaybeCheckForPluginUpdateNotice(now);
            FlushQueuedChatMessages(now);
            FlushPendingDeathRecapLinks(now);
            UpdateCombatTimerState(now);
            RefreshPartyState();
            PruneLiveCaptureState(now);
            PruneRecentOwnSharedDeathPosts(now);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not refresh Better Deaths state.");
        }
    }

    private void MaybeCheckForPluginUpdateNotice(DateTime now)
    {
        if (!string.IsNullOrEmpty(pendingUpdateNoticeKey))
        {
            var noticeKey = pendingUpdateNoticeKey;
            pendingUpdateNoticeKey = null;
            if (TryPrintUpdateNotice(noticeKey))
            {
                return;
            }
        }

        if (GetVersionMismatchNoticeKey() is { } mismatchNoticeKey)
        {
            SetPluginUpdateCheckState(PluginUpdateCheckState.VersionMismatch);
            if (TryPrintUpdateNotice(mismatchNoticeKey))
            {
                return;
            }
        }

        if (updateCheckInProgress)
        {
            return;
        }

        if (!PluginInterface.IsAutoUpdateComplete)
        {
            SetPluginUpdateCheckState(PluginUpdateCheckState.WaitingForDalamud);
            return;
        }

        if (nextPluginUpdateCheckAtUtc > now)
        {
            return;
        }

        updateCheckInProgress = true;
        SetPluginUpdateCheckState(PluginUpdateCheckState.Checking);
        _ = CheckForPluginUpdateNoticeAsync();
    }

    private async Task CheckForPluginUpdateNoticeAsync()
    {
        try
        {
            var update = await PluginInterface.CheckForUpdateAsync();
            if (update is not null)
            {
                availablePluginUpdateVersion = FormatVersionForDisplay(update.Version);
                availablePluginUpdateIsTesting = update.IsTesting;
                pendingUpdateNoticeKey = GetAvailableUpdateNoticeKey(update.Version, update.IsTesting);
                SetPluginUpdateCheckState(PluginUpdateCheckState.UpdateAvailable);
                return;
            }

            availablePluginUpdateVersion = null;
            availablePluginUpdateIsTesting = false;
            SetPluginUpdateCheckState(PluginUpdateCheckState.UpToDate);
        }
        catch (Exception ex)
        {
            pluginUpdateCheckError = ex.Message;
            SetPluginUpdateCheckState(PluginUpdateCheckState.Error);
            Log.Debug(ex, "Could not check Better Deaths update status.");
        }
        finally
        {
            lastPluginUpdateCheckAtUtc = DateTime.UtcNow;
            nextPluginUpdateCheckAtUtc = DateTime.UtcNow.Add(PluginUpdateCheckInterval);
            updateCheckInProgress = false;
        }
    }

    private static string? GetVersionMismatchNoticeKey()
    {
        var manifestVersion = PluginInterface.Manifest.AssemblyVersion;
        var assemblyVersion = typeof(Plugin).Assembly.GetName().Version;
        if (manifestVersion is null || assemblyVersion is null)
        {
            return null;
        }

        var manifestVersionText = FormatVersionForCompare(manifestVersion);
        var assemblyVersionText = FormatVersionForCompare(assemblyVersion);
        return manifestVersionText != assemblyVersionText
            ? $"mismatch:{manifestVersionText}:{assemblyVersionText}"
            : null;
    }

    private static string GetAvailableUpdateNoticeKey(Version version, bool isTesting)
    {
        return $"update:{FormatVersionForCompare(version)}:{(isTesting ? "testing" : "stable")}";
    }

    private static string FormatVersionForCompare(Version version)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}.{Math.Max(version.Revision, 0)}");
    }

    private static string FormatVersionForDisplay(Version? version)
    {
        return version is null ? "Unknown" : FormatVersionForCompare(version);
    }

    private void SetPluginUpdateCheckState(PluginUpdateCheckState state)
    {
        pluginUpdateCheckState = state;
        if (state != PluginUpdateCheckState.Error)
        {
            pluginUpdateCheckError = null;
        }
    }

    private bool TryPrintUpdateNotice(string noticeKey)
    {
        if (string.Equals(Configuration.LastAnnouncedUpdateNoticeKey, noticeKey, StringComparison.Ordinal))
        {
            return false;
        }

        Configuration.LastAnnouncedUpdateNoticeKey = noticeKey;
        SaveConfiguration();
        ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = CreateGreenText("[Better Deaths] update available. Open the Dalamud plugin installer to update."),
        });
        return true;
    }

    private static SeString CreateGreenText(string message)
    {
        return new SeStringBuilder()
            .AddUiForeground(message, ChatGreenColorKey)
            .Build();
    }

    private void RefreshPartyState()
    {
        var territoryId = ClientState.TerritoryType;
        if (territoryId != currentTerritoryId)
        {
            ArchiveCurrentPullForReview("Left territory", suppressResetStateDeaths: false);
            currentTerritoryId = territoryId;
            currentTerritoryName = GetTerritoryName(territoryId);
        }

        if (IsPvPCaptureBlocked())
        {
            ResetCurrentPull(suppressResetStateDeaths: false);
            currentMembers.Clear();
            return;
        }

        if (!IsDutyCaptureActive())
        {
            if (!currentPullClosedForReview &&
                (currentDeaths.Count > 0 || pullStartedAtUtc is not null || lastKnownPullElapsedSeconds > 0.0f))
            {
                ArchiveCurrentPullForReview("Left duty", suppressResetStateDeaths: false);
            }

            currentMembers.Clear();
            ClearPostResetDeathSuppression();
            return;
        }

        var nextMembers = BuildTrackedCharacterSnapshots();
        currentMembers.Clear();
        currentMembers.AddRange(nextMembers);

        currentMemberKeyScratch.Clear();
        foreach (var member in currentMembers)
        {
            currentMemberKeyScratch.Add(member.MemberKey);
        }

        deadMemberKeys.RemoveWhere(key => !currentMemberKeyScratch.Contains(key));
        postResetSuppressedDeadMemberKeys.RemoveWhere(key => !currentMemberKeyScratch.Contains(key));

        var now = DateTime.UtcNow;
        UpdatePostResetDeathSuppression();
        if (!ShouldCaptureLiveCombat(now))
        {
            return;
        }

        TrackRecentStatuses(currentMembers, now);
        TrackRecentHpHistory(currentMembers, now);

        foreach (var member in currentMembers)
        {
            if (!member.IsDead)
            {
                deadMemberKeys.Remove(member.MemberKey);
                postResetSuppressedDeadMemberKeys.Remove(member.MemberKey);
                continue;
            }

            if (postResetSuppressedDeadMemberKeys.Contains(member.MemberKey))
            {
                deadMemberKeys.Add(member.MemberKey);
                continue;
            }

            if (!deadMemberKeys.Add(member.MemberKey))
            {
                continue;
            }

            EnsurePullStarted(now);
            var death = CreateDeathRecord(member);
            currentDeaths.Add(death);
            QueueDeathRecapLink(death, now);
            AddDebugLog($"{member.MemberName} died to {FormatCause(DeathDisplaySelector.Select(death).Events)}.");
        }
    }

    private List<PartyMemberSnapshot> BuildTrackedCharacterSnapshots()
    {
        var members = new List<PartyMemberSnapshot>();
        var partyEntityIds = new HashSet<uint>();
        var partyIndex = 0;
        foreach (var member in PartyList)
        {
            var memberName = member.Name.TextValue;
            if (member.EntityId != 0)
            {
                partyEntityIds.Add(member.EntityId);
            }

            if (!Configuration.CapturePartyDeaths)
            {
                partyIndex++;
                continue;
            }

            var memberKey = member.ContentId != 0
                ? member.ContentId.ToString("X16")
                : member.EntityId != 0
                    ? $"entity:{member.EntityId:X8}"
                    : $"{memberName}:{partyIndex}";
            var classJobId = member.ClassJob.RowId;
            var isDead = member.GameObject?.IsDead == true ||
                (member.MaxHP > 0 && member.CurrentHP == 0);
            var shieldHp = CalculateShieldHp(member.GameObject, member.MaxHP);
            members.Add(new PartyMemberSnapshot(
                memberKey,
                memberName,
                partyIndex,
                classJobId,
                GetClassJobName(classJobId),
                member.ContentId,
                member.EntityId,
                member.CurrentHP,
                shieldHp,
                member.MaxHP,
                isDead,
                true,
                BuildStatusSnapshots(member.Statuses)));
            partyIndex++;
        }

        if (Configuration.CaptureOtherDeaths)
        {
            foreach (var gameObject in ObjectTable)
            {
                if (gameObject is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player ||
                    player.EntityId == 0 ||
                    partyEntityIds.Contains(player.EntityId) ||
                    player.MaxHp == 0)
                {
                    continue;
                }

                var memberName = player.Name.TextValue;
                if (string.IsNullOrWhiteSpace(memberName))
                {
                    continue;
                }

                var statusSnapshots = player is Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara
                    ? BuildStatusSnapshots(battleChara.StatusList)
                    : [];
                var classJobId = player.ClassJob.RowId;
                members.Add(new PartyMemberSnapshot(
                    $"entity:{player.EntityId:X8}",
                    memberName,
                    1000 + player.ObjectIndex,
                    classJobId,
                    GetClassJobName(classJobId),
                    0,
                    player.EntityId,
                    player.CurrentHp,
                    CalculateShieldHp(player, player.MaxHp),
                    player.MaxHp,
                    player.IsDead || player.CurrentHp == 0,
                    false,
                    statusSnapshots));
            }
        }

        return members
            .OrderBy(member => member.PartyIndex)
            .ThenBy(member => member.MemberName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static uint CalculateShieldHp(Dalamud.Game.ClientState.Objects.Types.IGameObject? gameObject, uint maxHp)
    {
        if (maxHp == 0 ||
            gameObject is not Dalamud.Game.ClientState.Objects.Types.ICharacter character)
        {
            return 0;
        }

        var shieldPercentage = Math.Clamp((double)character.ShieldPercentage, 0.0, 100.0);
        return (uint)Math.Round(maxHp * shieldPercentage / 100.0, MidpointRounding.AwayFromZero);
    }

    private unsafe void OnReceiveActionEffect(
        uint casterEntityId,
        Character* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        try
        {
            CaptureActionEffects(casterEntityId, casterPtr, header, effects, targetEntityIds);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process Better Deaths action effect.");
        }

        actionEffectHook?.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    private unsafe void CaptureActionEffects(
        uint casterEntityId,
        Character* casterPtr,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        var now = DateTime.UtcNow;
        if (!ShouldCaptureLiveCombat(now))
        {
            return;
        }

        if (header is null || effects is null || targetEntityIds is null || header->NumTargets == 0)
        {
            return;
        }

        var actionId = header->ActionId;
        var actionName = GetActionName(actionId);
        var sourceName = casterPtr is null ? $"Entity {casterEntityId:X8}" : casterPtr->NameString;
        var sourceStatuses = GetBossMitigationStatuses(BuildSourceStatusSnapshots(casterEntityId));
        var foundRelevantEffect = false;

        for (var targetIndex = 0; targetIndex < header->NumTargets; targetIndex++)
        {
            var member = FindCurrentMemberByTargetId(targetEntityIds[targetIndex]);
            if (member is null)
            {
                continue;
            }

            for (var effectIndex = 0; effectIndex < 8; effectIndex++)
            {
                ref var effect = ref effects[targetIndex].Effects[effectIndex];
                var kind = GetEventKind((ActionEffectKind)effect.Type);
                if (kind is null)
                {
                    continue;
                }

                foundRelevantEffect = true;
                EnsurePullStarted(now);

                var amount = (uint)effect.Value;
                if ((effect.Param4 & 0x40) == 0x40)
                {
                    amount += (uint)effect.Param3 << 16;
                }

                var liveMember = GetFreshMemberSnapshotForEvent(member);
                var record = new CombatEventRecord(
                    now,
                    CurrentPullElapsedSeconds,
                    liveMember.MemberKey,
                    liveMember.MemberName,
                    liveMember.PartyIndex,
                    casterEntityId,
                    sourceName,
                    actionId,
                    actionName,
                    GetActionIconId(actionId),
                    kind.Value,
                    amount,
                    liveMember.CurrentHp,
                    liveMember.ShieldHp,
                    liveMember.MaxHp,
                    (DamageType)(effect.Param1 & 0xF),
                    (effect.Param0 & 0x20) == 0x20,
                    (effect.Param0 & 0x40) == 0x40,
                    effect.Type == (byte)ActionEffectKind.BlockedDamage,
                    effect.Type == (byte)ActionEffectKind.ParriedDamage,
                    BuildEffectDetail((ActionEffectKind)effect.Type),
                    GetRelevantDeathStatuses(liveMember.Statuses),
                    sourceStatuses);
                AddRecentEvent(record);
            }
        }

        if (foundRelevantEffect)
        {
            AddDebugLog($"Captured {actionName} ({actionId}).");
        }
    }

    private PartyMemberSnapshot? FindCurrentMemberByTargetId(GameObjectId targetId)
    {
        return currentMembers.FirstOrDefault(member => TargetMatchesMember(targetId, member));
    }

    private PartyMemberSnapshot? FindCurrentMemberByName(string memberName)
    {
        return currentMembers.FirstOrDefault(member =>
            string.Equals(member.MemberName, memberName, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatLogEntityName(ILogMessageEntity entity)
    {
        return entity.Name.ToString();
    }

    private static bool TryGetCombatLogActionName(ILogMessage message, out string actionName)
    {
        if (message.ParameterCount > 0 && message.TryGetStringParameter(0, out var stringValue))
        {
            actionName = stringValue.ToString();
            return true;
        }

        actionName = string.Empty;
        return false;
    }

    private static bool TryGetCombatLogAmount(ILogMessage message, out uint amount)
    {
        amount = 0;
        if (message.ParameterCount <= 1 ||
            !message.TryGetIntParameter(1, out var intValue) ||
            intValue <= 0)
        {
            return false;
        }

        amount = (uint)intValue;
        return true;
    }

    private PartyMemberSnapshot GetFreshMemberSnapshotForEvent(PartyMemberSnapshot member)
    {
        if (member.EntityId == 0)
        {
            return currentMembers.FirstOrDefault(current => current.MemberKey == member.MemberKey) ?? member;
        }

        try
        {
            if (ObjectTable.SearchByEntityId(member.EntityId) is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
            {
                var statuses = player is Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara
                    ? BuildStatusSnapshots(battleChara.StatusList)
                    : member.Statuses;
                return member with
                {
                    CurrentHp = player.CurrentHp,
                    ShieldHp = CalculateShieldHp(player, player.MaxHp),
                    MaxHp = player.MaxHp,
                    IsDead = player.IsDead || player.CurrentHp == 0,
                    Statuses = statuses,
                };
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not refresh Better Deaths target snapshot for {EntityId:X8}.", member.EntityId);
        }

        return currentMembers.FirstOrDefault(current => current.MemberKey == member.MemberKey) ?? member;
    }

    private static bool TargetMatchesMember(GameObjectId targetId, PartyMemberSnapshot member)
    {
        if (targetId.ObjectId != 0 && member.EntityId == targetId.ObjectId)
        {
            return true;
        }

        if (targetId.Id <= uint.MaxValue)
        {
            var shortId = (uint)targetId.Id;
            return shortId != 0 && member.EntityId == shortId;
        }

        return false;
    }

    private PartyDeathRecord CreateDeathRecord(PartyMemberSnapshot member)
    {
        var now = DateTime.UtcNow;
        var events = GetRecentEvents(member.MemberKey, Math.Max(Configuration.RecentEventSeconds, BetterDeathsLeadUpCaptureSeconds));
        var hpHistory = GetRecentHpHistory(member.MemberKey, BetterDeathsLeadUpCaptureSeconds);
        var fatalSequence = CreateFatalSequence(member.MemberKey, now, events, hpHistory);
        var causeCutoff = now - TimeSpan.FromSeconds(Configuration.DeathCauseSeconds);
        var sequenceCause = fatalSequence is not null
            ? GetPrimaryFatalSequenceEvent(fatalSequence)
            : null;
        var fallbackEventCause = events
            .Where(combatEvent => combatEvent.SeenAtUtc >= causeCutoff)
            .Where(IsLikelyDeathCauseEvent)
            .OrderByDescending(combatEvent => combatEvent.SeenAtUtc)
            .ThenByDescending(combatEvent => combatEvent.Kind == DeathEventKind.Damage)
            .FirstOrDefault();
        var eventCause = sequenceCause ?? fallbackEventCause;
        var statusCause = CreateStatusDeathCause(member, now);
        var cause = ShouldPreferStatusCause(statusCause, eventCause)
            ? statusCause
            : eventCause ?? statusCause;

        return new PartyDeathRecord(
            now,
            CurrentPullElapsedSeconds,
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            member.ClassJobId,
            member.ClassJobName,
            member.CurrentHp,
            member.ShieldHp,
            member.MaxHp,
            cause,
            events,
            hpHistory,
            GetRelevantDeathStatuses(member.Statuses))
        {
            FatalSequence = fatalSequence,
        };
    }

    private FatalSequenceRecord? CreateFatalSequence(
        string memberKey,
        DateTime deathSeenAtUtc,
        IReadOnlyList<CombatEventRecord> events,
        IReadOnlyList<HpHistorySnapshot> hpHistory)
    {
        var lastAliveSnapshot = hpHistory
            .Where(snapshot => snapshot.SeenAtUtc <= deathSeenAtUtc)
            .Where(snapshot => snapshot.CurrentHp > 0 || snapshot.ShieldHp > 0)
            .OrderByDescending(snapshot => snapshot.SeenAtUtc)
            .FirstOrDefault();
        var startAtUtc = lastAliveSnapshot?.SeenAtUtc - FatalSequenceStartBuffer ??
            deathSeenAtUtc - TimeSpan.FromSeconds(Configuration.DeathCauseSeconds);
        var endAtUtc = deathSeenAtUtc + FatalSequenceEndBuffer;
        var sequenceEvents = events
            .Where(IsFatalSequenceEventCandidate)
            .Where(combatEvent => combatEvent.SeenAtUtc >= startAtUtc && combatEvent.SeenAtUtc <= endAtUtc)
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ToList();
        var sequenceLogEvents = GetRecentCombatLogEvents(memberKey, startAtUtc, endAtUtc);

        if (sequenceEvents.Count == 0 && sequenceLogEvents.Count == 0)
        {
            return null;
        }

        return new FatalSequenceRecord(startAtUtc, endAtUtc, lastAliveSnapshot, sequenceEvents, sequenceLogEvents);
    }

    private static bool IsFatalSequenceEventCandidate(CombatEventRecord combatEvent)
    {
        return combatEvent.Kind is DeathEventKind.Damage or DeathEventKind.Miss or DeathEventKind.Invulnerable;
    }

    private static bool IsLikelyDeathCauseEvent(CombatEventRecord combatEvent)
    {
        return DeathDisplaySelector.IsLikelyDeathCauseEvent(combatEvent);
    }

    private static CombatEventRecord? GetPrimaryFatalSequenceEvent(FatalSequenceRecord sequence)
    {
        return sequence.Events
            .Where(IsLikelyDeathCauseEvent)
            .OrderByDescending(combatEvent => combatEvent.SeenAtUtc)
            .ThenByDescending(combatEvent => combatEvent.Kind == DeathEventKind.Damage)
            .FirstOrDefault();
    }

    private static IReadOnlyList<CombatEventRecord> GetDisplayCauseEvents(PartyDeathRecord death)
    {
        return DeathDisplaySelector.Select(death).Events;
    }

    private static CombatEventRecord? GetPrimaryDisplayCauseEvent(IReadOnlyList<CombatEventRecord> causeEvents)
    {
        return causeEvents
            .OrderByDescending(combatEvent => combatEvent.SeenAtUtc)
            .ThenByDescending(combatEvent => combatEvent.Kind == DeathEventKind.Damage)
            .FirstOrDefault();
    }

    private static bool ShouldPreferStatusCause(CombatEventRecord? statusCause, CombatEventRecord? eventCause)
    {
        return statusCause is not null &&
            (eventCause is null || statusCause.SeenAtUtc >= eventCause.SeenAtUtc);
    }

    private CombatEventRecord? CreateStatusDeathCause(PartyMemberSnapshot member, DateTime now)
    {
        var cutoff = now - TimeSpan.FromSeconds(RecentStatusHistorySeconds);
        if (!recentStatusesByMember.TryGetValue(member.MemberKey, out var observations))
        {
            return null;
        }

        var observation = observations
            .Where(entry => entry.SeenAtUtc >= cutoff)
            .Where(entry => IsDoomStatus(entry.Status))
            .Where(entry => entry.Status.RemainingTime is > 0 and <= StatusDeathRemainingWindowSeconds)
            .OrderBy(entry => entry.Status.RemainingTime > 0 ? entry.Status.RemainingTime : float.MaxValue)
            .ThenByDescending(entry => entry.SeenAtUtc)
            .FirstOrDefault();
        if (observation is null)
        {
            return null;
        }

        var status = observation.Status;
        var sourceName = GetStatusSourceName(status.SourceId);
        return new CombatEventRecord(
            observation.SeenAtUtc,
            observation.PullElapsedSeconds,
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            status.SourceId,
            sourceName,
            status.Id,
            $"{status.Name} expired",
            status.IconId,
            DeathEventKind.Status,
            0,
            observation.CurrentHp,
            observation.ShieldHp,
            observation.MaxHp,
            DamageType.Unknown,
            false,
            false,
            false,
            false,
            "Doom-like status was seen shortly before KO.",
            GetRelevantDeathStatuses(observation.Statuses),
            GetBossMitigationStatuses(BuildSourceStatusSnapshots(status.SourceId)));
    }

    private void AddRecentEvent(CombatEventRecord record)
    {
        if (!recentEventsByMember.TryGetValue(record.MemberKey, out var events))
        {
            events = [];
            recentEventsByMember[record.MemberKey] = events;
        }

        events.Add(record);
    }

    private void AddRecentCombatLogEvent(CombatLogEventRecord record)
    {
        if (!recentCombatLogEventsByMember.TryGetValue(record.MemberKey, out var events))
        {
            events = [];
            recentCombatLogEventsByMember[record.MemberKey] = events;
        }

        events.Add(record);
        while (events.Count > MaxCombatLogEventsPerMember)
        {
            events.RemoveAt(0);
        }
    }

    private void TrackRecentStatuses(IEnumerable<PartyMemberSnapshot> members, DateTime now)
    {
        foreach (var member in members)
        {
            List<StatusObservation>? observations = null;
            foreach (var status in member.Statuses)
            {
                if (!IsTrackedStatusDeathCandidate(status))
                {
                    continue;
                }

                if (observations is null &&
                    !recentStatusesByMember.TryGetValue(member.MemberKey, out observations))
                {
                    observations = [];
                    recentStatusesByMember[member.MemberKey] = observations;
                }

                observations.RemoveAll(entry => entry.Status.Id == status.Id);
                observations.Add(new StatusObservation(
                    now,
                    CurrentPullElapsedSeconds,
                    status,
                    member.CurrentHp,
                    member.ShieldHp,
                    member.MaxHp,
                    member.Statuses.ToList()));
            }
        }
    }

    private void TrackRecentHpHistory(IEnumerable<PartyMemberSnapshot> members, DateTime now)
    {
        foreach (var member in members)
        {
            if (member.IsDead || member.MaxHp == 0)
            {
                continue;
            }

            if (lastHpHistorySampleByMember.TryGetValue(member.MemberKey, out var lastSampleAt) &&
                now - lastSampleAt < HpHistorySampleInterval)
            {
                continue;
            }

            if (!recentHpHistoryByMember.TryGetValue(member.MemberKey, out var history))
            {
                history = [];
                recentHpHistoryByMember[member.MemberKey] = history;
            }

            history.Add(new HpHistorySnapshot(
                now,
                CurrentPullElapsedSeconds,
                member.CurrentHp,
                member.ShieldHp,
                member.MaxHp,
                GetRelevantDeathStatuses(member.Statuses)));
            lastHpHistorySampleByMember[member.MemberKey] = now;
        }
    }

    private IReadOnlyList<HpHistorySnapshot> GetRecentHpHistory(string memberKey, int seconds)
    {
        if (!recentHpHistoryByMember.TryGetValue(memberKey, out var history))
        {
            return [];
        }

        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(seconds);
        return history
            .Where(snapshot => snapshot.SeenAtUtc >= cutoff)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ToList();
    }

    private IReadOnlyList<CombatEventRecord> GetRecentEvents(string memberKey, int seconds)
    {
        if (!recentEventsByMember.TryGetValue(memberKey, out var events))
        {
            return [];
        }

        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(seconds);
        return events
            .Where(combatEvent => combatEvent.SeenAtUtc >= cutoff)
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ToList();
    }

    private IReadOnlyList<CombatLogEventRecord> GetRecentCombatLogEvents(string memberKey, DateTime startAtUtc, DateTime endAtUtc)
    {
        if (!recentCombatLogEventsByMember.TryGetValue(memberKey, out var events))
        {
            return [];
        }

        return events
            .Where(combatEvent => combatEvent.SeenAtUtc >= startAtUtc && combatEvent.SeenAtUtc <= endAtUtc)
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ToList();
    }

    private void PruneLiveCaptureState(DateTime now)
    {
        if (nextLiveCapturePruneAtUtc > now)
        {
            return;
        }

        nextLiveCapturePruneAtUtc = now + LiveCapturePruneInterval;
        PruneRecentEvents(now);
        PruneRecentCombatLogEvents(now);
        PruneRecentStatuses(now);
        PruneRecentHpHistory(now);
    }

    private void PruneRecentEvents(DateTime now)
    {
        if (recentEventsByMember.Count == 0)
        {
            return;
        }

        var cutoff = now - TimeSpan.FromSeconds(Math.Max(Configuration.RecentEventSeconds, Configuration.DeathCauseSeconds) + 10);
        foreach (var key in recentEventsByMember.Keys.ToList())
        {
            recentEventsByMember[key].RemoveAll(combatEvent => combatEvent.SeenAtUtc < cutoff);
            if (recentEventsByMember[key].Count == 0)
            {
                recentEventsByMember.Remove(key);
            }
        }
    }

    private void PruneRecentHpHistory(DateTime now)
    {
        if (recentHpHistoryByMember.Count == 0)
        {
            return;
        }

        var cutoff = now - TimeSpan.FromSeconds(HpHistoryRetentionSeconds);
        foreach (var key in recentHpHistoryByMember.Keys.ToList())
        {
            recentHpHistoryByMember[key].RemoveAll(snapshot => snapshot.SeenAtUtc < cutoff);
            if (recentHpHistoryByMember[key].Count == 0)
            {
                recentHpHistoryByMember.Remove(key);
                lastHpHistorySampleByMember.Remove(key);
            }
        }
    }

    private void PruneRecentStatuses(DateTime now)
    {
        if (recentStatusesByMember.Count == 0)
        {
            return;
        }

        var cutoff = now - TimeSpan.FromSeconds(RecentStatusHistorySeconds);
        foreach (var key in recentStatusesByMember.Keys.ToList())
        {
            recentStatusesByMember[key].RemoveAll(entry => entry.SeenAtUtc < cutoff);
            if (recentStatusesByMember[key].Count == 0)
            {
                recentStatusesByMember.Remove(key);
            }
        }
    }

    private void OnDutyReset(IDutyStateEventArgs args)
    {
        if (IsPvPCaptureBlocked())
        {
            ResetCurrentPull(suppressResetStateDeaths: false);
            currentMembers.Clear();
            currentTerritoryId = ClientState.TerritoryType;
            currentTerritoryName = GetTerritoryName(currentTerritoryId);
            return;
        }

        ArchiveCurrentPullForReview("Duty reset");
        currentTerritoryId = ClientState.TerritoryType;
        currentTerritoryName = GetTerritoryName(currentTerritoryId);
    }

    private void ArchiveCurrentPullForReview(string reason, bool suppressResetStateDeaths = true)
    {
        if (currentDeaths.Count > 0)
        {
            CaptureCurrentPullSnapshot(reason);
            PrepareCurrentPullForReview(suppressResetStateDeaths);
            return;
        }

        if (pullStartedAtUtc is not null || lastKnownPullElapsedSeconds > 0.0f)
        {
            ResetCurrentPull(suppressResetStateDeaths);
        }
    }

    private bool CaptureCurrentPullSnapshot(string reason)
    {
        if (currentDeaths.Count == 0 || currentPullSnapshotCaptured)
        {
            return false;
        }

        var pullNumber = GetNextRecordedPullNumber();
        recordedPulls.Add(new PullDeathSnapshot(
            DateTime.UtcNow,
            reason,
            currentPullTerritoryId == 0 ? currentTerritoryId : currentPullTerritoryId,
            currentPullTerritoryId == 0 ? currentTerritoryName : currentPullTerritoryName,
            CurrentPullElapsedSeconds,
            currentDeaths.ToList())
        {
            PullNumber = pullNumber,
        });
        TrimRecordedPulls();
        SaveRecordedPullHistory();
        currentPullSnapshotCaptured = true;
        currentPullRecordedPullNumber = pullNumber;
        return true;
    }

    private void ResetCurrentPull(bool suppressResetStateDeaths = true)
    {
        currentDeaths.Clear();
        ClearLivePullCaptureState();
        if (suppressResetStateDeaths)
        {
            StartPostResetDeathSuppression();
        }
        else
        {
            ClearPostResetDeathSuppression();
        }

        pullStartedAtUtc = null;
        lastInCombatAtUtc = null;
        lastKnownPullElapsedSeconds = 0.0f;
        combatTimerRunning = false;
        currentPullClosedForReview = false;
        currentPullSnapshotCaptured = false;
        currentPullRecordedPullNumber = 0;
        currentPullTerritoryId = 0;
        currentPullTerritoryName = "Unknown territory";
    }

    private void PrepareCurrentPullForReview(bool suppressResetStateDeaths)
    {
        ClearLivePullCaptureState();
        if (suppressResetStateDeaths)
        {
            StartPostResetDeathSuppression();
        }
        else
        {
            ClearPostResetDeathSuppression();
        }

        pullStartedAtUtc = null;
        lastInCombatAtUtc = null;
        combatTimerRunning = false;
        currentPullClosedForReview = true;
    }

    private void ClearLivePullCaptureState()
    {
        recentEventsByMember.Clear();
        recentCombatLogEventsByMember.Clear();
        recentStatusesByMember.Clear();
        recentHpHistoryByMember.Clear();
        lastHpHistorySampleByMember.Clear();
        currentMemberKeyScratch.Clear();
        deadMemberKeys.Clear();
    }

    private static bool IsPvPCaptureBlocked()
    {
        return ClientState.IsPvP;
    }

    private static bool IsDutyCaptureActive()
    {
        return DutyState.IsDutyStarted;
    }

    private bool ShouldCaptureLiveCombat(DateTime now)
    {
        if (IsPvPCaptureBlocked() || !IsDutyCaptureActive())
        {
            return false;
        }

        if (Condition[ConditionFlag.InCombat])
        {
            return true;
        }

        return lastInCombatAtUtc is { } lastInCombat &&
            now - lastInCombat <= PostCombatCaptureGrace;
    }

    private void StartPostResetDeathSuppression()
    {
        collectingPostResetDeadMembers = true;
        postResetSuppressedDeadMemberKeys.Clear();
        foreach (var member in currentMembers)
        {
            if (!member.IsDead)
            {
                continue;
            }

            postResetSuppressedDeadMemberKeys.Add(member.MemberKey);
            deadMemberKeys.Add(member.MemberKey);
        }
    }

    private void ClearPostResetDeathSuppression()
    {
        collectingPostResetDeadMembers = false;
        postResetSuppressedDeadMemberKeys.Clear();
    }

    private void UpdatePostResetDeathSuppression()
    {
        if (!IsDutyCaptureActive())
        {
            return;
        }

        var hasAliveMember = false;
        foreach (var member in currentMembers)
        {
            if (!member.IsDead)
            {
                hasAliveMember = true;
                postResetSuppressedDeadMemberKeys.Remove(member.MemberKey);
            }
        }

        if (!collectingPostResetDeadMembers)
        {
            return;
        }

        if (!Condition[ConditionFlag.InCombat])
        {
            foreach (var member in currentMembers)
            {
                if (!member.IsDead)
                {
                    continue;
                }

                if (postResetSuppressedDeadMemberKeys.Add(member.MemberKey))
                {
                    AddDebugLog($"Suppressed reset-state KO for {member.MemberName}.");
                }

                deadMemberKeys.Add(member.MemberKey);
            }
        }

        if (Condition[ConditionFlag.InCombat] || hasAliveMember)
        {
            collectingPostResetDeadMembers = false;
        }
    }

    private void EnsurePullStarted(DateTime now)
    {
        if (!IsDutyCaptureActive())
        {
            return;
        }

        if (currentPullClosedForReview)
        {
            ResetCurrentPull(suppressResetStateDeaths: false);
        }

        if (pullStartedAtUtc is null)
        {
            pullStartedAtUtc = now;
            currentPullTerritoryId = currentTerritoryId;
            currentPullTerritoryName = currentTerritoryName;
        }

        combatTimerRunning = combatTimerRunning || Condition[ConditionFlag.InCombat];
        lastKnownPullElapsedSeconds = CalculatePullElapsed(now);
    }

    private void UpdateCombatTimerState(DateTime now)
    {
        if (!IsDutyCaptureActive())
        {
            return;
        }

        var inCombat = Condition[ConditionFlag.InCombat];
        if (inCombat)
        {
            if (currentPullClosedForReview)
            {
                ResetCurrentPull(suppressResetStateDeaths: false);
            }

            lastInCombatAtUtc = now;
            if (pullStartedAtUtc is null)
            {
                pullStartedAtUtc = now;
                lastKnownPullElapsedSeconds = 0.0f;
                currentPullTerritoryId = currentTerritoryId;
                currentPullTerritoryName = currentTerritoryName;
            }

            combatTimerRunning = true;
            lastKnownPullElapsedSeconds = CalculatePullElapsed(now);
            return;
        }

        if (combatTimerRunning)
        {
            lastKnownPullElapsedSeconds = CalculatePullElapsed(now);
            combatTimerRunning = false;
        }
    }

    private float CalculatePullElapsed(DateTime now)
    {
        return pullStartedAtUtc is null
            ? lastKnownPullElapsedSeconds
            : (float)Math.Max(0.0, (now - pullStartedAtUtc.Value).TotalSeconds);
    }

    private void TrimRecordedPulls()
    {
        while (recordedPulls.Count > Configuration.MaxRecordedPulls)
        {
            recordedPulls.RemoveAt(0);
        }
    }

    private void PruneRecentCombatLogEvents(DateTime now)
    {
        if (recentCombatLogEventsByMember.Count == 0)
        {
            return;
        }

        var cutoff = now - TimeSpan.FromSeconds(CombatLogEventRetentionSeconds);
        foreach (var key in recentCombatLogEventsByMember.Keys.ToList())
        {
            recentCombatLogEventsByMember[key].RemoveAll(combatEvent => combatEvent.SeenAtUtc < cutoff);
            if (recentCombatLogEventsByMember[key].Count == 0)
            {
                recentCombatLogEventsByMember.Remove(key);
            }
        }
    }

    private long GetNextRecordedPullNumber()
    {
        var next = Math.Max(1, nextRecordedPullNumber);
        if (recordedPulls.Count > 0)
        {
            next = Math.Max(next, recordedPulls.Max(pull => pull.PullNumber) + 1);
        }

        nextRecordedPullNumber = next + 1;
        return next;
    }

    private static string RecordedPullHistoryPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, RecordedPullHistoryFileName);

    private static string RecordedPullHistoryTempPath =>
        RecordedPullHistoryPath + ".tmp";

    private static string RecordedPullHistoryBackupPath =>
        RecordedPullHistoryPath + ".bak";

    private void LoadRecordedPullHistory()
    {
        var loadedPulls = TryReadRecordedPullHistoryFile(RecordedPullHistoryPath);

        if (loadedPulls is null && File.Exists(RecordedPullHistoryTempPath))
        {
            loadedPulls = TryReadRecordedPullHistoryFile(RecordedPullHistoryTempPath);
        }

        if (loadedPulls is null && File.Exists(RecordedPullHistoryBackupPath))
        {
            loadedPulls = TryReadRecordedPullHistoryFile(RecordedPullHistoryBackupPath);
        }

        if (loadedPulls is null)
        {
            foreach (var backupPath in GetRecordedPullHistoryRollingBackupPaths())
            {
                loadedPulls = TryReadRecordedPullHistoryFile(backupPath);
                if (loadedPulls is not null)
                {
                    break;
                }
            }
        }

        if (loadedPulls is null)
        {
            return;
        }

        recordedPulls.Clear();
        recordedPulls.AddRange(NormalizeRecordedPullNumbers(loadedPulls
            .Where(pull => pull is { Deaths.Count: > 0 })
            .OrderBy(pull => pull.CapturedAtUtc)));
        TrimRecordedPulls();
        nextRecordedPullNumber = recordedPulls.Count == 0
            ? 1
            : recordedPulls.Max(pull => pull.PullNumber) + 1;
    }

    private static List<PullDeathSnapshot> NormalizeRecordedPullNumbers(IEnumerable<PullDeathSnapshot> pulls)
    {
        var normalized = new List<PullDeathSnapshot>();
        var usedPullNumbers = new HashSet<long>();
        var nextPullNumber = 1L;

        foreach (var pull in pulls)
        {
            var pullNumber = pull.PullNumber;
            if (pullNumber <= 0 || !usedPullNumbers.Add(pullNumber))
            {
                while (usedPullNumbers.Contains(nextPullNumber))
                {
                    nextPullNumber++;
                }

                pullNumber = nextPullNumber;
                usedPullNumbers.Add(pullNumber);
            }

            nextPullNumber = Math.Max(nextPullNumber, pullNumber + 1);
            normalized.Add(pull with { PullNumber = pullNumber });
        }

        return normalized;
    }

    private List<PullDeathSnapshot>? TryReadRecordedPullHistoryFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : DeserializeRecordedPullHistory(json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Could not read Better Deaths recorded pull history at {path}.");
            return null;
        }
    }

    private static List<PullDeathSnapshot>? DeserializeRecordedPullHistory(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => JsonSerializer.Deserialize<List<PullDeathSnapshot>>(json, RecordedPullHistoryJsonOptions),
            JsonValueKind.Object when document.RootElement.TryGetProperty(nameof(RecordedPullHistoryFile.Pulls), out _) =>
                JsonSerializer.Deserialize<RecordedPullHistoryFile>(json, RecordedPullHistoryJsonOptions)?.Pulls,
            _ => null,
        };
    }

    private void SaveRecordedPullHistory()
    {
        try
        {
            Directory.CreateDirectory(PluginInterface.ConfigDirectory.FullName);
            var json = JsonSerializer.Serialize(
                new RecordedPullHistoryFile(RecordedPullHistorySchemaVersion, recordedPulls),
                RecordedPullHistoryJsonOptions);
            if (!RecordedPullHistoryNeedsWrite(json))
            {
                return;
            }

            CreateRecordedPullHistoryRollingBackup();
            File.WriteAllText(RecordedPullHistoryTempPath, json);

            if (File.Exists(RecordedPullHistoryPath))
            {
                File.Replace(RecordedPullHistoryTempPath, RecordedPullHistoryPath, RecordedPullHistoryBackupPath, true);
            }
            else
            {
                File.Move(RecordedPullHistoryTempPath, RecordedPullHistoryPath, true);
            }

            PruneRecordedPullHistoryRollingBackups();
            lastSavedRecordedPullHistoryJson = json;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not save Better Deaths recorded pull history.");
        }
    }

    private bool RecordedPullHistoryNeedsWrite(string json)
    {
        try
        {
            if (File.Exists(RecordedPullHistoryPath) &&
                string.Equals(lastSavedRecordedPullHistoryJson, json, StringComparison.Ordinal))
            {
                return false;
            }

            if (!File.Exists(RecordedPullHistoryPath))
            {
                return true;
            }

            var existingJson = File.ReadAllText(RecordedPullHistoryPath);
            if (!string.Equals(existingJson, json, StringComparison.Ordinal))
            {
                return true;
            }

            lastSavedRecordedPullHistoryJson = json;
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not compare Better Deaths recorded pull history before saving.");
            return true;
        }
    }

    private static void DeleteRecordedPullHistoryFiles()
    {
        try
        {
            foreach (var path in GetRecordedPullHistoryFilesForDelete())
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, $"Could not delete Better Deaths recorded pull history file at {path}.");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not delete Better Deaths recorded pull history files.");
        }
    }

    private static IEnumerable<string> GetRecordedPullHistoryFilesForDelete()
    {
        yield return RecordedPullHistoryPath;
        yield return RecordedPullHistoryTempPath;
        yield return RecordedPullHistoryBackupPath;

        foreach (var backupPath in GetRecordedPullHistoryRollingBackupPaths())
        {
            yield return backupPath;
        }
    }

    private void CreateRecordedPullHistoryRollingBackup()
    {
        try
        {
            if (!File.Exists(RecordedPullHistoryPath) ||
                TryReadRecordedPullHistoryFile(RecordedPullHistoryPath) is null)
            {
                return;
            }

            var backupPath = Path.Combine(
                PluginInterface.ConfigDirectory.FullName,
                $"recorded-pulls.backup.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.json");
            File.Copy(RecordedPullHistoryPath, backupPath, false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not create Better Deaths recorded pull history rolling backup.");
        }
    }

    private static IEnumerable<string> GetRecordedPullHistoryRollingBackupPaths()
    {
        try
        {
            if (!Directory.Exists(PluginInterface.ConfigDirectory.FullName))
            {
                return [];
            }

            return Directory.EnumerateFiles(
                    PluginInterface.ConfigDirectory.FullName,
                    RecordedPullHistoryRollingBackupSearchPattern,
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not list Better Deaths recorded pull history rolling backups.");
            return [];
        }
    }

    private static void PruneRecordedPullHistoryRollingBackups()
    {
        try
        {
            foreach (var backupPath in GetRecordedPullHistoryRollingBackupPaths().Skip(RecordedPullHistoryRollingBackupCount))
            {
                File.Delete(backupPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not prune Better Deaths recorded pull history rolling backups.");
        }
    }

    private static DeathEventKind? GetEventKind(ActionEffectKind effectKind)
    {
        return effectKind switch
        {
            ActionEffectKind.Miss => DeathEventKind.Miss,
            ActionEffectKind.Damage or ActionEffectKind.BlockedDamage or ActionEffectKind.ParriedDamage => DeathEventKind.Damage,
            ActionEffectKind.Invulnerable or ActionEffectKind.PartialInvulnerable => DeathEventKind.Invulnerable,
            _ => null,
        };
    }

    private static string BuildEffectDetail(ActionEffectKind effectKind)
    {
        return effectKind switch
        {
            ActionEffectKind.Miss => "Missed",
            ActionEffectKind.BlockedDamage => "Blocked",
            ActionEffectKind.ParriedDamage => "Parried",
            ActionEffectKind.Invulnerable => "Invulnerable",
            ActionEffectKind.PartialInvulnerable => "Partially invulnerable",
            _ => string.Empty,
        };
    }

    private IReadOnlyList<StatusSnapshot> BuildStatusSnapshots(IEnumerable<IStatus> statuses)
    {
        var snapshots = new List<StatusSnapshot>();
        foreach (var status in statuses)
        {
            var statusId = status.StatusId;
            if (statusId == 0)
            {
                continue;
            }

            snapshots.Add(new StatusSnapshot(
                statusId,
                GetStatusName(statusId),
                GetStatusIconId(statusId),
                status.SourceId,
                status.Param,
                status.RemainingTime));
        }

        return snapshots
            .OrderBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.Id)
            .ToList();
    }

    private IReadOnlyList<StatusSnapshot> BuildSourceStatusSnapshots(uint sourceEntityId)
    {
        if (sourceEntityId == 0)
        {
            return [];
        }

        try
        {
            var sourceObject = ObjectTable.SearchByEntityId(sourceEntityId);
            if (sourceObject is Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara)
            {
                return BuildStatusSnapshots(battleChara.StatusList);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not snapshot source statuses for {SourceEntityId:X8}.", sourceEntityId);
        }

        return [];
    }

    private static IReadOnlyList<StatusSnapshot> GetRelevantDeathStatuses(IEnumerable<StatusSnapshot> statuses)
    {
        return PipeDeduplicateStatusSnapshots(statuses
                .Where(IsRelevantDeathStatus))
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();
    }

    internal IReadOnlyList<StatusSnapshot> GetRelevantPlayerStatusesForDisplay(IEnumerable<StatusSnapshot> statuses)
    {
        return GetRelevantDeathStatuses(statuses);
    }

    internal static IReadOnlyList<StatusSnapshot> GetBossMitigationStatusesForDisplay(IEnumerable<StatusSnapshot> statuses)
    {
        return GetBossMitigationStatuses(statuses);
    }

    internal static bool ShouldShowPlayerStatusTimerForDisplay(StatusSnapshot status)
    {
        return ContainsAny(status.Name, EncounterDebuffNameFragments);
    }

    private static IReadOnlyList<StatusSnapshot> GetBossMitigationStatuses(IEnumerable<StatusSnapshot> statuses)
    {
        return PipeDeduplicateStatusSnapshots(statuses
                .Where(IsBossMitigationStatus))
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();
    }

    private static IEnumerable<StatusSnapshot> PipeDeduplicateStatusSnapshots(IEnumerable<StatusSnapshot> statuses)
    {
        return statuses
            .GroupBy(status => (status.Id, status.IconId, status.SourceId))
            .Select(group => group
                .OrderBy(status => status.RemainingTime <= 0.0f ? float.MaxValue : status.RemainingTime)
                .ThenBy(status => status.StackCount)
                .First());
    }

    private static bool IsRelevantDeathStatus(StatusSnapshot status)
    {
        return IsDefensiveStatus(status) ||
            ContainsAny(status.Name, EncounterDebuffNameFragments);
    }

    private static bool IsTrackedStatusDeathCandidate(StatusSnapshot status)
    {
        return IsDoomStatus(status);
    }

    private static bool IsDoomStatus(StatusSnapshot status)
    {
        return ContainsAny(status.Name, DoomStatusNameFragments);
    }

    private static bool IsDefensiveStatus(StatusSnapshot status)
    {
        return ContainsExact(status.Name, DefensiveStatusNames);
    }

    private static bool IsPlayerDebuffStatus(StatusSnapshot status)
    {
        return !IsDefensiveStatus(status) &&
            ContainsAny(status.Name, EncounterDebuffNameFragments);
    }

    private static bool IsBossMitigationStatus(StatusSnapshot status)
    {
        return BossMitigationStatusIds.Contains(status.Id);
    }

    private static bool ContainsAny(string value, IEnumerable<string> fragments)
    {
        return fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsExact(string value, IEnumerable<string> names)
    {
        return names.Any(name => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    }

    private string GetActionName(uint actionId)
    {
        if (actionNameCache.TryGetValue(actionId, out var cachedName))
        {
            return cachedName;
        }

        var name = actionId == 0 ? "Unknown action" : $"Action {actionId}";
        try
        {
            var action = DataManager.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(actionId);
            var sheetName = action?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                name = sheetName;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load action name for {ActionId}.", actionId);
        }

        actionNameCache[actionId] = name;
        return name;
    }

    private string GetStatusSourceName(uint sourceId)
    {
        if (sourceId == 0)
        {
            return "Unknown source";
        }

        try
        {
            var sourceObject = ObjectTable.SearchByEntityId(sourceId);
            var name = sourceObject?.Name.TextValue;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not resolve status source for {SourceId:X8}.", sourceId);
        }

        return $"Entity {sourceId:X8}";
    }

    private uint GetActionIconId(uint actionId)
    {
        if (actionIconCache.TryGetValue(actionId, out var cachedIconId))
        {
            return cachedIconId;
        }

        var iconId = 0u;
        try
        {
            var action = DataManager.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(actionId);
            iconId = action?.Icon ?? 0u;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load action icon for {ActionId}.", actionId);
        }

        actionIconCache[actionId] = iconId;
        return iconId;
    }

    private string GetStatusName(uint statusId)
    {
        if (statusNameCache.TryGetValue(statusId, out var cachedName))
        {
            return cachedName;
        }

        var name = $"Status {statusId}";
        try
        {
            var status = DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId);
            var sheetName = status?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                name = sheetName;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load status name for {StatusId}.", statusId);
        }

        statusNameCache[statusId] = name;
        return name;
    }

    private uint GetStatusIconId(uint statusId)
    {
        if (statusIconCache.TryGetValue(statusId, out var cachedIconId))
        {
            return cachedIconId;
        }

        var iconId = 0u;
        try
        {
            var status = DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId);
            iconId = status?.Icon ?? 0u;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load status icon for {StatusId}.", statusId);
        }

        statusIconCache[statusId] = iconId;
        return iconId;
    }

    private string GetClassJobName(uint classJobId)
    {
        if (classJobNameCache.TryGetValue(classJobId, out var cachedName))
        {
            return cachedName;
        }

        var name = classJobId == 0 ? "Unknown job" : $"Job {classJobId}";
        try
        {
            var classJob = DataManager.GetExcelSheet<ClassJob>()?.GetRowOrDefault(classJobId);
            var abbreviation = classJob?.Abbreviation.ExtractText();
            if (!string.IsNullOrWhiteSpace(abbreviation))
            {
                name = abbreviation;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load class job name for {ClassJobId}.", classJobId);
        }

        classJobNameCache[classJobId] = name;
        return name;
    }

    private string GetTerritoryName(uint territoryId)
    {
        if (territoryNameCache.TryGetValue(territoryId, out var cachedName))
        {
            return cachedName;
        }

        var name = territoryId == 0 ? "Unknown territory" : $"Territory {territoryId}";
        try
        {
            var territory = DataManager.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
            var sheetName = territory?.PlaceName.ValueNullable?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                name = sheetName;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load territory name for {TerritoryId}.", territoryId);
        }

        territoryNameCache[territoryId] = name;
        return name;
    }

    private void AddDebugLog(string message)
    {
        if (!Configuration.DebugLogEnabled)
        {
            return;
        }

        debugLogEntries.Add(new DebugLogEntry(DateTime.UtcNow, CurrentPullElapsedSeconds, message));
        while (debugLogEntries.Count > MaxDebugLogEntries)
        {
            debugLogEntries.RemoveAt(0);
        }
    }

    private static string FormatCause(CombatEventRecord? cause)
    {
        return cause is null
            ? "Unknown"
            : cause.Kind == DeathEventKind.Status
                ? cause.ActionName
                : $"{cause.ActionName} ({cause.Amount:N0})";
    }

    private static string FormatCause(IReadOnlyList<CombatEventRecord> causes)
    {
        if (causes.Count == 0)
        {
            return "Unknown";
        }

        if (causes.Count == 1)
        {
            return FormatCause(causes[0]);
        }

        var totalDamage = causes
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .Aggregate(0UL, (sum, cause) => sum + cause.Amount);
        return totalDamage > 0
            ? $"{causes.Count} causes ({totalDamage:N0} total)"
            : $"{causes.Count} causes";
    }

    private static string FormatEffectiveHp(uint currentHp, uint shieldHp, uint maxHp)
    {
        var effectiveHp = (ulong)currentHp + shieldHp;
        return maxHp == 0
            ? $"{effectiveHp:N0}"
            : $"{effectiveHp:N0} / {maxHp:N0} ({(double)effectiveHp / maxHp:P0})";
    }

    private static string FormatDeathChatCauseLine(CombatEventRecord cause, HpHistorySnapshot? snapshot)
    {
        var currentHp = snapshot?.CurrentHp ?? cause.CurrentHp;
        var shieldHp = snapshot?.ShieldHp ?? cause.ShieldHp;
        var maxHp = snapshot?.MaxHp ?? cause.MaxHp;
        return cause.Kind == DeathEventKind.Status
            ? $"{cause.ActionName} from {cause.SourceName}. HP before KO: {FormatEffectiveHp(currentHp, shieldHp, maxHp)}."
            : $"{cause.Amount:N0} from {cause.ActionName} by {cause.SourceName}. HP before hit: {FormatEffectiveHp(currentHp, shieldHp, maxHp)}.";
    }

    private static string FormatDeathChatMultiHitLine(IReadOnlyList<CombatEventRecord> damageEvents, HpHistorySnapshot? snapshot)
    {
        var currentHp = snapshot?.CurrentHp ?? damageEvents[0].CurrentHp;
        var shieldHp = snapshot?.ShieldHp ?? damageEvents[0].ShieldHp;
        var maxHp = snapshot?.MaxHp ?? damageEvents[0].MaxHp;
        var totalDamage = damageEvents.Aggregate(0UL, (sum, cause) => sum + cause.Amount);
        var sourceName = GetSharedDamageSourceName(damageEvents);
        return $"{totalDamage:N0} damage by {damageEvents.Count} hits from {sourceName}. HP before hit: {FormatEffectiveHp(currentHp, shieldHp, maxHp)}.";
    }

    private static string GetSharedDamageSourceName(IReadOnlyList<CombatEventRecord> damageEvents)
    {
        var sourceNames = damageEvents
            .Select(cause => cause.SourceName)
            .Where(sourceName => !string.IsNullOrWhiteSpace(sourceName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return sourceNames.Count == 1 ? sourceNames[0] : "multiple targets";
    }

    private static string FormatHp(uint currentHp, uint shieldHp, uint maxHp)
    {
        var effectiveHp = (ulong)currentHp + shieldHp;
        return maxHp == 0
            ? $"{currentHp:N0} + {shieldHp:N0} shield"
            : $"{currentHp:N0} + {shieldHp:N0} shield / {maxHp:N0} ({(double)effectiveHp / maxHp:P0})";
    }

    private static string FormatStatusList(IReadOnlyList<StatusSnapshot> statuses)
    {
        return statuses.Count == 0
            ? "none captured"
            : string.Join(", ", statuses.Select(FormatStatus));
    }

    private static string FormatStatus(StatusSnapshot status)
    {
        var stacks = status.StackCount == 0 ? string.Empty : $" x{status.StackCount}";
        return $"{status.Name}{stacks}";
    }

    private static string FormatCombatTimer(float elapsedSeconds)
    {
        var totalSeconds = (int)MathF.Max(0.0f, elapsedSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private void RememberOwnSharedDeathPost(PartyDeathRecord death)
    {
        var post = BuildSharedDeathPost(death);
        var now = DateTime.UtcNow;
        PruneRecentOwnSharedDeathPosts(now);
        recentOwnSharedDeathPosts.Add(new RecentOwnSharedDeathPost(post, now.AddSeconds(OwnSharedRecapSuppressionSeconds)));
    }

    private bool IsRecentOwnSharedDeathPost(SharedDeathPost post)
    {
        var now = DateTime.UtcNow;
        PruneRecentOwnSharedDeathPosts(now);
        return recentOwnSharedDeathPosts.Any(entry => PostsMatch(entry.Post, post));
    }

    private void PruneRecentOwnSharedDeathPosts(DateTime now)
    {
        recentOwnSharedDeathPosts.RemoveAll(entry => entry.ExpiresAtUtc <= now);
    }

    private PartyDeathRecord? FindSharedDeathPost(SharedDeathPost post)
    {
        var candidates = currentDeaths
            .Concat(recordedPulls.AsEnumerable().Reverse().SelectMany(pull => pull.Deaths))
            .Where(death => IsSharedDeathCandidate(death, post))
            .OrderBy(death => MathF.Abs(GetSharedPostElapsedSeconds(death) - post.ElapsedSeconds))
            .ThenByDescending(death => death.SeenAtUtc)
            .ToList();

        var exactMatch = candidates.FirstOrDefault(death => DeathCauseMatchesSharedPost(death, post));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static bool IsSharedDeathCandidate(PartyDeathRecord death, SharedDeathPost post)
    {
        if (!string.Equals(death.MemberName, post.MemberName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(death.ClassJobName, post.ClassJobName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (MathF.Abs(GetSharedPostElapsedSeconds(death) - post.ElapsedSeconds) > SharedRecapMatchWindowSeconds)
        {
            return false;
        }

        return true;
    }

    private static bool DeathCauseMatchesSharedPost(PartyDeathRecord death, SharedDeathPost post)
    {
        var causeEvents = GetDisplayCauseEvents(death);
        if (post.Amount is null && post.ActionName is null && post.SourceName is null)
        {
            return causeEvents.Count == 0;
        }

        if (post.Amount is null)
        {
            return causeEvents.Any(cause =>
                cause.Kind == DeathEventKind.Status &&
                string.Equals(cause.ActionName, post.ActionName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cause.SourceName, post.SourceName, StringComparison.OrdinalIgnoreCase));
        }

        if (post.HitCount is { } hitCount)
        {
            var damageEvents = causeEvents
                .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
                .ToList();
            var totalDamage = damageEvents.Aggregate(0UL, (sum, cause) => sum + cause.Amount);
            if (damageEvents.Count != hitCount || totalDamage != post.Amount.Value)
            {
                return false;
            }

            return post.SourceName is null ||
                damageEvents.All(cause => string.Equals(cause.SourceName, post.SourceName, StringComparison.OrdinalIgnoreCase));
        }

        return causeEvents.Any(cause =>
            post.Amount.Value == cause.Amount &&
            string.Equals(cause.ActionName, post.ActionName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(cause.SourceName, post.SourceName, StringComparison.OrdinalIgnoreCase));
    }

    private static SharedDeathPost BuildSharedDeathPost(PartyDeathRecord death)
    {
        var causeEvents = GetDisplayCauseEvents(death);
        var damageEvents = causeEvents
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .ToList();
        if (damageEvents.Count > 1)
        {
            var totalDamage = damageEvents.Aggregate(0UL, (sum, cause) => sum + cause.Amount);
            var sourceName = GetSharedDamageSourceName(damageEvents);
            return new SharedDeathPost(
                GetSharedPostElapsedSeconds(death),
                death.MemberName,
                death.ClassJobName,
                null,
                sourceName == "multiple targets" ? null : sourceName,
                totalDamage,
                damageEvents.Count);
        }

        return causeEvents.FirstOrDefault() is { } cause
            ? new SharedDeathPost(
                GetSharedPostElapsedSeconds(death),
                death.MemberName,
                death.ClassJobName,
                cause.ActionName,
                cause.SourceName,
                cause.Kind == DeathEventKind.Status ? null : cause.Amount,
                null)
            : new SharedDeathPost(
                GetSharedPostElapsedSeconds(death),
                death.MemberName,
                death.ClassJobName,
                null,
                null,
                null,
                null);
    }

    private static float GetSharedPostElapsedSeconds(PartyDeathRecord death)
    {
        return (int)MathF.Max(0.0f, death.PullElapsedSeconds);
    }

    private static bool TryParseSharedDeathPost(string text, out SharedDeathPost post)
    {
        var cleaned = SanitizeChatText(text);
        var multiHitMatch = SharedMultiHitDeathPostRegex.Match(cleaned);
        if (multiHitMatch.Success &&
            TryParseCombatTimer(multiHitMatch.Groups["timer"].Value, out var multiHitElapsedSeconds) &&
            TryParseAmount(multiHitMatch.Groups["amount"].Value, out var multiHitAmount) &&
            int.TryParse(multiHitMatch.Groups["hits"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hitCount))
        {
            var sourceName = multiHitMatch.Groups["source"].Value;
            post = new SharedDeathPost(
                multiHitElapsedSeconds,
                multiHitMatch.Groups["name"].Value,
                multiHitMatch.Groups["job"].Value,
                null,
                string.Equals(sourceName, "multiple targets", StringComparison.OrdinalIgnoreCase) ? null : sourceName,
                multiHitAmount,
                hitCount);
            return true;
        }

        var knownMatch = SharedKnownDeathPostRegex.Match(cleaned);
        if (knownMatch.Success &&
            TryParseCombatTimer(knownMatch.Groups["timer"].Value, out var knownElapsedSeconds) &&
            TryParseAmount(knownMatch.Groups["amount"].Value, out var amount))
        {
            post = new SharedDeathPost(
                knownElapsedSeconds,
                knownMatch.Groups["name"].Value,
                knownMatch.Groups["job"].Value,
                knownMatch.Groups["action"].Value,
                knownMatch.Groups["source"].Value,
                amount,
                null);
            return true;
        }

        var statusMatch = SharedStatusDeathPostRegex.Match(cleaned);
        if (statusMatch.Success &&
            TryParseCombatTimer(statusMatch.Groups["timer"].Value, out var statusElapsedSeconds))
        {
            post = new SharedDeathPost(
                statusElapsedSeconds,
                statusMatch.Groups["name"].Value,
                statusMatch.Groups["job"].Value,
                statusMatch.Groups["action"].Value,
                statusMatch.Groups["source"].Value,
                null,
                null);
            return true;
        }

        var unknownMatch = SharedUnknownDeathPostRegex.Match(cleaned);
        if (unknownMatch.Success &&
            TryParseCombatTimer(unknownMatch.Groups["timer"].Value, out var unknownElapsedSeconds))
        {
            post = new SharedDeathPost(
                unknownElapsedSeconds,
                unknownMatch.Groups["name"].Value,
                unknownMatch.Groups["job"].Value,
                null,
                null,
                null,
                null);
            return true;
        }

        post = default;
        return false;
    }

    private static bool TryParseCombatTimer(string timer, out int elapsedSeconds)
    {
        var parts = timer.Split(':', 2);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            elapsedSeconds = minutes * 60 + seconds;
            return true;
        }

        elapsedSeconds = 0;
        return false;
    }

    private static bool TryParseAmount(string amountText, out ulong amount)
    {
        return ulong.TryParse(
            amountText.Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private static bool PostsMatch(SharedDeathPost left, SharedDeathPost right)
    {
        return left.ElapsedSeconds == right.ElapsedSeconds &&
            string.Equals(left.MemberName, right.MemberName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.ClassJobName, right.ClassJobName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.ActionName, right.ActionName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.SourceName, right.SourceName, StringComparison.OrdinalIgnoreCase) &&
            left.Amount == right.Amount &&
            left.HitCount == right.HitCount;
    }

    private string GetChatBrandingPrefix()
    {
        return Configuration.RemoveChatBranding ? string.Empty : "[Better Deaths] ";
    }

    private void QueueDeathRecapLink(PartyDeathRecord death, DateTime now)
    {
        if (!HasDeathRecapDetails(death))
        {
            return;
        }

        pendingDeathRecapLinks.Add(death);
        pendingDeathRecapLinksDueAtUtc = now.AddSeconds(DeathRecapLinkBatchDelaySeconds);
    }

    private static bool HasDeathRecapDetails(PartyDeathRecord death)
    {
        return GetDisplayCauseEvents(death).Count > 0 ||
            death.FatalSequence is { Events.Count: > 0 } ||
            death.FatalSequence is { LogEvents.Count: > 0 };
    }

    private void FlushPendingDeathRecapLinks(DateTime now)
    {
        if (pendingDeathRecapLinks.Count == 0)
        {
            pendingDeathRecapLinksDueAtUtc = null;
            return;
        }

        if (pendingDeathRecapLinksDueAtUtc is { } dueAt && dueAt > now)
        {
            return;
        }

        var deaths = pendingDeathRecapLinks
            .OrderBy(death => death.SeenAtUtc)
            .ThenBy(death => death.PartyIndex)
            .ToList();
        pendingDeathRecapLinks.Clear();
        pendingDeathRecapLinksDueAtUtc = null;

        PrintDeathRecapLink(deaths[0], GetDeathRecapBatchLabel(deaths));
    }

    private static string GetDeathRecapBatchLabel(IReadOnlyList<PartyDeathRecord> deaths)
    {
        var namesText = FormatDeathRecapNames(deaths);
        return deaths.Count switch
        {
            <= 1 => namesText,
            >= 8 => "Party wipe detected",
            _ => $"{deaths.Count} deaths detected ({namesText})",
        };
    }

    private static string FormatDeathRecapNames(IReadOnlyList<PartyDeathRecord> deaths)
    {
        const int maxShownNames = 4;
        var names = deaths
            .Select(death => death.MemberName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        if (names.Count == 0)
        {
            return "unknown";
        }

        var shownNames = names.Take(maxShownNames).ToList();
        var hiddenCount = names.Count - shownNames.Count;
        return hiddenCount > 0
            ? $"{string.Join(", ", shownNames)}, +{hiddenCount} more"
            : string.Join(", ", shownNames);
    }

    private void PrintDeathRecapLink(PartyDeathRecord death)
    {
        PrintDeathRecapLink(death, death.MemberName);
    }

    private void PrintDetectedSharedRecapLink(PartyDeathRecord death)
    {
        PrintDeathRecapLink(death, "Pull link detected", "[ Open Recap ]");
    }

    private void PrintDeathRecapLink(PartyDeathRecord death, string batchLabel)
    {
        var label = Configuration.RemoveChatBranding ? "[ Open recap ]" : "[ Open Better Deaths recap ]";
        PrintDeathRecapLink(death, batchLabel, label);
    }

    private void PrintDeathRecapLink(PartyDeathRecord death, string batchLabel, string label)
    {
        var batchText = string.IsNullOrWhiteSpace(batchLabel) ? string.Empty : $"{batchLabel} ";
        var message = new SeString(
            new TextPayload(GetChatBrandingPrefix()),
            new TextPayload(batchText),
            deathChatLinkPayload,
            new UIForegroundPayload(710),
            new TextPayload(label),
            new UIForegroundPayload(0),
            new DeathChatLinkPayload(death.SeenAtUtc.Ticks, GetMemberKeyHash(death.MemberKey)),
            RawPayload.LinkTerminator);

        ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = message,
        });
    }

    private void QueueChat(DeathChatChannel channel, string message)
    {
        var effectiveChannel = GetChatChannelOption(channel).Channel;
        foreach (var line in SplitChatMessage(SanitizeChatText(message)))
        {
            queuedChatMessages.Enqueue(new QueuedChatMessage(effectiveChannel, line));
        }
    }

    private void FlushQueuedChatMessages(DateTime now)
    {
        if (queuedChatMessages.Count == 0 || nextQueuedChatMessageAtUtc > now)
        {
            return;
        }

        var nextMessage = queuedChatMessages.Dequeue();
        SendChat(nextMessage.Channel, nextMessage.Message);
        nextQueuedChatMessageAtUtc = now.AddMilliseconds(QueuedChatDelayMs);
    }

    private static unsafe void SendChat(DeathChatChannel channel, string message)
    {
        try
        {
            var uiModule = UIModule.Instance();
            var shellModule = RaptureShellModule.Instance();
            if (uiModule == null || shellModule == null)
            {
                Log.Warning("Could not send Better Deaths chat message because the UI shell is unavailable.");
                ChatGui.Print("[Better Deaths] Could not send chat message.");
                return;
            }

            using var command = new Utf8String($"{GetChatChannelOption(channel).Command} {SanitizeChatText(message)}");
            shellModule->ExecuteCommandInner(&command, uiModule);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not send Better Deaths chat message.");
            ChatGui.Print("[Better Deaths] Could not send chat message.");
        }
    }

    private static ChatChannelOption GetChatChannelOption(DeathChatChannel channel)
    {
        return ChatChannelOptions.FirstOrDefault(option => option.Channel == channel) ??
            ChatChannelOptions.First(option => option.Channel == DeathChatChannel.Party);
    }

    private static IEnumerable<string> SplitChatMessage(string message)
    {
        if (message.Length <= MaxQueuedChatMessageLength)
        {
            yield return message;
            yield break;
        }

        var remaining = message;
        while (remaining.Length > MaxQueuedChatMessageLength)
        {
            var splitAt = remaining.LastIndexOf(' ', MaxQueuedChatMessageLength);
            if (splitAt <= 0)
            {
                splitAt = MaxQueuedChatMessageLength;
            }

            yield return remaining[..splitAt].Trim();
            remaining = remaining[splitAt..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            yield return remaining;
        }
    }

    private static string SanitizeChatText(string message)
    {
        var cleaned = message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        return cleaned;
    }

    private readonly record struct SharedDeathPost(
        float ElapsedSeconds,
        string MemberName,
        string ClassJobName,
        string? ActionName,
        string? SourceName,
        ulong? Amount,
        int? HitCount);

    private readonly record struct RecentOwnSharedDeathPost(
        SharedDeathPost Post,
        DateTime ExpiresAtUtc);

    private readonly record struct QueuedChatMessage(
        DeathChatChannel Channel,
        string Message);

    private sealed record StatusObservation(
        DateTime SeenAtUtc,
        float PullElapsedSeconds,
        StatusSnapshot Status,
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        IReadOnlyList<StatusSnapshot> Statuses);
}
