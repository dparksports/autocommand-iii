# AutoCommand III — v2026.10.30.16

The Rain tab works when you come back to it — and tells the truth while
it's warming up.

## What changed

- **Rain tab fixed: returning to the tab resurrected nothing.** The first
  build attached fine, but WPF's TabControl tears a tab's content out of
  the visual tree the moment you select another tab (Unloaded — the
  renderer stops, correctly, so a hidden tab costs nothing) and reconnects
  it when you return. On that return, `IsVisibleChanged` fires while the
  view is not yet "loaded", the restart path declines — and then the
  attach guard saw the renderer already existed and early-returned. The
  rain was a black pane with a frozen `attaching...` strip for the rest of
  the session. Attach now re-applies visibility on every re-entry: same
  renderer, resumed drawing. Proven in RainSelfTest with an
  away-and-back round trip whose lifecycle log shows the draw counter
  freeze while away and continue from the same value on return
  (536 → 609 → 914 → ...) — pre-fix it stayed frozen forever.
- **The diagnostics strip can no longer starve.** Both the strip timer and
  the harness's ran at DispatcherPriority.Background — on a busy machine
  Background work can be starved indefinitely while the app itself stays
  responsive, so the strip froze mid-sentence (even at the stock
  `attaching...`) while the rain was fine, and lied about the state. Both
  timers now run at Render priority: two string writes per 500 ms are
  nothing, and the strip now tells the truth.
- **RainSelfTest grew a real regression harness**: the away/back round
  trip is scripted, every diagnostics tick appends a timestamped lifecycle
  line (cols / draws / fps / visibility / loaded / tree membership) to
  `%TEMP%\rainselftest.log`, and dispatcher exceptions land in the same
  log instead of dying silently.
