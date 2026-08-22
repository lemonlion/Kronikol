using Kronikol.Tracking;

namespace Kronikol.Extensions.Otlp;

/// <summary>The span families <see cref="SpanToInteractionMapper"/> can turn into interactions (see <see cref="OtlpTapOptions.CaptureKinds"/>).</summary>
public static class OtlpCaptureKinds
{
    /// <summary>Database client spans (<c>db.system</c> / <c>db.system.name</c>). On by default.</summary>
    public const string Db = "db";

    /// <summary>HTTP client spans (<c>http.request.method</c> / <c>http.method</c>). On by default.</summary>
    public const string Http = "http";

    /// <summary>Messaging producer/consumer spans (<c>messaging.system</c>). Off by default.</summary>
    public const string Messaging = "messaging";

    /// <summary>RPC client spans (<c>rpc.system</c>, e.g. gRPC). Off by default.</summary>
    public const string Rpc = "rpc";

    /// <summary>The default set: <see cref="Db"/> and <see cref="Http"/>.</summary>
    public static readonly string[] Default = [Db, Http];
}

/// <summary>
/// The generic schema for one <see cref="OtlpTap"/>: where it listens, whether it forwards to a real
/// collector, who may talk to it, which spans become interactions and what the participants are called.
/// Topology <em>values</em> (ports, service names, the shared secret) stay in the host's configuration;
/// this is the shape they fill in. Also the input to <see cref="SpanToInteractionMapper"/>, which can be
/// used on its own (offline mapping of a captured export) without ever starting a listener.
/// </summary>
public sealed class OtlpTapOptions
{
    /// <summary>Port the receiver listens on. <c>0</c> binds a free port (read it back from <see cref="OtlpTap.BoundPort"/>). Required.</summary>
    public int ListenPort { get; set; }

    /// <summary>
    /// Interface to bind. Default <c>localhost</c>. Use <c>+</c>, <c>*</c>, <c>0.0.0.0</c> or <c>any</c> for
    /// every interface — needed when the exporter runs in a container and reaches the host through
    /// <c>host.docker.internal</c>. The listener is a plain socket (not <c>HttpListener</c>/http.sys), so a
    /// non-loopback bind needs no <c>netsh http add urlacl</c> and no elevation; the first bind may still
    /// raise a one-time Windows Defender firewall prompt for <c>dotnet.exe</c>. Always pair a non-loopback
    /// bind with <see cref="ExpectedHeaders"/>.
    /// </summary>
    public string ListenHost { get; set; } = "localhost";

    /// <summary>
    /// Optional real collector to forward every request to, byte-for-byte (same method, path, headers and
    /// body, including <c>Content-Encoding</c>); its status and body are relayed to the caller. Null (the
    /// default) answers <c>200</c> with an empty <c>ExportTraceServiceResponse</c> locally — the shape for a
    /// fan-out leaf, where the collector already keeps its own copy.
    /// </summary>
    public Uri? ForwardBaseUri { get; set; }

    /// <summary>
    /// Headers every request must carry (name → exact value, name case-insensitive). Anything missing or
    /// different is answered <c>401</c> and counted in <see cref="OtlpTap.UnauthenticatedRequests"/>, without
    /// being forwarded or mapped. Empty (the default) accepts everything — only safe on loopback. The
    /// intended use is a per-start shared secret, e.g. <c>x-kronikol-tap: &lt;random&gt;</c>, set on the
    /// exporter with <c>OTEL_EXPORTER_OTLP_HEADERS</c>.
    /// </summary>
    public Dictionary<string, string> ExpectedHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The path the receiver maps. Default <c>/v1/traces</c>.</summary>
    public string TracesPath { get; set; } = "/v1/traces";

    /// <summary>Largest export payload accepted, in bytes (bigger ones are answered <c>413</c>). Default 32 MiB.</summary>
    public int MaxRequestBytes { get; set; } = 32 * 1024 * 1024;

    /// <summary>
    /// How many accepted export payloads may wait to be mapped. The queue is bounded and drops the
    /// <em>newest</em> payload when full (D3: capture never blocks the producer); drops are counted in
    /// <see cref="OtlpTap.PayloadsDropped"/>. Default 256.
    /// </summary>
    public int QueueCapacity { get; set; } = 256;

