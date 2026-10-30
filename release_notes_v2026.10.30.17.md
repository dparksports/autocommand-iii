# AutoCommand III — v2026.10.30.17

One letter per cell: the cinema wall no longer superimposes glyphs.

## What changed

- **The rain reads clean — no more overwritten letters.** The decorative
  wall occupies every cell, and the exponential fade only *decays* a
  glyph, it never removes it. The blit is an over-composite, so a new
  glyph stamped onto a cell blended with the 10–30% ghost of a *different
  random katakana* from the stream's previous pass: opaque stroke cores
  replaced, every antialiased pixel mixed — two letters jammed in one
  cell, pane-wide. Every stamp now clears its cell first (deco trail,
  white tip, mid-trail mutation, the data lead, the bold data-trail
  stamp): replacement semantics, the film's one-letter-per-cell rule.
  The data lead clears the standard cell (not its 6%-larger lifted rect,
  which would nick the neighboring glyphs' edges); the bloom halo still
  blends — a glow is supposed to.
- **The overlay repo (autocommand-matrix) gets the identical renderer** —
  the shared engine, one fix, both faces.
