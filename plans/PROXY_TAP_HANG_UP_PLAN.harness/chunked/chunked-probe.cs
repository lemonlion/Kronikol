#:package Kronikol.Extensions.ProxyTap@4.18.1
// chunked-probe.cs: a CHUNKED request whose caller cuts the body short (plan §4.4 and Q6: does the managed listener read
// it as complete, so that ProxyTap forwards it cut short?). `dotnet run chunked-probe.cs`; the same file on each OS.
// The caller (a raw socket) sends `POST /x` with `Transfer-Encoding: chunked` and part of the body in one write; once the
// server holds the request and has been reading its body for 200 ms, the caller:
//   (a) has sent one whole chunk (a\r\n0123456789\r\n), then FIN (Shutdown(Send)) with no 0-size chunk, and keeps reading;
//   (b) has sent half a chunk (a\r\n01234), then FIN, and keeps reading;
//   (c), (d): (a) and (b) with LingerState(true, 0) and TcpClient.Dispose() in place of the FIN, as probe.cs and
//   listener-probe.cs reset their callers. TcpClient.Dispose() calls Shutdown(Both) before Close(), so this sends a
//   FIN and then an RST (socket/socket-probe.cs (d), (e));
//   (e), (f): (a) and (b) with a pure RST: LingerState(true, 0) and Dispose() of the TcpClient's Socket itself.
//   control: the whole body, 0-size chunk included (a\r\n0123456789\r\n0\r\n\r\n); the caller stays and reads.
// Part 1: a bare HttpListener whose handler reads the body with CopyToAsync into a MemoryStream (5 s cap) and sends
// nothing, so what a caller still reading hears in 3 s is the listener's own doing.
// Part 2: ProxyTap 4.18.1 (configured as probe/probe.cs does) in front of a raw socket upstream, one tap and one upstream
// per variant. The upstream records exactly what reaches it (the head, and a body of the head's Content-Length) and
// answers `HTTP/1.1 200 OK`, `Content-Length: 2`, `ok`. Read 5 s after the hang-up.
// Bytes are printed as text, CR and LF as \r and \n.
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;

Console.WriteLine($"ProxyTap {typeof(ProxyTap).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} on .NET {Environment.Version}, {RuntimeInformation.OSDescription}, HttpListener {(OperatingSystem.IsWindows() ? "http.sys" : "managed")}");
_ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(4)); Console.WriteLine("WATCHDOG: the probe ran past 4 min and was stopped"); Environment.Exit(2); });

var variants = new (string Label, string Chunks, HangUp HangUp)[]
{
    ("control: the whole body, 0-size chunk included; the caller stays", "a\r\n0123456789\r\n0\r\n\r\n", HangUp.None),
    ("(a) one whole chunk, then FIN, no 0-size chunk", "a\r\n0123456789\r\n", HangUp.Fin),
    ("(b) FIN mid-chunk (5 of its 10 bytes sent)", "a\r\n01234", HangUp.Fin),
    ("(c) one whole chunk, then LingerState(true, 0) and TcpClient.Dispose() (a FIN, then an RST)", "a\r\n0123456789\r\n", HangUp.TcpClientReset),
    ("(d) half a chunk (5 of its 10 bytes), then LingerState(true, 0) and TcpClient.Dispose() (a FIN, then an RST)", "a\r\n01234", HangUp.TcpClientReset),
    ("(e) one whole chunk, then a pure RST (LingerState(true, 0), Socket.Dispose())", "a\r\n0123456789\r\n", HangUp.SocketReset),
    ("(f) half a chunk (5 of its 10 bytes), then a pure RST (LingerState(true, 0), Socket.Dispose())", "a\r\n01234", HangUp.SocketReset),
};

