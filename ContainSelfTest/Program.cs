// Contain self-test — the blocking/safety path, offline:
//   1. BlocksStore — round-trip persistence against a temp dir: add/upsert/
//      pause/remove, event log, provenance parsing, grouping keys.
//   2. TelemetryRulesService — synthetic catalog beats the built-in seed;
//      file detection + JSON state probe driven entirely by temp files.
//   3. FirewallService — the v2 rule-name builders (the strings the whole
//      block/undo/coverage machinery joins on).
//   4. BlockContainmentVerifier — the verify_block_containment join: every
//      verdict (contained / stale-flow / leak) plus the identity guards
//      (pid reuse, archive image match, loopback) from synthetic rows.
// Nothing touches the real %LOCALAPPDATA% store, the hosts file, netsh, or
// Defender; those halves are exercised by the app itself.
using System;
using AutoCommand.Contain.Core;
using AutoCommand.Intercept.Core;
using AutoCommand.NetWatch.Core;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

int failures = 0;
void Check(string name, bool pass, string evidence = "")
{
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name}{(pass || evidence.Length == 0 ? "" : "  — " + evidence)}");
    if (!pass) failures++;
}

// ─────────────────────────────────────────────────────────────────────
// 1. BlocksStore — the undo ledger every block surface writes through
// ─────────────────────────────────────────────────────────────────────
string storeDir = Path.Combine(Path.GetTempPath(), "contain-selftest-" + Guid.NewGuid().ToString("N"));
try
{
    // DI pilot payoff: the ledger's storage location is constructor-
    // injected — no global static to redirect, no "set DataDirectory
    // BEFORE first use" ordering trap. This test owns its ledger outright.
    var store = new BlocksLedger(storeDir);
    store.Load();
    Check("empty store starts empty", store.Records.Count == 0);

    store.AddOrUpdate(new BlockRecord
    {
        Kind = "ip", Key = "1.2.3.4", ProcessName = "chrome.exe",
        ClassAtBlock = "Application", Origin = "blocks-tab",
    }, origin: "selftest");
    store.AddOrUpdate(new BlockRecord
    {
        Kind = "ip", Key = "5.6.7.8", ProcessName = "svchost.exe",
        ServiceKey = "diagtrack", SvcDisplayName = "Connected User Experiences and Telemetry",
        ClassAtBlock = "Microsoft", Origin = "monitor",
    }, origin: "selftest");
    store.AddOrUpdate(new BlockRecord
    {
        Kind = "proc", Key = @"c:\tools\upd.exe", ProcessName = "upd.exe",
        ClassAtBlock = "Application", Origin = "blocks-tab",
    }, origin: "selftest");
    Check("three adds → three records", store.Records.Count == 3,
          $"count={store.Records.Count}");

    // upsert: same kind+key refreshes in place, never duplicates
    store.AddOrUpdate(new BlockRecord
    {
        Kind = "ip", Key = "1.2.3.4", ProcessName = "chrome.exe",
        ClassAtBlock = "Akamai (MS CDN)", Origin = "repair",
    }, origin: "selftest");
    Check("re-block same key upserts (still 3)", store.Records.Count == 3);
    Check("upsert refreshed the classifier snapshot",
          store.Records.First(r => r.Key == "1.2.3.4").ClassAtBlock == "Akamai (MS CDN)");

    // pause semantics: rules off, record kept
    store.SetStatus("ip", "5.6.7.8", "off", origin: "selftest");
    Check("pause keeps the record with Status=off",
          store.Records.First(r => r.Key == "5.6.7.8").Status == "off");

    // persistence round-trip: a fresh process state reloads from disk
    store.Load();
    Check("reload from disk keeps 3 records", store.Records.Count == 3,
          $"count={store.Records.Count}");
    Check("reload keeps the pause", store.Records.First(r => r.Key == "5.6.7.8").Status == "off");
    Check("store file exists", File.Exists(store.StorePath), store.StorePath);

    // the event ledger the Blocks tab shows
    Check("event log recorded adds", File.Exists(store.EventLogPath) &&
          File.ReadAllLines(store.EventLogPath).Count(l => l.Contains("1.2.3.4")) >= 2);

    // remove
    store.Remove("proc", @"c:\tools\upd.exe", origin: "selftest");
    Check("remove drops the record", store.Records.Count == 2 &&
          store.Records.All(r => r.Kind != "proc"));

    // provenance parser: what block rules carry in their description
    Check("provenance: svchost label parses to service key",
          store.TryParseServiceFromDescription(
              "[blocked 2026-10-25 10:00 UTC] svchost.exe [Connected User Experiences and Telemetry]",
              out string svcKey, out string svcDisplay) &&
          svcKey == "connected user experiences and telemetry",
          $"key='{svcKey}'");
    Check("provenance: '+2 more' collapses to the primary service",
          store.TryParseServiceFromDescription(
              "[blocked x] svchost.exe [DiagTrack+2 more]", out string k2, out _) &&
          k2 == "diagtrack", $"key='{k2}'");
    Check("provenance: non-svchost process rejected",
          !store.TryParseServiceFromDescription("[blocked x] chrome.exe [Whatever]", out _, out _));
    Check("provenance: 'no service' rejected",
          !store.TryParseServiceFromDescription("[blocked x] svchost.exe [no service]", out _, out _));
    Check("provenance: garbage rejected",
          !store.TryParseServiceFromDescription("plain description", out _, out _));

    // grouping: the Blocks tab groups rows by service identity
    Check("group key: ip rows group by service key",
          store.ServiceGroupKey(store.Records.First(r => r.Key == "5.6.7.8")) == "diagtrack");
}
finally
{
    try { Directory.Delete(storeDir, true); } catch { }
}

