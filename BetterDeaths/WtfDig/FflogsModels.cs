using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BetterDeaths.WtfDig;

internal sealed record FflogsReportInput(string Code, int? FightId, bool UseLastFight);

internal sealed class FflogsReportSummary
{
    public required string Code { get; init; }
    public string Title { get; init; } = string.Empty;
    public double StartTime { get; init; }
    public double EndTime { get; init; }
    public FflogsZone? Zone { get; init; }
    public IReadOnlyList<FflogsFight> Fights { get; init; } = [];
    public IReadOnlyList<FflogsAbility> Abilities { get; init; } = [];
    public IReadOnlyList<FflogsActor> Actors { get; init; } = [];
}

internal sealed class FflogsZone
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

internal sealed class FflogsFight
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int EncounterID { get; init; }
    public int? Difficulty { get; init; }
    public bool? Kill { get; init; }
    public double StartTime { get; init; }
    public double EndTime { get; init; }
    public double? FightPercentage { get; init; }
    public IReadOnlyList<int>? FriendlyPlayers { get; init; }
    public IReadOnlyList<FflogsPhaseTransition>? PhaseTransitions { get; init; }

    [JsonIgnore]
    public double DurationMs => EndTime - StartTime;
}

internal sealed class FflogsPhaseTransition
{
    public int Id { get; init; }
    public double StartTime { get; init; }
}

internal sealed class FflogsAbility
{
    public uint GameID { get; init; }
    public string Name { get; init; } = string.Empty;
}

internal sealed class FflogsActor
{
    public int Id { get; init; }
    public uint GameID { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string SubType { get; init; } = string.Empty;
}

internal sealed class FflogsResources
{
    public long HitPoints { get; init; }
    public long MaxHitPoints { get; init; }
    public long? Mp { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Facing { get; init; }
    public long? Absorb { get; init; }
}

internal sealed class FflogsEvent
{
    public double Timestamp { get; init; }
    public string Type { get; init; } = string.Empty;
    public int? SourceID { get; init; }
    public int? TargetID { get; init; }
    public uint? AbilityGameID { get; init; }
    public long? Amount { get; init; }
    public long? UnmitigatedAmount { get; init; }
    public long? Absorbed { get; init; }
    public long? Mitigated { get; init; }
    public uint? KillingAbilityGameID { get; init; }
    public int? Stack { get; init; }
    public uint? MarkerID { get; init; }
    public int? SourceInstance { get; init; }
    public int? TargetInstance { get; init; }
    public double? Duration { get; init; }
    public uint? ExtraAbilityGameID { get; init; }
    public long? ExtraInfo { get; init; }
    public FflogsResources? SourceResources { get; init; }
    public FflogsResources? TargetResources { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

internal enum FflogsEventDataType
{
    Casts,
    DamageTaken,
    CombatantInfo,
    Buffs,
    Debuffs,
    Deaths,
}

internal enum FflogsHostilityType
{
    Friendlies,
    Enemies,
}

internal sealed record FflogsEventQuery(
    string Code,
    int FightId,
    double StartTime,
    double EndTime,
    FflogsEventDataType? DataType = null,
    FflogsHostilityType HostilityType = FflogsHostilityType.Friendlies,
    bool IncludeResources = false,
    uint? AbilityId = null,
    string? FilterExpression = null,
    int CacheTtl = 0);

internal interface IWtfDigEventSource
{
    Task<IReadOnlyList<FflogsEvent>> FetchAllEventsAsync(
        FflogsEventQuery query,
        CancellationToken cancellationToken);
}
