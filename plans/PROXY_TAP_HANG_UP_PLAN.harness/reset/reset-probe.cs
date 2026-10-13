#:package Kronikol.Extensions.ProxyTap@4.18.1
// reset-probe.cs: a caller that resets mid-body (Content-Length), by HOW it resets (plan §1 item 4, §2.3: the body read
// of a reset caller throws HttpListenerException 400 on Linux, IOException on macOS per the issue). `dotnet run
// reset-probe.cs`; the same file on each OS. The caller sends `POST` with `Content-Length: 1000000` and one body byte,
// then, once the server holds the request and has been reading its body for 200 ms, resets in one of two ways:
//   pure RST: LingerState(true, 0), then Dispose() of the Socket itself; the peer gets an RST and nothing else;
//   TcpClient: LingerState(true, 0), then TcpClient.Dispose() after GetStream(), as probe.cs path B and listener-probe.cs
//     section 1 reset; NetworkStream.Dispose() calls Shutdown(Both) before Close(), so the peer gets a FIN, then an RST.
// Part 1: a bare HttpListener; what the handler's CopyToAsync of the body throws (5 s cap), five tries of each.
// Part 2: ProxyTap 4.18.1, configured as probe/probe.cs does, 10 callers per way: ForwardFailures, the 'proxy-tap
// error' lines and what is still in flight 3 s after the resets. Its upstream is a raw socket that only counts accepts.
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;

Console.WriteLine($"ProxyTap {typeof(ProxyTap).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} on .NET {Environment.Version}, {RuntimeInformation.OSDescription}, HttpListener {(OperatingSystem.IsWindows() ? "http.sys" : "managed")}");
_ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(3)); Console.WriteLine("WATCHDOG: the probe ran past 3 min and was stopped"); Environment.Exit(2); });

var ways = new (string Label, bool Pure)[]
{
    ("pure RST (LingerState(true, 0), Socket.Dispose())", true),
    ("TcpClient (LingerState(true, 0), TcpClient.Dispose() after GetStream(): a FIN, then an RST)", false),
};

// ---- Part 1: a bare HttpListener
Console.WriteLine();
Console.WriteLine("Part 1: a bare HttpListener; the handler's CopyToAsync of the body, the caller resets mid-body");
{
    var port = FreePort();
    var listener = new HttpListener();
    listener.Prefixes.Add($"http://localhost:{port}/");
    listener.Start();
    foreach (var (label, pure) in ways)
    {
        string? first = null;
        for (var t = 1; t <= 5; t++)
        {
            var client = await Partial(port);
            var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var read = ctx.Request.InputStream.CopyToAsync(Stream.Null);
            await Task.Delay(200);
            Reset(client, pure);
            string outcome;
            try { await read.WaitAsync(TimeSpan.FromSeconds(5)); outcome = "completed with no exception"; }
            catch (TimeoutException) { outcome = "still pending after 5 s"; }
            catch (Exception ex) { outcome = $"threw {Chain(ex)}"; }
            try { ctx.Response.Abort(); } catch { }
            if (first is not null && outcome == first) { Console.WriteLine($"{label} (try {t}): same as try 1"); continue; }
            first ??= outcome;
            Console.WriteLine($"{label} (try {t}): the body read {outcome}");
        }
    }
    listener.Close();
}

// ---- Part 2: through ProxyTap, 10 callers per way
Console.WriteLine();
Console.WriteLine("Part 2: ProxyTap 4.18.1, 10 callers reset mid-body, read 3 s after the resets");
var upstream = new TcpListener(IPAddress.Loopback, 0);
upstream.Start();
var accepted = 0;
_ = Task.Run(async () =>
{
    while (true)
    {
        Socket s;
        try { s = await upstream.AcceptSocketAsync(); } catch { return; }
        Interlocked.Increment(ref accepted);
        s.Dispose();
    }
});
foreach (var (label, pure) in ways)
{
    var log = new List<string>();
    var port = FreePort();
    var tap = new ProxyTap(new ProxyTapOptions
    {
        ListenPort = port, ForwardBaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)upstream.LocalEndpoint).Port}"), CallerName = "app", ServiceName = "api",
        CaptureUnattributedRequests = true, FallbackTestId = "t1", Sink = new NoSink(),
        Log = line => { lock (log) log.Add(line); },
    });
    await tap.StartAsync();
    Volatile.Write(ref accepted, 0);
    var clients = new List<TcpClient>();
    for (var i = 0; i < 10; i++) clients.Add(await Partial(port));
    for (var waited = 0; InFlight(tap) < clients.Count && waited < 5000; waited += 20) await Task.Delay(20);
    var held = InFlight(tap);
    await Task.Delay(200);
    foreach (var c in clients) Reset(c, pure);
    await Task.Delay(3000);
    string[] errors;
    lock (log) errors = log.Where(l => l.Contains("proxy-tap error")).ToArray();
    var kinds = string.Join("; ", errors.GroupBy(e => e[(e.IndexOf("proxy-tap error:") + 17)..]).Select(g => $"{g.Count()}x \"{g.Key}\""));
    Console.WriteLine($"{label}:");
    Console.WriteLine($"    in flight before the resets {held}, 3 s after {InFlight(tap)}; ForwardFailures {tap.ForwardFailures}, RequestsHandled {tap.RequestsHandled}, RequestsCaptured {tap.RequestsCaptured}");
    Console.WriteLine($"    'proxy-tap error' lines: {errors.Length}{(kinds == "" ? "" : $" ({kinds})")}");
    Console.WriteLine($"    connections the upstream accepted: {Volatile.Read(ref accepted)}");
    await tap.DisposeAsync();
}
upstream.Stop();
return;

static async Task<TcpClient> Partial(int port)
{
    var c = new TcpClient();
    await c.ConnectAsync("localhost", port);
    await c.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"POST /q HTTP/1.1\r\nHost: localhost:{port}\r\nContent-Length: 1000000\r\n\r\n{{"));
    return c;
}

static void Reset(TcpClient c, bool pure)
{
    c.Client.LingerState = new LingerOption(true, 0);
    if (pure) c.Client.Dispose();
    else c.Dispose();
}

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }

static int InFlight(ProxyTap tap) => (int)typeof(ProxyTap).GetField("_inflight", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tap)!;

static string Describe(Exception ex)
{
    var extra = ex switch
    {
        HttpListenerException h => $" ErrorCode={h.ErrorCode} NativeErrorCode={h.NativeErrorCode}",
        SocketException s => $" SocketErrorCode={s.SocketErrorCode} ErrorCode={s.ErrorCode} NativeErrorCode={s.NativeErrorCode}",
        _ => "",
    };
    return $"{ex.GetType().FullName} HResult=0x{ex.HResult:X8} ({ex.HResult}){extra} \"{ex.Message}\"";
}

static string Chain(Exception ex)
{
    var parts = new List<string>();
    for (Exception? e = ex; e is not null; e = e.InnerException)
        parts.Add(Describe(e));
    return string.Join(" <- inner ", parts);
}

sealed class NoSink : IRequestResponseSink { public void Log(RequestResponseLog log) { } }
