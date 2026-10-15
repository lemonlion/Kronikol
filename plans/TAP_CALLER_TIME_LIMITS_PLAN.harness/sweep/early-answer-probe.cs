#:property PublishAot=false
// early-answer-probe.cs, for plans/TAP_CALLER_TIME_LIMITS_PLAN.md (#173), R0 item 3: does a caller hear an answer that
// the server writes before it has read the request body, when the server then closes the connection?
// Run: `dotnet run early-answer-probe.cs [trials]` (20 trials per cell by default).
//
// The server is a TcpListener on 127.0.0.1, port 0 (the probe keeps its listener). It serves each connection as
// OtlpTap.ServeAsync does at 3240c983 (OtlpTap.cs L277-306 and L459-475, HttpRequestReader.cs L124-150 and L285-290):
// TcpClient.NoDelay, TcpClient.GetStream(), a 16 KiB buffer filled by NetworkStream.ReadAsync(Memory<byte>,
// CancellationToken), the answer written with NetworkStream.WriteAsync and FlushAsync, and the stream and the client
// disposed by `await using` and `using`. Disposing the NetworkStream calls Socket.Shutdown(Both) and then Close. The
// server reads to the end of the head, then:
//   a  writes the 401 and disposes at once;
//   b  writes the 401, calls Socket.Shutdown(Send), and disposes at once;
//   c  writes the 401, calls Socket.Shutdown(Send), then reads and discards until EOF, 2 s, or the declared
//      Content-Length (body bytes read with the head count towards it), whichever comes first, and disposes: a
//      lingering close;
//   d  reads and discards the whole body first, as HttpRequestReader's 413 path does (DiscardAsync over the
//      Content-Length, L96-100; for a chunked body, each chunk and the trailers, as ReadChunkedAsync drains a body over
//      the cap), then writes the 401 and disposes.
// Its answer is always "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".
//
// The callers, each trial on a new connection and bounded at 15 s:
//   http-4K, http-64K, http-1M, http-8M  HttpClient over a new SocketsHttpHandler with its defaults: POST /v1/traces with
//                                        a ByteArrayContent of 4 KiB, 64 KiB, 1 MiB or 8 MiB (application/x-protobuf)
//   http-chunked-1M                      the same with a 1 MiB StreamContent over a stream that cannot seek, sent with
//                                        Transfer-Encoding: chunked
//   raw-1M                               a Socket that writes the head (Content-Length: 1048576) and the 1 MiB body in one
//                                        SendAsync, then reads until EOF or an error; it reads even when the send failed
// A trial's outcome is what the caller saw: the status, or the exception as a chain of types (with each
// HttpRequestException's and HttpIOException's HttpRequestError and each SocketException's SocketErrorCode), and the time
// from the start of the send. An HttpClient failure is marked "sending" or "reading" from its IOException's message
// ("Unable to write data ..." or "Error while copying content ...", against "Unable to read data ..."); SocketsHttpHandler
// sends the whole body before it reads the answer when there is no Expect: 100-continue (HttpConnection.cs L570-598 at
// v10.0.13), so "reading" means its send had completed. The server's half of the line says how much of the body it had
// read with the head when it answered, whether its answer was written, and how its drain ended.
// Before the trials, for context, the probe measures loopback buffering against a peer that accepts and never reads: how
// many bytes 64 KiB sends get accepted before one waits 500 ms, and whether one SendAsync of 8 MiB completes within 1 s.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var trials = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 20;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
var server = new Server(listener);
_ = server.AcceptLoopAsync();

