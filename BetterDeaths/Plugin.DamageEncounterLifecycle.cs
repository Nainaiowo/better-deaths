namespace BetterDeaths;

using System;
using Dalamud.Game.ClientState.Conditions;

public sealed partial class Plugin
{
    private readonly OverworldDamageEncounterState overworldDamageEncounterState = new();
    private bool overworldDamageCaptureWasEnabled;

    private static bool IsBoundByDutyForDamageMeter() =>
        Condition[ConditionFlag.BoundByDuty] || Condition[ConditionFlag.BoundByDuty56] ||
        Condition[ConditionFlag.BoundByDuty95];

    private bool IsOverworldDamageCaptureEnabled() =>
        CaptureTimingPolicy.IsOverworldDamageMeterCapture(
            ClientState.IsLoggedIn, IsDutyCaptureActive(), IsBoundByDutyForDamageMeter(), IsPvPCaptureBlocked(),
            currentTerritoryId == ClientState.TerritoryType && GetContentCaptureState().SupportsOverworldMeter);

    internal bool IsDamageMeterManualResetAvailable => IsOverworldDamageCaptureEnabled();

    internal bool CanStartNewDamageEncounter =>
        IsDamageMeterManualResetAvailable && damageParsingModule.GetEncounterActivity().HasEncounter;

    internal bool RequestNewDamageEncounter() => overworldDamageEncounterState.RequestReset(CanStartNewDamageEncounter);

    private void OnDamageMeterResetCommand(string command, string args)
    {
        if (!IsDamageMeterManualResetAvailable)
        {
            ChatGui.Print("[Better Deaths] Manual DPS meter resets are only available in the overworld, not inside duties.");
        }
        else if (!RequestNewDamageEncounter())
        {
            ChatGui.Print("[Better Deaths] No active DPS encounter to reset. Saved encounters are kept.");
        }
    }

    private void UpdateDamageEncounterLifecycle(DateTime nowUtc)
    {
        var overworld = IsOverworldDamageCaptureEnabled();
        if (overworldDamageCaptureWasEnabled && !overworld)
        {
            if (CaptureTimingPolicy.CanFinishOverworldOnCaptureExit(
                    ClientState.IsLoggedIn, IsDutyCaptureActive(), IsBoundByDutyForDamageMeter(),
                    GetContentCaptureState().SupportsOverworldMeter))
            {
                ResolveRawCombatQueues(nowUtc);
                EndDamageEncounter(nowUtc, ClientState.IsLoggedIn ? "Left overworld" : "Logged out");
            }
            overworldDamageEncounterState.Reset();
        }

        overworldDamageCaptureWasEnabled = overworld;
        // A request queued outside a duty must be discarded if eligibility changed before execution.
        if (overworldDamageEncounterState.ConsumeReset(CanStartNewDamageEncounter))
        {
            EndDamageEncounter(nowUtc, "Manual new encounter", preserveActiveEffects: true);
            overworldDamageEncounterState.Reset();
        }

        damageParsingModule.SetCombatActive(
            IsDutyCaptureActive() && !IsPvPCaptureBlocked() && IsEffectiveInCombat(), nowUtc);
    }

    private void UpdateOverworldDamageEncounter(DateTime nowUtc, bool participantInCombat)
    {
        if (!IsOverworldDamageCaptureEnabled())
            return;

        var activity = damageParsingModule.GetEncounterActivity();
        if (overworldDamageEncounterState.ShouldEnd(
                activity.HasEncounter, participantInCombat, activity.LatestDamageAtUtc, nowUtc))
        {
            EndDamageEncounter(nowUtc, "Overworld combat ended", preserveActiveEffects: true);
            overworldDamageEncounterState.Reset();
        }
    }
}
