using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.ProxyTap;

/// <summary>
/// A transparent HTTP tee for Kronikol's <em>proxy-tap topology</em>: listens on a port, forwards every
/// request byte-for-byte to a real service, and records a copy of the exchange as a Kronikol
/// request/response pair attributed to the running test. Nothing inside the services needs to change —
/// the browser/test fixture stamps the <c>test-tracking-*</c> headers (or a W3C <c>traceparent</c>), the
/// identity rides the real request through the services, and the tap on each hop is the sink.
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

    /// <summary>Whether the listener is accepting connections.</summary>
    public bool IsListening => _listener.IsListening;

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
        })
        {
            BaseAddress = _options.ForwardBaseUri,
            Timeout = _options.ForwardTimeout + TimeSpan.FromSeconds(10),
        };

        _listener.Prefixes.Add(ListenUri.ToString());
        _listener.Start();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
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
        try
        {
            await HandleAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            // The caller hung up; nothing to answer.
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke($"[{_options.DisplayName}] proxy-tap error: {ex.Message}");
            TryRespondBadGateway(context, ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _inflight);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var pathAndQuery = request.Url?.PathAndQuery ?? "/";
        var requestHeaders = ReadHeaders(request.Headers);
        var inboundTraceparent = TraceParent.TryParse(request.Headers["traceparent"]);
        var requestStarted = DateTimeOffset.UtcNow;

        byte[] requestBody = [];
        if (request.HasEntityBody)
        {
            using var buffer = new MemoryStream();
            await request.InputStream.CopyToAsync(buffer).ConfigureAwait(false);
            requestBody = buffer.ToArray();
        }

        // ---- identity --------------------------------------------------------------------------
        var identity = ResolveIdentity(requestHeaders, inboundTraceparent);
        var ignore = requestHeaders.ContainsKey(TestTrackingHttpHeaders.Ignore);
        var capture = identity is not null || _options.CaptureUnattributedRequests;
        identity ??= capture ? (_options.FallbackTestName, _options.FallbackTestId ?? Guid.NewGuid().ToString("N")) : null;

        var traceIdHeader = FirstValue(requestHeaders, TestTrackingHttpHeaders.TraceIdHeader);
        var kronikolTraceId = traceIdHeader is not null && Guid.TryParse(traceIdHeader, out var parsedTrace) ? parsedTrace : Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        // ---- spans + traceparent ----------------------------------------------------------------
        Activity? server = null;
        Activity? client = null;
        if (_options.EmitActivities)
        {
            server = inboundTraceparent is not null && ActivityContext.TryParse(inboundTraceparent.ToString(), null, isRemote: true, out var parent)
                ? ActivitySource.StartActivity($"tap {_options.DisplayName}", ActivityKind.Server, parent)
                : ActivitySource.StartActivity($"tap {_options.DisplayName}", ActivityKind.Server);
            server?.SetTag("kronikol.caller", _options.CallerName);
            server?.SetTag("kronikol.service", _options.ServiceName);
            server?.SetTag("http.request.method", request.HttpMethod);
            server?.SetTag("url.path", request.Url?.AbsolutePath);
            if (identity is not null)
            {
                server?.SetTag("kronikol.test.name", identity.Value.Name);
                server?.SetTag("kronikol.test.id", identity.Value.Id);
            }

            client = ActivitySource.StartActivity("forward", ActivityKind.Client);
        }

        string? forwardedTraceparent;
        string activityTraceId;
        string activitySpanId;
        if (client is not null)
        {
            forwardedTraceparent = client.Id;
            activityTraceId = client.TraceId.ToString();
            activitySpanId = client.SpanId.ToString();
        }
        else if (inboundTraceparent is not null)
        {
            forwardedTraceparent = null; // copied verbatim below — transparent when nobody listens
            activityTraceId = inboundTraceparent.TraceId;
            activitySpanId = inboundTraceparent.ParentSpanId;
        }
        else if (_options.SynthesizeTraceparent)
        {
            activityTraceId = ActivityTraceId.CreateRandom().ToString();
            activitySpanId = ActivitySpanId.CreateRandom().ToString();
            forwardedTraceparent = TraceParent.Format(activityTraceId, activitySpanId);
        }
        else
        {
            activityTraceId = ActivityTraceId.CreateRandom().ToString();
            activitySpanId = ActivitySpanId.CreateRandom().ToString();
            forwardedTraceparent = null;
        }

        // ---- forward ---------------------------------------------------------------------------
        var lifetime = _cts?.Token ?? CancellationToken.None;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        deadline.CancelAfter(_options.ForwardTimeout);

        using var message = new HttpRequestMessage(new HttpMethod(request.HttpMethod), pathAndQuery);
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
        await RespondAsync(context, status, responseHeaders, responseContentType, responseContentEncoding, responseBody).ConfigureAwait(false);
        Interlocked.Increment(ref _handled);

        if (!capture || identity is null)
            return;

        Record(identity.Value, ignore, kronikolTraceId, requestResponseId, request.HttpMethod, pathAndQuery,
            requestHeaders, requestBody, request.Headers["Content-Encoding"], requestStarted,
            status, responseHeaders, responseBody, responseContentEncoding, responseReceived,
            activityTraceId, activitySpanId);
        Interlocked.Increment(ref _captured);
    }

    // ------------------------------------------------------------------ identity

    internal (string Name, string Id)? ResolveIdentity(IReadOnlyDictionary<string, string[]> headers, TraceParent? traceparent)
    {
        if (_options.IdentityResolver is not null)
        {
            var custom = _options.IdentityResolver(headers, traceparent);
            if (custom is not null)
                return custom;
        }

        var name = FirstValue(headers, TestTrackingHttpHeaders.CurrentTestNameHeader);
        foreach (var fallback in _options.TestNameHeaderFallbacks)
        {
            if (name is not null) break;
            name = FirstValue(headers, fallback);
        }

        var id = FirstValue(headers, TestTrackingHttpHeaders.CurrentTestIdHeader);
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
            message.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CurrentTestNameHeader, HeaderSafe(identity.Name));
        if (!inbound.ContainsKey(TestTrackingHttpHeaders.CurrentTestIdHeader))
            message.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CurrentTestIdHeader, HeaderSafe(identity.Id));
        if (!inbound.ContainsKey(TestTrackingHttpHeaders.CallerNameHeader))
            message.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CallerNameHeader, HeaderSafe(_options.ServiceName));
        if (!inbound.ContainsKey(TestTrackingHttpHeaders.TraceIdHeader))
            message.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.TraceIdHeader, traceId.ToString());
    }

    /// <summary>ISO-8859-1-safe, bounded header value.</summary>
    internal static string HeaderSafe(string value)
    {
        var sb = new StringBuilder(Math.Min(value.Length, 512));
        foreach (var c in value)
        {
            if (sb.Length >= 512) break;
            sb.Append(c is >= (char)0x20 and <= (char)0x7e ? c : '?');
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------ capture

    private void Record(
        (string Name, string Id) identity, bool ignore, Guid traceId, Guid requestResponseId,
        string method, string pathAndQuery,
        IReadOnlyDictionary<string, string[]> requestHeaders, byte[] requestBody, string? requestEncoding, DateTimeOffset requestStarted,
        int status, (string Key, string? Value)[] responseHeaders, byte[] responseBody, string? responseEncoding, DateTimeOffset responseReceived,
        string activityTraceId, string activitySpanId)
    {
        var uri = new Uri(_options.ForwardBaseUri!, pathAndQuery);
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
        HttpListenerContext context, int status, (string Key, string? Value)[] headers, string? contentType, string? contentEncoding, byte[] body)
    {
        try
        {
            context.Response.StatusCode = status;
            foreach (var (key, value) in headers)
            {
                if (value is null || HopByHop.Contains(key) || key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) || key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    context.Response.Headers.Add(key, value);
                }
                catch (ArgumentException)
                {
                    // Restricted header (e.g. Date/Server are owned by HttpListener) — skip.
                }
            }

            if (contentType is not null)
                context.Response.ContentType = contentType;
            if (contentEncoding is not null)
                context.Response.Headers["Content-Encoding"] = contentEncoding;

            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // The client hung up; the exchange is still recorded.
        }
    }

    private static void TryRespondBadGateway(HttpListenerContext context, string message)
    {
        try
        {
            context.Response.StatusCode = 502;
            var payload = Encoding.UTF8.GetBytes(
                "{\"error\":{\"code\":502,\"message\":\"proxy-tap: " + message.Replace("\\", "\\\\").Replace("\"", "'") + "\"}}");
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = payload.Length;
            context.Response.OutputStream.Write(payload);
            context.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // Nothing left to tell.
        }
    }

    /// <summary>Stops listening, drains in-flight exchanges briefly (≤ 2 s) so their captures land, and releases the upstream client.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            if (_listener.IsListening)
                _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already closed.
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