Console.WriteLine($"early-answer-probe on .NET {Environment.Version}, {RuntimeInformation.OSDescription} ({RuntimeInformation.RuntimeIdentifier}), {Environment.ProcessorCount} logical CPUs");
Console.WriteLine($"{trials} trials per cell, each on a new connection; started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
Console.WriteLine($"server: TcpListener on 127.0.0.1:{port}; its answer: {Wire.Escape(Server.Answer)}");
await Context.LoopbackBufferingAsync();

var variants = new (char Id, string What)[]
{
    ('a', "write the 401, then dispose at once (NetworkStream.Dispose: Shutdown(Both), Close)"),
    ('b', "write the 401, Shutdown(Send), then dispose at once"),
    ('c', "write the 401, Shutdown(Send), discard until EOF, 2 s or the Content-Length, then dispose (lingering close)"),
    ('d', "discard the whole body first (the 413 path), then write the 401 and dispose"),
};

var body4K = new byte[4 * 1024];
var body64K = new byte[64 * 1024];
var body1M = new byte[1024 * 1024];
var body8M = new byte[8 * 1024 * 1024];
var callers = new (string Name, Func<Task<Outcome>> Run)[]
{
    ("http-4K", () => Callers.HttpAsync(port, body4K, chunked: false)),
    ("http-64K", () => Callers.HttpAsync(port, body64K, chunked: false)),
    ("http-1M", () => Callers.HttpAsync(port, body1M, chunked: false)),
    ("http-8M", () => Callers.HttpAsync(port, body8M, chunked: false)),
    ("http-chunked-1M", () => Callers.HttpAsync(port, body1M, chunked: true)),
    ("raw-1M", () => Callers.RawAsync(port, body1M)),
};

// One exchange per caller against variant d first, not counted, so the first counted trial does not pay for the JIT.
foreach (var (_, run) in callers)
    await RunTrialAsync('d', run);
Console.WriteLine("warm-up: one exchange per caller against variant d, not counted");

var cells = new List<Cell>();
foreach (var (variant, what) in variants)
{
    Console.WriteLine();
    Console.WriteLine($"== variant {variant}: {what}");
    foreach (var (name, run) in callers)
    {
        var cell = new Cell(variant, name);
        for (var i = 1; i <= trials; i++)
        {
            var (outcome, records) = await RunTrialAsync(variant, run);
            cell.Add(outcome, records);
            Console.WriteLine($"{variant} {name,-15} #{i:00}  {outcome.Text} in {outcome.Ms:0.0} ms  | server: {ServerRecord.Describe(records)}");
        }

        cells.Add(cell);
        Console.WriteLine($"{variant} {name,-15} => 401 {cell.Count("401")}, reset {cell.Count("reset")}, aborted {cell.Count("aborted")}, other {cell.Count("other")}");
    }
}

Console.WriteLine();
Console.WriteLine("== the heads the server read (first counted trial of each caller)");
foreach (var cell in cells.Where(c => c.Variant == 'a'))
    Console.WriteLine($"{cell.Caller,-15}  {cell.SampleHead?.Replace("\r\n", " | ") ?? "(none)"}");

Console.WriteLine();
Console.WriteLine("== summary. 401: the caller got the 401. reset: it did not, and SocketException(ConnectionReset) ended its send or");
Console.WriteLine("   read. aborted: it did not, and SocketException(ConnectionAborted) did. other: anything else. Each cell's");
Console.WriteLine("   outcomes are listed below the table. ms: from the start of the send to the outcome. body read with the");
Console.WriteLine("   head: body bytes the server had read, in the same reads as the head, when it answered.");
Console.WriteLine();
Console.WriteLine($"{"variant",-8} {"caller",-16} {"401",4} {"reset",6} {"aborted",8} {"other",6} {"median ms",10} {"max ms",8}  {"body read with the head (B)",-28} {"connections",11}");
foreach (var cell in cells)
    Console.WriteLine($"{cell.Variant,-8} {cell.Caller,-16} {cell.Count("401"),4} {cell.Count("reset"),6} {cell.Count("aborted"),8} {cell.Count("other"),6} {cell.MedianMs,10:0.0} {cell.MaxMs,8:0.0}  {cell.BodyReadRange,-28} {cell.Connections,11}");

Console.WriteLine();
Console.WriteLine("== outcomes per cell (count, then the outcome; an exception's messages once per outcome; the server's drain in c and d)");
foreach (var cell in cells)
{
    Console.WriteLine($"{cell.Variant} {cell.Caller}");
    foreach (var (text, count, example) in cell.Distinct())
    {
        Console.WriteLine($"  {count,3}  {text}");
        if (example is not null)
            Console.WriteLine($"       messages: {example}");
    }

    foreach (var (drain, count) in cell.Drains())
        Console.WriteLine($"  {count,3}  server drain: {drain}");
}

Console.WriteLine();
Console.WriteLine($"finished {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
listener.Stop();
return;

async Task<(Outcome Outcome, List<ServerRecord> Records)> RunTrialAsync(char variant, Func<Task<Outcome>> run)
{
    var trial = server.Begin(variant);
    var outcome = await run();
    var records = await server.EndAsync(trial, TimeSpan.FromSeconds(10));
    return (outcome, records);
}

static class Wire
{
    public static string Escape(byte[] bytes) => Encoding.ASCII.GetString(bytes).Replace("\r", "\\r").Replace("\n", "\\n");

    public static string Chain(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            parts.Add(e switch
            {
                HttpRequestException h => $"HttpRequestException({h.HttpRequestError})",
                HttpIOException io => $"HttpIOException({io.HttpRequestError})",
                SocketException s => $"SocketException({s.SocketErrorCode})",
                _ => e.GetType().Name,
            });
        }

        return string.Join(" > ", parts);
    }

    public static string Messages(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
            parts.Add(e.Message);
        return string.Join(" | ", parts);
    }

    public static bool IsReset(Exception? ex) => Has(ex, SocketError.ConnectionReset);

    public static bool IsAborted(Exception? ex) => Has(ex, SocketError.ConnectionAborted);

    /// <summary>The outcome's kind: 401 is decided by the caller; this sorts a failure.</summary>
    public static string Kind(params Exception?[] errors) =>
        errors.Any(IsReset) ? "reset" : errors.Any(IsAborted) ? "aborted" : "other";

    /// <summary>"sending" or "reading" for an HttpClient failure, from the IOException's message; empty when neither.</summary>
    public static string Phase(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e.Message.StartsWith("Unable to write data", StringComparison.Ordinal) || e.Message.StartsWith("Error while copying content", StringComparison.Ordinal))
                return "sending";
            if (e.Message.StartsWith("Unable to read data", StringComparison.Ordinal))
                return "reading";
        }

        return "";
    }

    private static bool Has(Exception? ex, SocketError code)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SocketException s && s.SocketErrorCode == code)
                return true;
        }

        return false;
    }
}

