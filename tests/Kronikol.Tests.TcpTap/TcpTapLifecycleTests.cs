using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.TcpTap;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// A tap starts once and stops once. Nothing guarded either: a second start bound a second set of listeners and left the
/// first serving after the stop, a start after the stop served and could not be stopped, a start racing a stop could leave
/// a disposed tap answering, and the token given to <c>StartAsync</c> was the tap's lifetime, so cancelling it after the
/// start ended the accept loops and left the listeners open, with callers connected and never served (the #174 audit).
/// </summary>
public class TcpTapLifecycleTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private static RedisTap Tap(StubServer server) => new(new RedisTapOptions
    {
        ListenPort = 0,
        ListenHost = "127.0.0.1",
        ForwardHost = "127.0.0.1",
        ForwardPort = server.Port,
        CallerName = "svc",
        ServiceName = "redis",
        Sink = new RecordingSink(),
        EmitActivities = false,
    });

    private static StubServer Pong() => new(_ => Encoding.ASCII.GetBytes(Resp.Simple("PONG")));

    /// <summary>What a client that dials 127.0.0.1:<paramref name="port"/> hears in answer to a PING, or why it heard nothing.</summary>
    private static async Task<string> PingAsync(int port)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
        }
        catch (SocketException ex)
        {
            return $"no connection ({ex.SocketErrorCode})";
        }

        var stream = client.GetStream();
        var heard = new StringBuilder();
        var buffer = new byte[256];
        using var patience = new CancellationTokenSource(Patience);
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Resp.Command("PING")), patience.Token);
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
            // Nothing more came.
        }

        return heard.Length == 0 ? "nothing" : heard.ToString();
    }

    [Fact]
    public async Task ASecondStartIsRefusedAndTheFirstKeepsServing()
    {
        await using var server = Pong();
        await using var tap = Tap(server);
        await tap.StartAsync();
        var port = tap.BoundPort;

        var again = await Assert.ThrowsAsync<InvalidOperationException>(() => tap.StartAsync());

        Assert.Contains("already been started", again.Message, StringComparison.Ordinal);
        Assert.Equal(port, tap.BoundPort);
        Assert.Equal("+PONG\r\n", await PingAsync(port));
        await tap.DisposeAsync();
        Assert.StartsWith("no connection", await PingAsync(port));
        Assert.Equal(0, tap.AcceptFailures);
    }

    [Fact]
    public async Task AStartAfterTheStopIsRefusedAndBindsNothing()
    {
        await using var server = Pong();
        var tap = Tap(server);
        await tap.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tap.StartAsync());

        Assert.False(tap.IsListening);
        Assert.Equal(0, tap.BoundPort);
    }

    [Fact]
    public async Task CancellingTheStartsTokenAfterTheStartLeavesTheTapServing()
    {
        await using var server = Pong();
        await using var tap = Tap(server);
        using var start = new CancellationTokenSource();
        await tap.StartAsync(start.Token);

        await start.CancelAsync();

        Assert.True(tap.IsListening);
        Assert.Equal("+PONG\r\n", await PingAsync(tap.BoundPort));
        Assert.Equal("+PONG\r\n", await PingAsync(tap.BoundPort));
    }

    [Fact]
    public async Task AStartWhoseTokenIsAlreadyCancelledBindsNothing()
    {
        await using var server = Pong();
        await using var tap = Tap(server);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tap.StartAsync(new CancellationToken(canceled: true)));

        Assert.False(tap.IsListening);
        Assert.Equal(0, tap.BoundPort);
    }

    [Fact]
    public async Task AStartThatFailsCanBeTriedAgain()
    {
        await using var server = Pong();
        using var holder = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        holder.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        holder.Listen();
        var taken = ((IPEndPoint)holder.LocalEndPoint!).Port;
        await using var tap = new RedisTap(new RedisTapOptions
        {
            ListenPort = taken,
            ListenHost = "127.0.0.1",
            ForwardHost = "127.0.0.1",
            ForwardPort = server.Port,
            CallerName = "svc",
            ServiceName = "redis",
            Sink = new RecordingSink(),
            EmitActivities = false,
        });

        await Assert.ThrowsAsync<SocketException>(() => tap.StartAsync());
        Assert.False(tap.IsListening);

        holder.Dispose();
        await tap.StartAsync();
        Assert.Equal(taken, tap.BoundPort);
        Assert.Equal("+PONG\r\n", await PingAsync(taken));
    }

    /// <summary>
    /// A start and a stop that race leave nothing listening: the stop waits for a start under way and stops what it bound,
    /// and a start after the stop binds nothing. Unguarded, most such taps were still answering after both had finished.
    /// </summary>
    [Fact]
    public async Task AStartRacingTheStopLeavesNothingListening()
    {
        await using var server = Pong();
        for (var round = 0; round < 200; round++)
        {
            var tap = Tap(server);
            // Each listener the tap makes, so the fact can ask whether any is still bound once both are done. Dialling the
            // port instead would take about 2 s a refusal on Windows, and the port is free for another test to take.
            var made = new List<TcpListener>();
            tap.CreateListener = (address, port) =>
            {
                var listener = new TcpListener(address, port);
                lock (made) made.Add(listener);
                return listener;
            };
            using var go = new ManualResetEventSlim();
            var start = Task.Run(() =>
            {
                go.Wait();
                return tap.StartAsync();
            });
            var stop = Task.Run(async () =>
            {
                go.Wait();
                await tap.DisposeAsync();
            });
            go.Set();
            try
            {
                await start;
            }
            catch (ObjectDisposedException)
            {
                // The stop came first.
            }

            await stop;
            Assert.False(tap.IsListening, $"round {round}: the tap says it is listening after its stop");
            lock (made)
                Assert.All(made, listener => Assert.False(listener.Server.IsBound, $"round {round}: a listener is still bound after the stop"));
        }
    }
}
