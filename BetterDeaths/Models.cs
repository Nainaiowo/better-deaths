namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Numerics;

public enum DeathEventKind
{
    Damage,
    Heal,
    Miss,
    Invulnerable,
    Status,
    Other,
}

public enum DamageType
{
    Unknown,
    Slashing,
    Piercing,
    Blunt,
    Shot,
    Magic,
    Breath,
    Physical,
    LimitBreak,
}

public enum CombatEventHpSource
{
    UnknownLegacy,
    LatestPriorSample,
    NoPreHitSample,
    DirectCombatEventSnapshot,
}

public enum ReplayActorKind
{
    Player,
    Enemy,
}

public enum ReplayPositionSampleSource
{
    Unknown,
    PeriodicPlayer,
    PeriodicEnemyObject,
    ActionEffectSource,
    ActionEffectTarget,
    MarkerMechanic,
}

public enum ReplayMechanicShape
{
    Circle,
    Donut,
    Cone,
    Line,
    Tower,
    Stack,
    Spread,
    Label,
    Tether,
}

public sealed record StatusSnapshot(
    uint Id,
    string Name,
    uint IconId,
    uint SourceId,
    ushort StackCount,
    float RemainingTime);

public sealed record PartyMemberSnapshot(
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint ClassJobId,
    string ClassJobName,
    ulong ContentId,
    uint EntityId,
    uint CurrentHp,
    uint ShieldHp,
    uint MaxHp,
    bool IsDead,
    bool IsPartyMember,
    Vector3 Position,
    float Rotation,
    IReadOnlyList<StatusSnapshot> Statuses)
{
    public bool HasWorldObject { get; init; }

    public bool WorldObjectIsDead { get; init; }

    public byte ClassJobLevel { get; init; }
}

public sealed record ReplayPositionSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    string ActorKey,
    string ActorName,
    ReplayActorKind ActorKind,
    int PartyIndex,
    uint EntityId,
    uint ClassJobId,
    string ClassJobName,
    float X,
    float Y,
    float Z,
    float Rotation,
    uint CurrentHp,
    uint ShieldHp,
    uint MaxHp,
    bool IsDead,
    bool IsTargetable)
{
    public ReplayPositionSampleSource SampleSource { get; init; } = ReplayPositionSampleSource.Unknown;
}

public sealed record ReplayMarkerSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    string ActorKey,
    string ActorName,
    ReplayActorKind ActorKind,
    int PartyIndex,
    uint EntityId,
    uint ClassJobId,
    string ClassJobName,
    uint MarkerId,
    uint RawMarkerId)
{
    public float RemainingTime { get; init; }
}

public sealed record ReplayMechanicSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    float DurationSeconds,
    string SourceKey,
    string SourceName,
    ReplayMechanicShape Shape,
    float X,
    float Y,
    float Z,
    float Rotation,
    float Radius,
    float Length,
    float Width,
    float AngleDegrees,
    string Label,
    string RawEventKind,
    uint RawEventId,
    uint RawState,
    bool IsKnown);

public sealed record ReplayAnalyzerEventSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    string EventType,
    uint SourceEntityId,
    string SourceName,
    uint AbilityId,
    string AbilityName,
    bool HasSourcePosition,
    float X,
    float Y,
    float Z,
    float Rotation);

public sealed record ReplayWorldMarkerSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    int MarkerIndex,
    string Label,
    bool Active,
    float X,
    float Y,
    float Z);

public sealed record ReplayMitigationSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint ClassJobId,
    string ClassJobName,
    uint ActionId,
    string ActionName,
    uint ActionIconId,
    PossibleMitigationScope Scope,
    float DurationSeconds,
    IReadOnlyList<StatusSnapshot> Statuses)
{
    public bool ObservedEffect { get; init; }

    public IReadOnlyList<string> TargetMemberNames { get; init; } = [];
}

public sealed record StatusSetSnapshot(DateTime SeenAtUtc, IReadOnlyList<StatusSnapshot> Statuses)
{
    public bool IsUpdateOnly { get; init; }

    public bool IsComplete { get; init; }

    public bool IsAuthoritative { get; init; }

    public IReadOnlyList<StatusSnapshot> RemovedStatuses { get; init; } = [];
}

