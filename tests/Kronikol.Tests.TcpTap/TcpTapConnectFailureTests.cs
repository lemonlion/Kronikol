using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.TcpTap;
using Kronikol.Reports;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// A connection the tap could not forward is counted and reported (#174, plans/TCPTAP_FAILED_CONNECT_PLAN.md R2). A connect
/// to the real server that timed out was taken for the tap shutting down and left no trace; a refused one was a log line
/// and nothing else. The tap now tells the three endings apart by its own tokens: stopping (its lifetime token), out of
/// time (the connect's deadline) and failed (anything else).
/// </summary>
public class TcpTapConnectFailureTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private sealed class Run
    {
        public List<string> Log { get; } = [];

        public List<CaptureDegradation> Events { get; } = [];

        public string[] Lines(string containing)
        {
            lock (Log)
                return Log.Where(line => line.Contains(containing, StringComparison.Ordinal)).ToArray();
        }

        public CaptureDegradation[] Raised(CaptureDegradationKind kind)
        {
            lock (Events)
                return Events.Where(e => e.Kind == kind).ToArray();
        }
    }

    private static RedisTap Tap(Run run, string forwardHost, int forwardPort, Action<RedisTapOptions>? configure = null)
    {
        var options = new RedisTapOptions
        {
            ListenPort = 0,
            ListenHost = "127.0.0.1",
            ForwardHost = forwardHost,
            ForwardPort = forwardPort,
            CallerName = "svc",
            ServiceName = "redis",
            Sink = new RecordingSink(),
            EmitActivities = false,
            Log = line => { lock (run.Log) run.Log.Add(line); },
            OnCaptureDegraded = e => { lock (run.Events) run.Events.Add(e); },
        };
        configure?.Invoke(options);
        return new RedisTap(options);
    }

    /// <summary>
    /// A client that dials the tap and, unless told otherwise, sends a PING at once, as a service does before the tap has
    /// reached its server; holds the connection.
    /// </summary>
    private sealed class Caller : IDisposable
    {
        private readonly TcpClient _client = new();

        public static async Task<Caller> DialAsync(TcpTapCore tap, bool pingAtOnce = true)
        {
            var caller = new Caller();
            await caller._client.ConnectAsync(IPAddress.Loopback, tap.BoundPort);
            if (pingAtOnce)
                await caller._client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(Resp.Command("PING")));
            return caller;
        }

        /// <summary>What the caller heard before the tap let it go: the reply's text, then "an end of stream" or "a reset", or "nothing" if the tap held it.</summary>
        public async Task<string> HearAsync()
        {
            var heard = new StringBuilder();
            var buffer = new byte[256];
            using var patience = new CancellationTokenSource(Patience);
            try
            {
                while (true)
                {
                    var read = await _client.GetStream().ReadAsync(buffer, patience.Token);
                    if (read == 0)
                        return heard + "an end of stream";
                    heard.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
            }
            catch (OperationCanceledException)
            {
                return heard + "nothing";
            }
            catch (IOException)
            {
                return heard + "a reset";
            }
        }

        public async Task<string> PingAsync()
        {
            var stream = _client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Resp.Command("PING")));
            var heard = new StringBuilder();
            var buffer = new byte[256];
            using var patience = new CancellationTokenSource(Patience);
            try
            {
                while (!heard.ToString().EndsWith("\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, patience.Token);
                    if (read == 0)
                        break;
                    heard.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                // The tap let it go, or held it: the answer so far is what the caller heard.
            }

            return heard.ToString();
        }

        public void Dispose() => _client.Dispose();
    }

    /// <summary>
    /// A listener that never accepts, with its queue full: a further SYN gets no answer on Windows or Linux, so a connect
    /// to it waits until its deadline (harness <c>connect/</c> B1).
    /// </summary>
    private sealed class FullBacklog : IDisposable
    {
        private readonly List<Socket> _held = [];

        private FullBacklog(Socket listener) => _held.Add(listener);

        public int Port => ((IPEndPoint)_held[0].LocalEndPoint!).Port;

        public static async Task<FullBacklog> OpenAsync()
        {
            var listener = LoopbackPort.Hold(
                () =>
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    return socket;
                },
                socket => ((IPEndPoint)socket.LocalEndPoint!).Port,
                socket => socket.Dispose());
            listener.Listen(1);
            var full = new FullBacklog(listener);
            for (var i = 0; i < 64; i++)
            {
                var filler = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                full._held.Add(filler);
                var connect = filler.ConnectAsync(new IPEndPoint(IPAddress.Loopback, full.Port));
                if (await Task.WhenAny(connect, Task.Delay(500)) != connect)
                    return full; // this SYN got no answer: the queue is full
            }

            throw new InvalidOperationException("64 connects to a listener with a backlog of 1 all completed; no full queue to test against.");
        }

        public void Dispose()
        {
            foreach (var socket in _held)
                socket.Dispose();
        }
    }

    [Fact]
    public async Task ARefusedConnectIsCountedLoggedRaisedAndReported()
    {
        using var refusing = LoopbackPort.Closed();
        var port = ((IPEndPoint)refusing.LocalEndPoint!).Port;
        var run = new Run();
        var countedWhenRaised = -1L;
        TcpTapCore? tapSeen = null;
        // Windows answers a refusal after about 2 s per address (it resends the SYN), so the deadline leaves room for it.
        await using var tap = Tap(run, "127.0.0.1", port, o =>
        {
            o.ConnectTimeout = TimeSpan.FromSeconds(10);
            o.OnCaptureDegraded = e =>
            {
                lock (run.Events) run.Events.Add(e);
                // A host that reads the counter as the event arrives sees the event in it, as with every other kind.
                if (e.Kind == CaptureDegradationKind.UpstreamConnectFailed)
                    Interlocked.Exchange(ref countedWhenRaised, tapSeen!.UpstreamConnectFailures);
            };
        });
        tapSeen = tap;
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap);
        var heard = await caller.HearAsync();

        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 1, 15_000), "the failed connect was not counted");
        Assert.NotEqual("nothing", heard);
        var raised = Assert.Single(run.Raised(CaptureDegradationKind.UpstreamConnectFailed));
        Assert.Equal(1, Interlocked.Read(ref countedWhenRaised));
        Assert.Equal(1, raised.ConnectionId);
        Assert.Equal("svc→redis", raised.Tap);
        Assert.StartsWith($"could not connect to 127.0.0.1:{port} (SocketException ConnectionRefused: ", raised.Detail);
        Assert.EndsWith("); closed it without forwarding anything", raised.Detail);

        var line = Assert.Single(run.Lines("could not connect to"));
        Assert.StartsWith("[svc→redis] tcp-tap connection 1 from 127.0.0.1:", line);
        Assert.Contains($": could not connect to 127.0.0.1:{port} (SocketException ConnectionRefused: ", line);
        Assert.Empty(run.Lines(" error: "));

        var entry = Assert.Single(tap.Diagnostics());
        Assert.Equal(DiagnosticKind.CaptureDegraded, entry.Kind);
        Assert.StartsWith($"svc→redis: 1 connection(s) could not be forwarded, because the tap could not connect to 127.0.0.1:{port} (1 failed, the last with SocketException ConnectionRefused: ", entry.Message);
        Assert.EndsWith("Z; the tap closed each one, so svc's commands on them never reached redis", entry.Message);
        Assert.DoesNotContain("ConnectTimeout", entry.Message);
        Assert.True(await Wait.UntilAsync(() => tap.LiveConnections == 0, 15_000));
    }

    [Fact]
    public async Task AConnectThatRunsPastConnectTimeoutIsCountedAsTimedOut()
    {
        using var server = await FullBacklog.OpenAsync();
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", server.Port, o => o.ConnectTimeout = TimeSpan.FromSeconds(1));
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap);
        var heard = await caller.HearAsync();

        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 1, 15_000), "the timed-out connect was not counted");
        Assert.NotEqual("nothing", heard);
        var raised = Assert.Single(run.Raised(CaptureDegradationKind.UpstreamConnectFailed));
        Assert.Equal($"could not connect to 127.0.0.1:{server.Port} within ConnectTimeout (00:00:01); closed it without forwarding anything", raised.Detail);
        Assert.Contains(run.Lines("could not connect to"), l => l.EndsWith($": could not connect to 127.0.0.1:{server.Port} within ConnectTimeout (00:00:01); closed it without forwarding anything", StringComparison.Ordinal));

        var entry = Assert.Single(tap.Diagnostics());
        Assert.StartsWith($"svc→redis: 1 connection(s) could not be forwarded, because the tap could not connect to 127.0.0.1:{server.Port} (1 got no connection within ConnectTimeout 00:00:01), the first at ", entry.Message);
        Assert.DoesNotContain("failed, the last", entry.Message);
    }

    [Fact]
    public async Task AHostNameThatDoesNotResolveIsCountedAsFailed()
    {
        var run = new Run();
        await using var tap = Tap(run, "no-such-host.invalid", 6379, o => o.ConnectTimeout = TimeSpan.FromSeconds(10));
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap);
        await caller.HearAsync();

        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 1, 15_000), "the failed connect was not counted");
        var raised = Assert.Single(run.Raised(CaptureDegradationKind.UpstreamConnectFailed));
        Assert.StartsWith("could not connect to no-such-host.invalid:6379 (SocketException HostNotFound: ", raised.Detail);
        Assert.Contains("(1 failed, the last with SocketException HostNotFound: ", Assert.Single(tap.Diagnostics()).Message);
    }

    [Fact]
    public async Task AConnectCutShortByStoppingTheTapIsNotAFailure()
    {
        using var server = await FullBacklog.OpenAsync();
        var run = new Run();
        var tap = Tap(run, "127.0.0.1", server.Port, o => o.ConnectTimeout = TimeSpan.FromSeconds(30));
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap);
        Assert.True(await Wait.UntilAsync(() => tap.LiveConnections == 1, 15_000));
        await Task.Delay(300); // the connect is waiting on a SYN nobody answers
        await tap.DisposeAsync();

        // The timeout and the tap's own token throw the same OperationCanceledException (harness connect/ G2): only the
        // tokens tell stopping from a timeout.
        Assert.NotEqual("nothing", await caller.HearAsync());
        Assert.Equal(0, tap.UpstreamConnectFailures);
        Assert.Empty(run.Raised(CaptureDegradationKind.UpstreamConnectFailed));
        Assert.Empty(run.Lines("could not connect"));
        Assert.Empty(run.Lines(" error: "));
        Assert.Empty(tap.Diagnostics());
        Assert.Equal(0, tap.LiveConnections);
    }

    [Theory]
    [InlineData(9, 3, "(9 failed, the last with SocketException ConnectionRefused: Connection refused; 3 got no connection within ConnectTimeout 00:00:05)")]
    [InlineData(9, 0, "(9 failed, the last with SocketException ConnectionRefused: Connection refused)")]
    [InlineData(0, 3, "(3 got no connection within ConnectTimeout 00:00:05)")]
    public void TheDiagnosticLineLeavesOutAPartWithNothingInIt(long failed, long timedOut, string causes)
    {
        var line = TcpTapCore.ConnectFailureLine(
            "svc→redis", "localhost:6379", "svc", "redis", failed, timedOut,
            failed == 0 ? null : "SocketException ConnectionRefused: Connection refused",
            TimeSpan.FromSeconds(5), new DateTimeOffset(2026, 10, 15, 14, 3, 35, TimeSpan.Zero));

        Assert.Equal(
            $"svc→redis: {failed + timedOut} connection(s) could not be forwarded, because the tap could not connect to localhost:6379 {causes}, the first at 14:03:35Z; the tap closed each one, so svc's commands on them never reached redis",
            line);
    }

    [Fact]
    public async Task ADecoderFactoryThatThrowsForwardsTheConnectionUndecoded()
    {
        await using var server = new StubServer(_ => Encoding.ASCII.GetBytes(Resp.Simple("PONG")));
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", server.Port, o => o.DecoderFactory = _ => throw new InvalidOperationException("factory failed"));
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap, pingAtOnce: false);
        Assert.Equal("+PONG\r\n", await caller.PingAsync());
        Assert.Equal("+PONG\r\n", await caller.PingAsync());

        Assert.Equal(1, tap.DecodingDisabledConnections);
        Assert.Equal(1, tap.DecodeErrors);
        Assert.Equal(0, tap.UpstreamConnectFailures);
        var raised = Assert.Single(run.Raised(CaptureDegradationKind.DecodingDisabled));
        Assert.StartsWith("InvalidOperationException: factory failed", raised.Detail);
        Assert.Single(run.Lines("tcp-tap the decoder factory threw on connection 1 (InvalidOperationException: factory failed)"));
        Assert.Empty(run.Lines(" error: "));
        var entry = Assert.Single(tap.Diagnostics());
        Assert.Contains("decoding disabled on 1 connection(s)", entry.Message);
        Assert.Contains("(InvalidOperationException: factory failed)", entry.Message);
    }

    [Fact]
    public async Task TheStopLineNamesFailedConnectsAndFailedAccepts()
    {
        using var refusing = LoopbackPort.Closed();
        var run = new Run();
        var tap = Tap(run, "127.0.0.1", ((IPEndPoint)refusing.LocalEndPoint!).Port, o => o.ConnectTimeout = TimeSpan.FromSeconds(10));
        await tap.StartAsync();
        using (var caller = await Caller.DialAsync(tap))
            await caller.HearAsync();
        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 1, 15_000), "the failed connect was not counted");

        await tap.DisposeAsync();

        Assert.EndsWith(", 0 failed accepts, 1 failed connects)", Assert.Single(run.Lines("tcp-tap stopped (")));
    }

    [Fact]
    public async Task CallbacksThatThrowLoseNeitherTheCountNorTheConnection()
    {
        using var refusing = LoopbackPort.Closed();
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", ((IPEndPoint)refusing.LocalEndPoint!).Port, o =>
        {
            o.ConnectTimeout = TimeSpan.FromSeconds(10);
            o.Log = line =>
            {
                lock (run.Log) run.Log.Add(line);
                throw new InvalidOperationException("There is no currently active test.");
            };
            o.OnCaptureDegraded = e =>
            {
                lock (run.Events) run.Events.Add(e);
                throw new InvalidOperationException("the host's dashboard is gone");
            };
        });
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap);
        Assert.NotEqual("nothing", await caller.HearAsync());

        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 1, 15_000), "the failed connect was not counted");
        Assert.True(await Wait.UntilAsync(() => tap.LiveConnections == 0, 15_000), "the connection was never released");
        Assert.Single(run.Raised(CaptureDegradationKind.UpstreamConnectFailed));
        Assert.Single(run.Lines("could not connect to"));
        // Degrade's own line about the throwing callback goes through the guarded writer, so nothing escapes it.
        Assert.Single(run.Lines("OnCaptureDegraded threw"));
        Assert.Empty(run.Lines(" error: "));
    }

    [Fact]
    public async Task TheDiagnosticLineNamesTheLastFailureAndWhenTheFirstHappened()
    {
        using var refusing = LoopbackPort.Closed();
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", ((IPEndPoint)refusing.LocalEndPoint!).Port, o => o.ConnectTimeout = TimeSpan.FromSeconds(10));
        await tap.StartAsync();
        var before = DateTimeOffset.UtcNow;
        using (var first = await Caller.DialAsync(tap))
            await first.HearAsync();
        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 1, 15_000), "the first failed connect was not counted");
        var after = DateTimeOffset.UtcNow;

        // The options are read per connection: the next one fails another way.
        tap.Options.ForwardHost = "no-such-host.invalid";
        using (var second = await Caller.DialAsync(tap))
            await second.HearAsync();
        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 2, 15_000), "the second failed connect was not counted");

        var line = Assert.Single(tap.Diagnostics()).Message;
        Assert.Contains("(2 failed, the last with SocketException HostNotFound: ", line);
        var seconds = Enumerable.Range(0, (int)(after - before).TotalSeconds + 2)
            .Select(i => before.AddSeconds(i).ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(seconds, s => line.Contains($", the first at {s}Z;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedAcceptIsRaisedAsATapWideEventAndCountedInPublic()
    {
        await using var server = new StubServer(_ => Encoding.ASCII.GetBytes(Resp.Simple("PONG")));
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", server.Port);
        var tries = 0;
        tap.Accepting.Accept = (listener, ct) => Interlocked.Increment(ref tries) <= 3
            ? ValueTask.FromException<TcpClient>(new SocketException((int)SocketError.TooManyOpenSockets))
            : listener.AcceptTcpClientAsync(ct);
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap, pingAtOnce: false);
        Assert.Equal("+PONG\r\n", await caller.PingAsync());

        Assert.Equal(3, tap.AcceptFailures);
        var raised = Assert.Single(run.Raised(CaptureDegradationKind.AcceptFailed));
        Assert.Equal(0, raised.ConnectionId);
        Assert.StartsWith($"accept on 127.0.0.1:{tap.BoundPort} failed (SocketException TooManyOpenSockets: ", raised.Detail);
    }

    [Fact]
    public async Task ACaptureDegradedCallbackThatThrowsDoesNotStopTheAcceptLoop()
    {
        await using var server = new StubServer(_ => Encoding.ASCII.GetBytes(Resp.Simple("PONG")));
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", server.Port, o =>
        {
            o.Log = _ => throw new InvalidOperationException("There is no currently active test.");
            o.OnCaptureDegraded = _ => throw new InvalidOperationException("the host's dashboard is gone");
        });
        var tries = 0;
        tap.Accepting.Accept = (listener, ct) => Interlocked.Increment(ref tries) <= 3
            ? ValueTask.FromException<TcpClient>(new SocketException((int)SocketError.TooManyOpenSockets))
            : listener.AcceptTcpClientAsync(ct);
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap, pingAtOnce: false);
        Assert.Equal("+PONG\r\n", await caller.PingAsync());
        Assert.Equal(3, tap.AcceptFailures);
    }
}
