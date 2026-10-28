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
        public bool IsUiAvailable => Application.Current?.MainWindow != null;

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

            dispatcher.BeginInvoke(() =>
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
