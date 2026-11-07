using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths.PartyCooldowns;

internal sealed class PartyCooldownLifecycle
{
    private readonly Dictionary<(ulong Member, byte Group), uint> observedTerritories = [];
    private uint? pendingEntry;
    private uint? currentTerritory;

    public bool HasPendingEntry => pendingEntry is not null;
    public void BeginDuty(uint territory)
    {
        EnterTerritory(territory);
        pendingEntry = territory;
    }
    public void EndEntry() => pendingEntry = null;

    public void EnterTerritory(uint territory)
    {
        if (currentTerritory == territory)
            return;
        currentTerritory = territory;
        // Only preparation in this visit can protect a timer from a duty-entry reset.
        observedTerritories.Clear();
    }

    public void Observe(ulong member, byte group, uint territory)
    {
        if (currentTerritory is null)
            EnterTerritory(territory);
        if (currentTerritory == territory)
            observedTerritories[(member, group)] = territory;
    }

    public bool CanConfirmReadyAtEntry(ulong member, byte group, uint currentTerritory) =>
        pendingEntry == currentTerritory && observedTerritories.GetValueOrDefault((member, group)) != currentTerritory;

    public void RetainMembers(IReadOnlySet<ulong> members)
    {
        foreach (var key in observedTerritories.Keys.Where(key => !members.Contains(key.Member)).ToArray())
            observedTerritories.Remove(key);
    }

    public void Clear()
    {
        observedTerritories.Clear();
        pendingEntry = null;
        currentTerritory = null;
    }
}
