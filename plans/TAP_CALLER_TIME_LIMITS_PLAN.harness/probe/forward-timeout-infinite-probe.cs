#:package Kronikol.Extensions.ProxyTap@4.20.2
#:property PublishAot=false
// forward-timeout-infinite-probe.cs (TAP_CALLER_TIME_LIMITS_PLAN, #173; read/proxytap.md section 3): ProxyTap 4.20.2 in
// front of an upstream that answers after 20 s, with ForwardTimeout = Timeout.InfiniteTimeSpan. The tap sets its
// HttpClient.Timeout to ForwardTimeout + 10 s, which for -1 ms is 9.999 s: does the caller get a 502 at about 10 s, and
// what do the 502 body, the counters and Diagnostics() say? ForwardTimeout = 30 s is the control (the answer at 20 s).
// Three edge values follow, none of which Validate rejects: TimeSpan.Zero, -5 s (CancelAfter throws for a negative value
// other than -1 ms) and -10 s (an HttpClient.Timeout of zero). 3 concurrent callers per case.
// `dotnet run forward-timeout-infinite-probe.cs [upstream-delay-seconds] [callers]` (20, 3).
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var delay = TimeSpan.FromSeconds(args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 20);
var callerCount = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;

Console.WriteLine($"ProxyTap {typeof(ProxyTap).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} on .NET {Environment.Version}, {RuntimeInformation.OSDescription} ({RuntimeInformation.RuntimeIdentifier})");
Console.WriteLine($"upstream answers {delay.TotalSeconds:0} s after each request; {callerCount} concurrent caller(s) per case; ProxyTapOptions defaults: ForwardTimeout {new ProxyTapOptions().ForwardTimeout}, ConnectTimeout {new ProxyTapOptions().ConnectTimeout}; started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
Console.WriteLine();

// The two slow cases run side by side, each with its own upstream; the edge values each take well under a second.
var slow = await Task.WhenAll(
    RunAsync("Timeout.InfiniteTimeSpan", Timeout.InfiniteTimeSpan),
    RunAsync("30 s (control)", TimeSpan.FromSeconds(30)));
foreach (var block in slow)
    Console.Write(block);
foreach (var (name, value) in new[] { ("TimeSpan.Zero", TimeSpan.Zero), ("-5 s", TimeSpan.FromSeconds(-5)), ("-10 s", TimeSpan.FromSeconds(-10)) })
    Console.Write(await RunAsync(name, value));
return;

async Task<string> RunAsync(string name, TimeSpan forwardTimeout)
{
    var sb = new StringBuilder();
    sb.AppendLine($"---- ForwardTimeout = {name} ({forwardTimeout})");
    var upstream = new SlowUpstream(delay);
    var log = new List<string>();
    var port = FreePort();
    var tap = new ProxyTap(new ProxyTapOptions
    {
        ListenPort = port,
        ForwardBaseUri = new Uri($"http://127.0.0.1:{upstream.Port}"),
        CallerName = "web",
        ServiceName = "api",
        Sink = new NoSink(),
        CaptureUnattributedRequests = true,
        FallbackTestId = "forward-timeout",
        ForwardTimeout = forwardTimeout,
        Log = line => { lock (log) log.Add(line); },
    });
    try
    {
        await tap.StartAsync();
    }
    catch (Exception ex)
    {
        sb.AppendLine($"  StartAsync threw {ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
        await DisposeQuietly(tap, sb);
        upstream.Stop();
        sb.AppendLine();
        return sb.ToString();
    }

    var client = (HttpClient?)typeof(ProxyTap).GetField("_upstream", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(tap);
    sb.AppendLine($"  the tap's HttpClient.Timeout: {client?.Timeout.ToString() ?? "?"}");

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    var results = await Task.WhenAll(Enumerable.Range(0, callerCount).Select(async i =>
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync($"http://localhost:{port}/slow/{i}");
            var body = await response.Content.ReadAsStringAsync();
            return (Text: $"{(int)response.StatusCode} {response.ReasonPhrase}, body {(body.Length > 600 ? body[..600] + "..." : body)}", Seconds: watch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            return (Text: $"{ex.GetType().Name}: {ex.Message}", Seconds: watch.Elapsed.TotalSeconds);
        }
    }));
    foreach (var group in results.GroupBy(r => r.Text))
        sb.AppendLine($"  {group.Count()}x after {group.Min(r => r.Seconds):0.0}-{group.Max(r => r.Seconds):0.0} s: {group.Key}");

    await Task.Delay(500);
    sb.AppendLine($"  upstream saw {upstream.Seen} request(s); handled {tap.RequestsHandled}, captured {tap.RequestsCaptured}, ForwardFailures {tap.ForwardFailures}");
    foreach (var d in tap.Diagnostics())
        sb.AppendLine($"  Diagnostics(): {d}");
    lock (log)
        foreach (var line in log.Where(l => !l.Contains("listening on", StringComparison.Ordinal)).GroupBy(l => l))
            sb.AppendLine($"  log ({line.Count()}x): {line.Key}");
    await DisposeQuietly(tap, sb);
    upstream.Stop();
    sb.AppendLine();
    return sb.ToString();
}

static async Task DisposeQuietly(ProxyTap tap, StringBuilder sb)
{
    try { await tap.DisposeAsync(); }
    catch (Exception ex) { sb.AppendLine($"  DisposeAsync threw {ex.GetType().Name}: {ex.Message}"); }
}

static int FreePort()
{
    var p = new TcpListener(IPAddress.Loopback, 0);
    p.Start();
    var port = ((IPEndPoint)p.LocalEndpoint).Port;
    p.Stop();
    return port;
}

// A raw-socket upstream that answers "ok" to each request on a connection, a fixed delay after it arrives.
sealed class SlowUpstream
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly TimeSpan _delay;
    private int _seen;

    public SlowUpstream(TimeSpan delay)
    {
        _delay = delay;
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Seen => Volatile.Read(ref _seen);

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

                pending.RemoveRange(0, end + 4);
                Interlocked.Increment(ref _seen);
                await Task.Delay(_delay);
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 2\r\n\r\nok"u8.ToArray());
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
