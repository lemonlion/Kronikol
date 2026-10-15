#:property PublishAot=false
// send-rate-piece-probe.cs, for plans/TAP_CALLER_TIME_LIMITS_PLAN.md (#173), section 4.3 item 4: ProxyTap is to write
// each answer in pieces of at most 16 KiB (ContentLength64 first, then one awaited WriteAsync per piece), where today it
// writes the whole body in one WriteAsync. On Windows HttpListener is http.sys, whose send-rate timer
// (MinSendBytesPerSecond, 150 B/s by default) resets a connection whose send does not complete in time. The model fitted
// in ../listener/README.md ("Also found", item 2) says a stalled send is reset (pending bytes / 150, rounded up to a
// multiple of 5 s) + 5 s after it began: about 115 s for a 16 KiB piece, 35 s for 4 KiB and 15 s for 1 KiB. That run's
// stalled send was always the answer's first, so it could not tell a deadline taken from the pending piece from one
// taken from every byte since the answer began, or from the bytes TCP had yet to deliver. This probe runs the three
// piece sizes and prints the same rule applied to each of the three.
//
// Run on Windows: `dotnet run send-rate-piece-probe.cs > send-rate-piece-probe-windows.txt`. stdout is the result and
// stderr is progress; the argument `quick` caps the run at 40 s, a smoke test that is not a measurement. Elsewhere the
// probe stops at once: the managed listener on Linux and macOS has no send-rate timer.
//
// Server: one HttpListener on http://localhost:<free port>/, its TimeoutManager untouched. For each GET /?piece=N it sets
// ContentLength64 = 8 MiB and writes the body in pieces of N bytes, one WriteAsync per piece, each awaited before the
// next, on a thread of its own. It records when each call is made and when each write completes, and the time and
// exception (type, ErrorCode, NativeErrorCode, HResult) of the write that fails. The first write carries the head.
//
// Callers: nine raw sockets, three per piece size (16 KiB, 4 KiB, 1 KiB). All nine connect first and then send their
// requests back to back, so all nine run at once. Each has ReceiveBufferSize = 16 KiB, sends "GET /?piece=N HTTP/1.1"
// with a Host header, and posts no receive, so its receive window closes. While the server's write is pending the probe
// only polls, every 0.25 s, Socket.Available (the bytes that reached the caller; it consumes nothing) and the TCP table
// (whether each end of the connection is still there). Once the server's write has failed the caller is told to read,
// and it reports what it gets.
//
// Before the callers start: an excerpt of `dotnet --info`, the Windows build and http.sys's file version, the timeouts
// of the request queue that serves the port (`netsh http show servicestate view=requestq`), and one HEAD exchange on a
// separate connection, which shows the head that write 1 carries. The run is capped at 8 minutes. The source holds no
// backslash: CR, LF and the backslash are built from char codes.
#pragma warning disable CA1416 // the registry is read on Windows only; the probe stops before that elsewhere
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
return await Probe.RunAsync(args);

static class Probe
{
    internal const int Rate = 150;
    internal const long AnswerLength = 8L * 1024 * 1024;
    internal const int CallerReceiveBuffer = 16 * 1024;
    internal const int CallersPerPiece = 3;
    internal static readonly int[] PieceSizes = { 16 * 1024, 4 * 1024, 1024 };
    internal static readonly Stopwatch Clock = Stopwatch.StartNew();
    internal static double Now => Clock.Elapsed.TotalSeconds;
    internal static readonly string CR = ((char)13).ToString();
    internal static readonly string LF = ((char)10).ToString();
    internal static readonly string CRLF = CR + LF;
    internal static readonly string BS = ((char)92).ToString();
    internal static readonly byte[] HeadEnd = { 13, 10, 13, 10 };
    static readonly object PrintGate = new();

    internal static void Out(string line)
    {
        lock (PrintGate)
        {
            Console.Out.Write(line + LF);
            Console.Out.Flush();
        }
    }

