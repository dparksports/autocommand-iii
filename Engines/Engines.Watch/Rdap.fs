// F# port of Core\NetWatch\RdapService.cs (namespace AutoCommand.NetWatch.Core),
// an autocommand-ii service autoscope never ported — written for AutoCommand
// III so the F# geo engine (Dns.fs) and the transmissions pipeline share one
// in-engine RDAP client. Behavioral faithfulness is the contract: same
// block-keyed cache, same IANA-bootstrap routing with rdap.org fallback, same
// throttle layers (pacer + per-host cooldown honoring Retry-After + negative
// prefix caching), same TTL clamps, same public member names.
//
// F# port notes (deviations, none behavioral beyond the cache location):
//   • The C# nested public class RdapService.CacheEntry is a top-level
//     RdapCacheEntry (F# cannot nest types in a class); no C# caller named
//     the nested type explicitly (verified) — all access is via var/results.
//   • The C# cached to ProgramData\AutoCommand (machine-wide); engines take
//     every path from the identity, so the cache and bootstrap files live
//     under WatchConfig.Identity.DataRoot. Same file names, same JSON, so an
//     existing machine cache can be carried over by hand.
//   • The C# private nested BootstrapRule/BootstrapFile are public DTOs in
//     the RdapBootstrapDto module — F# caps member accessibility to the
//     type's visibility, so private DTO properties would compile non-public
//     and System.Text.Json would silently write {}.

namespace AutoCommand.Engines.Watch

open AutoCommand.Engines.Core

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices

/// <summary>One RDAP answer for a whole registered block (was the nested
/// RdapService.CacheEntry). Transient entries (queue full / cooldown) are
/// returned to the caller but never cached or persisted.</summary>
[<AllowNullLiteral>]
type RdapCacheEntry() =
    member val StartIp: string = null with get, set
    member val EndIp: string = null with get, set
    member val QueryIp: string = null with get, set
    member val Netname: string = null with get, set
    member val Org: string = null with get, set
    member val Country: string = null with get, set
    member val Type: string = null with get, set
    member val AbuseEmail: string = null with get, set
    member val IsNegative: bool = false with get, set
    member val FetchedUtc = DateTime() with get, set
    member val ExpiresUtc = DateTime() with get, set
    /// <summary>Cooldown/queue answer, returned to the caller but never
    /// cached (already expired, skipped on Save).</summary>
    [<JsonIgnore>]
    member val IsTransient: bool = false with get, set
    member this.IsCurrent: bool = DateTime.UtcNow < this.ExpiresUtc

module RdapBootstrapDto =

    /// <summary>One IANA bootstrap rule (RFC 9224): the CIDRs one registry
    /// serves and its RDAP URL.</summary>
    type BootstrapRule() =
        member val Family = 0 with get, set // 4 or 6
        member val Cidrs: ResizeArray<string> = null with get, set
        member val Url: string = null with get, set

    type BootstrapFile() =
        member val FetchedUtc = DateTime() with get, set
        member val Rules: ResizeArray<BootstrapRule> = null with get, set

