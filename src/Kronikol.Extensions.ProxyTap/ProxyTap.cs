using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kronikol.Constants;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Extensions.ProxyTap;

/// <summary>
/// A transparent HTTP tee for Kronikol's <em>proxy-tap topology</em>: listens on a port, forwards every
/// request byte-for-byte to a real service, and records a copy of the exchange as a Kronikol
/// request/response pair attributed to the running test. Nothing inside the services needs to change —
/// the browser/test fixture stamps the <c>test-tracking-*</c> headers (or a W3C <c>traceparent</c>), the
/// identity rides the real request through the services, and the tap on each hop is the sink. For a caller that
/// can do neither (a mobile app), the host names the test through <see cref="ProxyTapOptions.IdentityResolver"/>,
/// and <see cref="ProxyTapOptions.TraceIdFromIdentity"/> forwards the test id as the trace id.
/// </summary>
/// <remarks>
/// <para>Modelled on <see cref="TestTrackingMessageHandler"/> (which fields it fills) and
/// <see cref="TestTrackingContextMiddleware"/> (how inbound identity is read); it re-stamps the four
/// correlation headers on the forwarded request so the chain survives a hop that drops them, and it is
/// the capture-time security boundary — secret headers are redacted before anything reaches a sink.</para>
/// <para>The forwarded bytes are the original bytes; capture decodes (gzip/deflate/br) and caps a
/// <em>copy</em>, after the response has been written back to the caller, so the capture cost never sits
/// on the request path. Built on <see cref="HttpListener"/>, so it needs no ASP.NET Core host and binds
/// <c>localhost</c> without URL ACLs.</para>
/// </remarks>
public sealed class ProxyTap : IAsyncDisposable
{
    /// <summary>The <see cref="System.Diagnostics.ActivitySource"/> name taps emit spans on (see <see cref="ProxyTapOptions.EmitActivities"/>).</summary>
    public const string ActivitySourceName = "Kronikol.ProxyTap";