// ---- Part 1: a bare HttpListener
Console.WriteLine();
Console.WriteLine("Part 1: a bare HttpListener; the handler reads the body with CopyToAsync (5 s cap) and sends nothing");
{
    var port = FreePort();
    var listener = new HttpListener();
    listener.Prefixes.Add($"http://localhost:{port}/");
    listener.Start();
    foreach (var (label, chunks, hangUp) in variants)
    {
        var client = new TcpClient();
        await client.ConnectAsync("localhost", port);
        await client.Client.SendAsync(Encoding.ASCII.GetBytes($"POST /x HTTP/1.1\r\nHost: localhost:{port}\r\nTransfer-Encoding: chunked\r\n\r\n{chunks}"));
        var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var body = new MemoryStream();
        var read = ctx.Request.InputStream.CopyToAsync(body);
        await Task.Delay(200); // the handler is reading the body when the caller hangs up
        var heard = HangUpAndListen(client, hangUp);
        var outcome = await BodyOutcome(read, body, TimeSpan.FromSeconds(5));
        Console.WriteLine($"{label}:");
        Console.WriteLine($"    request: HasEntityBody {ctx.Request.HasEntityBody}, ContentLength64 {ctx.Request.ContentLength64}");
        Console.WriteLine($"    the body read {outcome}");
        if (heard is not null) Console.WriteLine($"    the caller, still reading, got in 3 s: {await heard}");
        try { ctx.Response.Abort(); } catch { }
        try { client.Dispose(); } catch { }
    }
    listener.Close();
}

// ---- Part 2: through ProxyTap
Console.WriteLine();
Console.WriteLine("Part 2: ProxyTap 4.18.1 in front of a raw socket upstream that records what reaches it and answers 200 \"ok\"");
foreach (var (label, chunks, hangUp) in variants)
{
    var upstream = new TcpListener(IPAddress.Loopback, 0);
    upstream.Start();
    var reached = new ConcurrentQueue<string>();
    _ = Task.Run(async () =>
    {
        while (true)
        {
            Socket s;
            try { s = await upstream.AcceptSocketAsync(); } catch { return; }
            _ = Task.Run(() => Upstream(s, reached));
        }
    });

    var log = new List<string>();
    var port = FreePort();
    var tap = new ProxyTap(new ProxyTapOptions
    {
        ListenPort = port, ForwardBaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)upstream.LocalEndpoint).Port}"), CallerName = "app", ServiceName = "api",
        CaptureUnattributedRequests = true, FallbackTestId = "t1", Sink = new NoSink(),
        Log = line => { lock (log) log.Add(line); },
    });
    await tap.StartAsync();

    var client = new TcpClient();
    await client.ConnectAsync("localhost", port);
    await client.Client.SendAsync(Encoding.ASCII.GetBytes($"POST /x HTTP/1.1\r\nHost: localhost:{port}\r\nTransfer-Encoding: chunked\r\n\r\n{chunks}"));
    var held = "";
    if (hangUp != HangUp.None)
    {
        await WaitFor(() => InFlight(tap) == 1, 5000); // the tap holds the request and is reading its body
        held = InFlight(tap) == 1 ? "" : " (the tap did not hold the request within 5 s)";
        await Task.Delay(200);
    }
    var heard = HangUpAndListen(client, hangUp);
    var seen = heard is null ? null : await heard;
    await Task.Delay(heard is null ? 5000 : 2000); // 5 s after the hang-up in all

    Console.WriteLine($"{label}:{held}");
    Console.WriteLine($"    the upstream received: {(reached.IsEmpty ? "nothing" : string.Join(" | then ", reached))}");
    Console.WriteLine($"    tap: ForwardFailures {tap.ForwardFailures}, RequestsHandled {tap.RequestsHandled}, RequestsCaptured {tap.RequestsCaptured}, in flight {InFlight(tap)}");
    if (seen is not null) Console.WriteLine($"    the caller, still reading, got in 3 s: {seen}");
    lock (log) foreach (var line in log.Where(l => !l.Contains("proxy-tap listening"))) Console.WriteLine($"    log: {line}");

    await tap.DisposeAsync();
    try { client.Dispose(); } catch { }
    upstream.Stop();
}
return;

// ================================================================================================================

static Task<string>? HangUpAndListen(TcpClient client, HangUp hangUp)
{
    switch (hangUp)
    {
        case HangUp.TcpClientReset:
            client.Client.LingerState = new LingerOption(true, 0);
            client.Dispose();
            return null;
        case HangUp.SocketReset:
            client.Client.LingerState = new LingerOption(true, 0);
            client.Client.Dispose();
            return null;
        case HangUp.Fin:
            client.Client.Shutdown(SocketShutdown.Send);
            break;
    }
    return WhatTheCallerReads(client.Client, TimeSpan.FromSeconds(3));
}