sealed record Outcome(string Kind, string Text, double Ms, string? Example);

static class Context
{
    /// <summary>How much a loopback sender gets accepted when the peer accepts and never reads, three fresh connections each way.</summary>
    public static async Task LoopbackBufferingAsync()
    {
        var side = new TcpListener(IPAddress.Loopback, 0);
        side.Start();
        try
        {
            var stepped = new List<string>();
            for (var round = 0; round < 3; round++)
            {
                using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                await client.ConnectAsync(side.LocalEndpoint);
                using var peer = await side.AcceptSocketAsync();
                var chunk = new byte[64 * 1024];
                long total = 0;
                while (total < 64L * 1024 * 1024)
                {
                    using var wait = new CancellationTokenSource(500);
                    try
                    {
                        total += await client.SendAsync(chunk, SocketFlags.None, wait.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                stepped.Add($"{total:N0} B (the peer held {peer.Available:N0})");
            }

            var single = new List<string>();
            for (var round = 0; round < 3; round++)
            {
                using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                await client.ConnectAsync(side.LocalEndpoint);
                using var peer = await side.AcceptSocketAsync();
                var send = client.SendAsync(new byte[8 * 1024 * 1024], SocketFlags.None);
                var completed = await Task.WhenAny(send, Task.Delay(1000)) == send;
                single.Add($"{(completed ? "completed" : "pending after 1 s")} (the peer held {peer.Available:N0})");
            }

            Console.WriteLine("context, loopback buffering with a peer that accepts and never reads (sender and peer at default buffer sizes):");
            Console.WriteLine($"  64 KiB sends accepted before one waited 500 ms: {string.Join("; ", stepped)}");
            Console.WriteLine($"  one SendAsync of 8 MiB: {string.Join("; ", single)}");
        }
        finally
        {
            side.Stop();
        }
    }
}

static class Callers
{
    public static async Task<Outcome> HttpAsync(int port, byte[] body, bool chunked)
    {
        using var client = new HttpClient(new SocketsHttpHandler());
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v1/traces");
        HttpContent content = chunked ? new StreamContent(new UnseekableStream(body)) : new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        request.Content = content;
        if (chunked)
            request.Headers.TransferEncodingChunked = true;

        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start = Stopwatch.GetTimestamp();
        try
        {
            using var response = await client.SendAsync(request, bound.Token);
            var status = (int)response.StatusCode;
            return new Outcome(status == 401 ? "401" : "other", $"status {status}", Stopwatch.GetElapsedTime(start).TotalMilliseconds, null);
        }
        catch (Exception ex)
        {
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var phase = Wire.Phase(ex);
            var text = (phase.Length > 0 ? phase + ": " : "") + Wire.Chain(ex) + (bound.IsCancellationRequested ? " (the probe's 15 s bound)" : "");
            return new Outcome(Wire.Kind(ex), text, ms, Wire.Messages(ex));
        }
    }

    public static async Task<Outcome> RawAsync(int port, byte[] body)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
        var head = Encoding.ASCII.GetBytes($"POST /v1/traces HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nContent-Type: application/x-protobuf\r\nContent-Length: {body.Length}\r\n\r\n");
        var request = new byte[head.Length + body.Length];
        head.CopyTo(request, 0);
        body.CopyTo(request, head.Length);

        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start = Stopwatch.GetTimestamp();
        string send;
        Exception? sendError = null;
        try
        {
            var sent = await socket.SendAsync(request.AsMemory(), SocketFlags.None, bound.Token);
            send = sent == request.Length ? "send ok" : $"send {sent:N0} of {request.Length:N0} B";
        }
        catch (Exception ex)
        {
            sendError = ex;
            send = "send " + Wire.Chain(ex);
        }

        var received = new MemoryStream();
        var buffer = new byte[16 * 1024];
        string end;
        Exception? readError = null;
        try
        {
            while (true)
            {
                var n = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, bound.Token);
                if (n == 0)
                {
                    end = "FIN";
                    break;
                }

                received.Write(buffer, 0, n);
            }
        }
        catch (Exception ex)
        {
            readError = ex;
            end = Wire.Chain(ex) + (bound.IsCancellationRequested ? " (the probe's 15 s bound)" : "");
        }

        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var answer = Encoding.ASCII.GetString(received.ToArray());
        var status = answer.Length >= 12 && answer.StartsWith("HTTP/1.1 ", StringComparison.Ordinal) ? answer.Substring(9, 3) : null;
        var got = received.Length == 0 ? "read nothing" : status is null ? $"read {received.Length} B, no status line" : $"read {status} ({received.Length} B)";
        var kind = status == "401" ? "401" : Wire.Kind(sendError, readError);
        var errors = new[] { sendError, readError }.Where(e => e is not null).Select(e => Wire.Messages(e!)).ToArray();
        return new Outcome(kind, $"{send}; {got}, then {end}", ms, errors.Length == 0 ? null : string.Join(" / ", errors));
    }
}

/// <summary>A stream over a byte array that cannot seek, so StreamContent cannot compute a length.</summary>
sealed class UnseekableStream(byte[] data) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var n = Math.Min(buffer.Length, data.Length - _position);
        data.AsSpan(_position, n).CopyTo(buffer);
        _position += n;
        return n;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(Read(buffer.Span));
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Read(buffer, offset, count));
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

