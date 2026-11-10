# P4 Captured Signals

`P4CapturedSignals.json` retains the P4 timing and encoded Neo Exdeath tells
from saved pulls 2324, 2326, 2330, and 2355. The cast times are those produced
by the local event adapter during the November 10, 2026 audit. The repeated
observations of unchanged tells are deliberately retained.

The expected Grand Cross and Flood calls were checked against the captured
tell values: 1121 is fake and 1122 is real. Do not regenerate those expected
results from the analyzer under test. Tests use canonical actor identities
and no player names, account identifiers, or credentials.

The integration test feeds the observations through `LocalPullEventSource`
and then `P4Analyzer`, including a helper actor for the Antilight cast. The
fixture protects saved-pull compatibility, repeated observations, separate
same-valued tells, and cast-time pairing rather than array-position pairing.

The wound tests also retain pull 2330's unchanged-wound sequence: status 5541
at 779.42285s, Flood at 790.10223s, and removal at 790.9034s. Wound variants
4887/4888 and 5541/5542 must not be treated as fixed initial/final phases.
Resolution is determined from observed mechanic events and wound history,
including an unchanged wound that persisted through the check. A death
clearing the wound, or expiry before Flood, does not prove a successful
resolution.
