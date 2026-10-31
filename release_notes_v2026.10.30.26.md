# AutoCommand III — v2026.10.30.26

The rain pane's width stops churning the list.

## What changed

- **Auto-fit width changes are debounced.** The alive-process count
  churns as connections start and stop, and every pane-width change
  re-measures the list's DataGrid — an un-debounced fit re-measures the
  grid at churn rate, which shows up as a busy cursor and a pane that
  never settles (worst right after folding the nav, when everything
  re-measures at once). The fit now applies immediately once, then
  settles on a one-second trailing cadence while the count keeps
  moving — the pane still tracks reality, the grid re-measures at most
  once a second, and a user-dragged (manual) width is unaffected.
- (For context: the invisible fold triangles and the missing drag
  resize were fixed in .25 — solid drawn arrows + a real splitter with
  double-click re-fit. If you're seeing a clipped rain sliver at the
  right edge, you're on .24; update.)
