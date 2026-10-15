#:package Kronikol.Extensions.TcpTap@4.20.2
#:property PublishAot=false
// tcptap-stall-probe.cs (TAP_CALLER_TIME_LIMITS_PLAN, #173): what TcpTap 4.20.2 does with a pair one side has stopped
// on, sampled every 15 s. `dotnet run tcptap-stall-probe.cs [seconds] [pairs] [reap-seconds]` (90 s, 3, 20 s by default).
//   T1   the client sends 7 bytes then half-closes (Shutdown(Send)); the upstream reads, stays open and silent
//   T2   the upstream half-closes as soon as it accepts; the client stays open and silent (it reads, never writes)
//   T3   the upstream writes 8 MB at once; the client never reads (receive buffer 16 KiB)
//   T4   control: an idle pair, nothing sent either way (it must stay open)
//   T3N  T3 with ReapStuckConnectionsAfter on, same plain TcpTap and pass-through decoder: the reaper reads only
//        IProtocolDecoder.OldestUnansweredSince, which a decoder that tracks nothing leaves null
//   T3R  T3 through RedisTap with ReapStuckConnectionsAfter on: the client sends a RESP GET and the upstream answers an
//        8 MB bulk string, so the Redis decoder holds the command pending until the reply's last byte is decoded
// A plain TcpTap needs a DecoderFactory (Validate throws without one); T1 to T4 and T3N use a decoder that does nothing.
// Each case has its own tap and upstream; options are the defaults (reaper off) except where named. A side that may read
// keeps one receive pending, so a FIN or a reset is timed when it arrives; every socket of every pair, both ends, is also
// watched in the TCP table once a second.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.Extensions.TcpTap;
using Kronikol.Tracking;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var seconds = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 90;
var pairCount = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
var reapAfter = TimeSpan.FromSeconds(args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 20);
const int SampleEvery = 15;
var clock = Stopwatch.StartNew();
double Now() => clock.Elapsed.TotalSeconds;

