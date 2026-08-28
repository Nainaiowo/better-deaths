namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;

internal enum DamageEventOutcome
{
    Damage,
    Miss,
    Resisted,
    Invulnerable,
}

internal enum DamageAttributionQuality
{
    Exact,
    Estimated,
    Unattributed,
}

internal enum DamageResolutionQuality
{
    Unresolved,
    Observed,
    Resolved,
    KnownZeroHp,
}

internal sealed record DamageActorIdentity(
    uint EntityId,
    string Name,
    uint OwnerEntityId,
    string OwnerName,
    bool IsPlayer,
    uint ClassJobId)
{
    public uint BaseId { get; init; }

    public byte ObjectKind { get; init; }

    public byte SubKind { get; init; }

    public bool IsPet { get; init; }

    public bool IsLimitBreak { get; init; }

    public bool IsPartyMember { get; init; }
}

internal sealed record DamageStatusSnapshot(
    uint StatusId,
    DamageActorIdentity Source,
    ushort Parameter,
    float RemainingTime);

internal sealed record DamageHpSnapshot(
    uint CurrentHp,
    uint ShieldHp,
    uint MaxHp);

internal sealed record DamageEffectResult(
    DateTime SeenAtUtc,
    uint ActionSequence,
    DamageActorIdentity Target,
    DamageHpSnapshot Snapshot);

internal sealed record DamageActionPacket(
    long PacketSequence,
    DateTime SeenAtUtc,
    uint ActionSequence,
    DamageActorIdentity Source,
    uint ActionId,
    string ActionName,
    IReadOnlyList<DamageActionTarget> Targets)
{
    public DateTime? CapturedAtUtc { get; init; }

    public uint ActionCategoryId { get; init; }

    public double? DirectPotency { get; init; }

    public bool CanCalibratePotency { get; init; }

    public bool IsAutoAttack { get; init; }

    public byte ActionType { get; init; }

    public ushort SourceSequence { get; init; }

    public ushort SpellId { get; init; }

    public byte AnimationVariation { get; init; }

    public uint AnimationTargetEntityId { get; init; }

    public DamageActorIdentity? SourceOwner { get; init; }

    public IReadOnlyList<DamageStatusApplication> StatusApplications { get; init; } = [];

    public IReadOnlyList<DamageStatusSnapshot> SourceStatuses { get; init; } = [];

    public bool HasSourceStatusSnapshot { get; init; }
}

internal sealed record DamageStatusApplication(
    DamageActorIdentity Target,
    DamageActorIdentity Source,
    uint StatusId,
    string StatusName,
    uint StatusIconId,
    uint ActionId,
    string ActionName,
    DateTime SeenAtUtc,
    float DurationSeconds,
    bool IsPeriodicDamage,
    bool IsReactiveDamage,
    bool IsRemoval)
{
    public double? PeriodicPotency { get; init; }

    public byte? BaseDamageLowByte { get; init; }

    public byte? CriticalRateLowByte { get; init; }

    public byte? EffectParameterByte { get; init; }

    public string SnapshotKey { get; init; } = string.Empty;

    public ushort Parameter { get; init; }

    public uint ActionCategoryId { get; init; }

    public byte DamageType { get; init; }

    public byte ElementType { get; init; }

    public IReadOnlyList<DamageStatusSnapshot> SourceStatuses { get; init; } = [];

    public IReadOnlyList<DamageStatusSnapshot> TargetStatuses { get; init; } = [];

    public bool HasSourceStatusSnapshot { get; init; }

    public bool HasTargetStatusSnapshot { get; init; }
}

internal sealed record PeriodicDamageTick(
    long PacketSequence,
    DateTime SeenAtUtc,
    DamageActorIdentity Target,
    uint StatusId,
    string StatusName,
    uint StatusIconId,
    uint Amount,
    DamageActorIdentity? Source)
{
    public DateTime? CapturedAtUtc { get; init; }

    public DamageHpSnapshot? TargetHp { get; init; }
}

