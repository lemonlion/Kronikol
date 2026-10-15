#:package Kronikol.Extensions.ProxyTap@4.20.2
#:property PublishAot=false
// proxytap-stall-probe.cs (TAP_CALLER_TIME_LIMITS_PLAN, #173): what ProxyTap 4.20.2 and the HttpListener under it do with
// a caller that stalls, case by case, sampled every 15 s. `dotnet run proxytap-stall-probe.cs [seconds] [callers]`
// (200 s and 3 callers by default).
//   H0  connects and sends nothing
//   H1  sends a request line and Host, no blank line, and stalls
//   B1  a whole head with Content-Length: 100000 and 1,000 body bytes, then stalls with the socket open (no FIN)
//   W1  a whole request for an 8 MB answer, never read (receive buffer 16 KiB)
//   K1  one small exchange read whole, then the keep-alive connection left idle
// Each case has its own tap; all share one raw-socket upstream that counts the requests it sees per path. A caller that
// may read keeps one receive pending, so a FIN, a reset or an answer is timed to the moment it arrives; W1 never reads, so
// both ends of its connection are watched in the TCP table once a second. On Linux the managed listener's tables
// (_listenerContexts, _connections) are read by reflection; on Windows, http.sys's request queue through netsh.
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var seconds = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 200;
var callerCount = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
const int SampleEvery = 15;
var clock = Stopwatch.StartNew();
double Now() => clock.Elapsed.TotalSeconds;

Console.WriteLine($"ProxyTap {Version(typeof(ProxyTap))} on .NET {Environment.Version}, {RuntimeInformation.OSDescription} ({RuntimeInformation.RuntimeIdentifier})");
Console.WriteLine($"HttpListener: {(OperatingSystem.IsWindows() ? "http.sys" : "managed")}; {callerCount} caller(s) per case; a sample every {SampleEvery} s to {seconds} s; started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");

var upstream = new RawUpstream();
var cases = new List<Case>
{
    new("H0", "connects, sends nothing", "/h0", Reads: true),
    new("H1", "request line + Host, no blank line, stalls", "/h1", Reads: true),
    new("B1", "head with Content-Length: 100000 + 1,000 body bytes, stalls (no FIN)", "/b1", Reads: true),
    new("W1", "whole request for an 8 MB answer, never reads (receive buffer 16 KiB)", "/w1", Reads: false),
    new("K1", "one small exchange read whole, then idle on keep-alive", "/k1", Reads: true),
};

foreach (var c in cases)
{
    var port = FreePort();
    c.Tap = new ProxyTap(new ProxyTapOptions
    {
        ListenPort = port,
        ForwardBaseUri = new Uri($"http://127.0.0.1:{upstream.Port}"),
        CallerName = "web",
        ServiceName = "api",
        Name = c.Id,
        Sink = new NoSink(),
        CaptureUnattributedRequests = true,
        FallbackTestId = "stall",
        Log = line => { lock (c.Log) c.Log.Add(line); },
    });
    await c.Tap.StartAsync();
    c.Port = port;
}

var listener0 = Listener(cases[0].Tap!);
Console.WriteLine($"TimeoutManager getters on the tap's listener: {TimeoutManagerGetters(listener0)}");
if (OperatingSystem.IsWindows())
    Console.WriteLine($"http.sys server session timeouts for the tap's queue (netsh): {HttpSysTimeouts(cases[0].Port)}");
Console.WriteLine();

// ---- set every case up, then sample them together
foreach (var c in cases)
{
    for (var i = 0; i < callerCount; i++)
    {
        var caller = await Caller.ConnectAsync(c.Port, c.Id == "W1" ? 16 * 1024 : null);
        var s = caller.Client.Client;
        switch (c.Id)
        {
            case "H0":
                break;
            case "H1":
                await s.SendAsync(Encoding.ASCII.GetBytes($"GET /h1 HTTP/1.1\r\nHost: localhost:{c.Port}\r\n").AsMemory(), SocketFlags.None);
                break;
            case "B1":
                await s.SendAsync(Encoding.ASCII.GetBytes($"POST /b1 HTTP/1.1\r\nHost: localhost:{c.Port}\r\nContent-Type: application/octet-stream\r\nContent-Length: 100000\r\n\r\n").AsMemory(), SocketFlags.None);
                await s.SendAsync(new byte[1000].AsMemory(), SocketFlags.None);
                break;
            case "W1":
                await s.SendAsync(Encoding.ASCII.GetBytes($"GET /w1 HTTP/1.1\r\nHost: localhost:{c.Port}\r\n\r\n").AsMemory(), SocketFlags.None);
                break;
            case "K1":
                await s.SendAsync(Encoding.ASCII.GetBytes($"GET /k1 HTTP/1.1\r\nHost: localhost:{c.Port}\r\n\r\n").AsMemory(), SocketFlags.None);
                caller.Exchange = await ReadOneAnswerAsync(s, TimeSpan.FromSeconds(10));
                break;
        }

        caller.StallStart = Now();
        if (c.Reads)
            caller.Listen(Now);
        c.Callers.Add(caller);
    }
}

