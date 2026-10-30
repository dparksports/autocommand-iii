# AutoCommand III — v2026.10.30.20

The rain moves in with the monitor: one tab, one glance apart.

## What changed

- **The Rain tab is now the Process Monitor's right panel.** The grid and
  the rain share one tab — the live table on the left, the same live
  table raining on the right (330 px, the dark pane it had) — no second
  nav stop, no mode switch between reading and watching. The rain
  attaches at startup with the tab and renders from launch.
- **The thin rail between them collapses it.** One click: the panel
  folds away (chevron flips › → ‹) and the rain's own visibility
  machinery stops the renderer and the feed — a collapsed panel costs
  nothing, not a frame. Click again to bring it back. The choice
  persists across launches as one word in
  `%LOCALAPPDATA%\AutoCommand\rain_panel.txt`, applied before layout so
  a collapsed panel never flashes open.
- The rain's right-click menu (font, colors, speed, movie look, charsets,
  diagnostics) is unchanged — it belongs to the pane, wherever it lives.
