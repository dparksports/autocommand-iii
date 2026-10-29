using System.Linq;
using System.Windows;

using AutoCommand.Agent.Core;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.Intercept.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;

using AutoCommand.Services;
using AutoCommand.Engines.Core;

namespace AutoCommand
{
    public partial class App : Application
    {
        // ── upgrade carry-over notice ────────────────────────────────────────
        // All state lives outside the program folder (%LOCALAPPDATA%\AutoCommand
        // for the blocks ledger, preferences and traces; %ProgramData%\AutoCommand
        // for the machine-wide caches), so an update that unzips a new folder
        // keeps everything. The marker file makes that explicit: the first run
        // from a changed install folder says so in one dialog, then goes quiet.
        private static void NotifyIfInstallMoved()
        {
            const string markerSubdir = "install";
            try
            {
                string stateDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoCommand");
                string markerPath = System.IO.Path.Combine(stateDir, markerSubdir, "last-exe-path.txt");
                string current = Environment.ProcessPath
                    ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

                bool hasPriorState = System.IO.Directory.Exists(stateDir) &&
                    System.IO.Directory.EnumerateFileSystemEntries(stateDir).Any(
                        e => !string.Equals(System.IO.Path.GetFileName(e), markerSubdir,
                             System.StringComparison.OrdinalIgnoreCase));
                if (!hasPriorState)
                {
                    WriteInstallMarker(markerPath, current); // fresh machine: nothing to announce
                    return;
                }

                string previous = System.IO.File.Exists(markerPath)
                    ? System.IO.File.ReadAllText(markerPath).Trim()
                    : null;
                if (!string.Equals(previous, current, System.StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "AutoCommand was updated or moved.\n\n" +
                        "Your existing data is untouched and still in use — every block, the undo " +
                        "ledger (Restore Internet), your preferences, traces and caches. It all lives " +
                        "outside the program folder, in:\n" + stateDir +
                        "\n\nNothing was reset, and deleting the old program folder is safe.",
                        "AutoCommand — updated", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                WriteInstallMarker(markerPath, current);
            }
            catch
            {
                // the marker is best-effort; it must never block startup
            }
        }

        private static void WriteInstallMarker(string markerPath, string current)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(markerPath));
            System.IO.File.WriteAllText(markerPath, current);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // The F# watch engines are app-agnostic: give them AutoCommand's
            // identity (data root, rule prefixes, port base) before anything
            // touches them (same call autoscope makes at startup).
            AutoCommand.Engines.Watch.WatchConfig.Configure(
                AutoCommand.Engines.Core.Identity.autocommand());

            // ── headless agent mode (no UI, no dialogs, no theme) ────────
            // --agent-headless [sec]
            //      start an AI session without the UI (scripts/tests).
            //      NOTE: the exe carries requireAdministrator, so MCP clients
            //      must NEVER spawn it — they spawn the small asInvoker
            //      proxy (AutoCommand.AgentProxy, ProjectReferenced and
            //      deployed next to this exe) which forwards here.
            var args = e.Args ?? System.Array.Empty<string>();
            if (args.Any(a => string.Equals(a, "--agent-headless", System.StringComparison.OrdinalIgnoreCase)))
            {
                RunAgentHeadless(args);
                return;
            }

            // ── headless OneSettings watchdog path (no UI) ──────────────
            // The \AutoCommand\OneSettingsCadenceWatchdog SYSTEM task (installed
            // from the OneSettings tab with the watchdog box checked) launches
            // AutoCommand.exe with this argument daily 03:30 and at logon to
            // re-apply the saved RefreshCache cadence if Windows servicing
            // reverted it. Silent; every action lands in the machine-readable
            // audit trail under %ProgramData%\AutoCommand\onesettings-cadence.
            if (args.Any(a => string.Equals(a, "onesettings-enforce", System.StringComparison.OrdinalIgnoreCase)))
            {
                RunOneSettingsEnforce();
                return;
            }

            // Upgrade carry-over: state lives outside the program folder, so
            // an update that unzips a new folder keeps every block, the undo
            // ledger, preferences and caches. First run from a changed folder
            // says so — once, one dialog (never on the headless paths above).
            NotifyIfInstallMoved();

            // Apply the saved theme before any window loads (brushes are
            // DynamicResource, so this also re-themes live when changed later)
            Services.ThemeService.ApplyTheme(Services.ThemeService.Saved);

            // Handle UI thread exceptions
            this.DispatcherUnhandledException += (s, args) =>
            {
                LogCrash(args.Exception, "Dispatcher");
                args.Handled = true; // Prevent app from closing
                MessageBox.Show($"A UI error occurred: {args.Exception.Message}\n\nDetails saved to crash.log.", 
                    "AutoCommand Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // Handle background thread exceptions
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is System.Exception ex)
                {
                    LogCrash(ex, "AppDomain");
                }
            };

            // Handle async Task exceptions
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LogCrash(args.Exception, "TaskScheduler");
                args.SetObserved();
            };

            // Finish a Sysmon repair that was waiting for a Windows restart
            FinishSysmonRepairIfPending();