    internal static void Progress(string text)
    {
        lock (PrintGate) Console.Error.Write($"[{Now,6:0.0} s] {text}{LF}");
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var quick = args.Contains("quick");
        var cap = quick ? 40.0 : 480.0;
        // The cap: the process ends `cap` seconds after it started, whatever is pending. The watch below ends 10 s
        // earlier, so the report is printed first.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, cap - Now)));
            Out($"CAP: the probe reached {cap:0} s and was stopped before its report was complete.");
            Environment.Exit(2);
        });

        Out("dotnet --info (excerpt):");
        foreach (var line in DotnetInfoExcerpt()) Out(line);
        Out("");
        Out($"send-rate-piece-probe on .NET {Environment.Version}, {RuntimeInformation.OSDescription} ({RuntimeInformation.RuntimeIdentifier}), {Environment.ProcessorCount} logical CPUs");
        if (!OperatingSystem.IsWindows())
        {
            Out("This probe measures http.sys and runs on Windows only: the managed listener on Linux and macOS has no send-rate timer.");
            return 1;
        }

        Out($"Windows build {WindowsBuild()}; http.sys file version {HttpSysVersion()}");
        Out($"started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z{(quick ? " in QUICK mode (a smoke run, not a measurement)" : "")}, capped at {cap:0} s. Times are seconds from the probe's start.");

        var target = (await Dns.GetHostAddressesAsync("localhost"))[0];
        var port = FreePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var byPort = new ConcurrentDictionary<int, Conn>();
        _ = Task.Run(() => DispatchAsync(listener, byPort));
        Out($"server: HttpListener on http://localhost:{port}/ with its default timeouts (TimeoutManager untouched); callers connect to {target}, localhost's first address");
        Out("");
        Out("netsh http show servicestate view=requestq, the request queue that serves the port:");
        foreach (var line in QueueReport(port)) Out("  " + line);

        var (headLength, headText) = await HeadAsync(target, port);
        Out("");
        Out(headLength > 0
            ? $"the answer's head, read from a HEAD exchange on a separate connection: {headLength} bytes, '{headText}'"
            : $"the HEAD exchange gave no head: {headText}");

        var conns = new List<Conn>();
        foreach (var piece in PieceSizes)
        {
            for (var k = 1; k <= CallersPerPiece; k++)
            {
                var socket = await Connect(target, port, CallerReceiveBuffer);
                var conn = new Conn(piece, k, socket);
                conns.Add(conn);
                byPort[conn.CallerPort] = conn;
            }
        }

        Out($"callers: {conns.Count} raw sockets, {CallersPerPiece} per piece size, each with ReceiveBufferSize = {conns[0].Caller.ReceiveBufferSize:N0}; none posts a receive until the server's write has failed");
        Progress("nine callers connected; sending their requests");
        foreach (var conn in conns) await conn.SendRequestAsync(port);
        Out($"the nine requests were sent from {conns.Min(c => c.SentAt):0.000} to {conns.Max(c => c.SentAt):0.000} s");

        var watchUntil = cap - 10;
        var announced = new HashSet<Conn>();
        while (Now < watchUntil)
        {
            await Task.Delay(250);
            var table = TcpTable();
            foreach (var conn in conns)
            {
                conn.Observe(table, port);
                if (conn.FailureNote() is { } note && announced.Add(conn)) Progress(note);
            }

            if (conns.All(c => c.DrainDone)) break;
        }

        var capped = !conns.All(c => c.DrainDone);
        Report(conns, headLength, capped);
        foreach (var conn in conns) conn.Cleanup();
        try { listener.Close(); } catch { }
        Out("");
        Out($"done at {Now:0.0} s");
        return 0;
    }

    static async Task DispatchAsync(HttpListener listener, ConcurrentDictionary<int, Conn> byPort)
    {
        while (true)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { return; }
            try
            {
                if (ctx.Request.HttpMethod == "HEAD")
                {
                    // The head a GET for this answer gets, without the body.
                    ctx.Response.ContentLength64 = AnswerLength;
                    ctx.Response.Close();
                    continue;
                }

                var remote = ctx.Request.RemoteEndPoint.Port;
                if (!int.TryParse(ctx.Request.QueryString["piece"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var piece) || piece <= 0 || !byPort.TryGetValue(remote, out var conn))
                {
                    Progress($"an unexpected request from port {remote}: {ctx.Request.HttpMethod} {ctx.Request.RawUrl}");
                    ctx.Response.Abort();
                    continue;
                }

                conn.Start(ctx, piece);
            }
            catch (Exception ex) { Progress($"dispatch failed: {Describe(ex)}"); }
        }
    }

    // ------------------------------------------------------------------------------------------------ the report
    static void Report(List<Conn> conns, int headLength, bool capped)
    {
        Out("");
        Out("== Each connection. The stall began when the last write completed or, if none did, when write 1 was called (write 1 carries the");
        Out("   head). 'model' is ../listener/README.md's: the pending send's bytes / 150 B/s, rounded up to a multiple of 5 s, plus 5 s. A");
        Out("   timer that is checked every 5 s fires between that rounded value and the model, depending on its phase: the 'window'.");
        Out("   'all bytes' is the same rule over every byte posted since write 1 was called, the head included, timed from that call.");
        Out("   'not at the caller' is the same rule over the bytes posted (head, completed writes and the pending one) less the bytes the");
        Out("   caller's socket held when the probe saw the stall: what TCP still had to deliver.");
        foreach (var conn in conns)
        {
            Out("");
            foreach (var line in conn.Describe(headLength)) Out(line);
        }

        Out("");
        Out("== Summary, one row per connection. Times in seconds; 'after stall' is when the pending write failed, less when the stall began.");
        Out($"{"piece",-7} {"caller",-6} {"writes done",11} {"bytes before stall",18} {"pending send",12} {"stall began",11} {"failed at",9} {"after stall",11} {"model",5} {"minus model",11} {"all-bytes model",15} {"not-at-caller model",19}  exception");
        foreach (var conn in conns) Out(conn.Row(headLength));
        Out("");
        foreach (var piece in PieceSizes) Out(PieceSummary(piece, conns.Where(c => c.Piece == piece).ToList(), headLength));
        if (capped) Out("The watch ended at the cap with some connection still waiting: its write is listed as still pending.");
    }

    static string PieceSummary(int piece, List<Conn> group, int headLength)
    {
        var rows = group.Select(c => c.Outcome(headLength)).ToList();
        var failed = rows.Where(r => r.FailedAfter >= 0).ToList();
        var models = rows.Select(r => r.Model).Distinct().OrderBy(m => m).Select(m => m.ToString("0", CultureInfo.InvariantCulture));
        var windows = rows.Select(r => $"{r.Model - 5:0} to {r.Model:0} s").Distinct();
        var after = failed.Count == 0 ? "none failed" : $"{failed.Min(r => r.FailedAfter):0.000} to {failed.Max(r => r.FailedAfter):0.000} s after the stall";
        return $"{piece / 1024} KiB: the pending write failed in {failed.Count} of {rows.Count}, {after}; model {string.Join("/", models)} s (window {string.Join(", ", windows)}); body bytes before the stall {string.Join("/", rows.Select(r => r.BytesBefore.ToString("N0", CultureInfo.InvariantCulture)))}";
    }

    internal static double Ceil5(double seconds) => Math.Ceiling(seconds / 5) * 5;

    internal static double Model(long bytes) => Ceil5((double)bytes / Rate) + 5;

    // ------------------------------------------------------------------------------------------------ helpers
    internal static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    internal static async Task<Socket> Connect(IPAddress target, int port, int receiveBuffer)
    {
        var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        if (receiveBuffer > 0) socket.ReceiveBufferSize = receiveBuffer;
        await socket.ConnectAsync(new IPEndPoint(target, port));
        return socket;
    }

    static async Task<(int Length, string Text)> HeadAsync(IPAddress target, int port)
    {
        try
        {
            using var socket = await Connect(target, port, 0);
            await socket.SendAsync(Encoding.ASCII.GetBytes($"HEAD / HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}{CRLF}"), SocketFlags.None);
            var buffer = new byte[4096];
            var data = new MemoryStream();
            using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                var n = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, bound.Token);
                if (n == 0) return (-1, $"FIN after {data.Length} bytes");
                data.Write(buffer, 0, n);
                var bytes = data.ToArray();
                var end = bytes.AsSpan().IndexOf(HeadEnd);
                if (end >= 0) return (end + 4, Escape(bytes.AsSpan(0, end + 4)));
            }
        }
        catch (Exception ex) { return (-1, Describe(ex)); }
    }

    // Both ends of every TCP connection, keyed by (local port, remote port).
    static Dictionary<(int Local, int Remote), string> TcpTable()
    {
        var map = new Dictionary<(int, int), string>();
        try
        {
            foreach (var c in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections())
                map[(c.LocalEndPoint.Port, c.RemoteEndPoint.Port)] = c.State.ToString();
        }
        catch { }
        return map;
    }

    // `dotnet --info`: the SDK's version and commit, the runtime environment and the host, as the sweep's other outputs.
    static List<string> DotnetInfoExcerpt()
    {
        var lines = new List<string>();
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--info") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            var all = p.StandardOutput.ReadToEnd().Replace(CR, "").Split(LF);
            p.WaitForExit();
            var keep = 0;
            foreach (var line in all)
            {
                if (line.StartsWith(".NET SDK:", StringComparison.Ordinal)) { lines.Add(line); keep = 2; }
                else if (line.StartsWith("Runtime Environment:", StringComparison.Ordinal)) { lines.Add(line); keep = 4; }
                else if (line.StartsWith("Host:", StringComparison.Ordinal)) { lines.Add(line); keep = 3; }
                else if (keep > 0) { lines.Add(line); keep--; }
            }
        }
        catch (Exception ex) { lines.Add($"(dotnet --info failed: {ex.GetType().Name} {ex.Message})"); }
        return lines;
    }

    static string WindowsBuild()
    {
        try
        {
            var key = string.Join(BS, "HKEY_LOCAL_MACHINE", "SOFTWARE", "Microsoft", "Windows NT", "CurrentVersion");
            return $"10.0.{Registry.GetValue(key, "CurrentBuild", "?")}.{Registry.GetValue(key, "UBR", "?")} ({Registry.GetValue(key, "DisplayVersion", "?")})";
        }
        catch (Exception ex) { return $"? ({ex.GetType().Name})"; }
    }

    static string HttpSysVersion()
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(Path.Combine(Environment.SystemDirectory, "drivers", "http.sys"));
            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        }
        catch (Exception ex) { return $"? ({ex.GetType().Name})"; }
    }

    // The registered URLs and each Timeouts section (the URL group's, then the server session's) of the queue that serves
    // the port. The view is split on the unindented "Request queue name:" only: each URL group repeats it indented.
    static List<string> QueueReport(int port)
    {
        var result = new List<string>();
        try
        {
            var psi = new ProcessStartInfo("netsh", "http show servicestate view=requestq") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            var text = p.StandardOutput.ReadToEnd().Replace(CR, "");
            p.WaitForExit();
            var block = text.Split(LF + "Request queue name:").FirstOrDefault(b => b.Contains($":{port}/", StringComparison.OrdinalIgnoreCase));
            if (block is null)
            {
                result.Add($"(no request queue lists port {port})");
                return result;
            }

            var lines = block.Split(LF);
            var section = "the queue";
            for (var i = 0; i < lines.Length; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("URL group ID:", StringComparison.Ordinal)) section = "URL group " + t["URL group ID:".Length..].Trim();
                else if (t.StartsWith("Server session ID:", StringComparison.Ordinal)) section = "server session " + t["Server session ID:".Length..].Trim();
                else if (t.Contains($":{port}/", StringComparison.OrdinalIgnoreCase)) result.Add($"registered URL: {t}");
                else if (t == "Timeouts:")
                {
                    var indent = lines[i].Length - lines[i].TrimStart().Length;
                    var items = new List<string>();
                    for (var j = i + 1; j < lines.Length; j++)
                    {
                        var l = lines[j];
                        if (l.Trim().Length == 0 || l.Length - l.TrimStart().Length <= indent) break;
                        items.Add(l.Trim());
                    }

                    result.Add($"{section}, Timeouts: {string.Join("; ", items)}");
                }
            }
        }
        catch (Exception ex) { result.Add($"(netsh failed: {ex.GetType().Name} {ex.Message})"); }
        return result;
    }

    internal static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var extra = e switch
            {
                HttpListenerException h => $" ErrorCode={h.ErrorCode} NativeErrorCode={h.NativeErrorCode}",
                SocketException s => $" SocketErrorCode={s.SocketErrorCode} ErrorCode={s.ErrorCode}",
                _ => "",
            };
            parts.Add($"{e.GetType().FullName} HResult=0x{e.HResult:X8}{extra} '{e.Message}'");
        }

        return string.Join(" <- inner ", parts);
    }

    internal static string Short(Exception ex) => ex switch
    {
        HttpListenerException h => $"HttpListenerException {h.ErrorCode}",
        SocketException s => $"SocketException {s.SocketErrorCode}",
        _ => ex.GetType().Name,
    };

    internal static string Escape(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder();
        foreach (var b in bytes)
        {
            if (b == 13) sb.Append(BS).Append('r');
            else if (b == 10) sb.Append(BS).Append('n');
            else if (b == 92) sb.Append(BS).Append(BS);
            else if (b < 32 || b > 126) sb.Append(BS).Append('x').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            else sb.Append((char)b);
        }

        return sb.ToString();
    }
}

