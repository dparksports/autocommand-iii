# AutoCommand III — v2026.10.30.9

The confirmation popup **is** the policy: the mutating toggle is gone, and
session grants are scoped to one target.

## What changed

- **Be My Guest loses the "Allow mutating actions" checkbox** (and its
  `mutating_pref.txt` preference, deleted on startup). The gate pair —
  `open_gate` / `close_gate` — is always offered to interactive sessions,
  and **every request confirms in the app** with the exact target,
  duration, auto-close time, and the agent's reason. No session-wide
  stance to configure or forget to turn off — and the "agent asked but
  nothing happened" failure mode (a toggle silently hiding `open_gate`
  from the session) is impossible by construction. Headless sessions
  remain gate-free (no UI to ask).
- **"For this session" grants are per-target, not per-tool.** Granting
  gates for one address or host covers only that target — every other
  blocked service or host asks again, first time, every session. The
  dialog's checkbox states its own scope ("Allow gates for THIS target
  for the rest of the session — other targets still ask"), and the
  activity log records each grant with "(this target only)".
- The unknown-tool vs tools-off error split in the bridge keeps its
  meaning for headless sessions.

## The agent surface, complete picture

19 read-only tools (observe everything: monitor, Sysmon, transmissions,
blocks, hits, containment, diagnosis, investigation, gates list) plus the
request path: `open_gate` (1/10/30/60 minutes, user-confirmed for one
specific target, self-closing on expiry or app exit), `close_gate`
(early close), `list_gates`. Blocking rules, processes, and scheduled
tasks are human-only surfaces.

## Upgrade notes

Restart the app to pick this up. First launch deletes the obsolete
`mutating_pref.txt`. Sessions started by older clients keep working —
the token and port scheme are unchanged.
