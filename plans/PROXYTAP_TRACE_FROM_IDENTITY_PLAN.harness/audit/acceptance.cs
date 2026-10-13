#:project ../../../src/Kronikol.Extensions.ProxyTap/Kronikol.Extensions.ProxyTap.csproj
#:project ../../../src/Kronikol.Extensions.Otlp/Kronikol.Extensions.Otlp.csproj
#:package OpenTelemetry@1.19.1
#:package OpenTelemetry.Exporter.InMemory@1.19.1
#:property PublishAot=false
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Ingestion;
using Kronikol.InternalFlow;
using Kronikol.Query;
using Kronikol.Tracking;
using OpenTelemetry;
using OpenTelemetry.Trace;

// The plan's section 5.3 acceptance, run by the #170 audit (it had not been run). Argument: "forward" (part A) or
// "chain on" / "chain off" (part B, TraceIdFromIdentity on, or off as the control). Built from this tree.
//   A. A tap with TraceIdFromIdentity forwards a call that brings no trace under the test id, sampled, with nothing
//      listening, with Kronikol's internal-flow listener attached and with the OpenTelemetry SDK attached.
//   B. The issue's chain: an app with no trace, the tap (its resolver names the test), a service that passes on only
//      traceparent and exports a database span per call to an OTLP tap (AttributeByTraceId), two calls at once.
//      One ingest joins a tests file, both taps' records and the span file; the query tool says where each call
//      landed, and the builder says what each tap call's popup holds.
const string TestId = "4bf92f3577b34da6a3ce929d0e0e4736";
var part = args.Length > 0 ? args[0] : "forward";
var on = args.Length < 2 || args[1] != "off";
using var caller = new HttpClient(new SocketsHttpHandler { ActivityHeadersPropagator = null });

if (part == "forward")
{
    foreach (var mode in new[] { "no listener", "Kronikol's internal-flow listener", "the OpenTelemetry SDK" })
    {
        using var internalFlow = mode.StartsWith("Kronikol", StringComparison.Ordinal) ? new InternalFlowActivityListener() : null;
        using var sdk = mode.StartsWith("the OpenTelemetry", StringComparison.Ordinal)
            ? Sdk.CreateTracerProviderBuilder().AddSource(ProxyTap.ActivitySourceName).AddInMemoryExporter(new List<Activity>()).Build()
            : null;
        var seen = new ConcurrentQueue<string>();
        using var upstream = new Stub(async context =>
        {
            seen.Enqueue(context.Request.Headers["traceparent"] ?? "(none)");
            context.Response.StatusCode = 200;
            context.Response.Close();
            await Task.CompletedTask;
        });
        await using var tap = new ProxyTap(new ProxyTapOptions
        {
            ListenPort = Stub.FreePort(), ForwardBaseUri = upstream.Uri, CallerName = "App", ServiceName = "Api",
            Sink = new Recorder(), IdentityResolver = (_, _) => ("places an order", TestId), TraceIdFromIdentity = true,
        });
        await tap.StartAsync();
        for (var i = 0; i < 2; i++)
            (await caller.GetAsync(new Uri(tap.ListenUri, "/orders"))).EnsureSuccessStatusCode();
        foreach (var header in seen)
        {
            var parts = header.Split('-');
            Console.WriteLine($"{mode,-36} forwarded {header}  {(parts.Length == 4 && parts[1] == TestId ? "== test id" : "NOT the test id")}, flags {(parts.Length == 4 ? parts[3] : "?")}");
        }
    }
    return;
}

var dir = Path.Combine(Path.GetTempPath(), $"kronikol-170-acceptance-{(on ? "on" : "off")}-{Guid.NewGuid().ToString("N")[..8]}");
Directory.CreateDirectory(dir);
var spansFile = Path.Combine(dir, "spans.jsonl");
var spanLines = new ConcurrentQueue<string>();

var otlpSink = new Recorder();
await using var otlp = new OtlpTap(new OtlpTapOptions { ListenPort = Stub.FreePort(), Sink = otlpSink });
await otlp.StartAsync();

// The service: reads only traceparent, runs a query under the span it was called by and exports it, as an
// OpenTelemetry service with a database instrumentation would.
using var service = new Stub(async context =>
{
    var traceparent = context.Request.Headers["traceparent"]!.Split('-');
    await Task.Delay(40);
    var start = DateTimeOffset.UtcNow;
    var line = OtlpLine(traceparent[1], ActivitySpanId.CreateRandom().ToString(), traceparent[2], $"SELECT for {traceparent[2][..6]}", start, start.AddMilliseconds(20));
    spanLines.Enqueue(line);
    using (var export = await caller.PostAsync(otlp.TracesEndpoint, new StringContent(line, Encoding.UTF8, "application/json")))
        export.EnsureSuccessStatusCode();
    await Task.Delay(80);
    context.Response.StatusCode = 200;
    context.Response.Close();
});

