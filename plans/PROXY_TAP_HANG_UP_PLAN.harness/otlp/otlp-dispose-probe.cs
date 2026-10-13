#:package Kronikol.Extensions.Otlp@4.18.1
// otlp-dispose-probe.cs: an OtlpTap disposed while its forward waits for the collector (plan §2.4 O1 and §4.2 R2 item 2:
// is a forward cut short by the tap's own disposal counted as a forward failure?). `dotnet run otlp-dispose-probe.cs`;
// the same file on each OS. Forwarding on, in front of a collector (a raw socket) that reads the export and never
// answers. One export is sent (HTTP/1.1, Content-Length, as otlp-probe.cs sends it); once the collector has it, and
// 300 ms more, the tap is disposed (DisposeAsync) while the forward waits. Recorded: the counters before, right after
// DisposeAsync returns and 1.5 s later; every log line with its time; what the exporter reads from just before the
// disposal until 5 s after it; and how the collector's connection ends. Three runs, each with its own collector and tap.
// Times are milliseconds since the run started.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Tracking;

Console.WriteLine($"Kronikol.Extensions.Otlp {typeof(OtlpTap).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} on .NET {Environment.Version}, {RuntimeInformation.OSDescription}");
_ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(3)); Console.WriteLine("WATCHDOG: the probe ran past 3 min and was stopped"); Environment.Exit(2); });

for (var run = 1; run <= 3; run++)
{
    var clock = Stopwatch.StartNew();
    string Now() => $"t={clock.ElapsedMilliseconds} ms";

    // The collector: reads one export (head and Content-Length body), never answers, and records how its connection ends.
    var collector = new TcpListener(IPAddress.Loopback, 0);
    collector.Start();
    var collected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var collectorEnd = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    _ = Task.Run(async () =>
    {
        Socket s;
        try { s = await collector.AcceptSocketAsync(); } catch (Exception ex) { collected.TrySetResult($"no connection: {ex.Message}"); return; }
        using (s)
        {
            var seen = new List<byte>();
            var buf = new byte[65536];
            using var cap = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                while (true)
                {
                    var text = Encoding.ASCII.GetString(seen.ToArray());
                    var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        var length = text[..end].Split("\r\n").Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Select(l => int.Parse(l[15..].Trim())).FirstOrDefault();
                        if (seen.Count >= end + 4 + length)
                        {
                            collected.TrySetResult($"{Now()} the collector has the export ({text.Split("\r\n")[0]}, Content-Length {length}) and does not answer");
                            break;
                        }
                    }
                    var n = await s.ReceiveAsync(buf, SocketFlags.None, cap.Token);
                    if (n == 0) { collected.TrySetResult($"{Now()} EOF before a whole export ({seen.Count} bytes)"); return; }
                    seen.AddRange(buf.AsSpan(0, n).ToArray());
                }
                var more = await s.ReceiveAsync(buf, SocketFlags.None, cap.Token);
                collectorEnd.TrySetResult(more == 0 ? $"{Now()} EOF from the tap's side" : $"{Now()} {more} more bytes");
            }
            catch (OperationCanceledException) { collectorEnd.TrySetResult("nothing within 20 s"); }
            catch (Exception ex) { collectorEnd.TrySetResult($"{Now()} {Chain(ex)}"); }
        }
    });

    var log = new List<string>();
    var tap = new OtlpTap(new OtlpTapOptions
    {
        ListenPort = 0, ListenHost = "127.0.0.1", ForwardBaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)collector.LocalEndpoint).Port}"),
        Sink = new NoSink(), Log = l => { lock (log) log.Add($"{Now()} {l}"); },
    });
    await tap.StartAsync();

    var exporter = new TcpClient();
    await exporter.ConnectAsync(IPAddress.Loopback, tap.BoundPort);
    var body = new byte[64];
    await exporter.Client.SendAsync(Encoding.ASCII.GetBytes($"POST /v1/traces HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/x-protobuf\r\nContent-Length: {body.Length}\r\n\r\n"));
    await exporter.Client.SendAsync(body);
    var got = await collected.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await Task.Delay(300); // the forward is now waiting for an answer that never comes

    using var window = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var heard = WhatTheExporterReads(exporter.Client, window.Token, Now);
    var before = Counters(tap);
    var disposeStarted = Now();
    string disposed;
    try { await tap.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)); disposed = "returned"; }
    catch (TimeoutException) { disposed = "had not returned after 15 s"; }
    catch (Exception ex) { disposed = $"threw {Chain(ex)}"; }
    var disposeEnded = Now();
    var rightAfter = Counters(tap);
    window.CancelAfter(TimeSpan.FromSeconds(5));
    await Task.Delay(1500);
    var later = Counters(tap);
    var (bytes, end) = await heard;
    string collectorSaw;
    try { collectorSaw = await collectorEnd.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
    catch (TimeoutException) { collectorSaw = "no end within 5 s of the check"; }

    Console.WriteLine();
    Console.WriteLine($"run {run}:");
    Console.WriteLine($"    {got}");
    Console.WriteLine($"    before DisposeAsync: {before}");
    Console.WriteLine($"    DisposeAsync started {disposeStarted}, {disposed} {disposeEnded}");
    Console.WriteLine($"    right after it returned: {rightAfter}");
    Console.WriteLine($"    1.5 s later: {later}");
    Console.WriteLine($"    the exporter read {bytes.Length} bytes{(bytes.Length > 0 ? $": \"{Escape(bytes)}\"" : "")}, then {end}");
    Console.WriteLine($"    the collector's connection: {collectorSaw}");
    lock (log) foreach (var line in log) Console.WriteLine($"    log: {line}");

    exporter.Dispose();
    collector.Stop();
}
return;

