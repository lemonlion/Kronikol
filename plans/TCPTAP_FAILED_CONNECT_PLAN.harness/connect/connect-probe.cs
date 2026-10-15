#:package Kronikol.Extensions.TcpTap@4.20.2
// connect-probe.cs: what Kronikol.Extensions.TcpTap 4.20.2 (from nuget.org) does when it cannot open its upstream
// connection (plan §2.2), and what the runtime throws on each path (plan §2.3). `dotnet run connect-probe.cs`; the same
// file on each OS. Every case starts a tap (ConnectTimeout 1.5 s unless it says otherwise), connects a client to it,
// sends a Redis PING and reads for up to 10 s, then prints:
//   what the client heard (EOF, a reset, bytes) and after how long;
//   the tap's counters (ConnectionsAccepted, LiveConnections, InteractionsCaptured, DecodeErrors);
//   every Log line, every OnCaptureDegraded event and every Diagnostics() entry.
// Cases: A refused (a port bound but not listening), by IP and by "localhost"; B no answer to the SYN (a listener whose
// backlog is full; TEST-NET-1 192.0.2.1); C a host name that does not resolve; D the DecoderFactory throws, upstream
// healthy; E the tap disposed while its connect waits. F times a client's connect to a tap that listens on "localhost",
// and a tap that forwards to "localhost" when the server listens on 127.0.0.1 only. G is TcpClient.ConnectAsync alone:
// the exception each failure throws, with its SocketErrorCode, its inner exceptions and how long it took.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.Extensions.TcpTap;
using Kronikol.Tracking;

Console.WriteLine($".NET {Environment.Version}, {RuntimeInformation.OSDescription}, TcpTap {typeof(TcpTap).Assembly.GetName().Version}");
_ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(6)); Console.WriteLine("WATCHDOG: the probe ran past 6 min and was stopped"); Environment.Exit(2); });

var ping = Encoding.ASCII.GetBytes("*1\r\n$4\r\nPING\r\n");

// A port that refuses: bound, never listening, so no other process can take it and every SYN gets an RST.
using var refusing = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
refusing.Bind(new IPEndPoint(IPAddress.Loopback, 0));
var refusedPort = ((IPEndPoint)refusing.LocalEndPoint!).Port;

// A port whose listener never accepts and whose queue is full: on Linux a further SYN is dropped, so a connect hangs.
var (fullPort, fullHeld, fullNote) = await FillBacklogAsync();
Console.WriteLine($"Full-backlog listener on 127.0.0.1:{fullPort}: {fullNote}");

// A healthy upstream that answers +PONG to anything, listening on 127.0.0.1 only.
var pongListener = new TcpListener(IPAddress.Loopback, 0);
pongListener.Start();
var pongPort = ((IPEndPoint)pongListener.LocalEndpoint).Port;
_ = Task.Run(async () =>
{
    while (true)
    {
        TcpClient c;
        try { c = await pongListener.AcceptTcpClientAsync(); } catch { return; }
        _ = Task.Run(async () =>
        {
            using (c)
            {
                var s = c.GetStream();
                var b = new byte[4096];
                try { while (await s.ReadAsync(b) > 0) await s.WriteAsync("+PONG\r\n"u8.ToArray()); } catch { }
            }
        });
    }
});

await Case("A1 refused, ForwardHost 127.0.0.1", o => { o.ForwardHost = "127.0.0.1"; o.ForwardPort = refusedPort; });
await Case("A2 refused, ForwardHost localhost", o => { o.ForwardHost = "localhost"; o.ForwardPort = refusedPort; });
await Case("A3 refused, ForwardHost localhost, ConnectTimeout 5 s (the default)", o => { o.ForwardHost = "localhost"; o.ForwardPort = refusedPort; o.ConnectTimeout = TimeSpan.FromSeconds(5); });
await Case("B1 no SYN answer (full backlog), ForwardHost 127.0.0.1", o => { o.ForwardHost = "127.0.0.1"; o.ForwardPort = fullPort; });
await Case("B2 no SYN answer, ForwardHost 192.0.2.1 (TEST-NET-1)", o => { o.ForwardHost = "192.0.2.1"; o.ForwardPort = 6379; });
await Case("C host name that does not resolve", o => { o.ForwardHost = "no-such-host.invalid"; o.ForwardPort = 6379; });
await Case("D DecoderFactory throws, upstream healthy", o =>
{
    o.ForwardHost = "127.0.0.1"; o.ForwardPort = pongPort;
    o.DecoderFactory = _ => throw new InvalidOperationException("factory failed");
});
await Case("D0 control: the same tap with the Redis decoder, upstream healthy", o => { o.ForwardHost = "127.0.0.1"; o.ForwardPort = pongPort; });
await Case("E the tap disposed 700 ms into a connect that waits (B2's address, ConnectTimeout 5 s)",
    o => { o.ForwardHost = "192.0.2.1"; o.ForwardPort = 6379; o.ConnectTimeout = TimeSpan.FromSeconds(5); }, disposeAfterMs: 700);

