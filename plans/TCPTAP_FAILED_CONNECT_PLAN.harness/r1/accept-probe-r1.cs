#:project ../../../src/Kronikol.Extensions.TcpTap/Kronikol.Extensions.TcpTap.csproj
#:project ../../../src/Kronikol.Extensions.Otlp/Kronikol.Extensions.Otlp.csproj
#:project ../../../src/Kronikol.Extensions.ProxyTap/Kronikol.Extensions.ProxyTap.csproj
// accept-probe-r1.cs: accept/accept-probe.cs built against the source tree it sits in (R1), not the 4.20.2 packages.
// accept-probe.cs: does one failed accept stop a tap from accepting for good? (plan §3). Both TcpTap and OtlpTap 4.20.2
// (from nuget.org) return from their accept loop on any SocketException (TcpTap.cs L191, OtlpTap.cs L268), and nothing
// starts it again: the listening socket stays open, so a later client's connect still completes in the kernel.
// ProxyTap's loop (ProxyTap.cs L223-228) returns on any HttpListenerException; its HttpListener accepts connections
// itself (http.sys on Windows, the managed listener elsewhere), so it is in every case below as the third tap.
// `dotnet run accept-probe.cs`; the same file on each OS.
//   E1 (every OS): rounds of 200 clients that connect at once and reset at once (LingerState(true, 0), Socket.Dispose(): an
//      RST alone), so some are reset while they wait in the listen queue; after each round, a liveness check (TcpTap: a
//      PING must come back +PONG through the tap within 3 s; OtlpTap: a GET must be answered within 3 s).
//   E2 (Linux): the process runs out of descriptors at the moment a client arrives (setrlimit lowers RLIMIT_NOFILE, /dev/null
//      handles take what is left, one is freed for the client's own socket), so the tap's accept fails with EMFILE; then
//      every descriptor is given back and the liveness check runs again.
// For each tap it prints IsListening, ConnectionsAccepted and every Log line.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Extensions.TcpTap;
using Kronikol.Tracking;

Console.WriteLine($".NET {Environment.Version}, {RuntimeInformation.OSDescription}, TcpTap {typeof(TcpTap).Assembly.GetName().Version}, OtlpTap {typeof(OtlpTap).Assembly.GetName().Version}");
_ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(6)); Console.WriteLine("WATCHDOG: the probe ran past 6 min and was stopped"); Environment.Exit(2); });

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

var tcpLog = new ConcurrentQueue<string>();
var otlpLog = new ConcurrentQueue<string>();
await using var tcpTap = new RedisTap(new RedisTapOptions
{
    ListenPort = 0, ListenHost = "127.0.0.1", ForwardHost = "127.0.0.1", ForwardPort = pongPort,
    CallerName = "svc", ServiceName = "redis", Sink = new NullSink(), EmitActivities = false, Log = tcpLog.Enqueue,
});
await tcpTap.StartAsync();
await using var otlpTap = new OtlpTap(new OtlpTapOptions { ListenPort = 0, ListenHost = "127.0.0.1", Sink = new NullSink(), Log = otlpLog.Enqueue });
await otlpTap.StartAsync();
var otlpPort = otlpTap.BoundPort;

// An HTTP upstream for the proxy tap: answers any request 200 "ok" and closes.
var httpUpstream = new TcpListener(IPAddress.Loopback, 0);
httpUpstream.Start();
var httpUpstreamPort = ((IPEndPoint)httpUpstream.LocalEndpoint).Port;
_ = Task.Run(async () =>
{
    while (true)
    {
        TcpClient c;
        try { c = await httpUpstream.AcceptTcpClientAsync(); } catch { return; }
        _ = Task.Run(async () =>
        {
            using (c)
            {
                var s = c.GetStream();
                var b = new byte[8192];
                var seen = "";
                try
                {
                    while (!seen.Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var n = await s.ReadAsync(b);
                        if (n == 0) return;
                        seen += Encoding.ASCII.GetString(b, 0, n);
                    }
                    await s.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
                }
                catch { }
            }
        });
    }
});

