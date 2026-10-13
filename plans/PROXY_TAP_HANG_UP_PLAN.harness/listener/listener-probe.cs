// listener-probe.cs: what a bare HttpListener throws and writes when a caller hangs up, and whether Abort() is safe in
// every state ProxyTap can reach (plan §2.3, §4). `dotnet run listener-probe.cs`; the same file on each OS.
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

Console.WriteLine($".NET {Environment.Version}, {RuntimeInformation.OSDescription}, HttpListener {(OperatingSystem.IsWindows() ? "http.sys" : "managed")}");
var port = FreePort();
using var listener = new HttpListener();
listener.Prefixes.Add($"http://localhost:{port}/");
listener.Start();

// ---- 1. Reading the body: the caller resets, or half-closes (FIN), mid-body
foreach (var hangUp in new[] { "reset", "FIN" })
    for (var i = 0; i < 5; i++)
    {
        var client = await Partial(port);
        var ctx = await listener.GetContextAsync();
        var read = Task.Run(async () => { await ctx.Request.InputStream.CopyToAsync(Stream.Null); });
        await Task.Delay(200);
        if (hangUp == "reset") { client.Client.LingerState = new LingerOption(true, 0); client.Dispose(); }
        else client.Client.Shutdown(SocketShutdown.Send);
        var outcome = await Outcome(read);
        if (hangUp == "FIN")
        {
            // What a caller still listening receives when the server gives up with Abort(), with nothing sent yet.
            ctx.Response.Abort();
            outcome += $"; after Abort() the caller receives: {await Receive(client)}";
            client.Dispose();
        }
        Console.WriteLine($"1. body read, caller {hangUp} mid-body (try {i + 1}): {outcome}");
    }

// ---- 2. What Abort() and Close() put on the wire for a caller still listening, by what was set first
foreach (var (label, act) in new (string, Action<HttpListenerResponse>)[]
{
    ("Abort(), nothing set", r => r.Abort()),
    ("StatusCode=400, Abort()", r => { r.StatusCode = 400; r.Abort(); }),
    ("StatusCode=400, KeepAlive=false, Close()", r => { r.StatusCode = 400; r.KeepAlive = false; r.Close(); }),
})
{
    var client = await Partial(port);
    var ctx = await listener.GetContextAsync();
    client.Client.Shutdown(SocketShutdown.Send);
    try { await ctx.Request.InputStream.CopyToAsync(Stream.Null); } catch { }
    var threw = Try(() => act(ctx.Response));
    Console.WriteLine($"2. {label} ({threw}): the caller receives {await Receive(client)}");
    client.Dispose();
}

// ---- 3. Writing the answer: the caller resets while the server writes 8 MB
{
    var client = new TcpClient { ReceiveBufferSize = 16 * 1024 };
    await client.ConnectAsync("localhost", port);
    await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"GET /big HTTP/1.1\r\nHost: localhost:{port}\r\n\r\n"));
    var ctx = await listener.GetContextAsync();
    ctx.Response.ContentLength64 = 8 * 1024 * 1024;
    var write = ctx.Response.OutputStream.WriteAsync(new byte[8 * 1024 * 1024]).AsTask();
    await client.GetStream().ReadExactlyAsync(new byte[1]);
    client.Client.LingerState = new LingerOption(true, 0);
    client.Dispose();
    Console.WriteLine($"3. answer write, caller reset mid-answer: {await Outcome(write)}");
    Console.WriteLine($"3. then Close(): {Try(() => ctx.Response.Close())}; then Abort(): {Try(() => ctx.Response.Abort())}");
}

