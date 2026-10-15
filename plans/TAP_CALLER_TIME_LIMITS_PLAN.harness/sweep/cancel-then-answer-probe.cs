#:property PublishAot=false
// cancel-then-answer-probe.cs, for plans/TAP_CALLER_TIME_LIMITS_PLAN.md (#173), R2 items 3 and 4: is a socket still usable
// for an answer after a CancellationToken cancelled its pending read?
// Run: `dotnet run cancel-then-answer-probe.cs [trials]` (20 trials per variant by default).
//
// The caller is a Socket that sends a partial head ("POST /v1/traces HTTP/1.1\r\nHost: x\r\n") and stalls. It keeps one
// receive pending throughout, so the time of each byte it hears, of a FIN (a receive of 0) and of an RST
// (SocketException ConnectionReset) is recorded.
// The server is a TcpListener on 127.0.0.1, port 0 (the probe keeps its listener). It serves each connection as OtlpTap
// does at 3240c983: TcpClient.NoDelay, TcpClient.GetStream(), a 16 KiB buffer read by NetworkStream.ReadAsync(Memory<byte>,
// CancellationToken) (HttpRequestReader.cs L285-290). It first reads the partial head, then starts the read that stalls,
// and:
//   a  NetworkStream.ReadAsync(Memory<byte>, token) is pending; 200 ms later the token's source is cancelled. The read is
//      awaited and its exception recorded. Then NetworkStream.WriteAsync of the 408 and FlushAsync with a fresh 5 s token,
//      Socket.Shutdown(Send), and the stream and the client disposed.
//   b  the same with Socket.ReceiveAsync(Memory<byte>, SocketFlags.None, token) as the read (the write is the same).
//   c  no cancellation: 200 ms into the pending NetworkStream.ReadAsync another thread does Socket.Send of the 408, then
//      Shutdown(Both), then, 100 ms later so that the read's end can be told apart, disposes the client. How the pending
//      read ends is recorded.
//   d  as a, but the caller sends 100 more bytes 50 ms after the cancel, and the server writes 50 ms after they are sent.
//      After the write, one more read (a fresh 1 s token) shows whether the caller's bytes were kept.
//   e  as d without the later read, so the server closes with the caller's 100 bytes unread. Not one of the four variants
//      asked for: it is what a tap does that answers and closes when the stalled caller starts sending again.
// The answer is "HTTP/1.1 408 Request Timeout\r\nContent-Length: 0\r\nConnection: close\r\n\r\n". Times are milliseconds
// after the cut: the cancel in a, b, d and e, the start of the other thread's Send in c.
// The caller's receive loop stops at its first FIN or RST, so 50 ms after that the probe also reads both ends of the
// connection from the TCP table (IPGlobalProperties.GetActiveTcpConnections, before the caller closes): the caller in
// CloseWait and the server in FinWait2 is a clean close; both gone means an RST came, even after a FIN.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var trials = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 20;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;