/// <summary>
/// RDAP client with a persistent, block-keyed cache, IANA-bootstrap
/// routing and throttle-aware pacing. See the C# header for the design
/// rationale (why block-keyed, why bootstrap instead of rdap.org, the
/// three throttle layers, private-space short-circuit).
/// </summary>
type RdapService() as this =

    // ── throttle policy ───────────────────────────────────────────────
    static let minRequestInterval = TimeSpan.FromSeconds 10.
    static let positiveTtlFloor = TimeSpan.FromDays 1.
    static let positiveTtlCeiling = TimeSpan.FromDays 30.
    static let defaultPositiveTtl = TimeSpan.FromDays 7.
    static let negativeTtl = TimeSpan.FromHours 6.
    static let networkErrorTtl = TimeSpan.FromMinutes 30.
    static let defaultCooldown = TimeSpan.FromMinutes 15.
    static let bootstrapRefreshInterval = TimeSpan.FromHours 24.
    static let bootstrapRetryInterval = TimeSpan.FromHours 1.
    static let maxQueuedLookups = 25

    static let http =
        let client = new HttpClient(Timeout = TimeSpan.FromSeconds 15.)

        try
            // registries (ARIN especially) throttle anonymous clients
            let v = typeof<RdapService>.Assembly.GetName().Version
            let version = if isNull (box v) then "1.0" else v.ToString(3)

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"{Branding.DisplayName}/{version} (Windows network-attribution client)")
        with _ -> () // UA is politeness, not correctness

        client

    let cachePath = Path.Combine(WatchConfig.Identity.DataRoot, "rdap-cache.json")
    let bootstrapPath = Path.Combine(WatchConfig.Identity.DataRoot, "rdap-bootstrap.json")

    let gateLock = obj ()
    let mutable entries = ResizeArray<RdapCacheEntry>()
    let inflight = Dictionary<string, Task<RdapCacheEntry>>()

    // request pacing / cooldown state (nextSlotUtc is serialized by the
    // gate; everything else by gateLock)
    let netGate = new SemaphoreSlim(1, 1)
    let mutable nextSlotUtc = DateTime()
    let hostCooldownUntil = Dictionary<string, DateTime>()
    let mutable queued = 0

    // IANA bootstrap (RFC 9224) — registry endpoint per CIDR
    let mutable bootstrap: ResizeArray<RdapBootstrapDto.BootstrapRule> = null
    let mutable bootstrapFetchedUtc = DateTime.MinValue
    let mutable bootstrapRetryAtUtc = DateTime()
    let mutable bootstrapRefreshTask: Task = null

    do
        this.Load()
        this.LoadBootstrap()

    static member val Instance: RdapService = RdapService()

    member _.CachePath: string = cachePath

    member _.EntryCount: int =
        lock gateLock (fun () -> entries.Count)

    // ── cache persistence ─────────────────────────────────────────────

    member private _.Load() : unit =
        try
            if File.Exists cachePath then
                match JsonSerializer.Deserialize<ResizeArray<RdapCacheEntry>>(File.ReadAllText cachePath) with
                | null -> ()
                | list -> entries <- list
        with _ -> entries <- List<RdapCacheEntry>()

    member private _.Save() : unit =
        try
            // trim: expired entries first, then oldest — the cache covers
            // whole blocks, so 500 entries is effectively unbounded reach
            lock gateLock (fun () ->
                let trimmed =
                    entries
                    |> Seq.sortByDescending (fun e -> e.ExpiresUtc)
                    |> Seq.truncate 500
                    |> ResizeArray

                entries <- trimmed)

            Directory.CreateDirectory(Path.GetDirectoryName cachePath) |> ignore

            File.WriteAllText(
                cachePath,
                JsonSerializer.Serialize(entries, JsonSerializerOptions(WriteIndented = true))
            )
        with _ -> () // cache is an optimization, never fatal

    // ── lookup ────────────────────────────────────────────────────────

    /// <summary>Cached block containing this IP, if any (never hits network).
    /// A current positive block outranks a covering negative prefix.</summary>
    member _.Cached(ip: string) : RdapCacheEntry =
        lock gateLock (fun () ->
            let mutable positive: RdapCacheEntry = null
            let mutable negative: RdapCacheEntry = null

            for e in entries do
                if e.IsCurrent && RdapService.Contains(e, ip) then
                    if not e.IsNegative then
                        if isNull (box positive) then positive <- e
                    elif isNull (box negative) then negative <- e

            if not (isNull (box positive)) then positive else negative)

    /// <summary>Lookup with cache + in-flight dedup. Network failures are
    /// returned as negative entries, not thrown. <paramref name="urgent"/>
    /// (user-initiated) skips the inter-request spacing, never the cooldown
    /// or queue limits.</summary>
    member _.LookupAsync(ip: string, [<Optional; DefaultParameterValue(false)>] urgent: bool) : Task<RdapCacheEntry> =
        lock gateLock (fun () ->
            let hit = this.Cached(ip)

            if not (isNull (box hit)) then
                Task.FromResult hit
            else
                match inflight.TryGetValue(ip) with
                | true, task -> task
                | _ ->
                    let task = this.FetchAsync(ip, urgent)
                    inflight.[ip] <- task
                    task)

    member private _.FetchAsync(ip: string, urgent: bool) : Task<RdapCacheEntry> =
        task {
            try
                let! entry = this.FetchCoreAsync(ip, urgent)

                if not entry.IsTransient then
                    lock gateLock (fun () ->
                        // drop this entry's predecessor and any entries the
                        // new positive block covers — but never let a negative
                        // result delete a still-valid positive block
                        entries.RemoveAll(fun e ->
                            obj.ReferenceEquals(e, entry)
                            || (not entry.IsNegative
                                && not e.IsNegative
                                && RdapService.Contains(entry, e.QueryIp)))
                        |> ignore

                        entries.Add entry)

                    this.Save()

                return entry
            finally
                lock gateLock (fun () -> inflight.Remove(ip) |> ignore)
        }

    member private _.FetchCoreAsync(ip: string, urgent: bool) : Task<RdapCacheEntry> =
        task {
            // Private/reserved space has no registry data — answer locally
            // instead of spending a request on it.
            let addr = RdapService.ParseIp ip

            if isNull (box addr) || RdapService.IsNonPublic addr then
                return RdapService.Negative(ip, "private/reserved range")

            else
                let url = this.ResolveEndpoint(ip, addr)
                let host = Uri(url).Host

                let mutable why = null
                let mutable cooldown = false
                let initial = this.CooldownActive host
                cooldown <- fst initial
                why <- snd initial

                if cooldown then
                    return RdapService.Transient(ip, why)
                else
                    let bumped = Interlocked.Increment(&queued)

                    if bumped > maxQueuedLookups then
                        Interlocked.Decrement(&queued) |> ignore
                        return RdapService.Transient(ip, "lookup queue full")
                    else
                        // F# port: the C# try { try/finally } catch { … } finally { … }
                        // nests three-deep — F# allows only with OR finally per try.
                        try
                            try
                                do! netGate.WaitAsync()

                                try
                                    // another request may have been throttled while we waited
                                    let recheck = this.CooldownActive host
                                    cooldown <- fst recheck
                                    why <- snd recheck

                                    if cooldown then
                                        return RdapService.Transient(ip, why)
                                    else
                                        if not urgent then
                                            let wait = nextSlotUtc - DateTime.UtcNow
                                            if wait > TimeSpan.Zero then do! Task.Delay wait

                                        nextSlotUtc <- DateTime.UtcNow + minRequestInterval

                                        use! resp = http.GetAsync(url)
                                        return! this.HandleResponse(resp, ip, host)
                                finally
                                    netGate.Release() |> ignore
                            with ex ->
                                // network trouble — shorter negative so attribution
                                // recovers quickly once the network does
                                return RdapService.Negative(ip, ex.Message, networkErrorTtl)
                        finally
                            Interlocked.Decrement(&queued) |> ignore
        }

    member private _.HandleResponse(resp: HttpResponseMessage, ip: string, host: string) : Task<RdapCacheEntry> =
        task {
            let code = int resp.StatusCode

            if code = 429 || code = 503 then
                let mutable cool = defaultCooldown

                if not (isNull (box resp.Headers.RetryAfter)) then
                    match resp.Headers.RetryAfter.Delta with
                    | d when d.HasValue && d.Value > TimeSpan.Zero ->
                        cool <- TimeSpan.FromMinutes(Math.Clamp(d.Value.TotalMinutes, 1., 60.))
                    | _ -> ()

                lock gateLock (fun () -> hostCooldownUntil.[host] <- DateTime.UtcNow + cool)

                return RdapService.Transient(ip, $"HTTP {code} — {host} cooling down")

            elif not resp.IsSuccessStatusCode then
                return RdapService.Negative(ip, $"HTTP {code}")

            else
                let! body = resp.Content.ReadAsStringAsync()
                let entry = RdapService.Parse(body, ip)

                if isNull (box entry) then
                    return RdapService.Negative(ip, "unparseable response")
                else
                    if not entry.IsNegative then
                        // clamp server max-age: a max-age=0 answer must not
                        // defeat the block-keyed cache
                        let mutable ttl = defaultPositiveTtl

                        if not (isNull (box resp.Headers.CacheControl)) then
                            let maxAge = resp.Headers.CacheControl.MaxAge
                            if maxAge.HasValue then ttl <- maxAge.Value

                        if ttl < positiveTtlFloor then ttl <- positiveTtlFloor
                        if ttl > positiveTtlCeiling then ttl <- positiveTtlCeiling
                        entry.ExpiresUtc <- DateTime.UtcNow + ttl

                    return entry
        }

    member private _.CooldownActive(host: string) : bool * string =
        lock gateLock (fun () ->
            match hostCooldownUntil.TryGetValue(host) with
            | true, until when DateTime.UtcNow < until ->
                true, $"{host} cooling down (throttled)"
            | _ -> false, null)

    // ── negative / transient entries ──────────────────────────────────

    /// <summary>Negative answer covering the queried IP's /24 (IPv4) or
    /// /48 (IPv6) prefix, so sibling-IP rotation shares one failure.</summary>
    static member private Negative(ip: string, why: string, ?ttl: TimeSpan) : RdapCacheEntry =
        let mutable lo = null
        let mutable hi = null

        try
            let b = IPAddress.Parse(ip).GetAddressBytes()

            if b.Length = 4 then
                lo <- $"{b.[0]}.{b.[1]}.{b.[2]}.0"
                hi <- $"{b.[0]}.{b.[1]}.{b.[2]}.255"
            elif b.Length = 16 then
                let start = Array.zeroCreate 16
                Array.Copy(b, start, 6)
                let last = (start.Clone() :?> byte[])

                for i = 6 to 15 do
                    last.[i] <- 0xFFuy

                lo <- IPAddress(start).ToString()
                hi <- IPAddress(last).ToString()
        with _ -> () // unparseable → exact-IP-only entry (Contains falls back)

        RdapCacheEntry(
            QueryIp = ip,
            IsNegative = true,
            Netname = why,
            StartIp = lo,
            EndIp = hi,
            FetchedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow + (defaultArg ttl negativeTtl)
        )

    /// <summary>Not an answer (queue full / host cooling down): returned so
    /// callers keep their static label, but never cached or persisted.</summary>
    static member private Transient(ip: string, why: string) : RdapCacheEntry =
        RdapCacheEntry(
            QueryIp = ip,
            IsNegative = true,
            IsTransient = true,
            Netname = why,
            FetchedUtc = DateTime.UtcNow // ExpiresUtc left at default → never current
        )

    // ── endpoint resolution: bootstrap first, rdap.org fallback ──────

    member private _.ResolveEndpoint(ip: string, addr: byte[]) : string =
        this.EnsureBootstrapFresh()
        let family = if addr.Length = 16 then 6 else 4

        let mutable found: string = null

        lock gateLock (fun () ->
            if not (isNull (box bootstrap)) then
                for r in bootstrap do
                    if isNull (box found) && r.Family = family then
                        for cidr in r.Cidrs do
                            if isNull (box found) && RdapService.CidrContains(cidr, addr) then
                                found <- r.Url.TrimEnd('/') + "/ip/" + ip)

        if not (isNull (box found)) then found else $"https://rdap.org/ip/{ip}"

    member private _.EnsureBootstrapFresh() : unit =
        lock gateLock (fun () ->
            let empty = isNull (box bootstrap) || bootstrap.Count = 0
            let stale = bootstrapFetchedUtc + bootstrapRefreshInterval < DateTime.UtcNow

            if (empty || stale) && DateTime.UtcNow >= bootstrapRetryAtUtc then
                bootstrapRetryAtUtc <- DateTime.UtcNow + bootstrapRetryInterval

                if isNull (box bootstrapRefreshTask) then
                    bootstrapRefreshTask <- this.RefreshBootstrapAsync())

    member private _.RefreshBootstrapAsync() : Task =
        task {
            // F# port: C#'s try { … } catch { … } finally { … } — F# allows
            // only with OR finally per try, and the catch never rethrows, so
            // the finally action runs as the next statement.
            try
                let rules = ResizeArray<RdapBootstrapDto.BootstrapRule>()

                for family in [ 4; 6 ] do
                    let! body = http.GetStringAsync($"https://data.iana.org/rdap/ipv{family}.json")
                    rules.AddRange(RdapService.ParseBootstrap(body, family))

                if rules.Count = 0 then failwith "empty bootstrap"
                else
                    let file = RdapBootstrapDto.BootstrapFile(FetchedUtc = DateTime.UtcNow, Rules = rules)
                    Directory.CreateDirectory(Path.GetDirectoryName bootstrapPath) |> ignore

                    File.WriteAllText(
                        bootstrapPath,
                        JsonSerializer.Serialize(file, JsonSerializerOptions(WriteIndented = true))
                    )

                    lock gateLock (fun () ->
                        bootstrap <- rules
                        bootstrapFetchedUtc <- file.FetchedUtc)
            with _ ->
                // keep whatever we already have (possibly nothing) and retry
                // later — lookups fall back to rdap.org meanwhile
                lock gateLock (fun () -> bootstrapRetryAtUtc <- DateTime.UtcNow + bootstrapRetryInterval)

            lock gateLock (fun () -> bootstrapRefreshTask <- null)
        }

    member private _.LoadBootstrap() : unit =
        try
            if File.Exists bootstrapPath then
                let file =
                    JsonSerializer.Deserialize<RdapBootstrapDto.BootstrapFile>(File.ReadAllText bootstrapPath)

                if not (isNull (box file)) && not (isNull (box file.Rules)) && file.Rules.Count > 0 then
                    bootstrap <- file.Rules
                    bootstrapFetchedUtc <- file.FetchedUtc
        with _ -> bootstrap <- null

    // ── parsing / address math ────────────────────────────────────────

    static member private ParseBootstrap(json: string, family: int) : ResizeArray<RdapBootstrapDto.BootstrapRule> =
        let rules = ResizeArray<RdapBootstrapDto.BootstrapRule>()

        try
            use doc = JsonDocument.Parse json

            for svc in doc.RootElement.GetProperty("services").EnumerateArray() do
                // RFC 9224 bootstrap format: each service is a nested
                // array [ [cidrs…], [urls…] ] — not keyed objects
                if svc.ValueKind = JsonValueKind.Array && svc.GetArrayLength() >= 2 then
                    let cidrs = svc.[0]
                    let urls = svc.[1]

                    if cidrs.ValueKind = JsonValueKind.Array && urls.ValueKind = JsonValueKind.Array then
                        let mutable url = null

                        for u in urls.EnumerateArray() do
                            let s = u.GetString()

                            if not (String.IsNullOrEmpty s) then
                                if isNull (box url) then url <- s

                                if s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then
                                    url <- s // prefer https when offered

                        if not (isNull (box url)) then
                            let list = ResizeArray<string>()

                            for c in cidrs.EnumerateArray() do
                                let s = c.GetString()
                                if not (String.IsNullOrEmpty s) then list.Add s

                            if list.Count > 0 then
                                rules.Add(
                                    RdapBootstrapDto.BootstrapRule(
                                        Family = family,
                                        Cidrs = list,
                                        Url = url
                                    )
                                )
        with _ -> () // malformed file → act as if absent (rdap.org fallback)

        rules

    static member private Parse(json: string, queryIp: string) : RdapCacheEntry =
        try
            use doc = JsonDocument.Parse json
            let root = doc.RootElement
            let mutable org = null
            let mutable abuse = null

            match root.TryGetProperty("entities") with
            | true, entities ->
                for ent in entities.EnumerateArray() do
                    let roles =
                        match ent.TryGetProperty("roles") with
                        | true, r -> r.EnumerateArray() |> Seq.map (fun x -> x.GetString()) |> Seq.toList
                        | _ -> []

                    let fn = RdapService.VcardValue(ent, "fn")

                    if Seq.exists (fun (x: string) -> x = "registrant") roles && isNull (box org) then org <- fn

                    if Seq.exists (fun (x: string) -> x = "abuse") roles then
                        if isNull (box abuse) then abuse <- RdapService.VcardValue(ent, "email")

                        if not (isNull (box fn)) && isNull (box abuse) then abuse <- fn
            | _ -> ()

            RdapCacheEntry(
                QueryIp = queryIp,
                StartIp = RdapService.GetString(root, "startAddress"),
                EndIp = RdapService.GetString(root, "endAddress"),
                Netname = RdapService.GetString(root, "name"),
                Country = RdapService.GetString(root, "country"),
                Type = RdapService.GetString(root, "type"),
                Org = org,
                AbuseEmail = abuse,
                FetchedUtc = DateTime.UtcNow
            )
        with _ -> Unchecked.defaultof<RdapCacheEntry>

    static member private VcardValue(entity: JsonElement, key: string) : string =
        try
            match entity.TryGetProperty("vcardArray") with
            | true, va when va.GetArrayLength() > 1 ->
                let mutable result = null

                for field in va.[1].EnumerateArray() do
                    if
                        isNull (box result)
                        && field.GetArrayLength() > 3
                        && field.[0].GetString() = key
                    then
                        result <- field.[3].GetString()

                result
            | _ -> null
        with _ -> null

    static member private GetString(e: JsonElement, prop: string) : string =
        match e.TryGetProperty(prop) with
        | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
        | _ -> null

    static member private ParseIp(ip: string) : byte[] =
        try
            IPAddress.Parse(ip).GetAddressBytes()
        with _ -> Unchecked.defaultof<byte[]>

    /// <summary>True when the entry's start–end range contains the IP (v4
    /// and v6). Entries without a range (legacy negatives) match only their
    /// exact query IP.</summary>
    static member private Contains(e: RdapCacheEntry, ip: string) : bool =
        if String.IsNullOrEmpty e.StartIp || String.IsNullOrEmpty e.EndIp then
            e.IsNegative && String.Equals(e.QueryIp, ip, StringComparison.OrdinalIgnoreCase)
        else
            try
                let lo = IPAddress.Parse(e.StartIp).GetAddressBytes()
                let hi = IPAddress.Parse(e.EndIp).GetAddressBytes()
                let v = IPAddress.Parse(ip).GetAddressBytes()

                if lo.Length <> v.Length then false
                else
                    let cmpLo = RdapService.Compare(v, lo)
                    let cmpHi = RdapService.Compare(v, hi)
                    cmpLo >= 0 && cmpHi <= 0
            with _ -> false

    static member private CidrContains(cidr: string, ip: byte[]) : bool =
        let slash = cidr.IndexOf('/')

        if slash <= 0 then
            false
        else
            try
                let net = IPAddress.Parse(cidr.Substring(0, slash)).GetAddressBytes()

                if net.Length <> ip.Length then
                    false
                else
                    match Int32.TryParse(cidr.Substring(slash + 1)) with
                    | true, bits when bits >= 0 && bits <= net.Length * 8 ->
                        let full = bits / 8
                        let rem = bits % 8
                        let mutable ok = true

                        for i = 0 to full - 1 do
                            if net.[i] <> ip.[i] then ok <- false

                        if ok && rem > 0 then
                            let mask = (0xFF <<< (8 - rem)) &&& 0xFF
                            (int net.[full] &&& mask) = (int ip.[full] &&& mask)
                        else
                            ok
                    | _ -> false
            with _ -> false

    /// <summary>Private/reserved/loopback/link-local/multicast space — no
    /// registry has data for it, so never spend a request on it.</summary>
    static member private IsNonPublic(b: byte[]) : bool =
        if b.Length = 4 then
            b.[0] = 0uy
            || b.[0] = 10uy
            || b.[0] = 127uy
            || (b.[0] = 169uy && b.[1] = 254uy)
            || (b.[0] = 172uy && (int b.[1] &&& 0xF0) = 16)
            || (b.[0] = 192uy && b.[1] = 168uy)
            || b.[0] >= 224uy
        elif b.Length = 16 then
            // IPv4-mapped (::ffff:a.b.c.d) — judge by the embedded v4 address
            let mutable mapped = b.[10] = 0xFFuy && b.[11] = 0xFFuy
            let mutable i = 0

            while mapped && i < 10 do
                if b.[i] <> 0uy then mapped <- false
                i <- i + 1

            if mapped then
                let v4 = Array.zeroCreate 4
                Array.Copy(b, 12, v4, 0, 4)
                RdapService.IsNonPublic v4
            else
                b.[0] = 0uy // ::/8 — IETF reserved (::, ::1, …)
                || (int b.[0] &&& 0xFE) = 0xFC // fc00::/7 unique-local
                || (b.[0] = 0xFEuy && (int b.[1] &&& 0xC0) = 0x80) // fe80::/10 link-local
                || b.[0] = 0xFFuy // ff00::/8 multicast
        else
            true // unknown family — don't send it to a public registry

    static member private Compare(a: byte[], b: byte[]) : int =
        let mutable result = 0
        let mutable i = 0

        while result = 0 && i < a.Length do
            if a.[i] <> b.[i] then result <- (if a.[i] < b.[i] then -1 else 1)
            i <- i + 1

        result