sealed class TrialState(char variant)
{
    public char Variant { get; } = variant;
    public List<ServerRecord> Records { get; } = [];
}

sealed class ServerRecord
{
    public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? Head { get; set; }
    public int BodyReadWithHead { get; set; } = -1;
    public string? Answer { get; set; }
    public string? Shutdown { get; set; }
    public string? DrainKind { get; set; }
    public string? Drain { get; set; }
    public string? Error { get; set; }
    public string? Note { get; set; }

    public string Describe()
    {
        var parts = new List<string>();
        if (BodyReadWithHead >= 0)
            parts.Add($"{BodyReadWithHead:N0} B of body read with the head");
        if (Answer is not null)
            parts.Add(Answer);
        if (Shutdown is not null)
            parts.Add(Shutdown);
        if (Drain is not null)
            parts.Add(Drain);
        if (Error is not null)
            parts.Add("serve threw " + Error);
        if (Note is not null)
            parts.Add(Note);
        return string.Join("; ", parts);
    }

    public static string Describe(List<ServerRecord> records) => records.Count switch
    {
        0 => "no connection",
        1 => records[0].Describe(),
        _ => $"{records.Count} connections: " + string.Join(" || ", records.Select(r => r.Describe())),
    };
}

sealed class Server(TcpListener listener)
{
    public static readonly byte[] Answer = Encoding.ASCII.GetBytes("HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    private TrialState? _current;

    public TrialState Begin(char variant)
    {
        var trial = new TrialState(variant);
        Volatile.Write(ref _current, trial);
        return trial;
    }

    /// <summary>Waits until every connection the trial opened has been served, or the limit passes.</summary>
    public async Task<List<ServerRecord>> EndAsync(TrialState trial, TimeSpan limit)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            ServerRecord[] records;
            lock (trial.Records)
                records = [.. trial.Records];
            var pending = records.Where(r => !r.Done.Task.IsCompleted).Select(r => r.Done.Task).ToArray();
            var remaining = limit - watch.Elapsed;
            if (pending.Length == 0 || remaining <= TimeSpan.Zero)
            {
                foreach (var r in records.Where(r => !r.Done.Task.IsCompleted))
                    r.Note ??= $"still being served after {limit.TotalSeconds:0} s";
                return [.. records];
            }

            await Task.WhenAny(Task.WhenAll(pending), Task.Delay(remaining));
        }
    }

    public async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                return;
            }

            var trial = Volatile.Read(ref _current);
            if (trial is null)
            {
                client.Dispose();
                continue;
            }

            var record = new ServerRecord();
            lock (trial.Records)
                trial.Records.Add(record);
            _ = Task.Run(() => ServeAsync(client, trial.Variant, record));
        }
    }

    private static async Task ServeAsync(TcpClient client, char variant, ServerRecord record)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();
                var reader = new RequestReader(stream);
                var head = await reader.ReadHeadAsync(CancellationToken.None);
                if (head is null)
                {
                    record.Note = "the connection ended inside the head";
                    return;
                }

                record.Head = head;
                var (contentLength, chunked) = Framing(head);
                record.BodyReadWithHead = reader.Buffered;
                switch (variant)
                {
                    case 'a':
                        record.Answer = await WriteAnswerAsync(stream);
                        break;
                    case 'b':
                        record.Answer = await WriteAnswerAsync(stream);
                        record.Shutdown = ShutdownSend(client.Client);
                        break;
                    case 'c':
                        record.Answer = await WriteAnswerAsync(stream);
                        record.Shutdown = ShutdownSend(client.Client);
                        (record.DrainKind, record.Drain) = await reader.LingerAsync(contentLength, TimeSpan.FromSeconds(2));
                        break;
                    case 'd':
                        (record.DrainKind, record.Drain) = await reader.DrainBodyAsync(contentLength, chunked);
                        record.Answer = await WriteAnswerAsync(stream);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            record.Error = Wire.Chain(ex);
        }
        finally
        {
            record.Done.TrySetResult();
        }
    }

    private static async Task<string> WriteAnswerAsync(Stream stream)
    {
        try
        {
            await stream.WriteAsync(Answer, CancellationToken.None);
            await stream.FlushAsync(CancellationToken.None);
            return "answer written";
        }
        catch (Exception ex)
        {
            return "answer not written: " + Wire.Chain(ex);
        }
    }

    private static string? ShutdownSend(Socket socket)
    {
        try
        {
            socket.Shutdown(SocketShutdown.Send);
            return null;
        }
        catch (Exception ex)
        {
            return "Shutdown(Send) threw " + Wire.Chain(ex);
        }
    }

    private static (long? ContentLength, bool Chunked) Framing(string head)
    {
        long? contentLength = null;
        var chunked = false;
        foreach (var line in head.Split("\r\n").Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
                contentLength = length;
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                chunked = true;
        }

        return (contentLength, chunked);
    }
}

