# AutoCommand III — v2026.10.30.8

Maintenance release following the manually-cut v2026.10.30.7: increments the
version on a clean tree and carries the release-notes title correction
(the v2026.10.30.7 notes were briefly mislabeled .30.6). No code changes
since v2026.10.30.7 — binaries are functionally identical; this release
exists so the pipeline, tag and version stay in lockstep.

## For anyone arriving here from v2026.10.30.7

That release (identical code) contained the agent-surface changes: the six
MCP bridge fixes from the field bug report (proper 401 + JSON-RPC error for
bad tokens, no stale tool list in session.json, compact payloads with
`structuredContent`, documented port scheme, distinct unknown-tool vs
mutating-off errors, one published row schema for `list_recent_block_hits`)
and the removal of `block_ip`, `unblock_ip`, `kill_process` and
`disable_task` — the agent surface is now 19 read-only tools plus the
user-confirmed, self-closing gate pair (`open_gate` / `close_gate` /
`list_gates`).
