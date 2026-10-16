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
        await using var tap = Tap(run, "127.0.0.1", server.Port, o => o.ConnectTimeout = TimeSpan.FromSeconds(30));
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
        // The caller's PING never went anywhere, so the connect was still waiting when the tap stopped.
        Assert.Equal(0, tap.BytesClientToServer);
        Assert.True(await Wait.UntilAsync(() => tap.LiveConnections == 0, 5_000), "the connection was not released");
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

    /// <summary>
    /// A factory that returns no decoder costs the connection its decoding as a throwing one does, and says so. Since
    /// 4.22.0 the tap took a null decoder for a factory that had thrown and been counted, so the connection was forwarded
    /// undecoded with nothing counted, raised or reported; before that, the null failed in the decode loop and was counted.
    /// </summary>
    [Fact]
    public async Task ADecoderFactoryThatReturnsNoDecoderIsCountedAsDecodingDisabled()
    {
        await using var server = new StubServer(_ => Encoding.ASCII.GetBytes(Resp.Simple("PONG")));
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", server.Port, o => o.DecoderFactory = _ => null!);
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap, pingAtOnce: false);
        Assert.Equal("+PONG\r\n", await caller.PingAsync());
        Assert.Equal("+PONG\r\n", await caller.PingAsync());

        Assert.Equal(1, tap.DecodingDisabledConnections);
        Assert.Equal(1, tap.DecodeErrors);
        var raised = Assert.Single(run.Raised(CaptureDegradationKind.DecodingDisabled));
        Assert.Equal(1, raised.ConnectionId);
        Assert.StartsWith("DecoderFactory returned null", raised.Detail);
        Assert.Single(run.Lines("tcp-tap the decoder factory returned no decoder on connection 1 (DecoderFactory returned null)"));
        var entry = Assert.Single(tap.Diagnostics());
        Assert.Contains("decoding disabled on 1 connection(s)", entry.Message);
        Assert.Contains("(DecoderFactory returned null)", entry.Message);
    }

    [Fact]
    public async Task TheStopLineNamesFailedConnectsAndFailedAccepts()
    {
        using var refusing = LoopbackPort.Closed();
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", ((IPEndPoint)refusing.LocalEndPoint!).Port, o => o.ConnectTimeout = TimeSpan.FromSeconds(10));
        await tap.StartAsync();
        using (var caller = await Caller.DialAsync(tap))
            await caller.HearAsync();
        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 1, 15_000), "the failed connect was not counted");

        await tap.DisposeAsync();

        Assert.EndsWith(", 0 failed accepts, 1 failed connects, 0 half-closed pairs closed, 0 stalled writes closed)", Assert.Single(run.Lines("tcp-tap stopped (")));
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
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
        tap.Clock = clock;
        await tap.StartAsync();
        using (var first = await Caller.DialAsync(tap))
            await first.HearAsync();
        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 1, 15_000), "the first failed connect was not counted");
        clock.Advance(TimeSpan.FromMinutes(5));

        // The options are read per connection: the next one fails another way.
        tap.Options.ForwardHost = "no-such-host.invalid";
        using (var second = await Caller.DialAsync(tap))
            await second.HearAsync();
        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 2, 15_000), "the second failed connect was not counted");

        var line = Assert.Single(tap.Diagnostics()).Message;
        Assert.Contains("(2 failed, the last with SocketException HostNotFound: ", line);
        Assert.Contains(", the first at 09:00:00Z;", line, StringComparison.Ordinal);
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

    /// <summary>A tap-wide event (<see cref="CaptureDegradationKind.AcceptFailed"/>) does not print as "connection 0".</summary>
    [Fact]
    public void ATapWideEventPrintsAsTapWide()
    {
        Assert.Equal("[svc→redis] tap-wide: AcceptFailed — accept on 127.0.0.1:6379 failed",
            new CaptureDegradation("svc→redis", 0, CaptureDegradationKind.AcceptFailed, "accept on 127.0.0.1:6379 failed").ToString());
        Assert.Equal("[svc→redis] connection 3: UpstreamConnectFailed — could not connect",
            new CaptureDegradation("svc→redis", 3, CaptureDegradationKind.UpstreamConnectFailed, "could not connect").ToString());
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

    /// <summary>
    /// A <see cref="TcpTapOptions.ConnectTimeout"/> of zero or less, other than <see cref="Timeout.InfiniteTimeSpan"/>, is
    /// refused at start, as <c>ProxyTap</c>'s is. A negative one used to start, and every connection then died where the
    /// deadline is set, before the connect: none was counted, raised or reported, only a "connection N error" log line
    /// each. Zero cut every connect at once.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-2L)]
    [InlineData(-5_000L)]
    public async Task AConnectTimeoutOfZeroOrLessIsRefusedAtStart(long milliseconds)
    {
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", 6379, o => o.ConnectTimeout = TimeSpan.FromMilliseconds(milliseconds));

        var refused = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => tap.StartAsync());

        Assert.Equal(nameof(TcpTapOptions.ConnectTimeout), refused.ParamName);
        Assert.False(tap.IsListening);
    }

    /// <summary>
    /// <see cref="Timeout.InfiniteTimeSpan"/>, and any value of about 24.8 days (<see cref="int.MaxValue"/> ms) or more, mean
    /// no deadline, as for <c>ProxyTap</c>. <see cref="TimeSpan.MaxValue"/> (<c>long.MaxValue</c> here) is what a host writes
    /// for "no limit", and it and anything past the timer's longest (about 49.7 days) used to kill every connection before
    /// its connect, uncounted. A refused connect under each is counted as failed, so the connect really ran.
    /// </summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(2_147_483_647L)]
    [InlineData(4_294_967_295L)]
    [InlineData(long.MaxValue)]
    public async Task AConnectTimeoutOfNoneOrPastTheTimersReachMeansNoDeadline(long milliseconds)
    {
        using var refusing = LoopbackPort.Closed();
        var port = ((IPEndPoint)refusing.LocalEndPoint!).Port;
        var timeout = milliseconds == long.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(milliseconds);
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", port, o => o.ConnectTimeout = timeout);
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap);
        await caller.HearAsync();

        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 1, 15_000), "the refused connect was not counted");
        Assert.Single(run.Raised(CaptureDegradationKind.UpstreamConnectFailed));
        Assert.Empty(run.Lines(" error: "));
    }

    /// <summary>
    /// The upstream's socket is made inside the counted path. With the process out of file descriptors (the case
    /// <see cref="TcpTapCore.AcceptFailures"/> was built for) the accept can take the last one, and making the upstream's socket
    /// then fails; that connection was closed with only a "connection N error" line, counted nowhere.
    /// </summary>
    [Fact]
    public async Task AnUpstreamSocketThatCannotBeMadeIsCountedAsAFailedConnect()
    {
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", 6379);
        tap.CreateUpstream = () => throw new SocketException((int)SocketError.TooManyOpenSockets);
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap);
        await caller.HearAsync();

        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 1, 15_000), "the failed connect was not counted");
        var raised = Assert.Single(run.Raised(CaptureDegradationKind.UpstreamConnectFailed));
        Assert.StartsWith("could not connect to 127.0.0.1:6379 (SocketException TooManyOpenSockets: ", raised.Detail);
        Assert.Empty(run.Lines(" error: "));
        var entry = Assert.Single(tap.Diagnostics());
        Assert.Contains("the last with SocketException TooManyOpenSockets: ", entry.Message);
        Assert.True(await Wait.UntilAsync(() => tap.LiveConnections == 0, 5_000), "the connection was not released");
    }

    /// <summary>
    /// An address in <see cref="TcpTapOptions.ForwardHost"/> is connected to as it is. Since 4.22.1 every host went through
    /// the name lookup, which refuses the unspecified addresses (0.0.0.0, ::) that Linux routes to the loopback.
    /// </summary>
    [Fact]
    public async Task AnAddressIsConnectedToWithNoLookup()
    {
        await using var server = new StubServer(_ => Encoding.ASCII.GetBytes(Resp.Simple("PONG")));
        var run = new Run();
        await using var tap = Tap(run, "127.0.0.1", server.Port);
        tap.ResolveForward = (_, _) => throw new InvalidOperationException("an address was looked up");
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap, pingAtOnce: false);
        Assert.Equal("+PONG\r\n", await caller.PingAsync());
        Assert.Equal(0, tap.UpstreamConnectFailures);
    }

    /// <summary>
    /// Where the system routes the unspecified address to the loopback, as Linux does, a tap forwarding to 0.0.0.0 reaches a
    /// server on 127.0.0.1. Since 4.22.1 the name lookup took the address, refused it as unspecified, and every connection
    /// was counted as a failed connect. Windows refuses to connect to 0.0.0.0, so there the fact cannot apply.
    /// </summary>
    [Fact]
    public async Task AnUnspecifiedForwardAddressReachesTheLoopbackWhereTheSystemRoutesIt()
    {
        await using var server = new StubServer(_ => Encoding.ASCII.GetBytes(Resp.Simple("PONG")));
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            var routed = true;
            try
            {
                await probe.ConnectAsync(new IPEndPoint(IPAddress.Any, server.Port));
            }
            catch (SocketException)
            {
                routed = false;
            }

            Assert.SkipUnless(routed, "this system does not route 0.0.0.0 to the loopback");
        }

        var run = new Run();
        await using var tap = Tap(run, "0.0.0.0", server.Port);
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap, pingAtOnce: false);
        Assert.Equal("+PONG\r\n", await caller.PingAsync());
        Assert.Equal(0, tap.UpstreamConnectFailures);
    }

    /// <summary>
    /// The name's lookup is inside <see cref="TcpTapOptions.ConnectTimeout"/>: a resolver that never answers costs the
    /// connection its deadline, counted as a timeout, never the tap's lifetime.
    /// </summary>
    [Fact]
    public async Task ANameWhoseLookupNeverAnswersRunsOutOfConnectTimeout()
    {
        var run = new Run();
        await using var tap = Tap(run, "kronikol-tap.invalid", 6379, o => o.ConnectTimeout = TimeSpan.FromMilliseconds(500));
        tap.ResolveForward = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        };
        await tap.StartAsync();

        using var caller = await Caller.DialAsync(tap);
        await caller.HearAsync();

        Assert.True(await Wait.UntilAsync(() => tap.UpstreamConnectFailures == 1, 15_000), "the lookup's timeout was not counted");
        var raised = Assert.Single(run.Raised(CaptureDegradationKind.UpstreamConnectFailed));
        Assert.StartsWith("could not connect to kronikol-tap.invalid:6379 within ConnectTimeout (00:00:00.5000000)", raised.Detail);
    }
}
