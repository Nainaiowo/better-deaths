namespace BetterDeaths.DamageParsing;

// Versioned numeric inputs for the meter estimator, separate from game tooltip data.
// These profiles affect estimator calibration only, never captured direct damage.
internal static class PeriodicCalibrationPotencyPolicy
{
    public const int Version = 2;

    public static double GetDirectPotency(ParsedDamageEvent damageEvent)
    {
        var source = damageEvent.AttributedSource ?? damageEvent.Source;
        if (source.Level == 0 || !PeriodicCalibrationCatalog.Contains(damageEvent.ActionId))
            return damageEvent.DirectPotency.GetValueOrDefault();

        var combo = damageEvent.RawEffectType == 3 && damageEvent.RawParam2 != 0;
        var secondary = damageEvent.IsSecondaryTarget ?? damageEvent.TargetIndex > 0;
        var potency = PeriodicCalibrationCatalog.GetPotency(damageEvent.ActionId, combo, secondary);
        if (potency <= 0)
            return 0;
        if (damageEvent.ActionId == 3584)
            potency += source.Level >= 72 ? 50 : source.Level >= 64 ? 20 : 0;
        if (source.ClassJobId == 28 && damageEvent.ActionId == 17870 && source.Level >= 54)
            potency = source.Level >= 82 ? 220 : source.Level >= 72 ? 200 : source.Level >= 64 ? 180 : 160;
        if (damageEvent.ActionId == 16511 && source.Level < 76)
            potency = 70;
        if (source.ClassJobId == 22 && source.Level < 76)
            potency -= damageEvent.ActionId switch { 75 => 80, 78 => 35, 87 => 45, _ => 0 };
        return potency;
    }

    public static double GetPeriodicPotency(DamageStatusApplication application)
    {
        if (application.Source.Level == 0)
            return application.StatusId == 0xA38 ? 85.0 : application.PeriodicPotency!.Value;
        // The shared status alone cannot distinguish Song of Torment from Nightbloom.
        if (application.StatusId == 1714 && application.ActionId != 23290)
            return application.PeriodicPotency!.Value;
        return application.StatusId switch
        {
            18 => 20, // Bad Breath
            118 => 40, // chaos thrust
            124 => 15, // venomous bite
            129 => 20, // windbite
            143 => 30, // aero
            144 => 50, // aero ii
            161 => 45, // thunder
            162 => 30, // thunder ii
            163 => 50, // thunder iii
            179 => 20, // bio
            189 => 40, // bio ii
            236 => 20, // choco beak
            248 => 30, // circle of scorn
            838 => 50, // combust
            843 => 60, // combust ii
            1200 => 20, // caustic bite
            1201 => 25, // stormbite
            1210 => 35, // thunder iv
            1228 => 50, // higanbana
            1714 => 75, // nightbloom
            1723 => 40, // feather rain
            1736 => 20, // dropsy
            1837 => 120, // sonic break
            1838 => 60, // bow shock
            1866 => 50, // bioblaster
            1871 => 85, // dia
            1881 => 70, // combust iii
            1895 => 85, // biolysis
            2440 => 350, // lost flare star
            2499 => 50, // incendiary burns
            2614 => 40, // eukrasian dosis
            2615 => 60, // eukrasian dosis ii
            2616 => 85, // eukrasian dosis iii
            2719 => 45, // chaotic spring
            3636 => 10, // begrimed
            3643 => 40, // mortal flame
            3712 => 120, // breath of magic
            3871 => 60, // high thunder
            3872 => 40, // high thunder ii
            3883 => 140, // baneful impaction
            3897 => 40, // eukrasian dyskrasia
            _ => application.PeriodicPotency!.Value,
        };
    }
}
