# AutoCommand III — v2026.10.30.3

The performance release: one calibrated scheduler for every periodic
instrument, one coalescing write path for every periodic disk writer, and a
long list of dead code removed. Plus the crash and contrast fixes from
earlier today.

## Instrumentation Metronome — spread, calibrated, self-budgeting

- **One registry, one scheduler** for periodic work. Instruments are
  phase-spread by a stable id hash (equal-cadence work never piles onto the
  same instant — the mechanism behind the 1 Hz desktop hitch) and heavy
  instruments are serialized a second apart.
- **Settings ▸ Instrumentation** replaces Security Check Rate: per
  instrument — on/off, cadence (1 s → 60 s → Off), live last-run stats,
  restore defaults. Persisted to `cadence_settings.json`, hot-applied;
  `check_interval.txt` migrates once as the enforcer's calibration.
- **Self-budgeting**: a run over 500 ms on your machine auto-slows its
  instrument (×5, capped 30 s) with an amber note, instead of stuttering
  the desktop.
- Migrated: the security enforcer fast checks and the svchost trace poll —
  whose 1 s WMI enumeration now gathers **off the UI thread** (the UI
  thread never blocks on WMI), drops its every-5th-tick second WMI pass,
  updates status at 1/5th cadence, and caps its recording buffers.

## Managed disk writes

- **BufferedAppender** (Engines.Core): journal lines buffer per path
  (self-flush at 256 KB); state/store rewrites are last-write-wins with a
  content guard — identical content never touches the disk.
- Routed writers: the Sysmon archive (event lines *and* the watermark that
  used to fully rewrite per event), the agent bridge's activity/feed logs,
  and the blocks store (`blocks.txt` + `blocks-events.log`).
- Flushes ride the metronome (disk-flush instrument, 30 s default, user
  calibratable) plus app exit; **readers flush their own paths first**, so
  read-after-write stays exact. Crash window = one flush interval — safe by
  design: the blocks store reconciles from the firewall rules, the archive
  watermark tolerates boundary duplicates.
- Settings shows the receipts: flushed/buffered bytes with top sinks, a
  **Minimize disk writes** preset (5-minute flush, 1 MB buffers), and
  Flush-now.

## Fixes

- **Crash (v2026.10.30.1)**: every bulk blocking action opened a preview
  window whose XAML fired a `Checked` handler before the second radio
  button existed — NullReferenceException on click. Handlers now attach
  after construction; the label update is null-safe.
- **Blocks tab contrast**: the tab carries its own brighter muted gray
  (~9.7:1 vs the ~6:1 app-wide secondary), 10 px text bumped to 11 px, and
  the search box no longer renders with default white chrome.

## Dead code removed

The v2026.10.30 sidebar slim removed tabs but left machinery: six orphaned
views (Dns, PythonSpawn, PythonInstrumentDialog, TaskHostSpawn,
BitsContentPreview, SvchostTrace), the BITS and PyTrace service families,
TelemetryRulesService, DnsQueryFeedService, three test projects that existed
only for them, and — from an earlier sweep — the orphaned AutoCommand.Sdk
project and the Roslyn package reference it justified. Nothing the MCP
bridge serves was lost: the bridge consumes engines, not views.

## Upgrade notes

No format, rule-name, or path changes; `blocks.txt`, `blocks-events.log`,
the Sysmon archive and all `AC-BLOCK-*` firewall rules carry over
untouched. First launch migrates `check_interval.txt` into
`cadence_settings.json` (the old file is ignored from then on).