// One caller and the server's side of its exchange.
sealed class Conn
{
    readonly object _gate = new();
    readonly List<(double At, long Value)> _available = new(); // each value Socket.Available took, from the poll that first saw it
    Writer? _writer;
    HttpListenerContext? _context;
    string? _availableError;
    double _availableErrorAt = -1, _stallSeenAt = -1;
    string? _callerTcp, _serverTcp;
    double _callerSeenAt = -1, _callerGoneAt = -1, _serverSeenAt = -1, _serverGoneAt = -1;
    Task? _drain;
    double _drainStartedAt = -1;
    string? _drainResult;

    public Conn(int piece, int index, Socket caller)
    {
        Piece = piece;
        Index = index;
        Caller = caller;
        CallerPort = ((IPEndPoint)caller.LocalEndPoint!).Port;
    }

    public int Piece { get; }
    public int Index { get; }
    public Socket Caller { get; }
    public int CallerPort { get; }
    public double SentAt { get; private set; } = -1;
    public double ContextAt { get; private set; } = -1;
    public int RequestedPiece { get; private set; }
    public string Name => $"{Piece / 1024} KiB #{Index}";
    public bool DrainDone { get { lock (_gate) return _drain is { IsCompleted: true }; } }

    public async Task SendRequestAsync(int port)
    {
        var request = Encoding.ASCII.GetBytes($"GET /?piece={Piece} HTTP/1.1{Probe.CRLF}Host: localhost:{port}{Probe.CRLF}{Probe.CRLF}");
        SentAt = Probe.Now;
        await Caller.SendAsync(request, SocketFlags.None);
    }

