// socket-probe.cs: how a reset reaches a reader on a raw socket, with no HttpListener in the way (plan §2.3: the body
// read of a reset caller throws HttpListenerException 400 on Linux, the managed listener's answer to an end of stream).
// `dotnet run socket-probe.cs`; the same file on each OS. Raw sockets only. A server accepts; the client sends 10 bytes,
// then resets with LingerState(true, 0) and Socket.Dispose(), which closes at once: a pure RST. The server reads:
//   (a) 300 ms after the reset, with nothing pending: three calls in a row;
//   (b) with a receive already pending when the reset arrives (it has read the 10 bytes first), then two calls more;
//   (c) (a) and (b) again through NetworkStream.ReadAsync.
// Then the same reads, through Socket.ReceiveAsync, when the client is a TcpClient reset as probe.cs, listener-probe.cs
// and chunked-probe.cs (c)/(d) reset theirs, LingerState(true, 0) and TcpClient.Dispose(): TcpClient.Dispose() and
// NetworkStream.Dispose() call Shutdown(SocketShutdown.Both) before Close(), so the peer gets a FIN before the RST:
//   (d) the TcpClient sent through TcpClient.Client (no NetworkStream was created);
//   (e) the TcpClient sent through TcpClient.GetStream() (its NetworkStream is disposed with it).
// Every read has a 2 s cap (3 s for the pending one). Three tries of each; a try that read exactly as try 1 did is
// printed as "same as try 1".
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

Console.WriteLine($".NET {Environment.Version}, {RuntimeInformation.OSDescription}");
_ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(3)); Console.WriteLine("WATCHDOG: the probe ran past 3 min and was stopped"); Environment.Exit(2); });

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;

foreach (var (label, viaStream) in new[] { ("Socket.ReceiveAsync", false), ("NetworkStream.ReadAsync", true) })
{
    Console.WriteLine();
    Console.WriteLine($"The client resets with LingerState(true, 0) and Socket.Dispose() (a pure RST); the server reads through {label}");
    await Tries($"{(viaStream ? "(c-a)" : "(a)")} {label}, read 300 ms after the reset, nothing pending", () => AfterTheReset(viaStream, Client.Socket));
    await Tries($"{(viaStream ? "(c-b)" : "(b)")} {label}, a read pending when the reset arrives", () => PendingAtTheReset(viaStream, Client.Socket));
}

foreach (var (name, kind) in new[] { ("(d) a TcpClient that sent through TcpClient.Client", Client.TcpClientSocket), ("(e) a TcpClient that sent through TcpClient.GetStream()", Client.TcpClientStream) })
{
    Console.WriteLine();
    Console.WriteLine($"{name} resets with LingerState(true, 0) and TcpClient.Dispose(); the server reads through Socket.ReceiveAsync");
    await Tries($"{name[..3]} read 300 ms after the reset, nothing pending", () => AfterTheReset(false, kind));
    await Tries($"{name[..3]} a read pending when the reset arrives", () => PendingAtTheReset(false, kind));
}

listener.Stop();
return;

static async Task Tries(string label, Func<Task<List<string>>> once)
{
    var tries = new List<List<string>>();
    for (var t = 0; t < 3; t++) tries.Add(await once());
    Console.WriteLine($"{label}:");
    for (var t = 0; t < tries.Count; t++)
    {
        if (t > 0 && tries[t].SequenceEqual(tries[0]))
        {
            Console.WriteLine($"  try {t + 1}: same as try 1");
            continue;
        }
        Console.WriteLine($"  try {t + 1}:");
        foreach (var line in tries[t]) Console.WriteLine($"    {line}");
    }
}

async Task<(Socket Server, Func<byte[], Task> Send, Action Reset)> Pair(Client kind)
{
    var accept = listener.AcceptSocketAsync();
    if (kind == Client.Socket)
    {
        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var server = await accept.WaitAsync(TimeSpan.FromSeconds(5));
        return (server, async b => await client.SendAsync(b), () => { client.LingerState = new LingerOption(true, 0); client.Dispose(); });
    }
    var tcp = new TcpClient(AddressFamily.InterNetwork);
    await tcp.ConnectAsync(IPAddress.Loopback, port);
    var accepted = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    Func<byte[], Task> send = kind == Client.TcpClientStream
        ? async b => await tcp.GetStream().WriteAsync(b)
        : async b => await tcp.Client.SendAsync(b);
    return (accepted, send, () => { tcp.Client.LingerState = new LingerOption(true, 0); tcp.Dispose(); });
}

async Task<List<string>> AfterTheReset(bool viaStream, Client kind)
{
    var (server, send, reset) = await Pair(kind);
    using var _ = server;
    var read = Reader(server, viaStream);
    await send("0123456789"u8.ToArray());
    reset();
    await Task.Delay(300);
    var calls = new List<string>();
    for (var call = 1; call <= 3; call++)
        calls.Add($"call {call}: {await Read(read, 2000)}");
    return calls;
}

async Task<List<string>> PendingAtTheReset(bool viaStream, Client kind)
{
    var (server, send, reset) = await Pair(kind);
    using var _ = server;
    var read = Reader(server, viaStream);
    await send("0123456789"u8.ToArray());
    var first = await Read(read, 2000);
    var pendingRead = Read(read, 3000);
    await Task.Delay(200);
    var wasPending = !pendingRead.IsCompleted;
    reset();
    var pending = await pendingRead;
    var after = await Read(read, 2000);
    var again = await Read(read, 2000);
    return
    [
        $"first read (before the reset): {first}",
        $"the pending read ({(wasPending ? "pending when the reset was sent" : "NOT pending: it had completed before the reset")}): {pending}",
        $"the call after: {after}",
        $"and again: {again}",
    ];
}

static Func<Memory<byte>, CancellationToken, ValueTask<int>> Reader(Socket server, bool viaStream)
{
    if (!viaStream)
        return (buffer, ct) => server.ReceiveAsync(buffer, SocketFlags.None, ct);
    var stream = new NetworkStream(server, ownsSocket: false);
    return (buffer, ct) => stream.ReadAsync(buffer, ct);
}

static async Task<string> Read(Func<Memory<byte>, CancellationToken, ValueTask<int>> read, int capMs)
{
    var buffer = new byte[4096];
    using var cts = new CancellationTokenSource(capMs);
    try
    {
        var n = await read(buffer, cts.Token);
        return n == 0 ? "returned 0 (end of stream)" : $"returned {n} bytes \"{Encoding.ASCII.GetString(buffer, 0, n)}\"";
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested) { return $"nothing within {capMs / 1000.0:0.#} s"; }
    catch (Exception ex) { return $"threw {Chain(ex)}"; }
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

enum Client { Socket, TcpClientSocket, TcpClientStream }
