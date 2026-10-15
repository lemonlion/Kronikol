#:package Kronikol.Extensions.Otlp@4.20.2
#:property PublishAot=false
// otlp-stall-probe.cs (TAP_CALLER_TIME_LIMITS_PLAN, #173): what OtlpTap 4.20.2 does with an exporter that stalls,
// sampled every 15 s. `dotnet run otlp-stall-probe.cs [seconds] [callers] [b1-exporters]` (200 s, 3, 10 by default).
//   H1  a partial head (request line + Host, no blank line), then a stall
//   B1  ten exporters, each a whole head declaring Content-Length = MaxRequestBytes (the default, 32 MiB) and 10 body
//       bytes, then a stall: the process's memory before and after says whether the tap allocates the body up front
//   K1  one complete export (JSON "{}") read whole, then the keep-alive connection left idle
// Each case has its own tap on 127.0.0.1. Each exporter keeps one receive pending, so a FIN, a reset or an answer is timed
// to the moment it arrives; both ends of each connection are also watched in the TCP table once a second.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Tracking;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var seconds = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 200;
var callerCount = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
var b1Count = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 10;
const int SampleEvery = 15;
var clock = Stopwatch.StartNew();
double Now() => clock.Elapsed.TotalSeconds;
var maxRequestBytes = new OtlpTapOptions().MaxRequestBytes;

Console.WriteLine($"Kronikol.Extensions.Otlp {Version(typeof(OtlpTap))} on .NET {Environment.Version}, {RuntimeInformation.OSDescription} ({RuntimeInformation.RuntimeIdentifier})");
Console.WriteLine($"{callerCount} exporter(s) for H1 and K1, {b1Count} for B1; a sample every {SampleEvery} s to {seconds} s; OtlpTapOptions.MaxRequestBytes default {maxRequestBytes:N0} B ({maxRequestBytes / 1048576.0:0.##} MiB); Server GC {System.Runtime.GCSettings.IsServerGC}; started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");

var cases = new List<Case>
{
    new("H1", "partial head (request line + Host), stalls", callerCount),
    new("B1", $"head with Content-Length {maxRequestBytes:N0} + 10 body bytes, stalls", b1Count),
    new("K1", "one export (JSON {}) read whole, then idle on keep-alive", callerCount),
};

foreach (var c in cases)
{
    c.Tap = new OtlpTap(new OtlpTapOptions
    {
        ListenPort = 0,
        ListenHost = "127.0.0.1",
        Sink = new NoSink(),
        Name = c.Id,
        Log = line => { lock (c.Log) c.Log.Add(line); },
    });
    await c.Tap.StartAsync();
}

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
var memoryBefore = Memory();
Console.WriteLine($"memory before any exporter connects (after a full collection): {memoryBefore}");
Console.WriteLine();

foreach (var c in cases)
{
    var port = c.Tap!.BoundPort;
    for (var i = 0; i < c.Count; i++)
    {
        var caller = await Caller.ConnectAsync(port);
        var s = caller.Client.Client;
        switch (c.Id)
        {
            case "H1":
                await s.SendAsync(Encoding.ASCII.GetBytes("POST /v1/traces HTTP/1.1\r\nHost: localhost\r\n").AsMemory(), SocketFlags.None);
                break;
            case "B1":
                await s.SendAsync(Encoding.ASCII.GetBytes($"POST /v1/traces HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/x-protobuf\r\nContent-Length: {maxRequestBytes}\r\n\r\n").AsMemory(), SocketFlags.None);
                await s.SendAsync(new byte[10].AsMemory(), SocketFlags.None);
                break;
            case "K1":
                await s.SendAsync(Encoding.ASCII.GetBytes("POST /v1/traces HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}").AsMemory(), SocketFlags.None);
                caller.Exchange = await ReadOneAnswerAsync(s, TimeSpan.FromSeconds(10));
                break;
        }

        caller.StallStart = Now();
        caller.Listen(Now);
        c.Callers.Add(caller);
    }
}

await Task.Delay(2000);
var memoryAfter = Memory();
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
var memoryAfterCollect = Memory();
Console.WriteLine($"memory 2 s after the B1 exporters stalled: {memoryAfter}");
Console.WriteLine($"  and after a full collection: {memoryAfterCollect}");
Console.WriteLine($"K1's answer head: {cases.First(c => c.Id == "K1").Callers[0].Exchange}");
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
    Console.WriteLine($"---- t = {t} s   memory: {Memory()}");
    foreach (var c in cases)
    {
        Console.WriteLine($"  {c.Id}  {Counters(c.Tap!)}");
        Console.WriteLine($"      exporters: {Group(c)}");
    }
}

