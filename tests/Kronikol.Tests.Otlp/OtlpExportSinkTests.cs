using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Otlp;

using Kronikol.Extensions.Otlp;

/// <summary>Accepts connections and never answers — the collector from hell, for D3 tests.</summary>
internal sealed class BlackHoleCollector : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<TcpClient> _clients = [];

    public BlackHoleCollector()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    lock (_clients) _clients.Add(client);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    return;
                }
            }
        });
    }

    public int Port { get; }

    public Uri TracesEndpoint => new($"http://localhost:{Port}/v1/traces");

    public void Dispose()
    {
        try { _listener.Stop(); } catch (SocketException) { }
        lock (_clients)
        {
            foreach (var client in _clients)
                client.Dispose();
        }
    }
}

public class OtlpExportSinkTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
    private const string TestId = "cafe651916cd43dd8448eb211c80319c";

    private static (RequestResponseLog Request, RequestResponseLog Response) HttpPair(string path = "/things")
    {
        var pairId = Guid.NewGuid();
        var request = new RequestResponseLog("My test", TestId, HttpMethod.Get, null,
            new Uri("http://api.example" + path), [], "backend", "web", RequestResponseType.Request,
            pairId, pairId, false)
        { Timestamp = T0 };
        var response = request with { Type = RequestResponseType.Response, StatusCode = (OneOf<HttpStatusCode, string>)HttpStatusCode.OK };
        response.Timestamp = T0.AddMilliseconds(25);
        return (request, response);
    }

    private static OtlpExportOptions Options(Uri endpoint) => new()
    {
        Endpoint = endpoint,
        FlushInterval = TimeSpan.FromMilliseconds(100),
    };

    [Fact]
    public async Task Pairs_stream_out_as_single_spans()
    {
        await using var collector = new SequencedCollector(200);
        await using var sink = new OtlpExportSink(Options(collector.TracesEndpoint));
        var (request, response) = HttpPair();

        sink.Log(request);
        sink.Log(response);
        await sink.FlushAsync();

        Assert.Equal(1, sink.SpansExported);
        List<byte[]> bodies;
        lock (collector.Seen) bodies = collector.Seen.Select(s => s.Body).ToList();
        var spans = bodies.SelectMany(b => OtlpTraceReader.ReadJson(b)).ToList();
        var span = Assert.Single(spans);
        Assert.Equal("GET", span.Name);
        Assert.Equal(TestId, span.TraceId);
        Assert.Null(span.Attribute("kronikol.orphan"));
    }

    [Fact]
    public async Task A_request_outliving_its_ttl_exports_as_an_orphan()
    {
        await using var collector = new SequencedCollector(200);
        var options = Options(collector.TracesEndpoint);
        options.PendingRequestTtl = TimeSpan.FromMilliseconds(50);
        await using var sink = new OtlpExportSink(options);
        var (request, _) = HttpPair();

        sink.Log(request);
        await Task.Delay(300);
        await sink.FlushAsync();

        Assert.Equal(1, sink.OrphanSpans);
        List<byte[]> bodies;
        lock (collector.Seen) bodies = collector.Seen.Select(s => s.Body).ToList();
        var span = Assert.Single(bodies.SelectMany(b => OtlpTraceReader.ReadJson(b)));
        Assert.Equal("true", span.Attribute("kronikol.orphan"));
        Assert.Equal(span.StartTimeUnixNano, span.EndTimeUnixNano);
    }

    [Fact]
    public async Task A_response_with_no_buffered_request_exports_as_an_orphan_immediately()
    {
        await using var collector = new SequencedCollector(200);
        await using var sink = new OtlpExportSink(Options(collector.TracesEndpoint));
        var (_, response) = HttpPair();

        sink.Log(response);
        await sink.FlushAsync();

        Assert.Equal(1, sink.OrphanSpans);
        List<byte[]> bodies;
        lock (collector.Seen) bodies = collector.Seen.Select(s => s.Body).ToList();
        var span = Assert.Single(bodies.SelectMany(b => OtlpTraceReader.ReadJson(b)));
        Assert.Equal("true", span.Attribute("kronikol.orphan"));
    }

    [Fact]
    public async Task Disposing_flushes_buffered_work_and_exports_pending_requests_as_orphans()
    {
        await using var collector = new SequencedCollector(200);
        var options = Options(collector.TracesEndpoint);
        options.FlushInterval = TimeSpan.FromMinutes(10); // nothing flushes on its own
        options.PendingRequestTtl = TimeSpan.FromMinutes(10);
        var sink = new OtlpExportSink(options);
        var (request, response) = HttpPair();
        var (lonely, _) = HttpPair("/lonely");
        sink.Log(request);
        sink.Log(response);
        sink.Log(lonely);

        await sink.DisposeAsync();

        Assert.Equal(2, sink.SpansExported);
        Assert.Equal(1, sink.OrphanSpans);
        List<byte[]> bodies;
        lock (collector.Seen) bodies = collector.Seen.Select(s => s.Body).ToList();
        var spans = bodies.SelectMany(b => OtlpTraceReader.ReadJson(b)).ToList();
        Assert.Equal(2, spans.Count);
        Assert.Single(spans, s => s.Attribute("kronikol.orphan") == "true");
    }

    [Fact]
    public async Task Log_never_blocks_and_drops_are_counted_when_the_collector_never_answers()
    {
        using var blackHole = new BlackHoleCollector();
        var options = Options(blackHole.TracesEndpoint);
        options.QueueCapacity = 8;
        options.BatchMaxSpans = 1;
        options.FlushInterval = TimeSpan.FromMilliseconds(20);
        options.ShutdownTimeout = TimeSpan.FromMilliseconds(200); // don't wait out the hung POST at dispose
        await using var sink = new OtlpExportSink(options);

        // Give the worker time to get stuck in its first POST.
        var (first, firstResponse) = HttpPair();
        sink.Log(first);
        sink.Log(firstResponse);
        await Task.Delay(200);

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 500; i++)
        {
            var (request, response) = HttpPair($"/burst/{i}");
            sink.Log(request);
            sink.Log(response);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"Log() must never block; took {stopwatch.ElapsedMilliseconds} ms");
        Assert.True(sink.EntriesDropped > 0, "the bounded queue should have dropped");

        var diagnostics = sink.Diagnostics();
        Assert.Contains(diagnostics, d => d.Kind == DiagnosticKind.CaptureDegraded && d.Message.Contains("dropped"));
    }

    [Fact]
    public async Task Failed_batches_surface_in_diagnostics()
    {
        await using var down = new SequencedCollector(503, 503, 503, 503);
        await using (var sink = new OtlpExportSink(Options(down.TracesEndpoint)))
        {
            var (request, response) = HttpPair();
            sink.Log(request);
            sink.Log(response);
            await sink.FlushAsync();

            Assert.Equal(0, sink.SpansExported);
            Assert.True(sink.SpansFailed >= 1);
            var diagnostics = sink.Diagnostics();
            Assert.Contains(diagnostics, d => d.Kind == DiagnosticKind.CaptureDegraded && d.Message.Contains("could not be delivered"));
        }
    }

    [Fact]
    public async Task Skipped_records_never_occupy_the_queue()
    {
        await using var collector = new SequencedCollector(200);
        await using var sink = new OtlpExportSink(Options(collector.TracesEndpoint));
        var (request, _) = HttpPair();
        var marker = request with { RequestResponseId = Guid.NewGuid() };
        marker.IsOverrideStart = true;
        var spanSourced = request with { RequestResponseId = Guid.NewGuid() };
        spanSourced.CapturedBy = Kronikol.Ingestion.InteractionMerger.SpanSource;

        sink.Log(marker);
        sink.Log(spanSourced);
        await sink.FlushAsync();

        Assert.Equal(2, sink.RecordsSkipped);
        Assert.Equal(0, sink.SpansExported + sink.OrphanSpans);
    }

    [Fact]
    public async Task A_flush_caught_behind_a_hung_collector_is_released_by_dispose()
    {
        using var blackHole = new BlackHoleCollector();
        var options = Options(blackHole.TracesEndpoint);
        options.BatchMaxSpans = 1;
        options.FlushInterval = TimeSpan.FromMilliseconds(20);
        options.ShutdownTimeout = TimeSpan.FromMilliseconds(200);
        var sink = new OtlpExportSink(options);
        var (request, response) = HttpPair();
        sink.Log(request);
        sink.Log(response);
        await Task.Delay(200); // let the worker get stuck in the POST

        var flush = sink.FlushAsync(); // its marker queues behind the hung batch
        await sink.DisposeAsync();     // times out, cancels the POST, must still release the flush waiter

        var done = await Task.WhenAny(flush, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(flush, done);
    }

    [Fact]
    public async Task Composes_with_other_sinks()
    {
        await using var collector = new SequencedCollector(200);
        var list = new ListSink();
        await using var otlp = new OtlpExportSink(Options(collector.TracesEndpoint));
        var composite = new CompositeRequestResponseSink(list, otlp);
        var (request, response) = HttpPair();

        composite.Log(request);
        composite.Log(response);
        await otlp.FlushAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal(1, otlp.SpansExported);
    }
}