var tapSink = new Recorder();
await using (var tap = new ProxyTap(new ProxyTapOptions
{
    ListenPort = Stub.FreePort(), ForwardBaseUri = service.Uri, CallerName = "App", ServiceName = "Api",
    Sink = tapSink, IdentityResolver = (_, _) => ("places an order", TestId), TraceIdFromIdentity = on,
}))
{
    await tap.StartAsync();
    var started = DateTimeOffset.UtcNow;
    // Two calls at once, as a mobile app makes them.
    await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        (await caller.GetAsync(new Uri(tap.ListenUri, "/orders"))).EnsureSuccessStatusCode()));
    for (var w = 0; (tapSink.Logs.Count < 4 || otlpSink.Logs.Count < 2) && w < 10_000; w += 20)
        await Task.Delay(20);
    var ended = DateTimeOffset.UtcNow;

    File.WriteAllLines(spansFile, spanLines);
    File.WriteAllLines(Path.Combine(dir, "tap.ndjson"), tapSink.Logs.Select(l => InteractionRecord.FromLog(l).ToJson()));
    File.WriteAllLines(Path.Combine(dir, "otlp.ndjson"), otlpSink.Logs.Select(l => InteractionRecord.FromLog(l).ToJson()));
    File.WriteAllLines(Path.Combine(dir, "tests.ndjson"),
    [
        new TestRunRecord { Event = "start", TestId = TestId, TestName = "places an order", Feature = "Orders", Timestamp = started.AddMilliseconds(-5) }.ToJson(),
        new TestRunRecord { Event = "end", TestId = TestId, Status = "passed", DurationMs = (ended - started).TotalMilliseconds, Timestamp = ended }.ToJson(),
    ]);
}

Console.WriteLine($"TraceIdFromIdentity {(on ? "on" : "off")}: the tap recorded {tapSink.Logs.Count} record(s), the OTLP tap {otlpSink.Logs.Count}");
foreach (var l in tapSink.Logs.Where(l => l.Type == RequestResponseType.Request))
    Console.WriteLine($"  tap call   test {l.TestId}  trace {l.ActivityTraceId}  span {l.ActivitySpanId}");
foreach (var l in otlpSink.Logs.Where(l => l.Type == RequestResponseType.Request))
    Console.WriteLine($"  database   test {l.TestId}  trace {l.ActivityTraceId}  span {l.ActivitySpanId}  {l.CallerName} -> {l.ServiceName}");

var options = IngestPipeline.DefaultOptions();
options.ReportsFolderPath = Path.Combine(dir, "report");
var flowSpans = OtlpTraceReader.ReadJsonLines(spansFile, null).Select(s => s.ToFlowSpan()).ToList();
var result = IngestPipeline.Run(new IngestRequest
{
    InteractionFiles = [Path.Combine(dir, "tap.ndjson"), Path.Combine(dir, "otlp.ndjson")],
    TestsFile = Path.Combine(dir, "tests.ndjson"),
    Options = options,
    FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test"),
    Spans = flowSpans,
});
Console.WriteLine($"ingest: {result.InteractionCount} record(s) into {result.ScenarioCount} scenario(s)");
QueryCommand.Run(["scenarios", result.ReportsDirectory], Console.Out, Console.Error);

// What each tap call's popup holds, from the builder the report uses, over the same records and spans.
var segmentsOf = typeof(InternalFlowSegmentBuilder).GetMethod("SegmentsOf", BindingFlags.NonPublic | BindingFlags.Static)!;
var segments = (Dictionary<string, InternalFlowSegment>)segmentsOf.Invoke(null, [tapSink.Logs.Concat(otlpSink.Logs).ToArray(), flowSpans.ToArray()])!;
foreach (var call in tapSink.Logs.Where(l => l.Type == RequestResponseType.Request))
{
    var shown = segments.TryGetValue($"iflow-{call.RequestResponseId}", out var segment) ? segment.FlowSpans.Select(s => s.Name).ToArray() : [];
    var own = $"SELECT for {call.ActivitySpanId![..6]}";
    Console.WriteLine($"  popup of the tap call {call.ActivitySpanId}: [{string.Join(", ", shown)}]  {(shown.SequenceEqual([own]) ? "its own query only" : "NOT its own query only")}");
}
Console.WriteLine($"files: {dir}");

static string OtlpLine(string trace, string span, string parent, string name, DateTimeOffset start, DateTimeOffset end) =>
    "{\"resourceSpans\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"Api\"}}]},"
    + "\"scopeSpans\":[{\"scope\":{\"name\":\"Npgsql\"},\"spans\":[{"
    + $"\"traceId\":\"{trace}\",\"spanId\":\"{span}\",\"parentSpanId\":\"{parent}\",\"name\":\"{name}\",\"kind\":3,"
    + $"\"startTimeUnixNano\":\"{Nanos(start)}\",\"endTimeUnixNano\":\"{Nanos(end)}\","
    + "\"attributes\":[{\"key\":\"db.system\",\"value\":{\"stringValue\":\"postgresql\"}},"
    + "{\"key\":\"db.name\",\"value\":{\"stringValue\":\"shop\"}},"
    + "{\"key\":\"db.statement\",\"value\":{\"stringValue\":\"SELECT * FROM orders\"}}]}]}]}]}";

static string Nanos(DateTimeOffset at) => ((at.ToUnixTimeMilliseconds() * 1_000_000L)).ToString(CultureInfo.InvariantCulture);

sealed class Recorder : IRequestResponseSink
{
    private readonly List<RequestResponseLog> _logs = [];
    public List<RequestResponseLog> Logs { get { lock (_logs) return _logs.ToList(); } }
    public void Log(RequestResponseLog log) { lock (_logs) _logs.Add(log); }
}

sealed class Stub : IDisposable
{
    private readonly HttpListener _listener = new();

    public Stub(Func<HttpListenerContext, Task> handle)
    {
        var port = FreePort();
        Uri = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(Uri.ToString());
        _listener.Start();
        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }
                _ = Task.Run(() => handle(context));
            }
        });
    }

    public Uri Uri { get; }

    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose() => _listener.Close();
}
