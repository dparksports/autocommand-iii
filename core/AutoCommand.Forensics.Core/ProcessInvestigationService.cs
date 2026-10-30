using System;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.NetWatch.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace AutoCommand.Forensics.Core
{
    /// <summary>
    /// One-call composite investigation of a process: joins the facts the
    /// individual surfaces already report — PID provenance (Process Monitor's
    /// PID inquiry), the traffic metrics behind the Traces cards, and the
    /// block-hit feed — into a single verdict and a plain-text report.
    ///
    /// Read-only and archive-based: it computes from the app's own recorded
    /// facts plus the live process table. It captures, blocks and changes
    /// nothing, and contains no live Sysmon join — the block-hit answer
    /// comes from the feed's in-memory cache, which is what keeps the whole
    /// call inside an interactive time budget even on large archives.
    ///
    /// Deliberately not a baseline engine: without an authored profile for
    /// the subject there is no "known good" to compare against, so the
    /// verdict describes what the numbers show (quiet, internal-only,
    /// unanswered outbound attempts, actively blocked) — the numbers are
    /// the facts.
    /// </summary>
    public static class ProcessInvestigationService
    {
        // ── public surface ────────────────────────────────────────────────

        public enum VerdictKind { Unknown, Quiet, InternalOnly, OutboundAttempts, Blocked, Mixed }

        public sealed class Investigation
        {
            public string Requested;            // subject text as asked
            public int? RequestedPid;
            public string ImageName;            // resolved image name traced
            public DateTime ComputedUtc;
            public int WindowHours;

            public ProvenanceReport Provenance; // null when no PID to inquire
            public TraceMetricsService.Snapshot Trace;  // null when no name to trace

            public List<BlockHitFeedService.HitAggregate> BlockHits = new();
            public int BlockHitCount;

            public VerdictKind Kind = VerdictKind.Unknown;
            public string VerdictHeadline;
            public string VerdictDetail;
            public List<string> Evidence = new();
        }

        /// <summary>
        /// Investigates a process by image name (wildcards ok) or PID.
        /// Synchronous and archive-backed; runs in seconds on typical
        /// windows (6 h default, 24 h cap — the full-day scan is the slow
        /// path) but is intended for a background thread all the same.
        /// </summary>
        public static Investigation Investigate(string processName, int? pid, int hours = 6)
        {
            hours = Math.Clamp(hours < 1 ? 6 : hours, 1, 24);
            var inv = new Investigation
            {
                Requested = string.IsNullOrWhiteSpace(processName) && pid.HasValue
                    ? $"PID {pid.Value}" : processName,
                RequestedPid = pid,
                ComputedUtc = DateTime.UtcNow,
                WindowHours = hours,
            };

            // ── subject resolution ────────────────────────────────────────
            // A PID inquires fully (provenance incl. the PID-reuse guard);
            // a name only traces. When a name is given, a live instance's
            // PID buys the provenance half for free — best effort.
            if (pid.HasValue)
            {
                inv.Provenance = ProcessProvenanceService.Collect(pid.Value);
                inv.ImageName = ImageNameOf(inv.Provenance);
            }
            else if (!string.IsNullOrWhiteSpace(processName))
            {
                inv.ImageName = NormalizeImageName(processName);
                int? live = FindLivePid(inv.ImageName);
                if (live.HasValue)
                    inv.Provenance = ProcessProvenanceService.Collect(live.Value, inv.ImageName);
            }

            // ── traffic profile ───────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(inv.ImageName))
            {
                var match = TraceMetricsService.Match.ForProcess(inv.ImageName);
                inv.Trace = TraceMetricsService.Compute(match, hours);
            }

            // ── block hits (feed's in-memory cache only — never a join) ──
            try
            {
                var agg = BlockHitFeedService.Instance.Aggregates(hours * 60, 200);
                inv.BlockHits = agg
                    .Where(a => !string.IsNullOrWhiteSpace(inv.ImageName) &&
                                ImageMatches(a.Image, inv.ImageName))
                    .ToList();
                inv.BlockHitCount = inv.BlockHits.Sum(a => a.Count);
            }
            catch { /* feed not running → no hits is the honest answer */ }

            Synthesize(inv);
            return inv;
        }

        // ── verdict synthesis ─────────────────────────────────────────────

        private static void Synthesize(Investigation inv)
        {
            var ev = inv.Evidence;

            if (inv.Provenance != null)
                ev.Add($"[provenance] {inv.Provenance.Verdict} — {inv.Provenance.VerdictDetail}");

            var t = inv.Trace;
            if (t != null)
            {
                var internalDests = t.Destinations.Where(d => IsInternalDestination(d.Ip)).ToList();
                var externalDests = t.Destinations.Where(d => !IsInternalDestination(d.Ip)).ToList();

                ev.Add($"[traffic] {t.ConnectsLast24H} connects in the {inv.WindowHours} h window " +
                       $"(~{t.ConnectsPerHour}/hour), {internalDests.Count} internal vs " +
                       $"{externalDests.Count} external destination(s)");
                foreach (var d in internalDests.Take(4))
                    ev.Add($"[traffic] internal: {d.Display}");
                foreach (var d in externalDests.Take(8))
                    ev.Add($"[traffic] external: {d.Display}");
                if (t.BeaconVerdict != null)
                    ev.Add($"[traffic] beacon: {t.BeaconVerdict} — {t.BeaconDetail}");
                ev.Add(t.BytesAttributable
                    ? $"[traffic] recorded TLS: {FormatBytes(t.TxBytes24H)} up / {FormatBytes(t.RxBytes24H)} down"
                    : "[traffic] recorded TLS: not attributable for this match");
                if (t.LiveConnectionCount > 0)
                    ev.Add($"[traffic] live now: {t.LiveConnectionCount} established connection(s)");
                if (t.ArchiveNote != null)
                    ev.Add($"[archive] {t.ArchiveNote}");
            }

            if (inv.BlockHitCount > 0)
            {
                ev.Add($"[blocks] {inv.BlockHitCount} block-hit(s) within feed retention:");
                foreach (var h in inv.BlockHits.Take(6))
                    ev.Add($"[blocks] {h.Process} → {h.DestIp}:{h.DestPort} ×{h.Count} ({h.MatchKind})");
            }
            else
                ev.Add("[blocks] 0 block-hits — nothing AutoCommand blocks is stopping it");

            // ── the verdict itself ────────────────────────────────────────
            // Describe the numbers; no baseline claims. Ordered by how much
            // each changes what the user should do next.
            if (t == null)
            {
                inv.Kind = VerdictKind.Unknown;
                inv.VerdictHeadline = "Identity only — no image name to trace";
                inv.VerdictDetail = "The PID's subject has exited (or the name was empty), so there is " +
                                    "nothing to match in the traffic archive. Rerun with the image name " +
                                    "to include its connections.";
            }
            else if (inv.BlockHitCount > 0)
            {
                inv.Kind = VerdictKind.Blocked;
                inv.VerdictHeadline = $"Actively blocked — AutoCommand stopped {inv.BlockHitCount} connection attempt(s)";
                inv.VerdictDetail = "Its traffic is failing because it is on the blocked list (or matching a " +
                                    "blocked app), not because of the network. See the Blocks tab before " +
                                    "blaming connectivity.";
            }
            else if (t.ConnectsLast24H == 0)
            {
                inv.Kind = VerdictKind.Quiet;
                inv.VerdictHeadline = "Quiet — no recorded connections in the window";
                inv.VerdictDetail = "Either it made no network attempts, or Sysmon/the archive has no " +
                                    "events for this window yet.";
            }
            else
            {
                bool anyExternal = t.Destinations.Any(d => !IsInternalDestination(d.Ip));
                bool tlsKnown = t.BytesAttributable;
                bool tlsSeen = tlsKnown && (t.TxBytes24H > 0 || t.RxBytes24H > 0);

                if (!anyExternal)
                {
                    inv.Kind = VerdictKind.InternalOnly;
                    inv.VerdictHeadline = $"Internal-only — all {t.ConnectsLast24H} connections stayed on the local network";
                    inv.VerdictDetail = "Discovery broadcasts and LAN services need no internet; this is " +
                                        "expected on an isolated network and is not exfiltration.";
                }
                else if (tlsKnown && !tlsSeen)
                {
                    inv.Kind = VerdictKind.OutboundAttempts;
                    inv.VerdictHeadline = "Outbound attempts, none completed — external addresses reached for, zero recorded TLS";
                    inv.VerdictDetail = "One-way traffic (SYNs, ClientHellos, UDP retries) with nothing coming " +
                                        "back: consistent with a network that has no outbound route, or a " +
                                        "silent peer. The attempts themselves still show up as activity.";
                }
                else
                {
                    inv.Kind = VerdictKind.Mixed;
                    inv.VerdictHeadline = "Mixed — external destinations with recorded TLS traffic";
                    inv.VerdictDetail = "Completed conversations were recorded (bytes flowed both ways). " +
                                        "Judge by the destination classifications and SNI hostnames in the " +
                                        "Transmissions tab.";
                }

                if (t.BeaconVerdict != null)
                    inv.VerdictHeadline += " — beacon-like cadence";
            }
        }

        // ── report text (Copy/Save buttons + the AI bridge share this) ────

        public static string BuildReportText(Investigation inv)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"PROCESS INVESTIGATION — {inv.Requested} · window {inv.WindowHours} h");
            sb.AppendLine($"Verdict: {inv.VerdictHeadline}");
            sb.AppendLine($"         {inv.VerdictDetail}");

            sb.AppendLine();
            sb.AppendLine("SUBJECT:");
            var p = inv.Provenance;
            if (p != null)
            {
                sb.AppendLine($"  PID {p.Pid} · {p.Name} · {(p.Alive ? "running" : "not running")}");
                if (!string.IsNullOrEmpty(p.ExePath)) sb.AppendLine($"  image: {p.ExePath}");
                if (!string.IsNullOrEmpty(p.Owner)) sb.AppendLine($"  owner: {p.Owner}");
                if (p.Created.HasValue) sb.AppendLine($"  started: {p.Created:yyyy-MM-dd HH:mm:ss}");
                if (!string.IsNullOrEmpty(p.Verdict)) sb.AppendLine($"  provenance: {p.Verdict} — {p.VerdictDetail}");
                if (p.Recycled && !string.IsNullOrEmpty(p.CurrentOwnerSummary))
                    sb.AppendLine("  ⚠ PID RECYCLED — subject exited; the number now belongs to: " + p.CurrentOwnerSummary);
            }
            else
                sb.AppendLine("  no live PID found for this name — provenance unavailable (rerun with a PID)");

            var t = inv.Trace;
            if (t != null)
            {
                sb.AppendLine();
                sb.AppendLine($"TRAFFIC ({inv.WindowHours} h, from the recorded archive):");
                sb.AppendLine($"  connects: {t.ConnectsLast1H} last hour · {t.ConnectsLast24H} in window · ~{t.ConnectsPerHour}/hour · {t.SpawnsLast24H} spawn(s)");
                foreach (var d in t.Destinations.Take(10))
                    sb.AppendLine($"  dest: {d.Display}");
                if (t.BeaconVerdict != null)
                    sb.AppendLine($"  beacon: {t.BeaconVerdict} — {t.BeaconDetail}");
                sb.AppendLine(t.BytesAttributable
                    ? $"  recorded TLS: {FormatBytes(t.TxBytes24H)} up / {FormatBytes(t.RxBytes24H)} down"
                    : "  recorded TLS: not attributable for this match");
                sb.AppendLine($"  live now: {t.LiveConnectionCount} established");
                if (t.ArchiveNote != null)
                    sb.AppendLine($"  note: {t.ArchiveNote}");
            }

            sb.AppendLine();
            sb.AppendLine("BLOCKS:");
            if (inv.BlockHitCount > 0)
                foreach (var h in inv.BlockHits.Take(8))
                    sb.AppendLine($"  {h.Process} → {h.DestIp}:{h.DestPort} ×{h.Count} ({h.MatchKind}), last {h.LastUtc:yyyy-MM-dd HH:mm:ss} UTC");
            else
                sb.AppendLine("  0 hits in the feed's retention window");

            sb.AppendLine();
            sb.AppendLine("EVIDENCE:");
            foreach (var line in inv.Evidence) sb.AppendLine("  " + line);

            sb.AppendLine();
            sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss} by AutoCommand Process Investigation (read-only)");
            return sb.ToString();
        }

        // ── helpers ───────────────────────────────────────────────────────

        private static string ImageNameOf(ProvenanceReport r)
        {
            if (!string.IsNullOrWhiteSpace(r.ExePath))
            {
                try { return Path.GetFileName(r.ExePath); } catch { }
            }
            // Exited subject with only a hinted name; strip any path parts.
            var n = r.Name;
            if (string.IsNullOrWhiteSpace(n) || n.StartsWith("<PID"))
                return null;
            return NormalizeImageName(n);
        }

        private static string NormalizeImageName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return null;
            if (name.Contains('*') || name.Contains('.')) return name;
            return name + ".exe"; // bare "reolink" → "reolink.exe"
        }

        private static int? FindLivePid(string imageName)
        {
            if (string.IsNullOrWhiteSpace(imageName)) return null;
            try
            {
                string bare = imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? imageName.Substring(0, imageName.Length - 4) : imageName;
                var procs = Process.GetProcessesByName(bare);
                if (procs.Length == 0 && imageName.Contains('*'))
                    procs = Process.GetProcesses().Where(pr =>
                    {
                        try { return WildcardMatch(imageName, pr.ProcessName + ".exe"); }
                        catch { return false; }
                    }).ToArray();
                var first = procs.FirstOrDefault();
                return first == null ? null : first.Id;
            }
            catch { return null; }
        }

        private static bool ImageMatches(string image, string imageName)
        {
            if (string.IsNullOrEmpty(image) || string.IsNullOrEmpty(imageName)) return false;
            string file;
            try { file = Path.GetFileName(image); } catch { file = image; }
            return imageName.Contains('*')
                ? WildcardMatch(imageName, file)
                : file.Equals(imageName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool WildcardMatch(string pattern, string text)
        {
            // Only the '*' wildcard is supported here, matching the
            // trace-match convention ('chrome*' → any chrome*.exe).
            static bool Inner(string p, string t)
            {
                int star = p.IndexOf('*');
                if (star < 0) return string.Equals(p, t, StringComparison.OrdinalIgnoreCase);
                string head = p.Substring(0, star), rest = p.Substring(star + 1);
                if (!t.StartsWith(head, StringComparison.OrdinalIgnoreCase)) return false;
                if (rest.Length == 0) return true;
                for (int i = t.Length - rest.Length; i >= star; i--)
                    if (Inner(rest, t.Substring(i))) return true;
                return false;
            }
            return Inner(pattern, text);
        }

        /// <summary>Local-network destination: loopback, RFC1918, link-local,
        /// broadcast or multicast. Anything else counts as external. IPv6 is
        /// kept coarse (loopback + link-local only) — the archive's
        /// destinations are overwhelmingly IPv4.</summary>
        private static bool IsInternalDestination(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return true;
            if (ip.Contains(':')) // IPv6
                return ip == "::1" || ip.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase);

            var parts = ip.Split('.');
            if (parts.Length != 4 || !parts.All(s => byte.TryParse(s, out _))) return true;
            byte a = byte.Parse(parts[0]), b = byte.Parse(parts[1]);

            if (a == 127 || a == 10 || a == 255 || a == 0) return true;         // loopback, private, broadcast, this-network
            if (a == 192 && b == 168) return true;                              // RFC1918
            if (a == 172 && b is >= 16 and <= 31) return true;                  // RFC1918
            if (a == 169 && b == 254) return true;                              // link-local
            if (a >= 224) return true;                                          // multicast/reserved (incl. 255.255.255.255)
            return false;
        }

        private static string FormatBytes(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1048576 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / 1048576.0:F1} MB"
        };
    }
}
