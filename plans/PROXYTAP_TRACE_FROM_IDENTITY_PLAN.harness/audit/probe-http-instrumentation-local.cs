#:project ../../../src/Kronikol.Extensions.ProxyTap/Kronikol.Extensions.ProxyTap.csproj
#:package OpenTelemetry@1.19.1
#:package OpenTelemetry.Instrumentation.Http@*
#:package OpenTelemetry.Exporter.InMemory@1.19.1
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Kronikol.Extensions.ProxyTap;
using Kronikol.InternalFlow;
using Kronikol.Tracking;
using OpenTelemetry;
using OpenTelemetry.Trace;

// Audit D3/D4: what reaches the service behind a tap whose own process instruments HttpClient, or that was started
// under an activity. The tap forwards with an HttpClient of its own, and that client's DiagnosticsHandler runs whatever
// instrumentation the process has. Modes:
//   none        nothing listening
//   otel-http   OpenTelemetry with AddHttpClientInstrumentation (default propagators), no Kronikol.ProxyTap source
//   otel-http+kronikol   the same, plus Kronikol's InternalFlowActivityListener (as in a test process)
//   otel-http+tap        OpenTelemetry with the tap's source and AddHttpClientInstrumentation
//   ambient     nothing listening, the tap started under an activity; SynthesizeTraceparent off
//   ambient+tap the tap started under an activity, a listener on the tap's source only; the option off
var mode = args.Length > 0 ? args[0] : "none";
// "inbound" as the second argument: the caller sends a sampled traceparent of its own, as a browser test does.
var inbound = args.Length > 1 && args[1] == "inbound" ? "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01" : null;
const string TestId = "4bf92f3577b34da6a3ce929d0e0e4736";
var exported = new List<Activity>();
TracerProvider? provider = mode switch
{
    "otel-http" or "otel-http+kronikol" => Sdk.CreateTracerProviderBuilder().AddHttpClientInstrumentation().AddInMemoryExporter(exported).Build(),
    "otel-http+tap" => Sdk.CreateTracerProviderBuilder().AddSource(ProxyTap.ActivitySourceName).AddHttpClientInstrumentation().AddInMemoryExporter(exported).Build(),
    _ => null,
};
using var internalFlow = mode == "otel-http+kronikol" ? new InternalFlowActivityListener() : null;
using var tapListener = mode == "ambient+tap" ? new ActivityListener
{
    ShouldListenTo = s => s.Name == ProxyTap.ActivitySourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
} : null;
if (tapListener is not null) ActivitySource.AddActivityListener(tapListener);
Console.WriteLine($"mode {mode}; ProxyTap {typeof(ProxyTap).Assembly.GetName().Version}; OpenTelemetry {typeof(Sdk).Assembly.GetName().Version}");

var upstreamPort = FreePort();
var upstream = new HttpListener();
upstream.Prefixes.Add($"http://127.0.0.1:{upstreamPort}/");
upstream.Start();
var received = new ConcurrentQueue<string>();
_ = Task.Run(async () =>
{
    while (upstream.IsListening)
    {
        var c = await upstream.GetContextAsync();
        received.Enqueue($"{c.Request.Headers["traceparent"] ?? "(none)"} baggage={c.Request.Headers["baggage"] ?? "(none)"}");
        c.Response.StatusCode = 200;
        c.Response.Close();
    }
});

var sink = new Recorder();
var ambient = mode.StartsWith("ambient", StringComparison.Ordinal) ? new Activity("tap start-up").Start() : null;
ambient?.AddBaggage("startup", "yes");
await using var tap = new ProxyTap(new ProxyTapOptions
{
    ListenPort = FreePort(), ForwardBaseUri = new Uri($"http://127.0.0.1:{upstreamPort}"), CallerName = "app", ServiceName = "api",
    Sink = sink, IdentityResolver = (_, _) => ("test", TestId), TraceIdFromIdentity = mode != "ambient+tap",
    SynthesizeTraceparent = mode != "ambient",
});
await tap.StartAsync();
if (ambient is not null)
{
    // Still running, as a fixture's activity is while its tests run; only this flow leaves it.
    Console.WriteLine($"ambient activity at start-up: {ambient.Id}");
    Activity.Current = null;
}

// The caller is not instrumented (a mobile app's requests reach the tap with no trace): no DiagnosticsHandler.
using var http = new HttpClient(new SocketsHttpHandler { ActivityHeadersPropagator = null });
for (var i = 0; i < 2; i++)
{
    var before = sink.Logs.Count;
    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(tap.ListenUri, "/x"));
    if (inbound is not null) request.Headers.TryAddWithoutValidation("traceparent", inbound);
    (await http.SendAsync(request)).EnsureSuccessStatusCode();
    received.TryDequeue(out var got);
    for (var w = 0; sink.Logs.Count < before + 2 && w < 5000; w += 10) await Task.Delay(10);
    var rec = sink.Logs.Last();
    var trace = got?.Split('-') is { Length: >= 4 } parts ? parts[1] : "?";
    Console.WriteLine($"request {i + 1}: sent {inbound ?? "(none)"}, forwarded {got}\n  forwarded trace {(trace == TestId ? "== test id" : inbound is not null && trace == "0af7651916cd43dd8448eb211c80319c" ? "== the caller's" : trace == rec.ActivityTraceId ? "== the record's trace" : "DIFFERS from the record's")}; record activityTraceId {rec.ActivityTraceId} spanId {rec.ActivitySpanId}");
}
provider?.ForceFlush();
foreach (var a in exported) Console.WriteLine($"  exported {a.Kind,-6} {a.DisplayName,-24} trace {a.TraceId} parentSpan {a.ParentSpanId} recorded {a.Recorded}");
provider?.Dispose();
ambient?.Stop();
upstream.Stop();

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }
sealed class Recorder : IRequestResponseSink { public ConcurrentQueue<RequestResponseLog> Logs { get; } = new(); public void Log(RequestResponseLog log) => Logs.Enqueue(log); }
