using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.ProxyTap;

/// <summary>Which request/response headers a <see cref="ProxyTap"/> records on the log entry.</summary>
public enum HeaderCapturePolicy
{
    /// <summary>Record every header except those in <see cref="ProxyTapOptions.SecretDenylist"/> (which are redacted or dropped). The default.</summary>
    AllExceptSecrets,

    /// <summary>Record every header verbatim — including secrets. Only for fully trusted, local-only use.</summary>
    All,

    /// <summary>Record only the headers named in <see cref="ProxyTapOptions.HeaderWhitelist"/> (secrets in the list are still redacted).</summary>
    Whitelist,

    /// <summary>Record no headers at all.</summary>
    None,
}

/// <summary>
/// The generic topology schema for one <see cref="ProxyTap"/>: where it listens, where it forwards, which
/// two participants the hop joins, and how it captures, attributes and redacts. Topology <em>values</em>
/// (ports, service names) stay in the host's configuration; this is the shape they fill in.
/// </summary>
public sealed class ProxyTapOptions
{
    /// <summary>Port the tap listens on (the address callers are pointed at). Required.</summary>
    public int ListenPort { get; set; }

    /// <summary>Host name the listener binds to. Default <c>localhost</c> (no URL ACL needed on Windows).</summary>
    public string ListenHost { get; set; } = "localhost";

    /// <summary>Base URI of the real service the tap forwards to (scheme + host + port). Required.</summary>
    public Uri? ForwardBaseUri { get; set; }

    /// <summary>The calling participant as it should appear in diagrams (the service that dials the tap). Required.</summary>
    public string CallerName { get; set; } = "";

    /// <summary>The receiving participant as it should appear in diagrams (the forwarded-to service). Required.</summary>
    public string ServiceName { get; set; } = "";

    /// <summary>Optional <see cref="DependencyCategories"/> value for the receiving service (shape/colour). Null = plain HTTP service.</summary>
    public string? DependencyCategory { get; set; }

    /// <summary>Optional <see cref="DependencyCategories"/> value for the caller.</summary>
    public string? CallerDependencyCategory { get; set; }

    /// <summary>Decoded request/response bodies longer than this are truncated in the capture (the forwarded bytes are never touched). Null = unlimited. Default 65536.</summary>
    public int? BodyCapBytes { get; set; } = 65536;

    /// <summary>Whether bodies are captured at all. Default true.</summary>
    public bool CaptureBodies { get; set; } = true;

    /// <summary>Header capture policy. Default <see cref="HeaderCapturePolicy.AllExceptSecrets"/>.</summary>
    public HeaderCapturePolicy HeaderPolicy { get; set; } = HeaderCapturePolicy.AllExceptSecrets;