static async Task<string> WhatTheCallerReads(Socket socket, TimeSpan window)
{
    var received = new List<byte>();
    var buffer = new byte[8192];
    using var cts = new CancellationTokenSource(window);
    string end;
    try
    {
        while (true)
        {
            var n = await socket.ReceiveAsync(buffer, SocketFlags.None, cts.Token);
            if (n == 0) { end = "EOF (a receive returned 0)"; break; }
            received.AddRange(buffer.AsSpan(0, n).ToArray());
        }
    }
    catch (OperationCanceledException) { end = $"nothing more within {window.TotalSeconds:0} s (the connection is still open)"; }
    catch (Exception ex) { end = $"a reset: {Chain(ex)}"; }
    return $"{received.Count} bytes{(received.Count > 0 ? $" \"{Escape(received.ToArray())}\"" : "")}, then {end}";
}

static async Task<string> BodyOutcome(Task read, MemoryStream body, TimeSpan cap)
{
    try
    {
        await read.WaitAsync(cap);
        return $"completed with no exception: {body.Length} bytes \"{Escape(body.ToArray())}\"";
    }
    catch (TimeoutException) { return $"blocked {cap.TotalSeconds:0} s ({body.Length} bytes read so far)"; }
    catch (Exception ex) { return $"threw after {body.Length} bytes: {Chain(ex)}"; }
}

// The upstream: one exchange after another on a connection. The head is read to its blank line, then a body of the head's
// Content-Length (or, if the head says chunked, everything up to the 0-size chunk), each recorded exactly as received.
static async Task Upstream(Socket s, ConcurrentQueue<string> reached)
{
    using var _ = s;
    var pending = new List<byte>();
    var buffer = new byte[65536];
    using var cap = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
        while (true)
        {
            int end;
            while ((end = CollectionsMarshal.AsSpan(pending).IndexOf("\r\n\r\n"u8)) < 0)
            {
                var n = await s.ReceiveAsync(buffer, SocketFlags.None, cap.Token);
                if (n == 0)
                {
                    if (pending.Count > 0) reached.Enqueue($"{pending.Count} bytes of a head, then EOF: \"{Escape(pending.ToArray())}\"");
                    return;
                }
                pending.AddRange(buffer.AsSpan(0, n).ToArray());
            }
            var head = pending.GetRange(0, end + 4).ToArray();
            pending.RemoveRange(0, end + 4);
            var headText = Encoding.ASCII.GetString(head);
            var length = 0;
            foreach (var line in headText.Split("\r\n").Skip(1))
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    length = int.Parse(line[15..].Trim());
            var chunked = headText.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase);
            int bodyLength;
            while (true)
            {
                bodyLength = chunked ? ChunkedEnd(pending) : (pending.Count >= length ? length : -1);
                if (bodyLength >= 0) break;
                var n = await s.ReceiveAsync(buffer, SocketFlags.None, cap.Token);
                if (n == 0)
                {
                    reached.Enqueue($"\"{Escape(head)}\" and {pending.Count} body bytes \"{Escape(pending.ToArray())}\" of {(chunked ? "a chunked body" : $"Content-Length {length}")}, then EOF");
                    return;
                }
                pending.AddRange(buffer.AsSpan(0, n).ToArray());
            }
            var body = pending.GetRange(0, bodyLength).ToArray();
            pending.RemoveRange(0, bodyLength);
            reached.Enqueue($"\"{Escape(head)}\" and a body of {body.Length} bytes \"{Escape(body)}\"");
            await s.SendAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"u8.ToArray());
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) { reached.Enqueue($"the upstream's connection failed: {Chain(ex)}"); }
}

// The length of a chunked body through its 0-size chunk and blank line, or -1 when it has not all arrived (no trailers).
static int ChunkedEnd(List<byte> data)
{
    var span = CollectionsMarshal.AsSpan(data);
    if (span.StartsWith("0\r\n\r\n"u8)) return 5;
    var at = span.IndexOf("\r\n0\r\n\r\n"u8);
    return at < 0 ? -1 : at + 7;
}

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }

static async Task WaitFor(Func<bool> condition, int ms)
{
    for (var waited = 0; !condition() && waited < ms; waited += 20) await Task.Delay(20);
}

static int InFlight(ProxyTap tap) => (int)typeof(ProxyTap).GetField("_inflight", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tap)!;

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

enum HangUp { None, Fin, TcpClientReset, SocketReset }

sealed class NoSink : IRequestResponseSink { public void Log(RequestResponseLog log) { } }
