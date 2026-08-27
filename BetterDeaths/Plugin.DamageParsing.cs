namespace BetterDeaths;

using BetterDeaths.DamageParsing;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed partial class Plugin
{
    private void ParseDamageActionEffectPacket(RawActionEffectPacket packet)
    {
        try
        {
            var targets = new List<DamageActionTarget>(packet.Targets.Count);
            foreach (var target in packet.Targets)
            {
                var effects = new List<DamageActionEffect>(target.Effects.Count);
                foreach (var effect in target.Effects)
                {
                    effects.Add(new DamageActionEffect(
                        effect.EffectIndex,
                        effect.Type,
                        (byte)effect.Param0,
                        (byte)effect.Param1,
                        (byte)effect.Param3,
                        (byte)effect.Param4,
                        effect.Value)
                    {
                        Param2 = (byte)effect.Param2,
                    });
                }

                var targetEntityId = GetDamageTargetEntityId(target.TargetId);
                targets.Add(new DamageActionTarget(
                    target.TargetIndex,
                    CaptureDamageActorIdentity(targetEntityId, string.Empty),
                    effects));
            }

            var source = CaptureDamageActorIdentity(packet.CasterEntityId, packet.CasterName);
            var sourceOwner = source.OwnerEntityId == 0
                ? null
                : CaptureDamageActorIdentity(source.OwnerEntityId, source.OwnerName);
            var actionCategoryId = GetActionCategoryId(packet.ActionId);
            var statusApplications = BuildDamageStatusApplications(packet, source, sourceOwner);
            var damagePacket = new DamageActionPacket(
                packet.Sequence,
                packet.SeenAtUtc,
                packet.ActionSequence,
                source,
                packet.ActionId,
                GetActionName(packet.ActionId),
                targets)
            {
                ActionCategoryId = actionCategoryId,
                IsAutoAttack = actionCategoryId == 1,
                ActionType = packet.ActionType,
                SourceSequence = packet.SourceSequence,
                SpellId = packet.SpellId,
                AnimationVariation = packet.AnimationVariation,
                AnimationTargetEntityId = packet.AnimationTargetEntityId,
                SourceOwner = sourceOwner,
                StatusApplications = statusApplications,
            };
            var parsed = damageParsingModule.Process(damagePacket, allowAutomaticEncounterStart: false);
            QueueDamageMeterActionDebug(packet, statusApplications);
            QueueDamageMeterParsedDebug("Action", parsed);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not parse Better Deaths damage action {ActionId}.", packet.ActionId);
        }
    }

    private IReadOnlyList<DamageStatusApplication> BuildDamageStatusApplications(
        RawActionEffectPacket packet,
        DamageActorIdentity source,
        DamageActorIdentity? sourceOwner)
    {
        const byte applyStatusToTarget = 14;
        const byte applyStatusToSource = 15;
        const byte removeStatusFromTarget = 17;
        const byte removeStatusFromSource = 18;
        var applications = new List<DamageStatusApplication>();
        var attributedSource = GetAttributedDamageSource(source, sourceOwner);
        string? snapshotKey = null;
        foreach (var target in packet.Targets)
        {
            var packetTarget = CaptureDamageActorIdentity(GetDamageTargetEntityId(target.TargetId), string.Empty);
            foreach (var effect in target.Effects)
            {
                if (effect.Type is not (applyStatusToTarget or applyStatusToSource or
                    removeStatusFromTarget or removeStatusFromSource) ||
                    effect.Value == 0 || effect.Value > ushort.MaxValue)
                {
                    continue;
                }

                var statusTarget = effect.Type is applyStatusToSource or removeStatusFromSource
                    ? source
                    : packetTarget;
                var isRemoval = effect.Type is removeStatusFromTarget or removeStatusFromSource;
                applications.Add(CreateDamageStatusApplication(
                    statusTarget,
                    attributedSource,
                    effect.Value,
                    packet.ActionId,
                    GetActionName(packet.ActionId),
                    packet.SeenAtUtc,
                    0.0f,
                    isRemoval) with
                {
                    SnapshotKey = isRemoval
                        ? string.Empty
                        : snapshotKey ??= BuildDamageSnapshotKey(
                            packet.SourceSnapshot ?? CaptureRawCombatSnapshot(packet.CasterEntityId)),
                });
            }
        }

        return applications;
    }

    private void ObserveDamageEffectResult(RawEffectResultPacket packet)
    {
        var targetEntityId = NormalizeActorEntityId(packet.TargetId != 0 ? packet.TargetId : packet.ActorId);
        if (targetEntityId == 0)
        {
            return;
        }

        var target = CaptureDamageActorIdentity(targetEntityId, string.Empty);
        foreach (var status in packet.Statuses.Where(status => status.EffectId != 0))
        {
            var rawSource = CaptureDamageActorIdentity(status.SourceActorId, string.Empty);
            var source = GetAttributedDamageSource(rawSource, CaptureDamageActorOwner(rawSource));
            var application = CreateDamageStatusApplication(
                target,
                source,
                status.EffectId,
                0,
                string.Empty,
                packet.SeenAtUtc,
                status.Duration,
                false);
            damageParsingModule.ObserveStatus(application);
            QueueDamageMeterStatusDebug("EffectResult", application);
        }
    }

    private void ParseDamageActorControl(RawActorControlPacket packet)
    {
        if (packet.Category == ActorControlDotCategory && packet.Param2 > 0)
        {
            var target = CaptureDamageActorIdentity(packet.EntityId, string.Empty);
            var statusId = packet.Param1 <= ushort.MaxValue ? packet.Param1 : 0;
            DamageActorIdentity? source = null;
            if (statusId != 0)
            {
                var rawSource = CaptureDamageActorIdentity(packet.Param3, string.Empty);
                source = rawSource.EntityId == 0
                    ? null
                    : GetAttributedDamageSource(rawSource, CaptureDamageActorOwner(rawSource));
            }

            var tick = new PeriodicDamageTick(
                packet.Sequence,
                packet.SeenAtUtc,
                target,
                statusId,
                statusId == 0 ? string.Empty : GetStatusName(statusId),
                statusId == 0 ? 0 : GetStatusIconId(statusId),
                packet.Param2,
                source);
            QueueDamageMeterPeriodicDebug(packet, tick);
            damageParsingModule.ProcessPeriodicTick(tick, allowAutomaticEncounterStart: false);
            return;
        }

        if (packet.Category is not ActorControlGainEffectCategory and not ActorControlLoseEffectCategory ||
            packet.Param1 == 0 || packet.Param1 > ushort.MaxValue)
        {
            if (packet.Category == ActorControlUpdateEffectCategory &&
                packet.Param2 is > 0 and <= ushort.MaxValue)
            {
                damageParsingModule.RefreshStatus(packet.EntityId, packet.Param2, packet.SeenAtUtc);
            }

            return;
        }

        var statusTarget = CaptureDamageActorIdentity(packet.EntityId, string.Empty);
        var rawStatusSource = CaptureDamageActorIdentity(packet.Param3, string.Empty);
        var statusSource = GetAttributedDamageSource(rawStatusSource, CaptureDamageActorOwner(rawStatusSource));
        var application = CreateDamageStatusApplication(
            statusTarget,
            statusSource,
            packet.Param1,
            0,
            string.Empty,
            packet.SeenAtUtc,
            0.0f,
            packet.Category == ActorControlLoseEffectCategory);
        damageParsingModule.ObserveStatus(application);
        QueueDamageMeterStatusDebug("ActorControl", application);
    }

    private DamageStatusApplication CreateDamageStatusApplication(
        DamageActorIdentity target,
        DamageActorIdentity source,
        uint statusId,
        uint actionId,
        string actionName,
        DateTime seenAtUtc,
        float durationSeconds,
        bool isRemoval)
    {
        return new DamageStatusApplication(
            target,
            source,
            statusId,
            GetStatusName(statusId),
            GetStatusIconId(statusId),
            actionId,
            actionName,
            seenAtUtc,
            durationSeconds,
            IsPeriodicDamageStatus(statusId) || GroundDamageStatusPolicy.IsKnown(statusId),
            IsReactiveDamageStatus(statusId),
            isRemoval);
    }

    private DamageActorIdentity? CaptureDamageActorOwner(DamageActorIdentity actor)
    {
        return actor.OwnerEntityId == 0
            ? null
            : CaptureDamageActorIdentity(actor.OwnerEntityId, actor.OwnerName);
    }

    private static DamageActorIdentity GetAttributedDamageSource(
        DamageActorIdentity source,
        DamageActorIdentity? owner)
    {
        return source.IsPet && owner is not null ? owner : source;
    }

    private DamageActorIdentity CaptureDamageActorIdentity(uint entityId, string fallbackName)
    {
        entityId = NormalizeActorEntityId(entityId);
        var defaultName = !string.IsNullOrWhiteSpace(fallbackName)
            ? fallbackName
            : entityId == 0
                ? "Unknown actor"
                : $"Entity {entityId:X8}";
        if (entityId == 0)
        {
            return new DamageActorIdentity(0, defaultName, 0, string.Empty, false, 0);
        }

        try
        {
            var gameObject = ObjectTable.SearchByEntityId(entityId);
            if (gameObject is null)
            {
                return CaptureKnownDamageActorIdentity(entityId, defaultName) ??
                    new DamageActorIdentity(entityId, defaultName, 0, string.Empty, false, 0);
            }

            var ownerEntityId = NormalizeActorEntityId(gameObject.OwnerId);
            if (ownerEntityId == entityId)
            {
                ownerEntityId = 0;
            }

            var ownerName = string.Empty;
            if (ownerEntityId != 0)
            {
                ownerName = ObjectTable.SearchByEntityId(ownerEntityId)?.Name.TextValue ?? string.Empty;
            }

            var player = gameObject as IPlayerCharacter;
            var battleNpc = gameObject as IBattleNpc;
            var name = gameObject.Name.TextValue;
            var isPartyMember = currentMembers.Any(member => member.EntityId == entityId) ||
                lastKnownMembersByKey.Values.Any(member => member.EntityId == entityId) ||
                PartyList.Any(member => member.EntityId == entityId);
            return new DamageActorIdentity(
                entityId,
                string.IsNullOrWhiteSpace(name) ? defaultName : name,
                ownerEntityId,
                ownerName,
                player is not null,
                player?.ClassJob.RowId ?? 0)
            {
                BaseId = gameObject.BaseId,
                ObjectKind = (byte)gameObject.ObjectKind,
                SubKind = gameObject.SubKind,
                IsPet = battleNpc?.BattleNpcKind is BattleNpcSubKind.Pet or BattleNpcSubKind.Buddy,
                IsPartyMember = isPartyMember,
            };
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture damage-parser identity for {EntityId:X8}.", entityId);
            return CaptureKnownDamageActorIdentity(entityId, defaultName) ??
                new DamageActorIdentity(entityId, defaultName, 0, string.Empty, false, 0);
        }
    }

    private DamageActorIdentity? CaptureKnownDamageActorIdentity(uint entityId, string fallbackName)
    {
        var member = currentMembers.FirstOrDefault(candidate => candidate.EntityId == entityId);
        if (member is not null)
        {
            return new DamageActorIdentity(
                entityId,
                string.IsNullOrWhiteSpace(member.MemberName) ? fallbackName : member.MemberName,
                0,
                string.Empty,
                true,
                member.ClassJobId)
            {
                IsPartyMember = member.IsPartyMember,
            };
        }

        var partyMember = PartyList.FirstOrDefault(candidate => candidate.EntityId == entityId);
        if (partyMember is not null)
        {
            var partyName = partyMember.Name.TextValue;
            return new DamageActorIdentity(
                entityId,
                string.IsNullOrWhiteSpace(partyName) ? fallbackName : partyName,
                0,
                string.Empty,
                true,
                partyMember.ClassJob.RowId)
            {
                IsPartyMember = true,
            };
        }

        member = lastKnownMembersByKey.Values.FirstOrDefault(candidate => candidate.EntityId == entityId);
        if (member is null)
        {
            return null;
        }

        return new DamageActorIdentity(
            entityId,
            string.IsNullOrWhiteSpace(member.MemberName) ? fallbackName : member.MemberName,
            0,
            string.Empty,
            true,
            member.ClassJobId)
        {
            IsPartyMember = member.IsPartyMember,
        };
    }

    private static uint GetDamageTargetEntityId(RawTargetId targetId)
    {
        if (targetId.ObjectId != 0)
        {
            return NormalizeActorEntityId(targetId.ObjectId);
        }

        return targetId.Id <= uint.MaxValue
            ? NormalizeActorEntityId((uint)targetId.Id)
            : 0;
    }

    private static string BuildDamageSnapshotKey(RawCombatSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Statuses.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            '|',
            snapshot.Statuses
                .Where(status => status.StatusId != 0)
                .OrderBy(status => status.StatusId)
                .ThenBy(status => status.SourceId)
                .ThenBy(status => status.StackCount)
                .Select(status => $"{status.StatusId:X4}:{status.SourceId:X8}:{status.StackCount}"));
    }

    private void QueueDamageMeterActionDebug(
        RawActionEffectPacket packet,
        IReadOnlyList<DamageStatusApplication> applications)
    {
        if (!ShouldSaveDamageMeterDebug())
        {
            return;
        }

        QueueDebugCaptureRecord("DamageMeterAction", new
        {
            packet.Sequence,
            packet.SeenAtUtc,
            packet.ActionSequence,
            packet.CasterEntityId,
            packet.CasterName,
            packet.ActionId,
            ActionName = GetActionName(packet.ActionId),
            Targets = packet.Targets.Select(target => new
            {
                target.TargetIndex,
                TargetEntityId = GetDamageTargetEntityId(target.TargetId),
                target.Effects,
            }),
            StatusApplications = applications.Select(application => new
            {
                TargetEntityId = application.Target.EntityId,
                SourceEntityId = application.Source.EntityId,
                application.StatusId,
                application.StatusName,
                application.SnapshotKey,
                application.IsRemoval,
            }),
        });
    }

    private void QueueDamageMeterPeriodicDebug(RawActorControlPacket packet, PeriodicDamageTick tick)
    {
        if (!ShouldSaveDamageMeterDebug())
        {
            return;
        }

        QueueDebugCaptureRecord("DamageMeterPeriodic", new
        {
            packet.Sequence,
            packet.SeenAtUtc,
            packet.Category,
            packet.Param1,
            packet.Param2,
            packet.Param3,
            packet.Param4,
            packet.Param5,
            packet.Param6,
            packet.Param7,
            packet.Param8,
            TargetEntityId = tick.Target.EntityId,
            TargetName = tick.Target.Name,
            tick.StatusId,
            tick.StatusName,
            tick.Amount,
            SourceEntityId = tick.Source?.EntityId ?? 0,
            SourceName = tick.Source?.Name ?? string.Empty,
        });
    }

    private void QueueDamageMeterStatusDebug(string stage, DamageStatusApplication application)
    {
        if (!ShouldSaveDamageMeterDebug())
        {
            return;
        }

        QueueDebugCaptureRecord("DamageMeterStatus", new
        {
            Stage = stage,
            application.SeenAtUtc,
            TargetEntityId = application.Target.EntityId,
            TargetName = application.Target.Name,
            SourceEntityId = application.Source.EntityId,
            SourceName = application.Source.Name,
            application.StatusId,
            application.StatusName,
            application.ActionId,
            application.ActionName,
            application.DurationSeconds,
            application.IsPeriodicDamage,
            application.IsReactiveDamage,
            application.IsRemoval,
            application.SnapshotKey,
        });
    }

    private void QueueDamageMeterParsedDebug(string stage, IReadOnlyList<ParsedDamageEvent> parsed)
    {
        if (!ShouldSaveDamageMeterDebug() || parsed.Count == 0)
        {
            return;
        }

        QueueDebugCaptureRecord("DamageMeterParsed", new
        {
            Stage = stage,
            Events = parsed.Select(damageEvent => new
            {
                damageEvent.EventId,
                damageEvent.PacketSequence,
                damageEvent.SeenAtUtc,
                SourceEntityId = damageEvent.Source.EntityId,
                SourceName = damageEvent.Source.Name,
                TargetEntityId = damageEvent.Target.EntityId,
                TargetName = damageEvent.Target.Name,
                damageEvent.ActionId,
                damageEvent.ActionName,
                damageEvent.StatusId,
                damageEvent.Amount,
                Outcome = damageEvent.Outcome.ToString(),
                Attribution = damageEvent.AttributionQuality.ToString(),
                damageEvent.IsPeriodic,
            }),
        });
    }

    private bool ShouldSaveDamageMeterDebug()
    {
        return Configuration.DebugLogEnabled && Configuration.DebugSaveToFileEnabled;
    }

    private DamageEncounterSnapshot? EndDamageEncounter(DateTime endedAtUtc, string reason)
    {
        var ended = damageParsingModule.EndEncounter(endedAtUtc, reason);
        if (ShouldSaveDamageMeterDebug())
        {
            QueueDebugCaptureRecord("DamageMeterEncounterEnd", new
            {
                EndedAtUtc = endedAtUtc,
                Reason = reason,
                HasEncounter = ended is not null,
                TotalDamage = ended?.TotalDamage ?? 0,
                ExactDamage = ended?.ExactDamage ?? 0,
                EstimatedDamage = ended?.EstimatedDamage ?? 0,
                UnattributedDamage = ended?.UnattributedDamage ?? 0,
                DurationSeconds = ended?.DurationSeconds ?? 0.0,
                PacketCount = ended?.PacketCount ?? 0,
            });
        }

        return ended;
    }
}
