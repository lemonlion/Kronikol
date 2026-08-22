using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.TcpTap;

/// <summary>How much detail a tap records for each decoded command.</summary>
public enum TapVerbosity
{
    /// <summary>Minimal labels only — keys, filters, values and reply payloads are omitted.</summary>
    Summarised,

    /// <summary>Classified label plus the target resource (key / collection) and the filter or value. The default.</summary>
    Detailed,

    /// <summary>The raw command name, the full endpoint in the URI, and the complete command and reply text.</summary>
    Raw,
}

/// <summary>
/// The generic topology schema for one <see cref="TcpTap"/>: where it listens, where it forwards, which two
/// participants the hop joins, how it attributes captures and how much it records. Topology <em>values</em>
/// (ports, service names) stay in the host's configuration; this is the shape they fill in.
/// </summary>
/// <remarks>
/// Mirrors <c>ProxyTapOptions</c> option-for-option where the two taps mean the same thing, so a host that
/// already configures HTTP taps configures DB taps the same way. The protocol-specific knobs live on
/// <see cref="RedisTapOptions"/> and <see cref="MongoTapOptions"/>.
/// </remarks>
public class TcpTapOptions
{
    /// <summary>Port the tap listens on (the address the service is pointed at). Required; <c>0</c> binds a free ephemeral port (see <see cref="TcpTap.BoundPort"/>).</summary>
    public int ListenPort { get; set; }

    /// <summary>Host name or address the listener binds to. Default <c>localhost</c>.</summary>
    public string ListenHost { get; set; } = "localhost";

    /// <summary>Host name or address of the real server the tap forwards to. Default <c>localhost</c>.</summary>
    public string ForwardHost { get; set; } = "localhost";

    /// <summary>Port of the real server the tap forwards to. Required.</summary>
    public int ForwardPort { get; set; }

    /// <summary>The calling participant as it should appear in diagrams (the service that dials the tap). Required.</summary>
    public string CallerName { get; set; } = "";

    /// <summary>The receiving participant as it should appear in diagrams (the database). Required.</summary>
    public string ServiceName { get; set; } = "";

    /// <summary>Optional <see cref="DependencyCategories"/> value for the receiving service (shape/colour). The Redis/Mongo taps set it for you.</summary>
    public string? DependencyCategory { get; set; }

    /// <summary>Optional <see cref="DependencyCategories"/> value for the caller.</summary>
    public string? CallerDependencyCategory { get; set; }

    /// <summary>Optional human-readable name of this tap for diagnostics (defaults to <c>caller→service</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Optional diagnostic log callback (listener start/stop, connection errors, decoder give-ups, drop counts).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Where captured entries go. Default: the in-process <see cref="RequestResponseLogger"/> store. Use <c>CompositeRequestResponseSink</c> to add an NDJSON file.</summary>
    public IRequestResponseSink Sink { get; set; } = RequestResponseLoggerSink.Instance;

    /// <summary>Phase stamped on captured entries. Default <see cref="TestPhase.Unknown"/>.</summary>
    public TestPhase Phase { get; set; } = TestPhase.Unknown;

    /// <summary>
    /// Test name every capture is attributed to when <see cref="IdentityResolver"/> yields nothing. A wire tap
    /// has no per-request identity — attribution normally happens at ingest, by test window
    /// (<c>IngestRequest.AttributeByTestWindow</c>). Default <c>Unknown</c>.
    /// </summary>
    public string FallbackTestName { get; set; } = TestIdentityScope.UnknownTestName;

    /// <summary>
    /// Test id every capture is attributed to when <see cref="IdentityResolver"/> yields nothing. Give the run
    /// its own bucket id so window attribution (or <c>--fold-unknown</c>) can re-home the records.
    /// Default <c>unknown</c>.
    /// </summary>
    public string FallbackTestId { get; set; } = TestIdentityScope.UnknownTestId;

