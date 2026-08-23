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
    private sealed record ActiveDmuP2PathOfLightTower(
        uint Index,
        string SourceKey,
        DateTime SeenAtUtc,
        Vector3 Position);

    private sealed record ActiveReplayMechanic(
        string ActiveKey,
        string SourceKey,
        uint SourceEntityId,
        uint CastActionId,
        uint ResolveActionId,
        DateTime CastStartedAtUtc,
        DateTime StartedAtUtc,
        DateTime FallbackEndAtUtc,
        bool EndsWhenSourceMissing,
        bool EndsWhenSourceStopsCasting);

    private readonly record struct ReplayActionSheetMetadata(
        string Name,
        byte CastType,
        byte EffectRange,
        byte XAxisModifier,
        sbyte Range,
        bool TargetArea);

    private void ResolveRawMapEffectPacket(RawMapEffectPacket packet)
    {
        CaptureReplayDmuP2PathOfLightMapEffect(packet);
        CaptureReplayDmuP5ArenaHoleMapEffect(packet);
    }

    private void CaptureReplayBlackHoleBlast(RawActionEffectPacket packet)
    {
        if (!IsDmuReplayCaptureContext() ||
            packet.ActionId != DmuBlackHoleNothingnessActionId ||
            !IsReplayBlackHoleObject(packet.CasterEntityId, packet.CasterName))
        {
            return;
        }

        foreach (var target in packet.Targets)
        {
            var member = FindCurrentMemberByTargetId(target.TargetId);
            if (member is null)
            {
                continue;
            }

            foreach (var effect in target.Effects)
            {
                if (GetEventKind((ActionEffectKind)effect.Type) != DeathEventKind.Damage)
                {
                    continue;
                }

                var amount = CalculateRawActionEffectAmount(effect);
                if (amount == 0)
                {
                    continue;
                }

                AddRecentReplayMechanicSnapshot(new ReplayMechanicSnapshot(
                    packet.SeenAtUtc,
                    CalculatePullElapsed(packet.SeenAtUtc),
                    1.4f,
                    $"black-hole-blast:{packet.CasterEntityId:X8}:{member.MemberKey}:{packet.Sequence}:{target.TargetIndex}",
                    $"Black Hole -> {member.MemberName}",
                    ReplayMechanicShape.Circle,
                    member.Position.X,
                    member.Position.Y,
                    member.Position.Z,
                    member.Rotation,
                    4.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Blast",
                    "black-hole-blast",
                    packet.ActionId,
                    amount,
                    true));
                break;
            }
        }
    }

    private void CaptureReplayDmuP2PathOfLightMapEffect(RawMapEffectPacket packet)
    {
        if (!IsDmuReplayCaptureContext())
        {
            return;
        }

        var rawState = packet.StateLow | ((uint)packet.StateHigh << 16);
        if (packet.Index is < 1 or > 8 ||
            rawState != DmuP2PathOfLightMapEffectState)
        {
            return;
        }

        var angleDegrees = 180.0f - ((packet.Index - 1) * 45.0f);
        var angleRadians = angleDegrees * MathF.PI / 180.0f;
        var x = DmuArenaCenterX + MathF.Sin(angleRadians) * DmuP2PathOfLightTowerDistance;
        var z = DmuArenaCenterZ + MathF.Cos(angleRadians) * DmuP2PathOfLightTowerDistance;
        var position = new Vector3(x, 0.0f, z);
        var sourceKey = $"dmu-p2-path-of-light:{packet.Index}:{packet.Sequence}";
        PruneActiveDmuP2PathOfLightTowers(packet.SeenAtUtc);

        AddRecentReplayMechanicSnapshot(new ReplayMechanicSnapshot(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            DmuP2PathOfLightTowerFallbackDurationSeconds,
            sourceKey,
            $"Path of Light {packet.Index}",
            ReplayMechanicShape.Tower,
            position.X,
            position.Y,
            position.Z,
            0.0f,
            DmuP2PathOfLightTowerRadius,
            0.0f,
            0.0f,
            0.0f,
            "Path of Light",
            "dmu-p2-path-of-light",
            DmuP2PathOfLightActionId,
            rawState,
            true));
        activeDmuP2PathOfLightTowersByIndex[packet.Index] = new ActiveDmuP2PathOfLightTower(
            packet.Index,
            sourceKey,
            packet.SeenAtUtc,
            position);
    }

    private void CaptureReplayDmuP5ArenaHoleMapEffect(RawMapEffectPacket packet)
    {
        if (!IsDmuReplayCaptureContext())
        {
            return;
        }

        var rawState = packet.StateLow | ((uint)packet.StateHigh << 16);
        if (rawState != ReplayEncounterModules.DmuP5ArenaHoleMapEffectState ||
            !ReplayEncounterModules.TryGetDmuP5ArenaHolePosition(packet.Index, out var x, out var z) ||
            !activeDmuP5ArenaHoleIndices.Add(packet.Index))
        {
            return;
        }

        AddRecentReplayMechanicSnapshot(new ReplayMechanicSnapshot(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            DmuP5ArenaHoleSampleHoldSeconds,
            BuildDmuP5ArenaHoleSourceKey(packet.Index),
            "Arena",
            ReplayMechanicShape.Circle,
            x,
            0.0f,
            z,
            0.0f,
            ReplayEncounterModules.DmuP5ArenaHoleRadius,
            0.0f,
            0.0f,
            0.0f,
            "Hole",
            "dmu-p5-arena-hole",
            packet.Index,
            rawState,
            true));
    }

    private void CaptureReplayDmuP1Action(RawActionEffectPacket packet)
    {
        if (!IsDmuReplayCaptureContext())
        {
            return;
        }

        switch (packet.ActionId)
        {
            case DmuP1RevoltingRuinFirstActionId:
            case DmuP1RevoltingRuinSecondActionId:
                CaptureReplayDmuSourceCone(
                    packet,
                    100.0f,
                    120.0f,
                    "Revolting Ruin III",
                    "dmu-p1-revolting-ruin",
                    2.0f);
                break;
            case DmuP1BlizzardFirstActionId:
            case DmuP1BlizzardSecondActionId:
                CaptureReplayDmuSourceCone(
                    packet,
                    40.0f,
                    90.0f,
                    "Blizzard III",
                    "dmu-p1-blizzard",
                    2.0f);
                break;
            case DmuP1ThunderFirstActionId:
            case DmuP1ThunderSecondActionId:
                CaptureReplayDmuSourceLine(
                    packet,
                    40.0f,
                    10.0f,
                    "Thrumming Thunder III",
                    "dmu-p1-thunder",
                    2.0f);
                break;
            case DmuP1DoubleTroubleActionId:
                CaptureReplayDmuPacketTargetMechanics(
                    packet,
                    ReplayMechanicShape.Stack,
                    6.0f,
                    "Double Trouble",
                    "dmu-p1-double-trouble",
                    2.0f);
                break;
            case DmuP1WaveCannonActionId:
                CaptureReplayDmuSourceLine(
                    packet,
                    100.0f,
                    6.0f,
                    "Wave Cannon",
                    "dmu-p1-wave-cannon",
                    2.0f);
                break;
            case DmuP1ExplosionActionId:
                CaptureReplayDmuSourceTower(
                    packet,
                    4.0f,
                    "Explosion",
                    "dmu-p1-explosion",
                    2.0f);
                break;
            case DmuP1HyperdriveActionId:
                CaptureReplayDmuPacketTargetMechanics(
                    packet,
                    ReplayMechanicShape.Circle,
                    5.0f,
                    "Hyperdrive",
                    "dmu-p1-hyperdrive",
                    1.4f);
                break;
            case DmuP1GravitasActionId:
                CaptureReplayDmuPacketTargetMechanics(
                    packet,
                    ReplayMechanicShape.Stack,
                    5.0f,
                    "Gravitas",
                    "dmu-p1-gravitas",
                    2.0f);
                break;
            case DmuP1GravityActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    5.0f,
                    "Gravity III",
                    "dmu-p1-gravity",
                    2.0f);
                break;
            case DmuP1VitrophyreActionId:
                CaptureReplayDmuPacketTargetMechanics(
                    packet,
                    ReplayMechanicShape.Spread,
                    5.0f,
                    "Vitrophyre",
                    "dmu-p1-vitrophyre",
                    2.0f);
                break;
            case DmuP1GravitationalWaveActionId:
            case DmuP1IntemperateWillActionId:
                CaptureReplayDmuSourceCone(
                    packet,
                    100.0f,
                    180.0f,
                    packet.ActionId == DmuP1GravitationalWaveActionId ? "Gravitational Wave" : "Intemperate Will",
                    "dmu-p1-gravitational-wave",
                    2.0f);
                break;
        }
    }

    private void CaptureReplayDmuP2ForsakenAction(RawActionEffectPacket packet)
    {
        if (!IsDmuReplayCaptureContext())
        {
            return;
        }

        if (packet.ActionId is DmuP2SpelldriverActionId or DmuP2SpellscatterActionId or DmuP2SpellwaveActionId)
        {
            CaptureReplayDmuP2ForsakenTargetEvidence(packet);
        }

        switch (packet.ActionId)
        {
            case DmuP2PathOfLightActionId:
                EndReplayDmuP2PathOfLightTower(packet);
                break;
            case DmuP2SpelldriverActionId:
                CaptureReplayDmuSourceAnchoredMechanic(
                    packet,
                    ReplayMechanicShape.Stack,
                    5.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Spelldriver",
                    "dmu-p2-spelldriver",
                    2.0f);
                break;
            case DmuP2SpellscatterActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Spread,
                    5.0f,
                    "Spellscatter",
                    "dmu-p2-spellscatter",
                    2.0f);
                break;
            case DmuP2SpellwaveActionId:
                CaptureReplayDmuSourceCone(
                    packet,
                    40.0f,
                    90.0f,
                    "Spellwave",
                    "dmu-p2-spellwave",
                    2.0f);
                break;
            case DmuP2FuturesEndBossActionId:
            case DmuP2FuturesEndCloneActionId:
                CaptureReplayDmuP2ForsakenCloneDrops(packet, "Future's End");
                break;
            case DmuP2PastsEndBossActionId:
            case DmuP2PastsEndCloneActionId:
                CaptureReplayDmuP2ForsakenCloneDrops(packet, "Past's End");
                break;
            case DmuP2AllThingsEndingFirstActionId:
            case DmuP2AllThingsEndingSecondActionId:
                CaptureReplayDmuSourceCone(
                    packet,
                    100.0f,
                    180.0f,
                    "All Things Ending",
                    "dmu-p2-all-things-ending",
                    2.0f);
                break;
            case DmuP2UltimateEmbraceActionId:
                CaptureReplayDmuPacketTargetMechanics(
                    packet,
                    ReplayMechanicShape.Stack,
                    5.0f,
                    "Ultimate Embrace",
                    "dmu-p2-ultimate-embrace",
                    2.0f);
                break;
            case DmuP2WingsLeftActionId:
            case DmuP2WingsRightActionId:
                CaptureReplayDmuSourceLine(
                    packet,
                    80.0f,
                    40.0f,
                    "Wings of Destruction",
                    "dmu-p2-wings",
                    2.0f);
                break;
            case DmuP2WingsBusterActionId:
                CaptureReplayDmuPacketTargetMechanics(
                    packet,
                    ReplayMechanicShape.Circle,
                    7.0f,
                    "Wings of Destruction",
                    "dmu-p2-wings-buster",
                    2.0f);
                break;
        }
    }

    private void CaptureReplayDmuP3Action(RawActionEffectPacket packet)
    {
        if (!IsDmuReplayCaptureContext())
        {
            return;
        }

        switch (packet.ActionId)
        {
            case DmuP3AeroIIIAssaultActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    40.0f,
                    "Aero III",
                    "dmu-p3-aero-iii-assault",
                    2.0f);
                break;
            case DmuP3ThunderIIICircleActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    14.8f,
                    "Thunder III",
                    "dmu-p3-thunder-iii-circle",
                    2.0f);
                break;
            case DmuP3StrayFlamesActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Circle,
                    5.0f,
                    "Entropy",
                    "dmu-p3-stray-flames",
                    2.0f);
                break;
            case DmuP3InfernoActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Donut,
                    10.0f,
                    "Inferno",
                    "dmu-p3-inferno",
                    2.0f,
                    width: 4.0f);
                break;
            case DmuP3TsunamiActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Circle,
                    5.0f,
                    "Tsunami",
                    "dmu-p3-tsunami",
                    2.0f);
                break;
            case DmuP3StraySprayActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Donut,
                    10.0f,
                    "Fluid",
                    "dmu-p3-stray-spray",
                    2.0f,
                    width: 4.0f);
                break;
            case DmuP3CycloneActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Stack,
                    6.0f,
                    "Cyclone",
                    "dmu-p3-cyclone",
                    2.0f);
                break;
            case DmuP3ThunderIIIBusterActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Circle,
                    5.0f,
                    "Buster",
                    "dmu-p3-thunder-iii-buster",
                    2.0f);
                break;
            case DmuP3LatLongShockwaveActionId:
                CaptureReplayDmuSourceCone(
                    packet,
                    40.0f,
                    90.0f,
                    "Shockwave",
                    "dmu-p3-latlong-shockwave",
                    2.0f);
                break;
            case DmuP3UmbraSmashActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Circle,
                    20.0f,
                    "Umbra Smash",
                    "dmu-p3-umbra-smash",
                    2.5f);
                break;
            case DmuP3UltimaBlasterChargeActionId:
                CaptureReplayDmuSourceLine(
                    packet,
                    100.0f,
                    6.0f,
                    "Charge",
                    "dmu-p3-ultima-blaster-charge",
                    2.0f);
                break;
            case DmuP3SlapHappyBigActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    13.0f,
                    "Slam",
                    "dmu-p3-slap-happy-big",
                    2.0f);
                break;
            case DmuP3SlapHappySmallActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    6.0f,
                    "Center",
                    "dmu-p3-slap-happy-small",
                    2.0f);
                break;
            case DmuP3SlapHappyShockingImpactActionId:
                CaptureReplayDmuSourceCone(
                    packet,
                    100.0f,
                    60.0f,
                    "Shocking Impact",
                    "dmu-p3-slap-happy-shocking-impact",
                    2.0f);
                break;
            case DmuP3SlapHappyShockwaveActionId:
                CaptureReplayDmuSourceCone(
                    packet,
                    100.0f,
                    60.0f,
                    "Protean",
                    "dmu-p3-slap-happy-shockwave",
                    2.0f);
                break;
            case DmuBlackHoleNothingnessActionId:
                CaptureReplayDmuSourceLine(
                    packet,
                    125.0f,
                    6.0f,
                    "Nothingness",
                    "dmu-p3-nothingness",
                    2.0f);
                break;
            case DmuP3DamningEdictActionId:
                CaptureReplayDmuSourceLine(
                    packet,
                    60.0f,
                    80.0f,
                    "Damning Edict",
                    "dmu-p3-damning-edict",
                    2.0f);
                break;
            case DmuP3LookUponMeAndDespairActionId:
                CaptureReplayDmuSourceLine(
                    packet,
                    100.0f,
                    16.0f,
                    "Despair",
                    "dmu-p3-look-upon-me-and-despair",
                    2.0f);
                break;
            case DmuP3BlizzardIIIActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Circle,
                    6.0f,
                    "Blizzard III",
                    "dmu-p3-blizzard-iii",
                    2.0f);
                break;
            case DmuP3KnockDownActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Stack,
                    6.0f,
                    "Knock Down",
                    "dmu-p3-knock-down",
                    2.0f);
                break;
            case DmuP3StompAMoleActionId:
                CaptureReplayDmuSourceTower(
                    packet,
                    5.0f,
                    "Stomp",
                    "dmu-p3-stomp-a-mole",
                    2.0f);
                break;
            case DmuP3BigBangActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    6.0f,
                    "Big Bang",
                    "dmu-p3-big-bang",
                    2.0f);
                break;
        }
    }

    private void CaptureReplayDmuP4Action(RawActionEffectPacket packet)
    {
        if (!IsDmuReplayCaptureContext())
        {
            return;
        }

        switch (packet.ActionId)
        {
            case DmuP4GrandCrossActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    100.0f,
                    "Grand Cross",
                    "dmu-p4-grand-cross",
                    1.2f);
                break;
            case DmuP4InfernoHitActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    100.0f,
                    "Inferno",
                    "dmu-p4-inferno",
                    1.2f);
                break;
            case DmuP4TsunamiHitActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    100.0f,
                    "Tsunami",
                    "dmu-p4-tsunami",
                    1.2f);
                break;
            case DmuP4DeathBoltNormalActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Spread,
                    8.0f,
                    "Death Bolt",
                    "dmu-p4-death-bolt",
                    1.6f);
                break;
            case DmuP4DeathBoltInvertedActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Stack,
                    8.0f,
                    "Death Bolt",
                    "dmu-p4-death-bolt-inverted",
                    1.6f);
                break;
            case DmuP4DeathWaveNormalActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Stack,
                    8.0f,
                    "Death Wave",
                    "dmu-p4-death-wave",
                    1.6f);
                break;
            case DmuP4DeathWaveInvertedActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Spread,
                    8.0f,
                    "Death Wave",
                    "dmu-p4-death-wave-inverted",
                    1.6f);
                break;
            case DmuP4StrayFlamesNormalActionId:
                CaptureReplayDmuSourceAnchoredMechanic(
                    packet,
                    ReplayMechanicShape.Circle,
                    6.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Stray Flames",
                    "dmu-p4-stray-flames",
                    1.6f);
                break;
            case DmuP4StrayFlamesInvertedActionId:
                CaptureReplayDmuSourceAnchoredMechanic(
                    packet,
                    ReplayMechanicShape.Donut,
                    40.0f,
                    0.0f,
                    6.0f,
                    0.0f,
                    "Stray Flames",
                    "dmu-p4-stray-flames-inverted",
                    1.6f);
                break;
            case DmuP4StraySprayNormalActionId:
                CaptureReplayDmuSourceAnchoredMechanic(
                    packet,
                    ReplayMechanicShape.Donut,
                    40.0f,
                    0.0f,
                    6.0f,
                    0.0f,
                    "Stray Spray",
                    "dmu-p4-stray-spray",
                    1.6f);
                break;
            case DmuP4StraySprayInvertedActionId:
                CaptureReplayDmuSourceAnchoredMechanic(
                    packet,
                    ReplayMechanicShape.Circle,
                    6.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Stray Spray",
                    "dmu-p4-stray-spray-inverted",
                    1.6f);
                break;
            case DmuP4WhiteAntilightActionId:
                CaptureReplayDmuSourceLine(
                    packet,
                    47.0f,
                    21.0f,
                    "White Antilight",
                    "dmu-p4-white-antilight",
                    1.6f);
                break;
            case DmuP4BlackAntilightActionId:
                CaptureReplayDmuSourceLine(
                    packet,
                    47.0f,
                    21.0f,
                    "Black Antilight",
                    "dmu-p4-black-antilight",
                    1.6f);
                break;
            case DmuP4EdgeOfDeathActionId:
                CaptureReplayDmuSourceLine(
                    packet,
                    48.0f,
                    2.0f,
                    "Edge of Death",
                    "dmu-p4-edge-of-death",
                    1.6f);
                break;
            case DmuP4UltimaUpsurgeActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    100.0f,
                    "Ultima Upsurge",
                    "dmu-p4-ultima-upsurge",
                    1.2f);
                break;
        }
    }

    private void CaptureReplayDmuP5Action(RawActionEffectPacket packet)
    {
        if (!IsDmuReplayCaptureContext())
        {
            return;
        }

        switch (packet.ActionId)
        {
            case DmuP5UltimaRepeaterHitActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    100.0f,
                    "Ultima Repeater",
                    "dmu-p5-ultima-repeater",
                    0.9f);
                break;
            case DmuP5FellForcesTankActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Circle,
                    3.0f,
                    "Fell Forces",
                    "dmu-p5-fell-forces-tank",
                    1.4f);
                break;
            case DmuP5FellForcesHealerActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Circle,
                    5.0f,
                    "Fell Forces",
                    "dmu-p5-fell-forces-healer",
                    1.4f);
                break;
            case DmuP5FellForcesDpsActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Circle,
                    5.0f,
                    "Fell Forces",
                    "dmu-p5-fell-forces-dps",
                    1.4f);
                break;
            case DmuP5FloodLineActionId:
                CaptureReplayDmuSourceLine(
                    packet,
                    40.0f,
                    10.0f,
                    "Flood",
                    "dmu-p5-flood",
                    1.4f);
                break;
            case DmuP5ChaoticFloodActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Stack,
                    6.0f,
                    "Chaotic Flood",
                    "dmu-p5-chaotic-flood",
                    1.4f);
                break;
            case DmuP5FlareActionId:
            case DmuP5HolyActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Spread,
                    5.0f,
                    "Maddening Orchestra",
                    "dmu-p5-maddening-orchestra-spread",
                    1.4f);
                break;
            case DmuP5ChaoticFlareActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Stack,
                    5.0f,
                    "Chaotic Flare",
                    "dmu-p5-chaotic-flare",
                    1.4f);
                break;
            case DmuP5FlareDiffusionActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Circle,
                    25.0f,
                    "Surprise Flare",
                    "dmu-p5-surprise-flare",
                    1.4f);
                break;
            case DmuP5ChaoticHolyActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Stack,
                    6.0f,
                    "Surprise Holy",
                    "dmu-p5-surprise-holy",
                    1.4f);
                break;
            case DmuP5TriadFireActionId:
            case DmuP5TriadBlizzardActionId:
            case DmuP5TriadThunderActionId:
                CaptureReplayDmuSourceTower(
                    packet,
                    3.0f,
                    "Celestriad",
                    "dmu-p5-celestriad-tower",
                    2.0f);
                break;
            case DmuP5QuakeActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    10.0f,
                    "Quake",
                    "dmu-p5-quake",
                    2.0f);
                break;
            case DmuP5TornadoActionId:
                CaptureReplayDmuSourceAnchoredMechanic(
                    packet,
                    ReplayMechanicShape.Donut,
                    40.0f,
                    0.0f,
                    10.0f,
                    0.0f,
                    "Tornado",
                    "dmu-p5-tornado",
                    2.0f);
                break;
            case DmuP5StrayApocalypseFirstActionId:
            case DmuP5StrayApocalypseRestActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    6.0f,
                    "Stray Apocalypse",
                    "dmu-p5-stray-apocalypse",
                    1.4f);
                break;
            case DmuP5StrayEntropyActionId:
                CaptureReplayDmuPacketTargetMechanics(
                    packet,
                    ReplayMechanicShape.Spread,
                    5.0f,
                    "Stray Entropy",
                    "dmu-p5-stray-entropy",
                    1.4f);
                break;
            case DmuP5ForsakenGroundActionId:
            case DmuP5ForsakenPuddleActionId:
                CaptureReplayDmuSourceCircle(
                    packet,
                    8.0f,
                    "Forsaken",
                    "dmu-p5-forsaken-ground",
                    2.0f);
                break;
            case DmuP5ForsakenBondsActionId:
                CaptureReplayDmuPacketTargetMechanics(
                    packet,
                    ReplayMechanicShape.Stack,
                    6.0f,
                    "Forsaken Bonds",
                    "dmu-p5-forsaken-bonds",
                    2.0f);
                break;
        }
    }

    private void CaptureReplayCatalogCastPrediction(
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string sourceName,
        DateTime seenAtUtc,
        List<ReplayMechanicSnapshot> mechanicSnapshots)
    {
        if (battleNpc is not Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara ||
            !battleChara.IsCasting ||
            battleChara.CastActionId == 0 ||
            HasActiveReplayMechanicForCast(battleNpc.EntityId, battleChara.CastActionId) ||
            !TryResolveReplayMechanic(battleChara.CastActionId, out var mechanic))
        {
            return;
        }

        var position = battleNpc.Position;
        if (mechanic.Anchor == ReplayMechanicAnchor.Target &&
            TryGetReplayCastTargetPosition(battleChara, out var targetPosition))
        {
            position = targetPosition;
        }

        position = GetReplayMechanicDrawPosition(position, battleNpc.Rotation, mechanic.Geometry);
        var rawEventKind = mechanic.IsKnown ? "bossmod-cast" : "action-sheet-cast";
        var durationSeconds = GetRemainingReplayCastSeconds(battleChara) + DmuReplayPredictionFallbackGraceSeconds;
        var castStartedAtUtc = GetReplayCastStartedAtUtc(seenAtUtc, battleChara);
        var sourceKey = $"{rawEventKind}:{battleNpc.EntityId:X8}:{battleChara.CastActionId}:{castStartedAtUtc.Ticks}";
        var snapshot = CreateReplayCatalogSnapshot(
            seenAtUtc,
            durationSeconds,
            sourceKey,
            sourceName,
            mechanic,
            position,
            battleNpc.Rotation,
            rawEventKind,
            battleChara.CastActionId,
            battleNpc.EntityId);

        RegisterActiveReplayMechanicSnapshot(
            mechanicSnapshots,
            snapshot,
            BuildActiveReplayMechanicKey(rawEventKind, battleNpc.EntityId, battleChara.CastActionId, "main"),
            battleNpc.EntityId,
            battleChara.CastActionId,
            battleChara.CastActionId,
            true,
            true,
            castStartedAtUtc);
    }

    private void CaptureReplayCatalogActionEffect(RawActionEffectPacket packet, bool suppressActionEffect)
    {
        if (suppressActionEffect ||
            !TryGetReplayPacketEnemySourcePose(packet, out var sourcePosition, out var sourceRotation, out var sourceName) ||
            !TryResolveReplayMechanic(packet.ActionId, out var mechanic))
        {
            return;
        }

        var rawEventKind = mechanic.IsKnown ? "bossmod-action" : "action-sheet-action";
        if (mechanic.Anchor == ReplayMechanicAnchor.Target)
        {
            var targetPositions = GetReplayPacketTargetPositions(packet);
            for (var index = 0; index < targetPositions.Count; index++)
            {
                var position = GetReplayMechanicDrawPosition(targetPositions[index], sourceRotation, mechanic.Geometry);
                AddRecentReplayMechanicSnapshot(CreateReplayCatalogSnapshot(
                    packet.SeenAtUtc,
                    1.4f,
                    $"{rawEventKind}:{packet.CasterEntityId:X8}:{packet.Sequence}:{index}",
                    sourceName,
                    mechanic,
                    position,
                    sourceRotation,
                    rawEventKind,
                    packet.ActionId,
                    packet.CasterEntityId));
            }

            return;
        }

        sourcePosition = GetReplayMechanicDrawPosition(sourcePosition, sourceRotation, mechanic.Geometry);
        AddRecentReplayMechanicSnapshot(CreateReplayCatalogSnapshot(
            packet.SeenAtUtc,
            1.4f,
            $"{rawEventKind}:{packet.CasterEntityId:X8}:{packet.Sequence}",
            sourceName,
            mechanic,
            sourcePosition,
            sourceRotation,
            rawEventKind,
            packet.ActionId,
            packet.CasterEntityId));
    }

    private bool TryResolveReplayMechanic(uint actionId, out ResolvedReplayMechanic mechanic)
    {
        var territoryId = currentPullTerritoryId == 0
            ? currentTerritoryId
            : currentPullTerritoryId;
        var metadata = GetReplayActionSheetMetadata(actionId);
        return ReplayMechanicCatalog.TryResolve(
            territoryId,
            actionId,
            metadata?.Name ?? GetActionName(actionId),
            metadata?.CastType ?? 0,
            metadata?.EffectRange ?? 0,
            metadata?.XAxisModifier ?? 0,
            metadata?.Range ?? 0,
            metadata?.TargetArea ?? false,
            out mechanic);
    }

    private ReplayActionSheetMetadata? GetReplayActionSheetMetadata(uint actionId)
    {
        if (replayActionSheetMetadataCache.TryGetValue(actionId, out var cached))
        {
            return cached;
        }

        ReplayActionSheetMetadata? result = null;
        try
        {
            var action = DataManager.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(actionId);
            if (action is not null)
            {
                result = new ReplayActionSheetMetadata(
                    action.Value.Name.ExtractText(),
                    action.Value.CastType,
                    action.Value.EffectRange,
                    action.Value.XAxisModifier,
                    action.Value.Range,
                    action.Value.TargetArea);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load replay geometry for action {ActionId}.", actionId);
        }

        replayActionSheetMetadataCache[actionId] = result;
        return result;
    }

    private bool TryGetReplayCastTargetPosition(
        Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara,
        out Vector3 position)
    {
        position = default;
        try
        {
            var target = ObjectTable.SearchById(battleChara.CastTargetObjectId);
            if (target is null || !IsUsableReplayPosition(target.Position))
            {
                return false;
            }

            position = target.Position;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not resolve replay cast target for action {ActionId}.", battleChara.CastActionId);
            return false;
        }
    }

    private bool TryGetReplayPacketEnemySourcePose(
        RawActionEffectPacket packet,
        out Vector3 position,
        out float rotation,
        out string name)
    {
        var capturedPose = packet.ReplayPoses.FirstOrDefault(pose =>
            pose.SampleSource == ReplayPositionSampleSource.ActionEffectSource &&
            pose.ActorKind == ReplayActorKind.Enemy &&
            IsUsableReplayPosition(pose.Position));
        if (capturedPose is not null)
        {
            position = capturedPose.Position;
            rotation = capturedPose.Rotation;
            name = capturedPose.ActorName;
            return true;
        }

        try
        {
            if (ObjectTable.SearchByEntityId(packet.CasterEntityId) is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc)
            {
                position = default;
                rotation = 0.0f;
                name = string.Empty;
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not inspect replay action source {EntityId:X8}.", packet.CasterEntityId);
            position = default;
            rotation = 0.0f;
            name = string.Empty;
            return false;
        }

        return TryGetReplayActionSourcePose(packet, out position, out rotation, out name);
    }

    private IReadOnlyList<Vector3> GetReplayPacketTargetPositions(RawActionEffectPacket packet)
    {
        var positions = packet.ReplayPoses
            .Where(pose =>
                pose.SampleSource == ReplayPositionSampleSource.ActionEffectTarget &&
                IsUsableReplayPosition(pose.Position))
            .GroupBy(pose => pose.EntityId)
            .Select(group => group.First().Position)
            .ToList();
        if (positions.Count > 0)
        {
            return positions;
        }

        if (packet.HasTargetPosition && IsUsableReplayPosition(packet.TargetPosition))
        {
            return [packet.TargetPosition];
        }

        if (TryGetReplayPacketMechanicCenter(packet, out var center))
        {
            return [center];
        }

        return [];
    }

    private ReplayMechanicSnapshot CreateReplayCatalogSnapshot(
        DateTime seenAtUtc,
        float durationSeconds,
        string sourceKey,
        string sourceName,
        ResolvedReplayMechanic mechanic,
        Vector3 position,
        float rotation,
        string rawEventKind,
        uint actionId,
        uint sourceEntityId)
    {
        return new ReplayMechanicSnapshot(
            seenAtUtc,
            CalculatePullElapsed(seenAtUtc),
            durationSeconds,
            sourceKey,
            sourceName,
            mechanic.Geometry.Shape,
            position.X,
            position.Y,
            position.Z,
            rotation,
            mechanic.Geometry.Radius,
            mechanic.Geometry.Length,
            mechanic.Geometry.Width,
            mechanic.Geometry.AngleDegrees,
            mechanic.Label,
            rawEventKind,
            actionId,
            sourceEntityId,
            mechanic.IsKnown);
    }

    private static Vector3 GetReplayMechanicDrawPosition(
        Vector3 anchor,
        float rotation,
        ReplayMechanicGeometry geometry)
    {
        if (geometry.Shape != ReplayMechanicShape.Line || geometry.Length <= 0.0f)
        {
            return anchor;
        }

        var direction = ReplayDirectionFromRotation(rotation);
        return OffsetReplayPosition(anchor, direction * (geometry.Length * 0.5f));
    }

    private void CaptureReplayDmuP3CastPrediction(
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string name,
        DateTime seenAtUtc,
        List<ReplayMechanicSnapshot> mechanicSnapshots)
    {
        if (!IsDmuReplayCaptureContext() ||
            battleNpc is not Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara ||
            !battleChara.IsCasting ||
            battleChara.CastActionId == 0)
        {
            return;
        }

        var castActionId = battleChara.CastActionId;
        var remainingCastSeconds = GetRemainingReplayCastSeconds(battleChara);
        var castStartedAtUtc = GetReplayCastStartedAtUtc(seenAtUtc, battleChara);
        switch (castActionId)
        {
            case DmuP3ThunderIIICircleActionId:
                RegisterActiveReplayMechanicSnapshot(
                    mechanicSnapshots,
                    CreateDmuSourcePredictionSnapshot(
                        seenAtUtc,
                        battleNpc,
                        name,
                        ReplayMechanicShape.Circle,
                        battleNpc.Position,
                        battleNpc.Rotation,
                        14.8f,
                        0.0f,
                        0.0f,
                        0.0f,
                        "Thunder III",
                        "dmu-p3-thunder-iii-predicted",
                        castActionId,
                        "main",
                        remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds),
                    BuildActiveReplayMechanicKey("dmu-p3-thunder-iii-predicted", battleNpc.EntityId, castActionId, "main"),
                    battleNpc.EntityId,
                    castActionId,
                    DmuP3ThunderIIICircleActionId,
                    true,
                    true,
                    castStartedAtUtc);
                break;
            case DmuP3LongitudinalImplosionCastActionId:
                CaptureReplayDmuP3LatLongPrediction(
                    battleNpc,
                    name,
                    seenAtUtc,
                    mechanicSnapshots,
                    castActionId,
                    [battleNpc.Rotation, battleNpc.Rotation + MathF.PI],
                    remainingCastSeconds,
                    castStartedAtUtc);
                break;
            case DmuP3LatitudinalImplosionCastActionId:
                CaptureReplayDmuP3LatLongPrediction(
                    battleNpc,
                    name,
                    seenAtUtc,
                    mechanicSnapshots,
                    castActionId,
                    [battleNpc.Rotation + (MathF.PI * 0.5f), battleNpc.Rotation - (MathF.PI * 0.5f)],
                    remainingCastSeconds,
                    castStartedAtUtc);
                break;
            case DmuP3SlapHappyLeftHandCastActionId:
                CaptureReplayDmuP3SlapHappyPrediction(
                    battleNpc,
                    name,
                    seenAtUtc,
                    mechanicSnapshots,
                    castActionId,
                    useLeftHand: true,
                    remainingCastSeconds,
                    castStartedAtUtc);
                break;
            case DmuP3SlapHappyRightHandCastActionId:
                CaptureReplayDmuP3SlapHappyPrediction(
                    battleNpc,
                    name,
                    seenAtUtc,
                    mechanicSnapshots,
                    castActionId,
                    useLeftHand: false,
                    remainingCastSeconds,
                    castStartedAtUtc);
                break;
            case DmuP3DamningEdictActionId:
                RegisterActiveReplayMechanicSnapshot(
                    mechanicSnapshots,
                    CreateDmuForwardLinePredictionSnapshot(
                        seenAtUtc,
                        battleNpc,
                        name,
                        60.0f,
                        80.0f,
                        "Damning Edict",
                        "dmu-p3-damning-edict-predicted",
                        castActionId,
                        "main",
                        remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds),
                    BuildActiveReplayMechanicKey("dmu-p3-damning-edict-predicted", battleNpc.EntityId, castActionId, "main"),
                    battleNpc.EntityId,
                    castActionId,
                    DmuP3DamningEdictActionId,
                    true,
                    true,
                    castStartedAtUtc);
                break;
            case DmuP3LookUponMeAndDespairActionId:
                RegisterActiveReplayMechanicSnapshot(
                    mechanicSnapshots,
                    CreateDmuForwardLinePredictionSnapshot(
                        seenAtUtc,
                        battleNpc,
                        name,
                        100.0f,
                        16.0f,
                        "Despair",
                        "dmu-p3-despair-predicted",
                        castActionId,
                        "main",
                        remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds),
                    BuildActiveReplayMechanicKey("dmu-p3-despair-predicted", battleNpc.EntityId, castActionId, "main"),
                    battleNpc.EntityId,
                    castActionId,
                    DmuP3LookUponMeAndDespairActionId,
                    true,
                    true,
                    castStartedAtUtc);
                break;
            case DmuP3BlizzardIIIActionId:
                RegisterActiveReplayMechanicSnapshot(
                    mechanicSnapshots,
                    CreateDmuSourcePredictionSnapshot(
                        seenAtUtc,
                        battleNpc,
                        name,
                        ReplayMechanicShape.Circle,
                        battleNpc.Position,
                        battleNpc.Rotation,
                        6.0f,
                        0.0f,
                        0.0f,
                        0.0f,
                        "Blizzard III",
                        "dmu-p3-blizzard-iii-predicted",
                        castActionId,
                        "main",
                        remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds),
                    BuildActiveReplayMechanicKey("dmu-p3-blizzard-iii-predicted", battleNpc.EntityId, castActionId, "main"),
                    battleNpc.EntityId,
                    castActionId,
                    DmuP3BlizzardIIIActionId,
                    true,
                    true,
                    castStartedAtUtc);
                break;
            case DmuP3StompAMoleVisualActionId:
                CaptureReplayDmuP3StompAMolePrediction(
                    battleNpc,
                    name,
                    seenAtUtc,
                    mechanicSnapshots,
                    castActionId,
                    remainingCastSeconds,
                    castStartedAtUtc);
                break;
        }
    }

    private void CaptureReplayDmuP1P2CastPrediction(
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string name,
        DateTime seenAtUtc,
        List<ReplayMechanicSnapshot> mechanicSnapshots)
    {
        if (!IsDmuReplayCaptureContext() ||
            battleNpc is not Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara ||
            !battleChara.IsCasting ||
            battleChara.CastActionId == 0)
        {
            return;
        }

        var castActionId = battleChara.CastActionId;
        var remainingCastSeconds = GetRemainingReplayCastSeconds(battleChara);
        var castStartedAtUtc = GetReplayCastStartedAtUtc(seenAtUtc, battleChara);
        switch (castActionId)
        {
            case DmuP1RevoltingRuinFirstActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Cone,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    100.0f,
                    100.0f,
                    0.0f,
                    120.0f,
                    "Revolting Ruin III",
                    "dmu-p1-revolting-ruin-predicted",
                    castActionId,
                    castActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP1BlizzardFirstActionId:
            case DmuP1BlizzardSecondActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Cone,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    40.0f,
                    40.0f,
                    0.0f,
                    90.0f,
                    "Blizzard III",
                    "dmu-p1-blizzard-predicted",
                    castActionId,
                    castActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP1ThunderFirstActionId:
            case DmuP1ThunderSecondActionId:
                RegisterReplayForwardLinePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    40.0f,
                    10.0f,
                    "Thrumming Thunder III",
                    "dmu-p1-thunder-predicted",
                    castActionId,
                    castActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP1ExplosionActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Tower,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    4.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Explosion",
                    "dmu-p1-explosion-predicted",
                    castActionId,
                    castActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP2WingsLeftActionId:
            case DmuP2WingsRightActionId:
                RegisterReplayForwardLinePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    80.0f,
                    40.0f,
                    "Wings of Destruction",
                    "dmu-p2-wings-predicted",
                    castActionId,
                    castActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
        }
    }

    private void CaptureReplayDmuP4P5CastPrediction(
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string name,
        DateTime seenAtUtc,
        List<ReplayMechanicSnapshot> mechanicSnapshots)
    {
        if (!IsDmuReplayCaptureContext() ||
            battleNpc is not Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara ||
            !battleChara.IsCasting ||
            battleChara.CastActionId == 0)
        {
            return;
        }

        var castActionId = battleChara.CastActionId;
        var remainingCastSeconds = GetRemainingReplayCastSeconds(battleChara);
        var castStartedAtUtc = GetReplayCastStartedAtUtc(seenAtUtc, battleChara);
        switch (castActionId)
        {
            case DmuP4GrandCrossActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Circle,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    100.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Grand Cross",
                    "dmu-p4-grand-cross-predicted",
                    castActionId,
                    DmuP4GrandCrossActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP4InfernoCastActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Circle,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    100.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Inferno",
                    "dmu-p4-inferno-predicted",
                    castActionId,
                    DmuP4InfernoHitActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: false,
                    castStartedAtUtc);
                break;
            case DmuP4TsunamiCastActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Circle,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    100.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Tsunami",
                    "dmu-p4-tsunami-predicted",
                    castActionId,
                    DmuP4TsunamiHitActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: false,
                    castStartedAtUtc);
                break;
            case DmuP4StrayFlamesNormalActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Circle,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    6.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Stray Flames",
                    "dmu-p4-stray-flames-predicted",
                    castActionId,
                    DmuP4StrayFlamesNormalActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP4StrayFlamesInvertedActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Donut,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    40.0f,
                    0.0f,
                    6.0f,
                    0.0f,
                    "Stray Flames",
                    "dmu-p4-stray-flames-inverted-predicted",
                    castActionId,
                    DmuP4StrayFlamesInvertedActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP4StraySprayNormalActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Donut,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    40.0f,
                    0.0f,
                    6.0f,
                    0.0f,
                    "Stray Spray",
                    "dmu-p4-stray-spray-predicted",
                    castActionId,
                    DmuP4StraySprayNormalActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP4StraySprayInvertedActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Circle,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    6.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Stray Spray",
                    "dmu-p4-stray-spray-inverted-predicted",
                    castActionId,
                    DmuP4StraySprayInvertedActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP4WhiteAntilightActionId:
                RegisterReplayForwardLinePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    47.0f,
                    21.0f,
                    "White Antilight",
                    "dmu-p4-white-antilight-predicted",
                    castActionId,
                    DmuP4WhiteAntilightActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP4BlackAntilightActionId:
                RegisterReplayForwardLinePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    47.0f,
                    21.0f,
                    "Black Antilight",
                    "dmu-p4-black-antilight-predicted",
                    castActionId,
                    DmuP4BlackAntilightActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP4EdgeOfDeathActionId:
                RegisterReplayForwardLinePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    48.0f,
                    2.0f,
                    "Edge of Death",
                    "dmu-p4-edge-of-death-predicted",
                    castActionId,
                    DmuP4EdgeOfDeathActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP4UltimaUpsurgeActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Circle,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    100.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Ultima Upsurge",
                    "dmu-p4-ultima-upsurge-predicted",
                    castActionId,
                    DmuP4UltimaUpsurgeActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP5FloodRectCastActionId:
                RegisterReplayForwardLinePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    40.0f,
                    10.0f,
                    "Flood",
                    "dmu-p5-flood-predicted",
                    castActionId,
                    DmuP5FloodLineActionId,
                    remainingCastSeconds + DmuP5FloodPredictionExtraSeconds,
                    endsWhenSourceStopsCasting: false,
                    castStartedAtUtc,
                    endsWhenSourceMissing: false);
                break;
            case DmuP5StrayApocalypseFirstActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Circle,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    6.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Stray Apocalypse",
                    "dmu-p5-stray-apocalypse-predicted",
                    castActionId,
                    castActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
            case DmuP5ForsakenGroundActionId:
            case DmuP5ForsakenPuddleActionId:
                RegisterReplaySourcePrediction(
                    mechanicSnapshots,
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Circle,
                    battleNpc.Position,
                    battleNpc.Rotation,
                    8.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    "Forsaken",
                    "dmu-p5-forsaken-ground-predicted",
                    castActionId,
                    castActionId,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds,
                    endsWhenSourceStopsCasting: true,
                    castStartedAtUtc);
                break;
        }
    }

    private void RegisterReplayForwardLinePrediction(
        List<ReplayMechanicSnapshot> mechanicSnapshots,
        DateTime seenAtUtc,
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string name,
        float length,
        float width,
        string label,
        string rawEventKind,
        uint castActionId,
        uint resolveActionId,
        float durationSeconds,
        bool endsWhenSourceStopsCasting,
        DateTime castStartedAtUtc,
        bool endsWhenSourceMissing = true)
    {
        RegisterActiveReplayMechanicSnapshot(
            mechanicSnapshots,
            CreateDmuForwardLinePredictionSnapshot(
                seenAtUtc,
                battleNpc,
                name,
                length,
                width,
                label,
                rawEventKind,
                castActionId,
                "main",
                durationSeconds),
            BuildActiveReplayMechanicKey(rawEventKind, battleNpc.EntityId, castActionId, "main"),
            battleNpc.EntityId,
            castActionId,
            resolveActionId,
            endsWhenSourceMissing,
            endsWhenSourceStopsCasting,
            castStartedAtUtc);
    }

    private void RegisterReplaySourcePrediction(
        List<ReplayMechanicSnapshot> mechanicSnapshots,
        DateTime seenAtUtc,
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string name,
        ReplayMechanicShape shape,
        Vector3 position,
        float rotation,
        float radius,
        float length,
        float width,
        float angleDegrees,
        string label,
        string rawEventKind,
        uint castActionId,
        uint resolveActionId,
        float durationSeconds,
        bool endsWhenSourceStopsCasting,
        DateTime castStartedAtUtc)
    {
        RegisterActiveReplayMechanicSnapshot(
            mechanicSnapshots,
            CreateDmuSourcePredictionSnapshot(
                seenAtUtc,
                battleNpc,
                name,
                shape,
                position,
                rotation,
                radius,
                length,
                width,
                angleDegrees,
                label,
                rawEventKind,
                castActionId,
                "main",
                durationSeconds),
            BuildActiveReplayMechanicKey(rawEventKind, battleNpc.EntityId, castActionId, "main"),
            battleNpc.EntityId,
            castActionId,
            resolveActionId,
            true,
            endsWhenSourceStopsCasting,
            castStartedAtUtc);
    }

    private void CaptureReplayDmuP3LatLongPrediction(
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string name,
        DateTime seenAtUtc,
        List<ReplayMechanicSnapshot> mechanicSnapshots,
        uint castActionId,
        IReadOnlyList<float> rotations,
        float remainingCastSeconds,
        DateTime castStartedAtUtc)
    {
        for (var index = 0; index < rotations.Count; index++)
        {
            var variant = index.ToString(CultureInfo.InvariantCulture);
            RegisterActiveReplayMechanicSnapshot(
                mechanicSnapshots,
                CreateDmuSourcePredictionSnapshot(
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Cone,
                    battleNpc.Position,
                    rotations[index],
                    40.0f,
                    40.0f,
                    0.0f,
                    90.0f,
                    "Shockwave",
                    "dmu-p3-latlong-shockwave-predicted",
                    castActionId,
                    variant,
                    remainingCastSeconds + DmuReplayPredictionFallbackGraceSeconds + 2.5f),
                BuildActiveReplayMechanicKey("dmu-p3-latlong-shockwave-predicted", battleNpc.EntityId, castActionId, variant),
                battleNpc.EntityId,
                castActionId,
                DmuP3LatLongShockwaveActionId,
                true,
                false,
                castStartedAtUtc);
        }
    }

    private void CaptureReplayDmuP3SlapHappyPrediction(
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string name,
        DateTime seenAtUtc,
        List<ReplayMechanicSnapshot> mechanicSnapshots,
        uint castActionId,
        bool useLeftHand,
        float remainingCastSeconds,
        DateTime castStartedAtUtc)
    {
        var forward = ReplayDirectionFromRotation(battleNpc.Rotation);
        var side = useLeftHand
            ? RotateReplayVectorLeft(forward)
            : RotateReplayVectorRight(forward);
        var baseOffset = side * 10.0f;
        var sideLeft = RotateReplayVectorLeft(baseOffset);
        var sideRight = RotateReplayVectorRight(baseOffset);
        var arenaCenter = new Vector3(DmuArenaCenterX, battleNpc.Position.Y, DmuArenaCenterZ);
        var positions = useLeftHand
            ? new[]
            {
                OffsetReplayPosition(arenaCenter, baseOffset + sideLeft),
                OffsetReplayPosition(arenaCenter, baseOffset),
                OffsetReplayPosition(arenaCenter, baseOffset + sideRight),
                arenaCenter,
            }
            : new[]
            {
                OffsetReplayPosition(arenaCenter, baseOffset + sideRight),
                OffsetReplayPosition(arenaCenter, baseOffset),
                OffsetReplayPosition(arenaCenter, baseOffset + sideLeft),
                arenaCenter,
            };
        var radii = new[] { 13.0f, 13.0f, 13.0f, 6.0f };
        var labels = new[] { "Slam 1", "Slam 2", "Slam 3", "Center" };
        for (var index = 0; index < positions.Length; index++)
        {
            var variant = index.ToString(CultureInfo.InvariantCulture);
            var durationSeconds = remainingCastSeconds + DmuReplaySlapHappyResolveDelaySeconds[index];
            RegisterActiveReplayMechanicSnapshot(
                mechanicSnapshots,
                CreateDmuSourcePredictionSnapshot(
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Circle,
                    positions[index],
                    0.0f,
                    radii[index],
                    0.0f,
                    0.0f,
                    0.0f,
                    labels[index],
                    "dmu-p3-slap-happy-predicted",
                    castActionId,
                    variant,
                    durationSeconds),
                BuildActiveReplayMechanicKey("dmu-p3-slap-happy-predicted", battleNpc.EntityId, castActionId, variant),
                battleNpc.EntityId,
                castActionId,
                0,
                true,
                false,
                castStartedAtUtc);
        }
    }

    private void CaptureReplayDmuP3StompAMolePrediction(
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string name,
        DateTime seenAtUtc,
        List<ReplayMechanicSnapshot> mechanicSnapshots,
        uint castActionId,
        float remainingCastSeconds,
        DateTime castStartedAtUtc)
    {
        var forward = ReplayDirectionFromRotation(battleNpc.Rotation);
        var offsets = new[]
        {
            RotateReplayVectorRight(forward) * 10.0f,
            RotateReplayVectorLeft(forward) * 10.0f,
        };
        for (var index = 0; index < offsets.Length; index++)
        {
            var variant = index.ToString(CultureInfo.InvariantCulture);
            var durationSeconds = remainingCastSeconds + DmuReplayStompAMoleResolveDelaySeconds[index];
            RegisterActiveReplayMechanicSnapshot(
                mechanicSnapshots,
                CreateDmuSourcePredictionSnapshot(
                    seenAtUtc,
                    battleNpc,
                    name,
                    ReplayMechanicShape.Tower,
                    OffsetReplayPosition(battleNpc.Position, offsets[index]),
                    0.0f,
                    5.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    $"Stomp {index + 1}",
                    "dmu-p3-stomp-a-mole-predicted",
                    castActionId,
                    variant,
                    durationSeconds),
                BuildActiveReplayMechanicKey("dmu-p3-stomp-a-mole-predicted", battleNpc.EntityId, castActionId, variant),
                battleNpc.EntityId,
                castActionId,
                0,
                true,
                false,
                castStartedAtUtc);
        }
    }

    private ReplayMechanicSnapshot CreateDmuForwardLinePredictionSnapshot(
        DateTime seenAtUtc,
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string sourceName,
        float length,
        float width,
        string label,
        string rawEventKind,
        uint castActionId,
        string variant,
        float durationSeconds)
    {
        var direction = ReplayDirectionFromRotation(battleNpc.Rotation);
        var center = new Vector3(
            battleNpc.Position.X + (direction.X * length * 0.5f),
            battleNpc.Position.Y,
            battleNpc.Position.Z + (direction.Y * length * 0.5f));
        return CreateDmuSourcePredictionSnapshot(
            seenAtUtc,
            battleNpc,
            sourceName,
            ReplayMechanicShape.Line,
            center,
            battleNpc.Rotation,
            0.0f,
            length,
            width,
            0.0f,
            label,
            rawEventKind,
            castActionId,
            variant,
            durationSeconds);
    }

    private ReplayMechanicSnapshot CreateDmuSourcePredictionSnapshot(
        DateTime seenAtUtc,
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string sourceName,
        ReplayMechanicShape shape,
        Vector3 position,
        float rotation,
        float radius,
        float length,
        float width,
        float angleDegrees,
        string label,
        string rawEventKind,
        uint castActionId,
        string variant,
        float durationSeconds)
    {
        var safeDurationSeconds = Math.Max(DmuReplayActiveMechanicMinDurationSeconds, durationSeconds);
        return new ReplayMechanicSnapshot(
            seenAtUtc,
            CalculatePullElapsed(seenAtUtc),
            safeDurationSeconds,
            $"{rawEventKind}:cast:{battleNpc.EntityId:X8}:{castActionId}:{variant}:{seenAtUtc.Ticks}",
            string.IsNullOrWhiteSpace(sourceName) ? GetEntityDisplayName(battleNpc.EntityId) : sourceName,
            shape,
            position.X,
            position.Y,
            position.Z,
            rotation,
            radius,
            length,
            width,
            angleDegrees,
            label,
            rawEventKind,
            castActionId,
            battleNpc.EntityId,
            true);
    }

    private void CaptureReplayDmuPacketCenteredMechanic(
        RawActionEffectPacket packet,
        ReplayMechanicShape shape,
        float radius,
        string label,
        string rawEventKind,
        float durationSeconds,
        float length = 0.0f,
        float width = 0.0f,
        float angleDegrees = 0.0f)
    {
        if (!TryGetReplayPacketMechanicCenter(packet, out var center))
        {
            return;
        }

        var sourceName = string.IsNullOrWhiteSpace(packet.CasterName)
            ? GetEntityDisplayName(packet.CasterEntityId)
            : packet.CasterName;

        AddRecentReplayMechanicSnapshot(new ReplayMechanicSnapshot(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            durationSeconds,
            $"{rawEventKind}:{packet.CasterEntityId:X8}:{packet.ActionId}:{packet.Sequence}",
            sourceName,
            shape,
            center.X,
            center.Y,
            center.Z,
            0.0f,
            radius,
            length,
            width,
            angleDegrees,
            label,
            rawEventKind,
            packet.ActionId,
            packet.ActionSequence,
            true));
    }

    private void CaptureReplayDmuPacketTargetMechanics(
        RawActionEffectPacket packet,
        ReplayMechanicShape shape,
        float radius,
        string label,
        string rawEventKind,
        float durationSeconds)
    {
        var sourceName = string.IsNullOrWhiteSpace(packet.CasterName)
            ? GetEntityDisplayName(packet.CasterEntityId)
            : packet.CasterName;
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in packet.Targets)
        {
            var member = FindCurrentMemberByTargetId(target.TargetId);
            if (member is null || !seenTargets.Add(member.MemberKey))
            {
                continue;
            }

            AddRecentReplayMechanicSnapshot(new ReplayMechanicSnapshot(
                packet.SeenAtUtc,
                CalculatePullElapsed(packet.SeenAtUtc),
                durationSeconds,
                $"{rawEventKind}:{packet.CasterEntityId:X8}:{packet.ActionId}:{packet.Sequence}:{member.MemberKey}",
                sourceName,
                shape,
                member.Position.X,
                member.Position.Y,
                member.Position.Z,
                member.Rotation,
                radius,
                0.0f,
                0.0f,
                0.0f,
                label,
                rawEventKind,
                packet.ActionId,
                unchecked((uint)target.TargetIndex),
                true));
        }
    }

    private void CaptureReplayDmuSourceCone(
        RawActionEffectPacket packet,
        float length,
        float angleDegrees,
        string label,
        string rawEventKind,
        float durationSeconds)
    {
        if (!TryGetReplayActionSourcePose(packet, out var position, out var rotation, out var sourceName))
        {
            return;
        }

        AddRecentReplayMechanicSnapshot(new ReplayMechanicSnapshot(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            durationSeconds,
            $"{rawEventKind}:{packet.CasterEntityId:X8}:{packet.Sequence}",
            sourceName,
            ReplayMechanicShape.Cone,
            position.X,
            position.Y,
            position.Z,
            rotation,
            length,
            length,
            0.0f,
            angleDegrees,
            label,
            rawEventKind,
            packet.ActionId,
            packet.CasterEntityId,
            true));
    }

    private void CaptureReplayDmuSourceCircle(
        RawActionEffectPacket packet,
        float radius,
        string label,
        string rawEventKind,
        float durationSeconds)
    {
        CaptureReplayDmuSourceAnchoredMechanic(
            packet,
            ReplayMechanicShape.Circle,
            radius,
            0.0f,
            0.0f,
            0.0f,
            label,
            rawEventKind,
            durationSeconds);
    }

    private void CaptureReplayDmuSourceTower(
        RawActionEffectPacket packet,
        float radius,
        string label,
        string rawEventKind,
        float durationSeconds)
    {
        CaptureReplayDmuSourceAnchoredMechanic(
            packet,
            ReplayMechanicShape.Tower,
            radius,
            0.0f,
            0.0f,
            0.0f,
            label,
            rawEventKind,
            durationSeconds);
    }

    private void CaptureReplayDmuSourceLine(
        RawActionEffectPacket packet,
        float length,
        float width,
        string label,
        string rawEventKind,
        float durationSeconds)
    {
        if (!TryGetReplayActionSourcePose(packet, out var position, out var rotation, out var sourceName))
        {
            return;
        }

        var direction = ReplayDirectionFromRotation(rotation);
        var center = new Vector3(
            position.X + (direction.X * length * 0.5f),
            position.Y,
            position.Z + (direction.Y * length * 0.5f));

        AddRecentReplayMechanicSnapshot(new ReplayMechanicSnapshot(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            durationSeconds,
            $"{rawEventKind}:{packet.CasterEntityId:X8}:{packet.Sequence}",
            sourceName,
            ReplayMechanicShape.Line,
            center.X,
            center.Y,
            center.Z,
            rotation,
            0.0f,
            length,
            width,
            0.0f,
            label,
            rawEventKind,
            packet.ActionId,
            packet.CasterEntityId,
            true));
    }

    private void CaptureReplayDmuSourceAnchoredMechanic(
        RawActionEffectPacket packet,
        ReplayMechanicShape shape,
        float radius,
        float length,
        float width,
        float angleDegrees,
        string label,
        string rawEventKind,
        float durationSeconds)
    {
        if (!TryGetReplayActionSourcePose(packet, out var position, out var rotation, out var sourceName))
        {
            return;
        }

        AddRecentReplayMechanicSnapshot(new ReplayMechanicSnapshot(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            durationSeconds,
            $"{rawEventKind}:{packet.CasterEntityId:X8}:{packet.Sequence}",
            sourceName,
            shape,
            position.X,
            position.Y,
            position.Z,
            rotation,
            radius,
            length,
            width,
            angleDegrees,
            label,
            rawEventKind,
            packet.ActionId,
            packet.CasterEntityId,
            true));
    }

    private bool TryGetReplayActionSourcePose(RawActionEffectPacket packet, out Vector3 position, out float rotation, out string sourceName)
    {
        position = packet.CasterPosition;
        rotation = packet.CasterRotation;
        sourceName = string.IsNullOrWhiteSpace(packet.CasterName)
            ? GetEntityDisplayName(packet.CasterEntityId)
            : packet.CasterName;

        if (packet.HasCasterPose && IsUsableReplayPosition(position))
        {
            return true;
        }

        return TryGetReplayObjectPose(packet.CasterEntityId, out position, out rotation, out sourceName);
    }

    private void RegisterActiveReplayMechanicSnapshot(
        List<ReplayMechanicSnapshot> mechanicSnapshots,
        ReplayMechanicSnapshot snapshot,
        string activeKey,
        uint sourceEntityId,
        uint castActionId,
        uint resolveActionId,
        bool endsWhenSourceMissing,
        bool endsWhenSourceStopsCasting,
        DateTime? castStartedAtUtc = null)
    {
        var activeCastStartedAtUtc = castStartedAtUtc ?? snapshot.SeenAtUtc;
        var currentStillWithinFallback = false;
        if (activeReplayMechanicsByKey.TryGetValue(activeKey, out var current) &&
            snapshot.SeenAtUtc <= current.FallbackEndAtUtc)
        {
            currentStillWithinFallback = true;
            if (castStartedAtUtc is null ||
                Duration(current.CastStartedAtUtc, activeCastStartedAtUtc) <= ReplayPositionSampleInterval)
            {
                return;
            }
        }

        if (current is not null)
        {
            ClampRecentReplayMechanicEnd(current.SourceKey, currentStillWithinFallback ? snapshot.SeenAtUtc : current.FallbackEndAtUtc);
        }

        var fallbackEndAtUtc = snapshot.SeenAtUtc.AddSeconds(Math.Max(DmuReplayActiveMechanicMinDurationSeconds, snapshot.DurationSeconds));
        activeReplayMechanicsByKey[activeKey] = new ActiveReplayMechanic(
            activeKey,
            snapshot.SourceKey,
            sourceEntityId,
            castActionId,
            resolveActionId,
            activeCastStartedAtUtc,
            snapshot.SeenAtUtc,
            fallbackEndAtUtc,
            endsWhenSourceMissing,
            endsWhenSourceStopsCasting);
        mechanicSnapshots.Add(snapshot);
    }

    private void ResolveActiveReplayMechanicsForAction(RawActionEffectPacket packet)
    {
        if (activeReplayMechanicsByKey.Count == 0)
        {
            return;
        }

        foreach (var entry in activeReplayMechanicsByKey.Values
            .Where(active => ReplayActiveMechanicMatchesResolveAction(active, packet))
            .ToList())
        {
            ClampRecentReplayMechanicEnd(entry.SourceKey, packet.SeenAtUtc);
            activeReplayMechanicsByKey.Remove(entry.ActiveKey);
        }
    }

    private bool HasActiveReplayMechanicForAction(RawActionEffectPacket packet)
    {
        return activeReplayMechanicsByKey.Values.Any(active => ReplayActiveMechanicMatchesResolveAction(active, packet));
    }

    private bool HasActiveReplayMechanicForCast(uint sourceEntityId, uint castActionId)
    {
        return activeReplayMechanicsByKey.Values.Any(active =>
            active.SourceEntityId == sourceEntityId &&
            active.CastActionId == castActionId);
    }

    private void AddActionEffectReplayPoseSamples(RawActionEffectPacket packet)
    {
        if (packet.ReplayPoses.Count == 0)
        {
            return;
        }

        var addedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pose in packet.ReplayPoses)
        {
            if (!ShouldSaveActionEffectReplayPose(pose) ||
                !TryCreateReplayPositionSnapshot(pose, out var snapshot) ||
                !addedKeys.Add($"{snapshot.SampleSource}:{snapshot.ActorKey}"))
            {
                continue;
            }

            AddRecentReplayPositionSnapshot(snapshot);
        }
    }

    private static bool ShouldSaveActionEffectReplayPose(RawActorPoseSnapshot pose)
    {
        if (pose.ActorKind != ReplayActorKind.Enemy)
        {
            return true;
        }

        if (pose.IsTargetable)
        {
            return true;
        }

        // Untargetable action-effect enemies are usually mechanic anchors or transient clones.
        // The mechanic draw path captures those shapes separately; periodic object sampling owns visible NPC presence.
        return false;
    }

    private bool TryCreateReplayPositionSnapshot(RawActorPoseSnapshot pose, out ReplayPositionSnapshot snapshot)
    {
        snapshot = default!;
        var actorKey = string.Empty;
        var actorName = pose.ActorName;
        var partyIndex = pose.PartyIndex;
        var classJobId = pose.ClassJobId;
        var classJobName = pose.ClassJobName;
        if (pose.ActorKind == ReplayActorKind.Player)
        {
            var member = FindCurrentMemberByEntityId(pose.EntityId);
            if (member is not null)
            {
                actorKey = $"player:{member.MemberKey}";
                actorName = member.MemberName;
                partyIndex = member.PartyIndex;
                classJobId = member.ClassJobId;
                classJobName = member.ClassJobName;
            }
            else if (pose.EntityId != 0)
            {
                actorKey = $"player:entity:{pose.EntityId:X8}";
            }
        }
        else if (pose.EntityId != 0)
        {
            actorKey = $"enemy:{pose.EntityId:X8}";
        }

        if (string.IsNullOrWhiteSpace(actorKey) ||
            !IsUsableReplayPosition(pose.Position))
        {
            return false;
        }

        snapshot = new ReplayPositionSnapshot(
            pose.SeenAtUtc,
            CalculatePullElapsed(pose.SeenAtUtc),
            actorKey,
            string.IsNullOrWhiteSpace(actorName) ? GetEntityDisplayName(pose.EntityId) : actorName,
            pose.ActorKind,
            partyIndex,
            pose.EntityId,
            classJobId,
            classJobName,
            pose.Position.X,
            pose.Position.Y,
            pose.Position.Z,
            pose.Rotation,
            pose.CurrentHp,
            pose.ShieldHp,
            pose.MaxHp,
            pose.IsDead,
            pose.IsTargetable)
        {
            SampleSource = pose.SampleSource,
        };
        return true;
    }

    private static bool ReplayActiveMechanicMatchesResolveAction(ActiveReplayMechanic active, RawActionEffectPacket packet)
    {
        if (active.ResolveActionId == 0 ||
            active.ResolveActionId != packet.ActionId)
        {
            return false;
        }

        return active.SourceEntityId == 0 ||
            packet.CasterEntityId == 0 ||
            active.SourceEntityId == packet.CasterEntityId;
    }

    private void UpdateActiveReplayMechanicLifetimes(
        DateTime seenAtUtc,
        IReadOnlySet<uint> seenEntityIds,
        IReadOnlyDictionary<uint, uint> castingActionByEntityId)
    {
        if (activeReplayMechanicsByKey.Count == 0)
        {
            return;
        }

        foreach (var active in activeReplayMechanicsByKey.Values.ToList())
        {
            if (seenAtUtc >= active.FallbackEndAtUtc)
            {
                ClampRecentReplayMechanicEnd(active.SourceKey, active.FallbackEndAtUtc);
                activeReplayMechanicsByKey.Remove(active.ActiveKey);
                continue;
            }

            if (active.EndsWhenSourceMissing &&
                !seenEntityIds.Contains(active.SourceEntityId))
            {
                ClampRecentReplayMechanicEnd(active.SourceKey, seenAtUtc);
                activeReplayMechanicsByKey.Remove(active.ActiveKey);
                continue;
            }

            if (active.EndsWhenSourceStopsCasting &&
                (!castingActionByEntityId.TryGetValue(active.SourceEntityId, out var castActionId) ||
                    castActionId != active.CastActionId))
            {
                ClampRecentReplayMechanicEnd(active.SourceKey, seenAtUtc);
                activeReplayMechanicsByKey.Remove(active.ActiveKey);
            }
        }
    }

    private static string BuildActiveReplayMechanicKey(string rawEventKind, uint sourceEntityId, uint castActionId, string variant)
    {
        return $"{rawEventKind}:{sourceEntityId:X8}:{castActionId}:{variant}";
    }

    private static string BuildDmuP5ArenaHoleSourceKey(uint mapEffectIndex)
    {
        return $"dmu-p5-arena-hole:{mapEffectIndex:X2}";
    }

    private static float GetRemainingReplayCastSeconds(Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara)
    {
        var total = MathF.Max(0.0f, battleChara.TotalCastTime);
        var current = Math.Clamp(battleChara.CurrentCastTime, 0.0f, MathF.Max(total, battleChara.CurrentCastTime));
        return Math.Max(0.25f, total - current);
    }

    private static DateTime GetReplayCastStartedAtUtc(DateTime seenAtUtc, Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara)
    {
        return seenAtUtc.AddSeconds(-MathF.Max(0.0f, battleChara.CurrentCastTime));
    }

    private static Vector2 ReplayDirectionFromRotation(float rotation)
    {
        return new Vector2(MathF.Sin(rotation), MathF.Cos(rotation));
    }

    private static float ReplayRotationFromDirection(float x, float z)
    {
        return MathF.Atan2(x, z);
    }

    private static Vector2 RotateReplayVectorLeft(Vector2 vector)
    {
        return new Vector2(vector.Y, -vector.X);
    }

    private static Vector2 RotateReplayVectorRight(Vector2 vector)
    {
        return new Vector2(-vector.Y, vector.X);
    }

    private static Vector3 OffsetReplayPosition(Vector3 position, Vector2 offset)
    {
        return new Vector3(position.X + offset.X, position.Y, position.Z + offset.Y);
    }

    private void EndReplayDmuP2PathOfLightTower(RawActionEffectPacket packet)
    {
        if (!TryFindActiveDmuP2PathOfLightTower(packet, out var tower))
        {
            return;
        }

        CaptureReplayDmuP2PathOfLightActivationEvidence(packet, tower);
        ClampRecentReplayMechanicEnd(tower.SourceKey, packet.SeenAtUtc);
        activeDmuP2PathOfLightTowersByIndex.Remove(tower.Index);
    }

    private void CaptureReplayAnalyzerEvent(RawActionEffectPacket packet)
    {
        if (!IsDmuReplayCaptureContext() || !IsWtfDigAnalyzerSignal(packet.ActionId))
        {
            return;
        }

        var hasSourcePosition = TryGetReplayActionSourcePose(
            packet,
            out var position,
            out var rotation,
            out var sourceName);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = string.IsNullOrWhiteSpace(packet.CasterName)
                ? GetEntityDisplayName(packet.CasterEntityId)
                : packet.CasterName;
        }

        var snapshot = new ReplayAnalyzerEventSnapshot(
            packet.SeenAtUtc,
            CalculatePullElapsed(packet.SeenAtUtc),
            "cast",
            packet.CasterEntityId,
            sourceName,
            packet.ActionId,
            GetActionName(packet.ActionId),
            hasSourcePosition,
            position.X,
            position.Y,
            position.Z,
            rotation);
        var duplicate = recentReplayAnalyzerEvents.LastOrDefault(entry =>
            entry.SourceEntityId == snapshot.SourceEntityId &&
            entry.AbilityId == snapshot.AbilityId &&
            Math.Abs((entry.SeenAtUtc - snapshot.SeenAtUtc).TotalMilliseconds) <= 100);
        if (duplicate is null)
        {
            recentReplayAnalyzerEvents.Add(snapshot);
        }
    }

    private static bool IsWtfDigAnalyzerSignal(uint actionId) => actionId is
        47764 or // Mystery Magic
        47780 or // Mana Charge
        47781 or // Mana Release
        47801 or // Tele-Trouncing
        47804 or // Forsaken
        47843 or // Ultima Blaster
        47867 or // Black Hole
        49884 or // Kefka Says
        50067;   // Flood of Naught

    private void CaptureReplayDmuP2PathOfLightActivationEvidence(
        RawActionEffectPacket packet,
        ActiveDmuP2PathOfLightTower tower)
    {
        CaptureReplayDmuP2ForsakenTargetEvidence(
            packet,
            ReplayEncounterModules.DmuP2PathOfLightActivationRawEventKind,
            "Path of Light activation",
            tower.Index);
    }

    private void CaptureReplayDmuP2ForsakenTargetEvidence(
        RawActionEffectPacket packet,
        string rawEventKind = ReplayEncounterModules.DmuP2ForsakenTargetRawEventKind,
        string? label = null,
        uint? towerIndex = null)
    {
        var sourceName = string.IsNullOrWhiteSpace(packet.CasterName)
            ? GetEntityDisplayName(packet.CasterEntityId)
            : packet.CasterName;
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in packet.Targets)
        {
            var member = FindCurrentMemberByTargetId(target.TargetId);
            if (member is null || !seenTargets.Add(member.MemberKey))
            {
                continue;
            }

            var targetPose = packet.ReplayPoses.FirstOrDefault(pose =>
                pose.TargetIndex == target.TargetIndex &&
                pose.SampleSource == ReplayPositionSampleSource.ActionEffectTarget &&
                pose.ActorKind == ReplayActorKind.Player &&
                IsUsableReplayPosition(pose.Position));
            var position = targetPose?.Position ?? member.Position;
            var rotation = targetPose?.Rotation ?? member.Rotation;
            var amount = target.Effects
                .Where(effect => GetEventKind((ActionEffectKind)effect.Type) == DeathEventKind.Damage)
                .Aggregate(0UL, (total, effect) => total + CalculateRawActionEffectAmount(effect));
            var sourceKey = towerIndex is { } index
                ? $"{rawEventKind}:{index}:{packet.CasterEntityId:X8}:{packet.Sequence}:{member.MemberKey}"
                : $"{rawEventKind}:{packet.CasterEntityId:X8}:{packet.Sequence}:{member.MemberKey}";

            AddRecentReplayMechanicSnapshot(new ReplayMechanicSnapshot(
                packet.SeenAtUtc,
                CalculatePullElapsed(packet.SeenAtUtc),
                0.05f,
                sourceKey,
                $"{sourceName} -> {member.MemberName}",
                ReplayMechanicShape.Label,
                position.X,
                position.Y,
                position.Z,
                rotation,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                label ?? $"{GetActionName(packet.ActionId)} target",
                rawEventKind,
                packet.ActionId,
                (uint)Math.Min(uint.MaxValue, amount),
                true));
        }
    }

    private void CaptureReplayDmuP2ForsakenCloneDrops(RawActionEffectPacket packet, string label)
    {
        var sourceName = string.IsNullOrWhiteSpace(packet.CasterName)
            ? GetEntityDisplayName(packet.CasterEntityId)
            : packet.CasterName;
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in packet.Targets)
        {
            var member = FindCurrentMemberByTargetId(target.TargetId);
            if (member is null || !seenTargets.Add(member.MemberKey))
            {
                continue;
            }

            var targetPose = packet.ReplayPoses.FirstOrDefault(pose =>
                pose.TargetIndex == target.TargetIndex &&
                pose.SampleSource == ReplayPositionSampleSource.ActionEffectTarget &&
                pose.ActorKind == ReplayActorKind.Player &&
                IsUsableReplayPosition(pose.Position));
            var position = targetPose?.Position ?? member.Position;
            var rotation = targetPose?.Rotation ?? member.Rotation;
            var amount = target.Effects
                .Where(effect => GetEventKind((ActionEffectKind)effect.Type) == DeathEventKind.Damage)
                .Aggregate(0UL, (total, effect) => total + CalculateRawActionEffectAmount(effect));

            AddRecentReplayMechanicSnapshot(new ReplayMechanicSnapshot(
                packet.SeenAtUtc,
                CalculatePullElapsed(packet.SeenAtUtc),
                2.4f,
                $"{ReplayEncounterModules.DmuP2ForsakenCloneDropRawEventKind}:{packet.CasterEntityId:X8}:{packet.Sequence}:{member.MemberKey}",
                $"{sourceName} -> {member.MemberName}",
                ReplayMechanicShape.Spread,
                position.X,
                position.Y,
                position.Z,
                rotation,
                5.0f,
                0.0f,
                0.0f,
                0.0f,
                label,
                ReplayEncounterModules.DmuP2ForsakenCloneDropRawEventKind,
                packet.ActionId,
                (uint)Math.Min(uint.MaxValue, amount),
                true));
        }
    }

    private bool TryFindActiveDmuP2PathOfLightTower(
        RawActionEffectPacket packet,
        out ActiveDmuP2PathOfLightTower tower)
    {
        PruneActiveDmuP2PathOfLightTowers(packet.SeenAtUtc);
        if (activeDmuP2PathOfLightTowersByIndex.Count == 0)
        {
            tower = default!;
            return false;
        }

        var resolveCandidates = activeDmuP2PathOfLightTowersByIndex.Values
            .Where(candidate => (packet.SeenAtUtc - candidate.SeenAtUtc).TotalSeconds >= DmuP2PathOfLightTowerMinResolveMatchSeconds)
            .ToList();
        if (resolveCandidates.Count == 0)
        {
            tower = default!;
            return false;
        }

        if (TryGetReplayPathOfLightResolvePosition(packet, out var resolvePosition))
        {
            var nearest = resolveCandidates
                .OrderBy(candidate => DistanceXZ(candidate.Position, resolvePosition))
                .First();
            if (DistanceXZ(nearest.Position, resolvePosition) <= DmuP2PathOfLightTowerResolveMatchDistance)
            {
                tower = nearest;
                return true;
            }
        }

        if (resolveCandidates.Count == 1)
        {
            tower = resolveCandidates[0];
            return true;
        }

        tower = default!;
        return false;
    }

    private void PruneActiveDmuP2PathOfLightTowers(DateTime now)
    {
        var staleIndexes = activeDmuP2PathOfLightTowersByIndex
            .Where(entry => (now - entry.Value.SeenAtUtc).TotalSeconds > DmuP2PathOfLightTowerMaxMatchSeconds)
            .Select(entry => entry.Key)
            .ToList();
        foreach (var index in staleIndexes)
        {
            activeDmuP2PathOfLightTowersByIndex.Remove(index);
        }
    }

    private bool TryGetReplayPathOfLightResolvePosition(RawActionEffectPacket packet, out Vector3 position)
    {
        if (packet.HasCasterPose && IsUsableReplayPosition(packet.CasterPosition))
        {
            position = packet.CasterPosition;
            return true;
        }

        if (packet.HasTargetPosition && IsUsableReplayPosition(packet.TargetPosition))
        {
            position = packet.TargetPosition;
            return true;
        }

        foreach (var target in packet.Targets)
        {
            var member = FindCurrentMemberByTargetId(target.TargetId);
            if (member is null)
            {
                continue;
            }

            position = member.Position;
            return true;
        }

        position = default;
        return false;
    }

    private void ClampRecentReplayMechanicEnd(string sourceKey, DateTime endAtUtc)
    {
        if (!recentReplayMechanicsBySource.TryGetValue(sourceKey, out var history) ||
            history.Count == 0)
        {
            return;
        }

        var last = history[^1];
        if (endAtUtc < last.SeenAtUtc)
        {
            return;
        }

        var durationSeconds = Math.Max(0.05f, (float)(endAtUtc - last.SeenAtUtc).TotalSeconds);
        if (durationSeconds < last.DurationSeconds)
        {
            history[^1] = last with
            {
                DurationSeconds = durationSeconds,
            };
        }
    }

    private void ExtendRecentReplayMechanicEnd(string sourceKey, DateTime endAtUtc)
    {
        if (!recentReplayMechanicsBySource.TryGetValue(sourceKey, out var history) ||
            history.Count == 0)
        {
            return;
        }

        var last = history[^1];
        if (endAtUtc <= last.SeenAtUtc)
        {
            return;
        }

        var durationSeconds = Math.Max(0.05f, (float)(endAtUtc - last.SeenAtUtc).TotalSeconds);
        if (durationSeconds > last.DurationSeconds)
        {
            history[^1] = last with
            {
                DurationSeconds = durationSeconds,
            };
        }
    }

    private static bool IsDmuCasterPoseReplayAction(uint actionId)
    {
        return actionId is DmuP1RevoltingRuinFirstActionId or
            DmuP1RevoltingRuinSecondActionId or
            DmuP1BlizzardFirstActionId or
            DmuP1BlizzardSecondActionId or
            DmuP1ThunderFirstActionId or
            DmuP1ThunderSecondActionId or
            DmuP1WaveCannonActionId or
            DmuP1ExplosionActionId or
            DmuP1GravityActionId or
            DmuP1GravitationalWaveActionId or
            DmuP1IntemperateWillActionId or
            DmuP2PathOfLightActionId or
            DmuP2SpelldriverActionId or
            DmuP2SpellwaveActionId or
            DmuP2AllThingsEndingFirstActionId or
            DmuP2AllThingsEndingSecondActionId or
            DmuP2WingsLeftActionId or
            DmuP2WingsRightActionId or
            DmuBlackHoleNothingnessActionId or
            DmuP3AeroIIIAssaultActionId or
            DmuP3ThunderIIICircleActionId or
            DmuP3LatLongShockwaveActionId or
            DmuP3UltimaBlasterChargeActionId or
            DmuP3SlapHappyBigActionId or
            DmuP3SlapHappySmallActionId or
            DmuP3SlapHappyShockingImpactActionId or
            DmuP3SlapHappyShockwaveActionId or
            DmuP3DamningEdictActionId or
            DmuP3LookUponMeAndDespairActionId or
            DmuP3BlizzardIIIActionId or
            DmuP3StompAMoleActionId or
            DmuP3BigBangActionId or
            DmuP4GrandCrossActionId or
            DmuP4InfernoHitActionId or
            DmuP4TsunamiHitActionId or
            DmuP4StrayFlamesNormalActionId or
            DmuP4StrayFlamesInvertedActionId or
            DmuP4StraySprayNormalActionId or
            DmuP4StraySprayInvertedActionId or
            DmuP4WhiteAntilightActionId or
            DmuP4BlackAntilightActionId or
            DmuP4EdgeOfDeathActionId or
            DmuP4UltimaUpsurgeActionId or
            DmuP5UltimaRepeaterHitActionId or
            DmuP5FloodLineActionId or
            DmuP5TriadFireActionId or
            DmuP5TriadBlizzardActionId or
            DmuP5TriadThunderActionId or
            DmuP5QuakeActionId or
            DmuP5TornadoActionId or
            DmuP5StrayApocalypseFirstActionId or
            DmuP5StrayApocalypseRestActionId or
            DmuP5ForsakenGroundActionId or
            DmuP5ForsakenPuddleActionId;
    }

    private bool TryGetReplayPacketMechanicCenter(RawActionEffectPacket packet, out Vector3 center)
    {
        if (packet.HasTargetPosition && IsUsableReplayPosition(packet.TargetPosition))
        {
            center = packet.TargetPosition;
            return true;
        }

        foreach (var target in packet.Targets)
        {
            var member = FindCurrentMemberByTargetId(target.TargetId);
            if (member is null)
            {
                continue;
            }

            center = member.Position;
            return true;
        }

        center = default;
        return false;
    }

    private bool TryGetReplayObjectPose(uint entityId, out Vector3 position, out float rotation, out string name)
    {
        position = default;
        rotation = 0.0f;
        name = string.Empty;

        entityId = NormalizeActorEntityId(entityId);
        if (entityId == 0)
        {
            return false;
        }

        try
        {
            var gameObject = ObjectTable.SearchByEntityId(entityId);
            if (gameObject is null ||
                !IsUsableReplayPosition(gameObject.Position))
            {
                return false;
            }

            position = gameObject.Position;
            rotation = gameObject.Rotation;
            name = string.IsNullOrWhiteSpace(gameObject.Name.TextValue)
                ? $"Entity {entityId:X8}"
                : gameObject.Name.TextValue;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture Better Deaths replay pose for {EntityId:X8}.", entityId);
            return false;
        }
    }

    private static bool IsUsableReplayPosition(Vector3 position)
    {
        return float.IsFinite(position.X) &&
            float.IsFinite(position.Y) &&
            float.IsFinite(position.Z) &&
            (MathF.Abs(position.X) > 0.001f || MathF.Abs(position.Z) > 0.001f);
    }

    private static float DistanceXZ(Vector3 left, Vector3 right)
    {
        var dx = left.X - right.X;
        var dz = left.Z - right.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    private void TrackRecentReplayPositions(IReadOnlyList<PartyMemberSnapshot> members, DateTime now)
    {
        TrackRecentReplayDebuffs(members, now);
        TrackRecentReplayWorldMarkers(now);
        ExtendReplayDmuP5ArenaHoles(now);

        if (Duration(now, lastReplayPlayerPositionSampleAtUtc) >= ReplayPlayerPositionSampleInterval)
        {
            lastReplayPlayerPositionSampleAtUtc = now;
            foreach (var member in members)
            {
                AddRecentReplayPositionSnapshot(CreatePlayerReplayPositionSnapshot(member, now));
            }
        }

        var objectSampleInterval = GetReplayObjectPositionSampleInterval(now);
        if (Duration(now, lastReplayObjectPositionSampleAtUtc) < objectSampleInterval)
        {
            return;
        }

        lastReplayObjectPositionSampleAtUtc = now;
        var (enemySnapshots, mechanicSnapshots) = CaptureReplayObjectSnapshots(now, objectSampleInterval);
        foreach (var enemy in enemySnapshots)
        {
            AddRecentReplayPositionSnapshot(enemy);
        }

        foreach (var mechanic in mechanicSnapshots)
        {
            AddRecentReplayMechanicSnapshot(mechanic);
        }
    }

    private void ExtendReplayDmuP5ArenaHoles(DateTime seenAtUtc)
    {
        if (!IsDmuReplayCaptureContext() || activeDmuP5ArenaHoleIndices.Count == 0)
        {
            return;
        }

        var endAtUtc = seenAtUtc.AddSeconds(DmuP5ArenaHoleSampleHoldSeconds);
        foreach (var mapEffectIndex in activeDmuP5ArenaHoleIndices)
        {
            ExtendRecentReplayMechanicEnd(BuildDmuP5ArenaHoleSourceKey(mapEffectIndex), endAtUtc);
        }
    }

    private void TrackRecentReplayWorldMarkers(DateTime now)
    {
        if (replayWorldMarkersCapturedForPull)
        {
            return;
        }

        if (Duration(now, lastReplayWorldMarkerSampleAtUtc) < ReplayWorldMarkerSampleInterval)
        {
            return;
        }

        lastReplayWorldMarkerSampleAtUtc = now;
        replayWorldMarkersCapturedForPull = TryCaptureInitialReplayWorldMarkers(now);
    }

    private unsafe bool TryCaptureInitialReplayWorldMarkers(DateTime now)
    {
        var controller = MarkingController.Instance();
        if (controller == null)
        {
            return false;
        }

        var markers = controller->FieldMarkers;
        var markerCount = Math.Min(ReplayWorldMarkerCount, markers.Length);
        if (markerCount == 0)
        {
            return false;
        }

        for (var markerIndex = 0; markerIndex < markerCount; markerIndex++)
        {
            var marker = markers[markerIndex];
            if (!marker.Active)
            {
                continue;
            }

            var seenAtUtc = pullStartedAtUtc is { } pullStarted
                ? pullStarted
                : now;
            recentReplayWorldMarkers.Add(new ReplayWorldMarkerSnapshot(
                seenAtUtc,
                CalculatePullElapsed(seenAtUtc),
                markerIndex,
                GetReplayWorldMarkerLabel(markerIndex),
                true,
                marker.X / 1000.0f,
                marker.Y / 1000.0f,
                marker.Z / 1000.0f));
        }

        return true;
    }

    private static string GetReplayWorldMarkerLabel(int markerIndex)
    {
        return markerIndex switch
        {
            0 => "A",
            1 => "B",
            2 => "C",
            3 => "D",
            4 => "1",
            5 => "2",
            6 => "3",
            7 => "4",
            _ => "?",
        };
    }

    private TimeSpan GetReplayObjectPositionSampleInterval(DateTime now)
    {
        return ShouldUseFastReplayPositionSampling(now)
            ? ReplayTetherPositionSampleInterval
            : ReplayPositionSampleInterval;
    }

    private bool ShouldUseFastReplayPositionSampling(DateTime now)
    {
        return HasActiveReplayTether(now) ||
            HasLiveReplayTether();
    }

    private bool HasActiveReplayTether(DateTime now)
    {
        foreach (var history in recentReplayMechanicsBySource.Values)
        {
            if (history.Count == 0)
            {
                continue;
            }

            var latest = history[^1];
            if (latest.Shape != ReplayMechanicShape.Tether)
            {
                continue;
            }

            var activeUntil = latest.SeenAtUtc
                .AddSeconds(Math.Max(0.05f, latest.DurationSeconds))
                .Add(ReplayTetherActiveGrace);
            if (now <= activeUntil)
            {
                return true;
            }
        }

        return false;
    }

    private unsafe bool HasLiveReplayTether()
    {
        if (currentMembers.Count == 0)
        {
            return false;
        }

        foreach (var member in currentMembers)
        {
            if (member.EntityId == 0)
            {
                continue;
            }

            try
            {
                if (ObjectTable.SearchByEntityId(member.EntityId) is not Dalamud.Game.ClientState.Objects.Types.ICharacter character ||
                    character.Address == nint.Zero)
                {
                    continue;
                }

                var characterStruct = (Character*)character.Address;
                var tethers = characterStruct->Vfx.Tethers;
                for (var index = 0; index < tethers.Length; index++)
                {
                    var tether = tethers[index];
                    if (tether.Id != 0 &&
                        tether.TargetId.ObjectId != 0)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // This path is checked every frame before replay sampling. Avoid log spam if the
                // object table changes while the client is updating actors.
            }
        }

        return false;
    }

    private ReplayPositionSnapshot CreatePlayerReplayPositionSnapshot(PartyMemberSnapshot member, DateTime seenAtUtc)
    {
        return new ReplayPositionSnapshot(
            seenAtUtc,
            CalculatePullElapsed(seenAtUtc),
            $"player:{member.MemberKey}",
            member.MemberName,
            ReplayActorKind.Player,
            member.PartyIndex,
            member.EntityId,
            member.ClassJobId,
            member.ClassJobName,
            member.Position.X,
            member.Position.Y,
            member.Position.Z,
            member.Rotation,
            member.CurrentHp,
            member.ShieldHp,
            member.MaxHp,
            member.IsDead,
            true)
        {
            SampleSource = ReplayPositionSampleSource.PeriodicPlayer,
        };
    }

    private (IReadOnlyList<ReplayPositionSnapshot> EnemySnapshots, IReadOnlyList<ReplayMechanicSnapshot> MechanicSnapshots) CaptureReplayObjectSnapshots(DateTime seenAtUtc, TimeSpan sampleInterval)
    {
        var enemySnapshots = new List<ReplayPositionSnapshot>();
        var mechanicSnapshots = new List<ReplayMechanicSnapshot>();
        var seenEntityIds = new HashSet<uint>();
        var castingActionByEntityId = new Dictionary<uint, uint>();
        foreach (var gameObject in ObjectTable)
        {
            if (gameObject is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc ||
                battleNpc.EntityId == 0 ||
                battleNpc.EntityId == InvalidActorEntityId ||
                !seenEntityIds.Add(battleNpc.EntityId))
            {
                continue;
            }

            if (battleNpc is Dalamud.Game.ClientState.Objects.Types.IBattleChara battleChara &&
                battleChara.IsCasting &&
                battleChara.CastActionId != 0)
            {
                castingActionByEntityId[battleNpc.EntityId] = battleChara.CastActionId;
            }

            var name = battleNpc.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            CaptureReplayDmuP4RealityTellMarker(battleNpc, name, seenAtUtc);
            CaptureReplayDmuP1P2CastPrediction(battleNpc, name, seenAtUtc, mechanicSnapshots);
            CaptureReplayDmuP3CastPrediction(battleNpc, name, seenAtUtc, mechanicSnapshots);
            CaptureReplayDmuP4P5CastPrediction(battleNpc, name, seenAtUtc, mechanicSnapshots);
            CaptureReplayCatalogCastPrediction(battleNpc, name, seenAtUtc, mechanicSnapshots);

            if (string.Equals(name, "Black Hole", StringComparison.OrdinalIgnoreCase))
            {
                mechanicSnapshots.Add(new ReplayMechanicSnapshot(
                    seenAtUtc,
                    CalculatePullElapsed(seenAtUtc),
                    (float)ReplayPositionSampleInterval.TotalSeconds * 1.5f,
                    $"object:{battleNpc.EntityId:X8}",
                    name,
                    ReplayMechanicShape.Circle,
                    battleNpc.Position.X,
                    battleNpc.Position.Y,
                    battleNpc.Position.Z,
                    battleNpc.Rotation,
                    2.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    name,
                    "object",
                    battleNpc.BaseId,
                    battleNpc.EntityId,
                    true));
                CaptureReplayBlackHoleTethers(battleNpc, seenAtUtc, sampleInterval, mechanicSnapshots);
            }

            if (battleNpc.BattleNpcKind != Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Combatant ||
                battleNpc.MaxHp == 0)
            {
                continue;
            }

            enemySnapshots.Add(new ReplayPositionSnapshot(
                seenAtUtc,
                CalculatePullElapsed(seenAtUtc),
                $"enemy:{battleNpc.EntityId:X8}",
                name,
                ReplayActorKind.Enemy,
                2000 + battleNpc.ObjectIndex,
                battleNpc.EntityId,
                0,
                string.Empty,
                battleNpc.Position.X,
                battleNpc.Position.Y,
                battleNpc.Position.Z,
                battleNpc.Rotation,
                battleNpc.CurrentHp,
                CalculateShieldHp(battleNpc, battleNpc.MaxHp),
                battleNpc.MaxHp,
                battleNpc.IsDead || battleNpc.CurrentHp == 0,
                battleNpc.IsTargetable)
            {
                SampleSource = ReplayPositionSampleSource.PeriodicEnemyObject,
            });
        }

        UpdateActiveReplayMechanicLifetimes(seenAtUtc, seenEntityIds, castingActionByEntityId);
        CaptureReplayTethersFromPlayers(seenAtUtc, sampleInterval, mechanicSnapshots);

        return (
            enemySnapshots
                .OrderByDescending(snapshot => snapshot.IsTargetable)
                .ThenByDescending(snapshot => snapshot.MaxHp)
                .ThenBy(snapshot => snapshot.ActorName, StringComparer.OrdinalIgnoreCase)
                .Take(MaxReplayEnemyActors)
                .ToList(),
            mechanicSnapshots);
    }

    private void CaptureReplayDmuP4RealityTellMarker(
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        string name,
        DateTime seenAtUtc)
    {
        if (!IsDmuReplayCaptureContext() ||
            !IsDmuP4RealityTellBoss(name))
        {
            return;
        }

        try
        {
            foreach (var status in battleNpc.StatusList)
            {
                if (status.StatusId != DmuP4RealityTellStatusId)
                {
                    continue;
                }

                AddRecentReplayMarkerSnapshot(new ReplayMarkerSnapshot(
                    seenAtUtc,
                    CalculatePullElapsed(seenAtUtc),
                    $"enemy:{battleNpc.EntityId:X8}",
                    name,
                    ReplayActorKind.Enemy,
                    2000 + battleNpc.ObjectIndex,
                    battleNpc.EntityId,
                    0,
                    string.Empty,
                    DmuP4RealityTellStatusId,
                    status.Param),
                    TimeSpan.FromSeconds(10));
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture Better Deaths DMU P4 reality tell for {EntityId:X8}.", battleNpc.EntityId);
        }
    }

    private unsafe void CaptureReplayBlackHoleTethers(
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc,
        DateTime seenAtUtc,
        TimeSpan sampleInterval,
        List<ReplayMechanicSnapshot> mechanicSnapshots)
    {
        if (!IsDmuReplayCaptureContext() ||
            battleNpc is not Dalamud.Game.ClientState.Objects.Types.ICharacter character ||
            character.Address == nint.Zero)
        {
            return;
        }

        try
        {
            var characterStruct = (Character*)character.Address;
            var tethers = characterStruct->Vfx.Tethers;
            for (var index = 0; index < tethers.Length; index++)
            {
                var tether = tethers[index];
                if (tether.Id != DmuBlackHoleTetherId ||
                    tether.TargetId.ObjectId == 0)
                {
                    continue;
                }

                var targetMember = FindCurrentMemberByEntityId(tether.TargetId.ObjectId);
                if (targetMember is null)
                {
                    continue;
                }

                AddReplayTetherSnapshot(
                    seenAtUtc,
                    battleNpc.EntityId,
                    battleNpc.Name.TextValue,
                    battleNpc.Position,
                    targetMember,
                    "black-hole-tether",
                    DmuBlackHoleTetherId,
                    "Tether",
                    true,
                    sampleInterval,
                    mechanicSnapshots);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture Better Deaths Black Hole replay tether for {EntityId:X8}.", battleNpc.EntityId);
        }
    }

    private unsafe void CaptureReplayTethersFromPlayers(
        DateTime seenAtUtc,
        TimeSpan sampleInterval,
        List<ReplayMechanicSnapshot> mechanicSnapshots)
    {
        var isDmu = IsDmuReplayCaptureContext();
        foreach (var member in currentMembers)
        {
            if (member.EntityId == 0 ||
                ObjectTable.SearchByEntityId(member.EntityId) is not Dalamud.Game.ClientState.Objects.Types.ICharacter character ||
                character.Address == nint.Zero)
            {
                continue;
            }

            try
            {
                var characterStruct = (Character*)character.Address;
                var tethers = characterStruct->Vfx.Tethers;
                for (var index = 0; index < tethers.Length; index++)
                {
                    var tether = tethers[index];
                    if (tether.TargetId.ObjectId == 0 ||
                        ObjectTable.SearchByEntityId(tether.TargetId.ObjectId) is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc tetherSource)
                    {
                        continue;
                    }

                    if (isDmu &&
                        tether.Id == DmuBlackHoleTetherId &&
                        IsReplayBlackHoleObject(tetherSource.EntityId, tetherSource.Name.TextValue))
                    {
                        AddReplayTetherSnapshot(
                            seenAtUtc,
                            tetherSource.EntityId,
                            tetherSource.Name.TextValue,
                            tetherSource.Position,
                            member,
                            "black-hole-tether",
                            DmuBlackHoleTetherId,
                            "Tether",
                            true,
                            sampleInterval,
                            mechanicSnapshots);
                        continue;
                    }

                    if (isDmu &&
                        tether.Id == DmuGravenImageTetherId &&
                        IsReplayGravenImageObject(tetherSource.EntityId, tetherSource.Name.TextValue))
                    {
                        AddReplayTetherSnapshot(
                            seenAtUtc,
                            tetherSource.EntityId,
                            tetherSource.Name.TextValue,
                            tetherSource.Position,
                            member,
                            "graven-image-tether",
                            DmuGravenImageTetherId,
                            "Tether",
                            true,
                            sampleInterval,
                            mechanicSnapshots);
                        continue;
                    }

                    var territoryId = currentPullTerritoryId == 0
                        ? currentTerritoryId
                        : currentPullTerritoryId;
                    var catalogEntries = BossModUltimateCatalog.FindIdentifiers(
                        territoryId,
                        ReplayCatalogIdentifierKind.Tether,
                        tether.Id);
                    var catalogEntry = catalogEntries.FirstOrDefault();
                    var isKnown = catalogEntries.Count > 0;
                    AddReplayTetherSnapshot(
                        seenAtUtc,
                        tetherSource.EntityId,
                        tetherSource.Name.TextValue,
                        tetherSource.Position,
                        member,
                        isKnown ? "bossmod-tether" : "generic-tether",
                        tether.Id,
                        isKnown ? ReplayMechanicCatalog.HumanizeIdentifier(catalogEntry.Name) : $"Tether #{tether.Id}",
                        isKnown,
                        sampleInterval,
                        mechanicSnapshots);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not capture Better Deaths player-side replay tether for {EntityId:X8}.", member.EntityId);
            }
        }
    }

    private void AddReplayTetherSnapshot(
        DateTime seenAtUtc,
        uint sourceEntityId,
        string sourceName,
        Vector3 sourcePosition,
        PartyMemberSnapshot targetMember,
        string rawEventKind,
        uint rawEventId,
        string label,
        bool isKnown,
        TimeSpan sampleInterval,
        List<ReplayMechanicSnapshot> mechanicSnapshots)
    {
        var source = sourcePosition;
        var target = targetMember.Position;
        if (!float.IsFinite(source.X) ||
            !float.IsFinite(source.Z) ||
            !float.IsFinite(target.X) ||
            !float.IsFinite(target.Z))
        {
            return;
        }

        var dx = target.X - source.X;
        var dz = target.Z - source.Z;
        var distance = MathF.Sqrt((dx * dx) + (dz * dz));
        if (distance <= 0.05f)
        {
            return;
        }

        var capturedSourceName = string.IsNullOrWhiteSpace(sourceName)
            ? "Tether source"
            : sourceName;
        mechanicSnapshots.Add(new ReplayMechanicSnapshot(
            seenAtUtc,
            CalculatePullElapsed(seenAtUtc),
            (float)sampleInterval.TotalSeconds * 1.5f,
            $"{rawEventKind}:{sourceEntityId:X8}:{targetMember.EntityId:X8}",
            $"{capturedSourceName} -> {targetMember.MemberName}",
            ReplayMechanicShape.Tether,
            source.X + (dx * 0.5f),
            (source.Y + target.Y) * 0.5f,
            source.Z + (dz * 0.5f),
            ReplayRotationFromDirection(dx, dz),
            0.0f,
            distance,
            0.35f,
            0.0f,
            label,
            rawEventKind,
            rawEventId,
            targetMember.EntityId,
            isKnown));
    }

    private bool IsDmuReplayCaptureContext()
    {
        var territoryId = currentPullTerritoryId == 0
            ? currentTerritoryId
            : currentPullTerritoryId;
        return ReplayEncounterModules.IsDancingMadUltimate(territoryId);
    }

    private static bool IsDmuP4RealityTellBoss(string name)
    {
        return string.Equals(name, "Neo Exdeath", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Exdeath", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Chaos", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsReplayBlackHoleObject(uint entityId, string capturedName)
    {
        if (string.Equals(capturedName, "Black Hole", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entityId == 0)
        {
            return false;
        }

        try
        {
            return ObjectTable.SearchByEntityId(entityId) is Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc &&
                string.Equals(battleNpc.Name.TextValue, "Black Hole", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not inspect Better Deaths replay object {EntityId:X8}.", entityId);
            return false;
        }
    }

    private bool IsReplayGravenImageObject(uint entityId, string capturedName)
    {
        if (string.Equals(capturedName, "Graven Image", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entityId == 0)
        {
            return false;
        }

        try
        {
            return ObjectTable.SearchByEntityId(entityId) is Dalamud.Game.ClientState.Objects.Types.IBattleNpc battleNpc &&
                (battleNpc.BaseId == DmuGravenImageBaseId ||
                    string.Equals(battleNpc.Name.TextValue, "Graven Image", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not inspect Better Deaths Graven Image replay object {EntityId:X8}.", entityId);
            return false;
        }
    }

    private void AddRecentReplayPositionSnapshot(ReplayPositionSnapshot snapshot)
    {
        if (!recentReplayPositionsByActor.TryGetValue(snapshot.ActorKey, out var history))
        {
            history = [];
            recentReplayPositionsByActor[snapshot.ActorKey] = history;
        }

        for (var index = history.Count - 1; index >= 0 && index >= history.Count - 8; index--)
        {
            var existing = history[index];
            if (!ReplayPositionSnapshotsDuplicate(existing, snapshot, GetReplayPositionDuplicateWindow(snapshot)))
            {
                continue;
            }

            if (ReplayPositionSampleSourceRank(snapshot.SampleSource) > ReplayPositionSampleSourceRank(existing.SampleSource))
            {
                history[index] = snapshot;
            }

            return;
        }

        history.Add(snapshot);
    }

    private static TimeSpan GetReplayPositionDuplicateWindow(ReplayPositionSnapshot snapshot)
    {
        return snapshot.SampleSource is ReplayPositionSampleSource.PeriodicPlayer or ReplayPositionSampleSource.PeriodicEnemyObject
            ? ReplayStationaryPositionDuplicateWindow
            : ReplayPositionDuplicateWindow;
    }

    private static bool ReplayPositionSnapshotsDuplicate(
        ReplayPositionSnapshot existing,
        ReplayPositionSnapshot snapshot,
        TimeSpan duplicateWindow)
    {
        return Duration(existing.SeenAtUtc, snapshot.SeenAtUtc) <= duplicateWindow &&
            existing.CurrentHp == snapshot.CurrentHp &&
            existing.ShieldHp == snapshot.ShieldHp &&
            existing.MaxHp == snapshot.MaxHp &&
            existing.IsDead == snapshot.IsDead &&
            existing.IsTargetable == snapshot.IsTargetable &&
            Vector3.Distance(
                new Vector3(existing.X, existing.Y, existing.Z),
                new Vector3(snapshot.X, snapshot.Y, snapshot.Z)) <= 0.03f &&
            Math.Abs(existing.Rotation - snapshot.Rotation) <= 0.03f;
    }

    private static int ReplayPositionSampleSourceRank(ReplayPositionSampleSource sampleSource)
    {
        return sampleSource switch
        {
            ReplayPositionSampleSource.ActionEffectSource or ReplayPositionSampleSource.ActionEffectTarget => 3,
            ReplayPositionSampleSource.MarkerMechanic => 2,
            ReplayPositionSampleSource.PeriodicPlayer or ReplayPositionSampleSource.PeriodicEnemyObject => 1,
            _ => 0,
        };
    }

    private void AddRecentReplayMechanicSnapshot(ReplayMechanicSnapshot snapshot)
    {
        replayMechanicCaptureRevision++;
        if (!recentReplayMechanicsBySource.TryGetValue(snapshot.SourceKey, out var history))
        {
            history = [];
            recentReplayMechanicsBySource[snapshot.SourceKey] = history;
        }

        var last = history.Count == 0 ? null : history[^1];
        if (last is not null &&
            IsReplaySingleActiveSourceMechanic(snapshot) &&
            string.Equals(last.RawEventKind, snapshot.RawEventKind, StringComparison.OrdinalIgnoreCase) &&
            snapshot.SeenAtUtc > last.SeenAtUtc)
        {
            history[^1] = last with
            {
                DurationSeconds = Math.Max(0.05f, (float)(snapshot.SeenAtUtc - last.SeenAtUtc).TotalSeconds),
            };
            last = history[^1];
        }

        if (last is not null &&
            last.RawEventId == snapshot.RawEventId &&
            last.RawState == snapshot.RawState &&
            Vector3.Distance(new Vector3(last.X, last.Y, last.Z), new Vector3(snapshot.X, snapshot.Y, snapshot.Z)) <= 0.05f &&
            Duration(last.SeenAtUtc, snapshot.SeenAtUtc) <= ReplayPositionSampleInterval)
        {
            history[^1] = last with
            {
                DurationSeconds = Math.Max(
                    last.DurationSeconds,
                    (float)(snapshot.SeenAtUtc - last.SeenAtUtc).TotalSeconds + snapshot.DurationSeconds),
            };
            return;
        }

        history.Add(snapshot);
        while (history.Count > MaxRecentReplayMechanicsPerSource)
        {
            history.RemoveAt(0);
        }
    }

    private static bool IsReplaySingleActiveSourceMechanic(ReplayMechanicSnapshot snapshot)
    {
        return snapshot.Shape == ReplayMechanicShape.Tether;
    }

    private IReadOnlyList<ReplayPositionSnapshot> GetRecentReplayPositions(DateTime startAtUtc, DateTime endAtUtc)
    {
        if (recentReplayPositionsByActor.Count == 0 || endAtUtc < startAtUtc)
        {
            return [];
        }

        return recentReplayPositionsByActor.Values
            .SelectMany(history => history)
            .Where(snapshot => snapshot.SeenAtUtc >= startAtUtc && snapshot.SeenAtUtc <= endAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.ActorKind)
            .ThenBy(snapshot => snapshot.PartyIndex)
            .ThenBy(snapshot => snapshot.ActorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<ReplayPositionSnapshot> GetCurrentPullReplayPositions(DateTime endAtUtc)
    {
        return GetRecentReplayPositions(GetCurrentPullReplayStartAtUtc(endAtUtc), endAtUtc);
    }

    private IReadOnlyList<ReplayMarkerSnapshot> GetRecentReplayMarkers(DateTime startAtUtc, DateTime endAtUtc)
    {
        if (recentReplayMarkersByActor.Count == 0 || endAtUtc < startAtUtc)
        {
            return [];
        }

        return recentReplayMarkersByActor.Values
            .SelectMany(history => history)
            .Where(snapshot => snapshot.SeenAtUtc >= startAtUtc && snapshot.SeenAtUtc <= endAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.ActorKind)
            .ThenBy(snapshot => snapshot.PartyIndex)
            .ThenBy(snapshot => snapshot.ActorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<ReplayMarkerSnapshot> GetCurrentPullReplayMarkers(DateTime endAtUtc)
    {
        return GetRecentReplayMarkers(GetCurrentPullReplayStartAtUtc(endAtUtc), endAtUtc);
    }

    public IReadOnlyList<ReplayMarkerSnapshot> GetCurrentPullReplayMarkersForReview()
    {
        return GetCurrentPullReplayMarkers(DateTime.UtcNow);
    }

    private IReadOnlyList<ReplayMechanicSnapshot> GetRecentReplayMechanics(DateTime startAtUtc, DateTime endAtUtc)
    {
        if (recentReplayMechanicsBySource.Count == 0 || endAtUtc < startAtUtc)
        {
            return [];
        }

        return recentReplayMechanicsBySource.Values
            .SelectMany(history => history)
            .Where(snapshot => snapshot.SeenAtUtc <= endAtUtc &&
                snapshot.SeenAtUtc.AddSeconds(Math.Max(0.05f, snapshot.DurationSeconds)) >= startAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.SourceKey, StringComparer.Ordinal)
            .ToList();
    }

    private IReadOnlyList<ReplayMechanicSnapshot> GetCurrentPullReplayMechanics(DateTime endAtUtc)
    {
        return GetRecentReplayMechanics(GetCurrentPullReplayStartAtUtc(endAtUtc), endAtUtc);
    }

    private IReadOnlyList<ReplayAnalyzerEventSnapshot> GetCurrentPullReplayAnalyzerEvents(DateTime endAtUtc)
    {
        var startAtUtc = GetCurrentPullReplayStartAtUtc(endAtUtc);
        return recentReplayAnalyzerEvents
            .Where(snapshot => snapshot.SeenAtUtc >= startAtUtc && snapshot.SeenAtUtc <= endAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.AbilityId)
            .ToList();
    }

    private IReadOnlyList<ReplayWorldMarkerSnapshot> GetRecentReplayWorldMarkers(DateTime startAtUtc, DateTime endAtUtc)
    {
        if (recentReplayWorldMarkers.Count == 0 || endAtUtc < startAtUtc)
        {
            return [];
        }

        return recentReplayWorldMarkers
            .Where(snapshot => snapshot.SeenAtUtc >= startAtUtc && snapshot.SeenAtUtc <= endAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.MarkerIndex)
            .ToList();
    }

    private IReadOnlyList<ReplayWorldMarkerSnapshot> GetCurrentPullReplayWorldMarkers(DateTime endAtUtc)
    {
        return GetRecentReplayWorldMarkers(GetCurrentPullReplayStartAtUtc(endAtUtc), endAtUtc);
    }

    private IReadOnlyList<ReplayMitigationSnapshot> GetRecentReplayMitigations(DateTime startAtUtc, DateTime endAtUtc)
    {
        if (recentReplayMitigations.Count == 0 || endAtUtc < startAtUtc)
        {
            return [];
        }

        return recentReplayMitigations
            .Where(snapshot => snapshot.SeenAtUtc <= endAtUtc &&
                snapshot.SeenAtUtc.AddSeconds(Math.Max(0.05f, snapshot.DurationSeconds)) >= startAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.PartyIndex)
            .ThenBy(snapshot => snapshot.MemberName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.ActionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<ReplayMitigationSnapshot> GetCurrentPullReplayMitigations(DateTime endAtUtc)
    {
        return GetRecentReplayMitigations(GetCurrentPullReplayStartAtUtc(endAtUtc), endAtUtc);
    }

    private IReadOnlyList<ReplayDebuffSnapshot> GetRecentReplayDebuffs(DateTime startAtUtc, DateTime endAtUtc)
    {
        if (recentReplayDebuffs.Count == 0 || endAtUtc < startAtUtc)
        {
            return [];
        }

        return recentReplayDebuffs
            .Where(snapshot => snapshot.SeenAtUtc >= startAtUtc && snapshot.SeenAtUtc <= endAtUtc)
            .OrderBy(snapshot => snapshot.SeenAtUtc)
            .ThenBy(snapshot => snapshot.PartyIndex)
            .ThenBy(snapshot => snapshot.MemberName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.Status.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<ReplayDebuffSnapshot> GetCurrentPullReplayDebuffs(DateTime endAtUtc)
    {
        return GetRecentReplayDebuffs(GetCurrentPullReplayStartAtUtc(endAtUtc), endAtUtc);
    }

    private DateTime GetCurrentPullReplayStartAtUtc(DateTime now)
    {
        var safetyCutoff = now - TimeSpan.FromSeconds(FullReplayMaxRetentionSeconds);
        if (pullStartedAtUtc is { } pullStarted)
        {
            return pullStarted >= safetyCutoff
                ? pullStarted
                : safetyCutoff;
        }

        return now - TimeSpan.FromSeconds(DeathReplayLeadUpSeconds);
    }

    private void PruneRecentReplayPositions(DateTime now)
    {
        if (recentReplayPositionsByActor.Count == 0)
        {
            return;
        }

        var cutoff = GetCurrentPullReplayStartAtUtc(now);
        foreach (var actorKey in recentReplayPositionsByActor.Keys.ToList())
        {
            recentReplayPositionsByActor[actorKey].RemoveAll(snapshot => snapshot.SeenAtUtc < cutoff);
            if (recentReplayPositionsByActor[actorKey].Count == 0)
            {
                recentReplayPositionsByActor.Remove(actorKey);
            }
        }
    }

    private void PruneRecentReplayMarkers(DateTime now)
    {
        if (recentReplayMarkersByActor.Count == 0)
        {
            return;
        }

        var cutoff = GetCurrentPullReplayStartAtUtc(now);
        foreach (var actorKey in recentReplayMarkersByActor.Keys.ToList())
        {
            recentReplayMarkersByActor[actorKey].RemoveAll(snapshot => snapshot.SeenAtUtc < cutoff);
            if (recentReplayMarkersByActor[actorKey].Count == 0)
            {
                recentReplayMarkersByActor.Remove(actorKey);
            }
        }
    }

    private void PruneRecentReplayMechanics(DateTime now)
    {
        if (recentReplayMechanicsBySource.Count == 0)
        {
            return;
        }

        var cutoff = GetCurrentPullReplayStartAtUtc(now);
        foreach (var sourceKey in recentReplayMechanicsBySource.Keys.ToList())
        {
            recentReplayMechanicsBySource[sourceKey].RemoveAll(snapshot => snapshot.SeenAtUtc < cutoff);
            if (recentReplayMechanicsBySource[sourceKey].Count == 0)
            {
                recentReplayMechanicsBySource.Remove(sourceKey);
            }
        }
    }

    private void PruneRecentReplayAnalyzerEvents(DateTime now)
    {
        if (recentReplayAnalyzerEvents.Count == 0)
        {
            return;
        }

        var cutoff = GetCurrentPullReplayStartAtUtc(now);
        recentReplayAnalyzerEvents.RemoveAll(snapshot => snapshot.SeenAtUtc < cutoff);
    }

    private void PruneRecentReplayWorldMarkers(DateTime now)
    {
        if (recentReplayWorldMarkers.Count == 0)
        {
            return;
        }

        // Waymarks are stable during combat and only produce up to eight snapshots.
        if (replayWorldMarkersCapturedForPull)
        {
            return;
        }

        var cutoff = GetCurrentPullReplayStartAtUtc(now);
        recentReplayWorldMarkers.RemoveAll(snapshot => snapshot.SeenAtUtc < cutoff);
    }

    private void PruneRecentReplayMitigations(DateTime now)
    {
        if (recentReplayMitigations.Count == 0)
        {
            return;
        }

        var cutoff = GetCurrentPullReplayStartAtUtc(now);
        recentReplayMitigations.RemoveAll(snapshot =>
            snapshot.SeenAtUtc.AddSeconds(Math.Max(0.05f, snapshot.DurationSeconds)) < cutoff);
    }
}