    /// <summary>The source the server/client spans are started on. Attach an OpenTelemetry listener to export them.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive", "Proxy-Connection",
        "Expect", "Upgrade", "TE", "Trailer",
    };

    // How long the client's own timeout runs behind the forward's deadline, as a backstop.
    private static readonly TimeSpan ClientTimeoutBackstop = TimeSpan.FromSeconds(10);

    private readonly ProxyTapOptions _options;
    private readonly HttpListener _listener = new();
    // Every answer whose handler has not finished, so DisposeAsync can settle each by its phase.
    private readonly ConcurrentDictionary<CallerAnswer, byte> _answers = new();
    // Cancelled once the listener is closed, which ends the accept loop's wait for a request.
    private readonly CancellationTokenSource _listenerClosed = new();
    private HttpClient? _upstream;
    private CancellationTokenSource? _cts;
    // The tap's lifetime, taken once while the tap runs: a handler that outlives DisposeAsync reads it after _cts is disposed.
    private CancellationToken _lifetime;
    private Task? _loop;
    private int _inflight;
    private int _disposed;
    private long _handled;
    private long _captured;
    private long _forwardFailures;
    private long _failuresRecorded;
    private long _failuresWithoutIdentity;
    private long _uncaptured;
    private long _captureFaults;
    private string? _firstCaptureFault;
    private long _traceIdNotApplied;
    private StallWatch? _watch;
    private long _bodyTimeouts;
    private long _answerTimeouts;

    /// <summary>Creates a tap for the given options (validated on <see cref="StartAsync"/>).</summary>
    public ProxyTap(ProxyTapOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The options this tap runs with.</summary>
    public ProxyTapOptions Options => _options;

    /// <summary>The URI callers should dial.</summary>
    public Uri ListenUri => new($"http://{_options.ListenHost}:{_options.ListenPort}/");

    /// <summary>Exchanges answered from the upstream so far. A forward that failed is not one.</summary>
    public long RequestsHandled => Interlocked.Read(ref _handled);

    /// <summary>Of <see cref="RequestsHandled"/>, the exchanges captured (attributed to a test and recorded). A failed forward that was recorded is not added here.</summary>
    public long RequestsCaptured => Interlocked.Read(ref _captured);

    /// <summary>
    /// Requests the tap could not forward (the upstream was unreachable, timed out or the exchange threw) and answered
    /// <c>502 Bad Gateway</c> instead. One that names a test, or that <see cref="ProxyTapOptions.CaptureUnattributedRequests"/>
    /// takes, is recorded as a failed call. A request whose caller hangs up before it has arrived whole is not counted: it
    /// was never forwarded, and its caller is not there to answer. Nor is a forward the tap's own disposal cut short, nor
    /// a fault in capture, which <see cref="Diagnostics"/> reports on a line of its own.
    /// </summary>
    public long ForwardFailures => Interlocked.Read(ref _forwardFailures);

    /// <summary>
    /// Callers cut by <see cref="ProxyTapOptions.CallerStallTimeout"/>: ones that stalled while sending a request body,
    /// which were not forwarded, and ones that stalled while reading an answer, whose exchanges were recorded when captured.
    /// <see cref="Diagnostics"/> says how many of each.
    /// </summary>
    public long CallerTimeouts => Interlocked.Read(ref _bodyTimeouts) + Interlocked.Read(ref _answerTimeouts);

    /// <summary>Whether the listener is accepting connections.</summary>
    public bool IsListening => _listener.IsListening;

    // Exchanges whose handler has not finished (Kronikol.Tests.ProxyTap): a fact hangs up only once the tap holds the request.
    internal int InFlight => Volatile.Read(ref _inflight);

    // Whether the tap is stopping: DisposeAsync has begun, or the token StartAsync was given is cancelled. A request that
    // arrives then is answered 503 and never forwarded.
    internal bool Stopping => Volatile.Read(ref _disposed) == 1 || _lifetime.IsCancellationRequested;

    // Wraps the caller's request body before it is read (Kronikol.Tests.ProxyTap): a fact throws from the read whatever
    // exception it names, on any platform. Null in production.
    internal Func<Stream, Stream>? WrapRequestBody { get; set; }

    // The phases a caller is watched in, as the watch and the tap's messages name them.
    internal const string BodyPhase = "request body";
    internal const string AnswerPhase = "answer";

    // The size of each read of a request body and each write of an answer. A body read on Windows, and a chunked one on
    // Linux, returns only once its buffer is full or the body ends, so a read is progress only if its buffer is small.
    internal const int PieceBytes = 16 * 1024;

    // The tap's clock (Kronikol.Tests.ProxyTap): a fact moves a fake one past the stall limit. Set before StartAsync.
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    // Whether a debugger is attached (Kronikol.Tests.ProxyTap): every fact says no, so the suite passes under one too.
    internal Func<bool> DebuggerAttached { get; set; } = static () => Debugger.IsAttached;

    // The watch over callers' transfers, or null when there is no limit (Kronikol.Tests.ProxyTap).
    internal StallWatch? Watch => _watch;

    /// <summary>
    /// Capture health as report diagnostics: one <see cref="DiagnosticKind.CaptureDegraded"/> entry per
    /// non-zero problem counter, worded for a report reader (<c>web→graphql: 3 request(s) could not be
    /// forwarded …</c>). Empty when the tap is healthy. A host hands the list to
    /// <see cref="Kronikol.Ingestion.IngestRequest.HostDiagnostics"/> so a tap that dropped arrows is a
    /// line in the report, not only in a log.
    /// </summary>
    public IReadOnlyList<DiagnosticEntry> Diagnostics()
    {
        var name = _options.DisplayName;
        var entries = new List<DiagnosticEntry>();

        var forwardFailures = ForwardFailures;
        if (forwardFailures > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                string.Create(CultureInfo.InvariantCulture, $"{name}: {forwardFailures:N0} request(s) could not be forwarded and were answered 502 Bad Gateway — the upstream was unreachable, timed out or the exchange threw; {WhichWereRecorded(forwardFailures)}")));

        var bodyTimeouts = Interlocked.Read(ref _bodyTimeouts);
        var answerTimeouts = Interlocked.Read(ref _answerTimeouts);
        if (bodyTimeouts + answerTimeouts > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                string.Create(CultureInfo.InvariantCulture, $"{name}: {bodyTimeouts + answerTimeouts:N0} caller(s) stalled for CallerStallTimeout ({_options.CallerStallTimeout}) and were cut: {bodyTimeouts:N0} sending a request body (not forwarded), {answerTimeouts:N0} reading an answer (recorded when captured)")));

        // Counted where it happens, not worked out as handled - captured: a record that threw is a capture fault, not a
        // request without a test identity.
        var handled = RequestsHandled;
        var uncaptured = Interlocked.Read(ref _uncaptured);
        if (uncaptured > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                string.Create(CultureInfo.InvariantCulture, $"{name}: {uncaptured:N0} of {handled:N0} forwarded request(s) carried no test identity and were not captured (no test-tracking headers or traceparent reached this hop; CaptureUnattributedRequests is off)")));

        var notApplied = Interlocked.Read(ref _traceIdNotApplied);
        if (notApplied > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.OptionNotApplied,
                string.Create(CultureInfo.InvariantCulture, $"{name}: TraceIdFromIdentity did not apply to {notApplied:N0} request(s), because their test id is not a W3C trace id (32 lower-case hex digits, not all zero); they were forwarded under a random trace, so the services behind this tap cannot be matched to the test")));

        var captureFaults = CaptureFaults;
        if (captureFaults > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                string.Create(CultureInfo.InvariantCulture, $"{name}: {captureFaults:N0} capture fault(s): the IdentityResolver, the sink, the body decoder or an ActivityListener threw, so a call may be missing, unattributed or untraced (the first: {Volatile.Read(ref _firstCaptureFault)}); what the callers got was not changed")));

        return entries;
    }

    /// <summary>The forward-failure line's last clause: which of the failed requests are recorded as failed calls.</summary>
    private string WhichWereRecorded(long total)
    {
        var recorded = Interlocked.Read(ref _failuresRecorded);
        var withoutIdentity = Interlocked.Read(ref _failuresWithoutIdentity);
        var other = total - recorded - withoutIdentity;
        if (recorded == total)
            return "each is recorded as a failed call";
        if (withoutIdentity == total)
            return "they carried no test identity and were not recorded";

        var parts = new List<string>();
        if (recorded > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{recorded:N0} {(recorded == 1 ? "is" : "are")} recorded as {(recorded == 1 ? "a failed call" : "failed calls")}"));
        if (withoutIdentity > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{withoutIdentity:N0} carried no test identity and {(withoutIdentity == 1 ? "was" : "were")} not recorded"));
        if (other > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{other:N0} {(other == 1 ? "was" : "were")} not recorded"));
        return string.Join(", ", parts);
    }

    // Faults in capture (the IdentityResolver, the sink, the body decoder, an ActivityListener, or anything the tap did
    // not expect once the caller had its answer): logged, counted, and never what the caller gets.
    internal long CaptureFaults => Interlocked.Read(ref _captureFaults);

    private void CaptureFault(string what, Exception exception)
    {
        if (Interlocked.Increment(ref _captureFaults) == 1)
            Volatile.Write(ref _firstCaptureFault, $"{exception.GetType().Name}, {exception.Message}");
        TryLog($"[{_options.DisplayName}] proxy-tap: {what} threw {exception.GetType().Name}: {exception.Message}");
    }

    /// <summary>
    /// Writes a line through <see cref="ProxyTapOptions.Log"/>. The callback is the host's code, and nothing it throws
    /// changes what the tap does: one that writes to a test's output after the test has ended throws.
    /// </summary>
    private void TryLog(string message)
    {
        try
        {
            _options.Log?.Invoke(message);
        }
        catch
        {
            // The host's callback failed; the tap carries on.
        }
    }

    /// <summary>Starts listening. Throws if the options are invalid or the port cannot be bound.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();

        var forwardDeadline = ProxyTapOptions.Deadline(_options.ForwardTimeout);
        _upstream = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            // None: the wire bytes must reach the caller exactly as the service sent them; capture decodes its own copy.
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = ProxyTapOptions.Deadline(_options.ConnectTimeout) ?? Timeout.InfiniteTimeSpan,
            UseCookies = false,
            // None: the traceparent a forward carries is the tap's to choose, and its client span is the forward's span.
            // With a propagator the handler runs whatever HttpClient instrumentation the process has, and OpenTelemetry's
            // puts a span of its own in place of the header: a new trace when nothing listens to the tap, the sampled
            // flag cleared under Kronikol's internal-flow listener.
            ActivityHeadersPropagator = null,
        })
        {
            BaseAddress = _options.ForwardBaseUri,
            // A backstop behind the forward's own deadline (HandleAsync), and none when the forward has none: an infinite
            // ForwardTimeout (-1 ms) plus 10 s came to 9.999 s, which cut every forward there.
            Timeout = forwardDeadline is { } forward && forward < ProxyTapOptions.LongestDeadline - ClientTimeoutBackstop
                ? forward + ClientTimeoutBackstop
                : Timeout.InfiniteTimeSpan,
        };

        _listener.Prefixes.Add(ListenUri.ToString());
        _listener.Start();

        if (_options.StallLimit is not null)
            _watch = new StallWatch(Clock, DebuggerAttached, line => TryLog($"[{_options.DisplayName}] proxy-tap: {line}"));

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _lifetime = _cts.Token;
        // The accept loop, and every request it hands on, runs in the context it is started in. Started under an activity
        // (a fixture's, a host's), every call that brought no trace would be forwarded in that activity's trace, with its
        // baggage, whatever test made it. Requests start with no current activity.
        var current = Activity.Current;
        Activity.Current = null;
        try
        {
            _loop = Task.Run(AcceptLoopAsync, CancellationToken.None);
        }
        finally
        {
            Activity.Current = current;
        }
        TryLog($"[{_options.DisplayName}] proxy-tap listening on {ListenUri} → {_options.ForwardBaseUri}");
        return Task.CompletedTask;
    }

    // Runs until the listener closes, not until the tap's token is cancelled: while the tap stops, a request that arrives is
    // answered 503 (GuardedHandleAsync) rather than left to the listener's close, which answers 200 OK on Linux and macOS.
    private async Task AcceptLoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(_listenerClosed.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException or InvalidOperationException
                || Volatile.Read(ref _disposed) == 1)
            {
                // The listener closed under the wait. One that closes as the wait begins throws what the platform throws
                // then: on Windows an ArgumentException ("The handle is invalid"), which failed up to one disposal in seven
                // of a tap disposed straight after it started.
                return;
            }

            _ = Task.Run(() => GuardedHandleAsync(context), CancellationToken.None);
        }
    }

    private async Task GuardedHandleAsync(HttpListenerContext context)
    {
        Interlocked.Increment(ref _inflight);
        var answer = new CallerAnswer(context.Response);
        _answers.TryAdd(answer, 0);
        try
        {
            if (Stopping)
                await RefuseWhileStoppingAsync(answer).ConfigureAwait(false);
            else
                await HandleAsync(context, answer).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Only a fault the tap did not expect lands here: the caller's request and answer, the forward, the record, the
            // identity resolver and the span listeners all have catches of their own. It is counted by what the caller
            // got: a 502, when it had no answer yet, is a forward failure; after its answer, a capture fault. An answer the
            // tap's stopping ended, or the stall limit cut, is neither: what its I/O throws from then on is the stop's or
            // the limit's, and the cut was logged when it was made.
            if (answer.IsEnded)
            {
                TryLog($"[{_options.DisplayName}] proxy-tap stopping: {ex.Message}");
            }
            else if (!answer.IsTimedOut)
            {
                TryLog($"[{_options.DisplayName}] proxy-tap error: {ex.Message}");
                if (answer.IsClosed)
                {
                    CaptureFault("the tap", ex);
                }
                else
                {
                    Interlocked.Increment(ref _forwardFailures);
                    await TryRespondAsync(answer, 502, BadGatewayBody(ex.Message)).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            // Every exit, whatever it did: a response the tap did not close is aborted, or the managed listener (Linux,
            // macOS) holds its connection until the tap is disposed.
            answer.EndUnclosed();
            _answers.TryRemove(answer, out _);
            Interlocked.Decrement(ref _inflight);
        }
    }

    /// <summary>
    /// Answers a request that arrived while the tap stops: <c>503</c> and <c>Connection: close</c>, never forwarded, so
    /// the caller is told the truth rather than left to the listener's close.
    /// </summary>
    private Task RefuseWhileStoppingAsync(CallerAnswer answer) => TryRespondAsync(answer, 503, ErrorBody(503, "the tap is stopping"));

    /// <summary>
    /// Reads a request body in pieces of at most <see cref="PieceBytes"/>, recording progress after each: a larger read
    /// would show progress only once its whole buffer had arrived.
    /// </summary>
    private static async Task<byte[]> ReadBodyAsync(Stream body, StallWatch.Entry? reading)
    {
        using var buffer = new MemoryStream();
        var piece = new byte[PieceBytes];
        int read;
        while ((read = await body.ReadAsync(piece.AsMemory()).ConfigureAwait(false)) > 0)
        {
            buffer.Write(piece, 0, read);
            reading?.Progress();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Cuts a caller that stalled past <see cref="ProxyTapOptions.CallerStallTimeout"/>, unless its answer was closed or
    /// ended first: <c>408</c> and a close while it sends its body (on Windows a reset, since http.sys writes nothing on an
    /// abort), a close while it reads its answer. Called by the limit's action and by the handler that lost the race to it,
    /// and only one of them cuts, counts and logs.
    /// </summary>
    private void CutStalled(CallerAnswer answer, string phase)
    {
        // Counted once the cut has won and before the caller is let go: a host that reads CallerTimeouts or Diagnostics() as
        // soon as the caller is gone sees it.
        var body = phase == BodyPhase;
        if (!answer.TimeOut(body ? 408 : null, () => Interlocked.Increment(ref body ? ref _bodyTimeouts : ref _answerTimeouts)))
            return;

        if (body)
        {
            TryLog($"[{_options.DisplayName}] proxy-tap caller stalled {_options.CallerStallTimeout} sending its request body; not forwarded, {(OperatingSystem.IsWindows() ? "connection reset" : "answered 408 and closed")}");
        }
        else
        {
            TryLog($"[{_options.DisplayName}] proxy-tap caller stalled {_options.CallerStallTimeout} reading its answer; closed");
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CallerAnswer answer)
    {
        var request = context.Request;
        var pathAndQuery = request.Url?.PathAndQuery ?? "/";
        // The base's scheme and authority with the request's own path, never a relative resolution against the base: a
        // path starting with "//" is a network-path reference there and names a host of its own (the listener also
        // reads "/\" as "//"). The path always starts with "/", so this is the configured upstream for every request.
        var forwardUri = new Uri(_options.ForwardBaseUri!.GetLeftPart(UriPartial.Authority) + pathAndQuery);
        var requestHeaders = ReadHeaders(request.Headers);
        var inboundTraceparent = TraceParent.TryParse(request.Headers["traceparent"]);
        var requestStarted = DateTimeOffset.UtcNow;

        byte[] requestBody = [];
        if (request.HasEntityBody)
        {
            // Watched from the first read, so a caller that sends a head and no byte of its body is cut too.
            var reading = _watch?.Watch(BodyPhase, _options.StallLimit, () => CutStalled(answer, BodyPhase));
            try
            {
                var body = WrapRequestBody is { } wrap ? wrap(request.InputStream) : request.InputStream;
                requestBody = await ReadBodyAsync(body, reading).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The request never arrived whole: the caller hung up or half-closed mid-body, sent a broken chunked
                // body, the tap is stopping, or the stall limit cut it. The exception says which only by platform and by
                // how the caller closed (an IOException for a reset on Linux and macOS, an HttpListenerException for a
                // FIN there and for everything on Windows), so the step decides, not the type. Nothing reached the
                // upstream: no forward failure, and a 400 for a caller still listening.
                if (reading is not null && !reading.TryEnd())
                {
                    CutStalled(answer, BodyPhase);
                    return;
                }

                answer.StatusIfUnclosed = 400;
                return;
            }

            // The body's end and its limit settle by one move on the entry: a lost move is the limit's, whatever the read
            // returned (a chunked read cut by the abort can end as if the body were whole).
            if (reading is not null && !reading.TryEnd())
            {
                CutStalled(answer, BodyPhase);
                return;
            }
        }

        // The request is whole. Unless the tap's stopping ended its answer meanwhile, it is the forward's from here on.
        if (!answer.TryBeginForward())
            return;

        // ---- identity --------------------------------------------------------------------------
        var identity = ResolveIdentity(requestHeaders, inboundTraceparent);

        // The resolver is the host's code, and it can hold a request until the tap has begun to stop. A forward the stop
        // came before is never begun: the request is refused as one that arrives while the tap stops, neither forwarded
        // nor recorded, and a caller still being answered hears the 503.
        if (Stopping)
        {
            await RefuseWhileStoppingAsync(answer).ConfigureAwait(false);
            return;
        }

        var resolved = identity; // what the request named, before the fallback below: only that may name a trace
        var ignore = requestHeaders.ContainsKey(TestTrackingHttpHeaders.Ignore);
        var capture = identity is not null || _options.CaptureUnattributedRequests;
        identity ??= capture ? (_options.FallbackTestName, _options.FallbackTestId ?? Guid.NewGuid().ToString("N")) : null;

        // Publish "this service is busy with that test" for the length of the forward, so a capturer that
        // cannot read headers (a database tee on the same service) can attribute what it sees. Opt-in via
        // ProxyTapOptions.InFlightRegistry; the using scope covers every exit from this method, so a
        // failed forward can never leave a stale identity behind.
        using var inFlight = _options.InFlightRegistry is { } registry && identity is { } inFlightIdentity
            ? registry.Register(_options.ServiceName, inFlightIdentity.Name, inFlightIdentity.Id)
            : null;

        var traceIdHeader = FirstValue(requestHeaders, TestTrackingHttpHeaders.TraceIdHeader);
        var kronikolTraceId = traceIdHeader is not null && Guid.TryParse(traceIdHeader, out var parsedTrace) ? parsedTrace : Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        // ---- spans + traceparent ----------------------------------------------------------------
        // With TraceIdFromIdentity, a request that brings no trace and names a test (and is not ignored) is forwarded
        // under the test id, as a browser test's requests arrive; an id that is not a trace id as written is counted.
        var identityTrace = _options.TraceIdFromIdentity && inboundTraceparent is null && !ignore && resolved is { } named
            ? IsTraceId(named.Id) ? named.Id : NotApplied()
            : null;
        Activity? outer = null; // what was current before the server span: put back if a listener throws as one ends
        Activity? server = null;
        Activity? client = null;
        if (_options.EmitActivities)
        {
            // One parent for both spans: the caller's trace when it sent one, else the test's. The test's has a default
            // span id, so the server span is a root of the test's trace rather than the child of a span no one exports.
            // A default context leaves the server span to Activity.Current, as StartActivity without a parent does.
            var parent = inboundTraceparent is not null && ActivityContext.TryParse(inboundTraceparent.ToString(), null, isRemote: true, out var inboundContext)
                ? inboundContext
                : identityTrace is { } testTrace
                    ? new ActivityContext(ActivityTraceId.CreateFromString(testTrace), default, ActivityTraceFlags.Recorded, isRemote: true)
                    : default;
            // A parent with no span id adopts Activity.Current, and the test's trace would hang off it. The accept loop
            // starts with none, but the host's IdentityResolver runs on this flow and can set one. It starts here, as a root.
            if (parent != default && parent.SpanId == default)
                Activity.Current = null;
            outer = Activity.Current;
            server = StartSpan(() => ActivitySource.StartActivity($"tap {_options.DisplayName}", ActivityKind.Server, parent));
            server?.SetTag("kronikol.caller", _options.CallerName);
            server?.SetTag("kronikol.service", _options.ServiceName);
            server?.SetTag("http.request.method", request.HttpMethod);
            server?.SetTag("url.path", request.Url?.AbsolutePath);
            if (identity is not null)
            {
                server?.SetTag("kronikol.test.name", identity.Value.Name);
                server?.SetTag("kronikol.test.id", identity.Value.Id);
            }

            // The client span is the server span's child. When a listener dropped the server span, it starts under
            // the same parent, so a sampler that keeps it cannot start a new trace in place of the caller's.
            client = server is not null
                ? StartSpan(() => ActivitySource.StartActivity("forward", ActivityKind.Client))
                : StartSpan(() => ActivitySource.StartActivity("forward", ActivityKind.Client, parent));
        }

        string? forwardedTraceparent;
        string activityTraceId;
        string activitySpanId;
        if (client is not null)
        {
            activityTraceId = client.TraceId.ToString();
            activitySpanId = client.SpanId.ToString();
            // Not client.Id: a listener that records without sampling (AllData, as Kronikol's internal-flow listener
            // does) leaves the span's sampled flag clear, and forwarding that would tell every service behind the tap
            // to drop a trace the caller sampled. The caller's flags go on, or sampled for a trace the tap starts, as
            // without a listener; a recorded span still says sampled.
            forwardedTraceparent = TraceParent.Format(activityTraceId, activitySpanId,
                (byte)((inboundTraceparent?.Flags ?? 1) | (client.Recorded ? 1 : 0)));
        }
        else if (inboundTraceparent is not null)
        {
            forwardedTraceparent = null; // copied verbatim below — transparent when nobody listens
            activityTraceId = inboundTraceparent.TraceId;
            activitySpanId = inboundTraceparent.ParentSpanId;
        }
        else if (_options.SynthesizeTraceparent)
        {
            activityTraceId = identityTrace ?? ActivityTraceId.CreateRandom().ToString();
            activitySpanId = ActivitySpanId.CreateRandom().ToString();
            forwardedTraceparent = TraceParent.Format(activityTraceId, activitySpanId);
        }
        else
        {
            // Nothing is sent, but the records belong to the test's trace when TraceIdFromIdentity applies.
            activityTraceId = identityTrace ?? ActivityTraceId.CreateRandom().ToString();
            activitySpanId = ActivitySpanId.CreateRandom().ToString();
            forwardedTraceparent = null;
        }

        // ---- forward ---------------------------------------------------------------------------
        var lifetime = _lifetime;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        if (ProxyTapOptions.Deadline(_options.ForwardTimeout) is { } forwardDeadline)
            deadline.CancelAfter(forwardDeadline);

        using var message = new HttpRequestMessage(new HttpMethod(request.HttpMethod), forwardUri);
        if (requestBody.Length > 0 || request.HasEntityBody)
            message.Content = new ByteArrayContent(requestBody);

        CopyRequestHeaders(request, message);

        if (forwardedTraceparent is not null)
        {
            message.Headers.Remove("traceparent");
            message.Headers.TryAddWithoutValidation("traceparent", forwardedTraceparent);
        }

        if (_options.ReinjectCorrelation && identity is not null && !ignore)
            Reinject(message, requestHeaders, identity.Value, kronikolTraceId);

        BeforeForward?.Invoke();

        var watch = Stopwatch.StartNew();
        int status;
        byte[] responseBody;
        (string Key, string? Value)[] responseHeaders;
        string? responseContentType;
        string? responseContentEncoding;
        try
        {
            using var response = await _upstream!.SendAsync(message, HttpCompletionOption.ResponseContentRead, deadline.Token).ConfigureAwait(false);
            responseBody = await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            responseContentType = response.Content.Headers.ContentType?.ToString();
            responseContentEncoding = response.Content.Headers.ContentEncoding.Count > 0 ? string.Join(",", response.Content.Headers.ContentEncoding) : null;
            responseHeaders = response.Headers.Concat(response.Content.Headers)
                .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
                .ToArray();
        }
        catch (Exception ex)
        {
            // Everything in the try is the forward's, so whatever it threw is the forward's failure (#171).
            var failure = ForwardFailure.Classify(ex, deadline.Token, lifetime);
            var reason = ForwardFailure.Reason(failure, ex, _options);
            var error = failure == ForwardFailureKind.Failed ? reason : "proxy-tap: " + reason;
            var badGateway = BadGatewayBody(reason);

            client?.SetStatus(ActivityStatusCode.Error, error);
            client?.SetTag("error.type", ForwardFailure.ErrorType(ex));
            EndSpan(client, restore: server ?? outer);
            server?.SetTag("http.response.status_code", 502);
            server?.SetStatus(ActivityStatusCode.Error);
            EndSpan(server, restore: outer);

            // Recorded, counted and logged before the 502 goes out, so a caller holding its 502 finds the failure in the
            // records, the counters and the log, as it found the count before the record existed.
            var recorded = capture && identity is not null && TryRecord(identity.Value, ignore, kronikolTraceId, requestResponseId,
                request.HttpMethod, forwardUri, requestHeaders, requestBody, request.Headers["Content-Encoding"], requestStarted,
                new ResponseHalf(FailedSend.Status(ex), [("Content-Type", "application/json")], badGateway, null, DateTimeOffset.UtcNow, error),
                activityTraceId, activitySpanId);

            if (failure == ForwardFailureKind.Stopping)
            {
                TryLog($"[{_options.DisplayName}] proxy-tap stopping: {error}");
            }
            else
            {
                if (recorded)
                    Interlocked.Increment(ref _failuresRecorded);
                else if (!capture || identity is null)
                    Interlocked.Increment(ref _failuresWithoutIdentity);
                Interlocked.Increment(ref _forwardFailures);
                TryLog($"[{_options.DisplayName}] proxy-tap error: {error}");
            }

            // Written for a stopping tap too: DisposeAsync keeps the listener open until the drain is over, and a forward it
            // cut short is answered the 502 its record names.
            await TryRespondAsync(answer, 502, badGateway).ConfigureAwait(false);
            return;
        }
        finally
        {
            watch.Stop();
        }

        var responseReceived = DateTimeOffset.UtcNow;
        client?.SetTag("http.response.status_code", status);
        server?.SetTag("http.response.status_code", status);
        if (status >= 500)
            server?.SetStatus(ActivityStatusCode.Error);
        EndSpan(client, restore: server ?? outer);
        EndSpan(server, restore: outer);

        // Respond first, record second. An answer the tap's stopping has ended is not written, and the exchange is still
        // handled and recorded, as one whose caller hangs up while it is written.
        if (answer.TryBeginAnswer())
            await RespondAsync(answer, status, responseHeaders, responseContentType, responseContentEncoding, responseBody).ConfigureAwait(false);
        Interlocked.Increment(ref _handled);
        AfterAnswered?.Invoke();

        if (!capture || identity is null)
        {
            Interlocked.Increment(ref _uncaptured);
            return;
        }

        if (TryRecord(identity.Value, ignore, kronikolTraceId, requestResponseId, request.HttpMethod, forwardUri,
                requestHeaders, requestBody, request.Headers["Content-Encoding"], requestStarted,
                new ResponseHalf((HttpStatusCode)status, responseHeaders, responseBody, responseContentEncoding, responseReceived, null),
                activityTraceId, activitySpanId))
            Interlocked.Increment(ref _captured);
    }

    /// <summary>
    /// Starts a span, and treats a listener that throws as a capture fault: the call goes on as if nobody listened.
    /// <see cref="Activity.Start"/> makes the span current before it tells the listeners, so a throw there leaves that
    /// span current and running; it is stopped, and what was current is put back.
    /// </summary>
    internal Activity? StartSpan(Func<Activity?> start)
    {
        var before = Activity.Current;
        try
        {
            return start();
        }
        catch (Exception ex)
        {
            if (Activity.Current is { } orphan && orphan != before && orphan.Source == ActivitySource)
            {
                try
                {
                    orphan.Stop();
                }
                catch (Exception)
                {
                    // The same listener again; the span is stopped either way.
                }
            }

            Activity.Current = before;
            CaptureFault("an ActivityListener", ex);
            return null;
        }
    }

    /// <summary>
    /// Ends a span, and treats a listener that throws as a capture fault. The span is stopped by then, but a throw leaves
    /// it current, so <paramref name="restore"/> is put back.
    /// </summary>
    internal void EndSpan(Activity? span, Activity? restore)
    {
        if (span is null)
            return;
        try
        {
            span.Dispose();
        }
        catch (Exception ex)
        {
            if (Activity.Current == span)
                Activity.Current = restore;
            CaptureFault("an ActivityListener", ex);
        }
    }

    /// <summary>Records the exchange, and treats a sink (or anything else in the record) that throws as a capture fault.</summary>
    private bool TryRecord(
        (string Name, string Id) identity, bool ignore, Guid traceId, Guid requestResponseId, string method, Uri uri,
        IReadOnlyDictionary<string, string[]> requestHeaders, byte[] requestBody, string? requestEncoding, DateTimeOffset requestStarted,
        ResponseHalf response, string activityTraceId, string activitySpanId)
    {
        try
        {
            Record(identity, ignore, traceId, requestResponseId, method, uri, requestHeaders, requestBody, requestEncoding, requestStarted,
                response, activityTraceId, activitySpanId);
            return true;
        }
        catch (Exception ex)
        {
            CaptureFault("the sink", ex);
            return false;
        }
    }

    /// <summary>The response half of a record: the upstream's answer, or a failed forward's status, 502 body and cause.</summary>
    private readonly record struct ResponseHalf(
        OneOf<HttpStatusCode, string> Status, (string Key, string? Value)[] Headers, byte[] Body, string? Encoding, DateTimeOffset Received, string? Error);

    // ------------------------------------------------------------------ identity

    /// <summary>
    /// Whether <paramref name="id"/> is a W3C trace id as written: 32 lower-case hex digits, not all zero, which is
    /// what <see cref="ActivityTraceId.CreateFromString(ReadOnlySpan{char})"/> takes and what every reader behind the
    /// tap turns back into the same test id.
    /// </summary>
    internal static bool IsTraceId(string? id)
    {
        // Null too: the resolver's id is declared non-null, but a host can return null, and the request is forwarded then.
        if (id is not { Length: 32 })
            return false;
        var nonZero = false;
        foreach (var c in id)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                return false;
            nonZero |= c != '0';
        }

        return nonZero;
    }

    /// <summary>Counts a request <see cref="ProxyTapOptions.TraceIdFromIdentity"/> could not apply to, logs the first, and returns null.</summary>
    private string? NotApplied()
    {
        if (Interlocked.Increment(ref _traceIdNotApplied) == 1)
            TryLog($"[{_options.DisplayName}] proxy-tap: TraceIdFromIdentity did not apply, because the test id is not a W3C trace id (32 lower-case hex digits, not all zero); the request was forwarded under a random trace. Diagnostics() counts every such request.");
        return null;
    }

    internal (string Name, string Id)? ResolveIdentity(IReadOnlyDictionary<string, string[]> headers, TraceParent? traceparent)
    {
        if (_options.IdentityResolver is not null)
        {
            try
            {
                var custom = _options.IdentityResolver(headers, traceparent);
                if (custom is not null)
                    return custom;
            }
            catch (Exception ex)
            {
                // The host's rule failed for this request: the built-in rules decide, as when it returns null, and the
                // call is forwarded either way.
                CaptureFault("the IdentityResolver", ex);
            }
        }

        // Kronikol's own headers carry a value that is not plain ASCII in the UTF-8'' form (TrackingHeaderValue).
        var name = TrackingHeaderValue.Decode(FirstValue(headers, TestTrackingHttpHeaders.CurrentTestNameHeader));
        foreach (var fallback in _options.TestNameHeaderFallbacks)
        {
            if (name is not null) break;
            name = FirstValue(headers, fallback);
        }

        var id = TrackingHeaderValue.Decode(FirstValue(headers, TestTrackingHttpHeaders.CurrentTestIdHeader));
        foreach (var fallback in _options.TestIdHeaderFallbacks)
        {
            if (id is not null) break;
            id = FirstValue(headers, fallback);
        }

        if (id is null && _options.IdentityFromTraceparent && traceparent is not null)
            id = traceparent.TraceId;

        if (id is null)
            return null;

        return (name ?? _options.FallbackTestName, id);
    }

    private void Reinject(HttpRequestMessage message, IReadOnlyDictionary<string, string[]> inbound, (string Name, string Id) identity, Guid traceId)
    {
        if (!inbound.ContainsKey(TestTrackingHttpHeaders.CurrentTestNameHeader))
            message.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CurrentTestNameHeader, TrackingHeaderValue.Encode(identity.Name));
        if (!inbound.ContainsKey(TestTrackingHttpHeaders.CurrentTestIdHeader))
            message.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CurrentTestIdHeader, TrackingHeaderValue.Encode(identity.Id));
        if (!inbound.ContainsKey(TestTrackingHttpHeaders.CallerNameHeader))
            message.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CallerNameHeader, TrackingHeaderValue.Encode(_options.ServiceName));
        if (!inbound.ContainsKey(TestTrackingHttpHeaders.TraceIdHeader))
            message.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.TraceIdHeader, traceId.ToString());
    }

    // ------------------------------------------------------------------ capture

    private void Record(
        (string Name, string Id) identity, bool ignore, Guid traceId, Guid requestResponseId,
        string method, Uri uri,
        IReadOnlyDictionary<string, string[]> requestHeaders, byte[] requestBody, string? requestEncoding, DateTimeOffset requestStarted,
        ResponseHalf response,
        string activityTraceId, string activitySpanId)
    {
        var httpMethod = new HttpMethod(method);

        var capturedRequestHeaders = FilterHeaders(requestHeaders.SelectMany(h => h.Value.Select(v => (h.Key, (string?)v))));
        var capturedResponseHeaders = FilterHeaders(response.Headers);

        var requestContent = _options.CaptureBodies ? CapturedBody(requestBody, requestEncoding) : null;
        var responseContent = _options.CaptureBodies ? CapturedBody(response.Body, response.Encoding) : null;

        _options.Sink.Log(new RequestResponseLog(
            identity.Name, identity.Id, httpMethod, requestContent, uri, capturedRequestHeaders,
            _options.ServiceName, _options.CallerName, RequestResponseType.Request, traceId, requestResponseId, ignore,
            DependencyCategory: _options.DependencyCategory, CallerDependencyCategory: _options.CallerDependencyCategory)
        {
            Timestamp = requestStarted,
            ActivityTraceId = activityTraceId,
            ActivitySpanId = activitySpanId,
            Phase = _options.Phase,
        });

        _options.Sink.Log(new RequestResponseLog(
            identity.Name, identity.Id, httpMethod, responseContent, uri, capturedResponseHeaders,
            _options.ServiceName, _options.CallerName, RequestResponseType.Response, traceId, requestResponseId, ignore,
            response.Status,
            DependencyCategory: _options.DependencyCategory, CallerDependencyCategory: _options.CallerDependencyCategory)
        {
            Timestamp = response.Received,
            Error = response.Error,
            ActivityTraceId = activityTraceId,
            ActivitySpanId = activitySpanId,
            Phase = _options.Phase,
        });
    }

    internal (string Key, string? Value)[] FilterHeaders(IEnumerable<(string Key, string? Value)> headers)
    {
        if (_options.HeaderPolicy == HeaderCapturePolicy.None)
            return [];

        var result = new List<(string Key, string? Value)>();
        foreach (var header in headers)
        {
            if (_options.HeaderPolicy == HeaderCapturePolicy.Whitelist && !_options.HeaderWhitelist.Contains(header.Key))
                continue;

            if (_options.HeaderPolicy != HeaderCapturePolicy.All && _options.SecretDenylist.Contains(header.Key))
            {
                if (!_options.DropSecretHeaders)
                    result.Add((header.Key, _options.RedactedValue));
                continue;
            }

            result.Add(header);
        }

        return result.ToArray();
    }

    internal string? CapturedBody(byte[] raw, string? contentEncoding)
    {
        if (raw.Length == 0)
            return null;

        if (!TryDecodeBody(raw, contentEncoding, out var text))
            return $"<{raw.Length} bytes, undecodable content-encoding '{contentEncoding}'>";

        if (_options.BodyCapBytes is { } cap && text.Length > cap)
            return $"{text[..cap]}\n\n…truncated ({text.Length} chars total)";

        return text;
    }

    /// <summary>Decodes a body per its <c>Content-Encoding</c> (identity, gzip, deflate, br). False means "opaque".</summary>
    public static bool TryDecodeBody(byte[] raw, string? contentEncoding, out string body)
    {
        body = string.Empty;
        try
        {
            switch (contentEncoding?.Trim().ToLowerInvariant())
            {
                case null or "" or "identity":
                    body = Encoding.UTF8.GetString(raw);
                    return true;
                case "gzip":
                {
                    using var source = new MemoryStream(raw);
                    using var gzip = new GZipStream(source, CompressionMode.Decompress);
                    using var reader = new StreamReader(gzip, Encoding.UTF8);
                    body = reader.ReadToEnd();
                    return true;
                }
                case "deflate":
                {
                    using var source = new MemoryStream(raw);
                    using var deflate = new DeflateStream(source, CompressionMode.Decompress);
                    using var reader = new StreamReader(deflate, Encoding.UTF8);
                    body = reader.ReadToEnd();
                    return true;
                }
                case "br":
                {
                    using var source = new MemoryStream(raw);
                    using var brotli = new BrotliStream(source, CompressionMode.Decompress);
                    using var reader = new StreamReader(brotli, Encoding.UTF8);
                    body = reader.ReadToEnd();
                    return true;
                }
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ plumbing

    private static IReadOnlyDictionary<string, string[]> ReadHeaders(System.Collections.Specialized.NameValueCollection headers)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in headers.AllKeys)
        {
            if (key is null) continue;
            result[key] = headers.GetValues(key) ?? [];
        }

        return result;
    }

    private static string? FirstValue(IReadOnlyDictionary<string, string[]> headers, string name) =>
        headers.TryGetValue(name, out var values) && values.Length > 0 && !string.IsNullOrWhiteSpace(values[0]) ? values[0] : null;

    private static void CopyRequestHeaders(HttpListenerRequest request, HttpRequestMessage message)
    {
        foreach (var key in request.Headers.AllKeys)
        {
            if (key is null || HopByHop.Contains(key))
                continue;
            var values = request.Headers.GetValues(key);
            if (values is null)
                continue;
            foreach (var value in values)
            {
                if (!message.Headers.TryAddWithoutValidation(key, value))
                    message.Content?.Headers.TryAddWithoutValidation(key, value);
            }
        }
    }

    private async Task RespondAsync(
        CallerAnswer answer, int status, (string Key, string? Value)[] headers, string? contentType, string? contentEncoding, byte[] body)
    {
        var response = answer.Response;
        try
        {
            CloseAfterIfStopping(response);
            response.StatusCode = status;
            foreach (var (key, value) in headers)
            {
                if (value is null || HopByHop.Contains(key) || key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) || key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    response.Headers.Add(key, value);
                }
                catch (ArgumentException)
                {
                    // Restricted header (e.g. Date/Server are owned by HttpListener) — skip.
                }
            }

            if (contentType is not null)
                response.ContentType = contentType;
            if (contentEncoding is not null)
                response.Headers["Content-Encoding"] = contentEncoding;

            await WriteAndCloseAsync(answer, body).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException || answer.IsEnded || answer.IsTimedOut)
        {
            // The client hung up, the tap's stopping ended the answer, or the stall limit cut it (whatever its write threw
            // then); the exchange is still recorded, and GuardedHandleAsync aborts the response.
        }
    }

    /// <summary>
    /// Writes an answer's body in pieces (<see cref="PieceLength"/>), recording progress after each, and closes the response
    /// unless the stall limit cut it. The body keeps its <c>Content-Length</c>: the managed listener would send it chunked
    /// otherwise, and there a chunked answer's abort waits until the caller reads.
    /// </summary>
    private async Task WriteAndCloseAsync(CallerAnswer answer, byte[] body)
    {
        var response = answer.Response;
        response.ContentLength64 = body.Length;
        var writing = _watch?.Watch(AnswerPhase, _options.StallLimit, () => CutStalled(answer, AnswerPhase));
        var written = false;
        try
        {
            var output = response.OutputStream;
            // A write pending when the limit aborts the response completes without an error on the managed listener, so
            // the loop reads the answer's state, not the write's outcome.
            for (var offset = 0; offset < body.Length && !answer.IsTimedOut;)
            {
                var length = PieceLength(body.Length - offset);
                await output.WriteAsync(body.AsMemory(offset, length)).ConfigureAwait(false);
                offset += length;
                writing?.Progress();
            }

            written = true;
        }
        finally
        {
            // The answer's end and its limit settle by one move on the entry: a lost move is the limit's.
            if (writing is not null && !writing.TryEnd())
                CutStalled(answer, AnswerPhase);
        }

        if (written && !answer.IsTimedOut)
            answer.Close();
    }

    /// <summary>
    /// The length of an answer's next piece, given what is left of it: <see cref="PieceBytes"/>, or all of the rest once
    /// less than two pieces' worth is left. The last piece is so 16 to 32 KiB long, and never short enough for http.sys to
    /// reset it, at its send rate, before the tap's own limit acts.
    /// </summary>
    internal static int PieceLength(int remaining) => remaining < 2 * PieceBytes ? remaining : PieceBytes;

    /// <summary>
    /// Once the tap is stopping, an answer closes its connection after it (<c>Connection: close</c>): closing the listener
    /// resets the connections http.sys keeps alive, and a caller that had not yet read its answer lost it to that reset.
    /// </summary>
    private void CloseAfterIfStopping(HttpListenerResponse response)
    {
        if (Stopping)
            response.KeepAlive = false;
    }

    /// <summary>
    /// The body of the tap's own <c>502</c>: <c>{"error":{"code":502,"message":"proxy-tap: …"}}</c>, valid JSON whatever the
    /// message holds (a quote stays a quote, a line break is escaped, non-ASCII stays readable).
    /// </summary>
    internal static byte[] BadGatewayBody(string message) => ErrorBody(502, message);

    /// <summary>The body of an answer of the tap's own: <c>{"error":{"code":…,"message":"proxy-tap: …"}}</c>.</summary>
    internal static byte[] ErrorBody(int code, string message)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteStartObject("error");
            json.WriteNumber("code", code);
            json.WriteString("message", "proxy-tap: " + message);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    // Called just before the forward, and once the caller has its answer (Kronikol.Tests.ProxyTap): a fact throws from
    // either to stand for a fault the tap does not expect. Null in production.
    internal Action? BeforeForward { get; set; }

    internal Action? AfterAnswered { get; set; }

    /// <summary>
    /// Writes an answer of the tap's own, a JSON body with <paramref name="status"/>, unless the answer has already ended.
    /// The write is awaited and watched like any answer's: on Windows an abort cannot interrupt a synchronous write.
    /// </summary>
    private async Task TryRespondAsync(CallerAnswer answer, int status, byte[] payload)
    {
        if (!answer.IsOpen)
            return; // closed, ended by the tap's stopping, or cut by the stall limit: nothing left to tell

        var response = answer.Response;
        try
        {
            CloseAfterIfStopping(response);
            response.StatusCode = status;
            response.ContentType = "application/json";
            await WriteAndCloseAsync(answer, payload).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException || answer.IsEnded || answer.IsTimedOut)
        {
            // Nothing left to tell; GuardedHandleAsync aborts the response.
        }
    }

    /// <summary>
    /// The caller's response, owned by <see cref="GuardedHandleAsync"/>: the tap closes it once an answer is written, and
    /// <see cref="EndUnclosed"/> ends every response not closed that way, whichever path left it. The managed
    /// <see cref="HttpListener"/> (Linux, macOS) keeps a connection until its response is closed or aborted, so a
    /// response left open is a socket held until the tap is disposed.
    /// </summary>
    /// <remarks>
    /// The answer has one state, moved only by <see cref="Interlocked.CompareExchange(ref int, int, int)"/>, since the
    /// handler and <see cref="DisposeAsync"/> may end it from two threads at once: each acts only when its move wins, so a
    /// response is closed or ended once, and never aborted after a close that succeeded.
    /// </remarks>
    internal sealed class CallerAnswer(HttpListenerResponse response)
    {
        // Open phases first: the request body is being read (or not yet begun), the forward is running, the answer is
        // being written. Then the ends: closing, closed by the tap, ended by an abort, or cut by the stall limit.
        private const int Reading = 0, Forwarding = 1, Answering = 2, Closing = 3, Closed = 4, Ended = 5, TimedOut = 6;

        private int _state;

        public HttpListenerResponse Response { get; } = response;

        /// <summary>The status a response not yet started is ended with: 502, or 400 when the request never arrived whole.</summary>
        public int StatusIfUnclosed { get; set; } = 502;

        /// <summary>Whether the tap closed the response itself, which it is only once <see cref="HttpListenerResponse.Close()"/> has returned.</summary>
        public bool IsClosed => Volatile.Read(ref _state) == Closed;

        /// <summary>Whether the response was ended by an abort: whatever its I/O throws from then on is that end's, never a forward failure.</summary>
        public bool IsEnded => Volatile.Read(ref _state) == Ended;

        /// <summary>Whether the stall limit cut the caller: whatever the response's I/O throws from then on is the limit's.</summary>
        public bool IsTimedOut => Volatile.Read(ref _state) == TimedOut;

        /// <summary>Whether neither a close nor an end has begun.</summary>
        public bool IsOpen => Volatile.Read(ref _state) <= Answering;

        /// <summary>The request is whole and the forward begins; false when the answer was ended meanwhile.</summary>
        public bool TryBeginForward() => Interlocked.CompareExchange(ref _state, Forwarding, Reading) == Reading;

        /// <summary>The upstream has answered and the answer is written; false when the answer was ended meanwhile.</summary>
        public bool TryBeginAnswer() => Interlocked.CompareExchange(ref _state, Answering, Forwarding) == Forwarding;

        /// <summary>
        /// Closes the response, unless it was ended first; true when this call closed it. A
        /// <see cref="HttpListenerResponse.Close()"/> that throws (on Windows, 1229 for a caller gone) is passed on and leaves
        /// the answer open, so <see cref="EndUnclosed"/> still aborts it.
        /// </summary>
        public bool Close()
        {
            var phase = Volatile.Read(ref _state);
            if (phase > Answering || Interlocked.CompareExchange(ref _state, Closing, phase) != phase)
                return false;
            try
            {
                Response.Close();
                Volatile.Write(ref _state, Closed);
                return true;
            }
            catch
            {
                Volatile.Write(ref _state, phase);
                throw;
            }
        }

        /// <summary>
        /// Aborts the response unless the tap closed it, with <see cref="StatusIfUnclosed"/> set first, and never throws: it
        /// runs in a <c>finally</c> before the in-flight count goes down, and a failed end leaves the response as it was.
        /// True when this call ended it.
        /// </summary>
        public bool EndUnclosed() => TryEnd(StatusIfUnclosed, readingOnly: false, closeConnection: false, Ended);

        /// <summary>Ends the answer of a request whose body is still being read, or not yet begun, with <paramref name="status"/>; false in any other phase.</summary>
        public bool EndWhileReading(int status) => TryEnd(status, readingOnly: true, closeConnection: true, Ended);

        /// <summary>Ends the answer in whatever open phase it is, with <paramref name="status"/> where its head is not yet sent.</summary>
        public bool EndWith(int status) => TryEnd(status, readingOnly: false, closeConnection: true, Ended);

        /// <summary>
        /// Cuts a caller that stalled past the limit, in whatever open phase its answer is, with <paramref name="status"/>
        /// where the head is not yet sent (408 while the body is read) and the connection closed; false when the answer was
        /// closed or ended first. <paramref name="claimed"/> runs once the cut has won and before the caller is let go, so
        /// what it records is there by the time the caller hears the end.
        /// </summary>
        public bool TimeOut(int? status, Action? claimed = null) => TryEnd(status, readingOnly: false, closeConnection: true, TimedOut, claimed);

        private bool TryEnd(int? status, bool readingOnly, bool closeConnection, int end, Action? claimed = null)
        {
            while (true)
            {
                var phase = Volatile.Read(ref _state);
                if (phase > Answering || (readingOnly && phase != Reading))
                    return false;
                if (Interlocked.CompareExchange(ref _state, end, phase) == phase)
                    break;
            }

            claimed?.Invoke();

            // The managed listener's Abort() first writes a head not yet sent, with the status set (200 unless told
            // otherwise), so a failure status makes that head true; once a head is out, the late status is never sent
            // and Abort() only closes. http.sys sends nothing more.
            if (status is { } code)
            {
                try
                {
                    Response.StatusCode = code;
                }
                catch (Exception)
                {
                    // The response is closed (the setter does not throw once the head has gone).
                }
            }

            if (closeConnection)
            {
                try
                {
                    Response.KeepAlive = false;
                }
                catch (Exception)
                {
                    // The head has gone; the abort closes the connection all the same.
                }
            }

            try
            {
                Response.Abort();
            }
            catch (Exception)
            {
                // On Windows the listener is closed (the tap is stopping), and its request queue with it.
            }

            return true;
        }
    }

    /// <summary>
    /// Stops the tap, settling each exchange by its phase before the listener closes, so no caller is told that a request
    /// the tap did not answer succeeded: a request whose body is still arriving is answered <c>503 Service
    /// Unavailable</c> (on Windows the connection is reset), a forward in flight is cut short and answered the
    /// <c>502</c> its record names, an answer in hand is given up to 2 s to finish so its capture lands, and a request
    /// that arrives meanwhile is answered <c>503</c> without being forwarded. What is still open after those 2 s is ended.
    /// Then it releases the upstream client. Safe to await more than once, and on a tap that never started. It gives the
    /// port back without asking for it again, so another listener taking that port cannot fail the disposal.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        // The stall limit's heartbeat stops first: the stop settles every exchange itself from here on.
        _watch?.Dispose();

        // Stopping from here on: a forward in flight is cut and answers its own 502 (HandleAsync), and a request that
        // arrives is answered 503 (GuardedHandleAsync).
        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);

        // Closing the managed listener (Linux, macOS) answers 200 OK on every connection it still holds, and waits a
        // second on each body that is not coming; an answer still reading its request's body is ended first.
        foreach (var answer in _answers.Keys)
            answer.EndWhileReading(503);

        for (var waited = 0; Volatile.Read(ref _inflight) > 0 && waited < 2_000; waited += 25)
            await Task.Delay(25).ConfigureAwait(false);

        foreach (var answer in _answers.Keys)
            answer.EndWith(503);

        try
        {
            // Close() alone, never Stop() first. On the managed HttpListener (the implementation .NET
            // uses off Windows) both calls remove the listener from HttpEndPointManager: Stop() frees
            // the port, and Close()'s second removal goes through GetEPListener, which *binds* a fresh
            // endpoint listener because the map no longer holds one for that host and port — throwing
            // "Address already in use" if anything claimed the port in between. Giving a port up must
            // not depend on being able to get it back. Close() stops listening on its own, and the
            // accept loop's wait ends just below.
            _listener.Close();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException)
        {
            // Already closed, or the port has moved on. Nothing left to release.
        }

        await _listenerClosed.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        _upstream?.Dispose();
        _cts?.Dispose();
        _listenerClosed.Dispose();
        TryLog($"[{_options.DisplayName}] proxy-tap stopped ({RequestsHandled} forwarded, {RequestsCaptured} captured)");
    }
}
