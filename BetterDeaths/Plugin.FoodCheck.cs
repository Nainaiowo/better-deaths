using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.DutyState;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private CancellationTokenSource? partyFoodStatusPollingSession;
    private Dictionary<uint, PartyFoodStatusSample> partyFoodStatusByEntity = [];
    private bool foodCheckUpdateFailureReported;

    private bool NormalizePartyFoodConfiguration()
    {
        var warningMinutes = Math.Clamp(Configuration.PartyFoodWarningMinutes, 1, 60);
        var placementIconSize = Configuration.GetPartyCooldownPlacement().IconSize;
        var markerIconSize = FoodCheckMarkerPolicy.NormalizeIconSize(
            Configuration.PartyFoodStatusIconSize ?? placementIconSize, placementIconSize);
        var markerOffsetX = FoodCheckMarkerPolicy.NormalizeOffset(Configuration.PartyFoodStatusIconOffsetX);
        var markerOffsetY = FoodCheckMarkerPolicy.NormalizeOffset(Configuration.PartyFoodStatusIconOffsetY);
        var tintOpacity = float.IsFinite(Configuration.PartyFoodPlateTintOpacity)
            ? Math.Clamp(Configuration.PartyFoodPlateTintOpacity, 0.04f, 0.40f)
            : 0.10f;
        var tintHeight = FoodCheckTintPolicy.NormalizeHeightFraction(Configuration.PartyFoodPlateTintHeight);
        var tintOffsetY = FoodCheckTintPolicy.NormalizeOffsetY(Configuration.PartyFoodPlateTintOffsetY);
        var changed = warningMinutes != Configuration.PartyFoodWarningMinutes ||
            Configuration.PartyFoodStatusIconSize is null ||
            !markerIconSize.Equals(Configuration.PartyFoodStatusIconSize) ||
            !markerOffsetX.Equals(Configuration.PartyFoodStatusIconOffsetX) ||
            !markerOffsetY.Equals(Configuration.PartyFoodStatusIconOffsetY) ||
            !tintOpacity.Equals(Configuration.PartyFoodPlateTintOpacity) ||
            !tintHeight.Equals(Configuration.PartyFoodPlateTintHeight) ||
            !tintOffsetY.Equals(Configuration.PartyFoodPlateTintOffsetY);
        Configuration.PartyFoodWarningMinutes = warningMinutes;
        Configuration.PartyFoodStatusIconSize = markerIconSize;
        Configuration.PartyFoodStatusIconOffsetX = markerOffsetX;
        Configuration.PartyFoodStatusIconOffsetY = markerOffsetY;
        Configuration.PartyFoodPlateTintOpacity = tintOpacity;
        Configuration.PartyFoodPlateTintHeight = tintHeight;
        Configuration.PartyFoodPlateTintOffsetY = tintOffsetY;
        return changed;
    }

    internal void SetPartyFoodStatusVisibility(bool visible)
    {
        Configuration.ShowPartyFoodStatus = visible;
        if (!visible)
            StopPartyFoodStatusPolling();
        else
            RefreshPartyFoodStatusPolling();
        SaveConfiguration();
    }

    internal void SetPartyFoodOutsideInstances(bool enabled)
    {
        Configuration.PartyFoodShowOutsideInstances = enabled;
        RefreshPartyFoodStatusPolling();
        SaveConfiguration();
    }

    internal bool TryGetPartyFoodStatus(uint entityId, out PartyFoodStatusSample status) =>
        Volatile.Read(ref partyFoodStatusByEntity).TryGetValue(entityId, out status);

    internal bool PartyFoodStatusCacheAvailable => Volatile.Read(ref partyFoodStatusByEntity).Count > 0;

    private bool CanPollPartyFoodStatus() => FoodCheckPollingPolicy.ShouldPoll(
        Configuration.ShowPartyFoodStatus,
        ClientState.IsLoggedIn,
        IsDutyCaptureActive() || IsBoundByDutyForDamageMeter(),
        Configuration.PartyFoodShowOutsideInstances,
        Condition[ConditionFlag.InCombat],
        ClientState.IsPvP);

    private void InitializePartyFoodStatusPolling()
    {
        Condition.ConditionChange += OnPartyFoodConditionChanged;
        ClientState.Login += OnPartyFoodLogin;
        ClientState.Logout += OnPartyFoodLogout;
        ClientState.TerritoryChanged += OnPartyFoodTerritoryChanged;
        ClientState.EnterPvP += OnPartyFoodContextChanged;
        ClientState.LeavePvP += OnPartyFoodContextChanged;
        DutyState.DutyStarted += OnPartyFoodDutyStateChanged;
        DutyState.DutyWiped += OnPartyFoodDutyStateChanged;
        DutyState.DutyRecommenced += OnPartyFoodDutyStateChanged;
        DutyState.DutyCompleted += OnPartyFoodDutyStateChanged;
        RefreshPartyFoodStatusPolling();
    }

    private void DisposePartyFoodStatusPolling()
    {
        Condition.ConditionChange -= OnPartyFoodConditionChanged;
        ClientState.Login -= OnPartyFoodLogin;
        ClientState.Logout -= OnPartyFoodLogout;
        ClientState.TerritoryChanged -= OnPartyFoodTerritoryChanged;
        ClientState.EnterPvP -= OnPartyFoodContextChanged;
        ClientState.LeavePvP -= OnPartyFoodContextChanged;
        DutyState.DutyStarted -= OnPartyFoodDutyStateChanged;
        DutyState.DutyWiped -= OnPartyFoodDutyStateChanged;
        DutyState.DutyRecommenced -= OnPartyFoodDutyStateChanged;
        DutyState.DutyCompleted -= OnPartyFoodDutyStateChanged;
        StopPartyFoodStatusPolling();
    }

    private void OnPartyFoodConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag is ConditionFlag.InCombat or ConditionFlag.BoundByDuty or ConditionFlag.BoundByDuty56 or ConditionFlag.BoundByDuty95)
            RefreshPartyFoodStatusPolling();
    }

    private void OnPartyFoodLogin() => RefreshPartyFoodStatusPolling();

    private void OnPartyFoodLogout(int type, int code) => StopPartyFoodStatusPolling();

    private void OnPartyFoodTerritoryChanged(uint territoryType) => RefreshPartyFoodStatusPolling();

    private void OnPartyFoodContextChanged() => RefreshPartyFoodStatusPolling();

    private void OnPartyFoodDutyStateChanged(IDutyStateEventArgs args) => RefreshPartyFoodStatusPolling();

    private void RefreshPartyFoodStatusPolling()
    {
        if (disposing || !CanPollPartyFoodStatus())
        {
            StopPartyFoodStatusPolling();
            return;
        }

        if (Volatile.Read(ref partyFoodStatusPollingSession) is not null)
            return;

        var session = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref partyFoodStatusPollingSession, session, null) is not null)
        {
            session.Dispose();
            return;
        }
        _ = RunPartyFoodStatusPollingAsync(session);
    }

    private void StopPartyFoodStatusPolling()
    {
        var session = Interlocked.Exchange(ref partyFoodStatusPollingSession, null);
        if (session is not null)
        {
            try
            {
                session.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
        ClearPartyFoodStatusCache();
    }

    private async Task RunPartyFoodStatusPollingAsync(CancellationTokenSource session)
    {
        try
        {
            while (!session.IsCancellationRequested)
            {
                var keepPolling = await Framework.RunOnTick(() =>
                {
                    if (session.IsCancellationRequested || !CanPollPartyFoodStatus())
                        return false;
                    CapturePartyFoodStatusCache();
                    return true;
                }, delay: FoodCheckPollingPolicy.Interval, cancellationToken: session.Token).ConfigureAwait(false);
                if (!keepPolling)
                    break;
            }
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!disposing)
                Log.Warning(error, "Could not schedule party food status updates.");
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref partyFoodStatusPollingSession, null, session), session))
                ClearPartyFoodStatusCache();
            session.Dispose();
        }
    }

    private void CapturePartyFoodStatusCache()
    {
        try
        {
            var next = new Dictionary<uint, PartyFoodStatusSample>(PartyList.Length + 1);
            var localPlayer = ObjectTable.LocalPlayer;
            var includesLocalPlayer = false;
            foreach (var member in PartyList)
            {
                if (member.EntityId is 0 or InvalidActorEntityId || member.GameObject is not IBattleChara character)
                    continue;
                next[member.EntityId] = ReadPartyFoodStatus(character.StatusList);
                includesLocalPlayer |= member.EntityId == localPlayer?.EntityId;
            }

            if (localPlayer is IBattleChara localCharacter && localPlayer.EntityId is not (0 or InvalidActorEntityId) &&
                !includesLocalPlayer)
                next[localPlayer.EntityId] = ReadPartyFoodStatus(localCharacter.StatusList);

            PublishPartyFoodStatuses(next);
            foodCheckUpdateFailureReported = false;
        }
        catch (Exception error)
        {
            ClearPartyFoodStatusCache();
            if (!foodCheckUpdateFailureReported)
            {
                foodCheckUpdateFailureReported = true;
                Log.Warning(error, "Could not update party food status.");
            }
        }
    }

    private static PartyFoodStatusSample ReadPartyFoodStatus(IEnumerable<IStatus> statuses)
    {
        foreach (var status in statuses)
            if (status.StatusId == FoodCheckFormatter.WellFedStatusId)
                return new(true, status.RemainingTime);
        return new(false, 0);
    }

    private void PublishPartyFoodStatuses(Dictionary<uint, PartyFoodStatusSample> statuses) =>
        Volatile.Write(ref partyFoodStatusByEntity, statuses);

    private void ClearPartyFoodStatusCache()
    {
        if (Volatile.Read(ref partyFoodStatusByEntity).Count > 0)
            PublishPartyFoodStatuses([]);
    }
}
