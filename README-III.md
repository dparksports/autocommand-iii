# AutoCommand III — autocommand-ii's app, core engines in F#

AutoCommand III is autocommand-ii (latest, 2026.10.29 sources) with its
core engine layer in F#: the UI, app services, and tool registry stay C#,
and every engine family — firewall, sysmon watching, DNS/geo/RDAP, TLS
interception, transmissions, forensics, site-doctor diagnosis, and the
MCP agent bridge — runs on the F# engine projects under `Engines/`,
injected with AutoCommand's identity. The engines were seeded from
autoscope's ports, then autocommand-ii's newer features were ported in
(the autoscope fork predates them), and the remaining services that
autoscope never ported were ported fresh.

## Layout

```
AutoCommand.csproj        C# WPF app (autocommand-ii UI)
core/                     C# projects — thin facades/composition roots over
                          the F# engines plus the app-only services that
                          stay C# (BITS watcher, telemetry rules, the
                          intercept proxy, RDAP consumers, task scheduler…)
Engines/                  F# engine projects (AutoCommand.Engines.*)
  Engines.Core            AppIdentity, IpClassification, TrafficRow
  Engines.Firewall        firewall engine + strong-typed COM (no `dynamic`)
  Engines.Diagnosis       verdict / probe / fingerprint / dependency join
  Engines.Watch           sysmon watch+archive+install, sniffer, TCP capture,
                          TLS decrypt/keylog, transmissions pipeline+archive,
                          DNS (geo/idle/retry) + RDAP, provenance,
                          task attribution, image spawn, svchost identity
  Engines.Bridge          MCP agent bridge + client-registration catalog
```

## Identity injected at startup

`App.OnStartup` calls `WatchConfig.Configure(Identity.autocommand())`;
`BridgeHost.Wire()` constructs the bridge engine + MCP catalog the same
way; `Fw`/`FirewallService` construct the firewall engine with
`Naming.autocommand()`: data root `%LOCALAPPDATA%\AutoCommand`, bridge
port base 47821, `AC-BLOCK-*` rule prefixes (legacy `AutoCommand IP
Block - `), user-block marker `AutoCommand User Block`, MCP id
`autocommand`.

## What runs on F# engines (all verified by self-tests)

| cluster | F# engine | self-test |
|---|---|---|
| Firewall incl. COM | Engines.Firewall (behind the C# `FirewallService` facade) | ContainSelfTest ALL PASS |
| Site verdict / probe / fingerprint / dependency | Engines.Diagnosis | SiteDoctorSelfTest ALL PASS |
| Sysmon watch, archive, installer | Engines.Watch | NetWatchSelfTest ALL CHECKS PASSED |
| DNS geo/org + idle reconciler + RDAP | Engines.Watch (Dns.fs, Rdap.fs) | NetWatchSelfTest (idle batch) |
| TCP capture, TLS decrypt/keylog, transmissions | Engines.Watch | PyTrace reassemble PASS; TransmissionsView paths compile+run |
| Provenance / task attribution / image spawn | Engines.Watch | TaskAttributionSelfTest (live, exit 0, same as ii) |
| MCP agent bridge + client registration | Engines.Bridge (behind `BridgeHost` facades) | ProxyInstallSelfTest PASSED |

BitsTraceSelfTest reports 2–5 failures on this machine — the same
"real log" family as the untouched autocommand-ii (it reads live BITS
activity; environmental, not a regression). PyTraceSelfTest `unblock`
prints FAIL in both this tree and the original (Zone.Identifier behavior
of this machine); `reassemble` PASSES.

## autocommand-ii features ported into F# during this migration

- Firewall: `AddAppBlockWithPathAsync` + path-hash sibling scheme
  (`PrimaryAppRuleName`/`SuffixedAppRuleName`/`PathHash8`/
  `GetAppRulesForExe`), `RetireDeadAppBlockRulesAsync`,
  `BlockedTarget.StaleRules`/`CoverageNote` + stale-path inventory logic,
  `GroupToggleResult.Notes`.
- **Dns.fs rewritten to the full autocommand-ii architecture**: the /24
  geo prefix cache (14d TTL, 24h negatives), adaptive ip-api backoff with
  skip-spacing for urgent lookups, the RDAP registry fallback, and the
  whole resolve-state surface — `ResolveNow`, `RetryUnresolved`,
  `DescribeResolveState`, `UnresolvedIps`, `StartIdleReconciliation`,
  `RunIdlePassForTest` (internal, NetWatchSelfTest).
- **Rdap.fs ported fresh** (autoscope never had it): block-keyed cache,
  IANA-bootstrap routing with rdap.org fallback, three-layer throttling,
  private-space short-circuit, `RdapService.Instance`.
- `SysmonWatcherService.QuietFor` (last-event idle signal).
- `SvchostMonitorItem.ResolveStateText`.
- `TlsKeylogStore` (the instance core of the ii keylog split) plus the
  injected-dir constructor seam on `TlsKeylogService`.
- `ProcessProvenanceService.BuildReportText`.
- `TrafficStat.FormatBytes` (public).
- Bridge: stale-registration detection (`FindStaleRegistrations` /
  `StaleMcpRegistration` / `ReadRegisteredCommand`).
- Verdict strings rebranded AutoCommand (ii wording).

## What stays C# (app/services, by design — same call autoscope made)

`AgentToolRegistry` (the tool surface, adapted to the engine's
`IAgentTools` seam via `BridgeHost`), `BlocksStore`,
`TelemetryRulesService`, `InterceptProxyService`/`CaService`/
`SystemHooks`, `BitsWatcherService` + BITS models, `TraceMetricsService`,
`PyTraceInstaller/Reader`, `ProcessInvestigationService`,
`RdapService` consumers, `SysmonDiagnosticsService`,
`SysmonProcessAuditService`, `DnsQueryFeedService`, `ProcessRunner`,
`TaskSchedulerService`, svchost-watch (vendored analyzer CLI),
OneSettingsCadence, AgentHost/AgentProxy (deployment faces).

## The seam patterns (for future services)

- **C# static facade** over the F# engine, constructed with the app
  identity: `FirewallService` (Contain), `AgentBridgeService`/
  `McpClientCatalog` inside `BridgeHost` (Agent). Consumers compile
  unchanged; the engine holds zero app values.
- **Adapters for injected seams**: `BridgeHost.ToolRegistryAdapter`
  (C# registry → F# `IAgentTools`), `Services/AgentBridgeUI` (WPF → F#
  `IAgentBridgeUI`, replacing the old `WpfAgentInteraction`).
- **Row adapter**: `RowAdapter.ToRows` maps app `TransmissionRecord`s to
  the engine-side `TrafficRow` where diagnosis consumes archive rows.
- Known interop gotchas handled along the way: F# tuples carry no C#
  element names (deconstruct at call sites), F# `bool*string` surfaces as
  `Tuple<_,_>` (deconstruction still works), C# nested types are
  top-level in F#, `InternalsVisibleTo` for test seams, per-project
  global usings/aliases to keep old names resolving.

## Build & test

```
dotnet build AutoCommand.csproj          # whole app + engines
dotnet run --project ContainSelfTest     # ALL PASS
dotnet run --project SiteDoctorSelfTest  # ALL PASS
dotnet run --project NetWatchSelfTest    # ALL CHECKS PASSED
dotnet run --project ProxyInstallSelfTest# PASSED
dotnet run --project PyTraceSelfTest -- reassemble   # PASS
```