/// <summary>HttpRequestReader's buffer, fill, head and drain logic (HttpRequestReader.cs at 3240c983), with discards only.</summary>
sealed class RequestReader(Stream stream)
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxChunkLineBytes = 1024;

    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    /// <summary>Bytes read from the socket and not yet consumed: after the head, the body bytes that came with it.</summary>
    public int Buffered => _end - _start;

    public async Task<string?> ReadHeadAsync(CancellationToken ct)
    {
        var head = new List<byte>(1024);
        var matched = 0;
        while (true)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                return null;

            while (_start < _end)
            {
                var b = _buffer[_start++];
                head.Add(b);
                matched = b switch
                {
                    (byte)'\r' when matched is 0 or 2 => matched + 1,
                    (byte)'\n' when matched is 1 or 3 => matched + 1,
                    _ => 0,
                };

                if (matched == 4)
                    return Encoding.UTF8.GetString(head.ToArray(), 0, head.Count - 4);
                if (head.Count > MaxHeaderBytes)
                    return null;
            }
        }
    }

    /// <summary>Variant c: discard until EOF, the limit, or the declared length, whichever comes first.</summary>
    public async Task<(string Kind, string Text)> LingerAsync(long? contentLength, TimeSpan limit)
    {
        var watch = Stopwatch.StartNew();
        long discarded = Buffered;
        _start = _end;
        using var cts = new CancellationTokenSource(limit);
        try
        {
            while (contentLength is null || discarded < contentLength)
            {
                if (!await FillAsync(cts.Token).ConfigureAwait(false))
                    return ("EOF", $"drain: EOF after {discarded:N0} B in {watch.Elapsed.TotalMilliseconds:0} ms");
                discarded += _end - _start;
                _start = _end;
            }

            return ("Content-Length reached", $"drain: Content-Length reached ({discarded:N0} B) in {watch.Elapsed.TotalMilliseconds:0} ms");
        }
        catch (OperationCanceledException)
        {
            return ($"{limit.TotalSeconds:0} s limit", $"drain: {limit.TotalSeconds:0} s limit after {discarded:N0} B");
        }
        catch (Exception ex)
        {
            var chain = Wire.Chain(ex);
            return (chain, $"drain: {chain} after {discarded:N0} B in {watch.Elapsed.TotalMilliseconds:0} ms");
        }
    }

    /// <summary>Variant d: the 413 path. DiscardAsync over the Content-Length, or each chunk and the trailers.</summary>
    public async Task<(string Kind, string Text)> DrainBodyAsync(long? contentLength, bool chunked)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            if (chunked)
            {
                var (ok, bytes) = await DrainChunkedAsync(CancellationToken.None).ConfigureAwait(false);
                return ok
                    ? ("chunked body drained", $"drain: chunked body drained ({bytes:N0} B of data) in {watch.Elapsed.TotalMilliseconds:0} ms")
                    : ("chunked body cut short", $"drain: chunked body cut short or malformed after {bytes:N0} B");
            }

            if (contentLength is > 0)
            {
                var discarded = await DiscardAsync(contentLength.Value, CancellationToken.None).ConfigureAwait(false);
                return discarded == contentLength
                    ? ("body drained", $"drain: body drained ({discarded:N0} B) in {watch.Elapsed.TotalMilliseconds:0} ms")
                    : ("stream ended in the body", $"drain: stream ended after {discarded:N0} of {contentLength:N0} B");
            }

            return ("no body", "drain: no body");
        }
        catch (Exception ex)
        {
            var chain = Wire.Chain(ex);
            return (chain, $"drain: {chain} in {watch.Elapsed.TotalMilliseconds:0} ms");
        }
    }

    private async Task<long> DiscardAsync(long count, CancellationToken ct)
    {
        var remaining = count;
        while (remaining > 0)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                break;
            var available = (int)Math.Min(_end - _start, remaining);
            _start += available;
            remaining -= available;
        }

        return count - remaining;
    }

    private async Task<(bool Ok, long Bytes)> DrainChunkedAsync(CancellationToken ct)
    {
        long total = 0;
        while (true)
        {
            var sizeLine = await ReadLineAsync(MaxChunkLineBytes, ct).ConfigureAwait(false);
            if (sizeLine is null)
                return (false, total);
            var semicolon = sizeLine.IndexOf(';');
            if (!long.TryParse((semicolon < 0 ? sizeLine : sizeLine[..semicolon]).Trim(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var size) || size < 0)
                return (false, total);
            if (size == 0)
                break;
            var discarded = await DiscardAsync(size, ct).ConfigureAwait(false);
            total += discarded;
            if (await ReadLineAsync(MaxChunkLineBytes, ct).ConfigureAwait(false) is not "")
                return (false, total);
        }

        var trailerBytes = 0;
        while (true)
        {
            var trailer = await ReadLineAsync(MaxHeaderBytes, ct).ConfigureAwait(false);
            if (trailer is null)
                return (false, total);
            if (trailer.Length == 0)
                break;
            trailerBytes += trailer.Length + 2;
            if (trailerBytes > MaxHeaderBytes)
                return (false, total);
        }

        return (true, total);
    }

    private async Task<string?> ReadLineAsync(int maxBytes, CancellationToken ct)
    {
        var line = new List<byte>(16);
        while (true)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                return line.Count == 0 ? null : Encoding.ASCII.GetString(line.ToArray());
            var b = _buffer[_start++];
            if (b == (byte)'\n')
                return Encoding.ASCII.GetString(line.ToArray()).TrimEnd('\r');
            line.Add(b);
            if (line.Count > maxBytes)
                return null;
        }
    }

    private async Task<bool> FillAsync(CancellationToken ct)
    {
        _start = 0;
        _end = await stream.ReadAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);
        return _end > 0;
    }
}

