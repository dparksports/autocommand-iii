# AutoCommand III — v2026.10.30.14

Hotfix: the Blocks tab's lists had no text at all.

## Fixed

- **Blocks master/detail lists rendered without text.** Not a contrast
  problem — the labels never rendered. The master-detail redesign's
  view-models exposed public *fields*, and WPF `{Binding}` reads only
  *properties*: every binding to one silently produced empty, so the
  category list showed its checkboxes, badges and green state bars but
  no titles, and the rows showed no targets or services. (The filter
  chips kept their text — they render via `ToString()`.) All 49 fields
  are now properties; a repo-wide audit confirms no other view binds to
  fields. This bug shipped with the master-detail redesign and was
  independent of the theme changes — the light theme just made it easy
  to notice.

No other changes from v2026.10.30.13.
