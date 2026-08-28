using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Constants;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kronikol.Tests.ProxyTap;

/// <summary>A stub upstream that records what actually arrived on the wire and answers a canned reply.</summary>
internal sealed class StubUpstream : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<HttpListenerRequest, byte[], (int Status, byte[] Body, string? ContentType, string? ContentEncoding, Dictionary<string, string>? Headers)> _reply;
    public readonly List<(string Method, string Path, Dictionary<string, string> Headers, string Body)> Seen = [];

    public StubUpstream(Func<HttpListenerRequest, byte[], (int, byte[], string?, string?, Dictionary<string, string>?)> reply)
    {
        _reply = reply;
        Port = FreePort();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    public int Port { get; }
    public Uri BaseUri => new($"http://localhost:{Port}");

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }

            try
            {
                using var buffer = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(buffer);
                var body = buffer.ToArray();
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in context.Request.Headers.AllKeys)
                    if (key is not null) headers[key] = context.Request.Headers[key]!;
                lock (Seen) Seen.Add((context.Request.HttpMethod, context.Request.Url!.PathAndQuery, headers, Encoding.UTF8.GetString(body)));

                var (status, reply, contentType, encoding, extra) = _reply(context.Request, body);
                context.Response.StatusCode = status;
                if (contentType is not null) context.Response.ContentType = contentType;
                if (encoding is not null) context.Response.Headers["Content-Encoding"] = encoding;
                if (extra is not null) foreach (var (k, v) in extra) context.Response.Headers[k] = v;
                context.Response.ContentLength64 = reply.Length;
                await context.Response.OutputStream.WriteAsync(reply);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                // client gone
            }
        }
    }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public ValueTask DisposeAsync()
    {
        try { _listener.Stop(); _listener.Close(); } catch (ObjectDisposedException) { }
        return ValueTask.CompletedTask;
    }
}

/// <summary>Collects logs for one test without touching the global store.</summary>
internal sealed class ListSink : IRequestResponseSink
{
    public readonly List<RequestResponseLog> Logs = [];
    public void Log(RequestResponseLog log) { lock (Logs) Logs.Add(log); }
}

public class ProxyTapTests
{
    private static (int Status, byte[] Body, string? ContentType, string? ContentEncoding, Dictionary<string, string>? Headers) JsonOk(HttpListenerRequest _, byte[] __) =>
        (200, Encoding.UTF8.GetBytes("""{"data":{"ok":true}}"""), "application/json", null, new Dictionary<string, string> { ["X-Upstream"] = "yes" });

