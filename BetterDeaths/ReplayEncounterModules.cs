namespace BetterDeaths;

using System.Collections.Generic;
using System.Linq;

internal readonly record struct ReplayMarkerInfo(string ShortLabel, string Description);

internal interface IReplayEncounterModule
{
    string Name { get; }

    bool AppliesTo(uint territoryId);

    bool IsReplayOverheadStatus(uint statusId);

    bool TryGetMarkerInfo(uint markerId, out ReplayMarkerInfo info);

    IReadOnlyList<ReplayMechanicSnapshot> GetReplayMechanics(PartyDeathRecord death);
}

internal static class ReplayEncounterModules
{
    private static readonly IReplayEncounterModule FallbackModule = new GenericReplayEncounterModule();
    private static readonly IReadOnlyList<IReplayEncounterModule> Modules =
    [
        new DmuReplayEncounterModule(),
    ];

    public static IReplayEncounterModule Get(uint territoryId)
    {
        return Modules.FirstOrDefault(module => module.AppliesTo(territoryId)) ?? FallbackModule;
    }

    public static bool IsReplayOverheadStatus(uint territoryId, uint statusId)
    {
        return Get(territoryId).IsReplayOverheadStatus(statusId);
    }

    private sealed class GenericReplayEncounterModule : IReplayEncounterModule
    {
        public string Name => "Generic";

        public bool AppliesTo(uint territoryId) => true;

        public bool IsReplayOverheadStatus(uint statusId) => false;

        public bool TryGetMarkerInfo(uint markerId, out ReplayMarkerInfo info)
        {
            if (TryGetNumber(markerId, out var number))
            {
                info = new ReplayMarkerInfo(number.ToString(), $"Number {number}");
                return true;
            }

            info = default;
            return false;
        }

        public IReadOnlyList<ReplayMechanicSnapshot> GetReplayMechanics(PartyDeathRecord death)
        {
            return death.ReplayMechanics;
        }
    }

    private sealed class DmuReplayEncounterModule : IReplayEncounterModule
    {
        private const uint TerritoryDancingMadUltimate = 1363;

        public string Name => "Dancing Mad Ultimate";

        public bool AppliesTo(uint territoryId) => territoryId == TerritoryDancingMadUltimate;

        public bool IsReplayOverheadStatus(uint statusId)
        {
            return statusId is 3004 or 3005 or 3006 or
                5084 or 5085 or 5086;
        }

        public bool TryGetMarkerInfo(uint markerId, out ReplayMarkerInfo info)
        {
            info = markerId switch
            {
                127 => new ReplayMarkerInfo("Spread", "Fire spread"),
                128 => new ReplayMarkerInfo("Stack", "Fire stack"),
                161 => new ReplayMarkerInfo("Stack", "Final stack"),
                715 => new ReplayMarkerInfo("Stack", "Forsaken stack"),
                716 => new ReplayMarkerInfo("Spread", "Forsaken spread"),
                717 => new ReplayMarkerInfo("Cone", "Forsaken cone"),
                3004 => new ReplayMarkerInfo("1st", "First in line"),
                3005 => new ReplayMarkerInfo("2nd", "Second in line"),
                3006 => new ReplayMarkerInfo("3rd", "Third in line"),
                5084 => new ReplayMarkerInfo("Stack", "Head stack"),
                5085 => new ReplayMarkerInfo("Circle", "Circle"),
                5086 => new ReplayMarkerInfo("Fan", "Fan"),
                _ => default,
            };
            return !string.IsNullOrEmpty(info.ShortLabel) ||
                !string.IsNullOrEmpty(info.Description) ||
                FallbackModule.TryGetMarkerInfo(markerId, out info);
        }

        public IReadOnlyList<ReplayMechanicSnapshot> GetReplayMechanics(PartyDeathRecord death)
        {
            return death.ReplayMechanics;
        }
    }

    private static bool TryGetNumber(uint markerId, out int number)
    {
        if (markerId is >= 79 and <= 86)
        {
            number = (int)markerId - 78;
            return true;
        }

        number = markerId switch
        {
            336 => 1,
            337 => 2,
            338 => 3,
            339 => 4,
            437 => 5,
            438 => 6,
            439 => 7,
            440 => 8,
            _ => 0,
        };

        return number > 0;
    }
}