// ---- 4. Abort() in every state the tap's finally can meet
{
    var (ctx, client) = await Answered(listener, port);
    Console.WriteLine($"4. Abort() after Close(): {Try(() => ctx.Response.Abort())}");
    client.Dispose();
    var twiceClient = await Partial(port);
    var twice = await listener.GetContextAsync();
    Console.WriteLine($"4. Abort() twice: {Try(() => { twice.Response.Abort(); twice.Response.Abort(); })}");
    twiceClient.Dispose();

    // Keep-alive: Close() hands the connection to the next request; an Abort() of the OLD response must not touch it.
    var keep = new TcpClient();
    await keep.ConnectAsync("localhost", port);
    var s = keep.GetStream();
    await s.WriteAsync(Encoding.ASCII.GetBytes($"GET /1 HTTP/1.1\r\nHost: localhost:{port}\r\n\r\n"));
    var first = await listener.GetContextAsync();
    first.Response.ContentLength64 = 2; first.Response.OutputStream.Write("ok"u8); first.Response.Close();
    await s.WriteAsync(Encoding.ASCII.GetBytes($"GET /2 HTTP/1.1\r\nHost: localhost:{port}\r\n\r\n"));
    var second = await listener.GetContextAsync();
    var abortOld = Try(() => first.Response.Abort());
    second.Response.ContentLength64 = 2; second.Response.OutputStream.Write("ok"u8);
    var secondClose = Try(() => second.Response.Close());
    await Task.Delay(200);
    var buf = new byte[4096]; var total = 0;
    using (var cts = new CancellationTokenSource(2000)) { try { while (true) { var n = await s.ReadAsync(buf.AsMemory(total), cts.Token); if (n == 0) break; total += n; if (Encoding.ASCII.GetString(buf, 0, total).Split("HTTP/1.1 200").Length > 2) break; } } catch { } }
    Console.WriteLine($"4. keep-alive: Abort() of the closed first response: {abortOld}; second answered: {secondClose}, two 200s on the wire: {Encoding.ASCII.GetString(buf, 0, total).Split("HTTP/1.1 200").Length - 1 == 2}");
    keep.Dispose();
}

// ---- 5. Abort() after the listener is closed, on a context from before the close
{
    var client = await Partial(port);
    var ctx = await listener.GetContextAsync();
    listener.Close();
    Console.WriteLine($"5. Abort() after HttpListener.Close(): {Try(() => ctx.Response.Abort())}");
    client.Dispose();
}
return;

static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); var port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }

static async Task<TcpClient> Partial(int port)
{
    var c = new TcpClient();
    await c.ConnectAsync("localhost", port);
    await c.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"POST /q HTTP/1.1\r\nHost: localhost:{port}\r\nContent-Length: 1000000\r\n\r\n{{"));
    return c;
}

static async Task<(HttpListenerContext, TcpClient)> Answered(HttpListener listener, int port)
{
    var c = new TcpClient();
    await c.ConnectAsync("localhost", port);
    await c.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"GET / HTTP/1.1\r\nHost: localhost:{port}\r\nConnection: close\r\n\r\n"));
    var ctx = await listener.GetContextAsync();
    ctx.Response.ContentLength64 = 2;
    ctx.Response.OutputStream.Write("ok"u8);
    ctx.Response.Close();
    return (ctx, c);
}

static async Task<string> Outcome(Task t)
{
    try { await t.WaitAsync(TimeSpan.FromSeconds(5)); return "completed"; }
    catch (TimeoutException) { return "still pending after 5 s"; }
    catch (HttpListenerException ex) { return $"HttpListenerException {ex.ErrorCode} \"{ex.Message}\""; }
    catch (Exception ex) { return $"{ex.GetType().Name} \"{ex.Message}\"{(ex.InnerException is { } inner ? $" (inner {inner.GetType().Name})" : "")}"; }
}

static async Task<string> Receive(TcpClient c)
{
    var buffer = new byte[4096];
    using var cts = new CancellationTokenSource(3000);
    try
    {
        var n = await c.Client.ReceiveAsync(buffer, SocketFlags.None, cts.Token);
        return n == 0 ? "a FIN, no bytes" : $"\"{Encoding.ASCII.GetString(buffer, 0, n).Split("\r\n")[0]}\" ({n} bytes)";
    }
    catch (OperationCanceledException) { return "nothing within 3 s"; }
    catch (SocketException ex) { return $"a reset ({ex.SocketErrorCode})"; }
}

static string Try(Action a)
{
    try { a(); return "no exception"; }
    catch (Exception ex) { return $"THREW {ex.GetType().Name}: {ex.Message}"; }
}
