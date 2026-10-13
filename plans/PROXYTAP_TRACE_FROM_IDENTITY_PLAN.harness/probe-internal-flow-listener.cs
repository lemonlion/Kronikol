#:package Kronikol.Extensions.ProxyTap@4.18.1
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Kronikol.Extensions.ProxyTap;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

// Kronikol's own internal-flow listener (AllData on every source but System.Net.Http), as TestTrackingMessageHandler
// starts it in a test process. Arg "none" runs without it.
var mode = args.Length > 0 ? args[0] : "internalflow";
using var flow = mode == "internalflow" ? new InternalFlowActivityListener() : null;
Console.WriteLine($"mode {mode}, ProxyTap {typeof(ProxyTap).Assembly.GetName().Version}, runtime {Environment.Version}");

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
    IdentityResolver = (_, _) => ("A scenario", "4bf92f3577b34da6a3ce929d0e0e4736"), Sink = sink,
});
await tap.StartAsync();
using var http = new HttpClient();
foreach (var sent in new[] { "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00", null })
{
    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(tap.ListenUri, "/x"));
    if (sent is not null) request.Headers.TryAddWithoutValidation("traceparent", sent);
    (await http.SendAsync(request)).EnsureSuccessStatusCode();
    received.TryDequeue(out var got);
    var flags = got?.Split('-') is { Length: 4 } p ? p[3] : "?";
    Console.WriteLine($"sent      {sent ?? "(none)"}\n  forwarded {got}  sampled flag {(flags == "01" ? "SET" : "CLEARED")}");
}

// The R1 design under the same listener: parent (test trace, default span, Recorded, remote), child client.
var src = ProxyTap.ActivitySource;
using (var server = src.StartActivity("tap sim", ActivityKind.Server, new ActivityContext(ActivityTraceId.CreateFromString("4bf92f3577b34da6a3ce929d0e0e4736"), default, ActivityTraceFlags.Recorded, isRemote: true)))
{
    using var client = src.StartActivity("forward", ActivityKind.Client);
    Console.WriteLine($"R1 sim: server {server?.Id ?? "null"} recorded {server?.Recorded}; client {client?.Id ?? "null"} recorded {client?.Recorded}");
}
upstream.Stop();

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }
sealed class Recorder : IRequestResponseSink { public ConcurrentQueue<RequestResponseLog> Logs { get; } = new(); public void Log(RequestResponseLog log) => Logs.Enqueue(log); }
