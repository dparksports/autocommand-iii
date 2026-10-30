# AutoCommand III — v2026.10.30.25

The divider you can see, grab, and drag.

## What changed

- **The fold triangles are drawn, not typed.** The ‹ › characters were
  guillemets — a few pixels of ink at font size, in a gray a shade off
  the pane background, on a rail the same color as the panes beside it:
  effectively invisible, twice reported. The rail is now drawn with Path
  geometry (11×13 px solid triangles in the near-white primary text
  color) on a splitter strip in the border gray — visibly a control, in
  both themes. Verified by rendering the exact rail strip to a bitmap in
  isolation (a screen capture of the live app proved flaky).
- **The divider is a real GridSplitter.** Drag it anywhere between the
  triangles to give either pane more or less room; a size preview
  follows the drag. Double-click re-fits the rain pane to its alive
  columns (the auto-fit that sizes the pane to exactly its rain, no gap
  strip). Dragging marks the width as manual — the auto-fit stands down
  until the next double-click — and the chosen width persists in
  `%LOCALAPPDATA%\AutoCommand\rain_panel.txt` (`auto` / `manual=N`,
  old one-line files still migrate). The splitter disables in list-only
  mode (nothing to drag into a hidden pane) and the fold triangles keep
  their same corners.
- The rain pane's fold/restore behavior is unchanged: a hidden pane
  costs no frames, and the renderer still publishes the alive column
  count that drives the auto-fit.
