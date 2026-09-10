namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Globalization;

internal sealed record DamageStatusTimingUpdate(
    uint TargetEntityId, byte Slot, DamageStatusSnapshot Status, DateTime SeenAtUtc,
    bool MissingInformation = false);

// Network slot updates own expiry. Reading a countdown must never move that expiry.
internal sealed class DamageStatusTimingLedger
{
    private static readonly TimeSpan HistoryRetention = TimeSpan.FromSeconds(35);
    private readonly Dictionary<uint, Dictionary<byte, SlotState>> actors = [];

    public bool Observe(DamageStatusTimingUpdate update)
    {
        if (update.TargetEntityId == 0 || update.Slot >= 60 || !float.IsFinite(update.Status.RemainingTime))
            return false;
        if (!actors.TryGetValue(update.TargetEntityId, out var slots))
            actors[update.TargetEntityId] = slots = [];
        if (!slots.TryGetValue(update.Slot, out var slot))
            slots[update.Slot] = slot = new();
        if (slot.LastSeenAtUtc > update.SeenAtUtc)
            return false;
        slot.LastSeenAtUtc = update.SeenAtUtc;

        var incoming = update.Status with { StatusSlot = update.Slot };
        var remaining = DamageStatusTiming.DecodeRemaining(incoming.RemainingTime);
        var duration = remaining == 0 ? 9999 : Math.Min(9999, remaining);
        var expiry = update.SeenAtUtc.AddSeconds(duration);
        var previous = slot.Current;
        if (update.MissingInformation && previous?.Status.StatusId is null or 0)
            return false;
        if (incoming.StatusId != 0 && previous?.Status.StatusId is > 0)
        {
            var sameIdentity = incoming.StatusId == previous.Status.StatusId &&
                incoming.Source.EntityId == previous.Status.Source.EntityId;
            if (update.MissingInformation)
            {
                if (incoming.Parameter == previous.Status.Parameter)
                    return false;
                incoming = incoming with { Source = previous.Status.Source };
                expiry = previous.ExpiresAtUtc;
                duration = previous.Duration;
            }
            else if (sameIdentity)
            {
                var difference = Math.Abs((expiry - previous.ExpiresAtUtc).TotalSeconds);
                var changedDuration = duration != 9999 && difference > 2.0;
                if (!changedDuration && duration != 9999 && slot.RefreshPending && difference > 0.2)
                {
                    changedDuration = true;
                    slot.RefreshPending = false;
                }
                if (!changedDuration && incoming.Parameter == previous.Status.Parameter)
                    return false;
            }
        }
        else if (incoming.StatusId == 0 && previous?.Status.StatusId is null or 0)
        {
            // Keep an empty slot as evidence that a later memory read cannot resurrect it.
            slot.Current ??= new(incoming, update.SeenAtUtc, expiry, expiry, duration);
            return false;
        }

        if (previous is not null)
        {
            slot.History.RemoveAll(entry => entry.UntilUtc < update.SeenAtUtc - HistoryRetention);
            slot.History.Add((previous, update.SeenAtUtc));
        }
        slot.Current = new(incoming, update.SeenAtUtc, expiry,
            update.SeenAtUtc.AddSeconds(ReportedDuration(duration)), duration);
        return true;
    }

    public void MarkRefresh(uint targetEntityId, uint statusId)
    {
        if (actors.TryGetValue(targetEntityId, out var slots))
            foreach (var slot in slots.Values)
                if (slot.Current?.Status.StatusId == statusId)
                    slot.RefreshPending = true;
    }

    public IReadOnlyList<DamageStatusSnapshot> Resolve(uint actorId,
        IReadOnlyList<DamageStatusSnapshot> captured, DateTime seenAtUtc) =>
        Resolve(actorId, captured, seenAtUtc, sourceModifierWindow: false);

