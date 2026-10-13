#:package Kronikol.Extensions.ProxyTap@4.18.1
#:package Kronikol.Extensions.Otlp@4.18.1
// The plan audit's measurements (2026-10-13), on the published 4.18.1 packages:
//   A. request targets: absolute form naming another host or the tap, a backslash and a triple slash (both taps);
//   B. what a ConnectTimeout looks like through the tap's HttpClient settings, and HttpRequestError per failure;
//   C. an ActivityListener that throws when a tap span starts or stops;
//   D. the candidate R0 rule (base authority + origin form) over the same targets, on this OS's Uri.
// Run: dotnet run --file probe-audit.cs
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }

HttpListener Serve(int port, string name, List<string> seen)
{
    var l = new HttpListener(); l.Prefixes.Add($"http://127.0.0.1:{port}/"); l.Start();
    _ = Task.Run(async () =>
    {
        while (l.IsListening)
        {
            HttpListenerContext c; try { c = await l.GetContextAsync(); } catch { return; }
            lock (seen) seen.Add($"{name} got {c.Request.HttpMethod} {c.Request.RawUrl}");
            var body = Encoding.UTF8.GetBytes(name); c.Response.ContentLength64 = body.Length;
            try { await c.Response.OutputStream.WriteAsync(body); c.Response.Close(); } catch { }
        }
    });
    return l;
}

static async Task<string> Raw(int port, string requestLine)
{
    using var tcp = new TcpClient();
    await tcp.ConnectAsync(IPAddress.Loopback, port);
    var stream = tcp.GetStream();
    await stream.WriteAsync(Encoding.ASCII.GetBytes($"{requestLine}\r\nHost: localhost:{port}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
    using var reader = new StreamReader(stream, Encoding.ASCII);
    var answer = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(15));
    var parts = answer.Split("\r\n\r\n", 2);
    var body = parts.Length > 1 ? parts[1] : "";
    return $"{answer.Split("\r\n")[0]} / body '{(body.Length > 90 ? body[..90] + "…" : body)}'";
}

void Flush(List<string> seen) { Thread.Sleep(300); lock (seen) { foreach (var s in seen) Console.WriteLine($"     {s}"); seen.Clear(); } }

Console.WriteLine($"Kronikol {typeof(ProxyTap).Assembly.GetName().Version}, .NET {Environment.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
var seen = new List<string>();
var configuredPort = FreePort(); var otherPort = FreePort();
using var configured = Serve(configuredPort, "configured", seen);
using var other = Serve(otherPort, "OTHER", seen);
var records = new List<RequestResponseLog>();

// ---- A. ProxyTap targets
Console.WriteLine("A. ProxyTap");
var tapPort = FreePort();
await using (var tap = new ProxyTap(new ProxyTapOptions
{
    ListenPort = tapPort, ForwardBaseUri = new Uri($"http://127.0.0.1:{configuredPort}"), CallerName = "app", ServiceName = "api",
    CaptureUnattributedRequests = true, FallbackTestId = "t1", Sink = new ListSink(records), EmitActivities = false,
}))
{
    await tap.StartAsync();
    foreach (var target in new[]
             {
                 $"http://127.0.0.1:{otherPort}/abs-other", $"http://localhost:{tapPort}/abs-self",
                 $"/\\127.0.0.1:{otherPort}/backslash", $"///127.0.0.1:{otherPort}/triple", $"/%5C127.0.0.1:{otherPort}/encoded-backslash",
             })
    {
        Console.WriteLine($"  GET {target} -> {await Raw(tapPort, $"GET {target} HTTP/1.1")}");
        Flush(seen);
    }
    lock (records) foreach (var r in records.Where(r => r.Type == RequestResponseType.Request)) Console.WriteLine($"     recorded uri {r.Uri}");
    records.Clear();
}

// ---- A. OtlpTap targets
Console.WriteLine("A. OtlpTap (forward mode)");
var otlpPort = FreePort();
await using (var otlp = new OtlpTap(new OtlpTapOptions { ListenPort = otlpPort, ForwardBaseUri = new Uri($"http://127.0.0.1:{configuredPort}"), Sink = new ListSink(records) }))
{
    await otlp.StartAsync();
    foreach (var line in new[]
             {
                 $"POST /\\127.0.0.1:{otherPort}/v1/traces HTTP/1.1", $"POST ///127.0.0.1:{otherPort}/v1/traces HTTP/1.1",
                 "OPTIONS * HTTP/1.1", $"CONNECT 127.0.0.1:{otherPort} HTTP/1.1", $"GET 127.0.0.1:{otherPort}/no-slash HTTP/1.1",
             })
    {
        string result; try { result = await Raw(otlpPort, line); } catch (Exception ex) { result = $"{ex.GetType().Name}: {ex.Message}"; }
        Console.WriteLine($"  {line} -> {result}");
        Flush(seen);
    }
}

// ---- B. failure shapes through the tap's HttpClient settings (StartAsync L109-121)
Console.WriteLine("B. failure shapes (FailedSend.Status | HttpRequestError | elapsed | Describe)");
async Task Shape(string label, Uri baseUri, TimeSpan connectTimeout, TimeSpan forwardTimeout, string path = "/x")
{
    using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = connectTimeout, UseCookies = false })
    { BaseAddress = baseUri, Timeout = forwardTimeout + TimeSpan.FromSeconds(10) };
    using var lifetime = new CancellationTokenSource();
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
    deadline.CancelAfter(forwardTimeout);
    var watch = Stopwatch.StartNew();
    try
    {
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, path), HttpCompletionOption.ResponseContentRead, deadline.Token);
        Console.WriteLine($"  {label}: answered {(int)response.StatusCode} after {watch.ElapsedMilliseconds} ms");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  {label}: {FailedSend.Status(ex)} | {(ex as HttpRequestException)?.HttpRequestError} | {watch.ElapsedMilliseconds} ms | deadline cancelled={deadline.IsCancellationRequested}");
        Console.WriteLine($"     {FailedSend.Describe(ex)}");
    }
}
await Shape("connect timeout (10.255.255.1, ConnectTimeout 1 s)", new Uri("http://10.255.255.1:81"), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(20));
await Shape("refused", new Uri($"http://127.0.0.1:{FreePort()}"), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20));
await Shape("no such host", new Uri("http://kronikol-no-such-host.invalid"), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20));

