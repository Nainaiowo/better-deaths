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
using NativePartyMember = FFXIVClientStructs.FFXIV.Client.Game.Group.PartyMember;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private const double PartyCooldownRosterInterval = 1;
    private readonly PartyCooldownTracker partyCooldownTracker = new();
    private readonly object partyCooldownQueueLock = new();
    private readonly Queue<PartyCooldownObservation> partyCooldownObservations = new(256);
    private readonly PartyCooldownObservation[] partyCooldownDrain = new PartyCooldownObservation[256];
    private Dictionary<uint, PartyCooldownActor> partyCooldownActors = [];
    private bool partyCooldownHasTempera;
    private HashSet<ulong> partyCooldownMemberKeys = [];
    private readonly Dictionary<ulong, (uint Job, byte Level)> partyCooldownContexts = [];
    private PartyCooldownCatalog? loadedPartyCooldownCatalog;
    private readonly PartyCooldownRosterMember[] partyCooldownRoster = new PartyCooldownRosterMember[9];
    private int partyCooldownRosterCount;
    private bool partyCooldownRosterInvalidated = true;
    private int partyCooldownLayoutRevision;
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
                foreach (var entry in Configuration.PartyCooldownRowLayouts.ToArray())
                {
                    if (entry.Value is null)
                    {
                        Configuration.PartyCooldownRowLayouts.Remove(entry.Key);
                        changed = true;
                        continue;
                    }
                    var selection = Configuration.PartyCooldownActions.GetValueOrDefault(entry.Key) ??
                        partyCooldownCatalogTask.Result.DefaultSelection(entry.Key);
                    changed |= entry.Value.Normalize(selection);
                    if (entry.Value.Enabled && !selection.SequenceEqual(entry.Value.Flatten()))
                    {
                        Configuration.PartyCooldownActions[entry.Key] = entry.Value.Flatten();
                        changed = true;
                    }
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
        if (Configuration.PartyCooldownRowLayouts is null)
        {
            Configuration.PartyCooldownRowLayouts = [];
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
        if (Configuration.PartyCooldownPlacements is null)
        {
            Configuration.PartyCooldownPlacements = new();
            changed = true;
        }
        changed |= Configuration.PartyCooldownPlacements.Normalize(new()
        {
            IconSize = size,
            Spacing = gap,
            OffsetX = x,
            OffsetY = y,
        });
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
            var displayId = PartyCooldownSelectionPolicy.DisplayAction(action.RowId);
            var display = displayId != action.RowId ? sheet.GetRowOrDefault(displayId) ?? action : action;
            var name = display.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;
            actions.Add(new PartyCooldownAction(action.RowId, name, display.Icon, action.ClassJobLevel,
                action.CooldownGroup, action.Recast100ms / 10f, action.MaxCharges, jobs,
                defaults.Contains(action.RowId), action.CanTargetParty, PartyCooldownSelectionPolicy.UnsupportedReason(action.RowId),
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
        jobId = PartyCooldownCatalog.NormalizeJob(jobId);
        var selection = catalog.NormalizeSelection(jobId, actionIds);
        Configuration.PartyCooldownActions[jobId] = selection;
        if (actionIds is null)
            Configuration.PartyCooldownRowLayouts.Remove(jobId);
        else if (Configuration.PartyCooldownRowLayouts.TryGetValue(jobId, out var rows))
        {
            rows.Normalize(selection);
            if (rows.Enabled)
                Configuration.PartyCooldownActions[jobId] = rows.Flatten();
        }
        SavePartyCooldownLayout();
    }

    internal PartyCooldownRows GetPartyCooldownRows(uint jobId)
    {
        jobId = PartyCooldownCatalog.NormalizeJob(jobId);
        if (!Configuration.PartyCooldownRowLayouts.TryGetValue(jobId, out var rows))
        {
            rows = new();
            rows.Normalize(GetPartyCooldownSelection(jobId));
            Configuration.PartyCooldownRowLayouts[jobId] = rows;
        }
        return rows;
    }

    internal void SetPartyCooldownRows(uint jobId, PartyCooldownRows rows)
    {
        if (GetPartyCooldownCatalog() is not { } catalog)
            return;
        jobId = PartyCooldownCatalog.NormalizeJob(jobId);
        var selection = catalog.NormalizeSelection(jobId, rows.Flatten());
        rows.Normalize(selection);
        Configuration.PartyCooldownRowLayouts[jobId] = rows;
        Configuration.PartyCooldownActions[jobId] = rows.Flatten();
        SavePartyCooldownLayout();
    }

    private void SavePartyCooldownLayout()
    {
        partyCooldownLayoutRevision++;
        SaveConfiguration();
        partyCooldownRosterInvalidated = true;
        nextPartyCooldownRosterAt = 0;
        InvalidatePartyCooldownDisplay();
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
                PublishPartyCooldownActors([]);
                partyCooldownRosterInvalidated = true;
                nextPartyCooldownRosterAt = 0;
                partyCooldownTerritory = ClientState.TerritoryType;
            }
            if (GetPartyCooldownCatalog() is not { } catalog)
                return;
            if (now >= nextPartyCooldownRosterAt)
            {
                RefreshPartyCooldownRoster(catalog, now);
                nextPartyCooldownRosterAt = now + PartyCooldownRosterInterval;
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
                nextTemperaResolutionAt = double.PositiveInfinity;
                InvalidatePartyCooldownDisplay();
            }
            var rosterCheckedForBatch = false;
            for (var index = 0; index < observationCount; index++)
            {
                var observation = partyCooldownDrain[index];
                if (observation.TerritoryId != ClientState.TerritoryType)
                    continue;
                // Confirmed terminal evidence belongs to the member even while their live entity is unavailable.
                if (observation.MemberKey != 0 && partyCooldownMemberKeys.Contains(observation.MemberKey) &&
                    observation.Tempera is { } terminal && TemperaCooldownTracker.IsTerminal(terminal))
                    temperaCooldownTracker.Observe(observation.MemberKey, terminal);
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
                    nextPartyCooldownRosterAt = now + PartyCooldownRosterInterval;
                    rosterCheckedForBatch = true;
                    actor = partyCooldownActors.GetValueOrDefault(observation.EntityId);
                }
                if (actor is null || observation.MemberKey != 0 && observation.MemberKey != actor.Key)
                    continue;
                if (observation.Tempera is { } tempera)
                {
                    temperaCooldownTracker.Observe(actor.Key, tempera);
                    ScheduleTemperaCooldownCheck(actor.Key);
                    if (tempera.Kind != TemperaEventKind.Damage)
                        RefreshPartyCooldownDisplay(entityId, actor, now, TemperaCooldownTracker.CoatAction);
                    continue;
                }
                if (observation.ActionId == TemperaCooldownTracker.GrassaAction)
                {
                    temperaCooldownTracker.Observe(actor.Key, new(TemperaEventKind.Conversion, observation.SeenAt));
                    ScheduleTemperaCooldownCheck(actor.Key);
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
                InvalidatePartyCooldownDisplay();
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
        var memberCount = PartyList.Length;
        for (var index = 0; index < memberCount && count < roster.Length; index++)
        {
            if (!TryReadPartyCooldownMember(index, memberCount, out var member))
                continue;
            roster[count++] = member;
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
            PublishPartyCooldownActors(next);
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
        PublishPartyCooldownActors(next);
        roster[..count].CopyTo(partyCooldownRoster);
        partyCooldownRosterCount = count;
        partyCooldownRosterInvalidated = false;
        InvalidatePartyCooldownDisplay();
    }

    private bool IsCurrentPartyCooldownMember(uint entityId)
    {
        if (ObjectTable.LocalPlayer?.EntityId == entityId)
            return true;
        var count = PartyList.Length;
        for (var index = 0; index < count; index++)
            if (TryReadPartyCooldownMember(index, count, out var member) && member.EntityId == entityId)
                return true;
        return false;
    }

    private unsafe bool TryReadPartyCooldownMember(int index, int count, out PartyCooldownRosterMember member)
    {
        member = default;
        var address = count > 8 ? PartyList.GetAllianceMemberAddress(index) : PartyList.GetPartyMemberAddress(index);
        if (address == 0)
            return false;
        // The same bound native structure used by Dalamud, without boxing a party-member wrapper.
        var native = (NativePartyMember*)address;
        var entity = native->EntityId;
        var content = native->ContentId;
        if (content == 0 && entity == 0)
            return false;
        var key = content != 0 ? content : 0xF000000000000000ul | entity;
        member = new(key, entity, PartyCooldownCatalog.NormalizeJob(native->ClassJob), native->Level);
        return true;
    }

    private void PublishPartyCooldownActors(Dictionary<uint, PartyCooldownActor> actors)
    {
        Volatile.Write(ref partyCooldownActors, actors);
        Volatile.Write(ref partyCooldownHasTempera,
            actors.Values.Any(actor => actor.JobId == 42 && actor.Profiles.ContainsKey(TemperaCooldownTracker.CoatAction)));
        nextTemperaResolutionAt = double.PositiveInfinity;
        foreach (var actor in actors.Values)
            ScheduleTemperaCooldownCheck(actor.Key);
    }

    private unsafe void AddPartyCooldownActor(Dictionary<uint, PartyCooldownActor> next, PartyCooldownCatalog catalog,
        ulong key, uint entityId, uint jobId, byte level, double now)
    {
        jobId = PartyCooldownCatalog.NormalizeJob(jobId);
        var selection = GetPartyCooldownSelection(jobId);
        if (partyCooldownActors.TryGetValue(entityId, out var previous) && previous.Key == key &&
            previous.JobId == jobId && previous.Level == level && previous.LayoutRevision == partyCooldownLayoutRevision &&
            previous.Selection.SequenceEqual(selection))
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
        var visible = new List<PartyCooldownAction>(selection.Count);
        var seen = new HashSet<uint>();
        Span<byte> rowIndices = stackalloc byte[PartyCooldownRows.MaximumAbilities];
        Configuration.PartyCooldownRowLayouts.TryGetValue(jobId, out var rows);
        foreach (var id in selection)
            if (catalog.AtLevel(jobId, id, level) is { } action && profiles.ContainsKey(action.Id) && seen.Add(action.Id))
            {
                rowIndices[visible.Count] = rows?.RowFor(id) ?? 0;
                visible.Add(action);
            }
        var actions = visible.ToArray();
        var actor = new PartyCooldownActor(key, jobId, level, partyCooldownLayoutRevision, profiles, selection.ToArray(), actions,
            new(actions, rowIndices[..actions.Length]));
        foreach (var profile in profiles.Values)
            _ = partyCooldownTracker.Get(key, profile, now);
        next[entityId] = actor;
    }

    private void ClearPartyCooldownTracking()
    {
        PublishPartyCooldownActors([]);
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
        InvalidatePartyCooldownDisplay();
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
        nextTemperaResolutionAt = double.PositiveInfinity;
        InvalidatePartyCooldownDisplay();
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

    private void InvalidatePartyCooldownDisplay()
    {
        foreach (var actor in partyCooldownActors.Values)
            actor.Display.Invalidate();
        nextPartyCooldownDisplayAt = 0;
    }

    private unsafe void RefreshPartyCooldownDisplay(double now)
    {
        var ownEntity = ObjectTable.LocalPlayer?.EntityId ?? 0;
        var manager = ownEntity != 0 ? ActionManager.Instance() : null;
        foreach (var pair in partyCooldownActors)
            RefreshPartyCooldownDisplay(pair.Value, now, pair.Key == ownEntity ? manager : null);
    }

    private unsafe void RefreshPartyCooldownDisplay(uint entityId, PartyCooldownActor actor, double now, uint? usedAction = null)
    {
        var ownEntity = ObjectTable.LocalPlayer?.EntityId ?? 0;
        var manager = entityId == ownEntity ? ActionManager.Instance() : null;
        RefreshPartyCooldownDisplay(actor, now, manager, usedAction);
    }

    private unsafe void RefreshPartyCooldownDisplay(PartyCooldownActor actor, double now, ActionManager* manager, uint? usedAction = null)
    {
        var group = usedAction is { } action && actor.Profiles.TryGetValue(action, out var usedProfile) ? usedProfile.Group : 0;
        // Own hotbar reads and captured uses bypass the settled teammate cache.
        var refreshMask = manager is not null || usedAction is not null ? uint.MaxValue : actor.Display.TimerRefreshMask;
        for (var index = 0; index < actor.Visible.Length; index++)
        {
            var visible = actor.Visible[index];
            if ((refreshMask & (1u << index)) == 0 || group != 0 && visible.Group != group)
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

    private sealed record PartyCooldownActor(ulong Key, uint JobId, byte Level, int LayoutRevision,
        Dictionary<uint, PartyCooldownProfile> Profiles, IReadOnlyList<uint> Selection, PartyCooldownAction[] Visible,
        PartyCooldownDisplayRow Display);
}