static async Task<(byte[] Bytes, string End)> WhatTheExporterReads(Socket socket, CancellationToken window, Func<string> now)
{
    var received = new List<byte>();
    var buffer = new byte[8192];
    try
    {
        while (true)
        {
            var n = await socket.ReceiveAsync(buffer, SocketFlags.None, window);
            if (n == 0) return (received.ToArray(), $"EOF ({now()})");
            received.AddRange(buffer.AsSpan(0, n).ToArray());
        }
    }
    catch (OperationCanceledException) { return (received.ToArray(), "nothing more within 5 s of the disposal (the connection is still open)"); }
    catch (Exception ex) { return (received.ToArray(), $"a reset ({now()}): {Chain(ex)}"); }
}

static string Counters(OtlpTap tap) => string.Join(", ", typeof(OtlpTap).GetProperties()
    .Where(p => p.PropertyType == typeof(long)).Select(p => $"{p.Name} {p.GetValue(tap)}"));

static string Escape(byte[] bytes)
{
    const char bs = (char)92;
    var sb = new StringBuilder();
    foreach (var b in bytes)
    {
        if (b == 13) sb.Append(bs).Append('r');
        else if (b == 10) sb.Append(bs).Append('n');
        else if (b == 92) sb.Append(bs).Append(bs);
        else if (b < 32 || b > 126) sb.Append(bs).Append('x').Append(b.ToString("X2"));
        else sb.Append((char)b);
    }
    return sb.ToString();
}

static string Describe(Exception ex)
{
    var extra = ex switch
    {
        SocketException s => $" SocketErrorCode={s.SocketErrorCode} ErrorCode={s.ErrorCode} NativeErrorCode={s.NativeErrorCode}",
        _ => "",
    };
    return $"{ex.GetType().FullName} HResult=0x{ex.HResult:X8}{extra} \"{ex.Message}\"";
}

static string Chain(Exception ex)
{
    var parts = new List<string>();
    for (Exception? e = ex; e is not null; e = e.InnerException)
        parts.Add(Describe(e));
    return string.Join(" <- inner ", parts);
}

sealed class NoSink : IRequestResponseSink { public void Log(RequestResponseLog log) { } }