public sealed record StatusSourceSnapshot(
    uint EntityId, string MemberKey, string MemberName, int PartyIndex, uint ClassJobId, string ClassJobName);

public sealed record PlayerStatusSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint ClassJobId,
    string ClassJobName,
    uint EntityId,
    IReadOnlyList<StatusSnapshot> Statuses)
{
    public bool IsUpdateOnly { get; init; }

    public bool IsComplete { get; init; }

    public bool IsAuthoritative { get; init; }

    public IReadOnlyList<StatusSnapshot> RemovedStatuses { get; init; } = [];

    public IReadOnlyList<StatusSourceSnapshot> Sources { get; init; } = [];

    public StatusSetSnapshot ToStatusSet() => new(SeenAtUtc, Statuses)
    {
        IsUpdateOnly = IsUpdateOnly,
        IsComplete = IsComplete,
        IsAuthoritative = IsAuthoritative,
        RemovedStatuses = RemovedStatuses,
    };
}

public sealed record ReplayDebuffSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint ClassJobId,
    string ClassJobName,
    StatusSnapshot Status,
    bool Active);

public enum EnvironmentalDeathKind
{
    PossibleEnvironmental,
    LikelyFall,
    LikelyDeathWall,
    PossibleDeathWall,
    LikelyWalled,
}

public sealed record EnvironmentalDeathAssessment(
    EnvironmentalDeathKind Kind,
    float Confidence,
    string Summary,
    IReadOnlyList<string> Evidence)
{
    public bool EnvironmentSourceDeath { get; init; }
}

public sealed record CombatEventRecord(
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
    DeathEventKind Kind,
    uint Amount,
    uint CurrentHp,
    uint ShieldHp,
    uint MaxHp,
    DamageType DamageType,
    bool Critical,
    bool DirectHit,
    bool Blocked,
    bool Parried,
    string Detail,
    IReadOnlyList<StatusSnapshot> Statuses,
    IReadOnlyList<StatusSnapshot> SourceStatuses)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<CombatEventRecord> ConstituentEvents { get; init; } = [];

    public string? EventIdentity { get; init; }

    public uint EventOrdinal { get; init; }

    public CombatEventHpSource HpSource { get; init; }

    public bool HasPlayerStatusSnapshot { get; init; }

    public bool HasSourceStatusSnapshot { get; init; }

    public uint ActionSequence { get; init; }

    public DateTime? ResultSeenAtUtc { get; init; }

    public uint ResultCurrentHp { get; init; }

    public uint ResultShieldHp { get; init; }

    public uint ResultMaxHp { get; init; }

    public IReadOnlyList<StatusSnapshot> ResultStatuses { get; init; } = [];
}

public sealed record CombatLogEventRecord(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    string SourceName,
    string TargetName,
    uint LogMessageId,
    string ActionName,
    uint Amount);

public sealed record HpHistorySnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    uint CurrentHp,
    uint ShieldHp,
    uint MaxHp,
    IReadOnlyList<StatusSnapshot> Statuses);

public sealed record SourceMitigationSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    uint SourceEntityId,
    string SourceName,
    IReadOnlyList<StatusSnapshot> Statuses)
{
    public bool IsUpdateOnly { get; init; }

    public bool IsComplete { get; init; }

    public bool IsAuthoritative { get; init; }

    public IReadOnlyList<StatusSnapshot> RemovedStatuses { get; init; } = [];

    public IReadOnlyList<StatusSourceSnapshot> Sources { get; init; } = [];

    public StatusSetSnapshot ToStatusSet() => new(SeenAtUtc, Statuses)
    {
        IsUpdateOnly = IsUpdateOnly,
        IsComplete = IsComplete,
        IsAuthoritative = IsAuthoritative,
        RemovedStatuses = RemovedStatuses,
    };
}

public sealed record EnemyHpSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    uint EntityId,
    string Name,
    uint CurrentHp,
    uint MaxHp,
    bool IsTargetable);

public enum PossibleMitigationScope
{
    Personal,
    Targeted,
    Party,
    Boss,
}

public readonly record struct MitigationSimulationHit(DateTime SeenAtUtc, uint SourceEntityId, uint ActionId,
    uint ActionSequence, long EventOrdinal, string? EventIdentity);

