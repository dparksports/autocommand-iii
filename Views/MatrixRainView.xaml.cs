using System;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Matrix;

namespace AutoCommand.Views
{
    /// <summary>
    /// The Rain tab — the same engine as the standalone overlay
    /// (github.com/dparksports/autocommand-matrix), embedded as a face of
    /// the app: it renders the LIVE Process Monitor table (no engines of
    /// its own) for users who'd rather watch the animation than read the
    /// grid. Rendering pauses whenever the tab is hidden.
    /// </summary>
    public partial class MatrixRainView : UserControl
    {
        private MatrixRainConfig _cfg;
        private MatrixRainFeed _feed;
        private MatrixRenderer _renderer;
        private bool _attaching;

        public MatrixRainView()
        {
            InitializeComponent();

            Loaded += (s, e) => Attach();
            IsVisibleChanged += (s, e) => ApplyVisibility();
            SizeChanged += (s, e) => _renderer?.SetSurfaceSize(ActualWidth, ActualHeight);
        }

        /// <summary>Wire up against the Process Monitor's live table. Both
        /// views are constructed at startup, but be resilient anyway: retry
        /// briefly if the monitor view has not registered yet.</summary>
        private void Attach()
        {
            if (_renderer != null || _attaching) return;
            _attaching = true;

            var tracked = SvchostMonitorView.Current?.TrackedTable;
            if (tracked == null)
            {
                // Monitor view not registered yet — retry shortly.
                _attaching = false;
                var retry = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                retry.Tick += (s2, e2) => { retry.Stop(); Attach(); };
                retry.Start();
                return;
            }

            _cfg = MatrixRainConfig.LoadOrDefault();
            _feed = new MatrixRainFeed(tracked);
            _renderer = new MatrixRenderer(AmbientImage, RainImage, () => _feed.Latest, _cfg);
            _renderer.SetSurfaceSize(ActualWidth, ActualHeight);
            _attaching = false;

            ApplyVisibility();
        }

        /// <summary>Render only while the tab is on screen — a hidden tab
        /// costs nothing.</summary>
        private void ApplyVisibility()
        {
            if (_renderer == null) return;
            if (IsVisible && IsLoaded)
            {
                _feed?.Start();
                _renderer.SetSurfaceSize(ActualWidth, ActualHeight);
                _renderer.Start();
            }
            else
            {
                _renderer.Stop();
                _feed?.Stop();
            }
        }
    }
}
