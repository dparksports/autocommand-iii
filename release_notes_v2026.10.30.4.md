# AutoCommand III — v2026.10.30.4

**Block gates** for AI agents: a time-boxed unblock the user confirms for
one specific target, which closes itself.

## The agent flow

Three new MCP tools:

- **`open_gate(target, minutes, reason?, dry_run?)`** — temporarily stop
  blocking ONE specific blocked IP or process image for **1, 10, 30 or 60
  minutes** (the agent picks the shortest window its task needs). The user
  confirms the exact target in AutoCommand — the dialog shows the target,
  the rule count, the wall-clock auto-close time, and the agent's reason.
  Blocking returns ON automatically when the window ends. `dry_run` previews
  the resolution without asking anyone.
- **`close_gate(target)`** — close early, blocking back ON immediately.
  The tool descriptions teach this as good citizenship: every minute a gate
  is open is a minute its target is unblocked.
- **`list_gates()`** — what's open, remaining minutes, requester, reason —
  check before requesting; `list_blocks` rows now carry
  `gate_minutes_remaining` too.

**Specificity is enforced, not requested**: an ambiguous name (40 blocked
svchost targets) returns the candidate list with an error telling the agent
to be precise — it never acts. Unknown targets and invalid durations return
teaching errors. Re-opening an existing gate extends it after a fresh
confirmation; gates confirm every time (the "always for this session" grant
does not apply to them).

## Safety properties

- **gates.json is written synchronously before blocking turns off** — the
  one store that does not ride the managed write buffers, because a lost
  gate record would mean protection stays down.
- **App exit closes every open gate** (a dead app never leaves a door
  open); startup closes anything that expired while it was down, and the
  10-second `gate-watch` instrument (calibratable like every instrument)
  closes gates as their windows end.
- Only `AC-BLOCK-*` rules AutoCommand itself created are ever touched;
  every transition lands in `blocks-events.log` (`mcp`, `gate-expiry`,
  `gate-exit`), and the Blocks tab shows live gate state on the row
  ("gate 8m · mcp") with the auto-close time on hover.

## Also in this release train

Managed buffer writes for every periodic disk writer, the instrumentation
metronome with user-calibrated cadences, the Blocks master-detail redesign
with blocking ON/OFF wording, the preview-window crash fix, and the
dead-code sweep (v2026.10.30.1–.3).
