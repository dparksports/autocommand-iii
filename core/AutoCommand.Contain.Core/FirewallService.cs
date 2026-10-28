using System;
using System.Collections.Generic;
using AutoCommand.Engines.Core;
using AutoCommand.Engines.Firewall;

namespace AutoCommand.Contain.Core
{
    /// <summary>
    /// App-side facade over the F# firewall engine (Engines.Firewall): the
    /// engine is constructed once with AutoCommand's naming policy
    /// (AC-BLOCK, see Naming.autocommand()) and exposed exactly as the old
    /// C# FirewallService was — Instance plus the static helpers — so every
    /// caller compiles unchanged. The engine itself is app-agnostic COM
    /// management (strong-typed INetFwPolicy2, no `dynamic`).
    /// </summary>
    public class FirewallService
    {
        public const string NewIpPrefix = "AC-BLOCK-IP ";
        public const string NewAppPrefix = "AC-BLOCK-APP ";

        /// <summary>The single engine instance all app code goes through.</summary>
        public static AutoCommand.Engines.Firewall.FirewallService Instance { get; } =
            new AutoCommand.Engines.Firewall.FirewallService(Naming.autocommand());

        public static string ResolveGroupName(string grouping) => AutoCommand.Engines.Firewall.FirewallService.ResolveGroupName(grouping);

        public static string PrimaryAppRuleName(string exeName) => Instance.PrimaryAppRuleName(exeName);

        public static string SuffixedAppRuleName(string exeName, string appPath) =>
            Instance.SuffixedAppRuleName(exeName, appPath);

        public static Dictionary<string, FirewallLogHit> GetRecentLogHits(int minutes) =>
            AutoCommand.Engines.Firewall.FirewallService.GetRecentLogHits(minutes);

        public static bool FirewallDropLoggingOn() => AutoCommand.Engines.Firewall.FirewallService.FirewallDropLoggingOn();

        public static void EnsureDropLoggingOnce() => AutoCommand.Engines.Firewall.FirewallService.EnsureDropLoggingOnce();

        public static string SetDropLogging(bool enable) => AutoCommand.Engines.Firewall.FirewallService.SetDropLogging(enable);
    }
}
