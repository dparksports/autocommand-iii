using AutoCommand.NetWatch.Core;
using System;
using AutoCommand.Contain.Core;
using AutoCommand.Intercept.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoCommand.Contain.Core
{
    /// <summary>
    /// The containment join behind verify_block_containment: proves a blocked
    /// process is SILENT, not just still trying. A block hit (Sysmon EID 3)
    /// is an attempt, never an outcome — the kernel drops the connect and
    /// nothing logs whether anything else got through — so no single source
    /// proves silence. This three-way join does: block attempts × live
    /// kernel sockets × archived TLS bytes since the block was created (t0).
    ///
    /// Verdicts, per process instance (image + pid):
    ///   leak        — an established socket to a blocked destination whose
    ///                 owning process started after t0, or archived bytes to
    ///                 a blocked destination since t0. Escalate.
    ///   stale-flow  — a socket whose owning process predates t0 (or whose
    ///                 start time is unknown): app-layer firewall rules never
    ///                 sever flows that were already up when the rule landed,
    ///                 so it may still be alive. Sever or restart.
    ///   contained   — attempts in the window, no joined socket, no bytes
    ///                 since t0: the block is dropping everything.
    ///
    /// Identity rule (the same one BuildMonitorSnapshot enforces): a bare
    /// pid is not an identity. A live socket only joins when its owning
    /// process started before the instance's last attempt; an archive row
    /// only joins when its image name matches. A reused pid is flagged,
    /// never accused.
    ///
    /// Known limit (deliberate, documented): ATTEMPTS are grouped by
    /// pid + image only. If a pid is reused while the hit-feed window still
    /// contains the previous instance's attempts, both processes' attempts
    /// merge into one instance (shared t0, summed count). The socket and
    /// archive joins keep their start-time/name guards, so this can only
    /// overcount attempts — it cannot turn a contained instance into a
    /// leak. With a 200-aggregate feed window this needs a reuse inside
    /// the look-back minutes to matter.
    ///
    /// Pure logic over plain rows — the bridge handler feeds it live data,
    /// ContainSelfTest feeds it synthetic rows. No I/O, no elevation.
    /// </summary>
    public static class BlockContainmentVerifier
    {
        public const string Contained = "contained";
        public const string StaleFlow = "stale-flow";
        public const string Leak = "leak";

        /// <summary>One established kernel socket, with the owner identity
        /// resolved at snapshot time. StartedUtc null = owner exited or WMI
        /// unavailable — the socket still counts, undated.</summary>
        public sealed class LiveSocket
        {
            public int Pid;
            public string RemoteIp;
            public int RemotePort;
            public string ProcessName;
            public DateTime? ProcessStartedUtc;
        }

        public sealed class Instance
        {
            public string Image;
            public int Pid;
            public string Process;             // basename of Image
            public bool AppBlocked;            // image itself is blocked (stricter join)
            public int Attempts;
            public DateTime FirstUtc, LastUtc;
            public List<string> BlockedIps = new();     // blocked destinations hit
            public DateTime T0;
            public bool T0Approximate;         // rule had no provenance — fell back to first hit
            public string Verdict;
            public bool PidReused;             // a live pid match was rejected as a different process
            public List<string> SocketEvidence = new();
            public int FlowsSinceT0;
            public long BytesSinceT0;
            public string Note = "";
        }

        /// <summary>App-block match, the same rule the feed applies: exact
        /// path, or the exe name (a blocked name at a sibling path still
        /// counts). Public so the handler's quiet-target roll-up agrees with
        /// the instance join.</summary>
        public static bool IsAppBlockMatch(BlockedTarget t, string image)
        {
            if (t?.Kind != "app" || string.IsNullOrEmpty(image)) return false;
            return string.Equals(t.AppPath, image, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(t.Target, Path.GetFileName(image), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when any joined socket's owner provably started at
        /// or before t0 (the dated form of a stale flow).</summary>
        private static bool SocketOwnerPredatesBlock(List<LiveSocket> sockets, Instance inst) =>
            sockets.Any(s => s.Pid == inst.Pid && s.ProcessStartedUtc.HasValue &&
                             s.ProcessStartedUtc.Value <= inst.T0);

        /// <summary>
        /// Worst instance a target is responsible for — the Blocks tab's
        /// per-row roll-up. storeKind is the BlocksStore kind ("ip" or
        /// "proc"), key the IP or the full image path. Null when no instance
        /// touched the target in this snapshot (quiet).
        /// </summary>
        public static Instance WorstForTarget(List<Instance> instances, string storeKind, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            Instance worst = null;
            foreach (var i in instances)
            {
                bool match = storeKind == "ip"
                    ? i.BlockedIps.Any(b => string.Equals(b, key, StringComparison.OrdinalIgnoreCase))
                    : string.Equals(i.Image, key, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(Path.GetFileName(i.Image ?? ""), Path.GetFileName(key),
                          StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
                if (worst == null || Rank(i) > Rank(worst)) worst = i;
            }
            return worst;
        }

        private static int Rank(Instance i) =>
            i.Verdict == Leak ? 2 : i.Verdict == StaleFlow ? 1 : 0;

        /// <summary>Enabled blocks no instance touched in this snapshot —
        /// the quiet roll-up, owned HERE so the bridge tool and any future
        /// consumer apply one rule (the same reason IsAppBlockMatch is
        /// public). Disabled/paused rules are never listed: nothing to
        /// verify. The caller supplies presentation (JSON, rows).</summary>
        public static List<BlockedTarget> QuietTargets(
            List<BlockedTarget> inventory, List<Instance> instances)
        {
            var hitIps = instances.SelectMany(i => i.BlockedIps)
                                  .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hitImages = instances.Select(i => i.Image).ToList();
            return inventory.Where(t => t.EnabledRules > 0 && !Referenced(t, hitIps, hitImages)).ToList();
        }

        private static bool Referenced(BlockedTarget t,
                                       HashSet<string> hitIps, List<string> hitImages) =>
            t.Kind == "ip"
                ? hitIps.Contains(t.Ip)
                : hitImages.Any(img => IsAppBlockMatch(t, img));

        public static List<Instance> Evaluate(
            List<BlockHitFeedService.HitAggregate> hits,
            List<BlockedTarget> inventory,
            List<LiveSocket> sockets,
            List<TransmissionRecord> flows)
        {
            var activeIp = new HashSet<string>(
                inventory.Where(t => t.Kind == "ip" && t.EnabledRules > 0 && !string.IsNullOrEmpty(t.Ip))
                         .Select(t => t.Ip),
                StringComparer.OrdinalIgnoreCase);

            bool AppBlocked(string image) =>
                inventory.Any(t => t.EnabledRules > 0 && IsAppBlockMatch(t, image));

            var result = new List<Instance>();

            foreach (var g in hits.GroupBy(h => new { h.Image, h.Pid }))
            {
                var rows = g.ToList();
                var inst = new Instance
                {
                    Image = g.Key.Image,
                    Pid = g.Key.Pid,
                    Process = Path.GetFileName(g.Key.Image),
                    AppBlocked = AppBlocked(g.Key.Image),
                    Attempts = rows.Sum(r => r.Count),
                    FirstUtc = rows.Min(r => r.FirstUtc),
                    LastUtc = rows.Max(r => r.LastUtc),
                    BlockedIps = rows.Where(r => !string.IsNullOrEmpty(r.DestIp))
                                     .Select(r => r.DestIp).Distinct().ToList(),
                };

                // t0: earliest rule-creation time among the targets this
                // instance hit. No provenance anywhere (legacy rules) → the
                // first attempt, flagged approximate.
                var t0Candidates = inventory
                    .Where(t => t.EnabledRules > 0 && t.Created.HasValue &&
                                (inst.AppBlocked
                                    ? IsAppBlockMatch(t, inst.Image)
                                    : rows.Any(r => string.Equals(r.DestIp, t.Ip, StringComparison.OrdinalIgnoreCase))))
                    .Select(t => t.Created.Value)
                    .ToList();
                if (t0Candidates.Count > 0)
                {
                    inst.T0 = t0Candidates.Min();
                }
                else
                {
                    inst.T0 = inst.FirstUtc;
                    inst.T0Approximate = true;
                }

                // ── sockets ── an app-blocked process must have NO socket
                // anywhere routable; an ip-blocked one only answers for
                // blocked destinations (sockets elsewhere are allowed).
                bool SocketConcern(LiveSocket s) =>
                    inst.AppBlocked || activeIp.Contains(s.RemoteIp);

                bool stale = false, leak = false, undatedSocket = false;
                foreach (var s in sockets)
                {
                    if (s.Pid != inst.Pid || !SocketConcern(s)) continue;
                    if (TcpConnectionTable.IsLocalOrNonRoutable(s.RemoteIp)) continue;

                    // Identity guard: a socket owner that started after the
                    // last attempt is a different process under a reused pid.
                    if (s.ProcessStartedUtc.HasValue && s.ProcessStartedUtc.Value > inst.LastUtc)
                    {
                        inst.PidReused = true;
                        continue;
                    }

                    inst.SocketEvidence.Add($"{s.RemoteIp}:{s.RemotePort}");
                    if (s.ProcessStartedUtc.HasValue && s.ProcessStartedUtc.Value > inst.T0)
                        leak = true;
                    else
                    {
                        stale = true;
                        if (!s.ProcessStartedUtc.HasValue) undatedSocket = true;
                    }
                }

                // ── archived bytes ── joins on (pid, image basename): the
                // archive spans pid lifetimes, the name keeps the identity.
                string instBase = Path.GetFileName(inst.Image);
                foreach (var f in flows)
                {
                    if (f.Pid != inst.Pid) continue;
                    if (!string.Equals(Path.GetFileName(f.Image ?? ""), instBase, StringComparison.OrdinalIgnoreCase)) continue;
                    if (f.TsUtc < inst.T0) continue;
                    if (!inst.AppBlocked && !activeIp.Contains(f.RemoteIp ?? "")) continue;

                    long bytes = f.ClientBytes + f.ServerBytes;
                    if (bytes <= 0) continue;
                    inst.FlowsSinceT0++;
                    inst.BytesSinceT0 += bytes;
                    leak = true;
                }

                if (leak) inst.Verdict = Leak;
                else if (stale) inst.Verdict = StaleFlow;
                else inst.Verdict = Contained;

                if (inst.Verdict == Leak && inst.SocketEvidence.Count == 0 && inst.FlowsSinceT0 > 0)
                    inst.Note = "Archived TLS bytes flowed to a blocked destination after the block was created.";
                else if (inst.Verdict == StaleFlow)
                    inst.Note = undatedSocket && !SocketOwnerPredatesBlock(sockets, inst)
                        ? "Socket owner's start time is unknown (exited or WMI unavailable) — treated as possibly predating the block."
                        : "Firewall rules never sever flows established before the block — this socket may still be alive. Sever or restart the process.";
                if (inst.PidReused && inst.Verdict == Contained)
                    inst.Note = string.IsNullOrEmpty(inst.Note)
                        ? "A live socket under this pid belonged to a newer process (pid reuse) — not attributed to this instance."
                        : inst.Note + " A reused-pid socket was also rejected.";

                result.Add(inst);
            }

            return result
                .OrderByDescending(i => i.Verdict == Leak)
                .ThenByDescending(i => i.Verdict == StaleFlow)
                .ThenByDescending(i => i.Attempts)
                .ToList();
        }
    }
}