    /// <summary>Headers recorded under <see cref="HeaderCapturePolicy.Whitelist"/> (case-insensitive).</summary>
    public HashSet<string> HeaderWhitelist { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Headers that must never be written anywhere (case-insensitive). Defaults to
    /// <see cref="CaptureRedaction.DefaultSecretHeaders"/>. Applied at capture, before the sink — this is the
    /// security boundary, independent of any render-time exclusion.
    /// </summary>
    public HashSet<string> SecretDenylist { get; } = new(CaptureRedaction.DefaultSecretHeaders, StringComparer.OrdinalIgnoreCase);

    /// <summary>When true, secret headers are removed from the capture; when false (default) their value is replaced with <see cref="RedactedValue"/>.</summary>
    public bool DropSecretHeaders { get; set; }

    /// <summary>Replacement for redacted header values. Default <c>[REDACTED]</c>.</summary>
    public string RedactedValue { get; set; } = "[REDACTED]";

    /// <summary>
    /// Re-stamp any of the four <see cref="TestTrackingHttpHeaders"/> correlation headers that are missing on
    /// the forwarded request (test name, test id, caller name, trace id), so attribution survives a
    /// downstream hop that would otherwise drop them. Default true.
    /// </summary>
    public bool ReinjectCorrelation { get; set; } = true;

    /// <summary>
    /// When the request carries no <c>test-tracking-current-test-id</c>, attribute it to the W3C trace id of
    /// its inbound <c>traceparent</c> (the model browser-driven E2E suites use: the test mints the trace, so
    /// trace id == test id). Default true.
    /// </summary>
    public bool IdentityFromTraceparent { get; set; } = true;

    /// <summary>Additional request headers to read the test <em>name</em> from when <c>test-tracking-current-test-name</c> is absent (e.g. a legacy <c>x-my-test</c> header).</summary>
    public List<string> TestNameHeaderFallbacks { get; } = [];

    /// <summary>Additional request headers to read the test <em>id</em> from when <c>test-tracking-current-test-id</c> is absent (checked before the traceparent fallback).</summary>
    public List<string> TestIdHeaderFallbacks { get; } = [];

    /// <summary>
    /// Custom identity resolver. Receives the inbound request headers (multi-valued) and the parsed
    /// traceparent (or null); return the (name, id) to attribute to, or null to use the built-in rules.
    /// </summary>
    public Func<IReadOnlyDictionary<string, string[]>, TraceParent?, (string Name, string Id)?>? IdentityResolver { get; set; }

    /// <summary>Name used when only an id could be resolved. Default <c>Unknown</c> (the tests file / ingest normalises it later).</summary>
    public string FallbackTestName { get; set; } = TestIdentityScope.UnknownTestName;

    /// <summary>
    /// Requests with no resolvable identity are forwarded but not captured (health probes, warm-ups). Set
    /// true to capture them under <see cref="FallbackTestName"/> / a fresh id anyway. Default false.
    /// </summary>
    public bool CaptureUnattributedRequests { get; set; }

    /// <summary>
    /// When <see cref="CaptureUnattributedRequests"/> is on, capture every unattributed request under this
    /// fixed test id (so they all render in one "traffic outside any test" scenario) instead of a fresh id
    /// per request. Default null (fresh id per request).
    /// </summary>
    public string? FallbackTestId { get; set; }

    /// <summary>
    /// When set, this tap publishes the identity of every request it is currently forwarding to
    /// <see cref="ServiceName"/>, so a capturer that cannot read headers — a database tee on the same
    /// service's connections — can ask <see cref="InFlightIdentityRegistry.MostRecentFor"/> who that
    /// traffic belongs to. Default null: nothing is published and the tap behaves exactly as before.
    /// See <see cref="InFlightIdentityRegistry"/> for when this is a good idea and when ingest-time
    /// window attribution is the better one.
    /// </summary>
    public InFlightIdentityRegistry? InFlightRegistry { get; set; }

    /// <summary>Where captured entries go. Default: the in-process <see cref="RequestResponseLogger"/> store. Use <c>CompositeRequestResponseSink</c> to add an NDJSON file.</summary>
    public IRequestResponseSink Sink { get; set; } = RequestResponseLoggerSink.Instance;

    /// <summary>Phase stamped on captured entries. Default <see cref="TestPhase.Unknown"/>.</summary>
    public TestPhase Phase { get; set; } = TestPhase.Unknown;

    /// <summary>Upper bound for one forwarded exchange. Generous by default (200 s) — a tap must never be the tightest deadline on the path.</summary>
    public TimeSpan ForwardTimeout { get; set; } = TimeSpan.FromSeconds(200);

    /// <summary>Connect timeout to the forward target. Default 5 s.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Emit a server span (parented on the inbound <c>traceparent</c>) and a client span for the forwarded
    /// call on <see cref="ProxyTap.ActivitySource"/>, re-parenting the forwarded <c>traceparent</c> on the
    /// client span so the downstream service nests under the tap in a distributed trace. Free when no
    /// listener is attached. Default true.
    /// </summary>
    public bool EmitActivities { get; set; } = true;

    /// <summary>When the inbound request has no <c>traceparent</c>, synthesise one for the forwarded request (so every hop is traceable). Default true.</summary>
    public bool SynthesizeTraceparent { get; set; } = true;

    /// <summary>Optional diagnostic log callback (listener start/stop, forwarding errors).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Optional human-readable name of this tap for diagnostics (defaults to <c>caller→service</c>).</summary>
    public string? Name { get; set; }

    internal string DisplayName => Name ?? $"{CallerName}→{ServiceName}";

    internal void Validate()
    {
        if (ListenPort is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(ListenPort), "ListenPort must be a valid TCP port.");
        if (ForwardBaseUri is null || !ForwardBaseUri.IsAbsoluteUri)
            throw new ArgumentException("ForwardBaseUri must be an absolute URI (scheme + host + port).", nameof(ForwardBaseUri));
        if (string.IsNullOrWhiteSpace(CallerName))
            throw new ArgumentException("CallerName is required.", nameof(CallerName));
        if (string.IsNullOrWhiteSpace(ServiceName))
            throw new ArgumentException("ServiceName is required.", nameof(ServiceName));
        if (string.IsNullOrWhiteSpace(ListenHost))
            throw new ArgumentException("ListenHost is required.", nameof(ListenHost));
    }
}

/// <summary>A parsed W3C <c>traceparent</c> header.</summary>
/// <param name="TraceId">32 lowercase hex characters.</param>
/// <param name="ParentSpanId">16 lowercase hex characters.</param>
/// <param name="Flags">The trace-flags byte (bit 0 = sampled).</param>
public sealed record TraceParent(string TraceId, string ParentSpanId, byte Flags)
{
    /// <summary>Whether the sampled flag is set.</summary>
    public bool Sampled => (Flags & 1) == 1;

    /// <summary>Parses a <c>00-{trace}-{span}-{flags}</c> value; returns null for anything malformed (including an all-zero trace id).</summary>
    public static TraceParent? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var parts = value.Trim().Split('-');
        if (parts.Length < 4 || parts[1].Length != 32 || parts[2].Length != 16 || parts[3].Length != 2)
            return null;
        if (!IsHex(parts[1]) || !IsHex(parts[2]) || !IsHex(parts[3]))
            return null;
        if (parts[1].All(c => c == '0'))
            return null;
        return new TraceParent(parts[1].ToLowerInvariant(), parts[2].ToLowerInvariant(), Convert.ToByte(parts[3], 16));
    }

    /// <summary>Formats a header value for the given ids.</summary>
    public static string Format(string traceId, string spanId, byte flags = 1) => $"00-{traceId}-{spanId}-{flags:x2}";

    /// <summary>Renders this value back to header form.</summary>
    public override string ToString() => Format(TraceId, ParentSpanId, Flags);

    private static bool IsHex(string s) => s.All(Uri.IsHexDigit);
}