// HttpListener takes no port 0: take a free one (a probe can live with the race a test may not).
var proxyPortProbe = new TcpListener(IPAddress.Loopback, 0);
proxyPortProbe.Start();
var proxyPort = ((IPEndPoint)proxyPortProbe.LocalEndpoint).Port;
proxyPortProbe.Stop();
var proxyLog = new ConcurrentQueue<string>();
await using var proxyTap = new Kronikol.Extensions.ProxyTap.ProxyTap(new Kronikol.Extensions.ProxyTap.ProxyTapOptions
{
    ListenPort = proxyPort, ListenHost = "localhost", ForwardBaseUri = new Uri($"http://127.0.0.1:{httpUpstreamPort}"),
    CallerName = "web", ServiceName = "api", Sink = new NullSink(), EmitActivities = false, CaptureUnattributedRequests = true,
    Log = proxyLog.Enqueue,
});
await proxyTap.StartAsync();

Console.WriteLine($"TcpTap on 127.0.0.1:{tcpTap.BoundPort}, OtlpTap on 127.0.0.1:{otlpPort}, ProxyTap on localhost:{proxyPort}; alive at start: {await TcpAlive()} / {await OtlpAlive()} / {await ProxyAlive()}");

// E0: a bare TcpListener under the same bursts, whose loop records what AcceptTcpClientAsync throws and keeps going.
Console.WriteLine();
Console.WriteLine("E0 a bare TcpListener (backlog 128, as TcpTap's AcceptBacklog) whose accept loop records every exception and continues");
{
    var bare = new TcpListener(IPAddress.Loopback, 0);
    bare.Start(128);
    var barePort = ((IPEndPoint)bare.LocalEndpoint).Port;
    var thrown = new ConcurrentDictionary<string, int>();
    var accepted = 0;
    using var stop = new CancellationTokenSource();
    var loop = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var c = await bare.AcceptTcpClientAsync(stop.Token);
                Interlocked.Increment(ref accepted);
                _ = Task.Run(() => c.Dispose());
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                var key = ex is SocketException se ? $"SocketException {se.SocketErrorCode} ({se.ErrorCode}): {se.Message}" : $"{ex.GetType().FullName}: {ex.Message}";
                thrown.AddOrUpdate(key, 1, (_, n) => n + 1);
            }
        }
    });
    for (var round = 1; round <= 5; round++)
    {
        var resets = await Burst(barePort);
        await Task.Delay(200);
        Console.WriteLine($"  round {round}: resets {resets}; accepted so far {Volatile.Read(ref accepted)}; thrown so far: {(thrown.IsEmpty ? "nothing" : string.Join("; ", thrown.Select(kv => $"{kv.Key} x{kv.Value}")))}");
    }
    await stop.CancelAsync();
    bare.Stop();
    try { await loop; } catch { }
}

Console.WriteLine();
Console.WriteLine("E1 rounds of 200 connect-then-reset clients");
// Every round bursts both taps and checks both, up to three times a second apart, so a check that only timed out under
// the burst's load reads as "answered on try N", and only three misses in a row read as stopped.
for (var round = 1; round <= 15; round++)
{
    var (rt, ro, rp) = (await Burst(tcpTap.BoundPort), await Burst(otlpPort), await Burst(proxyPort));
    await Task.Delay(200);
    var t = await Liveness(TcpAlive);
    var o = await Liveness(OtlpAlive);
    var p = await Liveness(ProxyAlive);
    Console.WriteLine($"  round {round}: resets {rt} / {ro} / {rp}; TcpTap {Say(t)}, OtlpTap {Say(o)}, ProxyTap {Say(p)}");
}

static string Say(int tries) => tries switch { 0 => "NOT ANSWERING (3 tries)", 1 => "answered", var n => $"answered on try {n}" };

static async Task<int> Liveness(Func<Task<bool>> alive)
{
    for (var i = 1; i <= 3; i++)
    {
        if (await alive())
            return i;
        await Task.Delay(1000);
    }
    return 0;
}

Report("after E1");