// F: the dual-stack cost of "localhost".
Console.WriteLine();
Console.WriteLine("F1 a client's connect to a tap listening on ListenHost \"localhost\" (TcpTap binds what ResolveBindAddress returns)");
{
    var log = new ConcurrentQueue<string>();
    await using var tap = new RedisTap(Options(o => { o.ForwardHost = "127.0.0.1"; o.ForwardPort = pongPort; }, log));
    await tap.StartAsync();
    foreach (var host in new[] { "127.0.0.1", "localhost", "127.0.0.1", "localhost" })
    {
        var sw = Stopwatch.StartNew();
        using var c = new TcpClient();
        try
        {
            await c.ConnectAsync(host, tap.BoundPort);
            Console.WriteLine($"  connect to {host}:{tap.BoundPort} took {sw.ElapsedMilliseconds} ms, remote {c.Client.RemoteEndPoint}");
        }
        catch (Exception ex) { Console.WriteLine($"  connect to {host} failed after {sw.ElapsedMilliseconds} ms: {Describe(ex)}"); }
    }
    Console.WriteLine($"  Dns.GetHostAddresses(\"localhost\") = {string.Join(", ", Dns.GetHostAddresses("localhost").Select(a => a.ToString()))}");
}

Console.WriteLine("F2 the first PONG through a tap whose ForwardHost is \"localhost\" or \"127.0.0.1\", the server on 127.0.0.1 only");
foreach (var host in new[] { "127.0.0.1", "localhost", "127.0.0.1", "localhost" })
{
    var log = new ConcurrentQueue<string>();
    await using var tap = new RedisTap(Options(o => { o.ForwardHost = host; o.ForwardPort = pongPort; o.ConnectTimeout = TimeSpan.FromSeconds(5); }, log));
    await tap.StartAsync();
    using var c = new TcpClient { NoDelay = true };
    await c.ConnectAsync(IPAddress.Loopback, tap.BoundPort);
    var sw = Stopwatch.StartNew();
    var s = c.GetStream();
    await s.WriteAsync(ping);
    var heard = await ReadAsync(s, 10_000);
    Console.WriteLine($"  ForwardHost {host}: {heard.What} after {sw.ElapsedMilliseconds} ms");
}

// G: the runtime on its own.
Console.WriteLine();
Console.WriteLine("G TcpClient.ConnectAsync(host, port, token) with CancelAfter(1.5 s), as TcpTap calls it");
foreach (var (label, host, port) in new[]
         {
             ("refused, 127.0.0.1", "127.0.0.1", refusedPort),
             ("refused, localhost", "localhost", refusedPort),
             ("full backlog, 127.0.0.1", "127.0.0.1", fullPort),
             ("TEST-NET-1", "192.0.2.1", 6379),
             ("no such host", "no-such-host.invalid", 6379),
         })
{
    using var c = new TcpClient { NoDelay = true };
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromSeconds(1.5));
    var sw = Stopwatch.StartNew();
    try
    {
        await c.ConnectAsync(host, port, cts.Token);
        Console.WriteLine($"  {label}: CONNECTED after {sw.ElapsedMilliseconds} ms");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  {label}: after {sw.ElapsedMilliseconds} ms, token cancelled {cts.IsCancellationRequested}: {Describe(ex)}");
    }
}

Console.WriteLine("G2 the same connect with the caller's own token cancelled 700 ms in (TEST-NET-1), linked as TcpTap links it");
{
    using var lifetime = new CancellationTokenSource();
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
    linked.CancelAfter(TimeSpan.FromSeconds(5));
    lifetime.CancelAfter(700);
    using var c = new TcpClient();
    var sw = Stopwatch.StartNew();
    try { await c.ConnectAsync("192.0.2.1", 6379, linked.Token); Console.WriteLine("  CONNECTED"); }
    catch (Exception ex) { Console.WriteLine($"  after {sw.ElapsedMilliseconds} ms, lifetime cancelled {lifetime.IsCancellationRequested}: {Describe(ex)}"); }
}

foreach (var h in fullHeld) h.Dispose();
pongListener.Stop();
Console.WriteLine();
Console.WriteLine("done");
return;

