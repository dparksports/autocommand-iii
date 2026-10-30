# AutoCommand III — v2026.10.30.23

Unblocking a process now really unblocks it, and duplicate block rules are
visible and cleanable in one click.

## What changed

- **Unblock lifts the whole name family.** A process block was always a
  *name* family — the app keeps a blocked executable covered at every path
  it runs from (that is how a blocked app survives its own updates) — but
  Unblock only removed the one path's rules, so a sibling at another path
  kept the name blocked and quietly re-blocked the just-unblocked path the
  next time the process ran. For something that spawns constantly (Git
  Bash), "unblocked" never stuck: observed live, an unblock at 20:17 was
  re-blocked at 20:20 and reported blocked again from then on. Unblock now
  lifts every path of the executable, the confirmation dialog says so, and
  the coverage cache is told immediately so nothing re-extends in the gap.
- **Unblock verifies before it claims success.** After removing rules, the
  app re-reads the live firewall: if anything still covers the target you
  get a loud warning with the surviving rule count instead of a green
  toast. The silent-failure era — "✅ unblocked" followed by ⛔ again — is
  over.
- **Duplicate rules can no longer pile up.** Six identical rules for one
  executable were observed accumulating in a single minute (concurrent
  process-create events all passing the "not yet blocked" check before any
  rule landed). Rule adds are serialized now, and the duplicate check
  compares normalized paths, so `..`-bearing path spellings no longer
  double-block.
- **"Clean duplicates" on the Rules tab.** A new **Dups** column shows how
  many rules on each target are exact duplicates of another (blocking is
  unchanged by them — they are clutter, skewed counts and reconcile noise),
  and a **🧹 Clean duplicates (N)** button retires them in one click. The
  removal is rename-then-remove: each retiree gets a unique quarantine name
  before the by-name delete, so it can never take the keeper with it; one
  rule per identity stays with its provenance. The action lands in the
  Audit tab as a resolved fleet incident. Intended coverage — one
  executable name at several paths, TCP+UDP per IP — is never counted as
  duplicates.
- **`audit_block_duplicates` bridge tool** (read-only): per-target duplicate
  counts and a verdict, so an agent session can check for residue and point
  at the button.
- **CI:** TaskAttributionSelfTest builds again (the dead-code sweep removed
  the `known-tasks.json` asset but missed this harness's copy step).

## Verification

Release and Debug builds clean; all five strict offline suites green
(NetWatchSelfTest, the 43-check OneSettingsCadence suite, Contain with +19
new checks from this work, ProxyInstall incl. the stub-drift guard, Bridge);
the two advisory live-channel suites run. On the
development machine the detector reports 374 IP targets exactly at their
2-rule quota (zero flagged) and flags exactly the known race-era residue.
