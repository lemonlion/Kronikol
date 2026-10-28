using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Reports;
using Microsoft.Extensions.Time.Testing;
using Tap = Kronikol.Extensions.ProxyTap.ProxyTap;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// A caller that stalls in its request body, or in reading its answer, is cut once it has moved nothing for
/// <see cref="ProxyTapOptions.CallerStallTimeout"/> (plans/TAP_CALLER_TIME_LIMITS_PLAN.md R1, #173). Unless a fact says it
/// runs on the real clock, it drives the limit with a fake one and moves it in step with the I/O, each step ordered
/// through the watch's list of entries: a steady fact moves the clock only once the tap has recorded progress at the
/// current time, and a stall fact only once the entry exists and its stamp has stopped moving.
/// </summary>
public class ProxyTapStallTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PastTheLimit = Limit + TimeSpan.FromSeconds(1);
    private const int EightMegabytes = 8 * 1024 * 1024;
    private const int Piece = 16 * 1024;

    public enum BodyKind
    {
        ContentLength,
        Chunked,
        NoByte,
    }

    private static (int, byte[], string?, string?, Dictionary<string, string>?) Ok(HttpListenerRequest _, byte[] __) =>
        (200, "ok"u8.ToArray(), "text/plain", null, null);

    // Answers with the length of the body it was sent, so a fact sees the forward carried all of it.
    private static (int, byte[], string?, string?, Dictionary<string, string>?) Length(HttpListenerRequest _, byte[] body) =>
        (200, Encoding.ASCII.GetBytes(body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)), "text/plain", null, null);

    private static string StalledRequest(BodyKind kind) => kind switch
    {
        BodyKind.ContentLength => "POST /stalled HTTP/1.1\r\nHost: localhost\r\nContent-Length: 100\r\n\r\nhello",
        BodyKind.Chunked => "POST /stalled HTTP/1.1\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n",
        _ => "POST /stalled HTTP/1.1\r\nHost: localhost\r\nContent-Length: 100\r\n\r\n",
    };

    [Theory]
    [InlineData(BodyKind.ContentLength, false)]
    [InlineData(BodyKind.Chunked, false)]
    [InlineData(BodyKind.NoByte, false)]
    [InlineData(BodyKind.ContentLength, true)]
    [InlineData(BodyKind.Chunked, true)]
    [InlineData(BodyKind.NoByte, true)]
    public async Task A_caller_that_stalls_in_its_body_is_cut_and_not_forwarded(BodyKind kind, bool realClock)
    {
        await using var upstream = new StubUpstream(Ok);
        var log = new List<string>();
        var clock = realClock ? null : new FakeTimeProvider();
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), log, clock);

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, StalledRequest(kind));
        if (clock is not null)
        {
            await StalledAsync(tap, Tap.BodyPhase);
            clock.Advance(PastTheLimit);
        }

        var heard = await caller.HearAsync(Patience);
        Assert.True(heard.LetGo, $"the stalled caller was held: {heard}");
        if (OperatingSystem.IsWindows())
        {
            // http.sys writes nothing on an abort: the connection is reset.
            Assert.Empty(heard.Bytes);
            Assert.StartsWith("a reset", heard.Ending);
        }
        else
        {
            Assert.StartsWith("HTTP/1.1 408 ", heard.FirstLine);
            Assert.Contains("Connection: close", heard.Head, StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the handler never ended");
        Assert.Equal(1, tap.CallerTimeouts);
        Assert.Equal(0, tap.ForwardFailures);
        lock (upstream.Seen)
            Assert.Empty(upstream.Seen);
        // The line is written once the caller is let go, on the cut's thread, which may come after the handler has ended.
        Assert.True(await WaitUntilAsync(() =>
        {
            lock (log)
                return log.Any(line => line.Contains("proxy-tap caller stalled", StringComparison.Ordinal)
                    && line.Contains("sending its request body; not forwarded", StringComparison.Ordinal));
        }), "the cut was not logged");
        var diagnostic = Assert.Single(tap.Diagnostics(), entry => entry.Message.Contains("stalled for CallerStallTimeout", StringComparison.Ordinal));
        Assert.Equal(DiagnosticKind.CaptureDegraded, diagnostic.Kind);
        Assert.Contains("1 caller(s) stalled for CallerStallTimeout (00:00:01) and were cut: 1 sending a request body (not forwarded), 0 reading an answer", diagnostic.Message, StringComparison.Ordinal);
        await tap.DisposeAsync();
        lock (log)
            Assert.Contains(log, line => line.Contains("proxy-tap stopped (", StringComparison.Ordinal)
                && line.EndsWith(", 0 forward failures, 1 caller timeouts)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_body_read_that_ends_after_the_limit_fired_is_not_forwarded()
    {
        await using var upstream = new StubUpstream(Ok);
        var clock = new FakeTimeProvider();
        using var hold = new ManualResetEventSlim();
        StallWatch.Entry? body = null;
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), [], clock, prepare: t =>
        {
            // The body ends, whole and normally, once the limit has fired and before its action has run: the read the
            // limit raced, which the swap on the entry gives to the limit.
            t.WrapRequestBody = _ => new EndsWhenStream(() => body?.HasFired == true);
        });
        tap.Watch!.BeforeAct = _ => hold.Wait(Patience);

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "POST /raced HTTP/1.1\r\nHost: localhost\r\nContent-Length: 5\r\n\r\nhello");
        body = await StalledAsync(tap, Tap.BodyPhase);
        clock.Advance(PastTheLimit);

        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0 || Seen(upstream) > 0), "the handler neither ended nor forwarded");
        hold.Set(); // the limit's action, held until the handler has read the end of the body
        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the handler never ended");
        Assert.Equal(0, Seen(upstream));
        Assert.Equal(1, tap.CallerTimeouts);
        Assert.Equal(0, tap.ForwardFailures);
    }

    private static int Seen(StubUpstream upstream)
    {
        lock (upstream.Seen)
            return upstream.Seen.Count;
    }

    [Fact]
    public async Task A_caller_that_stops_reading_its_answer_is_cut_and_recorded()
    {
        var answer = new byte[EightMegabytes];
        await using var upstream = new StubUpstream((_, _) => (200, answer, "application/octet-stream", null, null));
        var registry = new InFlightIdentityRegistry();
        var sink = new ListSink();
        var log = new List<string>();
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(upstream.BaseUri, sink, log, clock, o => o.InFlightRegistry = registry);

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /big HTTP/1.1\r\nHost: localhost\r\n\r\n", receiveBuffer: Piece);
        await StalledAsync(tap, Tap.AnswerPhase);
        clock.Advance(PastTheLimit);

        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the handler never ended: the caller is still held");
        Assert.Equal(1, tap.CallerTimeouts);
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Equal(1, tap.RequestsHandled);
        lock (sink.Logs)
            Assert.NotEmpty(sink.Logs); // the exchange is recorded, as one whose caller hangs up mid-answer
        Assert.Null(registry.MostRecentFor("api"));
        Assert.True(await WaitUntilAsync(() =>
        {
            lock (log)
                return log.Any(line => line.Contains("proxy-tap caller stalled", StringComparison.Ordinal)
                    && line.Contains("reading its answer; closed", StringComparison.Ordinal));
        }), "the cut was not logged");
        Assert.Contains(tap.Diagnostics(), entry => entry.Kind == DiagnosticKind.CaptureDegraded
            && entry.Message.Contains("0 sending a request body (not forwarded), 1 reading an answer (recorded when captured)", StringComparison.Ordinal));
        var heard = await caller.HearAsync(Patience);
        Assert.True(heard.LetGo, $"the caller was held: {heard}");
        Assert.True(heard.Bytes.Length < answer.Length, $"the whole answer arrived: {heard}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_slow_steady_body_is_never_cut(bool chunked)
    {
        const int pieces = 40;
        await using var upstream = new StubUpstream(Length);
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), [], clock);

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, chunked
            ? "POST /steady HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\nTransfer-Encoding: chunked\r\n\r\n"
            : $"POST /steady HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\nContent-Length: {pieces * Piece}\r\n\r\n");
        var stream = caller.Client.GetStream();
        // Until the tap has read the head there is no body to make progress, so the clock must not move before it has
        // (OtlpTapStallTests' slow export, which a loaded runner failed that way).
        await EntryAsync(tap, Tap.BodyPhase);
        var piece = new byte[Piece];
        for (var i = 0; i < pieces; i++)
        {
            if (chunked)
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"{Piece:X}\r\n"));
            await stream.WriteAsync(piece);
            if (chunked)
                await stream.WriteAsync("\r\n"u8.ToArray());
            await ProgressedAsync(tap, Tap.BodyPhase, clock);
            clock.Advance(TimeSpan.FromMilliseconds(600));
        }

        if (chunked)
            await stream.WriteAsync("0\r\n\r\n"u8.ToArray());

        var heard = await caller.HearAsync(Patience);
        Assert.StartsWith("HTTP/1.1 200 ", heard.FirstLine);
        Assert.Equal((pieces * Piece).ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.ASCII.GetString(heard.Body));
        Assert.Equal(0, tap.CallerTimeouts);
    }

    [Fact]
    public async Task A_slow_steady_reader_is_never_cut()
    {
        var answer = new byte[EightMegabytes];
        new Random(173).NextBytes(answer);
        await using var upstream = new StubUpstream((_, _) => (200, answer, "application/octet-stream", null, null));
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), [], clock);

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /big HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n", receiveBuffer: Piece);
        var stream = caller.Client.GetStream();
        await EntryAsync(tap, Tap.AnswerPhase);
        using var received = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            // A mebibyte at a time, then the clock moves 0.6 s: about 13 KB a second of fake time, far under the limit.
            var target = received.Length + (1024 * 1024);
            var read = 1;
            while (received.Length < target && (read = await stream.ReadAsync(buffer)) > 0)
                received.Write(buffer, 0, read);
            if (read == 0)
                break;
            await ProgressedAsync(tap, Tap.AnswerPhase, clock);
            clock.Advance(TimeSpan.FromMilliseconds(600));
        }

        var all = received.ToArray();
        var head = Encoding.ASCII.GetString(all, 0, Math.Min(all.Length, 512));
        Assert.StartsWith("HTTP/1.1 200 ", head);
        var bodyStart = head.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        Assert.Equal(answer, all[bodyStart..]);
        Assert.Equal(0, tap.CallerTimeouts);
    }

    [Fact]
    public async Task An_answer_cut_by_the_limit_ends_at_once()
    {
        // The real clock: the cut comes at most a beat after the limit, and the tap lets the caller go at once, whatever
        // the caller does not read. An answer sent chunked would hold it on Linux, whose abort waits for the caller.
        var answer = new byte[EightMegabytes];
        await using var upstream = new StubUpstream((_, _) => (200, answer, "application/octet-stream", null, null));
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), [], clock: null);

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /big HTTP/1.1\r\nHost: localhost\r\n\r\n", receiveBuffer: Piece);
        await EntryAsync(tap, Tap.AnswerPhase);
        var started = DateTime.UtcNow;
        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the handler never ended: the caller is still held");
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.Zero, Limit + StallWatchBeat + TimeSpan.FromSeconds(2));
        Assert.Equal(1, tap.CallerTimeouts);
    }

    [Fact]
    public async Task A_slow_upstream_is_not_the_callers_stall()
    {
        using var release = new ManualResetEventSlim();
        await using var upstream = new StubUpstream((_, _) =>
        {
            release.Wait(Patience);
            return (200, "slow"u8.ToArray(), "text/plain", null, null);
        });
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), [], clock);

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "POST /slow HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\nContent-Length: 5\r\n\r\nhello");
        Assert.True(await WaitUntilAsync(() => { lock (upstream.Seen) return upstream.Seen.Count == 1; }), "the forward never reached the upstream");
        Assert.DoesNotContain(tap.Watch!.Entries, entry => entry.IsActive); // the forward is ForwardTimeout's, not the caller's
        clock.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(300);
        release.Set();

        var heard = await caller.HearAsync(Patience);
        Assert.StartsWith("HTTP/1.1 200 ", heard.FirstLine);
        Assert.Equal(0, tap.CallerTimeouts);
    }

    [Fact]
    public async Task The_limit_and_the_answer_racing_end_it_once()
    {
        var port = StubUpstream.FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        for (var round = 0; round < 1000; round++)
        {
            using var caller = await RawCaller.SendAsync(port, "GET /race HTTP/1.1\r\nHost: localhost\r\n\r\n");
            var context = await listener.GetContextAsync().WaitAsync(Patience);
            var answer = new Tap.CallerAnswer(context.Response) { StatusIfUnclosed = 503 };
            Assert.True(answer.TryBeginForward());
            Assert.True(answer.TryBeginAnswer());
            using var start = new Barrier(3);
            var won = await Task.WhenAll(
                Task.Run(() => { start.SignalAndWait(); return answer.TimeOut(null); }),
                Task.Run(() => { start.SignalAndWait(); return answer.Close(); }),
                Task.Run(() => { start.SignalAndWait(); return answer.EndUnclosed(); }));
            Assert.Equal(1, won.Count(each => each));
            Assert.Equal(won[1], answer.IsClosed);
            Assert.Equal(won[0], answer.IsTimedOut);
            Assert.Equal(won[2], answer.IsEnded);
        }
    }

    [Fact]
    public async Task A_cut_is_counted_before_the_caller_is_let_go()
    {
        // A host reads CallerTimeouts, or Diagnostics(), as soon as the caller is gone: the count must already be there.
        // What the cut records runs before the response is aborted, while the caller has heard nothing; once it heard the
        // 408 (or, on Windows, the reset), the poll would see it.
        var port = StubUpstream.FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        using var caller = await RawCaller.SendAsync(port, StalledRequest(BodyKind.ContentLength));
        var context = await listener.GetContextAsync().WaitAsync(Patience);
        var answer = new Tap.CallerAnswer(context.Response) { StatusIfUnclosed = 503 };

        bool? heardBeforeTheCount = null;
        Assert.True(answer.TimeOut(408, () => heardBeforeTheCount = caller.Client.Client.Poll(300_000, System.Net.Sockets.SelectMode.SelectRead)));

        Assert.False(heardBeforeTheCount, "the caller was let go before the cut was recorded");
        Assert.True(answer.IsTimedOut);
        var heard = await caller.HearAsync(Patience);
        Assert.True(heard.LetGo, $"the caller was held: {heard}");
    }

    [Fact]
    public async Task A_stall_the_tap_cuts_is_counted_before_its_caller_is_let_go()
    {
        // Through the tap's own cut: the fact above holds CallerAnswer.TimeOut to its order, and a cut that counted after
        // the abort, as 4.26.0's did, passed every fact on an idle machine.
        await using var upstream = new StubUpstream(Ok);
        var clock = new FakeTimeProvider();
        var countedAtLetGo = new StrongBox<long>(-1);
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), [], clock,
            prepare: t => t.BeforeCallerLetGo = () => Interlocked.CompareExchange(ref countedAtLetGo.Value, t.CallerTimeouts, -1));

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, StalledRequest(BodyKind.ContentLength));
        await StalledAsync(tap, Tap.BodyPhase);
        clock.Advance(PastTheLimit);

        Assert.True((await caller.HearAsync(Patience)).LetGo, "the stalled caller was held");
        Assert.Equal(1, Interlocked.Read(ref countedAtLetGo.Value));
    }

    [Fact]
    public async Task No_limit_applies_while_a_debugger_is_attached()
    {
        await using var upstream = new StubUpstream(Ok);
        var clock = new FakeTimeProvider();
        var attached = new StrongBox<bool>(true);
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), [], clock, debugger: () => Volatile.Read(ref attached.Value));

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, StalledRequest(BodyKind.ContentLength));
        await StalledAsync(tap, Tap.BodyPhase);
        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(500);
        Assert.Equal(0, tap.CallerTimeouts);
        Assert.Contains(tap.Watch!.Entries, entry => entry.Phase == Tap.BodyPhase && entry.IsActive);

        Volatile.Write(ref attached.Value, false);
        clock.Advance(StallWatchBeat);
        Assert.True(await WaitUntilAsync(() => tap.CallerTimeouts == 1), "a stall that outlived the detach was not cut at the next beat");
    }

    [Fact]
    public void The_stall_timeout_is_100_seconds_by_default() =>
        Assert.Equal(TimeSpan.FromSeconds(100), new ProxyTapOptions().CallerStallTimeout);

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(-1000)]
    public async Task A_stall_timeout_of_zero_or_less_is_refused(int milliseconds)
    {
        await using var tap = new Tap(new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = new Uri("http://localhost:1"),
            CallerName = "web",
            ServiceName = "api",
            CallerStallTimeout = TimeSpan.FromMilliseconds(milliseconds),
        });
        var thrown = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => tap.StartAsync());
        Assert.Equal(nameof(ProxyTapOptions.CallerStallTimeout), thrown.ParamName);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("infinite")]
    [InlineData("max")]
    public async Task A_stall_timeout_of_none_never_cuts(string value)
    {
        TimeSpan? limit = value switch
        {
            "null" => null,
            "infinite" => Timeout.InfiniteTimeSpan,
            _ => TimeSpan.MaxValue,
        };
        await using var upstream = new StubUpstream(Ok);
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), [], clock, o => o.CallerStallTimeout = limit);

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, StalledRequest(BodyKind.ContentLength));
        Assert.True(await WaitUntilAsync(() => tap.InFlight == 1), "the tap never took the request");
        if (value == "max")
            await StalledAsync(tap, Tap.BodyPhase);
        else
            Assert.Null(tap.Watch); // no limit, no watch
        clock.Advance(TimeSpan.FromHours(1));
        await Task.Delay(500);

        Assert.Equal(0, tap.CallerTimeouts);
        Assert.Equal(1, tap.InFlight);
    }

    [Fact]
    public async Task Keep_alive_requests_leave_no_entry_behind()
    {
        await using var upstream = new StubUpstream(Ok);
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), [], clock);

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "");
        var stream = caller.Client.GetStream();
        for (var i = 0; i < 20; i++)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"POST /kept/{i} HTTP/1.1\r\nHost: localhost\r\nContent-Length: 5\r\n\r\nhello"));
            var status = await ReadOneAnswerAsync(stream);
            Assert.StartsWith("HTTP/1.1 200 ", status);
            Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), $"exchange {i} never ended");
            Assert.Empty(tap.Watch!.Entries);
            clock.Advance(PastTheLimit);
        }

        Assert.Equal(0, tap.CallerTimeouts);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(502)]
    public async Task An_exchange_answered_by_the_tap_leaves_no_entry_behind(int status)
    {
        await using var upstream = new StubUpstream(Ok);
        var clock = new FakeTimeProvider();
        var forward = status == 502 ? new Uri($"http://127.0.0.1:{StubUpstream.FreePort()}") : upstream.BaseUri;
        await using var tap = await StartAsync(forward, new ListSink(), [], clock, prepare: t =>
        {
            if (status == 400)
                t.WrapRequestBody = _ => new ThrowingStream();
        });

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "POST /answered HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\nContent-Length: 5\r\n\r\nhello");
        var heard = await caller.HearAsync(Patience);
        if (status == 400 && OperatingSystem.IsWindows())
            Assert.True(heard.LetGo, $"the caller was held: {heard}"); // http.sys writes nothing on an abort: a reset
        else
            Assert.StartsWith($"HTTP/1.1 {status} ", heard.FirstLine);
        Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the exchange never ended");
        Assert.Empty(tap.Watch!.Entries);
        clock.Advance(PastTheLimit);
        await Task.Delay(200);
        Assert.Equal(0, tap.CallerTimeouts);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("answer")]
    public async Task A_read_completing_as_the_limit_fires_is_forwarded_or_cut_never_both(string phase)
    {
        var answer = new byte[phase == "answer" ? EightMegabytes : 2];
        new Random(173).NextBytes(answer);
        await using var upstream = new StubUpstream((_, body) => (200, phase == "answer" ? answer : Encoding.ASCII.GetBytes(body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)), "application/octet-stream", null, null));
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(upstream.BaseUri, new ListSink(), [], clock);

        using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, phase == "body"
            ? "POST /raced HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\nContent-Length: 10\r\n\r\nhello"
            : "GET /raced HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n", receiveBuffer: Piece);
        var entry = await StalledAsync(tap, phase == "body" ? Tap.BodyPhase : Tap.AnswerPhase);
        Task<byte[]>? reading = null;
        tap.Watch!.BeforeFire = due =>
        {
            if (due != entry)
                return;
            // The transfer completes in the gap between the heartbeat's look and its swap, and ends its entry first.
            if (phase == "body")
                caller.Client.GetStream().Write("world"u8);
            else
                reading = ReadToEndAsync(caller);
            SpinWait.SpinUntil(() => !due.IsActive, Patience);
        };
        clock.Advance(PastTheLimit);

        byte[] all;
        if (phase == "body")
        {
            var heard = await caller.HearAsync(Patience);
            Assert.StartsWith("HTTP/1.1 200 ", heard.FirstLine);
            Assert.Equal("10", Encoding.ASCII.GetString(heard.Body));
        }
        else
        {
            Assert.True(await WaitUntilAsync(() => reading is not null), "the heartbeat never reached the entry");
            all = await reading!.WaitAsync(Patience);
            var head = Encoding.ASCII.GetString(all, 0, Math.Min(all.Length, 512));
            Assert.StartsWith("HTTP/1.1 200 ", head);
            Assert.Equal(answer, all[(head.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..]);
        }

        Assert.Equal(0, tap.CallerTimeouts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16_383)]
    [InlineData(16_384)]
    [InlineData(16_385)]
    [InlineData(32_767)]
    [InlineData(32_768)]
    [InlineData(32_769)]
    [InlineData(EightMegabytes)]
    public void The_answer_is_cut_into_pieces_of_16_KiB_and_a_longer_last(int length)
    {
        var pieces = new List<int>();
        for (var offset = 0; offset < length;)
        {
            var piece = Tap.PieceLength(length - offset);
            pieces.Add(piece);
            offset += piece;
        }

        Assert.Equal(length, pieces.Sum());
        if (length == 0)
        {
            Assert.Empty(pieces);
            return;
        }

        Assert.All(pieces.SkipLast(1), piece => Assert.Equal(Piece, piece));
        if (length < Piece)
            Assert.Equal(length, pieces[^1]);
        else
            Assert.InRange(pieces[^1], Piece, (2 * Piece) - 1);
    }

    // ------------------------------------------------------------------ helpers

    private static TimeSpan StallWatchBeat => StallWatch.Beat;

    private static async Task<Tap> StartAsync(
        Uri forward, ListSink sink, List<string> log, TimeProvider? clock, Action<ProxyTapOptions>? configure = null,
        Action<Tap>? prepare = null, Func<bool>? debugger = null)
    {
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = forward,
            CallerName = "web",
            ServiceName = "api",
            Sink = sink,
            CaptureUnattributedRequests = true,
            FallbackTestId = "stalling",
            CallerStallTimeout = Limit,
            Log = line => { lock (log) log.Add(line); },
        };
        configure?.Invoke(options);
        var tap = new Tap(options)
        {
            Clock = clock ?? TimeProvider.System,
            // Every fact says whether a debugger is attached, so the suite passes under one too.
            DebuggerAttached = debugger ?? (static () => false),
        };
        prepare?.Invoke(tap);
        await tap.StartAsync();
        return tap;
    }

    /// <summary>The active entry of <paramref name="phase"/>, once there is one.</summary>
    private static async Task<StallWatch.Entry> EntryAsync(Tap tap, string phase)
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

    /// <summary>The active entry of <paramref name="phase"/>, once its stamp has stopped moving: the transfer has stalled.</summary>
    private static async Task<StallWatch.Entry> StalledAsync(Tap tap, string phase)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            var entry = await EntryAsync(tap, phase);
            var stamp = entry.LastProgress;
            await Task.Delay(200);
            if (entry.IsActive && entry.LastProgress == stamp)
                return entry;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"the {phase} never stalled");
        }
    }

    /// <summary>Waits until the tap has recorded progress at the clock's current time, or the phase's transfer has ended.</summary>
    private static async Task ProgressedAsync(Tap tap, string phase, FakeTimeProvider clock)
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

    /// <summary>Reads one answer on a kept-alive connection: its head, then as many bytes as its Content-Length says.</summary>
    private static async Task<string> ReadOneAnswerAsync(Stream stream)
    {
        var head = new StringBuilder();
        var one = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(one) == 0)
                throw new EndOfStreamException($"the connection ended after: {head}");
            head.Append((char)one[0]);
        }

        var text = head.ToString();
        var lengthLine = text.Split("\r\n").First(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var remaining = int.Parse(lengthLine["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        var body = new byte[remaining];
        await stream.ReadExactlyAsync(body);
        return text.Split("\r\n")[0];
    }

    private static Task<byte[]> ReadToEndAsync(RawCaller caller) => Task.Run(async () =>
    {
        using var all = new MemoryStream();
        await caller.Client.GetStream().CopyToAsync(all);
        return all.ToArray();
    });

    /// <summary>A request body that ends, with no bytes, once <paramref name="ended"/> says so.</summary>
    private sealed class EndsWhenStream(Func<bool> ended) : Stream
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
            while (!ended())
                await Task.Delay(10, cancellationToken);
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

    /// <summary>A request body whose read throws, as one whose caller hangs up mid-body: the 400 path.</summary>
    private sealed class ThrowingStream : Stream
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

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("the caller hung up");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("the caller hung up"));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException<int>(new IOException("the caller hung up"));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
