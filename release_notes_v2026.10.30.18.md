# AutoCommand III — v2026.10.30.18

The Audit tab renders, grant scope tightens everywhere, and 1,300 lines of
dead code leave the building.

## Fixed

- **The Audit tab actually renders.** v2026.10.30.15 shipped it with its
  incident rows blank and every severity badge stuck green: the bound
  members (`Severity`, `Sentence`, `Meta`, …) were public *fields*, and WPF
  bindings to fields silently produce nothing — the same 49-instance bug
  class the Blocks tab's redesign shipped with. The display members are
  properties now; grouping state that only code touches stays fields.
- **"Always allow" is now per-target for every mutating tool.** The shared
  bridge engine's grant key previously scoped per-tool except for the gate
  pair; since AutoScope's mutating tools run on this same engine, a
  session-wide "always allow kill_process" would have covered *any*
  process. Grant keys now carry the target (`ip` / `pid` / `task_path`) for
  every tool that has one — a grant covers exactly what the user saw in
  the confirmation, nothing more.

## Removed (dead-module scan across the family)

- `DefenderMonitoringService` and `DefenderPrivacyService` — compiled into
  every build, instantiated by no app.
- `ExternalStartMonitor`, `VendorCatalog`, `BloatwareStateConverter` —
  same story, whole files.
- `AuthenticodeVerifier` (this repo's copy — Shields Up ships and uses its
  own), four XAML-orphan converters (`StatusToColor`,
  `TaskStateToColor`, `AppNameToDisplay`, `GroupExpandedState`), and the
  `NetworkAdapterItem` / `EfiFileResult` / `NetworkConnectionItem` model
  classes (Shields Up carries its own live copies).
- Net −1,326 lines. The scan covered 441 types across the three apps,
  cross-checked against XAML and every repo before deletion.

## Upgrade notes

Restart the app as usual. No data, config, or session changes; block
inventories and audit trails are untouched.
