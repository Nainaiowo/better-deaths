namespace BetterDeaths.DamageParsing;

using System.Collections.Generic;

// Versioned numeric inputs for the meter estimator, separate from game tooltip data.
// These profiles do not change direct damage, enable excluded samples, or affect rDPS rules.
internal static class PeriodicCalibrationPotencyPolicy
{
    public const int Version = 1;

    private sealed record Profile(double Primary, double Combo, double Secondary, double SecondaryCombo);
    private static readonly IReadOnlyDictionary<uint, Profile> Direct = new Dictionary<uint, Profile>
    {
        [9] = new(220, 220, 220, 220), // fast blade
        [15] = new(170, 330, 170, 330), // riot blade
        [31] = new(240, 240, 240, 240), // heavy swing
        [37] = new(190, 340, 190, 340), // maim
        [45] = new(220, 500, 220, 500), // storm's eye
        [61] = new(420, 420, 420, 420), // twin snakes
        [64] = new(180, 180, 180, 180), // steel peak
        [75] = new(230, 230, 230, 230), // true thrust
        [78] = new(130, 280, 130, 280), // vorpal thrust
        [87] = new(140, 250, 140, 250), // disembowel
        [92] = new(320, 320, 320, 320), // jump
        [2240] = new(300, 300, 300, 300), // spinning edge
        [2242] = new(240, 400, 240, 400), // gust slash
        [2247] = new(200, 200, 200, 200), // throwing dagger
        [2265] = new(500, 500, 500, 500), // fuma shuriken
        [2267] = new(740, 740, 740, 740), // raiton
        [2271] = new(580, 580, 580, 580), // suiton
        [3539] = new(200, 460, 200, 460), // royal authority
        [3547] = new(400, 400, 400, 400), // the forbidden chakra
        [3555] = new(280, 280, 140, 140), // geirskogul
        [3558] = new(260, 260, 260, 260), // empyreal arrow
        [3562] = new(400, 400, 400, 400), // sidewinder
        [3617] = new(300, 300, 300, 300), // hard slash
        [3623] = new(240, 380, 240, 380), // syphon strike
        [3643] = new(540, 540, 540, 540), // carve and spit
        [7387] = new(420, 420, 420, 420), // upheaval
        [7391] = new(240, 240, 240, 240), // quietus
        [7392] = new(600, 600, 600, 600), // bloodspiller
        [7400] = new(720, 720, 360, 360), // nastrond
        [7409] = new(280, 280, 280, 280), // refulgent arrow
        [7411] = new(220, 220, 220, 220), // heated split shot
        [7412] = new(140, 320, 140, 320), // heated slug shot
        [7413] = new(160, 420, 160, 420), // heated clean shot
        [7477] = new(200, 200, 200, 200), // hakaze
        [7478] = new(140, 300, 140, 300), // jinpu
        [7479] = new(140, 300, 140, 300), // shifu
        [7480] = new(160, 340, 160, 340), // yukikaze
        [7505] = new(360, 360, 360, 360), // verthunder
        [7507] = new(360, 360, 360, 360), // veraero
        [7510] = new(380, 380, 380, 380), // verfire
        [7511] = new(380, 380, 380, 380), // verstone
        [7515] = new(180, 180, 180, 180), // displacement
        [7517] = new(480, 480, 480, 480), // fleche
        [7519] = new(420, 420, 420, 420), // contre sixte
        [7525] = new(650, 650, 293, 293), // verflare
        [7526] = new(650, 650, 293, 293), // verholy
        [7527] = new(340, 340, 340, 340), // enchanted riposte
        [7528] = new(190, 380, 190, 380), // enchanted zwerchhau
        [7529] = new(190, 560, 190, 560), // enchanted redoublement
        [15989] = new(220, 220, 220, 220), // cascade
        [15990] = new(120, 280, 120, 280), // fountain
        [15991] = new(280, 280, 280, 280), // reverse cascade
        [15992] = new(340, 340, 340, 340), // fountainfall
        [16005] = new(540, 540, 216, 216), // saber dance
        [16007] = new(180, 180, 180, 180), // fan dance
        [16009] = new(220, 220, 88, 88), // fan dance iii
        [16137] = new(300, 300, 300, 300), // keen edge
        [16145] = new(240, 460, 240, 460), // solid barrel
        [16146] = new(440, 440, 440, 440), // gnashing fang
        [16147] = new(500, 500, 500, 500), // savage claw
        [16150] = new(560, 560, 560, 560), // wicked talon
        [16156] = new(220, 220, 220, 220), // jugular rip
        [16157] = new(260, 260, 260, 260), // abdomen tear
        [16158] = new(300, 300, 300, 300), // eye gouge
        [16162] = new(420, 420, 420, 420), // burst strike
        [16165] = new(800, 800, 800, 800), // blasting zone
        [16460] = new(460, 460, 460, 460), // atonement
        [16468] = new(120, 160, 120, 160), // stalwart soul
        [16479] = new(320, 320, 320, 320), // raiden thrust
        [16480] = new(840, 840, 504, 504), // stardiver
        [16495] = new(220, 220, 220, 220), // burst shot
        [16497] = new(180, 180, 180, 180), // auto crossbow
        [16498] = new(650, 650, 650, 650), // drill
        [16500] = new(660, 660, 660, 660), // air anchor
        [16514] = new(580, 580, 580, 580), // fountain of fire
        [16524] = new(140, 140, 140, 140), // verthunder ii
        [16525] = new(140, 140, 140, 140), // veraero ii
        [16527] = new(180, 180, 180, 180), // engagement
        [16528] = new(420, 420, 420, 420), // enchanted reprise
        [16530] = new(750, 750, 338, 338), // scorch
        [16532] = new(85, 85, 85, 85), // dia
        [16535] = new(1400, 1400, 700, 700), // afflatus misery
        [16539] = new(165, 165, 165, 165), // art of war
        [17870] = new(240, 240, 240, 240), // ruin ii
        [24283] = new(300, 300, 300, 300), // dosis
        [24289] = new(400, 400, 280, 280), // phlegma
        [24304] = new(300, 300, 150, 150), // toxikon
        [24312] = new(380, 380, 380, 380), // dosis iii
        [24316] = new(380, 380, 190, 190), // toxikon ii
        [24318] = new(380, 380, 228, 228), // pneuma attack
        [24373] = new(420, 420, 420, 420), // slice
        [24374] = new(260, 500, 260, 500), // waxing slice
        [24375] = new(280, 600, 280, 600), // infernal slice
        [24376] = new(140, 140, 140, 140), // spinning scythe
        [24377] = new(120, 180, 120, 180), // nightmare scythe
        [24378] = new(300, 300, 300, 300), // shadow of death
        [24379] = new(100, 100, 100, 100), // whorl of death
        [24380] = new(520, 520, 520, 520), // soul slice
        [24386] = new(300, 300, 300, 300), // harpe
        [24388] = new(800, 800, 480, 480), // harvest moon
        [24390] = new(440, 440, 440, 440), // unveiled gibbet
        [24391] = new(440, 440, 440, 440), // unveiled gallows
        [24399] = new(280, 280, 280, 280), // lemure's slice
        [25759] = new(180, 180, 180, 180), // hypervelocity
        [25769] = new(1500, 1500, 975, 975), // phantom rush
        [25771] = new(160, 460, 160, 460), // heavens' thrust
        [25773] = new(440, 440, 220, 220), // wyrmwind thrust
        [25777] = new(700, 700, 700, 700), // forked raiju
        [25778] = new(700, 700, 700, 700), // fleeting raiju
        [25781] = new(1000, 1000, 600, 600), // ogi namikiri
        [25782] = new(1000, 1000, 600, 600), // kaeshi: namikiri
        [25788] = new(660, 660, 465, 465), // chain saw
        [25791] = new(460, 460, 184, 184), // fan dance iv
        [25820] = new(500, 500, 500, 500), // astral impulse
        [25823] = new(620, 620, 620, 620), // ruby rite
        [25824] = new(340, 340, 340, 340), // topaz rite
        [25825] = new(280, 280, 280, 280), // emerald rite
        [25835] = new(560, 560, 224, 224), // crimson cyclone
        [25836] = new(160, 160, 64, 64), // mountain buster
        [25837] = new(520, 520, 208, 208), // slipstream
        [25855] = new(440, 440, 440, 440), // verthunder iii
        [25856] = new(440, 440, 440, 440), // veraero iii
        [25858] = new(850, 850, 383, 383), // resolution
        [25859] = new(350, 350, 350, 350), // glare iii
        [25865] = new(320, 320, 320, 320), // broil iv
        [25871] = new(270, 270, 270, 270), // fall malific
        [25872] = new(140, 140, 140, 140), // gravity ii
        [25874] = new(270, 270, 150, 150), // macrocosmos
        [25882] = new(600, 600, 390, 390), // flint strike
        [25885] = new(560, 560, 224, 224), // crimson strike
        [34608] = new(300, 300, 300, 300), // hunter's sting
        [34609] = new(300, 300, 300, 300), // swiftskin's sting
        [34623] = new(260, 260, 260, 260), // vicepit
        [34626] = new(750, 750, 188, 188), // reawaken
        [36918] = new(500, 500, 500, 500), // supplication
        [36919] = new(540, 540, 540, 540), // sepulchre
        [36937] = new(800, 800, 480, 480), // reign of beasts
        [36938] = new(900, 900, 540, 540), // noble blood
        [36952] = new(460, 460, 460, 460), // drakesbane
        [36978] = new(240, 240, 240, 240), // blazing shot
        [36998] = new(1400, 1400, 560, 560), // enkindle solar bahamut
        [37005] = new(950, 950, 428, 428), // vice of thorns
    };

    public static double GetDirectPotency(ParsedDamageEvent damageEvent)
    {
        var source = damageEvent.AttributedSource ?? damageEvent.Source;
        if (source.Level == 0 || !Direct.TryGetValue(damageEvent.ActionId, out var profile))
            return damageEvent.DirectPotency!.Value;

        var combo = damageEvent.RawEffectType == 3 && damageEvent.RawParam2 != 0;
        var potency = (damageEvent.IsSecondaryTarget ?? damageEvent.TargetIndex > 0)
            ? combo ? profile.SecondaryCombo : profile.Secondary
            : combo ? profile.Combo : profile.Primary;
        if (source.ClassJobId == 22 && source.Level < 76)
            potency -= damageEvent.ActionId switch { 75 => 80, 78 => 35, 87 => 45, _ => 0 };
        if (source.ClassJobId == 28 && damageEvent.ActionId == 17870 && source.Level >= 54)
            potency = source.Level >= 82 ? 220 : source.Level >= 72 ? 200 : source.Level >= 64 ? 180 : 160;
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
