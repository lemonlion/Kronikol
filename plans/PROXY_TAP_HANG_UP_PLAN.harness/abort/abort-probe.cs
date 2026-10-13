// abort-probe.cs: what HttpListenerResponse.Abort() puts on the wire for a caller that has NOT hung up: it is still owed
// most of its body and is reading (plan §2.3, §4.1). `dotnet run abort-probe.cs`; the same file on each OS.
// A bare HttpListener. The caller (a raw socket) sends `POST /x` with `Content-Length: 1000` and 10 body bytes, does not
// half-close, and reads from just before the handler acts until 3 s after the handler's Abort(). The handler, 200 ms
// after it has the request:
//   (a) Abort(), with nothing set and the body unread;
//   (b) StatusCode = 400, then Abort(), the body unread;
//   (c) one ReadAsync of the body into a 10-byte buffer (the 10 bytes it can have), then Abort();
//   (c2) one ReadAsync into a 4096-byte buffer, awaited for at most 2 s, then Abort(), then what the read did.
// Three tries of each. The caller's bytes are printed as text, CR and LF as \r and \n; a try whose bytes match try 1's
// apart from the Date header's value is printed as "same as try 1".
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

Console.WriteLine($".NET {Environment.Version}, {RuntimeInformation.OSDescription}, HttpListener {(OperatingSystem.IsWindows() ? "http.sys" : "managed")}");
_ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(3)); Console.WriteLine("WATCHDOG: the probe ran past 3 min and was stopped"); Environment.Exit(2); });

var port = FreePort();
var listener = new HttpListener();
listener.Prefixes.Add($"http://localhost:{port}/");
listener.Start();

var cases = new (string Label, Func<HttpListenerContext, Task<string>> Act)[]
{
    ("(a) Abort(), nothing set, the body unread", ctx => Task.FromResult($"Abort(): {Try(ctx.Response.Abort)}")),
    ("(b) StatusCode = 400, then Abort(), the body unread", ctx => Task.FromResult($"StatusCode = 400: {Try(() => ctx.Response.StatusCode = 400)}; Abort(): {Try(ctx.Response.Abort)}")),
    ("(c) one ReadAsync into a 10-byte buffer, then Abort()", ctx => ReadOnceThenAbort(ctx, 10)),
    ("(c2) one ReadAsync into a 4096-byte buffer (2 s cap), then Abort()", ctx => ReadOnceThenAbort(ctx, 4096)),
};

foreach (var (label, act) in cases)
{
    Console.WriteLine();
    Console.WriteLine($"{label}:");
    string? first = null;
    for (var t = 1; t <= 3; t++)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("localhost", port);
        await client.Client.SendAsync(Encoding.ASCII.GetBytes($"POST /x HTTP/1.1\r\nHost: localhost:{port}\r\nContent-Length: 1000\r\n\r\n0123456789"));
        var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);
        using var window = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var caller = WhatTheCallerReads(client.Client, window.Token);
        var handler = await act(ctx);
        window.CancelAfter(TimeSpan.FromSeconds(3)); // the caller reads until 3 s after the handler's Abort()
        var (bytes, end) = await caller;
        var text = Escape(bytes);
        var masked = Regex.Replace(text, "Date: [^\\\\]*", "Date: ...") + end;
        if (first is not null && masked == first)
        {
            Console.WriteLine($"  try {t}: handler: {handler}; the caller read: same as try 1");
            continue;
        }
        first ??= masked;
        Console.WriteLine($"  try {t}: handler: {handler}");
        Console.WriteLine($"    the caller read {bytes.Length} bytes{(bytes.Length > 0 ? $": \"{text}\"" : "")}");
        Console.WriteLine($"    then: {end}");
    }
}

listener.Close();
return;

static async Task<string> ReadOnceThenAbort(HttpListenerContext ctx, int size)
{
    var read = ctx.Request.InputStream.ReadAsync(new byte[size]).AsTask();
    string outcome;
    try { outcome = $"returned {await read.WaitAsync(TimeSpan.FromSeconds(2))} bytes"; }
    catch (TimeoutException) { outcome = "still pending after 2 s"; }
    catch (Exception ex) { outcome = $"threw {Chain(ex)}"; }
    var abort = Try(ctx.Response.Abort);
    var then = "";
    if (outcome.StartsWith("still"))
    {
        try { then = $"; the pending read then returned {await read.WaitAsync(TimeSpan.FromSeconds(2))} bytes"; }
        catch (TimeoutException) { then = "; the pending read was still pending 2 s after Abort()"; }
        catch (Exception ex) { then = $"; the pending read then threw {Chain(ex)}"; }
    }
    return $"ReadAsync({size}): {outcome}; Abort(): {abort}{then}";
}

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }

static async Task<(byte[] Bytes, string End)> WhatTheCallerReads(Socket socket, CancellationToken window)
{
    var received = new List<byte>();
    var buffer = new byte[8192];
    try
    {
        while (true)
        {
            var n = await socket.ReceiveAsync(buffer, SocketFlags.None, window);
            if (n == 0) return (received.ToArray(), "EOF (a receive returned 0)");
            received.AddRange(buffer.AsSpan(0, n).ToArray());
        }
    }
    catch (OperationCanceledException) { return (received.ToArray(), "nothing more within 3 s of the Abort() (the connection is still open)"); }
    catch (Exception ex) { return (received.ToArray(), $"a reset: {Chain(ex)}"); }
}

static string Try(Action a)
{
    try { a(); return "no exception"; }
    catch (Exception ex) { return $"THREW {Chain(ex)}"; }
}

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
    return $"{ex.GetType().FullName} HResult=0x{ex.HResult:X8}{extra} \"{ex.Message}\"";
}

static string Chain(Exception ex)
{
    var parts = new List<string>();
    for (Exception? e = ex; e is not null; e = e.InnerException)
        parts.Add(Describe(e));
    return string.Join(" <- inner ", parts);
}
