using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Kronikol.Extensions.ProxyTap;
using Tap = Kronikol.Extensions.ProxyTap.ProxyTap;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// A caller that hangs up on a tap (#172, plans/PROXY_TAP_HANG_UP_PLAN.md R1). On Linux and macOS the managed
/// <see cref="HttpListener"/> keeps a connection until its response is closed or aborted, so a response the tap gives
/// up on without either holds its socket until the tap is disposed; http.sys (Windows) drops such a request itself. A
/// reset here is an RST alone unless a case says otherwise: <see cref="TcpClient.Dispose()"/> with a zero linger shuts
/// the socket down first, a FIN then an RST, and Linux reads that FIN as an end of stream.
/// </summary>
public class ProxyTapHangUpTests
{
    private const string HttpSysDropsIt =
        "http.sys (Windows) drops a request whose connection died by itself, so no connection can be held there; the " +
        "managed listener (Linux, macOS) holds it, and Linux CI runs this fact (plans/PROXY_TAP_HANG_UP_PLAN.md section 2.2)";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static (int, byte[], string?, string?, Dictionary<string, string>?) Ok(HttpListenerRequest _, byte[] __) =>
        (200, "ok"u8.ToArray(), "text/plain", null, null);

    [Fact]
    public async Task A_caller_that_resets_while_the_answer_is_written_leaves_no_connection_held()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), HttpSysDropsIt);
        var big = new byte[8 * 1024 * 1024];
        await using var upstream = new StubUpstream((_, _) => (200, big, "application/octet-stream", null, null));
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, sink, log);

        // A small receive window, so the answer cannot all sit in socket buffers: the tap is still writing it.
        using var caller = await SendAsync(tap, "GET /big HTTP/1.1\r\nHost: localhost\r\n\r\n", receiveBuffer: 16 * 1024);
        Assert.Equal(1, await caller.Client.ReceiveAsync(new byte[1]));
        ResetAlone(caller);

        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the exchange never ended");
        await AssertNothingHeldAsync(tap);
        // The upstream answered: the exchange is handled and recorded, and the caller's leaving is no forward failure.
        Assert.Equal(1, tap.RequestsHandled);
        Assert.Equal(1, tap.RequestsCaptured);
        Assert.Equal(2, sink.Logs.Count);
        Assert.Equal(0, tap.ForwardFailures);
    }

    [Theory]
    [InlineData("an RST alone")]
    [InlineData("a FIN, then an RST")]
    public async Task A_caller_that_resets_mid_body_leaves_no_connection_held_and_is_no_forward_failure(string hangUp)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), HttpSysDropsIt);
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, sink, log);

        using var caller = await SendAsync(tap, "POST /query HTTP/1.1\r\nHost: localhost\r\nContent-Length: 1000000\r\n\r\n{");
        Assert.True(await WaitUntilAsync(() => tap.InFlight == 1), "the tap never took the request");
        if (hangUp == "an RST alone")
            ResetAlone(caller);
        else
            FinThenReset(caller);

        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the exchange never ended");
        await AssertNothingHeldAsync(tap);
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Empty(Errors(log));
        Assert.Empty(upstream.Seen);
        Assert.Equal(0, tap.RequestsHandled);
        Assert.Empty(sink.Logs);
    }

    [Fact]
    public async Task A_caller_that_half_closes_mid_body_is_answered_400_and_let_go()
    {
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, sink, log);

        using var caller = await SendAsync(tap, "POST /query HTTP/1.1\r\nHost: localhost\r\nContent-Length: 1000000\r\n\r\n{");
        Assert.True(await WaitUntilAsync(() => tap.InFlight == 1), "the tap never took the request");
        caller.Client.Shutdown(SocketShutdown.Send); // a FIN: the caller stops sending and keeps listening

        // On Windows http.sys answers it itself; on the managed listener only the tap can.
        var (firstLine, letGo) = await HearAsync(caller);
        Assert.StartsWith("HTTP/1.1 400 ", firstLine);
        Assert.True(letGo, "the caller was answered, and its connection was then kept open");
        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the exchange never ended");
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Empty(Errors(log));
        Assert.Empty(upstream.Seen);
        if (!OperatingSystem.IsWindows())
            await AssertNothingHeldAsync(tap);
    }

    [Fact]
    public async Task An_upstream_that_fails_after_its_caller_hung_up_is_a_forward_failure_and_leaves_no_connection_held()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), HttpSysDropsIt);
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var upstream = new RawUpstream(async socket =>
        {
            await RawUpstream.ReadHeadAsync(socket);
            holding.TrySetResult();
            await release.Task;
            RawUpstream.Reset(socket); // the upstream fails the exchange: no answer, a reset
        });
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, sink, log);

        using var caller = await SendAsync(tap, "GET /hold HTTP/1.1\r\nHost: localhost\r\n\r\n");
        await holding.Task.WaitAsync(Patience);
        ResetAlone(caller);
        await Task.Delay(300); // the caller is gone before the upstream fails
        release.TrySetResult();

        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the exchange never ended");
        await AssertNothingHeldAsync(tap);
        // The forward did fail, so it is still counted, caller or no caller.
        Assert.Equal(1, tap.ForwardFailures);
        Assert.Single(Errors(log));
    }

    public static TheoryData<string> ReadFailures => new()
    {
        "IOException", "HttpListenerException", "ObjectDisposedException", "InvalidOperationException",
    };

    [Theory]
    [MemberData(nameof(ReadFailures))]
    public async Task A_body_read_that_throws_is_the_callers_failure_whatever_the_exception(string thrown)
    {
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, sink, log, t =>
            t.WrapRequestBody = body => new FailingReadStream(body, () => thrown switch
            {
                "IOException" => new IOException("the read failed"),
                "HttpListenerException" => new HttpListenerException(995),
                "ObjectDisposedException" => new ObjectDisposedException("the request body"),
                _ => new InvalidOperationException("the read failed"),
            }));

        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var hearing = new CancellationTokenSource(Patience);
        HttpStatusCode? status = null;
        try
        {
            using var response = await client.PostAsync(new Uri(tap.ListenUri, "/query"), new StringContent("{}"), hearing.Token);
            status = response.StatusCode;
        }
        catch (TaskCanceledException) when (hearing.IsCancellationRequested)
        {
            Assert.Fail($"the caller heard nothing within {Patience.TotalSeconds:0} s");
        }
        catch (HttpRequestException) when (OperatingSystem.IsWindows())
        {
            // http.sys sends nothing on Abort(): the caller's connection is reset.
        }

        if (OperatingSystem.IsWindows())
            Assert.True(status is null or HttpStatusCode.BadRequest, $"the caller was answered {status}");
        else
            Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the exchange never ended");
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Empty(Errors(log));
        Assert.Empty(upstream.Seen);
    }

    [Theory]
    [InlineData("after Close()")]
    [InlineData("after Abort()")]
    [InlineData("twice")]
    [InlineData("after the listener closed")]
    [InlineData("after a failed write")]
    public async Task Ending_an_unclosed_response_never_throws(string when)
    {
        var port = StubUpstream.FreePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        try
        {
            using var caller = new TcpClient();
            await caller.ConnectAsync("localhost", port);
            await caller.Client.SendAsync(Encoding.ASCII.GetBytes($"GET /x HTTP/1.1\r\nHost: localhost:{port}\r\n\r\n"));
            var context = await listener.GetContextAsync().WaitAsync(Patience);
            var answer = new Tap.CallerAnswer(context.Response);

            switch (when)
            {
                case "after Close()":
                    context.Response.Close();
                    break;
                case "after Abort()":
                    context.Response.Abort();
                    break;
                case "twice":
                    answer.EndUnclosed();
                    break;
                case "after the listener closed":
                    listener.Close();
                    break;
                case "after a failed write":
                    ResetAlone(caller);
                    await Task.Delay(200);
                    try
                    {
                        context.Response.ContentLength64 = 1 << 20;
                        await context.Response.OutputStream.WriteAsync(new byte[1 << 20]);
                    }
                    catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
                    {
                        // The caller is gone.
                    }

                    break;
            }

            Assert.Null(Record.Exception(answer.EndUnclosed));
        }
        finally
        {
            try { listener.Close(); } catch (ObjectDisposedException) { }
        }
    }

    [Fact]
    public async Task An_upstream_body_cut_short_stays_a_forward_failure()
    {
        using var upstream = new RawUpstream(async socket =>
        {
            await RawUpstream.ReadHeadAsync(socket);
            await socket.SendAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\n" + new string('x', 50)));
            await Task.Delay(100);
            RawUpstream.Reset(socket);
        });
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, sink, log);

        using var client = new HttpClient();
        using var response = await client.GetAsync(new Uri(tap.ListenUri, "/cut"));

        // An IOException inside a failed forward is the upstream's, not a caller hanging up.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, tap.ForwardFailures);
        Assert.Single(Errors(log));
    }

    [Fact]
    public async Task Keep_alive_requests_are_answered_on_one_connection()
    {
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, sink, log);

        var connections = 0;
        using var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 1,
            ConnectCallback = async (context, ct) =>
            {
                Interlocked.Increment(ref connections);
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception)
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        using var client = new HttpClient(handler);
        for (var i = 0; i < 20; i++)
        {
            using var response = await client.GetAsync(new Uri(tap.ListenUri, $"/again/{i}"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        }

        Assert.Equal(1, connections);
        Assert.True(await WaitUntilAsync(() => tap.RequestsHandled == 20), $"{tap.RequestsHandled} of 20 handled");
        Assert.Equal(0, tap.ForwardFailures);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<Tap> StartAsync(Uri forward, ListSink sink, List<string> log, Action<Tap>? configure = null)
    {
        var tap = new Tap(new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = forward,
            CallerName = "web",
            ServiceName = "api",
            Sink = sink,
            CaptureUnattributedRequests = true,
            FallbackTestId = "hang-up",
            Log = line => { lock (log) log.Add(line); },
        });
        configure?.Invoke(tap);
        await tap.StartAsync();
        return tap;
    }

    private static async Task<TcpClient> SendAsync(Tap tap, string request, int? receiveBuffer = null)
    {
        var caller = new TcpClient();
        if (receiveBuffer is { } size)
            caller.ReceiveBufferSize = size;
        await caller.ConnectAsync("localhost", tap.Options.ListenPort);
        await caller.Client.SendAsync(Encoding.ASCII.GetBytes(request));
        return caller;
    }

    /// <summary>An RST and nothing else: a zero linger, then the socket's own <c>Dispose()</c>.</summary>
    private static void ResetAlone(TcpClient caller)
    {
        caller.Client.LingerState = new LingerOption(true, 0);
        caller.Client.Dispose();
    }

    /// <summary>The issue's repro: <see cref="TcpClient.Dispose()"/> with a zero linger shuts its stream down first, so a FIN, then an RST.</summary>
    private static void FinThenReset(TcpClient caller)
    {
        caller.Client.LingerState = new LingerOption(true, 0);
        _ = caller.GetStream();
        caller.Dispose();
    }

    /// <summary>The first line the caller hears, and whether the tap then let it go (an end of stream or a reset), within <see cref="Patience"/>.</summary>
    private static async Task<(string FirstLine, bool LetGo)> HearAsync(TcpClient caller)
    {
        var heard = new List<byte>();
        var buffer = new byte[4096];
        using var hearing = new CancellationTokenSource(Patience);
        try
        {
            while (true)
            {
                var read = await caller.Client.ReceiveAsync(buffer, SocketFlags.None, hearing.Token);
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

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                return false;
            await Task.Delay(20);
        }

        return true;
    }

    private static string[] Errors(List<string> log)
    {
        lock (log)
            return log.Where(line => line.Contains("proxy-tap error", StringComparison.Ordinal)).ToArray();
    }

    private static async Task AssertNothingHeldAsync(Tap tap)
    {
        var released = await WaitUntilAsync(() => Held(tap) == (0, 0));
        var (contexts, connections) = Held(tap);
        Assert.True(released,
            $"the listener still holds {contexts} context(s) and {connections} connection(s) {Patience.TotalSeconds:0} s after the exchange ended");
    }

    /// <summary>
    /// What the tap's managed listener still holds: its dispatched contexts and its connections, two private tables read
    /// by reflection (the names plans/PROXY_TAP_HANG_UP_PLAN.harness/probe/probe.cs lists).
    /// </summary>
    private static (int Contexts, int Connections) Held(Tap tap)
    {
        var listener = (HttpListener)Field(typeof(Tap), "_listener").GetValue(tap)!;
        return (Count(listener, "_listenerContexts"), Count(listener, "_connections"));
    }

    private static int Count(HttpListener listener, string name)
    {
        var table = (ICollection)Field(typeof(HttpListener), name).GetValue(listener)!;
        lock (table.SyncRoot)
            return table.Count;
    }

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"{type.FullName} has no field {name} on .NET {Environment.Version}, so this fact cannot see what the listener " +
            "holds; find the table that replaced it (plans/PROXY_TAP_HANG_UP_PLAN.harness/probe/probe.cs lists every collection field)");

    /// <summary>
    /// A request body whose read fails with the exception given, once it has taken every byte the caller sent, so the
    /// listener has nothing left unread when it closes the connection.
    /// </summary>
    private sealed class FailingReadStream(Stream body, Func<Exception> failure) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            body.CopyTo(Null);
            throw failure();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await body.CopyToAsync(Null, cancellationToken);
            throw failure();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>An upstream on a raw socket, for answers an <see cref="HttpListener"/> cannot give: its <c>Abort()</c> writes a whole <c>200</c> when nothing was sent.</summary>
    private sealed class RawUpstream : IDisposable
    {
        private readonly TcpListener _listener = LoopbackPort.Listening();

        public RawUpstream(Func<Socket, Task> serve)
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

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await serve(socket);
                        }
                        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                        {
                            // The tap is gone.
                        }
                        finally
                        {
                            socket.Dispose();
                        }
                    });
                }
            });
        }

        public int Port { get; }

        public Uri BaseUri => new($"http://127.0.0.1:{Port}");

        public static async Task ReadHeadAsync(Socket socket)
        {
            var seen = new StringBuilder();
            var buffer = new byte[8192];
            while (!seen.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await socket.ReceiveAsync(buffer);
                if (read == 0)
                    return;
                seen.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
        }

        public static void Reset(Socket socket)
        {
            socket.LingerState = new LingerOption(true, 0);
            socket.Dispose();
        }

        public void Dispose() => _listener.Stop();
    }
}