internal sealed record DamageActionTarget(
    int TargetIndex,
    DamageActorIdentity Target,
    IReadOnlyList<DamageActionEffect> Effects)
{
    public DamageHpSnapshot? TargetHp { get; init; }

    public IReadOnlyList<DamageStatusSnapshot> TargetStatuses { get; init; } = [];

    public bool HasTargetStatusSnapshot { get; init; }
}

internal sealed record DamageActionEffect(
    int EffectIndex,
    byte Type,
    byte Param0,
    byte Param1,
    byte Param3,
    byte Param4,
    uint Value)
{
    public byte Param2 { get; init; }
}

internal sealed record ParsedDamageEvent(
    string EventId,
    long PacketSequence,
    DateTime SeenAtUtc,
    uint ActionSequence,
    DamageActorIdentity Source,
    DamageActorIdentity Target,
    uint ActionId,
    string ActionName,
    int TargetIndex,
    int EffectIndex,
    DamageEventOutcome Outcome,
    uint Amount,
    byte DamageType,
    bool Critical,
    bool DirectHit,
    bool Blocked,
    bool Parried,
    byte RawEffectType,
    byte RawParam0,
    byte RawParam1,
    byte RawParam3,
    byte RawParam4)
{
    public DateTime? CapturedAtUtc { get; init; }

    public double? DirectPotency { get; init; }

    public bool CanCalibratePotency { get; init; }

    public double? MeterAmount { get; init; }

    public double RawMeterAmount => MeterAmount ?? Amount;

    public double? CalculatedAmount { get; init; }

    public double EffectiveMeterAmount => CalculatedAmount ?? RawMeterAmount;

    public DamageResolutionQuality ResolutionQuality { get; init; }

    public double AbsorbedDamage { get; init; }

    public double OverkillDamage { get; init; }

    public DamageHpSnapshot? TargetHpBefore { get; init; }

    public DamageHpSnapshot? TargetHpAfter { get; init; }

    public byte ElementType { get; init; }

    public uint ActionCategoryId { get; init; }

    public bool IsAutoAttack { get; init; }

    public byte ActionType { get; init; }

    public ushort SourceSequence { get; init; }

    public ushort SpellId { get; init; }

    public byte AnimationVariation { get; init; }

    public uint AnimationTargetEntityId { get; init; }

    public byte RawParam2 { get; init; }

    public bool IsSourceEntry { get; init; }

    public DamageActorIdentity? PacketTarget { get; init; }

    public DamageActorIdentity? AttributedSource { get; init; }

    public DamageAttributionQuality AttributionQuality { get; init; } = DamageAttributionQuality.Exact;

    public bool IsPeriodic { get; init; }

    public uint StatusId { get; init; }

    public uint StatusIconId { get; init; }

    public byte? CriticalRateLowByte { get; init; }

    public IReadOnlyList<DamageStatusSnapshot> SourceStatuses { get; init; } = [];

    public IReadOnlyList<DamageStatusSnapshot> TargetStatuses { get; init; } = [];

    public bool HasSourceStatusSnapshot { get; init; }

    public bool HasTargetStatusSnapshot { get; init; }
}

internal sealed record DamageActionSummary(
    uint ActionId,
    string ActionName,
    ulong TotalDamage,
    int Swings,
    int Hits,
    int Misses,
    int Resists,
    int InvulnerableHits,
    int CriticalHits,
    int DirectHits,
    int CriticalDirectHits,
    int BlockedHits,
    int ParriedHits)
{
    public double? MeterDamage { get; init; }

    public double EffectiveMeterDamage => MeterDamage ?? TotalDamage;

    public bool IsAutoAttack { get; init; }

    public uint ActionCategoryId { get; init; }

    public ulong PeriodicDamage { get; init; }

    public ulong EstimatedDamage { get; init; }

    public ulong UnattributedDamage { get; init; }

    public int PeriodicHits { get; init; }

    public ulong MaxHitAmount { get; init; }
}

