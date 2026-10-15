using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Kronikol.Reports;

namespace Kronikol.Extensions.Otlp;

/// <summary>
/// An OTLP/HTTP <em>receiver-tee</em>: it accepts <c>POST /v1/traces</c> exactly as a collector does
/// (protobuf and JSON, gzip), optionally forwards the export byte-for-byte to a real collector, and — off
/// the request path — maps the client spans it recognises to Kronikol request/response pairs on an
/// <see cref="Kronikol.Tracking.IRequestResponseSink"/>.
/// </summary>
/// <remarks>
/// <para><strong>Why a span tap at all.</strong> A proxy tap sees the wire and therefore the payloads, but
/// it has to guess which test a call belongs to. A span carries the W3C trace id, and a browser-driven
/// suite mints the trace id as the test id — so the span path gives <em>exact</em> attribution, and it
/// reaches hops that cannot be proxied at all. Run both and let
/// <see cref="Kronikol.Ingestion.InteractionMerger"/> fold the duplicates at ingest.</para>
/// <para><strong>Never on the hot path.</strong> The request is authenticated, forwarded (or acknowledged)
/// and answered first; the payload is then handed to a bounded channel that drops the newest item when
/// full, and a background worker decodes and maps it. A slow or blocked sink can never slow the exporter
/// down (observability invariant D3).</para>
/// <para><strong>Plain sockets, not <c>HttpListener</c>.</strong> Unlike
/// <c>Kronikol.Extensions.ProxyTap</c>, this listener speaks HTTP/1.1 over <see cref="TcpListener"/>. It
/// has to: on Windows, http.sys refuses a non-loopback prefix without <c>netsh http add urlacl</c> or
/// elevation (<c>http://+:p/</c> → "Access is denied", <c>http://0.0.0.0:p/</c> → "The request is not
/// supported"), and a containerised exporter reaching the host through <c>host.docker.internal</c> needs
/// exactly that non-loopback bind. A socket listener also registers nothing with http.sys, so a tap can
/// never outlive its process. Pair a non-loopback bind with <see cref="OtlpTapOptions.ExpectedHeaders"/>.</para>
/// </remarks>
public sealed class OtlpTap : IAsyncDisposable
{
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive", "Proxy-Connection",
        "Expect", "Upgrade", "TE", "Trailer",
    };

    private readonly OtlpTapOptions _options;
    private readonly Channel<Payload> _queue;
    private readonly List<TcpListener> _listeners = [];
    private HttpClient? _upstream;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private Task? _mapLoop;
    private int _disposed;
    private long _requests;
    private long _unauthenticated;
    private long _forwardFailures;
    private long _dropped;
    private long _rejected;
    private long _failed;
    private long _spansReceived;
    private long _spansMapped;
    private long _spansIgnored;

    /// <summary>Creates a tap for the given options (validated on <see cref="StartAsync"/>).</summary>
    public OtlpTap(OtlpTapOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _queue = Channel.CreateBounded<Payload>(new BoundedChannelOptions(Math.Max(1, options.QueueCapacity))
        {
            // Wait, not DropWrite: TryWrite then returns false when the queue is full instead of silently
            // dropping, so the tap can count the drop. Nothing ever awaits a write, so nothing ever blocks.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        Accepting = new TapAcceptLoop(line => TryLog($"[{_options.DisplayName}] otlp-tap {line}"));
    }

    /// <summary>The options this tap runs with.</summary>
    public OtlpTapOptions Options => _options;

    /// <summary>The accept loop each listener runs; a fact makes an accept fail through its seams.</summary>
    internal TapAcceptLoop Accepting { get; }

    /// <summary>The port actually bound (equals <c>ListenPort</c> unless that was 0). Zero before <see cref="StartAsync"/>.</summary>
    public int BoundPort { get; private set; }

    /// <summary>The endpoint an exporter should be pointed at once the tap is listening.</summary>
    public Uri TracesEndpoint => new($"http://{DisplayHost()}:{(BoundPort == 0 ? _options.ListenPort : BoundPort)}{_options.TracesPath}");

    /// <summary>Whether the listener is accepting connections.</summary>
    public bool IsListening => _listeners.Count > 0 && Volatile.Read(ref _disposed) == 0;

    /// <summary>Authenticated export requests accepted so far.</summary>
    public long RequestsReceived => Interlocked.Read(ref _requests);

    /// <summary>
    /// Requests rejected with <c>401</c> because <see cref="OtlpTapOptions.ExpectedHeaders"/> was not satisfied. The headers
    /// are checked once the head is read, before anything of the body is read or allocated, so a request over
    /// <see cref="OtlpTapOptions.MaxRequestBytes"/> without them is counted here, not in <see cref="PayloadsRejected"/>.
    /// </summary>
    public long UnauthenticatedRequests => Interlocked.Read(ref _unauthenticated);

    /// <summary>Requests whose forward to <see cref="OtlpTapOptions.ForwardBaseUri"/> failed (answered <c>502</c>).</summary>
    public long ForwardFailures => Interlocked.Read(ref _forwardFailures);

    /// <summary>Export payloads dropped because the mapping queue was full (D3: capture never blocks).</summary>
    public long PayloadsDropped => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Export requests refused with <c>413</c> because the body exceeded <see cref="OtlpTapOptions.MaxRequestBytes"/>; their
    /// spans were never read. One whose exporter hung up before the drain was over is not counted: it was never answered.
    /// </summary>
    public long PayloadsRejected => Interlocked.Read(ref _rejected);

    /// <summary>Accepted payloads whose decode, span mapping or sink write threw (a sink that cannot write, an unexpected span shape); their spans are lost.</summary>
    public long PayloadsFailed => Interlocked.Read(ref _failed);

    /// <summary>
    /// Accepts that failed while the tap was running; on Linux, a process out of file descriptors is one cause. The tap
    /// keeps listening and tries again, pausing up to a second between tries, so exporters that arrive meanwhile wait in
    /// the listen queue. Reported by <see cref="Diagnostics"/>.
    /// </summary>
    public long AcceptFailures => Accepting.Failures;

    /// <summary>Spans decoded from accepted payloads.</summary>
    public long SpansReceived => Interlocked.Read(ref _spansReceived);

    /// <summary>Spans that became interactions.</summary>
    public long SpansMapped => Interlocked.Read(ref _spansMapped);

    /// <summary>Spans decoded but not captured (server/internal spans, excluded kinds, unrecognised shapes).</summary>
    public long SpansIgnored => Interlocked.Read(ref _spansIgnored);

    /// <summary>
    /// Capture health as report diagnostics: one <see cref="DiagnosticKind.CaptureDegraded"/> entry per
    /// non-zero problem counter — accepts that failed, payloads dropped, rejected or failed, exports refused as
    /// unauthenticated, forwards that failed — worded for a report reader (<c>otlp: 2 export payload(s)
    /// dropped …</c>). Empty when the tap is healthy. A host hands the list to
    /// <see cref="Kronikol.Ingestion.IngestRequest.HostDiagnostics"/> so lost spans are a line in the
    /// report, not only in a log. <see cref="SpansIgnored"/> is by design (server spans, excluded kinds)
    /// and is not reported.
    /// </summary>
    public IReadOnlyList<DiagnosticEntry> Diagnostics()
    {
        var name = _options.DisplayName;
        var entries = new List<DiagnosticEntry>();

        if (Accepting.DiagnosticLine(name) is { } acceptLine)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded, acceptLine));

        var dropped = PayloadsDropped;
        if (dropped > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                string.Create(CultureInfo.InvariantCulture, $"{name}: {dropped:N0} export payload(s) dropped because the mapping queue was full (QueueCapacity {_options.QueueCapacity}) — their spans are missing from the diagrams; the exporter was never delayed")));

        var rejected = PayloadsRejected;
        if (rejected > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                string.Create(CultureInfo.InvariantCulture, $"{name}: {rejected:N0} export request(s) refused with 413 Payload Too Large (MaxRequestBytes {_options.MaxRequestBytes:N0}) — their spans were never read")));

        var failed = PayloadsFailed;
        if (failed > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                string.Create(CultureInfo.InvariantCulture, $"{name}: {failed:N0} export payload(s) failed while being decoded, mapped or written to the sink — their spans are lost (see the tap's log for the exception)")));

        var unauthenticated = UnauthenticatedRequests;
        if (unauthenticated > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                string.Create(CultureInfo.InvariantCulture, $"{name}: {unauthenticated:N0} export request(s) rejected with 401 — ExpectedHeaders not satisfied; if that was your exporter, its spans never reached the report")));

        var forwardFailures = ForwardFailures;
        if (forwardFailures > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                string.Create(CultureInfo.InvariantCulture, $"{name}: {forwardFailures:N0} export(s) could not be forwarded to {_options.ForwardBaseUri} and were answered 502 Bad Gateway — the spans were still mapped here, but the real collector never saw them")));

        return entries;
    }

    /// <summary>Starts listening. Throws if the options are invalid or the port cannot be bound.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();

        if (_options.ForwardBaseUri is not null)
        {
            _upstream = new HttpClient(new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
            })
            {
                BaseAddress = _options.ForwardBaseUri,
                Timeout = TimeSpan.FromSeconds(30),
            };
        }

        Bind();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        _acceptLoop = Task.WhenAll(_listeners.Select(l => Task.Run(() => AcceptLoopAsync(l, token), CancellationToken.None)));
        _mapLoop = Task.Run(() => MapLoopAsync(_cts.Token), CancellationToken.None);
        TryLog($"[{_options.DisplayName}] otlp-tap listening on {TracesEndpoint}"
               + (_options.ForwardBaseUri is null ? "" : $" → {_options.ForwardBaseUri}"));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Binds the listening sockets. <c>localhost</c> takes both loopbacks (an exporter that dials
    /// <c>http://localhost:port</c> resolves <c>::1</c> first on Windows; without the IPv6 socket every
    /// export pays a connect-refused round trip), and <c>+</c>/<c>any</c> takes one dual-mode IPv6 socket,
    /// which serves IPv4 peers too. A secondary bind that fails is logged and skipped, never fatal.
    /// </summary>
    private void Bind()
    {
        var addresses = BindAddresses(_options.ListenHost);
        foreach (var (address, dualMode) in addresses)
        {
            var port = BoundPort == 0 ? _options.ListenPort : BoundPort;
            var listener = new TcpListener(address, port);
            try
            {
                if (dualMode)
                    listener.Server.DualMode = true;
                listener.Start();
            }
            catch (Exception ex) when (ex is SocketException or NotSupportedException)
            {
                if (_listeners.Count == 0)
                    throw;
                TryLog($"[{_options.DisplayName}] otlp-tap could not also bind {address}: {ex.Message}");
                continue;
            }

            BoundPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            _listeners.Add(listener);
        }
    }

    private static (IPAddress Address, bool DualMode)[] BindAddresses(string host)
    {
        var address = ResolveBindAddress(host);
        if (address.Equals(IPAddress.Loopback))
            return Socket.OSSupportsIPv6 ? [(IPAddress.Loopback, false), (IPAddress.IPv6Loopback, false)] : [(IPAddress.Loopback, false)];
        if (address.Equals(IPAddress.Any))
            return Socket.OSSupportsIPv6 ? [(IPAddress.IPv6Any, true)] : [(IPAddress.Any, false)];
        return [(address, false)];
    }

    /// <summary>Resolves the bind address: <c>localhost</c> → loopback, <c>+</c>/<c>*</c>/<c>0.0.0.0</c>/<c>any</c> → every interface, <c>::</c> → every IPv6 interface, anything else parsed or resolved.</summary>
    internal static IPAddress ResolveBindAddress(string host)
    {
        switch (host.Trim().ToLowerInvariant())
        {
            case "localhost" or "127.0.0.1":
                return IPAddress.Loopback;
            case "+" or "*" or "any" or "0.0.0.0":
                return IPAddress.Any;
            case "::" or "[::]" or "::0":
                return IPAddress.IPv6Any;
        }

        if (IPAddress.TryParse(host, out var parsed))
            return parsed;

        var addresses = Dns.GetHostAddresses(host);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
               ?? addresses.FirstOrDefault()
               ?? IPAddress.Loopback;
    }

    private string DisplayHost() => _options.ListenHost.Trim() switch
    {
        "+" or "*" or "any" or "0.0.0.0" => "localhost",
        var other => other,
    };

    // ------------------------------------------------------------------ accept / serve

    private Task AcceptLoopAsync(TcpListener listener, CancellationToken ct) =>
        Accepting.RunAsync(listener, client => _ = Task.Run(() => ServeAsync(client, ct), CancellationToken.None), ct);

    private async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                // Inside the using: were setting it to throw, the client would be left to its finalizer, and its socket held.
                client.NoDelay = true;
                await using var stream = client.GetStream();
                var reader = new HttpRequestReader(stream, _options.MaxRequestBytes);
                while (!ct.IsCancellationRequested)
                {
                    var head = await reader.ReadHeadAsync(ct).ConfigureAwait(false);
                    if (head is null)
                        return;

                    // Refused before anything of the body is read or allocated.
                    if (Refusal(head) is { } refusal)
                    {
                        await WriteResponseAsync(stream, refusal.Status, refusal.Reason, "application/json", Utf8(refusal.Body), keepAlive: false, ct).ConfigureAwait(false);
                        await CloseInStagesAsync(client, reader, head, ct).ConfigureAwait(false);
                        return;
                    }

                    var request = await reader.ReadBodyAsync(head, ct).ConfigureAwait(false);
                    if (request is null)
                        return;

                    if (!await HandleAsync(request, stream, ct).ConfigureAwait(false))
                    {
                        await CloseInStagesAsync(client, reader, unread: null, ct).ConfigureAwait(false);
                        return;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The exporter hung up, or we are shutting down.
        }
        catch (Exception ex)
        {
            TryLog($"[{_options.DisplayName}] otlp-tap connection error: {ex.Message}");
        }
    }

    /// <summary>
    /// Why a request is refused on its head alone, or null: a caller without <see cref="OtlpTapOptions.ExpectedHeaders"/>
    /// first, whatever its size, so it has nothing of its body read or allocated; then a <c>Content-Length</c> that is not
    /// digits (its framing is lost, RFC 9112 section 6.3) or is past what an <c>int</c> holds (over any cap). Counted and
    /// logged here.
    /// </summary>
    private (int Status, string Reason, string Body)? Refusal(HttpRequestHead head)
    {
        if (!IsAuthenticated(head.Headers))
        {
            Interlocked.Increment(ref _unauthenticated);
            TryLog($"[{_options.DisplayName}] otlp-tap rejected an unauthenticated {head.Method} {head.Path}");
            return (401, "Unauthorized", "{\"error\":\"unauthorized\"}");
        }

        switch (head.Framing)
        {
            case BodyFraming.InvalidLength:
                TryLog($"[{_options.DisplayName}] otlp-tap refused a {head.Method} {head.Path} whose Content-Length is not a number");
                return (400, "Bad Request", "{\"error\":\"otlp-tap: Content-Length must be digits alone\"}");
            case BodyFraming.LengthPastInt:
                Interlocked.Increment(ref _rejected);
                TryLog($"[{_options.DisplayName}] otlp-tap refused an export over MaxRequestBytes ({_options.MaxRequestBytes})");
                return (413, "Payload Too Large", "{\"error\":\"payload too large\"}");
            default:
                return null;
        }
    }

    /// <summary>
    /// Closes a connection whose answer is written, in stages: a FIN, then whatever the peer still sends read and thrown
    /// away until it ends or the declared body has come, giving up after <see cref="LingerQuiet"/> without a byte or
    /// <see cref="LingerTotal"/> in all, and only then the close. Closed at once, bytes the peer had sent and the tap had
    /// not read would turn the close into a reset, and the peer would lose an answer it had not read yet.
    /// </summary>
    private static async Task CloseInStagesAsync(TcpClient client, HttpRequestReader reader, HttpRequestHead? unread, CancellationToken ct)
    {
        try
        {
            client.Client.Shutdown(SocketShutdown.Send);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            return; // the peer is gone already
        }

        var declared = unread is { Framing: BodyFraming.ContentLength } head ? head.ContentLength : (long?)null;
        await reader.LingerAsync(declared, LingerQuiet, LingerTotal, ct).ConfigureAwait(false);
    }

    // A connection closed in stages lingers for what its peer still sends: 5 s without a byte, 30 s in all (nginx's
    // lingering_timeout and lingering_time), on the real clock.
    private static readonly TimeSpan LingerQuiet = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LingerTotal = TimeSpan.FromSeconds(30);

    private async Task<bool> HandleAsync(HttpRequestMessageData request, Stream stream, CancellationToken ct)
    {
        if (request.TooLarge)
        {
            Interlocked.Increment(ref _rejected);
            TryLog($"[{_options.DisplayName}] otlp-tap refused an export over MaxRequestBytes ({_options.MaxRequestBytes})");
            await WriteResponseAsync(stream, 413, "Payload Too Large", "application/json", Utf8("{\"error\":\"payload too large\"}"), request.KeepAlive, ct).ConfigureAwait(false);
            return request.KeepAlive;
        }

        var isTraces = request.Path.Equals(_options.TracesPath, StringComparison.OrdinalIgnoreCase)
                       && request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);

        if (isTraces)
            Interlocked.Increment(ref _requests);

        // Queue, then answer, then map: an answered export is in the queue, which disposing drains, so a tap disposed
        // the moment the exporter has its answer still maps it. Queuing never waits (TryWrite), so the exporter never
        // waits for Kronikol; mapping happens on the worker.
        if (isTraces && BeforeQueued is { } beforeQueued)
            await beforeQueued().ConfigureAwait(false);

        if (isTraces && request.Body.Length > 0 && !_queue.Writer.TryWrite(new Payload(request.Body, request.ContentType, request.ContentEncoding)))
        {
            Interlocked.Increment(ref _dropped);
            TryLog($"[{_options.DisplayName}] otlp-tap dropped an export payload "
                   + (Volatile.Read(ref _disposed) == 1 ? "(the tap is shutting down)" : "(queue full)"));
        }

        if (_options.ForwardBaseUri is not null)
        {
            await ForwardAsync(request, stream, ct).ConfigureAwait(false);
        }
        else if (isTraces)
        {
            var json = request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
            await WriteResponseAsync(stream, 200, "OK",
                json ? OtlpTraceReader.JsonContentType : OtlpTraceReader.ProtobufContentType,
                // An ExportTraceServiceResponse with no partial_success is the empty message: zero bytes.
                json ? Utf8("{}") : [],
                request.KeepAlive, ct).ConfigureAwait(false);
        }
        else
        {
            await WriteResponseAsync(stream, 404, "Not Found", "application/json", Utf8("{\"error\":\"not found\"}"), request.KeepAlive, ct).ConfigureAwait(false);
        }

        if (AfterResponseWritten is { } afterResponse)
            await afterResponse().ConfigureAwait(false);

        return request.KeepAlive;
    }

    // Awaited by an export's handler just before it queues the payload, and once its response is written
    // (Kronikol.Tests.Otlp): a fact disposes the tap at either point.
    internal Func<Task>? BeforeQueued { get; set; }

    internal Func<Task>? AfterResponseWritten { get; set; }

    private bool IsAuthenticated(IReadOnlyDictionary<string, string> headers)
    {
        if (_options.ExpectedHeaders.Count == 0)
            return true;

        foreach (var (name, expected) in _options.ExpectedHeaders)
        {
            if (!headers.TryGetValue(name, out var actual) || !string.Equals(actual, expected, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The path and query a request target forwards with: the forward always goes to
    /// <see cref="OtlpTapOptions.ForwardBaseUri"/>'s scheme and authority, whatever host the target names. Null for a
    /// target with no path to forward (the asterisk form, the authority form, a target with no leading slash).
    /// </summary>
    internal static string? OriginForm(string target)
    {
        // A path first: on Linux "/v1/traces" also parses as an absolute URI (file:///v1/traces).
        if (target.StartsWith('/'))
            return target;
        return Uri.TryCreate(target, UriKind.Absolute, out var absolute) && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
            ? absolute.PathAndQuery
            : null;
    }

    private async Task ForwardAsync(HttpRequestMessageData request, Stream stream, CancellationToken ct)
    {
        // Never resolved against the base: a target starting with "//" would name a host of its own.
        if (OriginForm(request.Target) is not { } origin)
        {
            TryLog($"[{_options.DisplayName}] otlp-tap refused a request target with no path to forward: {request.Method} {request.Target}");
            await WriteResponseAsync(stream, 400, "Bad Request", "application/json",
                Utf8("{\"error\":\"otlp-tap: the request target must be a path or an http URI\"}"), request.KeepAlive, ct).ConfigureAwait(false);
            return;
        }

        int status;
        string reason;
        byte[] body;
        string? contentType;
        string? contentEncoding;
        try
        {
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), new Uri(_options.ForwardBaseUri!.GetLeftPart(UriPartial.Authority) + origin));
            if (request.Body.Length > 0)
                message.Content = new ByteArrayContent(request.Body);

            foreach (var (name, value) in request.Headers)
            {
                if (HopByHop.Contains(name))
                    continue;
                if (!message.Headers.TryAddWithoutValidation(name, value))
                    message.Content?.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await _upstream!.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            status = (int)response.StatusCode;
            reason = response.ReasonPhrase ?? "OK";
            contentType = response.Content.Headers.ContentType?.ToString();
            contentEncoding = response.Content.Headers.ContentEncoding.Count > 0
                ? string.Join(",", response.Content.Headers.ContentEncoding)
                : null;
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or IOException or SocketException) && !ct.IsCancellationRequested)
        {
            // The collector was unreachable or did not answer in time. A cancellation of the tap's own token is not that:
            // the tap is stopping, and the exception ends the connection uncounted and unanswered (ServeAsync).
            Interlocked.Increment(ref _forwardFailures);
            TryLog($"[{_options.DisplayName}] otlp-tap forward failed: {ex.Message}");
            await WriteResponseAsync(stream, 502, "Bad Gateway", "application/json",
                Utf8("{\"error\":\"otlp-tap: upstream collector unreachable\"}"), request.KeepAlive, ct).ConfigureAwait(false);
            return;
        }

        // The collector has the export. A failure from here on is the exporter's, which hung up before its answer, and
        // falls to ServeAsync's hang-up catch.
        await WriteResponseAsync(stream, status, reason, contentType, body, request.KeepAlive, ct, contentEncoding).ConfigureAwait(false);
    }

    private static async Task WriteResponseAsync(
        Stream stream, int status, string reason, string? contentType, byte[] body, bool keepAlive, CancellationToken ct, string? contentEncoding = null)
    {
        var head = new StringBuilder(160);
        head.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(reason).Append("\r\n");
        if (contentType is not null)
            head.Append("Content-Type: ").Append(contentType).Append("\r\n");
        if (contentEncoding is not null)
            head.Append("Content-Encoding: ").Append(contentEncoding).Append("\r\n");
        head.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        head.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n\r\n");

        await stream.WriteAsync(Utf8(head.ToString()), ct).ConfigureAwait(false);
        if (body.Length > 0)
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    // ------------------------------------------------------------------ mapping worker

    private async Task MapLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var payload))
                    MapPayload(payload);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — drain what is already queued so nothing captured is lost.
        }

        while (_queue.Reader.TryRead(out var remaining))
            MapPayload(remaining);
    }

    private void MapPayload(Payload payload)
    {
        try
        {
            var bytes = Decompress(payload.Body, payload.ContentEncoding, _options.MaxRequestBytes);
            var spans = OtlpTraceReader.Read(bytes, payload.ContentType);
            Interlocked.Add(ref _spansReceived, spans.Count);

            foreach (var span in spans)
            {
                var mapped = SpanToInteractionMapper.Map(span, _options);
                if (mapped is null)
                {
                    Interlocked.Increment(ref _spansIgnored);
                    continue;
                }

                var (request, response) = mapped.ToLogs(_options.Phase);
                _options.Sink.Log(request);
                _options.Sink.Log(response);
                Interlocked.Increment(ref _spansMapped);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failed);
            TryLog($"[{_options.DisplayName}] otlp-tap could not map an export payload: {ex.Message}");
        }
    }

    /// <summary>Decodes a gzip/deflate <c>Content-Encoding</c>; anything else (including none) is returned as-is.</summary>
    internal static byte[] Decompress(byte[] body, string? contentEncoding, int cap)
    {
        var encoding = contentEncoding?.Trim().ToLowerInvariant();
        if (encoding is null or "" or "identity")
            return body;

        try
        {
            using var source = new MemoryStream(body);
            Stream decompressor = encoding switch
            {
                "gzip" or "x-gzip" => new GZipStream(source, CompressionMode.Decompress),
                "deflate" => new DeflateStream(source, CompressionMode.Decompress),
                "br" => new BrotliStream(source, CompressionMode.Decompress),
                _ => Stream.Null,
            };

            if (ReferenceEquals(decompressor, Stream.Null))
                return body;

            using (decompressor)
            {
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = decompressor.Read(chunk, 0, chunk.Length)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > cap)
                        break;
                }

                return buffer.ToArray();
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return body;
        }
    }

    /// <summary>Stops listening, drains what is queued and releases the upstream client.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _queue.Writer.TryComplete();

        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);

        foreach (var listener in _listeners)
        {
            try
            {
                listener.Stop();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
            {
                // Already closed.
            }
        }

        foreach (var task in new[] { _acceptLoop, _mapLoop })
        {
            if (task is null)
                continue;
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        _upstream?.Dispose();
        _cts?.Dispose();
        TryLog(
            $"[{_options.DisplayName}] otlp-tap stopped ({RequestsReceived} exports, {SpansReceived} spans, {SpansMapped} mapped, "
            + $"{SpansIgnored} ignored, {PayloadsDropped} dropped, {PayloadsRejected} rejected, {PayloadsFailed} failed, "
            + $"{UnauthenticatedRequests} unauthenticated, {ForwardFailures} forward failures, "
            + $"{AcceptFailures} failed accepts)");
    }

    /// <summary>
    /// Writes a line through <see cref="OtlpTapOptions.Log"/>. The callback is the host's code, and nothing it throws
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

    private readonly record struct Payload(byte[] Body, string? ContentType, string? ContentEncoding);
}