Console.WriteLine($"cancel-then-answer-probe on .NET {Environment.Version}, {RuntimeInformation.OSDescription} ({RuntimeInformation.RuntimeIdentifier}), {Environment.ProcessorCount} logical CPUs");
Console.WriteLine($"{trials} trials per variant, each on a new connection; started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
Console.WriteLine($"server: TcpListener on 127.0.0.1:{port}");
Console.WriteLine($"the caller's partial head ({Wire.PartialHead.Length} B): {Wire.Escape(Wire.PartialHead)}");
Console.WriteLine($"the server's answer ({Wire.Answer.Length} B): {Wire.Escape(Wire.Answer)}");
Console.WriteLine($"the caller's later bytes in d and e ({Wire.Extra.Length} B): {Wire.Escape(Wire.Extra)}");
Console.WriteLine("times: ms after the cut (the cancel in a, b, d and e; the start of the other thread's Send in c)");

var variants = new (char Id, string What)[]
{
    ('a', "NetworkStream.ReadAsync(Memory<byte>, token) cancelled after 200 ms; WriteAsync + FlushAsync of the 408 (fresh 5 s token); Shutdown(Send); dispose"),
    ('b', "Socket.ReceiveAsync(Memory<byte>, SocketFlags.None, token) cancelled after 200 ms; then as a"),
    ('c', "no cancellation: 200 ms into the pending NetworkStream.ReadAsync another thread does Socket.Send of the 408, Shutdown(Both), and 100 ms later disposes"),
    ('d', "as a; the caller sends 100 more bytes 50 ms after the cancel, the server writes 50 ms after that, then reads once more (fresh 1 s token)"),
    ('e', "as d without the later read: the server closes with the caller's 100 bytes unread"),
};

// One trial per variant first, not counted, so the first counted trial does not pay for the JIT.
foreach (var (id, _) in variants)
    await RunTrialAsync(id);
Console.WriteLine("warm-up: one trial per variant, not counted");

var summaries = new List<Summary>();
foreach (var (id, what) in variants)
{
    Console.WriteLine();
    Console.WriteLine($"== variant {id}: {what}");
    var summary = new Summary(id);
    for (var i = 1; i <= trials; i++)
    {
        var trial = await RunTrialAsync(id);
        summary.Add(trial);
        Console.WriteLine($"{id} #{i:00}  {trial.Line()}");
    }

    summaries.Add(summary);
}

Console.WriteLine();
Console.WriteLine("== summary. 408 heard: the caller received the whole answer (and nothing else). then: how its connection ended after");
Console.WriteLine("   that (FIN: a receive of 0; RST: ConnectionReset). ms: median and max after the cut. clean in tcp: 50 ms after");
Console.WriteLine("   the end the caller was in CloseWait and the server in FinWait2 (no RST, even after the FIN).");
Console.WriteLine();
Console.WriteLine($"{"variant",-8} {"read pending at the cut",-24} {"408 heard",-10} {"then FIN",-9} {"then RST",-9} {"other end",-10} {"408 at ms",-13} {"end at ms",-13} {"clean in tcp",-12}");
foreach (var s in summaries)
    Console.WriteLine($"{s.Variant,-8} {s.PendingAtCut,-24} {s.Heard,-10} {s.Count(v => v.End == "FIN"),-9} {s.Count(v => v.End == "RST"),-9} {s.Count(v => v.End is not "FIN" and not "RST"),-10} {s.AnswerMs,-13} {s.EndMs,-13} {s.CleanInTcp,-12}");

Console.WriteLine();
Console.WriteLine("== per variant: how the server's read ended, its write, its shutdown, the later read (d), the caller's view");
foreach (var s in summaries)
{
    Console.WriteLine($"{s.Variant}");
    foreach (var line in s.Details())
        Console.WriteLine($"  {line}");
}

Console.WriteLine();
Console.WriteLine($"finished {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
listener.Stop();
return;

async Task<Trial> RunTrialAsync(char variant)
{
    var accept = listener.AcceptTcpClientAsync();
    var caller = new Caller();
    await caller.ConnectAsync(port);
    var client = await accept;
    var trial = new Trial(variant);
    try
    {
        await Server.ServeAsync(client, variant, caller, trial);
    }
    catch (Exception ex)
    {
        trial.Note = "serve threw " + Wire.Chain(ex);
    }

    await caller.WaitForEndAsync(TimeSpan.FromSeconds(3));
    await Task.Delay(50);
    trial.Tcp = Tcp.Pair(caller.LocalPort, port);
    trial.Caller = caller.View(trial.CutTicks);
    caller.Dispose();
    return trial;
}

static class Tcp
{
    /// <summary>Both ends of one loopback connection in the TCP table: "caller CloseWait, server FinWait2", or "gone".</summary>
    public static string Pair(int callerPort, int serverPort)
    {
        TcpState? caller = null;
        TcpState? server = null;
        foreach (var c in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections())
        {
            if (c.LocalEndPoint.Port == callerPort && c.RemoteEndPoint.Port == serverPort)
                caller = c.State;
            else if (c.LocalEndPoint.Port == serverPort && c.RemoteEndPoint.Port == callerPort)
                server = c.State;
        }

        return $"caller {caller?.ToString() ?? "gone"}, server {server?.ToString() ?? "gone"}";
    }
}

static class Wire
{
    public static readonly byte[] PartialHead = Encoding.ASCII.GetBytes("POST /v1/traces HTTP/1.1\r\nHost: x\r\n");
    public static readonly byte[] Answer = Encoding.ASCII.GetBytes("HTTP/1.1 408 Request Timeout\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
    public static readonly byte[] Extra = Encoding.ASCII.GetBytes("X-Later: " + new string('x', 89) + "\r\n");

    public static string Escape(byte[] bytes) => Encoding.ASCII.GetString(bytes).Replace("\r", "\\r").Replace("\n", "\\n");

    public static string Chain(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
            parts.Add(e is SocketException s ? $"SocketException({s.SocketErrorCode})" : e.GetType().Name);
        return string.Join(" > ", parts);
    }

    public static double Ms(long from, long to) => Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;

    public static string At(long from, long to) => to == 0 ? "?" : $"+{Ms(from, to):0.0}";
}

sealed record CallerView(int Bytes, bool FullAnswer, bool OnlyTheAnswer, double? AnswerMs, string End, double? EndMs);

sealed class Caller : IDisposable
{
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    private readonly MemoryStream _received = new();
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _answerTicks;
    private long _endTicks;
    private string? _end;

    public int LocalPort { get; private set; }

    public async Task ConnectAsync(int port)
    {
        await _socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
        LocalPort = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        await _socket.SendAsync(Wire.PartialHead, SocketFlags.None);
        _ = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>Sends the caller's later bytes (variants d and e).</summary>
    public async Task<string> SendExtraAsync()
    {
        try
        {
            var n = await _socket.SendAsync(Wire.Extra, SocketFlags.None);
            return $"{n} B sent";
        }
        catch (Exception ex)
        {
            return "send threw " + Wire.Chain(ex);
        }
    }

    public Task WaitForEndAsync(TimeSpan limit) => Task.WhenAny(_ended.Task, Task.Delay(limit));

    public CallerView View(long cutTicks)
    {
        lock (_received)
        {
            var bytes = _received.ToArray();
            var full = bytes.Length >= Wire.Answer.Length && bytes.AsSpan(0, Wire.Answer.Length).SequenceEqual(Wire.Answer);
            var only = bytes.AsSpan().SequenceEqual(Wire.Answer);
            return new CallerView(
                bytes.Length,
                full,
                only,
                _answerTicks == 0 ? null : Wire.Ms(cutTicks, _answerTicks),
                _end ?? "still open after 3 s",
                _endTicks == 0 ? null : Wire.Ms(cutTicks, _endTicks));
        }
    }

    public void Dispose() => _socket.Dispose();

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                int n;
                try
                {
                    n = await _socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None);
                }
                catch (Exception ex)
                {
                    Finish(ex is SocketException { SocketErrorCode: SocketError.ConnectionReset } ? "RST" : Wire.Chain(ex), Stopwatch.GetTimestamp());
                    return;
                }

                var now = Stopwatch.GetTimestamp();
                if (n == 0)
                {
                    Finish("FIN", now);
                    return;
                }

                lock (_received)
                {
                    _received.Write(buffer, 0, n);
                    if (_answerTicks == 0 && _received.Length >= Wire.Answer.Length)
                        _answerTicks = now;
                }
            }
        }
        finally
        {
            _ended.TrySetResult();
        }
    }

    private void Finish(string end, long ticks)
    {
        lock (_received)
        {
            _end = end;
            _endTicks = ticks;
        }
    }
}

