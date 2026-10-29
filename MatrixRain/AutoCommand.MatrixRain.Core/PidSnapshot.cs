using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using AutoCommand.Engines.Watch;

namespace AutoCommand.Matrix
{
    /// <summary>One column of the rain — the per-PID aggregation of every
    /// tracked (process → remote IP) conversation row sharing that PID.</summary>
    public class PidSnapshot
    {
        public int Pid;
        public string Name;
        public long TxPackets;
        public long TxBytes;
        public long RxPackets;
        public long RxBytes;
        public bool Blocked;        // firewall says this PID's traffic is stopped
        public DateTime LastSeenUtc;
        public bool Exited;         // process is gone (row kept for attribution)
        public int Connections;     // tracked remote-IP conversations
        public int Attempts;        // new conversations since the previous
                                    // snapshot — blocked PIDs roll red on these
    }

    /// <summary>
    /// The in-app rain feed: ATTACHES to the Process Monitor's live shared
    /// row table — the very rows the DataGrid shows — and aggregates them
    /// into per-PID snapshots every ~100 ms. It owns no engines of its own:
    /// the monitor tab's Sysmon watcher and raw-socket sniffer stay the
    /// single source of truth, so nothing is double-counted and no second
    /// raw socket is opened. (The standalone overlay builds its own engines;
    /// this is the same contract from the consuming side.)
    /// </summary>
    public sealed class MatrixRainFeed : IDisposable
    {
        public IReadOnlyList<PidSnapshot> Latest => _latest;
        private volatile IReadOnlyList<PidSnapshot> _latest = Array.Empty<PidSnapshot>();

        private readonly ConcurrentDictionary<string, SvchostMonitorItem> _tracked;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<int, int> _lastConnections = new();

        public MatrixRainFeed(ConcurrentDictionary<string, SvchostMonitorItem> trackedIps)
        {
            _tracked = trackedIps;
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _timer.Tick += (s, e) => BuildSnapshot();
        }

        public void Start()
        {
            BuildSnapshot();
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        private void BuildSnapshot()
        {
            try
            {
                var byPid = new Dictionary<int, PidSnapshot>();
                foreach (var item in _tracked.Values)
                {
                    int pid = item.ProcessId;
                    if (!byPid.TryGetValue(pid, out var s))
                    {
                        s = new PidSnapshot
                        {
                            Pid = pid,
                            Name = DisplayName(item)
                        };
                        byPid[pid] = s;
                    }
                    s.TxPackets += item.TxPackets;
                    s.TxBytes += item.TxBytes;
                    s.RxPackets += item.RxPackets;
                    s.RxBytes += item.RxBytes;
                    s.Connections++;
                    if (item.LastSeen > s.LastSeenUtc) s.LastSeenUtc = item.LastSeen;
                    if (item.IsTrafficBlocked) s.Blocked = true;
                    if (item.HasExited) s.Exited = true;
                }

                // Blocked-attempt pulses: a blocked PID's row count only
                // grows on new connection attempts (its packets never reach
                // the wire) — each new row rolls a red glyph, exactly as in
                // the standalone overlay.
                foreach (var s in byPid.Values)
                {
                    _lastConnections.TryGetValue(s.Pid, out int prev);
                    s.Attempts = s.Blocked ? Math.Max(0, s.Connections - prev) : 0;
                    _lastConnections[s.Pid] = s.Connections;
                }

                _latest = byPid.Values.OrderByDescending(p => p.TxBytes).ToList();
            }
            catch
            {
                // A row mid-mutation must never take the feed down.
            }
        }

        private static string DisplayName(SvchostMonitorItem item)
        {
            if (!string.IsNullOrEmpty(item.ProcessName)) return item.ProcessName;
            if (!string.IsNullOrEmpty(item.ImagePath)) return Path.GetFileName(item.ImagePath);
            return "pid " + item.ProcessId;
        }

        public void Dispose() => _timer.Stop();
    }
}
