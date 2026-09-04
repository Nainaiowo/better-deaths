namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;
using System.Linq;

internal static class DamageMeterPreviewData
{
    private const double DurationSeconds = 1105.413;
    private static readonly DamageEncounterSnapshot Preview = Build();

    public static DamageEncounterSnapshot Create() => Preview;

    private static DamageEncounterSnapshot Build()
    {
        var startedAtUtc = new DateTime(2026, 8, 26, 23, 14, 0, DateTimeKind.Utc);
        var endedAtUtc = startedAtUtc.AddSeconds(DurationSeconds);
        var sources = new List<DamageSourceSummary>
        {
            Source(1, "Player 1", 34, 87_277_125, 2_351, 755, 721, 275, 271_206, "Tendo Setsugekka", 1,
                (36966, "Tendo Setsugekka", 8_403_696UL, 42, 271_206UL),
                (36968, "Tendo Kaeshi Setsugekka", 8_042_398UL, 42, 244_396UL),
                (7, "Attack", 7_316_936UL, 759, 17_939UL),
                (7481, "Gekko", 5_808_750UL, 124, 88_445UL)),
            Source(2, "Player 2", 22, 82_898_024, 2_199, 673, 678, 250, 286_505, "Starcross", 0,
                (36952, "Drakesbane", 10_748_010UL, 152, 148_684UL),
                (7, "Attack", 7_810_282UL, 679, 24_500UL),
                (36956, "Starcross", 6_340_601UL, 37, 286_505UL),
                (16479, "Raiden Thrust", 6_032_769UL, 146, 82_963UL)),
            Source(3, "Player 3", 27, 68_646_870, 1_551, 579, 659, 242, 271_082, "Sunflare", 0,
                (36994, "Umbral Impulse", 12_553_748UL, 105, 170_472UL),
                (36990, "Necrotize", 6_708_478UL, 74, 129_078UL),
                (25824, "Topaz Rite", 6_199_738UL, 134, 81_646UL),
                (25823, "Ruby Rite", 5_876_508UL, 68, 151_987UL)),
            Source(4, "Player 4", 23, 64_332_829, 2_857, 653, 798, 257, 322_157, "Radiant Encore", 0,
                (16495, "Burst Shot", 13_590_410UL, 465, 57_375UL),
                (7409, "Refulgent Arrow", 8_830_226UL, 213, 80_154UL),
                (0, "Shot", 6_314_829UL, 692, 19_312UL),
                (36975, "Heartbreak Shot", 6_262_863UL, 245, 50_332UL)),
            Source(5, "Player 5", 37, 58_685_511, 2_081, 623, 467, 175, 200_882, "Double Down", 0,
                (7, "Attack", 5_397_472UL, 704, 16_729UL),
                (16165, "Blasting Zone", 5_362_038UL, 70, 147_347UL),
                (25760, "Double Down", 4_177_918UL, 38, 200_882UL),
                (16150, "Wicked Talon", 3_885_114UL, 70, 103_315UL)),
            Source(6, "Player 6", 19, 56_971_182, 2_065, 655, 460, 173, 192_423, "Confiteor", 0,
                (7, "Attack", 5_274_971UL, 848, 12_970UL),
                (16459, "Confiteor", 4_206_616UL, 38, 192_423UL),
                (25750, "Blade of Valor", 4_057_486UL, 36, 185_916UL),
                (36922, "Blade of Honor", 3_945_742UL, 36, 181_732UL)),
            Source(7, "Player 7", 24, 41_323_740, 1_114, 244, 167, 49, 307_510, "Afflatus Misery", 0,
                (25859, "Glare III", 17_609_620UL, 362, 90_270UL),
                (16535, "Afflatus Misery", 8_335_246UL, 42, 307_510UL),
                (37009, "Glare IV", 7_151_814UL, 66, 166_683UL),
                (16532, "Dia", 4_134_761UL, 345, 18_381UL)),
            Source(8, "Player 8", 28, 30_328_245, 1_010, 204, 120, 48, 87_130, "Broil IV", 0,
                (25865, "Broil IV", 22_327_810UL, 503, 87_130UL),
                (16540, "Biolysis", 3_948_540UL, 337, 18_315UL),
                (37012, "Baneful Impaction", 1_168_173UL, 50, 33_329UL),
                (25866, "Art of War II", 1_149_108UL, 42, 44_642UL)),
            LimitBreak(9, 6_147_806,
                (4242, "Dragonsong Dive", 3_085_932UL, 2, 1_542_966UL),
                (7861, "Doom of Living", 3_061_874UL, 2, 1_530_937UL)),
        };
        sources = sources
            .Select((source, index) => source.Source.IsPlayer
                ? source with
                {
                    ActiveStartedAtUtc = startedAtUtc.AddSeconds(0.5 + (index * 0.1)),
                    ActiveEndedAtUtc = endedAtUtc.AddSeconds(-3.5 - (index * 0.75)),
                    ActiveDurationSeconds = DurationSeconds - 4.0 - (index * 0.85),
                }
                : source)
            .ToList();
        var total = sources.Aggregate(0UL, (sum, source) => sum + source.TotalDamage);
        return new DamageEncounterSnapshot(
            startedAtUtc,
            endedAtUtc,
            endedAtUtc,
            "Preview",
            total,
            16_161,
            0,
            [],
            sources,
            [])
        {
            ExactDamage = total,
            RaidAdjustedDamage = total,
        };
    }