internal sealed record MitigationPrerequisite(uint ActionId, byte CooldownGroup, float CooldownSeconds,
    int MaxCharges, uint ReadyStatusId, IReadOnlyList<StatusSetSnapshot> StatusHistory);

public sealed record PossibleMitigationSnapshot(
    string Key,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint ClassJobId,
    string ClassJobName,
    uint ActionId,
    string ActionName,
    uint ActionIconId,
    PossibleMitigationScope Scope,
    float CooldownSeconds,
    DateTime? LastUsedAtUtc,
    float? LastUsedPullElapsedSeconds,
    string Availability,
    IReadOnlyList<StatusSnapshot> Statuses)
{
    public byte CooldownGroup { get; init; }

    public int MaxCharges { get; init; } = 1;

    [System.Text.Json.Serialization.JsonIgnore]
    internal MitigationPrerequisite? Prerequisite { get; init; }

    public bool WasUsedTooLate { get; init; }

    public IReadOnlyList<uint> EffectStatusIds { get; init; } = [];

    public IReadOnlyList<uint> SimulationSourceEntityIds { get; init; } = [];

    public IReadOnlyList<MitigationSimulationHit> SimulationHits { get; init; } = [];
}

public sealed record FatalSequenceRecord(
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    HpHistorySnapshot? LastAliveSnapshot,
    IReadOnlyList<CombatEventRecord> Events,
    IReadOnlyList<CombatLogEventRecord> LogEvents);

public sealed record PartyDeathRecord(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint ClassJobId,
    string ClassJobName,
    uint CurrentHp,
    uint ShieldHp,
    uint MaxHp,
    CombatEventRecord? LikelyCause,
    IReadOnlyList<CombatEventRecord> RecentEvents,
    IReadOnlyList<HpHistorySnapshot> HpHistory,
    IReadOnlyList<StatusSnapshot> StatusesAtDeath)
{
    public uint EntityId { get; init; }

    public FatalSequenceRecord? FatalSequence { get; init; }

    public IReadOnlyList<SourceMitigationSnapshot> SourceMitigationHistory { get; init; } = [];

    public IReadOnlyList<PlayerStatusSnapshot> PlayerStatusHistory { get; init; } = [];

    public IReadOnlyList<EnemyHpSnapshot> EnemyHpAtDeath { get; init; } = [];

    public IReadOnlyList<PossibleMitigationSnapshot> PossibleMitigations { get; init; } = [];

    public EnvironmentalDeathAssessment? EnvironmentalAssessment { get; init; }

    public string DetectionCode { get; init; } = string.Empty;

    public IReadOnlyList<ReplayPositionSnapshot> ReplayPositions { get; init; } = [];

    public IReadOnlyList<ReplayMarkerSnapshot> ReplayMarkers { get; init; } = [];

    public IReadOnlyList<ReplayMechanicSnapshot> ReplayMechanics { get; init; } = [];

    public IReadOnlyList<ReplayWorldMarkerSnapshot> ReplayWorldMarkers { get; init; } = [];
}

public sealed record PullDeathSnapshot(
    DateTime CapturedAtUtc,
    string Reason,
    uint TerritoryId,
    string TerritoryName,
    float PullElapsedSeconds,
    IReadOnlyList<PartyDeathRecord> Deaths)
{
    public long PullNumber { get; init; }

    public string CapturedPluginVersion { get; init; } = string.Empty;

    public string PullGroupId { get; init; } = string.Empty;

    public int PullGroupColorIndex { get; init; } = -1;

    public IReadOnlyList<ReplayPositionSnapshot> ReplayPositions { get; init; } = [];

    public IReadOnlyList<ReplayMarkerSnapshot> ReplayMarkers { get; init; } = [];

    public IReadOnlyList<ReplayMechanicSnapshot> ReplayMechanics { get; init; } = [];

    public IReadOnlyList<ReplayAnalyzerEventSnapshot> ReplayAnalyzerEvents { get; init; } = [];

    public IReadOnlyList<ReplayWorldMarkerSnapshot> ReplayWorldMarkers { get; init; } = [];

    public IReadOnlyList<ReplayMitigationSnapshot> ReplayMitigations { get; init; } = [];

    public IReadOnlyList<PlayerStatusSnapshot> ReplayStatusHistory { get; init; } = [];

    public IReadOnlyList<SourceMitigationSnapshot> ReplaySourceStatusHistory { get; init; } = [];

    public bool ReplayStatusHistoryCaptured { get; init; }

    public IReadOnlyList<ReplayDebuffSnapshot> ReplayDebuffs { get; init; } = [];

    public bool ReplayDebuffsCaptured { get; init; }
}