// B1 and W1 are in the tap's hands once the listener has dispatched their heads; wait for that before the clock starts.
await WaitForAsync(() => InFlight(cases.First(c => c.Id == "B1").Tap!) == callerCount, 5000);
await WaitForAsync(() => InFlight(cases.First(c => c.Id == "W1").Tap!) == callerCount, 5000);
foreach (var k1 in cases.First(c => c.Id == "K1").Callers.Take(1))
    Console.WriteLine($"K1's answer head: {k1.Exchange}");
Console.WriteLine();

var origin = Now();
var sampleTimes = Enumerable.Range(0, seconds / SampleEvery + 1).Select(i => i * SampleEvery).ToList();
if (sampleTimes[^1] != seconds)
    sampleTimes.Add(seconds);
var all = cases.SelectMany(c => c.Callers).ToList();

foreach (var t in sampleTimes)
{
    while (Now() < origin + t)
    {
        Observe(all, Now());
        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(origin + t - Now(), 0, 1)));
    }

    Observe(all, Now());
    var queues = HttpSysQueues(cases.Select(c => c.Port));
    Console.WriteLine($"---- t = {t} s");
    foreach (var c in cases)
    {
        var tables = ListenerTables(Listener(c.Tap!), c.Port);
        var queue = queues.TryGetValue(c.Port, out var q) ? $" | http.sys {q}" : "";
        Console.WriteLine($"  {c.Id}  in-flight {InFlight(c.Tap!)} | upstream saw {upstream.Count(c.Path)} | {tables}{queue}");
        Console.WriteLine($"      callers: {Group(c)}");
    }
}

foreach (var c in cases)
    c.Summary = Summary(c, seconds);

// ---- dispose each tap, timed, and look again
Console.WriteLine();
Console.WriteLine("---- DisposeAsync");
foreach (var c in cases)
{
    var listener = Listener(c.Tap!);
    var before = InFlight(c.Tap!);
    var at = Now() - origin;
    var watch = Stopwatch.StartNew();
    await c.Tap!.DisposeAsync();
    watch.Stop();
    c.DisposeSeconds = watch.Elapsed.TotalSeconds;
    c.InFlightBeforeDispose = before;
    await Task.Delay(1500);
    Observe(c.Callers, Now());
    string[] errors;
    lock (c.Log) errors = c.Log.Where(l => l.Contains("proxy-tap error", StringComparison.Ordinal)).ToArray();
    Console.WriteLine($"  {c.Id}  DisposeAsync at t = {at:0} s took {watch.Elapsed.TotalMilliseconds:N0} ms (in-flight {before} before, {InFlight(c.Tap!)} after) | {ListenerTables(listener, c.Port)}");
    Console.WriteLine($"      handled {c.Tap.RequestsHandled}, captured {c.Tap.RequestsCaptured}, ForwardFailures {c.Tap.ForwardFailures}, 'proxy-tap error' lines {errors.Length}{(errors.Length > 0 ? " (first: " + errors[0] + ")" : "")}");
    foreach (var d in c.Tap.Diagnostics())
        Console.WriteLine($"      Diagnostics(): {d}");
    Console.WriteLine($"      callers 1.5 s after: {Group(c)}");
    if (!c.Reads)
    {
        var drained = await Task.WhenAll(c.Callers.Select(x => x.DrainAsync(TimeSpan.FromSeconds(10))));
        Console.WriteLine($"      W1 callers read what was left: {string.Join("; ", drained)}");
    }
}