    // Only source-modifier calibration admits the inclusive one-second expiry window.
    // Unknown timing retains captured evidence; removals and replacements still own slot identity.
    public IReadOnlyList<DamageStatusSnapshot> ResolveSourceModifiers(uint actorId,
        IReadOnlyList<DamageStatusSnapshot> captured, DateTime seenAtUtc) =>
        Resolve(actorId, captured, seenAtUtc, sourceModifierWindow: true);

    private IReadOnlyList<DamageStatusSnapshot> Resolve(uint actorId,
        IReadOnlyList<DamageStatusSnapshot> captured, DateTime seenAtUtc, bool sourceModifierWindow)
    {
        if (!actors.TryGetValue(actorId, out var slots))
            return captured;
        var resolved = new List<DamageStatusSnapshot>(captured.Count);
        var used = new HashSet<byte>();
        foreach (var snapshot in captured)
        {
            var slotId = snapshot.StatusSlot;
            if (slotId is null)
                foreach (var (candidate, candidateSlot) in slots)
                    if (At(candidateSlot, seenAtUtc) is { } candidateState &&
                        candidateState.Status.StatusId == snapshot.StatusId &&
                        candidateState.Status.Source.EntityId == snapshot.Source.EntityId)
                    {
                        slotId = candidate;
                        break;
                    }
            if (slotId is not { } index || !slots.TryGetValue(index, out var slot) || At(slot, seenAtUtc) is not { } state)
            {
                // Older captures and statuses not yet received keep their captured evidence.
                resolved.Add(snapshot);
                continue;
            }
            if (state.Status.StatusId == snapshot.StatusId &&
                state.Status.Source.EntityId == snapshot.Source.EntityId)
            {
                resolved.Add(snapshot with { RemainingTime = Remaining(state, seenAtUtc, sourceModifierWindow) });
                used.Add(index);
            }
        }
        foreach (var (index, slot) in slots)
            if (!used.Contains(index) && At(slot, seenAtUtc) is { Status.StatusId: > 0 } state &&
                DamageStatusCapturePolicy.IsRelevant(state.Status.StatusId))
                resolved.Add(state.Status with { RemainingTime = Remaining(state, seenAtUtc, sourceModifierWindow) });
        return resolved;
    }

    private static float Remaining(Version state, DateTime seenAtUtc, bool sourceModifierWindow)
    {
        var remaining = (state.EmittedExpiresAtUtc - seenAtUtc).TotalSeconds + (sourceModifierWindow ? 1 : 0);
        // Modifier consumers test > 0, while the source window includes its exact end.
        return sourceModifierWindow && remaining == 0 ? float.Epsilon : (float)remaining;
    }

    public DamageStatusApplication Resolve(DamageStatusApplication application) => application with
    {
        SourceStatuses = Resolve(application.SourceStatusActorId ?? application.Source.EntityId,
            application.SourceStatuses, application.SeenAtUtc),
        TargetStatuses = Resolve(application.Target.EntityId, application.TargetStatuses, application.SeenAtUtc),
    };

    public void Clear() => actors.Clear();

    // Acceptance uses the wire float; downstream status consumers receive centiseconds.
    private static float ReportedDuration(float duration) =>
        float.Parse(duration.ToString("0.00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static Version? At(SlotState slot, DateTime seenAtUtc)
    {
        if (slot.Current is { } current && current.SeenAtUtc <= seenAtUtc)
            return current;
        for (var i = slot.History.Count - 1; i >= 0; i--)
            if (slot.History[i] is var entry && entry.Version.SeenAtUtc <= seenAtUtc && seenAtUtc < entry.UntilUtc)
                return entry.Version;
        return null;
    }

    private sealed record Version(DamageStatusSnapshot Status, DateTime SeenAtUtc, DateTime ExpiresAtUtc,
        DateTime EmittedExpiresAtUtc, float Duration);
    private sealed class SlotState
    {
        public Version? Current;
        public DateTime LastSeenAtUtc;
        public bool RefreshPending;
        public List<(Version Version, DateTime UntilUtc)> History { get; } = [];
    }
}
