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
    private void OnDutyReset(IDutyStateEventArgs args)
    {
        if (IsPvPCaptureBlocked())
        {
            ResetCurrentPull(suppressResetStateDeaths: false);
            currentMembers.Clear();
            currentTerritoryId = ClientState.TerritoryType;
            currentTerritoryName = GetTerritoryName(currentTerritoryId);
            ClearCurrentDutyInstancePullGroup();
            awaitingCombatClearAfterReset = false;
            return;
        }

        var now = DateTime.UtcNow;
        FinalizePendingDeathsForDutyReset(now);
        BeginResetCombatOverride();
        ArchiveCurrentPullForReview("Duty reset");
        currentTerritoryId = ClientState.TerritoryType;
        currentTerritoryName = GetTerritoryName(currentTerritoryId);
    }

    private void OnDutyStarted(IDutyStateEventArgs args)
    {
        ClearDebugDataForDutyEnter();
        OnDutyReset(args);
        damageParsingModule.ResetCalibration();
        EnsureCurrentDutyInstancePullGroup();
        deathRecapPopupWindow.RefreshVisibility();
    }

    private void ArchiveCurrentPullForReview(
        string reason,
        bool suppressResetStateDeaths = true,
        DateTime? endedAtUtc = null)
    {
        var resolvedEndAtUtc = endedAtUtc ?? DateTime.UtcNow;
        ResolveRawCombatQueues(resolvedEndAtUtc);
        EndDamageEncounter(resolvedEndAtUtc, reason);
        if (currentDeaths.Count > 0)
        {
            CaptureCurrentPullSnapshot(reason);
            PrepareCurrentPullForReview(suppressResetStateDeaths);
            return;
        }

        if (pullStartedAtUtc is not null || lastKnownPullElapsedSeconds > 0.0f)
        {
            ResetCurrentPull(suppressResetStateDeaths, resetDamageParser: false);
            return;
        }

        if (suppressResetStateDeaths)
        {
            ClearLivePullCaptureState();
            StartPostResetDeathSuppression();
        }
    }

    private bool CaptureCurrentPullSnapshot(string reason)
    {
        if (currentPullSnapshotCaptured ||
            currentDeaths.Count == 0)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        ResolveRawCombatQueues(now);
        var replayPositions = GetCurrentPullReplayPositions(now);
        var replayMarkers = GetCurrentPullReplayMarkers(now);
        var replayMechanics = GetCurrentPullReplayMechanics(now);
        var replayAnalyzerEvents = GetCurrentPullReplayAnalyzerEvents(now);
        var replayWorldMarkers = GetCurrentPullReplayWorldMarkers(now);
        var replayMitigations = GetCurrentPullReplayMitigations(now);
        var replayDebuffs = GetCurrentPullReplayDebuffs(now);
        WaitForRecordedPullHistoryLoadForMutation();
        EnsureCurrentDutyInstancePullGroup();
        var pullNumber = GetNextRecordedPullNumber();
        var snapshot = new PullDeathSnapshot(
            now,
            reason,
            currentPullTerritoryId == 0 ? currentTerritoryId : currentPullTerritoryId,
            currentPullTerritoryId == 0 ? currentTerritoryName : currentPullTerritoryName,
            CurrentPullElapsedSeconds,
            currentDeaths.ToList())
        {
            PullNumber = pullNumber,
            CapturedPluginVersion = GetCurrentPluginVersionForSavedData(),
            PullGroupId = currentDutyInstancePullGroupId,
            PullGroupColorIndex = currentDutyInstancePullGroupColorIndex,
            ReplayPositions = replayPositions,
            ReplayMarkers = replayMarkers,
            ReplayMechanics = replayMechanics,
            ReplayAnalyzerEvents = replayAnalyzerEvents,
            ReplayWorldMarkers = replayWorldMarkers,
            ReplayMitigations = replayMitigations,
            ReplayDebuffs = replayDebuffs,
            ReplayDebuffsCaptured = true,
        };

        lock (recordedPullLock)
        {
            recordedPulls.Add(CreateRecordedPullState(snapshot, detailDirty: true));
            TrimRecordedPullsLocked();
            UpdateRecordedPullSummariesLocked();
            MarkRecordedPullStorageDirtyLocked();
        }

        SaveRecordedPullHistory();
        currentPullSnapshotCaptured = true;
        currentPullRecordedPullNumber = pullNumber;
        return true;
    }

    private void ResetCurrentPull(
        bool suppressResetStateDeaths = true,
        bool resetDamageParser = true)
    {
        if (resetDamageParser)
        {
            EndDamageEncounter(DateTime.UtcNow, "Pull reset");
        }

        currentDeaths.Clear();
        ClearLivePullCaptureState();
        if (suppressResetStateDeaths)
        {
            StartPostResetDeathSuppression();
        }
        else
        {
            ClearPostResetDeathSuppression();
        }

        pullStartedAtUtc = null;
        lastInCombatAtUtc = null;
        lastKnownPullElapsedSeconds = 0.0f;
        combatTimerRunning = false;
        currentPullClosedForReview = false;
        currentPullSnapshotCaptured = false;
        currentPullRecordedPullNumber = 0;
        currentPullTerritoryId = 0;
        currentPullTerritoryName = "Unknown territory";
    }

    private void PrepareCurrentPullForReview(bool suppressResetStateDeaths)
    {
        ClearLivePullCaptureState();
        if (suppressResetStateDeaths)
        {
            StartPostResetDeathSuppression();
        }
        else
        {
            ClearPostResetDeathSuppression();
        }

        pullStartedAtUtc = null;
        lastInCombatAtUtc = null;
        combatTimerRunning = false;
        currentPullClosedForReview = true;
    }

    private void ClearLivePullCaptureState()
    {
        recentEventsByMember.Clear();
        recentCombatLogEventsByMember.Clear();
        recentStatusesByMember.Clear();
        recentHpHistoryByMember.Clear();
        recentReplayPositionsByActor.Clear();
        recentReplayMarkersByActor.Clear();
        recentReplayMechanicsBySource.Clear();
        recentReplayAnalyzerEvents.Clear();
        recentReplayWorldMarkers.Clear();
        recentReplayMitigations.Clear();
        recentReplayDebuffs.Clear();
        activeReplayDebuffs.Clear();
        replayWorldMarkersCapturedForPull = false;
        activeDmuP2PathOfLightTowersByIndex.Clear();
        activeDmuP5ArenaHoleIndices.Clear();
        activeReplayMechanicsByKey.Clear();
        recentSourceMitigationHistoryBySource.Clear();
        pendingEffectResultsByMemberSequence.Clear();
        pendingDeathCandidatesByMember.Clear();
        possibleMitigationUsesByMember.Clear();
        lastHpHistorySampleByMember.Clear();
        knownAliveMemberKeys.Clear();
        lastKnownMembersByKey.Clear();
        lastKnownMemberSeenAtUtc.Clear();
        lastReplayPlayerPositionSampleAtUtc = DateTime.MinValue;
        lastReplayObjectPositionSampleAtUtc = DateTime.MinValue;
        lastReplayWorldMarkerSampleAtUtc = DateTime.MinValue;
        lock (rawCombatQueueLock)
        {
            rawActionEffectPackets.Clear();
            rawCombatLogMessages.Clear();
            rawEffectResultPackets.Clear();
            rawActorControlPackets.Clear();
            rawMapEffectPackets.Clear();
        }

        deadMemberKeys.Clear();
    }

    private static bool IsPvPCaptureBlocked()
    {
        return ClientState.IsPvP;
    }

    private static bool IsDutyCaptureActive()
    {
        return DutyState.IsDutyStarted;
    }

    private bool ShouldCaptureLiveCombat(DateTime now)
    {
        return CaptureTimingPolicy.IsLiveCombatCapture(
            IsDutyCaptureActive(),
            IsPvPCaptureBlocked(),
            IsEffectiveInCombat(),
            lastInCombatAtUtc,
            now,
            PostCombatCaptureGrace);
    }

    private bool ShouldAcceptRawCombatCapture(DateTime now)
    {
        return CaptureTimingPolicy.ShouldAcceptRawCombatCapture(
            IsDutyCaptureActive(),
            IsPvPCaptureBlocked(),
            Configuration.CapturePartyDeaths || Configuration.CaptureOtherDeaths,
            IsEffectiveInCombat(),
            lastInCombatAtUtc,
            now,
            PostCombatCaptureGrace);
    }

    private bool ShouldAcceptDamageParserCapture(DateTime now)
    {
        return CaptureTimingPolicy.ShouldAcceptDamageParserPackets(
            IsDutyCaptureActive(),
            IsPvPCaptureBlocked());
    }

    private bool IsEffectiveInCombat()
    {
        return CaptureTimingPolicy.IsEffectiveInCombat(
            Condition[ConditionFlag.InCombat],
            awaitingCombatClearAfterReset);
    }

    private void BeginResetCombatOverride()
    {
        awaitingCombatClearAfterReset = true;
        combatTimerRunning = false;
        lastInCombatAtUtc = null;
    }

    private void StartPostResetDeathSuppression()
    {
        collectingPostResetDeadMembers = true;
        postResetSuppressedDeadMemberKeys.Clear();
        foreach (var member in currentMembers)
        {
            if (!member.IsDead)
            {
                continue;
            }

            postResetSuppressedDeadMemberKeys.Add(member.MemberKey);
            deadMemberKeys.Add(member.MemberKey);
        }
    }

    private void ClearPostResetDeathSuppression()
    {
        collectingPostResetDeadMembers = false;
        postResetSuppressedDeadMemberKeys.Clear();
    }

    private void UpdatePostResetDeathSuppression()
    {
        if (!IsDutyCaptureActive())
        {
            return;
        }

        var hasAliveMember = false;
        foreach (var member in currentMembers)
        {
            if (!member.IsDead)
            {
                hasAliveMember = true;
                postResetSuppressedDeadMemberKeys.Remove(member.MemberKey);
            }
        }

        if (!collectingPostResetDeadMembers)
        {
            return;
        }

        var inCombat = IsEffectiveInCombat();
        if (!inCombat)
        {
            foreach (var member in currentMembers)
            {
                if (!member.IsDead)
                {
                    continue;
                }

                if (postResetSuppressedDeadMemberKeys.Add(member.MemberKey))
                {
                    AddDebugLog($"Suppressed reset-state KO for {member.MemberName}.");
                }

                deadMemberKeys.Add(member.MemberKey);
            }
        }

        if (inCombat || hasAliveMember)
        {
            collectingPostResetDeadMembers = false;
        }
    }

    private void EnsurePullStarted(DateTime now)
    {
        if (!IsDutyCaptureActive())
        {
            return;
        }

        if (currentPullClosedForReview)
        {
            ResetCurrentPull(suppressResetStateDeaths: false);
        }

        if (pullStartedAtUtc is null)
        {
            pullStartedAtUtc = now;
            currentPullTerritoryId = currentTerritoryId;
            currentPullTerritoryName = currentTerritoryName;
        }

        combatTimerRunning = combatTimerRunning || IsEffectiveInCombat();
        lastKnownPullElapsedSeconds = CalculatePullElapsed(now);
    }

    private void UpdateCombatTimerState(DateTime now)
    {
        if (!IsDutyCaptureActive())
        {
            return;
        }

        var reportedInCombat = Condition[ConditionFlag.InCombat];
        if (awaitingCombatClearAfterReset)
        {
            combatTimerRunning = false;
            lastInCombatAtUtc = null;
            if (CaptureTimingPolicy.ShouldReleaseResetCombatOverride(
                    awaitingCombatClearAfterReset,
                    reportedInCombat))
            {
                awaitingCombatClearAfterReset = false;
                AddDebugLog("Duty reset combat override cleared after the game reported combat ended.");
            }

            return;
        }

        var inCombat = reportedInCombat;
        if (inCombat)
        {
            if (currentPullClosedForReview)
            {
                ResetCurrentPull(
                    suppressResetStateDeaths: false,
                    resetDamageParser: false);
            }

            lastInCombatAtUtc = now;
            if (pullStartedAtUtc is null)
            {
                pullStartedAtUtc = now;
                lastKnownPullElapsedSeconds = 0.0f;
                currentPullTerritoryId = currentTerritoryId;
                currentPullTerritoryName = currentTerritoryName;
            }

            combatTimerRunning = true;
            lastKnownPullElapsedSeconds = CalculatePullElapsed(now);
            return;
        }

        if (CaptureTimingPolicy.ShouldClosePull(
                IsDutyCaptureActive(),
                IsPvPCaptureBlocked(),
                inCombat,
                pullStartedAtUtc is not null,
                currentPullClosedForReview,
                lastInCombatAtUtc,
                now,
                PostCombatCaptureGrace))
        {
            var closeAtUtc = CaptureTimingPolicy.GetPullCloseTime(lastInCombatAtUtc!.Value, PostCombatCaptureGrace);
            lastKnownPullElapsedSeconds = CalculatePullElapsed(closeAtUtc);
            combatTimerRunning = false;
            ArchiveCurrentPullForReview(
                "Combat ended",
                suppressResetStateDeaths: false,
                endedAtUtc: closeAtUtc);
            return;
        }

        if (pullStartedAtUtc is not null &&
            lastInCombatAtUtc is not null &&
            ShouldCaptureLiveCombat(now))
        {
            combatTimerRunning = true;
            lastKnownPullElapsedSeconds = CalculatePullElapsed(now);
            return;
        }

        combatTimerRunning = false;
    }

    private float CalculatePullElapsed(DateTime now)
    {
        return pullStartedAtUtc is null
            ? lastKnownPullElapsedSeconds
            : (float)Math.Max(0.0, (now - pullStartedAtUtc.Value).TotalSeconds);
    }

    private void EnsureCurrentDutyInstancePullGroup()
    {
        if (!string.IsNullOrWhiteSpace(currentDutyInstancePullGroupId) || !IsDutyCaptureActive())
        {
            return;
        }

        if (TryRestoreCurrentDutyInstancePullGroup())
        {
            return;
        }

        if (recordedPullHistoryLoading)
        {
            return;
        }

        StartNewDutyInstancePullGroup();
    }

    private bool TryRestoreCurrentDutyInstancePullGroup()
    {
        var territoryId = currentTerritoryId == 0 ? ClientState.TerritoryType : currentTerritoryId;
        if (territoryId == 0)
        {
            return false;
        }

        if (Configuration.ActiveDutyInstancePullGroupTerritoryId == territoryId &&
            !string.IsNullOrWhiteSpace(Configuration.ActiveDutyInstancePullGroupId) &&
            Configuration.ActiveDutyInstancePullGroupColorIndex >= 0 &&
            Configuration.ActiveDutyInstancePullGroupColorIndex < PullGroupColorPaletteSize)
        {
            currentDutyInstancePullGroupId = Configuration.ActiveDutyInstancePullGroupId;
            currentDutyInstancePullGroupColorIndex = Configuration.ActiveDutyInstancePullGroupColorIndex;
            return true;
        }

        var now = DateTime.UtcNow;
        RecordedPullSummary? latestMatchingPull = null;
        lock (recordedPullLock)
        {
            latestMatchingPull = recordedPulls
                .Select(state => state.Summary)
                .Where(summary =>
                    summary.TerritoryId == territoryId &&
                    !string.IsNullOrWhiteSpace(summary.PullGroupId) &&
                    summary.PullGroupColorIndex >= 0 &&
                    summary.PullGroupColorIndex < PullGroupColorPaletteSize &&
                    summary.CapturedAtUtc > Configuration.ActiveDutyInstancePullGroupClearedAtUtc &&
                    now - summary.CapturedAtUtc <= RecentPullGroupRestoreWindow)
                .OrderByDescending(summary => summary.CapturedAtUtc)
                .FirstOrDefault();
        }

        if (latestMatchingPull is null)
        {
            return false;
        }

        currentDutyInstancePullGroupId = latestMatchingPull.PullGroupId;
        currentDutyInstancePullGroupColorIndex = latestMatchingPull.PullGroupColorIndex;
        PersistCurrentDutyInstancePullGroup();
        return true;
    }

    private void StartNewDutyInstancePullGroup()
    {
        currentDutyInstancePullGroupId = Guid.NewGuid().ToString("N");
        currentDutyInstancePullGroupColorIndex = GetNextDutyInstancePullGroupColorIndex();
        PersistCurrentDutyInstancePullGroup();
    }

    private void ClearCurrentDutyInstancePullGroup()
    {
        currentDutyInstancePullGroupId = string.Empty;
        currentDutyInstancePullGroupColorIndex = -1;
        if (!string.IsNullOrWhiteSpace(Configuration.ActiveDutyInstancePullGroupId) ||
            Configuration.ActiveDutyInstancePullGroupColorIndex != -1 ||
            Configuration.ActiveDutyInstancePullGroupTerritoryId != 0)
        {
            Configuration.ActiveDutyInstancePullGroupId = string.Empty;
            Configuration.ActiveDutyInstancePullGroupColorIndex = -1;
            Configuration.ActiveDutyInstancePullGroupTerritoryId = 0;
            Configuration.ActiveDutyInstancePullGroupClearedAtUtc = DateTime.UtcNow;
            SaveConfiguration();
        }
    }

    private void PersistCurrentDutyInstancePullGroup()
    {
        var territoryId = currentTerritoryId == 0 ? ClientState.TerritoryType : currentTerritoryId;
        if (territoryId == 0 || string.IsNullOrWhiteSpace(currentDutyInstancePullGroupId) || currentDutyInstancePullGroupColorIndex < 0)
        {
            return;
        }

        if (Configuration.ActiveDutyInstancePullGroupId == currentDutyInstancePullGroupId &&
            Configuration.ActiveDutyInstancePullGroupColorIndex == currentDutyInstancePullGroupColorIndex &&
            Configuration.ActiveDutyInstancePullGroupTerritoryId == territoryId)
        {
            return;
        }

        Configuration.ActiveDutyInstancePullGroupId = currentDutyInstancePullGroupId;
        Configuration.ActiveDutyInstancePullGroupColorIndex = currentDutyInstancePullGroupColorIndex;
        Configuration.ActiveDutyInstancePullGroupTerritoryId = territoryId;
        Configuration.ActiveDutyInstancePullGroupClearedAtUtc = DateTime.MinValue;
        SaveConfiguration();
    }

    private int GetNextDutyInstancePullGroupColorIndex()
    {
        lock (recordedPullLock)
        {
            var previousColorIndex = recordedPulls
                .Select(state => state.Summary.PullGroupColorIndex)
                .Where(colorIndex => colorIndex >= 0)
                .Cast<int?>()
                .LastOrDefault();
            return previousColorIndex is { } colorIndex
                ? (colorIndex + 1) % PullGroupColorPaletteSize
                : 0;
        }
    }
}
