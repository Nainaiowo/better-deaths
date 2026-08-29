namespace BetterDeaths;

using BetterDeaths.DamageParsing;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed partial class Plugin
{
    private void ObserveDamageMeterOffensiveCasts(DateTime observedAtUtc)
    {
        if (!ShouldAcceptDamageParserCapture(observedAtUtc) ||
            observedAtUtc - lastDamageMeterCastPollAtUtc < DamageMeterCastPollInterval)
        {
            return;
        }

        lastDamageMeterCastPollAtUtc = observedAtUtc;
        try
        {
            DateTime? earliestCastStartedAtUtc = null;
            foreach (var gameObject in ObjectTable)
            {
                if (gameObject is not IPlayerCharacter player ||
                    !player.IsCasting ||
                    player.CastActionId == 0 ||
                    !IsOffensiveDamageMeterCast(player.CastActionId))
                {
                    continue;
                }

                var elapsedCastSeconds = float.IsFinite(player.CurrentCastTime)
                    ? MathF.Max(0.0f, player.CurrentCastTime)
                    : 0.0f;
                var castStartedAtUtc = observedAtUtc.AddSeconds(-elapsedCastSeconds);
                earliestCastStartedAtUtc = earliestCastStartedAtUtc is null ||
                    castStartedAtUtc < earliestCastStartedAtUtc.Value
                        ? castStartedAtUtc
                        : earliestCastStartedAtUtc;
            }

            damageParsingModule.ObserveOffensiveCast(earliestCastStartedAtUtc, observedAtUtc);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not observe damage-meter cast starts.");
        }
    }

    private void RecordDamageMeterDeath(PartyMemberSnapshot member)
    {
        damageParsingModule.RecordDeath(new DamageActorIdentity(
            member.EntityId,
            member.MemberName,
            0,
            string.Empty,
            true,
            member.ClassJobId)
        {
            IsPartyMember = member.IsPartyMember,
        });
    }

    private void ParseDamageActionEffectPacket(RawActionEffectPacket packet)
    {
        try
        {
            var damageSeenAtUtc = packet.ServerFrameTiming?.SeenAtUtc ?? packet.SeenAtUtc;
            var source = CaptureDamageActorIdentity(packet.CasterEntityId, packet.CasterName);
            var sourceOwner = source.OwnerEntityId == 0
                ? null
                : CaptureDamageActorIdentity(source.OwnerEntityId, source.OwnerName);
            var attributedSource = GetAttributedDamageSource(source, sourceOwner);
            var sourceBaseRates = CaptureDamageBaseRates(attributedSource);
            var sourceStatuses = BuildDamageStatusSnapshots(packet.SourceSnapshot);
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
                    effects)
                {
                    TargetHp = target.TargetSnapshot is null
                        ? null
                        : new DamageHpSnapshot(
                            target.TargetSnapshot.CurrentHp,
                            target.TargetSnapshot.ShieldHp,
                            target.TargetSnapshot.MaxHp),
                    TargetStatuses = BuildDamageStatusSnapshots(target.TargetSnapshot),
                    HasTargetStatusSnapshot = target.TargetSnapshot is not null,
                });
            }

            var actionCategoryId = GetActionCategoryId(packet.ActionId);
            var potencyProfile = GetActionPotencyProfile(packet.ActionId);
            var calibratingDamageEffects = targets.Sum(target => target.Effects.Count(effect =>
                effect.Type is 3 or 5 or 6));
            var statusApplications = BuildDamageStatusApplications(
                packet,
                damageSeenAtUtc,
                source,
                sourceOwner,
                sourceStatuses,
                actionCategoryId);
            var damagePacket = new DamageActionPacket(
                packet.Sequence,
                damageSeenAtUtc,
                packet.ActionSequence,
                source,
                packet.ActionId,
                GetActionName(packet.ActionId),
                targets)
            {
                CapturedAtUtc = packet.SeenAtUtc,
                ActionCategoryId = actionCategoryId,
                DirectPotency = potencyProfile.DirectPotency,
                CanCalibratePotency = potencyProfile.DirectPotency is > 0.0 &&
                    calibratingDamageEffects == 1,
                IsAutoAttack = actionCategoryId == 1,
                ActionType = packet.ActionType,
                SourceSequence = packet.SourceSequence,
                SpellId = packet.SpellId,
                AnimationVariation = packet.AnimationVariation,
                AnimationTargetEntityId = packet.AnimationTargetEntityId,
                SourceOwner = sourceOwner,
                SourceBaseRates = sourceBaseRates,
                StatusApplications = statusApplications,
                SourceStatuses = sourceStatuses,
                HasSourceStatusSnapshot = packet.SourceSnapshot is not null,
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
        DateTime damageSeenAtUtc,
        DamageActorIdentity source,
        DamageActorIdentity? sourceOwner,
        IReadOnlyList<DamageStatusSnapshot> sourceStatuses,
        uint actionCategoryId)
    {
        const byte applyStatusToTarget = 14;
        const byte applyStatusToSource = 15;
        const byte removeStatusFromTarget = 17;
        const byte removeStatusFromSource = 18;
        var applications = new List<DamageStatusApplication>();
        var attributedSource = GetAttributedDamageSource(source, sourceOwner);
        var actionDamageProfile = GetActionDamageProfile(packet.ActionId);
        var potencyProfile = GetActionPotencyProfile(packet.ActionId);
        string? snapshotKey = null;
        foreach (var target in packet.Targets)
        {
            var packetTarget = CaptureDamageActorIdentity(GetDamageTargetEntityId(target.TargetId), string.Empty);
            var targetStatuses = BuildDamageStatusSnapshots(target.TargetSnapshot);
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
                    damageSeenAtUtc,
                    0.0f,
                    isRemoval) with
                {
                    PeriodicPotency = isRemoval ? null : potencyProfile.PeriodicPotency,
                    BaseDamageLowByte = isRemoval ? null : (byte)effect.Param0,
                    CriticalRateLowByte = isRemoval ? null : (byte)effect.Param1,
                    EffectParameterByte = isRemoval ? null : (byte)effect.Param2,
                    SnapshotKey = isRemoval
                        ? string.Empty
                        : snapshotKey ??= BuildDamageSnapshotKey(
                            packet.SourceSnapshot ?? CaptureRawCombatSnapshot(packet.CasterEntityId)),
                    // Variable-strength raid buffs store their applied percentage in Param0.
                    Parameter = (ushort)effect.Param0,
                    ActionCategoryId = actionCategoryId,
                    DamageType = actionDamageProfile.DamageType,
                    ElementType = actionDamageProfile.ElementType,
                    SourceBaseRates = CaptureDamageBaseRates(attributedSource),
                    SourceStatuses = sourceStatuses,
                    TargetStatuses = targetStatuses,
                    HasSourceStatusSnapshot = packet.SourceSnapshot is not null,
                    HasTargetStatusSnapshot = target.TargetSnapshot is not null,
                });
            }
        }

        return applications;
    }

    private IReadOnlyList<DamageStatusSnapshot> BuildDamageStatusSnapshots(RawCombatSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return [];
        }

        var identities = new Dictionary<uint, DamageActorIdentity>();
        var statuses = new List<DamageStatusSnapshot>();
        foreach (var status in snapshot.Statuses.Where(status =>
                     DamageStatusCapturePolicy.IsRelevant(status.StatusId)))
        {
            if (!identities.TryGetValue(status.SourceId, out var statusSource))
            {
                var rawSource = CaptureDamageActorIdentity(status.SourceId, string.Empty);
                statusSource = GetAttributedDamageSource(rawSource, CaptureDamageActorOwner(rawSource));
                identities[status.SourceId] = statusSource;
            }

            statuses.Add(new DamageStatusSnapshot(
                status.StatusId,
                statusSource,
                status.StackCount,
                status.RemainingTime));
        }

        return statuses;
    }

    private void ObserveDamageEffectResult(RawEffectResultPacket packet)
    {
        var damageSeenAtUtc = packet.ServerFrameTiming?.SeenAtUtc ?? packet.SeenAtUtc;
        var targetEntityId = NormalizeActorEntityId(packet.TargetId != 0 ? packet.TargetId : packet.ActorId);
        if (targetEntityId == 0)
        {
            return;
        }

        var target = CaptureDamageActorIdentity(targetEntityId, string.Empty);
        damageParsingModule.ObserveEffectResult(new DamageEffectResult(
            damageSeenAtUtc,
            packet.RelatedActionSequence,
            target,
            new DamageHpSnapshot(
                packet.CurrentHp,
                CalculateShieldHpFromPercent(packet.MaxHp, packet.ShieldPercent),
                packet.MaxHp)));
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
                damageSeenAtUtc,
                status.Duration,
                false);
            damageParsingModule.ObserveStatus(application);
            QueueDamageMeterStatusDebug("EffectResult", application);
        }
    }

    private void ParseDamageActorControl(RawActorControlPacket packet)
    {
        var damageSeenAtUtc = packet.ServerFrameTiming?.SeenAtUtc ?? packet.SeenAtUtc;
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
                damageSeenAtUtc,
                target,
                statusId,
                statusId == 0 ? string.Empty : GetStatusName(statusId),
                statusId == 0 ? 0 : GetStatusIconId(statusId),
                packet.Param2,
                source)
            {
                CapturedAtUtc = packet.SeenAtUtc,
                TargetHp = packet.TargetSnapshot is null
                    ? null
                    : new DamageHpSnapshot(
                        packet.TargetSnapshot.CurrentHp,
                        packet.TargetSnapshot.ShieldHp,
                        packet.TargetSnapshot.MaxHp),
            };
            QueueDamageMeterPeriodicDebug(packet, tick);
            damageParsingModule.ProcessPeriodicTick(tick, allowAutomaticEncounterStart: false);
            return;
        }

        if (packet.Category == ActorControlUpdateEffectCategory)
        {
            if (packet.Param2 is > 0 and <= ushort.MaxValue &&
                !TryObserveDamageActorControlStatus(packet, packet.Param2, isRemoval: false))
            {
                damageParsingModule.RefreshStatus(packet.EntityId, packet.Param2, damageSeenAtUtc);
            }

            return;
        }

        if (packet.Category is not ActorControlGainEffectCategory and not ActorControlLoseEffectCategory ||
            packet.Param1 == 0 || packet.Param1 > ushort.MaxValue)
        {
            return;
        }

        TryObserveDamageActorControlStatus(
            packet,
            packet.Param1,
            packet.Category == ActorControlLoseEffectCategory);
    }

    private bool TryObserveDamageActorControlStatus(
        RawActorControlPacket packet,
        uint statusId,
        bool isRemoval)
    {
        var rawStatus = packet.TargetSnapshot?.Statuses
            .Where(status => status.StatusId == statusId)
            .OrderByDescending(status => status.RemainingTime)
            .FirstOrDefault();
        var rawSourceId = rawStatus?.SourceId ??
            (packet.Category is ActorControlGainEffectCategory or ActorControlLoseEffectCategory
                ? packet.Param3
                : 0);
        if (!isRemoval && packet.Category == ActorControlUpdateEffectCategory && rawStatus is null)
        {
            return false;
        }

        var statusTarget = CaptureDamageActorIdentity(packet.EntityId, string.Empty);
        var rawStatusSource = CaptureDamageActorIdentity(rawSourceId, string.Empty);
        var statusSource = GetAttributedDamageSource(rawStatusSource, CaptureDamageActorOwner(rawStatusSource));
        var application = CreateDamageStatusApplication(
            statusTarget,
            statusSource,
            statusId,
            0,
            string.Empty,
            packet.ServerFrameTiming?.SeenAtUtc ?? packet.SeenAtUtc,
            rawStatus?.RemainingTime ?? 0.0f,
            isRemoval) with
        {
            Parameter = rawStatus?.StackCount ?? (ushort)Math.Min(packet.Param2, ushort.MaxValue),
            SnapshotKey = isRemoval ? string.Empty : BuildDamageSnapshotKey(packet.SourceSnapshot),
            SourceStatuses = BuildDamageStatusSnapshots(packet.SourceSnapshot),
            TargetStatuses = BuildDamageStatusSnapshots(packet.TargetSnapshot),
            HasSourceStatusSnapshot = packet.SourceSnapshot is not null,
            HasTargetStatusSnapshot = packet.TargetSnapshot is not null,
        };
        damageParsingModule.ObserveStatus(application);
        QueueDamageMeterStatusDebug("ActorControl", application);
        return true;
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
            isRemoval)
        {
            SourceBaseRates = CaptureDamageBaseRates(source),
        };
    }

    private unsafe DamageBaseRateSnapshot? CaptureDamageBaseRates(DamageActorIdentity source)
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer is null ||
            source.EntityId == 0 ||
            NormalizeActorEntityId(localPlayer.EntityId) != source.EntityId)
        {
            return null;
        }

        var uiState = UIState.Instance();
        if (uiState == null || uiState->PlayerState.CurrentLevel <= 0)
        {
            return null;
        }

        var level = (uint)uiState->PlayerState.CurrentLevel;
        var paramGrow = DataManager.GetExcelSheet<ParamGrow>()?.GetRowOrDefault(level);
        if (paramGrow is null)
        {
            return null;
        }

        return DamageBaseRatePolicy.FromAttributes(
            uiState->PlayerState.Attributes[27],
            uiState->PlayerState.Attributes[22],
            paramGrow.Value.BaseSpeed,
            paramGrow.Value.LevelModifier);
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
            ServerSeenAtUtc = packet.ServerFrameTiming?.SeenAtUtc,
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
                application.Parameter,
                application.PeriodicPotency,
                application.BaseDamageLowByte,
                application.CriticalRateLowByte,
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
            ServerSeenAtUtc = packet.ServerFrameTiming?.SeenAtUtc,
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
            tick.CapturedAtUtc,
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
            application.Parameter,
            application.PeriodicPotency,
            application.BaseDamageLowByte,
            application.CriticalRateLowByte,
            application.SourceBaseRates,
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
                damageEvent.MeterAmount,
                damageEvent.CalculatedAmount,
                Resolution = damageEvent.ResolutionQuality.ToString(),
                damageEvent.AbsorbedDamage,
                damageEvent.OverkillDamage,
                damageEvent.TargetHpBefore,
                damageEvent.TargetHpAfter,
                damageEvent.CapturedAtUtc,
                damageEvent.DirectPotency,
                damageEvent.CanCalibratePotency,
                damageEvent.SourceBaseRates,
                Outcome = damageEvent.Outcome.ToString(),
                Attribution = damageEvent.AttributionQuality.ToString(),
                damageEvent.IsPeriodic,
                damageEvent.HasSourceStatusSnapshot,
                damageEvent.HasTargetStatusSnapshot,
                SourceStatuses = damageEvent.SourceStatuses.Select(status => new
                {
                    status.StatusId,
                    SourceEntityId = status.Source.EntityId,
                    SourceName = status.Source.Name,
                    status.Parameter,
                    status.RemainingTime,
                }),
                TargetStatuses = damageEvent.TargetStatuses.Select(status => new
                {
                    status.StatusId,
                    SourceEntityId = status.Source.EntityId,
                    SourceName = status.Source.Name,
                    status.Parameter,
                    status.RemainingTime,
                }),
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
        if (ended is not null)
        {
            RecordCompletedDamageEncounter(ended);
        }

        if (ShouldSaveDamageMeterDebug())
        {
            QueueDebugCaptureRecord("DamageMeterEncounterEnd", new
            {
                EndedAtUtc = endedAtUtc,
                Reason = reason,
                HasEncounter = ended is not null,
                TotalDamage = ended?.TotalDamage ?? 0,
                MeterDamage = ended?.EffectiveMeterDamage ?? 0.0,
                ExactDamage = ended?.ExactDamage ?? 0,
                EstimatedDamage = ended?.EstimatedDamage ?? 0,
                UnattributedDamage = ended?.UnattributedDamage ?? 0,
                RaidAdjustedDamage = ended?.RaidAdjustedDamage ?? 0.0,
                MeterRaidAdjustedDamage = ended?.EffectiveMeterRaidAdjustedDamage ?? 0.0,
                DurationSeconds = ended?.DurationSeconds ?? 0.0,
                PacketCount = ended?.PacketCount ?? 0,
                Sources = ended?.Sources.Select(source => new
                {
                    SourceEntityId = source.Source.EntityId,
                    SourceName = source.Source.Name,
                    source.TotalDamage,
                    MeterDamage = source.EffectiveMeterDamage,
                    source.Swings,
                    source.Hits,
                    source.Misses,
                    source.Resists,
                    source.InvulnerableHits,
                    source.CriticalHits,
                    source.DirectHits,
                    source.CriticalDirectHits,
                    source.PeriodicHits,
                    source.RaidAdjustedDamage,
                    source.ExternalBuffDamageReceived,
                    source.RaidBuffDamageGiven,
                    source.SingleTargetBuffDamageReceived,
                }),
            });
        }

        return ended;
    }
}