// ---- the summary: one row per case
Console.WriteLine();
Console.WriteLine("---- summary before DisposeAsync (times are seconds after each caller's stall began; tcp histories are state@second from the 1 s watch)");
foreach (var c in cases)
    Console.WriteLine($"  {c.Id}  {c.What}: {c.Summary}; DisposeAsync {c.DisposeSeconds * 1000:N0} ms");
upstream.Stop();
return;

// ================================================================================================================

static string Summary(Case c, int seconds)
{
    var parts = new List<string>();
    foreach (var g in c.Callers.GroupBy(x => x.ReleaseDescription()))
        parts.Add($"{g.Count()}x {g.Key}");
    var text = string.Join(" | ", parts);
    return text.Replace("{held}", $"held through {seconds} s");
}

static string Group(Case c)
{
    var groups = c.Callers.GroupBy(x => x.Describe(c.Reads)).Select(g => $"{g.Count()}x {g.Key}");
    var text = string.Join(" | ", groups);
    if (!c.Reads)
        text += $" | unread B: {string.Join("/", c.Callers.Select(x => x.Unread().ToString("N0")))}";
    return text;
}

static void Observe(IEnumerable<Caller> callers, double now)
{
    var table = TcpTable();
    foreach (var c in callers)
        c.Observe(table, now);
}

static Dictionary<(int Local, int Remote), TcpState> TcpTable()
{
    var map = new Dictionary<(int, int), TcpState>();
    foreach (var c in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections())
        map[(c.LocalEndPoint.Port, c.RemoteEndPoint.Port)] = c.State;
    return map;
}

static async Task<string> ReadOneAnswerAsync(Socket s, TimeSpan within)
{
    var got = new List<byte>();
    var buf = new byte[65536];
    using var cts = new CancellationTokenSource(within);
    while (true)
    {
        var text = Encoding.ASCII.GetString(got.ToArray());
        var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (end >= 0)
        {
            var length = text[..end].Split("\r\n")
                .Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                .Select(l => int.Parse(l[15..].Trim(), CultureInfo.InvariantCulture)).FirstOrDefault();
            if (got.Count >= end + 4 + length)
                return text[..end].Replace("\r\n", " | ");
        }

        var n = await s.ReceiveAsync(buf.AsMemory(), SocketFlags.None, cts.Token);
        if (n == 0)
            return "closed before a whole answer";
        got.AddRange(buf.AsSpan(0, n).ToArray());
    }
}

static async Task WaitForAsync(Func<bool> condition, int ms)
{
    for (var waited = 0; !condition() && waited < ms; waited += 20)
        await Task.Delay(20);
}

static int FreePort()
{
    var p = new TcpListener(IPAddress.Loopback, 0);
    p.Start();
    var port = ((IPEndPoint)p.LocalEndpoint).Port;
    p.Stop();
    return port;
}

static string Version(Type t) => t.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

static int InFlight(ProxyTap tap) =>
    (int)typeof(ProxyTap).GetField("_inflight", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tap)!;

static HttpListener Listener(ProxyTap tap) =>
    (HttpListener)typeof(ProxyTap).GetField("_listener", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tap)!;

// The managed listener's two tables, and each held connection's timer fields (its int fields named *timeout*).
static string ListenerTables(HttpListener listener, int port)
{
    if (OperatingSystem.IsWindows())
        return "listener tables n/a (http.sys)";
    var contexts = Count(listener, "_listenerContexts");
    var connections = Count(listener, "_connections");
    var timers = ConnectionTimers(listener);
    return $"_listenerContexts {contexts}, _connections {connections}{(timers == "" ? "" : $" ({timers})")}, {Unregistered(port)}";
}

// The managed listener keeps a connection whose request head has not been parsed yet out of its own tables: it sits in
// its HttpEndPointListener's _unregisteredConnections (HttpEndPointManager.s_ipEndPoints, address -> port -> listener)
// until a whole head names a prefix. Counted here for this listener's port, with each such connection's timer field.
static string Unregistered(int port)
{
    try { return UnregisteredCore(port); }
    catch (Exception ex) { return $"endpoint listener unreadable ({ex.GetType().Name}: {ex.Message})"; }
}

