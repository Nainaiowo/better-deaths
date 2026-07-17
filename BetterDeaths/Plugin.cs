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

public sealed partial class Plugin : IDalamudPlugin
{
    private const string MainCommandName = "/betterdeaths";
    private const string ShortCommandName = "/bd";
    private const string WidgetCommandName = "/betterdeathswidget";
    private const string ShortWidgetCommandName = "/bdwidget";
    private const string BetterDeathsInternalName = "BetterDeaths";
    private const string LegacyDalamudRepositoryUrl = "https://raw.githubusercontent.com/Nainaiowo/IMakeSillyThings/refs/heads/main/repo.json";
    private const string PuniDalamudRepositoryUrl = "https://puni.sh/api/repository/nainai";
    private const string SharedRecapPrefix = "Recap:";
    private const string DeathRecapLinkLabel = "[ Death Link ]";
    private const string PullRecapLinkLabel = "[ Pull Link ]";
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
    private const string RecordedPullDetailJsonExtension = ".json";
    private const string RecordedPullDetailCompressedExtension = ".json.gz";
    private const string RecordedPullDetailMigrationTempSuffix = ".migration.tmp";
    private const int RecordedPullHistorySchemaVersion = 3;
    private const int RecordedPullIndexSchemaVersion = 6;
    private const int CurrentConfigurationVersion = 4;
    internal const int PullGroupColorPaletteSize = 8;
    private static readonly TimeSpan RecentPullGroupRestoreWindow = TimeSpan.FromHours(3);
    private const int RecordedPullHistoryRollingBackupCount = 5;
    private const int RecordedPullIndexRollingBackupCount = 3;
    private const string RecordedPullHistoryRollingBackupSearchPattern = "recorded-pulls.backup.*.json";
    private const string RecordedPullIndexRollingBackupSearchPattern = "recorded-pulls.index.backup.*.json";
    private const string AutoActionDisplayName = "Auto";
    private const ushort ChatGreenColorKey = 45;
    private const int BetterDeathsLeadUpSeconds = 10;
    private const int BetterDeathsLeadUpCaptureSeconds = BetterDeathsLeadUpSeconds + 10;
    private const int DeathReplayLeadUpSeconds = 30;
    private const int FullReplayMaxRetentionSeconds = 30 * 60;
    private const float EnvironmentalDeathMinimumConfidence = 0.35f;
    private const float EnvironmentalFallYDropThreshold = 2.5f;
    private const float EnvironmentalStrongFallYDropThreshold = 5.0f;
    private const float EnvironmentalKnownArenaEdgeTolerance = 1.25f;
    private const float EnvironmentalOutlierMinimumDistance = 18.0f;
    private const float EnvironmentalOutlierMinimumMargin = 6.0f;
    private const float EnvironmentalOutlierRadiusMultiplier = 1.35f;
    private const uint DmuBlackHoleTetherId = 84;
    private const uint DmuGravenImageTetherId = 45;
    private const uint DmuGravenImageBaseId = 19505;
    private const uint DmuBlackHoleNothingnessActionId = 47868;
    private const uint DmuP2PathOfLightActionId = 47806;
    private static readonly TimeSpan EnvironmentalDeathMotionWindow = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan EnvironmentalDeathPartyReferenceWindow = TimeSpan.FromSeconds(2.5);
    private const uint DmuP2SpelldriverActionId = 47808;
    private const uint DmuP2SpellscatterActionId = 47809;
    private const uint DmuP2SpellwaveActionId = 47810;
    private const uint DmuP2FuturesEndBossActionId = 47830;
    private const uint DmuP2PastsEndBossActionId = 47831;
    private const uint DmuP2FuturesEndCloneActionId = 47832;
    private const uint DmuP2PastsEndCloneActionId = 47833;
    private const uint DmuP2AllThingsEndingFirstActionId = 47836;
    private const uint DmuP2AllThingsEndingSecondActionId = 47837;
    private const uint DmuP2PathOfLightMapEffectState = 0x00020001;
    private const uint DmuP3AeroIIIAssaultActionId = 50167;
    private const uint DmuP3ThunderIIICircleActionId = 47890;
    private const uint DmuP3StrayFlamesActionId = 47859;
    private const uint DmuP3InfernoActionId = 47860;
    private const uint DmuP3TsunamiActionId = 47861;
    private const uint DmuP3StraySprayActionId = 47862;
    private const uint DmuP3CycloneActionId = 47864;
    private const uint DmuP3ThunderIIIBusterActionId = 47884;
    private const uint DmuP3LongitudinalImplosionCastActionId = 47869;
    private const uint DmuP3LatitudinalImplosionCastActionId = 47870;
    private const uint DmuP3LatLongShockwaveActionId = 47871;
    private const uint DmuP3UmbraSmashActionId = 47872;
    private const uint DmuP3UltimaBlasterChargeActionId = 47844;
    private const uint DmuP3SlapHappyRightHandCastActionId = 47846;
    private const uint DmuP3SlapHappyLeftHandCastActionId = 47847;
    private const uint DmuP3SlapHappyBigActionId = 47848;
    private const uint DmuP3SlapHappySmallActionId = 47849;
    private const uint DmuP3SlapHappyShockingImpactActionId = 47850;
    private const uint DmuP3SlapHappyShockwaveActionId = 47851;
    private const uint DmuP3DamningEdictActionId = 47873;
    private const uint DmuP3LookUponMeAndDespairCastFirstActionId = 47852;
    private const uint DmuP3LookUponMeAndDespairCastSecondActionId = 47853;
    private const uint DmuP3LookUponMeAndDespairActionId = 47854;
    private const uint DmuP3BlizzardIIIActionId = 47885;
    private const uint DmuP3KnockDownActionId = 47875;
    private const uint DmuP3StompAMoleVisualActionId = 47855;
    private const uint DmuP3StompAMoleActionId = 47856;
    private const uint DmuP3BigBangActionId = 47878;
    private const uint DmuP4GrandCrossActionId = 47892;
    private const uint DmuP4InfernoCastActionId = 47902;
    private const uint DmuP4TsunamiCastActionId = 47903;
    private const uint DmuP4InfernoHitActionId = 47904;
    private const uint DmuP4TsunamiHitActionId = 47905;
    private const uint DmuP4DeathBoltNormalActionId = 47896;
    private const uint DmuP4DeathBoltInvertedActionId = 47897;
    private const uint DmuP4DeathWaveNormalActionId = 47898;
    private const uint DmuP4DeathWaveInvertedActionId = 47899;
    private const uint DmuP4StrayFlamesNormalActionId = 47906;
    private const uint DmuP4StrayFlamesInvertedActionId = 47907;
    private const uint DmuP4StraySprayNormalActionId = 47908;
    private const uint DmuP4StraySprayInvertedActionId = 47909;
    private const uint DmuP4WhiteAntilightActionId = 50068;
    private const uint DmuP4BlackAntilightActionId = 50069;
    private const uint DmuP4EdgeOfDeathActionId = 50070;
    private const uint DmuP4UltimaUpsurgeActionId = 49738;
    private const uint DmuP5UltimaRepeaterHitActionId = 47937;
    private const uint DmuP5FellForcesDpsActionId = 50773;
    private const uint DmuP5FellForcesHealerActionId = 50772;
    private const uint DmuP5FellForcesTankActionId = 50771;
    private const uint DmuP5FloodRectCastActionId = 49539;
    private const uint DmuP5ChaoticFloodActionId = 47951;
    private const uint DmuP5FloodLineActionId = 49769;
    private const uint DmuP5FlareActionId = 47954;
    private const uint DmuP5HolyActionId = 47956;
    private const uint DmuP5ChaoticFlareActionId = 47955;
    private const uint DmuP5FlareDiffusionActionId = 47957;
    private const uint DmuP5ChaoticHolyActionId = 47958;
    private const float DmuReplayActiveMechanicMinDurationSeconds = 0.05f;
    private const float DmuReplayPredictionFallbackGraceSeconds = 0.75f;
    private const float DmuReplaySlapHappyPredictionExtraSeconds = 4.2f;
    private const float DmuReplayStompAMolePredictionExtraSeconds = 3.8f;
    private const float DmuArenaCenterX = 100.0f;
    private const float DmuArenaCenterZ = 100.0f;
    private const float DmuP2PathOfLightTowerDistance = 8.0f;
    private const float DmuP2PathOfLightTowerRadius = 4.0f;
    private const float DmuP2PathOfLightTowerFallbackDurationSeconds = 10.2f;
    private const float DmuP2PathOfLightTowerMinResolveMatchSeconds = 6.0f;
    private const float DmuP2PathOfLightTowerMaxMatchSeconds = 14.0f;
    private const float DmuP2PathOfLightTowerResolveMatchDistance = 7.0f;
    private const uint DmuP4RealityTellStatusId = 2056;
    private const int HpHistoryRetentionSeconds = BetterDeathsLeadUpCaptureSeconds + 5;
    private const int SourceMitigationHistoryRetentionSeconds = BetterDeathsLeadUpCaptureSeconds + 5;
    private const int CombatLogEventRetentionSeconds = BetterDeathsLeadUpCaptureSeconds + 5;
    private const int RawActionEffectRetentionSeconds = 5;
    private const int RawCombatLogRetentionSeconds = 10;
    private const int MaxRawActionEffectPackets = 256;
    private const int MaxRawCombatLogMessages = 256;
    private const int MaxRawEffectResultPackets = 256;
    private const int MaxRawActorControlPackets = 256;
    private const int MaxRawMapEffectPackets = 256;
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
    private const int ReplayWorldMarkerCount = 8;
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
    private const string MapEffectSignature = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 8B FA 41 0F B7 E8";
    public const float CurrentPullWidgetMinBackgroundOpacity = 0.35f;
    public const float CurrentPullWidgetMaxBackgroundOpacity = 1.0f;
    public const float MainWindowMinBackgroundOpacity = 0.20f;
    public const float MainWindowMaxBackgroundOpacity = 1.0f;
    public const float DefaultMainWindowBackgroundOpacity = 0.85f;
    public const float MinWidgetIconSize = 12.0f;
    public const float MaxWidgetIconSize = 32.0f;
    public const float MinReplayWorldMarkerOpacity = 0.15f;
    public const float MaxReplayWorldMarkerOpacity = 1.0f;
    public const float DefaultReplayWorldMarkerOpacity = 0.75f;
    private static readonly TimeSpan FatalSequenceStartBuffer = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan FatalSequenceEndBuffer = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan EffectResultActionMatchWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PostCombatCaptureGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PluginUpdateCheckInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RecordedPullIndexBackfillInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HpHistorySampleInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReplayPlayerPositionSampleInterval = TimeSpan.FromMilliseconds(66);
    private static readonly TimeSpan ReplayPositionSampleInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReplayPositionDuplicateWindow = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan ReplayStationaryPositionDuplicateWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReplayTetherPositionSampleInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReplayWorldMarkerSampleInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReplayTetherActiveGrace = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DeathActorControlLateMatchWindow = TimeSpan.FromSeconds(5);
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
    private static readonly AddonEvent[] AddonInspectorLifecycleEvents =
    [
        AddonEvent.PostSetup,
        AddonEvent.PostShow,
        AddonEvent.PostHide,
        AddonEvent.PostOpen,
        AddonEvent.PostClose,
        AddonEvent.PreFinalize,
    ];

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
    private readonly List<ReplayWorldMarkerSnapshot> recentReplayWorldMarkers = [];
    private readonly Dictionary<(string MemberKey, uint ActionSequence), PendingEffectResult> pendingEffectResultsByMemberSequence = [];
    private readonly Dictionary<uint, List<SourceMitigationSnapshot>> recentSourceMitigationHistoryBySource = [];
    private readonly Dictionary<string, Dictionary<string, TrackedPossibleMitigationUse>> possibleMitigationUsesByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> lastHpHistorySampleByMember = new(StringComparer.Ordinal);
    private DateTime lastReplayPlayerPositionSampleAtUtc = DateTime.MinValue;
    private DateTime lastReplayObjectPositionSampleAtUtc = DateTime.MinValue;
    private DateTime lastReplayWorldMarkerSampleAtUtc = DateTime.MinValue;
    private bool replayWorldMarkersCapturedForPull;
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
    private readonly Queue<RawMapEffectPacket> rawMapEffectPackets = [];
    private readonly Dictionary<uint, ActiveDmuP2PathOfLightTower> activeDmuP2PathOfLightTowersByIndex = [];
    private readonly Dictionary<string, ActiveReplayMechanic> activeReplayMechanicsByKey = new(StringComparer.Ordinal);
    private long nextRawActionEffectSequence = 1;
    private long nextRawCombatLogSequence = 1;
    private long nextRawEffectResultSequence = 1;
    private long nextRawActorControlSequence = 1;
    private long nextRawMapEffectSequence = 1;
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
    private Task? recordedPullIndexBackfillTask;
    private string? recordedPullHistoryLoadError;
    private bool recordedPullHistoryLoading;
    private bool recordedPullStorageDirty;
    private bool updateCheckInProgress;
    private bool effectResultHookEnabled;
    private bool actorControlHookEnabled;
    private bool mapEffectHookEnabled;
    private bool debugFreezeOnDeathEnabled;
    private bool debugCaptureFrozen;
    private bool addonInspectorLifecycleRegistered;
    private DateTime lastDebugCaptureFlushAtUtc = DateTime.MinValue;
    private DateTime nextRecordedPullIndexBackfillAtUtc = DateTime.MinValue;
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
    private Hook<ProcessMapEffectDelegate>? mapEffectHook;
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
    private string currentDutyInstancePullGroupId = string.Empty;
    private int currentDutyInstancePullGroupColorIndex = -1;
    private bool disposing;

