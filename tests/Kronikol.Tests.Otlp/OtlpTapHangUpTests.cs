using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.Otlp;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// An exporter that hangs up, or sends a chunked export the reader cannot take whole (#172,
/// plans/PROXY_TAP_HANG_UP_PLAN.md R2). The tap owns its sockets, so nothing leaks; what went wrong was the reading:
/// an exporter gone before its answer counted as a forward failure, and a chunked body cut short was answered 200.
/// Exporters and collectors here are raw sockets, since an HTTP client cannot cut a request short on purpose.
/// </summary>
public class OtlpTapHangUpTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly byte[] Export = OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv);

    private static string ContentLengthHead(int length) =>
        $"POST /v1/traces HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: {length}\r\n\r\n";

    private static byte[] ContentLengthExport() => [.. Encoding.ASCII.GetBytes(ContentLengthHead(Export.Length)), .. Export];

    private const string ChunkedHead =
        "POST /v1/traces HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\n\r\n";

    [Fact]
    public async Task An_exporter_that_hangs_up_before_its_answer_is_no_forward_failure()
    {
        using var collector = new HeldCollector();
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(sink, log, o => o.ForwardBaseUri = collector.BaseUri);

        using var exporter = await ConnectAsync(tap);
        await exporter.Client.SendAsync(ContentLengthExport());
        await collector.HasExport.Task.WaitAsync(Patience);
        ResetAlone(exporter);
        await Task.Delay(300); // the exporter is gone before the collector answers
        collector.Answer();

        // The collector has the export: the answer the tap could not deliver is the exporter's loss, not a failed forward.
        // On Linux the write to the reset exporter fails at once; the count would move within a second.
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(1, collector.Received);
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Empty(Lines(log, "forward failed"));
        Assert.Equal(1, tap.RequestsReceived);
    }

    [Fact]
    public async Task A_forward_cut_short_by_disposal_is_no_forward_failure()
    {
        using var collector = new HeldCollector(); // never answers
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(sink, log, o => o.ForwardBaseUri = collector.BaseUri);

        using var exporter = await ConnectAsync(tap);
        await exporter.Client.SendAsync(ContentLengthExport());
        await collector.HasExport.Task.WaitAsync(Patience);
        await tap.DisposeAsync();

        // DisposeAsync returns before the export's handler ends; the exporter's end of stream is the handler's end.
        var (firstLine, letGo) = await HearAsync(exporter);
        Assert.True(letGo, $"the exporter heard {firstLine} and its connection was kept");
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Empty(Lines(log, "forward failed"));
        Assert.Equal("an end of stream", firstLine); // stopping is no 502: nobody is left to tell
    }

    public static TheoryData<string, string, bool> CutShortChunkedExports => new()
    {
        { "no zero-size chunk, then a FIN", "10\r\n0123456789abcdef\r\n", true },
        { "half a chunk, then a FIN", "10\r\n01234567", true },
        { "a size line that is not hex", "10\r\n0123456789abcdef\r\nzz\r\n", false },
        { "a size line over 1,024 bytes", "10;" + new string('x', 1100) + "\r\n0123456789abcdef\r\n0\r\n\r\n", false },
        { "chunk data longer than its size", "5\r\n0123456789\r\n0\r\n\r\n", false },
        { "trailers past 64 KiB", "a\r\n0123456789\r\n0\r\n" + string.Concat(Enumerable.Range(0, 70).Select(i => $"X-Pad-{i}: {new string('x', 1000)}\r\n")) + "\r\n", false },
    };

    [Theory]
    [MemberData(nameof(CutShortChunkedExports))]
    public async Task A_chunked_export_cut_short_is_not_received(string variant, string chunks, bool halfClose)
    {
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(sink, log);

        using var exporter = await ConnectAsync(tap);
        await exporter.Client.SendAsync(Encoding.ASCII.GetBytes(ChunkedHead + chunks));
        if (halfClose)
            exporter.Client.Shutdown(SocketShutdown.Send);

        // Not a request: the tap closes the connection with no answer, as it does for a Content-Length body cut short.
        var (firstLine, letGo) = await HearAsync(exporter);
        Assert.False(firstLine.StartsWith("HTTP/", StringComparison.Ordinal), $"{variant}: the exporter was answered {firstLine}");
        Assert.True(letGo, $"{variant}: the connection was kept");
        Assert.Equal(0, tap.RequestsReceived);
        await tap.DisposeAsync();
        Assert.Equal(0, tap.SpansReceived + tap.PayloadsFailed + tap.PayloadsDropped);
        Assert.Empty(sink.Snapshot());
    }

    [Fact]
    public async Task A_complete_chunked_export_is_received_and_answered()
    {
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(sink, log);

        using var exporter = await ConnectAsync(tap);
        await exporter.Client.SendAsync(Encoding.ASCII.GetBytes(ChunkedHead + Chunked(Export, extension: ";name=value") + "0\r\n\r\n"));

        var answers = await ReadAnswersAsync(exporter, 1);
        Assert.Equal([200], answers);
        Assert.Equal(1, tap.RequestsReceived);
        await WaitForAsync(() => sink.Count == 2, "the chunked export to be mapped");
    }

    public static TheoryData<string, string> ChunkedTrailers => new()
    {
        { "one field", "X-Checksum: 1\r\n" },
        { "two fields", "X-Checksum: 1\r\nX-Signature: 2\r\n" },
        { "a field longer than a size line may be", "X-Signature: " + new string('s', 2000) + "\r\n" },
    };

    [Theory]
    [MemberData(nameof(ChunkedTrailers))]
    public async Task A_chunked_export_with_trailers_keeps_the_connection_in_step(string variant, string trailers)
    {
        _ = variant; // names the case in the test's display name
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(sink, log);

        using var exporter = await ConnectAsync(tap);
        await exporter.Client.SendAsync(Encoding.ASCII.GetBytes(ChunkedHead + Chunked(Export) + "0\r\n" + trailers + "\r\n"));
        await exporter.Client.SendAsync(ContentLengthExport());

        // Each export gets its own answer, in order, on the one connection.
        var answers = await ReadAnswersAsync(exporter, 2);
        Assert.Equal([200, 200], answers);
        Assert.Equal(2, tap.RequestsReceived);
        await WaitForAsync(() => sink.Count == 4, "both exports to be mapped");
    }

    [Theory]
    [InlineData("80000000")]
    [InlineData("ffffffff")]
    [InlineData("100000000000000000")]
    public async Task A_chunk_size_past_int_is_too_large(string size)
    {
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(sink, log);

        using var exporter = await ConnectAsync(tap);
        // Nothing after the size line: the tap answers without draining, so bytes it never read would reset the connection.
        await exporter.Client.SendAsync(Encoding.ASCII.GetBytes(ChunkedHead + size + "\r\n"));

        var (firstLine, letGo) = await HearAsync(exporter);
        Assert.StartsWith("HTTP/1.1 413 ", firstLine);
        Assert.True(letGo, "the tap answered 413 and kept a connection it could not drain");
        Assert.Equal(1, tap.PayloadsRejected);
        Assert.Equal(0, tap.RequestsReceived);
        Assert.Empty(Lines(log, "connection error"));
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<OtlpTap> StartAsync(ListSink sink, List<string> log, Action<OtlpTapOptions>? configure = null)
    {
        var options = new OtlpTapOptions
        {
            ListenPort = 0,
            ListenHost = "127.0.0.1",
            Sink = sink,
            Log = line => { lock (log) log.Add(line); },
        };
        configure?.Invoke(options);
        var tap = new OtlpTap(options);
        await tap.StartAsync();
        return tap;
    }

    private static async Task<TcpClient> ConnectAsync(OtlpTap tap)
    {
        var exporter = new TcpClient();
        await exporter.ConnectAsync(IPAddress.Loopback, tap.BoundPort);
        return exporter;
    }

    /// <summary>A body as chunks of up to 100 bytes, each size line carrying <paramref name="extension"/>; no last chunk.</summary>
    private static string Chunked(byte[] body, string extension = "")
    {
        var text = new StringBuilder();
        for (var offset = 0; offset < body.Length; offset += 100)
        {
            var length = Math.Min(100, body.Length - offset);
            text.Append(length.ToString("x")).Append(extension).Append("\r\n")
                .Append(Encoding.ASCII.GetString(body, offset, length)).Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>An RST and nothing else: a zero linger, then the socket's own <c>Dispose()</c>.</summary>
    private static void ResetAlone(TcpClient exporter)
    {
        exporter.Client.LingerState = new LingerOption(true, 0);
        exporter.Client.Dispose();
    }

    private static string[] Lines(List<string> log, string containing)
    {
        lock (log)
            return log.Where(line => line.Contains(containing, StringComparison.Ordinal)).ToArray();
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting for {what}.");
            await Task.Delay(20);
        }
    }

    /// <summary>The first line the exporter hears, and whether the tap then let it go (an end of stream or a reset), within <see cref="Patience"/>.</summary>
    private static async Task<(string FirstLine, bool LetGo)> HearAsync(TcpClient exporter)
    {
        var heard = new List<byte>();
        var buffer = new byte[4096];
        using var hearing = new CancellationTokenSource(Patience);
        try
        {
            while (true)
            {
                var read = await exporter.Client.ReceiveAsync(buffer, SocketFlags.None, hearing.Token);
                if (read == 0)
                    return (FirstLine(heard, "an end of stream"), true);
                heard.AddRange(buffer.AsSpan(0, read).ToArray());
            }
        }
        catch (OperationCanceledException)
        {
            return (FirstLine(heard, $"nothing within {Patience.TotalSeconds:0} s"), false);
        }
        catch (SocketException ex)
        {
            return (FirstLine(heard, $"a reset ({ex.SocketErrorCode})"), true);
        }
    }

    private static string FirstLine(List<byte> heard, string ifNothing)
    {
        var text = Encoding.ASCII.GetString(heard.ToArray());
        var end = text.IndexOf("\r\n", StringComparison.Ordinal);
        return text.Length == 0 ? ifNothing : end < 0 ? text : text[..end];
    }

    /// <summary>The status of each of the next <paramref name="count"/> answers on the connection, by their Content-Length.</summary>
    private static async Task<int[]> ReadAnswersAsync(TcpClient exporter, int count)
    {
        var statuses = new List<int>();
        var pending = new List<byte>();
        var buffer = new byte[8192];
        using var hearing = new CancellationTokenSource(Patience);
        while (statuses.Count < count)
        {
            var text = Encoding.ASCII.GetString(pending.ToArray());
            var headEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headEnd >= 0)
            {
                var lines = text[..headEnd].Split("\r\n");
                var length = lines.Skip(1)
                    .Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    .Select(l => int.Parse(l["Content-Length:".Length..].Trim()))
                    .FirstOrDefault();
                if (pending.Count >= headEnd + 4 + length)
                {
                    statuses.Add(int.Parse(lines[0].Split(' ')[1]));
                    pending.RemoveRange(0, headEnd + 4 + length);
                    continue;
                }
            }

            int read;
            try
            {
                read = await exporter.Client.ReceiveAsync(buffer, SocketFlags.None, hearing.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"{statuses.Count} of {count} answer(s) within {Patience.TotalSeconds:0} s: [{string.Join(", ", statuses)}]");
                throw;
            }
            catch (SocketException ex)
            {
                // The tap closed the connection with bytes of ours unread, so the close was a reset.
                Assert.Fail($"the tap reset the connection ({ex.SocketErrorCode}) after {statuses.Count} of {count} answer(s): [{string.Join(", ", statuses)}]");
                throw;
            }

            if (read == 0)
                Assert.Fail($"the tap closed the connection after {statuses.Count} of {count} answer(s): [{string.Join(", ", statuses)}]");
            pending.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        return [.. statuses];
    }

    /// <summary>
    /// A collector on a raw socket that reads one export, says so, and answers <c>200</c> only when told to: the tap's forward
    /// is held at a known point, before its answer.
    /// </summary>
    private sealed class HeldCollector : IDisposable
    {
        private readonly TcpListener _listener = LoopbackPort.Listening();
        private readonly TaskCompletionSource _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _received;

        public HeldCollector()
        {
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    Socket socket;
                    try
                    {
                        socket = await _listener.AcceptSocketAsync();
                    }
                    catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
                    {
                        return;
                    }

                    _ = Task.Run(() => ServeAsync(socket));
                }
            });
        }

        public int Port { get; }

        public Uri BaseUri => new($"http://127.0.0.1:{Port}");

        public TaskCompletionSource HasExport { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Received => Volatile.Read(ref _received);

        public void Answer() => _answer.TrySetResult();

        private async Task ServeAsync(Socket socket)
        {
            using (socket)
            {
                try
                {
                    var seen = new List<byte>();
                    var buffer = new byte[65536];
                    while (true)
                    {
                        var text = Encoding.ASCII.GetString(seen.ToArray());
                        var headEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (headEnd >= 0)
                        {
                            var length = text[..headEnd].Split("\r\n")
                                .Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                .Select(l => int.Parse(l["Content-Length:".Length..].Trim()))
                                .FirstOrDefault();
                            if (seen.Count >= headEnd + 4 + length)
                                break;
                        }

                        var read = await socket.ReceiveAsync(buffer);
                        if (read == 0)
                            return;
                        seen.AddRange(buffer.AsSpan(0, read).ToArray());
                    }

                    Interlocked.Increment(ref _received);
                    HasExport.TrySetResult();
                    await Task.WhenAny(_answer.Task, _closed.Task);
                    if (_answer.Task.IsCompleted)
                        await socket.SendAsync("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}"u8.ToArray());
                    await _closed.Task;
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    // The tap is gone.
                }
            }
        }

        public void Dispose()
        {
            _closed.TrySetResult();
            _listener.Stop();
        }
    }
}
