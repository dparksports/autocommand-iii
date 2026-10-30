using AutoCommand.NetWatch.Core;
using System.Collections.Generic;
using AutoCommand.Contain.Core;

namespace AutoCommand.Contain.Core
{
    public enum ServiceImpact { Normal = 0, Medium = 1, High = 2 }

    public sealed class ServiceInfo
    {
        public string DisplayName;
        public string Description;
        public ServiceImpact Impact;

        /// <summary>Phone Home tab only: the authored "why does this phone
        /// home, and what does normal look like" story. Null on entries that
        /// are not part of the curated Phone Home card set.</summary>
        public string PhoneHomeStory;

        /// <summary>Phone Home tab only: the upper edge of the typical
        /// connects-per-hour band, from observed behavior on ordinary
        /// machines. Null = no authored baseline (the card says so).</summary>
        public int? NormalMaxPerHour;
    }

    /// <summary>
    /// Plain-language descriptions for the Windows services and stock
    /// processes that actually show up in the block list — the difference
    /// between "svchost.exe" and "Delivery Optimization fetching Windows
    /// Update content". Lookup is by BlocksStore serviceKey (lowercase
    /// service name or exe stem). Anything unknown is the caller's honest
    /// "no description available" case, never a guess.
    /// Impact drives the Blocks tab ordering: High blocks break security
    /// features, sign-in or updates; Medium degrade features; Normal is fair
    /// game for blocking.
    /// </summary>
    public static class ServiceCatalog
    {
        private static readonly Dictionary<string, ServiceInfo> Entries = new()
        {
            // ── Windows services (svchost-hosted, by -s/SCM service name) ──
            ["wuauserv"] = H("Windows Update", "Downloads and installs Windows updates. Blocking it stops security patches from arriving.",
            story: "Talks to Windows Update during scans and downloads — bursts while updates are being fetched or installed, near-zero the rest of the day.",
            maxPerHour: 20),
            ["dosvc"] = M("Delivery Optimization", "Peers/CDN delivery of Windows Update and Store content — most Akamai/Gcore traffic from svchost is this service.",
            story: "Fetches Windows Update and Store content from Microsoft CDNs (and LAN peers). Sudden bursts during updates are expected; a steady all-day hum is not.",
            maxPerHour: 40),
            ["usosvc"] = M("Update Session Orchestrator", "Coordinates update download/install sessions between Windows Update and the user session."),
            ["mousocoreworker"] = M("Update Orchestrator Worker", "User-session worker for update orchestration (scans, downloads, restart prompts)."),
            ["bits"] = M("Background Intelligent Transfer", "Throttled background file transfers used by updates, Defender and administrators.",
            story: "Carries background file transfers — updates, Defender signature jobs, admin scripts — over HTTP(S) in throttled pieces, so connects come in small clusters.",
            maxPerHour: 15),
            ["dnscache"] = H("DNS Client (Dnscache)", "Resolves hostnames for the whole system. Blocking it breaks name resolution — the classic 'I blocked svchost and lost my internet'."),
            ["nlasvc"] = H("Network Location Awareness", "Detects network connectivity/state; NCSI probes. Blocking breaks network status detection and often all traffic."),
            ["ncsi"] = H("Network Connectivity Indicator", "The active probe behind the taskbar internet icon (msftconnecttest.com)."),
            ["dhcp"] = H("DHCP Client", "Obtains the machine's IP address. Blocking it kills new network connections."),
            ["winhttpautoproxysvc"] = M("WinHTTP Proxy Discovery", "Finds the network's proxy settings (WPAD) for system services."),
            ["cryptsvc"] = M("Cryptographic Services", "Root-certificate trust and catalog verification — blocking breaks HTTPS for system components.",
            story: "Fetches certificate revocation lists and root-certificate updates on behalf of every program doing TLS. Plain port-80 fetches to Cloudflare/Google CDNs are normal — CRLs are signed, so they travel unencrypted by design. Its rate tracks how much TLS the whole machine is doing.",
            maxPerHour: 30),
            ["wpnservice"] = M("Windows Push Notifications", "Delivers live tiles, toasts and app notifications (WNS channel).",
            story: "Keeps a channel open to the Windows Push Notification service and cycles it periodically — that is how toasts, live tiles and app notifications arrive.",
            maxPerHour: 12),
            ["w32time"] = N("Windows Time", "Keeps the system clock in sync (NTP)."),
            ["eventlog"] = H("Windows Event Log", "Collects system logs. Blocking can make services fail to start."),
            ["schedule"] = H("Task Scheduler", "Runs scheduled tasks; many updaters depend on it."),
            ["lanmanworkstation"] = N("Workstation (SMB client)", "File shares and network drives."),
            ["lanmanserver"] = N("Server (SMB server)", "Shares this machine's folders with the network."),
            ["wsearch"] = N("Windows Search", "Builds the local search index; occasional online suggestions fetch."),
            ["sysmain"] = N("SysMain (Superfetch)", "Prefetch/memory optimization; touches defrag/trim endpoints."),
            ["appinfo"] = N("Application Information", "Elevates UAC prompts — rarely network-active."),
            ["trkwks"] = N("Distributed Link Tracking", "Maintains shell links; phones home to Microsoft rarely."),
            ["themes"] = N("Themes", "Visual styles; not network-active on its own."),
            ["netprofm"] = M("Network List Service", "Reports network connectivity and properties to apps. Its probes hit Microsoft/Akamai edges; blocking can flip the taskbar to 'no internet' and break Store/Start web content.",
            story: "Reports network connectivity and properties to apps; probes Microsoft/Akamai edges whenever the network state changes (Wi-Fi roam, VPN up/down, resume).",
            maxPerHour: 12),
            ["installservice"] = N("Microsoft Store Install Service", "Installs and updates Store apps and their licenses."),
            ["cdpsvc"] = M("Connected Devices Platform", "Device discovery/continuity between your PC, phones and the cloud."),
            ["tokenbroker"] = M("Account Token Broker", "Microsoft-account / AAD sign-in tokens for apps. Blocking breaks app sign-in."),
            ["licensing"] = M("Client License Service", "Windows/Store licensing checks."),
            ["wlidsvc"] = M("Microsoft Account Sign-in", "Sign-in helper for Microsoft accounts.",
            story: "Sign-in helper for Microsoft accounts — checks identity and token endpoints when apps sign in or silently refresh credentials.",
            maxPerHour: 10),
            ["diagtrack"] = N("Connected User Experiences (DiagTrack)", "Windows diagnostic/telemetry upload. A legitimate blocking target if you want less telemetry.",
            story: "Batches Windows diagnostic/usage data and uploads it to Microsoft on a timer — roughly every 15–40 minutes, usually over one long-lived TLS connection rather than rapid beacons.",
            maxPerHour: 6),
            ["ssdpsrv"] = N("SSDP Discovery", "UPnP device discovery on the local network."),
            ["fdrespub"] = N("Function Discovery Publish", "Publishes this PC on the network for discovery."),

            // ── stock processes (exe-stem keys) ─────────────────────────────
            ["lsass"] = H("Local Security Authority (lsass.exe)", "Enforces local security policy: Kerberos, NTLM and Microsoft-account sign-in. Blocking it risks locking you out of the machine."),
            ["msmpeng"] = H("Defender Antivirus Engine (MsMpEng)", "The real-time antimalware engine. Blocking stops protection and signature scans."),
            ["mpcmdrun"] = H("Defender Command-Line Tool (MpCmdRun)", "Defender signature downloads and on-demand scans."),
            ["mpdefendercoreservice"] = H("Defender Core Service", "Defender platform coordination and update checks."),
            ["nisdrv"] = H("Defender Network Inspection", "Network-level intrusion inspection driver support."),
            ["smartscreen"] = H("SmartScreen (smartscreen.exe)", "Reputation checks on downloads and links. Blocking it disables that warning layer."),
            ["msedge"] = M("Microsoft Edge", "The Edge browser. Blocking it stops browsing in Edge (and anything embedding it)."),
            ["msedgewebview2"] = M("Edge WebView2", "Embeds web content in Start menu, Widgets, Settings and many apps. Blocking breaks those surfaces."),
            ["microsoftedgeupdate"] = M("Edge Update", "Keeps Edge and WebView2 current."),
            ["searchhost"] = M("Start Menu Search (SearchHost)", "The Start menu's search UI, including web suggestions."),
            ["startmenuexperiencehost"] = M("Start Menu (StartMenuExperienceHost)", "The Start menu shell; fetches web results and tile content."),
            ["widgets"] = M("Widgets (Windows Web Experience)", "The widgets board — pure web content delivery."),
            ["backgroundtaskhost"] = M("UWP Background Tasks", "Runs Store apps' background work (mail sync, live tiles, notifications)."),
            ["taskhostw"] = M("Task Host (taskhostw)", "Legacy host for scheduled-task COM handlers and DLL-based tasks."),
            ["werfault"] = N("Windows Error Reporting (WerFault)", "Uploads crash reports to Microsoft after failures."),
            ["svchost:unresolved"] = N("svchost — service unknown", "Service identity could not be recovered yet. Run attribution repair to name the actual service."),
        };

