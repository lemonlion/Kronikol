#:package Kronikol.Extensions.ProxyTap@4.18.1
// probe.cs: what a ProxyTap still holds after its callers hang up, path by path (plan §2).
// `dotnet run probe.cs [callers]` runs against the published package. make-local.sh writes probe-local.cs, the same
// file with the #:package line swapped for a #:project reference to the worktree, for the prototype (plan §5).
using System.Collections;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;

var callers = args.Length > 0 ? int.Parse(args[0]) : 10;
var tapAssembly = typeof(ProxyTap).Assembly;
Console.WriteLine($"ProxyTap {tapAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} on .NET {Environment.Version}, {RuntimeInformation.OSDescription}, {callers} caller(s) per path");
Console.WriteLine();

// ---- upstream: /big answers 8 MB, /small 2 bytes, /hold waits for Release() and then resets the tap's connection
var upstreamPort = FreePort();
var upstream = new HttpListener();
upstream.Prefixes.Add($"http://localhost:{upstreamPort}/");
upstream.Start();
var big = new byte[8 * 1024 * 1024];
var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var holding = 0;
_ = Task.Run(async () =>
{
    while (upstream.IsListening)
    {
        HttpListenerContext c;
        try { c = await upstream.GetContextAsync(); } catch { return; }
        _ = Task.Run(async () =>
        {
            try
            {
                await c.Request.InputStream.CopyToAsync(Stream.Null);
                if (c.Request.Url!.AbsolutePath == "/hold")
                {
                    Interlocked.Increment(ref holding);
                    await hold.Task;
                    c.Response.Abort(); // the upstream fails the exchange: the tap's forward throws
                    return;
                }
                var body = c.Request.Url.AbsolutePath == "/big" ? big : "ok"u8.ToArray();
                c.Response.ContentLength64 = body.Length;
                await c.Response.OutputStream.WriteAsync(body);
                c.Response.Close();
            }
            catch { try { c.Response.Abort(); } catch { } }
        });
    }
});

// ---- a failing upstream for path D: a raw socket that reads the request, waits for the release, then resets. The
// managed HttpListener cannot stand in for it: its Abort() writes a 200 with no body when nothing was sent yet.
var rawUpstream = new TcpListener(IPAddress.Loopback, 0);
rawUpstream.Start();
var rawPort = ((IPEndPoint)rawUpstream.LocalEndpoint).Port;
_ = Task.Run(async () =>
{
    while (true)
    {
        Socket s;
        try { s = await rawUpstream.AcceptSocketAsync(); } catch { return; }
        _ = Task.Run(async () =>
        {
            var buf = new byte[8192];
            var seen = "";
            while (!seen.Contains("\r\n\r\n")) { var n = await s.ReceiveAsync(buf); if (n == 0) break; seen += Encoding.ASCII.GetString(buf, 0, n); }
            Interlocked.Increment(ref holding);
            await hold.Task;
            s.LingerState = new LingerOption(true, 0);
            s.Dispose();
        });
    }
});

var results = new List<string>();

// ---- path A: the tap is writing a large answer when the caller resets (RespondAsync's catch, L555-558)
await RunPath("A  reset while the tap writes the answer", async (tap, port, log) =>
{
    var clients = await Connect(port, $"GET /big HTTP/1.1\r\nHost: localhost:{port}\r\n\r\n", callers, receiveBuffer: 16 * 1024);
    foreach (var c in clients) await c.GetStream().ReadExactlyAsync(new byte[1]);
    Reset(clients);
    return "";
});

// ---- path B: the caller resets mid-body (the issue's "mid-request": IOException, or HttpListenerException now and then)
await RunPath("B  reset mid-body", async (tap, port, log) =>
{
    var clients = await Connect(port, $"POST /query HTTP/1.1\r\nHost: localhost:{port}\r\nContent-Length: 1000000\r\n\r\n{{", callers);
    await WaitFor(() => InFlight(tap) == callers, 5000); // accepted and reading the body: never reset before the accept
    Reset(clients);
    return "";
});