    public void Start(HttpListenerContext ctx, int piece)
    {
        lock (_gate)
        {
            if (_context is not null) return;
            _context = ctx;
            ContextAt = Probe.Now;
            RequestedPiece = piece;
        }

        ctx.Response.ContentLength64 = Probe.AnswerLength;
        Volatile.Write(ref _writer, new Writer(ctx.Response.OutputStream, piece, Probe.AnswerLength));
    }

    // One poll: both ends in the TCP table; the caller's Available until it is told to read; the stall; the failure.
    public void Observe(Dictionary<(int Local, int Remote), string> table, int serverPort)
    {
        var now = Probe.Now;
        var writer = Volatile.Read(ref _writer);
        lock (_gate)
        {
            if (table.TryGetValue((CallerPort, serverPort), out var callerState)) { _callerTcp = callerState; _callerSeenAt = now; }
            else if (_callerSeenAt >= 0 && _callerGoneAt < 0) _callerGoneAt = now;
            if (table.TryGetValue((serverPort, CallerPort), out var serverState)) { _serverTcp = serverState; _serverSeenAt = now; }
            else if (_serverSeenAt >= 0 && _serverGoneAt < 0) _serverGoneAt = now;

            if (_drain is null && _availableError is null)
            {
                try
                {
                    long value = Caller.Available;
                    if (_available.Count == 0 || _available[^1].Value != value) _available.Add((now, value));
                }
                catch (Exception ex) { _availableError = Probe.Short(ex); _availableErrorAt = now; }
            }

            if (writer is null) return;
            var failed = writer.Failed;
            if (_stallSeenAt < 0 && !failed && !writer.Finished && now - writer.LastProgressAt >= 1) _stallSeenAt = now;
            if (failed && _drain is null) StartDrain();
        }
    }

