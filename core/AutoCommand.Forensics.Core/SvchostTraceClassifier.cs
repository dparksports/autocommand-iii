using System.Net;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.NetWatch.Core;
using System.Net.Sockets;

namespace AutoCommand.Forensics.Core
{
    /// <summary>
    /// The svchost-trace destination classifier: coarse, session-verified
    /// provider ranges rendered as display labels ("Microsoft/Azure",
    /// "Akamai (MS CDN)", "Gcore (MS CDN)", "IPv6/other", "UNCLASSIFIED").
    /// Moved here (Forensics core) with the phase 6 extraction — the
    /// AI-access bridge's svchost trace tool classifies destinations with
    /// it; the Svchost Trace view forwards to this copy.
    /// </summary>
    public static class SvchostTraceClassifier
    {
        private static readonly (string Name, string[] Cidrs)[] Ranges =
        {
            ("Microsoft/Azure", new[] { "20.0.0.0/8", "40.0.0.0/8", "13.64.0.0/11", "13.104.0.0/14",
                                        "52.96.0.0/12", "52.112.0.0/14", "65.52.0.0/14", "72.145.32.0/19", "172.176.0.0/12" }),
            ("Akamai (MS CDN)", new[] { "23.32.0.0/11", "23.192.0.0/11", "2.16.0.0/13", "95.100.0.0/15", "184.24.0.0/13", "104.64.0.0/10" }),
            ("Gcore (MS CDN)",  new[] { "92.223.0.0/16" }),
        };

        public static string Classify(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr) || addr.AddressFamily != AddressFamily.InterNetwork)
                return "IPv6/other";
            var b = addr.GetAddressBytes();
            uint v = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
            foreach (var (name, cidrs) in Ranges)
                foreach (var cidr in cidrs)
                {
                    var parts = cidr.Split('/');
                    var nb = IPAddress.Parse(parts[0]).GetAddressBytes();
                    uint net = (uint)(nb[0] << 24 | nb[1] << 16 | nb[2] << 8 | nb[3]);
                    uint mask = uint.MaxValue << (32 - int.Parse(parts[1]));
                    if ((v & mask) == (net & mask)) return name;
                }
            return "UNCLASSIFIED";
        }
    }
}