internal sealed record DamageSourceSummary(
    DamageActorIdentity Source,
    ulong TotalDamage,
    int Swings,
    int Hits,
    int Misses,
    int Resists,
    int InvulnerableHits,
    int CriticalHits,
    int DirectHits,
    int CriticalDirectHits,
    int BlockedHits,
    int ParriedHits,
    IReadOnlyList<DamageActionSummary> Actions)
{
    public double? MeterDamage { get; init; }

    public double EffectiveMeterDamage => MeterDamage ?? TotalDamage;

    public ulong PeriodicDamage { get; init; }

    public ulong EstimatedDamage { get; init; }

    public ulong UnattributedDamage { get; init; }

    public double RaidAdjustedDamage { get; init; }

    public double? MeterRaidAdjustedDamage { get; init; }

    public double EffectiveMeterRaidAdjustedDamage => MeterRaidAdjustedDamage ?? RaidAdjustedDamage;

    public double ExternalBuffDamageReceived { get; init; }

    public double? MeterExternalBuffDamageReceived { get; init; }

    public double EffectiveMeterExternalBuffDamageReceived =>
        MeterExternalBuffDamageReceived ?? ExternalBuffDamageReceived;

    public double RaidBuffDamageGiven { get; init; }

    public double? MeterRaidBuffDamageGiven { get; init; }

    public double EffectiveMeterRaidBuffDamageGiven => MeterRaidBuffDamageGiven ?? RaidBuffDamageGiven;

    public int PeriodicHits { get; init; }

    public ulong MaxHitAmount { get; init; }

    public string MaxHitActionName { get; init; } = string.Empty;

    public int Deaths { get; init; }
}

internal sealed record DamageTargetSummary(
    DamageActorIdentity Target,
    ulong TotalDamage,
    int Swings,
    int Hits,
    int Misses,
    int Resists,
    int InvulnerableHits);

internal sealed record DamageEncounterSnapshot(
    DateTime StartedAtUtc,
    DateTime SnapshotAtUtc,
    DateTime? EndedAtUtc,
    string EndReason,
    ulong TotalDamage,
    int PacketCount,
    int DuplicateEventCount,
    IReadOnlyList<ParsedDamageEvent> Events,
    IReadOnlyList<DamageSourceSummary> Sources,
    IReadOnlyList<DamageTargetSummary> Targets)
{
    public DateTime? MeterStartedAtUtc { get; init; }

    public DateTime? MeterSnapshotAtUtc { get; init; }

    public DateTime? MeterEndedAtUtc { get; init; }

    public double? MeterDamage { get; init; }

    public double EffectiveMeterDamage => MeterDamage ?? TotalDamage;

    public ulong ExactDamage { get; init; }

    public ulong EstimatedDamage { get; init; }

    public ulong UnattributedDamage { get; init; }

    public double RaidAdjustedDamage { get; init; }

    public double? MeterRaidAdjustedDamage { get; init; }

    public double EffectiveMeterRaidAdjustedDamage => MeterRaidAdjustedDamage ?? RaidAdjustedDamage;

    public double DurationSeconds
    {
        get
        {
            var start = MeterStartedAtUtc ?? StartedAtUtc;
            var end = MeterEndedAtUtc ?? MeterSnapshotAtUtc ?? EndedAtUtc ?? SnapshotAtUtc;
            return Math.Max(0.0, (end - start).TotalSeconds);
        }
    }

    public double DamagePerSecond => DurationSeconds <= 0.0 ? 0.0 : EffectiveMeterDamage / DurationSeconds;

    public double RaidDamagePerSecond => DurationSeconds <= 0.0
        ? 0.0
        : EffectiveMeterRaidAdjustedDamage / DurationSeconds;
}
