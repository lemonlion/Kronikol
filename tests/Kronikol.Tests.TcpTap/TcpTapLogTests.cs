using System.Net;
using System.Net.Sockets;
using Kronikol.Extensions.TcpTap;
using Kronikol.Extensions.TcpTap.Protocols;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// A <see cref="TcpTapOptions.Log"/> that throws changes nothing the tap does (plans/TAP_CALLER_TIME_LIMITS_PLAN.md §4.6
/// items 6 and 7). One bound to xUnit's test output throws once its test has ended. Unguarded, the throw from a decoder's
/// failure path disabled decoding and counted the error twice, one from a decoder's own line or the identity resolver's
/// became a decoder error, one in the release of a pair kept its sockets open and the pair counted for ever, one in the
/// reaper counted a reap and closed nothing, and one when the decode queue filled stopped forwarding mid-stream.
/// </summary>
public class TcpTapLogTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    // Each case, and the words of the one line whose writing throws in it: only that site is under test.
    public static TheoryData<string, string> Lines => new()
    {
        { "a decoder whose Dispose throws", "decoder dispose failed" },
        { "a decoder reset", "decoder reset on connection" },
        { "a decoder reset that throws", "decoder reset threw" },
        { "decoding disabled", "decoder gave up" },
        { "a connection closed mid-message", "closed mid-message" },
        { "an identity resolver that throws", "identity resolver threw" },
        { "a decoder's own line", "a line of the decoder's own" },
        { "a reap", "reaped:" },
        { "a full decode queue", "decode queue full" },
    };

    [Theory]
    [MemberData(nameof(Lines))]
    public async Task A_throwing_log_callback_changes_nothing(string line, string throwsOn)
    {
        var written = new List<string>();
        using var gate = new ManualResetEventSlim();
        await using var server = new StubServer(chunk => line == "a reap" ? null : chunk); // an echo, or silence
        var sink = new RecordingSink();
        var options = new TcpTapOptions
        {
            ListenPort = 0,
            ListenHost = "127.0.0.1",
            ForwardHost = "127.0.0.1",
            ForwardPort = server.Port,
            CallerName = "svc",
            ServiceName = "store",
            Sink = sink,
            EmitActivities = false,
            DecoderFactory = context => new ScriptedDecoder(context, line, gate),
            Log = text =>
            {
                lock (written) written.Add(text);
                if (text.Contains(throwsOn, StringComparison.Ordinal))
                    throw new InvalidOperationException("There is no currently active test.");
            },
        };
        switch (line)
        {
            case "an identity resolver that throws":
                options.IdentityResolver = () => throw new InvalidOperationException("the host's resolver failed");
                break;
            case "a reap":
                options.ReapStuckConnectionsAfter = TimeSpan.FromSeconds(1);
                break;
            case "a full decode queue":
                options.ChannelCapacity = 1;
                break;
        }

        await using var tap = new TcpTapCore(options);
        await tap.StartAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, tap.BoundPort);
        var stream = client.GetStream();

        switch (line)
        {
            case "a reap":
            {
                await stream.WriteAsync("command\n"u8.ToArray());
                // The reaper closes the pair: the client hears its connection end.
                Assert.Equal("an end of stream or a reset", await EndOfAsync(stream));
                Assert.Equal(1, tap.StuckConnectionsReaped);
                break;
            }
            case "a full decode queue":
            {
                // The decoder holds the first segment, the queue holds the second, and the rest are dropped: every byte is
                // still forwarded.
                var sent = 0;
                for (var i = 0; i < 6; i++)
                {
                    var segment = new byte[1000];
                    Array.Fill(segment, (byte)('a' + i));
                    await stream.WriteAsync(segment);
                    sent += segment.Length;
                    await Task.Delay(100); // one read, one segment
                }

                Assert.True(await Wait.UntilAsync(() => server.Received.Length == sent, (int)Patience.TotalMilliseconds),
                    $"{server.Received.Length:N0} of {sent:N0} bytes reached the server");
                Assert.True(tap.SegmentsDropped > 0);
                gate.Set();
                break;
            }
            default:
            {
                await stream.WriteAsync("command\n"u8.ToArray());
                var echo = new byte[8];
                await stream.ReadExactlyAsync(echo).AsTask().WaitAsync(Patience);
                break;
            }
        }

        client.Client.Shutdown(SocketShutdown.Send);
        Assert.True(await Wait.UntilAsync(() => tap.LiveConnections == 0, (int)Patience.TotalMilliseconds),
            "the pair was never released");

        switch (line)
        {
            case "a decoder reset":
                Assert.Equal(1, tap.DecodeErrors);
                Assert.Equal(1, tap.DecoderResets);
                Assert.Equal(0, tap.DecodingDisabledConnections);
                break;
            case "a decoder reset that throws":
            case "decoding disabled":
                Assert.Equal(1, tap.DecodeErrors);
                Assert.Equal(1, tap.DecodingDisabledConnections);
                break;
            case "a connection closed mid-message":
                Assert.True(await Wait.UntilAsync(() => tap.ConnectionsClosedMidMessage == 1, (int)Patience.TotalMilliseconds));
                Assert.Equal(0, tap.DecodeErrors);
                Assert.Equal(0, tap.DecodingDisabledConnections);
                break;
            case "an identity resolver that throws":
                Assert.True(await Wait.UntilAsync(() => sink.Logs.Count == 2, (int)Patience.TotalMilliseconds), "the exchange was not recorded");
                Assert.All(sink.Logs, log => Assert.Equal(options.FallbackTestName, log.TestName));
                Assert.Equal(0, tap.DecodeErrors);
                break;
            case "a decoder's own line":
            case "a decoder whose Dispose throws":
                Assert.Equal(0, tap.DecodeErrors);
                Assert.Equal(0, tap.DecodingDisabledConnections);
                break;
        }

        lock (written)
            Assert.Contains(written, text => text.Contains(throwsOn, StringComparison.Ordinal)); // the site was reached
    }

    [Fact]
    public async Task A_negative_cap_is_refused()
    {
        await using var tap = new TcpTapCore(new TcpTapOptions
        {
            ListenPort = 0,
            ListenHost = "127.0.0.1",
            ForwardHost = "127.0.0.1",
            ForwardPort = 6379,
            CallerName = "svc",
            ServiceName = "store",
            BodyCapBytes = -1,
            DecoderFactory = context => new ScriptedDecoder(context, "", null),
        });

        var refused = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => tap.StartAsync());

        Assert.Equal(nameof(TcpTapOptions.BodyCapBytes), refused.ParamName);
    }

    /// <summary>"an end of stream or a reset" once the tap lets the client go, or "nothing" within <see cref="Patience"/>.</summary>
    private static async Task<string> EndOfAsync(NetworkStream stream)
    {
        var buffer = new byte[256];
        using var patience = new CancellationTokenSource(Patience);
        try
        {
            while (await stream.ReadAsync(buffer, patience.Token) > 0)
            {
            }

            return "an end of stream or a reset";
        }
        catch (OperationCanceledException)
        {
            return "nothing";
        }
        catch (IOException)
        {
            return "an end of stream or a reset";
        }
    }

    /// <summary>A decoder that takes the path each case names, through the tap's own context.</summary>
    private sealed class ScriptedDecoder(TcpTapConnectionContext context, string line, ManualResetEventSlim? gate) : IProtocolDecoder
    {
        private int _segments;

        public void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
        {
            if (Interlocked.Increment(ref _segments) > 1)
                return;
            switch (line)
            {
                case "a decoder reset":
                case "a decoder reset that throws":
                    throw new TapProtocolException("the decoder lost its place", recoverable: true);
                case "decoding disabled":
                    throw new TapProtocolException("these bytes are not the protocol");
                case "an identity resolver that throws":
                    context.Record(new TapInteraction("Command", null, new Uri("tcp://store/command"), "command", "reply", "OK",
                        timestamp, timestamp));
                    break;
                case "a decoder's own line":
                    context.Log("a line of the decoder's own");
                    break;
                case "a full decode queue":
                    gate?.Wait(TimeSpan.FromSeconds(30));
                    break;
            }
        }

        public void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
        {
        }

        public void OnConnectionClosed()
        {
            if (line == "a connection closed mid-message")
                context.ReportClosedMidMessage("a command was left unanswered");
        }

        public bool TryReset() => line == "a decoder reset that throws" ? throw new InvalidOperationException("the reset failed") : true;

        public DateTimeOffset? OldestUnansweredSince => line == "a reap" ? DateTimeOffset.UtcNow.AddMinutes(-5) : null;

        public void Dispose()
        {
            if (line == "a decoder whose Dispose throws")
                throw new InvalidOperationException("the decoder could not let go");
        }
    }
}