        private static ServiceInfo H(string n, string d, string story = null, int? maxPerHour = null) => new() { DisplayName = n, Description = d, Impact = ServiceImpact.High, PhoneHomeStory = story, NormalMaxPerHour = maxPerHour };
        private static ServiceInfo M(string n, string d, string story = null, int? maxPerHour = null) => new() { DisplayName = n, Description = d, Impact = ServiceImpact.Medium, PhoneHomeStory = story, NormalMaxPerHour = maxPerHour };
        private static ServiceInfo N(string n, string d, string story = null, int? maxPerHour = null) => new() { DisplayName = n, Description = d, Impact = ServiceImpact.Normal, PhoneHomeStory = story, NormalMaxPerHour = maxPerHour };

        /// <summary>The Phone Home tab's curated card set, in display order
        /// (the tab re-sorts by measured connects/hour). Every key here has
        /// an authored PhoneHomeStory and a normal band.</summary>
        public static readonly string[] CuratedPhoneHomeKeys =
        {
            "diagtrack", "cryptsvc", "dosvc", "wpnservice",
            "wlidsvc", "netprofm", "wuauserv", "bits",
        };

        public static bool TryGet(string serviceKey, out ServiceInfo info) =>
            Entries.TryGetValue((serviceKey ?? "").Trim().ToLowerInvariant(), out info) &&
            !string.IsNullOrEmpty(serviceKey);

        /// <summary>Display name even when the catalog lacks an entry: the
        /// recorded display name, else the raw key humanized.</summary>
        public static string DisplayNameFor(string serviceKey, string recorded)
        {
            if (!string.IsNullOrEmpty(recorded)) return recorded;
            if (TryGet(serviceKey, out var c)) return c.DisplayName;
            return string.IsNullOrEmpty(serviceKey) ? "Unattributed" : serviceKey;
        }
    }
}
