#:package Kronikol.Extensions.Otlp@4.18.1
// otlp-probe.cs: the same misreading of a hang-up in OtlpTap (plan §3). `dotnet run otlp-probe.cs`.
// O1: forwarding on, the exporter resets before the collector's answer comes back: is it a forward failure?
// O2: a chunked export whose exporter half-closes (FIN) before the last chunk: is it taken as complete?
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Tracking;

Console.WriteLine($"Kronikol.Extensions.Otlp {typeof(OtlpTap).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} on .NET {Environment.Version}, {RuntimeInformation.OSDescription}");

// A collector that reads one request, counts it, waits for the release, then answers 200.
var collector = new TcpListener(IPAddress.Loopback, 0);
collector.Start();
var collectorPort = ((IPEndPoint)collector.LocalEndpoint).Port;
var collected = 0;
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
_ = Task.Run(async () =>
{
    while (true)
    {
        Socket s;
        try { s = await collector.AcceptSocketAsync(); } catch { return; }
        _ = Task.Run(async () =>
        {
            var seen = new List<byte>();
            var buf = new byte[65536];
            while (true)
            {
                var text = Encoding.ASCII.GetString(seen.ToArray());
                var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (end >= 0)
                {
                    var length = text.Split("\r\n").Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Select(l => int.Parse(l[15..].Trim())).FirstOrDefault();
                    if (seen.Count >= end + 4 + length) break;
                }
                var n = await s.ReceiveAsync(buf);
                if (n == 0) return;
                seen.AddRange(buf.AsSpan(0, n).ToArray());
            }
            Interlocked.Increment(ref collected);
            await release.Task;
            await s.SendAsync("HTTP/1.1 200 OK\r\nContent-Type: application/x-protobuf\r\nContent-Length: 0\r\n\r\n"u8.ToArray());
            await Task.Delay(1000);
            s.Dispose();
        });
    }
});

// ---- O1
{
    var (tap, log) = await Start(new Uri($"http://127.0.0.1:{collectorPort}"));
    var exporter = new TcpClient();
    await exporter.ConnectAsync(IPAddress.Loopback, tap.BoundPort);
    var body = new byte[64];
    await exporter.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"POST /v1/traces HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/x-protobuf\r\nContent-Length: {body.Length}\r\n\r\n"));
    await exporter.GetStream().WriteAsync(body);
    for (var i = 0; i < 100 && Volatile.Read(ref collected) == 0; i++) await Task.Delay(20);
    exporter.Client.LingerState = new LingerOption(true, 0);
    exporter.Dispose();                         // the exporter hangs up while the collector is answering
    await Task.Delay(500);
    release.TrySetResult();
    await Task.Delay(1500);
    Console.WriteLine($"O1 forward on, exporter reset before the answer: collector received {collected} export(s); {Counters(tap)}");
    foreach (var line in log) Console.WriteLine($"    log: {line}");
    await tap.DisposeAsync();
}

// ---- O2 (and O2b, a size line that is not hex)
foreach (var (label, chunks) in new[]
{
    ("O2 chunked, FIN before the last chunk", "10\r\n0123456789abcdef\r\n"),
    ("O2b chunked, a size line that is not hex", "10\r\n0123456789abcdef\r\nzz\r\n"),
})
{
    var (tap, log) = await Start(null);
    var exporter = new TcpClient();
    await exporter.ConnectAsync(IPAddress.Loopback, tap.BoundPort);
    await exporter.GetStream().WriteAsync(Encoding.ASCII.GetBytes("POST /v1/traces HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/x-protobuf\r\nTransfer-Encoding: chunked\r\n\r\n" + chunks));
    exporter.Client.Shutdown(SocketShutdown.Send);   // no terminating 0-size chunk
    var buf = new byte[4096];
    using var cts = new CancellationTokenSource(3000);
    string answer;
    try { var n = await exporter.Client.ReceiveAsync(buf, SocketFlags.None, cts.Token); answer = n == 0 ? "a FIN, no answer" : Encoding.ASCII.GetString(buf, 0, n).Split("\r\n")[0]; }
    catch (OperationCanceledException) { answer = "nothing within 3 s"; }
    catch (SocketException ex) { answer = $"a reset ({ex.SocketErrorCode})"; }
    await Task.Delay(500);
    Console.WriteLine($"{label}: the exporter got \"{answer}\"; {Counters(tap)}");
    exporter.Dispose();
    await tap.DisposeAsync();
}

collector.Stop();
return;

static async Task<(OtlpTap, List<string>)> Start(Uri? forward)
{
    var log = new List<string>();
    var tap = new OtlpTap(new OtlpTapOptions { ListenPort = 0, ListenHost = "127.0.0.1", ForwardBaseUri = forward, Sink = new NoSink(), Log = l => { lock (log) log.Add(l); } });
    await tap.StartAsync();
    return (tap, log);
}

static string Counters(OtlpTap tap) => string.Join(", ", typeof(OtlpTap).GetProperties()
    .Where(p => p.PropertyType == typeof(long)).Select(p => $"{p.Name} {p.GetValue(tap)}"));

sealed class NoSink : IRequestResponseSink { public void Log(RequestResponseLog log) { } }