RedisTapOptions Options(Action<RedisTapOptions> configure, ConcurrentQueue<string> log, ConcurrentQueue<CaptureDegradation>? degraded = null)
{
    var o = new RedisTapOptions
    {
        ListenPort = 0,
        ListenHost = "localhost",
        CallerName = "svc",
        ServiceName = "redis",
        Sink = new NullSink(),
        EmitActivities = false,
        ConnectTimeout = TimeSpan.FromSeconds(1.5),
        Log = log.Enqueue,
        OnCaptureDegraded = degraded is null ? null : degraded.Enqueue,
    };
    configure(o);
    return o;
}

async Task Case(string label, Action<RedisTapOptions> configure, int disposeAfterMs = 0)
{
    Console.WriteLine();
    Console.WriteLine(label);
    var log = new ConcurrentQueue<string>();
    var degraded = new ConcurrentQueue<CaptureDegradation>();
    var tap = new RedisTap(Options(configure, log, degraded));
    await tap.StartAsync();
    try
    {
        using var c = new TcpClient { NoDelay = true };
        await c.ConnectAsync(IPAddress.Loopback, tap.BoundPort);
        var sw = Stopwatch.StartNew();
        var s = c.GetStream();
        await s.WriteAsync(ping);
        if (disposeAfterMs > 0)
        {
            await Task.Delay(disposeAfterMs);
            var dsw = Stopwatch.StartNew();
            await tap.DisposeAsync();
            Console.WriteLine($"  DisposeAsync took {dsw.ElapsedMilliseconds} ms");
        }

        var heard = await ReadAsync(s, 10_000);
        Console.WriteLine($"  client: {heard.What} after {sw.ElapsedMilliseconds} ms");
        await Task.Delay(300);
        Console.WriteLine($"  counters: accepted {tap.ConnectionsAccepted}, live {tap.LiveConnections}, interactions {tap.InteractionsCaptured}, decode errors {tap.DecodeErrors}, decoding disabled {tap.DecodingDisabledConnections}");
        Console.WriteLine($"  Diagnostics(): {(tap.Diagnostics().Count == 0 ? "(none)" : string.Join(" | ", tap.Diagnostics()))}");
        Console.WriteLine($"  OnCaptureDegraded: {(degraded.IsEmpty ? "(none)" : string.Join(" | ", degraded))}");
    }
    finally
    {
        await tap.DisposeAsync();
    }

    foreach (var line in log.Where(l => !l.Contains("listening on")))
        Console.WriteLine($"  log: {line}");
}

static async Task<(string What, byte[] Bytes)> ReadAsync(NetworkStream s, int capMs)
{
    var buffer = new byte[4096];
    using var cts = new CancellationTokenSource(capMs);
    try
    {
        var n = await s.ReadAsync(buffer, cts.Token);
        return n == 0 ? ("EOF (a FIN)", []) : ($"{n} bytes: {Encoding.ASCII.GetString(buffer, 0, n).Replace("\r\n", "\\r\\n")}", buffer[..n]);
    }
    catch (OperationCanceledException) { return ($"nothing within {capMs} ms (connection still open)", []); }
    catch (IOException ex) when (ex.InnerException is SocketException se) { return ($"{se.SocketErrorCode} ({se.Message})", []); }
    catch (Exception ex) { return ($"{ex.GetType().Name}: {ex.Message}", []); }
}

static string Describe(Exception ex)
{
    var sb = new StringBuilder();
    for (var e = ex; e is not null; e = e.InnerException)
    {
        if (sb.Length > 0) sb.Append(" <- ");
        sb.Append(e.GetType().FullName);
        if (e is SocketException se) sb.Append($" [{se.SocketErrorCode}, {se.ErrorCode}]");
        sb.Append($": {e.Message}");
    }
    return sb.ToString();
}

static async Task<(int Port, List<Socket> Held, string Note)> FillBacklogAsync()
{
    var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    listener.Listen(1);
    var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
    var held = new List<Socket> { listener };
    for (var i = 0; i < 64; i++)
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        held.Add(s);
        var connect = s.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
        var finished = await Task.WhenAny(connect, Task.Delay(500));
        if (finished != connect)
            return (port, held, $"connect {i + 1} did not complete within 500 ms (queue full, SYN dropped)");
        if (connect.IsFaulted)
            return (port, held, $"connect {i + 1} failed: {connect.Exception!.InnerException!.Message} (a full queue is refused here)");
    }
    return (port, held, "64 connects all completed (no queue limit seen)");
}

sealed class NullSink : IRequestResponseSink
{
    public void Log(RequestResponseLog log) { }
}
