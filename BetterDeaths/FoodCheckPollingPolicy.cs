using System;

namespace BetterDeaths;

internal static class FoodCheckPollingPolicy
{
    public const double IntervalSeconds = 1.0;
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(IntervalSeconds);

    public static bool ShouldPoll(bool enabled, bool loggedIn, bool inInstance, bool showOutsideInstances,
        bool inCombat, bool isPvP) =>
        enabled && loggedIn && (inInstance || showOutsideInstances) && !inCombat && !isPvP;
}
