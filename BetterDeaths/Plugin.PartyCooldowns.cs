using BetterDeaths.PartyCooldowns;
using Dalamud.Game.DutyState;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private readonly PartyCooldownTracker partyCooldownTracker = new();
    private readonly object partyCooldownQueueLock = new();
    private readonly Queue<PartyCooldownObservation> partyCooldownObservations = new(256);
    private readonly PartyCooldownObservation[] partyCooldownDrain = new PartyCooldownObservation[256];
    private Dictionary<uint, PartyCooldownActor> partyCooldownActors = [];
    private HashSet<ulong> partyCooldownMemberKeys = [];
    private readonly Dictionary<ulong, (uint Job, byte Level)> partyCooldownContexts = [];
    private PartyCooldownCatalog? loadedPartyCooldownCatalog;
    private readonly PartyCooldownRosterMember[] partyCooldownRoster = new PartyCooldownRosterMember[9];
    private int partyCooldownRosterCount;
    private bool partyCooldownRosterInvalidated = true;
    private double nextPartyCooldownDisplayAt;
    private Task<PartyCooldownCatalog>? partyCooldownCatalogTask;
    private double nextPartyCooldownRosterAt;
    private uint partyCooldownTerritory;
    private readonly PartyCooldownLifecycle partyCooldownLifecycle = new();
    private bool partyCooldownQueueOverflowed;
    private bool partyCooldownCatalogFailureReported;
    private bool partyCooldownUpdateFailureReported;

    internal PartyCooldownCatalog? GetPartyCooldownCatalog()
    {
        partyCooldownCatalogTask ??= Task.Run(BuildPartyCooldownCatalog);
        if (partyCooldownCatalogTask.IsCompletedSuccessfully)
        {
            if (Volatile.Read(ref loadedPartyCooldownCatalog) is null)
            {
                var changed = false;
                foreach (var entry in Configuration.PartyCooldownActions.ToArray())
                {
                    var selection = partyCooldownCatalogTask.Result.NormalizeSelection(entry.Key, entry.Value);
                    if (entry.Value.SequenceEqual(selection))
                        continue;
                    Configuration.PartyCooldownActions[entry.Key] = selection;
                    changed = true;
                }
                Volatile.Write(ref loadedPartyCooldownCatalog, partyCooldownCatalogTask.Result);
                if (changed)
                    SaveConfiguration();
            }
            return partyCooldownCatalogTask.Result;
        }
        if (partyCooldownCatalogTask.IsFaulted && !partyCooldownCatalogFailureReported)
        {
            partyCooldownCatalogFailureReported = true;
            Log.Warning(partyCooldownCatalogTask.Exception, "Could not load the party cooldown ability catalog.");
        }
        return null;
    }

    internal bool PartyCooldownCatalogFailed => partyCooldownCatalogTask?.IsFaulted == true;

    private bool NormalizePartyCooldownConfiguration()
    {
        var changed = false;
        if (Configuration.PartyCooldownActions is null)
        {
            Configuration.PartyCooldownActions = [];
            changed = true;
        }
        foreach (var entry in Configuration.PartyCooldownActions.ToArray())
        {
            var normalized = entry.Value?.Where(id => id != 0).Distinct().Take(12).ToList() ?? [];
            if (entry.Value is null || !entry.Value.SequenceEqual(normalized))
            {
                Configuration.PartyCooldownActions[entry.Key] = normalized;
                changed = true;
            }
        }
        var size = PartyCooldownLayout.IconSize(Configuration.PartyCooldownIconSize);
        var gap = PartyCooldownLayout.Gap(Configuration.PartyCooldownIconGap);
        var x = PartyCooldownLayout.Offset(Configuration.PartyCooldownOffsetX);
        var y = PartyCooldownLayout.Offset(Configuration.PartyCooldownOffsetY);
        changed |= size != Configuration.PartyCooldownIconSize || gap != Configuration.PartyCooldownIconGap ||
            x != Configuration.PartyCooldownOffsetX || y != Configuration.PartyCooldownOffsetY;
        Configuration.PartyCooldownIconSize = size;
        Configuration.PartyCooldownIconGap = gap;
        Configuration.PartyCooldownOffsetX = x;
        Configuration.PartyCooldownOffsetY = y;
        if (!Enum.IsDefined(Configuration.PartyCooldownSide))
        {
            Configuration.PartyCooldownSide = PartyCooldownSide.Right;
            changed = true;
        }
        return changed;
    }

    private PartyCooldownCatalog BuildPartyCooldownCatalog()
    {
        _ = PartyCooldownLabels.Timer(default);
        var defaults = PossibleMitigationDefinitions.Select(definition => definition.ActionId).ToHashSet();
        var actions = new List<PartyCooldownAction>();
        var sheet = DataManager.GetExcelSheet<LuminaAction>();
        if (sheet is null)
            throw new InvalidOperationException("The game's Action sheet is unavailable.");
        foreach (var action in sheet)
        {
            if (disposing)
                break;
            if ((!action.IsPlayerAction && !PartyCooldownSelectionPolicy.IsExecutionVariant(action.RowId)) ||
                action.IsPvP || action.Icon == 0 || action.ClassJobLevel == 0 ||
                action.CooldownGroup is 0 or 58 || action.Recast100ms == 0 ||
                action.ClassJobCategory.ValueNullable is not { } category)
                continue;
            var jobs = PartyCooldownCatalog.Jobs.Where(job => CooldownCategoryIncludesJob(category, job.Id))
                .Select(job => job.Id).ToArray();
            if (jobs.Length == 0)
                continue;
            var name = action.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;
            actions.Add(new PartyCooldownAction(action.RowId, name, action.Icon, action.ClassJobLevel,
                action.CooldownGroup, action.Recast100ms / 10f, action.MaxCharges, jobs,
                defaults.Contains(action.RowId), action.CanTargetParty, GetConditionalCooldownReason(action.RowId),
                !PartyCooldownSelectionPolicy.Includes(action.RowId) || PartyCooldownSelectionPolicy.IsExecutionVariant(action.RowId)));
        }
        return new PartyCooldownCatalog(actions);
    }

    private static bool CooldownCategoryIncludesJob(ClassJobCategory category, uint jobId) => jobId switch
    {
        19 => category.PLD,
        21 => category.WAR,
        32 => category.DRK,
        37 => category.GNB,
        24 => category.WHM,
        28 => category.SCH,
        33 => category.AST,
        40 => category.SGE,
        20 => category.MNK,
        22 => category.DRG,
        30 => category.NIN,
        34 => category.SAM,
        39 => category.RPR,
        41 => category.VPR,
        23 => category.BRD,
        31 => category.MCH,
        38 => category.DNC,
        25 => category.BLM,
        27 => category.SMN,
        35 => category.RDM,
        42 => category.PCT,
        36 => category.BLU,
        43 => category.Unknown0,
        _ => false,
    };

    private static string? GetConditionalCooldownReason(uint actionId) => actionId switch
    {
        52 => "Infuriate's recharge can be reduced by other attacks. Party tracking is not available yet.",
        110 or 117 or 36975 => "Song procs change this recharge. Party tracking is not available yet.",
        2874 or 2890 or 36979 or 36980 => "Overheated attacks change this recharge. Party tracking is not available yet.",
        2259 or 2261 or 2263 => "Mudra recharge needs Ninjutsu-specific tracking. Party tracking is not available yet.",
        2262 or 36926 => "Other actions can reset or reduce this movement cooldown. Party tracking is not available yet.",
        44890 or 47092 or 44895 or >= 47238 and <= 47245 => "Summoning a familiar can reset this cooldown. Party tracking is not available yet.",
        _ => null,
    };

    internal IReadOnlyList<uint> GetPartyCooldownSelection(uint jobId)
    {
        var catalog = GetPartyCooldownCatalog();
        jobId = PartyCooldownCatalog.NormalizeJob(jobId);
        return Configuration.PartyCooldownActions.TryGetValue(jobId, out var selected)
            ? selected : catalog?.DefaultSelection(jobId) ?? [];
    }

    internal void SetPartyCooldownSelection(uint jobId, IEnumerable<uint>? actionIds)
    {
        if (GetPartyCooldownCatalog() is not { } catalog)
            return;
        Configuration.PartyCooldownActions[PartyCooldownCatalog.NormalizeJob(jobId)] = catalog.NormalizeSelection(jobId, actionIds);
        SaveConfiguration();
        partyCooldownRosterInvalidated = true;
        nextPartyCooldownRosterAt = 0;
        nextPartyCooldownDisplayAt = 0;
    }

    internal void SetPartyCooldownVisibility(bool visible)
    {
        Configuration.ShowPartyCooldowns = visible;
        if (!visible)
            ClearPartyCooldownTracking();
        nextPartyCooldownRosterAt = 0;
        SaveConfiguration();
    }

    private static double PartyCooldownNow => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private unsafe void CapturePartyCooldownAction(uint entityId, ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects, GameObjectId* targets)
    {
        if (!Configuration.ShowPartyCooldowns || disposing || header is null || entityId is 0 or InvalidActorEntityId ||
            header->ActionType != (byte)ActionType.Action || partyCooldownCatalogFailureReported)
            return;
        using var sample = MeasurePartyCooldown(PartyCooldownPhase.Capture);
        CaptureTemperaDamage(header, effects, targets);
        var catalog = Volatile.Read(ref loadedPartyCooldownCatalog);
        if (header->ActionId != TemperaCooldownTracker.GrassaAction &&
            catalog is not null && catalog.Find(header->ActionId) is not { UnsupportedReason: null })
            return;
        Volatile.Read(ref partyCooldownActors).TryGetValue(entityId, out var actor);
        if (actor is not null && header->ActionId != TemperaCooldownTracker.GrassaAction && !actor.Profiles.ContainsKey(header->ActionId))
            return;
        var observation = new PartyCooldownObservation(entityId, header->ActionId, header->GlobalSequence, PartyCooldownNow)
        {
            MemberKey = actor?.Key ?? 0,
            TerritoryId = ClientState.TerritoryType,
        };
        EnqueuePartyCooldownObservation(observation);
    }

    private void EnqueuePartyCooldownObservation(PartyCooldownObservation observation)
    {
        lock (partyCooldownQueueLock)
        {
            if (partyCooldownObservations.Count >= 256)
            {
                partyCooldownObservations.Dequeue();
                partyCooldownQueueOverflowed = true;
            }
            partyCooldownObservations.Enqueue(observation);
        }
    }

    private void UpdatePartyCooldownTracking()
    {
        using (MeasurePartyCooldown(PartyCooldownPhase.Update))
            UpdatePartyCooldownTrackingCore();
        MaybeLogPartyCooldownPerformance();
    }

    private void UpdatePartyCooldownTrackingCore()
    {
        try
        {
            if (!Configuration.ShowPartyCooldowns || !ClientState.IsLoggedIn || IsPvPCaptureBlocked())
            {
                if (partyCooldownActors.Count > 0 || partyCooldownMemberKeys.Count > 0 || partyCooldownLifecycle.HasPendingEntry)
                    ClearPartyCooldownTracking();
                return;
            }
            var now = PartyCooldownNow;
            if (partyCooldownTerritory != ClientState.TerritoryType)
            {
                partyCooldownLifecycle.EnterTerritory(ClientState.TerritoryType);
                // UI/entity IDs can change on zoning; content-ID cooldown history must not reset with them.
                Volatile.Write(ref partyCooldownActors, []);
                partyCooldownRosterInvalidated = true;
                nextPartyCooldownRosterAt = 0;
                partyCooldownTerritory = ClientState.TerritoryType;
            }
            if (GetPartyCooldownCatalog() is not { } catalog)
                return;
            if (now >= nextPartyCooldownRosterAt)
            {
                RefreshPartyCooldownRoster(catalog, now);
                nextPartyCooldownRosterAt = now + .25;
            }
            var observationCount = 0;
            var overflowed = false;
            lock (partyCooldownQueueLock)
            {
                overflowed = partyCooldownQueueOverflowed;
                partyCooldownQueueOverflowed = false;
                while (partyCooldownObservations.TryDequeue(out var observation))
                    partyCooldownDrain[observationCount++] = observation;
            }
            if (overflowed)
            {
                partyCooldownTracker.Clear();
                temperaCooldownTracker.Clear();
                nextPartyCooldownDisplayAt = 0;
            }
            var rosterCheckedForBatch = false;
            for (var index = 0; index < observationCount; index++)
            {
                var observation = partyCooldownDrain[index];
                if (observation.TerritoryId != ClientState.TerritoryType)
                    continue;
                var entityId = observation.EntityId;
                var actor = partyCooldownActors.GetValueOrDefault(entityId);
                if (actor is null && observation.MemberKey != 0)
                {
                    foreach (var candidate in partyCooldownActors)
                        if (candidate.Value.Key == observation.MemberKey)
                        {
                            entityId = candidate.Key;
                            actor = candidate.Value;
                            break;
                        }
                }
                // Preserve a joining member's first use without rescanning for nearby non-party casts.
                if (actor is null && !rosterCheckedForBatch && IsCurrentPartyCooldownMember(observation.EntityId))
                {
                    RefreshPartyCooldownRoster(catalog, now);
                    nextPartyCooldownRosterAt = now + .25;
                    rosterCheckedForBatch = true;
                    actor = partyCooldownActors.GetValueOrDefault(observation.EntityId);
                }
                if (actor is null || observation.MemberKey != 0 && observation.MemberKey != actor.Key)
                    continue;
                if (observation.Tempera is { } tempera)
                {
                    temperaCooldownTracker.Observe(actor.Key, tempera);
                    if (tempera.Kind != TemperaEventKind.Damage)
                        RefreshPartyCooldownDisplay(entityId, actor, now, TemperaCooldownTracker.CoatAction);
                    continue;
                }
                if (observation.ActionId == TemperaCooldownTracker.GrassaAction)
                {
                    temperaCooldownTracker.Observe(actor.Key, new(TemperaEventKind.Conversion, observation.SeenAt));
                    RefreshPartyCooldownDisplay(entityId, actor, now, TemperaCooldownTracker.CoatAction);
                    continue;
                }
                if (actor.Profiles.TryGetValue(observation.ActionId, out var profile))
                {
                    var accepted = partyCooldownTracker.Observe(actor.Key, profile, observation.SeenAt, observation.Sequence);
                    if (accepted)
                    {
                        if (observation.ActionId == TemperaCooldownTracker.CoatAction)
                            temperaCooldownTracker.Cast(actor.Key, observation.SeenAt);
                        RefreshPartyCooldownDisplay(entityId, actor, now, observation.ActionId);
                    }
                    partyCooldownLifecycle.Observe(actor.Key, profile.Group, observation.TerritoryId);
                }
            }
            if (partyCooldownLifecycle.HasPendingEntry)
            {
                foreach (var actor in partyCooldownActors.Values)
                    foreach (var profile in actor.Profiles.Values)
                        if (partyCooldownLifecycle.CanConfirmReadyAtEntry(actor.Key, profile.Group, ClientState.TerritoryType))
                            partyCooldownTracker.ConfirmReady(actor.Key, profile, now);
                partyCooldownLifecycle.EndEntry();
                nextPartyCooldownDisplayAt = 0;
            }
            ResolveTemperaCooldowns(now);
            if (now >= nextPartyCooldownDisplayAt)
            {
                RefreshPartyCooldownDisplay(PartyCooldownNow);
                nextPartyCooldownDisplayAt = now + .1;
            }
            partyCooldownUpdateFailureReported = false;
        }
        catch (Exception error)
        {
            if (!partyCooldownUpdateFailureReported)
            {
                partyCooldownUpdateFailureReported = true;
                Log.Warning(error, "Could not update party cooldowns.");
            }
        }
    }

    private void RefreshPartyCooldownRoster(PartyCooldownCatalog catalog, double now)
    {
        Span<PartyCooldownRosterMember> roster = stackalloc PartyCooldownRosterMember[9];
        var count = 0;
        var local = ObjectTable.LocalPlayer;
        var includesLocal = false;
        foreach (var member in PartyList)
        {
            if (count >= roster.Length)
                break;
            var key = member.ContentId != 0 ? member.ContentId : 0xF000000000000000ul | member.EntityId;
            if (member.ContentId == 0 && member.EntityId == 0)
                continue;
            roster[count++] = new(key, member.EntityId, PartyCooldownCatalog.NormalizeJob(member.ClassJob.RowId), member.Level);
            includesLocal |= member.EntityId == local?.EntityId;
        }
        if (local is not null && !includesLocal && count < roster.Length)
        {
            var key = 0xF000000000000000ul | local.EntityId;
            roster[count++] = new(key, local.EntityId, PartyCooldownCatalog.NormalizeJob(local.ClassJob.RowId), local.Level);
        }
        if (!partyCooldownRosterInvalidated && count == partyCooldownRosterCount &&
            roster[..count].SequenceEqual(partyCooldownRoster.AsSpan(0, count)))
            return;
        var next = new Dictionary<uint, PartyCooldownActor>(count);
        var members = new HashSet<ulong>(count);
        for (var index = 0; index < count; index++)
        {
            var member = roster[index];
            members.Add(member.Key);
            if (member.EntityId is > 0 and not InvalidActorEntityId)
                AddPartyCooldownActor(next, catalog, member.Key, member.EntityId, member.Job, member.Level, now);
        }
        if (members.Count == 0 && local is null)
        {
            Volatile.Write(ref partyCooldownActors, next);
            partyCooldownRosterInvalidated = true;
            return;
        }
        if (!partyCooldownMemberKeys.SetEquals(members))
        {
            partyCooldownTracker.RetainMembers(members);
            temperaCooldownTracker.RetainMembers(members);
            foreach (var key in partyCooldownContexts.Keys.Where(key => !members.Contains(key)).ToArray())
                partyCooldownContexts.Remove(key);
            partyCooldownLifecycle.RetainMembers(members);
            partyCooldownMemberKeys = members;
        }
        // The receive hook only reads these dictionaries; publish replacements rather than mutate them.
        Volatile.Write(ref partyCooldownActors, next);
        roster[..count].CopyTo(partyCooldownRoster);
        partyCooldownRosterCount = count;
        partyCooldownRosterInvalidated = false;
        nextPartyCooldownDisplayAt = 0;
    }

    private bool IsCurrentPartyCooldownMember(uint entityId)
    {
        if (ObjectTable.LocalPlayer?.EntityId == entityId)
            return true;
        foreach (var member in PartyList)
            if (member.EntityId == entityId)
                return true;
        return false;
    }

    private unsafe void AddPartyCooldownActor(Dictionary<uint, PartyCooldownActor> next, PartyCooldownCatalog catalog,
        ulong key, uint entityId, uint jobId, byte level, double now)
    {
        jobId = PartyCooldownCatalog.NormalizeJob(jobId);
        var selection = GetPartyCooldownSelection(jobId);
        if (partyCooldownActors.TryGetValue(entityId, out var previous) && previous.Key == key &&
            previous.JobId == jobId && previous.Level == level && previous.Selection.SequenceEqual(selection))
        {
            next[entityId] = previous;
            return;
        }
        if (partyCooldownContexts.TryGetValue(key, out var context) && context != (jobId, level))
        {
            partyCooldownTracker.Reset(key);
            temperaCooldownTracker.Reset(key);
        }
        partyCooldownContexts[key] = (jobId, level);
        var profiles = new Dictionary<uint, PartyCooldownProfile>();
        foreach (var action in catalog.CaptureActionsForJob(jobId))
        {
            if (action.Level > level || action.UnsupportedReason is not null)
                continue;
            var charges = Math.Clamp((int)ActionManager.GetMaxCharges(action.Id, level), 1, 5);
            profiles[action.Id] = new PartyCooldownProfile(action.Id, action.Group,
                PartyCooldownCatalog.RecastAtLevel(action.Id, level, action.RecastSeconds), charges);
        }
        foreach (var group in profiles.Values.GroupBy(profile => profile.Group).ToArray())
        {
            var charges = group.Max(profile => profile.MaxCharges);
            foreach (var profile in group)
                profiles[profile.ActionId] = profile with { MaxCharges = charges };
        }
        var visible = selection.Select(id => catalog.AtLevel(jobId, id, level)).OfType<PartyCooldownAction>()
            .Where(action => profiles.ContainsKey(action.Id)).DistinctBy(action => action.Id).ToArray();
        var actor = new PartyCooldownActor(key, jobId, level, profiles, selection.ToArray(), visible, new(visible));
        foreach (var profile in profiles.Values)
            _ = partyCooldownTracker.Get(key, profile, now);
        next[entityId] = actor;
    }

    private void ClearPartyCooldownTracking()
    {
        Volatile.Write(ref partyCooldownActors, []);
        partyCooldownTracker.Clear();
        temperaCooldownTracker.Clear();
        partyCooldownMemberKeys.Clear();
        partyCooldownContexts.Clear();
        partyCooldownLifecycle.Clear();
        partyCooldownRosterInvalidated = true;
        partyCooldownRosterCount = 0;
        lock (partyCooldownQueueLock)
        {
            partyCooldownObservations.Clear();
            partyCooldownQueueOverflowed = false;
        }
        nextPartyCooldownRosterAt = 0;
        nextPartyCooldownDisplayAt = 0;
    }

    private void ConfirmPartyCooldownDutyReset()
    {
        var now = PartyCooldownNow;
        lock (partyCooldownQueueLock)
        {
            partyCooldownObservations.Clear();
            partyCooldownQueueOverflowed = false;
        }
        foreach (var actor in partyCooldownActors.Values)
            foreach (var profile in actor.Profiles.Values)
                partyCooldownTracker.ConfirmReady(actor.Key, profile, now);
        partyCooldownLifecycle.Clear();
        temperaCooldownTracker.Clear();
        nextPartyCooldownDisplayAt = 0;
    }

    private void OnPartyCooldownDutyReset(IDutyStateEventArgs args)
    {
        try
        {
            ConfirmPartyCooldownDutyReset();
        }
        catch (Exception error)
        {
            Log.Debug(error, "Could not reset party cooldowns after a duty reset.");
        }
    }

    private void OnPartyCooldownDutyStarted(IDutyStateEventArgs args)
    {
        if (Configuration.ShowPartyCooldowns)
            partyCooldownLifecycle.BeginDuty(ClientState.TerritoryType);
    }

    internal int CopyPartyCooldownDisplay(uint entityId, Span<PartyCooldownDisplayEntry> destination) =>
        Volatile.Read(ref partyCooldownActors).TryGetValue(entityId, out var actor) ? actor.Display.CopyTo(destination) : 0;

    internal int PartyCooldownActorCount => Volatile.Read(ref partyCooldownActors).Count;

    private void RefreshPartyCooldownDisplay(double now)
    {
        foreach (var pair in partyCooldownActors)
            RefreshPartyCooldownDisplay(pair.Key, pair.Value, now);
    }

    private unsafe void RefreshPartyCooldownDisplay(uint entityId, PartyCooldownActor actor, double now, uint? usedAction = null)
    {
        var group = usedAction is { } action && actor.Profiles.TryGetValue(action, out var usedProfile) ? usedProfile.Group : 0;
        var ownEntity = ObjectTable.LocalPlayer?.EntityId ?? 0;
        var manager = entityId == ownEntity ? ActionManager.Instance() : null;
        for (var index = 0; index < actor.Visible.Length; index++)
        {
            var visible = actor.Visible[index];
            if (group != 0 && visible.Group != group)
                continue;
            actor.Display.Update(index, CalculatePartyCooldownStatus(actor, visible, now, manager));
        }
    }

    private unsafe PartyCooldownStatus CalculatePartyCooldownStatus(PartyCooldownActor actor, PartyCooldownAction action, double now,
        ActionManager* manager)
    {
        if (!actor.Profiles.TryGetValue(action.Id, out var profile))
            return default;
        if (manager is not null)
        {
            var group = manager->GetRecastGroup((int)ActionType.Action, action.Id);
            var detail = group >= 0 ? manager->GetRecastGroupDetail(group) : null;
            var charges = (int)manager->GetCurrentCharges(action.Id);
            return PartyCooldownStatus.FromNative(charges, profile.MaxCharges,
                manager->GetRecastTimeElapsed(ActionType.Action, action.Id),
                detail is not null && detail->IsActive ? manager->GetRecastTime(ActionType.Action, action.Id) : 0);
        }
        var status = partyCooldownTracker.Get(actor.Key, profile, now);
        return action.Id == TemperaCooldownTracker.CoatAction && status.Charges == 0 && temperaCooldownTracker.IsUncertain(actor.Key, now)
            ? status with { IsKnown = false } : status;
    }

    private sealed record PartyCooldownActor(ulong Key, uint JobId, byte Level,
        Dictionary<uint, PartyCooldownProfile> Profiles, IReadOnlyList<uint> Selection, PartyCooldownAction[] Visible,
        PartyCooldownDisplayRow Display);
}
