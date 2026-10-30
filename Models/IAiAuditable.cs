using AutoCommand.Agent.Core;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;

namespace AutoCommand.Models
{
    public interface IAiAuditable
    {
        /// <summary>
        /// Returns a structured, plain-text representation of the current tab's state 
        /// (e.g. visible rows, active rules) to be sent to the AI for analysis.
        /// </summary>
        string GetAuditContext();
    }
}
