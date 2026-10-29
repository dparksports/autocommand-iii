# AutoCommand III — v2026.10.30.6

The agent-surface release: six bridge fixes from field bug reports, and the
mutating tool set pared down to exactly the gate pair.

## MCP bridge fixes

1. **Bad Bearer token now answers properly** — HTTP 401 plus a JSON-RPC
   error envelope explaining that tokens change on every session restart
   and to re-read `session.json` (was a silent close / non-RPC body). The
   loopback-403 and payload-too-large rejects carry JSON-RPC errors too —
   one uniform error shape.
2. **`session.json` no longer carries a tool list** — it went stale the
   moment a build added a tool mid-session; the live `tools/list` is
   authoritative. The file's contract is `{port, token, pid, mutatingAllowed}`.
3. **No more double-decoding** — tool payloads are compact JSON (no `\r\n`
   inside the text), and parsed JSON is attached as `structuredContent`
   for MCP 2025-06-18 clients.
4. **Port scheme documented** — the bridge takes `portBase + first free
   offset` per running instance; sibling instances reject foreign tokens.
   Read `session.json`, never hardcode a port.
5. **Distinct errors** — a typo reads "Unknown tool — call tools/list"; a
   real tool behind the policy reads "exists, but this session has mutating
   tools OFF — enable in AI access settings and restart the session."
6. **One published row schema** — `list_recent_block_hits` rows now use
   `block-hits.jsonl`'s exact field names (`Image`, `Pid`, `DestIp`,
   `DestPort`, `MatchKind`) with aggregate extras (`Process`, `Attempts`,
   `FirstUtc`, `LastUtc`) additive. Note: field renames from the old
   `ts_last_utc` / `destination` / `blocked_kind` names.

## The agent surface is now observe + request

`block_ip`, `unblock_ip`, `kill_process`, and `disable_task` are removed.
Agents observe everything (19 read-only tools) and may request exactly one
mutating thing: **a time-boxed gate** — `open_gate` / `close_gate`, each
call confirmed by the user for one specific target, each window
self-closing (1/10/30/60 min, expiry, or app exit). Killing processes,
disabling scheduled tasks, and creating or deleting block rules are
human-only surfaces again.

## Also in this release train

The complete block-gate service with agent-visible lifecycle
(v2026.10.30.4/.5).

## Upgrade notes

Restart the app (and kill any stale second instance — it holds a sibling
bridge port and a stale token). Agents parsing `list_recent_block_hits`
rows must use the new field names. No format or firewall-rule changes.
