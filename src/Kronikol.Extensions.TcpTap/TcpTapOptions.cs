using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.TcpTap;

/// <summary>How much detail a tap records for each decoded command.</summary>
public enum TapVerbosity
{
    /// <summary>
    /// Minimal labels only: keys, filters and values are omitted, and reply payloads too, except where a protocol's
    /// options keep them (a MongoDB reply note stays while <see cref="MongoTapOptions.LogResponseContent"/> is on).
    /// </summary>
    Summarised,

    /// <summary>
    /// Classified label plus the target resource (key / collection) and what the command carried: a key's value, or a
    /// MongoDB command without its name and the driver's fields. The default.
    /// </summary>
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

    /// <summary>
    /// Host name or address the listener binds to. Default <c>localhost</c>, which listens on both loopbacks, 127.0.0.1 and
    /// ::1, on the same port: a client that dials <c>localhost</c> may try either first, and on Windows a refused address
    /// costs about 2 s. If something else holds the port on ::1, the tap listens on 127.0.0.1 alone and says so in its log
    /// and in <see cref="TcpTap.Diagnostics"/>. <c>*</c> or <c>+</c> listens on every interface, IPv4 and IPv6, with one
    /// dual-mode socket (every IPv4 interface where there is no IPv6). An address listens on that address alone.
    /// </summary>
    public string ListenHost { get; set; } = "localhost";

    /// <summary>
    /// Host name or address of the real server the tap forwards to. Default <c>localhost</c>. A name's addresses are tried
    /// IPv4 first, so a server listening on 127.0.0.1 alone is reached at once; one listening on ::1 alone is reached after
    /// the IPv4 address refuses, which on Windows takes about 2 s, so under a <see cref="ConnectTimeout"/> shorter than that
    /// it is not reached at all. An address is connected to as it is, with no lookup.
    /// </summary>
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

    /// <summary>
    /// Optional diagnostic log callback: the listener starting and stopping, a second listener it could not open, accepts
    /// that failed, connects to the real server that failed or ran past <see cref="ConnectTimeout"/>, connection errors,
    /// decoder resets and give-ups, drop counts.
    /// Whatever it throws is ignored, so one bound to a test's output that has ended changes nothing the tap does.
    /// </summary>
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

    /// <summary>Recorded content longer than this is truncated (the forwarded bytes are never touched). Null = unlimited. Default 65536. A negative value is refused at start.</summary>
    public int? BodyCapBytes { get; set; } = 65536;

    /// <summary>Whether reply payloads are recorded at all. When false the response arrow still renders (with its status and timing) but carries no note, and a failed reply's own message (a MongoDB errmsg) is not kept as the call's error either. Default true.</summary>
    public bool CaptureReplies { get; set; } = true;

    /// <summary>Backlog passed to the listening socket. Default 128.</summary>
    public int AcceptBacklog { get; set; } = 128;

    /// <summary>
    /// How long the tap waits to connect to <see cref="ForwardHost"/>:<see cref="ForwardPort"/> for each connection it
    /// accepts, the name's resolution included. A connect that fails or runs past it closes the caller's connection without
    /// forwarding anything, and is counted (<see cref="TcpTap.UpstreamConnectFailures"/>), logged, raised
    /// (<see cref="CaptureDegradationKind.UpstreamConnectFailed"/>) and reported by <see cref="TcpTap.Diagnostics"/>. On
    /// Windows a refused connect takes about 2 s per address, because Windows sends the SYN again after a reset, so under a
    /// shorter timeout a refusal arrives as a timeout. Default 5 s. <see cref="Timeout.InfiniteTimeSpan"/>, or about 24.8 days
    /// (<see cref="int.MaxValue"/> milliseconds) or more, means no deadline; zero or less is refused at start.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The longest deadline a connect takes, as <c>ProxyTap</c>'s: <see cref="int.MaxValue"/> milliseconds.</summary>
    internal static readonly TimeSpan LongestConnectDeadline = TimeSpan.FromMilliseconds(int.MaxValue);

    // The connect's deadline, or none: InfiniteTimeSpan, or a value the deadline's timer could not be set to, which used to
    // throw before every connect.
    internal TimeSpan? ConnectLimit =>
        ConnectTimeout == Timeout.InfiniteTimeSpan || ConnectTimeout >= LongestConnectDeadline ? null : ConnectTimeout;

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
    /// segments, a connection closed mid-message or reaped, a connection the tap could not forward, an accept that
    /// failed — so a host can flag the tap before the report is generated. The same facts are available afterwards as
    /// counters and through <see cref="TcpTap.Diagnostics"/>. Invoked on the task where the event happens: the decoder's
    /// for decoding events (the connection's own when <see cref="DecoderFactory"/> fails), a byte pump's for dropped segments (reported at most once per connection per minute), a
    /// thread-pool thread for a reaped connection, the connection's own for a failed connect and the accept loop's for a
    /// failed accept, so keep it quick. An exception it throws is caught and logged. Default null.
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

