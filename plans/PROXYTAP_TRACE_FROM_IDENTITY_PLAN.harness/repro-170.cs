#:package Kronikol.Extensions.ProxyTap@4.18.1
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;

// A test id that is also a valid W3C trace id, as a browser suite mints them.
const string TestId = "4bf92f3577b34da6a3ce929d0e0e4736";

var upstreamPort = FreePort();
var upstream = new HttpListener();
upstream.Prefixes.Add($"http://127.0.0.1:{upstreamPort}/");
upstream.Start();
var received = new ConcurrentQueue<string>();
_ = Task.Run(async () =>
{
    while (upstream.IsListening)
    {
        var context = await upstream.GetContextAsync();
        received.Enqueue(context.Request.Headers["traceparent"] ?? "(none)");
        context.Response.StatusCode = 200;
        context.Response.Close();
    }
});

var sink = new Recorder();
var tapPort = FreePort();
await using var tap = new ProxyTap(new ProxyTapOptions
{
    ListenPort = tapPort,
    ForwardBaseUri = new Uri($"http://127.0.0.1:{upstreamPort}"),
    CallerName = "app",
    ServiceName = "api",
    // The caller can't stamp a test id (a mobile app, say), so the host knows whose call it is.
    IdentityResolver = (_, _) => ("A scenario", TestId),
    Sink = sink,
});
await tap.StartAsync();
using var http = new HttpClient { BaseAddress = tap.ListenUri };

async Task Call(string label, string? traceparent = null)
{
    while (received.TryDequeue(out _)) { }
    var before = sink.Logs.Count;
    using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql") { Content = new StringContent("{}") };
    if (traceparent is not null)
        request.Headers.TryAddWithoutValidation("traceparent", traceparent);
    (await http.SendAsync(request)).EnsureSuccessStatusCode();
    var forwarded = received.TryDequeue(out var value) ? value : "(nothing)";
    var traceId = forwarded.Split('-') is { Length: 4 } parts ? parts[1] : "(none)";
    // The tap answers first and records second, so wait for the pair.
    for (var waited = 0; sink.Logs.Count < before + 2 && waited < 5000; waited += 10)
        await Task.Delay(10);
    var recorded = sink.Logs.Last();
    Console.WriteLine($"{label,-44} forwarded trace id {traceId}  {(traceId == TestId ? "== test id" : "!= test id")}  (recorded testId {recorded.TestId}, activityTraceId {recorded.ActivityTraceId})");
}

Console.WriteLine($"Kronikol.Extensions.ProxyTap {typeof(ProxyTap).Assembly.GetName().Version}, test id {TestId}");
for (var i = 1; i <= 3; i++)
    await Call($"no traceparent, call {i}");
await Call("traceparent naming the test id (browser model)", $"00-{TestId}-00f067aa0ba902b7-01");

using var listener = new ActivityListener
{
    ShouldListenTo = source => source.Name == ProxyTap.ActivitySource.Name,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
};
ActivitySource.AddActivityListener(listener);
for (var i = 1; i <= 2; i++)
    await Call($"no traceparent, listener attached, call {i}");

upstream.Stop();

static int FreePort()
{
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    return port;
}

sealed class Recorder : IRequestResponseSink
{
    public ConcurrentQueue<RequestResponseLog> Logs { get; } = new();
    public void Log(RequestResponseLog log) => Logs.Enqueue(log);
}
