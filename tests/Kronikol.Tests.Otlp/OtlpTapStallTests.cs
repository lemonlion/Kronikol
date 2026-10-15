using System.Collections;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// An exporter that stalls in a request head or body, or in reading its answer, is closed once it has moved nothing for
/// <see cref="OtlpTapOptions.CallerStallTimeout"/>, and a connection waiting for a request's first byte once it has waited
/// <see cref="OtlpTapOptions.KeepAliveTimeout"/> (plans/TAP_CALLER_TIME_LIMITS_PLAN.md R2, #173). Unless a fact says it
/// runs on the real clock, it drives the limits with a fake one and moves it in step with the I/O, each step ordered
/// through the watch's list of entries, as <c>ProxyTapStallTests</c> does.
/// </summary>
public class OtlpTapStallTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PastTheLimit = Limit + TimeSpan.FromSeconds(1);
    private const int EightMegabytes = 8 * 1024 * 1024;
    private const int Piece = 16 * 1024;

    private static readonly byte[] Export = OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv);

    public enum BodyKind
    {
        ContentLength,
        Chunked,
        NoByte,
    }

    public enum InsideBody
    {
        OverCapDiscard,
        ChunkSizeLine,
        ChunkLineBreak,
        Trailers,
    }

    private static string Head(string framing) =>
        $"POST /v1/traces HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\n{framing}\r\n";

    private static byte[] WholeExport(bool close = false) =>
        [.. Encoding.ASCII.GetBytes(Head((close ? "Connection: close\r\n" : "") + $"Content-Length: {Export.Length}\r\n")), .. Export];

    private static string StalledBody(BodyKind kind) => kind switch
    {
        BodyKind.ContentLength => Head("Content-Length: 100\r\n") + "hello",
        BodyKind.Chunked => Head("Transfer-Encoding: chunked\r\n") + "5\r\nhello\r\n",
        _ => Head("Content-Length: 100\r\n"),
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stalled_head_is_answered_408_and_closed(bool realClock)
    {
        var log = new List<string>();
        var clock = realClock ? null : new FakeTimeProvider();
        await using var tap = await StartAsync(new ListSink(), log, clock);

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync("POST /v1/traces HTTP/1.1\r\nHost: loc"); // the rest of the head never comes
        if (clock is not null)
        {
            await StalledAsync(tap, OtlpTap.SendingPhase);
            clock.Advance(PastTheLimit);
        }

        AssertTimedOut(await exporter.HearAsync(Patience));
        Assert.True(await WaitUntilAsync(() => tap.CallerTimeouts == 1), "the stall was not counted");
        Assert.Equal(0, tap.RequestsReceived);
        lock (log)
            Assert.Contains(log, line => line.Contains("otlp-tap exporter stalled 00:00:01 sending an export; not received, answered 408 and closed", StringComparison.Ordinal));
        var diagnostic = Assert.Single(tap.Diagnostics(), entry => entry.Message.Contains("stalled for CallerStallTimeout", StringComparison.Ordinal));
        Assert.Equal(DiagnosticKind.CaptureDegraded, diagnostic.Kind);
        Assert.Contains(
            "1 exporter connection(s) stalled for CallerStallTimeout (00:00:01) and were closed: 1 sending an export (not received), 0 reading an answer (the export already received)",
            diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BodyKind.ContentLength)]
    [InlineData(BodyKind.Chunked)]
    [InlineData(BodyKind.NoByte)]
    public async Task A_stalled_body_is_answered_408_and_not_received(BodyKind kind)
    {
        var sink = new ListSink();
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(sink, [], clock);

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync(StalledBody(kind));
        await StalledAsync(tap, OtlpTap.SendingPhase);
        clock.Advance(PastTheLimit);

        AssertTimedOut(await exporter.HearAsync(Patience));
        Assert.Equal(1, tap.CallerTimeouts);
        Assert.Equal(0, tap.RequestsReceived);
        Assert.Equal(0, tap.SpansReceived);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public async Task An_idle_keep_alive_connection_is_closed_quietly()
    {
        var log = new List<string>();
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(new ListSink(), log, clock);

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync(WholeExport());
        Assert.StartsWith("HTTP/1.1 200 ", await ReadOneAnswerAsync(exporter.Client.GetStream()));

        await AssertIdleThenClosedAsync(tap, exporter, clock, log);
    }

    [Fact]
    public async Task A_connection_that_never_sends_is_closed_after_the_keep_alive_limit()
    {
        var log = new List<string>();
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(new ListSink(), log, clock);

        using var exporter = await RawExporter.ConnectAsync(tap);

        await AssertIdleThenClosedAsync(tap, exporter, clock, log);
    }

    [Fact]
    public async Task A_slow_steady_export_is_received()
    {
        const int pieces = 40;
        await using var collector = new StubCollector();
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(new ListSink(), [], clock, o => o.ForwardBaseUri = collector.BaseUri);

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync(Head($"Content-Length: {pieces * Piece}\r\n"));
        var body = new byte[pieces * Piece];
        new Random(173).NextBytes(body);
        for (var i = 0; i < pieces; i++)
        {
            await exporter.SendAsync(body[(i * Piece)..((i + 1) * Piece)]);
            await ProgressedAsync(tap, OtlpTap.SendingPhase, clock);
            clock.Advance(TimeSpan.FromMilliseconds(600));
        }

        Assert.StartsWith("HTTP/1.1 200 ", await ReadOneAnswerAsync(exporter.Client.GetStream()));
        Assert.Equal(0, tap.CallerTimeouts);
        lock (collector.Seen)
            Assert.Equal(body, Assert.Single(collector.Seen).Body);
    }

    [Fact]
    public async Task An_exporter_that_reuses_its_connection_keeps_it()
    {
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(new ListSink(), [], clock);

        using var exporter = await RawExporter.ConnectAsync(tap);
        var stream = exporter.Client.GetStream();
        for (var i = 0; i < 3; i++)
        {
            await exporter.SendAsync(WholeExport());
            Assert.StartsWith("HTTP/1.1 200 ", await ReadOneAnswerAsync(stream));
            await EntryAsync(tap, OtlpTap.KeptAlivePhase);
            clock.Advance(PastTheLimit); // past CallerStallTimeout, inside KeepAliveTimeout
        }

        await Task.Delay(300);
        Assert.False(exporter.Client.Client.Poll(200_000, SelectMode.SelectRead), "the kept-alive connection was closed or written to");
        Assert.Equal(3, tap.RequestsReceived);
        Assert.Equal(0, tap.CallerTimeouts);
    }

    [Fact]
    public async Task Disposing_the_tap_is_not_a_time_limit()
    {
        var log = new List<string>();
        var clock = new FakeTimeProvider();
        var tap = await StartAsync(new ListSink(), log, clock);

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync(StalledBody(BodyKind.ContentLength));
        await StalledAsync(tap, OtlpTap.SendingPhase);
        await tap.DisposeAsync();
        clock.Advance(PastTheLimit);
        var heard = await exporter.HearAsync(Patience);

        Assert.DoesNotContain("408", heard.FirstLine, StringComparison.Ordinal);
        Assert.Equal(0, tap.CallerTimeouts);
        lock (log)
        {
            Assert.DoesNotContain(log, line => line.Contains("stalled", StringComparison.Ordinal));
            Assert.Contains(log, line => line.Contains("otlp-tap stopped (", StringComparison.Ordinal)
                && line.EndsWith(", 0 caller timeouts)", StringComparison.Ordinal));
        }
    }

    /// <summary>A value of the property's type that differs from a fresh <see cref="OtlpTapOptions"/>'s.</summary>
    private static object NonDefault(PropertyInfo property, object? fresh) => property.PropertyType switch
    {
        var t when t == typeof(int) && property.Name == nameof(OtlpTapOptions.ListenPort) => 54321,
        var t when t == typeof(int) || t == typeof(int?) => (fresh as int? ?? 0) + 17,
        // Registration validates the options, and a path must start with a slash.
        var t when t == typeof(string) && property.Name == nameof(OtlpTapOptions.TracesPath) => "/x/traces",
        var t when t == typeof(string) => "x-" + property.Name,
        var t when t == typeof(Uri) => new Uri("http://collector.example:4318/"),
        var t when t == typeof(bool) => !(bool)fresh!,
        var t when t.IsEnum => Enum.GetValues(t).Cast<object>().First(v => !v.Equals(fresh)),
        var t when t == typeof(TimeSpan?) => ((TimeSpan?)fresh ?? TimeSpan.Zero) + TimeSpan.FromSeconds(1),
        var t when t == typeof(IRequestResponseSink) => new ListSink(),
        var t when t == typeof(Action<string>) => new Action<string>(_ => { }),
        var t when t == typeof(Func<string, bool>) => new Func<string, bool>(_ => true),
        var t => throw new InvalidOperationException(
            $"No test value for OtlpTapOptions.{property.Name} ({t}); add one so the copy of the new option is checked."),
    };

    [Fact]
    public void Registering_taps_copies_every_option()
    {
        var fresh = new OtlpTapOptions();
        var source = new OtlpTapOptions();
        var properties = typeof(OtlpTapOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            var freshValue = property.GetValue(fresh);
            if (property.CanWrite)
            {
                property.SetValue(source, NonDefault(property, freshValue));
                Assert.False(Equals(freshValue, property.GetValue(source)), $"{property.Name} still has its default");
            }
            else if (property.GetValue(source) is Dictionary<string, string> map)
                map["x-" + property.Name] = "y";
            else if (property.GetValue(source) is HashSet<string> set)
                set.Add("x-" + property.Name);
            else
                Assert.Fail($"OtlpTapOptions.{property.Name} is get-only and not a collection the fact knows how to fill");
        }

        var services = new ServiceCollection();
        services.AddOtlpTapTestTracking([source]);
        using var provider = services.BuildServiceProvider();
        var copy = provider.GetRequiredService<OtlpTap>().Options;

        Assert.NotSame(source, copy);
        foreach (var property in properties)
        {
            var expected = property.GetValue(source);
            var actual = property.GetValue(copy);
            if (expected is IEnumerable items and not string)
                Assert.True(
                    items.Cast<object>().OrderBy(x => x.ToString()).SequenceEqual(((IEnumerable)actual!).Cast<object>().OrderBy(x => x.ToString())),
                    $"{property.Name} was not copied");
            else
                Assert.True(Equals(expected, actual), $"{property.Name} was not copied: expected {expected}, got {actual}");
        }
    }

    [Fact]
    public async Task A_slow_collector_is_not_the_exporters_stall()
    {
        using var collector = new OtlpTapHangUpTests.HeldCollector();
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(new ListSink(), [], clock, o => o.ForwardBaseUri = collector.BaseUri);

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync(WholeExport());
        await collector.HasExport.Task.WaitAsync(Patience);
        Assert.DoesNotContain(tap.Watch!.Entries, entry => entry.IsActive); // the forward is ForwardTimeout's, not the exporter's
        clock.Advance(PastTheLimit);
        await Task.Delay(300);
        collector.Answer();

        Assert.StartsWith("HTTP/1.1 200 ", await ReadOneAnswerAsync(exporter.Client.GetStream()));
        Assert.Equal(0, tap.CallerTimeouts);
    }

    [Theory]
    [InlineData(InsideBody.OverCapDiscard)]
    [InlineData(InsideBody.ChunkSizeLine)]
    [InlineData(InsideBody.ChunkLineBreak)]
    [InlineData(InsideBody.Trailers)]
    public async Task A_stall_inside_a_body_is_a_body_stall(InsideBody where)
    {
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(new ListSink(), [], clock, o => o.MaxRequestBytes = 1024);

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync(where switch
        {
            InsideBody.OverCapDiscard => Head("Content-Length: 65536\r\n") + new string('x', 2048),
            InsideBody.ChunkSizeLine => Head("Transfer-Encoding: chunked\r\n") + "5",
            InsideBody.ChunkLineBreak => Head("Transfer-Encoding: chunked\r\n") + "5\r\nhello",
            _ => Head("Transfer-Encoding: chunked\r\n") + "5\r\nhello\r\n0\r\nX-Trailer: a",
        });
        await StalledAsync(tap, OtlpTap.SendingPhase);
        clock.Advance(PastTheLimit);

        AssertTimedOut(await exporter.HearAsync(Patience));
        Assert.Equal(1, tap.CallerTimeouts);
        Assert.Equal(0, tap.PayloadsRejected);
        Assert.Equal(0, tap.RequestsReceived);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_relayed_answer_is_written_in_pieces(bool steady)
    {
        var answer = new string('x', EightMegabytes);
        await using var collector = new StubCollector(200, answer);
        var log = new List<string>();
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(new ListSink(), log, clock, o => o.ForwardBaseUri = collector.BaseUri);

        using var exporter = await RawExporter.ConnectAsync(tap, receiveBuffer: Piece);
        await exporter.SendAsync(WholeExport(close: true));
        if (!steady)
        {
            // The exporter reads nothing of its answer.
            await StalledAsync(tap, OtlpTap.AnsweringPhase);
            clock.Advance(PastTheLimit);

            Assert.True(await WaitUntilAsync(() => tap.CallerTimeouts == 1), "the exporter that stopped reading was not cut");
            Assert.Equal(1, tap.RequestsReceived);
            lock (log)
                Assert.Contains(log, line => line.Contains("otlp-tap exporter stalled 00:00:01 reading its answer; closed", StringComparison.Ordinal));
            Assert.Contains(tap.Diagnostics(), entry => entry.Kind == DiagnosticKind.CaptureDegraded
                && entry.Message.Contains("0 sending an export (not received), 1 reading an answer (the export already received)", StringComparison.Ordinal));
            var heard = await exporter.HearAsync(Patience);
            Assert.True(heard.Bytes.Length < EightMegabytes, $"the whole answer arrived: {heard}");
            Assert.False(heard.Ending.StartsWith("nothing more", StringComparison.Ordinal), $"the exporter was held: {heard}");
            return;
        }

        // A mebibyte at a time, then the clock moves 0.6 s: about 1.7 MB a second of fake time, each piece progress.
        var stream = exporter.Client.GetStream();
        await EntryAsync(tap, OtlpTap.AnsweringPhase);
        using var received = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var target = received.Length + (1024 * 1024);
            var read = 1;
            while (received.Length < target && (read = await stream.ReadAsync(buffer)) > 0)
                received.Write(buffer, 0, read);
            if (read == 0)
                break;
            await ProgressedAsync(tap, OtlpTap.AnsweringPhase, clock);
            clock.Advance(TimeSpan.FromMilliseconds(600));
        }

        var all = received.ToArray();
        var head = Encoding.ASCII.GetString(all, 0, Math.Min(all.Length, 512));
        Assert.StartsWith("HTTP/1.1 200 ", head);
        Assert.Equal(EightMegabytes, all.Length - (head.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4));
        Assert.Equal(0, tap.CallerTimeouts);
    }

    [Theory]
    [InlineData("export")]
    [InlineData("answer")]
    public async Task A_transfer_completing_as_the_limit_fires_is_received_or_cut_never_both(string phase)
    {
        await using var collector = new StubCollector(200, new string('x', EightMegabytes));
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(new ListSink(), [], clock, o =>
        {
            if (phase == "answer")
                o.ForwardBaseUri = collector.BaseUri;
        });

        using var exporter = await RawExporter.ConnectAsync(tap, receiveBuffer: Piece);
        await exporter.SendAsync(phase == "export"
            ? Head("Connection: close\r\nContent-Length: 10\r\n") + "hello"
            : Encoding.ASCII.GetString(WholeExport(close: true)));
        var entry = await StalledAsync(tap, phase == "export" ? OtlpTap.SendingPhase : OtlpTap.AnsweringPhase);
        Task<byte[]>? reading = null;
        tap.Watch!.BeforeFire = due =>
        {
            if (due != entry)
                return;
            // The transfer completes in the gap between the heartbeat's look and its swap, and ends its entry first.
            if (phase == "export")
                exporter.Client.GetStream().Write("world"u8);
            else
                reading = Task.Run(async () =>
                {
                    using var all = new MemoryStream();
                    await exporter.Client.GetStream().CopyToAsync(all);
                    return all.ToArray();
                });
            SpinWait.SpinUntil(() => !due.IsActive, Patience);
        };
        clock.Advance(PastTheLimit);

        if (phase == "export")
        {
            var heard = await exporter.HearAsync(Patience);
            Assert.StartsWith("HTTP/1.1 ", heard.FirstLine);
            Assert.DoesNotContain(" 408 ", heard.FirstLine, StringComparison.Ordinal);
            Assert.Equal(1, tap.RequestsReceived);
        }
        else
        {
            Assert.True(await WaitUntilAsync(() => reading is not null), "the heartbeat never reached the entry");
            var all = await reading!.WaitAsync(Patience);
            var head = Encoding.ASCII.GetString(all, 0, Math.Min(all.Length, 512));
            Assert.StartsWith("HTTP/1.1 200 ", head);
            Assert.Equal(EightMegabytes, all.Length - (head.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4));
        }

        Assert.Equal(0, tap.CallerTimeouts);
    }

    [Theory]
    [InlineData("export")]
    [InlineData("answer")]
    public async Task A_transfer_ending_after_the_limit_fired_is_cut(string phase)
    {
        await using var collector = new StubCollector(200, new string('x', EightMegabytes));
        var clock = new FakeTimeProvider();
        using var hold = new ManualResetEventSlim();
        await using var tap = await StartAsync(new ListSink(), [], clock, o =>
        {
            if (phase == "answer")
                o.ForwardBaseUri = collector.BaseUri;
        });
        tap.Watch!.BeforeAct = _ => hold.Wait(Patience);

        using var exporter = await RawExporter.ConnectAsync(tap, receiveBuffer: Piece);
        await exporter.SendAsync(phase == "export"
            ? Encoding.ASCII.GetBytes(Head($"Content-Length: {Export.Length}\r\n"))
            : WholeExport(close: true));
        var entry = await StalledAsync(tap, phase == "export" ? OtlpTap.SendingPhase : OtlpTap.AnsweringPhase);
        clock.Advance(PastTheLimit);
        Assert.True(await WaitUntilAsync(() => entry.HasFired), "the limit never fired");

        // The transfer ends, whole, once the limit has fired and before its action has run: the swap on the entry gave it to
        // the limit, so the tap cuts it itself.
        Task<byte[]>? reading = null;
        if (phase == "export")
            await exporter.SendAsync(Export);
        else
            reading = Task.Run(async () =>
            {
                using var all = new MemoryStream();
                await exporter.Client.GetStream().CopyToAsync(all);
                return all.ToArray();
            });
        Assert.True(await WaitUntilAsync(() => tap.CallerTimeouts == 1), "a transfer that ended after its limit fired was not cut");
        hold.Set(); // the limit's action, held until the tap has seen the transfer end

        if (phase == "export")
        {
            AssertTimedOut(await exporter.HearAsync(Patience));
            Assert.Equal(0, tap.RequestsReceived);
        }
        else
        {
            await reading!.WaitAsync(Patience);
            Assert.Contains(tap.Diagnostics(), each => each.Message.Contains("0 sending an export (not received), 1 reading an answer", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task No_limit_applies_while_a_debugger_is_attached()
    {
        var clock = new FakeTimeProvider();
        var attached = new StrongBox<bool>(true);
        await using var tap = await StartAsync(new ListSink(), [], clock, debugger: () => Volatile.Read(ref attached.Value));

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync(StalledBody(BodyKind.ContentLength));
        await StalledAsync(tap, OtlpTap.SendingPhase);
        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(500);
        Assert.Equal(0, tap.CallerTimeouts);
        Assert.Contains(tap.Watch!.Entries, entry => entry.Phase == OtlpTap.SendingPhase && entry.IsActive);

        Volatile.Write(ref attached.Value, false);
        clock.Advance(StallWatch.Beat);
        Assert.True(await WaitUntilAsync(() => tap.CallerTimeouts == 1), "a stall that outlived the detach was not cut at the next beat");
    }

    [Fact]
    public void The_limits_are_100_and_120_seconds_by_default()
    {
        var options = new OtlpTapOptions();
        Assert.Equal(TimeSpan.FromSeconds(100), options.CallerStallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(120), options.KeepAliveTimeout);
    }

    [Theory]
    [InlineData(nameof(OtlpTapOptions.CallerStallTimeout), 0)]
    [InlineData(nameof(OtlpTapOptions.CallerStallTimeout), -2)]
    [InlineData(nameof(OtlpTapOptions.CallerStallTimeout), -1000)]
    [InlineData(nameof(OtlpTapOptions.KeepAliveTimeout), 0)]
    [InlineData(nameof(OtlpTapOptions.KeepAliveTimeout), -2)]
    [InlineData(nameof(OtlpTapOptions.KeepAliveTimeout), -1000)]
    public async Task A_limit_of_zero_or_less_is_refused(string option, int milliseconds)
    {
        var options = new OtlpTapOptions { ListenPort = 0, ListenHost = "127.0.0.1", Sink = new ListSink() };
        typeof(OtlpTapOptions).GetProperty(option)!.SetValue(options, (TimeSpan?)TimeSpan.FromMilliseconds(milliseconds));
        await using var tap = new OtlpTap(options);

        var thrown = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => tap.StartAsync());
        Assert.Equal(option, thrown.ParamName);
    }

    [Theory]
    [InlineData(nameof(OtlpTapOptions.CallerStallTimeout), "null")]
    [InlineData(nameof(OtlpTapOptions.CallerStallTimeout), "infinite")]
    [InlineData(nameof(OtlpTapOptions.CallerStallTimeout), "max")]
    [InlineData(nameof(OtlpTapOptions.KeepAliveTimeout), "null")]
    [InlineData(nameof(OtlpTapOptions.KeepAliveTimeout), "infinite")]
    [InlineData(nameof(OtlpTapOptions.KeepAliveTimeout), "max")]
    public async Task A_limit_of_none_never_cuts(string option, string value)
    {
        TimeSpan? limit = value switch
        {
            "null" => null,
            "infinite" => Timeout.InfiniteTimeSpan,
            _ => TimeSpan.MaxValue,
        };
        var clock = new FakeTimeProvider();
        await using var tap = await StartAsync(new ListSink(), [], clock, o => typeof(OtlpTapOptions).GetProperty(option)!.SetValue(o, limit));

        using var exporter = await RawExporter.ConnectAsync(tap);
        var phase = option == nameof(OtlpTapOptions.CallerStallTimeout) ? OtlpTap.SendingPhase : OtlpTap.KeptAlivePhase;
        if (phase == OtlpTap.SendingPhase)
            await exporter.SendAsync(StalledBody(BodyKind.ContentLength));
        if (value == "max")
        {
            await StalledAsync(tap, phase);
        }
        else
        {
            await Task.Delay(300);
            Assert.DoesNotContain(tap.Watch!.Entries, entry => entry.Phase == phase); // no limit, no entry
        }

        clock.Advance(TimeSpan.FromHours(1));
        await Task.Delay(500);

        Assert.Equal(0, tap.CallerTimeouts);
        Assert.False(exporter.Client.Client.Poll(200_000, SelectMode.SelectRead), "the connection was closed or answered");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(502)]
    public async Task An_exchange_leaves_no_entry_behind(int status)
    {
        var clock = new FakeTimeProvider();
        var forward = status == 502 ? new Uri($"http://127.0.0.1:{LoopbackPort.Free()}") : null;
        await using var tap = await StartAsync(new ListSink(), [], clock, o => o.ForwardBaseUri = forward);

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync(status == 400 ? Encoding.ASCII.GetBytes(Head("Content-Length: abc\r\n")) : WholeExport());
        exporter.Client.Client.Shutdown(SocketShutdown.Send); // the exporter is done: the tap's read ends the connection
        var heard = await exporter.HearAsync(Patience);

        Assert.StartsWith($"HTTP/1.1 {status} ", heard.FirstLine);
        Assert.True(await WaitUntilAsync(() => tap.Watch!.Entries.Count == 0), "an entry outlived its connection");
        clock.Advance(PastTheLimit);
        await Task.Delay(200);
        Assert.Equal(0, tap.CallerTimeouts);
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertTimedOut(Answer heard)
    {
        Assert.Equal(["HTTP/1.1 408 Request Timeout"], heard.StatusLines);
        Assert.Contains("Connection: close", heard.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(heard.Closed, $"the tap answered 408 and did not close its side: {heard}");
    }

    /// <summary>
    /// The connection outlives <see cref="OtlpTapOptions.CallerStallTimeout"/> idle, unanswered and open, and is closed with
    /// nothing written once it has waited <see cref="OtlpTapOptions.KeepAliveTimeout"/>, counted and logged nowhere.
    /// </summary>
    private static async Task AssertIdleThenClosedAsync(OtlpTap tap, RawExporter exporter, FakeTimeProvider clock, List<string> log)
    {
        await StalledAsync(tap, OtlpTap.KeptAlivePhase);
        clock.Advance(PastTheLimit);
        await Task.Delay(300);
        Assert.False(exporter.Client.Client.Poll(200_000, SelectMode.SelectRead), "the tap closed or wrote to an idle connection before KeepAliveTimeout");

        clock.Advance(KeepAlive);
        var heard = await exporter.HearAsync(Patience);
        Assert.True(heard.Closed, $"the idle connection was not closed: {heard}");
        Assert.Empty(heard.Bytes);
        Assert.Equal(0, tap.CallerTimeouts);
        Assert.DoesNotContain(tap.Diagnostics(), entry => entry.Message.Contains("stalled", StringComparison.Ordinal));
        lock (log)
            Assert.DoesNotContain(log, line => line.Contains("stalled", StringComparison.Ordinal));
    }

    private static async Task<OtlpTap> StartAsync(
        ListSink sink, List<string> log, TimeProvider? clock, Action<OtlpTapOptions>? configure = null, Func<bool>? debugger = null)
    {
        var options = new OtlpTapOptions
        {
            ListenPort = 0,
            ListenHost = "127.0.0.1",
            Sink = sink,
            CallerStallTimeout = Limit,
            KeepAliveTimeout = KeepAlive,
            Log = line => { lock (log) log.Add(line); },
        };
        configure?.Invoke(options);
        var tap = new OtlpTap(options)
        {
            Clock = clock ?? TimeProvider.System,
            // Every fact says whether a debugger is attached, so the suite passes under one too.
            DebuggerAttached = debugger ?? (static () => false),
        };
        await tap.StartAsync();
        return tap;
    }

    /// <summary>The active entry of <paramref name="phase"/>, once there is one.</summary>
    private static async Task<StallWatch.Entry> EntryAsync(OtlpTap tap, string phase)
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
    private static async Task<StallWatch.Entry> StalledAsync(OtlpTap tap, string phase)
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
    private static async Task ProgressedAsync(OtlpTap tap, string phase, FakeTimeProvider clock)
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
        var body = new byte[int.Parse(lengthLine["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture)];
        await stream.ReadExactlyAsync(body);
        return text.Split("\r\n")[0];
    }
}