    // The caller is told to read: what it gets, until FIN, RST, or 3 s without a byte.
    void StartDrain()
    {
        var startedAt = Probe.Now;
        _drainStartedAt = startedAt;
        _drain = Task.Run(async () =>
        {
            var buffer = new byte[64 * 1024];
            long got = 0;
            string result;
            try
            {
                while (true)
                {
                    var receive = Caller.ReceiveAsync(buffer.AsMemory(), SocketFlags.None).AsTask();
                    if (await Task.WhenAny(receive, Task.Delay(3000)) != receive) { result = $"{got:N0} bytes, then nothing for 3 s (no FIN, no RST)"; break; }
                    var n = await receive;
                    if (n == 0) { result = $"{got:N0} bytes, then FIN {Probe.Now - startedAt:0.000} s after it started reading"; break; }
                    got += n;
                }
            }
            catch (SocketException ex)
            {
                var end = ex.SocketErrorCode == SocketError.ConnectionReset ? "RST" : $"SocketException {ex.SocketErrorCode}";
                result = $"{got:N0} bytes, then {end} {Probe.Now - startedAt:0.000} s after it started reading";
            }
            catch (Exception ex) { result = $"{got:N0} bytes, then {ex.GetType().Name} '{ex.Message}'"; }
            lock (_gate) _drainResult = result;
        });
    }