// ---- path C: the caller half-closes mid-body (FIN) and keeps reading, so it can see whether the tap lets go
await RunPath("C  FIN mid-body, caller still reading", async (tap, port, log) =>
{
    var clients = await Connect(port, $"POST /query HTTP/1.1\r\nHost: localhost:{port}\r\nContent-Length: 1000000\r\n\r\n{{", callers);
    await WaitFor(() => InFlight(tap) == callers, 5000);
    foreach (var c in clients) c.Client.Shutdown(SocketShutdown.Send);
    var seen = await Task.WhenAll(clients.Select(c => WhatTheCallerSees(c, TimeSpan.FromSeconds(5))));
    var summary = string.Join(", ", seen.GroupBy(s => s).Select(g => $"{g.Count()} {g.Key}"));
    foreach (var c in clients) c.Dispose();
    return $"caller saw: {summary}";
});

// ---- path D: the upstream fails after the caller has reset (TryRespondBadGateway's catch, L573-576)
await RunPath("D  upstream fails after the caller reset", async (tap, port, log) =>
{
    Volatile.Write(ref holding, 0);
    var clients = await Connect(port, $"GET /hold HTTP/1.1\r\nHost: localhost:{port}\r\n\r\n", callers);
    await WaitFor(() => Volatile.Read(ref holding) == callers, 5000);
    Reset(clients);
    await Task.Delay(500);
    hold.TrySetResult();
    return "";
}, forward: new Uri($"http://127.0.0.1:{rawPort}"));

// ---- path E: control. 20 keep-alive requests on one connection must all be answered on that one connection
await RunPath("E  control: keep-alive", async (tap, port, log) =>
{
    using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 1 });
    var ok = 0;
    for (var i = 0; i < 20; i++)
        if ((await http.GetAsync($"http://localhost:{port}/small")).IsSuccessStatusCode) ok++;
    var established = ServerSide(port).Count(c => c.State == TcpState.Established);
    return $"{ok}/20 answered 200, server-side connections established: {established}";
});

upstream.Close();
Console.WriteLine();
foreach (var line in results) Console.WriteLine(line);
return;

// ================================================================================================================

async Task RunPath(string name, Func<ProxyTap, int, List<string>, Task<string>> drive, Uri? forward = null)
{
    var log = new List<string>();
    var port = FreePort();
    var tap = new ProxyTap(new ProxyTapOptions
    {
        ListenPort = port, ForwardBaseUri = forward ?? new Uri($"http://localhost:{upstreamPort}"), CallerName = "app", ServiceName = "api",
        CaptureUnattributedRequests = true, FallbackTestId = "t1", Sink = new NoSink(),
        Log = line => { lock (log) log.Add(line); },
    });
    await tap.StartAsync();
    var baseline = OrphanSockets();
    var note = await drive(tap, port, log);
    await Task.Delay(3000);

    var held = ListenerTables(tap);
    var orphans = OrphanSockets() - baseline;
    var tcp = string.Join(" ", ServerSide(port).GroupBy(c => c.State).Select(g => $"{g.Key}={g.Count()}"));
    var queue = HttpSysActiveRequests(port);
    string[] errors;
    lock (log) errors = log.Where(l => l.Contains("proxy-tap error")).ToArray();
    var errorKinds = string.Join("; ", errors.GroupBy(e => e[(e.IndexOf("proxy-tap error:") + 17)..]).Select(g => $"{g.Count()}x \"{g.Key}\""));
    results.Add($"{name}\n    in flight {InFlight(tap)}, ForwardFailures {tap.ForwardFailures}, handled {tap.RequestsHandled}, captured {tap.RequestsCaptured}" +
                $"\n    listener tables: {held}" +
                (OperatingSystem.IsLinux() ? $"\n    orphaned TCP sockets (fd held, no TCP table entry): {orphans}" : "") +
                $"\n    server-side TCP on the tap's port: {(tcp == "" ? "none" : tcp)}" +
                (queue is null ? "" : $"\n    http.sys active requests in the tap's queue: {queue}") +
                $"\n    'proxy-tap error' lines: {errors.Length}{(errorKinds == "" ? "" : " (" + errorKinds + ")")}" +
                (note == "" ? "" : $"\n    {note}"));

    await tap.DisposeAsync();
    var afterDispose = OperatingSystem.IsLinux() ? $", orphaned sockets after dispose: {OrphanSockets() - baseline}" : "";
    results[^1] += $"\n    after DisposeAsync: listener tables {ListenerTables(tap)}{afterDispose}";
    hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
}

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }

