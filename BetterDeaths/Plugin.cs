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

public sealed partial class Plugin : IDalamudPlugin
{
    private const string MainCommandName = "/betterdeaths";
    private const string ShortCommandName = "/bd";
    private const string WidgetCommandName = "/betterdeathswidget";
    private const string ShortWidgetCommandName = "/bdwidget";
    private const string BetterDeathsInternalName = "BetterDeaths";
    private const string LegacyDalamudRepositoryUrl = "https://raw.githubusercontent.com/Nainaiowo/IMakeSillyThings/refs/heads/main/repo.json";
    private const string PuniDalamudRepositoryUrl = "https://puni.sh/api/repository/nainai";
    private const string ThankYouNoticeId = "ui-polish-thank-you-2026-06";
    private const string SharedRecapPrefix = "Recap:";
    private const float SharedRecapMatchWindowSeconds = 5.0f;
    private const int RecentStatusHistorySeconds = 20;
    private const float StatusDeathRemainingWindowSeconds = 5.0f;
    private const int OwnSharedRecapSuppressionSeconds = 5;
    private const int QueuedChatDelayMs = 200;
    private const int DetectedSharedRecapLinkDelayMs = 800;
    private const int DeathRecapLinkBatchDelaySeconds = 3;
    private const int MaxQueuedChatMessageLength = 450;
    private const int MaxDebugLogEntries = 1000;
    private const string DebugCaptureFileName = "debug-capture.jsonl";
    private const long MaxDebugCaptureFileBytes = 25L * 1024L * 1024L;
    private const long DebugCaptureTrimTargetBytes = 20L * 1024L * 1024L;
    private const int MaxQueuedDebugCaptureFileLines = 5000;
    private const string RecordedPullHistoryFileName = "recorded-pulls.json";
    private const string RecordedPullIndexFileName = "recorded-pulls.index.json";
    private const string RecordedPullDetailsDirectoryName = "recorded-pull-details";
    private const int RecordedPullHistorySchemaVersion = 3;
    private const int RecordedPullIndexSchemaVersion = 4;
    private const int CurrentConfigurationVersion = 4;
    private const int RecordedPullHistoryRollingBackupCount = 5;
    private const int RecordedPullIndexRollingBackupCount = 3;
    private const string RecordedPullHistoryRollingBackupSearchPattern = "recorded-pulls.backup.*.json";
    private const string RecordedPullIndexRollingBackupSearchPattern = "recorded-pulls.index.backup.*.json";
    private const string AutoActionDisplayName = "Auto";
    private const ushort ChatGreenColorKey = 45;
    private const int BetterDeathsLeadUpSeconds = 10;
    private const int BetterDeathsLeadUpCaptureSeconds = BetterDeathsLeadUpSeconds + 10;
    private const int DeathReplayLeadUpSeconds = 20;
    private const int DeathReplayPostDeathSeconds = 10;
    private const int DeathReplayMarkerCarryInSeconds = 30;
    private const int DeathReplayRetentionSeconds = DeathReplayLeadUpSeconds + DeathReplayPostDeathSeconds + 5;
    private const uint DmuBlackHoleTetherId = 84;
    private const uint DmuGravenImageTetherId = 45;
    private const uint DmuGravenImageBaseId = 19505;
    private const uint DmuBlackHoleNothingnessActionId = 47868;
    private const int HpHistoryRetentionSeconds = BetterDeathsLeadUpCaptureSeconds + 5;
    private const int SourceMitigationHistoryRetentionSeconds = BetterDeathsLeadUpCaptureSeconds + 5;
    private const int CombatLogEventRetentionSeconds = BetterDeathsLeadUpCaptureSeconds + 5;
    private const int RawActionEffectRetentionSeconds = 5;
    private const int RawCombatLogRetentionSeconds = 10;
    private const int MaxRawActionEffectPackets = 256;
    private const int MaxRawCombatLogMessages = 256;
    private const int MaxRawEffectResultPackets = 256;
    private const int MaxRawActorControlPackets = 256;
    private const int MaxDebugEffectResultEvents = 1000;
    private const int MaxDebugActorControlEvents = 1000;
    private const int MaxAddonInspectorEvents = 500;
    private const int MaxAddonInspectorNodes = 500;
    private const int MaxAddonInspectorAtkValues = 128;
    private const int AddonInspectorDuplicateSuppressSeconds = 3;
    private const int MaxRecentHpHistoryPerMember = 240;
    private const int MaxSourceMitigationHistoryPerSource = 80;
    private const int MaxEnemyHpSnapshotsAtDeath = 5;
    private const int MaxReplayEnemyActors = 12;
    private const int MaxActionEffectTargets = 32;
    private const int MaxEffectResultEntries = 4;
    private const int MaxRecentEventsPerMember = 160;
    private const int MaxCombatLogEventsPerMember = 80;
    private const int MaxRecentReplayMarkersPerActor = 64;
    private const int MaxRecentReplayMechanicsPerSource = 96;
    private const uint ActorControlDeathCategory = 0x6;
    private const uint ActorControlGainEffectCategory = 0x14;
    private const uint ActorControlLoseEffectCategory = 0x15;
    private const uint ActorControlUpdateEffectCategory = 0x16;
    private const uint ActorControlTargetIconCategory = 0x22;
    private const uint ActorControlHotCategory = 0x604;
    private const uint ActorControlDotCategory = 0x605;
    private const uint InvalidActorEntityId = 0xE0000000;
    private const string ActorControlSignature = "E8 ?? ?? ?? ?? 0F B7 0B 83 E9 64";
    private const string EffectResultSignature = "48 8B C4 44 88 40 18 89 48 08";
    public const float CurrentPullWidgetMinBackgroundOpacity = 0.35f;
    public const float CurrentPullWidgetMaxBackgroundOpacity = 1.0f;
    public const float MainWindowMinBackgroundOpacity = 0.20f;
    public const float MainWindowMaxBackgroundOpacity = 1.0f;
    public const float DefaultMainWindowBackgroundOpacity = 0.85f;
    public const float MinWidgetIconSize = 12.0f;
    public const float MaxWidgetIconSize = 32.0f;
    private static readonly TimeSpan FatalSequenceStartBuffer = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan FatalSequenceEndBuffer = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan EffectResultActionMatchWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PostCombatCaptureGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PluginUpdateCheckInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan HpHistorySampleInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReplayPositionSampleInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HpHistoryDuplicateWindow = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan EffectResultHpHistoryPreResultWindow = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan EffectResultHpHistoryPostResultWindow = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan LiveCapturePruneInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DebugCaptureFlushInterval = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions RecordedPullHistoryJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };
    private static readonly JsonSerializerOptions DebugCaptureJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };
    private static readonly Regex SharedDamageDeathPostRegex = new(
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\):\s+(?<amount>[\d,]+)\s+damage\.(?:\s+HP before hit:\s+.+?\.)?(?:\s+Overkill:\s+(?:[\d,]+|-)\.)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
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
        @"^(?:\[Better Deaths\]\s*)?Recap:\s*(?<timer>\d{2,}:\d{2})\s+(?<name>.+?)\s+\((?<job>[^)]*)\):\s+(?:likely walled/)?non-hit KO\.(?:\s+HP before KO:\s+.+\.)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly AddonEvent[] AddonInspectorLifecycleEvents =
    [
        AddonEvent.PostSetup,
        AddonEvent.PostShow,
        AddonEvent.PostHide,
        AddonEvent.PostOpen,
        AddonEvent.PostClose,
        AddonEvent.PreFinalize,
    ];

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

    private enum RepositoryMigrationResult
    {
        NotFound,
        DevPlugin,
        UnexpectedSource,
        AlreadyMigrated,
        Migrated,
    }

    private sealed record RecordedPullHistoryFile(int SchemaVersion, List<PullDeathSnapshot> Pulls);

    private sealed record RecordedPullIndexFile(int SchemaVersion, List<RecordedPullIndexEntry> Pulls);

    private sealed record RecordedPullIndexEntry(
        DateTime CapturedAtUtc,
        string Reason,
        uint TerritoryId,
        string TerritoryName,
        float PullElapsedSeconds,
        int DeathCount,
        long PullNumber,
        string DetailFileName)
    {
        public string CapturedPluginVersion { get; init; } = string.Empty;
    }

    private sealed class RecordedPullState
    {
        public RecordedPullState(
            RecordedPullSummary summary,
            string detailFileName,
            PullDeathSnapshot? detail,
            bool detailDirty)
        {
            Summary = summary;
            DetailFileName = detailFileName;
            Detail = detail;
            DetailDirty = detailDirty;
        }

        public RecordedPullSummary Summary { get; set; }

        public string DetailFileName { get; set; }

        public PullDeathSnapshot? Detail { get; set; }

        public bool DetailDirty { get; set; }
    }

    private readonly record struct PlayerLabelCandidate(
        string MemberKey,
        string MemberName,
        int PartyIndex,
        uint ClassJobId,
        string ClassJobName,
        DateTime SeenAtUtc);

    private sealed record DebugCaptureFileRecord(
        DateTime SeenAtUtc,
        float PullElapsedSeconds,
        uint TerritoryId,
        string TerritoryName,
        string Kind,
        JsonElement Data);

    private sealed record DebugActionEffectRecord(
        DateTime SeenAtUtc,
        float PullElapsedSeconds,
        string MemberKey,
        string MemberName,
        int PartyIndex,
        uint SourceEntityId,
        string SourceName,
        uint ActionId,
        string ActionName,
        uint ActionIconId,
        string Kind,
        int KindId,
        uint Amount,
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        string DamageType,
        int DamageTypeId,
        bool Critical,
        bool DirectHit,
        bool Blocked,
        bool Parried,
        string Detail,
        string? EventIdentity,
        uint EventOrdinal,
        uint ActionSequence,
        string HpSource,
        int HpSourceId,
        DateTime? ResultSeenAtUtc,
        uint ResultCurrentHp,
        uint ResultShieldHp,
        uint ResultMaxHp,
        IReadOnlyList<StatusSnapshot> Statuses,
        IReadOnlyList<StatusSnapshot> SourceStatuses,
        IReadOnlyList<StatusSnapshot> ResultStatuses);

    private sealed record RawActionEffectPacket(
        long Sequence,
        DateTime SeenAtUtc,
        uint ActionSequence,
        uint CasterEntityId,
        string CasterName,
        uint ActionId,
        RawCombatSnapshot? SourceSnapshot,
        IReadOnlyList<RawActionEffectTarget> Targets);

    private sealed record RawActionEffectTarget(
        int TargetIndex,
        RawTargetId TargetId,
        RawCombatSnapshot? TargetSnapshot,
        IReadOnlyList<RawActionEffectSlot> Effects);

    private sealed record RawTargetId(ulong Id, uint ObjectId);

    private sealed record RawCombatSnapshot(
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        IReadOnlyList<RawStatusSnapshot> Statuses);

    private sealed record RawStatusSnapshot(
        uint StatusId,
        uint SourceId,
        ushort StackCount,
        float RemainingTime);

    private sealed record RawActionEffectSlot(
        int EffectIndex,
        byte Type,
        uint Param0,
        uint Param1,
        uint Param3,
        uint Param4,
        uint Value);

    private sealed record RawCombatLogMessage(
        long Sequence,
        DateTime SeenAtUtc,
        uint LogMessageId,
        string SourceName,
        bool SourceIsPlayer,
        string TargetName,
        bool TargetIsPlayer,
        string ActionName,
        uint Amount);

    private sealed record RawEffectResultPacket(
        long Sequence,
        DateTime SeenAtUtc,
        uint TargetId,
        uint RelatedActionSequence,
        uint ActorId,
        uint CurrentHp,
        uint MaxHp,
        ushort CurrentMp,
        byte ShieldPercent,
        byte EffectCount,
        byte IsReplay,
        IReadOnlyList<RawEffectResultStatus> Statuses);

    private sealed record RawEffectResultStatus(
        byte EffectIndex,
        ushort EffectId,
        ushort StackCount,
        float Duration,
        uint SourceActorId);

    private sealed record RawActorControlPacket(
        long Sequence,
        DateTime SeenAtUtc,
        uint EntityId,
        uint Category,
        uint Param1,
        uint Param2,
        uint Param3,
        uint Param4,
        uint Param5,
        uint Param6,
        uint Param7,
        uint Param8,
        ulong TargetId,
        byte Param9,
        RawCombatSnapshot? TargetSnapshot,
        RawCombatSnapshot? SourceSnapshot);

    private sealed record PendingEffectResult(
        RawEffectResultPacket Packet,
        uint ShieldHp,
        IReadOnlyList<StatusSnapshot> ResultStatuses);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private unsafe struct EffectResultPacket
    {
        public uint Unknown1;
        public uint RelatedActionSequence;
        public uint ActorId;
        public uint CurrentHp;
        public uint MaxHp;
        public ushort CurrentMp;
        public ushort Unknown3;
        public byte DamageShield;
        public byte EffectCount;
        public ushort Unknown6;
        public fixed byte Effects[64];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct EffectResultStatusEntry
    {
        public byte EffectIndex;
        public byte Unknown1;
        public ushort EffectId;
        public ushort StackCount;
        public ushort Unknown3;
        public float Duration;
        public uint SourceActorId;
    }

    private delegate void ProcessPacketEffectResultDelegate(uint targetId, IntPtr actionIntegrityData, byte isReplay);

    private delegate void ProcessPacketActorControlDelegate(
        uint entityId,
        uint category,
        uint param1,
        uint param2,
        uint param3,
        uint param4,
        uint param5,
        uint param6,
        uint param7,
        uint param8,
        ulong targetId,
        byte param9);

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
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("BetterDeaths");
    private readonly RecapWindow recapWindow;
    private readonly CurrentPullWidgetWindow currentPullWidgetWindow;
    private readonly DeathRecapPopupWindow deathRecapPopupWindow;
    private readonly List<PartyMemberSnapshot> currentMembers = [];
    private readonly List<PartyDeathRecord> currentDeaths = [];
    private readonly List<RecordedPullState> recordedPulls = [];
    private readonly object recordedPullLock = new();
    private IReadOnlyList<RecordedPullSummary> recordedPullSummaries = [];
    private readonly List<DebugLogEntry> debugLogEntries = [];
    private readonly Dictionary<string, DebugStatusSnapshot> debugStatusSnapshotsByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> debugStatusPersistSignaturesByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DebugEffectResultSnapshot> debugEffectResultSnapshotsByTarget = new(StringComparer.Ordinal);
    private readonly List<DebugEffectResultSnapshot> debugEffectResultHistory = [];
    private readonly List<DebugActorControlEvent> debugActorControlEvents = [];
    private readonly List<AddonInspectorEvent> addonInspectorEvents = [];
    private readonly Dictionary<string, DateTime> addonInspectorEventSeenAtBySignature = new(StringComparer.Ordinal);
    private readonly Queue<string> debugCaptureFileLines = new();
    private readonly object debugCaptureFileLock = new();
    private readonly DalamudLinkPayload deathChatLinkPayload;
    private readonly Dictionary<string, List<CombatEventRecord>> recentEventsByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CombatLogEventRecord>> recentCombatLogEventsByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<StatusObservation>> recentStatusesByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<HpHistorySnapshot>> recentHpHistoryByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ReplayPositionSnapshot>> recentReplayPositionsByActor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ReplayMarkerSnapshot>> recentReplayMarkersByActor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ReplayMechanicSnapshot>> recentReplayMechanicsBySource = new(StringComparer.Ordinal);
    private readonly Dictionary<(string MemberKey, uint ActionSequence), PendingEffectResult> pendingEffectResultsByMemberSequence = [];
    private readonly Dictionary<uint, List<SourceMitigationSnapshot>> recentSourceMitigationHistoryBySource = [];
    private readonly Dictionary<string, Dictionary<string, TrackedPossibleMitigationUse>> possibleMitigationUsesByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> lastHpHistorySampleByMember = new(StringComparer.Ordinal);
    private DateTime lastReplayPositionSampleAtUtc = DateTime.MinValue;
    private DateTime lastCurrentDeathReplayUpdateAtUtc = DateTime.MinValue;
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
    private readonly object rawCombatQueueLock = new();
    private readonly Queue<RawActionEffectPacket> rawActionEffectPackets = [];
    private readonly Queue<RawCombatLogMessage> rawCombatLogMessages = [];
    private readonly Queue<RawEffectResultPacket> rawEffectResultPackets = [];
    private readonly Queue<RawActorControlPacket> rawActorControlPackets = [];
    private long nextRawActionEffectSequence = 1;
    private long nextRawCombatLogSequence = 1;
    private long nextRawEffectResultSequence = 1;
    private long nextRawActorControlSequence = 1;
    private long nextResolvedCombatEventOrdinal = 1;
    private DateTime? pendingDeathRecapLinksDueAtUtc;
    private DateTime nextQueuedChatMessageAtUtc = DateTime.MinValue;
    private DateTime nextPluginUpdateCheckAtUtc = DateTime.MinValue;
    private DateTime nextLiveCapturePruneAtUtc = DateTime.MinValue;
    private DateTime? lastPluginUpdateCheckAtUtc;
    private PluginUpdateCheckState pluginUpdateCheckState = PluginUpdateCheckState.NotChecked;
    private string? availablePluginUpdateVersion;
    private bool availablePluginUpdateIsTesting;
    private string? pluginUpdateCheckError;
    private AddonInspectorSnapshot? addonInspectorSnapshot;
    private string? pendingUpdateNoticeKey;
    private CancellationTokenSource? recordedPullHistoryLoadCts;
    private Task? recordedPullHistoryLoadTask;
    private string? recordedPullHistoryLoadError;
    private bool recordedPullHistoryLoading;
    private bool recordedPullStorageDirty;
    private bool updateCheckInProgress;
    private bool effectResultHookEnabled;
    private bool actorControlHookEnabled;
    private bool debugFreezeOnDeathEnabled;
    private bool debugCaptureFrozen;
    private bool addonInspectorLifecycleRegistered;
    private DateTime lastDebugCaptureFlushAtUtc = DateTime.MinValue;
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
    private Hook<ProcessPacketEffectResultDelegate>? effectResultHook;
    private Hook<ProcessPacketActorControlDelegate>? actorControlHook;
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

    public IReadOnlyList<RecordedPullSummary> RecordedPulls => recordedPullSummaries;

    public bool RecordedPullHistoryLoading => recordedPullHistoryLoading;

    public string? RecordedPullHistoryLoadError => recordedPullHistoryLoadError;

    public IReadOnlyList<DebugLogEntry> DebugLogEntries => debugLogEntries;

    public IReadOnlyList<DebugStatusSnapshot> DebugStatusSnapshots => debugStatusSnapshotsByMember.Values
        .OrderBy(snapshot => snapshot.PartyIndex)
        .ThenBy(snapshot => snapshot.MemberName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<DebugEffectResultSnapshot> DebugEffectResultSnapshots => debugEffectResultSnapshotsByTarget.Values
        .OrderBy(snapshot => snapshot.PullElapsedSeconds)
        .ThenBy(snapshot => snapshot.TargetName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<DebugEffectResultSnapshot> DebugEffectResultHistory => debugEffectResultHistory
        .OrderBy(snapshot => snapshot.SeenAtUtc)
        .ThenBy(snapshot => snapshot.TargetName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<DebugActorControlEvent> DebugActorControlEvents => debugActorControlEvents
        .OrderBy(entry => entry.SeenAtUtc)
        .ThenBy(entry => entry.Category)
        .ToList();

    public IReadOnlyList<AddonInspectorEvent> AddonInspectorEvents => addonInspectorEvents
        .OrderByDescending(entry => entry.SeenAtUtc)
        .ThenBy(entry => entry.AddonName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public AddonInspectorSnapshot? AddonInspectorSnapshot => addonInspectorSnapshot;

    public bool DebugIsDutyCaptureActive => IsDutyCaptureActive();

    public bool DebugIsPvPCaptureBlocked => IsPvPCaptureBlocked();

    public bool DebugIsInCombat => Condition[ConditionFlag.InCombat];

    public bool DebugShouldCaptureLiveCombat => ShouldCaptureLiveCombat(DateTime.UtcNow);

    public bool DebugEffectResultHookEnabled => effectResultHookEnabled;

    public bool DebugActorControlHookEnabled => actorControlHookEnabled;

    public bool DebugFreezeOnDeathEnabled => debugFreezeOnDeathEnabled;

    public bool DebugCaptureFrozen => debugCaptureFrozen;

    public string DebugCaptureFilePath => DebugCaptureFileFullPath;

    public long DebugCaptureFileSizeBytes => GetDebugCaptureFileSizeBytes();

    public long DebugCaptureMaxFileSizeBytes => MaxDebugCaptureFileBytes;

    public string LocalDataDirectoryPath => PluginInterface.ConfigDirectory.FullName;

    public long LocalDataDirectorySizeBytes => GetDirectorySizeBytes(PluginInterface.ConfigDirectory.FullName);

    public long RecordedPullStorageSizeBytes => GetRecordedPullStorageSizeBytes();

    public int RecordedPullDetailFileCount => GetRecordedPullDetailFileCount();

    public int DebugCaptureQueuedLineCount
    {
        get
        {
            lock (debugCaptureFileLock)
            {
                return debugCaptureFileLines.Count;
            }
        }
    }

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
        NormalizeUserConfiguration();
        BeginLoadRecordedPullHistory();
        deathChatLinkPayload = ChatGui.AddChatLinkHandler(0, OnDeathChatLinkClick);

        recapWindow = new RecapWindow(this)
        {
            IsOpen = Configuration.ShowWindow,
        };
        windowSystem.AddWindow(recapWindow);

        deathRecapPopupWindow = new DeathRecapPopupWindow(this, recapWindow);
        windowSystem.AddWindow(deathRecapPopupWindow);

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

        try
        {
            actorControlHook = GameInteropProvider.HookFromSignature<ProcessPacketActorControlDelegate>(
                ActorControlSignature,
                OnProcessPacketActorControl);
            actorControlHook.Enable();
            actorControlHookEnabled = true;
        }
        catch (Exception ex)
        {
            actorControlHookEnabled = false;
            actorControlHook = null;
            Log.Warning(ex, "Better Deaths ActorControl debug hook could not be enabled.");
        }

        try
        {
            effectResultHook = GameInteropProvider.HookFromSignature<ProcessPacketEffectResultDelegate>(
                EffectResultSignature,
                OnProcessPacketEffectResult);
            effectResultHook.Enable();
            effectResultHookEnabled = true;
        }
        catch (Exception ex)
        {
            effectResultHookEnabled = false;
            effectResultHook = null;
            Log.Warning(ex, "Better Deaths EffectResult debug hook could not be enabled.");
        }

        DutyState.DutyStarted += OnDutyStarted;
        DutyState.DutyWiped += OnDutyReset;
        DutyState.DutyRecommenced += OnDutyReset;
        ChatGui.ChatMessage += OnChatMessage;
        ChatGui.LogMessage += OnLogMessage;
        if (Configuration.ShowDebugTab)
        {
            RegisterAddonInspectorLifecycleListeners();
        }

        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenMainUi;
        _ = MigrateToPuniRepositoryAsync();
    }

    public void Dispose()
    {
        disposing = true;
        CaptureCurrentPullSnapshot("Plugin unloaded");
        SaveRecordedPullHistory();
        FlushDebugCaptureFile(force: true);
        recordedPullHistoryLoadCts?.Cancel();
        try
        {
            recordedPullHistoryLoadTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
        {
            Log.Warning(ex, "Better Deaths recorded pull history load did not finish before disposal.");
        }

        recordedPullHistoryLoadCts?.Dispose();
        PluginInterface.UiBuilder.OpenConfigUi -= OpenMainUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Framework.Update -= OnFrameworkUpdate;
        UnregisterAddonInspectorLifecycleListeners();
        ChatGui.LogMessage -= OnLogMessage;
        ChatGui.ChatMessage -= OnChatMessage;
        DutyState.DutyRecommenced -= OnDutyReset;
        DutyState.DutyWiped -= OnDutyReset;
        DutyState.DutyStarted -= OnDutyStarted;
        effectResultHook?.Dispose();
        actorControlHook?.Dispose();
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

    public bool ShouldShowThankYouNotice()
    {
        return !string.Equals(Configuration.LastAcknowledgedNoticeId, ThankYouNoticeId, StringComparison.Ordinal);
    }

    public void MarkThankYouNoticeAcknowledged()
    {
        if (string.Equals(Configuration.LastAcknowledgedNoticeId, ThankYouNoticeId, StringComparison.Ordinal))
        {
            return;
        }

        Configuration.LastAcknowledgedNoticeId = ThankYouNoticeId;
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

    private async Task MigrateToPuniRepositoryAsync()
    {
        if (Configuration.PuniRepositoryMigrationComplete)
        {
            return;
        }

        try
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(PuniDalamudRepositoryUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Debug(
                    "Better Deaths Puni repository migration skipped because {RepositoryUrl} returned {StatusCode}.",
                    PuniDalamudRepositoryUrl,
                    response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!PuniRepositoryContainsInstallableBetterDeaths(json))
            {
                Log.Debug("Better Deaths Puni repository migration skipped because the Puni feed does not contain an installable BetterDeaths release yet.");
                return;
            }

            TryAddDalamudRepository(PuniDalamudRepositoryUrl);
            var migrationResult = TryMigrateInstalledPluginRepository();
            if (migrationResult is RepositoryMigrationResult.Migrated or RepositoryMigrationResult.AlreadyMigrated)
            {
                Configuration.PuniRepositoryMigrationComplete = true;
                SaveConfiguration();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Better Deaths Puni repository migration failed.");
        }
    }

    private static bool PuniRepositoryContainsInstallableBetterDeaths(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var plugin in document.RootElement.EnumerateArray())
            {
                if (!TryGetJsonString(plugin, "InternalName", out var internalName) ||
                    !string.Equals(internalName, BetterDeathsInternalName, StringComparison.Ordinal))
                {
                    continue;
                }

                return TryGetJsonString(plugin, "AssemblyVersion", out var assemblyVersion) &&
                    !string.IsNullOrWhiteSpace(assemblyVersion) &&
                    TryGetJsonString(plugin, "DownloadLinkInstall", out var installLink) &&
                    !string.IsNullOrWhiteSpace(installLink) &&
                    TryGetJsonString(plugin, "DownloadLinkUpdate", out var updateLink) &&
                    !string.IsNullOrWhiteSpace(updateLink);
            }
        }
        catch (JsonException ex)
        {
            Log.Debug(ex, "Better Deaths Puni repository migration could not parse the Puni feed.");
        }

        return false;
    }

    private static bool TryGetJsonString(JsonElement element, string propertyName, out string value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString() ?? string.Empty;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static void TryAddDalamudRepository(string repositoryUrl)
    {
        try
        {
            var assembly = typeof(IDalamudPlugin).Assembly;
            var config = GetDalamudService(assembly, "Dalamud.Configuration.Internal.DalamudConfiguration");
            if (config is null)
            {
                Log.Debug("Better Deaths Puni repository migration could not find DalamudConfiguration.");
                return;
            }

            var repoList = GetPropertyValue(config, "ThirdRepoList") as IList;
            if (repoList is null)
            {
                Log.Debug("Better Deaths Puni repository migration could not find ThirdRepoList.");
                return;
            }

            var changed = false;
            foreach (var repo in repoList)
            {
                var existingUrl = GetStringProperty(repo, "Url");
                if (!string.Equals(existingUrl, repositoryUrl, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                changed |= SetBoolProperty(repo, "IsEnabled", true);
                SaveDalamudRepositoryConfiguration(assembly, config, changed);
                return;
            }

            var repoType = assembly.GetType("Dalamud.Configuration.ThirdPartyRepoSettings", throwOnError: false);
            if (repoType is null)
            {
                Log.Debug("Better Deaths Puni repository migration could not find ThirdPartyRepoSettings.");
                return;
            }

            var newRepo = Activator.CreateInstance(repoType);
            if (newRepo is null)
            {
                Log.Debug("Better Deaths Puni repository migration could not create ThirdPartyRepoSettings.");
                return;
            }

            SetStringProperty(newRepo, "Url", repositoryUrl);
            SetBoolProperty(newRepo, "IsEnabled", true);
            repoList.Add(newRepo);
            SaveDalamudRepositoryConfiguration(assembly, config, changed: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Better Deaths Puni repository migration could not add the Puni repository.");
        }
    }

    private static RepositoryMigrationResult TryMigrateInstalledPluginRepository()
    {
        try
        {
            var assembly = typeof(IDalamudPlugin).Assembly;
            var manager = GetDalamudService(assembly, "Dalamud.Plugin.Internal.PluginManager");
            var plugins = GetPropertyValue(manager, "InstalledPlugins") as IEnumerable;
            if (plugins is null)
            {
                Log.Debug("Better Deaths Puni repository migration could not find InstalledPlugins.");
                return RepositoryMigrationResult.NotFound;
            }

            foreach (var plugin in plugins)
            {
                if (!string.Equals(GetStringProperty(plugin, "InternalName"), BetterDeathsInternalName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (GetBoolProperty(plugin, "IsDev") == true)
                {
                    return RepositoryMigrationResult.DevPlugin;
                }

                var manifest = GetFieldValue(plugin, "manifest") ?? GetPropertyValue(plugin, "Manifest");
                if (manifest is null)
                {
                    Log.Debug("Better Deaths Puni repository migration found BetterDeaths but could not read its manifest.");
                    return RepositoryMigrationResult.NotFound;
                }

                var installedFromUrl = GetStringProperty(manifest, "InstalledFromUrl");
                if (string.Equals(installedFromUrl, PuniDalamudRepositoryUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return RepositoryMigrationResult.AlreadyMigrated;
                }

                if (string.IsNullOrWhiteSpace(installedFromUrl) ||
                    (!installedFromUrl.Contains("IMakeSillyThings", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(installedFromUrl, LegacyDalamudRepositoryUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    Log.Debug(
                        "Better Deaths Puni repository migration did not rewrite unexpected install URL: {InstalledFromUrl}",
                        installedFromUrl ?? string.Empty);
                    return RepositoryMigrationResult.UnexpectedSource;
                }

                SetStringProperty(manifest, "InstalledFromUrl", PuniDalamudRepositoryUrl);
                InvokeMethod(plugin, "SaveManifest", "Migrated to Puni.sh repository");
                Log.Information("Better Deaths migrated installed repository URL to Puni.sh.");
                return RepositoryMigrationResult.Migrated;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Better Deaths Puni repository migration could not rewrite the installed plugin source.");
        }

        return RepositoryMigrationResult.NotFound;
    }

    private static object? GetDalamudService(Assembly assembly, string serviceTypeName)
    {
        var serviceType = assembly.GetType("Dalamud.Service`1", throwOnError: false);
        var requestedType = assembly.GetType(serviceTypeName, throwOnError: false);
        if (serviceType is null || requestedType is null)
        {
            return null;
        }

        return serviceType
            .MakeGenericType(requestedType)
            .GetMethod("Get", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private static void SaveDalamudRepositoryConfiguration(Assembly assembly, object config, bool changed)
    {
        if (!changed)
        {
            return;
        }

        InvokeMethod(config, "QueueSave");
        var manager = GetDalamudService(assembly, "Dalamud.Plugin.Internal.PluginManager");
        InvokeMethod(manager, "SetPluginReposFromConfigAsync", true);
    }

    private static object? GetPropertyValue(object? instance, string propertyName)
    {
        return instance?.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(instance);
    }

    private static object? GetFieldValue(object? instance, string fieldName)
    {
        return instance?.GetType()
            .GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(instance);
    }

    private static string? GetStringProperty(object? instance, string propertyName)
    {
        return GetPropertyValue(instance, propertyName) as string;
    }

    private static bool? GetBoolProperty(object? instance, string propertyName)
    {
        return GetPropertyValue(instance, propertyName) as bool?;
    }

    private static bool SetStringProperty(object? instance, string propertyName, string value)
    {
        var property = instance?.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.SetMethod is null)
        {
            return false;
        }

        var previous = property.GetValue(instance) as string;
        if (string.Equals(previous, value, StringComparison.Ordinal))
        {
            return false;
        }

        property.SetValue(instance, value);
        return true;
    }

    private static bool SetBoolProperty(object? instance, string propertyName, bool value)
    {
        var property = instance?.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.SetMethod is null || property.PropertyType != typeof(bool))
        {
            return false;
        }

        var previous = property.GetValue(instance) as bool?;
        if (previous == value)
        {
            return false;
        }

        property.SetValue(instance, value);
        return true;
    }

    private static void InvokeMethod(object? instance, string methodName, params object[] parameters)
    {
        if (instance is null)
        {
            return;
        }

        instance.GetType()
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(instance, parameters);
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

    public void SetShowDebugTab(bool show)
    {
        if (Configuration.ShowDebugTab == show)
        {
            return;
        }

        Configuration.ShowDebugTab = show;
        if (show)
        {
            RegisterAddonInspectorLifecycleListeners();
        }
        else
        {
            UnregisterAddonInspectorLifecycleListeners();
        }

        SaveConfiguration();
    }

    public void SetDebugLogEnabled(bool enabled)
    {
        Configuration.DebugLogEnabled = enabled;
        SaveConfiguration();
    }

    public void SetDebugSaveToFileEnabled(bool enabled)
    {
        Configuration.DebugSaveToFileEnabled = enabled;
        SaveConfiguration();
        if (!enabled)
        {
            FlushDebugCaptureFile(force: true);
        }
    }

    public void SetDebugFreezeOnDeathEnabled(bool enabled)
    {
        debugFreezeOnDeathEnabled = enabled;
        if (!enabled && debugCaptureFrozen)
        {
            debugCaptureFrozen = false;
            AddDebugLog("Debug capture resumed.");
        }
    }

    public void SetDebugCaptureFrozen(bool frozen)
    {
        if (debugCaptureFrozen == frozen)
        {
            return;
        }

        debugCaptureFrozen = frozen;
        AddDebugLog(frozen ? "Debug capture frozen." : "Debug capture resumed.");
    }

    public void ClearDebugLog()
    {
        debugLogEntries.Clear();
        debugStatusSnapshotsByMember.Clear();
        debugStatusPersistSignaturesByMember.Clear();
        debugEffectResultSnapshotsByTarget.Clear();
        debugEffectResultHistory.Clear();
        debugActorControlEvents.Clear();
        addonInspectorEvents.Clear();
        addonInspectorEventSeenAtBySignature.Clear();
        addonInspectorSnapshot = null;
        debugCaptureFrozen = false;
    }

    public void ClearAddonInspector()
    {
        addonInspectorEvents.Clear();
        addonInspectorEventSeenAtBySignature.Clear();
        addonInspectorSnapshot = null;
    }

    public void CaptureAddonInspectorSnapshot(string addonName)
    {
        addonName = addonName.Trim();
        if (string.IsNullOrWhiteSpace(addonName))
        {
            addonInspectorSnapshot = CreateAddonInspectorErrorSnapshot(addonName, "Enter an addon name first.");
            return;
        }

        try
        {
            var addon = GameGui.GetAddonByName(addonName);
            addonInspectorSnapshot = CaptureAddonInspectorSnapshot(addonName, addon);
        }
        catch (Exception ex)
        {
            addonInspectorSnapshot = CreateAddonInspectorErrorSnapshot(addonName, ex.Message);
            Log.Debug(ex, "Could not capture Better Deaths addon inspector snapshot for {AddonName}.", addonName);
        }
    }

    public void ClearSavedDebugCaptureFile()
    {
        lock (debugCaptureFileLock)
        {
            debugCaptureFileLines.Clear();
        }

        try
        {
            if (File.Exists(DebugCaptureFileFullPath))
            {
                File.Delete(DebugCaptureFileFullPath);
            }

            if (File.Exists(DebugCaptureTempFilePath))
            {
                File.Delete(DebugCaptureTempFilePath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not clear Better Deaths debug capture file.");
        }
    }

    private void RegisterAddonInspectorLifecycleListeners()
    {
        if (addonInspectorLifecycleRegistered)
        {
            return;
        }

        foreach (var eventType in AddonInspectorLifecycleEvents)
        {
            AddonLifecycle.RegisterListener(eventType, OnAddonInspectorLifecycleEvent);
        }

        addonInspectorLifecycleRegistered = true;
    }

    private void UnregisterAddonInspectorLifecycleListeners()
    {
        if (!addonInspectorLifecycleRegistered)
        {
            return;
        }

        AddonLifecycle.UnregisterListener(OnAddonInspectorLifecycleEvent);
        addonInspectorLifecycleRegistered = false;
    }

    private void OnAddonInspectorLifecycleEvent(AddonEvent eventType, AddonArgs args)
    {
        if (disposing)
        {
            return;
        }

        try
        {
            var addon = args.Addon;
            var isKnown = addon.Address != 0 && !addon.IsNull;
            var now = DateTime.UtcNow;
            var signature = $"{eventType}|{args.AddonName}|{addon.Address}";
            if (addonInspectorEventSeenAtBySignature.TryGetValue(signature, out var lastSeen) &&
                now - lastSeen < TimeSpan.FromSeconds(AddonInspectorDuplicateSuppressSeconds))
            {
                return;
            }

            addonInspectorEventSeenAtBySignature[signature] = now;
            addonInspectorEvents.Add(new AddonInspectorEvent(
                now,
                eventType.ToString(),
                args.AddonName,
                addon.Address,
                isKnown && addon.IsReady,
                isKnown && addon.IsVisible));

            while (addonInspectorEvents.Count > MaxAddonInspectorEvents)
            {
                addonInspectorEvents.RemoveAt(0);
            }

            foreach (var expiredKey in addonInspectorEventSeenAtBySignature
                         .Where(pair => now - pair.Value > TimeSpan.FromMinutes(5))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                addonInspectorEventSeenAtBySignature.Remove(expiredKey);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture Better Deaths addon inspector lifecycle event.");
        }
    }

    private static AddonInspectorSnapshot CreateAddonInspectorErrorSnapshot(string addonName, string error)
    {
        return new AddonInspectorSnapshot(
            DateTime.UtcNow,
            addonName,
            0,
            false,
            false,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0,
            [],
            [],
            error);
    }

    private unsafe AddonInspectorSnapshot CaptureAddonInspectorSnapshot(string addonName, AtkUnitBasePtr addon)
    {
        if (addon.Address == 0 || addon.IsNull)
        {
            return CreateAddonInspectorErrorSnapshot(
                addonName,
                "Addon was not found. Open the game window first, then snapshot again.");
        }

        var nodes = CaptureAddonInspectorNodes((AtkUnitBase*)addon.Address, out var nodeCount);
        return new AddonInspectorSnapshot(
            DateTime.UtcNow,
            addonName,
            addon.Address,
            addon.IsReady,
            addon.IsVisible,
            addon.X,
            addon.Y,
            addon.Width,
            addon.Height,
            nodeCount,
            nodes,
            CaptureAddonInspectorAtkValues(addon),
            null);
    }

    private static unsafe IReadOnlyList<AddonInspectorNode> CaptureAddonInspectorNodes(
        AtkUnitBase* unit,
        out int nodeCount)
    {
        var nodes = new List<AddonInspectorNode>();
        nodeCount = 0;
        if (unit is null)
        {
            return nodes;
        }

        CaptureAddonInspectorNode(unit->RootNode, nodes, ref nodeCount, 0);
        return nodes;
    }

    private static unsafe void CaptureAddonInspectorNode(
        AtkResNode* node,
        List<AddonInspectorNode> nodes,
        ref int nodeCount,
        int depth)
    {
        if (node is null || nodes.Count >= MaxAddonInspectorNodes || depth > 80)
        {
            return;
        }

        var current = node;
        while (current is not null && nodes.Count < MaxAddonInspectorNodes)
        {
            nodeCount++;
            nodes.Add(new AddonInspectorNode(
                nodes.Count,
                current->NodeId,
                current->Type.ToString(),
                current->IsVisible(),
                current->X,
                current->Y,
                current->Width,
                current->Height,
                ReadAddonInspectorNodeText(current)));

            if (current->ChildNode is not null)
            {
                CaptureAddonInspectorNode(current->ChildNode, nodes, ref nodeCount, depth + 1);
            }

            current = current->NextSiblingNode;
        }
    }

    private static unsafe string? ReadAddonInspectorNodeText(AtkResNode* node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            var textNode = node->GetAsAtkTextNode();
            if (textNode is null)
            {
                return null;
            }

            var text = textNode->NodeText.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<AddonInspectorValue> CaptureAddonInspectorAtkValues(AtkUnitBasePtr addon)
    {
        var values = new List<AddonInspectorValue>();
        try
        {
            var index = 0;
            foreach (var atkValue in addon.AtkValues)
            {
                if (values.Count >= MaxAddonInspectorAtkValues)
                {
                    break;
                }

                values.Add(new AddonInspectorValue(
                    index,
                    atkValue.ValueType.ToString(),
                    FormatAddonInspectorAtkValue(atkValue)));
                index++;
            }
        }
        catch
        {
            values.Add(new AddonInspectorValue(0, "Error", "Could not read AtkValues."));
        }

        return values;
    }

    private static string FormatAddonInspectorAtkValue(AtkValuePtr atkValue)
    {
        try
        {
            var value = atkValue.GetValue();
            return value?.ToString() ?? "-";
        }
        catch (Exception ex)
        {
            return $"Unreadable: {ex.Message}";
        }
    }

    public void ClearRecordedPulls()
    {
        WaitForRecordedPullHistoryLoadForMutation();
        lock (recordedPullLock)
        {
            recordedPulls.Clear();
            recordedPullSummaries = [];
            nextRecordedPullNumber = 1;
            recordedPullStorageDirty = false;
        }

        currentPullRecordedPullNumber = 0;
        DeleteRecordedPullHistoryFiles();
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

    public void PrintDeathInformationToChat(PartyDeathRecord death)
    {
        var timer = FormatCombatTimer(death.PullElapsedSeconds);
        var playerLabel = $"{FormatPlayerDisplayName(death)} ({death.ClassJobName})";
        var prefix = GetChatBrandingPrefix();
        var selection = DeathDisplaySelector.Select(death);
        var causeEvents = selection.Events;
        if (causeEvents.Count == 0)
        {
            RememberOwnSharedDeathPost(death);
            var hpSuffix = selection.Snapshot is null
                ? string.Empty
                : $" HP before KO: {FormatDeathChatHp(selection.Snapshot.CurrentHp, selection.Snapshot.ShieldHp, selection.Snapshot.MaxHp)}.";
            QueueChat(Configuration.DeathChatChannel, $"{prefix}{SharedRecapPrefix} {timer} {playerLabel}: non-hit KO.{hpSuffix}");
            QueueChat(Configuration.DeathChatChannel, $"Active mits: {FormatDeathStatusList(death, selection)}.");
            QueueChat(Configuration.DeathChatChannel, $"Player debuffs: {FormatPlayerDebuffStatusList(death, selection)}.");
            return;
        }

        RememberOwnSharedDeathPost(death);
        var damageEvents = causeEvents
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .ToList();
        if (damageEvents.Count > 0)
        {
            QueueChat(Configuration.DeathChatChannel, $"{prefix}{SharedRecapPrefix} {timer} {playerLabel}: {FormatDeathChatDamageLine(damageEvents, selection.Snapshot)}");
        }
        else
        {
            QueueChat(Configuration.DeathChatChannel, $"{prefix}{SharedRecapPrefix} {timer} {playerLabel}: {FormatDeathChatCauseLine(causeEvents[0], selection.Snapshot)}");
        }

        QueueChat(Configuration.DeathChatChannel, $"Active mits: {FormatDeathStatusList(death, selection)}.");
        QueueChat(Configuration.DeathChatChannel, $"Player debuffs: {FormatPlayerDebuffStatusList(death, selection)}.");
        QueueDeathRecapLinkMessage(death);
    }

    public void QueueBetterDeathsChatMessage(string message)
    {
        QueueChat(Configuration.DeathChatChannel, $"{GetChatBrandingPrefix()}{message}");
    }

    public void QueuePlainChatMessage(string message)
    {
        QueueChat(Configuration.DeathChatChannel, message);
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
        var defenses = GetPrimaryDisplayPlayerStatuses(death, selection)
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
        var debuffs = GetPrimaryDisplayPlayerStatuses(death, selection)
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

    private static IReadOnlyList<StatusSnapshot> GetPrimaryDisplayPlayerStatuses(PartyDeathRecord death, DeathDisplaySelection selection)
    {
        return GetRelevantDeathStatuses(
            GetPrimaryDisplayStatusSnapshot(death, selection)
                .Concat(selection.Events.SelectMany(combatEvent => combatEvent.Statuses)));
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

    public string FormatPlayerDisplayName(PartyDeathRecord death)
    {
        return FormatPlayerDisplayName(death, FindDeathLabelContext(death));
    }

    public string FormatPlayerDisplayName(PartyDeathRecord death, IReadOnlyList<PartyDeathRecord>? context)
    {
        if (!Configuration.RedactPlayerNames)
        {
            return death.MemberName;
        }

        return FormatRedactedPlayerLabel(ToPlayerLabelCandidate(death), ToPlayerLabelCandidates(context));
    }

    public string FormatPlayerDisplayName(
        string memberName,
        string memberKey,
        int partyIndex,
        uint classJobId,
        string classJobName)
    {
        if (!Configuration.RedactPlayerNames)
        {
            return memberName;
        }

        return FormatRedactedPlayerLabel(new PlayerLabelCandidate(
            memberKey,
            memberName,
            partyIndex,
            classJobId,
            classJobName,
            DateTime.MinValue));
    }

    public string FormatKnownPlayerName(string name)
    {
        if (!Configuration.RedactPlayerNames || string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return BuildKnownPlayerNameMap().TryGetValue(name, out var label)
            ? label
            : name;
    }

    public string RedactKnownPlayerNamesInText(string text)
    {
        if (!Configuration.RedactPlayerNames || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        foreach (var pair in BuildKnownPlayerNameMap()
                     .OrderByDescending(pair => pair.Key.Length))
        {
            text = text.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private Dictionary<string, string> BuildKnownPlayerNameMap()
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddPlayerLabelCandidates(labels, currentMembers.Select(ToPlayerLabelCandidate));
        AddDeathLabelCandidates(labels, currentDeaths);

        foreach (var pull in GetLoadedRecordedPullDetails().AsEnumerable().Reverse())
        {
            AddDeathLabelCandidates(labels, pull.Deaths);
        }

        return labels;
    }

    private static void AddDeathLabelCandidates(Dictionary<string, string> labels, IReadOnlyList<PartyDeathRecord> deaths)
    {
        AddPlayerLabelCandidates(labels, ToPlayerLabelCandidates(deaths));
    }

    private static void AddPlayerLabelCandidates(
        Dictionary<string, string> labels,
        IEnumerable<PlayerLabelCandidate> candidates)
    {
        var context = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.MemberName))
            .ToList();

        foreach (var candidate in context)
        {
            labels.TryAdd(candidate.MemberName, FormatRedactedPlayerLabel(candidate, context));
        }
    }

    private IReadOnlyList<PartyDeathRecord>? FindDeathLabelContext(PartyDeathRecord death)
    {
        if (currentDeaths.Any(candidate => DeathRecordsMatch(candidate, death)))
        {
            return currentDeaths;
        }

        return GetLoadedRecordedPullDetails()
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(pull => pull.Deaths.Any(candidate => DeathRecordsMatch(candidate, death)))
            ?.Deaths;
    }

    private List<PlayerLabelCandidate> GetDeathLabelContext(PartyDeathRecord death)
    {
        return ToPlayerLabelCandidates(FindDeathLabelContext(death));
    }

    private static bool DeathRecordsMatch(PartyDeathRecord left, PartyDeathRecord right)
    {
        return left.SeenAtUtc.Ticks == right.SeenAtUtc.Ticks &&
            string.Equals(left.MemberKey, right.MemberKey, StringComparison.Ordinal);
    }

    private static PlayerLabelCandidate ToPlayerLabelCandidate(PartyDeathRecord death)
    {
        return new PlayerLabelCandidate(
            death.MemberKey,
            death.MemberName,
            death.PartyIndex,
            death.ClassJobId,
            death.ClassJobName,
            death.SeenAtUtc);
    }

    private static PlayerLabelCandidate ToPlayerLabelCandidate(PartyMemberSnapshot member)
    {
        return new PlayerLabelCandidate(
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            member.ClassJobId,
            member.ClassJobName,
            DateTime.MinValue);
    }

    private static List<PlayerLabelCandidate> ToPlayerLabelCandidates(IReadOnlyList<PartyDeathRecord>? deaths)
    {
        return deaths?.Select(ToPlayerLabelCandidate).ToList() ?? [];
    }

    private static string FormatRedactedPlayerLabel(PlayerLabelCandidate candidate, IReadOnlyList<PlayerLabelCandidate>? context = null)
    {
        var role = GetRedactedRoleLabel(candidate.ClassJobName);
        var sameRoleCandidates = (context ?? [])
            .Where(other => string.Equals(GetRedactedRoleLabel(other.ClassJobName), role, StringComparison.Ordinal))
            .GroupBy(GetPlayerLabelCandidateKey, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(NormalizedPartyIndex)
                .ThenBy(other => other.SeenAtUtc)
                .ThenBy(other => other.MemberName, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(NormalizedPartyIndex)
            .ThenBy(other => other.SeenAtUtc)
            .ThenBy(other => other.MemberName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = sameRoleCandidates.FindIndex(other => PlayerLabelCandidatesMatch(other, candidate));
        return $"{role} {(index >= 0 ? index + 1 : GetFallbackRedactedRoleIndex(candidate, role))}";
    }

    private static string GetRedactedRoleLabel(string classJobName)
    {
        var job = classJobName.Trim().ToUpperInvariant();
        return job switch
        {
            "GLA" or "PLD" or "MRD" or "WAR" or "DRK" or "GNB" => "Tank",
            "CNJ" or "WHM" or "SCH" or "AST" or "SGE" => "Healer",
            _ => "DPS",
        };
    }

    private static int GetFallbackRedactedRoleIndex(PlayerLabelCandidate candidate, string role)
    {
        if (candidate.PartyIndex < 0)
        {
            return 1;
        }

        return role switch
        {
            "Tank" => candidate.PartyIndex <= 1 ? candidate.PartyIndex + 1 : 1,
            "Healer" => candidate.PartyIndex is >= 2 and <= 3 ? candidate.PartyIndex - 1 : 1,
            "DPS" => candidate.PartyIndex >= 4 ? candidate.PartyIndex - 3 : Math.Max(1, candidate.PartyIndex + 1),
            _ => Math.Max(1, candidate.PartyIndex + 1),
        };
    }

    private static string GetPlayerLabelCandidateKey(PlayerLabelCandidate candidate)
    {
        return string.IsNullOrWhiteSpace(candidate.MemberKey)
            ? $"name:{candidate.MemberName}:{candidate.ClassJobName}"
            : $"key:{candidate.MemberKey}";
    }

    private static bool PlayerLabelCandidatesMatch(PlayerLabelCandidate left, PlayerLabelCandidate right)
    {
        if (!string.IsNullOrWhiteSpace(left.MemberKey) && !string.IsNullOrWhiteSpace(right.MemberKey))
        {
            return string.Equals(left.MemberKey, right.MemberKey, StringComparison.Ordinal);
        }

        return string.Equals(left.MemberName, right.MemberName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.ClassJobName, right.ClassJobName, StringComparison.OrdinalIgnoreCase);
    }

    private static int NormalizedPartyIndex(PlayerLabelCandidate candidate)
    {
        return candidate.PartyIndex < 0 ? int.MaxValue : candidate.PartyIndex;
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

                QueueDetectedSharedRecapLink(death);
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
        if (!ShouldAcceptRawCombatCapture(now))
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

        if (!TryGetCombatLogAmount(message, out var amount) || amount == 0)
        {
            return;
        }

        var actionName = TryGetCombatLogActionName(message, out var parsedActionName) &&
            !string.IsNullOrWhiteSpace(parsedActionName)
            ? parsedActionName
            : "Auto";
        var targetName = FormatLogEntityName(target);
        EnqueueRawCombatLogMessage(new RawCombatLogMessage(
            GetNextRawCombatLogSequence(),
            now,
            logMessageId,
            FormatLogEntityName(source),
            source.IsPlayer,
            targetName,
            target.IsPlayer,
            actionName,
            amount));
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
            FlushDebugCaptureFile(now);
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

    private static string GetCurrentPluginVersionForSavedData()
    {
        return FormatVersionForDisplay(typeof(Plugin).Assembly.GetName().Version);
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
        TrackDebugStatusSnapshots(currentMembers, now);
        UpdatePostResetDeathSuppression();
        if (ShouldAcceptRawCombatCapture(now))
        {
            ResolveRawCombatQueues(now);
        }

        if (!ShouldCaptureLiveCombat(now))
        {
            return;
        }

        TrackRecentStatuses(currentMembers, now);
        TrackRecentHpHistory(currentMembers, now);
        TrackRecentReplayPositions(currentMembers, now);

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

            TryCaptureDeath(member, now, "Framework");
        }

        UpdateCurrentDeathReplayPositions(now);
    }

    private List<PartyMemberSnapshot> BuildTrackedCharacterSnapshots()
    {
        var members = new List<PartyMemberSnapshot>();
        var partyEntityIds = new HashSet<uint>();
        var partyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localPlayer = ObjectTable.LocalPlayer;
        var partyIndex = 0;
        foreach (var member in PartyList)
        {
            var memberName = member.Name.TextValue;
            if (!string.IsNullOrWhiteSpace(memberName))
            {
                partyNames.Add(memberName);
            }

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
                member.GameObject?.Position ?? Vector3.Zero,
                member.GameObject?.Rotation ?? 0.0f,
                BuildCharacterStatusSnapshots(member.GameObject, member.Statuses)));
            partyIndex++;
        }

        AddLocalPlayerSnapshotIfMissing(members, partyEntityIds, partyNames, partyIndex, localPlayer);

        if (Configuration.CaptureOtherDeaths)
        {
            var excludedOtherEntityIds = partyEntityIds.ToHashSet();
            if (localPlayer?.EntityId is { } localEntityId && localEntityId != 0)
            {
                excludedOtherEntityIds.Add(localEntityId);
            }

            foreach (var gameObject in ObjectTable)
            {
                if (gameObject is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player ||
                    player.EntityId == 0 ||
                    excludedOtherEntityIds.Contains(player.EntityId) ||
                    player.MaxHp == 0)
                {
                    continue;
                }

                var memberName = player.Name.TextValue;
                if (string.IsNullOrWhiteSpace(memberName))
                {
                    continue;
                }

                var statusSnapshots = BuildCharacterStatusSnapshots(player, []);
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
                    player.Position,
                    player.Rotation,
                    statusSnapshots));
            }
        }

        return members
            .OrderBy(member => member.PartyIndex)
            .ThenBy(member => member.MemberName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddLocalPlayerSnapshotIfMissing(
        List<PartyMemberSnapshot> members,
        HashSet<uint> trackedEntityIds,
        HashSet<string> trackedNames,
        int partyIndex,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter? localPlayer)
    {
        if (!Configuration.CapturePartyDeaths ||
            localPlayer is null ||
            localPlayer.EntityId == 0 ||
            localPlayer.MaxHp == 0)
        {
            return;
        }

        var memberName = localPlayer.Name.TextValue;
        if (string.IsNullOrWhiteSpace(memberName) ||
            trackedEntityIds.Contains(localPlayer.EntityId) ||
            trackedNames.Contains(memberName))
        {
            return;
        }

        var statusSnapshots = BuildCharacterStatusSnapshots(localPlayer, []);
        var classJobId = localPlayer.ClassJob.RowId;
        members.Add(new PartyMemberSnapshot(
            $"entity:{localPlayer.EntityId:X8}",
            memberName,
            partyIndex,
            classJobId,
            GetClassJobName(classJobId),
            0,
            localPlayer.EntityId,
            localPlayer.CurrentHp,
            CalculateShieldHp(localPlayer, localPlayer.MaxHp),
            localPlayer.MaxHp,
            localPlayer.IsDead || localPlayer.CurrentHp == 0,
            true,
            localPlayer.Position,
            localPlayer.Rotation,
            statusSnapshots));
        trackedEntityIds.Add(localPlayer.EntityId);
        trackedNames.Add(memberName);
    }

    private IReadOnlyList<StatusSnapshot> BuildCharacterStatusSnapshots(
        Dalamud.Game.ClientState.Objects.Types.IGameObject? gameObject,
        IEnumerable<IStatus> fallbackStatuses)
    {
        return gameObject is Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara
            ? BuildStatusSnapshots(battleChara.StatusList)
            : BuildStatusSnapshots(fallbackStatuses);
    }

    private IReadOnlyList<StatusSnapshot> BuildCharacterStatusSnapshotsOrFallback(
        Dalamud.Game.ClientState.Objects.Types.IGameObject? gameObject,
        IReadOnlyList<StatusSnapshot> fallbackStatuses)
    {
        return gameObject is Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara
            ? BuildStatusSnapshots(battleChara.StatusList)
            : fallbackStatuses;
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

    private static uint CalculateShieldHpFromPercent(uint maxHp, byte shieldPercent)
    {
        if (maxHp == 0 || shieldPercent == 0)
        {
            return 0;
        }

        var shieldPercentage = Math.Clamp((double)shieldPercent, 0.0, 100.0);
        return (uint)Math.Round(maxHp * shieldPercentage / 100.0, MidpointRounding.AwayFromZero);
    }

    private RawCombatSnapshot? CaptureRawCombatSnapshot(GameObjectId targetId, bool playerOnly = false)
    {
        var entityId = GetEntityId(targetId);
        return entityId == 0 ? null : CaptureRawCombatSnapshot(entityId, playerOnly);
    }

    private RawCombatSnapshot? CaptureRawCombatSnapshot(uint entityId, bool playerOnly = false)
    {
        if (entityId == 0)
        {
            return null;
        }

        try
        {
            var gameObject = ObjectTable.SearchByEntityId(entityId);
            if (playerOnly && gameObject is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter)
            {
                return null;
            }

            if (gameObject is not Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara)
            {
                return null;
            }

            var maxHp = battleChara.MaxHp;
            var statuses = new List<RawStatusSnapshot>();
            foreach (var status in battleChara.StatusList)
            {
                if (status.StatusId == 0)
                {
                    continue;
                }

                statuses.Add(new RawStatusSnapshot(
                    status.StatusId,
                    status.SourceId,
                    status.Param,
                    status.RemainingTime));
            }

            return new RawCombatSnapshot(
                battleChara.CurrentHp,
                CalculateShieldHp(battleChara, maxHp),
                maxHp,
                statuses);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture Better Deaths raw combat snapshot for {EntityId:X8}.", entityId);
            return null;
        }
    }

    private static bool NormalizeCustomTheme(CustomThemeConfiguration customTheme, BetterDeathsTheme selectedTheme)
    {
        var changed = false;
        var defaults = BetterDeathsThemeCatalog.CreateCustomThemeConfiguration(BetterDeathsThemeCatalog.GetTheme(selectedTheme));

        if (customTheme.SchemaVersion < CustomThemeConfiguration.CurrentSchemaVersion)
        {
            customTheme.Divider = defaults.Divider;
            customTheme.Accent = defaults.Accent;
            customTheme.AccentSoft = defaults.AccentSoft;
            customTheme.DisabledText = defaults.DisabledText;
            customTheme.SpamWarningText = defaults.SpamWarningText;
            customTheme.OverkillText = defaults.OverkillText;
            customTheme.FrameBackground = defaults.FrameBackground;
            customTheme.FrameHoverBackground = defaults.FrameHoverBackground;
            customTheme.PopupBackground = defaults.PopupBackground;
            customTheme.ButtonHoverColor = defaults.ButtonHoverColor;
            customTheme.SelectedButtonHoverColor = defaults.SelectedButtonHoverColor;
            customTheme.ButtonActiveColor = defaults.ButtonActiveColor;
            customTheme.SelectedButtonText = defaults.SelectedButtonText;
            customTheme.CheckboxBackground = defaults.CheckboxBackground;
            customTheme.CheckboxHoverBackground = defaults.CheckboxHoverBackground;
            customTheme.CheckboxActiveBackground = defaults.CheckboxActiveBackground;
            customTheme.CheckboxCheckMark = defaults.CheckboxCheckMark;
            customTheme.CheckboxBorder = defaults.CheckboxBorder;
            customTheme.SliderGrab = defaults.SliderGrab;
            customTheme.SliderGrabActive = defaults.SliderGrabActive;
            customTheme.HeaderBackground = defaults.HeaderBackground;
            customTheme.HeaderHoverBackground = defaults.HeaderHoverBackground;
            customTheme.HeaderActiveBackground = defaults.HeaderActiveBackground;
            customTheme.TableRowAlt = defaults.TableRowAlt;
            customTheme.FocusedRow = defaults.FocusedRow;
            customTheme.FocusedRowAccent = defaults.FocusedRowAccent;
            customTheme.TimelineSelectedRow = defaults.TimelineSelectedRow;
            customTheme.TimelinePressedRow = defaults.TimelinePressedRow;
            customTheme.ScrollbarBackground = defaults.ScrollbarBackground;
            customTheme.ScrollbarGrab = defaults.ScrollbarGrab;
            customTheme.ScrollbarGrabHover = defaults.ScrollbarGrabHover;
            customTheme.ScrollbarGrabActive = defaults.ScrollbarGrabActive;
            customTheme.ChangelogTab = defaults.ChangelogTab;
            customTheme.ChangelogTabHover = defaults.ChangelogTabHover;
            customTheme.ChangelogTabActive = defaults.ChangelogTabActive;
            customTheme.HpBar = defaults.HpBar;
            customTheme.ShieldBar = defaults.ShieldBar;
            customTheme.BarBackground = defaults.BarBackground;
            customTheme.BarBorder = defaults.BarBorder;
            customTheme.WidgetWindowBackground = defaults.WidgetWindowBackground;
            customTheme.WidgetTitleBackground = defaults.WidgetTitleBackground;
            customTheme.WidgetTitleActiveBackground = defaults.WidgetTitleActiveBackground;
            customTheme.WidgetBorder = defaults.WidgetBorder;
            customTheme.WidgetResizeGrip = defaults.WidgetResizeGrip;
            customTheme.WidgetResizeGripHover = defaults.WidgetResizeGripHover;
            customTheme.WidgetResizeGripActive = defaults.WidgetResizeGripActive;
            customTheme.UpdateBannerBackground = defaults.UpdateBannerBackground;
            customTheme.UpdateBannerText = defaults.UpdateBannerText;
            customTheme.NoticeBorder = defaults.NoticeBorder;
            customTheme.NoticeText = defaults.NoticeText;
            customTheme.NoticeButton = defaults.NoticeButton;
            customTheme.NoticeButtonHover = defaults.NoticeButtonHover;
            customTheme.SchemaVersion = CustomThemeConfiguration.CurrentSchemaVersion;
            changed = true;
        }

        bool EnsureColor(ThemeColorValue? color, Action<ThemeColorValue> setColor)
        {
            if (color is not null)
            {
                return false;
            }

            setColor(new ThemeColorValue());
            return true;
        }

        changed |= EnsureColor(customTheme.WindowBackground, value => customTheme.WindowBackground = value);
        changed |= EnsureColor(customTheme.ContentBackground, value => customTheme.ContentBackground = value);
        changed |= EnsureColor(customTheme.RaisedBackground, value => customTheme.RaisedBackground = value);
        changed |= EnsureColor(customTheme.Border, value => customTheme.Border = value);
        changed |= EnsureColor(customTheme.Divider, value => customTheme.Divider = value);
        changed |= EnsureColor(customTheme.Accent, value => customTheme.Accent = value);
        changed |= EnsureColor(customTheme.AccentSoft, value => customTheme.AccentSoft = value);
        changed |= EnsureColor(customTheme.RegularText, value => customTheme.RegularText = value);
        changed |= EnsureColor(customTheme.MutedText, value => customTheme.MutedText = value);
        changed |= EnsureColor(customTheme.GoldText, value => customTheme.GoldText = value);
        changed |= EnsureColor(customTheme.DisabledText, value => customTheme.DisabledText = value);
        changed |= EnsureColor(customTheme.DamageText, value => customTheme.DamageText = value);
        changed |= EnsureColor(customTheme.HealText, value => customTheme.HealText = value);
        changed |= EnsureColor(customTheme.WarningText, value => customTheme.WarningText = value);
        changed |= EnsureColor(customTheme.SpamWarningText, value => customTheme.SpamWarningText = value);
        changed |= EnsureColor(customTheme.OverkillText, value => customTheme.OverkillText = value);
        changed |= EnsureColor(customTheme.FrameBackground, value => customTheme.FrameBackground = value);
        changed |= EnsureColor(customTheme.FrameHoverBackground, value => customTheme.FrameHoverBackground = value);
        changed |= EnsureColor(customTheme.PopupBackground, value => customTheme.PopupBackground = value);
        changed |= EnsureColor(customTheme.ButtonColor, value => customTheme.ButtonColor = value);
        changed |= EnsureColor(customTheme.ButtonHoverColor, value => customTheme.ButtonHoverColor = value);
        changed |= EnsureColor(customTheme.SelectedButtonColor, value => customTheme.SelectedButtonColor = value);
        changed |= EnsureColor(customTheme.SelectedButtonHoverColor, value => customTheme.SelectedButtonHoverColor = value);
        changed |= EnsureColor(customTheme.ButtonActiveColor, value => customTheme.ButtonActiveColor = value);
        changed |= EnsureColor(customTheme.ButtonText, value => customTheme.ButtonText = value);
        changed |= EnsureColor(customTheme.SelectedButtonText, value => customTheme.SelectedButtonText = value);
        changed |= EnsureColor(customTheme.CheckboxBackground, value => customTheme.CheckboxBackground = value);
        changed |= EnsureColor(customTheme.CheckboxHoverBackground, value => customTheme.CheckboxHoverBackground = value);
        changed |= EnsureColor(customTheme.CheckboxActiveBackground, value => customTheme.CheckboxActiveBackground = value);
        changed |= EnsureColor(customTheme.CheckboxCheckMark, value => customTheme.CheckboxCheckMark = value);
        changed |= EnsureColor(customTheme.CheckboxBorder, value => customTheme.CheckboxBorder = value);
        changed |= EnsureColor(customTheme.SliderGrab, value => customTheme.SliderGrab = value);
        changed |= EnsureColor(customTheme.SliderGrabActive, value => customTheme.SliderGrabActive = value);
        changed |= EnsureColor(customTheme.HeaderBackground, value => customTheme.HeaderBackground = value);
        changed |= EnsureColor(customTheme.HeaderHoverBackground, value => customTheme.HeaderHoverBackground = value);
        changed |= EnsureColor(customTheme.HeaderActiveBackground, value => customTheme.HeaderActiveBackground = value);
        changed |= EnsureColor(customTheme.TableRowAlt, value => customTheme.TableRowAlt = value);
        changed |= EnsureColor(customTheme.FocusedRow, value => customTheme.FocusedRow = value);
        changed |= EnsureColor(customTheme.FocusedRowAccent, value => customTheme.FocusedRowAccent = value);
        changed |= EnsureColor(customTheme.TimelineSelectedRow, value => customTheme.TimelineSelectedRow = value);
        changed |= EnsureColor(customTheme.TimelinePressedRow, value => customTheme.TimelinePressedRow = value);
        changed |= EnsureColor(customTheme.ScrollbarBackground, value => customTheme.ScrollbarBackground = value);
        changed |= EnsureColor(customTheme.ScrollbarGrab, value => customTheme.ScrollbarGrab = value);
        changed |= EnsureColor(customTheme.ScrollbarGrabHover, value => customTheme.ScrollbarGrabHover = value);
        changed |= EnsureColor(customTheme.ScrollbarGrabActive, value => customTheme.ScrollbarGrabActive = value);
        changed |= EnsureColor(customTheme.ChangelogTab, value => customTheme.ChangelogTab = value);
        changed |= EnsureColor(customTheme.ChangelogTabHover, value => customTheme.ChangelogTabHover = value);
        changed |= EnsureColor(customTheme.ChangelogTabActive, value => customTheme.ChangelogTabActive = value);
        changed |= EnsureColor(customTheme.HpBar, value => customTheme.HpBar = value);
        changed |= EnsureColor(customTheme.ShieldBar, value => customTheme.ShieldBar = value);
        changed |= EnsureColor(customTheme.BarBackground, value => customTheme.BarBackground = value);
        changed |= EnsureColor(customTheme.BarBorder, value => customTheme.BarBorder = value);
        changed |= EnsureColor(customTheme.WidgetWindowBackground, value => customTheme.WidgetWindowBackground = value);
        changed |= EnsureColor(customTheme.WidgetTitleBackground, value => customTheme.WidgetTitleBackground = value);
        changed |= EnsureColor(customTheme.WidgetTitleActiveBackground, value => customTheme.WidgetTitleActiveBackground = value);
        changed |= EnsureColor(customTheme.WidgetBorder, value => customTheme.WidgetBorder = value);
        changed |= EnsureColor(customTheme.WidgetResizeGrip, value => customTheme.WidgetResizeGrip = value);
        changed |= EnsureColor(customTheme.WidgetResizeGripHover, value => customTheme.WidgetResizeGripHover = value);
        changed |= EnsureColor(customTheme.WidgetResizeGripActive, value => customTheme.WidgetResizeGripActive = value);
        changed |= EnsureColor(customTheme.UpdateBannerBackground, value => customTheme.UpdateBannerBackground = value);
        changed |= EnsureColor(customTheme.UpdateBannerText, value => customTheme.UpdateBannerText = value);
        changed |= EnsureColor(customTheme.NoticeBorder, value => customTheme.NoticeBorder = value);
        changed |= EnsureColor(customTheme.NoticeText, value => customTheme.NoticeText = value);
        changed |= EnsureColor(customTheme.NoticeButton, value => customTheme.NoticeButton = value);
        changed |= EnsureColor(customTheme.NoticeButtonHover, value => customTheme.NoticeButtonHover = value);

        return changed;
    }

    private static uint GetEntityId(GameObjectId targetId)
    {
        if (targetId.ObjectId != 0)
        {
            return targetId.ObjectId;
        }

        return targetId.Id <= uint.MaxValue ? (uint)targetId.Id : 0;
    }

    private static string GetEffectResultDebugKey(DebugEffectResultSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.MemberKey))
        {
            return snapshot.MemberKey;
        }

        return snapshot.TargetId != 0
            ? $"target:{snapshot.TargetId:X8}"
            : $"actor:{snapshot.ActorId:X8}";
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
            EnqueueRawActionEffects(casterEntityId, header, effects, targetEntityIds);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process Better Deaths action effect.");
        }

        actionEffectHook?.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    private unsafe void OnProcessPacketEffectResult(uint targetId, IntPtr actionIntegrityData, byte isReplay)
    {
        effectResultHook?.Original(targetId, actionIntegrityData, isReplay);

        try
        {
            EnqueueRawEffectResult(targetId, actionIntegrityData, isReplay);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process Better Deaths EffectResult packet.");
        }
    }

    private void OnProcessPacketActorControl(
        uint entityId,
        uint category,
        uint param1,
        uint param2,
        uint param3,
        uint param4,
        uint param5,
        uint param6,
        uint param7,
        uint param8,
        ulong targetId,
        byte param9)
    {
        actorControlHook?.Original(entityId, category, param1, param2, param3, param4, param5, param6, param7, param8, targetId, param9);

        try
        {
            EnqueueRawActorControl(entityId, category, param1, param2, param3, param4, param5, param6, param7, param8, targetId, param9);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process Better Deaths ActorControl packet.");
        }
    }

    private void EnqueueRawActorControl(
        uint entityId,
        uint category,
        uint param1,
        uint param2,
        uint param3,
        uint param4,
        uint param5,
        uint param6,
        uint param7,
        uint param8,
        ulong targetId,
        byte param9)
    {
        var now = DateTime.UtcNow;
        if (!ShouldAcceptRawCombatCapture(now))
        {
            return;
        }

        var shouldCaptureSnapshots = category is ActorControlDeathCategory or
            ActorControlGainEffectCategory or
            ActorControlLoseEffectCategory or
            ActorControlUpdateEffectCategory or
            ActorControlTargetIconCategory or
            ActorControlHotCategory or
            ActorControlDotCategory;
        var sourceEntityId = category is ActorControlHotCategory or ActorControlDotCategory
            ? NormalizeActorEntityId(param3)
            : 0;

        EnqueueRawActorControlPacket(new RawActorControlPacket(
            GetNextRawActorControlSequence(),
            now,
            entityId,
            category,
            param1,
            param2,
            param3,
            param4,
            param5,
            param6,
            param7,
            param8,
            targetId,
            param9,
            shouldCaptureSnapshots ? CaptureRawCombatSnapshot(entityId, playerOnly: true) : null,
            sourceEntityId == 0 ? null : CaptureRawCombatSnapshot(sourceEntityId)));
    }

    private unsafe void EnqueueRawEffectResult(uint targetId, IntPtr actionIntegrityData, byte isReplay)
    {
        var now = DateTime.UtcNow;
        if (!ShouldAcceptRawCombatCapture(now) ||
            actionIntegrityData == IntPtr.Zero)
        {
            return;
        }

        var packet = (EffectResultPacket*)actionIntegrityData;
        var effectCount = Math.Min(packet->EffectCount, (byte)MaxEffectResultEntries);
        var statuses = new List<RawEffectResultStatus>(effectCount);
        var effects = (EffectResultStatusEntry*)packet->Effects;
        for (var i = 0; i < effectCount; i++)
        {
            var effect = effects[i];
            if (effect.EffectId == 0)
            {
                continue;
            }

            statuses.Add(new RawEffectResultStatus(
                effect.EffectIndex,
                effect.EffectId,
                effect.StackCount,
                effect.Duration,
                effect.SourceActorId));
        }

        EnqueueRawEffectResultPacket(new RawEffectResultPacket(
            GetNextRawEffectResultSequence(),
            now,
            targetId,
            packet->RelatedActionSequence,
            packet->ActorId,
            packet->CurrentHp,
            packet->MaxHp,
            packet->CurrentMp,
            packet->DamageShield,
            packet->EffectCount,
            isReplay,
            statuses));
    }

    private unsafe void EnqueueRawActionEffects(
        uint casterEntityId,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        var now = DateTime.UtcNow;
        if (!ShouldAcceptRawCombatCapture(now) ||
            header is null ||
            effects is null ||
            targetEntityIds is null ||
            header->NumTargets == 0)
        {
            return;
        }

        var sourceSnapshot = CaptureRawCombatSnapshot(casterEntityId);
        var targetCount = Math.Min((int)header->NumTargets, MaxActionEffectTargets);
        var targets = new List<RawActionEffectTarget>(targetCount);
        for (var targetIndex = 0; targetIndex < targetCount; targetIndex++)
        {
            var rawEffects = new List<RawActionEffectSlot>(8);
            for (var effectIndex = 0; effectIndex < 8; effectIndex++)
            {
                ref var effect = ref effects[targetIndex].Effects[effectIndex];
                if (effect.Type == (byte)ActionEffectKind.Nothing)
                {
                    continue;
                }

                rawEffects.Add(new RawActionEffectSlot(
                    effectIndex,
                    (byte)effect.Type,
                    (uint)effect.Param0,
                    (uint)effect.Param1,
                    (uint)effect.Param3,
                    (uint)effect.Param4,
                    (uint)effect.Value));
            }

            if (rawEffects.Count == 0)
            {
                continue;
            }

            var targetId = targetEntityIds[targetIndex];
            targets.Add(new RawActionEffectTarget(
                targetIndex,
                new RawTargetId(targetId.Id, targetId.ObjectId),
                CaptureRawCombatSnapshot(targetId, playerOnly: true),
                rawEffects));
        }

        if (targets.Count == 0)
        {
            return;
        }

        EnqueueRawActionEffectPacket(new RawActionEffectPacket(
            GetNextRawActionEffectSequence(),
            now,
            header->GlobalSequence,
            casterEntityId,
            GetEntityDisplayName(casterEntityId),
            header->ActionId,
            sourceSnapshot,
            targets));
    }

    private long GetNextRawActionEffectSequence()
    {
        lock (rawCombatQueueLock)
        {
            return nextRawActionEffectSequence++;
        }
    }

    private long GetNextRawCombatLogSequence()
    {
        lock (rawCombatQueueLock)
        {
            return nextRawCombatLogSequence++;
        }
    }

    private long GetNextRawEffectResultSequence()
    {
        lock (rawCombatQueueLock)
        {
            return nextRawEffectResultSequence++;
        }
    }

    private long GetNextRawActorControlSequence()
    {
        lock (rawCombatQueueLock)
        {
            return nextRawActorControlSequence++;
        }
    }

    private uint GetNextResolvedCombatEventOrdinal()
    {
        return nextResolvedCombatEventOrdinal > uint.MaxValue
            ? uint.MaxValue
            : (uint)nextResolvedCombatEventOrdinal++;
    }

    private void EnqueueRawActionEffectPacket(RawActionEffectPacket packet)
    {
        lock (rawCombatQueueLock)
        {
            rawActionEffectPackets.Enqueue(packet);
            while (rawActionEffectPackets.Count > MaxRawActionEffectPackets)
            {
                rawActionEffectPackets.Dequeue();
            }
        }
    }

    private void EnqueueRawCombatLogMessage(RawCombatLogMessage message)
    {
        lock (rawCombatQueueLock)
        {
            rawCombatLogMessages.Enqueue(message);
            while (rawCombatLogMessages.Count > MaxRawCombatLogMessages)
            {
                rawCombatLogMessages.Dequeue();
            }
        }
    }

    private void EnqueueRawEffectResultPacket(RawEffectResultPacket packet)
    {
        lock (rawCombatQueueLock)
        {
            rawEffectResultPackets.Enqueue(packet);
            while (rawEffectResultPackets.Count > MaxRawEffectResultPackets)
            {
                rawEffectResultPackets.Dequeue();
            }
        }
    }

    private void EnqueueRawActorControlPacket(RawActorControlPacket packet)
    {
        lock (rawCombatQueueLock)
        {
            rawActorControlPackets.Enqueue(packet);
            while (rawActorControlPackets.Count > MaxRawActorControlPackets)
            {
                rawActorControlPackets.Dequeue();
            }
        }
    }

    private void ResolveRawCombatQueues(DateTime now)
    {
        var actionPackets = DrainRawActionEffectPackets(now);
        actionPackets.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        foreach (var packet in actionPackets)
        {
            ResolveRawActionEffectPacket(packet);
        }

        var combatLogMessages = DrainRawCombatLogMessages(now);
        combatLogMessages.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        foreach (var message in combatLogMessages)
        {
            ResolveRawCombatLogMessage(message);
        }

        var effectResultPackets = DrainRawEffectResultPackets(now);
        effectResultPackets.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        foreach (var packet in effectResultPackets)
        {
            ResolveRawEffectResultPacket(packet);
        }

        var actorControlPackets = DrainRawActorControlPackets(now);
        actorControlPackets.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        foreach (var packet in actorControlPackets)
        {
            ResolveRawActorControlPacket(packet);
        }
    }

    private List<RawActionEffectPacket> DrainRawActionEffectPackets(DateTime now)
    {
        lock (rawCombatQueueLock)
        {
            var cutoff = now - TimeSpan.FromSeconds(RawActionEffectRetentionSeconds);
            while (rawActionEffectPackets.Count > 0 && rawActionEffectPackets.Peek().SeenAtUtc < cutoff)
            {
                rawActionEffectPackets.Dequeue();
            }

            if (rawActionEffectPackets.Count == 0)
            {
                return [];
            }

            var packets = rawActionEffectPackets.ToList();
            rawActionEffectPackets.Clear();
            return packets;
        }
    }

    private List<RawCombatLogMessage> DrainRawCombatLogMessages(DateTime now)
    {
        lock (rawCombatQueueLock)
        {
            var cutoff = now - TimeSpan.FromSeconds(RawCombatLogRetentionSeconds);
            while (rawCombatLogMessages.Count > 0 && rawCombatLogMessages.Peek().SeenAtUtc < cutoff)
            {
                rawCombatLogMessages.Dequeue();
            }

            if (rawCombatLogMessages.Count == 0)
            {
                return [];
            }

            var messages = rawCombatLogMessages.ToList();
            rawCombatLogMessages.Clear();
            return messages;
        }
    }

    private List<RawEffectResultPacket> DrainRawEffectResultPackets(DateTime now)
    {
        lock (rawCombatQueueLock)
        {
            var cutoff = now - TimeSpan.FromSeconds(RawCombatLogRetentionSeconds);
            while (rawEffectResultPackets.Count > 0 && rawEffectResultPackets.Peek().SeenAtUtc < cutoff)
            {
                rawEffectResultPackets.Dequeue();
            }

            if (rawEffectResultPackets.Count == 0)
            {
                return [];
            }

            var packets = rawEffectResultPackets.ToList();
            rawEffectResultPackets.Clear();
            return packets;
        }
    }

    private List<RawActorControlPacket> DrainRawActorControlPackets(DateTime now)
    {
        lock (rawCombatQueueLock)
        {
            var cutoff = now - TimeSpan.FromSeconds(RawCombatLogRetentionSeconds);
            while (rawActorControlPackets.Count > 0 && rawActorControlPackets.Peek().SeenAtUtc < cutoff)
            {
                rawActorControlPackets.Dequeue();
            }

            if (rawActorControlPackets.Count == 0)
            {
                return [];
            }

            var packets = rawActorControlPackets.ToList();
            rawActorControlPackets.Clear();
            return packets;
        }
    }

    private void ResolveRawActionEffectPacket(RawActionEffectPacket packet)
    {
        TrackPossibleMitigationActionUse(packet);
        CaptureReplayBlackHoleBlast(packet);

        string? actionName = null;
        uint? actionIconId = null;
        string? sourceName = null;
        IReadOnlyList<StatusSnapshot>? sourceStatuses = null;
        var foundRelevantEffect = false;
        var trackedSourceStatuses = false;

        foreach (var target in packet.Targets)
        {
            var member = FindCurrentMemberByTargetId(target.TargetId);
            if (member is null)
            {
                continue;
            }

            var targetSnapshot = target.TargetSnapshot;
            var directTargetStatuses = BuildStatusSnapshots(targetSnapshot);
            var hasDirectTargetHp = targetSnapshot is { MaxHp: > 0 } &&
                (targetSnapshot.CurrentHp > 0 || targetSnapshot.ShieldHp > 0);
            var priorHp = hasDirectTargetHp
                ? null
                : GetLatestPriorHpStateSnapshot(member.MemberKey, packet.SeenAtUtc, TimeSpan.FromMilliseconds(1500));
            var hpSource = hasDirectTargetHp
                ? CombatEventHpSource.DirectCombatEventSnapshot
                : priorHp is null
                    ? CombatEventHpSource.NoPreHitSample
                    : CombatEventHpSource.LatestPriorSample;
            var statusSource = target.TargetSnapshot is not null
                ? directTargetStatuses
                : priorHp is null
                    ? member.Statuses
                    : DeduplicateStatusSnapshots(member.Statuses.Concat(priorHp.Statuses));
            var playerStatuses = GetRelevantDeathStatuses(statusSource);
            var eventCurrentHp = hasDirectTargetHp ? targetSnapshot!.CurrentHp : priorHp?.CurrentHp ?? 0;
            var eventShieldHp = hasDirectTargetHp ? targetSnapshot!.ShieldHp : priorHp?.ShieldHp ?? 0;
            var eventMaxHp = hasDirectTargetHp ? targetSnapshot!.MaxHp : priorHp?.MaxHp ?? member.MaxHp;

            foreach (var effect in target.Effects)
            {
                var kind = GetEventKind((ActionEffectKind)effect.Type);
                if (kind is null)
                {
                    continue;
                }

                if (kind.Value == DeathEventKind.Heal &&
                    pullStartedAtUtc is null &&
                    !ShouldCaptureLiveCombat(DateTime.UtcNow))
                {
                    continue;
                }

                foundRelevantEffect = true;
                EnsurePullStarted(packet.SeenAtUtc);
                var resolvedActionName = actionName ??= GetActionName(packet.ActionId);
                var resolvedActionIconId = actionIconId ??= GetActionIconId(packet.ActionId);
                var resolvedSourceName = sourceName ??= string.IsNullOrWhiteSpace(packet.CasterName)
                    ? GetEntityDisplayName(packet.CasterEntityId)
                    : packet.CasterName;
                var shouldTrackSourceStatuses = kind.Value != DeathEventKind.Heal;
                var resolvedSourceStatuses = shouldTrackSourceStatuses
                    ? sourceStatuses ??= GetBossMitigationStatuses(packet.SourceSnapshot is null
                        ? BuildSourceStatusSnapshots(packet.CasterEntityId)
                        : BuildStatusSnapshots(packet.SourceSnapshot))
                    : [];
                if (shouldTrackSourceStatuses && !trackedSourceStatuses)
                {
                    TrackRecentSourceMitigationSnapshot(packet.CasterEntityId, resolvedSourceName, packet.SeenAtUtc, resolvedSourceStatuses);
                    trackedSourceStatuses = true;
                }

                var amount = CalculateRawActionEffectAmount(effect);

                var eventOrdinal = GetNextResolvedCombatEventOrdinal();
                var record = new CombatEventRecord(
                    packet.SeenAtUtc,
                    CalculatePullElapsed(packet.SeenAtUtc),
                    member.MemberKey,
                    member.MemberName,
                    member.PartyIndex,
                    packet.CasterEntityId,
                    resolvedSourceName,
                    packet.ActionId,
                    resolvedActionName,
                    resolvedActionIconId,
                    kind.Value,
                    amount,
                    eventCurrentHp,
                    eventShieldHp,
                    eventMaxHp,
                    (DamageType)(effect.Param1 & 0xF),
                    (effect.Param0 & 0x20) == 0x20,
                    (effect.Param0 & 0x40) == 0x40,
                    effect.Type == (byte)ActionEffectKind.BlockedDamage,
                    effect.Type == (byte)ActionEffectKind.ParriedDamage,
                    BuildEffectDetail((ActionEffectKind)effect.Type),
                    playerStatuses,
                    resolvedSourceStatuses)
                {
                    EventIdentity = $"{packet.Sequence}:{target.TargetIndex}:{effect.EffectIndex}:{member.MemberKey}:{packet.ActionId}",
                    EventOrdinal = eventOrdinal,
                    ActionSequence = packet.ActionSequence,
                    HpSource = hpSource,
                };
                record = AttachPendingEffectResult(record);
                AddRecentEvent(record);
                BackfillCombatEventToCapturedDeaths(record);
                QueueDebugCaptureRecord("ActionEffect", CreateDebugActionEffectRecord(record));
            }
        }

        if (foundRelevantEffect)
        {
            AddDebugLog($"Captured {actionName} ({packet.ActionId}).");
        }
    }

    private void CaptureReplayBlackHoleBlast(RawActionEffectPacket packet)
    {
        if (!IsDmuReplayCaptureContext() ||
            packet.ActionId != DmuBlackHoleNothingnessActionId ||
            !IsReplayBlackHoleObject(packet.CasterEntityId, packet.CasterName))
        {
            return;
        }

        foreach (var target in packet.Targets)
        {
            var member = FindCurrentMemberByTargetId(target.TargetId);
            if (member is null)
            {
                continue;
            }

            foreach (var effect in target.Effects)
            {
                if (GetEventKind((ActionEffectKind)effect.Type) != DeathEventKind.Damage)
                {
                    continue;
                }

                var amount = CalculateRawActionEffectAmount(effect);
                if (amount == 0)
                {
                    continue;
                }

                AddRecentReplayMechanicSnapshot(new ReplayMechanicSnapshot(
                    packet.SeenAtUtc,
                    CalculatePullElapsed(packet.SeenAtUtc),
                    1.4f,
                    $"black-hole-blast:{packet.CasterEntityId:X8}:{member.MemberKey}:{packet.Sequence}:{target.TargetIndex}",
                    $"Black Hole -> {member.MemberName}",
                    ReplayMechanicShape.Circle,
                    member.Position.X,
                    member.Position.Y,
                    member.Position.Z,
                    member.Rotation,
                    4.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Blast",
                    "black-hole-blast",
                    packet.ActionId,
                    amount,
                    true));
                break;
            }
        }
    }

    private static uint CalculateRawActionEffectAmount(RawActionEffectSlot effect)
    {
        var amount = effect.Value;
        if ((effect.Param4 & 0x40) == 0x40)
        {
            amount += effect.Param3 << 16;
        }

        return amount;
    }

    private void ResolveRawCombatLogMessage(RawCombatLogMessage message)
    {
        if (message.SourceIsPlayer || !message.TargetIsPlayer)
        {
            return;
        }

        var member = FindCurrentMemberByName(message.TargetName);
        if (member is null)
        {
            return;
        }

        EnsurePullStarted(message.SeenAtUtc);
        var record = new CombatLogEventRecord(
            message.SeenAtUtc,
            CalculatePullElapsed(message.SeenAtUtc),
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            message.SourceName,
            message.TargetName,
            message.LogMessageId,
            message.ActionName,
            message.Amount);
        AddRecentCombatLogEvent(record);
    }

    private void ResolveRawEffectResultPacket(RawEffectResultPacket packet)
    {
        var member = FindCurrentMemberByEffectResultPacket(packet);
        var shouldRecordDebug = Configuration.DebugLogEnabled &&
            !debugCaptureFrozen &&
            (member is not null || Configuration.CaptureOtherDeaths);
        if (member is null && !shouldRecordDebug)
        {
            return;
        }

        var packetStatuses = BuildEffectResultStatusSnapshots(packet);
        var mergedStatuses = member is null
            ? packetStatuses
            : DeduplicateStatusSnapshots(member.Statuses.Concat(packetStatuses));
        var shieldHp = CalculateShieldHpFromPercent(packet.MaxHp, packet.ShieldPercent);

        if (member is not null)
        {
            var resultStatuses = GetRelevantDeathStatuses(mergedStatuses);
            StorePendingEffectResult(member.MemberKey, packet, shieldHp, resultStatuses);
            AttachEffectResultToCombatEvents(member, packet, shieldHp, mergedStatuses);
            RemoveIntermediateEffectResultHpHistorySnapshots(member.MemberKey, packet, shieldHp);
            CaptureEffectResultHpSnapshot(member, packet, shieldHp, mergedStatuses);
            if (packet.MaxHp > 0 && packet.CurrentHp == 0)
            {
                TryCaptureDeath(
                    member with
                    {
                        CurrentHp = 0,
                        ShieldHp = 0,
                        MaxHp = packet.MaxHp,
                        IsDead = true,
                        Statuses = mergedStatuses,
                    },
                    packet.SeenAtUtc,
                    "EffectResult");
            }
        }

        if (!shouldRecordDebug)
        {
            return;
        }

        var targetName = member?.MemberName ??
            GetEntityDisplayName(packet.TargetId != 0 ? packet.TargetId : packet.ActorId);
        var statuses = BuildDebugEffectResultStatuses(packet);

        var snapshot = new DebugEffectResultSnapshot(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            packet.TargetId,
            targetName,
            member?.MemberKey,
            packet.ActorId,
            packet.CurrentHp,
            packet.MaxHp,
            packet.CurrentMp,
            packet.ShieldPercent,
            shieldHp,
            packet.EffectCount,
            packet.RelatedActionSequence,
            packet.IsReplay != 0,
            statuses);
        debugEffectResultSnapshotsByTarget[GetEffectResultDebugKey(snapshot)] = snapshot;
        debugEffectResultHistory.Add(snapshot);
        while (debugEffectResultHistory.Count > MaxDebugEffectResultEvents)
        {
            debugEffectResultHistory.RemoveAt(0);
        }

        QueueDebugCaptureRecord("EffectResult", snapshot);
        AddDebugLog(
            $"EffectResult {targetName}: HP {packet.CurrentHp:N0}/{packet.MaxHp:N0}, shield {packet.ShieldPercent:N0}% ({shieldHp:N0}), effects {statuses.Count:N0}/{packet.EffectCount:N0}, seq {packet.RelatedActionSequence}.");
    }

    private void StorePendingEffectResult(
        string memberKey,
        RawEffectResultPacket packet,
        uint shieldHp,
        IReadOnlyList<StatusSnapshot> resultStatuses)
    {
        if (packet.RelatedActionSequence == 0 || packet.MaxHp == 0)
        {
            return;
        }

        pendingEffectResultsByMemberSequence[(memberKey, packet.RelatedActionSequence)] =
            new PendingEffectResult(packet, shieldHp, resultStatuses);
    }

    private CombatEventRecord AttachPendingEffectResult(CombatEventRecord record)
    {
        if (record.ActionSequence == 0 ||
            !pendingEffectResultsByMemberSequence.TryGetValue((record.MemberKey, record.ActionSequence), out var pendingResult) ||
            !TryAttachEffectResult(
                record,
                pendingResult.Packet,
                pendingResult.ShieldHp,
                pendingResult.ResultStatuses,
                out var updatedRecord))
        {
            return record;
        }

        return updatedRecord;
    }

    private void AttachEffectResultToCombatEvents(
        PartyMemberSnapshot member,
        RawEffectResultPacket packet,
        uint shieldHp,
        IReadOnlyList<StatusSnapshot> mergedStatuses)
    {
        if (packet.RelatedActionSequence == 0 || packet.MaxHp == 0)
        {
            return;
        }

        var resultStatuses = GetRelevantDeathStatuses(mergedStatuses);
        var attachedCount = 0;
        if (recentEventsByMember.TryGetValue(member.MemberKey, out var events))
        {
            for (var index = 0; index < events.Count; index++)
            {
                if (TryAttachEffectResult(events[index], packet, shieldHp, resultStatuses, out var updatedEvent))
                {
                    events[index] = updatedEvent;
                    attachedCount++;
                }
            }
        }

        attachedCount += AttachEffectResultToDeathList(currentDeaths, member.MemberKey, packet, shieldHp, resultStatuses);
        attachedCount += AttachEffectResultToDeathList(pendingDeathRecapLinks, member.MemberKey, packet, shieldHp, resultStatuses);
        if (attachedCount > 0 && Configuration.DebugLogEnabled)
        {
            AddDebugLog(
                $"Linked EffectResult seq {packet.RelatedActionSequence} to {attachedCount:N0} combat event(s).");
        }
    }

    private static int AttachEffectResultToDeathList(
        List<PartyDeathRecord> deaths,
        string memberKey,
        RawEffectResultPacket packet,
        uint shieldHp,
        IReadOnlyList<StatusSnapshot> resultStatuses)
    {
        var attachedCount = 0;
        for (var index = 0; index < deaths.Count; index++)
        {
            var death = deaths[index];
            if (!string.Equals(death.MemberKey, memberKey, StringComparison.Ordinal))
            {
                continue;
            }

            var likelyCause = death.LikelyCause;
            if (likelyCause is not null &&
                TryAttachEffectResult(likelyCause, packet, shieldHp, resultStatuses, out var updatedLikelyCause))
            {
                likelyCause = updatedLikelyCause;
                attachedCount++;
            }

            var recentEvents = AttachEffectResultToEventList(
                death.RecentEvents,
                packet,
                shieldHp,
                resultStatuses,
                out var recentAttachedCount);
            attachedCount += recentAttachedCount;

            var fatalSequence = death.FatalSequence;
            if (fatalSequence is not null)
            {
                var fatalEvents = AttachEffectResultToEventList(
                    fatalSequence.Events,
                    packet,
                    shieldHp,
                    resultStatuses,
                    out var fatalAttachedCount);
                if (fatalAttachedCount > 0)
                {
                    fatalSequence = fatalSequence with { Events = fatalEvents };
                    attachedCount += fatalAttachedCount;
                }
            }

            if (likelyCause != death.LikelyCause ||
                !ReferenceEquals(recentEvents, death.RecentEvents) ||
                fatalSequence != death.FatalSequence)
            {
                deaths[index] = death with
                {
                    LikelyCause = likelyCause,
                    RecentEvents = recentEvents,
                    FatalSequence = fatalSequence,
                };
            }
        }

        return attachedCount;
    }

    private static IReadOnlyList<CombatEventRecord> AttachEffectResultToEventList(
        IReadOnlyList<CombatEventRecord> events,
        RawEffectResultPacket packet,
        uint shieldHp,
        IReadOnlyList<StatusSnapshot> resultStatuses,
        out int attachedCount)
    {
        attachedCount = 0;
        List<CombatEventRecord>? updatedEvents = null;
        for (var index = 0; index < events.Count; index++)
        {
            var combatEvent = events[index];
            if (!TryAttachEffectResult(combatEvent, packet, shieldHp, resultStatuses, out var updatedEvent))
            {
                updatedEvents?.Add(combatEvent);
                continue;
            }

            updatedEvents ??= events.Take(index).ToList();
            updatedEvents.Add(updatedEvent);
            attachedCount++;
        }

        return updatedEvents ?? events;
    }

    private static bool TryAttachEffectResult(
        CombatEventRecord combatEvent,
        RawEffectResultPacket packet,
        uint shieldHp,
        IReadOnlyList<StatusSnapshot> resultStatuses,
        out CombatEventRecord updatedEvent)
    {
        updatedEvent = combatEvent;
        if (!EffectResultMatchesCombatEvent(combatEvent, packet))
        {
            return false;
        }

        if (combatEvent.ResultSeenAtUtc is { } existingResultAt &&
            Duration(combatEvent.SeenAtUtc, existingResultAt) <= Duration(combatEvent.SeenAtUtc, packet.SeenAtUtc))
        {
            return false;
        }

        updatedEvent = combatEvent with
        {
            ResultSeenAtUtc = packet.SeenAtUtc,
            ResultCurrentHp = packet.CurrentHp,
            ResultShieldHp = shieldHp,
            ResultMaxHp = packet.MaxHp,
            ResultStatuses = resultStatuses,
        };
        return true;
    }

    private static bool EffectResultMatchesCombatEvent(CombatEventRecord combatEvent, RawEffectResultPacket packet)
    {
        return packet.RelatedActionSequence != 0 &&
            combatEvent.ActionSequence != 0 &&
            combatEvent.ActionSequence == packet.RelatedActionSequence &&
            Duration(combatEvent.SeenAtUtc, packet.SeenAtUtc) <= EffectResultActionMatchWindow;
    }

    private void ResolveRawActorControlPacket(RawActorControlPacket packet)
    {
        var member = FindCurrentMemberByEntityId(packet.EntityId);
        if (packet.Category == ActorControlTargetIconCategory)
        {
            CaptureActorControlTargetIcon(packet, member);
        }

        if (member is not null)
        {
            CaptureActorControlStatusChange(packet, member);
            CaptureActorControlHotEvent(packet, member);
            CaptureActorControlDotEvent(packet, member);
        }

        if (packet.Category == ActorControlDeathCategory && member is not null)
        {
            var bestKnownStatuses = GetBestKnownStatuses(member, packet.SeenAtUtc);
            TryCaptureDeath(
                member with
                {
                    CurrentHp = 0,
                    ShieldHp = 0,
                    IsDead = true,
                    Statuses = bestKnownStatuses,
                },
                packet.SeenAtUtc,
                "ActorControl");
        }

        if (!Configuration.DebugLogEnabled || debugCaptureFrozen)
        {
            return;
        }

        if (member is null && !Configuration.CaptureOtherDeaths)
        {
            return;
        }

        var entityName = member?.MemberName ?? GetEntityDisplayName(packet.EntityId);
        var targetName = GetActorControlTargetName(packet.TargetId);
        var categoryName = GetActorControlCategoryName(packet.Category);
        var debugEvent = new DebugActorControlEvent(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            packet.EntityId,
            entityName,
            packet.Category,
            categoryName,
            packet.Param1,
            packet.Param2,
            packet.Param3,
            packet.Param4,
            packet.Param5,
            packet.Param6,
            packet.Param7,
            packet.Param8,
            packet.TargetId,
            targetName,
            packet.Param9);

        debugActorControlEvents.Add(debugEvent);
        while (debugActorControlEvents.Count > MaxDebugActorControlEvents)
        {
            debugActorControlEvents.RemoveAt(0);
        }

        QueueDebugCaptureRecord("ActorControl", debugEvent);
        AddDebugLog(
            $"ActorControl {categoryName} for {entityName}: p1 {packet.Param1}, p2 {packet.Param2}, target {FormatDebugActorControlTarget(packet.TargetId)}.");
        if (debugFreezeOnDeathEnabled && packet.Category == ActorControlDeathCategory)
        {
            SetDebugCaptureFrozen(true);
        }
    }

    private PartyMemberSnapshot? FindCurrentMemberByTargetId(GameObjectId targetId)
    {
        return currentMembers.FirstOrDefault(member => TargetMatchesMember(targetId, member));
    }

    private PartyMemberSnapshot? FindCurrentMemberByTargetId(RawTargetId targetId)
    {
        return currentMembers.FirstOrDefault(member => TargetMatchesMember(targetId, member));
    }

    private PartyMemberSnapshot? FindCurrentMemberByName(string memberName)
    {
        return currentMembers.FirstOrDefault(member =>
            string.Equals(member.MemberName, memberName, StringComparison.OrdinalIgnoreCase));
    }

    private PartyMemberSnapshot? FindCurrentMemberByEntityId(uint entityId)
    {
        return entityId == 0
            ? null
            : currentMembers.FirstOrDefault(member => member.EntityId != 0 && member.EntityId == entityId);
    }

    private PartyMemberSnapshot? FindCurrentMemberByEffectResultPacket(RawEffectResultPacket packet)
    {
        return currentMembers.FirstOrDefault(member =>
            member.EntityId != 0 &&
            (member.EntityId == packet.TargetId || member.EntityId == packet.ActorId));
    }

    private void CaptureActorControlTargetIcon(RawActorControlPacket packet, PartyMemberSnapshot? member)
    {
        var markerId = NormalizeTargetIconId(packet.Param1);
        if (markerId == 0)
        {
            return;
        }

        var marker = CreateReplayMarkerSnapshot(packet, markerId, member);
        if (marker is null)
        {
            return;
        }

        AddRecentReplayMarkerSnapshot(marker);
    }

    private ReplayMarkerSnapshot? CreateReplayMarkerSnapshot(
        RawActorControlPacket packet,
        uint markerId,
        PartyMemberSnapshot? member)
    {
        var entityId = NormalizeActorEntityId(packet.EntityId);
        if (entityId == 0)
        {
            return null;
        }

        if (member is not null)
        {
            return new ReplayMarkerSnapshot(
                packet.SeenAtUtc,
                CalculatePullElapsed(packet.SeenAtUtc),
                $"player:{member.MemberKey}",
                member.MemberName,
                ReplayActorKind.Player,
                member.PartyIndex,
                member.EntityId,
                member.ClassJobId,
                member.ClassJobName,
                markerId,
                packet.Param1);
        }

        try
        {
            var gameObject = ObjectTable.SearchByEntityId(entityId);
            if (gameObject is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
            {
                var classJobId = player.ClassJob.RowId;
                return new ReplayMarkerSnapshot(
                    packet.SeenAtUtc,
                    CalculatePullElapsed(packet.SeenAtUtc),
                    $"player:entity:{player.EntityId:X8}",
                    player.Name.TextValue,
                    ReplayActorKind.Player,
                    1000 + player.ObjectIndex,
                    player.EntityId,
                    classJobId,
                    GetClassJobName(classJobId),
                    markerId,
                    packet.Param1);
            }

            if (gameObject is Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc)
            {
                return new ReplayMarkerSnapshot(
                    packet.SeenAtUtc,
                    CalculatePullElapsed(packet.SeenAtUtc),
                    $"enemy:{battleNpc.EntityId:X8}",
                    battleNpc.Name.TextValue,
                    ReplayActorKind.Enemy,
                    2000 + battleNpc.ObjectIndex,
                    battleNpc.EntityId,
                    0,
                    string.Empty,
                    markerId,
                    packet.Param1);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture Better Deaths replay marker for {EntityId:X8}.", entityId);
        }

        return null;
    }

    private void AddRecentReplayMarkerSnapshot(ReplayMarkerSnapshot snapshot)
    {
        if (!recentReplayMarkersByActor.TryGetValue(snapshot.ActorKey, out var history))
        {
            history = [];
            recentReplayMarkersByActor[snapshot.ActorKey] = history;
        }

        var last = history.Count == 0 ? null : history[^1];
        if (last is not null &&
            last.MarkerId == snapshot.MarkerId &&
            Duration(last.SeenAtUtc, snapshot.SeenAtUtc) <= TimeSpan.FromMilliseconds(50))
        {
            return;
        }

        history.Add(snapshot);
        while (history.Count > MaxRecentReplayMarkersPerActor)
        {
            history.RemoveAt(0);
        }
    }

    private static uint NormalizeTargetIconId(uint markerId)
    {
        return markerId;
    }

    private HpHistorySnapshot? GetLatestPriorHpSnapshot(string memberKey, DateTime seenAtUtc, TimeSpan maxAge)
    {
        if (!recentHpHistoryByMember.TryGetValue(memberKey, out var history))
        {
            return null;
        }

        HpHistorySnapshot? latest = null;
        for (var index = history.Count - 1; index >= 0; index--)
        {
            var snapshot = history[index];
            if (snapshot.SeenAtUtc > seenAtUtc ||
                seenAtUtc - snapshot.SeenAtUtc > maxAge ||
                snapshot.CurrentHp == 0 && snapshot.ShieldHp == 0)
            {
                continue;
            }

            if (latest is null || snapshot.SeenAtUtc > latest.SeenAtUtc)
            {
                latest = snapshot;
            }
        }

        return latest;
    }

    private HpHistorySnapshot? GetLatestPriorHpStateSnapshot(string memberKey, DateTime seenAtUtc, TimeSpan maxAge)
    {
        var latest = GetLatestPriorHpSnapshot(memberKey, seenAtUtc, maxAge);
        if (!recentStatusesByMember.TryGetValue(memberKey, out var observations))
        {
            return latest;
        }

        for (var index = observations.Count - 1; index >= 0; index--)
        {
            var observation = observations[index];
            if (observation.SeenAtUtc > seenAtUtc ||
                seenAtUtc - observation.SeenAtUtc > maxAge ||
                observation.CurrentHp == 0 && observation.ShieldHp == 0)
            {
                continue;
            }

            if (latest is not null && observation.SeenAtUtc <= latest.SeenAtUtc)
            {
                continue;
            }

            var candidate = new HpHistorySnapshot(
                observation.SeenAtUtc,
                observation.PullElapsedSeconds,
                observation.CurrentHp,
                observation.ShieldHp,
                observation.MaxHp,
                GetRelevantDeathStatuses(observation.Statuses));
            if (ShouldSkipIntermediateEffectResultHpStateSnapshot(memberKey, candidate))
            {
                continue;
            }

            latest = candidate;
        }

        return latest;
    }

    private bool ShouldSkipIntermediateEffectResultHpStateSnapshot(string memberKey, HpHistorySnapshot snapshot)
    {
        foreach (var combatEvent in GetRecentEffectResultEvents(memberKey))
        {
            if (IsIntermediateEffectResultHpHistorySnapshot(snapshot, combatEvent))
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<StatusSnapshot> GetBestKnownStatuses(PartyMemberSnapshot member, DateTime seenAtUtc)
    {
        var priorHp = GetLatestPriorHpStateSnapshot(member.MemberKey, seenAtUtc, TimeSpan.FromMilliseconds(1500));
        return priorHp is null
            ? member.Statuses
            : DeduplicateStatusSnapshots(member.Statuses.Concat(priorHp.Statuses));
    }

    private void CaptureActorControlHotEvent(RawActorControlPacket packet, PartyMemberSnapshot member)
    {
        if (packet.Category != ActorControlHotCategory ||
            packet.Param2 == 0)
        {
            return;
        }

        if (pullStartedAtUtc is null &&
            !ShouldCaptureLiveCombat(DateTime.UtcNow))
        {
            return;
        }

        EnsurePullStarted(packet.SeenAtUtc);
        var targetSnapshot = packet.TargetSnapshot;
        var directTargetStatuses = BuildStatusSnapshots(targetSnapshot);
        var priorHp = GetLatestPriorHpStateSnapshot(member.MemberKey, packet.SeenAtUtc, TimeSpan.FromMilliseconds(1500));
        var hpSource = priorHp is null
            ? CombatEventHpSource.NoPreHitSample
            : CombatEventHpSource.LatestPriorSample;
        var sourceEntityId = NormalizeActorEntityId(packet.Param3);
        if (sourceEntityId == member.EntityId)
        {
            sourceEntityId = 0;
        }

        var sourceName = sourceEntityId == 0
            ? "Healing over time"
            : GetEntityDisplayName(sourceEntityId);
        var actionId = packet.Param1 != 0 ? packet.Param1 : ActorControlHotCategory;
        var actionName = packet.Param1 != 0
            ? GetStatusName(packet.Param1)
            : "HoT tick";
        var actionIconId = packet.Param1 != 0 ? GetStatusIconId(packet.Param1) : 0;
        var statusSource = packet.TargetSnapshot is not null
            ? directTargetStatuses
            : priorHp is null
                ? GetBestKnownStatuses(member, packet.SeenAtUtc)
                : DeduplicateStatusSnapshots(member.Statuses.Concat(priorHp.Statuses));
        var eventOrdinal = GetNextResolvedCombatEventOrdinal();
        var record = new CombatEventRecord(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            sourceEntityId,
            sourceName,
            actionId,
            actionName,
            actionIconId,
            DeathEventKind.Heal,
            packet.Param2,
            priorHp?.CurrentHp ?? 0,
            priorHp?.ShieldHp ?? 0,
            priorHp?.MaxHp ?? member.MaxHp,
            DamageType.Unknown,
            false,
            false,
            false,
            false,
            "Periodic healing tick.",
            GetRelevantDeathStatuses(statusSource),
            [])
        {
            EventIdentity = $"actor:{packet.Sequence}:hot:{member.MemberKey}:{packet.Param1}:{packet.Param2}",
            EventOrdinal = eventOrdinal,
            HpSource = hpSource,
        };
        AddRecentEvent(record);
        BackfillCombatEventToCapturedDeaths(record);
        QueueDebugCaptureRecord("ActionEffect", CreateDebugActionEffectRecord(record));
    }

    private void CaptureActorControlDotEvent(RawActorControlPacket packet, PartyMemberSnapshot member)
    {
        if (packet.Category != ActorControlDotCategory ||
            packet.Param2 == 0)
        {
            return;
        }

        var targetSnapshot = packet.TargetSnapshot;
        var directTargetStatuses = BuildStatusSnapshots(targetSnapshot);
        var priorHp = GetLatestPriorHpStateSnapshot(member.MemberKey, packet.SeenAtUtc, TimeSpan.FromMilliseconds(1500));
        var hpSource = priorHp is null
            ? CombatEventHpSource.NoPreHitSample
            : CombatEventHpSource.LatestPriorSample;
        var sourceEntityId = NormalizeActorEntityId(packet.Param3);
        if (sourceEntityId == member.EntityId)
        {
            sourceEntityId = 0;
        }

        var sourceName = sourceEntityId == 0
            ? "Damage over time"
            : GetEntityDisplayName(sourceEntityId);
        var sourceStatuses = sourceEntityId == 0
            ? []
            : GetBossMitigationStatuses(packet.SourceSnapshot is null
                ? BuildSourceStatusSnapshots(sourceEntityId)
                : BuildStatusSnapshots(packet.SourceSnapshot));
        TrackRecentSourceMitigationSnapshot(sourceEntityId, sourceName, packet.SeenAtUtc, sourceStatuses);
        var statusSource = packet.TargetSnapshot is not null
            ? directTargetStatuses
            : priorHp is null
                ? GetBestKnownStatuses(member, packet.SeenAtUtc)
                : DeduplicateStatusSnapshots(member.Statuses.Concat(priorHp.Statuses));
        var eventOrdinal = GetNextResolvedCombatEventOrdinal();
        var record = new CombatEventRecord(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            sourceEntityId,
            sourceName,
            ActorControlDotCategory,
            "DoT tick",
            0,
            DeathEventKind.Damage,
            packet.Param2,
            priorHp?.CurrentHp ?? 0,
            priorHp?.ShieldHp ?? 0,
            priorHp?.MaxHp ?? member.MaxHp,
            DamageType.Unknown,
            false,
            false,
            false,
            false,
            "Periodic damage tick.",
            GetRelevantDeathStatuses(statusSource),
            sourceStatuses)
        {
            EventIdentity = $"actor:{packet.Sequence}:dot:{member.MemberKey}:{packet.Param2}",
            EventOrdinal = eventOrdinal,
            HpSource = hpSource,
        };
        AddRecentEvent(record);
        BackfillCombatEventToCapturedDeaths(record);
        QueueDebugCaptureRecord("ActionEffect", CreateDebugActionEffectRecord(record));
    }

    private void CaptureActorControlStatusChange(RawActorControlPacket packet, PartyMemberSnapshot member)
    {
        var statusLookup = packet.TargetSnapshot is null
            ? member.Statuses
            : BuildStatusSnapshots(packet.TargetSnapshot);
        if (!TryCreateActorControlStatusSnapshot(packet, statusLookup, out var status))
        {
            return;
        }

        CaptureReplayOverheadStatus(packet, member, status);

        if (!IsRelevantDeathStatus(status) && !IsTrackedStatusDeathCandidate(status))
        {
            return;
        }

        var statusesForSnapshot = packet.Category == ActorControlLoseEffectCategory
            ? DeduplicateStatusSnapshots(statusLookup.Where(existing => !StatusMatchesPacketStatus(existing, status)))
            : DeduplicateStatusSnapshots(statusLookup
                .Where(existing => existing.Id != status.Id || existing.SourceId != status.SourceId)
                .Append(status));
        var snapshotCurrentHp = packet.TargetSnapshot?.CurrentHp ?? member.CurrentHp;
        var snapshotShieldHp = packet.TargetSnapshot?.ShieldHp ?? member.ShieldHp;
        var snapshotMaxHp = packet.TargetSnapshot?.MaxHp ?? member.MaxHp;

        AddRecentStatusObservation(
            member.MemberKey,
            packet.SeenAtUtc,
            status,
            snapshotCurrentHp,
            snapshotShieldHp,
            snapshotMaxHp,
            statusesForSnapshot);

    }

    private void CaptureReplayOverheadStatus(RawActorControlPacket packet, PartyMemberSnapshot member, StatusSnapshot status)
    {
        if (packet.Category != ActorControlGainEffectCategory ||
            !ReplayEncounterModules.IsReplayOverheadStatus(currentPullTerritoryId == 0 ? currentTerritoryId : currentPullTerritoryId, status.Id))
        {
            return;
        }

        AddRecentReplayMarkerSnapshot(new ReplayMarkerSnapshot(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            $"player:{member.MemberKey}",
            member.MemberName,
            ReplayActorKind.Player,
            member.PartyIndex,
            member.EntityId,
            member.ClassJobId,
            member.ClassJobName,
            status.Id,
            status.Id));
    }

    private bool TryCreateActorControlStatusSnapshot(
        RawActorControlPacket packet,
        IEnumerable<StatusSnapshot> statuses,
        out StatusSnapshot status)
    {
        var statusId = packet.Category switch
        {
            ActorControlGainEffectCategory => packet.Param1,
            ActorControlLoseEffectCategory => packet.Param1,
            ActorControlUpdateEffectCategory => packet.Param2,
            _ => 0u,
        };
        if (statusId == 0 || statusId > ushort.MaxValue)
        {
            status = default!;
            return false;
        }

        var sourceId = packet.Category == ActorControlUpdateEffectCategory
            ? FindStatusSourceId(statuses, statusId)
            : NormalizeActorEntityId(packet.Param3);
        var stackCount = packet.Category == ActorControlUpdateEffectCategory
            ? ClampStatusStackCount(packet.Param3)
            : ClampStatusStackCount(packet.Param2);
        var remainingTime = packet.Category == ActorControlLoseEffectCategory
            ? 0.0f
            : FindStatusRemainingTime(statuses, statusId, sourceId);
        status = new StatusSnapshot(
            statusId,
            GetStatusName(statusId),
            GetStatusIconId(statusId),
            sourceId,
            stackCount,
            remainingTime);
        return true;
    }

    private static uint NormalizeActorEntityId(uint entityId)
    {
        return entityId is 0 or InvalidActorEntityId or uint.MaxValue ? 0 : entityId;
    }

    private static bool StatusMatchesPacketStatus(StatusSnapshot existing, StatusSnapshot packetStatus)
    {
        return existing.Id == packetStatus.Id &&
            (packetStatus.SourceId == 0 || existing.SourceId == packetStatus.SourceId);
    }

    private static ushort ClampStatusStackCount(uint value)
    {
        return value > ushort.MaxValue ? ushort.MaxValue : (ushort)value;
    }

    private static uint FindStatusSourceId(IEnumerable<StatusSnapshot> statuses, uint statusId)
    {
        return statuses
            .Where(status => status.Id == statusId)
            .Select(status => status.SourceId)
            .FirstOrDefault();
    }

    private static float FindStatusRemainingTime(IEnumerable<StatusSnapshot> statuses, uint statusId, uint sourceId)
    {
        var status = statuses
            .Where(status => status.Id == statusId)
            .OrderByDescending(status => status.SourceId == sourceId)
            .ThenBy(status => status.RemainingTime <= 0.0f ? float.MaxValue : status.RemainingTime)
            .FirstOrDefault();
        return status?.RemainingTime ?? 0.0f;
    }

    private static string GetEntityDisplayName(uint entityId)
    {
        if (entityId == 0)
        {
            return "Unknown source";
        }

        try
        {
            var gameObject = ObjectTable.SearchByEntityId(entityId);
            var name = gameObject?.Name.TextValue;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not resolve Better Deaths source name for {EntityId:X8}.", entityId);
        }

        return $"Entity {entityId:X8}";
    }

    private static string GetActorControlTargetName(ulong targetId)
    {
        if (targetId == 0)
        {
            return "Unknown target";
        }

        var lowerTargetId = (uint)(targetId & uint.MaxValue);
        return lowerTargetId == 0
            ? $"Target 0x{targetId:X16}"
            : GetEntityDisplayName(lowerTargetId);
    }

    private static string GetActorControlCategoryName(uint category)
    {
        return category switch
        {
            0x6 => "Death",
            0xF => "CancelAbility",
            0x14 => "GainEffect",
            0x15 => "LoseEffect",
            0x16 => "UpdateEffect",
            0x22 => "TargetIcon",
            0x23 => "Tether",
            0x36 => "Targetable",
            0x6D => "DirectorUpdate",
            0x1F6 => "SetTargetSign",
            0x1F9 => "LimitBreak",
            0x604 => "HoT",
            0x605 => "DoT",
            _ => $"Category 0x{category:X}",
        };
    }

    private static string FormatDebugActorControlTarget(ulong targetId)
    {
        return targetId == 0
            ? "-"
            : $"0x{targetId:X16}";
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
                var statuses = BuildCharacterStatusSnapshotsOrFallback(player, member.Statuses);
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

    private static bool TargetMatchesMember(RawTargetId targetId, PartyMemberSnapshot member)
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

    private bool TryCaptureDeath(PartyMemberSnapshot member, DateTime deathSeenAtUtc, string signalSource)
    {
        if (pullStartedAtUtc is null && !ShouldCaptureLiveCombat(DateTime.UtcNow))
        {
            return false;
        }

        if (postResetSuppressedDeadMemberKeys.Contains(member.MemberKey))
        {
            deadMemberKeys.Add(member.MemberKey);
            return false;
        }

        if (!deadMemberKeys.Add(member.MemberKey))
        {
            return false;
        }

        EnsurePullStarted(deathSeenAtUtc);
        var death = CreateDeathRecord(member, deathSeenAtUtc);
        currentDeaths.Add(death);
        if (Configuration.ShowDeathRecapPopup && IsLocalPlayer(member))
        {
            deathRecapPopupWindow.DisplayDeath(death);
        }

        QueueDeathRecapLink(death, DateTime.UtcNow);
        AddDebugLog($"{member.MemberName} died to {FormatCause(DeathDisplaySelector.Select(death).Events)} via {signalSource}.");
        return true;
    }

    private static bool IsLocalPlayer(PartyMemberSnapshot member)
    {
        var localPlayer = ObjectTable.LocalPlayer;
        return localPlayer is not null &&
            localPlayer.EntityId != 0 &&
            member.EntityId == localPlayer.EntityId;
    }

    private PartyDeathRecord CreateDeathRecord(PartyMemberSnapshot member, DateTime deathSeenAtUtc)
    {
        var events = GetRecentEvents(member.MemberKey, deathSeenAtUtc, Math.Max(Configuration.RecentEventSeconds, BetterDeathsLeadUpCaptureSeconds));
        var hpHistory = GetRecentHpHistory(member.MemberKey, deathSeenAtUtc, BetterDeathsLeadUpCaptureSeconds);
        var sourceMitigationHistory = GetRecentSourceMitigationHistory(
            events.Select(combatEvent => combatEvent.SourceEntityId),
            deathSeenAtUtc,
            BetterDeathsLeadUpCaptureSeconds);
        var fatalSequence = CreateFatalSequence(member.MemberKey, deathSeenAtUtc, events, hpHistory);
        var causeCutoff = deathSeenAtUtc - TimeSpan.FromSeconds(Configuration.DeathCauseSeconds);
        var sequenceCause = fatalSequence is not null
            ? GetPrimaryFatalSequenceEvent(fatalSequence)
            : null;
        var fallbackEventCause = events
            .Where(combatEvent => combatEvent.SeenAtUtc >= causeCutoff)
            .Where(combatEvent => combatEvent.SeenAtUtc <= deathSeenAtUtc + FatalSequenceEndBuffer)
            .Where(IsLikelyDeathCauseEvent)
            .OrderByDescending(combatEvent => combatEvent.SeenAtUtc)
            .ThenByDescending(combatEvent => combatEvent.Kind == DeathEventKind.Damage)
            .FirstOrDefault();
        var eventCause = sequenceCause ?? fallbackEventCause;
        var statusCause = CreateStatusDeathCause(member, deathSeenAtUtc);
        var cause = ShouldPreferStatusCause(statusCause, eventCause)
            ? statusCause
            : eventCause ?? statusCause;

        return new PartyDeathRecord(
            deathSeenAtUtc,
            CalculatePullElapsed(deathSeenAtUtc),
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
            SourceMitigationHistory = sourceMitigationHistory,
            EnemyHpAtDeath = CaptureEnemyHpSnapshotsAtDeath(deathSeenAtUtc),
            PossibleMitigations = BuildPossibleMitigationSnapshotsForDeath(member, deathSeenAtUtc),
            ReplayPositions = GetRecentReplayPositions(
                deathSeenAtUtc - TimeSpan.FromSeconds(DeathReplayLeadUpSeconds),
                deathSeenAtUtc),
            ReplayMarkers = GetReplayMarkersForDeath(deathSeenAtUtc, deathSeenAtUtc),
            ReplayMechanics = GetRecentReplayMechanics(
                deathSeenAtUtc - TimeSpan.FromSeconds(DeathReplayLeadUpSeconds),
                deathSeenAtUtc),
        };
    }

    private IReadOnlyList<EnemyHpSnapshot> CaptureEnemyHpSnapshotsAtDeath(DateTime deathSeenAtUtc)
    {
        var snapshots = new List<EnemyHpSnapshot>();
        var seenEntityIds = new HashSet<uint>();
        foreach (var gameObject in ObjectTable)
        {
            if (gameObject is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc ||
                battleNpc.BattleNpcKind != Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Combatant ||
                battleNpc.EntityId == 0 ||
                battleNpc.EntityId == InvalidActorEntityId ||
                battleNpc.MaxHp == 0 ||
                battleNpc.CurrentHp == 0 ||
                !seenEntityIds.Add(battleNpc.EntityId))
            {
                continue;
            }

            var name = battleNpc.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            snapshots.Add(new EnemyHpSnapshot(
                deathSeenAtUtc,
                CalculatePullElapsed(deathSeenAtUtc),
                battleNpc.EntityId,
                name,
                battleNpc.CurrentHp,
                battleNpc.MaxHp,
                battleNpc.IsTargetable));
        }

        return snapshots
            .OrderByDescending(snapshot => snapshot.IsTargetable)
            .ThenByDescending(snapshot => snapshot.MaxHp)
            .ThenBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxEnemyHpSnapshotsAtDeath)
            .ToList();
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

    private void BackfillCombatEventToCapturedDeaths(CombatEventRecord record)
    {
        BackfillCombatEventToDeathList(currentDeaths, record);
        BackfillCombatEventToDeathList(pendingDeathRecapLinks, record);
    }

    private void BackfillCombatEventToDeathList(List<PartyDeathRecord> deaths, CombatEventRecord record)
    {
        for (var index = 0; index < deaths.Count; index++)
        {
            var death = deaths[index];
            if (!EventBelongsToDeathRecord(record, death) ||
                CombatEventListContains(death.RecentEvents, record))
            {
                continue;
            }

            var recentEvents = AddCombatEventToOrderedList(death.RecentEvents, record);
            var fatalSequence = IsFatalSequenceEventCandidate(record)
                ? CreateFatalSequence(death.MemberKey, death.SeenAtUtc, recentEvents, death.HpHistory)
                : death.FatalSequence;
            var likelyCause = ShouldUseBackfilledEventAsLikelyCause(death, record)
                ? record
                : death.LikelyCause;

            deaths[index] = death with
            {
                LikelyCause = likelyCause,
                RecentEvents = recentEvents,
                FatalSequence = fatalSequence,
            };
        }
    }

    private static bool EventBelongsToDeathRecord(CombatEventRecord record, PartyDeathRecord death)
    {
        if (!string.Equals(record.MemberKey, death.MemberKey, StringComparison.Ordinal))
        {
            return false;
        }

        var startAtUtc = death.SeenAtUtc - TimeSpan.FromSeconds(BetterDeathsLeadUpCaptureSeconds);
        var endAtUtc = death.SeenAtUtc + FatalSequenceEndBuffer;
        return IsTimestampInWindow(record.SeenAtUtc, startAtUtc, endAtUtc) ||
            record.ResultSeenAtUtc is { } resultSeenAtUtc &&
            IsTimestampInWindow(resultSeenAtUtc, startAtUtc, endAtUtc);
    }

    private static bool IsTimestampInWindow(DateTime seenAtUtc, DateTime startAtUtc, DateTime endAtUtc)
    {
        return seenAtUtc >= startAtUtc && seenAtUtc <= endAtUtc;
    }

    private static IReadOnlyList<CombatEventRecord> AddCombatEventToOrderedList(
        IReadOnlyList<CombatEventRecord> events,
        CombatEventRecord record)
    {
        return events
            .Append(record)
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ThenBy(combatEvent => combatEvent.EventOrdinal)
            .ToList();
    }

    private static bool CombatEventListContains(IReadOnlyList<CombatEventRecord> events, CombatEventRecord record)
    {
        return events.Any(combatEvent => CombatEventsReferToSameCapture(combatEvent, record));
    }

    private static bool CombatEventsReferToSameCapture(CombatEventRecord first, CombatEventRecord second)
    {
        if (!string.IsNullOrWhiteSpace(first.EventIdentity) &&
            !string.IsNullOrWhiteSpace(second.EventIdentity))
        {
            return string.Equals(first.EventIdentity, second.EventIdentity, StringComparison.Ordinal);
        }

        return first.EventOrdinal != 0 &&
            first.EventOrdinal == second.EventOrdinal;
    }

    private static bool ShouldUseBackfilledEventAsLikelyCause(PartyDeathRecord death, CombatEventRecord record)
    {
        if (!IsLikelyDeathCauseEvent(record))
        {
            return false;
        }

        var causeCutoff = death.SeenAtUtc - TimeSpan.FromSeconds(BetterDeathsLeadUpSeconds);
        if (record.SeenAtUtc < causeCutoff ||
            record.SeenAtUtc > death.SeenAtUtc + FatalSequenceEndBuffer)
        {
            return false;
        }

        return death.LikelyCause is null ||
            record.SeenAtUtc > death.LikelyCause.SeenAtUtc ||
            record.SeenAtUtc == death.LikelyCause.SeenAtUtc &&
            record.Kind == DeathEventKind.Damage &&
            death.LikelyCause.Kind != DeathEventKind.Damage;
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
        while (events.Count > MaxRecentEventsPerMember)
        {
            events.RemoveAt(0);
        }
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
            TrackRecentStatusObservations(
                member.MemberKey,
                now,
                member.Statuses,
                member.CurrentHp,
                member.ShieldHp,
                member.MaxHp);
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

            AddRecentHpHistorySnapshot(
                member.MemberKey,
                new HpHistorySnapshot(
                    now,
                    CurrentPullElapsedSeconds,
                    member.CurrentHp,
                    member.ShieldHp,
                    member.MaxHp,
                    GetRelevantDeathStatuses(member.Statuses)));
        }
    }

    private void CaptureEffectResultHpSnapshot(
        PartyMemberSnapshot member,
        RawEffectResultPacket packet,
        uint shieldHp,
        IReadOnlyList<StatusSnapshot> mergedStatuses)
    {
        if (packet.MaxHp == 0 ||
            pullStartedAtUtc is null && !ShouldCaptureLiveCombat(DateTime.UtcNow))
        {
            return;
        }

        TrackRecentStatusObservations(
            member.MemberKey,
            packet.SeenAtUtc,
            mergedStatuses,
            packet.CurrentHp,
            shieldHp,
            packet.MaxHp);

        if (packet.CurrentHp == 0 && shieldHp == 0)
        {
            return;
        }

        AddRecentHpHistorySnapshot(
            member.MemberKey,
            new HpHistorySnapshot(
                packet.SeenAtUtc,
                CalculatePullElapsed(packet.SeenAtUtc),
                packet.CurrentHp,
                shieldHp,
                packet.MaxHp,
                GetRelevantDeathStatuses(mergedStatuses)));
    }

    private void TrackRecentStatusObservations(
        string memberKey,
        DateTime seenAtUtc,
        IEnumerable<StatusSnapshot> statuses,
        uint currentHp,
        uint shieldHp,
        uint maxHp)
    {
        var statusList = statuses.ToList();
        foreach (var status in statusList)
        {
            if (!IsTrackedStatusDeathCandidate(status))
            {
                continue;
            }

            AddRecentStatusObservation(
                memberKey,
                seenAtUtc,
                status,
                currentHp,
                shieldHp,
                maxHp,
                statusList);
        }
    }

    private void AddRecentStatusObservation(
        string memberKey,
        DateTime seenAtUtc,
        StatusSnapshot status,
        uint currentHp,
        uint shieldHp,
        uint maxHp,
        IReadOnlyList<StatusSnapshot> statuses)
    {
        if (!recentStatusesByMember.TryGetValue(memberKey, out var observations))
        {
            observations = [];
            recentStatusesByMember[memberKey] = observations;
        }

        observations.RemoveAll(entry => entry.Status.Id == status.Id);
        observations.Add(new StatusObservation(
            seenAtUtc,
            CalculatePullElapsed(seenAtUtc),
            status,
            currentHp,
            shieldHp,
            maxHp,
            statuses));
    }

    private void AddRecentHpHistorySnapshot(string memberKey, HpHistorySnapshot snapshot)
    {
        if (!recentHpHistoryByMember.TryGetValue(memberKey, out var history))
        {
            history = [];
            recentHpHistoryByMember[memberKey] = history;
        }

        if (ShouldSkipIntermediateEffectResultHpHistorySnapshot(memberKey, snapshot, history))
        {
            return;
        }

        RemoveIntermediateEffectResultHpHistorySnapshots(memberKey, snapshot, history);

        for (var index = history.Count - 1; index >= 0; index--)
        {
            var existing = history[index];
            if (!CanMergeCapturedHpHistorySnapshot(existing, snapshot))
            {
                continue;
            }

            history[index] = SelectPreferredHpHistorySnapshot(existing, snapshot);
            UpdateLastHpHistorySample(memberKey, snapshot.SeenAtUtc);
            return;
        }

        history.Add(snapshot);
        while (history.Count > MaxRecentHpHistoryPerMember)
        {
            history.RemoveAt(0);
        }

        UpdateLastHpHistorySample(memberKey, snapshot.SeenAtUtc);
    }

    private void TrackRecentReplayPositions(IReadOnlyList<PartyMemberSnapshot> members, DateTime now)
    {
        if (Duration(now, lastReplayPositionSampleAtUtc) < ReplayPositionSampleInterval)
        {
            return;
        }

        lastReplayPositionSampleAtUtc = now;
        foreach (var member in members)
        {
            AddRecentReplayPositionSnapshot(CreatePlayerReplayPositionSnapshot(member, now));
        }

        var (enemySnapshots, mechanicSnapshots) = CaptureReplayObjectSnapshots(now);
        foreach (var enemy in enemySnapshots)
        {
            AddRecentReplayPositionSnapshot(enemy);
        }

        foreach (var mechanic in mechanicSnapshots)
        {
            AddRecentReplayMechanicSnapshot(mechanic);
        }
    }

    private ReplayPositionSnapshot CreatePlayerReplayPositionSnapshot(PartyMemberSnapshot member, DateTime seenAtUtc)
    {
        return new ReplayPositionSnapshot(
            seenAtUtc,
            CalculatePullElapsed(seenAtUtc),
            $"player:{member.MemberKey}",
            member.MemberName,
            ReplayActorKind.Player,
            member.PartyIndex,
            member.EntityId,
            member.ClassJobId,
            member.ClassJobName,
            member.Position.X,
            member.Position.Y,
            member.Position.Z,
            member.Rotation,
            member.CurrentHp,
            member.ShieldHp,
            member.MaxHp,
            member.IsDead,
            true);
    }

    private (IReadOnlyList<ReplayPositionSnapshot> EnemySnapshots, IReadOnlyList<ReplayMechanicSnapshot> MechanicSnapshots) CaptureReplayObjectSnapshots(DateTime seenAtUtc)
    {
        var enemySnapshots = new List<ReplayPositionSnapshot>();
        var mechanicSnapshots = new List<ReplayMechanicSnapshot>();
        var seenEntityIds = new HashSet<uint>();
        foreach (var gameObject in ObjectTable)
        {
            if (gameObject is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc ||
                battleNpc.EntityId == 0 ||
                battleNpc.EntityId == InvalidActorEntityId ||
                !seenEntityIds.Add(battleNpc.EntityId))
            {
                continue;
            }

            var name = battleNpc.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (string.Equals(name, "Black Hole", StringComparison.OrdinalIgnoreCase))
            {
                mechanicSnapshots.Add(new ReplayMechanicSnapshot(
                    seenAtUtc,
                    CalculatePullElapsed(seenAtUtc),
                    (float)ReplayPositionSampleInterval.TotalSeconds * 1.5f,
                    $"object:{battleNpc.EntityId:X8}",
                    name,
                    ReplayMechanicShape.Circle,
                    battleNpc.Position.X,
                    battleNpc.Position.Y,
                    battleNpc.Position.Z,
                    battleNpc.Rotation,
                    3.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    name,
                    "object",
                    battleNpc.BaseId,
                    battleNpc.EntityId,
                    true));
                CaptureReplayBlackHoleTethers(battleNpc, seenAtUtc, mechanicSnapshots);
            }

            if (battleNpc.BattleNpcKind != Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Combatant ||
                battleNpc.MaxHp == 0)
            {
                continue;
            }

            enemySnapshots.Add(new ReplayPositionSnapshot(
                seenAtUtc,
                CalculatePullElapsed(seenAtUtc),
                $"enemy:{battleNpc.EntityId:X8}",
                name,
                ReplayActorKind.Enemy,
                2000 + battleNpc.ObjectIndex,
                battleNpc.EntityId,
                0,
                string.Empty,
                battleNpc.Position.X,
                battleNpc.Position.Y,
                battleNpc.Position.Z,
                battleNpc.Rotation,
                battleNpc.CurrentHp,
                CalculateShieldHp(battleNpc, battleNpc.MaxHp),
                battleNpc.MaxHp,
                battleNpc.IsDead || battleNpc.CurrentHp == 0,
                battleNpc.IsTargetable));
        }

        CaptureReplayDmuTethersFromPlayers(seenAtUtc, mechanicSnapshots);

        return (
            enemySnapshots
                .OrderByDescending(snapshot => snapshot.IsTargetable)
                .ThenByDescending(snapshot => snapshot.MaxHp)
                .ThenBy(snapshot => snapshot.ActorName, StringComparer.OrdinalIgnoreCase)
                .Take(MaxReplayEnemyActors)
                .ToList(),
            mechanicSnapshots);
    }

    private unsafe void CaptureReplayBlackHoleTethers(
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        DateTime seenAtUtc,
        List<ReplayMechanicSnapshot> mechanicSnapshots)
    {
        if (!IsDmuReplayCaptureContext() ||
            battleNpc is not Dalamud.Game.ClientState.Objects.Types.ICharacter character ||
            character.Address == nint.Zero)
        {
            return;
        }

        try
        {
            var characterStruct = (Character*)character.Address;
            var tethers = characterStruct->Vfx.Tethers;
            for (var index = 0; index < tethers.Length; index++)
            {
                var tether = tethers[index];
                if (tether.Id != DmuBlackHoleTetherId ||
                    tether.TargetId.ObjectId == 0)
                {
                    continue;
                }

                var targetMember = FindCurrentMemberByEntityId(tether.TargetId.ObjectId);
                if (targetMember is null)
                {
                    continue;
                }

                AddReplayDmuTetherSnapshot(
                    seenAtUtc,
                    battleNpc.EntityId,
                    battleNpc.Name.TextValue,
                    battleNpc.Position,
                    targetMember,
                    "black-hole-tether",
                    DmuBlackHoleTetherId,
                    "Tether",
                    mechanicSnapshots);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture Better Deaths Black Hole replay tether for {EntityId:X8}.", battleNpc.EntityId);
        }
    }

    private unsafe void CaptureReplayDmuTethersFromPlayers(
        DateTime seenAtUtc,
        List<ReplayMechanicSnapshot> mechanicSnapshots)
    {
        if (!IsDmuReplayCaptureContext())
        {
            return;
        }

        foreach (var member in currentMembers)
        {
            if (member.EntityId == 0 ||
                ObjectTable.SearchByEntityId(member.EntityId) is not Dalamud.Game.ClientState.Objects.Types.ICharacter character ||
                character.Address == nint.Zero)
            {
                continue;
            }

            try
            {
                var characterStruct = (Character*)character.Address;
                var tethers = characterStruct->Vfx.Tethers;
                for (var index = 0; index < tethers.Length; index++)
                {
                    var tether = tethers[index];
                    if (tether.TargetId.ObjectId == 0 ||
                        ObjectTable.SearchByEntityId(tether.TargetId.ObjectId) is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc tetherSource)
                    {
                        continue;
                    }

                    if (tether.Id == DmuBlackHoleTetherId &&
                        IsReplayBlackHoleObject(tetherSource.EntityId, tetherSource.Name.TextValue))
                    {
                        AddReplayDmuTetherSnapshot(
                            seenAtUtc,
                            tetherSource.EntityId,
                            tetherSource.Name.TextValue,
                            tetherSource.Position,
                            member,
                            "black-hole-tether",
                            DmuBlackHoleTetherId,
                            "Tether",
                            mechanicSnapshots);
                        continue;
                    }

                    if (tether.Id == DmuGravenImageTetherId &&
                        IsReplayGravenImageObject(tetherSource.EntityId, tetherSource.Name.TextValue))
                    {
                        AddReplayDmuTetherSnapshot(
                            seenAtUtc,
                            tetherSource.EntityId,
                            tetherSource.Name.TextValue,
                            tetherSource.Position,
                            member,
                            "graven-image-tether",
                            DmuGravenImageTetherId,
                            "Tether",
                            mechanicSnapshots);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not capture Better Deaths player-side replay tether for {EntityId:X8}.", member.EntityId);
            }
        }
    }

    private void AddReplayDmuTetherSnapshot(
        DateTime seenAtUtc,
        uint sourceEntityId,
        string sourceName,
        Vector3 sourcePosition,
        PartyMemberSnapshot targetMember,
        string rawEventKind,
        uint rawEventId,
        string label,
        List<ReplayMechanicSnapshot> mechanicSnapshots)
    {
        var source = sourcePosition;
        var target = targetMember.Position;
        if (!float.IsFinite(source.X) ||
            !float.IsFinite(source.Z) ||
            !float.IsFinite(target.X) ||
            !float.IsFinite(target.Z))
        {
            return;
        }

        var dx = target.X - source.X;
        var dz = target.Z - source.Z;
        var distance = MathF.Sqrt((dx * dx) + (dz * dz));
        if (distance <= 0.05f)
        {
            return;
        }

        var capturedSourceName = string.IsNullOrWhiteSpace(sourceName)
            ? "Tether source"
            : sourceName;
        mechanicSnapshots.Add(new ReplayMechanicSnapshot(
            seenAtUtc,
            CalculatePullElapsed(seenAtUtc),
            (float)ReplayPositionSampleInterval.TotalSeconds * 1.5f,
            $"{rawEventKind}:{sourceEntityId:X8}:{targetMember.EntityId:X8}",
            $"{capturedSourceName} -> {targetMember.MemberName}",
            ReplayMechanicShape.Tether,
            source.X + (dx * 0.5f),
            (source.Y + target.Y) * 0.5f,
            source.Z + (dz * 0.5f),
            MathF.Atan2(dz, dx),
            0.0f,
            distance,
            0.35f,
            0.0f,
            label,
            rawEventKind,
            rawEventId,
            targetMember.EntityId,
            true));
    }

    private bool IsDmuReplayCaptureContext()
    {
        var territoryId = currentPullTerritoryId == 0
            ? currentTerritoryId
            : currentPullTerritoryId;
        return ReplayEncounterModules.IsDancingMadUltimate(territoryId);
    }

    private bool IsReplayBlackHoleObject(uint entityId, string capturedName)
    {
        if (string.Equals(capturedName, "Black Hole", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entityId == 0)
        {
            return false;
        }

        try
        {
            return ObjectTable.SearchByEntityId(entityId) is Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc &&
                string.Equals(battleNpc.Name.TextValue, "Black Hole", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not inspect Better Deaths replay object {EntityId:X8}.", entityId);
            return false;
        }
    }

    private bool IsReplayGravenImageObject(uint entityId, string capturedName)
    {
        if (string.Equals(capturedName, "Graven Image", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entityId == 0)
        {
            return false;
        }

        try
        {
            return ObjectTable.SearchByEntityId(entityId) is Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc &&
                (battleNpc.BaseId == DmuGravenImageBaseId ||
                    string.Equals(battleNpc.Name.TextValue, "Graven Image", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not inspect Better Deaths Graven Image replay object {EntityId:X8}.", entityId);
            return false;
        }
    }

    private void AddRecentReplayPositionSnapshot(ReplayPositionSnapshot snapshot)
    {
        if (!recentReplayPositionsByActor.TryGetValue(snapshot.ActorKey, out var history))
        {
            history = [];
            recentReplayPositionsByActor[snapshot.ActorKey] = history;
        }

        history.Add(snapshot);
    }

    private void AddRecentReplayMechanicSnapshot(ReplayMechanicSnapshot snapshot)
    {
        if (!recentReplayMechanicsBySource.TryGetValue(snapshot.SourceKey, out var history))
        {
            history = [];
            recentReplayMechanicsBySource[snapshot.SourceKey] = history;
        }

        var last = history.Count == 0 ? null : history[^1];
        if (last is not null &&
            IsReplaySingleActiveSourceMechanic(snapshot) &&
            string.Equals(last.RawEventKind, snapshot.RawEventKind, StringComparison.OrdinalIgnoreCase) &&
            snapshot.SeenAtUtc > last.SeenAtUtc)
        {
            history[^1] = last with
            {
                DurationSeconds = Math.Max(0.05f, (float)(snapshot.SeenAtUtc - last.SeenAtUtc).TotalSeconds),
            };
            last = history[^1];
        }

        if (last is not null &&
            last.RawEventId == snapshot.RawEventId &&
            last.RawState == snapshot.RawState &&
            Vector3.Distance(new Vector3(last.X, last.Y, last.Z), new Vector3(snapshot.X, snapshot.Y, snapshot.Z)) <= 0.05f &&
            Duration(last.SeenAtUtc, snapshot.SeenAtUtc) <= ReplayPositionSampleInterval)
        {
            history[^1] = last with
            {
                DurationSeconds = Math.Max(
                    last.DurationSeconds,
                    (float)(snapshot.SeenAtUtc - last.SeenAtUtc).TotalSeconds + snapshot.DurationSeconds),
            };
            return;
        }

        history.Add(snapshot);
        while (history.Count > MaxRecentReplayMechanicsPerSource)
        {
            history.RemoveAt(0);
        }
    }

    private static bool IsReplaySingleActiveSourceMechanic(ReplayMechanicSnapshot snapshot)
    {
        return string.Equals(snapshot.RawEventKind, "black-hole-tether", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snapshot.RawEventKind, "graven-image-tether", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldSkipIntermediateEffectResultHpHistorySnapshot(
        string memberKey,
        HpHistorySnapshot snapshot,
        IReadOnlyList<HpHistorySnapshot> history)
    {
        foreach (var combatEvent in GetRecentEffectResultEvents(memberKey))
        {
            if (!IsIntermediateEffectResultHpHistorySnapshot(snapshot, combatEvent) ||
                !HasAuthoritativeEffectResultHpSnapshot(history, combatEvent))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void RemoveIntermediateEffectResultHpHistorySnapshots(
        string memberKey,
        HpHistorySnapshot snapshot,
        List<HpHistorySnapshot> history)
    {
        var matchingEvent = GetRecentEffectResultEvents(memberKey)
            .FirstOrDefault(combatEvent =>
                HpSnapshotMatchesEffectResult(snapshot, combatEvent) &&
                IsNearEffectResultHpHistorySnapshot(snapshot.SeenAtUtc, combatEvent.ResultSeenAtUtc!.Value));
        if (matchingEvent is null)
        {
            return;
        }

        history.RemoveAll(existing => IsIntermediateEffectResultHpHistorySnapshot(existing, matchingEvent));
    }

    private void RemoveIntermediateEffectResultHpHistorySnapshots(
        string memberKey,
        RawEffectResultPacket packet,
        uint shieldHp)
    {
        if (packet.MaxHp == 0 ||
            !recentHpHistoryByMember.TryGetValue(memberKey, out var history))
        {
            return;
        }

        history.RemoveAll(snapshot => IsIntermediateEffectResultHpHistorySnapshot(snapshot, packet, shieldHp));
    }

    private IEnumerable<CombatEventRecord> GetRecentEffectResultEvents(string memberKey)
    {
        if (!recentEventsByMember.TryGetValue(memberKey, out var events))
        {
            yield break;
        }

        foreach (var combatEvent in events)
        {
            if (CombatEventHasResultHp(combatEvent))
            {
                yield return combatEvent;
            }
        }
    }

    private static bool IsIntermediateEffectResultHpHistorySnapshot(
        HpHistorySnapshot snapshot,
        CombatEventRecord combatEvent)
    {
        return CombatEventHasResultHp(combatEvent) &&
            combatEvent.ResultSeenAtUtc is { } resultSeenAtUtc &&
            snapshot.SeenAtUtc >= combatEvent.SeenAtUtc &&
            IsNearEffectResultHpHistorySnapshot(snapshot.SeenAtUtc, resultSeenAtUtc) &&
            !HpSnapshotMatchesEffectResult(snapshot, combatEvent);
    }

    private static bool HistoryContainsEffectResultHpSnapshot(
        IReadOnlyList<HpHistorySnapshot> history,
        CombatEventRecord combatEvent)
    {
        return history.Any(snapshot =>
            snapshot.SeenAtUtc >= combatEvent.SeenAtUtc &&
            combatEvent.ResultSeenAtUtc is { } resultSeenAtUtc &&
            IsWithinEffectResultHpHistoryPreResultWindow(snapshot.SeenAtUtc, resultSeenAtUtc) &&
            HpSnapshotMatchesEffectResult(snapshot, combatEvent));
    }

    private static bool HasAuthoritativeEffectResultHpSnapshot(
        IReadOnlyList<HpHistorySnapshot> history,
        CombatEventRecord combatEvent)
    {
        return EffectResultIsZeroHp(combatEvent) ||
            HistoryContainsEffectResultHpSnapshot(history, combatEvent);
    }

    private static bool IsIntermediateEffectResultHpHistorySnapshot(
        HpHistorySnapshot snapshot,
        RawEffectResultPacket packet,
        uint shieldHp)
    {
        return packet.MaxHp > 0 &&
            IsNearEffectResultHpHistorySnapshot(snapshot.SeenAtUtc, packet.SeenAtUtc) &&
            !HpSnapshotMatchesEffectResult(snapshot, packet, shieldHp);
    }

    private static bool IsNearEffectResultHpHistorySnapshot(DateTime snapshotSeenAtUtc, DateTime resultSeenAtUtc)
    {
        if (snapshotSeenAtUtc <= resultSeenAtUtc)
        {
            return IsWithinEffectResultHpHistoryPreResultWindow(snapshotSeenAtUtc, resultSeenAtUtc);
        }

        return Duration(snapshotSeenAtUtc, resultSeenAtUtc) <= EffectResultHpHistoryPostResultWindow;
    }

    private static bool IsWithinEffectResultHpHistoryPreResultWindow(DateTime first, DateTime second)
    {
        return Duration(first, second) <= EffectResultHpHistoryPreResultWindow;
    }

    private static bool HpSnapshotMatchesEffectResult(HpHistorySnapshot snapshot, CombatEventRecord combatEvent)
    {
        return snapshot.CurrentHp == combatEvent.ResultCurrentHp &&
            snapshot.ShieldHp == combatEvent.ResultShieldHp &&
            snapshot.MaxHp == combatEvent.ResultMaxHp;
    }

    private static bool HpSnapshotMatchesEffectResult(
        HpHistorySnapshot snapshot,
        RawEffectResultPacket packet,
        uint shieldHp)
    {
        return snapshot.CurrentHp == packet.CurrentHp &&
            snapshot.ShieldHp == shieldHp &&
            snapshot.MaxHp == packet.MaxHp;
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

    private static bool CanMergeCapturedHpHistorySnapshot(HpHistorySnapshot existing, HpHistorySnapshot snapshot)
    {
        return IsWithinHpHistoryDuplicateWindow(existing.SeenAtUtc, snapshot.SeenAtUtc) &&
            existing.CurrentHp == snapshot.CurrentHp &&
            existing.ShieldHp == snapshot.ShieldHp &&
            existing.MaxHp == snapshot.MaxHp &&
            StatusListsMatchForHpHistoryCapture(existing.Statuses, snapshot.Statuses);
    }

    private static bool IsWithinHpHistoryDuplicateWindow(DateTime first, DateTime second)
    {
        return Duration(first, second) <= HpHistoryDuplicateWindow;
    }

    private static TimeSpan Duration(DateTime first, DateTime second)
    {
        return first >= second ? first - second : second - first;
    }

    private static HpHistorySnapshot SelectPreferredHpHistorySnapshot(HpHistorySnapshot existing, HpHistorySnapshot snapshot)
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

    private static bool StatusListsMatchForHpHistoryCapture(
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

    private void UpdateLastHpHistorySample(string memberKey, DateTime seenAtUtc)
    {
        if (!lastHpHistorySampleByMember.TryGetValue(memberKey, out var lastSampleAt) ||
            seenAtUtc > lastSampleAt)
        {
            lastHpHistorySampleByMember[memberKey] = seenAtUtc;
        }
    }

    private void TrackRecentSourceMitigationSnapshot(
        uint sourceEntityId,
        string sourceName,
        DateTime seenAtUtc,
        IEnumerable<StatusSnapshot> statuses)
    {
        if (sourceEntityId == 0)
        {
            return;
        }

        var sourceStatuses = GetBossMitigationStatuses(statuses)
            .Where(status => status.RemainingTime > 0.0f)
            .ToList();
        if (sourceStatuses.Count == 0)
        {
            return;
        }

        if (!recentSourceMitigationHistoryBySource.TryGetValue(sourceEntityId, out var history))
        {
            history = [];
            recentSourceMitigationHistoryBySource[sourceEntityId] = history;
        }

        history.Add(new SourceMitigationSnapshot(
            seenAtUtc,
            CalculatePullElapsed(seenAtUtc),
            sourceEntityId,
            sourceName,
            sourceStatuses));
        while (history.Count > MaxSourceMitigationHistoryPerSource)
        {
            history.RemoveAt(0);
        }
    }

    private IReadOnlyList<SourceMitigationSnapshot> GetRecentSourceMitigationHistory(
        IEnumerable<uint> sourceEntityIds,
        DateTime seenAtUtc,
        int seconds)
    {
        var sourceIds = sourceEntityIds
            .Where(sourceEntityId => sourceEntityId != 0)
            .Distinct()
            .ToList();
        if (sourceIds.Count == 0 || recentSourceMitigationHistoryBySource.Count == 0)
        {
            return [];
        }

        var cutoff = seenAtUtc - TimeSpan.FromSeconds(seconds);
        var endAtUtc = seenAtUtc + FatalSequenceEndBuffer;
        return sourceIds
            .Where(recentSourceMitigationHistoryBySource.ContainsKey)
            .SelectMany(sourceEntityId => recentSourceMitigationHistoryBySource[sourceEntityId])
            .Where(snapshot => snapshot.SeenAtUtc >= cutoff && snapshot.SeenAtUtc <= endAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.SourceEntityId)
            .ToList();
    }

    private void TrackDebugStatusSnapshots(IEnumerable<PartyMemberSnapshot> members, DateTime now)
    {
        if (!Configuration.DebugLogEnabled || debugCaptureFrozen)
        {
            return;
        }

        foreach (var member in members)
        {
            var statuses = member.Statuses.ToList();
            if (debugStatusSnapshotsByMember.TryGetValue(member.MemberKey, out var existing))
            {
                statuses = MergeDebugStatuses(existing.Statuses, statuses);
            }

            debugStatusSnapshotsByMember[member.MemberKey] = new DebugStatusSnapshot(
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
                member.IsDead,
                member.IsPartyMember,
                statuses);
            var snapshot = debugStatusSnapshotsByMember[member.MemberKey];
            var signature = BuildDebugStatusPersistSignature(snapshot);
            if (!debugStatusPersistSignaturesByMember.TryGetValue(member.MemberKey, out var existingSignature) ||
                !string.Equals(signature, existingSignature, StringComparison.Ordinal))
            {
                debugStatusPersistSignaturesByMember[member.MemberKey] = signature;
                QueueDebugCaptureRecord("StatusSnapshot", snapshot);
            }
        }
    }

    private static List<StatusSnapshot> MergeDebugStatuses(
        IReadOnlyList<StatusSnapshot> existingStatuses,
        IReadOnlyList<StatusSnapshot> currentStatuses)
    {
        var merged = new Dictionary<(uint Id, uint SourceId), StatusSnapshot>();
        foreach (var status in existingStatuses)
        {
            merged[(status.Id, status.SourceId)] = status;
        }

        foreach (var status in currentStatuses)
        {
            merged[(status.Id, status.SourceId)] = status;
        }

        return merged.Values.ToList();
    }

    private IReadOnlyList<StatusSnapshot> BuildEffectResultStatusSnapshots(RawEffectResultPacket packet)
    {
        return DeduplicateStatusSnapshots(packet.Statuses
            .Where(status => status.EffectId != 0)
            .Select(status => new StatusSnapshot(
                status.EffectId,
                GetStatusName(status.EffectId),
                GetStatusIconId(status.EffectId),
                status.SourceActorId,
                status.StackCount,
                status.Duration)));
    }

    private IReadOnlyList<DebugEffectResultStatus> BuildDebugEffectResultStatuses(RawEffectResultPacket packet)
    {
        return packet.Statuses
            .Select(status => new DebugEffectResultStatus(
                status.EffectIndex,
                status.EffectId,
                GetStatusName(status.EffectId),
                GetStatusIconId(status.EffectId),
                status.StackCount,
                status.Duration,
                status.SourceActorId,
                GetStatusSourceName(status.SourceActorId)))
            .OrderBy(status => status.EffectIndex)
            .ThenBy(status => status.EffectId)
            .ToList();
    }

    private static string BuildDebugStatusPersistSignature(DebugStatusSnapshot snapshot)
    {
        return string.Join(
            "|",
            snapshot.IsDead ? "dead" : "alive",
            snapshot.CurrentHp,
            snapshot.ShieldHp,
            snapshot.MaxHp,
            string.Join(
                ";",
                snapshot.Statuses
                    .OrderBy(status => status.Id)
                    .ThenBy(status => status.SourceId)
                    .Select(status => $"{status.Id}:{status.SourceId}:{status.StackCount}")));
    }

    private static DebugActionEffectRecord CreateDebugActionEffectRecord(CombatEventRecord record)
    {
        return new DebugActionEffectRecord(
            record.SeenAtUtc,
            record.PullElapsedSeconds,
            record.MemberKey,
            record.MemberName,
            record.PartyIndex,
            record.SourceEntityId,
            record.SourceName,
            record.ActionId,
            record.ActionName,
            record.ActionIconId,
            record.Kind.ToString(),
            (int)record.Kind,
            record.Amount,
            record.CurrentHp,
            record.ShieldHp,
            record.MaxHp,
            record.DamageType.ToString(),
            (int)record.DamageType,
            record.Critical,
            record.DirectHit,
            record.Blocked,
            record.Parried,
            record.Detail,
            record.EventIdentity,
            record.EventOrdinal,
            record.ActionSequence,
            record.HpSource.ToString(),
            (int)record.HpSource,
            record.ResultSeenAtUtc,
            record.ResultCurrentHp,
            record.ResultShieldHp,
            record.ResultMaxHp,
            record.Statuses,
            record.SourceStatuses,
            record.ResultStatuses);
    }

    private IReadOnlyList<HpHistorySnapshot> GetRecentHpHistory(string memberKey, DateTime endAtUtc, int seconds)
    {
        if (!recentHpHistoryByMember.TryGetValue(memberKey, out var history))
        {
            return [];
        }

        var cutoff = endAtUtc - TimeSpan.FromSeconds(seconds);
        return history
            .Where(snapshot => snapshot.SeenAtUtc >= cutoff && snapshot.SeenAtUtc <= endAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ToList();
    }

    private IReadOnlyList<ReplayPositionSnapshot> GetRecentReplayPositions(DateTime startAtUtc, DateTime endAtUtc)
    {
        if (recentReplayPositionsByActor.Count == 0 || endAtUtc < startAtUtc)
        {
            return [];
        }

        return recentReplayPositionsByActor.Values
            .SelectMany(history => history)
            .Where(snapshot => snapshot.SeenAtUtc >= startAtUtc && snapshot.SeenAtUtc <= endAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.ActorKind)
            .ThenBy(snapshot => snapshot.PartyIndex)
            .ThenBy(snapshot => snapshot.ActorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<ReplayMarkerSnapshot> GetReplayMarkersForDeath(DateTime deathSeenAtUtc, DateTime endAtUtc)
    {
        var fallbackStartAtUtc = deathSeenAtUtc - TimeSpan.FromSeconds(DeathReplayLeadUpSeconds + DeathReplayMarkerCarryInSeconds);
        var startAtUtc = pullStartedAtUtc is { } pullStarted
            ? pullStarted
            : fallbackStartAtUtc;
        return GetRecentReplayMarkers(startAtUtc, endAtUtc);
    }

    private IReadOnlyList<ReplayMarkerSnapshot> GetRecentReplayMarkers(DateTime startAtUtc, DateTime endAtUtc)
    {
        if (recentReplayMarkersByActor.Count == 0 || endAtUtc < startAtUtc)
        {
            return [];
        }

        return recentReplayMarkersByActor.Values
            .SelectMany(history => history)
            .Where(snapshot => snapshot.SeenAtUtc >= startAtUtc && snapshot.SeenAtUtc <= endAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.ActorKind)
            .ThenBy(snapshot => snapshot.PartyIndex)
            .ThenBy(snapshot => snapshot.ActorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<ReplayMechanicSnapshot> GetRecentReplayMechanics(DateTime startAtUtc, DateTime endAtUtc)
    {
        if (recentReplayMechanicsBySource.Count == 0 || endAtUtc < startAtUtc)
        {
            return [];
        }

        return recentReplayMechanicsBySource.Values
            .SelectMany(history => history)
            .Where(snapshot => snapshot.SeenAtUtc <= endAtUtc &&
                snapshot.SeenAtUtc.AddSeconds(Math.Max(0.05f, snapshot.DurationSeconds)) >= startAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.SourceKey, StringComparer.Ordinal)
            .ToList();
    }

    private void UpdateCurrentDeathReplayPositions(DateTime now)
    {
        if (currentDeaths.Count == 0 ||
            recentReplayPositionsByActor.Count == 0 &&
            recentReplayMarkersByActor.Count == 0 &&
            recentReplayMechanicsBySource.Count == 0)
        {
            return;
        }

        if (Duration(now, lastCurrentDeathReplayUpdateAtUtc) < ReplayPositionSampleInterval)
        {
            return;
        }

        lastCurrentDeathReplayUpdateAtUtc = now;
        for (var index = 0; index < currentDeaths.Count; index++)
        {
            var death = currentDeaths[index];
            var endAtUtc = death.SeenAtUtc + TimeSpan.FromSeconds(DeathReplayPostDeathSeconds);
            if (now < death.SeenAtUtc || now > endAtUtc + ReplayPositionSampleInterval)
            {
                continue;
            }

            var replayEndAtUtc = now <= endAtUtc ? now : endAtUtc;
            var replayPositions = recentReplayPositionsByActor.Count == 0
                ? death.ReplayPositions
                : GetRecentReplayPositions(
                    death.SeenAtUtc - TimeSpan.FromSeconds(DeathReplayLeadUpSeconds),
                    replayEndAtUtc);
            var replayMarkers = recentReplayMarkersByActor.Count == 0
                ? death.ReplayMarkers
                : GetReplayMarkersForDeath(death.SeenAtUtc, replayEndAtUtc);
            var replayMechanics = recentReplayMechanicsBySource.Count == 0
                ? death.ReplayMechanics
                : GetRecentReplayMechanics(
                    death.SeenAtUtc - TimeSpan.FromSeconds(DeathReplayLeadUpSeconds),
                    replayEndAtUtc);
            if (replayPositions.Count <= death.ReplayPositions.Count &&
                replayMarkers.Count <= death.ReplayMarkers.Count &&
                replayMechanics.Count <= death.ReplayMechanics.Count)
            {
                continue;
            }

            currentDeaths[index] = death with
            {
                ReplayPositions = replayPositions,
                ReplayMarkers = replayMarkers,
                ReplayMechanics = replayMechanics,
            };
        }
    }

    private IReadOnlyList<CombatEventRecord> GetRecentEvents(string memberKey, DateTime endAtUtc, int seconds)
    {
        if (!recentEventsByMember.TryGetValue(memberKey, out var events))
        {
            return [];
        }

        var cutoff = endAtUtc - TimeSpan.FromSeconds(seconds);
        var endWithBuffer = endAtUtc + FatalSequenceEndBuffer;
        return events
            .Where(combatEvent => EventTouchesWindow(combatEvent, cutoff, endWithBuffer))
            .OrderBy(combatEvent => combatEvent.SeenAtUtc)
            .ToList();
    }

    private static bool EventTouchesWindow(CombatEventRecord combatEvent, DateTime startAtUtc, DateTime endAtUtc)
    {
        return IsTimestampInWindow(combatEvent.SeenAtUtc, startAtUtc, endAtUtc) ||
            combatEvent.ResultSeenAtUtc is { } resultSeenAtUtc &&
            IsTimestampInWindow(resultSeenAtUtc, startAtUtc, endAtUtc);
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
        PruneRecentReplayPositions(now);
        PruneRecentReplayMarkers(now);
        PruneRecentReplayMechanics(now);
        PruneRecentSourceMitigationHistory(now);
        PrunePendingEffectResults(now);
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

    private void PruneRecentReplayPositions(DateTime now)
    {
        if (recentReplayPositionsByActor.Count == 0)
        {
            return;
        }

        var cutoff = now - TimeSpan.FromSeconds(DeathReplayRetentionSeconds);
        foreach (var actorKey in recentReplayPositionsByActor.Keys.ToList())
        {
            recentReplayPositionsByActor[actorKey].RemoveAll(snapshot => snapshot.SeenAtUtc < cutoff);
            if (recentReplayPositionsByActor[actorKey].Count == 0)
            {
                recentReplayPositionsByActor.Remove(actorKey);
            }
        }
    }

    private void PruneRecentReplayMarkers(DateTime now)
    {
        if (recentReplayMarkersByActor.Count == 0)
        {
            return;
        }

        var cutoff = pullStartedAtUtc ?? now - TimeSpan.FromSeconds(DeathReplayLeadUpSeconds + DeathReplayMarkerCarryInSeconds + DeathReplayPostDeathSeconds + 5);
        foreach (var actorKey in recentReplayMarkersByActor.Keys.ToList())
        {
            recentReplayMarkersByActor[actorKey].RemoveAll(snapshot => snapshot.SeenAtUtc < cutoff);
            if (recentReplayMarkersByActor[actorKey].Count == 0)
            {
                recentReplayMarkersByActor.Remove(actorKey);
            }
        }
    }

    private void PruneRecentReplayMechanics(DateTime now)
    {
        if (recentReplayMechanicsBySource.Count == 0)
        {
            return;
        }

        var cutoff = now - TimeSpan.FromSeconds(DeathReplayRetentionSeconds);
        foreach (var sourceKey in recentReplayMechanicsBySource.Keys.ToList())
        {
            recentReplayMechanicsBySource[sourceKey].RemoveAll(snapshot => snapshot.SeenAtUtc < cutoff);
            if (recentReplayMechanicsBySource[sourceKey].Count == 0)
            {
                recentReplayMechanicsBySource.Remove(sourceKey);
            }
        }
    }

    private void PruneRecentSourceMitigationHistory(DateTime now)
    {
        if (recentSourceMitigationHistoryBySource.Count == 0)
        {
            return;
        }

        var cutoff = now - TimeSpan.FromSeconds(SourceMitigationHistoryRetentionSeconds);
        foreach (var sourceEntityId in recentSourceMitigationHistoryBySource.Keys.ToList())
        {
            recentSourceMitigationHistoryBySource[sourceEntityId].RemoveAll(snapshot => snapshot.SeenAtUtc < cutoff);
            if (recentSourceMitigationHistoryBySource[sourceEntityId].Count == 0)
            {
                recentSourceMitigationHistoryBySource.Remove(sourceEntityId);
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

    private void PrunePendingEffectResults(DateTime now)
    {
        if (pendingEffectResultsByMemberSequence.Count == 0)
        {
            return;
        }

        var cutoff = now - TimeSpan.FromSeconds(RawCombatLogRetentionSeconds);
        foreach (var key in pendingEffectResultsByMemberSequence.Keys.ToList())
        {
            if (pendingEffectResultsByMemberSequence[key].Packet.SeenAtUtc < cutoff)
            {
                pendingEffectResultsByMemberSequence.Remove(key);
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

    private void OnDutyStarted(IDutyStateEventArgs args)
    {
        ClearDebugDataForDutyEnter();
        OnDutyReset(args);
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

        WaitForRecordedPullHistoryLoadForMutation();
        var pullNumber = GetNextRecordedPullNumber();
        var snapshot = new PullDeathSnapshot(
            DateTime.UtcNow,
            reason,
            currentPullTerritoryId == 0 ? currentTerritoryId : currentPullTerritoryId,
            currentPullTerritoryId == 0 ? currentTerritoryName : currentPullTerritoryName,
            CurrentPullElapsedSeconds,
            currentDeaths.ToList())
        {
            PullNumber = pullNumber,
            CapturedPluginVersion = GetCurrentPluginVersionForSavedData(),
        };

        lock (recordedPullLock)
        {
            recordedPulls.Add(CreateRecordedPullState(snapshot, detailDirty: true));
            TrimRecordedPullsLocked();
            UpdateRecordedPullSummariesLocked();
            recordedPullStorageDirty = true;
        }

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
        recentReplayPositionsByActor.Clear();
        recentReplayMarkersByActor.Clear();
        recentReplayMechanicsBySource.Clear();
        recentSourceMitigationHistoryBySource.Clear();
        pendingEffectResultsByMemberSequence.Clear();
        possibleMitigationUsesByMember.Clear();
        lastHpHistorySampleByMember.Clear();
        lastReplayPositionSampleAtUtc = DateTime.MinValue;
        lastCurrentDeathReplayUpdateAtUtc = DateTime.MinValue;
        lock (rawCombatQueueLock)
        {
            rawActionEffectPackets.Clear();
            rawCombatLogMessages.Clear();
            rawEffectResultPackets.Clear();
            rawActorControlPackets.Clear();
        }

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

    private bool ShouldAcceptRawCombatCapture(DateTime now)
    {
        if (IsPvPCaptureBlocked() ||
            !IsDutyCaptureActive() ||
            (!Configuration.CapturePartyDeaths && !Configuration.CaptureOtherDeaths))
        {
            return false;
        }

        return Condition[ConditionFlag.InCombat] ||
            pullStartedAtUtc is not null ||
            lastInCombatAtUtc is { } lastInCombat && now - lastInCombat <= PostCombatCaptureGrace ||
            currentMembers.Count > 0;
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

    private void ClearDebugDataForDutyEnter()
    {
        if (debugStatusSnapshotsByMember.Count == 0 &&
            debugEffectResultSnapshotsByTarget.Count == 0 &&
            debugEffectResultHistory.Count == 0 &&
            debugActorControlEvents.Count == 0 &&
            debugLogEntries.Count == 0 &&
            !debugCaptureFrozen)
        {
            return;
        }

        debugLogEntries.Clear();
        debugStatusSnapshotsByMember.Clear();
        debugStatusPersistSignaturesByMember.Clear();
        debugEffectResultSnapshotsByTarget.Clear();
        debugEffectResultHistory.Clear();
        debugActorControlEvents.Clear();
        debugCaptureFrozen = false;
        AddDebugLog("Cleared debug data for duty enter.");
    }

    private float CalculatePullElapsed(DateTime now)
    {
        return pullStartedAtUtc is null
            ? lastKnownPullElapsedSeconds
            : (float)Math.Max(0.0, (now - pullStartedAtUtc.Value).TotalSeconds);
    }

    private void TrimRecordedPullsLocked()
    {
        while (recordedPulls.Count > Configuration.MaxRecordedPulls)
        {
            recordedPulls.RemoveAt(0);
            recordedPullStorageDirty = true;
        }
    }

    private void UpdateRecordedPullSummariesLocked()
    {
        recordedPullSummaries = recordedPulls
            .Select(state => state.Summary)
            .ToList();
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
        lock (recordedPullLock)
        {
            var next = GetNextRecordedPullNumberLocked();
            nextRecordedPullNumber = next + 1;
            return next;
        }
    }

    private long GetNextRecordedPullNumberLocked()
    {
        var next = Math.Max(1, nextRecordedPullNumber);
        if (recordedPulls.Count > 0)
        {
            next = Math.Max(next, recordedPulls.Max(pull => pull.Summary.PullNumber) + 1);
        }

        return next;
    }

    private static string RecordedPullHistoryPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, RecordedPullHistoryFileName);

    private static string RecordedPullIndexPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, RecordedPullIndexFileName);

    private static string RecordedPullDetailsDirectoryPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, RecordedPullDetailsDirectoryName);

    private static string DebugCaptureFileFullPath =>
        Path.Combine(PluginInterface.ConfigDirectory.FullName, DebugCaptureFileName);

    private static string DebugCaptureTempFilePath =>
        DebugCaptureFileFullPath + ".tmp";

    private static string RecordedPullHistoryTempPath =>
        RecordedPullHistoryPath + ".tmp";

    private static string RecordedPullHistoryBackupPath =>
        RecordedPullHistoryPath + ".bak";

    private static string RecordedPullIndexTempPath =>
        RecordedPullIndexPath + ".tmp";

    private static string RecordedPullIndexBackupPath =>
        RecordedPullIndexPath + ".bak";

    private void BeginLoadRecordedPullHistory()
    {
        recordedPullHistoryLoading = true;
        recordedPullHistoryLoadError = null;
        recordedPullHistoryLoadCts = new CancellationTokenSource();
        var token = recordedPullHistoryLoadCts.Token;
        recordedPullHistoryLoadTask = Task.Run(() => LoadRecordedPullHistoryInBackground(token), token);
    }

    private void LoadRecordedPullHistoryInBackground(CancellationToken cancellationToken)
    {
        try
        {
            var states = LoadRecordedPullStates(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ApplyLoadedRecordedPullStates(states);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            recordedPullHistoryLoadError = ex.Message;
            Log.Warning(ex, "Could not load Better Deaths recorded pull history.");
        }
        finally
        {
            recordedPullHistoryLoading = false;
        }
    }

    private List<RecordedPullState> LoadRecordedPullStates(CancellationToken cancellationToken)
    {
        if (TryReadRecordedPullIndexFile(RecordedPullIndexPath) is { Count: > 0 } indexedStates)
        {
            return NormalizeRecordedPullStates(indexedStates);
        }

        if (TryReadRecordedPullIndexFile(RecordedPullIndexTempPath) is { Count: > 0 } tempIndexedStates)
        {
            return NormalizeRecordedPullStates(tempIndexedStates);
        }

        if (TryReadRecordedPullIndexFile(RecordedPullIndexBackupPath) is { Count: > 0 } backupIndexedStates)
        {
            return NormalizeRecordedPullStates(backupIndexedStates);
        }

        foreach (var backupPath in GetRecordedPullIndexRollingBackupPaths())
        {
            if (TryReadRecordedPullIndexFile(backupPath) is { Count: > 0 } rollingIndexedStates)
            {
                return NormalizeRecordedPullStates(rollingIndexedStates);
            }
        }

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
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPulls = NormalizeRecordedPullNumbers(loadedPulls
            .Where(pull => pull is { Deaths.Count: > 0 })
            .OrderBy(pull => pull.CapturedAtUtc))
            .TakeLast(Configuration.MaxRecordedPulls)
            .ToList();
        var migratedStates = normalizedPulls
            .Select(pull => CreateRecordedPullState(pull, detailDirty: true))
            .ToList();
        try
        {
            WriteRecordedPullStorageSnapshot(migratedStates, createBackup: false);
            foreach (var state in migratedStates)
            {
                state.DetailDirty = false;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not migrate Better Deaths recorded pulls into split storage.");
        }

        return migratedStates;
    }

    private void ApplyLoadedRecordedPullStates(List<RecordedPullState> states)
    {
        lock (recordedPullLock)
        {
            recordedPulls.Clear();
            recordedPulls.AddRange(states);
            TrimRecordedPullsLocked();
            UpdateRecordedPullSummariesLocked();
            nextRecordedPullNumber = GetNextRecordedPullNumberLocked();
            recordedPullStorageDirty = false;
        }
    }

    private static List<RecordedPullState> NormalizeRecordedPullStates(IEnumerable<RecordedPullState> states)
    {
        var normalized = new List<RecordedPullState>();
        var usedPullNumbers = new HashSet<long>();
        var nextPullNumber = 1L;

        foreach (var state in states
                     .Where(state => state.Summary.DeathCount > 0)
                     .OrderBy(state => state.Summary.CapturedAtUtc))
        {
            var pullNumber = state.Summary.PullNumber;
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
            var summary = state.Summary with { PullNumber = pullNumber };
            normalized.Add(new RecordedPullState(summary, state.DetailFileName, state.Detail, state.DetailDirty));
        }

        return normalized;
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

    private List<RecordedPullState>? TryReadRecordedPullIndexFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var index = JsonSerializer.Deserialize<RecordedPullIndexFile>(json, RecordedPullHistoryJsonOptions);
            return index?.Pulls
                .Where(entry => entry.DeathCount > 0 && !string.IsNullOrWhiteSpace(entry.DetailFileName))
                .Select(entry =>
                {
                    var summary = new RecordedPullSummary(
                        entry.CapturedAtUtc,
                        entry.Reason,
                        entry.TerritoryId,
                        entry.TerritoryName,
                        entry.PullElapsedSeconds,
                        entry.DeathCount)
                    {
                        PullNumber = entry.PullNumber,
                        CapturedPluginVersion = entry.CapturedPluginVersion ?? string.Empty,
                    };
                    return new RecordedPullState(summary, entry.DetailFileName, null, detailDirty: false);
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Could not read Better Deaths recorded pull index at {path}.");
            return null;
        }
    }

    private void SaveRecordedPullHistory()
    {
        if (!recordedPullStorageDirty)
        {
            return;
        }

        WaitForRecordedPullHistoryLoadForMutation();
        List<RecordedPullState> snapshot;
        lock (recordedPullLock)
        {
            if (!recordedPullStorageDirty)
            {
                return;
            }

            snapshot = recordedPulls.ToList();
        }

        try
        {
            WriteRecordedPullStorageSnapshot(snapshot, createBackup: true);
            lock (recordedPullLock)
            {
                foreach (var state in recordedPulls)
                {
                    state.DetailDirty = false;
                }

                recordedPullStorageDirty = false;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not save Better Deaths recorded pull history.");
        }
    }

    private static void WriteRecordedPullStorageSnapshot(
        IReadOnlyList<RecordedPullState> states,
        bool createBackup)
    {
        Directory.CreateDirectory(PluginInterface.ConfigDirectory.FullName);
        Directory.CreateDirectory(RecordedPullDetailsDirectoryPath);

        foreach (var state in states)
        {
            if (state.Detail is null)
            {
                continue;
            }

            var detailPath = GetRecordedPullDetailPath(state.DetailFileName);
            if (!state.DetailDirty && File.Exists(detailPath))
            {
                continue;
            }

            var detailJson = JsonSerializer.Serialize(state.Detail, RecordedPullHistoryJsonOptions);
            File.WriteAllText(detailPath, detailJson);
        }

        var index = new RecordedPullIndexFile(
            RecordedPullIndexSchemaVersion,
            states
                .Select(state => new RecordedPullIndexEntry(
                    state.Summary.CapturedAtUtc,
                    state.Summary.Reason,
                    state.Summary.TerritoryId,
                    state.Summary.TerritoryName,
                    state.Summary.PullElapsedSeconds,
                    state.Summary.DeathCount,
                    state.Summary.PullNumber,
                    state.DetailFileName)
                {
                    CapturedPluginVersion = state.Summary.CapturedPluginVersion ?? string.Empty,
                })
                .ToList());
        var indexJson = JsonSerializer.Serialize(index, RecordedPullHistoryJsonOptions);
        if (createBackup)
        {
            CreateRecordedPullIndexRollingBackup();
        }

        File.WriteAllText(RecordedPullIndexTempPath, indexJson);
        if (File.Exists(RecordedPullIndexPath))
        {
            File.Replace(RecordedPullIndexTempPath, RecordedPullIndexPath, RecordedPullIndexBackupPath, true);
        }
        else
        {
            File.Move(RecordedPullIndexTempPath, RecordedPullIndexPath, true);
        }

        PruneRecordedPullIndexRollingBackups();
        PruneOrphanRecordedPullDetailFiles(states);
    }

    private void WaitForRecordedPullHistoryLoadForMutation()
    {
        var task = recordedPullHistoryLoadTask;
        if (task is null || task.IsCompleted)
        {
            return;
        }

        try
        {
            if (task.Wait(TimeSpan.FromSeconds(10)))
            {
                return;
            }

            recordedPullHistoryLoadCts?.Cancel();
            Log.Warning("Better Deaths recorded pull history load timed out before mutation; canceling background load.");
            task.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
        {
            Log.Warning(ex, "Better Deaths recorded pull history load did not finish before mutation.");
        }
    }

    public PullDeathSnapshot? GetRecordedPullDetails(RecordedPullSummary summary)
    {
        RecordedPullState? state;
        lock (recordedPullLock)
        {
            state = FindRecordedPullStateLocked(summary);
            if (state?.Detail is not null)
            {
                return state.Detail;
            }
        }

        if (state is null)
        {
            return null;
        }

        var detail = TryReadRecordedPullDetailFile(GetRecordedPullDetailPath(state.DetailFileName));
        if (detail is null)
        {
            return null;
        }

        lock (recordedPullLock)
        {
            state = FindRecordedPullStateLocked(summary);
            if (state is null)
            {
                return detail;
            }

            state.Detail = detail;
            state.DetailDirty = false;
            return state.Detail;
        }
    }

    public PullDeathSnapshot? GetLoadedRecordedPullDetails(RecordedPullSummary summary)
    {
        lock (recordedPullLock)
        {
            return FindRecordedPullStateLocked(summary)?.Detail;
        }
    }

    private List<PullDeathSnapshot> GetLoadedRecordedPullDetails()
    {
        lock (recordedPullLock)
        {
            return recordedPulls
                .Select(state => state.Detail)
                .Where(detail => detail is not null)
                .Cast<PullDeathSnapshot>()
                .ToList();
        }
    }

    private List<PullDeathSnapshot> GetRecordedPullDetailsForSearch()
    {
        return RecordedPulls
            .Reverse()
            .Select(GetRecordedPullDetails)
            .Where(detail => detail is not null)
            .Cast<PullDeathSnapshot>()
            .ToList();
    }

    private RecordedPullState? FindRecordedPullStateLocked(RecordedPullSummary summary)
    {
        return recordedPulls.FirstOrDefault(state =>
            state.Summary.PullNumber == summary.PullNumber &&
            state.Summary.CapturedAtUtc.Ticks == summary.CapturedAtUtc.Ticks);
    }

    private static PullDeathSnapshot? TryReadRecordedPullDetailFile(string path)
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
                : JsonSerializer.Deserialize<PullDeathSnapshot>(json, RecordedPullHistoryJsonOptions);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Could not read Better Deaths recorded pull details at {path}.");
            return null;
        }
    }

    private static RecordedPullState CreateRecordedPullState(PullDeathSnapshot pull, bool detailDirty)
    {
        var summary = new RecordedPullSummary(
            pull.CapturedAtUtc,
            pull.Reason,
            pull.TerritoryId,
            pull.TerritoryName,
            pull.PullElapsedSeconds,
            pull.Deaths.Count)
        {
            PullNumber = pull.PullNumber,
            CapturedPluginVersion = pull.CapturedPluginVersion ?? string.Empty,
        };
        return new RecordedPullState(summary, BuildRecordedPullDetailFileName(pull), pull, detailDirty);
    }

    private static string BuildRecordedPullDetailFileName(PullDeathSnapshot pull)
    {
        var pullNumber = Math.Max(0, pull.PullNumber);
        return $"pull-{pullNumber:D6}-{pull.CapturedAtUtc.Ticks}.json";
    }

    private static string GetRecordedPullDetailPath(string fileName)
    {
        return Path.Combine(RecordedPullDetailsDirectoryPath, Path.GetFileName(fileName));
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

            if (Directory.Exists(RecordedPullDetailsDirectoryPath))
            {
                Directory.Delete(RecordedPullDetailsDirectoryPath, recursive: true);
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
        yield return RecordedPullIndexPath;
        yield return RecordedPullIndexTempPath;
        yield return RecordedPullIndexBackupPath;

        foreach (var backupPath in GetRecordedPullHistoryRollingBackupPaths())
        {
            yield return backupPath;
        }

        foreach (var backupPath in GetRecordedPullIndexRollingBackupPaths())
        {
            yield return backupPath;
        }
    }

    private static void CreateRecordedPullIndexRollingBackup()
    {
        try
        {
            if (!File.Exists(RecordedPullIndexPath))
            {
                return;
            }

            var backupPath = Path.Combine(
                PluginInterface.ConfigDirectory.FullName,
                $"recorded-pulls.index.backup.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.json");
            File.Copy(RecordedPullIndexPath, backupPath, false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not create Better Deaths recorded pull index rolling backup.");
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

    private static IEnumerable<string> GetRecordedPullIndexRollingBackupPaths()
    {
        try
        {
            if (!Directory.Exists(PluginInterface.ConfigDirectory.FullName))
            {
                return [];
            }

            return Directory.EnumerateFiles(
                    PluginInterface.ConfigDirectory.FullName,
                    RecordedPullIndexRollingBackupSearchPattern,
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not list Better Deaths recorded pull index rolling backups.");
            return [];
        }
    }

    private static void PruneRecordedPullIndexRollingBackups()
    {
        try
        {
            foreach (var backupPath in GetRecordedPullIndexRollingBackupPaths().Skip(RecordedPullIndexRollingBackupCount))
            {
                File.Delete(backupPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not prune Better Deaths recorded pull index rolling backups.");
        }
    }

    private static void PruneOrphanRecordedPullDetailFiles(IReadOnlyList<RecordedPullState> states)
    {
        try
        {
            if (!Directory.Exists(RecordedPullDetailsDirectoryPath))
            {
                return;
            }

            var retainedNames = states
                .Select(state => Path.GetFileName(state.DetailFileName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(RecordedPullDetailsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (!retainedNames.Contains(Path.GetFileName(path)))
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not prune Better Deaths recorded pull detail files.");
        }
    }

    private static DeathEventKind? GetEventKind(ActionEffectKind effectKind)
    {
        return effectKind switch
        {
            ActionEffectKind.Miss => DeathEventKind.Miss,
            ActionEffectKind.Damage or ActionEffectKind.BlockedDamage or ActionEffectKind.ParriedDamage => DeathEventKind.Damage,
            ActionEffectKind.Heal => DeathEventKind.Heal,
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

    private IReadOnlyList<StatusSnapshot> BuildStatusSnapshots(IEnumerable<RawStatusSnapshot> statuses)
    {
        var snapshots = new List<StatusSnapshot>();
        foreach (var status in statuses)
        {
            if (status.StatusId == 0)
            {
                continue;
            }

            snapshots.Add(new StatusSnapshot(
                status.StatusId,
                GetStatusName(status.StatusId),
                GetStatusIconId(status.StatusId),
                status.SourceId,
                status.StackCount,
                status.RemainingTime));
        }

        return snapshots
            .OrderBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.Id)
            .ToList();
    }

    private IReadOnlyList<StatusSnapshot> BuildStatusSnapshots(RawCombatSnapshot? snapshot)
    {
        return snapshot is null ? [] : BuildStatusSnapshots(snapshot.Statuses);
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
        return DeduplicateStatusSnapshots(statuses
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
        return IsRelevantDeathStatus(status) && status.RemainingTime > 0.0f;
    }

    private static IReadOnlyList<StatusSnapshot> GetBossMitigationStatuses(IEnumerable<StatusSnapshot> statuses)
    {
        return DeduplicateStatusSnapshots(statuses
                .Where(IsBossMitigationStatus))
            .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(status => status.Id)
            .ToList();
    }

    private static IReadOnlyList<StatusSnapshot> DeduplicateStatusSnapshots(IEnumerable<StatusSnapshot> statuses)
    {
        return PipeDeduplicateStatusSnapshots(statuses).ToList();
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
        return DefensiveStatusNames.Contains(status.Name);
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

    private string GetActionName(uint actionId)
    {
        if (actionNameCache.TryGetValue(actionId, out var cachedName))
        {
            return cachedName;
        }

        var name = actionId == 0 ? "Unknown action" : "Auto";
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

        var entry = new DebugLogEntry(DateTime.UtcNow, CurrentPullElapsedSeconds, message);
        debugLogEntries.Add(entry);
        while (debugLogEntries.Count > MaxDebugLogEntries)
        {
            debugLogEntries.RemoveAt(0);
        }

        QueueDebugCaptureRecord("DebugLog", entry);
    }

    private void QueueDebugCaptureRecord<T>(string kind, T data)
    {
        if (!Configuration.DebugLogEnabled || !Configuration.DebugSaveToFileEnabled)
        {
            return;
        }

        try
        {
            var record = new DebugCaptureFileRecord(
                DateTime.UtcNow,
                CurrentPullElapsedSeconds,
                currentTerritoryId,
                currentTerritoryName,
                kind,
                JsonSerializer.SerializeToElement(data, DebugCaptureJsonOptions));
            var line = JsonSerializer.Serialize(record, DebugCaptureJsonOptions);
            lock (debugCaptureFileLock)
            {
                debugCaptureFileLines.Enqueue(line);
                while (debugCaptureFileLines.Count > MaxQueuedDebugCaptureFileLines)
                {
                    debugCaptureFileLines.Dequeue();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not queue Better Deaths debug capture row.");
        }
    }

    private void FlushDebugCaptureFile(DateTime? now = null, bool force = false)
    {
        if (!force && (!Configuration.DebugLogEnabled || !Configuration.DebugSaveToFileEnabled))
        {
            return;
        }

        var currentTime = now ?? DateTime.UtcNow;
        List<string> lines = [];
        lock (debugCaptureFileLock)
        {
            if (debugCaptureFileLines.Count == 0)
            {
                return;
            }

            if (!force &&
                debugCaptureFileLines.Count < 500 &&
                currentTime - lastDebugCaptureFlushAtUtc < DebugCaptureFlushInterval)
            {
                return;
            }

            while (debugCaptureFileLines.Count > 0)
            {
                lines.Add(debugCaptureFileLines.Dequeue());
            }
        }

        try
        {
            Directory.CreateDirectory(PluginInterface.ConfigDirectory.FullName);
            File.AppendAllLines(DebugCaptureFileFullPath, lines, Encoding.UTF8);
            TrimDebugCaptureFileToCap();
            lastDebugCaptureFlushAtUtc = currentTime;
        }
        catch (Exception ex)
        {
            lastDebugCaptureFlushAtUtc = currentTime;
            Log.Warning(ex, "Could not write Better Deaths debug capture file.");
        }
    }

    private static long GetDebugCaptureFileSizeBytes()
    {
        try
        {
            return File.Exists(DebugCaptureFileFullPath)
                ? new FileInfo(DebugCaptureFileFullPath).Length
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static long GetRecordedPullStorageSizeBytes()
    {
        try
        {
            var total = GetFileSizeBytes(RecordedPullHistoryPath) +
                GetFileSizeBytes(RecordedPullHistoryTempPath) +
                GetFileSizeBytes(RecordedPullHistoryBackupPath) +
                GetFileSizeBytes(RecordedPullIndexPath) +
                GetFileSizeBytes(RecordedPullIndexTempPath) +
                GetFileSizeBytes(RecordedPullIndexBackupPath);

            foreach (var backupPath in GetRecordedPullHistoryRollingBackupPaths())
            {
                total += GetFileSizeBytes(backupPath);
            }

            foreach (var backupPath in GetRecordedPullIndexRollingBackupPaths())
            {
                total += GetFileSizeBytes(backupPath);
            }

            total += GetDirectorySizeBytes(RecordedPullDetailsDirectoryPath);
            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static int GetRecordedPullDetailFileCount()
    {
        try
        {
            return Directory.Exists(RecordedPullDetailsDirectoryPath)
                ? Directory.EnumerateFiles(RecordedPullDetailsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly).Count()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static long GetDirectorySizeBytes(string directoryPath)
    {
        try
        {
            return Directory.Exists(directoryPath)
                ? Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                    .Sum(GetFileSizeBytes)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static long GetFileSizeBytes(string path)
    {
        try
        {
            return File.Exists(path)
                ? new FileInfo(path).Length
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void TrimDebugCaptureFileToCap()
    {
        try
        {
            if (!File.Exists(DebugCaptureFileFullPath) ||
                new FileInfo(DebugCaptureFileFullPath).Length <= MaxDebugCaptureFileBytes)
            {
                return;
            }

            var lines = File.ReadAllLines(DebugCaptureFileFullPath, Encoding.UTF8);
            var retainedLines = new List<string>();
            var retainedBytes = 0L;
            for (var index = lines.Length - 1; index >= 0; index--)
            {
                var line = lines[index];
                var lineBytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                if (retainedLines.Count > 0 && retainedBytes + lineBytes > DebugCaptureTrimTargetBytes)
                {
                    break;
                }

                retainedLines.Add(line);
                retainedBytes += lineBytes;
            }

            retainedLines.Reverse();
            File.WriteAllLines(DebugCaptureTempFilePath, retainedLines, Encoding.UTF8);
            File.Move(DebugCaptureTempFilePath, DebugCaptureFileFullPath, true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not trim Better Deaths debug capture file.");
        }
    }

    private static string FormatCause(CombatEventRecord? cause)
    {
        return cause is null
            ? "Unknown"
            : cause.Kind == DeathEventKind.Status
                ? FormatActionNameForDisplay(cause)
                : $"{FormatActionNameForDisplay(cause)} ({cause.Amount:N0})";
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

    private static string FormatDeathChatHp(uint currentHp, uint shieldHp, uint maxHp)
    {
        var hpText = maxHp == 0
            ? $"{currentHp:N0}"
            : $"{currentHp:N0} ({(double)currentHp / maxHp:P0})";
        return shieldHp > 0
            ? $"{hpText} + {shieldHp:N0} shield"
            : hpText;
    }

    private string FormatDeathChatCauseLine(CombatEventRecord cause, HpHistorySnapshot? snapshot)
    {
        var hpText = FormatDeathChatHp(snapshot, cause);
        var hpSuffix = hpText is null
            ? string.Empty
            : cause.Kind == DeathEventKind.Status
                ? $" HP before KO: {hpText}."
                : $" HP before hit: {hpText}.";
        var sourceName = FormatKnownPlayerName(cause.SourceName);
        return cause.Kind == DeathEventKind.Status
            ? $"{FormatActionNameForDisplay(cause)} from {sourceName}.{hpSuffix}"
            : $"{cause.Amount:N0} from {FormatActionNameForDisplay(cause)} by {sourceName}.{hpSuffix}";
    }

    private static string FormatDeathChatDamageLine(IReadOnlyList<CombatEventRecord> damageEvents, HpHistorySnapshot? snapshot)
    {
        var hpDisplay = GetDeathChatHpDisplay(snapshot, damageEvents[0]);
        var hpSuffix = hpDisplay.Text is null
            ? string.Empty
            : $" HP before hit: {hpDisplay.Text}.";
        var totalDamage = damageEvents.Aggregate(0UL, (sum, cause) => sum + cause.Amount);
        return $"{totalDamage:N0} damage.{hpSuffix} Overkill: {FormatDeathChatOverkill(totalDamage, hpDisplay.HpBeforeHit)}.";
    }

    private static string? FormatDeathChatHp(HpHistorySnapshot? snapshot, CombatEventRecord fallbackEvent)
    {
        return GetDeathChatHpDisplay(snapshot, fallbackEvent).Text;
    }

    private static DeathChatHpDisplay GetDeathChatHpDisplay(HpHistorySnapshot? snapshot, CombatEventRecord fallbackEvent)
    {
        if (snapshot is not null)
        {
            return new DeathChatHpDisplay(
                FormatDeathChatHp(snapshot.CurrentHp, snapshot.ShieldHp, snapshot.MaxHp),
                snapshot.CurrentHp);
        }

        if (fallbackEvent.HpSource != CombatEventHpSource.NoPreHitSample &&
            fallbackEvent.MaxHp > 0 &&
            (fallbackEvent.CurrentHp > 0 || fallbackEvent.ShieldHp > 0))
        {
            return new DeathChatHpDisplay(
                FormatDeathChatHp(fallbackEvent.CurrentHp, fallbackEvent.ShieldHp, fallbackEvent.MaxHp),
                fallbackEvent.CurrentHp);
        }

        return new DeathChatHpDisplay(null, null);
    }

    private static string FormatDeathChatOverkill(ulong incomingDamage, ulong? hpBeforeHit)
    {
        if (hpBeforeHit is null)
        {
            return "-";
        }

        return incomingDamage > hpBeforeHit.Value
            ? $"{incomingDamage - hpBeforeHit.Value:N0}"
            : "0";
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
            .Concat(GetRecordedPullDetailsForSearch().SelectMany(pull => pull.Deaths))
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

    private bool IsSharedDeathCandidate(PartyDeathRecord death, SharedDeathPost post)
    {
        if (!SharedPostMemberMatches(death, post.MemberName))
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

    private bool SharedPostMemberMatches(PartyDeathRecord death, string postMemberName)
    {
        if (string.Equals(death.MemberName, postMemberName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var redactedName = FormatRedactedPlayerLabel(ToPlayerLabelCandidate(death), GetDeathLabelContext(death));
        return string.Equals(redactedName, postMemberName, StringComparison.OrdinalIgnoreCase);
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
                ActionNamesMatch(cause.ActionName, post.ActionName) &&
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

        if (post.ActionName is null && post.SourceName is null)
        {
            var totalDamage = causeEvents
                .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
                .Aggregate(0UL, (sum, cause) => sum + cause.Amount);
            return totalDamage == post.Amount.Value;
        }

        return causeEvents.Any(cause =>
            post.Amount.Value == cause.Amount &&
            ActionNamesMatch(cause.ActionName, post.ActionName) &&
            string.Equals(cause.SourceName, post.SourceName, StringComparison.OrdinalIgnoreCase));
    }

    private SharedDeathPost BuildSharedDeathPost(PartyDeathRecord death)
    {
        var causeEvents = GetDisplayCauseEvents(death);
        var damageEvents = causeEvents
            .Where(cause => cause.Kind == DeathEventKind.Damage && cause.Amount > 0)
            .ToList();
        var memberName = FormatSharedPostMemberName(death);
        if (damageEvents.Count > 0)
        {
            var totalDamage = damageEvents.Aggregate(0UL, (sum, cause) => sum + cause.Amount);
            return new SharedDeathPost(
                GetSharedPostElapsedSeconds(death),
                memberName,
                death.ClassJobName,
                null,
                null,
                totalDamage,
                null);
        }

        return causeEvents.FirstOrDefault() is { } cause
            ? new SharedDeathPost(
                GetSharedPostElapsedSeconds(death),
                memberName,
                death.ClassJobName,
                FormatActionNameForDisplay(cause),
                cause.SourceName,
                cause.Kind == DeathEventKind.Status ? null : cause.Amount,
                null)
            : new SharedDeathPost(
                GetSharedPostElapsedSeconds(death),
                memberName,
                death.ClassJobName,
                null,
                null,
                null,
                null);
    }

    private string FormatSharedPostMemberName(PartyDeathRecord death)
    {
        return Configuration.RedactPlayerNames
            ? FormatRedactedPlayerLabel(ToPlayerLabelCandidate(death), GetDeathLabelContext(death))
            : death.MemberName;
    }

    private static float GetSharedPostElapsedSeconds(PartyDeathRecord death)
    {
        return (int)MathF.Max(0.0f, death.PullElapsedSeconds);
    }

    private static bool TryParseSharedDeathPost(string text, out SharedDeathPost post)
    {
        var cleaned = SanitizeChatText(text);
        var damageMatch = SharedDamageDeathPostRegex.Match(cleaned);
        if (damageMatch.Success &&
            TryParseCombatTimer(damageMatch.Groups["timer"].Value, out var damageElapsedSeconds) &&
            TryParseAmount(damageMatch.Groups["amount"].Value, out var damageAmount))
        {
            post = new SharedDeathPost(
                damageElapsedSeconds,
                damageMatch.Groups["name"].Value,
                damageMatch.Groups["job"].Value,
                null,
                null,
                damageAmount,
                null);
            return true;
        }

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
            ActionNamesMatch(left.ActionName, right.ActionName) &&
            string.Equals(left.SourceName, right.SourceName, StringComparison.OrdinalIgnoreCase) &&
            left.Amount == right.Amount &&
            left.HitCount == right.HitCount;
    }

    private static string FormatActionNameForDisplay(CombatEventRecord combatEvent)
    {
        return IsLikelyAutoAttack(combatEvent)
            ? AutoActionDisplayName
            : FormatActionNameForDisplay(combatEvent.ActionName);
    }

    private static string FormatActionNameForDisplay(string actionName)
    {
        return IsAutoAttackActionName(actionName)
            ? AutoActionDisplayName
            : actionName;
    }

    private static bool ActionNamesMatch(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(
            FormatActionNameForDisplay(left),
            FormatActionNameForDisplay(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyAutoAttack(CombatEventRecord combatEvent)
    {
        return combatEvent.Kind == DeathEventKind.Damage &&
            IsAutoAttackActionName(combatEvent.ActionName);
    }

    private static bool IsAutoAttackActionName(string actionName)
    {
        if (string.Equals(actionName, AutoActionDisplayName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actionName, "Attack", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string actionPrefix = "Action ";
        return actionName.StartsWith(actionPrefix, StringComparison.Ordinal) &&
            actionName[actionPrefix.Length..].All(char.IsDigit);
    }

    private string GetChatBrandingPrefix()
    {
        return Configuration.RemoveChatBranding ? string.Empty : "[Better Deaths] ";
    }

    private void QueueDeathRecapLink(PartyDeathRecord death, DateTime now)
    {
        if (!Configuration.PostDeathRecapLinksOnDeath)
        {
            return;
        }

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

    private string GetDeathRecapBatchLabel(IReadOnlyList<PartyDeathRecord> deaths)
    {
        var namesText = FormatDeathRecapNames(deaths);
        return deaths.Count switch
        {
            <= 1 => namesText,
            >= 8 => "Party wipe detected",
            _ => $"{deaths.Count} deaths detected ({namesText})",
        };
    }

    private string FormatDeathRecapNames(IReadOnlyList<PartyDeathRecord> deaths)
    {
        const int maxShownNames = 4;
        var names = deaths
            .Select(death => FormatPlayerDisplayName(death, deaths))
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
        PrintDeathRecapLink(death, FormatPlayerDisplayName(death));
    }

    private void QueueDetectedSharedRecapLink(PartyDeathRecord death)
    {
        QueueDeathRecapLinkMessage(
            death,
            "Pull link detected",
            "[ Open Recap ]",
            DateTime.UtcNow.AddMilliseconds(DetectedSharedRecapLinkDelayMs));
    }

    private void PrintDeathRecapLink(PartyDeathRecord death, string batchLabel)
    {
        PrintDeathRecapLink(death, batchLabel, "[ Open Recap ]");
    }

    private void PrintDeathRecapLink(PartyDeathRecord death, string batchLabel, string label)
    {
        ChatGui.Print(BuildDeathRecapLinkEntry(death, batchLabel, label));
    }

    private void QueueDeathRecapLinkMessage(PartyDeathRecord death)
    {
        QueueDeathRecapLinkMessage(
            death,
            FormatPlayerDisplayName(death),
            "[ Open Recap ]",
            DateTime.MinValue);
    }

    private void QueueDeathRecapLinkMessage(PartyDeathRecord death, string batchLabel, string label, DateTime notBeforeUtc)
    {
        queuedChatMessages.Enqueue(QueuedChatMessage.Local(BuildDeathRecapLinkEntry(death, batchLabel, label), notBeforeUtc));
    }

    private XivChatEntry BuildDeathRecapLinkEntry(PartyDeathRecord death, string batchLabel, string label)
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

        return new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = message,
        };
    }

    private void QueueChat(DeathChatChannel channel, string message)
    {
        var effectiveChannel = GetChatChannelOption(channel).Channel;
        foreach (var line in SplitChatMessage(SanitizeChatText(message)))
        {
            queuedChatMessages.Enqueue(QueuedChatMessage.Outgoing(effectiveChannel, line));
        }
    }

    private void FlushQueuedChatMessages(DateTime now)
    {
        if (queuedChatMessages.Count == 0 || nextQueuedChatMessageAtUtc > now)
        {
            return;
        }

        var nextMessage = queuedChatMessages.Peek();
        if (nextMessage.NotBeforeUtc > now)
        {
            return;
        }

        queuedChatMessages.Dequeue();
        if (nextMessage.LocalEntry is { } localEntry)
        {
            ChatGui.Print(localEntry);
        }
        else
        {
            SendChat(nextMessage.Channel, nextMessage.Message);
        }

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

    private readonly record struct DeathChatHpDisplay(
        string? Text,
        ulong? HpBeforeHit);

    private readonly record struct RecentOwnSharedDeathPost(
        SharedDeathPost Post,
        DateTime ExpiresAtUtc);

    private readonly record struct QueuedChatMessage(
        DeathChatChannel Channel,
        string Message,
        XivChatEntry? LocalEntry,
        DateTime NotBeforeUtc)
    {
        public static QueuedChatMessage Outgoing(DeathChatChannel channel, string message)
        {
            return new QueuedChatMessage(channel, message, null, DateTime.MinValue);
        }

        public static QueuedChatMessage Local(XivChatEntry entry, DateTime notBeforeUtc)
        {
            return new QueuedChatMessage(DeathChatChannel.Echo, string.Empty, entry, notBeforeUtc);
        }
    }

    private sealed record StatusObservation(
        DateTime SeenAtUtc,
        float PullElapsedSeconds,
        StatusSnapshot Status,
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        IReadOnlyList<StatusSnapshot> Statuses);
}
