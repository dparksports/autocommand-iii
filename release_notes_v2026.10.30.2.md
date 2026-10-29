# AutoCommand III — v2026.10.30.2

The Blocks tab rebuilt as a **master–detail** view, with wording that always
names its object — plus a fix for a crash shipped in v2026.10.30.1.

## Crash fix (all v2026.10.30.1 users should upgrade)

Opening any bulk action's preview crashed the app: the preview window's XAML
wired a `Checked` handler that fired while the window was still parsing
(`IsChecked="True"` ran before the second radio button existed). Every bulk
button — Turn blocking back ON / OFF, the category buttons, the Restore
guide — hit it. Handlers now attach after construction and the label update
is null-safe.

## Master–detail categories

- **Left panel** — every category visible at once: severity badge (only when
  it matters: 🔴 high, 🟠 medium, nothing for normal), service name, a
  proportional on/off state bar and `N on · M off` counts. Clicking a
  category shows its blocks on the right; ↑/↓ walks the list.
- **Right panel** — the selected category's detail: description, totals and
  leak warnings, scoped actions (`Restore blocking (N off)` /
  `Stop blocking (N on)`), and the familiar row grid.
- A pinned **All blocks** row keeps the flat everything-view; the **search
  box** finds categories *and* targets (an IP hit selects its category); a
  splitter sizes the panes.
- The old accordion expander — its chevron, and the always-white impact
  circle — are gone. Basket checkboxes now appear **only** on rows where
  they can act (blocking OFF); active rows show no dead grey checker.
- The state ribbon lenses the **category list**: click `⏸ N blocking OFF`
  and the left panel shortens to exactly the categories holding inactive
  rules, with each category's checkbox selecting its OFF blocks for
  restore.

## Wording: the object is always BLOCKING

"Resume paused" asked users to reconstruct a reference point — is the
process paused, or is blocking the process paused? Every verb now names its
object, and state words are ON/OFF, which need no memory of prior state:

- `Turn blocking back ON (289 off)` · `Turn blocking OFF (55 on)`
- Rows: `Stop blocking` / `Restore blocking`
- Chips: `⛔ N blocking ON` / `⏸ N blocking OFF` / `◐ N partial`
- Toast: `Blocking restored — 289 targets ON again` (with ↩ Undo)

## Upgrade notes

No format, rule-name, or path changes — `blocks.txt`, `blocks-events.log`
and all `AC-BLOCK-*` firewall rules carry over untouched.