Console.WriteLine($"Kronikol.Extensions.TcpTap {Version(typeof(TcpTap))} on .NET {Environment.Version}, {RuntimeInformation.OSDescription} ({RuntimeInformation.RuntimeIdentifier})");
Console.WriteLine($"{pairCount} pair(s) per case; a sample every {SampleEvery} s to {seconds} s; ReapStuckConnectionsAfter {reapAfter.TotalSeconds:0} s for T3N and T3R, default (null, off) elsewhere; started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
var defaults = new TcpTapOptions();
Console.WriteLine($"TcpTapOptions defaults: ReapStuckConnectionsAfter {(defaults.ReapStuckConnectionsAfter?.ToString() ?? "null")}, ConnectTimeout {defaults.ConnectTimeout}, DrainTimeout {defaults.DrainTimeout}, ReadBufferBytes {defaults.ReadBufferBytes:N0}");
Console.WriteLine();

var cases = new List<Case>
{
    new("T1", "client sends 7 B then Shutdown(Send); upstream reads, stays open and silent", Upstream.Behaviour.Silent, Redis: false, Reap: false),
    new("T2", "upstream Shutdown(Send) on accept; client open and silent", Upstream.Behaviour.HalfCloseAtOnce, Redis: false, Reap: false),
    new("T3", "upstream writes 8 MB; client never reads", Upstream.Behaviour.Write8MB, Redis: false, Reap: false),
    new("T4", "control: idle pair, nothing sent", Upstream.Behaviour.Silent, Redis: false, Reap: false),
    new("T3N", $"T3, reaper on ({reapAfter.TotalSeconds:0} s), pass-through decoder", Upstream.Behaviour.Write8MB, Redis: false, Reap: true),
    new("T3R", $"T3 through RedisTap, reaper on ({reapAfter.TotalSeconds:0} s): RESP GET, 8 MB bulk reply", Upstream.Behaviour.RespBulk8MB, Redis: true, Reap: true),
};

foreach (var c in cases)
{
    c.Upstream = new Upstream(c.Behave, Now);
    Action<string> log = line => { lock (c.Log) c.Log.Add(line); };
    if (c.Redis)
    {
        var options = new RedisTapOptions
        {
            ListenPort = 0, ForwardHost = "127.0.0.1", ForwardPort = c.Upstream.Port, CallerName = "svc", Name = c.Id,
            Sink = new NoSink(), EmitActivities = false, Log = log, ReapStuckConnectionsAfter = c.Reap ? reapAfter : null,
        };
        c.Tap = new RedisTap(options);
    }
    else
    {
        var options = new TcpTapOptions
        {
            ListenPort = 0, ForwardHost = "127.0.0.1", ForwardPort = c.Upstream.Port, CallerName = "svc", ServiceName = "raw", Name = c.Id,
            Sink = new NoSink(), EmitActivities = false, Log = log, DecoderFactory = _ => new NullDecoder(),
            ReapStuckConnectionsAfter = c.Reap ? reapAfter : null,
        };
        c.Tap = new TcpTap(options);
    }

    await c.Tap.StartAsync();
}

foreach (var c in cases)
{
    var port = c.Tap!.BoundPort;
    for (var i = 0; i < pairCount; i++)
    {
        var neverReads = c.Id is "T3" or "T3N" or "T3R";
        var client = await Peer.ConnectAsync(port, neverReads ? 16 * 1024 : null);
        var s = client.Socket;
        switch (c.Id)
        {
            case "T1":
                await s.SendAsync("hello\r\n"u8.ToArray().AsMemory(), SocketFlags.None);
                s.Shutdown(SocketShutdown.Send);
                break;
            case "T3R":
                await s.SendAsync("*2\r\n$3\r\nGET\r\n$1\r\nk\r\n"u8.ToArray().AsMemory(), SocketFlags.None);
                break;
        }

        if (!neverReads)
            client.Listen(Now);
        c.Clients.Add(client);
    }
}

// every upstream has accepted its pairs: the tap connects upstream as it accepts
foreach (var c in cases)
    await WaitForAsync(() => c.Upstream!.Connections.Count == pairCount, 5000);
var origin = Now();
foreach (var c in cases)
{
    foreach (var p in c.Clients)
        p.StallStart = origin;
    foreach (var p in c.Upstream!.Connections)
        p.StallStart = origin;
}

var sampleTimes = Enumerable.Range(0, seconds / SampleEvery + 1).Select(i => i * SampleEvery).ToList();
if (sampleTimes[^1] != seconds)
    sampleTimes.Add(seconds);

foreach (var t in sampleTimes)
{
    while (Now() < origin + t)
    {
        ObserveAll(cases, Now() - origin);
        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(origin + t - Now(), 0, 1)));
    }

    ObserveAll(cases, Now() - origin);
    Console.WriteLine($"---- t = {t} s");
    foreach (var c in cases)
    {
        Console.WriteLine($"  {c.Id,-4} {Counters(c.Tap!)}");
        Console.WriteLine($"       clients:  {Group(c.Clients.Select(p => p.Describe(c.Id is "T3" or "T3N" or "T3R")))}");
        Console.WriteLine($"       upstream: {Group(c.Upstream!.Connections.Select(p => p.Describe(false)))}");
    }
}

foreach (var c in cases)
{
    var neverReads = c.Id is "T3" or "T3N" or "T3R";
    c.Summary = $"live {string.Join(" > ", c.LiveHistory)}; clients: {Group(c.Clients.Select(p => p.Describe(neverReads) + p.History()))}; upstream: {Group(c.Upstream!.Connections.Select(p => p.Describe(false) + p.History()))}";
}

Console.WriteLine();
Console.WriteLine("---- DisposeAsync");
foreach (var c in cases)
{
    var at = Now() - origin;
    var watch = Stopwatch.StartNew();
    await c.Tap!.DisposeAsync();
    watch.Stop();
    c.DisposeSeconds = watch.Elapsed.TotalSeconds;
    await Task.Delay(1500);
    ObserveAll([c], Now() - origin);
    Console.WriteLine($"  {c.Id,-4} DisposeAsync at t = {at:0} s took {watch.Elapsed.TotalMilliseconds:N0} ms | {Counters(c.Tap)}");
    lock (c.Log)
        foreach (var line in c.Log.Where(l => !l.Contains("listening on", StringComparison.Ordinal)))
            Console.WriteLine($"       log: {line}");
    foreach (var d in c.Tap.Diagnostics())
        Console.WriteLine($"       Diagnostics(): {d}");
    Console.WriteLine($"       clients 1.5 s after:  {Group(c.Clients.Select(p => p.Describe(c.Id is "T3" or "T3N" or "T3R")))}");
    Console.WriteLine($"       upstream 1.5 s after: {Group(c.Upstream!.Connections.Select(p => p.Describe(false)))}");
}

