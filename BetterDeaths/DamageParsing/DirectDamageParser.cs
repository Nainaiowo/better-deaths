namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;

internal sealed class DirectDamageParser
{
    private static readonly DamageActorIdentity LimitBreakSource = new(
        0,
        "Limit Break",
        0,
        string.Empty,
        false,
        0)
    {
        IsLimitBreak = true,
        IsPartyMember = true,
    };

    private enum DamageActionEffectKind : byte
    {
        Miss = 1,
        FullResist = 2,
        Damage = 3,
        BlockedDamage = 5,
        ParriedDamage = 6,
        Invulnerable = 7,
        PartialInvulnerable = 74,
    }

    public IReadOnlyList<ParsedDamageEvent> Parse(DamageActionPacket packet)
    {
        var parsed = new List<ParsedDamageEvent>();
        foreach (var target in packet.Targets)
        {
            foreach (var effect in target.Effects)
            {
                if (!TryGetOutcome(effect.Type, out var outcome))
                {
                    continue;
                }

                var isDamage = outcome == DamageEventOutcome.Damage;
                var critical = isDamage && (effect.Param0 & 0x20) != 0;
                var directHit = isDamage && (effect.Param0 & 0x40) != 0;
                var isSourceEntry = (effect.Param4 & 0x80) != 0;
                var attributedSource = packet.ActionCategoryId == 9
                    ? LimitBreakSource
                    : packet.Source.IsPet && packet.SourceOwner is not null
                        ? packet.SourceOwner
                        : packet.Source;
                parsed.Add(new ParsedDamageEvent(
                    BuildEventId(packet, target, effect),
                    packet.PacketSequence,
                    packet.SeenAtUtc,
                    packet.ActionSequence,
                    packet.Source,
                    isSourceEntry ? packet.Source : target.Target,
                    packet.ActionId,
                    packet.ActionName,
                    target.TargetIndex,
                    effect.EffectIndex,
                    outcome,
                    isDamage ? DecodeAmount(effect) : 0,
                    (byte)(effect.Param1 & 0x0F),
                    critical,
                    directHit,
                    effect.Type == (byte)DamageActionEffectKind.BlockedDamage,
                    effect.Type == (byte)DamageActionEffectKind.ParriedDamage,
                    effect.Type,
                    effect.Param0,
                    effect.Param1,
                    effect.Param3,
                    effect.Param4)
                {
                    ElementType = (byte)(effect.Param1 >> 4),
                    ActionCategoryId = packet.ActionCategoryId,
                    IsAutoAttack = packet.IsAutoAttack,
                    ActionType = packet.ActionType,
                    SourceSequence = packet.SourceSequence,
                    SpellId = packet.SpellId,
                    AnimationVariation = packet.AnimationVariation,
                    AnimationTargetEntityId = packet.AnimationTargetEntityId,
                    RawParam2 = effect.Param2,
                    IsSourceEntry = isSourceEntry,
                    PacketTarget = target.Target,
                    AttributedSource = attributedSource,
                    SourceStatuses = packet.SourceStatuses,
                    TargetStatuses = target.TargetStatuses,
                    HasSourceStatusSnapshot = packet.HasSourceStatusSnapshot,
                    HasTargetStatusSnapshot = target.HasTargetStatusSnapshot,
                });
            }
        }

        return parsed;
    }

    internal static uint DecodeAmount(DamageActionEffect effect)
    {
        var amount = (ulong)effect.Value;
        if ((effect.Param4 & 0x40) != 0)
        {
            amount += (ulong)effect.Param3 << 16;
        }

        return amount > uint.MaxValue ? uint.MaxValue : (uint)amount;
    }

    private static bool TryGetOutcome(byte rawType, out DamageEventOutcome outcome)
    {
        outcome = (DamageActionEffectKind)rawType switch
        {
            DamageActionEffectKind.Damage or
            DamageActionEffectKind.BlockedDamage or
            DamageActionEffectKind.ParriedDamage => DamageEventOutcome.Damage,
            DamageActionEffectKind.Miss => DamageEventOutcome.Miss,
            DamageActionEffectKind.FullResist => DamageEventOutcome.Resisted,
            DamageActionEffectKind.Invulnerable or
            DamageActionEffectKind.PartialInvulnerable => DamageEventOutcome.Invulnerable,
            _ => default,
        };

        return (DamageActionEffectKind)rawType is
            DamageActionEffectKind.Damage or
            DamageActionEffectKind.BlockedDamage or
            DamageActionEffectKind.ParriedDamage or
            DamageActionEffectKind.Miss or
            DamageActionEffectKind.FullResist or
            DamageActionEffectKind.Invulnerable or
            DamageActionEffectKind.PartialInvulnerable;
    }

    private static string BuildEventId(
        DamageActionPacket packet,
        DamageActionTarget target,
        DamageActionEffect effect)
    {
        var actionIdentity = packet.ActionSequence != 0
            ? $"global:{packet.ActionSequence}"
            : packet.SourceSequence != 0
                ? $"source:{packet.Source.EntityId:X8}:{packet.SourceSequence}"
                : $"local:{packet.PacketSequence}";
        return $"{actionIdentity}:{target.TargetIndex}:{effect.EffectIndex}:" +
            $"{packet.Source.EntityId:X8}:{target.Target.EntityId:X8}:{packet.ActionId}";
    }
}