    public string? FailureNote()
    {
        var w = Volatile.Read(ref _writer);
        if (w is null || !w.Failed) return null;
        var s = w.Snap();
        return $"{Name}: write {s.FailedIndex + 1} failed at {s.FailedAt:0.0} s, {s.FailedAt - s.StallAt:0.0} s after the stall began ({Probe.Short(s.Error!)})";
    }

    public sealed record Result(long BytesBefore, long PendingBytes, double StallAt, double FailedAt, double FailedAfter, double Model, double AllBytesModel, double NotAtCallerModel, string Exception);

    public Result Outcome(int headLength)
    {
        var w = Volatile.Read(ref _writer);
        if (w is null) return new Result(0, 0, -1, -1, -1, double.NaN, double.NaN, double.NaN, "no context");
        var s = w.Snap();
        var head = Math.Max(0, headLength);
        var pendingIndex = s.Completed;
        var pending = s.Piece + (pendingIndex == 0 ? head : 0);
        var allBytes = head + (long)(pendingIndex + 1) * s.Piece;
        long held;
        lock (_gate) held = AvailableValueAt(_stallSeenAt);
        var failed = s.Error is not null;
        return new Result(
            (long)s.Completed * s.Piece,
            pending,
            s.StallAt,
            failed ? s.FailedAt : -1,
            failed ? s.FailedAt - s.StallAt : -1,
            Probe.Model(pending),
            Probe.Model(allBytes),
            held >= 0 ? Probe.Model(allBytes - held) : double.NaN,
            failed ? Probe.Short(s.Error!) : s.Finished ? "every write completed" : $"still pending at {Probe.Now:0.0}");
    }

    public string Row(int headLength)
    {
        var r = Outcome(headLength);
        var failedAt = r.FailedAt >= 0 ? r.FailedAt.ToString("0.000", CultureInfo.InvariantCulture) : "-";
        var after = r.FailedAfter >= 0 ? r.FailedAfter.ToString("0.000", CultureInfo.InvariantCulture) : "-";
        var minus = r.FailedAfter >= 0 ? (r.FailedAfter - r.Model).ToString("+0.000;-0.000", CultureInfo.InvariantCulture) : "-";
        var w = Volatile.Read(ref _writer);
        var done = w is null ? "-" : w.Snap().Completed.ToString(CultureInfo.InvariantCulture);
        return $"{Piece / 1024 + " KiB",-7} {"#" + Index,-6} {done,11} {r.BytesBefore,18:N0} {r.PendingBytes,12:N0} {r.StallAt,11:0.000} {failedAt,9} {after,11} {r.Model,5:0} {minus,11} {r.AllBytesModel,15:0} {r.NotAtCallerModel,19:0}  {r.Exception}";
    }

