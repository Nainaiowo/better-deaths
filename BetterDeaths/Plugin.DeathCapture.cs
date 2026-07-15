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

public sealed partial class Plugin
{
    private readonly record struct DeathCaptureContext(bool EnvironmentSourceDeath);

    private sealed record PendingEffectResult(
        RawEffectResultPacket Packet,
        uint ShieldHp,
        IReadOnlyList<StatusSnapshot> ResultStatuses);

    private void RefreshPartyState()
    {
        var territoryId = ClientState.TerritoryType;
        if (territoryId != currentTerritoryId)
        {
            ArchiveCurrentPullForReview("Left territory", suppressResetStateDeaths: false);
            currentTerritoryId = territoryId;
            currentTerritoryName = GetTerritoryName(territoryId);
            ClearCurrentDutyInstancePullGroup();
        }

        if (IsPvPCaptureBlocked())
        {
            ResetCurrentPull(suppressResetStateDeaths: false);
            currentMembers.Clear();
            ClearCurrentDutyInstancePullGroup();
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

        EnsureCurrentDutyInstancePullGroup();

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

    private void ResolveRawActionEffectPacket(RawActionEffectPacket packet)
    {
        TrackPossibleMitigationActionUse(packet);
        CaptureReplayBlackHoleBlast(packet);
        ResolveActiveReplayMechanicsForAction(packet);
        CaptureReplayDmuP2ForsakenAction(packet);
        CaptureReplayDmuP3Action(packet);

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
        else
        {
            CaptureActorControlSourceMitigationStatusChange(packet);
        }

        if (packet.Category == ActorControlDeathCategory && member is not null)
        {
            var bestKnownStatuses = GetBestKnownStatuses(member, packet.SeenAtUtc);
            var deathContext = CreateActorControlDeathContext(packet, member);
            TryCaptureDeath(
                member with
                {
                    CurrentHp = 0,
                    ShieldHp = 0,
                    IsDead = true,
                    Statuses = bestKnownStatuses,
                },
                packet.SeenAtUtc,
                "ActorControl",
                deathContext);
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
        return currentMembers.FirstOrDefault(member =>
            TargetMatchesMember(targetId, member));
    }

    private PartyMemberSnapshot? FindCurrentMemberByTargetId(RawTargetId targetId)
    {
        return currentMembers.FirstOrDefault(member =>
            TargetMatchesMember(targetId, member));
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
            : currentMembers.FirstOrDefault(member =>
                member.EntityId != 0 &&
                member.EntityId == entityId);
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

    private void AddRecentReplayMarkerSnapshot(ReplayMarkerSnapshot snapshot, TimeSpan? duplicateWindow = null)
    {
        if (!recentReplayMarkersByActor.TryGetValue(snapshot.ActorKey, out var history))
        {
            history = [];
            recentReplayMarkersByActor[snapshot.ActorKey] = history;
        }

        var suppressWindow = duplicateWindow ?? TimeSpan.FromMilliseconds(50);
        var last = history.Count == 0 ? null : history[^1];
        if (last is not null &&
            last.MarkerId == snapshot.MarkerId &&
            last.RawMarkerId == snapshot.RawMarkerId &&
            Duration(last.SeenAtUtc, snapshot.SeenAtUtc) <= suppressWindow)
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

    private void CaptureActorControlSourceMitigationStatusChange(RawActorControlPacket packet)
    {
        if (packet.Category is not ActorControlGainEffectCategory and not ActorControlUpdateEffectCategory)
        {
            return;
        }

        if (!TryGetSourceMitigationActor(packet.EntityId, out var sourceName))
        {
            return;
        }

        var sourceStatuses = BuildSourceStatusSnapshots(packet.EntityId);
        if (!TryCreateActorControlStatusSnapshot(packet, sourceStatuses, out var status) ||
            !IsBossMitigationStatus(status))
        {
            return;
        }

        TrackRecentSourceMitigationSnapshot(
            packet.EntityId,
            sourceName,
            packet.SeenAtUtc,
            sourceStatuses.Append(status));
    }

    private void CaptureReplayOverheadStatus(RawActorControlPacket packet, PartyMemberSnapshot member, StatusSnapshot status)
    {
        if (packet.Category is not ActorControlGainEffectCategory and not ActorControlUpdateEffectCategory ||
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
            status.Id)
        {
            RemainingTime = status.RemainingTime,
        });
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

    private static DeathCaptureContext CreateActorControlDeathContext(RawActorControlPacket packet, PartyMemberSnapshot member)
    {
        var environmentSourceDeath = packet.Param1 == InvalidActorEntityId &&
            (packet.Param2 == member.EntityId || packet.Param2 == packet.EntityId);
        return new DeathCaptureContext(environmentSourceDeath);
    }

    private bool TryCaptureDeath(
        PartyMemberSnapshot member,
        DateTime deathSeenAtUtc,
        string signalSource,
        DeathCaptureContext? deathContext = null)
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
            if (deathContext is { EnvironmentSourceDeath: true } context)
            {
                ApplyEnvironmentSourceDeathToCapturedDeaths(member, deathSeenAtUtc, context);
            }

            return false;
        }

        EnsurePullStarted(deathSeenAtUtc);
        var death = CreateDeathRecord(member, deathSeenAtUtc, deathContext);
        currentDeaths.Add(death);
        if (Configuration.ShowDeathRecapPopup && IsLocalPlayer(member))
        {
            deathRecapPopupWindow.DisplayDeath(death);
        }

        QueueDeathRecapLink(death, DateTime.UtcNow);
        AddDebugLog($"{member.MemberName} died to {FormatCause(DeathDisplaySelector.Select(death).Events)} via {signalSource}.");
        return true;
    }

    private void ApplyEnvironmentSourceDeathToCapturedDeaths(
        PartyMemberSnapshot member,
        DateTime deathSeenAtUtc,
        DeathCaptureContext context)
    {
        if (!context.EnvironmentSourceDeath)
        {
            return;
        }

        var updatedDeath = ApplyEnvironmentSourceDeathToDeathList(currentDeaths, member, deathSeenAtUtc);
        _ = ApplyEnvironmentSourceDeathToDeathList(pendingDeathRecapLinks, member, deathSeenAtUtc);
        if (updatedDeath is not null &&
            !pendingDeathRecapLinks.Any(pending => DeathRecordsMatch(pending, updatedDeath)))
        {
            QueueDeathRecapLink(updatedDeath, DateTime.UtcNow);
        }
    }

    private PartyDeathRecord? ApplyEnvironmentSourceDeathToDeathList(
        List<PartyDeathRecord> deaths,
        PartyMemberSnapshot member,
        DateTime deathSeenAtUtc)
    {
        var territoryId = currentPullTerritoryId != 0
            ? currentPullTerritoryId
            : ClientState.TerritoryType;

        for (var index = 0; index < deaths.Count; index++)
        {
            var death = deaths[index];
            if (!string.Equals(death.MemberKey, member.MemberKey, StringComparison.Ordinal) ||
                Duration(death.SeenAtUtc, deathSeenAtUtc) > DeathActorControlLateMatchWindow ||
                death.EnvironmentalAssessment is { EnvironmentSourceDeath: true })
            {
                continue;
            }

            var updatedDeath = death with
            {
                LikelyCause = null,
                EnvironmentalAssessment = CreateEnvironmentalDeathAssessment(
                    member,
                    death.SeenAtUtc,
                    territoryId,
                    null,
                    death.FatalSequence,
                    death.ReplayPositions,
                    environmentSourceDeath: true),
            };
            deaths[index] = updatedDeath;
            return updatedDeath;
        }

        return null;
    }

    private static bool IsLocalPlayer(PartyMemberSnapshot member)
    {
        var localPlayer = ObjectTable.LocalPlayer;
        return localPlayer is not null &&
            localPlayer.EntityId != 0 &&
            member.EntityId == localPlayer.EntityId;
    }

    private PartyDeathRecord CreateDeathRecord(
        PartyMemberSnapshot member,
        DateTime deathSeenAtUtc,
        DeathCaptureContext? deathContext)
    {
        var events = GetRecentEvents(member.MemberKey, deathSeenAtUtc, Math.Max(Configuration.RecentEventSeconds, BetterDeathsLeadUpCaptureSeconds));
        var hpHistory = GetRecentHpHistory(member.MemberKey, deathSeenAtUtc, BetterDeathsLeadUpCaptureSeconds);
        var enemyHpAtDeath = CaptureEnemyHpSnapshotsAtDeath(deathSeenAtUtc);
        CaptureSourceMitigationSnapshotsForActiveEnemies(enemyHpAtDeath, deathSeenAtUtc);
        var sourceMitigationHistory = GetRecentSourceMitigationHistory(
            events
                .Select(combatEvent => combatEvent.SourceEntityId)
                .Concat(enemyHpAtDeath.Select(enemy => enemy.EntityId)),
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
        var environmentSourceDeath = deathContext is { EnvironmentSourceDeath: true };
        var cause = environmentSourceDeath
            ? null
            : ShouldPreferStatusCause(statusCause, eventCause)
                ? statusCause
                : eventCause ?? statusCause;
        var replayPositions = GetRecentReplayPositions(
            deathSeenAtUtc - TimeSpan.FromSeconds(DeathReplayLeadUpSeconds),
            deathSeenAtUtc);
        var territoryId = currentPullTerritoryId != 0
            ? currentPullTerritoryId
            : ClientState.TerritoryType;
        var environmentalAssessment = CreateEnvironmentalDeathAssessment(
            member,
            deathSeenAtUtc,
            territoryId,
            cause,
            fatalSequence,
            replayPositions,
            environmentSourceDeath);

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
            EnemyHpAtDeath = enemyHpAtDeath,
            PossibleMitigations = BuildPossibleMitigationSnapshotsForDeath(member, deathSeenAtUtc),
            EnvironmentalAssessment = environmentalAssessment,
            ReplayPositions = replayPositions,
            ReplayMarkers = GetReplayMarkersForDeath(deathSeenAtUtc, deathSeenAtUtc),
            ReplayMechanics = GetRecentReplayMechanics(
                deathSeenAtUtc - TimeSpan.FromSeconds(DeathReplayLeadUpSeconds),
                deathSeenAtUtc),
            ReplayWorldMarkers = GetReplayWorldMarkersForDeath(deathSeenAtUtc, deathSeenAtUtc),
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
                !battleNpc.IsTargetable ||
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
            .OrderByDescending(snapshot => snapshot.MaxHp)
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
            if (member.IsDead ||
                member.MaxHp == 0)
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

    private void TrackActiveSourceMitigationHistory(DateTime seenAtUtc)
    {
        if (!ShouldCaptureLiveCombat(seenAtUtc))
        {
            return;
        }

        foreach (var gameObject in ObjectTable)
        {
            if (gameObject is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc ||
                !IsSourceMitigationActor(battleNpc))
            {
                continue;
            }

            TrackRecentSourceMitigationSnapshot(
                battleNpc.EntityId,
                battleNpc.Name.TextValue,
                seenAtUtc,
                BuildStatusSnapshots(battleNpc.StatusList));
        }
    }

    private void CaptureSourceMitigationSnapshotsForActiveEnemies(
        IEnumerable<EnemyHpSnapshot> enemies,
        DateTime seenAtUtc)
    {
        foreach (var enemy in enemies)
        {
            if (enemy.EntityId == 0)
            {
                continue;
            }

            try
            {
                if (ObjectTable.SearchByEntityId(enemy.EntityId) is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc ||
                    !IsSourceMitigationActor(battleNpc))
                {
                    continue;
                }

                TrackRecentSourceMitigationSnapshot(
                    battleNpc.EntityId,
                    battleNpc.Name.TextValue,
                    seenAtUtc,
                    BuildStatusSnapshots(battleNpc.StatusList));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not snapshot active source mitigation for {SourceEntityId:X8}.", enemy.EntityId);
            }
        }
    }

    private static bool IsSourceMitigationActor(Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc)
    {
        return battleNpc.BattleNpcKind == Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Combatant &&
            battleNpc.EntityId != 0 &&
            battleNpc.EntityId != InvalidActorEntityId &&
            battleNpc.MaxHp > 0 &&
            battleNpc.CurrentHp > 0 &&
            battleNpc.IsTargetable &&
            !string.IsNullOrWhiteSpace(battleNpc.Name.TextValue);
    }

    private bool TryGetSourceMitigationActor(uint entityId, out string sourceName)
    {
        sourceName = string.Empty;
        if (entityId == 0)
        {
            return false;
        }

        try
        {
            if (ObjectTable.SearchByEntityId(entityId) is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc ||
                !IsSourceMitigationActor(battleNpc))
            {
                return false;
            }

            sourceName = battleNpc.Name.TextValue;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not inspect source mitigation actor {SourceEntityId:X8}.", entityId);
            return false;
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
        TrackActiveSourceMitigationHistory(now);
        PruneRecentEvents(now);
        PruneRecentCombatLogEvents(now);
        PruneRecentStatuses(now);
        PruneRecentHpHistory(now);
        PruneRecentReplayPositions(now);
        PruneRecentReplayMarkers(now);
        PruneRecentReplayMechanics(now);
        PruneRecentReplayWorldMarkers(now);
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
        return TryGetDefensiveStatusDefinition(status, out _);
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

    private sealed record StatusObservation(
        DateTime SeenAtUtc,
        float PullElapsedSeconds,
        StatusSnapshot Status,
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        IReadOnlyList<StatusSnapshot> Statuses);

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
}