if (OperatingSystem.IsLinux())
{
    Console.WriteLine();
    Console.WriteLine("E2 the process out of descriptors when a client arrives (EMFILE), then every descriptor given back");
    var open = Directory.GetFiles("/proc/self/fd").Length;
    Getrlimit(7, out var old);
    var limit = new RLimit { Cur = (ulong)(open + 48), Max = old.Max };
    Console.WriteLine($"  {open} descriptors open; RLIMIT_NOFILE soft {old.Cur} -> {limit.Cur}: {(Setrlimit(7, ref limit) == 0 ? "set" : "FAILED " + Marshal.GetLastPInvokeError())}");
    // A bare listener beside the taps, whose loop records each exception and goes straight round again: what the runtime
    // throws, how often a loop with no pause spins while the descriptors are gone, and whether it serves once they are back.
    var bare = new TcpListener(IPAddress.Loopback, 0);
    bare.Start(128);
    var barePort = ((IPEndPoint)bare.LocalEndpoint).Port;
    var bareThrown = new ConcurrentDictionary<string, int>();
    var bareAccepted = 0;
    using var bareStop = new CancellationTokenSource();
    var bareLoop = Task.Run(async () =>
    {
        while (!bareStop.IsCancellationRequested)
        {
            try
            {
                var c = await bare.AcceptTcpClientAsync(bareStop.Token);
                Interlocked.Increment(ref bareAccepted);
                c.Dispose();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                var key = ex is SocketException se ? $"SocketException {se.SocketErrorCode} ({se.ErrorCode}, native {se.NativeErrorCode}): {se.Message}" : $"{ex.GetType().FullName}: {ex.Message}";
                bareThrown.AddOrUpdate(key, 1, (_, n) => n + 1);
            }
        }
    });

    var hogs = new List<FileStream>();
    try { while (true) hogs.Add(new FileStream("/dev/null", FileMode.Open, FileAccess.Read)); }
    catch (IOException ex) { Console.WriteLine($"  {hogs.Count} /dev/null handles open, then: {ex.Message}"); }

    for (var i = 0; i < 4; i++) { hogs[^1].Dispose(); hogs.RemoveAt(hogs.Count - 1); }
    var c1 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    var c2 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    var c3 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    var c4 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    await c1.ConnectAsync(IPAddress.Loopback, tcpTap.BoundPort);
    await c2.ConnectAsync(IPAddress.Loopback, otlpPort);
    await c3.ConnectAsync(IPAddress.Loopback, barePort);
    await c4.ConnectAsync(IPAddress.Loopback, proxyPort);
    Console.WriteLine("  four clients connected (the kernel completed each handshake); waiting 1 s for the accepts");
    await Task.Delay(1000);
    Console.WriteLine($"  the bare loop in that second: accepted {Volatile.Read(ref bareAccepted)}; thrown: {(bareThrown.IsEmpty ? "nothing" : string.Join("; ", bareThrown.Select(kv => $"{kv.Key} x{kv.Value}")))}");

    foreach (var h in hogs) h.Dispose();
    Setrlimit(7, ref old);
    Console.WriteLine($"  every descriptor given back; RLIMIT_NOFILE soft back to {old.Cur}");
    await Task.Delay(500);
    Console.WriteLine($"  the bare loop 500 ms later: accepted {Volatile.Read(ref bareAccepted)} (the queued client)");
    c1.Dispose(); c2.Dispose(); c3.Dispose(); c4.Dispose();
    using (var again = new TcpClient())
    {
        await again.ConnectAsync(IPAddress.Loopback, barePort);
        await Task.Delay(300);
    }
    Console.WriteLine($"  a new client to the bare listener: accepted {Volatile.Read(ref bareAccepted)}");
    await bareStop.CancelAsync();
    bare.Stop();
    try { await bareLoop; } catch { }
    Console.WriteLine($"  liveness now: TcpTap {Say(await Liveness(TcpAlive))}, OtlpTap {Say(await Liveness(OtlpAlive))}, ProxyTap {Say(await Liveness(ProxyAlive))}");
    await Task.Delay(5000);
    Console.WriteLine($"  and 5 s later: TcpTap {Say(await Liveness(TcpAlive))}, OtlpTap {Say(await Liveness(OtlpAlive))}, ProxyTap {Say(await Liveness(ProxyAlive))}");
    Report("after E2");
}

pongListener.Stop();
Console.WriteLine();
Console.WriteLine("done");
return;

