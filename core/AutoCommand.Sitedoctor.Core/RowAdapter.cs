using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoCommand.Engines.Core;
using AutoCommand.Engines.Watch;

namespace AutoCommand.Sitedoctor.Core
{
    /// <summary>
    /// Maps the app's TransmissionRecord into the engine-side TrafficRow
    /// view. The F# diagnosis engines never see app/UI models — each host
    /// maps its own record types into TrafficRow at the call site (see the
    /// TrafficRow.fs header); this is AutoCommand's mapping.
    /// </summary>
    public static class RowAdapter
    {
        public static TrafficRow ToRow(TransmissionRecord r) =>
            new TrafficRow(
                r.TsUtc,
                r.Pid,
                r.Image ?? "",
                Path.GetFileName(r.Image ?? ""),
                r.RemoteIp ?? "",
                r.RemotePort,
                r.Sni ?? "",
                r.ClientBytes,
                r.ServerBytes);

        public static List<TrafficRow> ToRows(IEnumerable<TransmissionRecord> rows) =>
            rows?.Select(ToRow).ToList() ?? new List<TrafficRow>();
    }
}
