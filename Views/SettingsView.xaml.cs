using System;
using AutoCommand.Agent.Core;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.Intercept.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutoCommand.Services;
using AutoCommand.Helpers;

namespace AutoCommand.Views
{
    public partial class SettingsView : UserControl
    {
        private const string TaskName = FreshSetupService.StartupTaskName;

        public SettingsView()
        {
            InitializeComponent();

            // Live version from the assembly instead of a hardcoded string
            // (same source as the status bar)
            VersionValueText.Text =
                FileVersionInfo.GetVersionInfo(Environment.ProcessPath).ProductVersion ?? "unknown";
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckStartupTask();

            // Load Auto-Mitigate Adapters preference
            AutoMitigateAdaptersCheck.Checked -= AutoMitigateAdaptersCheck_Checked;
            AutoMitigateAdaptersCheck.Unchecked -= AutoMitigateAdaptersCheck_Unchecked;
            AutoMitigateAdaptersCheck.IsChecked = SecurityEnforcer.AutoMitigateAdapters;
            AutoMitigateAdaptersCheck.Checked += AutoMitigateAdaptersCheck_Checked;
            AutoMitigateAdaptersCheck.Unchecked += AutoMitigateAdaptersCheck_Unchecked;

            // Load the security check rate (fast-check tick interval)
            CheckRateCombo.SelectionChanged -= CheckRateCombo_SelectionChanged;
            foreach (var item in CheckRateCombo.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag && tag == SecurityEnforcer.CheckIntervalSeconds.ToString())
                {
                    item.IsSelected = true;
                    break;
                }
            }
            CheckRateCombo.SelectionChanged += CheckRateCombo_SelectionChanged;

            // Load theme preference (Dark | Light)
            ThemeCombo.SelectionChanged -= ThemeCombo_SelectionChanged;
            foreach (var item in ThemeCombo.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag && tag == ThemeService.Saved)
                {
                    item.IsSelected = true;
                    break;
                }
            }
            ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;

        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string theme)
            {
                ThemeService.ApplyTheme(theme);
            }
        }

        private void CheckRateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CheckRateCombo.SelectedItem is ComboBoxItem item
                && item.Tag is string tag && int.TryParse(tag, out int seconds))
            {
                SecurityEnforcer.CheckIntervalSeconds = seconds;
            }
        }

        private void AutoMitigateAdaptersCheck_Checked(object sender, RoutedEventArgs e)
        {
            SecurityEnforcer.AutoMitigateAdapters = true;
        }

        private void AutoMitigateAdaptersCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            SecurityEnforcer.AutoMitigateAdapters = false;
        }

        private async Task CheckStartupTask()
        {
            bool exists = false;
            await Task.Run(() =>
            {
                try
                {
                    Type tsType = Type.GetTypeFromProgID("Schedule.Service");
                    dynamic ts = Activator.CreateInstance(tsType);
                    ts.Connect();
                    dynamic rootFolder = ts.GetFolder("\\");
                    dynamic task = rootFolder.GetTask(TaskName);
                    exists = task != null;
                }
                catch
                {
                    exists = false;
                }
            });

            // Detach events to avoid infinite loop
            LaunchOnStartupCheck.Checked -= LaunchOnStartupCheck_Checked;
            LaunchOnStartupCheck.Unchecked -= LaunchOnStartupCheck_Unchecked;
            
            LaunchOnStartupCheck.IsChecked = exists;

            LaunchOnStartupCheck.Checked += LaunchOnStartupCheck_Checked;
            LaunchOnStartupCheck.Unchecked += LaunchOnStartupCheck_Unchecked;
        }

        private async void LaunchOnStartupCheck_Checked(object sender, RoutedEventArgs e)
        {
            bool ok = await FreshSetupService.EnableLaunchAtLogon();
            if (!ok)
                MessageBox.Show("Failed to create startup task.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            await CheckStartupTask();
        }

        private async void LaunchOnStartupCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            await FreshSetupService.DisableLaunchAtLogon();
            await CheckStartupTask();
        }

        // -----------------------------------------------------------------------
        // Unblock (Mark-of-the-Web removal) — see Helpers/MotwUnblocker.cs
        // -----------------------------------------------------------------------

        private async void UnblockAppBtn_Click(object sender, RoutedEventArgs e)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            UnblockAppBtn.IsEnabled = false;
            UnblockStatusText.Text = $"Scanning {dir} …";
            try
            {
                var result = await System.Threading.Tasks.Task.Run(() => MotwUnblocker.UnblockFolder(dir));
                UnblockStatusText.Text = result.Summary;
                if (result.Unblocked > 0)
                    MessageBox.Show(
                        $"Removed the download mark from {result.Unblocked} file(s) in this app's folder.\n\n" +
                        "SmartScreen / Smart App Control will no longer treat them as downloaded\n" +
                        "files — taskbar pins and direct launches work normally now.",
                        "Unblocked", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                UnblockAppBtn.IsEnabled = true;
            }
        }

        private async void UnblockFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Pick the folder to unblock (e.g. a freshly extracted AutoCommand zip)"
            };
            if (dialog.ShowDialog() != true) return;

            UnblockFolderBtn.IsEnabled = false;
            UnblockStatusText.Text = $"Scanning {dialog.FolderName} …";
            try
            {
                var result = await System.Threading.Tasks.Task.Run(() => MotwUnblocker.UnblockFolder(dialog.FolderName));
                UnblockStatusText.Text = result.Summary;
                if (result.Failures.Count > 0)
                    MessageBox.Show(
                        $"{result.Summary}\n\nFiles in use or in admin-only locations could not be updated —\n" +
                        "close the apps using them, or re-run AutoCommand as Administrator and try again.",
                        "Unblock", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                UnblockFolderBtn.IsEnabled = true;
            }
        }

        // ── Community: the live anonymous count + share ─────────────────────

        private void CommunityGraph_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // The worker serves the graph page at its root; before the
                // endpoint is deployed, fall back to the releases page.
                string url = UsagePingService.GraphPageUrl ?? UsagePingService.ReleasesUrl;
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { /* browser launch refused — nothing to do */ }
        }

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = UsagePingService.GraphPageUrl ?? UsagePingService.ReleasesUrl;
                Clipboard.SetText(url);
                ShareStatusText.Text = "Link copied — every install counts. Thank you!";
                var clear = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                clear.Tick += (s, _) => { ShareStatusText.Text = ""; clear.Stop(); };
                clear.Start();
            }
            catch { /* clipboard unavailable — nothing to do */ }
        }
    }
}
