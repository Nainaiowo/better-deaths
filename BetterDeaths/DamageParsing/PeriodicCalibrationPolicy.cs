namespace BetterDeaths.DamageParsing;

using System.Collections.Generic;
using System.Linq;

// Calibration inputs for periodic estimates; captured direct damage stays unchanged.
internal static class PeriodicCalibrationPolicy
{
    // Fixed diagnostic inputs, deliberately separate from current game tooltips.
    // Unknown and variable healing actions cannot supply a calibration value.
    public static double? HealingPotency(uint actionId) => actionId switch
    {
        24292 or 37034 => 100,
        825 => 150,
        21492 or 24283 or 24289 or 24293 or 24297 or 24304 or 24306 or 24307 or
            24312 or 24313 or 24314 or 24315 or 24316 or 24318 or 37032 => 170,
        802 or 16548 => 180,
        186 or 16139 or 16553 or 16556 or 17151 or 17152 or 37013 => 200,
        37016 => 240,
        42 or 133 or 3595 or 3601 or 16546 or 16547 or 37010 or 37030 => 250,
        185 or 827 or 3632 or 7388 or 16015 or 23416 or 24286 or 24291 or 24310 or 36944 => 300,
        16543 or 16544 => 320,
        7514 => 350,
        37015 => 360,
        124 or 3540 or 3571 or 3583 or 3600 or 7384 or 7445 or 16230 or 16458 or 16459 or
            16534 or 23272 or 24299 or 25748 or 25749 or 25750 or 25830 or 25863 or 34681 => 400,
        190 or 24284 => 450,
        120 or 3594 or 3641 or 3643 or 36997 => 500,
        131 or 189 or 24296 or 27524 or 34566 => 600,
        3570 or 24303 => 700,
        135 or 3610 or 7541 or 16531 => 800,
        3552 => 1200,
        _ => null,
    };

    public static bool ExcludesCriticalSample(uint actionId,
        IReadOnlyList<DamageStatusSnapshot> source, IReadOnlyList<DamageStatusSnapshot> target,
        IReadOnlyList<DamageStatusSnapshot>? timedSource = null)
    {
        if (actionId is 16465 or 16463 or 25673 or 53 or 25767 or 2246 or 16486 or 25781 or 25782 or 36982 ||
            target.Any(status => status.StatusId == 0x4C5) ||
            actionId != 7 && source.Any(status => status.StatusId == 0x74) ||
            actionId != 8 && source.Any(status => status.StatusId == 0x353))
        {
            return true;
        }
        // A consumed guarantee belongs to the hit above; chance buffs use accepted expiry.
        return (timedSource ?? source).Any(status => status.RemainingTime >= 1 && (status.StatusId switch
        {
            2216 or 2125 or 1825 or 786 or 851 or 86 => true,
            1177 => actionId is 3549 or 3550,
            _ => false,
        }));
    }

    public static bool IsRelevantStatus(uint id) => id is
        1884 or 1892 or 2716 or 1202 or 2126 or 2498 or 238 or 49 or 42 or 43 or 44 or
        102 or 172 or 591 or 697 or 933 or 2707 or 2611 or 2620 or 2621 or 2622 or
        3898 or 791 or 2710 or 317 or 1875 or 87 or 1912 or 1872 or
        2216 or 2125 or 1825 or 786 or 851 or 86 or 1177 or 0x74 or 0x4C5;

    public static double CriticalBuffRate(IReadOnlyList<DamageStatusSnapshot> sourceStatuses,
        IReadOnlyList<DamageStatusSnapshot> targetStatuses)
    {
        var rate = sourceStatuses.Where(status => status.RemainingTime >= 1)
            .DistinctBy(status => (status.StatusId, status.Source.EntityId))
            .Sum(status => status.StatusId switch
            {
                2216 => 0.02,
                786 => 0.10,
                2125 or 1825 => 0.20,
                851 or 86 => 1.0,
                _ => 0.0,
            });
        if (targetStatuses.Any(status => status.StatusId == 0x4C5)) rate += 0.10;
        if (sourceStatuses.Any(status => status.StatusId is 0x74 or 0x353)) return 1.0;
        return rate;
    }

    public static double? HealingMultiplier(uint category, IReadOnlyList<DamageStatusSnapshot> source,
        IReadOnlyList<DamageStatusSnapshot> target)
    {
        // These need encounter-specific or encoded values that are not captured.
        if (source.Any(status => status.StatusId is 42 or 697) || target.Any(status => status.StatusId == 591))
        {
            return null;
        }
        var multiplier = 1.0;
        var brink = source.Any(status => status.StatusId == 44);
        foreach (var status in source.DistinctBy(status => status.StatusId))
        {
            multiplier += status.StatusId switch
            {
                49 => 0.10,
                43 => brink ? 0 : -0.25,
                44 => -0.50,
                _ when category is not (2 or 4) => 0,
                1892 or 2126 => 0.20,
                2716 => 0.05,
                2498 => 1.0,
                238 => 0.30,
                933 => -0.30,
                2611 => 0.50,
                3898 or 791 or 1872 when category == 2 => 0.20,
                317 when category == 2 => 0.10,
                1875 => 0.10,
                _ => 0,
            };
        }
        if (category is 2 or 4)
        {
            foreach (var status in target.DistinctBy(status => status.StatusId))
            {
                multiplier += status.StatusId switch
                {
                    1884 or 102 or 2620 or 2621 or 2710 or 1912 => 0.10,
                    1202 => 0.15,
                    172 => -0.50,
                    2707 => 0.05,
                    2622 or 87 => 0.20,
                    _ => 0,
                };
            }
        }
        return multiplier > 0 ? multiplier : null;
    }
}
