# Tempera Cooldown Captures

The companion JSON contains anonymized excerpts from the local
`Network_30304_20261106.log` capture. Player identifiers and names are omitted.
Times are relative to each Coat cast. Status IDs, action sequences, damage-result
HP, and the source windows are retained so each excerpt can be checked again.

`Remaining` is reconstructed from the preceding ten-second status application.
`ServerFrame` numbers are normalized correlation groups, not original packet IDs.
These fixtures verify the state machine; they do not replace an in-game check of
the native hook integration.

Positive cases carry the damage-result HP and sequence into the removal context.
This represents the causal context required at runtime: an explicit status-slot
clear must be present in that damage result, and the native handler must apply
it. Matching log timestamps alone never authorize a runtime refund.

The result reader's separate tests use synthetic binary packets, not captured
packet bytes. Their layout was verified against the installed October 20 game
executable: a four-byte batch header, 0x58-byte records, and four possible
16-byte slot updates per record. The production reader checks the touched native
slots before and after the existing result handler. Tempera does not require
additional global SetStatus/RemoveStatus hooks or a server-frame timestamp hook.

The reviewed file contains 93 self-shield removals: 40 conversions, one expiry,
and 52 other early removals. Of the latter, 51 have a matching nonlethal damage
sequence/result at the logged removal time. One has a one-millisecond timing
difference and is not used as positive frame-correlation evidence.

No self-Tempera removal on death was found in the available November logs.
Death, reordered notifications, missing evidence and duplicate packets are
therefore covered by explicit synthetic regression tests rather than labelled
as captured examples.
