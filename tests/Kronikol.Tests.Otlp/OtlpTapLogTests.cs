using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Tracking;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// A <see cref="OtlpTapOptions.Log"/> that throws changes nothing the tap does, and the stop line names every counter
/// (plans/TAP_CALLER_TIME_LIMITS_PLAN.md §4.6 items 6, 7 and 10). One bound to xUnit's test output throws once its test
/// has ended; unguarded, the throw came before the answer, so the exporter got a closed connection and nothing else, and
/// one from the map loop ended it for good: no later export was mapped, and <c>DisposeAsync</c> threw.
/// </summary>
public class OtlpTapLogTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly byte[] Export = OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv);

    private static string Head(int contentLength, string target = "/v1/traces") =>
        $"POST {target} HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: {contentLength}\r\n\r\n";

    // Each case, and the words of the one line whose writing throws in it: only that site is under test.
    public static TheoryData<string, string> Lines => new()
    {
        { "an export over MaxRequestBytes", "refused an export over MaxRequestBytes" },
        { "an unauthenticated export", "rejected an unauthenticated" },
        { "a full queue", "dropped an export payload" },
        { "a target with no path", "refused a request target with no path" },
        { "a forward that failed", "forward failed" },
        { "a payload that cannot be mapped", "could not map an export payload" },
    };

    [Theory]
    [MemberData(nameof(Lines))]
    public async Task A_throwing_log_callback_changes_nothing(string line, string throwsOn)
    {
        var written = new List<string>();
        using var refusing = LoopbackPort.Closed();
        using var gate = new ManualResetEventSlim();
        var sink = new GatedSink(line == "a full queue" ? gate : null, throwFirst: line == "a payload that cannot be mapped" ? 1 : 0);
        var options = new OtlpTapOptions
        {
            ListenPort = 0,
            ListenHost = "127.0.0.1",
            Sink = sink,
            Log = text =>
            {
                lock (written) written.Add(text);
                if (text.Contains(throwsOn, StringComparison.Ordinal))
                    throw new InvalidOperationException("There is no currently active test.");
            },
        };
        switch (line)
        {
            case "an export over MaxRequestBytes":
                options.MaxRequestBytes = 16;
                break;
            case "an unauthenticated export":
                options.ExpectedHeaders["Authorization"] = "Bearer secret";
                break;
            case "a full queue":
                options.QueueCapacity = 1;
                break;
            case "a target with no path":
                options.ForwardBaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)refusing.LocalEndPoint!).Port}");
                break;
            case "a forward that failed":
                options.ForwardBaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)refusing.LocalEndPoint!).Port}");
                break;
        }

        var tap = new OtlpTap(options);
        await tap.StartAsync();
        try
        {
            switch (line)
            {
                case "an export over MaxRequestBytes":
                    Assert.Equal(["HTTP/1.1 413 Payload Too Large"], await ExchangeAsync(tap, Head(Export.Length), Export));
                    Assert.Equal(1, tap.PayloadsRejected);
                    break;
                case "an unauthenticated export":
                    Assert.Equal(["HTTP/1.1 401 Unauthorized"], await ExchangeAsync(tap, Head(Export.Length), Export));
                    Assert.Equal(1, tap.UnauthenticatedRequests);
                    break;
                case "a full queue":
                {
                    // The first export holds the mapper in the sink, the second fills the queue, the third is dropped.
                    Assert.Equal(["HTTP/1.1 200 OK"], await ExchangeAsync(tap, Head(Export.Length), Export));
                    await WaitForAsync(() => sink.Entered, "the mapper to reach the sink");
                    Assert.Equal(["HTTP/1.1 200 OK"], await ExchangeAsync(tap, Head(Export.Length), Export));
                    Assert.Equal(["HTTP/1.1 200 OK"], await ExchangeAsync(tap, Head(Export.Length), Export));
                    Assert.Equal(1, tap.PayloadsDropped);
                    gate.Set();
                    break;
                }
                case "a target with no path":
                    Assert.Equal(["HTTP/1.1 400 Bad Request"], await ExchangeAsync(tap, Head(Export.Length, "*"), Export));
                    break;
                case "a forward that failed":
                    Assert.Equal(["HTTP/1.1 502 Bad Gateway"], await ExchangeAsync(tap, Head(Export.Length), Export));
                    Assert.Equal(1, tap.ForwardFailures);
                    break;
                case "a payload that cannot be mapped":
                {
                    // The sink throws at the first payload, as one that cannot write does.
                    Assert.Equal(["HTTP/1.1 200 OK"], await ExchangeAsync(tap, Head(Export.Length), Export));
                    await WaitForAsync(() => tap.PayloadsFailed == 1, "the first payload to fail");
                    Assert.Equal(["HTTP/1.1 200 OK"], await ExchangeAsync(tap, Head(Export.Length), Export));
                    await WaitForAsync(() => tap.SpansMapped == 1, "the next export to be mapped"); // the map loop goes on
                    break;
                }
            }
        }
        finally
        {
            gate.Set();
            await tap.DisposeAsync(); // threw after a throwing map loop
        }

        lock (written)
            Assert.Contains(written, text => text.Contains(throwsOn, StringComparison.Ordinal)); // the site was reached
    }

    [Fact]
    public async Task The_stop_line_names_every_counter()
    {
        var log = new List<string>();
        var tap = new OtlpTap(new OtlpTapOptions
        {
            ListenPort = 0,
            ListenHost = "127.0.0.1",
            Sink = new GatedSink(null, throwFirst: 1),
            MaxRequestBytes = 4096,
            Log = line => { lock (log) log.Add(line); },
        });
        await tap.StartAsync();
        Assert.Equal(["HTTP/1.1 200 OK"], await ExchangeAsync(tap, Head(Export.Length), Export)); // its sink write throws
        Assert.Equal(["HTTP/1.1 413 Payload Too Large"], await ExchangeAsync(tap, Head(8192), new byte[8192]));
        await WaitForAsync(() => tap.PayloadsFailed == 1, "the first payload to fail");

        await tap.DisposeAsync();

        string stop;
        lock (log)
            stop = Assert.Single(log, l => l.Contains("otlp-tap stopped (", StringComparison.Ordinal));
        Assert.Contains(", 1 rejected, 1 failed, ", stop, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_negative_cap_is_refused()
    {
        var tap = new OtlpTap(new OtlpTapOptions { ListenPort = 0, ListenHost = "127.0.0.1", ContentCapBytes = -1 });

        var refused = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => tap.StartAsync());

        Assert.Equal(nameof(OtlpTapOptions.ContentCapBytes), refused.ParamName);
        Assert.False(tap.IsListening);
        await tap.DisposeAsync();
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Sends one request on a connection of its own and returns the status lines heard before the tap's answer is whole.</summary>
    private static async Task<string[]> ExchangeAsync(OtlpTap tap, string head, byte[] body)
    {
        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync([.. Encoding.ASCII.GetBytes(head), .. body]);
        exporter.Client.Client.Shutdown(SocketShutdown.Send); // one request: the tap closes once it has answered
        var heard = await exporter.HearAsync(Patience);
        return heard.StatusLines;
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

    /// <summary>A sink that waits at each write until <c>gate</c> is set, when it has one, and throws at its first <c>throwFirst</c> writes.</summary>
    private sealed class GatedSink(ManualResetEventSlim? gate, int throwFirst = 0) : IRequestResponseSink
    {
        private int _entered;
        private int _writes;

        public bool Entered => Volatile.Read(ref _entered) == 1;

        public void Log(RequestResponseLog log)
        {
            Volatile.Write(ref _entered, 1);
            gate?.Wait(TimeSpan.FromSeconds(30));
            if (Interlocked.Increment(ref _writes) <= throwFirst)
                throw new IOException("the sink cannot write");
        }
    }
}
