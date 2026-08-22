using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kronikol.Tests.Otlp;

/// <summary>Collects what a tap captured.</summary>
internal sealed class ListSink : IRequestResponseSink
{
    public readonly List<RequestResponseLog> Logs = [];

    public void Log(RequestResponseLog log)
    {
        lock (Logs) Logs.Add(log);
    }

    public int Count
    {
        get { lock (Logs) return Logs.Count; }
    }

    public RequestResponseLog[] Snapshot()
    {
        lock (Logs) return Logs.ToArray();
    }
}

/// <summary>A sink that blocks, so the tap's bounded queue fills and has to drop.</summary>
internal sealed class BlockingSink(SemaphoreSlim gate) : IRequestResponseSink
{
    public void Log(RequestResponseLog log) => gate.Wait(TimeSpan.FromSeconds(30));
}

/// <summary>A sink that cannot write, so an accepted payload is lost and counted.</summary>
internal sealed class ThrowingSink : IRequestResponseSink
{
    public void Log(RequestResponseLog log) => throw new IOException("disk full");
}

/// <summary>A stub collector that records exactly what arrived on the wire.</summary>
internal sealed class StubCollector : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    public readonly List<(string Method, string Path, Dictionary<string, string> Headers, byte[] Body)> Seen = [];

    public StubCollector(int status = 200, string body = "{}")
    {
        Status = status;
        Body = body;
        Port = FreePort();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    public int Status { get; }

    public string Body { get; }

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
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in context.Request.Headers.AllKeys)
                    if (key is not null) headers[key] = context.Request.Headers[key]!;
                lock (Seen) Seen.Add((context.Request.HttpMethod, context.Request.Url!.PathAndQuery, headers, buffer.ToArray()));

                var payload = Encoding.UTF8.GetBytes(Body);
                context.Response.StatusCode = Status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload);
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

public class OtlpTapTests
{
    private static OtlpTapOptions Options(IRequestResponseSink sink)
    {
        var options = new OtlpTapOptions { ListenPort = 0, Sink = sink };
        options.ServiceNameMap["localhost:27099"] = "mongo";
        return options;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        OtlpTap tap, byte[] body, string contentType, string? contentEncoding = null, IDictionary<string, string>? headers = null, string? path = null)
    {
        using var client = new HttpClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{tap.BoundPort}{path ?? tap.Options.TracesPath}")
        {
            Content = new ByteArrayContent(body),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        if (contentEncoding is not null)
            message.Content.Headers.ContentEncoding.Add(contentEncoding);
        if (headers is not null)
            foreach (var (name, value) in headers)
                message.Headers.TryAddWithoutValidation(name, value);
        return await client.SendAsync(message);
    }

    private static async Task WaitForAsync(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    private static Kronikol.ReportConfigurationOptions ReportOptions(string directory)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = directory;
        options.GenerateComponentDiagram = false;
        return options;
    }

    private static byte[] Gzip(byte[] raw)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(raw, 0, raw.Length);
        return buffer.ToArray();
    }

    [Fact]
    public async Task Accepts_the_json_encoding_and_maps_the_spans_it_carries()
    {
        var sink = new ListSink();
        await using var tap = new OtlpTap(Options(sink));
        await tap.StartAsync();

        using var response = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{}", await response.Content.ReadAsStringAsync());
        await WaitForAsync(() => sink.Count == 2, "the mongo span to be mapped");

        var logs = sink.Snapshot();
        Assert.Equal("Find ← Trial", logs[0].Method.Value?.ToString());
        Assert.Equal("mongo", logs[0].ServiceName);
        Assert.Equal(OtlpGoldens.TestTraceId, logs[0].TestId);
        Assert.Equal(InteractionMerger.SpanSource, logs[0].CapturedBy);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
        Assert.Equal(1, tap.RequestsReceived);
        Assert.Equal(1, tap.SpansMapped);
    }

    [Fact]
    public async Task Accepts_the_protobuf_encoding_and_answers_an_empty_export_response()
    {
        var sink = new ListSink();
        await using var tap = new OtlpTap(Options(sink));
        await tap.StartAsync();

        using var response = await PostAsync(tap, OtlpGoldens.MongoFindProtobuf(), "application/x-protobuf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-protobuf", response.Content.Headers.ContentType?.ToString());
        Assert.Empty(await response.Content.ReadAsByteArrayAsync()); // ExportTraceServiceResponse{} is zero bytes
        await WaitForAsync(() => sink.Count == 2, "the protobuf span to be mapped");
    }

