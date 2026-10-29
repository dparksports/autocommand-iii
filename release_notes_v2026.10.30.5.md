# AutoCommand III — v2026.10.30.5

Completes the **block-gate** service (introduced in v2026.10.30.4): gate
lifecycle is now agent-visible everywhere the bridge records state.

## What's new

- **Gate lifecycle in the bridge's activity log** — the agent-facing record
  (Be My Guest tab, offline agents): `opened` (target, duration, reason) at
  the user's confirmation, `closed (by agent)` on an early close, and a
  startup subscription logs `closed (gate-expiry)` / `closed (gate-exit)` —
  the two closes nobody is around to watch. A window ending is visible even
  if the requesting agent and its client are long gone.
- **The periodic monitor snapshot (`feed.jsonl`) carries `open_gates`** —
  target, remaining minutes, closes-at, requesting client — so feed-only
  consumers see which targets are temporarily unblocked at every snapshot,
  no tool call needed.

## The complete gate capability (v2026.10.30.4 + this)

Agents resolve and preview one specific blocked target (`dry_run`), request
a 1/10/30/60-minute unblock with a reason; the user confirms the exact
target and wall-clock auto-close; blocking returns ON by itself. Gates
persist across restarts (synchronously, before blocking turns off), close
on app exit and expiry via the calibratable `gate-watch` instrument, show
live state on Blocks tab rows ("gate 8m · mcp"), and are exposed through
`open_gate` / `close_gate` / `list_gates`, the `gate_minutes_remaining`
field on every `list_blocks` row, the activity log, and now the feed.
Ambiguous names return candidates instead of acting; gates confirm every
time.
