// ProxyInstallSelfTest — drives McpClientRegistration's install decision
// logic (the versioned-refresh + lock + fallback rules) against synthetic
// directories, no WPF and no elevation needed.
//
//   dotnet run --project ProxyInstallSelfTest
//
// Scenarios (I2 acceptance):
//   1. upgrade:     a complete OLD install exists, a NEW source appears →
//                   installs v<new>, returns the new path, old folder is
//                   cleaned when unlocked, kept when locked
//   2. lock:        the source exe is held open → install fails, the
//                   previously-good path is kept and reported, a dev path is
//                   never registered
//   3. no source:   legacy flat set only → legacy path returned unchanged
//   4. nothing:     no source, no prior install → null
//   5. migration: stale-registration scan — flags broken (deleted proxy)
//                 and outdated (older install) 'autocommand' entries,
//                 skips unregistered/malformed; Register repairs.
using System.Diagnostics;
using AutoCommand.Agent.Core;
using AutoCommand.Engines.Bridge;
using AgentBridgeService = AutoCommand.Agent.Core.AgentBridgeService;
using McpClientCatalog = AutoCommand.Agent.Core.McpClientCatalog;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.Intercept.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;

int failures = 0;
void Check(string name, bool pass, string detail)
{
    Console.WriteLine((pass ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " — " + detail : ""));
    if (!pass) failures++;
}

