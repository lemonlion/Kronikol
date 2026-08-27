using System.IO.Compression;
using System.Net;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Tracking;

namespace Kronikol.Tests.Otlp;

/// <summary>A collector stub whose status can differ per request (first N fail, rest succeed).</summary>
internal sealed class SequencedCollector : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private int _served;
    public readonly List<(Dictionary<string, string> Headers, byte[] Body)> Seen = [];

    public SequencedCollector(params int[] statuses)
    {
        Statuses = statuses;
        Port = StubCollector.FreePort();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    public int[] Statuses { get; }

    public int Port { get; }

    public Uri TracesEndpoint => new($"http://localhost:{Port}/v1/traces");

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
                int index;
                lock (Seen)
                {
                    index = _served++;
                    Seen.Add((headers, buffer.ToArray()));
                }

                context.Response.StatusCode = index < Statuses.Length ? Statuses[index] : Statuses[^1];
                context.Response.Close(Encoding.UTF8.GetBytes("{}"), willBlock: false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        try { _listener.Stop(); _listener.Close(); } catch (ObjectDisposedException) { }
        return ValueTask.CompletedTask;
    }
}

public class OtlpExporterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
    private const string TestId = "cafe651916cd43dd8448eb211c80319c";

    private static (RequestResponseLog Request, RequestResponseLog Response) HttpPair(
        string path = "/things", string caller = "web", string service = "backend", Guid? id = null)
    {
        var pairId = id ?? Guid.NewGuid();
        var request = new RequestResponseLog("My test", TestId, HttpMethod.Get, null,
            new Uri("http://api.example" + path), [], service, caller, RequestResponseType.Request,
            pairId, pairId, false)
        { Timestamp = T0 };
        var response = request with { Type = RequestResponseType.Response, StatusCode = (OneOf<HttpStatusCode, string>)HttpStatusCode.OK };
        response.Timestamp = T0.AddMilliseconds(25);
        return (request, response);
    }

    [Fact]
    public async Task Exports_pairs_as_spans_the_reader_decodes_back()
    {
        await using var collector = new SequencedCollector(200);
        var options = new OtlpExportOptions { Endpoint = collector.TracesEndpoint };
        options.Headers["x-kronikol-export"] = "secret";
        using var exporter = new OtlpExporter(options);
        var (request, response) = HttpPair();

        var result = await exporter.ExportAsync([request, response]);

        Assert.True(result.Success);
        Assert.Equal(1, result.SpansExported);
        Assert.Equal(1, result.TraceCount);
        Assert.Equal(0, result.OrphanSpans);
        Assert.Equal(1, result.BatchesSent);
        Assert.Equal(0, result.BatchesFailed);

        var (headers, body) = Assert.Single(collector.Seen);
        Assert.Equal("secret", headers["x-kronikol-export"]);
        Assert.Contains("application/json", headers["Content-Type"]);
        var spans = OtlpTraceReader.ReadJson(body);
        var span = Assert.Single(spans);
        Assert.Equal("GET", span.Name);
        Assert.Equal(TestId, span.TraceId);
        Assert.Equal("http://api.example/things", span.Attribute("url.full"));
    }

    [Fact]
    public async Task Gzip_option_compresses_the_payload()
    {
        await using var collector = new SequencedCollector(200);
        var options = new OtlpExportOptions { Endpoint = collector.TracesEndpoint, Gzip = true };
        using var exporter = new OtlpExporter(options);
        var (request, response) = HttpPair();

        var result = await exporter.ExportAsync([request, response]);

        Assert.True(result.Success);
        var (headers, body) = Assert.Single(collector.Seen);
        Assert.Equal("gzip", headers["Content-Encoding"]);
        using var decompressed = new MemoryStream();
        await using (var gzip = new GZipStream(new MemoryStream(body), CompressionMode.Decompress))
            await gzip.CopyToAsync(decompressed);
        Assert.Single(OtlpTraceReader.ReadJson(decompressed.ToArray()));
    }

    [Fact]
    public async Task Pages_by_batch_max_spans()
    {
        await using var collector = new SequencedCollector(200);
        var options = new OtlpExportOptions { Endpoint = collector.TracesEndpoint, BatchMaxSpans = 2 };
        using var exporter = new OtlpExporter(options);
        var logs = new List<RequestResponseLog>();
        for (var i = 0; i < 5; i++)
        {
            var (request, response) = HttpPair(path: $"/things/{i}");
            logs.Add(request);
            logs.Add(response);
        }

        var result = await exporter.ExportAsync(logs);

        Assert.Equal(5, result.SpansExported);
        Assert.Equal(3, result.BatchesSent);
        lock (collector.Seen)
            Assert.Equal(3, collector.Seen.Count);
    }

    [Fact]
    public async Task A_failed_batch_is_retried_once_and_then_counted()
    {
        // First attempt 500, immediate retry 200 → batch lands, one failure logged, none lost.
        await using var flaky = new SequencedCollector(500, 200);
        var options = new OtlpExportOptions { Endpoint = flaky.TracesEndpoint };
        using var exporter = new OtlpExporter(options);
        var (request, response) = HttpPair();

        var result = await exporter.ExportAsync([request, response]);

        Assert.True(result.Success);
        Assert.Equal(1, result.SpansExported);
        Assert.Equal(1, result.BatchesSent);
        lock (flaky.Seen)
            Assert.Equal(2, flaky.Seen.Count);

        // Both attempts fail → the batch is counted failed, never thrown.
        await using var down = new SequencedCollector(503, 503);
        using var failing = new OtlpExporter(new OtlpExportOptions { Endpoint = down.TracesEndpoint });
        var failedResult = await failing.ExportAsync([request, response]);
        Assert.False(failedResult.Success);
        Assert.Equal(1, failedResult.BatchesFailed);
        Assert.Equal(0, failedResult.SpansExported);
        Assert.Equal(1, failedResult.SpansFailed);
    }

    [Fact]
    public async Task An_unreachable_endpoint_is_counted_not_thrown()
    {
        var options = new OtlpExportOptions { Endpoint = new Uri($"http://localhost:{StubCollector.FreePort()}/v1/traces") };
        using var exporter = new OtlpExporter(options);
        var (request, response) = HttpPair();

        var result = await exporter.ExportAsync([request, response]);

        Assert.False(result.Success);
        Assert.Equal(1, result.BatchesFailed);
    }

    [Fact]
    public async Task Full_circle_an_exported_capture_round_trips_through_a_live_OtlpTap()
    {
        var sink = new ListSink();
        var tapOptions = new OtlpTapOptions { ListenPort = 0, Sink = sink };
        await using var tap = new OtlpTap(tapOptions);
        await tap.StartAsync();

        var (httpRequest, httpResponse) = HttpPair();
        var redisId = Guid.NewGuid();
        var redisRequest = new RequestResponseLog("My test", TestId, "GET (Hit)", null,
            new Uri("redis://db0/insights:trials"), [], "redis", "backend", RequestResponseType.Request,
            redisId, redisId, false, DependencyCategory: Kronikol.Constants.DependencyCategories.Redis)
        { Timestamp = T0.AddMilliseconds(5) };
        var redisResponse = redisRequest with { Type = RequestResponseType.Response, StatusCode = (OneOf<HttpStatusCode, string>)"OK" };
        redisResponse.Timestamp = T0.AddMilliseconds(7);

        using var exporter = new OtlpExporter(new OtlpExportOptions { Endpoint = tap.TracesEndpoint });
        var result = await exporter.ExportAsync([httpRequest, httpResponse, redisRequest, redisResponse]);
        Assert.True(result.Success);
        Assert.Equal(2, result.SpansExported);

        await tap.DisposeAsync(); // drains the mapping queue

        var logs = sink.Snapshot();
        Assert.Equal(4, logs.Length); // two calls, each a request/response pair

        // Semantic equivalence: the tap rebuilds labels from semconv attributes.
        var httpBack = logs.Where(l => l.Uri.ToString() == "http://api.example/things").ToArray();
        Assert.Equal(2, httpBack.Length);
        Assert.All(httpBack, l => Assert.Equal(TestId, l.TestId));
        Assert.All(httpBack, l => Assert.Equal("web", l.CallerName));
        Assert.All(httpBack, l => Assert.Equal("backend", l.ServiceName));
        Assert.Equal("200", httpBack.First(l => l.Type == RequestResponseType.Response).StatusCode?.Value switch
        {
            HttpStatusCode code => ((int)code).ToString(),
            var other => other?.ToString(),
        });

        var redisBack = logs.Where(l => l.Uri.Scheme == "redis").ToArray();
        Assert.Equal(2, redisBack.Length);
        Assert.All(redisBack, l => Assert.Equal(TestId, l.TestId));
        // The tap's mapper derives the verb from the span name and the target from db attributes.
        Assert.All(redisBack, l => Assert.Equal("GET", l.Method.Value?.ToString()));
        Assert.All(redisBack, l => Assert.Equal(Kronikol.Constants.DependencyCategories.Redis, l.DependencyCategory));
        // D4 both ways: the re-captured pair carries the exported W3C ids.
        Assert.All(redisBack, l => Assert.Equal(TestId, l.ActivityTraceId));
    }
}