    /// <summary>
    /// Optional identity source consulted per captured interaction — a host that also runs HTTP taps can plug
    /// an in-flight registry here ("the identity of the request currently in flight on this service"). Return
    /// null to fall back to <see cref="FallbackTestName"/>/<see cref="FallbackTestId"/>. Never throws out of
    /// the tap: an exception is caught, counted and treated as null.
    /// </summary>
    public Func<(string Name, string Id)?>? IdentityResolver { get; set; }

    /// <summary>Emit a client span per decoded interaction on <see cref="TcpTap.ActivitySource"/> (<c>Kronikol.TcpTap</c>). Free when no listener is attached. Default true.</summary>
    public bool EmitActivities { get; set; } = true;

    /// <summary>How much detail to record. Default <see cref="TapVerbosity.Detailed"/>.</summary>
    public TapVerbosity Verbosity { get; set; } = TapVerbosity.Detailed;

    /// <summary>Recorded content longer than this is truncated (the forwarded bytes are never touched). Null = unlimited. Default 65536.</summary>
    public int? BodyCapBytes { get; set; } = 65536;

    /// <summary>Whether reply payloads are recorded at all. When false the response arrow still renders (with its status and timing) but carries no note. Default true.</summary>
    public bool CaptureReplies { get; set; } = true;

    /// <summary>Backlog passed to the listening socket. Default 128.</summary>
    public int AcceptBacklog { get; set; } = 128;

    /// <summary>Connect timeout to the forward target. Default 5 s.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long <see cref="TcpTap.DisposeAsync"/> waits for live connections to finish so their captures land. Default 2 s.</summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Segments buffered per connection between the byte pumps and the decoder. Full = the segment is dropped
    /// and counted (<see cref="TcpTap.SegmentsDropped"/>) — capture degrades, forwarding never blocks (D3).
    /// Default 1024.
    /// </summary>
    public int ChannelCapacity { get; set; } = 1024;

    /// <summary>
    /// Bytes a decoder may hold while waiting for the rest of a message, per direction. Default 8 MiB. A Redis
    /// bulk payload never counts against it (payloads over <see cref="RedisTapOptions.MaxBulkBytes"/> are streamed
    /// past, not buffered), so crossing it means the stream is desynchronised: the decoder is reset and re-arms at
    /// the next command boundary when <see cref="ResyncAfterOverflow"/> is on, otherwise decoding stops for that
    /// connection. Forwarding continues either way.
    /// </summary>
    public int MaxBufferedBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// When a decoder loses its place (the <see cref="MaxBufferedBytes"/> cap, a pending-queue overflow, a protocol
    /// error on a connection that was decoding fine), reset it and resume at the next command boundary instead of
    /// disabling decoding for the rest of the connection. The first interaction recorded after a reset is stamped
    /// <c>[resynchronised — pairing uncertain]</c> (and <c>x-kronikol-capture: resynced</c>) because a reply still in
    /// flight may pair with the wrong command until the connection goes idle. Off = disable decoding, counted and
    /// reported. Default true.
    /// </summary>
    public bool ResyncAfterOverflow { get; set; } = true;

    /// <summary>
    /// Called as capture-loss events happen — an oversize payload streamed past, a decoder reset or give-up, dropped
    /// segments, a connection closed mid-message — so a host can flag the tap before the report is generated. The
    /// same facts are available afterwards as counters and through <see cref="TcpTap.Diagnostics"/>. Invoked on the
    /// decoder task (never on a byte pump, except for dropped segments, which are reported at most once per connection
    /// per minute); an exception it throws is caught and logged. Default null.
    /// </summary>
    public Action<CaptureDegradation>? OnCaptureDegraded { get; set; }

    /// <summary>
    /// Heuristic stall detector for <see cref="TcpTap.Diagnostics"/>: when at least this many bytes have been forwarded
    /// since the last recorded interaction, the diagnostics include a "decoding may have stalled" entry — bytes are
    /// flowing but nothing is being recorded (which may also mean the traffic is all excluded chatter). Null = off.
    /// Default 1 MiB.
    /// </summary>
    public long? DecodingStallBytes { get; set; } = 1024 * 1024;

