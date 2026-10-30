# AutoCommand III — v2026.10.30.22

Either pane folds: read the list, watch the rain, or give either one the
whole tab.

## What changed

- **The rail between the panes now folds either side.** Two chevrons:
  ‹ toggles the process list (rain-only mode — the animation owns the
  whole tab), › toggles the rain (list-only — the monitor's original
  full-width layout). A chevron disables exactly when pressing it would
  hide the only visible pane, so both can never fold at once; a disabled
  chevron dims to a quarter opacity and its tooltip becomes "Show …",
  because its next press restores its pane.
- A hidden pane costs nothing: the rain's own visibility machinery stops
  its renderer and feed (not a frame spent), and the hidden list stops
  rendering while its engines keep feeding the shared table the rain
  reads — rain-only mode is a live view, not a screenshot.
- The choice persists in `%LOCALAPPDATA%\AutoCommand\rain_panel.txt` as
  "both" | "rain" | "grid", applied before layout so a folded pane never
  flashes open on launch; older "collapsed"/"expanded" files migrate
  ("collapsed" meant rain-hidden → "grid").