    /// <summary>Where mapped interactions go. Default: the in-process <see cref="RequestResponseLogger"/> store. Use an <c>NdjsonInteractionWriter</c> (or a <c>CompositeRequestResponseSink</c>) to feed <c>kronikol ingest</c>.</summary>
    public IRequestResponseSink Sink { get; set; } = RequestResponseLoggerSink.Instance;

    /// <summary>Phase stamped on mapped interactions. Default <see cref="TestPhase.Unknown"/>.</summary>
    public TestPhase Phase { get; set; } = TestPhase.Unknown;

    // ------------------------------------------------------------------ mapping

    /// <summary>
    /// OTel <c>service.name</c> (and, for the receiving side of a call, <c>peer.service</c> /
    /// <c>server.address</c> / <c>server.address:port</c> / the <c>db.system</c>) → the participant name the
    /// diagram should use. Case-insensitive. Unmapped names are used verbatim.
    /// </summary>
    public Dictionary<string, string> ServiceNameMap { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which span families become interactions. Default <c>db</c> + <c>http</c> (see <see cref="OtlpCaptureKinds"/>).</summary>
    public HashSet<string> CaptureKinds { get; } = new(OtlpCaptureKinds.Default, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Map <see cref="OtlpSpanKind.Server"/> spans too. Default false: in a tapped stack the inbound hop is
    /// already captured with bodies by a proxy tap, so mapping it as well would draw the arrow twice. Turn it
    /// on for a stack with no taps.
    /// </summary>
    public bool IncludeServerSpans { get; set; }

    /// <summary>
    /// Attribute each interaction to its span's W3C trace id (<c>testId = traceId</c>). This is the exact
    /// attribution path: a browser-driven suite mints the trace id as the test id, so no window guessing is
    /// needed. Default true. When false — or when <see cref="KnownTestIds"/> rejects the trace id — the
    /// interaction is attributed to <see cref="FallbackTestId"/> (and the ingest's fold bucket or window
    /// attribution takes over).
    /// </summary>
    public bool AttributeByTraceId { get; set; } = true;

    /// <summary>
    /// Optional predicate over a trace id: return false for a trace that is not a test, to send it to
    /// <see cref="FallbackTestId"/> instead. Default null — every trace id is taken as a test id and the
    /// ingest decides what is known.
    /// </summary>
    public Func<string, bool>? KnownTestIds { get; set; }

    /// <summary>Test id used when the trace id cannot be the test id. Default null: a stable id derived from the trace id is used, so the calls of one trace still group.</summary>
    public string? FallbackTestId { get; set; }

    /// <summary>Test name stamped on mapped interactions. Default <c>Unknown</c> (the tests file / ingest normalises it later).</summary>
    public string FallbackTestName { get; set; } = TestIdentityScope.UnknownTestName;

    /// <summary>Captured statements/queries longer than this are truncated. Null = unlimited. Default 65536.</summary>
    public int? ContentCapBytes { get; set; } = 65536;

    /// <summary>Participant name used when a span carries no <c>service.name</c>. Default <c>unknown-service</c>.</summary>
    public string DefaultCallerName { get; set; } = "unknown-service";

    // ------------------------------------------------------------------ diagnostics

    /// <summary>Optional diagnostic log callback (listener start/stop, forward failures, drops).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Optional human-readable name of this tap for diagnostics. Default <c>otlp</c>.</summary>
    public string? Name { get; set; }

    internal string DisplayName => Name ?? "otlp";

    internal void Validate()
    {
        if (ListenPort is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(ListenPort), "ListenPort must be a valid TCP port (or 0 for a free one).");
        if (string.IsNullOrWhiteSpace(ListenHost))
            throw new ArgumentException("ListenHost is required.", nameof(ListenHost));
        if (ForwardBaseUri is not null && !ForwardBaseUri.IsAbsoluteUri)
            throw new ArgumentException("ForwardBaseUri must be an absolute URI (scheme + host + port).", nameof(ForwardBaseUri));
        if (string.IsNullOrWhiteSpace(TracesPath) || !TracesPath.StartsWith('/'))
            throw new ArgumentException("TracesPath must be an absolute path such as /v1/traces.", nameof(TracesPath));
        if (QueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity), "QueueCapacity must be positive.");
        if (MaxRequestBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRequestBytes), "MaxRequestBytes must be positive.");
    }
}