    private static DamageSourceSummary Source(
        uint entityId,
        string name,
        uint classJobId,
        ulong damage,
        int hits,
        int criticalHits,
        int directHits,
        int criticalDirectHits,
        ulong maxHit,
        string maxHitName,
        int deaths,
        params (uint Id, string Name, ulong Damage, int Hits, ulong Max)[] actions)
    {
        var directEligible = Math.Max(1, hits);
        var actionRows = actions.Select(action => Action(
            action,
            criticalHits / (double)directEligible,
            directHits / (double)directEligible,
            criticalDirectHits / (double)directEligible)).ToList();
        var identity = new DamageActorIdentity(entityId, name, 0, string.Empty, true, classJobId)
        {
            IsPartyMember = true,
        };
        return new DamageSourceSummary(
            identity,
            damage,
            hits,
            hits,
            0,
            0,
            0,
            criticalHits,
            directHits,
            criticalDirectHits,
            0,
            0,
            actionRows)
        {
            RaidAdjustedDamage = damage,
            MaxHitAmount = maxHit,
            MaxHitActionName = maxHitName,
            Deaths = deaths,
        };
    }

    private static DamageSourceSummary LimitBreak(
        uint entityId,
        ulong damage,
        params (uint Id, string Name, ulong Damage, int Hits, ulong Max)[] actions)
    {
        var rows = actions.Select(action => Action(action, 0.0, 0.0, 0.0)).ToList();
        var identity = new DamageActorIdentity(entityId, "Limit Break", 0, string.Empty, false, 0)
        {
            IsLimitBreak = true,
            IsPartyMember = true,
        };
        return new DamageSourceSummary(identity, damage, 4, 4, 0, 0, 0, 0, 0, 0, 0, 0, rows)
        {
            RaidAdjustedDamage = damage,
            MaxHitAmount = 1_542_966,
            MaxHitActionName = "Dragonsong Dive",
        };
    }

    private static DamageActionSummary Action(
        (uint Id, string Name, ulong Damage, int Hits, ulong Max) action,
        double criticalRate,
        double directRate,
        double criticalDirectRate)
    {
        return new DamageActionSummary(
            action.Id,
            action.Name,
            action.Damage,
            action.Hits,
            action.Hits,
            0,
            0,
            0,
            (int)Math.Round(action.Hits * criticalRate),
            (int)Math.Round(action.Hits * directRate),
            (int)Math.Round(action.Hits * criticalDirectRate),
            0,
            0)
        {
            MaxHitAmount = action.Max,
        };
    }
}
