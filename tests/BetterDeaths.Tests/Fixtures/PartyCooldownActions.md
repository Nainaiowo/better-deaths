# Party Cooldown Coverage Audit

Reviewed 2026-11-07 and rechecked 2026-11-08 against installed client data version `2026.10.16.0000.0000`.

## Evidence and Scope

`PartyCooldownActions.json` contains 420 unique Action-sheet rows: current PvE player actions and explicitly recognized transformed executions, with a nonzero independent cooldown group other than shared-GCD group 58, a positive recast, a name/icon, an acquisition level, and an eligible job category. Base recasts and charges are game data, not potency estimates. Role actions are associated with every eligible job. Class IDs normalize to their corresponding jobs.

The audit covers all 23 jobs in the catalog, including both limited jobs. ActionTransient descriptions were inspected for prerequisite-only buttons and resets. The official [job guide index](https://na.finalfantasyxiv.com/jobguide/battle/) and job guides were used as a cross-check, including [Dark Knight](https://na.finalfantasyxiv.com/jobguide/darkknight/), [Pictomancer](https://na.finalfantasyxiv.com/jobguide/pictomancer/), [Machinist](https://na.finalfantasyxiv.com/jobguide/machinist/), and [Beastmaster](https://na.finalfantasyxiv.com/jobguide/beastmaster/).

The JSON's Included, Defensive, Variant, and Unsupported values freeze the reviewed selection decisions. They are regression expectations, not independent proof that native capture works. The snapshot must be regenerated and reviewed after a game-data update; it cannot detect new actions in a future client by itself.

The repeat audit explicitly accounts for all 420 candidates: each is included or has a reviewed exclusion in `PartyCooldownCoverageTests`. Optional role control abilities are now included: Interject, Low Blow, Head Graze, Foot Graze, Leg Graze, and Leg Sweep. Their actual game rows are tested through selection and timer start; no existing user selection or default is expanded automatically. These are control tools, not a promise that a boss is interruptible or susceptible to a status.

Granted defensive follow-ups are represented by their parent cycle rather than false one-second availability: Divine Caress follows Temperance, Sun Sign follows Neutral Sect, Earth's Reply follows Riddle of Earth, and Tempera Grassa follows Coat. The official [White Mage](https://na.finalfantasyxiv.com/jobguide/whitemage/) and [Astrologian](https://na.finalfantasyxiv.com/jobguide/astrologian/) guides confirm that Caress and Sun Sign require their granting effects. Ordinary shared-GCD shield/healing spells are not independent cooldowns; this remains a cooldown tracker, not a complete hotbar-readiness tracker.

The saved network log also confirms an owner-sourced Whispering Dawn command (action 16537) before the pet's execution (action 803), on 2026-11-04 at 03:58:16-17 local time. This supports retaining the existing owner-command capture path rather than starting the cooldown a second time from pet effects.

## Job Review

Existing burst and defensive choices remain in scope. This table highlights expanded choices or reviewed exceptions, not the full final ability list. Every job has explicit offensive/support and defensive coverage tests against the game-row snapshot.

| Job | Expanded choices or reviewed behavior |
| --- | --- |
| Paladin | Circle of Scorn, Spirits Within/Expiacion, Intervene; existing burst, shields, invulnerability, and party mitigation retained. |
| Warrior | Onslaught, Upheaval/Orogeny; Infuriate is visible but disabled because attacks reduce its recharge. |
| Dark Knight | Salted Earth, Abyssal Drain, Carve and Spit, Shadowbringer. Salt and Darkness is a consumed Salted Earth follow-up, not a reusable 20-second damage window. |
| Gunbreaker | Danger/Blasting Zone, Gnashing Fang, Bow Shock, Bloodfest; existing burst, shields, and invulnerability retained. |
| White Mage | Assize, Tetragrammaton, Thin Air. Divine Caress is granted by Temperance, not an independent one-second shield cooldown. |
| Scholar | Whispering Dawn, Indomitability, Excogitation, Fey Blessing. Emergency Tactics is visible but disabled because Seraphism changes its recast. |
| Astrologian | Essential Dignity and Celestial Opposition. Draw remains the timer for the card cycle; individual card plays are not new one-second cooldowns. |
| Sage | Phlegma and its upgrades, Psyche, Ixochole, Pepsis; existing shields, mitigation, healing buffs, and major recovery retained. |
| Monk | Perfect Balance and positional support; chakra spenders remain resource-gated actions rather than reusable cooldowns. |
| Dragoon | Life Surge, Jump/High Jump, Stardiver; Wyrmwind Thrust remains a resource spender. |
| Ninja | Assassinate, Dream Within a Dream. Mudra choices explain why Ninjutsu-specific recharge tracking is unavailable. |
| Samurai | Meditate and damaging movement cooldowns Gyoten/Yaten; existing major burst, Meikyo, Third Eye/Tengentsu retained. One-second Kenki spenders and granted follow-ups are not independent choices. |
| Reaper | Soul Slice/Soul Scythe. Enshroud and Soul Reaver spenders are resource actions, not recurring five-second windows. |
| Viper | Vicewinder/Vicepit; Serpent's Ire and role defenses retained. Granted tail/fang/blood follow-ups are not independent cooldowns. |
| Bard | Empyreal Arrow, Sidewinder. Bloodletter/Rain of Death/Heartbreak Shot explain unsupported proc-based reductions. |
| Machinist | Hot Shot/Air Anchor, Drill/Bioblaster, Flamethrower. Gauss/Ricochet and upgraded variants remain disabled until Overheated reductions are tracked. Queen/Rook are battery summons, not six-second reusable cooldowns. |
| Dancer | Standard Step; Finishing Move must update the same shared cooldown. Existing dance burst, mitigation, and healing retained. |
| Black Mage | Triplecast and casting/MP support; existing Ley Lines, Manafont, Amplifier, and Manaward retained. |
| Summoner | Energy Drain/Siphon, Enkindle and its transformed executions, Lux Solaris. Timer readiness does not prove a summon phase or prerequisite is active. |
| Red Mage | Corps-a-corps, Displacement/Engagement, Fleche, Contre Sixte, Acceleration; existing raid buff and defensive choices retained. |
| Pictomancer | Living Muse and all four creature executions; Mog of the Ages and Madeen share their parent timer. Tempera Grassa remains conversion evidence for Coat, not a separate mitigation recast. |
| Blue Mage | Feather Rain/Eruption, Surpanakha, Rose of Destruction/Ruby Dynamics. Shared recasts remain shared. Winged Reprobation is disabled because its first casts reset its own timer. Shared-GCD defensive spells do not become independent timers. |
| Beastmaster | Seedsower, Cloud Skim, Scouring Ash, Soul Crush, Shield Charge, Parting Blow. Familiar-reset abilities remain disabled; summons whose cooldown starts on retreat are not ordinary on-use timers. |

Casting/MP recovery and positional support are optional choices. Pure movement without damage or defense, enmity tools, routine shared-GCD attacks, and resource-only spenders remain outside the chooser. A ready cooldown never promises enough resources, an active motif/summon/buff, a target in range, or an equipped limited-job spell.

## Regression Checks

- `PartyCooldownCoverageTests`: every job's reviewed damage/support and defensive selections survive the real catalog's group, level, and short-recast filters; conditional choices remain visible but unselectable.
- Every candidate row has an inclusion or explicit exclusion decision, preventing a refreshed snapshot from silently admitting an unaudited omission.
- `PartyCooldownSelectionPolicyTests`: newly admitted transformed executions canonicalize to one tile; actual execution IDs remain capturable. Short, explicitly reviewed independent defensives and damaging movement abilities are not rejected as shared GCDs.
- Actual snapshot rows exercise Finishing Move, creature Muses, Madeen, and transformed Enkindles through catalog lookup and the existing shared-group tracker, verifying timer and charge changes without duplicate tiles.
- `OnboardingGuideTests`: all cooldown lessons belong to HUD, remain in selection/layout/preview order, and route to the Party Cooldowns view. Preview wording explicitly identifies sample data.

Selections remain limited to 12 visible icons per job. New choices do not replace the user's saved selections. Defaults, duty-reset rules, death capture, What-if calculations, and damage calculations are not expanded by this audit.

In-game visual walkthrough checks remain separate from these offline tests. The user completed a 4m25s performance check and reported smooth play; the accepted combined mean was 0.0219 ms per update. Remote recasts can still be affected by gear/status modifiers or missing observations. This is not a claim of 100% live readiness detection or support for the explicitly disabled conditional actions.
