# AutoCommand III — v2026.10.30.15

The gate tools work again, every change to your blocks becomes answerable,
and updating Windows stops being a panic-button decision.

## The gate crash — fixed

**`open_gate` / `close_gate` failed on every call** with WPF's "The calling
thread cannot access this object because a different thread owns it" — even
`dry_run`, which was supposed to touch nothing. Root cause: the bridge's UI
availability pre-check read `Application.MainWindow` (a thread-verified WPF
property) on the RPC listener thread, *before* the tool handler ran, so the
exception fired before the dry-run branch could ever be reached.

Three fixes, each regression-tested in BridgeSelfTest section 7 (whose stub
UI throws the exact field exception):

- **`IsUiAvailable` is thread-safe** — the `MainWindow` read is marshalled
  to the UI dispatcher; a shut-down dispatcher reads as "no UI".
- **`dry_run=true` bypasses confirmation at the engine level** — the
  documented "resolve and preview only" contract now holds even in a
  session with no UI at all.
- **A UI hook that throws denies cleanly** — clients get the actionable
  "no UI to confirm with" denial instead of raw WPF exception text.

## 🧾 Audit — a new CONTAIN tab

Every change to your blocks — yours, AutoCommand's, or anything else —
narrated in plain language, one incident per event burst, newest day first.
The status strip answers "does anything need me?" before the list appears;
red is reserved for external changes that were not confirmed undone.

- **Incidents, not log lines**: the monitor's 3-block burst reads as
  "AutoCommand blocked 2 addresses and 1 program over a quick burst"; a
  restore-guide pause reads as "The restore guide paused 162 blocks".
- **Fleet operations are now audited** — the panic button, Re-enable all,
  restore-except-risky, legacy consolidation, and manual rule toggles in
  the Firewall tab previously bypassed blocks-events.log entirely (the
  Oct 27 fleet-disable was reconstructible only by forensics on a CSV
  export). Each now writes one audit line with counts and origin.
- **"Since last visit" is the default range**, backed by a shared
  `audit-seen.txt` marker — the same marker the Blocks tab's digest toast
  uses, so the two surfaces never disagree about what you've seen.
- **Raw ledger** toggle for the one-row-per-event view (same filter, same
  facts), plus CSV export of exactly what's on screen.
- External modifications (`external-modified`) and future OS-attributed
  events already have a severity path: restored → amber, un-remediated →
  red with an attention summary.

## 🪟 Windows Update — a new SYSTEM tab

The sanctioned update path for a machine this locked down: security
patches can't arrive through full blocking, and the panic button was the
only way through.

- **Scope is computed, never guessed**: exactly the blocks whose service
  attribution matches the updates/Defender symptoms (ServiceOutageMap —
  the same rule the restore guide uses), shown to you before anything
  changes. Everything else stays blocked the whole session.
- **Blocks lift as gates, not pauses**: a 120-minute hard stop restores
  everything even if the app crashes, exits, or the machine reboots.
- **The tab drives the update** via the Windows Update Agent (late-bound
  COM — no interop dependency): scan → download → install with phase
  reporting, a live countdown to auto-restore, and "Restore all blocks
  now" one click away. A CDN escalation button opens the Akamai/Gcore
  edge tier only if a download stalls — announced, audited, restored with
  everything else.
- **The after-loop is the feature**: gates close on completion, every
  lifted block is re-enabled and *verified against the live firewall*
  (one retry for stragglers, never a silent skip), dead-path rules are
  retired (Windows Update moves Defender's binaries every time), update
  services restart to sever lingering sockets, and the whole session
  lands in Audit as one incident — "You updated Windows — N blocks paused
  for M min, all restored".
- **Nightly Defender window (opt-in)**: a standing 3:00–3:20 AM gate for
  signature updates only, same machinery, runs while the app is running.

## Also in this release

- The Blocks tab's digest toast and the Audit tab share one visit marker,
  and both count the new fleet/update events.
- ContainSelfTest gains the audit-narration and update-session sections
  (16 + 4 checks); BridgeSelfTest gains the gate-path regression section.

## Upgrade notes

Restart the app to pick this up — the running instance predates both the
gate fix and the new tabs. The Rain tab's stuck "attaching…" from builds
before v2026.10.30.14 resolves with this restart as well (its diagnostics
strip has named every failure stage since .14; if it ever sticks again,
the strip says which stage and why). Existing `matrix_rain.json`,
session tokens, and block inventories are unchanged.