    public Configuration Configuration { get; }

    public IReadOnlyList<PartyMemberSnapshot> CurrentMembers => currentMembers;

    public IReadOnlyList<PartyDeathRecord> CurrentDeaths => currentDeaths;

    public IReadOnlyList<RecordedPullSummary> RecordedPulls => recordedPullSummaries;

    public bool RecordedPullHistoryLoading => recordedPullHistoryLoading;

    public string? RecordedPullHistoryLoadError => recordedPullHistoryLoadError;

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

    public string CurrentDutyInstancePullGroupId => currentDutyInstancePullGroupId;

    public int CurrentDutyInstancePullGroupColorIndex => currentDutyInstancePullGroupColorIndex;

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

        try
        {
            mapEffectHook = GameInteropProvider.HookFromSignature<ProcessMapEffectDelegate>(
                MapEffectSignature,
                OnProcessMapEffect);
            mapEffectHook.Enable();
            mapEffectHookEnabled = true;
        }
        catch (Exception ex)
        {
            mapEffectHookEnabled = false;
            mapEffectHook = null;
            Log.Warning(ex, "Better Deaths MapEffect replay hook could not be enabled.");
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
        PluginInterface.UiBuilder.Draw += DrawUi;
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

        try
        {
            recordedPullIndexBackfillTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
        {
            Log.Warning(ex, "Better Deaths recorded pull index backfill did not finish before disposal.");
        }

        recordedPullHistoryLoadCts?.Dispose();
        PluginInterface.UiBuilder.OpenConfigUi -= OpenMainUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        PluginInterface.UiBuilder.Draw -= DrawUi;
        Framework.Update -= OnFrameworkUpdate;
        UnregisterAddonInspectorLifecycleListeners();
        ChatGui.LogMessage -= OnLogMessage;
        ChatGui.ChatMessage -= OnChatMessage;
        DutyState.DutyRecommenced -= OnDutyReset;
        DutyState.DutyWiped -= OnDutyReset;
        DutyState.DutyStarted -= OnDutyStarted;
        mapEffectHook?.Dispose();
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

    private void DrawUi()
    {
        windowSystem.Draw();
    }

    public bool IsDeathRecapPopupTestActive => deathRecapPopupWindow.IsTestPopupActive;

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
            MaybeBackfillRecordedPullDeathMemberNames(now);
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
            Type = XivChatType.SystemMessage,
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

    private static uint GetEntityIdFromRawTargetId(GameObjectId targetId)
    {
        if (targetId.ObjectId != 0)
        {
            return targetId.ObjectId;
        }

        return targetId.Id <= uint.MaxValue
            ? (uint)targetId.Id
            : 0;
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


}