            // Grow the Sysmon channel to its sized retention on machines
            // where Sysmon predates the sizing — the 64 MB default wraps in
            // ~4 h on an active box, which silently shorts the forensic
            // history the PID Inquiry promises. No-op when already sized,
            // not installed, or the session is not elevated.
            //
            // Then the Sysmon archive: prune past retention, backfill
            // everything the live channel still holds, and follow it live.
            // The channel is a rolling window; the archive is what makes
            // "every spawn retroactively traceable" true for weeks.
            PrepareSysmonAtStartup();

            RemoveLegacyAnalyticsRegistryValues();

            // Telemetry sinkhole: when hosts pins point at 127.0.0.1 (sinkhole
            // mode, toggled from the Black Hole tab), start the logging
            // listener immediately so proof accumulates even if that tab is
            // never opened this session. Touching the proof store also wires
            // its recorder to the sinkhole. No sinkhole pins → both are no-ops.

            // Managed disk writes: block store + event journals buffer
            // and flush on one calibratable cadence (Settings shows it)
            BlocksStore.DeferredSaves = true;
            InstrumentMetronome.Instance.Register(new InstrumentSpec
            {
                Id = "disk-flush",
                Name = "Disk flush (managed buffers)",
                Description = "Writes every buffered journal line and pending store rewrite to disk. " +
                              "Between flushes, writes coalesce in memory (bounded by a byte threshold " +
                              "that self-flushes) - per-event disk writes are what this replaces.",
                Cost = InstrumentCost.Light,
                DefaultSeconds = 30,
                MinSeconds = 5,
                MaxSeconds = 600,
                Callback = () =>
                {
                    BufferedAppender.FlushAll();
                    BlocksStore.FlushPending();
                    return Task.CompletedTask;
                },
            });

            // Block gates: close anything that expired while the app was
            // down, then keep watch — an open gate is a promise to re-block
            _ = Agent.Core.BlockGateService.Instance.ReconcileOnStartupAsync();
            InstrumentMetronome.Instance.Register(new InstrumentSpec
            {
                Id = "gate-watch",
                Name = "Block gate watch",
                Description = "Closes expired block gates - temporary agent-requested unblocks whose " +
                              "time window ended - restoring blocking automatically.",
                Cost = InstrumentCost.Light,
                DefaultSeconds = 10,
                MinSeconds = 5,
                MaxSeconds = 60,
                Callback = () => Agent.Core.BlockGateService.Instance.CloseExpiredAsync(),
            });

            SinkholeService.Instance.EnsureRunningForCurrentPins();
            _ = Services.TelemetryProofService.Instance.TotalDenied;

            // Anonymous usage ping (opt-in, off by default): sends the user's
            // answer into effect — consented installs ping once a day, everyone
            // else is never asked again after the bounded offer. Fully inert
            // while UsagePingService.Endpoint is unconfigured. Spec and server
            // code: telemetry/ in the repository.
            _ = Services.UsagePingService.RunStartupFlowAsync();

            // The F# bridge engine + the app's tool surface, then the WPF
            // confirm seam: mutating AI-session asks show the
            // AgentConfirmWindow dialog at the app's UI. Headless hosts Wire
            // without a Ui — their mutating tools are denied with the
            // plain-language note, exactly as before.
            Agent.Core.BridgeHost.Wire();
            AutoCommand.Engines.Bridge.AgentBridgeService.Ui = new Services.AgentBridgeUI();

            // Auto-started AI session (opt-in from Settings → AI access): the
            // consent was given once at the checkbox; every launch honors it.
            // Read-only unless the mutating preference is separately on.
            if (AgentAutostartPrefOn())
            {
                var (ok, message) = AgentBridgeService.Instance.Start(
                    allowMutating: AgentMutatingPrefOn());
                if (ok)
                    AgentBridgeService.Instance.LogActivity("session", "started (auto)",
                        $"mutating {(AgentMutatingPrefOn() ? "on" : "off")}");
            }
        }

        private static string AgentPrefPath(string name) => System.IO.Path.Combine(
            AgentBridgeService.AgentDir, name);

        private static bool AgentAutostartPrefOn()
        {
            try { return System.IO.File.ReadAllText(AgentPrefPath("autostart_pref.txt")).Trim() == "on"; }
            catch { return false; }
        }

        private static bool AgentMutatingPrefOn()
        {
            try { return System.IO.File.ReadAllText(AgentPrefPath("mutating_pref.txt")).Trim() == "on"; }
            catch { return false; }
        }