    [Fact]
    public async Task Accepts_a_gzipped_payload()
    {
        var sink = new ListSink();
        await using var tap = new OtlpTap(Options(sink));
        await tap.StartAsync();

        using var response = await PostAsync(tap, Gzip(OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv)), "application/json", "gzip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await WaitForAsync(() => sink.Count == 2, "the gzipped span to be mapped");
    }

    [Fact]
    public async Task Forwards_byte_for_byte_to_a_real_collector_and_relays_its_answer()
    {
        var sink = new ListSink();
        await using var collector = new StubCollector(status: 202, body: "{\"partialSuccess\":{}}");
        var options = Options(sink);
        options.ForwardBaseUri = collector.BaseUri;
        await using var tap = new OtlpTap(options);
        await tap.StartAsync();

        var payload = Gzip(OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv));
        using var response = await PostAsync(tap, payload, "application/json", "gzip",
            new Dictionary<string, string> { ["x-kronikol-tap"] = "s3cret" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("{\"partialSuccess\":{}}", await response.Content.ReadAsStringAsync());

        var seen = Assert.Single(collector.Seen);
        Assert.Equal("POST", seen.Method);
        Assert.Equal("/v1/traces", seen.Path);
        Assert.Equal(payload, seen.Body); // still gzipped: the tee never re-encodes
        Assert.Equal("gzip", seen.Headers["Content-Encoding"]);
        Assert.Equal("application/json", seen.Headers["Content-Type"]);
        Assert.Equal("s3cret", seen.Headers["x-kronikol-tap"]);

        await WaitForAsync(() => sink.Count == 2, "the forwarded span to be mapped as well");
    }

    [Fact]
    public async Task A_forward_to_a_dead_collector_answers_502_and_is_counted()
    {
        var sink = new ListSink();
        var options = Options(sink);
        options.ForwardBaseUri = new Uri($"http://localhost:{StubCollector.FreePort()}");
        await using var tap = new OtlpTap(options);
        await tap.StartAsync();

        using var response = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, tap.ForwardFailures);
    }

    [Fact]
    public async Task Requests_without_the_shared_secret_are_rejected_with_401_and_never_mapped()
    {
        var sink = new ListSink();
        var options = Options(sink);
        options.ExpectedHeaders["x-kronikol-tap"] = "s3cret";
        await using var tap = new OtlpTap(options);
        await tap.StartAsync();

        using var missing = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        using var wrong = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json",
            headers: new Dictionary<string, string> { ["x-kronikol-tap"] = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        using var right = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json",
            headers: new Dictionary<string, string> { ["X-Kronikol-Tap"] = "s3cret" });
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);