sealed class Trial(char variant)
{
    public char Variant { get; } = variant;
    public bool PendingAtCut { get; set; }
    public long CutTicks { get; set; }
    public string Read { get; set; } = "?";
    public long ReadEndedTicks { get; set; }
    public string? ExtraSent { get; set; }
    public long ExtraSentTicks { get; set; }
    public string Write { get; set; } = "?";
    public long WriteDoneTicks { get; set; }
    public string? LaterRead { get; set; }
    public string Shutdown { get; set; } = "?";
    public long ShutdownTicks { get; set; }
    public bool? ReadEndedBeforeDispose { get; set; }
    public long ClosedTicks { get; set; }
    public string? Note { get; set; }
    public string? Tcp { get; set; }
    public CallerView? Caller { get; set; }

    public string Line()
    {
        var parts = new List<string> { $"read pending at the cut: {(PendingAtCut ? "yes" : "no")}" };
        if (Variant == 'c')
        {
            parts.Add($"Socket.Send: {Write} at {Wire.At(CutTicks, WriteDoneTicks)}");
            parts.Add($"Shutdown(Both): {Shutdown} at {Wire.At(CutTicks, ShutdownTicks)}");
            parts.Add($"read: {Read} at {Wire.At(CutTicks, ReadEndedTicks)} ({(ReadEndedBeforeDispose == true ? "before" : "after")} the dispose)");
            parts.Add($"disposed at {Wire.At(CutTicks, ClosedTicks)}");
        }
        else
        {
            parts.Add($"read: {Read} at {Wire.At(CutTicks, ReadEndedTicks)}");
            if (ExtraSent is not null)
                parts.Add($"caller's later bytes: {ExtraSent} at {Wire.At(CutTicks, ExtraSentTicks)}");
            parts.Add($"write: {Write} at {Wire.At(CutTicks, WriteDoneTicks)}");
            if (LaterRead is not null)
                parts.Add($"later read: {LaterRead}");
            parts.Add($"Shutdown(Send): {Shutdown}");
            parts.Add($"disposed at {Wire.At(CutTicks, ClosedTicks)}");
        }

        if (Caller is { } c)
        {
            var heard = c.FullAnswer ? $"the 408{(c.OnlyTheAnswer ? "" : $" and {c.Bytes - Wire.Answer.Length} B more")} at +{c.AnswerMs:0.0}" : $"{c.Bytes} B, not the whole 408";
            parts.Add($"caller: {heard}, then {c.End}{(c.EndMs is { } end ? $" at +{end:0.0}" : "")}");
        }

        if (Tcp is not null)
            parts.Add($"tcp 50 ms later: {Tcp}");
        if (Note is not null)
            parts.Add(Note);
        return string.Join(" | ", parts);
    }
}