sealed class Cell(char variant, string caller)
{
    private readonly List<Outcome> _outcomes = [];
    private readonly List<List<ServerRecord>> _servers = [];

    public char Variant { get; } = variant;
    public string Caller { get; } = caller;
    public string? SampleHead { get; private set; }

    public void Add(Outcome outcome, List<ServerRecord> records)
    {
        _outcomes.Add(outcome);
        _servers.Add(records);
        SampleHead ??= records.FirstOrDefault(r => r.Head is not null)?.Head;
    }

    public int Count(string kind) => _outcomes.Count(o => o.Kind == kind);

    public double MedianMs
    {
        get
        {
            var sorted = _outcomes.Select(o => o.Ms).Order().ToArray();
            return sorted.Length == 0 ? double.NaN : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        }
    }

    public double MaxMs => _outcomes.Count == 0 ? double.NaN : _outcomes.Max(o => o.Ms);

    public int Connections => _servers.Sum(s => s.Count);

    public string BodyReadRange
    {
        get
        {
            var values = _servers.SelectMany(s => s).Where(r => r.BodyReadWithHead >= 0).Select(r => r.BodyReadWithHead).ToArray();
            if (values.Length == 0)
                return "-";
            var (min, max) = (values.Min(), values.Max());
            return min == max ? $"{min:N0}" : $"{min:N0} to {max:N0}";
        }
    }

    public IEnumerable<(string Text, int Count, string? Example)> Distinct() =>
        _outcomes.GroupBy(o => o.Text).OrderByDescending(g => g.Count()).Select(g => (g.Key, g.Count(), g.First().Example));

    public IEnumerable<(string Drain, int Count)> Drains() =>
        _servers.SelectMany(s => s).Where(r => r.DrainKind is not null).GroupBy(r => r.DrainKind!).OrderByDescending(g => g.Count()).Select(g => (g.Key, g.Count()));
}