foreach (var c in cases)
    c.Summary = string.Join(" | ", c.Callers.GroupBy(x => x.ReleaseDescription()).Select(g => $"{g.Count()}x {g.Key}")).Replace("{held}", $"held through {seconds} s");

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
    Observe(c.Callers, Now());
    Console.WriteLine($"  {c.Id}  DisposeAsync at t = {at:0} s took {watch.Elapsed.TotalMilliseconds:N0} ms | {Counters(c.Tap)}");
    lock (c.Log)
        foreach (var line in c.Log.Where(l => !l.Contains("listening on", StringComparison.Ordinal)))
            Console.WriteLine($"      log: {line}");
    foreach (var d in c.Tap.Diagnostics())
        Console.WriteLine($"      Diagnostics(): {d}");
    Console.WriteLine($"      exporters 1.5 s after: {Group(c)}");
}

foreach (var c in cases)
    foreach (var x in c.Callers)
        x.Client.Dispose();
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
Console.WriteLine($"memory after every tap was disposed (after a full collection): {Memory()}");

Console.WriteLine();
Console.WriteLine("---- summary before DisposeAsync (times are seconds after each exporter's stall began)");
foreach (var c in cases)
    Console.WriteLine($"  {c.Id}  {c.What}: {c.Summary}; DisposeAsync {c.DisposeSeconds * 1000:N0} ms");
Console.WriteLine($"  memory: before {memoryBefore}");
Console.WriteLine($"          B1 stalled {memoryAfter}");
Console.WriteLine($"          B1 stalled, after a full collection {memoryAfterCollect}");
return;

// ================================================================================================================

static string Memory()
{
    using var p = Process.GetCurrentProcess();
    var info = GC.GetGCMemoryInfo();
    return $"GC.GetTotalMemory(false) {GC.GetTotalMemory(false) / 1048576.0:N1} MiB, GC heap {info.HeapSizeBytes / 1048576.0:N1} MiB, GC committed {info.TotalCommittedBytes / 1048576.0:N1} MiB, working set {p.WorkingSet64 / 1048576.0:N1} MiB, private {p.PrivateMemorySize64 / 1048576.0:N1} MiB";
}

static string Counters(OtlpTap tap) => string.Join(", ", typeof(OtlpTap).GetProperties()
    .Where(p => p.PropertyType == typeof(long)).Select(p => $"{p.Name} {p.GetValue(tap)}"));

static string Group(Case c) => string.Join(" | ", c.Callers.GroupBy(x => x.Describe()).Select(g => $"{g.Count()}x {g.Key}"));

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

static string Version(Type t) => t.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

sealed record Case(string Id, string What, int Count)
{
    public OtlpTap? Tap { get; set; }
    public List<string> Log { get; } = [];
    public List<Caller> Callers { get; } = [];
    public double DisposeSeconds { get; set; }
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

    public static async Task<Caller> ConnectAsync(int port)
    {
        var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
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

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append(_end is null ? "open" : $"{_end} at {_endAt - StallStart:0} s");
        var (text, length, at) = Heard();
        if (length > 0)
            sb.Append($", heard \"{FirstLine(text)}\" ({length} B) at {at - StallStart:0} s");
        sb.Append($", tcp exporter/tap {Name(_clientState)}/{Name(_tapState)}");
        return sb.ToString();
    }

    public string ReleaseDescription()
    {
        var (text, length, at) = Heard();
        var heard = length > 0 ? $"heard \"{FirstLine(text)}\" ({length} B) at {at - StallStart:0} s, " : "heard nothing, ";
        var released = _end is not null ? $"{_end} at {_endAt - StallStart:0} s"
            : _leftEstablishedAt is { } left ? $"tcp left Established at {left - StallStart:0} s"
            : "{held}";
        var history = _clientHistory.Count > 1 || _tapHistory.Count > 1
            ? $" [tcp exporter {string.Join(" > ", _clientHistory)}; tap {string.Join(" > ", _tapHistory)}]"
            : "";
        return heard + released + history;
    }
}

sealed class NoSink : IRequestResponseSink
{
    public void Log(RequestResponseLog log) { }
}
