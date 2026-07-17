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

    private void ResolveRawMapEffectPacket(RawMapEffectPacket packet)
    {
        CaptureReplayDmuP2PathOfLightMapEffect(packet);
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

    private void CaptureReplayDmuP2ForsakenAction(RawActionEffectPacket packet)
    {
        if (!IsDmuReplayCaptureContext())
        {
            return;
        }

        switch (packet.ActionId)
        {
            case DmuP2PathOfLightActionId:
                EndReplayDmuP2PathOfLightTower(packet);
                break;
            case DmuP2SpelldriverActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Stack,
                    5.0f,
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
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Spread,
                    5.0f,
                    "Future's End",
                    "dmu-p2-futures-end",
                    2.4f);
                break;
            case DmuP2PastsEndBossActionId:
            case DmuP2PastsEndCloneActionId:
                CaptureReplayDmuPacketCenteredMechanic(
                    packet,
                    ReplayMechanicShape.Spread,
                    5.0f,
                    "Past's End",
                    "dmu-p2-pasts-end",
                    2.4f);
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
                    45.0f,
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
                    ReplayMechanicShape.Circle,
                    6.0f,
                    "Surprise Holy",
                    "dmu-p5-surprise-holy",
                    1.4f);
                break;
        }
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
        DateTime castStartedAtUtc)
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
            true,
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
        var durationSeconds = remainingCastSeconds + DmuReplaySlapHappyPredictionExtraSeconds;
        for (var index = 0; index < positions.Length; index++)
        {
            var variant = index.ToString(CultureInfo.InvariantCulture);
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
        var durationSeconds = remainingCastSeconds + DmuReplayStompAMolePredictionExtraSeconds;
        for (var index = 0; index < offsets.Length; index++)
        {
            var variant = index.ToString(CultureInfo.InvariantCulture);
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
            $"{rawEventKind}:{packet.ActionId}:{packet.Sequence}",
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
        if (!IsDmuReplayCaptureContext() ||
            activeReplayMechanicsByKey.Count == 0)
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

        ClampRecentReplayMechanicEnd(tower.SourceKey, packet.SeenAtUtc);
        activeDmuP2PathOfLightTowersByIndex.Remove(tower.Index);
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

    private static bool IsDmuCasterPoseReplayAction(uint actionId)
    {
        return actionId is DmuP2PathOfLightActionId or
            DmuP2SpellwaveActionId or
            DmuP2AllThingsEndingFirstActionId or
            DmuP2AllThingsEndingSecondActionId or
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
            DmuP5FloodLineActionId;
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
        TrackRecentReplayWorldMarkers(now);

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
            CaptureReplayDmuP3CastPrediction(battleNpc, name, seenAtUtc, mechanicSnapshots);
            CaptureReplayDmuP4P5CastPrediction(battleNpc, name, seenAtUtc, mechanicSnapshots);

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
                    3.0f,
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
        CaptureReplayDmuTethersFromPlayers(seenAtUtc, sampleInterval, mechanicSnapshots);

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

                AddReplayDmuTetherSnapshot(
                    seenAtUtc,
                    battleNpc.EntityId,
                    battleNpc.Name.TextValue,
                    battleNpc.Position,
                    targetMember,
                    "black-hole-tether",
                    DmuBlackHoleTetherId,
                    "Tether",
                    sampleInterval,
                    mechanicSnapshots);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not capture Better Deaths Black Hole replay tether for {EntityId:X8}.", battleNpc.EntityId);
        }
    }

    private unsafe void CaptureReplayDmuTethersFromPlayers(
        DateTime seenAtUtc,
        TimeSpan sampleInterval,
        List<ReplayMechanicSnapshot> mechanicSnapshots)
    {
        if (!IsDmuReplayCaptureContext())
        {
            return;
        }

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

                    if (tether.Id == DmuBlackHoleTetherId &&
                        IsReplayBlackHoleObject(tetherSource.EntityId, tetherSource.Name.TextValue))
                    {
                        AddReplayDmuTetherSnapshot(
                            seenAtUtc,
                            tetherSource.EntityId,
                            tetherSource.Name.TextValue,
                            tetherSource.Position,
                            member,
                            "black-hole-tether",
                            DmuBlackHoleTetherId,
                            "Tether",
                            sampleInterval,
                            mechanicSnapshots);
                        continue;
                    }

                    if (tether.Id == DmuGravenImageTetherId &&
                        IsReplayGravenImageObject(tetherSource.EntityId, tetherSource.Name.TextValue))
                    {
                        AddReplayDmuTetherSnapshot(
                            seenAtUtc,
                            tetherSource.EntityId,
                            tetherSource.Name.TextValue,
                            tetherSource.Position,
                            member,
                            "graven-image-tether",
                            DmuGravenImageTetherId,
                            "Tether",
                            sampleInterval,
                            mechanicSnapshots);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not capture Better Deaths player-side replay tether for {EntityId:X8}.", member.EntityId);
            }
        }
    }

    private void AddReplayDmuTetherSnapshot(
        DateTime seenAtUtc,
        uint sourceEntityId,
        string sourceName,
        Vector3 sourcePosition,
        PartyMemberSnapshot targetMember,
        string rawEventKind,
        uint rawEventId,
        string label,
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
            true));
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
}
