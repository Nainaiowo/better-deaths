# Tempera Status Update Replay

The companion fixture retains the shield-related fields from the local November
7 capture, including two separate teammate tests at 20:35 and 20:37 Eastern time.
Names are omitted and the shield owner is consistently replaced with 10000001.
Unrelated status entries are omitted; their slots are zeroed in the replay.

These are captured log field values, not verbatim packet bytes. Tests reconstruct
the native layouts from them: two 30-entry halves of 12-byte statuses, and the
existing 0x58-byte effect-result layout. Duration words retain their original IEEE
754 bits. The result log's packed slot/status word is unpacked into the native
slot byte and status-ID ushort. Native countdown time between updates is simulated
from the preceding captured duration rather than described as captured memory.

The first sequence applies Coat in slot 30 through a status list, then clears it
in the matched nonlethal damage result. The second replaces Coat with Grassa in
slot 31, refreshes Grassa in a result, and clears it following a damage action
whose HP damage is zero. The tests feed the real readers into the cooldown model
and displayed timer; they do not inject a synthetic Applied event into the model.

The new native entry points were separately checked against the installed game
executable, whose file is dated October 20, 2026. Each signature matched once:
RVA 008A78F0 receives a manager and a 30-status buffer; RVA 008A7980 receives a
manager and two 30-status buffers. Both write the slots through SetStatus and
return void. Production capture compares native slots before and after the
original handler. Their runtime loading still requires an in-game check.