    public List<string> Describe(int headLength)
    {
        var lines = new List<string>();
        var w = Volatile.Read(ref _writer);
        lock (_gate)
        {
            lines.Add($"{Name} (caller port {CallerPort}): request sent at {SentAt:0.000}; " + (ContextAt >= 0 ? $"the server had its context at {ContextAt:0.000} (piece={RequestedPiece})" : "NO context reached the server"));
            if (w is null) return lines;
            var s = w.Snap();
            var head = Math.Max(0, headLength);
            lines.Add($"  writes: {s.Completed} of {s.Count} completed, {(long)s.Completed * s.Piece:N0} body bytes (the {headLength}-byte head went out with write 1); the longest WriteAsync call took {s.LongestCall * 1000:0.000} ms to return its Task");
            var completions = s.Completed == 0
                ? "none"
                : string.Join(", ", s.DoneAt.Take(s.Completed).Select(t => ((t - s.CalledAt[0]) * 1000).ToString("0.000", CultureInfo.InvariantCulture)));
            lines.Add($"  completions, ms after write 1 was called: {completions}");
            if (s.Completed >= s.Count)
            {
                lines.Add("  every write completed: no stall");
                return lines;
            }

            var stallAt = s.StallAt;
            var stallWhat = s.Completed > 0 ? $"write {s.Completed} completed" : "write 1 was called (no write completed)";
            var next = s.CalledAt.Length > s.Completed ? $"; write {s.Completed + 1} was called {(s.CalledAt[s.Completed] - stallAt) * 1000:0.000} ms later" : "";
            lines.Add($"  the stall began at {stallAt:0.000}, when {stallWhat}{next}; the caller held {AvailableAt(_stallSeenAt)} when the probe saw the stall at {_stallSeenAt:0.000}");

            var pendingIndex = s.Completed;
            var pending = s.Piece + (pendingIndex == 0 ? head : 0);
            var pendingText = pendingIndex == 0 ? $"{s.Piece:N0} B and the {head}-byte head, {pending:N0} B" : $"{pending:N0} B";
            var ceil = Probe.Ceil5((double)pending / Probe.Rate);
            var model = Probe.Model(pending);
            var allBytes = head + (long)(pendingIndex + 1) * s.Piece;
            var held = AvailableValueAt(_stallSeenAt);
            lines.Add(held >= 0
                ? $"  posted (head, {s.Completed} completed writes and the pending one): {allBytes:N0} B; not at the caller when the probe saw the stall: {allBytes - held:N0} B, so {(double)(allBytes - held) / Probe.Rate:0.00} s, model {Probe.Model(allBytes - held):0} s"
                : $"  posted (head, {s.Completed} completed writes and the pending one): {allBytes:N0} B; the caller's holding at the stall is unknown");
            if (s.Error is not null)
            {
                var after = s.FailedAt - stallAt;
                lines.Add($"  write {s.FailedIndex + 1} ({pendingText}) FAILED at {s.FailedAt:0.000} ({s.Where}), {after:0.000} s after the stall began: {Probe.Describe(s.Error)}");
                lines.Add($"  model for that send: {pending:N0} / {Probe.Rate} = {(double)pending / Probe.Rate:0.00} s, rounded up to {ceil:0} s, + 5 s = {model:0} s (window {ceil:0} to {model:0} s); measured minus model = {after - model:+0.000;-0.000} s");
                lines.Add($"  all bytes since write 1 was called: {allBytes:N0} B, so {(double)allBytes / Probe.Rate:0.00} s, model {Probe.Model(allBytes):0} s; measured {s.FailedAt - s.CalledAt[0]:0.000} s after write 1 was called");
                var before = _available.LastOrDefault(a => a.At < s.FailedAt);
                var reached = before.At > 0 ? before.Value - head - (long)s.Completed * s.Piece : -1;
                var reachedText = reached >= 0 ? $"; so {reached:N0} B of the pending send had reached the caller and {pending - reached:N0} B had not" : "";
                lines.Add($"  the caller held {(before.At > 0 ? $"{before.Value:N0} B" : "?")} at its last poll before the failure; {AvailableHistory()}{reachedText}");
            }
            else
            {
                lines.Add($"  write {pendingIndex + 1} ({pendingText}) STILL PENDING at {Probe.Now:0.000}, {Probe.Now - stallAt:0.000} s after the stall began; model {model:0} s (window {ceil:0} to {model:0} s); all bytes {allBytes:N0} B, model {Probe.Model(allBytes):0} s");
                lines.Add($"  the caller holds {(_available.Count > 0 ? $"{_available[^1].Value:N0} B" : "?")}; {AvailableHistory()}");
            }

            lines.Add($"  TCP table: the caller's end {Tcp(_callerTcp, _callerSeenAt, _callerGoneAt)}; http.sys's end {Tcp(_serverTcp, _serverSeenAt, _serverGoneAt)}");
            if (_drainStartedAt >= 0) lines.Add($"  the caller was told to read at {_drainStartedAt:0.000} and read {_drainResult ?? "(still reading at the report)"}");
            else lines.Add("  the caller was never told to read");
        }

        return lines;
    }

