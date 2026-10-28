namespace AutoCommand.Engines.Watch

open AutoCommand.Engines.Core

/// <summary>Host configuration for the watch engines: call
/// WatchConfig.configure(identity) once at app startup, before any engine
/// member is touched. Engines are app-agnostic; paths and ports come from
/// the identity.</summary>
[<AbstractClass; Sealed>]
type WatchConfig private () =
    static let mutable identity = Unchecked.defaultof<AppIdentity>
    static let mutable configured = false
    static member Configure(id: AppIdentity) =
        identity <- id
        configured <- true
    static member Identity =
        if not configured then failwith "WatchConfig.Configure(identity) must be called at app startup"
        identity

// F# port note: this interface lived in RowModel.fs, but RowModel needs
// DnsResolutionService (ResolveStateText) and Dns needs the interface —
// F# orders files, so the seam lives here, before both (C# had no order).
/// <summary>The seam DnsResolutionService pushes resolved hostnames
/// through, so any row model (SvchostMonitorItem, TrafficStat) can be
/// resolved without the resolver knowing the concrete type.</summary>
type IResolvableHost =
    abstract RemoteIp: string with get
    abstract Hostname: string with get, set
