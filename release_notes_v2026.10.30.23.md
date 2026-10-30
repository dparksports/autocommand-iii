# AutoCommand III — v2026.10.30.23

The rail becomes findable, and the rain pane is exactly as wide as its
rain — no gap strip.

## What changed

- **The rail's chevrons are visible now.** The fold rail widens 13→18 px
  and the ‹ / › glyphs grow 10→13 pt — at the old size they all but
  vanished against the pane background. Same behavior as .22: ‹ folds the
  process list (rain only), › folds the rain (list only), a chevron
  disables when its press would empty the tab.
- **The rain pane self-sizes to its columns.** The renderer now publishes
  the alive, capped column count and the cell width in DIP
  (ActiveColumns / CellWidthDip / ActiveColumnsChanged, raised on the UI
  thread); the rain view sets its own Width to exactly
  columns × cell — the hosting Grid column is Auto and simply follows.
  Three alive processes → a three-column pane hugging the docked edge;
  nine → nine; nothing alive → a single empty column of presence. The
  dark gap between the pane's edge and its rain is gone in both modes:
  beside the list, and in rain-only mode where the list column absorbs
  the free space and the rain hugs the edge. The panel reflows live as
  processes come and go.
- RainSelfTest logs the activeCols transitions
  (`activeCols -> 3` on the first snapshot).
