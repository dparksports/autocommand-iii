using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoCommand.Helpers;
using AutoCommand.NetWatch.Core;

namespace AutoCommand.Views
{
    /// <summary>
    /// The DNS Queries tab: who resolves what, how often, and how regularly.
    /// Two facets over one feed — per domain (beacon-hunting: a low-variance
    /// query cadence is the signature) and per process (what is this program
    /// looking up?). The feed starts here and on the AI bridge; the loop is
    /// visibility-gated (RefreshLoop) and every 3rd tick suffices — DNS is
    /// not a live packet view.
    /// </summary>
    public partial class DnsView : UserControl
    {
        private RefreshLoop _loop;
        private bool _byProcess;
        private int _minutes = 360;
        private string _contains;

        private sealed class DomainRow
        {
            public string Glyph, GlyphToolTip, Domain, AskedBy, Queries, FirstSeen, LastSeen, Regularity, Resolved;
        }

        private sealed class ProcessRow
        {
            public string Process, FirstSeen, LastSeen, TopDomains;
            public int Pid, Domains, Queries;
        }

        public DnsView()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loop != null) return; // Loaded fires on every tab re-entry
            DnsQueryFeedService.Instance.Start();
            _loop = RefreshLoop.Start(this, TimeSpan.FromSeconds(10), BuildRows, ApplyRows);
        }

        // ── filters ─────────────────────────────────────────────────────────

        private void FacetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _byProcess = FacetCombo.SelectedIndex == 1;
            if (DomainGrid == null) return;
            DomainGrid.Visibility = _byProcess ? Visibility.Collapsed : Visibility.Visible;
            ProcessGrid.Visibility = _byProcess ? Visibility.Visible : Visibility.Collapsed;
            _loop?.Trigger();
        }

        private void MinutesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _minutes = FacetCombo == null ? 360 : FacetCombo.SelectedIndex switch
            {
                0 => 60, 2 => 1440, _ => 360,
            };
            _loop?.Trigger();
        }

        private void ContainsBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyContains();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => ApplyContains();

        private void ApplyContains()
        {
            _contains = string.IsNullOrWhiteSpace(ContainsBox.Text) ? null : ContainsBox.Text.Trim();
            _loop?.Trigger();
        }

        // ── build (thread pool) + apply (UI thread) ─────────────────────────

        private List<object> BuildRows()
        {
            DnsQueryFeedService.Instance.Start(); // idempotent; also recovers a stopped watcher
            if (_byProcess)
                return DnsQueryFeedService.Instance.ProcessAggregates(_minutes, 200, _contains)
                    .Select(p => (object)new ProcessRow
                    {
                        Process = p.Process,
                        Pid = p.Pid,
                        Domains = p.DistinctDomains,
                        Queries = p.TotalQueries,
                        FirstSeen = p.FirstUtc.ToLocalTime().ToString("MM-dd HH:mm"),
                        LastSeen = p.LastUtc.ToLocalTime().ToString("MM-dd HH:mm"),
                        TopDomains = string.Join(" · ", p.TopDomains),
                    }).ToList();

            return DnsQueryFeedService.Instance.DomainAggregates(_minutes, 200, _contains)
                .Select(d => (object)new DomainRow
                {
                    Domain = d.QueryName,
                    AskedBy = d.RequestedBy.Count == 0 ? "?"
                        : string.Join(" · ", d.RequestedBy.Take(3).Select(r => $"{r.Process} ×{r.Count}"))
                          + (d.RequestedBy.Count > 3 ? $" (+{d.RequestedBy.Count - 3})" : ""),
                    Queries = d.TotalQueries.ToString(),
                    FirstSeen = d.FirstUtc.ToLocalTime().ToString("MM-dd HH:mm"),
                    LastSeen = d.LastUtc.ToLocalTime().ToString("MM-dd HH:mm"),
                    Glyph = d.EncryptedResolverHit ? "🔒"
                          : (d.GapCv != null && d.TotalQueries >= 5 && d.GapCv < 0.3 ? "⏱" : ""),
                    GlyphToolTip = d.EncryptedResolverHit
                        ? "Encrypted resolver: this process is about to resolve via DoH/DoT — the individual lookups are invisible to any local observer, including this tab."
                        : (d.GapCv != null && d.TotalQueries >= 5 && d.GapCv < 0.3
                            ? $"Regular cadence (gap variation {d.GapCv:0.00}) — the machine asks for this name on a metronome. Beacons look like this; scheduled updaters do too."
                            : ""),
                    Regularity = d.GapCv == null ? "—"
                        : d.GapCv < 0.3 ? $"regular ({d.GapCv:0.00})"
                        : d.GapCv < 0.8 ? $"mixed ({d.GapCv:0.00})" : $"bursty ({d.GapCv:0.00})",
                    Resolved = d.EncryptedResolverHit ? "—"
                        : string.Join(" · ", d.ResolvedIps.Take(4)) + (d.ResolvedIps.Count > 4 ? $" (+{d.ResolvedIps.Count - 4})" : ""),
                }).ToList();
        }

        private void ApplyRows(List<object> rows)
        {
            var feed = DnsQueryFeedService.Instance;
            var feedNote = feed.WatcherError;
            StatusText.Text = (feedNote != null ? feedNote + " · " : "") +
                $"{feed.QueryCount} queries in the 24 h feed · showing {_minutes} min" +
                (_contains != null ? $" · filter '{_contains}'" : "") +
                (feed.IsRunning ? "" : " · feed stopped");

            if (_byProcess)
                ProcessGrid.ItemsSource = rows.OfType<ProcessRow>().ToList();
            else
                DomainGrid.ItemsSource = rows.OfType<DomainRow>().ToList();
        }
    }
}
