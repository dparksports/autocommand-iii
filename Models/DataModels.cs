using AutoCommand.Agent.Core;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;

namespace AutoCommand.Models
{
    // ScheduledTaskItem moved to core\AutoCommand.Forensics.Core
    // (ScheduledTaskItem.cs) with the phase 4 extraction — TaskSchedulerService
    // builds these rows and lives in the core now.

    // FirewallRuleItem moved to core\AutoCommand.Contain.Core
    // (FirewallRuleItem.cs) with the phase 3 extraction — FirewallService
    // builds these rows and lives in the core now.

    public class StartupItem
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Location { get; set; }
        public string TargetPath { get; set; }
    }

    public class AppPackageItem
    {
        public string Name { get; set; }

        /// <summary>Exact Appx package name (e.g. Microsoft.Todos) — identity used
        /// for user-added custom bloatware entries, unlike the display Name.</summary>
        public string PackageName { get; set; }
        public string FullName { get; set; }
        public string Version { get; set; }
        public string Publisher { get; set; }
        public string InstallLocation { get; set; }
        public string SignatureStatus { get; set; }
        public string SignerCertificate { get; set; }
        public bool IsSystem { get; set; }
    }
}

