using System.Net;
using Kronikol.Extensions.ProxyTap;
using Tap = Kronikol.Extensions.ProxyTap.ProxyTap;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// A tap starts once and stops when it is disposed, as <c>TcpTap</c> and <c>OtlpTap</c> do: a second start is refused, a
/// start after the dispose binds nothing, a start and a dispose that race leave nothing running, and the token
/// <see cref="Tap.StartAsync"/> is given aborts a start that has not begun and nothing after it. That token was linked into
/// the tap's lifetime, so cancelling it once the tap had started left it listening and answering every request 503.
/// </summary>
public class ProxyTapLifecycleTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static (int, byte[], string?, string?, Dictionary<string, string>?) Ok(HttpListenerRequest _, byte[] __) =>
        (200, "ok"u8.ToArray(), "text/plain", null, null);

    [Fact]
    public async Task A_second_start_is_refused_and_the_first_keeps_serving()
    {
        await using var upstream = new StubUpstream(Ok);
        var clock = new CountingClock();
        await using var tap = NewTap(upstream.BaseUri, clock);
        await tap.StartAsync();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => tap.StartAsync());

        Assert.Equal("This tap has already been started; a tap starts once and stops when it is disposed.", thrown.Message);
        Assert.StartsWith("HTTP/1.1 200 ", await GetAsync(tap));
        Assert.Equal(1, clock.Running); // one heartbeat, not one per start
    }

    [Fact]
    public async Task A_start_after_the_dispose_is_refused_and_binds_nothing()
    {
        var tap = NewTap(new Uri("http://localhost:1"), new CountingClock());
        await tap.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => tap.StartAsync());
        Assert.False(tap.IsListening);
    }

    [Fact]
    public async Task Cancelling_the_start_token_after_the_start_leaves_the_tap_serving()
    {
        await using var upstream = new StubUpstream(Ok);
        await using var tap = NewTap(upstream.BaseUri, new CountingClock());
        using var start = new CancellationTokenSource();
        await tap.StartAsync(start.Token);

        await start.CancelAsync();

        Assert.StartsWith("HTTP/1.1 200 ", await GetAsync(tap)); // it was answered 503, as a stopping tap answers
    }

    [Fact]
    public async Task A_start_whose_token_is_cancelled_binds_nothing()
    {
        var clock = new CountingClock();
        await using var tap = NewTap(new Uri("http://localhost:1"), clock);
        using var start = new CancellationTokenSource();
        await start.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tap.StartAsync(start.Token));

        Assert.False(tap.IsListening);
        Assert.Equal(0, clock.Running);
    }

    [Fact]
    public async Task A_start_racing_the_dispose_leaves_nothing_running()
    {
        var clock = new CountingClock();
        var listening = 0;
        for (var round = 0; round < 200; round++)
        {
            var tap = NewTap(new Uri("http://localhost:1"), clock);
            var starting = Task.Run(async () =>
            {
                try
                {
                    await tap.StartAsync();
                }
                catch (ObjectDisposedException)
                {
                    // The dispose came first.
                }
            });
            var disposing = Task.Run(async () => await tap.DisposeAsync());
            await Task.WhenAll(starting, disposing).WaitAsync(Patience);
            if (tap.IsListening)
                listening++;
        }

        Assert.Equal(0, listening);
        Assert.Equal(0, clock.Running); // a heartbeat made after the dispose had stopped the watch ran for good
    }

    [Fact]
    public async Task A_failed_wait_for_a_request_is_logged_and_tried_again()
    {
        // The listener's wait fails while the tap runs (out of handles, say). The loop ended on it, and the tap went on
        // listening with no one to take a request: callers connected and were never answered, and nothing said so.
        await using var upstream = new StubUpstream(Ok);
        var log = new List<string>();
        var pauses = new List<TimeSpan>();
        var failures = 3;
        await using var tap = NewTap(upstream.BaseUri, new CountingClock(), log);
        tap.GetContext = listener => Interlocked.Decrement(ref failures) >= 0
            ? throw new HttpListenerException(1450, "Insufficient system resources exist to complete the requested service")
            : listener.GetContextAsync();
        tap.Pause = (pause, _) =>
        {
            lock (pauses)
                pauses.Add(pause);
            return Task.CompletedTask;
        };
        await tap.StartAsync();

        Assert.StartsWith("HTTP/1.1 200 ", await GetAsync(tap));

        lock (pauses)
            Assert.Equal([TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40)], pauses);
        lock (log)
            Assert.Single(log, line => line.Contains(
                "proxy-tap could not take a request (HttpListenerException: Insufficient system resources exist to complete the requested service); still listening, and trying again with pauses of up to 1 s",
                StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ helpers

    private static Tap NewTap(Uri forward, TimeProvider clock, List<string>? log = null) => new(new ProxyTapOptions
    {
        ListenPort = StubUpstream.FreePort(),
        ForwardBaseUri = forward,
        CallerName = "web",
        ServiceName = "api",
        Sink = new ListSink(),
        CaptureUnattributedRequests = true,
        FallbackTestId = "lifecycle",
        Log = log is null ? null : line => { lock (log) log.Add(line); },
    })
    {
        Clock = clock,
        DebuggerAttached = static () => false,
    };

    private static async Task<string> GetAsync(Tap tap)
    {
        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /anyone HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
        return (await caller.HearAsync(Patience)).FirstLine;
    }

    /// <summary>The system's clock, counting the timers made on it that have not been disposed.</summary>
    private sealed class CountingClock : TimeProvider
    {
        private int _running;

        public int Running => Volatile.Read(ref _running);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _running);
            return new Counted(this, base.CreateTimer(callback, state, dueTime, period));
        }

        private sealed class Counted(CountingClock owner, ITimer inner) : ITimer
        {
            private int _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    Interlocked.Decrement(ref owner._running);
                inner.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