// ─────────────────────────────────────────────────────────────────────
// 2. TelemetryRulesService — catalog load + offline evaluation
// ─────────────────────────────────────────────────────────────────────
string assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
string rulesPath = Path.Combine(assetsDir, "telemetry_rules.json");
try
{
    Directory.CreateDirectory(assetsDir);
    string detOn  = Path.Combine(Path.GetTempPath(), "contain-selftest-on.json");
    string detOff = Path.Combine(Path.GetTempPath(), "contain-selftest-off.json");
    string detGone = Path.Combine(Path.GetTempPath(), "contain-selftest-gone-" + Guid.NewGuid().ToString("N") + ".json");
    File.WriteAllText(detOn,  JsonSerializer.Serialize(new { sync = true }));
    File.WriteAllText(detOff, JsonSerializer.Serialize(new { sync = false }));

    File.WriteAllText(rulesPath, JsonSerializer.Serialize(new
    {
        rules = new object[]
        {
            new
            {
                id = "selftest-on", appName = "SelfTest ON", category = "test", mechanism = "x",
                scope = "User", riskNote = "none",
                detection = new { type = "file", path = detOn },
                stateProbe = new { type = "json", path = detOn, key = "sync", offValues = new[] { "false" } },
            },
            new
            {
                id = "selftest-off", appName = "SelfTest OFF", category = "test", mechanism = "x",
                scope = "User", riskNote = "none",
                detection = new { type = "file", path = detOff },
                stateProbe = new { type = "json", path = detOff, key = "sync", offValues = new[] { "false" } },
            },
            new
            {
                id = "selftest-undetected", appName = "SelfTest ABSENT", category = "test", mechanism = "x",
                scope = "User", riskNote = "none",
                detection = new { type = "file", path = detGone },
                stateProbe = new { type = "json", path = detGone, key = "sync", offValues = new[] { "false" } },
            },
        }
    }));

    var svc = new TelemetryRulesService();
    Check("catalog file beats the built-in seed", svc.Rules.Count == 3,
          $"count={svc.Rules.Count}");

    var statuses = await svc.EvaluateAllAsync();
    var on  = statuses.First(s => s.Rule.Id == "selftest-on");
    var off = statuses.First(s => s.Rule.Id == "selftest-off");
    var gone = statuses.First(s => s.Rule.Id == "selftest-undetected");
    Check("detected + still on → State=On", on.Detected && on.State == TelemetryState.On,
          $"detected={on.Detected} state={on.State} evidence='{on.Evidence}'");
    Check("detected + off-value → State=Off", off.Detected && off.State == TelemetryState.Off,
          $"detected={off.Detected} state={off.State} evidence='{off.Evidence}'");
    Check("not installed → Detected=false", !gone.Detected, gone.Evidence);

    // seed fallback: with no catalog file the built-in seed rules load
    File.Delete(rulesPath);
    var seedSvc = new TelemetryRulesService();
    Check("missing catalog falls back to the built-in seed",
          seedSvc.Rules.Count >= 2 && seedSvc.Rules.All(r => r.Id.StartsWith("ultralytics")),
          $"count={seedSvc.Rules.Count}");
}
finally
{
    try { File.Delete(rulesPath); Directory.Delete(assetsDir, true); } catch { }
}

