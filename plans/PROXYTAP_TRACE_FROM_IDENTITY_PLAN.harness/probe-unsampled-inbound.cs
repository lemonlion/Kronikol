#:package Kronikol.Extensions.ProxyTap@4.18.1
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;

// A sampler shaped like OpenTelemetry's default ParentBased(AlwaysOn): a remote parent that is not sampled is dropped,
// a root is sampled.
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == ProxyTap.ActivitySourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> o) =>
        o.Parent.TraceId != default && !o.Parent.TraceFlags.HasFlag(ActivityTraceFlags.Recorded)
            ? ActivitySamplingResult.None
            : ActivitySamplingResult.AllDataAndRecorded,
};
ActivitySource.AddActivityListener(listener);

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
var tapPort = FreePort();
await using var tap = new ProxyTap(new ProxyTapOptions
{
    ListenPort = tapPort, ForwardBaseUri = new Uri($"http://127.0.0.1:{upstreamPort}"), CallerName = "app", ServiceName = "api",
    CaptureUnattributedRequests = true, FallbackTestId = "t1", Sink = new NoSink(),
});
await tap.StartAsync();
using var http = new HttpClient();
foreach (var flags in new[] { "01", "00" })
{
    var sent = $"00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-{flags}";
    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(tap.ListenUri, "/x"));
    request.Headers.TryAddWithoutValidation("traceparent", sent);
    (await http.SendAsync(request)).EnsureSuccessStatusCode();
    received.TryDequeue(out var got);
    Console.WriteLine($"sent {sent}\n  forwarded {got}  {(got?.Contains("0af7651916cd43dd8448eb211c80319c") == true ? "same trace" : "TRACE REPLACED")}");
}
upstream.Stop();
static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }
sealed class NoSink : IRequestResponseSink { public void Log(RequestResponseLog log) { } }