Console.WriteLine();
Console.WriteLine("---- summary at the end of sampling, before DisposeAsync (times are seconds after the stall began; live = the tap's LiveConnections over time, value@second)");
foreach (var c in cases)
    Console.WriteLine($"  {c.Id,-4} {c.What}: {c.Summary}; DisposeAsync {c.DisposeSeconds * 1000:N0} ms");
foreach (var c in cases)
    c.Upstream!.Stop();
return;

// ================================================================================================================

static void ObserveAll(IEnumerable<Case> cases, double t)
{
    var table = TcpTable();
    foreach (var c in cases)
    {
        foreach (var p in c.Clients)
            p.Observe(table, t);
        foreach (var p in c.Upstream!.Connections)
            p.Observe(table, t);
        var live = c.Tap!.LiveConnections;
        if (c.LastLive != live)
        {
            c.LiveHistory.Add($"{live}@{t:0}");
            c.LastLive = live;
        }
    }
}

static string Counters(TcpTap tap) =>
    $"LiveConnections {tap.LiveConnections}, accepted {tap.ConnectionsAccepted}, bytes client>server {tap.BytesClientToServer:N0}, server>client {tap.BytesServerToClient:N0}, StuckConnectionsReaped {tap.StuckConnectionsReaped}, ConnectionsClosedMidMessage {tap.ConnectionsClosedMidMessage}, SegmentsDropped {tap.SegmentsDropped}";

static string Group(IEnumerable<string> items) => string.Join(" | ", items.GroupBy(x => x).Select(g => $"{g.Count()}x {g.Key}"));

static Dictionary<(int Local, int Remote), TcpState> TcpTable()
{
    var map = new Dictionary<(int, int), TcpState>();
    foreach (var c in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections())
        map[(c.LocalEndPoint.Port, c.RemoteEndPoint.Port)] = c.State;
    return map;
}

static async Task WaitForAsync(Func<bool> condition, int ms)
{
    for (var waited = 0; !condition() && waited < ms; waited += 20)
        await Task.Delay(20);
}

static string Version(Type t) => t.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

sealed record Case(string Id, string What, Upstream.Behaviour Behave, bool Redis, bool Reap)
{
    public TcpTap? Tap { get; set; }
    public Upstream? Upstream { get; set; }
    public List<string> Log { get; } = [];
    public List<Peer> Clients { get; } = [];
    public List<string> LiveHistory { get; } = [];
    public int LastLive { get; set; } = -1;
    public double DisposeSeconds { get; set; }
    public string Summary { get; set; } = "";
}

/// <summary>One end of a pair: a client of the tap, or a connection the upstream accepted from it.</summary>
sealed class Peer(Socket socket, string role)
{
    public Socket Socket { get; } = socket;
    public string Role { get; } = role;
    public int LocalPort { get; } = ((IPEndPoint)socket.LocalEndPoint!).Port;
    public int RemotePort { get; } = ((IPEndPoint)socket.RemoteEndPoint!).Port;
    public double StallStart { get; set; }
    private long _received;
    private volatile string? _end;
    private double _endAt;
    public long Written;
    public string? WriteState;
    private TcpState? _mine, _theirs;
    private bool _seen;
    private readonly List<string> _mineHistory = [], _theirsHistory = [];