public sealed record RecordedPullSummary(
    DateTime CapturedAtUtc,
    string Reason,
    uint TerritoryId,
    string TerritoryName,
    float PullElapsedSeconds,
    int DeathCount)
{
    public long PullNumber { get; init; }

    public string CapturedPluginVersion { get; init; } = string.Empty;

    public string PullGroupId { get; init; } = string.Empty;

    public int PullGroupColorIndex { get; init; } = -1;

    public IReadOnlyList<string> DeathMemberNames { get; init; } = [];

    public bool DeathMemberNamesIndexed { get; init; }

    public IReadOnlyList<RecordedDeathReference> DeathReferences { get; init; } = [];

    public bool DeathReferencesIndexed { get; init; }
}

public readonly record struct RecordedDeathReference(long SeenAtUtcTicks, uint MemberKeyHash);

public sealed record DebugLogEntry(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    string Message);

public sealed record DebugStatusSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint ClassJobId,
    string ClassJobName,
    uint CurrentHp,
    uint ShieldHp,
    uint MaxHp,
    bool IsDead,
    bool IsPartyMember,
    IReadOnlyList<StatusSnapshot> Statuses)
{
    public bool HasWorldObject { get; init; }

    public bool WorldObjectIsDead { get; init; }
}

public sealed record DebugEffectResultStatus(
    byte EffectIndex,
    ushort EffectId,
    string Name,
    uint IconId,
    ushort StackCount,
    float Duration,
    uint SourceActorId,
    string SourceName);

public sealed record DebugEffectResultSnapshot(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    uint TargetId,
    string TargetName,
    string? MemberKey,
    uint ActorId,
    uint CurrentHp,
    uint MaxHp,
    ushort CurrentMp,
    byte ShieldPercent,
    uint ShieldHp,
    byte EffectCount,
    uint RelatedActionSequence,
    bool IsReplay,
    IReadOnlyList<DebugEffectResultStatus> Statuses);

public sealed record DebugActorControlEvent(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    uint EntityId,
    string EntityName,
    uint Category,
    string CategoryName,
    uint Param1,
    uint Param2,
    uint Param3,
    uint Param4,
    uint Param5,
    uint Param6,
    uint Param7,
    uint Param8,
    ulong TargetId,
    string TargetName,
    byte Param9);

public sealed record AddonInspectorEvent(
    DateTime SeenAtUtc,
    string EventName,
    string AddonName,
    nint Address,
    bool IsReady,
    bool IsVisible);

public sealed record AddonInspectorSnapshot(
    DateTime SeenAtUtc,
    string AddonName,
    nint Address,
    bool IsReady,
    bool IsVisible,
    float X,
    float Y,
    float Width,
    float Height,
    int NodeCount,
    IReadOnlyList<AddonInspectorNode> Nodes,
    IReadOnlyList<AddonInspectorValue> AtkValues,
    string? Error);

public sealed record AddonInspectorNode(
    int Index,
    uint NodeId,
    string NodeType,
    bool IsVisible,
    float X,
    float Y,
    ushort Width,
    ushort Height,
    string? Text);

public sealed record AddonInspectorValue(
    int Index,
    string Type,
    string Value);

public enum PluginUpdateCheckState
{
    NotChecked,
    WaitingForDalamud,
    Checking,
    UpToDate,
    UpdateAvailable,
    VersionMismatch,
    Error,
}

public sealed record PluginUpdateStatus(
    PluginUpdateCheckState State,
    string InstalledVersion,
    string ManifestVersion,
    string? AvailableVersion,
    bool AvailableVersionIsTesting,
    DateTime? LastCheckedAtUtc,
    DateTime? NextCheckAtUtc,
    string? Error);
