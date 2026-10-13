#:package Kronikol.Extensions.ProxyTap@4.18.1
#:package OpenTelemetry@1.19.1
#:package OpenTelemetry.Exporter.InMemory@1.19.1
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;
using OpenTelemetry;
using OpenTelemetry.Trace;

// The real OpenTelemetry SDK, as a service host would configure it: AddSource + an exporter, default sampler
// (ParentBased(AlwaysOn)), then the same with a ratio sampler and with AlwaysOff.
var samplerName = args.Length > 0 ? args[0] : "default";
var exported = new List<Activity>();
var builder = Sdk.CreateTracerProviderBuilder().AddSource(ProxyTap.ActivitySourceName).AddSource("r1-sim").AddInMemoryExporter(exported);
builder = samplerName switch
{
    "ratio0" => builder.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.0))),
    "alwaysoff" => builder.SetSampler(new AlwaysOffSampler()),
    _ => builder,
};
using var provider = builder.Build();
Console.WriteLine($"OpenTelemetry {typeof(Sdk).Assembly.GetName().Version}, sampler {samplerName}, ProxyTap {typeof(ProxyTap).Assembly.GetName().Version}");

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
        received.Enqueue(c.Request.Headers["traceparent"] ?? "(none)");
        c.Response.StatusCode = 200;
        c.Response.Close();
    }
});
var sink = new Recorder();
var tapPort = FreePort();
await using var tap = new ProxyTap(new ProxyTapOptions
{
    ListenPort = tapPort, ForwardBaseUri = new Uri($"http://127.0.0.1:{upstreamPort}"), CallerName = "app", ServiceName = "api",
    CaptureUnattributedRequests = true, FallbackTestId = "t1", Sink = sink,
});
await tap.StartAsync();
using var http = new HttpClient();
foreach (var sent in new[] { "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00", null })
{
    exported.Clear();
    var before = sink.Logs.Count;
    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(tap.ListenUri, "/x"));
    if (sent is not null) request.Headers.TryAddWithoutValidation("traceparent", sent);
    (await http.SendAsync(request)).EnsureSuccessStatusCode();
    received.TryDequeue(out var got);
    for (var w = 0; sink.Logs.Count < before + 2 && w < 5000; w += 10) await Task.Delay(10);
    provider.ForceFlush();
    var rec = sink.Logs.Last();
    Console.WriteLine($"sent      {sent ?? "(none)"}\n  forwarded {got}  {(sent is null ? "" : got?.Contains("0af7651916cd43dd8448eb211c80319c") == true ? "same trace" : "TRACE REPLACED")}\n  record activityTraceId {rec.ActivityTraceId} spanId {rec.ActivitySpanId}");
    foreach (var a in exported) Console.WriteLine($"  exported {a.Kind,-6} {a.DisplayName,-14} trace {a.TraceId} parentSpan {a.ParentSpanId} recorded {a.Recorded}");
    if (exported.Count == 0) Console.WriteLine("  exported nothing");
}

// R1's design simulated on the same provider: a server span under (test trace, default span id, Recorded, remote),
// a client span under it, as ProxyTap would start them.
exported.Clear();
var sim = new ActivitySource("r1-sim");
var testTrace = ActivityTraceId.CreateFromString("4bf92f3577b34da6a3ce929d0e0e4736");
using (var server = sim.StartActivity("tap app→api", ActivityKind.Server, new ActivityContext(testTrace, default, ActivityTraceFlags.Recorded, isRemote: true)))
{
    using var client = sim.StartActivity("forward", ActivityKind.Client);
    Console.WriteLine($"R1 sim: server {(server is null ? "null" : $"{server.Id} recorded {server.Recorded} allData {server.IsAllDataRequested}")}; client {(client is null ? "null" : $"{client.Id} recorded {client.Recorded}")}");
}
provider.ForceFlush();
foreach (var a in exported) Console.WriteLine($"  exported {a.Kind,-6} {a.DisplayName,-14} trace {a.TraceId} parentSpan {a.ParentSpanId} hasRemoteParent {a.HasRemoteParent}");
if (exported.Count == 0) Console.WriteLine("  exported nothing");
upstream.Stop();

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }
sealed class Recorder : IRequestResponseSink { public ConcurrentQueue<RequestResponseLog> Logs { get; } = new(); public void Log(RequestResponseLog log) => Logs.Enqueue(log); }
