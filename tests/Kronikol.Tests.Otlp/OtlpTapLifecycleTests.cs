using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.Otlp;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// A tap starts once and stops once. Nothing guarded either: a start after the stop bound its sockets and served with no
/// way left to stop it, a second start bound a second set of listeners, and the token given to <c>StartAsync</c> was the
/// tap's lifetime, so cancelling it after the start ended the accept loops and left the listeners open, with exporters
/// connected and never served (the #174 audit).
/// </summary>
public class OtlpTapLifecycleTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private static OtlpTap Tap() => new(new OtlpTapOptions
    {
        ListenPort = 0,
        ListenHost = "127.0.0.1",
        Sink = new ListSink(),
    });

    /// <summary>The status line a request to 127.0.0.1:<paramref name="port"/> is answered with, or why there was none.</summary>
    private static async Task<string> AskAsync(int port)
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
        var buffer = new byte[1024];
        using var patience = new CancellationTokenSource(Patience);
        try
        {
            await stream.WriteAsync("GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"u8.ToArray(), patience.Token);
            while (!heard.ToString().Contains("\r\n", StringComparison.Ordinal))
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

        var text = heard.ToString();
        var end = text.IndexOf("\r\n", StringComparison.Ordinal);
        return text.Length == 0 ? "nothing" : end < 0 ? text : text[..end];
    }

    [Fact]
    public async Task A_second_start_is_refused_and_the_first_keeps_serving()
    {
        await using var tap = Tap();
        await tap.StartAsync();
        var port = tap.BoundPort;

        var again = await Assert.ThrowsAsync<InvalidOperationException>(() => tap.StartAsync());

        Assert.Contains("already been started", again.Message, StringComparison.Ordinal);
        Assert.Equal(port, tap.BoundPort);
        Assert.StartsWith("HTTP/1.1 ", await AskAsync(port));
    }

    [Fact]
    public async Task A_start_after_the_stop_is_refused_and_binds_nothing()
    {
        var tap = Tap();
        await tap.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tap.StartAsync());

        Assert.False(tap.IsListening);
        Assert.Equal(0, tap.BoundPort);
    }

    [Fact]
    public async Task Cancelling_the_starts_token_after_the_start_leaves_the_tap_serving()
    {
        await using var tap = Tap();
        using var start = new CancellationTokenSource();
        await tap.StartAsync(start.Token);

        await start.CancelAsync();

        Assert.True(tap.IsListening);
        Assert.StartsWith("HTTP/1.1 ", await AskAsync(tap.BoundPort));
        Assert.StartsWith("HTTP/1.1 ", await AskAsync(tap.BoundPort));
    }

    [Fact]
    public async Task A_start_whose_token_is_already_cancelled_binds_nothing()
    {
        await using var tap = Tap();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tap.StartAsync(new CancellationToken(canceled: true)));

        Assert.False(tap.IsListening);
        Assert.Equal(0, tap.BoundPort);
    }

    /// <summary>
    /// A start and a stop that race leave nothing listening: the stop waits for a start under way and stops what it bound,
    /// and a start after the stop binds nothing. The listeners the tap makes are recorded, so the fact asks whether any is
    /// still bound rather than dialling a port another test may have taken since.
    /// </summary>
    [Fact]
    public async Task A_start_racing_the_stop_leaves_nothing_listening()
    {
        for (var round = 0; round < 200; round++)
        {
            var tap = Tap();
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
