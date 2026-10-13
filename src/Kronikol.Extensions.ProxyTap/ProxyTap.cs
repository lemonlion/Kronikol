using System.Globalization;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
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

    private readonly ProxyTapOptions _options;
    private readonly HttpListener _listener = new();
    private HttpClient? _upstream;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _inflight;
    private int _disposed;
    private long _handled;
    private long _captured;
    private long _forwardFailures;
    private long _traceIdNotApplied;

    /// <summary>Creates a tap for the given options (validated on <see cref="StartAsync"/>).</summary>
    public ProxyTap(ProxyTapOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The options this tap runs with.</summary>
    public ProxyTapOptions Options => _options;

    /// <summary>The URI callers should dial.</summary>
    public Uri ListenUri => new($"http://{_options.ListenHost}:{_options.ListenPort}/");

    /// <summary>Exchanges forwarded so far.</summary>
    public long RequestsHandled => Interlocked.Read(ref _handled);

    /// <summary>Exchanges captured (attributed to a test) so far.</summary>
    public long RequestsCaptured => Interlocked.Read(ref _captured);

    /// <summary>
    /// Requests the tap could not forward (the upstream was unreachable, timed out or the exchange threw) and answered
    /// <c>502 Bad Gateway</c> instead. A request whose caller hangs up before it has arrived whole is not counted: it was
    /// never forwarded, and its caller is not there to answer.
    /// </summary>
    public long ForwardFailures => Interlocked.Read(ref _forwardFailures);

    /// <summary>Whether the listener is accepting connections.</summary>
    public bool IsListening => _listener.IsListening;

    // Exchanges whose handler has not finished (Kronikol.Tests.ProxyTap): a fact hangs up only once the tap holds the request.
    internal int InFlight => Volatile.Read(ref _inflight);

    // Wraps the caller's request body before it is read (Kronikol.Tests.ProxyTap): a fact throws from the read whatever
    // exception it names, on any platform. Null in production.
    internal Func<Stream, Stream>? WrapRequestBody { get; set; }

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
                string.Create(CultureInfo.InvariantCulture, $"{name}: {forwardFailures:N0} request(s) could not be forwarded and were answered 502 Bad Gateway — the upstream was unreachable, timed out or the exchange threw; those calls are missing from the diagrams")));

        var handled = RequestsHandled;
        var uncaptured = handled - RequestsCaptured;
        if (uncaptured > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                string.Create(CultureInfo.InvariantCulture, $"{name}: {uncaptured:N0} of {handled:N0} forwarded request(s) carried no test identity and were not captured (no test-tracking headers or traceparent reached this hop; CaptureUnattributedRequests is off)")));

        var notApplied = Interlocked.Read(ref _traceIdNotApplied);
        if (notApplied > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.OptionNotApplied,
                string.Create(CultureInfo.InvariantCulture, $"{name}: TraceIdFromIdentity did not apply to {notApplied:N0} request(s), because their test id is not a W3C trace id (32 lower-case hex digits, not all zero); they were forwarded under a random trace, so the services behind this tap cannot be matched to the test")));

        return entries;
    }

    /// <summary>Starts listening. Throws if the options are invalid or the port cannot be bound.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();

        _upstream = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            // None: the wire bytes must reach the caller exactly as the service sent them; capture decodes its own copy.
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = _options.ConnectTimeout,
            UseCookies = false,
            // None: the traceparent a forward carries is the tap's to choose, and its client span is the forward's span.
            // With a propagator the handler runs whatever HttpClient instrumentation the process has, and OpenTelemetry's
            // puts a span of its own in place of the header: a new trace when nothing listens to the tap, the sampled
            // flag cleared under Kronikol's internal-flow listener.
            ActivityHeadersPropagator = null,
        })
        {
            BaseAddress = _options.ForwardBaseUri,
            Timeout = _options.ForwardTimeout + TimeSpan.FromSeconds(10),
        };

        _listener.Prefixes.Add(ListenUri.ToString());
        _listener.Start();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // The accept loop, and every request it hands on, runs in the context it is started in. Started under an activity
        // (a fixture's, a host's), every call that brought no trace would be forwarded in that activity's trace, with its
        // baggage, whatever test made it. Requests start with no current activity.
        var current = Activity.Current;
        Activity.Current = null;
        try
        {
            _loop = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
        }
        finally
        {
            Activity.Current = current;
        }
        _options.Log?.Invoke($"[{_options.DisplayName}] proxy-tap listening on {ListenUri} → {_options.ForwardBaseUri}");
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => GuardedHandleAsync(context), CancellationToken.None);
        }
    }

    private async Task GuardedHandleAsync(HttpListenerContext context)
    {
        Interlocked.Increment(ref _inflight);
        var answer = new CallerAnswer(context.Response);
        try
        {
            await HandleAsync(context, answer).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            // Not the caller: reading its request and writing its answer have catches of their own, so the listener no
            // longer throws here. What still lands here is an ObjectDisposedException from another step, such as the
            // upstream client disposed by DisposeAsync after its drain, or a disposed sink. The finally ends the response.
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _forwardFailures);
            _options.Log?.Invoke($"[{_options.DisplayName}] proxy-tap error: {ex.Message}");
            TryRespondBadGateway(answer, ex.Message);
        }
        finally
        {
            // Every exit, whatever it did: a response the tap did not close is aborted, or the managed listener (Linux,
            // macOS) holds its connection until the tap is disposed.
            answer.EndUnclosed();
            Interlocked.Decrement(ref _inflight);
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
            try
            {
                using var buffer = new MemoryStream();
                var body = WrapRequestBody is { } wrap ? wrap(request.InputStream) : request.InputStream;
                await body.CopyToAsync(buffer).ConfigureAwait(false);
                requestBody = buffer.ToArray();
            }
            catch (Exception)
            {
                // The request never arrived whole: the caller hung up or half-closed mid-body, sent a broken chunked
                // body, or the tap is stopping. The exception says which only by platform and by how the caller closed
                // (an IOException for a reset on Linux and macOS, an HttpListenerException for a FIN there and for
                // everything on Windows), so the step decides, not the type. Nothing reached the upstream: no forward
                // failure, and a 400 for a caller still listening.
                answer.StatusIfUnclosed = 400;
                return;
            }
        }

        // ---- identity --------------------------------------------------------------------------
        var identity = ResolveIdentity(requestHeaders, inboundTraceparent);
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
            server = ActivitySource.StartActivity($"tap {_options.DisplayName}", ActivityKind.Server, parent);
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
                ? ActivitySource.StartActivity("forward", ActivityKind.Client)
                : ActivitySource.StartActivity("forward", ActivityKind.Client, parent);
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
        var lifetime = _cts?.Token ?? CancellationToken.None;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        deadline.CancelAfter(_options.ForwardTimeout);

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
        finally
        {
            watch.Stop();
        }

        var responseReceived = DateTimeOffset.UtcNow;
        client?.SetTag("http.response.status_code", status);
        server?.SetTag("http.response.status_code", status);
        if (status >= 500)
            server?.SetStatus(ActivityStatusCode.Error);
        client?.Dispose();
        server?.Dispose();

        // Respond first, record second.
        await RespondAsync(answer, status, responseHeaders, responseContentType, responseContentEncoding, responseBody).ConfigureAwait(false);
        Interlocked.Increment(ref _handled);

        if (!capture || identity is null)
            return;

        Record(identity.Value, ignore, kronikolTraceId, requestResponseId, request.HttpMethod, forwardUri,
            requestHeaders, requestBody, request.Headers["Content-Encoding"], requestStarted,
            status, responseHeaders, responseBody, responseContentEncoding, responseReceived,
            activityTraceId, activitySpanId);
        Interlocked.Increment(ref _captured);
    }

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
            _options.Log?.Invoke($"[{_options.DisplayName}] proxy-tap: TraceIdFromIdentity did not apply, because the test id is not a W3C trace id (32 lower-case hex digits, not all zero); the request was forwarded under a random trace. Diagnostics() counts every such request.");
        return null;
    }

    internal (string Name, string Id)? ResolveIdentity(IReadOnlyDictionary<string, string[]> headers, TraceParent? traceparent)
    {
        if (_options.IdentityResolver is not null)
        {
            var custom = _options.IdentityResolver(headers, traceparent);
            if (custom is not null)
                return custom;
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
        int status, (string Key, string? Value)[] responseHeaders, byte[] responseBody, string? responseEncoding, DateTimeOffset responseReceived,
        string activityTraceId, string activitySpanId)
    {
        var httpMethod = new HttpMethod(method);

        var capturedRequestHeaders = FilterHeaders(requestHeaders.SelectMany(h => h.Value.Select(v => (h.Key, (string?)v))));
        var capturedResponseHeaders = FilterHeaders(responseHeaders);

        var requestContent = _options.CaptureBodies ? CapturedBody(requestBody, requestEncoding) : null;
        var responseContent = _options.CaptureBodies ? CapturedBody(responseBody, responseEncoding) : null;

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
            (HttpStatusCode)status,
            DependencyCategory: _options.DependencyCategory, CallerDependencyCategory: _options.CallerDependencyCategory)
        {
            Timestamp = responseReceived,
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
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException or IOException)
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

    private static async Task RespondAsync(
        CallerAnswer answer, int status, (string Key, string? Value)[] headers, string? contentType, string? contentEncoding, byte[] body)
    {
        var response = answer.Response;
        try
        {
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

            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            answer.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // The client hung up; the exchange is still recorded, and GuardedHandleAsync aborts the response.
        }
    }

    private static void TryRespondBadGateway(CallerAnswer answer, string message)
    {
        var response = answer.Response;
        try
        {
            response.StatusCode = 502;
            var payload = Encoding.UTF8.GetBytes(
                "{\"error\":{\"code\":502,\"message\":\"proxy-tap: " + message.Replace("\\", "\\\\").Replace("\"", "'") + "\"}}");
            response.ContentType = "application/json";
            response.ContentLength64 = payload.Length;
            response.OutputStream.Write(payload);
            answer.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
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
    internal sealed class CallerAnswer(HttpListenerResponse response)
    {
        public HttpListenerResponse Response { get; } = response;

        /// <summary>The status a response not yet started is ended with: 502, or 400 when the request never arrived whole.</summary>
        public int StatusIfUnclosed { get; set; } = 502;

        /// <summary>Whether the tap closed the response itself, which sets it only once <see cref="HttpListenerResponse.Close()"/> has returned.</summary>
        public bool Closed { get; private set; }

        public void Close()
        {
            Response.Close();
            Closed = true;
        }

        /// <summary>
        /// Aborts the response unless the tap closed it, with <see cref="StatusIfUnclosed"/> set first, and never throws: it
        /// runs in a <c>finally</c> before the in-flight count goes down, and a failed end leaves the response as it was.
        /// </summary>
        public void EndUnclosed()
        {
            if (Closed)
                return;

            // The managed listener's Abort() first writes a head not yet sent, with the status set (200 unless told
            // otherwise), so a failure status makes that head true; once a head is out, the late status is never sent
            // and Abort() only closes. http.sys sends nothing more.
            try
            {
                Response.StatusCode = StatusIfUnclosed;
            }
            catch (Exception)
            {
                // The response is closed (the setter does not throw once the head has gone).
            }

            try
            {
                Response.Abort();
            }
            catch (Exception)
            {
                // On Windows the listener is closed (the tap is stopping), and its request queue with it.
            }
        }
    }

    /// <summary>
    /// Stops listening, drains in-flight exchanges briefly (≤ 2 s) so their captures land, and releases the
    /// upstream client. Safe to await more than once, and on a tap that never started. It gives the port
    /// back without asking for it again, so another listener taking that port cannot fail the disposal.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            // Close() alone, never Stop() first. On the managed HttpListener (the implementation .NET
            // uses off Windows) both calls remove the listener from HttpEndPointManager: Stop() frees
            // the port, and Close()'s second removal goes through GetEPListener, which *binds* a fresh
            // endpoint listener because the map no longer holds one for that host and port — throwing
            // "Address already in use" if anything claimed the port in between. Giving a port up must
            // not depend on being able to get it back. Close() stops listening on its own, and the
            // accept loop is already unblocked by the cancellation above.
            _listener.Close();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException)
        {
            // Already closed, or the port has moved on. Nothing left to release.
        }

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

        for (var waited = 0; Volatile.Read(ref _inflight) > 0 && waited < 2_000; waited += 25)
            await Task.Delay(25).ConfigureAwait(false);

        _upstream?.Dispose();
        _cts?.Dispose();
        _options.Log?.Invoke($"[{_options.DisplayName}] proxy-tap stopped ({RequestsHandled} forwarded, {RequestsCaptured} captured)");
    }
}