static string UnregisteredCore(int port)
{
    var manager = typeof(HttpListener).Assembly.GetType("System.Net.HttpEndPointManager");
    var endpoints = manager?.GetFields(BindingFlags.NonPublic | BindingFlags.Static).Select(f => f.GetValue(null)).OfType<IDictionary>().FirstOrDefault();
    if (endpoints is null)
        return "(no HttpEndPointManager endpoint table)";
    var parts = new List<string>();
    lock (endpoints)
    {
        foreach (DictionaryEntry address in endpoints)
        {
            if (address.Value is not IDictionary ports || !ports.Contains(port))
                continue;
            var epl = ports[port]!;
            // the endpoint listener's set of connections still waiting for a whole head: _unregisteredConnections, a
            // HashSet<HttpConnection> in .NET 10 (the runtime locks the set itself around every change)
            var field = epl.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(f =>
                f.FieldType.IsGenericType && f.FieldType.GetGenericArguments()[0].Name == "HttpConnection");
            if (field?.GetValue(epl) is not IEnumerable table)
            {
                var names = string.Join(" ", epl.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(f => $"{f.Name}:{f.FieldType.Name}"));
                parts.Add($"{address.Key}: (no collection of HttpConnection; fields {names})");
                continue;
            }

            object[] keys;
            lock (table)
                keys = table is IDictionary d ? d.Keys.Cast<object>().ToArray() : table.Cast<object>().ToArray();
            var timer = keys.Length == 0 ? null : keys[0].GetType().GetField("_timeout", BindingFlags.NonPublic | BindingFlags.Instance);
            var timers = timer is null ? "" : $" (_timeout={string.Join("/", keys.Select(k => timer.GetValue(k)).Distinct())})";
            parts.Add($"{address.Key} {epl.GetType().Name}.{field.Name} {keys.Length}{timers}");
        }
    }

    return parts.Count == 0 ? "endpoint listener for the port: none" : string.Join(", ", parts);
}

static string Count(HttpListener listener, string name)
{
    var field = typeof(HttpListener).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
    if (field?.GetValue(listener) is not ICollection table)
        return $"(no field {name})";
    lock (table.SyncRoot)
        return table.Count.ToString(CultureInfo.InvariantCulture);
}

static string ConnectionTimers(HttpListener listener)
{
    var field = typeof(HttpListener).GetField("_connections", BindingFlags.NonPublic | BindingFlags.Instance);
    if (field?.GetValue(listener) is not IDictionary table)
        return "";
    object[] keys;
    lock (((ICollection)table).SyncRoot)
        keys = table.Keys.Cast<object>().ToArray();
    if (keys.Length == 0)
        return "";
    var fields = keys[0].GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
        .Where(f => f.FieldType == typeof(int) && f.Name.Contains("timeout", StringComparison.OrdinalIgnoreCase)).ToArray();
    return string.Join(" ", fields.Select(f => $"{keys[0].GetType().Name}.{f.Name}=" + string.Join("/", keys.Select(k => f.GetValue(k)).Distinct())));
}

static string TimeoutManagerGetters(HttpListener listener)
{
    var parts = new List<string>();
    try
    {
        var tm = listener.TimeoutManager;
        foreach (var p in typeof(HttpListenerTimeoutManager).GetProperties())
        {
            try { parts.Add($"{p.Name}={p.GetValue(tm)}"); }
            catch (TargetInvocationException ex) { parts.Add($"{p.Name} throws {ex.InnerException?.GetType().Name}"); }
        }
    }
    catch (Exception ex) { parts.Add($"TimeoutManager throws {ex.GetType().Name}"); }
    return string.Join(", ", parts);
}

static string NetshRequestQueues()
{
    var psi = new ProcessStartInfo("netsh", "http show servicestate view=requestq") { RedirectStandardOutput = true, UseShellExecute = false };
    using var p = Process.Start(psi)!;
    var text = p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    return text.Replace("\r\n", "\n");
}

// Windows: http.sys's counters for each tap's request queue. Split on the unindented line only: each queue's URL-group
// section repeats "Request queue name:" indented.
static Dictionary<int, string> HttpSysQueues(IEnumerable<int> ports)
{
    var result = new Dictionary<int, string>();
    if (!OperatingSystem.IsWindows())
        return result;
    var text = NetshRequestQueues();
    foreach (var block in text.Split("\nRequest queue name:"))
        foreach (var port in ports)
            if (block.Contains($":{port}/", StringComparison.OrdinalIgnoreCase))
                result[port] = $"active requests {Value(block, "Active requests")}, queued {Value(block, "Queued requests")}, arrived {Value(block, "Requests arrived")}";
    return result;
}

