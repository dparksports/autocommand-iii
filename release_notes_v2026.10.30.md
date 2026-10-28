# AutoCommand III — v2026.10.30

The same app — every view, every behavior, every self-test — with its core
engine layer rebuilt in **F#**. The C# WPF shell and app services now drive
app-agnostic F# engines; nothing about what you see or click changed.

## What's new in III

- **F# core engines** (`Engines/`, five projects, ~17.6k lines): firewall
  management with strong-typed COM (no `dynamic`), Sysmon watching and
  archive, the TCP capture / TLS decrypt / transmissions pipeline, DNS
  geo/org resolution with the idle reconciler and an in-engine RDAP
  client, process provenance / task attribution / spawn watching, the
  site-doctor verdict engine, and the MCP agent bridge. 32 C# engine
  implementations retired.
- **Everything autocommand-ii added after the engine fork was ported
  forward**: the full DNS geo architecture (/24 prefix cache, adaptive
  backoff, RDAP fallback, ResolveNow / RetryUnresolved /
  DescribeResolveState, idle reconciliation), firewall v2 path-hash app
  blocks with `RetireDeadAppBlockRulesAsync`, stale-block inventory
  surfacing (`StaleRules` / `CoverageNote`), `TlsKeylogStore`,
  `BuildReportText`, and MCP stale-registration detection.
- **Engines are app-agnostic by construction** — the same engine codebase
  serves the AutoCommand / AutoScope / AutoTrack family; every app name,
  rule prefix, data path and port is injected via `AppIdentity`.
- **Self-tests, all green**: ContainSelfTest (firewall), SiteDoctorSelfTest
  (diagnosis), NetWatchSelfTest (watch + DNS idle), ProxyInstallSelfTest
  (bridge + stale detection), DnsFeedSelfTest, OneSettingsCadenceSelfTest
  (52/52), PyTraceSelfTest reassemble.

## Upgrade notes

- The DNS / geo / RDAP caches now live under `%LOCALAPPDATA%\AutoCommand`
  (engine-identity paths); same file names and formats — copy them over by
  hand if you want to keep a warm cache.
- AI sessions, firewall rule names (`AC-BLOCK-*`), the undo ledger and the
  loopback bridge port (47821) are unchanged; existing rules and MCP client
  registrations carry over untouched.
