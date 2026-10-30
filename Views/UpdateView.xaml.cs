// 🪟 Windows Update tab — the sanctioned update path for a machine this
// locked down. State machine mirrors the design: Ready (instruction +
// computed scope) → Active (phases, countdown, escalation, restore-now)
// → After (the close-verify-repair-report checklist). All orchestration
// lives in WindowsUpdateSession; this view only renders and confirms.
using AutoCommand.Contain.Core;
using AutoCommand.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AutoCommand.Views
{
    public partial class UpdateView : UserControl
    {
        private readonly DispatcherTimer _uiTimer;
        private string _phaseDone = "";
        private string _phaseCurrent = "";

        public UpdateView()
        {
            InitializeComponent();
            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += (s, e) => OnTimer();
            IsVisibleChanged += (s, e) => { if (IsVisible) Reload(); };
        }

        private void Reload()
        {
            WindowsUpdateSession.Instance.EnsureNightlyTimer();
            NightlyCheck.IsChecked = WindowsUpdateSession.Instance.Settings.NightlyDefenderWindow;

            if (WindowsUpdateSession.Instance.IsRunning)
            {
                ShowActive("Updating…", "Session in progress.");
            }
            else
            {
                ReadyPanel.Visibility = Visibility.Visible;
                ActivePanel.Visibility = Visibility.Collapsed;
                AfterPanel.Visibility = Visibility.Collapsed;
                _ = LoadScopeAsync();
            }
            LoadHistory();
        }

        private async System.Threading.Tasks.Task LoadScopeAsync()
        {
            try
            {
                var scope = await WindowsUpdateSession.ComputeScopeAsync();
                PathwayCount.Text = $"{scope.UpdatePathway.Targets.Count} blocks";
                DefenderCount.Text = $"{scope.Defender.Targets.Count} programs";
                ReserveLine.Text = $"Update CDNs held in reserve: {scope.CdnReserve} Akamai/Gcore edge blocks — " +
                    "opened only if a download stalls (button appears during the session), and they go back with everything else.";
                StayingLine.Text = $"Everything else stays blocked — {scope.StayingOn} targets never open for an update.";
                ReadyDetail.Text = scope.LiftedCount == 0
                    ? "No active update-pathway blocks right now — Windows Update isn't being blocked by anything this app manages."
                    : $"Your blocks are currently stopping update traffic ({scope.LiftedCount} targets). " +
                      "When you want to update, use this tab rather than Settings.";
                StartBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ReadyDetail.Text = $"Couldn't compute the update scope: {ex.Message}";
                StartBtn.IsEnabled = false;
            }
        }

        // ── controls ─────────────────────────────────────────────────────

        private async void CheckBtn_Click(object sender, RoutedEventArgs e)
        {
            CheckBtn.IsEnabled = false;
            CheckBtn.Content = "Checking…";
            try
            {
                // A scan itself needs no blocks lifted — it fails politely
                // behind them, which is exactly what we want to show.
                var titles = await System.Threading.Tasks.Task.Run(() => WuApiScan());
                ReadyDetail.Text = titles.Count == 0
                    ? "Windows reports nothing to install."
                    : $"{titles.Count} update(s) waiting: {string.Join(", ", titles.Take(3))}" +
                      (titles.Count > 3 ? "…" : "");
            }
            catch (Exception ex)
            {
                ReadyDetail.Text = $"Scan couldn't complete: {ex.Message} — that's expected while update blocks are on; " +
                    "the real session lifts them first.";
            }
            finally
            {
                CheckBtn.Content = "Check for updates first";
                CheckBtn.IsEnabled = true;
            }
        }

        // WuApi is internal to Services; the view goes through the session
        // for everything that changes state, and a bare scan is read-only.
        private static List<string> WuApiScan()
        {
            var t = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (t == null) return new List<string>();
            dynamic session = System.Activator.CreateInstance(t);
            dynamic result = session.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0");
            var titles = new List<string>();
            int n = result.Updates.Count;
            for (int i = 0; i < n; i++) titles.Add((string)result.Updates.Item(i).Title);
            return titles;
        }

        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            var scope = await WindowsUpdateSession.ComputeScopeAsync();
            if (scope.LiftedCount == 0)
            {
                MessageBox.Show("Nothing to lift — no active update-pathway blocks were found.",
                    "Windows Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Update Windows now?\n\n" +
                $"· {scope.UpdatePathway.Targets.Count} update-pathway blocks and {scope.Defender.Targets.Count} Defender " +
                $"programs will lift temporarily\n" +
                $"· {scope.StayingOn} targets stay blocked the whole time\n" +
                $"· Everything comes back automatically when it finishes — hard stop " +
                $"{DateTime.Now.AddHours(2):HH:mm} even if something goes wrong.",
                "🪟 Update Windows", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _phaseDone = ""; _phaseCurrent = "";
            ShowActive("Updating…", "Lifting exactly the scoped blocks…");
            WidenBtn.Visibility = Visibility.Collapsed;

            var session = WindowsUpdateSession.Instance;
            await session.RunAsync(r => Dispatcher.BeginInvoke(() => ApplyReport(r)));
        }

        private void ApplyReport(UpdateSessionReport r)
        {
            if (r.Phase == "done")
            {
                _uiTimer.Stop();
                ShowAfter(r);
                LoadHistory();
                return;
            }

            // phase transitions accumulate as a done-list, newest active
            if (r.Phase != _phaseCurrent && _phaseCurrent.Length > 0) _phaseDone += $"✓ {_phaseCurrent}\n";
            _phaseCurrent = r.Phase;

            ActiveHeadline.Text = r.Phase switch
            {
                "scanning" => "Checking what Windows needs…",
                "downloading" => "Downloading updates…",
                "installing" => "Installing — the long part, your PC stays usable",
                "closing" => "Putting every block back…",
                _ => ActiveHeadline.Text
            };
            if (r.Note.Length > 0) ActiveNote.Text = r.Note;
            PhaseList.Text = _phaseDone + "▸ " + r.Phase + (r.FoundUpdates > 0 ? $"  ({r.FoundUpdates} update(s))" : "");
            if (r.Phase == "downloading") WidenBtn.Visibility = Visibility.Visible;
            if (!_uiTimer.IsEnabled) _uiTimer.Start();
            OnTimer();
        }

        private void OnTimer()
        {
            if (!WindowsUpdateSession.Instance.IsRunning) { _uiTimer.Stop(); return; }
            var left = WindowsUpdateSession.Instance.HardStopUtc - DateTime.UtcNow;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            Countdown.Text = $"⏳ auto-restore at {WindowsUpdateSession.Instance.HardStopUtc.ToLocalTime():HH:mm} " +
                             $"({(int)left.TotalMinutes} min left) — you don't have to remember anything.";
        }

        private async void WidenBtn_Click(object sender, RoutedEventArgs e)
        {
            WidenBtn.IsEnabled = false;
            int n = await WindowsUpdateSession.Instance.WidenToCdnAsync();
            ActiveNote.Text = n > 0
                ? $"Escalation: {n} CDN-edge blocks opened for update content — they go back with everything else."
                : "No CDN-edge blocks were eligible.";
            WidenBtn.IsEnabled = true;
        }

        private void RestoreNow_Click(object sender, RoutedEventArgs e)
        {
            WindowsUpdateSession.Instance.RequestRestore();
            ActiveNote.Text = "Closing the session — putting every block back…";
        }

        private void ShowActive(string head, string note)
        {
            ReadyPanel.Visibility = Visibility.Collapsed;
            AfterPanel.Visibility = Visibility.Collapsed;
            ActivePanel.Visibility = Visibility.Visible;
            ActiveHeadline.Text = head;
            ActiveNote.Text = note;
        }

        private void ShowAfter(UpdateSessionReport r)
        {
            ReadyPanel.Visibility = Visibility.Collapsed;
            ActivePanel.Visibility = Visibility.Collapsed;
            AfterPanel.Visibility = Visibility.Visible;

            AfterHeadline.Text = r.Error == null
                ? "✓ Done — everything is blocked again"
                : "✓ Session finished (with a note)";
            AfterSummary.Text = (r.Note ?? "") + (r.RebootRequired ? " Restart pending to finish installing." : "");
            Checklist.ItemsSource = r.Checklist.Count > 0
                ? r.Checklist
                : new List<string> { "Nothing was lifted — no update-pathway blocks were active." };
            RestartCard.Visibility = r.RebootRequired ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Back_Click(object sender, RoutedEventArgs e) => Reload();

        private void RestartNow_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Restart now to finish installing?", "Restart", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 0")
            { UseShellExecute = false, CreateNoWindow = true });
        }

        private void NightlyCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (NightlyCheck == null) return;
            var s = WindowsUpdateSession.Instance.Settings;
            s.NightlyDefenderWindow = NightlyCheck.IsChecked == true;
            WindowsUpdateSession.Instance.SaveSettings();
        }

        // ── history (from the audit trail — sessions that finished) ─────

        private void LoadHistory()
        {
            var lines = new List<string>();
            try
            {
                var events = AuditTrail.ReadEvents(BlocksStore.EventLogPath);
                var opened = new Dictionary<string, DateTime>(StringComparer.Ordinal);
                foreach (var ev in events)
                {
                    if (ev.Action == "update-opened") opened[ev.Detail] = ev.TimeUtc;
                    else if (ev.Action == "update-restored")
                    {
                        lines.Add($"{ev.TimeUtc.ToLocalTime():ddd h:mm tt}  ·  {ev.Detail}");
                        opened.Clear();   // pair conservatively: latest opened → this restored
                    }
                }
                foreach (var kv in opened)
                    lines.Add($"{kv.Value.ToLocalTime():ddd h:mm tt}  ·  {kv.Key}  ·  (no restore recorded — check Audit)");
            }
            catch { /* history is best-effort */ }
            lines.Reverse();
            if (lines.Count == 0) lines.Add("No update sessions yet — this list fills from your Audit trail.");
            HistoryList.ItemsSource = lines.Take(8).ToList();
        }
    }
}