// ─────────────────────────────────────────────────────────────────────
// 3. FirewallService — the v2 rule-name builders every surface joins on
// ─────────────────────────────────────────────────────────────────────
Check("ip rule prefix is the v2 shape", FirewallService.NewIpPrefix == "AC-BLOCK-IP ");
Check("app rule prefix is the v2 shape", FirewallService.NewAppPrefix == "AC-BLOCK-APP ");
Check("primary app rule name shape",
      FirewallService.PrimaryAppRuleName("upd.exe") == "AC-BLOCK-APP upd.exe (Out)",
      FirewallService.PrimaryAppRuleName("upd.exe"));

string s1 = FirewallService.SuffixedAppRuleName("upd.exe", @"C:\Tools\upd.exe");
string s2 = FirewallService.SuffixedAppRuleName("upd.exe", @"C:\Tools\upd.exe");
string s3 = FirewallService.SuffixedAppRuleName("upd.exe", @"C:\Tools\v2\upd.exe");
Check("sibling rule name extends the primary", s1.StartsWith("AC-BLOCK-APP upd.exe (Out) · ", StringComparison.Ordinal), s1);
Check("sibling suffix is 8 lowercase hex chars",
      System.Text.RegularExpressions.Regex.IsMatch(s1.Substring(s1.LastIndexOf('·') + 1).Trim(), "^[0-9a-f]{8}$"), s1);
Check("same path → same sibling name", s1 == s2);
Check("different path → different sibling name", s1 != s3);

// ─────────────────────────────────────────────────────────────────────
// 4. BlockContainmentVerifier — the containment join behind the
//    verify_block_containment bridge tool. All rows synthetic; the live
//    halves (feed, kernel table, WMI, archive) are exercised by the app.
// ─────────────────────────────────────────────────────────────────────
DateTime cNow = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
DateTime cT0 = new(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);

BlockHitFeedService.HitAggregate CAgg(string image, int pid, string ip, int count,
    DateTime first, DateTime last, string kind = "ip") =>
    new() { Image = image, Process = Path.GetFileName(image), Pid = pid,
            DestIp = ip, DestPort = 443, MatchKind = kind,
            Count = count, FirstUtc = first, LastUtc = last };

var cInv = new List<BlockedTarget>
{
    new() { Target = "1.2.3.4", Kind = "ip", Ip = "1.2.3.4", Rules = 1, EnabledRules = 1, Created = cT0 },
    new() { Target = "5.6.7.8", Kind = "ip", Ip = "5.6.7.8", Rules = 1, EnabledRules = 1, Created = cT0 },
    new() { Target = "upd.exe", Kind = "app", AppPath = @"C:\Tools\upd.exe", Rules = 1, EnabledRules = 1, Created = cT0 },
    new() { Target = "9.9.9.9", Kind = "ip", Ip = "9.9.9.9", Rules = 1, EnabledRules = 1, Created = null },
};

var cV1 = BlockContainmentVerifier.Evaluate(
    new() { CAgg(@"C:\Tools\chrome.exe", 100, "1.2.3.4", 5, cT0.AddMinutes(5), cNow.AddMinutes(-2)) },
    cInv, new(), new());
Check("containment: attempts only → contained",
      cV1.Count == 1 && cV1[0].Verdict == BlockContainmentVerifier.Contained && cV1[0].Attempts == 5,
      cV1.Count == 1 ? cV1[0].Verdict : $"count={cV1.Count}");
Check("containment: t0 from rule provenance", cV1[0].T0 == cT0 && !cV1[0].T0Approximate);

var cV2 = BlockContainmentVerifier.Evaluate(
    new() { CAgg(@"C:\Tools\upd.exe", 200, "8.8.8.8", 3, cNow.AddMinutes(-90), cNow.AddMinutes(-10), kind: "app") },
    cInv,
    new() { new BlockContainmentVerifier.LiveSocket { Pid = 200, RemoteIp = "8.8.8.8", RemotePort = 443,
             ProcessName = "upd.exe", ProcessStartedUtc = cT0.AddHours(-1) } },
    new());
Check("containment: socket owner predates block → stale-flow",
      cV2[0].Verdict == BlockContainmentVerifier.StaleFlow, cV2[0].Verdict);