    public static async Task<Peer> ConnectAsync(int port, int? receiveBuffer)
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (receiveBuffer is { } size)
            s.ReceiveBufferSize = size; // so the reply cannot all sit in socket buffers
        await s.ConnectAsync(IPAddress.Loopback, port);
        s.NoDelay = true;
        return new Peer(s, "client");
    }

    /// <summary>Keeps one receive pending: bytes, a FIN or a reset are recorded when they arrive.</summary>
    public void Listen(Func<double> now) => _ = Task.Run(async () =>
    {
        var buf = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var n = await Socket.ReceiveAsync(buf.AsMemory(), SocketFlags.None);
                if (n == 0)
                {
                    _endAt = now();
                    _end = "FIN";
                    return;
                }

                Interlocked.Add(ref _received, n);
            }
        }
        catch (SocketException ex)
        {
            _endAt = now();
            _end = $"RST ({ex.SocketErrorCode})";
        }
        catch (ObjectDisposedException) { }
    });

    public void Observe(Dictionary<(int Local, int Remote), TcpState> table, double t)
    {
        TcpState? mine = table.TryGetValue((LocalPort, RemotePort), out var a) ? a : null;
        TcpState? theirs = table.TryGetValue((RemotePort, LocalPort), out var b) ? b : null;
        if (!_seen || mine != _mine)
            _mineHistory.Add($"{Name(mine)}@{t:0}");
        if (!_seen || theirs != _theirs)
            _theirsHistory.Add($"{Name(theirs)}@{t:0}");
        _mine = mine;
        _theirs = theirs;
        _seen = true;
    }

    private static string Name(TcpState? s) => s?.ToString() ?? "gone";

    private long Unread()
    {
        try { return Socket.Available; }
        catch (Exception) { return -1; }
    }

    public string Describe(bool neverReads)
    {
        var sb = new StringBuilder();
        if (neverReads)
            sb.Append($"not read ({Unread():N0} B waiting)");
        else
            sb.Append(_end is null ? "open" : $"{_end} from the tap at {Math.Max(0, _endAt - StallStart):0} s");
        var received = Interlocked.Read(ref _received);
        if (received > 0)
            sb.Append($", read {received:N0} B");
        if (WriteState is not null)
            sb.Append($", wrote {Interlocked.Read(ref Written):N0} of {8 * 1024 * 1024:N0} B ({WriteState})");
        sb.Append($", tcp {Role}/tap {Name(_mine)}/{Name(_theirs)}");
        return sb.ToString();
    }

    public string History() => _mineHistory.Count > 1 || _theirsHistory.Count > 1
        ? $" [tcp {Role} {string.Join(" > ", _mineHistory)}; tap {string.Join(" > ", _theirsHistory)}]"
        : "";
}

/// <summary>A raw upstream per case: what it does with each connection the tap opens.</summary>
sealed class Upstream
{
    public enum Behaviour { Silent, HalfCloseAtOnce, Write8MB, RespBulk8MB }

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Behaviour _behaviour;
    private readonly Func<double> _now;
    private readonly List<Peer> _connections = [];

    public Upstream(Behaviour behaviour, Func<double> now)
    {
        _behaviour = behaviour;
        _now = now;
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<Peer> Connections
    {
        get { lock (_connections) return [.. _connections]; }
    }

    public void Stop() => _listener.Stop();

    private async Task AcceptAsync()
    {
        while (true)
        {
            Socket s;
            try { s = await _listener.AcceptSocketAsync(); }
            catch (Exception) { return; }
            var peer = new Peer(s, "upstream");
            lock (_connections) _connections.Add(peer);
            _ = Task.Run(() => ServeAsync(peer));
        }
    }

    private async Task ServeAsync(Peer peer)
    {
        switch (_behaviour)
        {
            case Behaviour.Silent:
                peer.Listen(_now);
                break;
            case Behaviour.HalfCloseAtOnce:
                peer.Socket.Shutdown(SocketShutdown.Send);
                peer.Listen(_now);
                break;
            case Behaviour.Write8MB:
                peer.Listen(_now);
                await WriteAsync(peer, []);
                break;
            case Behaviour.RespBulk8MB:
                var first = new byte[256];
                var n = await peer.Socket.ReceiveAsync(first.AsMemory(), SocketFlags.None); // the command
                peer.Listen(_now);
                if (n > 0)
                    await WriteAsync(peer, Encoding.ASCII.GetBytes($"${8 * 1024 * 1024}\r\n"), "\r\n"u8.ToArray());
                break;
        }
    }

    private static async Task WriteAsync(Peer peer, byte[] prefix, byte[]? suffix = null)
    {
        var chunk = new byte[64 * 1024];
        peer.WriteState = "writing";
        try
        {
            using var stream = new NetworkStream(peer.Socket, ownsSocket: false);
            if (prefix.Length > 0)
                await stream.WriteAsync(prefix);
            for (var sent = 0; sent < 8 * 1024 * 1024; sent += chunk.Length)
            {
                await stream.WriteAsync(chunk);
                Interlocked.Add(ref peer.Written, chunk.Length);
            }

            if (suffix is not null)
                await stream.WriteAsync(suffix);
            peer.WriteState = "all written";
        }
        catch (Exception ex)
        {
            peer.WriteState = $"write failed: {(ex is IOException { InnerException: SocketException se } ? se.SocketErrorCode.ToString() : ex.GetType().Name)}";
        }
    }
}

sealed class NullDecoder : IProtocolDecoder
{
    public void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp) { }
    public void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp) { }
    public void OnConnectionClosed() { }
    public void Dispose() { }
}

sealed class NoSink : IRequestResponseSink
{
    public void Log(RequestResponseLog log) { }
}
