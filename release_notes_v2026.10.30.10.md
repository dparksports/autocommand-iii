# AutoCommand III — v2026.10.30.10

The audit tab, the bridge session/transport fixes from the field report,
and the Shields Up light theme — consolidated.

## What changed

- **New Audit tab: the story of every block, told in sentences.**
  `blocks-events.log` has always recorded everything; now it's readable.
  Consecutive events group into incidents, and each incident is one
  plain-language sentence ("AutoCommand blocked 6 addresses and 2 programs
  over a few minutes", "You pressed the panic button — every block rule
  went off") with day headers, severity chips and the raw lines one click
  deep. The Blocks tab shows a one-toast-per-visit digest (counts by
  category, never one toast per event), and a first visit adopts history
  silently instead of replaying the log. Fleet operations (panic button,
  re-enable-all, restore-except-risky, consolidate) and hand-toggled rules
  now write audit events at the spots the pause history couldn't see.
- **AI access bridge: the session-file and transport round.** A sibling
  app instance exiting can no longer revoke the live session's
  `session.json` (deletion is pid-guarded now — the field report's
  "session.json never written" was written-then-deleted by the old
  build's exit). Every transport error speaks one JSON-RPC dialect
  (405 included); lengthless/chunked POSTs answer 411 and malformed
  bodies a real 400 instead of closing the connection bare (the
  flaky-empty-response class); rejections drain the request body so
  Windows sends FIN, not RST; tool JSON embedded in results reads as
  conventional `\"` escapes. Sixteen new wire-level self-test checks
  lock these behaviors in.
- **Light theme is now the Shields Up look.** Settings → theme → Light
  switches live to the Shields Up design system (#ECF2F9 canvas, white
  surfaces, #007AFF accent, light-tuned status colors), and the last
  ~60 hard-coded dark-tuned colors across the views were routed through
  theme brushes so both themes hold everywhere. Dark remains the
  default.
- **Smaller download:** the AgentProxy bundle ships compressed
  (15.3 → 11.5 MB).

## Upgrade notes

Restart the app to pick this up. No settings or data migrations; the
bridge token/port scheme is unchanged, and the Audit tab adopts your
existing `blocks-events.log` history on first visit without replaying
it as notifications.