    /// <summary>Size of each socket read. Default 32 KiB.</summary>
    public int ReadBufferBytes { get; set; } = 32 * 1024;

    /// <summary>Optional hook applied to every key / identifier before it is recorded (Redis keys, channel names).</summary>
    public Func<string, string>? KeyRedaction { get; set; }

    /// <summary>Optional hook applied to every recorded value or payload text (Redis values and reply payloads).</summary>
    public Func<string, string>? ValueRedaction { get; set; }

    /// <summary>Builds the per-connection protocol decoder. Set for you by <see cref="RedisTap"/> / <see cref="MongoTap"/>; set it yourself to tee any other protocol.</summary>
    public Func<TcpTapConnectionContext, IProtocolDecoder>? DecoderFactory { get; set; }

    /// <summary>The name used in diagnostics.</summary>
    public string DisplayName => Name ?? $"{CallerName}→{ServiceName}";

    /// <summary>Throws when a required option is missing or out of range.</summary>
    public virtual void Validate()
    {
        if (ListenPort is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(ListenPort), "ListenPort must be a valid TCP port (0 = ephemeral).");
        if (ForwardPort is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(ForwardPort), "ForwardPort must be a valid TCP port.");
        if (string.IsNullOrWhiteSpace(ListenHost))
            throw new ArgumentException("ListenHost is required.", nameof(ListenHost));
        if (string.IsNullOrWhiteSpace(ForwardHost))
            throw new ArgumentException("ForwardHost is required.", nameof(ForwardHost));
        if (string.IsNullOrWhiteSpace(CallerName))
            throw new ArgumentException("CallerName is required.", nameof(CallerName));
        if (string.IsNullOrWhiteSpace(ServiceName))
            throw new ArgumentException("ServiceName is required.", nameof(ServiceName));
        if (ChannelCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(ChannelCapacity), "ChannelCapacity must be positive.");
        if (ReadBufferBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(ReadBufferBytes), "ReadBufferBytes must be positive.");
        if (MaxBufferedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxBufferedBytes), "MaxBufferedBytes must be positive.");
        if (DecodingStallBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(DecodingStallBytes), "DecodingStallBytes must be positive, or null to disable the detector.");
        if (DecoderFactory is null)
            throw new ArgumentException("DecoderFactory is required (use RedisTap/MongoTap, or set it to tee another protocol).", nameof(DecoderFactory));
    }

    /// <summary>Copies every property of this instance onto <paramref name="target"/>. Overridden by the protocol option types.</summary>
    public virtual void CopyTo(TcpTapOptions target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.ListenPort = ListenPort;
        target.ListenHost = ListenHost;
        target.ForwardHost = ForwardHost;
        target.ForwardPort = ForwardPort;
        target.CallerName = CallerName;
        target.ServiceName = ServiceName;
        target.DependencyCategory = DependencyCategory;
        target.CallerDependencyCategory = CallerDependencyCategory;
        target.Name = Name;
        target.Log = Log;
        target.Sink = Sink;
        target.Phase = Phase;
        target.FallbackTestName = FallbackTestName;
        target.FallbackTestId = FallbackTestId;
        target.IdentityResolver = IdentityResolver;
        target.EmitActivities = EmitActivities;
        target.Verbosity = Verbosity;
        target.BodyCapBytes = BodyCapBytes;
        target.CaptureReplies = CaptureReplies;
        target.AcceptBacklog = AcceptBacklog;
        target.ConnectTimeout = ConnectTimeout;
        target.DrainTimeout = DrainTimeout;
        target.ChannelCapacity = ChannelCapacity;
        target.MaxBufferedBytes = MaxBufferedBytes;
        target.ResyncAfterOverflow = ResyncAfterOverflow;
        target.OnCaptureDegraded = OnCaptureDegraded;
        target.DecodingStallBytes = DecodingStallBytes;
        target.ReadBufferBytes = ReadBufferBytes;
        target.KeyRedaction = KeyRedaction;
        target.ValueRedaction = ValueRedaction;
        target.DecoderFactory = DecoderFactory;
    }
}
