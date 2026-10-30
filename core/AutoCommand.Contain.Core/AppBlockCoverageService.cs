using AutoCommand.NetWatch.Core;
using System;
using AutoCommand.Contain.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AutoCommand.Contain.Core
{
    /// <summary>
    /// Keeps name-blocked apps blocked wherever they run from. Windows Firewall
    /// enforces an app rule by exact image path, and binaries move — WebView2
    /// lives in a versioned folder that is deleted on every runtime update, so
    /// a block pinned to the old path goes inert while the process keeps
    /// transmitting from the new one. This service closes that gap:
    ///
    ///   · watches Sysmon EID 1 (process create) — when an image whose exe NAME
    ///     is blocked appears at a path no rule covers, a sibling rule for that
    ///     path is added immediately, at the moment the new binary first runs;
    ///   · sweeps running processes at startup (covers processes created before
    ///     the watcher started);
    ///   · periodically retires dead-path rules once a live sibling exists
    ///     (FirewallService.RetireDeadAppBlockRulesAsync — never the last rule
    ///     of a target).
    ///
    /// Auto-extending only ever blocks MORE — a malware binary name-squatting a
    /// blocked exe in another folder gets blocked too — so no confirmation is
    /// needed. Every extension is appended to app-block-extensions.jsonl beside
    /// the block-hits archive (house persistence pattern) so the audit trail
    /// survives restarts.
    /// </summary>
    public sealed class AppBlockCoverageService
    {
        public static AppBlockCoverageService Instance => _instance ??= new AppBlockCoverageService();
        private static AppBlockCoverageService _instance;

        private const int MaxHistory = 500;
        private static readonly TimeSpan InventoryRefresh = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan FirstSweepDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan GcInterval = TimeSpan.FromHours(6);

        /// <summary>One coverage extension / retirement, also persisted as JSONL.</summary>
        public sealed class CoverageEvent
        {
            public string TimeUtc { get; set; }
            public string Action { get; set; }      // "extended" | "retired" | "startup-sweep-extended"
            public string Exe { get; set; }
            public string Path { get; set; }
            public string RuleName { get; set; }
            public string Detail { get; set; }
        }

        private readonly object _gate = new();
        private HashSet<string> _blockedNames = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _coveredPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CoverageEvent> _history = new();
        private EventLogWatcher _watcher;
        private Timer _refreshTimer, _gcTimer;
        private bool _running, _dirty;

        public string WatcherError { get; private set; }
        public DateTime LastRefreshUtc { get; private set; }
        public bool IsRunning { get { lock (_gate) return _running; } }

        /// <summary>Persistence root; settable for offline self-tests (house pattern).</summary>
        public static string StoreDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCommand");

        private static string StorePath => Path.Combine(StoreDirectory, "app-block-extensions.jsonl");

        private AppBlockCoverageService() { }

        /// <summary>Idempotent: safe to call from startup, the Blocks tab, bridge handlers.</summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_running) return;
                _running = true;
            }
            Load();
            _ = RefreshCacheAsync();
            _ = Task.Run(StartupSweepAsync);

            try
            {
                var query = new EventLogQuery("Microsoft-Windows-Sysmon/Operational",
                    PathType.LogName, "*[System[(EventID=1)]]");
                _watcher = new EventLogWatcher(query);
                _watcher.EventRecordWritten += Watcher_ProcessCreated;
                _watcher.Enabled = true;
            }
            catch (EventLogNotFoundException)
            {
                WatcherError = "Sysmon is not installed — live coverage extension is inactive " +
                    "(startup sweep and retire pass still run). Install Sysmon from the Sysmon Audit tab.";
            }
            catch (Exception ex)
            {
                WatcherError = $"Failed to start process-create watcher: {ex.Message}";
            }

            _refreshTimer = new Timer(_ => _ = RefreshCacheAsync(), null,
                InventoryRefresh, InventoryRefresh);
            _gcTimer = new Timer(_ => _ = RunRetirePassAsync(), null,
                FirstSweepDelay + TimeSpan.FromSeconds(30), GcInterval);
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (!_running) return;
                _running = false;
                _refreshTimer?.Dispose(); _refreshTimer = null;
                _gcTimer?.Dispose(); _gcTimer = null;
            }
            _watcher?.Dispose();
            _watcher = null;
            SaveIfDirty();
        }

        // ── cache ─────────────────────────────────────────────────────────

        private async Task RefreshCacheAsync()
        {
            try
            {
                var inv = await FirewallService.Instance.GetBlockedInventoryAsync();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in inv)
                {
                    if (t.Kind != "app" || t.EnabledRules == 0) continue;   // paused blocks must not auto-extend
                    names.Add(t.Target);
                    if (!string.IsNullOrEmpty(t.AppPath))
                        paths.Add(t.AppPath.ToLowerInvariant());
                }
                lock (_gate)
                {
                    _blockedNames = names;
                    _coveredPaths = paths;
                    LastRefreshUtc = DateTime.UtcNow;
                }
            }
            catch { /* cache stays at last good snapshot; next tick retries */ }
        }

        // ── triggers ──────────────────────────────────────────────────────

        private void Watcher_ProcessCreated(object sender, EventRecordWrittenEventArgs e)
        {
            try
            {
                if (e.EventRecord == null) return;
                var doc = XDocument.Parse(e.EventRecord.ToXml());
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
                var eventData = doc.Root?.Element(ns + "EventData");
                if (eventData == null) return;

                string image = "", parent = "";
                foreach (var data in eventData.Elements(ns + "Data"))
                {
                    switch (data.Attribute("Name")?.Value)
                    {
                        case "Image": image = data.Value; break;
                        case "ParentImage": parent = data.Value; break;
                    }
                }
                if (string.IsNullOrEmpty(image)) return;

                _ = EnsureCoveredAsync(image, "process create", parent);
            }
            catch { /* a malformed record never takes the watcher down */ }
        }

        /// <summary>
        /// Startup sweep: processes already running when the service starts were
        /// never seen by the EID 1 watcher — cover their paths too. MainModule
        /// needs access the app may lack for elevated processes; per-process
        /// failures are skipped silently.
        /// </summary>
        private async Task StartupSweepAsync()
        {
            try
            {
                await Task.Delay(FirstSweepDelay);   // let the cache fill first
                foreach (var proc in Process.GetProcesses())
                {
                    string path = null;
                    try { path = proc.MainModule?.FileName; }
                    catch { /* elevated / exited / protected — skip */ }
                    finally { proc.Dispose(); }
                    if (!string.IsNullOrEmpty(path))
                        await EnsureCoveredAsync(path, "startup sweep", null);
                }
            }
            catch { /* sweep is best-effort */ }
        }

        /// <summary>
        /// The shared decision: exe name blocked, this path not covered → add a
        /// sibling rule with full provenance. Cheap two-set lookup when nothing
        /// matches, so it is safe to call for every process create.
        /// </summary>
        public async Task EnsureCoveredAsync(string imagePath, string trigger, string parentImage)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath)) return;
                string exe = Path.GetFileName(imagePath);
                string pathKey = imagePath.ToLowerInvariant();
                lock (_gate)
                {
                    if (!_blockedNames.Contains(exe)) return;
                    if (_coveredPaths.Contains(pathKey)) return;
                }

                string utc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
                string desc = $"[blocked {utc} UTC] {exe} — coverage auto-extended to new path " +
                    $"({trigger}; app updated or moved) · parent: {parentImage ?? "unknown"} · " +
                    $"AutoCommand AppBlockCoverageService";

                var (created, _, ruleName) = await FirewallService.Instance.AddAppBlockWithPathAsync(imagePath, desc);
                if (created > 0)
                {
                    Record(new CoverageEvent
                    {
                        TimeUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        Action = trigger == "startup sweep" ? "startup-sweep-extended" : "extended",
                        Exe = exe,
                        Path = imagePath,
                        RuleName = ruleName ?? "",
                        Detail = $"trigger: {trigger} · parent: {parentImage ?? "unknown"}",
                    });
                    await RefreshCacheAsync();
                }
            }
            catch { /* never let a coverage check disturb the caller */ }
        }

        /// <summary>
        /// Called the moment a target is unblocked: drops the exe name from
        /// the cached blocked-name set so a process-create arriving before
        /// the next 30 s inventory refresh cannot re-extend the block the
        /// user just lifted (for a constantly-spawning exe that window is a
        /// guaranteed resurrection, not a race). The next RefreshCacheAsync
        /// re-reads the firewall and restores truth — including a surviving
        /// sibling's rules, should any remain.
        /// </summary>
        public void NotifyUnblocked(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return;
            lock (_gate)
            {
                _blockedNames.Remove(exeName);
            }
        }

        private async Task RunRetirePassAsync()
        {
            try
            {
                var (targets, retired) = await FirewallService.Instance.RetireDeadAppBlockRulesAsync();
                if (retired > 0)
                {
                    Record(new CoverageEvent
                    {
                        TimeUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        Action = "retired",
                        Exe = "",
                        Path = "",
                        RuleName = "",
                        Detail = $"{retired} dead-path rule(s) retired across {targets} app target(s)",
                    });
                    await RefreshCacheAsync();
                }
            }
            catch { /* next pass retries */ }
        }

        // ── history + persistence (house JSONL pattern) ───────────────────

        public List<CoverageEvent> RecentEvents(int max = 100)
        {
            lock (_gate) return _history.TakeLast(max).Reverse().ToList();
        }

        private void Record(CoverageEvent e)
        {
            lock (_gate)
            {
                _history.Add(e);
                if (_history.Count > MaxHistory) _history.RemoveRange(0, _history.Count - MaxHistory);
                _dirty = true;
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(StorePath)) return;
                lock (_gate)
                {
                    foreach (string line in File.ReadAllLines(StorePath))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var e = JsonSerializer.Deserialize<CoverageEvent>(line);
                        if (e != null) _history.Add(e);
                    }
                    if (_history.Count > MaxHistory) _history.RemoveRange(0, _history.Count - MaxHistory);
                }
            }
            catch { /* unreadable history is not fatal */ }
        }

        private void SaveIfDirty()
        {
            try
            {
                List<CoverageEvent> snapshot;
                lock (_gate)
                {
                    if (!_dirty) return;
                    snapshot = _history.ToList();
                    _dirty = false;
                }
                Directory.CreateDirectory(StoreDirectory);
                File.WriteAllLines(StorePath, snapshot.Select(e => JsonSerializer.Serialize(e)));
            }
            catch { /* persistence is best-effort */ }
        }
    }
}