static class Server
{
    public static async Task ServeAsync(TcpClient client, char variant, Caller caller, Trial t)
    {
        using (client)
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var socket = client.Client;
            var buffer = new byte[16 * 1024];

            // The partial head, read as OtlpTap's reader reads it before it waits for the rest.
            var got = 0;
            while (got < Wire.PartialHead.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(got), CancellationToken.None);
                if (n == 0)
                {
                    t.Note = "the caller closed inside the head";
                    return;
                }

                got += n;
            }

            if (variant == 'c')
            {
                await WithoutCancellationAsync(client, stream, socket, buffer, t);
                return;
            }

            using var cts = new CancellationTokenSource();
            var read = Observe(variant == 'b'
                ? socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, cts.Token)
                : stream.ReadAsync(buffer.AsMemory(), cts.Token));
            await Task.Delay(200);
            t.PendingAtCut = !read.IsCompleted;
            t.CutTicks = Stopwatch.GetTimestamp();
            cts.Cancel();
            var (bytes, error, ended) = await read;
            t.Read = error is null ? $"returned {bytes} B" : Wire.Chain(error);
            t.ReadEndedTicks = ended;

            if (variant is 'd' or 'e')
            {
                var wait = TimeSpan.FromMilliseconds(50) - Stopwatch.GetElapsedTime(t.CutTicks);
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait);
                t.ExtraSentTicks = Stopwatch.GetTimestamp();
                t.ExtraSent = await caller.SendExtraAsync();
                await Task.Delay(50);
            }

            using (var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                try
                {
                    await stream.WriteAsync(Wire.Answer, writeCts.Token);
                    await stream.FlushAsync(writeCts.Token);
                    t.Write = "ok";
                }
                catch (Exception ex)
                {
                    t.Write = Wire.Chain(ex);
                }

                t.WriteDoneTicks = Stopwatch.GetTimestamp();
            }

            if (variant == 'd')
                t.LaterRead = await LaterReadAsync(stream, buffer);

            try
            {
                socket.Shutdown(SocketShutdown.Send);
                t.Shutdown = "ok";
            }
            catch (Exception ex)
            {
                t.Shutdown = Wire.Chain(ex);
            }

            t.ShutdownTicks = Stopwatch.GetTimestamp();
            await stream.DisposeAsync();
        }

        t.ClosedTicks = Stopwatch.GetTimestamp();
    }

    private static async Task WithoutCancellationAsync(TcpClient client, NetworkStream stream, Socket socket, byte[] buffer, Trial t)
    {
        var read = Observe(stream.ReadAsync(buffer.AsMemory(), CancellationToken.None));
        await Task.Delay(200);
        t.PendingAtCut = !read.IsCompleted;
        await Task.Run(() =>
        {
            t.CutTicks = Stopwatch.GetTimestamp();
            try
            {
                var n = socket.Send(Wire.Answer);
                t.Write = n == Wire.Answer.Length ? "ok" : $"sent {n} B";
            }
            catch (Exception ex)
            {
                t.Write = Wire.Chain(ex);
            }

            t.WriteDoneTicks = Stopwatch.GetTimestamp();
            try
            {
                socket.Shutdown(SocketShutdown.Both);
                t.Shutdown = "ok";
            }
            catch (Exception ex)
            {
                t.Shutdown = Wire.Chain(ex);
            }

            t.ShutdownTicks = Stopwatch.GetTimestamp();
            Thread.Sleep(100);
            t.ReadEndedBeforeDispose = read.IsCompleted;
            client.Dispose();
            t.ClosedTicks = Stopwatch.GetTimestamp();
        });

        var (bytes, error, ended) = await read;
        t.Read = error is null ? $"returned {bytes} B" : Wire.Chain(error);
        t.ReadEndedTicks = ended;
    }

    private static async Task<string> LaterReadAsync(NetworkStream stream, byte[] buffer)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var seen = 0;
        try
        {
            while (seen < Wire.Extra.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(seen), cts.Token);
                if (n == 0)
                    return $"EOF after {seen} B";
                seen += n;
            }

            return buffer.AsSpan(0, seen).SequenceEqual(Wire.Extra) ? $"{seen} B, the caller's bytes" : $"{seen} B, not the caller's bytes";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return $"nothing more within 1 s ({seen} B)";
        }
        catch (Exception ex)
        {
            return $"{Wire.Chain(ex)} after {seen} B";
        }
    }

    private static async Task<(int Bytes, Exception? Error, long EndedTicks)> Observe(ValueTask<int> pending)
    {
        try
        {
            var n = await pending.ConfigureAwait(false);
            return (n, null, Stopwatch.GetTimestamp());
        }
        catch (Exception ex)
        {
            return (0, ex, Stopwatch.GetTimestamp());
        }
    }
}