    string AvailableAt(double at)
    {
        var value = AvailableValueAt(at);
        return value >= 0 ? $"{value:N0} B" : "?";
    }

    // What Socket.Available read at the last poll at or before `at`, or -1. The caller holds _gate.
    long AvailableValueAt(double at)
    {
        if (at < 0) return -1;
        var sample = _available.LastOrDefault(a => a.At <= at);
        return sample.At > 0 ? sample.Value : -1;
    }

    string AvailableHistory()
    {
        var steps = string.Join(", ", _available.Select(a => $"{a.Value:N0} B from {a.At:0.00}"));
        var error = _availableError is null ? "" : $"; Available threw {_availableError} at {_availableErrorAt:0.00}";
        return $"Available over the run: {steps}{error}";
    }

    static string Tcp(string? state, double seenAt, double goneAt) =>
        seenAt < 0 ? "was never seen"
        : goneAt < 0 ? $"was still there at the end ({state}, seen at {seenAt:0.00})"
        : $"was {state} at {seenAt:0.00} and gone at {goneAt:0.00}";

    public void Cleanup()
    {
        try { _context?.Response.Abort(); } catch { }
        try { Caller.Dispose(); } catch { }
    }
}

// The answer's writes: one awaited WriteAsync per piece, on a thread of its own, so a call that blocks would show.
sealed class Writer
{
    readonly object _gate = new();
    readonly double[] _calledAt;
    readonly double[] _doneAt;
    int _completed, _failedIndex = -1;
    double _failedAt = -1, _longestCall, _lastProgressAt;
    Exception? _error;
    string _where = "";
    bool _finished;

    public Writer(Stream stream, int piece, long total)
    {
        Piece = piece;
        Count = (int)(total / piece);
        _calledAt = new double[Count];
        _doneAt = new double[Count];
        _lastProgressAt = Probe.Now;
        var buffer = new byte[piece];
        buffer.AsSpan().Fill((byte)'x');
        Done = Task.Factory.StartNew(() => Loop(stream, buffer), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public int Piece { get; }
    public int Count { get; }
    public Task Done { get; }
    public bool Failed { get { lock (_gate) return _error is not null; } }
    public bool Finished { get { lock (_gate) return _finished; } }
    public double LastProgressAt { get { lock (_gate) return _lastProgressAt; } }

    void Loop(Stream stream, byte[] buffer)
    {
        for (var i = 0; i < Count; i++)
        {
            var calledAt = Probe.Now;
            lock (_gate)
            {
                _calledAt[i] = calledAt;
                if (i == 0) _lastProgressAt = calledAt;
            }

            Task task;
            try { task = stream.WriteAsync(buffer, 0, Piece, CancellationToken.None); }
            catch (Exception ex) { Fail(i, ex, "the WriteAsync call itself threw"); return; }
            var returned = Probe.Now - calledAt;
            lock (_gate) _longestCall = Math.Max(_longestCall, returned);
            try { task.GetAwaiter().GetResult(); }
            catch (Exception ex) { Fail(i, ex, "its Task faulted"); return; }
            var doneAt = Probe.Now;
            lock (_gate)
            {
                _doneAt[i] = doneAt;
                _completed = i + 1;
                _lastProgressAt = doneAt;
            }
        }

        lock (_gate) _finished = true;
    }

    void Fail(int index, Exception ex, string where)
    {
        var at = Probe.Now;
        lock (_gate)
        {
            _failedAt = at;
            _failedIndex = index;
            _error = ex;
            _where = where;
            _finished = true;
        }
    }

    public sealed record Snapshot(int Piece, int Count, int Completed, double[] CalledAt, double[] DoneAt, int FailedIndex, double FailedAt, Exception? Error, string Where, double LongestCall, bool Finished)
    {
        // When the stall began: the last completion, or the first call if no write completed.
        public double StallAt => Completed > 0 ? DoneAt[Completed - 1] : CalledAt[0];
    }

    public Snapshot Snap()
    {
        lock (_gate)
        {
            var called = _calledAt[..Math.Min(Count, _completed + 1)];
            var done = _doneAt[.._completed];
            return new Snapshot(Piece, Count, _completed, called, done, _failedIndex, _failedAt, _error, _where, _longestCall, _finished);
        }
    }
}
