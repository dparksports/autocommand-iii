using AutoCommand.NetWatch.Core;
using System;
using AutoCommand.Contain.Core;
using System.Collections.Generic;
using System.Linq;

namespace AutoCommand.Contain.Core
{
    /// <summary>
    /// Gathers the four live inputs the containment join needs and runs
    /// BlockContainmentVerifier. The single gatherer both consumers share —
    /// the verify_block_containment bridge tool and the Blocks tab's
    /// containment column — so the screen and the AI can never disagree
    /// about whether a block is holding.
    ///
    /// Kernel sockets first, owner identity second: the monitor snapshot's
    /// ordering rule, so a pid reused in between cannot attach the previous
    /// owner's start time to a live socket row. Every input degrades — WMI
    /// down means sockets join by pid with unknown start times, an
    /// unreadable archive means an empty flow list — and the verdicts carry
    /// what they were based on.
    /// </summary>
    public static class BlockContainmentReader
    {
        public sealed class Snapshot
        {
            public DateTime TakenUtc;
            public List<BlockHitFeedService.HitAggregate> Hits;
            public List<BlockedTarget> Inventory;
            public List<BlockContainmentVerifier.Instance> Instances;
            public string Note = "";   // feed/inventory errors, surfaced by both consumers
        }

        /// <summary>
        /// Gathers the four live inputs and runs the join. BLOCKS on the
        /// caller's thread (inventory fetch, full Win32_Process scan, archive
        /// read — hundreds of ms on a busy box), so it must run OFF the UI
        /// thread: it blocks on a task with SynchronizationContext-continuation
        /// semantics, and a direct UI-thread call would deadlock. Callers on
        /// a UI thread: `await Task.Run(() => Take(...))` (what the Blocks
        /// tab does) — this fails fast with a clear message instead of
        /// hanging. Handlers on the bridge's background threads call it
        /// directly.
        /// </summary>
        public static Snapshot Take(int minutes = 60)
        {
            if (System.Threading.SynchronizationContext.Current != null)
                throw new InvalidOperationException(
                    "BlockContainmentReader.Take blocks and must not run on a UI thread — " +
                    "call it via Task.Run from UI code.");

            var snap = new Snapshot { TakenUtc = DateTime.UtcNow };

            // Idempotent — every containment reader keeps the feed alive,
            // the same contract the bridge's EnsureFeedStarted follows.
            BlockHitFeedService.Instance.Start();

            snap.Inventory = FirewallService.Instance.GetBlockedInventoryAsync().GetAwaiter().GetResult();
            snap.Hits = BlockHitFeedService.Instance.Aggregates(minutes, 200);

            var conns = TcpConnectionTable.GetEstablished();
            var owners = new Dictionary<int, (string Name, DateTime? StartedUtc)>();
            try
            {
                using var s = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, Name, CreationDate FROM Win32_Process");
                foreach (System.Management.ManagementObject o in s.Get())
                {
                    DateTime? started = null;
                    try
                    {
                        if (o["CreationDate"] is string dmtf && dmtf.Length > 0)
                            started = System.Management.ManagementDateTimeConverter.ToDateTime(dmtf).ToUniversalTime();
                    }
                    catch { /* unreadable date — the name still joins */ }
                    owners[Convert.ToInt32(o["ProcessId"])] = (o["Name"]?.ToString() ?? "?", started);
                }
            }
            catch { /* WMI down — sockets join by pid with unknown start times */ }

            var sockets = conns.Select(c =>
            {
                owners.TryGetValue(c.Pid, out var owner);
                return new BlockContainmentVerifier.LiveSocket
                {
                    Pid = c.Pid,
                    RemoteIp = c.RemoteIp,
                    RemotePort = c.RemotePort,
                    ProcessName = owner.Name,
                    ProcessStartedUtc = owner.StartedUtc,
                };
            }).ToList();

            // Archive read bounded by the oldest block provenance; the
            // verifier applies the per-instance t0 on top of this.
            DateTime since = snap.Inventory.Where(t => t.Created.HasValue).Select(t => t.Created.Value)
                                  .DefaultIfEmpty(DateTime.UtcNow.AddMinutes(-minutes)).Min();
            var flows = TransmissionsArchiveService.Read(200, null, null, since);

            snap.Instances = BlockContainmentVerifier.Evaluate(snap.Hits, snap.Inventory, sockets, flows);

            var feed = BlockHitFeedService.Instance;
            if (feed.WatcherError != null) snap.Note = $"Feed note: {feed.WatcherError}";
            else if (feed.LastInventoryError != null) snap.Note = $"Inventory note: {feed.LastInventoryError}";
            return snap;
        }
    }
}