sealed class Summary(char variant)
{
    private readonly List<Trial> _trials = [];

    public char Variant { get; } = variant;

    public void Add(Trial trial) => _trials.Add(trial);

    public int Count(Func<CallerView, bool> predicate) => _trials.Count(t => t.Caller is { } c && predicate(c));

    public string PendingAtCut => $"{_trials.Count(t => t.PendingAtCut)}/{_trials.Count}";

    public string Heard => $"{Count(c => c.OnlyTheAnswer)}/{_trials.Count}";

    public string CleanInTcp => $"{_trials.Count(t => t.Tcp == "caller CloseWait, server FinWait2")}/{_trials.Count}";

    public string AnswerMs => MedianMax(_trials.Select(t => t.Caller?.AnswerMs));

    public string EndMs => MedianMax(_trials.Select(t => t.Caller?.EndMs));

    public IEnumerable<string> Details()
    {
        foreach (var (what, value) in new (string, Func<Trial, string?>)[]
                 {
                     ("read ended", t => t.Read + (t.ReadEndedBeforeDispose is { } before ? (before ? ", before the dispose" : ", after the dispose") : "")),
                     ("caller's later bytes", t => t.ExtraSent),
                     ("write", t => t.Write),
                     ("later read", t => t.LaterRead),
                     (Variant == 'c' ? "Shutdown(Both)" : "Shutdown(Send)", t => t.Shutdown),
                     ("caller heard", t => t.Caller is not { } c ? null : c.OnlyTheAnswer ? "the 408 and nothing else" : c.FullAnswer ? "the 408 and more" : $"{c.Bytes} B, not the whole 408"),
                     ("caller's end", t => t.Caller?.End),
                     ("tcp 50 ms after the end", t => t.Tcp),
                     ("note", t => t.Note),
                 })
        {
            var groups = _trials.Select(value).Where(v => v is not null).GroupBy(v => v!).OrderByDescending(g => g.Count()).ToArray();
            if (groups.Length > 0)
                yield return $"{what}: " + string.Join("; ", groups.Select(g => $"{g.Key} x{g.Count()}"));
        }

        yield return $"read ended at ms (median/max): {MedianMax(_trials.Select(t => (double?)Wire.Ms(t.CutTicks, t.ReadEndedTicks)))}";
        yield return $"write done at ms (median/max): {MedianMax(_trials.Select(t => t.WriteDoneTicks == 0 ? null : (double?)Wire.Ms(t.CutTicks, t.WriteDoneTicks)))}";
        yield return $"closed at ms (median/max): {MedianMax(_trials.Select(t => t.ClosedTicks == 0 ? null : (double?)Wire.Ms(t.CutTicks, t.ClosedTicks)))}";
        yield return $"the 408 heard at ms (median/max): {AnswerMs}; the end at ms (median/max): {EndMs}";
    }

    private static string MedianMax(IEnumerable<double?> values)
    {
        var sorted = values.Where(v => v is not null).Select(v => v!.Value).Order().ToArray();
        if (sorted.Length == 0)
            return "-";
        var median = sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        return $"{median:0.0}/{sorted[^1]:0.0}";
    }
}
