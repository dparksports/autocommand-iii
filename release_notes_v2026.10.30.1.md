# AutoCommand III — v2026.10.30.1

A ground-up redesign of the **Blocks tab** for large block inventories — the
view was built for dozens of blocks, but real machines accumulate hundreds
(one test machine: 344 targets, 565 of 671 rules disabled), and at that scale
the old binary "Pause all / Resume all" rows hid more than they said.

## What's new

- **State ribbon**: the passive count chips are now filters — click `⏸ N
  paused` (or active / partial / high-impact / unattributed / on-MS-infra)
  and the list lenses to exactly those rows, identity columns intact. The
  paused chip is the one view of every inactive block: what it is, who
  triggered it, when it was blocked, whether anything still tries it.
- **Honest group headers**: a proportional green/amber active-vs-paused bar
  with `N active · M paused` text, and two separate commands labeled with the
  count they will actually change — `▶ Resume paused (M)` / `⏸ Pause active
  (N)` — replacing the ambiguous all-or-nothing toggle that a mixed group
  rendered as "Pause all (131)".
- **One preview instead of a modal wall**: every bulk pause/resume (global,
  per group, per selection, and the Restore guide) goes through a single
  change-preview sheet — scope grouped by service, exact target/rule counts,
  amber warnings on 🔴 high-impact groups, and a one-click **"all except
  high-impact"** option. Apply writes one batched status flip and one audit
  event (`BlocksStore.SetStatuses`), not a file rewrite per target.
- **Undo everywhere**: applying shows a result toast with **↩ Undo** backed
  by a before-state snapshot, so "restore all 289 paused blocks" is one
  decision you can take back.
- **Truthful row state**: rows distinguish **partial** coverage (some of a
  target's rules on, some off — `◐`) from fully active or paused, and paused
  rows show *why* they are off — `⏸ 3d · Blocks tab` — recovered from the
  audit log's newest pause event.
- **Bulk-resume selection**: checkboxes on paused rows, a tri-state "Select
  all paused" master, and `▶ Resume selected (N targets · M rules)` through
  the same preview.

## Upgrade notes

- No format, rule-name, or path changes — `blocks.txt`, `blocks-events.log`
  and all `AC-BLOCK-*` firewall rules carry over untouched. The audit log is
  now also read (tail only) to recover pause provenance; nothing new is
  written to it beyond the batched events.
