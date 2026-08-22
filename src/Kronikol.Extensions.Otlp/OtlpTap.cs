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
    }

    /// <summary>The options this tap runs with.</summary>
    public OtlpTapOptions Options => _options;

    /// <summary>The port actually bound (equals <c>ListenPort</c> unless that was 0). Zero before <see cref="StartAsync"/>.</summary>
    public int BoundPort { get; private set; }

    /// <summary>The endpoint an exporter should be pointed at once the tap is listening.</summary>
    public Uri TracesEndpoint => new($"http://{DisplayHost()}:{(BoundPort == 0 ? _options.ListenPort : BoundPort)}{_options.TracesPath}");

    /// <summary>Whether the listener is accepting connections.</summary>
    public bool IsListening => _listeners.Count > 0 && Volatile.Read(ref _disposed) == 0;

    /// <summary>Authenticated export requests accepted so far.</summary>
    public long RequestsReceived => Interlocked.Read(ref _requests);

    /// <summary>Requests rejected with <c>401</c> because <see cref="OtlpTapOptions.ExpectedHeaders"/> was not satisfied.</summary>
    public long UnauthenticatedRequests => Interlocked.Read(ref _unauthenticated);

    /// <summary>Requests whose forward to <see cref="OtlpTapOptions.ForwardBaseUri"/> failed (answered <c>502</c>).</summary>
    public long ForwardFailures => Interlocked.Read(ref _forwardFailures);

    /// <summary>Export payloads dropped because the mapping queue was full (D3: capture never blocks).</summary>
    public long PayloadsDropped => Interlocked.Read(ref _dropped);

    /// <summary>Export requests refused with <c>413</c> because the body exceeded <see cref="OtlpTapOptions.MaxRequestBytes"/>; their spans were never read.</summary>
    public long PayloadsRejected => Interlocked.Read(ref _rejected);

    /// <summary>Accepted payloads whose decode, span mapping or sink write threw (a sink that cannot write, an unexpected span shape); their spans are lost.</summary>
    public long PayloadsFailed => Interlocked.Read(ref _failed);

    /// <summary>Spans decoded from accepted payloads.</summary>
    public long SpansReceived => Interlocked.Read(ref _spansReceived);

    /// <summary>Spans that became interactions.</summary>
    public long SpansMapped => Interlocked.Read(ref _spansMapped);

    /// <summary>Spans decoded but not captured (server/internal spans, excluded kinds, unrecognised shapes).</summary>
    public long SpansIgnored => Interlocked.Read(ref _spansIgnored);

    /// <summary>
    /// Capture health as report diagnostics: one <see cref="DiagnosticKind.CaptureDegraded"/> entry per
    /// non-zero problem counter — payloads dropped, rejected or failed, exports refused as
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

        var dropped = PayloadsDropped;
        if (dropped > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: {dropped:N0} export payload(s) dropped because the mapping queue was full (QueueCapacity {_options.QueueCapacity}) — their spans are missing from the diagrams; the exporter was never delayed"));

        var rejected = PayloadsRejected;
        if (rejected > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: {rejected:N0} export request(s) refused with 413 Payload Too Large (MaxRequestBytes {_options.MaxRequestBytes:N0}) — their spans were never read"));

        var failed = PayloadsFailed;
        if (failed > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: {failed:N0} export payload(s) failed while being decoded, mapped or written to the sink — their spans are lost (see the tap's log for the exception)"));

        var unauthenticated = UnauthenticatedRequests;
        if (unauthenticated > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: {unauthenticated:N0} export request(s) rejected with 401 — ExpectedHeaders not satisfied; if that was your exporter, its spans never reached the report"));

        var forwardFailures = ForwardFailures;
        if (forwardFailures > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: {forwardFailures:N0} export(s) could not be forwarded to {_options.ForwardBaseUri} and were answered 502 Bad Gateway — the spans were still mapped here, but the real collector never saw them"));

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
        _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap listening on {TracesEndpoint}"
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
                _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap could not also bind {address}: {ex.Message}");
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

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            client.NoDelay = true;
            using (client)
            {
                await using var stream = client.GetStream();
                var reader = new HttpRequestReader(stream, _options.MaxRequestBytes);
                while (!ct.IsCancellationRequested)
                {
                    var request = await reader.ReadAsync(ct).ConfigureAwait(false);
                    if (request is null)
                        return;

                    var keepAlive = await HandleAsync(request, stream, ct).ConfigureAwait(false);
                    if (!keepAlive)
                        return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The exporter hung up, or we are shutting down.
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap connection error: {ex.Message}");
        }
    }

    private async Task<bool> HandleAsync(HttpRequestMessageData request, Stream stream, CancellationToken ct)
    {
        if (request.TooLarge)
        {
            Interlocked.Increment(ref _rejected);
            _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap refused an export over MaxRequestBytes ({_options.MaxRequestBytes})");
            await WriteResponseAsync(stream, 413, "Payload Too Large", "application/json", Utf8("{\"error\":\"payload too large\"}"), request.KeepAlive, ct).ConfigureAwait(false);
            return request.KeepAlive;
        }

        if (!IsAuthenticated(request))
        {
            Interlocked.Increment(ref _unauthenticated);
            _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap rejected an unauthenticated {request.Method} {request.Path}");
            await WriteResponseAsync(stream, 401, "Unauthorized", "application/json", Utf8("{\"error\":\"unauthorized\"}"), request.KeepAlive, ct).ConfigureAwait(false);
            return request.KeepAlive;
        }

        var isTraces = request.Path.Equals(_options.TracesPath, StringComparison.OrdinalIgnoreCase)
                       && request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);

        if (isTraces)
            Interlocked.Increment(ref _requests);

        // Answer first, map second — the exporter must never wait for Kronikol.
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

        if (isTraces && request.Body.Length > 0 && !_queue.Writer.TryWrite(new Payload(request.Body, request.ContentType, request.ContentEncoding)))
        {
            Interlocked.Increment(ref _dropped);
            _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap dropped an export payload (queue full)");
        }

        return request.KeepAlive;
    }

    private bool IsAuthenticated(HttpRequestMessageData request)
    {
        if (_options.ExpectedHeaders.Count == 0)
            return true;

        foreach (var (name, expected) in _options.ExpectedHeaders)
        {
            if (!request.Headers.TryGetValue(name, out var actual) || !string.Equals(actual, expected, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private async Task ForwardAsync(HttpRequestMessageData request, Stream stream, CancellationToken ct)
    {
        try
        {
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Target);
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
            var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.ToString();
            var contentEncoding = response.Content.Headers.ContentEncoding.Count > 0
                ? string.Join(",", response.Content.Headers.ContentEncoding)
                : null;
            await WriteResponseAsync(stream, (int)response.StatusCode, response.ReasonPhrase ?? "OK", contentType, body,
                request.KeepAlive, ct, contentEncoding).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or SocketException)
        {
            Interlocked.Increment(ref _forwardFailures);
            _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap forward failed: {ex.Message}");
            await WriteResponseAsync(stream, 502, "Bad Gateway", "application/json",
                Utf8("{\"error\":\"otlp-tap: upstream collector unreachable\"}"), request.KeepAlive, ct).ConfigureAwait(false);
        }
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
            _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap could not map an export payload: {ex.Message}");
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
        _options.Log?.Invoke(
            $"[{_options.DisplayName}] otlp-tap stopped ({RequestsReceived} exports, {SpansReceived} spans, {SpansMapped} mapped, "
            + $"{SpansIgnored} ignored, {PayloadsDropped} dropped, {UnauthenticatedRequests} unauthenticated, {ForwardFailures} forward failures)");
    }

    private readonly record struct Payload(byte[] Body, string? ContentType, string? ContentEncoding);
}
