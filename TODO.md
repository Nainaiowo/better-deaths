# Better Deaths TODO

## Current Build

- [x] Create separate Better Deaths plugin.
- [x] Capture party death transitions.
- [x] Record recent action effects targeting party members.
- [x] Show current pull and recorded pull groups.
- [x] Show ordered death timeline.
- [x] Show fatal events, active statuses, and recent action timeline per death.

## Known Gaps To Test

- [ ] Confirm HP snapshots update at the right time after action effects.
- [ ] Revisit capture hook parity with Death Recap: compare ActionEffect, ActorControl death, EffectResult, and combat-log ordering for multi-hit actions where ActionEffect timestamps land before the final HP transition.
- [ ] Confirm fatal event timing against real wipe logs.
- [ ] Decide whether to capture non-party players.
- [ ] Add richer status grouping for mitigation, vuln, doom/death-style debuffs, and raid-specific mechanics.
- [ ] Add optional chat notification/link after a death.
- [ ] Add optional exports for sharing wipe review.

## Capture Hook Notes To Revisit

- Pull 169 showed a BLM death where the last ActionEffect row displayed only one Cyclone hit, but the combat-log confirmations around death contained the lethal tail.
- Death Recap buffers combat events per actor and stores the full recent combat list when ActorControl death arrives. Compare that approach with our ActionEffect, ActorControl death, EffectResult, and combat-log confirmation flow before changing capture hooks.
- Keep combat-log confirmations useful as display fallback, but do not treat unmatched tiny/extra log lines as fatal hits unless they match ActionEffect context or the capture hook is made more authoritative.