static string HttpSysTimeouts(int port)
{
    foreach (var block in NetshRequestQueues().Split("\nRequest queue name:"))
        if (block.Contains($":{port}/", StringComparison.OrdinalIgnoreCase))
            return string.Join("; ", block.Split('\n').Select(l => l.Trim())
                .Where(l => l.Contains("timeout (secs)", StringComparison.OrdinalIgnoreCase) || l.Contains("send rate", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Timeout values", StringComparison.Ordinal)));
    return "(queue not found)";
}

static string Value(string block, string key)
{
    var line = block.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(key + ":", StringComparison.Ordinal));
    return line is null ? "?" : line[(key.Length + 1)..].Trim();
}

sealed record Case(string Id, string What, string Path, bool Reads)
{
    public ProxyTap? Tap { get; set; }
    public int Port { get; set; }
    public List<string> Log { get; } = [];
    public List<Caller> Callers { get; } = [];
    public double DisposeSeconds { get; set; }
    public int InFlightBeforeDispose { get; set; }
    public string Summary { get; set; } = "";
}

sealed class Caller(TcpClient client, int clientPort, int tapPort)
{
    public TcpClient Client { get; } = client;
    public int ClientPort { get; } = clientPort;
    public int TapPort { get; } = tapPort;
    public double StallStart { get; set; }
    public string? Exchange { get; set; }
    private readonly List<byte> _heard = [];
    private double? _heardAt;
    private volatile string? _end;
    private double _endAt;
    private TcpState? _clientState, _tapState;
    private bool _seen;
    private readonly List<string> _clientHistory = [], _tapHistory = [];
    private double? _leftEstablishedAt;

    public static async Task<Caller> ConnectAsync(int port, int? receiveBuffer)
    {
        var client = new TcpClient();
        if (receiveBuffer is { } size)
            client.ReceiveBufferSize = size; // so the answer cannot all sit in socket buffers
        await client.ConnectAsync("localhost", port);
        return new Caller(client, ((IPEndPoint)client.Client.LocalEndPoint!).Port, port);
    }

    /// <summary>Keeps one receive pending: an answer, a FIN or a reset is recorded when it arrives.</summary>
    public void Listen(Func<double> now) => _ = Task.Run(async () =>
    {
        var buf = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var n = await Client.Client.ReceiveAsync(buf.AsMemory(), SocketFlags.None);
                if (n == 0)
                {
                    _endAt = now();
                    _end = "FIN";
                    return;
                }

                lock (_heard)
                {
                    _heardAt ??= now();
                    _heard.AddRange(buf.AsSpan(0, n).ToArray());
                }
            }
        }
        catch (SocketException ex)
        {
            _endAt = now();
            _end = $"RST ({ex.SocketErrorCode})";
        }
        catch (ObjectDisposedException) { }
    });

    public void Observe(Dictionary<(int Local, int Remote), TcpState> table, double now)
    {
        TcpState? client = table.TryGetValue((ClientPort, TapPort), out var cs) ? cs : null;
        TcpState? tap = table.TryGetValue((TapPort, ClientPort), out var ts) ? ts : null;
        if (!_seen || client != _clientState)
            _clientHistory.Add($"{Name(client)}@{now - StallStart:0}");
        if (!_seen || tap != _tapState)
            _tapHistory.Add($"{Name(tap)}@{now - StallStart:0}");
        if (_seen && _leftEstablishedAt is null && (client != TcpState.Established || tap != TcpState.Established))
            _leftEstablishedAt = now;
        _clientState = client;
        _tapState = tap;
        _seen = true;
    }

    public long Unread()
    {
        try { return Client.Client.Available; }
        catch (Exception) { return -1; }
    }

    private static string Name(TcpState? s) => s?.ToString() ?? "gone";

    private (string Text, int Length, double? At) Heard()
    {
        lock (_heard)
            return (Encoding.ASCII.GetString(_heard.ToArray()), _heard.Count, _heardAt);
    }

    private static string FirstLine(string text)
    {
        var end = text.IndexOf("\r\n", StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    public string Describe(bool reads)
    {
        var sb = new StringBuilder();
        sb.Append(reads ? (_end is null ? "open" : $"{_end} at {_endAt - StallStart:0.0} s") : "not read");
        var (text, length, at) = Heard();
        if (length > 0)
            sb.Append($", heard \"{FirstLine(text)}\" ({length} B) at {at - StallStart:0.0} s");
        sb.Append($", tcp caller/tap {Name(_clientState)}/{Name(_tapState)}");
        return sb.ToString();
    }

    public string ReleaseDescription()
    {
        var (text, length, at) = Heard();
        var heard = length > 0 ? $"heard \"{FirstLine(text)}\" ({length} B) at {at - StallStart:0.0} s, " : "heard nothing, ";
        var released = _end is not null ? $"{_end} at {_endAt - StallStart:0.0} s"
            : _leftEstablishedAt is { } left ? $"tcp left Established at {left - StallStart:0.0} s"
            : "{held}";
        var history = _clientHistory.Count > 1 || _tapHistory.Count > 1
            ? $" [tcp caller {string.Join(" > ", _clientHistory)}; tap {string.Join(" > ", _tapHistory)}]"
            : "";
        return heard + released + history;
    }

    /// <summary>W1 only, after DisposeAsync: read what is left and say how the connection ends.</summary>
    public async Task<string> DrainAsync(TimeSpan within)
    {
        var total = 0L;
        var buf = new byte[256 * 1024];
        using var cts = new CancellationTokenSource(within);
        try
        {
            while (true)
            {
                var n = await Client.Client.ReceiveAsync(buf.AsMemory(), SocketFlags.None, cts.Token);
                if (n == 0)
                    return $"{total:N0} B then FIN";
                total += n;
            }
        }
        catch (OperationCanceledException) { return $"{total:N0} B, still open after {within.TotalSeconds:0} s"; }
        catch (SocketException ex) { return $"{total:N0} B then RST ({ex.SocketErrorCode})"; }
    }
}

// A raw-socket upstream: answers each request on a connection in turn, 8 MB for /w1 and "ok" otherwise, and counts the
// requests it sees per path.
sealed class RawUpstream
{
    private static readonly byte[] Big = new byte[8 * 1024 * 1024];
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentDictionary<string, int> _seen = new();

    public RawUpstream()
    {
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Count(string path) => _seen.TryGetValue(path, out var n) ? n : 0;

    public void Stop() => _listener.Stop();

    private async Task AcceptAsync()
    {
        while (true)
        {
            Socket s;
            try { s = await _listener.AcceptSocketAsync(); }
            catch (Exception) { return; }
            _ = Task.Run(() => ServeAsync(s));
        }
    }

    private async Task ServeAsync(Socket s)
    {
        using var stream = new NetworkStream(s, ownsSocket: true);
        var pending = new List<byte>();
        var buf = new byte[65536];
        try
        {
            while (true)
            {
                int end;
                while ((end = HeadEnd(pending)) < 0)
                {
                    var n = await stream.ReadAsync(buf.AsMemory());
                    if (n == 0)
                        return;
                    pending.AddRange(buf.AsSpan(0, n).ToArray());
                }

                var head = Encoding.ASCII.GetString(pending.GetRange(0, end).ToArray());
                pending.RemoveRange(0, end + 4);
                var lines = head.Split("\r\n");
                var path = lines[0].Split(' ')[1];
                var length = lines.Skip(1).Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    .Select(l => int.Parse(l[15..].Trim(), CultureInfo.InvariantCulture)).FirstOrDefault();
                while (pending.Count < length)
                {
                    var n = await stream.ReadAsync(buf.AsMemory());
                    if (n == 0)
                        return;
                    pending.AddRange(buf.AsSpan(0, n).ToArray());
                }

                pending.RemoveRange(0, length);
                _seen.AddOrUpdate(path, 1, (_, v) => v + 1);
                var body = path == "/w1" ? Big : "ok"u8.ToArray();
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\n\r\n"));
                await stream.WriteAsync(body);
            }
        }
        catch (Exception) { }
    }

    private static int HeadEnd(List<byte> bytes)
    {
        for (var i = 0; i + 3 < bytes.Count; i++)
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
                return i;
        return -1;
    }
}

sealed class NoSink : IRequestResponseSink
{
    public void Log(RequestResponseLog log) { }
}
