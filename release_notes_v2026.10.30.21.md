# AutoCommand III — v2026.10.30.21

The rain's column count is the truth: one column per alive process,
never ten or more.

## What changed

- **Columns == alive processes.** Every snapshot counts the tracked
  processes that still have at least one live conversation row (the same
  rows the Process Monitor shows), and the pane lays out exactly that
  many columns — an IP leaves the table, its column leaves the pane;
  a new one arrives, a column slides out of the docked edge. `maxColumns`
  drops from 64 to **9** and is clamped 1–9 on load, so ten or more can
  never render regardless of what a hand-edited config says.
- **The cinema wall spans exactly the data columns** — hugging the docked
  edge — and the rest of the pane stays dark. Nothing alive, nothing
  rains. Streams persist across alive-count changes instead of being
  recreated, and any wall left outside the active region decays through
  the normal fade.
- **True edge anchoring fixed on the way**: the ranking promise ("busiest
  at the docked edge") silently broke whenever the grid wasn't full —
  `maxSlots-1-idx` clustered columns at the pane's left when docked
  right. Slots are now `gridCols-1-idx`, so the busiest column really is
  the rightmost (or leftmost, docked left).
- A column that loses its slot (death or cap) has its strip cleared once;
  the harness proves the shape: 3 synthetic PIDs → exactly 3 wall+data
  columns at the edge, everything else dark.
