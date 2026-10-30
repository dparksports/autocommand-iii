// AutoCommand AgentHost — headless AI-access session over the Agent core.
// Same session semantics as the app's --agent-headless path, minus WPF:
// single-instance guard, optional seconds cap, stdin-close stops, and
// mutating tools permanently denied (no confirmation seam installed).
using AutoCommand.Agent.Core;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;

int seconds = 0;
var hostArgs = Environment.GetCommandLineArgs();
if (hostArgs.Length > 1 && int.TryParse(hostArgs[1], out int parsed) && parsed > 0)
    seconds = parsed;

// Single-instance guard: a wake-knock while the interactive app is already
// running must not spawn a second bridge fighting over session.json.
foreach (var p in System.Diagnostics.Process.GetProcessesByName("AutoCommand"))
    using (p)
    {
        if (p.Id != Environment.ProcessId)
        {
            Console.WriteLine("another AutoCommand instance is running — headless session skipped");
            return 2;
        }
    }

AutoCommand.Agent.Core.BridgeHost.Wire();   // F# bridge engine + tools, no UI
var (ok, message) = AgentBridgeService.Instance.Start(allowMutating: false);
Console.WriteLine(ok
    ? $"AutoCommand agent session active ({message}) — Ctrl+C or close stdin to stop."
    : "agent session failed: " + message);
if (!ok) return 1;

if (seconds > 0) Thread.Sleep(TimeSpan.FromSeconds(seconds));
else while (Console.In.ReadLine() != null) { } // until stdin closes
AgentBridgeService.Instance.Stop();
return 0;