void Report(string when)
{
    Console.WriteLine($"  {when}: TcpTap IsListening {tcpTap.IsListening}, ConnectionsAccepted {tcpTap.ConnectionsAccepted}, LiveConnections {tcpTap.LiveConnections}; OtlpTap IsListening {otlpTap.IsListening}");
    // The accept loops' own tasks (private fields, read by reflection): RanToCompletion means the loop returned.
    var tcpLoop = typeof(TcpTap).GetField("_loop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(tcpTap) as Task;
    var otlpLoop = typeof(OtlpTap).GetField("_acceptLoop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(otlpTap) as Task;
    var proxyLoop = typeof(Kronikol.Extensions.ProxyTap.ProxyTap).GetField("_loop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(proxyTap) as Task;
    Console.WriteLine($"  {when}: TcpTap accept loop {tcpLoop?.Status.ToString() ?? "(field not found)"}; OtlpTap accept loops (WhenAll over its listeners) {otlpLoop?.Status.ToString() ?? "(field not found)"}; ProxyTap request loop {proxyLoop?.Status.ToString() ?? "(field not found)"}, IsListening {proxyTap.IsListening}, RequestsHandled {proxyTap.RequestsHandled}");
    foreach (var line in proxyLog.Where(l => !l.Contains("listening on")).GroupBy(l => l).Select(g => $"{g.Key}  (x{g.Count()})").Take(8))
        Console.WriteLine($"  ProxyTap log: {line}");
    foreach (var line in tcpLog.Where(l => !l.Contains("listening on")).GroupBy(l => l).Select(g => $"{g.Key}  (x{g.Count()})").Take(8))
        Console.WriteLine($"  TcpTap log: {line}");
    foreach (var line in otlpLog.Where(l => !l.Contains("listening on")).GroupBy(l => l).Select(g => $"{g.Key}  (x{g.Count()})").Take(8))
        Console.WriteLine($"  OtlpTap log: {line}");
}

async Task<int> Burst(int port)
{
    var sockets = new Socket[200];
    var connects = new Task[sockets.Length];
    for (var i = 0; i < sockets.Length; i++)
    {
        sockets[i] = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { LingerState = new LingerOption(true, 0) };
        connects[i] = sockets[i].ConnectAsync(IPAddress.Loopback, port);
    }

    try { await Task.WhenAll(connects).WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
    var reset = 0;
    foreach (var s in sockets)
    {
        if (s.Connected) reset++;
        s.Dispose();
    }
    return reset;
}

async Task<bool> TcpAlive()
{
    using var c = new TcpClient { NoDelay = true };
    using var cts = new CancellationTokenSource(3000);
    try
    {
        await c.ConnectAsync(IPAddress.Loopback, tcpTap.BoundPort, cts.Token);
        var s = c.GetStream();
        await s.WriteAsync(Encoding.ASCII.GetBytes("*1\r\n$4\r\nPING\r\n"), cts.Token);
        var b = new byte[64];
        return await s.ReadAsync(b, cts.Token) > 0;
    }
    catch { return false; }
}

async Task<bool> OtlpAlive()
{
    using var c = new TcpClient { NoDelay = true };
    using var cts = new CancellationTokenSource(3000);
    try
    {
        await c.ConnectAsync(IPAddress.Loopback, otlpPort, cts.Token);
        var s = c.GetStream();
        await s.WriteAsync(Encoding.ASCII.GetBytes("GET /alive HTTP/1.1\r\nHost: x\r\nConnection: close\r\n\r\n"), cts.Token);
        var b = new byte[256];
        var n = await s.ReadAsync(b, cts.Token);
        return n > 0 && Encoding.ASCII.GetString(b, 0, n).StartsWith("HTTP/1.1", StringComparison.Ordinal);
    }
    catch { return false; }
}

async Task<bool> ProxyAlive()
{
    using var c = new TcpClient { NoDelay = true };
    using var cts = new CancellationTokenSource(3000);
    try
    {
        await c.ConnectAsync(IPAddress.Loopback, proxyPort, cts.Token);
        var s = c.GetStream();
        await s.WriteAsync(Encoding.ASCII.GetBytes("GET /alive HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"), cts.Token);
        var b = new byte[256];
        var n = await s.ReadAsync(b, cts.Token);
        return n > 0 && Encoding.ASCII.GetString(b, 0, n).StartsWith("HTTP/1.1", StringComparison.Ordinal);
    }
    catch { return false; }
}

[DllImport("libc", EntryPoint = "getrlimit", SetLastError = true)]
static extern int Getrlimit(int resource, out RLimit limit);

[DllImport("libc", EntryPoint = "setrlimit", SetLastError = true)]
static extern int Setrlimit(int resource, ref RLimit limit);

[StructLayout(LayoutKind.Sequential)]
struct RLimit
{
    public ulong Cur;
    public ulong Max;
}

sealed class NullSink : IRequestResponseSink
{
    public void Log(RequestResponseLog log) { }
}
