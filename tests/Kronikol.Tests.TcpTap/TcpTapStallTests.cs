using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kronikol.Extensions.TcpTap;
using Kronikol.Extensions.TcpTap.Protocols;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Extensions.Time.Testing;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// A pair that cannot finish is released: one whose side has finished sending while the other stays silent past
/// <see cref="TcpTapOptions.HalfClosedTimeout"/>, and one whose write to a side has not completed for
/// <see cref="TcpTapOptions.StalledWriteTimeout"/>. An idle pair is never touched, and the stuck-connection reaper runs on
/// the tap's heartbeat and monotonic clock (plans/TAP_CALLER_TIME_LIMITS_PLAN.md R3, #173). Unless a fact says it runs on
/// the real clock, it drives the limits with a fake one, each step ordered through the watch's list of entries.
/// </summary>
public class TcpTapStallTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PastTheLimit = Limit + TimeSpan.FromSeconds(1);
    private const int EightMegabytes = 8 * 1024 * 1024;
    private const int Piece = 16 * 1024;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_pair_whose_upstream_stays_silent_after_the_client_half_closes_is_released(bool realClock)
    {
        var log = new List<string>();
        var clock = realClock ? null : new FakeTimeProvider();
        await using var run = await StartAsync(log, clock);
        await using var pair = await run.ConnectAsync();

        await pair.Client.GetStream().WriteAsync("hello"u8.ToArray());
        await ReceiveExactlyAsync(pair.Upstream, 5);
        pair.Client.Client.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await pair.Upstream.ReceiveAsync(new byte[16])); // the client's FIN, passed on
        if (clock is not null)
        {
            await StalledAsync(run.Tap, TcpTapCore.HalfClosedPhase);
            clock.Advance(PastTheLimit);
        }

        Assert.True(await WaitUntilAsync(() => run.Tap.LiveConnections == 0), "the pair was never released");
        Assert.Equal(1, run.Tap.HalfClosedPairsClosed);
        Assert.Equal(0, run.Tap.StalledWritesClosed);
        Assert.True(await EndedAsync(pair.Client.Client), "the client's side was left open");
        lock (log)
            Assert.Contains(log, line => line.Contains("closed: the client finished sending and the server moved nothing for 00:00:01", StringComparison.Ordinal));
        var diagnostic = Assert.Single(run.Tap.Diagnostics(), entry => entry.Message.Contains("HalfClosedTimeout", StringComparison.Ordinal));
        Assert.Equal(DiagnosticKind.CaptureDegraded, diagnostic.Kind);
        Assert.Contains("1 connection(s) closed after one side finished sending and the other moved nothing for HalfClosedTimeout (00:00:01)", diagnostic.Message, StringComparison.Ordinal);
        await run.Tap.DisposeAsync();
        lock (log)
            Assert.Contains(log, line => line.Contains("tcp-tap stopped (", StringComparison.Ordinal)
                && line.EndsWith(", 1 half-closed pairs closed, 0 stalled writes closed)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_limit_raises_no_capture_degradation_of_its_own()
    {
        // The close is counted and logged, not reported as capture lost: with nothing in flight, the pair lost nothing. A pair
        // closed mid-message is its decoder's to report (A_limit_that_cuts_a_reply_short_leaves_the_decoder_to_report_it).
        var degradations = new System.Collections.Concurrent.ConcurrentQueue<CaptureDegradation>();
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync([], clock, o => o.OnCaptureDegraded = degradations.Enqueue);
        await using var pair = await run.ConnectAsync(clientReceiveBuffer: Piece);

        pair.Client.Client.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await pair.Upstream.ReceiveAsync(new byte[16]));
        await StalledAsync(run.Tap, TcpTapCore.HalfClosedPhase);
        clock.Advance(PastTheLimit);
        Assert.True(await WaitUntilAsync(() => run.Tap.HalfClosedPairsClosed == 1), "the half-closed pair was not closed");

        await using var second = await run.ConnectAsync(clientReceiveBuffer: Piece);
        _ = SendQuietlyAsync(second.Upstream, new byte[EightMegabytes]);
        await StalledAsync(run.Tap, TcpTapCore.WritePhase);
        clock.Advance(PastTheLimit);
        Assert.True(await WaitUntilAsync(() => run.Tap.StalledWritesClosed == 1), "the stalled write was not closed");
        Assert.True(await WaitUntilAsync(() => run.Tap.LiveConnections == 0), "a pair was never released");

        Assert.Empty(degradations);
    }

    [Fact]
    public async Task A_pair_whose_client_stays_silent_after_the_upstream_half_closes_is_released()
    {
        var log = new List<string>();
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync(log, clock);
        await using var pair = await run.ConnectAsync();

        await pair.Upstream.SendAsync("hello"u8.ToArray());
        await ReceiveExactlyAsync(pair.Client.Client, 5);
        pair.Upstream.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await pair.Client.Client.ReceiveAsync(new byte[16])); // the upstream's FIN, passed on
        await StalledAsync(run.Tap, TcpTapCore.HalfClosedPhase);
        clock.Advance(PastTheLimit);

        Assert.True(await WaitUntilAsync(() => run.Tap.LiveConnections == 0), "the pair was never released");
        Assert.Equal(1, run.Tap.HalfClosedPairsClosed);
        lock (log)
            Assert.Contains(log, line => line.Contains("closed: the server finished sending and the client moved nothing for 00:00:01", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_idle_pair_is_never_closed()
    {
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync([], clock);
        await using var pair = await run.ConnectAsync();

        // Traffic first, so a write left marked pending would be seen.
        await pair.Client.GetStream().WriteAsync("ping"u8.ToArray());
        await ReceiveExactlyAsync(pair.Upstream, 4);
        await pair.Upstream.SendAsync("pong"u8.ToArray());
        await ReceiveExactlyAsync(pair.Client.Client, 4);
        Assert.True(await WaitUntilAsync(() => !PendingWrites(run.Tap).Any()), "a write was left marked pending");

        for (var i = 0; i < 3; i++)
        {
            clock.Advance(PastTheLimit); // three times both limits, and more
            await Task.Delay(100);
        }

        await Task.Delay(300);
        Assert.Equal(0, run.Tap.HalfClosedPairsClosed);
        Assert.Equal(0, run.Tap.StalledWritesClosed);
        Assert.Equal(1, run.Tap.LiveConnections);
        await pair.Client.GetStream().WriteAsync("again"u8.ToArray());
        await ReceiveExactlyAsync(pair.Upstream, 5); // and the pair still carries traffic
    }

    [Fact]
    public async Task A_client_that_stops_reading_a_large_reply_releases_the_pair()
    {
        var log = new List<string>();
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync(log, clock);
        await using var pair = await run.ConnectAsync(clientReceiveBuffer: Piece);

        _ = SendQuietlyAsync(pair.Upstream, new byte[EightMegabytes]); // the client reads none of it
        await StalledAsync(run.Tap, TcpTapCore.WritePhase);
        clock.Advance(PastTheLimit);

        Assert.True(await WaitUntilAsync(() => run.Tap.LiveConnections == 0), "the pair was never released");
        Assert.Equal(1, run.Tap.StalledWritesClosed);
        Assert.Equal(0, run.Tap.HalfClosedPairsClosed);
        lock (log)
            Assert.Contains(log, line => line.Contains("closed: a write to the client did not complete for 00:00:01 (it stopped reading)", StringComparison.Ordinal));
        Assert.Contains(run.Tap.Diagnostics(), entry => entry.Kind == DiagnosticKind.CaptureDegraded
            && entry.Message.Contains("1 connection(s) closed because a write to one side did not complete for StalledWriteTimeout (00:00:01): that side had stopped reading", StringComparison.Ordinal));
        await run.Tap.DisposeAsync();
        lock (log)
            Assert.Contains(log, line => line.Contains("tcp-tap stopped (", StringComparison.Ordinal)
                && line.EndsWith(", 0 half-closed pairs closed, 1 stalled writes closed)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_upstream_that_stops_reading_its_clients_commands_releases_the_pair()
    {
        var log = new List<string>();
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync(log, clock);
        await using var pair = await run.ConnectAsync(upstreamReceiveBuffer: Piece);

        _ = SendQuietlyAsync(pair.Client.Client, new byte[2 * EightMegabytes]); // the upstream reads none of it
        await StalledAsync(run.Tap, TcpTapCore.WritePhase);
        clock.Advance(PastTheLimit);

        Assert.True(await WaitUntilAsync(() => run.Tap.LiveConnections == 0), "the pair was never released");
        Assert.Equal(1, run.Tap.StalledWritesClosed);
        lock (log)
            Assert.Contains(log, line => line.Contains("closed: a write to the server did not complete for 00:00:01", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_slow_steady_reader_keeps_its_pair()
    {
        var reply = new byte[EightMegabytes];
        new Random(173).NextBytes(reply);
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync([], clock);
        await using var pair = await run.ConnectAsync(clientReceiveBuffer: Piece);

        // The upstream keeps its side open: after a FIN the client's silence would be HalfClosedTimeout's to judge, and the
        // tail of the reply that sits in the client's socket buffers is no progress the tap can see.
        var sending = Task.Run(() => pair.Upstream.SendAsync(reply));
        var stream = pair.Client.GetStream();
        using var received = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (received.Length < reply.Length)
        {
            // A mebibyte at a time, then the clock moves 0.6 s, under the limit, once the tap has written since the move.
            var target = Math.Min(received.Length + (1024 * 1024), reply.Length);
            while (received.Length < target)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, target - received.Length)));
                if (read == 0)
                    throw new EndOfStreamException($"the pair ended after {received.Length:N0} bytes");
                received.Write(buffer, 0, read);
            }

            await WroteSinceAsync(run.Tap, clock);
            clock.Advance(TimeSpan.FromMilliseconds(600));
        }

        await sending.WaitAsync(Patience);
        Assert.Equal(reply, received.ToArray());
        Assert.Equal(0, run.Tap.StalledWritesClosed);
        Assert.Equal(0, run.Tap.HalfClosedPairsClosed);
        Assert.Equal(1, run.Tap.LiveConnections);
    }

    [Fact]
    public async Task A_half_close_answered_in_time_ends_normally()
    {
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync([], clock);
        await using var pair = await run.ConnectAsync();

        await pair.Client.GetStream().WriteAsync("q"u8.ToArray());
        pair.Client.Client.Shutdown(SocketShutdown.Send);
        await ReceiveExactlyAsync(pair.Upstream, 1);
        Assert.Equal(0, await pair.Upstream.ReceiveAsync(new byte[16]));
        await EntryAsync(run.Tap, TcpTapCore.HalfClosedPhase);
        clock.Advance(TimeSpan.FromMilliseconds(600));
        await pair.Upstream.SendAsync("a"u8.ToArray());
        pair.Upstream.Shutdown(SocketShutdown.Send);

        await ReceiveExactlyAsync(pair.Client.Client, 1);
        Assert.Equal(0, await pair.Client.Client.ReceiveAsync(new byte[16]));
        Assert.True(await WaitUntilAsync(() => run.Tap.LiveConnections == 0), "the pair was never released");
        Assert.Equal(0, run.Tap.HalfClosedPairsClosed);
        Assert.Empty(run.Tap.Watch!.Entries);
    }

    [Fact]
    public async Task A_half_closed_pair_that_keeps_moving_is_kept()
    {
        const int steps = 5; // three times the limit, at 0.6 s a step
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync([], clock);
        await using var pair = await run.ConnectAsync();

        pair.Client.Client.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await pair.Upstream.ReceiveAsync(new byte[16]));
        await EntryAsync(run.Tap, TcpTapCore.HalfClosedPhase);
        var piece = new byte[Piece];
        for (var i = 0; i < steps; i++)
        {
            await pair.Upstream.SendAsync(piece);
            await ReceiveExactlyAsync(pair.Client.Client, Piece);
            await ProgressedAsync(run.Tap, TcpTapCore.HalfClosedPhase, clock);
            clock.Advance(TimeSpan.FromMilliseconds(600));
        }

        Assert.Equal(1, run.Tap.LiveConnections);
        pair.Upstream.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await pair.Client.Client.ReceiveAsync(new byte[16]));
        Assert.True(await WaitUntilAsync(() => run.Tap.LiveConnections == 0), "the pair was never released");
        Assert.Equal(0, run.Tap.HalfClosedPairsClosed);
    }

    [Fact]
    public async Task The_reaper_ignores_a_wall_clock_jump()
    {
        var fake = new FakeTimeProvider();
        var clock = new JumpingClock(fake);
        var decoder = new OneCommandDecoder();
        await using var run = await StartAsync([], clock, o => o.ReapStuckConnectionsAfter = TimeSpan.FromSeconds(30), _ => decoder);
        await using var pair = await run.ConnectAsync();

        var sentAt = clock.GetUtcNow();
        await pair.Client.GetStream().WriteAsync("cmd"u8.ToArray());
        await ReceiveExactlyAsync(pair.Upstream, 3);
        Assert.True(await WaitUntilAsync(() => decoder.Seen is not null), "the command was never decoded");
        Assert.Equal(sentAt, decoder.Seen); // a segment keeps the wall-clock time it arrived at

        fake.Advance(TimeSpan.FromSeconds(2)); // the reaper sees the command unanswered
        clock.Jump = TimeSpan.FromHours(1);
        fake.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(1500); // past a beat of a reaper on the real clock too
        Assert.Equal(0, run.Tap.StuckConnectionsReaped);
        Assert.Equal(1, run.Tap.LiveConnections);

        fake.Advance(TimeSpan.FromSeconds(30)); // and it reaps on the time that passed
        Assert.True(await WaitUntilAsync(() => run.Tap.StuckConnectionsReaped == 1), "the command's age on the monotonic clock never reaped it");
    }

    /// <summary>A value of the property's type that differs from a fresh <see cref="TcpTapOptions"/>'s.</summary>
    private static object NonDefault(PropertyInfo property, object? fresh) => property.PropertyType switch
    {
        var t when t == typeof(int) && property.Name.EndsWith("Port", StringComparison.Ordinal) => 54321,
        var t when t == typeof(int) || t == typeof(int?) => (fresh as int? ?? 0) + 17,
        var t when t == typeof(long?) => (fresh as long? ?? 0) + 17,
        var t when t == typeof(string) => "x-" + property.Name,
        var t when t == typeof(bool) => !(bool)fresh!,
        var t when t.IsEnum => Enum.GetValues(t).Cast<object>().First(v => !v.Equals(fresh)),
        var t when t == typeof(TimeSpan) => (TimeSpan)fresh! + TimeSpan.FromSeconds(1),
        var t when t == typeof(TimeSpan?) => ((TimeSpan?)fresh ?? TimeSpan.Zero) + TimeSpan.FromSeconds(1),
        var t when t == typeof(IRequestResponseSink) => new RecordingSink(),
        var t when t == typeof(Action<string>) => new Action<string>(_ => { }),
        var t when t == typeof(Action<CaptureDegradation>) => new Action<CaptureDegradation>(_ => { }),
        var t when t == typeof(Func<string, string>) => new Func<string, string>(value => value),
        var t when t == typeof(Func<(string Name, string Id)?>) => new Func<(string Name, string Id)?>(() => null),
        var t when t == typeof(Func<TcpTapConnectionContext, IProtocolDecoder>) => new Func<TcpTapConnectionContext, IProtocolDecoder>(_ => new OneCommandDecoder()),
        var t => throw new InvalidOperationException(
            $"No test value for TcpTapOptions.{property.Name} ({t}); add one so the copy of the new option is checked."),
    };

    [Fact]
    public void CopyTo_copies_every_option()
    {
        var fresh = new TcpTapOptions();
        var source = new TcpTapOptions();
        var properties = typeof(TcpTapOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod?.IsPublic == true)
            .ToArray();
        foreach (var property in properties)
        {
            var freshValue = property.GetValue(fresh);
            property.SetValue(source, NonDefault(property, freshValue));
            Assert.False(Equals(freshValue, property.GetValue(source)), $"{property.Name} still has its default");
        }

        var copy = new TcpTapOptions();
        source.CopyTo(copy);

        foreach (var property in properties)
            Assert.True(Equals(property.GetValue(source), property.GetValue(copy)), $"{property.Name} was not copied by CopyTo");
    }

    [Fact]
    public async Task Disposing_with_the_reaper_on_does_not_wait_for_its_tick()
    {
        var slowest = TimeSpan.Zero;
        for (var round = 0; round < 5; round++)
        {
            var run = await StartAsync([], clock: null, o => o.ReapStuckConnectionsAfter = TimeSpan.FromMinutes(5), _ => new OneCommandDecoder());
            var pairs = new List<RawPair>();
            for (var i = 0; i < 3; i++)
                pairs.Add(await run.ConnectAsync());
            Assert.True(await WaitUntilAsync(() => run.Tap.LiveConnections == 3), "the pairs never started");

            var watch = Stopwatch.StartNew();
            await run.Tap.DisposeAsync();
            watch.Stop();
            if (watch.Elapsed > slowest)
                slowest = watch.Elapsed;
            foreach (var pair in pairs)
                await pair.DisposeAsync();
            await run.DisposeAsync();
        }

        Assert.InRange(slowest, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task No_limit_applies_while_a_debugger_is_attached()
    {
        var clock = new FakeTimeProvider();
        var attached = new StrongBox<bool>(true);
        await using var run = await StartAsync([], clock, debugger: () => Volatile.Read(ref attached.Value));
        await using var pair = await run.ConnectAsync();

        pair.Client.Client.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await pair.Upstream.ReceiveAsync(new byte[16]));
        await StalledAsync(run.Tap, TcpTapCore.HalfClosedPhase);
        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(500);
        Assert.Equal(0, run.Tap.HalfClosedPairsClosed);
        Assert.Equal(1, run.Tap.LiveConnections);

        Volatile.Write(ref attached.Value, false);
        clock.Advance(StallWatch.Beat);
        Assert.True(await WaitUntilAsync(() => run.Tap.HalfClosedPairsClosed == 1), "a stall that outlived the detach was not cut at the next beat");
    }

    [Fact]
    public async Task The_reaper_acts_while_a_debugger_is_attached()
    {
        var clock = new FakeTimeProvider();
        var decoder = new OneCommandDecoder();
        await using var run = await StartAsync([], clock, o => o.ReapStuckConnectionsAfter = TimeSpan.FromSeconds(3), _ => decoder, debugger: () => true);
        await using var pair = await run.ConnectAsync();

        await pair.Client.GetStream().WriteAsync("cmd"u8.ToArray());
        await ReceiveExactlyAsync(pair.Upstream, 3);
        Assert.True(await WaitUntilAsync(() => decoder.Seen is not null), "the command was never decoded");
        clock.Advance(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(await WaitUntilAsync(() => run.Tap.StuckConnectionsReaped == 1), "the reaper stood down under a debugger, as it never did");
    }

    [Fact]
    public void The_limits_are_100_seconds_by_default()
    {
        var options = new TcpTapOptions();
        Assert.Equal(TimeSpan.FromSeconds(100), options.HalfClosedTimeout);
        Assert.Equal(TimeSpan.FromSeconds(100), options.StalledWriteTimeout);
    }

    [Theory]
    [InlineData(nameof(TcpTapOptions.HalfClosedTimeout), 0)]
    [InlineData(nameof(TcpTapOptions.HalfClosedTimeout), -2)]
    [InlineData(nameof(TcpTapOptions.HalfClosedTimeout), -1000)]
    [InlineData(nameof(TcpTapOptions.StalledWriteTimeout), 0)]
    [InlineData(nameof(TcpTapOptions.StalledWriteTimeout), -2)]
    [InlineData(nameof(TcpTapOptions.StalledWriteTimeout), -1000)]
    public void A_limit_of_zero_or_less_is_refused(string option, int milliseconds)
    {
        var options = Options(54321);
        typeof(TcpTapOptions).GetProperty(option)!.SetValue(options, (TimeSpan?)TimeSpan.FromMilliseconds(milliseconds));

        var thrown = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        Assert.Equal(option, thrown.ParamName);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("infinite")]
    [InlineData("max")]
    public async Task A_half_close_limit_of_none_never_closes(string value)
    {
        TimeSpan? limit = value switch
        {
            "null" => null,
            "infinite" => Timeout.InfiniteTimeSpan,
            _ => TimeSpan.MaxValue,
        };
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync([], clock, o => o.HalfClosedTimeout = limit);
        await using var pair = await run.ConnectAsync();

        pair.Client.Client.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await pair.Upstream.ReceiveAsync(new byte[16]));
        if (value == "max")
        {
            await StalledAsync(run.Tap, TcpTapCore.HalfClosedPhase);
        }
        else
        {
            await Task.Delay(300);
            Assert.DoesNotContain(run.Tap.Watch!.Entries, entry => entry.Phase == TcpTapCore.HalfClosedPhase); // no limit, no entry
        }

        clock.Advance(TimeSpan.FromHours(1));
        await Task.Delay(500);
        Assert.Equal(0, run.Tap.HalfClosedPairsClosed);
        Assert.Equal(1, run.Tap.LiveConnections);
    }

    [Fact]
    public async Task A_pair_that_ends_leaves_no_entry_behind()
    {
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync([], clock);
        await using var pair = await run.ConnectAsync();

        await pair.Client.GetStream().WriteAsync("bye"u8.ToArray());
        await ReceiveExactlyAsync(pair.Upstream, 3);
        pair.Client.Client.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await pair.Upstream.ReceiveAsync(new byte[16]));
        pair.Upstream.Shutdown(SocketShutdown.Send);
        Assert.Equal(0, await pair.Client.Client.ReceiveAsync(new byte[16]));

        Assert.True(await WaitUntilAsync(() => run.Tap.LiveConnections == 0), "the pair was never released");
        Assert.Empty(run.Tap.Watch!.Entries);
        clock.Advance(PastTheLimit);
        await Task.Delay(200);
        Assert.Equal(0, run.Tap.HalfClosedPairsClosed + run.Tap.StalledWritesClosed);
    }

    [Fact]
    public async Task A_start_that_cannot_bind_leaves_no_heartbeat_running()
    {
        // A tap whose start threw is often dropped undisposed, and a heartbeat left running would hold it for good.
        var clock = new CountingClock();
        var taken = LoopbackPort.Listening();
        try
        {
            var options = Options(54321);
            options.ListenPort = ((IPEndPoint)taken.LocalEndpoint).Port;
            var tap = new TcpTapCore(options) { Clock = clock, DebuggerAttached = static () => false };

            var thrown = await Assert.ThrowsAsync<SocketException>(() => tap.StartAsync());
            Assert.Equal(SocketError.AddressAlreadyInUse, thrown.SocketErrorCode);
            Assert.Equal(0, clock.Running);
        }
        finally
        {
            taken.Stop();
        }

        // The count sees the heartbeat: a start that binds runs one, and disposing the tap stops it.
        await using var run = await StartAsync([], clock);
        Assert.Equal(1, clock.Running);
        await run.Tap.DisposeAsync();
        Assert.Equal(0, clock.Running);
    }

    [Fact]
    public async Task A_pair_keeps_one_write_entry_however_many_writes_it_makes()
    {
        // A pending write is a timestamp the pump sets and clears, not an entry of its own (plans/TAP_CALLER_TIME_LIMITS_PLAN.md
        // §4.5 item 4): the hot path allocates nothing for the limit.
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync([], clock);
        await using var pair = await run.ConnectAsync();

        var seen = new HashSet<StallWatch.Entry>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < 50; i++)
        {
            await pair.Client.GetStream().WriteAsync("ping"u8.ToArray());
            await ReceiveExactlyAsync(pair.Upstream, 4);
            await pair.Upstream.SendAsync("pong"u8.ToArray());
            await ReceiveExactlyAsync(pair.Client.Client, 4);
            foreach (var entry in run.Tap.Watch!.Entries.Where(entry => entry.Phase == TcpTapCore.WritePhase))
                seen.Add(entry);
        }

        Assert.Single(seen);
    }

    [Fact]
    public async Task A_half_closed_pair_whose_client_stops_reading_is_closed_as_a_stalled_write()
    {
        // Both limits come due in the same beat: the client has finished sending, and the tap's write to it has not
        // completed. The client stopped reading, which is the stalled write's to report, every time.
        var log = new List<string>();
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync(log, clock);
        for (var round = 1; round <= 8; round++)
        {
            await using var pair = await run.ConnectAsync(clientReceiveBuffer: Piece);
            pair.Client.Client.Shutdown(SocketShutdown.Send);
            Assert.Equal(0, await pair.Upstream.ReceiveAsync(new byte[16]));
            _ = SendQuietlyAsync(pair.Upstream, new byte[EightMegabytes]); // the client reads none of it
            await StalledAsync(run.Tap, TcpTapCore.WritePhase);
            await StalledAsync(run.Tap, TcpTapCore.HalfClosedPhase);
            clock.Advance(PastTheLimit);

            Assert.True(await WaitUntilAsync(() => run.Tap.LiveConnections == 0), $"round {round}: the pair was never released");
            Assert.Equal(round, run.Tap.StalledWritesClosed);
            Assert.Equal(0, run.Tap.HalfClosedPairsClosed);
        }

        lock (log)
            Assert.DoesNotContain(log, line => line.Contains("finished sending", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pair_the_reaper_and_a_limit_both_find_is_closed_and_counted_once()
    {
        // The reaper at the stalled-write limit: a command the upstream never reads is both unanswered and a write that
        // does not complete, and both come due in the same beat.
        var log = new List<string>();
        var degradations = new System.Collections.Concurrent.ConcurrentQueue<CaptureDegradation>();
        var clock = new FakeTimeProvider();
        var decoder = new OneCommandDecoder();
        await using var run = await StartAsync(log, clock, o =>
        {
            o.ReapStuckConnectionsAfter = Limit;
            o.OnCaptureDegraded = degradations.Enqueue;
        }, _ => decoder);
        await using var pair = await run.ConnectAsync(upstreamReceiveBuffer: Piece);

        _ = SendQuietlyAsync(pair.Client.Client, new byte[2 * EightMegabytes]); // the upstream reads none of it
        Assert.True(await WaitUntilAsync(() => decoder.Seen is not null), "the command was never decoded");
        await StalledAsync(run.Tap, TcpTapCore.WritePhase);
        clock.Advance(PastTheLimit);

        Assert.True(await WaitUntilAsync(() => run.Tap.LiveConnections == 0), "the pair was never released");
        await Task.Delay(300); // a second close would have been counted by now
        Assert.Equal(1, run.Tap.StuckConnectionsReaped + run.Tap.StalledWritesClosed);
        Assert.Equal(run.Tap.StuckConnectionsReaped, degradations.Count(each => each.Kind == CaptureDegradationKind.StuckConnectionReaped));
        lock (log)
            Assert.Single(log, line => line.Contains(" reaped: ", StringComparison.Ordinal) || line.Contains(" closed: a write to", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_limit_that_cuts_a_reply_short_leaves_the_decoder_to_report_it()
    {
        // The limit's close raises nothing of its own, but a Redis client cut off mid-reply has lost that interaction, and
        // the decoder reports it as any connection closed mid-message.
        var log = new List<string>();
        var degradations = new System.Collections.Concurrent.ConcurrentQueue<CaptureDegradation>();
        var clock = new FakeTimeProvider();
        await using var run = await StartAsync(log, clock, o => o.OnCaptureDegraded = degradations.Enqueue,
            context => new RedisProtocolDecoder(context, new RedisTapOptions()));
        await using var pair = await run.ConnectAsync(clientReceiveBuffer: Piece);

        await pair.Client.GetStream().WriteAsync("*2\r\n$3\r\nGET\r\n$1\r\nk\r\n"u8.ToArray());
        await ReceiveExactlyAsync(pair.Upstream, 20);
        var reply = new byte[EightMegabytes];
        "$8388598\r\n"u8.CopyTo(reply); // a bulk string that fills the rest, short of its ending
        _ = SendQuietlyAsync(pair.Upstream, reply); // the client reads none of it
        await StalledAsync(run.Tap, TcpTapCore.WritePhase);
        clock.Advance(PastTheLimit);

        Assert.True(await WaitUntilAsync(() => run.Tap.ConnectionsClosedMidMessage == 1), "the decoder never reported the reply cut short");
        Assert.Equal(1, run.Tap.StalledWritesClosed);
        Assert.Equal([CaptureDegradationKind.ConnectionClosedMidMessage], degradations.Select(each => each.Kind));
        await run.Tap.DisposeAsync();
        lock (log)
            Assert.Contains(log, line => line.Contains("tcp-tap stopped (", StringComparison.Ordinal)
                && line.Contains(", 1 connections closed mid-message, ", StringComparison.Ordinal)
                && line.Contains(", 0 stuck connections reaped, ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_stop_line_names_the_connections_the_reaper_closed()
    {
        var log = new List<string>();
        var clock = new FakeTimeProvider();
        var decoder = new OneCommandDecoder();
        await using var run = await StartAsync(log, clock, o => o.ReapStuckConnectionsAfter = TimeSpan.FromSeconds(3), _ => decoder);
        await using var pair = await run.ConnectAsync();

        await pair.Client.GetStream().WriteAsync("cmd"u8.ToArray());
        await ReceiveExactlyAsync(pair.Upstream, 3);
        Assert.True(await WaitUntilAsync(() => decoder.Seen is not null), "the command was never decoded");
        clock.Advance(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await WaitUntilAsync(() => run.Tap.StuckConnectionsReaped == 1), "the command was never reaped");

        await run.Tap.DisposeAsync();
        lock (log)
            Assert.Contains(log, line => line.Contains("tcp-tap stopped (", StringComparison.Ordinal)
                && line.Contains(", 0 connections closed mid-message, ", StringComparison.Ordinal)
                && line.Contains(", 1 stuck connections reaped, ", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ helpers

    private static TcpTapOptions Options(int forwardPort) => new()
    {
        ListenPort = 0,
        ListenHost = "127.0.0.1",
        ForwardHost = "127.0.0.1",
        ForwardPort = forwardPort,
        CallerName = "app",
        ServiceName = "store",
        Sink = new RecordingSink(),
        DecoderFactory = _ => new OneCommandDecoder(),
        HalfClosedTimeout = Limit,
        StalledWriteTimeout = Limit,
    };

    private static async Task<TapRun> StartAsync(
        List<string> log, TimeProvider? clock, Action<TcpTapOptions>? configure = null,
        Func<TcpTapConnectionContext, IProtocolDecoder>? decoder = null, Func<bool>? debugger = null)
    {
        var upstream = LoopbackPort.Listening();
        var options = Options(((IPEndPoint)upstream.LocalEndpoint).Port);
        options.Log = line => { lock (log) log.Add(line); };
        if (decoder is not null)
            options.DecoderFactory = decoder;
        configure?.Invoke(options);
        var tap = new TcpTapCore(options)
        {
            Clock = clock ?? TimeProvider.System,
            // Every fact says whether a debugger is attached, so the suite passes under one too.
            DebuggerAttached = debugger ?? (static () => false),
        };
        await tap.StartAsync();
        return new TapRun(tap, upstream);
    }

    /// <summary>A tap and the listener it forwards to.</summary>
    private sealed class TapRun(TcpTapCore tap, TcpListener upstream) : IAsyncDisposable
    {
        public TcpTapCore Tap { get; } = tap;

        /// <summary>A client through the tap, and the upstream's end of the pair.</summary>
        public async Task<RawPair> ConnectAsync(int? clientReceiveBuffer = null, int? upstreamReceiveBuffer = null)
        {
            if (upstreamReceiveBuffer is { } upstreamSize)
                upstream.Server.ReceiveBufferSize = upstreamSize; // an accepted socket takes the listener's
            var client = new TcpClient();
            if (clientReceiveBuffer is { } size)
                client.ReceiveBufferSize = size; // small, so a reply it stops reading backs up into the tap's writes
            await client.ConnectAsync(IPAddress.Loopback, Tap.BoundPort);
            var accepted = await upstream.AcceptSocketAsync().WaitAsync(Patience);
            return new RawPair(client, accepted);
        }

        public async ValueTask DisposeAsync()
        {
            await Tap.DisposeAsync();
            upstream.Stop();
        }
    }

    private sealed class RawPair(TcpClient client, Socket upstream) : IAsyncDisposable
    {
        public TcpClient Client { get; } = client;

        public Socket Upstream { get; } = upstream;

        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            Upstream.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Reports one command as unanswered from the time its first bytes arrived until a reply comes.</summary>
    private sealed class OneCommandDecoder : IProtocolDecoder
    {
        private readonly object _gate = new();
        private DateTimeOffset? _oldest;
        private DateTimeOffset? _seen;

        public DateTimeOffset? Seen
        {
            get { lock (_gate) return _seen; }
        }

        public DateTimeOffset? OldestUnansweredSince
        {
            get { lock (_gate) return _oldest; }
        }

        public void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
        {
            lock (_gate)
            {
                _seen ??= timestamp;
                _oldest ??= timestamp;
            }
        }

        public void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
        {
            lock (_gate)
                _oldest = null;
        }

        public void OnConnectionClosed() { }

        public void Dispose() { }
    }

    /// <summary>
    /// A clock whose wall time can jump while its monotonic time does not: everything but <see cref="GetUtcNow"/> is the
    /// fake clock's (a subclass of <see cref="FakeTimeProvider"/> cannot do this: its timestamp reads its wall time).
    /// </summary>
    private sealed class JumpingClock(FakeTimeProvider inner) : TimeProvider
    {
        public TimeSpan Jump { get; set; }

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() + Jump;

        public override long GetTimestamp() => inner.GetTimestamp();

        public override long TimestampFrequency => inner.TimestampFrequency;

        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            inner.CreateTimer(callback, state, dueTime, period);
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

    private static async Task SendQuietlyAsync(Socket socket, byte[] bytes)
    {
        try
        {
            await socket.SendAsync(bytes);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // The tap closed the pair, as the fact means it to.
        }
    }

    private static async Task ReceiveExactlyAsync(Socket socket, int count)
    {
        var buffer = new byte[count];
        var received = 0;
        using var patience = new CancellationTokenSource(Patience);
        while (received < count)
        {
            var read = await socket.ReceiveAsync(buffer.AsMemory(received), SocketFlags.None, patience.Token);
            if (read == 0)
                throw new EndOfStreamException($"the connection ended after {received} of {count} bytes");
            received += read;
        }
    }

    /// <summary>Whether the socket's peer has closed it: an end of stream or a reset, within the patience.</summary>
    private static async Task<bool> EndedAsync(Socket socket)
    {
        var buffer = new byte[64 * 1024];
        using var patience = new CancellationTokenSource(Patience);
        try
        {
            while (await socket.ReceiveAsync(buffer, SocketFlags.None, patience.Token) > 0)
            {
            }

            return true;
        }
        catch (SocketException)
        {
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The active entry of <paramref name="phase"/>, once there is one.</summary>
    private static async Task<StallWatch.Entry> EntryAsync(TcpTapCore tap, string phase)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            if (tap.Watch?.Entries.FirstOrDefault(each => each.Phase == phase && each.IsActive) is { } entry)
                return entry;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"no {phase} entry appeared");
            await Task.Delay(20);
        }
    }

    /// <summary>
    /// The active entry of <paramref name="phase"/>, once its stamp has stopped moving: the transfer has stalled. For the
    /// pair's write entry the stamp is the mark of a pending write, one per write, so a write still pending is stalled.
    /// </summary>
    private static async Task<StallWatch.Entry> StalledAsync(TcpTapCore tap, string phase)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            var entry = await EntryAsync(tap, phase);
            var stamp = Stamp(entry);
            await Task.Delay(200);
            if (entry.IsActive && stamp is not null && Stamp(entry) == stamp)
                return entry;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"the {phase} never stalled");
        }
    }

    private static long? Stamp(StallWatch.Entry entry) => entry.Phase == TcpTapCore.WritePhase
        ? PendingWrites(entry).Select(mark => (long?)mark).FirstOrDefault()
        : entry.LastProgress;

    /// <summary>The marks of the writes a pair's write entry sees pending: when each began.</summary>
    private static IEnumerable<long> PendingWrites(StallWatch.Entry entry)
    {
        var limits = (TcpTapCore.PairLimits)entry.Subject!;
        foreach (var direction in new[] { TapDirection.ClientToServer, TapDirection.ServerToClient })
        {
            if (limits.WritePendingSince(direction) is { } mark)
                yield return mark;
        }
    }

    /// <summary>The marks of every write pending in the tap's pairs.</summary>
    private static IEnumerable<long> PendingWrites(TcpTapCore tap) =>
        (tap.Watch?.Entries ?? []).Where(each => each.Phase == TcpTapCore.WritePhase && each.IsActive).SelectMany(PendingWrites);

    /// <summary>Waits until the tap has recorded progress at the clock's current time, or the phase's entry has ended.</summary>
    private static async Task ProgressedAsync(TcpTapCore tap, string phase, FakeTimeProvider clock)
    {
        var now = clock.GetTimestamp();
        var deadline = DateTime.UtcNow + Patience;
        while (tap.Watch?.Entries.FirstOrDefault(each => each.Phase == phase && each.IsActive) is { } entry && entry.LastProgress < now)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"the {phase} made no progress at the current time");
            await Task.Delay(5);
        }
    }

    /// <summary>Waits until every pending write began at the clock's current time: none is left from before the last move.</summary>
    private static async Task WroteSinceAsync(TcpTapCore tap, FakeTimeProvider clock)
    {
        var now = clock.GetTimestamp();
        var deadline = DateTime.UtcNow + Patience;
        while (PendingWrites(tap).Any(mark => mark < now))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("a write begun before the clock's last move is still pending");
            await Task.Delay(5);
        }
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
}