    private static ProxyTapOptions Options(StubUpstream upstream, IRequestResponseSink sink, Action<ProxyTapOptions>? configure = null)
    {
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = upstream.BaseUri,
            CallerName = "web",
            ServiceName = "graphql",
            Sink = sink,
        };
        configure?.Invoke(options);
        return options;
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri listen, string path, string? body, params (string, string)[] headers)
    {
        var request = new HttpRequestMessage(method, new Uri(listen, path));
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        foreach (var (k, v) in headers)
            request.Headers.TryAddWithoutValidation(k, v);
        return request;
    }

    [Fact]
    public async Task Forwards_byte_for_byte_and_records_a_request_response_pair_attributed_by_headers()
    {
        await using var upstream = new StubUpstream(JsonOk);
        var sink = new ListSink();
        var options = Options(upstream, sink);
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(options);
        await tap.StartAsync();

        using var client = new HttpClient();
        var testId = Guid.NewGuid().ToString("N");
        using var request = Request(HttpMethod.Post, tap.ListenUri, "/sidekick?op=1", """{"query":"query Overview { overview }"}""",
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "overview renders"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, testId),
            ("X-Correlation-Id", "corr-1"));
        var response = await client.SendAsync(request);

        // Transparent forwarding.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"data":{"ok":true}}""", await response.Content.ReadAsStringAsync());
        Assert.Equal("yes", response.Headers.GetValues("X-Upstream").Single());
        var seen = Assert.Single(upstream.Seen);
        Assert.Equal("/sidekick?op=1", seen.Path);
        Assert.Equal("""{"query":"query Overview { overview }"}""", seen.Body);
        Assert.Equal("corr-1", seen.Headers["X-Correlation-Id"]);
        Assert.Equal($"localhost:{upstream.Port}", seen.Headers["Host"]); // hop-by-hop Host rewritten to the upstream

        // Capture.
        await tap.DisposeAsync();
        Assert.Equal(2, sink.Logs.Count);
        var req = sink.Logs[0];
        var resp = sink.Logs[1];
        Assert.Equal(RequestResponseType.Request, req.Type);
        Assert.Equal(RequestResponseType.Response, resp.Type);
        Assert.Equal(testId, req.TestId);
        Assert.Equal("overview renders", req.TestName);
        Assert.Equal("web", req.CallerName);
        Assert.Equal("graphql", req.ServiceName);
        Assert.Equal(HttpMethod.Post, req.Method.Value);
        Assert.Equal("/sidekick?op=1", req.Uri.PathAndQuery);
        Assert.Equal(upstream.Port, req.Uri.Port);
        Assert.Equal(req.RequestResponseId, resp.RequestResponseId);
        Assert.Equal(req.TraceId, resp.TraceId);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode!.Value);
        Assert.Contains("query Overview", req.Content!);
        Assert.Equal("""{"data":{"ok":true}}""", resp.Content);
        Assert.Contains(req.Headers, h => h.Key.Equals("X-Correlation-Id", StringComparison.OrdinalIgnoreCase) && h.Value == "corr-1");
        Assert.Contains(resp.Headers, h => h.Key == "X-Upstream" && h.Value == "yes");
        Assert.False(req.TrackingIgnore);
        Assert.NotNull(req.Timestamp);
        Assert.True(resp.Timestamp >= req.Timestamp);
        Assert.Equal(1, tap.RequestsCaptured);
    }

    [Fact]
    public async Task Reinjects_the_four_correlation_headers_when_missing_and_preserves_them_when_present()
    {
        await using var upstream = new StubUpstream(JsonOk);
        var sink = new ListSink();
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, sink));
        await tap.StartAsync();
        using var client = new HttpClient();

        // Only name+id inbound → caller name and trace id are added.
        using (var request = Request(HttpMethod.Get, tap.ListenUri, "/a", null,
                   (TestTrackingHttpHeaders.CurrentTestNameHeader, "T"), (TestTrackingHttpHeaders.CurrentTestIdHeader, "id-1")))
            await client.SendAsync(request);

        var first = upstream.Seen[0].Headers;
        Assert.Equal("T", first[TestTrackingHttpHeaders.CurrentTestNameHeader]);
        Assert.Equal("id-1", first[TestTrackingHttpHeaders.CurrentTestIdHeader]);
        Assert.Equal("graphql", first[TestTrackingHttpHeaders.CallerNameHeader]); // the next hop's caller is this tap's service
        Assert.True(Guid.TryParse(first[TestTrackingHttpHeaders.TraceIdHeader], out _));

        // All four inbound → forwarded verbatim, and the log's TraceId is the inbound one.
        var traceId = Guid.NewGuid();
        using (var request = Request(HttpMethod.Get, tap.ListenUri, "/b", null,
                   (TestTrackingHttpHeaders.CurrentTestNameHeader, "T"), (TestTrackingHttpHeaders.CurrentTestIdHeader, "id-2"),
                   (TestTrackingHttpHeaders.CallerNameHeader, "upstream-caller"), (TestTrackingHttpHeaders.TraceIdHeader, traceId.ToString())))
            await client.SendAsync(request);

        var second = upstream.Seen[1].Headers;
        Assert.Equal("upstream-caller", second[TestTrackingHttpHeaders.CallerNameHeader]);
        Assert.Equal(traceId.ToString(), second[TestTrackingHttpHeaders.TraceIdHeader]);
        await tap.DisposeAsync();
        Assert.Equal(traceId, sink.Logs.Last().TraceId);
    }

    [Fact]
    public async Task Falls_back_to_the_traceparent_trace_id_as_test_id_and_forwards_the_same_trace()
    {
        await using var upstream = new StubUpstream(JsonOk);
        var sink = new ListSink();
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, sink));
        await tap.StartAsync();
        using var client = new HttpClient();
        const string traceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        using (var request = Request(HttpMethod.Get, tap.ListenUri, "/c", null, ("traceparent", traceparent)))
            await client.SendAsync(request);

        Assert.Contains("0af7651916cd43dd8448eb211c80319c", upstream.Seen.Single().Headers["traceparent"]);
        await tap.DisposeAsync();
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", sink.Logs[0].TestId);
        Assert.Equal(TestIdentityScope.UnknownTestName, sink.Logs[0].TestName);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", sink.Logs[0].ActivityTraceId);
        // Re-injection stamps the resolved id so the next hop can attribute even without traceparent support.
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", upstream.Seen.Single().Headers[TestTrackingHttpHeaders.CurrentTestIdHeader]);
    }

    [Fact]
    public async Task Requests_without_identity_are_forwarded_but_not_captured_unless_opted_in()
    {
        await using var upstream = new StubUpstream(JsonOk);
        var sink = new ListSink();
        await using (var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, sink, o => o.SynthesizeTraceparent = false)))
        {
            await tap.StartAsync();
            using var client = new HttpClient();
            var response = await client.GetAsync(new Uri(tap.ListenUri, "/health"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Empty(sink.Logs);

        await using (var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, sink, o =>
        {
            o.CaptureUnattributedRequests = true;
            o.FallbackTestId = "session";
            o.FallbackTestName = "Traffic outside any test";
        })))
        {
            await tap.StartAsync();
            using var client = new HttpClient();
            await client.GetAsync(new Uri(tap.ListenUri, "/health"));
            await client.GetAsync(new Uri(tap.ListenUri, "/health"));
        }

        Assert.Equal(4, sink.Logs.Count);
        Assert.All(sink.Logs, l => Assert.Equal("session", l.TestId));
        Assert.All(sink.Logs, l => Assert.Equal("Traffic outside any test", l.TestName));
    }

    [Fact]
    public async Task Honours_the_ignore_header_and_alternative_identity_headers()
    {
        await using var upstream = new StubUpstream(JsonOk);
        var sink = new ListSink();
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, sink, o =>
        {
            o.TestNameHeaderFallbacks.Add("x-my-test");
            o.TestIdHeaderFallbacks.Add("x-my-test-id");
        }));
        await tap.StartAsync();
        using var client = new HttpClient();

        using (var request = Request(HttpMethod.Get, tap.ListenUri, "/d", null, ("x-my-test", "legacy name"), ("x-my-test-id", "legacy-id"), (TestTrackingHttpHeaders.Ignore, "true")))
            await client.SendAsync(request);

        await tap.DisposeAsync();
        Assert.Equal("legacy name", sink.Logs[0].TestName);
        Assert.Equal("legacy-id", sink.Logs[0].TestId);
        Assert.True(sink.Logs[0].TrackingIgnore);
        // Ignored requests do not get correlation re-injected.
        Assert.False(upstream.Seen.Single().Headers.ContainsKey(TestTrackingHttpHeaders.CurrentTestIdHeader));
    }

    [Fact]
    public async Task Header_policy_redacts_secrets_by_default_and_whitelist_keeps_only_named_headers()
    {
        await using var upstream = new StubUpstream(JsonOk);
        var sink = new ListSink();
        await using (var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, sink)))
        {
            await tap.StartAsync();
            using var client = new HttpClient();
            using var request = Request(HttpMethod.Get, tap.ListenUri, "/e", null,
                (TestTrackingHttpHeaders.CurrentTestIdHeader, "t"), ("Authorization", "Bearer secret-token"), ("Cookie", "s=1"), ("Accept", "application/json"));
            await client.SendAsync(request);
        }

        var req = sink.Logs[0];
        Assert.Equal("[REDACTED]", req.Headers.Single(h => h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)).Value);
        Assert.Equal("[REDACTED]", req.Headers.Single(h => h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)).Value);
        Assert.Contains(req.Headers, h => h.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(sink.Logs.SelectMany(l => l.Headers), h => h.Value?.Contains("secret-token") == true);
        // The secret still reached the upstream untouched (capture-only redaction).
        Assert.Equal("Bearer secret-token", upstream.Seen.Single().Headers["Authorization"]);

        sink.Logs.Clear();
        await using (var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, sink, o =>
        {
            o.HeaderPolicy = HeaderCapturePolicy.Whitelist;
            o.HeaderWhitelist.Add("Accept");
            o.HeaderWhitelist.Add("Authorization");
            o.DropSecretHeaders = true;
        })))
        {
            await tap.StartAsync();
            using var client = new HttpClient();
            using var request = Request(HttpMethod.Get, tap.ListenUri, "/f", null,
                (TestTrackingHttpHeaders.CurrentTestIdHeader, "t"), ("Authorization", "Bearer x"), ("Accept", "text/plain"), ("X-Other", "1"));
            await client.SendAsync(request);
        }

        var only = Assert.Single(sink.Logs[0].Headers);
        Assert.Equal("Accept", only.Key, ignoreCase: true);
    }

    [Fact]
    public async Task Bodies_are_decoded_for_capture_and_capped_without_touching_the_wire()
    {
        byte[] Gzip(string text)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                gz.Write(Encoding.UTF8.GetBytes(text));
            return ms.ToArray();
        }

        var big = new string('x', 200);
        await using var upstream = new StubUpstream((_, _) => (200, Gzip("""{"compressed":true}"""), "application/json", "gzip", null));
        var sink = new ListSink();
        await using (var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, sink, o => o.BodyCapBytes = 50)))
        {
            await tap.StartAsync();
            using var client = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None });
            using var request = Request(HttpMethod.Post, tap.ListenUri, "/g", big, (TestTrackingHttpHeaders.CurrentTestIdHeader, "t"));
            var response = await client.SendAsync(request);
            // Wire is untouched: still gzip-encoded bytes.
            Assert.Equal("gzip", response.Content.Headers.ContentEncoding.Single());
            var raw = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(Gzip("""{"compressed":true}"""), raw);
        }

        Assert.StartsWith(new string('x', 50), sink.Logs[0].Content);
        Assert.Contains("truncated (200 chars total)", sink.Logs[0].Content);
        Assert.Equal("""{"compressed":true}""", sink.Logs[1].Content);
    }

    [Fact]
    public async Task Secrets_never_reach_the_report_data_file_and_the_report_renders_the_hop()
    {
        await using var upstream = new StubUpstream(JsonOk);
        var testId = "tap-" + Guid.NewGuid().ToString("N");
        await using (var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, RequestResponseLoggerSink.Instance)))
        {
            await tap.StartAsync();
            using var client = new HttpClient();
            using var request = Request(HttpMethod.Post, tap.ListenUri, "/sidekick", """{"query":"query Overview { overview }"}""",
                (TestTrackingHttpHeaders.CurrentTestNameHeader, "overview renders"), (TestTrackingHttpHeaders.CurrentTestIdHeader, testId),
                ("Authorization", "Bearer never-on-disk"));
            await client.SendAsync(request);
        }

        var dir = Path.Combine(Path.GetTempPath(), "kronikol-tap-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new ReportConfigurationOptions { ReportsFolderPath = dir, InternalFlowTracking = false, GenerateComponentDiagram = true };
            DefaultDiagramsFetcher.Reset();
            ReportGenerator.CreateStandardReportsWithDiagrams(
                [new Feature { DisplayName = "E2E", Scenarios = [new Scenario { Id = testId, DisplayName = "overview renders", Result = ExecutionResult.Passed }] }],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
            DefaultDiagramsFetcher.Reset();

            var json = File.ReadAllText(Path.Combine(dir, "TestRunReport.json"));
            Assert.DoesNotContain("never-on-disk", json);
            Assert.Contains("/sidekick", json);
            var html = File.ReadAllText(Path.Combine(dir, "TestRunReport.html"));
            Assert.Contains("overview renders", html);
            Assert.DoesNotContain("data-no-interactions", html);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Ndjson_sink_and_in_process_store_can_be_combined_and_replayed_by_ingest()
    {
        await using var upstream = new StubUpstream(JsonOk);
        var path = Path.Combine(Path.GetTempPath(), "kronikol-tap-" + Guid.NewGuid().ToString("N") + ".ndjson");
        var testId = Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new NdjsonInteractionWriter(path))
            await using (var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, new CompositeRequestResponseSink(file))))
            {
                await tap.StartAsync();
                using var client = new HttpClient();
                using var request = Request(HttpMethod.Get, tap.ListenUri, "/h", null, (TestTrackingHttpHeaders.CurrentTestIdHeader, testId), (TestTrackingHttpHeaders.CurrentTestNameHeader, "ndjson"));
                await client.SendAsync(request);
            }

            var records = NdjsonInteractionReader.ReadFile(path);
            Assert.Equal(2, records.Count);
            Assert.Equal(testId, records[0].TestId);
            Assert.Equal("Request", records[0].Type);
            Assert.Equal("200", records[1].StatusCode);
            Assert.Equal(records[0].RequestResponseId, records[1].RequestResponseId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Emits_server_and_client_activities_and_reparents_the_forwarded_traceparent()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Kronikol.Extensions.ProxyTap.ProxyTap.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (activities) activities.Add(a); },
        };
        ActivitySource.AddActivityListener(listener);

        await using var upstream = new StubUpstream(JsonOk);
        var sink = new ListSink();
        await using (var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, sink)))
        {
            await tap.StartAsync();
            using var client = new HttpClient();
            using var request = Request(HttpMethod.Get, tap.ListenUri, "/i", null,
                (TestTrackingHttpHeaders.CurrentTestIdHeader, "t"), ("traceparent", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"));
            await client.SendAsync(request);
        }

        var server = activities.Single(a => a.Kind == ActivityKind.Server);
        var clientSpan = activities.Single(a => a.Kind == ActivityKind.Client);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", server.TraceId.ToString());
        Assert.Equal(server.SpanId, clientSpan.ParentSpanId);
        // The forwarded traceparent is the client span's id, same trace.
        Assert.Equal(clientSpan.Id, upstream.Seen.Single().Headers["traceparent"]);
        Assert.Equal(clientSpan.SpanId.ToString(), sink.Logs[0].ActivitySpanId);
    }

    [Fact]
    public async Task Upstream_down_answers_502_from_the_tap()
    {
        var sink = new ListSink();
        var deadPort = StubUpstream.FreePort();
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(), ForwardBaseUri = new Uri($"http://localhost:{deadPort}"), CallerName = "a", ServiceName = "b", Sink = sink,
        });
        await tap.StartAsync();
        using var client = new HttpClient();
        var response = await client.GetAsync(new Uri(tap.ListenUri, "/x"));
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("proxy-tap", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Options_are_validated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProxyTapOptions { ForwardBaseUri = new Uri("http://a"), CallerName = "a", ServiceName = "b" }.Validate());
        Assert.Throws<ArgumentException>(() => new ProxyTapOptions { ListenPort = 1234, CallerName = "a", ServiceName = "b" }.Validate());
        Assert.Throws<ArgumentException>(() => new ProxyTapOptions { ListenPort = 1234, ForwardBaseUri = new Uri("http://a"), ServiceName = "b" }.Validate());
        Assert.Contains("authorization", new ProxyTapOptions().SecretDenylist);
    }

    [Fact]
    public void TraceParent_parsing_is_strict()
    {
        var tp = TraceParent.TryParse("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
        Assert.NotNull(tp);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", tp!.TraceId);
        Assert.True(tp.Sampled);
        Assert.Null(TraceParent.TryParse("garbage"));
        Assert.Null(TraceParent.TryParse("00-00000000000000000000000000000000-b7ad6b7169203331-01"));
        Assert.Equal("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", tp.ToString());
    }

    [Fact]
    public async Task DI_registration_starts_and_stops_the_tap_with_the_host()
    {
        await using var upstream = new StubUpstream(JsonOk);
        var sink = new ListSink();
        var port = StubUpstream.FreePort();
        var services = new ServiceCollection();
        services.AddProxyTapTestTracking(o =>
        {
            o.ListenPort = port;
            o.ForwardBaseUri = upstream.BaseUri;
            o.CallerName = "web";
            o.ServiceName = "api";
            o.Sink = sink;
        });
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().OfType<ProxyTapHostedService>().Single();
        var tap = provider.GetRequiredService<Kronikol.Extensions.ProxyTap.ProxyTap>();
        Assert.Same(tap, hosted.Tap);

        await hosted.StartAsync(CancellationToken.None);
        Assert.True(tap.IsListening);
        using (var client = new HttpClient())
        using (var request = Request(HttpMethod.Get, tap.ListenUri, "/di", null, (TestTrackingHttpHeaders.CurrentTestIdHeader, "t")))
            await client.SendAsync(request);
        await hosted.StopAsync(CancellationToken.None);

        Assert.False(tap.IsListening);
        Assert.Equal(2, sink.Logs.Count);
    }

    /// <summary>
    /// The tap answers the caller BEFORE bumping its counters and recording
    /// ("respond first, record second" — bookkeeping never delays the forwarded
    /// exchange), so a counter read immediately after SendAsync races the
    /// increment. Waits out that gap; the expected value must then hold.
    /// </summary>
    private static async Task<long> EventuallyAsync(Func<long> read, long expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (read() != expected && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        return read();
    }

    [Fact]
    public async Task Diagnostics_are_empty_while_healthy_and_name_the_requests_that_were_not_captured()
    {
        await using var upstream = new StubUpstream(JsonOk);
        var sink = new ListSink();
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(Options(upstream, sink));
        Assert.Empty(tap.Diagnostics());
        await tap.StartAsync();

        using var client = new HttpClient();
        using (var attributed = Request(HttpMethod.Get, tap.ListenUri, "/captured", null, (TestTrackingHttpHeaders.CurrentTestIdHeader, Guid.NewGuid().ToString("N"))))
            (await client.SendAsync(attributed)).Dispose();
        // _captured increments after _handled, so waiting for it settles both.
        Assert.Equal(1, await EventuallyAsync(() => tap.RequestsCaptured, 1));
        Assert.Equal(1, tap.RequestsHandled);
        Assert.Empty(tap.Diagnostics());

        using (var anonymous = Request(HttpMethod.Get, tap.ListenUri, "/not-captured", null))
            (await client.SendAsync(anonymous)).Dispose();
        Assert.Equal(2, await EventuallyAsync(() => tap.RequestsHandled, 2));
        Assert.Equal(1, tap.RequestsCaptured);

        var entry = Assert.Single(tap.Diagnostics());
        Assert.Equal(DiagnosticKind.CaptureDegraded, entry.Kind);
        Assert.StartsWith("web→graphql: 1 of 2 forwarded request(s) carried no test identity and were not captured", entry.Message);
        Assert.Null(entry.ScenarioId);
    }

    [Fact]
    public async Task A_failed_forward_is_counted_and_reported_as_capture_degraded()
    {
        var sink = new ListSink();
        var deadPort = StubUpstream.FreePort();
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(), ForwardBaseUri = new Uri($"http://localhost:{deadPort}"), CallerName = "web", ServiceName = "api", Sink = sink, Name = "tap-web-api",
        });
        await tap.StartAsync();
        using var client = new HttpClient();
        using var response = await client.GetAsync(new Uri(tap.ListenUri, "/x"));
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        Assert.Equal(1, tap.ForwardFailures);
        Assert.Equal(0, tap.RequestsHandled);
        var entry = Assert.Single(tap.Diagnostics());
        Assert.Equal(DiagnosticKind.CaptureDegraded, entry.Kind);
        Assert.StartsWith("tap-web-api: 1 request(s) could not be forwarded and were answered 502 Bad Gateway", entry.Message);
    }
}
