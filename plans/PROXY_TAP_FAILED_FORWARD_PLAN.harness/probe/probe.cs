#:package Kronikol.Extensions.ProxyTap@4.18.1
// What ProxyTap 4.18.1 does on each way a request can fail after the tap took it.
// Run: dotnet run --file probe.cs   (output kept beside it as probe-output-4.18.1.txt)
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }

var started = new ConcurrentQueue<Activity>();
var stopped = new ConcurrentQueue<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == ProxyTap.ActivitySourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStarted = a => started.Enqueue(a),
    ActivityStopped = a => stopped.Enqueue(a),
};
ActivitySource.AddActivityListener(listener);

Console.WriteLine($"Kronikol.Extensions.ProxyTap {typeof(ProxyTap).Assembly.GetName().Version}, .NET {Environment.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

// A refused connection: bound, never listening.
var closed = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
closed.Bind(new IPEndPoint(IPAddress.Loopback, 0));
var closedPort = ((IPEndPoint)closed.LocalEndPoint!).Port;

// An upstream that accepts and never answers.
var silent = new TcpListener(IPAddress.Loopback, 0); silent.Start();
var silentPort = ((IPEndPoint)silent.LocalEndpoint).Port;
var held = new ConcurrentBag<TcpClient>();
_ = Task.Run(async () => { while (true) { try { held.Add(await silent.AcceptTcpClientAsync()); } catch { return; } } });

// An upstream that sends a status line and half its declared body, then closes.
var broken = new TcpListener(IPAddress.Loopback, 0); broken.Start();
var brokenPort = ((IPEndPoint)broken.LocalEndpoint).Port;
_ = Task.Run(async () =>
{
    while (true)
    {
        TcpClient c; try { c = await broken.AcceptTcpClientAsync(); } catch { return; }
        _ = Task.Run(async () =>
        {
            var s = c.GetStream(); var buf = new byte[8192]; await s.ReadAsync(buf);
            await s.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 100\r\nContent-Type: text/plain\r\n\r\n0123456789"));
            await s.FlushAsync(); c.Client.Shutdown(SocketShutdown.Both); c.Dispose();
        });
    }
});

// An upstream that answers 200 with a body labelled br that is not brotli.
var brPort = FreePort();
var br = new HttpListener(); br.Prefixes.Add($"http://127.0.0.1:{brPort}/"); br.Start();
_ = Task.Run(async () =>
{
    while (br.IsListening)
    {
        HttpListenerContext c; try { c = await br.GetContextAsync(); } catch { return; }
        var body = Encoding.ASCII.GetBytes("this is not brotli at all, just text");
        c.Response.StatusCode = 200; c.Response.Headers["Content-Encoding"] = "br"; c.Response.ContentLength64 = body.Length;
        await c.Response.OutputStream.WriteAsync(body); c.Response.Close();
    }
});

// A healthy upstream.
var okPort = FreePort();
var ok = new HttpListener(); ok.Prefixes.Add($"http://127.0.0.1:{okPort}/"); ok.Start();
var okSeen = 0;
_ = Task.Run(async () =>
{
    while (ok.IsListening)
    {
        HttpListenerContext c; try { c = await ok.GetContextAsync(); } catch { return; }
        Interlocked.Increment(ref okSeen);
        var body = Encoding.UTF8.GetBytes("{\"ok\":true}");
        c.Response.StatusCode = 200; c.Response.ContentType = "application/json"; c.Response.ContentLength64 = body.Length;
        await c.Response.OutputStream.WriteAsync(body); c.Response.Close();
    }
});

async Task Case(string label, int upstreamPort, Action<ProxyTapOptions>? configure = null, IRequestResponseSink? sinkOverride = null)
{
    while (started.TryDequeue(out _)) { }
    while (stopped.TryDequeue(out _)) { }
    var records = new List<RequestResponseLog>();
    var log = new ConcurrentQueue<string>();
    var options = new ProxyTapOptions
    {
        ListenPort = FreePort(), ForwardBaseUri = new Uri($"http://127.0.0.1:{upstreamPort}"),
        CallerName = "app", ServiceName = "api", Name = "app→api",
        IdentityResolver = (_, _) => ("A scenario", "t1"),
        Sink = sinkOverride ?? new ListSink(records),
        Log = m => log.Enqueue(m),
    };
    configure?.Invoke(options);
    var seenBefore = Volatile.Read(ref okSeen);
    await using var tap = new ProxyTap(options);
    await tap.StartAsync();
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    string outcome;
    try
    {
        using var answer = await http.PostAsync(new Uri(tap.ListenUri, "/query"), new StringContent("{}", Encoding.UTF8, "application/json"));
        var text = await answer.Content.ReadAsStringAsync();
        string json;
        try { JsonDocument.Parse(text); json = "valid JSON"; } catch (JsonException) { json = "NOT valid JSON"; }
        outcome = $"client got HTTP {(int)answer.StatusCode}, body {text.Length} chars ({json}): {text.Replace("\r", "\\r").Replace("\n", "\\n")}";
    }
    catch (Exception ex) { outcome = $"client threw {ex.GetType().Name}: {ex.Message}"; }
    await Task.Delay(400);
    Console.WriteLine($"== {label}");
    Console.WriteLine($"   {outcome}");
    Console.WriteLine($"   upstream reached: {(upstreamPort == okPort ? (Volatile.Read(ref okSeen) - seenBefore).ToString() : "n/a")}; sink got {records.Count} record(s){(records.Count > 0 ? ": " + string.Join(", ", records.Select(r => $"{r.Type} {r.StatusCode?.ToString() ?? "-"}{(r.Error is null ? "" : " error=" + r.Error)}")) : "")}");
    Console.WriteLine($"   ForwardFailures={tap.ForwardFailures} RequestsHandled={tap.RequestsHandled} RequestsCaptured={tap.RequestsCaptured}");
    foreach (var d in tap.Diagnostics()) Console.WriteLine($"   diagnostic: {d.Message}");
    foreach (var l in log.Where(l => l.Contains("error"))) Console.WriteLine($"   log: {l}");
    Console.WriteLine($"   activities started {started.Count} ({string.Join(", ", started.Select(a => a.Kind))}), stopped {stopped.Count} ({string.Join(", ", stopped.Select(a => $"{a.Kind}:{a.Status}"))})");
}

await Case("1. upstream refuses the connection", closedPort);
await Case("2. upstream accepts and never answers (ForwardTimeout 1 s)", silentPort, o => o.ForwardTimeout = TimeSpan.FromSeconds(1));
await Case("3. upstream sends half its declared body and closes", brokenPort);
await Case("4. IdentityResolver throws (message with a quote and a line break)", okPort, o => o.IdentityResolver = (_, _) => throw new KeyNotFoundException("no test holds \"device-7\"\nsecond line"));
await Case("5. sink throws", okPort, sinkOverride: new ThrowingSink());
await Case("6. upstream answers 200 with Content-Encoding: br and a body that is not brotli", brPort);
await Case("7. healthy upstream (control)", okPort);

// What the forward throws in each case, in FailedSend's words, and what BrotliStream throws on garbage.
using var plain = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(5) }) { Timeout = TimeSpan.FromSeconds(30) };
async Task Throws(string label, Func<Task> act)
{
    try { await act(); Console.WriteLine($"-- {label}: no exception"); }
    catch (Exception ex) { Console.WriteLine($"-- {label}: {FailedSend.Status(ex)} | {FailedSend.Describe(ex)}"); }
}
await Throws("refused", () => plain.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{closedPort}/"), HttpCompletionOption.ResponseContentRead));
await Throws("caller token CancelAfter(1 s), no answer", async () => { using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)); await plain.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{silentPort}/"), HttpCompletionOption.ResponseContentRead, cts.Token); });
await Throws("HttpClient.Timeout 1 s, no answer", async () => { using var c = new HttpClient { Timeout = TimeSpan.FromSeconds(1) }; await c.GetAsync($"http://127.0.0.1:{silentPort}/"); });
await Throws("half body (ResponseContentRead)", () => plain.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{brokenPort}/"), HttpCompletionOption.ResponseContentRead));
await Throws("BrotliStream over garbage", async () => { using var s = new BrotliStream(new MemoryStream(Encoding.ASCII.GetBytes("this is not brotli at all, just text")), CompressionMode.Decompress); using var r = new StreamReader(s); await r.ReadToEndAsync(); });
Console.WriteLine($"-- ProxyTap.TryDecodeBody(garbage, \"br\"): {Try(() => ProxyTap.TryDecodeBody(Encoding.ASCII.GetBytes("this is not brotli at all, just text"), "br", out _).ToString())}");

static string Try(Func<string> f) { try { return f(); } catch (Exception ex) { return $"threw {ex.GetType().Name}: {ex.Message}"; } }

sealed class ListSink(List<RequestResponseLog> list) : IRequestResponseSink
{
    public void Log(RequestResponseLog log) { lock (list) list.Add(log); }
}

sealed class ThrowingSink : IRequestResponseSink
{
    public void Log(RequestResponseLog log) => throw new IOException("disk full");
}