static async Task<List<TcpClient>> Connect(int port, string request, int n, int? receiveBuffer = null)
{
    var list = new List<TcpClient>();
    for (var i = 0; i < n; i++)
    {
        var c = new TcpClient();
        if (receiveBuffer is { } size) c.ReceiveBufferSize = size; // so the answer cannot all sit in socket buffers
        list.Add(c);
        await c.ConnectAsync("localhost", port);
        await c.GetStream().WriteAsync(Encoding.ASCII.GetBytes(request));
    }
    return list;
}

static void Reset(List<TcpClient> clients)
{
    foreach (var c in clients) { c.Client.LingerState = new LingerOption(true, 0); c.Dispose(); }
}

static async Task<string> WhatTheCallerSees(TcpClient c, TimeSpan within)
{
    var buffer = new byte[4096];
    using var cts = new CancellationTokenSource(within);
    try
    {
        // The socket, not GetStream(): TcpClient refuses a stream once the send side is shut down.
        var read = await c.Client.ReceiveAsync(buffer, SocketFlags.None, cts.Token);
        if (read == 0) return "closed by the tap (FIN)";
        var first = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0];
        return $"an answer ({first})";
    }
    catch (OperationCanceledException) { return $"nothing within {within.TotalSeconds:0} s (connection held)"; }
    catch (SocketException ex) { return $"reset by the tap ({ex.SocketErrorCode})"; }
}

static async Task WaitFor(Func<bool> condition, int ms)
{
    for (var waited = 0; !condition() && waited < ms; waited += 20) await Task.Delay(20);
}

static int InFlight(ProxyTap tap) => (int)typeof(ProxyTap).GetField("_inflight", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tap)!;

// Every collection field of the tap's HttpListener, with its count: on the managed listener these are the
// connections and contexts it still holds (names differ by implementation, so they are listed, not assumed).
static string ListenerTables(ProxyTap tap)
{
    var listener = (HttpListener)typeof(ProxyTap).GetField("_listener", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tap)!;
    var parts = new List<string>();
    foreach (var f in typeof(HttpListener).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        if (f.GetValue(listener) is ICollection c && f.Name != "_prefixes" && !f.FieldType.Name.Contains("Prefix"))
            parts.Add($"{f.Name}={c.Count}");
    return parts.Count == 0 ? "(none visible)" : string.Join(" ", parts);
}

static IEnumerable<TcpConnectionInformation> ServerSide(int port) =>
    IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Where(c => c.LocalEndPoint.Port == port);

// Linux: a socket the peer reset leaves /proc/net/tcp but stays an open fd until it is closed, so a leaked
// connection is a socket fd whose inode no table lists.
static int OrphanSockets()
{
    if (!OperatingSystem.IsLinux()) return 0;
    var known = new HashSet<string>();
    foreach (var table in new[] { "tcp", "tcp6", "udp", "udp6", "unix", "raw", "raw6" })
    {
        var path = $"/proc/self/net/{table}";
        if (!File.Exists(path)) continue;
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (table == "unix") { if (cols.Length > 6) known.Add(cols[6]); }
            else if (cols.Length > 9) known.Add(cols[9]);
        }
    }
    var orphans = 0;
    foreach (var fd in Directory.EnumerateFileSystemEntries("/proc/self/fd"))
    {
        string? target;
        try { target = new FileInfo(fd).LinkTarget; } catch { continue; }
        if (target is not null && target.StartsWith("socket:[") && !known.Contains(target[8..^1])) orphans++;
    }
    return orphans;
}

// Windows: http.sys keeps a request "active" until the app sends its last byte or cancels it.
static int? HttpSysActiveRequests(int port)
{
    if (!OperatingSystem.IsWindows()) return null;
    var psi = new System.Diagnostics.ProcessStartInfo("netsh", "http show servicestate view=requestq") { RedirectStandardOutput = true };
    using var p = System.Diagnostics.Process.Start(psi)!;
    var text = p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    // Split on the unindented line only: each queue's URL-group section repeats "Request queue name:" indented.
    foreach (var block in text.Replace("\r\n", "\n").Split("\nRequest queue name:"))
        if (block.Contains($":{port}/", StringComparison.OrdinalIgnoreCase))
        {
            var line = block.Split('\n').FirstOrDefault(l => l.Trim().StartsWith("Active requests:"));
            if (line is not null && int.TryParse(line.Split(':')[1].Trim(), out var active)) return active;
        }
    return -1;
}

sealed class NoSink : IRequestResponseSink { public void Log(RequestResponseLog log) { } }
