using System;
using AutoCommand.Agent.Core;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;

namespace AutoCommand.Models
{
    /// <summary>
    /// A user-defined tracing project — "watch this process (or this svchost
    /// service) and tell me how often it phones home, how much it sends and
    /// to whom". Projects are data, not code: one readable JSON file each
    /// under %LOCALAPPDATA%\AutoCommand\traces\projects, hot-reloaded, so a
    /// new project never needs a rebuild (and a definition can be shared by
    /// copying the file's text).
    /// </summary>
    public class TraceProject
    {
        /// <summary>Display name; also the file stem, so keep it path-safe.</summary>
        public string Name { get; set; } = "new project";

        /// <summary>
        /// Process image name to watch, case-insensitive, '*' wildcards
        /// allowed ("chrome*", "msedge.exe"). For svchost services set this
        /// to "svchost.exe" (or leave null) and use <see cref="ServiceKey"/>.
        /// </summary>
        public string MatchProcess { get; set; } = "*.exe";

        /// <summary>
        /// Optional SCM service key ("diagtrack", "dosvc") — restricts the
        /// match to the svchost instance(s) hosting that service. When set,
        /// MatchProcess is treated as svchost.exe regardless of its value.
        /// </summary>
        public string ServiceKey { get; set; } = "";

        /// <summary>Free-form note shown on the card.</summary>
        public string Notes { get; set; } = "";

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public override string ToString() => Name;
    }
}
