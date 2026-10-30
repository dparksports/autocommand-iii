# AutoCommand III — v2026.10.30.19

One polish release: the Blocks tab's master–detail layout flips sides.

## Blocks tab — master on the right

The category list (master) now lives on the **right**; the selected
category's blocks (detail) fill the **left**. The header text and the
layout comments moved with it — nothing else about the tab changed:
same grouping by the Windows service that made the connection, same
selection behavior replacing the old expand/collapse accordions, same
undo ledger underneath.

## Verification

Shipped behind the local gate (the release path builds binaries only):
NetWatchSelfTest, OneSettingsCadenceSelfTest, ContainSelfTest,
ProxyInstallSelfTest and BridgeSelfTest all green, plus Debug and
Release builds of the full app.
