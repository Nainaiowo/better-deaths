namespace BetterDeaths;

using System;
using System.Collections.Generic;

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
    IReadOnlyList<StatusSnapshot> Statuses);

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
    IReadOnlyList<StatusSnapshot> SourceStatuses);

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
    public FatalSequenceRecord? FatalSequence { get; init; }
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
}

public sealed record DebugLogEntry(
    DateTime SeenAtUtc,
    float PullElapsedSeconds,
    string Message);

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
