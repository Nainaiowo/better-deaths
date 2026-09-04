namespace BetterDeaths.DamageParsing;

using BetterDeaths.WtfDig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

internal sealed record DamageParityPlayerComparison(
    string PlayerName,
    string JobName,
    double LocalDamage,
    double ReferenceDamage,
    double LocalDirectDamage,
    double ReferenceDirectDamage,
    double LocalPeriodicDamage,
    double ReferencePeriodicDamage,
    bool HasLocalData,
    bool HasReferenceData)
{
    public double Difference => LocalDamage - ReferenceDamage;

    public double? DifferencePercent => DamageParityComparer.DifferencePercent(LocalDamage, ReferenceDamage);
}

internal sealed record DamageParityAbilityComparison(
    string PlayerName,
    string AbilityName,
    double LocalDamage,
    double ReferenceDamage,
    double LocalPeriodicDamage,
    double ReferencePeriodicDamage)
{
    public double Difference => LocalDamage - ReferenceDamage;

    public double? DifferencePercent => DamageParityComparer.DifferencePercent(LocalDamage, ReferenceDamage);
}

internal sealed record DamageParityComparison(
    string ReportTitle,
    int FightId,
    string FightName,
    double LocalDurationSeconds,
    double ReferenceDurationSeconds,
    double LocalDamage,
    double ReferenceDamage,
    double LocalDirectDamage,
    double ReferenceDirectDamage,
    double LocalPeriodicDamage,
    double ReferencePeriodicDamage,
    int LocalEventCount,
    int ReferenceEventCount,
    int ReferenceSimulatedPeriodicEventCount,
    double ReferenceSimulatedPeriodicDamage,
    double ReferenceUnassignedPetDamage,
    IReadOnlyList<DamageParityPlayerComparison> Players,
    IReadOnlyList<DamageParityAbilityComparison> Abilities)
{
    public double ReferenceCombinedPeriodicDamageExcluded { get; init; }
    public double ReferenceFinalizedPeriodicDamage { get; init; }
    public int ReferenceMissingFinalizedTicks { get; init; }
    public double LocalRawDamage { get; init; }
    public double LocalSimulatedPeriodicDamage { get; init; }
    public int LocalSimulatedPeriodicTicks { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public double LocalEncounterDps => LocalDurationSeconds <= 0.0 ? 0.0 : LocalDamage / LocalDurationSeconds;

    public double ReferenceEncounterDps => ReferenceDurationSeconds <= 0.0
        ? 0.0
        : ReferenceDamage / ReferenceDurationSeconds;

    public double Difference => LocalDamage - ReferenceDamage;

    public double? DifferencePercent => DamageParityComparer.DifferencePercent(LocalDamage, ReferenceDamage);
}

internal static class DamageParityComparer
{
    private static readonly IReadOnlyDictionary<string, string> PetOwnerJobs =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Normalize("Automaton Queen")] = Normalize("Machinist"),
            [Normalize("Rook Autoturret")] = Normalize("Machinist"),
            [Normalize("Bishop Autoturret")] = Normalize("Machinist"),
            [Normalize("Esteem")] = Normalize("Dark Knight"),
            [Normalize("Carbuncle")] = Normalize("Summoner"),
            [Normalize("Solar Bahamut")] = Normalize("Summoner"),
            [Normalize("Demi-Bahamut")] = Normalize("Summoner"),
            [Normalize("Demi-Phoenix")] = Normalize("Summoner"),
            [Normalize("Ruby Ifrit")] = Normalize("Summoner"),
            [Normalize("Emerald Garuda")] = Normalize("Summoner"),
            [Normalize("Topaz Titan")] = Normalize("Summoner"),
            [Normalize("Ifrit-Egi")] = Normalize("Summoner"),
            [Normalize("Garuda-Egi")] = Normalize("Summoner"),
            [Normalize("Titan-Egi")] = Normalize("Summoner"),
        };

    internal static DamageParityComparison Compare(
        DamageEncounterSnapshot local,
        FflogsReportSummary report,
        FflogsFight fight,
        IReadOnlyList<FflogsEvent> referenceEvents)
    {
        var localSources = local.Sources
            .Where(source => DamageMeterCombatantPolicy.ShouldDisplay(source.Source))
            .ToList();
        var localEvents = local.Events.Where(entry => entry.MeterEligibility == DamageMeterEligibility.Eligible &&
            entry.Outcome == DamageEventOutcome.Damage &&
            DamageMeterCombatantPolicy.ShouldDisplay(entry.AttributedSource ?? entry.Source)).ToList();
        if (localSources.GroupBy(source => Normalize(source.Source.Name)).Any(group =>
                group.Select(source => source.Source.EntityId).Distinct().Count() > 1))
        {
            throw new InvalidOperationException("Local players share a name. A name-only comparison cannot safely match this encounter.");
        }
        var localPlayers = localSources
            .GroupBy(source => Normalize(source.Source.Name), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => CreateLocalSource(group.ToList(), localEvents.Where(entry =>
                    Normalize((entry.AttributedSource ?? entry.Source).Name) == group.Key).ToList()),
                StringComparer.Ordinal);

        var actorsById = report.Actors.ToDictionary(actor => actor.Id);
        var abilitiesById = report.Abilities
            .GroupBy(ability => ability.GameID)
            .ToDictionary(group => group.Key, group => group.First().Name);
        var friendlyIds = (fight.FriendlyPlayers ?? [])
            .ToHashSet();
        var friendlyPlayers = report.Actors
            .Where(actor => friendlyIds.Contains(actor.Id) && IsPlayer(actor) && !IsLimitBreak(actor))
            .ToList();
        if (friendlyPlayers.GroupBy(actor => Normalize(actor.Name)).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Report players share a name. Explicit actor mapping is required for this comparison.");
        }
        var referencePlayers = new Dictionary<string, MutableReferenceSource>(StringComparer.Ordinal);
        var unassignedPetDamage = 0.0;
        var combinedPeriodicExcluded = 0.0;
        var finalizedPeriodicDamage = 0.0;
        var missingFinalizedTicks = 0;
        var acceptedEventCount = 0;
        var inferredPetOwnership = false;
        var referenceDamageEvents = referenceEvents
            .Where(IsFinalDamageEvent)
            .ToList();

        foreach (var damageEvent in referenceDamageEvents)
        {
            if (damageEvent.SourceID is not { } sourceId || damageEvent.Amount is not > 0)
            {
                continue;
            }

            actorsById.TryGetValue(sourceId, out var sourceActor);
            if (damageEvent.Tick == true && damageEvent.AbilityGameID == 500000)
            {
                combinedPeriodicExcluded += damageEvent.Amount.Value;
                continue;
            }

            if (sourceActor is not null && IsPlayer(sourceActor) && !friendlyIds.Contains(sourceId))
            {
                continue;
            }
            var owner = ResolveReferenceOwner(sourceActor, actorsById, friendlyPlayers);
            if (owner is null && sourceActor is null)
            {
                continue;
            }

            if (owner is null && !IsPet(sourceActor!) && !IsLimitBreak(sourceActor!))
            {
                continue;
            }

            var isUnassignedPet = owner is null && IsPet(sourceActor!);
            inferredPetOwnership |= owner is not null && sourceActor is not null &&
                IsPet(sourceActor) && sourceActor.PetOwner is null;
            var playerName = owner?.Name ?? sourceActor!.Name + (isUnassignedPet ? " (unassigned pet)" : string.Empty);
            var jobName = owner?.SubType ?? sourceActor!.SubType;
            var playerKey = Normalize(playerName);
            if (!referencePlayers.TryGetValue(playerKey, out var referenceSource))
            {
                referenceSource = new MutableReferenceSource(playerName, jobName);
                referencePlayers[playerKey] = referenceSource;
            }

            var amount = (double)damageEvent.Amount.Value;
            acceptedEventCount++;
            if (damageEvent.Tick == true && damageEvent.Simulated == true)
            {
                if (damageEvent.FinalizedAmount is { } finalized && double.IsFinite(finalized) && finalized >= 0)
                {
                    finalizedPeriodicDamage += finalized;
                }
                else
                {
                    missingFinalizedTicks++;
                }
            }
            var abilityName = damageEvent.AbilityGameID is { } abilityId &&
                abilitiesById.TryGetValue(abilityId, out var knownName)
                    ? knownName
                    : damageEvent.AbilityGameID is { } unknownId
                        ? $"Ability {unknownId}"
                        : "Unknown ability";
            referenceSource.Add(abilityName, amount, damageEvent.Tick == true, damageEvent.Simulated == true,
                AbilityKey(damageEvent.AbilityGameID ?? 0, damageEvent.Tick == true));
            if (isUnassignedPet)
            {
                unassignedPetDamage += amount;
            }
        }

        var playerKeys = localPlayers.Keys
            .Concat(referencePlayers.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var players = playerKeys
            .Select(key =>
            {
                localPlayers.TryGetValue(key, out var localSource);
                referencePlayers.TryGetValue(key, out var referenceSource);
                return new DamageParityPlayerComparison(
                    localSource?.Name ?? referenceSource?.Name ?? "Unknown player",
                    localSource?.JobName ?? referenceSource?.JobName ?? string.Empty,
                    localSource?.Damage ?? 0.0,
                    referenceSource?.Damage ?? 0.0,
                    localSource?.DirectDamage ?? 0.0,
                    referenceSource?.DirectDamage ?? 0.0,
                    localSource?.PeriodicDamage ?? 0.0,
                    referenceSource?.PeriodicDamage ?? 0.0,
                    localSource is not null,
                    referenceSource is not null);
            })
            .OrderByDescending(row => Math.Abs(row.Difference))
            .ThenBy(row => row.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var abilityKeys = localPlayers
            .SelectMany(player => player.Value.Abilities.Keys.Select(ability => (player.Key, ability)))
            .Concat(referencePlayers.SelectMany(player =>
                player.Value.Abilities.Keys.Select(ability => (player.Key, ability))))
            .Distinct()
            .ToList();
        var abilities = abilityKeys
            .Select(key =>
            {
                localPlayers.TryGetValue(key.Item1, out var localSource);
                referencePlayers.TryGetValue(key.Item1, out var referenceSource);
                MutableReferenceAbility? localAbility = null;
                MutableReferenceAbility? referenceAbility = null;
                localSource?.Abilities.TryGetValue(key.Item2, out localAbility);
                referenceSource?.Abilities.TryGetValue(key.Item2, out referenceAbility);
                return new DamageParityAbilityComparison(
                    localSource?.Name ?? referenceSource?.Name ?? "Unknown player",
                    localAbility?.Name ?? referenceAbility?.Name ?? "Unknown ability",
                    localAbility?.Damage ?? 0.0,
                    referenceAbility?.Damage ?? 0.0,
                    localAbility?.PeriodicDamage ?? 0.0,
                    referenceAbility?.PeriodicDamage ?? 0.0);
            })
            .OrderByDescending(row => Math.Abs(row.Difference))
            .ThenBy(row => row.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.AbilityName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var localDamage = localPlayers.Values.Sum(source => source.Damage);
        var localPeriodicDamage = localPlayers.Values.Sum(source => source.PeriodicDamage);
        var referenceDamage = referencePlayers.Values.Sum(source => source.Damage);
        var referencePeriodicDamage = referencePlayers.Values.Sum(source => source.PeriodicDamage);
        var warnings = new List<string>
        {
            "Event totals only; report table target exclusions and metric options have not been applied.",
        };
        if (inferredPetOwnership)
        {
            warnings.Add("Some pet owners were inferred from a unique job, not explicit owner metadata.");
        }
        if (local.Events.Count == 0)
        {
            warnings.Add("Local event details are unavailable; raw and independent periodic estimates cannot be compared.");
        }
        else if (Math.Abs(localEvents.Sum(entry => entry.EffectiveMeterAmount) - localDamage) > 0.001)
        {
            warnings.Add("Local event details are incomplete; event-only raw and estimated totals do not cover the entire encounter.");
        }
        if (localEvents.Any(entry => entry.IsPeriodic && entry.SimulatedPeriodicAmount is null &&
                entry.PeriodicEstimateUnavailableReason != "Observed source-specific tick"))
        {
            warnings.Add("Some periodic ticks lack independent estimates. The encounter export identifies missing calibration and unsupported attribute windows.");
        }
        return new DamageParityComparison(
            report.Title,
            fight.Id,
            fight.Name,
            local.DurationSeconds,
            Math.Max(0.0, fight.DurationMs / 1000.0),
            localDamage,
            referenceDamage,
            Math.Max(0.0, localDamage - localPeriodicDamage),
            Math.Max(0.0, referenceDamage - referencePeriodicDamage),
            localPeriodicDamage,
            referencePeriodicDamage,
            localEvents.Count,
            acceptedEventCount,
            referencePlayers.Values.Sum(source => source.SimulatedPeriodicEventCount),
            referencePlayers.Values.Sum(source => source.SimulatedPeriodicDamage),
            unassignedPetDamage,
            players,
            abilities)
        {
            ReferenceCombinedPeriodicDamageExcluded = combinedPeriodicExcluded,
            ReferenceFinalizedPeriodicDamage = finalizedPeriodicDamage,
            ReferenceMissingFinalizedTicks = missingFinalizedTicks,
            LocalRawDamage = localEvents.Sum(entry => entry.RawMeterAmount),
            LocalSimulatedPeriodicDamage = localEvents.Sum(entry => entry.SimulatedPeriodicAmount ?? 0),
            LocalSimulatedPeriodicTicks = localEvents.Count(entry => entry.SimulatedPeriodicAmount is not null),
            Warnings = warnings,
        };
    }

    internal static double? DifferencePercent(double local, double reference)
    {
        return reference <= 0.0 ? null : (local - reference) * 100.0 / reference;
    }

    private static MutableReferenceSource CreateLocalSource(
        IReadOnlyList<DamageSourceSummary> sources,
        IReadOnlyList<ParsedDamageEvent> events)
    {
        var first = sources[0];
        var local = new MutableReferenceSource(
            first.Source.Name,
            first.Source.ClassJobId == 0 ? string.Empty : $"Job {first.Source.ClassJobId}");
        var damage = sources.Sum(source => source.EffectiveMeterDamage);
        if (events.Count > 0 && Math.Abs(events.Sum(entry => entry.EffectiveMeterAmount) - damage) <= 0.001)
        {
            foreach (var entry in events)
            {
                local.Add(entry.ActionName, entry.EffectiveMeterAmount, entry.IsPeriodic, false,
                    AbilityKey(entry.ActionId, entry.IsPeriodic));
            }
            return local;
        }
        foreach (var action in sources.SelectMany(source => source.Actions))
        {
            local.AddLocal(action.ActionName, action.EffectiveMeterDamage, action.PeriodicDamage,
                AbilityKey(action.ActionId, action.PeriodicDamage > 0 && action.PeriodicDamage == action.TotalDamage));
        }

        // Preserve source totals even when older snapshots have no action breakdown.
        var periodicDamage = sources.Sum(source => source.Actions.Count > 0
            ? source.Actions.Sum(action => Math.Min(action.EffectiveMeterDamage, action.PeriodicDamage))
            : Math.Min(source.EffectiveMeterDamage, source.PeriodicDamage));
        local.SetTotals(damage, periodicDamage);
        return local;
    }

    private static FflogsActor? ResolveReferenceOwner(
        FflogsActor? source,
        IReadOnlyDictionary<int, FflogsActor> actorsById,
        IReadOnlyList<FflogsActor> friendlyPlayers)
    {
        if (source is null)
        {
            return null;
        }

        if (IsPlayer(source))
        {
            return source;
        }

        if (source.PetOwner is { } ownerId && actorsById.TryGetValue(ownerId, out var explicitOwner))
        {
            return friendlyPlayers.Any(player => player.Id == explicitOwner.Id) ? explicitOwner : null;
        }

        if (!IsPet(source) || !PetOwnerJobs.TryGetValue(Normalize(source.Name), out var ownerJob))
        {
            return null;
        }

        var candidates = friendlyPlayers
            .Where(player => Normalize(player.SubType) == ownerJob)
            .Take(2)
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static bool IsFinalDamageEvent(FflogsEvent damageEvent)
    {
        return string.Equals(damageEvent.Type, "damage", StringComparison.OrdinalIgnoreCase) &&
            damageEvent.Amount is > 0;
    }

    private static bool IsPlayer(FflogsActor actor) =>
        string.Equals(actor.Type, "Player", StringComparison.OrdinalIgnoreCase);

    private static bool IsPet(FflogsActor actor) =>
        string.Equals(actor.Type, "Pet", StringComparison.OrdinalIgnoreCase);

    private static bool IsLimitBreak(FflogsActor actor) =>
        string.Equals(actor.Type, "LimitBreak", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(actor.SubType, "LimitBreak", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(actor.Name, "Limit Break", StringComparison.OrdinalIgnoreCase);

    private static string AbilityKey(long id, bool periodic) => periodic
        ? $"status:{(id >= 1000000 ? id - 1000000 : id)}"
        : $"action:{id}";

    internal static FflogsFight SelectMatchingFight(DamageEncounterSnapshot local, FflogsReportSummary report, int? fightId)
    {
        var candidates = report.Fights.Where(fight => fight.EndTime > fight.StartTime &&
            (fightId is null || fight.Id == fightId)).ToList();
        var matches = candidates.Where(fight => MatchesInterval(local, report, fight)).ToList();
        return matches.Count == 1 ? matches[0] : throw new InvalidOperationException(
            matches.Count == 0
                ? "The report fight does not match the local encounter's UTC start/end. Select the same completed pull."
                : "More than one report fight overlaps this encounter. Include an explicit fight number.");
    }

    private static bool MatchesInterval(DamageEncounterSnapshot local, FflogsReportSummary report, FflogsFight fight)
    {
        var reportStart = DateTimeOffset.FromUnixTimeMilliseconds((long)report.StartTime).UtcDateTime;
        var start = reportStart.AddMilliseconds(fight.StartTime);
        var end = reportStart.AddMilliseconds(fight.EndTime);
        var localStart = local.MeterStartedAtUtc ?? local.StartedAtUtc;
        var localEnd = local.MeterEndedAtUtc ?? local.MeterSnapshotAtUtc ?? local.EndedAtUtc ?? local.SnapshotAtUtc;
        // Permit cast lead and wipe tails, but never silently compare another pull.
        return Math.Abs((localStart - start).TotalSeconds) <= 15 &&
            Math.Abs((localEnd - end).TotalSeconds) <= 30;
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private sealed class MutableReferenceSource
    {
        private double? explicitDamage;
        private double? explicitPeriodicDamage;

        public MutableReferenceSource(string name, string jobName)
        {
            Name = name;
            JobName = jobName;
        }

        public string Name { get; }
        public string JobName { get; }
        public double Damage => explicitDamage ?? Abilities.Values.Sum(ability => ability.Damage);
        public double PeriodicDamage => explicitPeriodicDamage ?? Abilities.Values.Sum(ability => ability.PeriodicDamage);
        public double DirectDamage => Math.Max(0.0, Damage - PeriodicDamage);
        public int SimulatedPeriodicEventCount { get; private set; }
        public double SimulatedPeriodicDamage { get; private set; }
        public Dictionary<string, MutableReferenceAbility> Abilities { get; } = new(StringComparer.Ordinal);

        public void Add(string abilityName, double amount, bool periodic, bool simulated, string key)
        {
            if (!Abilities.TryGetValue(key, out var ability))
            {
                ability = new MutableReferenceAbility(abilityName);
                Abilities[key] = ability;
            }

            ability.Add(amount, periodic);
            if (periodic && simulated)
            {
                SimulatedPeriodicEventCount++;
                SimulatedPeriodicDamage += amount;
            }
        }

        public void AddLocal(string abilityName, double amount, double periodicAmount, string key)
        {
            if (!Abilities.TryGetValue(key, out var ability))
            {
                ability = new MutableReferenceAbility(abilityName);
                Abilities[key] = ability;
            }

            ability.AddLocal(amount, periodicAmount);
        }

        public void SetTotals(double damage, double periodicDamage)
        {
            explicitDamage = damage;
            explicitPeriodicDamage = periodicDamage;
        }
    }

    private sealed class MutableReferenceAbility
    {
        public MutableReferenceAbility(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public double Damage { get; private set; }
        public double PeriodicDamage { get; private set; }

        public void Add(double amount, bool periodic)
        {
            Damage += amount;
            PeriodicDamage += periodic ? amount : 0.0;
        }

        public void AddLocal(double amount, double periodicAmount)
        {
            Damage += amount;
            PeriodicDamage += Math.Min(amount, periodicAmount);
        }
    }
}