string root = Path.Combine(Path.GetTempPath(), "autocommand-proxyinstall-selftest-" + Guid.NewGuid().ToString("N")[..8]);
string tools = Path.Combine(root, "tools");
Directory.CreateDirectory(tools);
try
{
    // ── helpers ──────────────────────────────────────────────────────────
    void MakeSource(string dir, string marker)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "AutoCommand.AgentProxy.exe"), "fake exe " + marker);
        File.WriteAllText(Path.Combine(dir, "coreclr.dll"), "fake runtime " + marker);
    }
    void MakeLegacyTools()
    {
        foreach (string f in new[] { "AutoCommand.AgentProxy.exe", "AutoCommand.AgentProxy.dll",
                                     "AutoCommand.AgentProxy.runtimeconfig.json", "AutoCommand.AgentProxy.deps.json" })
            File.WriteAllText(Path.Combine(tools, f), "legacy " + f);
    }

    // ── 1. upgrade: stale complete install + new source → refresh ────────
    MakeLegacyTools();                                   // the old flat install (complete)
    string srcOld = Path.Combine(root, "src-old"); MakeSource(srcOld, "old");
    string srcNew = Path.Combine(root, "src-new"); MakeSource(srcNew, "new");
    var (p1, m1) = McpClientCatalog.ResolveAndInstallProxy("2026.10.16", tools, new[] { srcOld });
    Check("1a upgrade installs new version", p1 == Path.Combine(tools, "v2026.10.16", "AutoCommand.AgentProxy.exe"), m1);
    Check("1b new bits actually copied",
          File.ReadAllText(p1!).Contains("old"), "source content came from src-old");
    Check("1c marker written", File.Exists(Path.Combine(tools, "v2026.10.16", "proxy.installed")), "");
    Check("1d legacy flat set cleaned (unlocked)",
          !File.Exists(Path.Combine(tools, "AutoCommand.AgentProxy.dll")), "");

    // idempotent: second call with same version → same path, no reinstall
    var (p1b, m1b) = McpClientCatalog.ResolveAndInstallProxy("2026.10.16", tools, new[] { srcNew });
    Check("1e same version → same path", p1b == p1, m1b);
    Check("1f v2026.10.16 content NOT refreshed by same-version call",
          File.ReadAllText(p1b!).Contains("old"), "still the old bits — refresh only on version change");

    // ── 2. locked old install: a running proxy holds v2026.10.16 open ────
    string lockedExe = p1!;
    using (var lockStream = new FileStream(lockedExe, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var (p2, m2) = McpClientCatalog.ResolveAndInstallProxy("2026.10.17", tools, new[] { srcNew });
        Check("2a next version installs alongside the locked one",
              p2 == Path.Combine(tools, "v2026.10.17", "AutoCommand.AgentProxy.exe"), m2);
        Check("2b locked old folder survives for its running proxy", File.Exists(lockedExe), "");
        Check("2c new install carries new bits", File.ReadAllText(p2!).Contains("new"), "");
    }
    // lock released → a later install cleans the stale folder
    _ = McpClientCatalog.ResolveAndInstallProxy("2026.10.18", tools, new[] { srcNew });
    Check("2d stale folder cleaned once unlocked",
          !Directory.Exists(Path.Combine(tools, "v2026.10.17")), "");

    // ── 3. every source locked → keep the previously-good path ───────────
    string keepExe = Path.Combine(tools, "v2026.10.18", "AutoCommand.AgentProxy.exe");
    using (var srcLock = new FileStream(Path.Combine(srcNew, "AutoCommand.AgentProxy.exe"),
                                        FileMode.Open, FileAccess.Read, FileShare.None)) // deny-all: a real lock
    {
        var (p3, m3) = McpClientCatalog.ResolveAndInstallProxy("2026.10.19", tools, new[] { srcNew });
        Check("3a locked source → falls back to newest good install", p3 == keepExe, m3);
        Check("3b failure is reported, not swallowed", m3.Contains("keeping"), m3);
    }
    Check("3c v2026.10.19 was not half-created", !Directory.Exists(Path.Combine(tools, "v2026.10.19")), "");

    // ── 4. no source anywhere, legacy only → legacy path ─────────────────
    string tools2 = Path.Combine(root, "tools2");
    Directory.CreateDirectory(tools2);
    foreach (string f in new[] { "AutoCommand.AgentProxy.exe", "AutoCommand.AgentProxy.dll",
                                 "AutoCommand.AgentProxy.runtimeconfig.json", "AutoCommand.AgentProxy.deps.json" })
        File.WriteAllText(Path.Combine(tools2, f), "legacy " + f);
    var (p4, m4) = McpClientCatalog.ResolveAndInstallProxy("2026.10.16", tools2, Array.Empty<string>());
    Check("4a no source → legacy flat install kept",
          p4 == Path.Combine(tools2, "AutoCommand.AgentProxy.exe"), m4);

    // ── 5. nothing at all → null, with a diagnostic message ──────────────
    var (p5, m5) = McpClientCatalog.ResolveAndInstallProxy("2026.10.16", Path.Combine(root, "tools3"),
                                                            Array.Empty<string>());
    Check("5a no source, no prior install → null", p5 == null, m5);

    // ── 6. migration self-heal: stale-registration scan ──────────────────
    // A config whose 'autocommand' entry points at a missing proxy (broken)
    // or at an older install than current (outdated) is flagged; current,
    // unregistered, malformed and multi-level-key configs scan clean.
    // Repair is the normal Register path — same backup + verify rules.
    string MakeClientConfig(string dir, string rootKey, string command)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "mcp.json");
        var entry = new System.Text.Json.Nodes.JsonObject();
        if (command != null) entry["command"] = command;
        entry["args"] = new System.Text.Json.Nodes.JsonArray();
        var rootJson = new System.Text.Json.Nodes.JsonObject();
        System.Text.Json.Nodes.JsonNode section = rootJson;
        foreach (string k in rootKey.Split('/'))
        {
            var next = new System.Text.Json.Nodes.JsonObject();
            ((System.Text.Json.Nodes.JsonObject)section)[k] = next;
            section = next;
        }
        ((System.Text.Json.Nodes.JsonObject)section)["autocommand"] = entry;
        File.WriteAllText(path, rootJson.ToJsonString());
        return path;
    }
    McpClientRegistration Client(string id, string configPath, string rootKey) =>
        new() { Id = id, DisplayName = id, ConfigPath = configPath, RootKeyPath = rootKey };

    string currentProxy = keepExe; // v2026.10.18 — the one complete install left above
    string dir6 = Path.Combine(root, "clients");
    var clients = new[]
    {
        Client("current",   MakeClientConfig(Path.Combine(dir6, "current"),   "mcpServers", currentProxy), "mcpServers"),
        Client("gone",      MakeClientConfig(Path.Combine(dir6, "gone"),      "mcpServers",
                              Path.Combine(tools, "v2026.10.16", "AutoCommand.AgentProxy.exe")), // cleaned above → missing
            "mcpServers"),
        Client("outdated",  MakeClientConfig(Path.Combine(dir6, "outdated"),  "mcpServers", Path.Combine(srcNew, "AutoCommand.AgentProxy.exe")), "mcpServers"),
        Client("unregistered", MakeClientConfig(Path.Combine(dir6, "unregistered"), "mcp/servers", null), "mcp/servers"),
    };
    string badPath = MakeClientConfig(Path.Combine(dir6, "malformed"), "mcpServers", "whatever");
    File.WriteAllText(badPath, "{ not json");
    var badClient = Client("malformed", badPath, "mcpServers");

    var found = McpClientCatalog.FindStaleRegistrations(currentProxy, clients.Append(badClient).ToArray());
    Check("6a exactly the broken + outdated flagged",
          found.Count == 2 && found.Any(s => s.Client.Id == "gone") && found.Any(s => s.Client.Id == "outdated"),
          "flagged: " + string.Join(",", found.Select(s => s.Client.Id)));
    Check("6b deleted-install registration is Broken",
          found.Single(s => s.Client.Id == "gone").Broken, "");
    Check("6c older-but-existing registration is not Broken",
          !found.Single(s => s.Client.Id == "outdated").Broken, "");
    Check("6d null current proxy → nothing flagged",
          McpClientCatalog.FindStaleRegistrations(null, clients).Count == 0, "");

    var (regOk, regMsg) = McpClientCatalog.Register(clients.First(c => c.Id == "gone"), currentProxy);
    Check("6e Register repairs the broken config", regOk, regMsg ?? "");
    var after = McpClientCatalog.FindStaleRegistrations(currentProxy, clients);
    Check("6f repaired config no longer flagged (only 'outdated' left)",
          after.Count == 1 && after[0].Client.Id == "outdated",
          "flagged: " + string.Join(",", after.Select(s => s.Client.Id)));

    // ── 7. offline stub drift tripwire ─────────────────────────────────
    // The AgentProxy's offline tool list must carry EVERY registered tool
    // (policy: an offline client always sees the full surface, each entry
    // answering 'needs an active session'). This check fails the suite the
    // moment the stub list drifts from the registry. Skips with a note when
    // the repo checkout isn't reachable (published-exe layout).
    string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AutoCommand.csproj")) &&
                File.Exists(Path.Combine(dir.FullName, "AgentProxy", "Program.cs")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
    string repoRoot = FindRepoRoot();
    if (repoRoot == null)
        Console.WriteLine("SKIP  stub-drift check — AgentProxy/Program.cs not found (published layout)");
    else
    {
        string stubSrc = File.ReadAllText(Path.Combine(repoRoot, "AgentProxy", "Program.cs"));
        var stubNames = System.Text.RegularExpressions.Regex
            .Matches(stubSrc, @"""name"":\s*""([a-z_]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        var regNames = AgentToolRegistry.ToolNames.ToHashSet();
        Check("7a stub list has no phantom tools", stubNames.IsSubsetOf(regNames),
              "phantom: " + string.Join(",", stubNames.Except(regNames)));
        Check("7b every registry tool has an offline stub", regNames.IsSubsetOf(stubNames),
              "missing: " + string.Join(",", regNames.Except(stubNames)));
    }
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

Console.WriteLine(failures == 0 ? "PROXY INSTALL SELF-TEST PASSED" : $"PROXY INSTALL SELF-TEST FAILED ({failures})");
return failures == 0 ? 0 : 1;