var cV3 = BlockContainmentVerifier.Evaluate(
    new() { CAgg(@"C:\Tools\upd.exe", 300, "8.8.8.8", 2, cNow.AddMinutes(-30), cNow.AddMinutes(-5), kind: "app") },
    cInv,
    new() { new BlockContainmentVerifier.LiveSocket { Pid = 300, RemoteIp = "8.8.8.8", RemotePort = 443,
             ProcessName = "upd.exe", ProcessStartedUtc = cT0.AddHours(1) } },
    new());
Check("containment: socket owner started after block → leak",
      cV3[0].Verdict == BlockContainmentVerifier.Leak, cV3[0].Verdict);

var cV4 = BlockContainmentVerifier.Evaluate(
    new() { CAgg(@"C:\Tools\b.exe", 400, "5.6.7.8", 4, cT0.AddMinutes(1), cNow.AddMinutes(-1)) },
    cInv, new(),
    new() { new TransmissionRecord { Pid = 400, Image = @"C:\Tools\b.exe",
             RemoteIp = "5.6.7.8", TsUtc = cT0.AddMinutes(30), ClientBytes = 5_000, ServerBytes = 1_000 } });
Check("containment: archived bytes to blocked IP after t0 → leak",
      cV4[0].Verdict == BlockContainmentVerifier.Leak && cV4[0].BytesSinceT0 == 6_000,
      $"{cV4[0].Verdict} bytes={cV4[0].BytesSinceT0}");

var cV5 = BlockContainmentVerifier.Evaluate(
    new() { CAgg(@"C:\Tools\b.exe", 410, "5.6.7.8", 4, cT0.AddMinutes(1), cNow.AddMinutes(-1)) },
    cInv, new(),
    new() { new TransmissionRecord { Pid = 410, Image = @"C:\Tools\b.exe",
             RemoteIp = "203.0.113.9", TsUtc = cT0.AddMinutes(30), ClientBytes = 9_000, ServerBytes = 0 } });
Check("containment: ip-block, bytes to unblocked IP stay allowed",
      cV5[0].Verdict == BlockContainmentVerifier.Contained, cV5[0].Verdict);

var cV6 = BlockContainmentVerifier.Evaluate(
    new() { CAgg(@"C:\Tools\b.exe", 420, "9.9.9.9", 2, cNow.AddMinutes(-45), cNow.AddMinutes(-3)) },
    cInv, new(), new());
Check("containment: legacy block → t0 approximate from first hit",
      cV6[0].T0 == cNow.AddMinutes(-45) && cV6[0].T0Approximate, cV6[0].T0.ToString("O"));

var cV7 = BlockContainmentVerifier.Evaluate(
    new() { CAgg(@"C:\Tools\upd.exe", 500, "8.8.8.8", 2, cNow.AddMinutes(-60), cNow.AddMinutes(-30), kind: "app") },
    cInv,
    new() { new BlockContainmentVerifier.LiveSocket { Pid = 500, RemoteIp = "8.8.8.8", RemotePort = 443,
             ProcessName = "upd.exe", ProcessStartedUtc = cNow.AddMinutes(-1) } },
    new());
Check("containment: reused-pid socket flagged, never accused",
      cV7[0].PidReused && cV7[0].Verdict == BlockContainmentVerifier.Contained,
      $"{cV7[0].Verdict} reused={cV7[0].PidReused}");

var cV8 = BlockContainmentVerifier.Evaluate(
    new() { CAgg(@"C:\Tools\upd.exe", 600, "8.8.8.8", 2, cT0.AddMinutes(5), cNow.AddMinutes(-5), kind: "app") },
    cInv, new(),
    new() { new TransmissionRecord { Pid = 600, Image = @"C:\Tools\other.exe",
             RemoteIp = "8.8.8.8", TsUtc = cT0.AddMinutes(30), ClientBytes = 9_999, ServerBytes = 0 } });
Check("containment: archive join requires image match",
      cV8[0].Verdict == BlockContainmentVerifier.Contained, cV8[0].Verdict);

var cV9 = BlockContainmentVerifier.Evaluate(
    new() { CAgg(@"C:\Tools\upd.exe", 700, "8.8.8.8", 2, cT0.AddMinutes(5), cNow.AddMinutes(-5), kind: "app") },
    cInv,
    new() { new BlockContainmentVerifier.LiveSocket { Pid = 700, RemoteIp = "127.0.0.1", RemotePort = 9050,
             ProcessName = "upd.exe", ProcessStartedUtc = cT0.AddHours(-2) } },
    new());
Check("containment: loopback socket is not exfiltration",
      cV9[0].Verdict == BlockContainmentVerifier.Contained, cV9[0].Verdict);

