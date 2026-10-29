using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoCommand.Views
{
    /// <summary>One service group inside a bulk-change preview.</summary>
    public sealed class BulkGroupLine
    {
        public string Glyph;        // 🔴 🟠 ⚪
        public string DisplayName;  // service/exe group name
        public int Targets;
        public int Rules;
        public bool HighImpact;
        public string Warning;      // shown in amber under the group name
    }

    /// <summary>
    /// The change-preview sheet every bulk pause/resume goes through — the
    /// replacement for per-group MessageBox confirms. Shows the exact scope
    /// grouped by service, warns on high-impact groups, and offers the scope
    /// most users actually want ("all except high-impact") as one click.
    /// Nothing applies until Apply; the caller offers Undo afterwards.
    /// </summary>
    public partial class BlockBulkPreviewWindow : Window
    {
        private readonly List<BulkGroupLine> _groups;

        /// <summary>True when the user chose "except high-impact" and such
        /// groups exist in scope.</summary>
        public bool ExcludeHighImpact { get; private set; }

        public BlockBulkPreviewWindow(string verb, List<BulkGroupLine> groups)
        {
            _groups = groups.OrderByDescending(g => g.HighImpact ? 1 : 0)
                             .ThenByDescending(g => g.Targets)
                             .ToList();
            int nAll = _groups.Sum(g => g.Targets);
            int mAll = _groups.Sum(g => g.Rules);
            var hi = _groups.Where(g => g.HighImpact).ToList();
            int nHi = hi.Sum(g => g.Targets);
            int mHi = hi.Sum(g => g.Rules);

            InitializeComponent();

            Title = $"{verb} blocks — preview";
            HeadlineText.Text = $"{verb} {nAll} target(s) — {mAll} rule(s) will be {(verb == "Pause" ? "disabled" : "enabled")}";
            SubText.Text = hi.Count > 0
                ? "Groups this machine depends on are marked 🔴 and carry a warning — pick the scope below before applying."
                : "Scope is grouped by the Windows service that made the blocked connection.";
            RbAll.Content = $"{verb} all — {nAll} target(s) · {mAll} rule(s)";
            if (hi.Count > 0)
                RbExcept.Content = $"{verb} all except 🔴 high-impact — {nAll - nHi} target(s) · {mAll - mHi} rule(s)";
            else
                RbExcept.Visibility = Visibility.Collapsed;

            GroupsLv.ItemsSource = _groups.Select(g =>
            {
                var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                var head = new StackPanel { Orientation = Orientation.Horizontal };
                head.Children.Add(new TextBlock
                {
                    Text = g.Glyph + "  ",
                    FontSize = 12,
                    ToolTip = g.HighImpact ? "High impact — blocking this breaks Windows security features, sign-in or updates"
                        : g.Glyph == "🟠" ? "Medium impact — blocking this degrades Windows features"
                        : "Normal impact — routine blocking target",
                });
                head.Children.Add(new TextBlock
                {
                    Text = g.DisplayName,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                });
                head.Children.Add(new TextBlock
                {
                    Text = $"   {g.Targets} target(s) · {g.Rules} rule(s)",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                });
                wrap.Children.Add(head);
                if (!string.IsNullOrEmpty(g.Warning))
                    wrap.Children.Add(new TextBlock
                    {
                        Text = "⚠ " + g.Warning,
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)FindResource("AccentAmberBrush"),
                        Margin = new Thickness(22, 2, 0, 0),
                    });
                return wrap;
            }).ToList();

            UpdateApplyLabel();
        }

        private void Scope_Changed(object sender, RoutedEventArgs e) => UpdateApplyLabel();

        private void UpdateApplyLabel()
        {
            ExcludeHighImpact = RbExcept.IsVisible && RbExcept.IsChecked == true;
            int n = _groups.Where(g => !(ExcludeHighImpact && g.HighImpact)).Sum(g => g.Targets);
            string verb = Title.StartsWith("Pause") ? "Pause" : "Resume";
            ApplyBtn.Content = $"{(ExcludeHighImpact ? verb + " (except 🔴)" : verb)} {n} target(s)";
        }

        private void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
