using System;
using System.Threading.Tasks;
using System.Windows;
using AutoCommand.Engines.Bridge;

namespace AutoCommand.Services
{
    /// <summary>
    /// The WPF implementation of the F# bridge engine's UI hooks (the port
    /// of the old WpfAgentInteraction / AgentConfirmWindow flow): marshals
    /// to the dispatcher and shows the AgentConfirmWindow dialog, Topmost,
    /// owned by the main window. Installed once at startup
    /// (AgentBridgeService.Ui = new AgentBridgeUI()); headless hosts never
    /// install one, which keeps their mutating tools denied-by-default with
    /// no dialog anywhere.
    /// </summary>
    public sealed class AgentBridgeUI : IAgentBridgeUI
    {
        /// <summary>
        /// Called from the RPC listener thread, before any mutating tool
        /// runs. Application.MainWindow is a VerifyThread() property, so the
        /// read itself must be marshalled — reading it inline was the crash
        /// behind EVERY mutating-tool call ("The calling thread cannot
        /// access this object…", even for dry_run, because the engine
        /// checks UI availability before the handler's dry-run branch).
        /// </summary>
        public bool IsUiAvailable
        {
            get
            {
                var app = Application.Current;      // static reference — thread-safe
                if (app == null) return false;
                var d = app.Dispatcher;             // DispatcherObject.Dispatcher is thread-safe by design
                if (d.CheckAccess()) return app.MainWindow != null;
                try { return (bool)d.Invoke(new Func<bool>(() => app.MainWindow != null)); }
                catch { return false; }             // dispatcher shut down = no UI
            }
        }

        public void BeginInvokeOnUI(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        // F# engine interface signature: the F# (bool, bool) tuple surfaces
        // as Tuple<bool, bool> in C# metadata.
        public async Task<System.Tuple<bool, bool>> ShowConfirmAsync(string tool, string detail)
        {
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher == null)
                return System.Tuple.Create(false, false);

            var tcs = new TaskCompletionSource<System.Tuple<bool, bool>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // fire-and-forget: the result arrives through tcs below
            _ = dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var win = new Views.AgentConfirmWindow(tool, detail)
                    {
                        Owner = Application.Current?.MainWindow,
                        Topmost = true,
                    };
                    win.ShowDialog();
                    tcs.TrySetResult(System.Tuple.Create(win.Allowed, win.AlwaysForTool));
                }
                catch
                {
                    tcs.TrySetResult(System.Tuple.Create(false, false));
                }
            });

            return await tcs.Task;
        }
    }
}