var cV10 = BlockContainmentVerifier.Evaluate(
    new() { CAgg(@"C:\Tools\a.exe", 800, "1.2.3.4", 99, cT0, cNow),
            CAgg(@"C:\Tools\upd.exe", 300, "8.8.8.8", 2, cNow.AddMinutes(-30), cNow.AddMinutes(-5), kind: "app") },
    cInv,
    new() { new BlockContainmentVerifier.LiveSocket { Pid = 300, RemoteIp = "8.8.8.8", RemotePort = 443,
             ProcessName = "upd.exe", ProcessStartedUtc = cT0.AddHours(1) } },
    new());
Check("containment: leak sorts before contained regardless of attempts",
      cV10.Count == 2 && cV10[0].Verdict == BlockContainmentVerifier.Leak && cV10[0].Pid == 300,
      cV10.Count == 2 ? $"{cV10[0].Verdict} pid={cV10[0].Pid}" : $"count={cV10.Count}");

var cApp = cInv[2];
Check("app match: exact path (case-insensitive)",
      BlockContainmentVerifier.IsAppBlockMatch(cApp, @"c:\tools\UPD.exe"));
Check("app match: sibling path by exe name",
      BlockContainmentVerifier.IsAppBlockMatch(cApp, @"C:\Tools\v2\upd.exe"));
Check("app match: different exe never matches",
      !BlockContainmentVerifier.IsAppBlockMatch(cApp, @"C:\Tools\upd2.exe"));

// WorstForTarget — the Blocks tab's per-row roll-up
var cInsts = new List<BlockContainmentVerifier.Instance>
{
    new() { Image = @"C:\Tools\chrome.exe", Pid = 100, Verdict = BlockContainmentVerifier.Contained,
            BlockedIps = new() { "1.2.3.4" } },
    new() { Image = @"C:\Tools\upd.exe", Pid = 200, Verdict = BlockContainmentVerifier.Leak,
            BlockedIps = new() { "1.2.3.4" } },
    new() { Image = @"C:\Tools\upd.exe", Pid = 210, Verdict = BlockContainmentVerifier.StaleFlow,
            BlockedIps = new() { "5.6.7.8" } },
};
var cW1 = BlockContainmentVerifier.WorstForTarget(cInsts, "ip", "1.2.3.4");
Check("roll-up: leak outranks contained for the same IP",
      cW1 != null && cW1.Verdict == BlockContainmentVerifier.Leak && cW1.Pid == 200,
      cW1?.Verdict ?? "null");
var cW2 = BlockContainmentVerifier.WorstForTarget(cInsts, "proc", @"C:\Tools\v2\upd.exe");
Check("roll-up: app target matches sibling path by basename",
      cW2 != null && cW2.Verdict == BlockContainmentVerifier.Leak, cW2?.Verdict ?? "null");
Check("roll-up: untouched target is quiet (null)",
      BlockContainmentVerifier.WorstForTarget(cInsts, "ip", "9.9.9.9") == null);

// QuietTargets — the engine-owned quiet roll-up (the rule the bridge tool
// applies; disabled rules are never listed because there is nothing to verify)
var qInv = new List<BlockedTarget>
{
    new() { Target = "5.6.7.8", Kind = "ip", Ip = "5.6.7.8", Rules = 2, EnabledRules = 2 },
    new() { Target = "9.9.9.9", Kind = "ip", Ip = "9.9.9.9", Rules = 2, EnabledRules = 2 },
    new() { Target = @"C:\Tools\upd.exe", Kind = "app", AppPath = @"C:\Tools\upd.exe", Rules = 1, EnabledRules = 1 },
    new() { Target = "7.7.7.7", Kind = "ip", Ip = "7.7.7.7", Rules = 2, EnabledRules = 0 }, // paused
};
var quietTargets = BlockContainmentVerifier.QuietTargets(qInv, cInsts);
Check("quiet: hit ip is not quiet", quietTargets.All(t => t.Ip != "5.6.7.8"));
Check("quiet: app-blocked image (sibling path) is not quiet",
      quietTargets.All(t => !BlockContainmentVerifier.IsAppBlockMatch(t, @"C:\Tools\v2\upd.exe")));
Check("quiet: untouched enabled ip is quiet; paused rule never listed",
      quietTargets.Any(t => t.Ip == "9.9.9.9") && quietTargets.All(t => t.Ip != "7.7.7.7"));

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL CONTAIN SELF-TESTS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;
