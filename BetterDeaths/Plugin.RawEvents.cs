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
    private sealed record RawActionEffectPacket(
        long Sequence,
        DateTime SeenAtUtc,
        uint ActionSequence,
        uint CasterEntityId,
        string CasterName,
        uint ActionId,
        bool HasCasterPose,
        Vector3 CasterPosition,
        float CasterRotation,
        bool HasTargetPosition,
        Vector3 TargetPosition,
        RawCombatSnapshot? SourceSnapshot,
        IReadOnlyList<RawActionEffectTarget> Targets,
        IReadOnlyList<RawActorPoseSnapshot> ReplayPoses);

    private sealed record RawActionEffectTarget(
        int TargetIndex,
        RawTargetId TargetId,
        RawCombatSnapshot? TargetSnapshot,
        IReadOnlyList<RawActionEffectSlot> Effects);

    private sealed record RawTargetId(ulong Id, uint ObjectId);

    private sealed record RawCombatSnapshot(
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        IReadOnlyList<RawStatusSnapshot> Statuses);

    private sealed record RawActorPoseSnapshot(
        DateTime SeenAtUtc,
        long PacketSequence,
        uint ActionSequence,
        int TargetIndex,
        ReplayPositionSampleSource SampleSource,
        uint EntityId,
        string ActorName,
        ReplayActorKind ActorKind,
        int PartyIndex,
        uint ClassJobId,
        string ClassJobName,
        Vector3 Position,
        float Rotation,
        uint CurrentHp,
        uint ShieldHp,
        uint MaxHp,
        bool IsDead,
        bool IsTargetable);

    private sealed record RawStatusSnapshot(
        uint StatusId,
        uint SourceId,
        ushort StackCount,
        float RemainingTime);

    private sealed record RawActionEffectSlot(
        int EffectIndex,
        byte Type,
        uint Param0,
        uint Param1,
        uint Param3,
        uint Param4,
        uint Value);

    private sealed record RawCombatLogMessage(
        long Sequence,
        DateTime SeenAtUtc,
        uint LogMessageId,
        string SourceName,
        bool SourceIsPlayer,
        string TargetName,
        bool TargetIsPlayer,
        string ActionName,
        uint Amount);

    private sealed record RawEffectResultPacket(
        long Sequence,
        DateTime SeenAtUtc,
        uint TargetId,
        uint RelatedActionSequence,
        uint ActorId,
        uint CurrentHp,
        uint MaxHp,
        ushort CurrentMp,
        byte ShieldPercent,
        byte EffectCount,
        byte IsReplay,
        IReadOnlyList<RawEffectResultStatus> Statuses);

    private sealed record RawEffectResultStatus(
        byte EffectIndex,
        ushort EffectId,
        ushort StackCount,
        float Duration,
        uint SourceActorId);

    private sealed record RawActorControlPacket(
        long Sequence,
        DateTime SeenAtUtc,
        uint EntityId,
        uint Category,
        uint Param1,
        uint Param2,
        uint Param3,
        uint Param4,
        uint Param5,
        uint Param6,
        uint Param7,
        uint Param8,
        ulong TargetId,
        byte Param9,
        RawCombatSnapshot? TargetSnapshot,
        RawCombatSnapshot? SourceSnapshot);

    private sealed record RawMapEffectPacket(
        long Sequence,
        DateTime SeenAtUtc,
        uint Index,
        ushort StateLow,
        ushort StateHigh);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private unsafe struct EffectResultPacket
    {
        public uint Unknown1;
        public uint RelatedActionSequence;
        public uint ActorId;
        public uint CurrentHp;
        public uint MaxHp;
        public ushort CurrentMp;
        public ushort Unknown3;
        public byte DamageShield;
        public byte EffectCount;
        public ushort Unknown6;
        public fixed byte Effects[64];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct EffectResultStatusEntry
    {
        public byte EffectIndex;
        public byte Unknown1;
        public ushort EffectId;
        public ushort StackCount;
        public ushort Unknown3;
        public float Duration;
        public uint SourceActorId;
    }

    private delegate void ProcessPacketEffectResultDelegate(uint targetId, IntPtr actionIntegrityData, byte isReplay);

    private delegate long ProcessMapEffectDelegate(long a1, uint index, ushort stateLow, ushort stateHigh);

    private delegate void ProcessPacketActorControlDelegate(
        uint entityId,
        uint category,
        uint param1,
        uint param2,
        uint param3,
        uint param4,
        uint param5,
        uint param6,
        uint param7,
        uint param8,
        ulong targetId,
        byte param9);

    private unsafe void OnReceiveActionEffect(
        uint casterEntityId,
        Character* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        try
        {
            EnqueueRawActionEffects(casterEntityId, targetPos, header, effects, targetEntityIds);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process Better Deaths action effect.");
        }

        actionEffectHook?.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    private unsafe void OnProcessPacketEffectResult(uint targetId, IntPtr actionIntegrityData, byte isReplay)
    {
        effectResultHook?.Original(targetId, actionIntegrityData, isReplay);

        try
        {
            EnqueueRawEffectResult(targetId, actionIntegrityData, isReplay);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process Better Deaths EffectResult packet.");
        }
    }

    private long OnProcessMapEffect(long a1, uint index, ushort stateLow, ushort stateHigh)
    {
        var result = mapEffectHook?.Original(a1, index, stateLow, stateHigh) ?? 0;

        try
        {
            EnqueueRawMapEffect(index, stateLow, stateHigh);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process Better Deaths MapEffect packet.");
        }

        return result;
    }

    private void OnProcessPacketActorControl(
        uint entityId,
        uint category,
        uint param1,
        uint param2,
        uint param3,
        uint param4,
        uint param5,
        uint param6,
        uint param7,
        uint param8,
        ulong targetId,
        byte param9)
    {
        actorControlHook?.Original(entityId, category, param1, param2, param3, param4, param5, param6, param7, param8, targetId, param9);

        try
        {
            EnqueueRawActorControl(entityId, category, param1, param2, param3, param4, param5, param6, param7, param8, targetId, param9);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process Better Deaths ActorControl packet.");
        }
    }

    private void EnqueueRawActorControl(
        uint entityId,
        uint category,
        uint param1,
        uint param2,
        uint param3,
        uint param4,
        uint param5,
        uint param6,
        uint param7,
        uint param8,
        ulong targetId,
        byte param9)
    {
        var now = DateTime.UtcNow;
        if (!ShouldAcceptRawCombatCapture(now))
        {
            return;
        }

        var shouldCaptureSnapshots = category is ActorControlDeathCategory or
            ActorControlGainEffectCategory or
            ActorControlLoseEffectCategory or
            ActorControlUpdateEffectCategory or
            ActorControlTargetIconCategory or
            ActorControlHotCategory or
            ActorControlDotCategory;
        var sourceEntityId = category is ActorControlHotCategory or ActorControlDotCategory
            ? NormalizeActorEntityId(param3)
            : 0;

        EnqueueRawActorControlPacket(new RawActorControlPacket(
            GetNextRawActorControlSequence(),
            now,
            entityId,
            category,
            param1,
            param2,
            param3,
            param4,
            param5,
            param6,
            param7,
            param8,
            targetId,
            param9,
            shouldCaptureSnapshots ? CaptureRawCombatSnapshot(entityId, playerOnly: true) : null,
            sourceEntityId == 0 ? null : CaptureRawCombatSnapshot(sourceEntityId)));
    }

    private unsafe void EnqueueRawEffectResult(uint targetId, IntPtr actionIntegrityData, byte isReplay)
    {
        var now = DateTime.UtcNow;
        if (!ShouldAcceptRawCombatCapture(now) ||
            actionIntegrityData == IntPtr.Zero)
        {
            return;
        }

        var packet = (EffectResultPacket*)actionIntegrityData;
        var effectCount = Math.Min(packet->EffectCount, (byte)MaxEffectResultEntries);
        var statuses = new List<RawEffectResultStatus>(effectCount);
        var effects = (EffectResultStatusEntry*)packet->Effects;
        for (var i = 0; i < effectCount; i++)
        {
            var effect = effects[i];
            if (effect.EffectId == 0)
            {
                continue;
            }

            statuses.Add(new RawEffectResultStatus(
                effect.EffectIndex,
                effect.EffectId,
                effect.StackCount,
                effect.Duration,
                effect.SourceActorId));
        }

        EnqueueRawEffectResultPacket(new RawEffectResultPacket(
            GetNextRawEffectResultSequence(),
            now,
            targetId,
            packet->RelatedActionSequence,
            packet->ActorId,
            packet->CurrentHp,
            packet->MaxHp,
            packet->CurrentMp,
            packet->DamageShield,
            packet->EffectCount,
            isReplay,
            statuses));
    }

    private void EnqueueRawMapEffect(uint index, ushort stateLow, ushort stateHigh)
    {
        var now = DateTime.UtcNow;
        if (!ShouldAcceptRawCombatCapture(now))
        {
            return;
        }

        EnqueueRawMapEffectPacket(new RawMapEffectPacket(
            GetNextRawMapEffectSequence(),
            now,
            index,
            stateLow,
            stateHigh));
    }

    private unsafe void EnqueueRawActionEffects(
        uint casterEntityId,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        var now = DateTime.UtcNow;
        if (!ShouldAcceptRawCombatCapture(now) ||
            header is null ||
            effects is null ||
            targetEntityIds is null ||
            header->NumTargets == 0)
        {
            return;
        }

        var sequence = GetNextRawActionEffectSequence();
        var sourceSnapshot = CaptureRawCombatSnapshot(casterEntityId);
        var replayPoses = new List<RawActorPoseSnapshot>();
        var replayPoseKeys = new HashSet<string>(StringComparer.Ordinal);
        AddRawActorPoseSnapshot(
            replayPoses,
            replayPoseKeys,
            casterEntityId,
            now,
            sequence,
            header->GlobalSequence,
            -1,
            ReplayPositionSampleSource.ActionEffectSource);
        var targetCount = Math.Min((int)header->NumTargets, MaxActionEffectTargets);
        var targets = new List<RawActionEffectTarget>(targetCount);
        for (var targetIndex = 0; targetIndex < targetCount; targetIndex++)
        {
            var rawEffects = new List<RawActionEffectSlot>(8);
            for (var effectIndex = 0; effectIndex < 8; effectIndex++)
            {
                ref var effect = ref effects[targetIndex].Effects[effectIndex];
                if (effect.Type == (byte)ActionEffectKind.Nothing)
                {
                    continue;
                }

                rawEffects.Add(new RawActionEffectSlot(
                    effectIndex,
                    (byte)effect.Type,
                    (uint)effect.Param0,
                    (uint)effect.Param1,
                    (uint)effect.Param3,
                    (uint)effect.Param4,
                    (uint)effect.Value));
            }

            if (rawEffects.Count == 0)
            {
                continue;
            }

            var targetId = targetEntityIds[targetIndex];
            AddRawActorPoseSnapshot(
                replayPoses,
                replayPoseKeys,
                GetEntityIdFromRawTargetId(targetId),
                now,
                sequence,
                header->GlobalSequence,
                targetIndex,
                ReplayPositionSampleSource.ActionEffectTarget);
            targets.Add(new RawActionEffectTarget(
                targetIndex,
                new RawTargetId(targetId.Id, targetId.ObjectId),
                CaptureRawCombatSnapshot(targetId, playerOnly: true),
                rawEffects));
        }

        if (targets.Count == 0)
        {
            return;
        }

        var capturedTargetPosition = targetPos is null ? Vector3.Zero : *targetPos;
        var hasTargetPosition = targetPos is not null && IsUsableReplayPosition(capturedTargetPosition);
        var hasCasterPose = false;
        var casterPosition = Vector3.Zero;
        var casterRotation = 0.0f;
        if (IsDmuReplayCaptureContext() &&
            IsDmuCasterPoseReplayAction(header->ActionId) &&
            TryGetReplayObjectPose(casterEntityId, out var capturedCasterPosition, out var capturedCasterRotation, out _))
        {
            hasCasterPose = true;
            casterPosition = capturedCasterPosition;
            casterRotation = capturedCasterRotation;
        }

        EnqueueRawActionEffectPacket(new RawActionEffectPacket(
            sequence,
            now,
            header->GlobalSequence,
            casterEntityId,
            GetEntityDisplayName(casterEntityId),
            header->ActionId,
            hasCasterPose,
            casterPosition,
            casterRotation,
            hasTargetPosition,
            capturedTargetPosition,
            sourceSnapshot,
            targets,
            replayPoses));
    }

    private void AddRawActorPoseSnapshot(
        List<RawActorPoseSnapshot> replayPoses,
        HashSet<string> replayPoseKeys,
        uint entityId,
        DateTime seenAtUtc,
        long packetSequence,
        uint actionSequence,
        int targetIndex,
        ReplayPositionSampleSource sampleSource)
    {
        if (entityId == 0 ||
            entityId == InvalidActorEntityId ||
            !replayPoseKeys.Add($"{sampleSource}:{entityId:X8}"))
        {
            return;
        }

        if (TryCaptureRawActorPoseSnapshot(
                entityId,
                seenAtUtc,
                packetSequence,
                actionSequence,
                targetIndex,
                sampleSource,
                out var pose))
        {
            replayPoses.Add(pose);
        }
    }

    private bool TryCaptureRawActorPoseSnapshot(
        uint entityId,
        DateTime seenAtUtc,
        long packetSequence,
        uint actionSequence,
        int targetIndex,
        ReplayPositionSampleSource sampleSource,
        out RawActorPoseSnapshot snapshot)
    {
        snapshot = default!;
        try
        {
            var gameObject = ObjectTable.SearchByEntityId(entityId);
            if (gameObject is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
            {
                var member = FindCurrentMemberByEntityId(player.EntityId);
                var classJobId = member?.ClassJobId ?? player.ClassJob.RowId;
                var maxHp = player.MaxHp;
                snapshot = new RawActorPoseSnapshot(
                    seenAtUtc,
                    packetSequence,
                    actionSequence,
                    targetIndex,
                    sampleSource,
                    player.EntityId,
                    member?.MemberName ?? player.Name.TextValue,
                    ReplayActorKind.Player,
                    member?.PartyIndex ?? 1000 + player.ObjectIndex,
                    classJobId,
                    member?.ClassJobName ?? GetClassJobName(classJobId),
                    player.Position,
                    player.Rotation,
                    player.CurrentHp,
                    CalculateShieldHp(player, maxHp),
                    maxHp,
                    player.IsDead || player.CurrentHp == 0,
                    true);
                return IsUsableReplayPosition(player.Position);
            }

            if (gameObject is Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc)
            {
                snapshot = new RawActorPoseSnapshot(
                    seenAtUtc,
                    packetSequence,
                    actionSequence,
                    targetIndex,
                    sampleSource,
                    battleNpc.EntityId,
                    battleNpc.Name.TextValue,
                    ReplayActorKind.Enemy,
                    2000 + battleNpc.ObjectIndex,
                    0,
                    string.Empty,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    battleNpc.CurrentHp,
                    CalculateShieldHp(battleNpc, battleNpc.MaxHp),
                    battleNpc.MaxHp,
                    battleNpc.IsDead || battleNpc.CurrentHp == 0,
                    battleNpc.IsTargetable);
                return IsUsableReplayPosition(battleNpc.Position);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture Better Deaths action-effect replay pose for {EntityId:X8}.", entityId);
        }

        return false;
    }

    private long GetNextRawActionEffectSequence()
    {
        lock (rawCombatQueueLock)
        {
            return nextRawActionEffectSequence++;
        }
    }

    private long GetNextRawCombatLogSequence()
    {
        lock (rawCombatQueueLock)
        {
            return nextRawCombatLogSequence++;
        }
    }

    private long GetNextRawEffectResultSequence()
    {
        lock (rawCombatQueueLock)
        {
            return nextRawEffectResultSequence++;
        }
    }

    private long GetNextRawActorControlSequence()
    {
        lock (rawCombatQueueLock)
        {
            return nextRawActorControlSequence++;
        }
    }

    private long GetNextRawMapEffectSequence()
    {
        lock (rawCombatQueueLock)
        {
            return nextRawMapEffectSequence++;
        }
    }

    private uint GetNextResolvedCombatEventOrdinal()
    {
        return nextResolvedCombatEventOrdinal > uint.MaxValue
            ? uint.MaxValue
            : (uint)nextResolvedCombatEventOrdinal++;
    }

    private void EnqueueRawActionEffectPacket(RawActionEffectPacket packet)
    {
        lock (rawCombatQueueLock)
        {
            rawActionEffectPackets.Enqueue(packet);
            while (rawActionEffectPackets.Count > MaxRawActionEffectPackets)
            {
                rawActionEffectPackets.Dequeue();
            }
        }
    }

    private void EnqueueRawCombatLogMessage(RawCombatLogMessage message)
    {
        lock (rawCombatQueueLock)
        {
            rawCombatLogMessages.Enqueue(message);
            while (rawCombatLogMessages.Count > MaxRawCombatLogMessages)
            {
                rawCombatLogMessages.Dequeue();
            }
        }
    }

    private void EnqueueRawEffectResultPacket(RawEffectResultPacket packet)
    {
        lock (rawCombatQueueLock)
        {
            rawEffectResultPackets.Enqueue(packet);
            while (rawEffectResultPackets.Count > MaxRawEffectResultPackets)
            {
                rawEffectResultPackets.Dequeue();
            }
        }
    }

    private void EnqueueRawActorControlPacket(RawActorControlPacket packet)
    {
        lock (rawCombatQueueLock)
        {
            rawActorControlPackets.Enqueue(packet);
            while (rawActorControlPackets.Count > MaxRawActorControlPackets)
            {
                rawActorControlPackets.Dequeue();
            }
        }
    }

    private void EnqueueRawMapEffectPacket(RawMapEffectPacket packet)
    {
        lock (rawCombatQueueLock)
        {
            rawMapEffectPackets.Enqueue(packet);
            while (rawMapEffectPackets.Count > MaxRawMapEffectPackets)
            {
                rawMapEffectPackets.Dequeue();
            }
        }
    }

    private void ResolveRawCombatQueues(DateTime now)
    {
        var mapEffectPackets = DrainRawMapEffectPackets(now);
        mapEffectPackets.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        foreach (var packet in mapEffectPackets)
        {
            ResolveRawMapEffectPacket(packet);
        }

        var actionPackets = DrainRawActionEffectPackets(now);
        actionPackets.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        foreach (var packet in actionPackets)
        {
            AddActionEffectReplayPoseSamples(packet);
            ResolveRawActionEffectPacket(packet);
        }

        var combatLogMessages = DrainRawCombatLogMessages(now);
        combatLogMessages.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        foreach (var message in combatLogMessages)
        {
            ResolveRawCombatLogMessage(message);
        }

        var effectResultPackets = DrainRawEffectResultPackets(now);
        effectResultPackets.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        foreach (var packet in effectResultPackets)
        {
            ResolveRawEffectResultPacket(packet);
        }

        var actorControlPackets = DrainRawActorControlPackets(now);
        actorControlPackets.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        foreach (var packet in actorControlPackets)
        {
            ResolveRawActorControlPacket(packet);
        }
    }

    private List<RawActionEffectPacket> DrainRawActionEffectPackets(DateTime now)
    {
        lock (rawCombatQueueLock)
        {
            var cutoff = now - TimeSpan.FromSeconds(RawActionEffectRetentionSeconds);
            while (rawActionEffectPackets.Count > 0 && rawActionEffectPackets.Peek().SeenAtUtc < cutoff)
            {
                rawActionEffectPackets.Dequeue();
            }

            if (rawActionEffectPackets.Count == 0)
            {
                return [];
            }

            var packets = rawActionEffectPackets.ToList();
            rawActionEffectPackets.Clear();
            return packets;
        }
    }

    private List<RawCombatLogMessage> DrainRawCombatLogMessages(DateTime now)
    {
        lock (rawCombatQueueLock)
        {
            var cutoff = now - TimeSpan.FromSeconds(RawCombatLogRetentionSeconds);
            while (rawCombatLogMessages.Count > 0 && rawCombatLogMessages.Peek().SeenAtUtc < cutoff)
            {
                rawCombatLogMessages.Dequeue();
            }

            if (rawCombatLogMessages.Count == 0)
            {
                return [];
            }

            var messages = rawCombatLogMessages.ToList();
            rawCombatLogMessages.Clear();
            return messages;
        }
    }

    private List<RawEffectResultPacket> DrainRawEffectResultPackets(DateTime now)
    {
        lock (rawCombatQueueLock)
        {
            var cutoff = now - TimeSpan.FromSeconds(RawCombatLogRetentionSeconds);
            while (rawEffectResultPackets.Count > 0 && rawEffectResultPackets.Peek().SeenAtUtc < cutoff)
            {
                rawEffectResultPackets.Dequeue();
            }

            if (rawEffectResultPackets.Count == 0)
            {
                return [];
            }

            var packets = rawEffectResultPackets.ToList();
            rawEffectResultPackets.Clear();
            return packets;
        }
    }

    private List<RawActorControlPacket> DrainRawActorControlPackets(DateTime now)
    {
        lock (rawCombatQueueLock)
        {
            var cutoff = now - TimeSpan.FromSeconds(RawCombatLogRetentionSeconds);
            while (rawActorControlPackets.Count > 0 && rawActorControlPackets.Peek().SeenAtUtc < cutoff)
            {
                rawActorControlPackets.Dequeue();
            }

            if (rawActorControlPackets.Count == 0)
            {
                return [];
            }

            var packets = rawActorControlPackets.ToList();
            rawActorControlPackets.Clear();
            return packets;
        }
    }

    private List<RawMapEffectPacket> DrainRawMapEffectPackets(DateTime now)
    {
        lock (rawCombatQueueLock)
        {
            var cutoff = now - TimeSpan.FromSeconds(RawCombatLogRetentionSeconds);
            while (rawMapEffectPackets.Count > 0 && rawMapEffectPackets.Peek().SeenAtUtc < cutoff)
            {
                rawMapEffectPackets.Dequeue();
            }

            if (rawMapEffectPackets.Count == 0)
            {
                return [];
            }

            var packets = rawMapEffectPackets.ToList();
            rawMapEffectPackets.Clear();
            return packets;
        }
    }
}
