using System.Diagnostics;
using System.Net;
using System.Text;
using Kronikol.Extensions.ProxyTap;
using Tap = Kronikol.Extensions.ProxyTap.ProxyTap;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// What a caller hears when the tap stops (plans/TAP_CALLER_TIME_LIMITS_PLAN.md §4.6 items 2 and 12). Closing the managed
/// <see cref="HttpListener"/> (Linux, macOS) answers <c>200 OK</c> on every connection it still holds, a request the tap
/// never forwarded among them, one a second, and the tap closed its listener before it waited for its handlers, so an
/// answer in hand was cut short and a request that arrived meanwhile was refused. The tap now settles each answer by its
/// phase before the listener closes.
/// </summary>
public class ProxyTapStoppingTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static (int, byte[], string?, string?, Dictionary<string, string>?) Ok(HttpListenerRequest _, byte[] __) =>
        (200, "ok"u8.ToArray(), "text/plain", null, null);

    [Fact]
    public async Task Stopping_the_tap_never_tells_a_caller_still_sending_its_body_that_it_succeeded()
    {
        await using var upstream = new StubUpstream(Ok);
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), log);

        var callers = new List<RawCaller>();
        for (var i = 0; i < 4; i++)
            callers.Add(await RawCaller.SendAsync(tap.Options.ListenPort, "POST /upload HTTP/1.1\r\nHost: localhost\r\nContent-Length: 1000000\r\n\r\n{"));
        Assert.True(await WaitUntilAsync(() => tap.InFlight == 4), $"the tap took {tap.InFlight} of 4 requests");

        var stopping = Stopwatch.StartNew();
        await tap.DisposeAsync();
        stopping.Stop();

        foreach (var caller in callers)
        {
            using (caller)
            {
                var heard = await caller.HearAsync(Patience);
                // http.sys sends nothing on Abort(): a caller there is reset, never told anything.
                if (OperatingSystem.IsWindows() && heard.Bytes.Length == 0)
                    Assert.StartsWith("a reset", heard.Ending);
                else
                    Assert.StartsWith("HTTP/1.1 503 ", heard.FirstLine);
            }
        }

        // Each is ended at once, not after the 2 s drain, and no close waits a second on a body that is not coming.
        Assert.True(stopping.Elapsed < TimeSpan.FromSeconds(1.5), $"DisposeAsync took {stopping.ElapsedMilliseconds:N0} ms");
        Assert.Empty(upstream.Seen);
        Assert.Equal(0, tap.ForwardFailures);
    }

    [Fact]
    public async Task Stopping_the_tap_answers_a_forward_it_cuts_short_with_the_502_its_record_names()
    {
        using var upstream = new HeldUpstream();
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, sink, log);

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /hold HTTP/1.1\r\nHost: localhost\r\n\r\n");
        await upstream.HasRequest.Task.WaitAsync(Patience);
        await tap.DisposeAsync();

        var heard = await caller.HearAsync(Patience);
        Assert.StartsWith("HTTP/1.1 502 ", heard.FirstLine);
        var body = Encoding.UTF8.GetString(heard.Body);
        Assert.Contains("the tap stopped before api answered", body, StringComparison.Ordinal);
        var recorded = Assert.Single(sink.Logs, l => l.Type == Kronikol.Tracking.RequestResponseType.Response);
        Assert.Equal(body, recorded.Content);
        Assert.Equal(0, tap.ForwardFailures);
    }

    [Fact]
    public async Task Stopping_the_tap_lets_an_answer_in_hand_finish()
    {
        var big = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(big);
        await using var upstream = new StubUpstream((_, _) => (200, big, "application/octet-stream", null, null));
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), log);

        // A small receive window, so the answer cannot all sit in socket buffers: the tap is still writing it.
        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /big HTTP/1.1\r\nHost: localhost\r\n\r\n", receiveBuffer: 16 * 1024);
        var first = new byte[1];
        Assert.Equal(1, await caller.Client.Client.ReceiveAsync(first));
        var stopping = tap.DisposeAsync().AsTask();

        var heard = await caller.HearAsync(Patience);
        await stopping.WaitAsync(Patience);

        var whole = new Heard([first[0], .. heard.Bytes], heard.Ending);
        Assert.StartsWith("HTTP/1.1 200 ", whole.FirstLine);
        Assert.Contains($"Content-Length: {big.Length}", whole.Head, StringComparison.OrdinalIgnoreCase);
        // What may follow the whole answer is the managed listener's own: closing it writes an empty 200 OK on each idle
        // kept-alive connection, which answers no request (plans/TAP_CALLER_TIME_LIMITS_PLAN.md §11).
        Assert.True(whole.Body.Length >= big.Length, $"the answer was cut short: {whole.Body.Length:N0} of {big.Length:N0} bytes");
        Assert.True(whole.Body.AsSpan(0, big.Length).SequenceEqual(big), "the answer that arrived is not the upstream's");
        var after = Encoding.ASCII.GetString(whole.Body.AsSpan(big.Length));
        Assert.True(after.Length == 0 || (after.StartsWith("HTTP/1.1 200 OK", StringComparison.Ordinal)
                                          && after.Contains("Connection: close", StringComparison.OrdinalIgnoreCase)
                                          && after.EndsWith("\r\n\r\n", StringComparison.Ordinal)),
            $"after the answer came: {after}");
        Assert.Equal(1, tap.RequestsHandled);
    }

    [Fact]
    public async Task Stopping_the_tap_answers_a_request_that_arrives_meanwhile_503_without_forwarding_it()
    {
        var big = new byte[8 * 1024 * 1024];
        await using var upstream = new StubUpstream((request, _) =>
            request.Url!.AbsolutePath == "/big" ? (200, big, "application/octet-stream", null, null) : (200, "ok"u8.ToArray(), "text/plain", null, null));
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), log);

        // A caller that stops reading its answer keeps the tap's drain going for its whole two seconds.
        using var stalled = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /big HTTP/1.1\r\nHost: localhost\r\n\r\n", receiveBuffer: 16 * 1024);
        Assert.Equal(1, await stalled.Client.Client.ReceiveAsync(new byte[1]));
        var stopping = tap.DisposeAsync().AsTask();
        Assert.True(await WaitUntilAsync(() => tap.Stopping), "the tap never began to stop");

        using var late = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /late HTTP/1.1\r\nHost: localhost\r\n\r\n");
        var heard = await late.HearAsync(Patience);
        await stopping.WaitAsync(Patience);

        Assert.StartsWith("HTTP/1.1 503 ", heard.FirstLine);
        Assert.Contains("Connection: close", heard.Head, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("proxy-tap: the tap is stopping", Encoding.UTF8.GetString(heard.Body), StringComparison.Ordinal);
        Assert.True(heard.LetGo, $"the late caller was answered and then held: {heard}");
        lock (upstream.Seen)
            Assert.DoesNotContain(upstream.Seen, seen => seen.Path == "/late");
        Assert.Equal(0, tap.ForwardFailures);
    }

    [Fact]
    public async Task A_body_that_ends_as_the_tap_stops_is_neither_forwarded_nor_recorded()
    {
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        var log = new List<string>();
        var asked = 0;
        Tap? stopping = null;
        await using var tap = await StartAsync(upstream.BaseUri, sink, log, o => o.IdentityResolver = (_, _) =>
        {
            Interlocked.Increment(ref asked);
            return null;
        }, prepare: t =>
        {
            stopping = t;
            // The body ends, whole, only once the tap has begun to stop and settled the answers still reading theirs.
            t.WrapRequestBody = _ => new EndsWhenStoppingStream(() => stopping!.Stopping);
        });

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "POST /late-body HTTP/1.1\r\nHost: localhost\r\nContent-Length: 5\r\n\r\nhello");
        Assert.True(await WaitUntilAsync(() => tap.InFlight == 1), "the tap never took the request");
        await tap.DisposeAsync();
        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the handler never ended");

        Assert.Empty(upstream.Seen);
        lock (sink.Logs)
            Assert.Empty(sink.Logs);
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Equal(0, Volatile.Read(ref asked)); // the host's resolver is not asked about a request that is not forwarded
    }

    [Fact]
    public async Task A_handler_that_outlives_disposal_is_not_a_forward_failure()
    {
        await using var upstream = new StubUpstream(Ok);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, sink, log, o => o.IdentityResolver = (_, _) =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            return null;
        });

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /slow-identity HTTP/1.1\r\nHost: localhost\r\n\r\n");
        Assert.True(entered.Wait(Patience), "the resolver was never called");
        await tap.DisposeAsync(); // the handler is still in the resolver: the drain gives up on it
        release.Set();

        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the handler never ended");
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Empty(Errors(log));
        Assert.Empty(upstream.Seen);
        // No forward was tried: none recorded as cut short, and no line about one.
        lock (sink.Logs)
            Assert.Empty(sink.Logs);
        lock (log)
            Assert.DoesNotContain(log, line => line.Contains("proxy-tap stopping", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_request_the_stop_reaches_before_its_forward_is_refused_and_not_recorded()
    {
        await using var upstream = new StubUpstream(Ok);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var sink = new ListSink();
        var log = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, sink, log, o => o.IdentityResolver = (_, _) =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            return null;
        });

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /resolving HTTP/1.1\r\nHost: localhost\r\n\r\n");
        Assert.True(entered.Wait(Patience), "the resolver was never called");
        var stopping = tap.DisposeAsync().AsTask();
        Assert.True(await WaitUntilAsync(() => tap.Stopping), "the tap never began to stop");
        release.Set(); // the resolver returns within the drain, before the forward has begun

        var heard = await caller.HearAsync(Patience);
        await stopping.WaitAsync(Patience);

        Assert.StartsWith("HTTP/1.1 503 ", heard.FirstLine);
        Assert.Contains("Connection: close", heard.Head, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("proxy-tap: the tap is stopping", Encoding.UTF8.GetString(heard.Body), StringComparison.Ordinal);
        lock (upstream.Seen)
            Assert.Empty(upstream.Seen);
        lock (sink.Logs)
            Assert.Empty(sink.Logs);
        Assert.Equal(0, tap.ForwardFailures);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<Tap> StartAsync(
        Uri forward, ListSink sink, List<string> log, Action<ProxyTapOptions>? configure = null, Action<Tap>? prepare = null)
    {
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = forward,
            CallerName = "web",
            ServiceName = "api",
            Sink = sink,
            CaptureUnattributedRequests = true,
            FallbackTestId = "stopping",
            Log = line => { lock (log) log.Add(line); },
        };
        configure?.Invoke(options);
        var tap = new Tap(options);
        prepare?.Invoke(tap);
        await tap.StartAsync();
        return tap;
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

    /// <summary>A request body that ends, with no bytes, half a second after the tap has begun to stop.</summary>
    private sealed class EndsWhenStoppingStream(Func<bool> stopping) : Stream
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

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (!stopping())
                await Task.Delay(10, cancellationToken);
            await Task.Delay(500, cancellationToken); // DisposeAsync settles the answers still reading as it begins
            return 0;
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
}
