// forward-probe.cs: what HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead) throws when the
// upstream cuts its answer's body short (plan §3.3: would an IOException filter around the tap's forward misfile an
// upstream failure as a caller's?). `dotnet run forward-probe.cs`; the same file on each OS.
// The client is an HttpClient over a SocketsHttpHandler set up as ProxyTap's (UseProxy false, AllowAutoRedirect false,
// AutomaticDecompression None, UseCookies false), and calls as ProxyTap does: SendAsync with ResponseContentRead, then
// Content.ReadAsByteArrayAsync. The upstream is a raw socket: it reads the request's head, sends its answer, waits
// 300 ms (the client is then reading the rest of the body) and hangs up:
//   control: Content-Length: 1000 and all 1000 bytes, then FIN;
//   (a) Content-Length: 1000 and 500 bytes, then FIN;
//   (b) Content-Length: 1000 and 500 bytes, then RST (LingerState(true, 0), Dispose);
//   (c) Transfer-Encoding: chunked, one chunk, then FIN with no last chunk.
// Three tries of each, each with a fresh HttpClient and upstream; a try that threw exactly as try 1 did is printed as
// "same as try 1". The chain is the outer exception first, then each InnerException.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

Console.WriteLine($".NET {Environment.Version}, {RuntimeInformation.OSDescription}");
_ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(3)); Console.WriteLine("WATCHDOG: the probe ran past 3 min and was stopped"); Environment.Exit(2); });

var cases = new (string Label, string Answer, bool Reset)[]
{
    ("control: Content-Length: 1000, all 1000 bytes sent, then FIN", "HTTP/1.1 200 OK\r\nContent-Length: 1000\r\n\r\n" + new string('x', 1000), false),
    ("(a) Content-Length: 1000, 500 bytes sent, then FIN", "HTTP/1.1 200 OK\r\nContent-Length: 1000\r\n\r\n" + new string('x', 500), false),
    ("(b) Content-Length: 1000, 500 bytes sent, then RST", "HTTP/1.1 200 OK\r\nContent-Length: 1000\r\n\r\n" + new string('x', 500), true),
    ("(c) Transfer-Encoding: chunked, one chunk (a\\r\\n0123456789\\r\\n), then FIN with no last chunk", "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\na\r\n0123456789\r\n", false),
};

foreach (var (label, answer, reset) in cases)
{
    Console.WriteLine();
    Console.WriteLine($"{label}:");
    List<string>? first = null;
    for (var t = 1; t <= 3; t++)
    {
        var (lines, ms) = await Forward(answer, reset);
        if (first is not null && lines.SequenceEqual(first))
        {
            Console.WriteLine($"  try {t} ({ms} ms): same as try 1");
            continue;
        }
        first ??= lines;
        Console.WriteLine($"  try {t} ({ms} ms):");
        foreach (var line in lines) Console.WriteLine($"    {line}");
    }
}
return;

static async Task<(List<string> Lines, long Ms)> Forward(string answer, bool reset)
{
    var server = new TcpListener(IPAddress.Loopback, 0);
    server.Start();
    var port = ((IPEndPoint)server.LocalEndpoint).Port;
    var upstream = Task.Run(async () =>
    {
        var s = await server.AcceptSocketAsync();
        var seen = new List<byte>();
        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Encoding.ASCII.GetString(seen.ToArray()).IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0)
        {
            var n = await s.ReceiveAsync(buffer, SocketFlags.None, cts.Token);
            if (n == 0) break;
            seen.AddRange(buffer.AsSpan(0, n).ToArray());
        }
        await s.SendAsync(Encoding.ASCII.GetBytes(answer));
        await Task.Delay(300);
        if (reset)
        {
            s.LingerState = new LingerOption(true, 0);
            s.Dispose();
            return null;
        }
        s.Shutdown(SocketShutdown.Send);
        return s;
    });

    using var http = new HttpClient(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
    })
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        Timeout = TimeSpan.FromSeconds(20),
    };
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    using var message = new HttpRequestMessage(HttpMethod.Get, "/x");
    var watch = Stopwatch.StartNew();
    var step = "SendAsync";
    var lines = new List<string>();
    try
    {
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, deadline.Token);
        step = "Content.ReadAsByteArrayAsync";
        var body = await response.Content.ReadAsByteArrayAsync(deadline.Token);
        lines.Add($"no exception: {(int)response.StatusCode}, a body of {body.Length} bytes");
    }
    catch (Exception ex)
    {
        lines.Add($"{step} threw; the outer exception is IOException: {ex is IOException}");
        for (Exception? e = ex; e is not null; e = e.InnerException)
            lines.Add($"{(ReferenceEquals(e, ex) ? "" : "<- inner ")}{Describe(e)}");
    }
    var ms = watch.ElapsedMilliseconds;
    try { (await upstream.WaitAsync(TimeSpan.FromSeconds(5)))?.Dispose(); }
    catch (Exception ex) { lines.Add($"(the upstream socket: {Describe(ex)})"); }
    server.Stop();
    return (lines, ms);
}

static string Describe(Exception ex)
{
    var extra = ex switch
    {
        HttpRequestException r => $" HttpRequestError={r.HttpRequestError} StatusCode={(r.StatusCode is { } code ? ((int)code).ToString() : "null")}",
        HttpIOException io => $" HttpRequestError={io.HttpRequestError}",
        SocketException s => $" SocketErrorCode={s.SocketErrorCode} ErrorCode={s.ErrorCode} NativeErrorCode={s.NativeErrorCode}",
        _ => "",
    };
    return $"{ex.GetType().FullName} HResult=0x{ex.HResult:X8} ({ex.HResult}){extra} \"{ex.Message}\"";
}
