# Party Cooldown Default Loadouts

Reviewed November 12, 2026 against the installed client data version
`2026.11.03.0000.0000`. All 420 rows in `PartyCooldownActions.json` were
compared with the installed Action sheet, including IDs, names, levels,
groups, recasts, charges, eligible jobs, and selection decisions.

`PartyCooldownDefaultLoadouts` explicitly covers every job in the catalog.
Each list contains protection first, then approximately two-minute offensive
cooldowns, then approximately one-minute offensive cooldowns. The latter two
groups include 110-second and 55-second burst cycles; trait-adjusted recasts
are checked by the regression tests. Defensive recasts are not limited to
one or two minutes. Pure healing, optional crowd control, movement-only
abilities, and role casting/MP utility remain manually selectable where
supported, but are not default selections. Pneuma is included explicitly.

Protective healing buffs that strengthen shields, such as Recitation,
Seraphism, Philosophia, and Neutral Sect, belong with defensives. The
[Scholar](https://na.finalfantasyxiv.com/jobguide/scholar/) and
[Sage](https://na.finalfantasyxiv.com/jobguide/sage/) guides were used to
cross-check the shield and healing-magic interactions. Pure heals or regens
such as Second Wind, Tetragrammaton, and Aurora are not included by default.

Granted follow-ups use their parent cycle rather than misleading short
recast timers: Sun Sign uses Neutral Sect, Divine Caress uses Temperance,
Consolation uses Summon Seraph, and Tempera Grassa uses Coat. This is a
default-selection change, not new prerequisite-availability tracking.

Paladin and Dark Knight require 13 default icons; the longest reviewed list,
Blue Mage, uses 20. The selectable maximum is 24, still across at most three
custom rows. Shared-cooldown limited-job spells remain separate options;
cooldown readiness does not verify equipped spells or familiar prerequisites.

The old default action IDs and per-job counts are frozen independently of
What-ifs. A one-time configuration migration preserves existing users'
implicit selections as well as explicitly saved or empty selections. New
configurations receive the new lists. Resetting a job deliberately opts it
into the new list. Layout Default affects only that side's geometry.

The [Ninja guide](https://na.finalfantasyxiv.com/jobguide/ninja/) confirms
Adept Assassination upgrades Assassinate at level 56. Their native cooldown
groups differ, so the level-synced default has an explicit predecessor
mapping in addition to the existing shared-group upgrade handling.

Tests exercise every list through the actual catalog filters, category
ordering, exact job eligibility, parent-only follow-ups, saved-choice
migration, level sync, 24-icon drag and row limits, and all 24 timer-refresh
bits. The cached 24-entry draw copy is also checked for managed allocations.