// ...and the same connect timeout through a live tap: what the caller and the counters see
var ctPort = FreePort();
await using (var tap = new ProxyTap(new ProxyTapOptions
{
    ListenPort = ctPort, ForwardBaseUri = new Uri("http://10.255.255.1:81"), CallerName = "app", ServiceName = "api",
    ConnectTimeout = TimeSpan.FromSeconds(1), ForwardTimeout = TimeSpan.FromSeconds(20), EmitActivities = false,
}))
{
    await tap.StartAsync();
    using var http = new HttpClient();
    var watch = Stopwatch.StartNew();
    var answer = await http.GetAsync($"http://localhost:{ctPort}/x");
    Console.WriteLine($"  through the tap: {(int)answer.StatusCode} after {watch.ElapsedMilliseconds} ms, body {await answer.Content.ReadAsStringAsync()}, ForwardFailures={tap.ForwardFailures}");
}

// ---- C. an ActivityListener that throws
Console.WriteLine("C. ActivityListener throwing (healthy upstream, request carries a test id)");
foreach (var where in new[] { "ActivityStarted", "ActivityStopped" })
{
    using var listener = new ActivityListener
    {
        ShouldListenTo = s => s.Name == ProxyTap.ActivitySourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };
    if (where == "ActivityStarted") listener.ActivityStarted = _ => throw new InvalidOperationException("listener threw");
    else listener.ActivityStopped = _ => throw new InvalidOperationException("listener threw");
    ActivitySource.AddActivityListener(listener);

    var port = FreePort();
    records.Clear();
    await using var tap = new ProxyTap(new ProxyTapOptions
    {
        ListenPort = port, ForwardBaseUri = new Uri($"http://127.0.0.1:{configuredPort}"), CallerName = "app", ServiceName = "api", Sink = new ListSink(records),
    });
    await tap.StartAsync();
    using var http = new HttpClient();
    using var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/healthy");
    request.Headers.Add("test-tracking-current-test-id", "t1");
    var answer = await http.SendAsync(request);
    await Task.Delay(300);
    Console.WriteLine($"  {where} throws: client got {(int)answer.StatusCode} '{await answer.Content.ReadAsStringAsync()}', records={records.Count}, ForwardFailures={tap.ForwardFailures}, handled={tap.RequestsHandled}");
    Flush(seen);
}

// ---- D. the candidate R0 rule on this OS
Console.WriteLine("D. candidate rule: new Uri(base.GetLeftPart(Authority) + target)");
var forwardBase = new Uri("http://127.0.0.1:5000/base/");
foreach (var target in new[] { "/plain?q=1", "//other:6000/x", "/\\other:6000/x", "///other:6000/x", "/a/../b" })
{
    string Try(Func<Uri> make) { try { var u = make(); return $"{u.Authority}{u.PathAndQuery}"; } catch (Exception ex) { return $"{ex.GetType().Name}: {ex.Message}"; } }
    Console.WriteLine($"  {target,-18} rule -> {Try(() => new Uri(forwardBase.GetLeftPart(UriPartial.Authority) + target)),-36} | today's relative -> {Try(() => new Uri(forwardBase, target))}");
}
Console.WriteLine($"  Uri.TryCreate(\"/v1/traces\", Absolute): {Uri.TryCreate("/v1/traces", UriKind.Absolute, out var abs)} {abs}");
Console.WriteLine($"  new Uri(\"/v1/traces\", RelativeOrAbsolute).IsAbsoluteUri: {new Uri("/v1/traces", UriKind.RelativeOrAbsolute).IsAbsoluteUri}");

sealed class ListSink(List<RequestResponseLog> list) : IRequestResponseSink { public void Log(RequestResponseLog log) { lock (list) list.Add(log); } }
