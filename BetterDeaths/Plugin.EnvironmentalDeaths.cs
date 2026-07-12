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
    private readonly record struct EnvironmentalPositionSample(
        DateTime SeenAtUtc,
        float X,
        float Y,
        float Z);

    private static EnvironmentalDeathAssessment? CreateEnvironmentalDeathAssessment(
        PartyMemberSnapshot member,
        DateTime deathSeenAtUtc,
        uint territoryId,
        CombatEventRecord? cause,
        FatalSequenceRecord? fatalSequence,
        IReadOnlyList<ReplayPositionSnapshot> replayPositions,
        bool environmentSourceDeath)
    {
        var targetSamples = GetEnvironmentalDeathTargetSamples(member, deathSeenAtUtc, replayPositions);
        if (targetSamples.Count < 2 && !environmentSourceDeath)
        {
            return null;
        }

        var evidence = new List<string>();
        var score = environmentSourceDeath ? 0.82f : 0.0f;
        if (environmentSourceDeath)
        {
            evidence.Add("The death packet reported the environment/no actor as the source.");
        }

        var noCapturedFatalEvent = cause is null &&
            fatalSequence is not { Events.Count: > 0 } &&
            fatalSequence is not { LogEvents.Count: > 0 };
        var hasFallEvidence = false;
        var hasPositionOutlierEvidence = false;
        var hasKnownArenaEdgeEvidence = false;

        if (targetSamples.Count >= 2)
        {
            var lastSample = targetSamples[^1];
            var recentSamples = targetSamples
                .Where(sample => sample.SeenAtUtc >= lastSample.SeenAtUtc - EnvironmentalDeathMotionWindow)
                .ToList();
            var highestSample = recentSamples
                .OrderByDescending(sample => sample.Y)
                .First();
            var verticalDrop = highestSample.Y - lastSample.Y;
            hasFallEvidence = verticalDrop >= EnvironmentalFallYDropThreshold;
            if (hasFallEvidence)
            {
                var dropSeconds = Math.Max(0.0, (lastSample.SeenAtUtc - highestSample.SeenAtUtc).TotalSeconds);
                score += verticalDrop >= EnvironmentalStrongFallYDropThreshold ? 0.65f : 0.48f;
                evidence.Add($"Recent position trail dropped {verticalDrop:0.0} yalms over {dropSeconds:0.0}s before KO.");
            }

            var replayModule = ReplayEncounterModules.Get(territoryId);
            if (replayModule.TryGetReplayArena(out var arena) &&
                TryGetEnvironmentalArenaEdgeDistance(arena, lastSample, out var edgeDistance, out var edgeReferenceDistance) &&
                edgeDistance <= EnvironmentalKnownArenaEdgeTolerance)
            {
                hasKnownArenaEdgeEvidence = true;
                if (noCapturedFatalEvent || environmentSourceDeath)
                {
                    score += edgeDistance <= 0.0f ? 0.60f : 0.48f;
                    evidence.Add(FormatKnownArenaEdgeEvidence(replayModule.Name, arena, edgeDistance, edgeReferenceDistance));
                }
            }

            if (TryGetEnvironmentalPartyReferenceBand(
                    replayPositions,
                    member,
                    deathSeenAtUtc,
                    out var partyCenter,
                    out var partyRadius,
                    out var referenceCount))
            {
                var targetDistance = DistanceXZ(new Vector3(lastSample.X, 0.0f, lastSample.Z), partyCenter);
                var outlierDistance = MathF.Max(
                    EnvironmentalOutlierMinimumDistance,
                    MathF.Max(
                        partyRadius + EnvironmentalOutlierMinimumMargin,
                        partyRadius * EnvironmentalOutlierRadiusMultiplier));
                if (targetDistance >= outlierDistance)
                {
                    hasPositionOutlierEvidence = true;
                    var excessDistance = MathF.Max(0.0f, targetDistance - partyRadius);
                    var outlierScore = Math.Clamp(excessDistance / 28.0f, 0.18f, 0.35f);
                    score += outlierScore;
                    evidence.Add(
                        $"Last position was {targetDistance:0.0} yalms from the inferred party center; recent party band was about {partyRadius:0.0} yalms from {referenceCount} players.");
                }
            }
        }

        if (noCapturedFatalEvent && score > 0.0f && !environmentSourceDeath)
        {
            score += 0.15f;
            evidence.Add("No captured fatal damage, status, or combat-log event. This supports environmental detection but is not proof.");
        }

        if (!environmentSourceDeath &&
            !hasFallEvidence &&
            (!(hasKnownArenaEdgeEvidence || hasPositionOutlierEvidence) || !noCapturedFatalEvent))
        {
            return null;
        }

        score = Math.Clamp(score, 0.0f, environmentSourceDeath ? 0.98f : 0.95f);
        if (!environmentSourceDeath &&
            (score < EnvironmentalDeathMinimumConfidence || evidence.Count == 0))
        {
            return null;
        }

        var kind = hasFallEvidence
            ? EnvironmentalDeathKind.LikelyFall
            : hasKnownArenaEdgeEvidence
                ? EnvironmentalDeathKind.LikelyDeathWall
                : environmentSourceDeath
                    ? EnvironmentalDeathKind.LikelyWalled
                : noCapturedFatalEvent
                    ? EnvironmentalDeathKind.PossibleDeathWall
                    : EnvironmentalDeathKind.PossibleEnvironmental;
        return new EnvironmentalDeathAssessment(
            kind,
            score,
            environmentSourceDeath ? "Walled" : CreateEnvironmentalDeathSummary(kind, score),
            evidence)
        {
            EnvironmentSourceDeath = environmentSourceDeath,
        };
    }

    private static List<EnvironmentalPositionSample> GetEnvironmentalDeathTargetSamples(
        PartyMemberSnapshot member,
        DateTime deathSeenAtUtc,
        IReadOnlyList<ReplayPositionSnapshot> replayPositions)
    {
        var startAtUtc = deathSeenAtUtc - EnvironmentalDeathMotionWindow;
        var actorKey = $"player:{member.MemberKey}";
        var samples = replayPositions
            .Where(snapshot => snapshot.ActorKind == ReplayActorKind.Player)
            .Where(snapshot => snapshot.SeenAtUtc >= startAtUtc && snapshot.SeenAtUtc <= deathSeenAtUtc)
            .Where(snapshot => EnvironmentalReplayPositionMatchesMember(snapshot, member, actorKey))
            .Where(snapshot => IsUsableReplayPosition(new Vector3(snapshot.X, snapshot.Y, snapshot.Z)))
            .Select(snapshot => new EnvironmentalPositionSample(
                snapshot.SeenAtUtc,
                snapshot.X,
                snapshot.Y,
                snapshot.Z))
            .ToList();
        samples.Sort((left, right) => left.SeenAtUtc.CompareTo(right.SeenAtUtc));

        if (IsUsableReplayPosition(member.Position) &&
            (samples.Count == 0 || Duration(samples[^1].SeenAtUtc, deathSeenAtUtc) > TimeSpan.FromMilliseconds(50)))
        {
            samples.Add(new EnvironmentalPositionSample(
                deathSeenAtUtc,
                member.Position.X,
                member.Position.Y,
                member.Position.Z));
        }

        samples.Sort((left, right) => left.SeenAtUtc.CompareTo(right.SeenAtUtc));
        return samples;
    }

    private static bool TryGetEnvironmentalPartyReferenceBand(
        IReadOnlyList<ReplayPositionSnapshot> replayPositions,
        PartyMemberSnapshot member,
        DateTime deathSeenAtUtc,
        out Vector3 partyCenter,
        out float partyRadius,
        out int referenceCount)
    {
        partyCenter = Vector3.Zero;
        partyRadius = 0.0f;
        referenceCount = 0;

        var startAtUtc = deathSeenAtUtc - EnvironmentalDeathPartyReferenceWindow;
        var targetActorKey = $"player:{member.MemberKey}";
        var references = replayPositions
            .Where(snapshot => snapshot.ActorKind == ReplayActorKind.Player)
            .Where(snapshot => snapshot.SeenAtUtc >= startAtUtc && snapshot.SeenAtUtc <= deathSeenAtUtc)
            .Where(snapshot => !snapshot.IsDead)
            .Where(snapshot => !EnvironmentalReplayPositionMatchesMember(snapshot, member, targetActorKey))
            .Where(snapshot => IsUsableReplayPosition(new Vector3(snapshot.X, snapshot.Y, snapshot.Z)))
            .GroupBy(snapshot => snapshot.ActorKey, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(snapshot => snapshot.SeenAtUtc).First())
            .ToList();
        if (references.Count < 3)
        {
            return false;
        }

        referenceCount = references.Count;
        var center = new Vector3(
            (float)references.Average(snapshot => snapshot.X),
            0.0f,
            (float)references.Average(snapshot => snapshot.Z));
        partyCenter = center;
        partyRadius = references
            .Select(snapshot => DistanceXZ(new Vector3(snapshot.X, 0.0f, snapshot.Z), center))
            .DefaultIfEmpty(0.0f)
            .Max();
        return true;
    }

    private static bool EnvironmentalReplayPositionMatchesMember(
        ReplayPositionSnapshot snapshot,
        PartyMemberSnapshot member,
        string actorKey)
    {
        return string.Equals(snapshot.ActorKey, actorKey, StringComparison.Ordinal) ||
            (member.EntityId != 0 && snapshot.EntityId == member.EntityId) ||
            (snapshot.PartyIndex == member.PartyIndex &&
                string.Equals(snapshot.ActorName, member.MemberName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetEnvironmentalArenaEdgeDistance(
        ReplayArenaInfo arena,
        EnvironmentalPositionSample sample,
        out float edgeDistance,
        out float edgeReferenceDistance)
    {
        edgeDistance = 0.0f;
        edgeReferenceDistance = 0.0f;
        if (!float.IsFinite(arena.CenterX) ||
            !float.IsFinite(arena.CenterZ) ||
            !float.IsFinite(arena.Radius) ||
            arena.Radius <= 0.1f)
        {
            return false;
        }

        var offsetX = sample.X - arena.CenterX;
        var offsetZ = sample.Z - arena.CenterZ;
        edgeReferenceDistance = arena.Shape switch
        {
            ReplayArenaShape.Circle => MathF.Sqrt((offsetX * offsetX) + (offsetZ * offsetZ)),
            ReplayArenaShape.Square => MathF.Max(MathF.Abs(offsetX), MathF.Abs(offsetZ)),
            _ => 0.0f,
        };
        edgeDistance = arena.Radius - edgeReferenceDistance;
        return true;
    }

    private static string FormatKnownArenaEdgeEvidence(
        string moduleName,
        ReplayArenaInfo arena,
        float edgeDistance,
        float edgeReferenceDistance)
    {
        var shape = arena.Shape switch
        {
            ReplayArenaShape.Circle => "circular",
            ReplayArenaShape.Square => "square",
            _ => "known",
        };
        var boundaryText = edgeDistance <= 0.0f
            ? $"{MathF.Abs(edgeDistance):0.0} yalms outside"
            : $"{edgeDistance:0.0} yalms inside";
        return $"Last position was {boundaryText} the known {moduleName} {shape} arena boundary ({edgeReferenceDistance:0.0}/{arena.Radius:0.0} yalms from center/edge reference).";
    }

    private static string CreateEnvironmentalDeathSummary(EnvironmentalDeathKind kind, float confidence)
    {
        var confidenceLabel = confidence >= 0.75f
            ? "high confidence"
            : confidence >= 0.55f
                ? "medium confidence"
                : "low confidence";
        return kind switch
        {
            EnvironmentalDeathKind.LikelyFall => $"Likely walled: fall/jump-off ({confidenceLabel})",
            EnvironmentalDeathKind.LikelyDeathWall => $"Likely walled: arena boundary ({confidenceLabel})",
            EnvironmentalDeathKind.LikelyWalled => $"Likely walled ({confidenceLabel})",
            EnvironmentalDeathKind.PossibleDeathWall => $"Possible wall or out-of-bounds KO ({confidenceLabel})",
            _ => $"Possible environmental KO ({confidenceLabel})",
        };
    }
}
