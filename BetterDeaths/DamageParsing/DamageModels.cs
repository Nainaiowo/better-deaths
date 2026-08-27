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

internal sealed record DamageActionPacket(
    long PacketSequence,
    DateTime SeenAtUtc,
    uint ActionSequence,
    DamageActorIdentity Source,
    uint ActionId,
    string ActionName,
    IReadOnlyList<DamageActionTarget> Targets)
{
    public uint ActionCategoryId { get; init; }

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
    public string SnapshotKey { get; init; } = string.Empty;

    public ushort Parameter { get; init; }

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
    DamageActorIdentity? Source);

internal sealed record DamageActionTarget(
    int TargetIndex,
    DamageActorIdentity Target,
    IReadOnlyList<DamageActionEffect> Effects)
{
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
    public ulong PeriodicDamage { get; init; }

    public ulong EstimatedDamage { get; init; }

    public ulong UnattributedDamage { get; init; }

    public double RaidAdjustedDamage { get; init; }

    public double ExternalBuffDamageReceived { get; init; }

    public double RaidBuffDamageGiven { get; init; }

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
    public ulong ExactDamage { get; init; }

    public ulong EstimatedDamage { get; init; }

    public ulong UnattributedDamage { get; init; }

    public double RaidAdjustedDamage { get; init; }

    public double DurationSeconds
    {
        get
        {
            var end = EndedAtUtc ?? SnapshotAtUtc;
            return Math.Max(0.0, (end - StartedAtUtc).TotalSeconds);
        }
    }

    public double DamagePerSecond => DurationSeconds <= 0.0 ? 0.0 : TotalDamage / DurationSeconds;

    public double RaidDamagePerSecond => DurationSeconds <= 0.0 ? 0.0 : RaidAdjustedDamage / DurationSeconds;
}