        // Headless AI session: same bridge the Settings card starts, minus the
        // UI. Runs until the given number of seconds elapse (0 = until this
        // console's stdin closes or the process is killed). Mutating tools are
        // always off here — nothing can confirm an action without the app UI.
        private void RunOneSettingsEnforce()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                // Engine handles the no-saved-state case as a no-op (normal
                // after a restore), and audits both success and failure — a
                // failed watchdog run must never block app shutdown.
                new OneSettingsCadence.CadenceEngine().Enforce();
            }
            catch { /* audited inside the engine where possible; always exit */ }
            Shutdown();
        }

        private void RunAgentHeadless(string[] args)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Single-instance guard: a wake-knock while the interactive app is
            // already running must not spawn a second bridge fighting over
            // session.json. Exit silently — the proxy's fallback guidance then
            // tells the user to start the session from Settings.
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("AutoCommand"))
                    using (p)
                    {
                        if (p.Id != System.Environment.ProcessId)
                        {
                            System.Console.WriteLine("another AutoCommand instance is running — headless session skipped");
                            Shutdown();
                            return;
                        }
                    }
            }
            catch { /* enumeration failure should not block the session */ }

            int seconds = 0;
            int flagIndex = System.Array.FindIndex(args,
                a => string.Equals(a, "--agent-headless", System.StringComparison.OrdinalIgnoreCase));
            if (flagIndex + 1 < args.Length && int.TryParse(args[flagIndex + 1], out int parsed) && parsed > 0)
                seconds = parsed;

            var (ok, message) = AgentBridgeService.Instance.Start(allowMutating: false);
            System.Console.WriteLine(ok
                ? $"AutoCommand agent session active ({message}) — Ctrl+C or close stdin to stop."
                : "agent session failed: " + message);

            if (ok)
            {
                if (seconds > 0) System.Threading.Thread.Sleep(TimeSpan.FromSeconds(seconds));
                else while (System.Console.In.ReadLine() != null) { } // until stdin closes
                AgentBridgeService.Instance.Stop();
            }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // a live session must never outlive the app: the session file is
            // what grants access, so it dies here even on a crash-next-launch
            AgentBridgeService.Instance.Stop();
            Agent.Core.BlockGateService.Instance.CloseAllAsync("gate-exit").GetAwaiter().GetResult();
            BufferedAppender.FlushAll();
            BlocksStore.FlushPending();
            InstrumentMetronome.Instance.Stop();
            base.OnExit(e);
        }

        // Older builds stored a Google Analytics consent flag and a client-id
        // GUID under HKCU\Software\AutoCommand. Usage telemetry was removed;
        // delete the leftovers so no orphaned tracking id survives.
        // Idempotent — no-op once clean.
        private void RemoveLegacyAnalyticsRegistryValues()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\AutoCommand", writable: true);
                key?.DeleteValue("EnableAnalytics", throwOnMissingValue: false);
                key?.DeleteValue("ClientId", throwOnMissingValue: false);
            }
            catch { /* registry unavailable — nothing to clean */ }
        }

        private async void FinishSysmonRepairIfPending()
        {
            try
            {
                var installer = new SysmonInstallerService();
                var (verified, message) = await installer.CompletePendingRepairIfAnyAsync();
                if (message == null) return; // no repair was pending

                if (verified)
                {
                    MessageBox.Show($"Sysmon repair verified after the restart.\n\n{message}",
                        "Sysmon repair complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Sysmon repair could not be verified after the restart.\n\nDetails: {message}\n\n" +
                        "Use the Sysmon setup button in the Process Monitor view to retry the repair.",
                        "Sysmon repair", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (System.Exception ex)
            {
                LogCrash(ex, "SysmonRepair");
            }
        }

        // Off the UI thread: DetectInstallState probes sc.exe/wevtutil, the
        // backfill can scan the whole retained channel, and none of it must
        // stall startup. Every step degrades silently when unelevated.
        private async void PrepareSysmonAtStartup()
        {
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    // one-shot ever: make pfirewall.log a usable second hit source
                    // (independent of Sysmon, so it runs before the install check)
                    FirewallService.EnsureDropLoggingOnce();

                    // name-blocked apps stay blocked across version moves: extends
                    // coverage when a blocked exe appears at a new path and retires
                    // rules whose path an update deleted. Runs without Sysmon too
                    // (startup sweep + retire pass need no event channel).
                    AppBlockCoverageService.Instance.Start();

                    // drain the DNS backlog when the machine is quiet: no new
                    // Sysmon events for a while = traffic idle = re-ask the
                    // unresolved hostnames/orgs in small polite batches
                    DnsResolutionService.Instance.StartIdleReconciliation(
                        () => SysmonWatcherService.QuietFor);

                    var installer = new SysmonInstallerService();
                    if (installer.DetectInstallState() != SysmonInstallState.Installed)
                        return; // nothing installed — the install path sizes and archives it
                    SysmonInstallerService.EnsureChannelSize();
                    SysmonArchiveService.Prune();
                    SysmonArchiveService.Backfill();
                    SysmonArchiveService.StartLiveWatch();
                    // real-time 'who is being blocked' feed: joins Sysmon connects
                    // against the blocked inventory; without it, blocks fire
                    // silently at the kernel and no tab or tool ever sees them
                    BlockHitFeedService.Instance.Start();
                });
            }
            catch (System.Exception ex)
            {
                LogCrash(ex, "SysmonStartup");
            }
        }

        private void LogCrash(System.Exception ex, string source)
        {
            try
            {
                string logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                string logEntry = $"\n[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex.ToString()}\n";
                System.IO.File.AppendAllText(logPath, logEntry);
            }
            catch { /* Failsafe */ }
        }
    }
}