    /// <summary>
    /// Close a connection on which a decoded command has sat <em>unanswered</em> for this long. With
    /// <see cref="HalfClosedTimeout"/> and <see cref="StalledWriteTimeout"/> it is one of three limits that close a pair,
    /// and it exists for one observed failure mode: a database
    /// client whose internal pipeline wedges (StackExchange.Redis 2.6.x seen live 2026-08-24: a
    /// machine-wide stall jammed its pipe writer, and from then on every command sat queued forever — no
    /// completion, no timeout, no reconnect — taking every request in the service down with it). Such a
    /// client recovers from a <em>closed socket</em> — it reconnects and fails its backlog fast — where it
    /// never recovers on its own. An unanswered command is the wedge's signature and only its signature:
    /// Redis and Mongo answer in milliseconds, so no healthy connection ever trips a multi-second
    /// threshold — and a genuinely idle connection (a pool member between checkouts) has nothing
    /// unanswered and is never touched. (A first version reaped on <em>silence</em> instead, and killed
    /// exactly those healthy idle pool connections.) Requires a decoder that reports
    /// <see cref="IProtocolDecoder.OldestUnansweredSince"/> (the Redis and Mongo decoders do); when
    /// decoding has been disabled for a connection the tap is blind there and never guesses. Reaps are
    /// counted (<see cref="TcpTap.StuckConnectionsReaped"/>), logged, and reported through
    /// <see cref="OnCaptureDegraded"/>. Null = off (the default).
    /// </summary>
    public TimeSpan? ReapStuckConnectionsAfter { get; set; }

    /// <summary>
    /// Close a pair once one side has finished sending (its FIN, which the tap passes on) and the other has moved no bytes
    /// for this long, counted from the FIN or from its last byte, whichever is later. A stall limit, not a total: a server
    /// that keeps streaming a reply to a client that has half-closed is never cut, and a pair where neither side has
    /// finished is never touched however long it sits idle. Redis and MongoDB clients do not half-close, so the default
    /// cuts nothing they do. Closes are counted (<see cref="TcpTap.HalfClosedPairsClosed"/>) and logged. No limit fires
    /// while a debugger is attached to the tap's process; a client or server paused in a debugger in a process of its own
    /// is closed after the limit, so set it to null for such a session. Default 100 s; null or
    /// <see cref="Timeout.InfiniteTimeSpan"/> for none, and zero or another negative value is refused at start.
    /// </summary>
    public TimeSpan? HalfClosedTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Close a pair when the tap's write to one side has not completed for this long: that side has stopped reading (a
    /// client that stops reading a large reply, or a server that stops reading its client's commands). A write carries up
    /// to <see cref="ReadBufferBytes"/>, so a reader has to take that much per limit; a pair with nothing to write is never
    /// touched. Closes are counted (<see cref="TcpTap.StalledWritesClosed"/>) and logged. No limit fires while a debugger is
    /// attached to the tap's process; a client or server paused in a debugger in a process of its own is closed after the
    /// limit, so set it to null for such a session. Default 100 s; null or <see cref="Timeout.InfiniteTimeSpan"/> for none,
    /// and zero or another negative value is refused at start.
    /// </summary>
    public TimeSpan? StalledWriteTimeout { get; set; } = TimeSpan.FromSeconds(100);

    // The limits as the watch takes them: none for null or infinite.
    internal TimeSpan? HalfClosedLimit => HalfClosedTimeout == Timeout.InfiniteTimeSpan ? null : HalfClosedTimeout;

    internal TimeSpan? StalledWriteLimit => StalledWriteTimeout == Timeout.InfiniteTimeSpan ? null : StalledWriteTimeout;

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
        // A negative deadline throws where it is set, before every connect, so no connection would ever be forwarded.
        if (ConnectTimeout <= TimeSpan.Zero && ConnectTimeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout), "ConnectTimeout must be positive, or Timeout.InfiniteTimeSpan for none.");
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
        // A range taken from a negative cap throws: Redis's parser at the start of every connection, MongoDB's decoder at
        // its first record.
        if (BodyCapBytes is < 0)
            throw new ArgumentOutOfRangeException(nameof(BodyCapBytes), "BodyCapBytes must be zero or more, or null for no cap.");
        if (DecodingStallBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(DecodingStallBytes), "DecodingStallBytes must be positive, or null to disable the detector.");
        if (ReapStuckConnectionsAfter is { } reap && reap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ReapStuckConnectionsAfter), "ReapStuckConnectionsAfter must be positive, or null to disable the reaper.");
        if (HalfClosedTimeout is { } halfClosed && halfClosed <= TimeSpan.Zero && halfClosed != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(HalfClosedTimeout), "HalfClosedTimeout must be positive, or null or Timeout.InfiniteTimeSpan for no limit.");
        if (StalledWriteTimeout is { } stalledWrite && stalledWrite <= TimeSpan.Zero && stalledWrite != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(StalledWriteTimeout), "StalledWriteTimeout must be positive, or null or Timeout.InfiniteTimeSpan for no limit.");
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
        target.ReapStuckConnectionsAfter = ReapStuckConnectionsAfter;
        target.HalfClosedTimeout = HalfClosedTimeout;
        target.StalledWriteTimeout = StalledWriteTimeout;
        target.KeyRedaction = KeyRedaction;
        target.ValueRedaction = ValueRedaction;
        target.DecoderFactory = DecoderFactory;
    }
}