        await WaitForAsync(() => sink.Count == 2, "only the authenticated export to be mapped");
        Assert.Equal(2, tap.UnauthenticatedRequests);
        Assert.Equal(1, tap.RequestsReceived);
    }

    [Fact]
    public async Task Another_path_is_404_when_the_tap_is_a_leaf()
    {
        var sink = new ListSink();
        await using var tap = new OtlpTap(Options(sink));
        await tap.StartAsync();

        using var response = await PostAsync(tap, [1, 2, 3], "application/x-protobuf", path: "/v1/metrics");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, tap.RequestsReceived);
    }

    [Fact]
    public async Task A_blocked_sink_never_slows_the_exporter_down_the_queue_drops_instead()
    {
        using var gate = new SemaphoreSlim(0);
        var options = Options(new BlockingSink(gate));
        options.QueueCapacity = 1;
        await using var tap = new OtlpTap(options);
        await tap.StartAsync();

        try
        {
            var payload = OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv);
            var started = DateTime.UtcNow;
            for (var i = 0; i < 12; i++)
            {
                using var response = await PostAsync(tap, payload, "application/json");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            var elapsed = DateTime.UtcNow - started;
            Assert.True(elapsed < TimeSpan.FromSeconds(5), $"12 exports took {elapsed.TotalMilliseconds:F0} ms against a blocked sink");
            await WaitForAsync(() => tap.PayloadsDropped > 0, "the bounded queue to drop");
            Assert.Equal(12, tap.RequestsReceived);
        }
        finally
        {
            gate.Release(64); // let the drain at dispose finish instantly
        }
    }

    [Fact]
    public async Task An_oversized_payload_is_refused_without_being_buffered()
    {
        var sink = new ListSink();
        var options = Options(sink);
        options.MaxRequestBytes = 64;
        await using var tap = new OtlpTap(options);
        await tap.StartAsync();

        using var response = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, tap.SpansReceived);
    }

    [Fact]
    public async Task Attribution_by_trace_id_can_be_swapped_for_a_fallback_bucket()
    {
        var sink = new ListSink();
        var options = Options(sink);
        options.AttributeByTraceId = false;
        options.FallbackTestId = "outside-any-test";
        options.FallbackTestName = "Traffic outside any test";
        await using var tap = new OtlpTap(options);
        await tap.StartAsync();

        using var response = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await WaitForAsync(() => sink.Count == 2, "the span to be mapped into the fallback bucket");
        var log = sink.Snapshot()[0];
        Assert.Equal("outside-any-test", log.TestId);
        Assert.Equal("Traffic outside any test", log.TestName);
        Assert.Equal(OtlpGoldens.TestTraceId, log.ActivityTraceId);
    }

    [Fact]
    public async Task Keep_alive_connections_carry_several_exports()
    {
        var sink = new ListSink();
        await using var tap = new OtlpTap(Options(sink));
        await tap.StartAsync();

        using var client = new HttpClient();
        for (var i = 0; i < 3; i++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{tap.BoundPort}/v1/traces")
            {
                Content = new ByteArrayContent(OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv)),
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await client.SendAsync(message);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await WaitForAsync(() => sink.Count == 6, "three exports over one connection");
    }

    [Fact]
    public void Options_are_validated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OtlpTapOptions { ListenPort = -1 }.Validate());
        Assert.Throws<ArgumentException>(() => new OtlpTapOptions { ListenPort = 1, ListenHost = " " }.Validate());
        Assert.Throws<ArgumentException>(() => new OtlpTapOptions { ListenPort = 1, TracesPath = "v1/traces" }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new OtlpTapOptions { ListenPort = 1, QueueCapacity = 0 }.Validate());
        new OtlpTapOptions { ListenPort = 4318 }.Validate();
    }

    [Fact]
    public void Bind_addresses_cover_loopback_and_every_interface()
    {
        Assert.Equal(IPAddress.Loopback, OtlpTap.ResolveBindAddress("localhost"));
        Assert.Equal(IPAddress.Any, OtlpTap.ResolveBindAddress("+"));
        Assert.Equal(IPAddress.Any, OtlpTap.ResolveBindAddress("0.0.0.0"));
        Assert.Equal(IPAddress.Any, OtlpTap.ResolveBindAddress("*"));
        Assert.Equal(IPAddress.IPv6Any, OtlpTap.ResolveBindAddress("::"));
        Assert.Equal(IPAddress.Parse("192.168.127.254"), OtlpTap.ResolveBindAddress("192.168.127.254"));
    }

    [Fact]
    public async Task DI_registration_starts_and_stops_the_tap_with_the_host()
    {
        var sink = new ListSink();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddOtlpTapTestTracking(o =>
        {
            o.ListenPort = 0;
            o.Sink = sink;
            o.Name = "tap-otlp";
        });

        using var host = builder.Build();
        var tap = host.Services.GetRequiredService<OtlpTap>();
        await host.StartAsync();
        Assert.True(tap.IsListening);
        Assert.True(tap.BoundPort > 0);

        using var response = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await WaitForAsync(() => sink.Count == 2, "the hosted tap to map");

        await host.StopAsync();
        Assert.False(tap.IsListening);
    }

    [Fact]
    public async Task The_captured_pair_replays_through_ingest_as_one_arrow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kronikol-otlp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var ndjson = Path.Combine(directory, "spans.ndjson");
            using (var writer = new NdjsonInteractionWriter(ndjson))
            {
                var options = Options(writer);
                await using var tap = new OtlpTap(options);
                await tap.StartAsync();
                using var response = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                await WaitForAsync(() => writer.LinesWritten == 2, "the pair to reach the NDJSON file");
            }

            var records = NdjsonInteractionReader.ReadFile(ndjson).ToList();
            Assert.Equal(2, records.Count);
            Assert.Equal(InteractionMerger.SpanSource, records[0].CapturedBy);
            Assert.Equal(OtlpGoldens.TestTraceId, records[0].TestId);

            var result = IngestPipeline.Run(new IngestRequest
            {
                InteractionFiles = [ndjson],
                Options = ReportOptions(Path.Combine(directory, "Reports")),
            });

            Assert.True(result.Generated);
            Assert.Equal(1, result.ScenarioCount);
            Assert.Equal(2, result.InteractionCount);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Diagnostics_are_empty_for_a_healthy_tap()
    {
        var sink = new ListSink();
        await using var tap = new OtlpTap(Options(sink));
        Assert.Empty(tap.Diagnostics());
        await tap.StartAsync();

        using var response = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await WaitForAsync(() => sink.Count == 2, "the pair to be mapped");

        Assert.Empty(tap.Diagnostics());
    }

    [Fact]
    public async Task Diagnostics_name_rejected_payloads_and_a_sink_that_throws()
    {
        var rejecting = Options(new ListSink());
        rejecting.Name = "otlp-di";
        rejecting.MaxRequestBytes = 64;
        await using var rejectingTap = new OtlpTap(rejecting);
        await rejectingTap.StartAsync();
        using (var tooLarge = await PostAsync(rejectingTap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json"))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal(1, rejectingTap.PayloadsRejected);
        var rejected = Assert.Single(rejectingTap.Diagnostics());
        Assert.Equal(Kronikol.Reports.DiagnosticKind.CaptureDegraded, rejected.Kind);
        Assert.StartsWith("otlp-di: 1 export request(s) refused with 413 Payload Too Large (MaxRequestBytes 64)", rejected.Message);

        // An accepted payload whose sink throws is lost — and counted.
        var failing = Options(new ThrowingSink());
        failing.Name = "otlp-di";
        await using var failingTap = new OtlpTap(failing);
        await failingTap.StartAsync();
        using (var accepted = await PostAsync(failingTap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json"))
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await WaitForAsync(() => failingTap.PayloadsFailed == 1, "the failed payload to be counted");
        var failed = Assert.Single(failingTap.Diagnostics());
        Assert.Equal(Kronikol.Reports.DiagnosticKind.CaptureDegraded, failed.Kind);
        Assert.StartsWith("otlp-di: 1 export payload(s) failed while being decoded, mapped or written to the sink", failed.Message);
    }

    [Fact]
    public async Task Diagnostics_name_unauthenticated_exports_failed_forwards_and_dropped_payloads()
    {
        var sink = new ListSink();
        var options = Options(sink);
        options.ExpectedHeaders["x-kronikol-tap"] = "s3cret";
        options.ForwardBaseUri = new Uri($"http://localhost:{StubCollector.FreePort()}");
        await using var tap = new OtlpTap(options);
        await tap.StartAsync();

        using (var missing = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json"))
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        using (var forwarded = await PostAsync(tap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json",
                   headers: new Dictionary<string, string> { ["x-kronikol-tap"] = "s3cret" }))
            Assert.Equal(HttpStatusCode.BadGateway, forwarded.StatusCode);

        var entries = tap.Diagnostics();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Message.StartsWith("otlp: 1 export request(s) rejected with 401", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.Message.Contains("1 export(s) could not be forwarded to http://localhost:") && e.Message.Contains("502 Bad Gateway"));

        // Dropped payloads (queue full) are the remaining counter; the blocked-sink test exercises the drop itself.
        var gate = new SemaphoreSlim(0);
        var blocked = Options(new BlockingSink(gate));
        blocked.QueueCapacity = 1;
        await using var droppingTap = new OtlpTap(blocked);
        await droppingTap.StartAsync();
        for (var i = 0; i < 8 && droppingTap.PayloadsDropped == 0; i++)
            using (await PostAsync(droppingTap, OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json")) { }
        await WaitForAsync(() => droppingTap.PayloadsDropped > 0, "the bounded queue to drop");
        gate.Release(64);
        var dropped = Assert.Single(droppingTap.Diagnostics());
        Assert.StartsWith("otlp: ", dropped.Message);
        Assert.Contains("export payload(s) dropped because the mapping queue was full (QueueCapacity 1)", dropped.Message);
    }
}
