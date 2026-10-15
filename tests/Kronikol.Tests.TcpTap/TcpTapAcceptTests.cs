using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.TcpTap;
using Kronikol.Reports;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// A failed accept never stops the tap (plans/TCPTAP_FAILED_CONNECT_PLAN.md R1). On Linux, one accept that failed for want
/// of a file descriptor ended the accept loop for good: the listening socket stayed open, callers connected and were never
/// served, and nothing said so. The facts make an accept fail through the loop's seam, as the process out of descriptors
/// does (harness <c>accept/</c> E2). The tap listens on 127.0.0.1 alone, so one loop calls the seam.
/// </summary>
public class TcpTapAcceptTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private static SocketException OutOfDescriptors() => new((int)SocketError.TooManyOpenSockets);

    private static RedisTapOptions Options(StubServer server, List<string> log, Action<RedisTapOptions>? configure = null)
    {
        var options = new RedisTapOptions
        {
            ListenPort = 0,
            ListenHost = "127.0.0.1",
            ForwardHost = "127.0.0.1",
            ForwardPort = server.Port,
            CallerName = "svc",
            ServiceName = "redis",
            Sink = new RecordingSink(),
            EmitActivities = false,
            Log = line => { lock (log) log.Add(line); },
        };
        configure?.Invoke(options);
        return options;
    }

    private static StubServer Pong() => new(_ => Encoding.ASCII.GetBytes(Resp.Simple("PONG")));

    /// <summary>What a client dialling the tap hears in answer to a PING within <see cref="Patience"/>; empty when nothing.</summary>
    private static async Task<string> PingAsync(TcpTapCore tap)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", tap.BoundPort);
        var stream = client.GetStream();
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
            // Nothing more came: the answer so far is what the client heard.
        }

        return heard.ToString();
    }

    private static string[] Lines(List<string> log, string containing)
    {
        lock (log)
            return log.Where(line => line.Contains(containing, StringComparison.Ordinal)).ToArray();
    }

    [Fact]
    public async Task AFailedAcceptDoesNotStopTheTap()
    {
        await using var server = Pong();
        var log = new List<string>();
        await using var tap = new RedisTap(Options(server, log));
        var tries = 0;
        tap.Accepting.Accept = (listener, ct) => Interlocked.Increment(ref tries) <= 3
            ? ValueTask.FromException<TcpClient>(OutOfDescriptors())
            : listener.AcceptTcpClientAsync(ct);
        await tap.StartAsync();

        Assert.Equal("+PONG\r\n", await PingAsync(tap));
        Assert.Equal(3, tap.Accepting.Failures);
        Assert.Equal(1, tap.ConnectionsAccepted);

        var failed = Assert.Single(Lines(log, "failed ("));
        Assert.Contains($"[svc→redis] tcp-tap accept on 127.0.0.1:{tap.BoundPort} failed (SocketException TooManyOpenSockets: ", failed);
        Assert.Contains("still listening", failed);
        var again = Assert.Single(Lines(log, "accepting again"));
        Assert.Contains($"accepting again on 127.0.0.1:{tap.BoundPort} after 3 failed accept(s) since ", again);

        var entry = Assert.Single(tap.Diagnostics());
        Assert.Equal(DiagnosticKind.CaptureDegraded, entry.Kind);
        Assert.StartsWith("svc→redis: 3 accept(s) failed, the first at ", entry.Message);
        Assert.Contains("(SocketException TooManyOpenSockets: ", entry.Message);
        Assert.Contains("the tap kept listening and tried again", entry.Message);

        await tap.DisposeAsync();
        Assert.Contains(", 3 failed accepts", Assert.Single(Lines(log, "tcp-tap stopped (")));
    }

    [Fact]
    public async Task StoppingTheTapWhileItWaitsForACallerCountsNothing()
    {
        await using var server = Pong();
        var log = new List<string>();
        var tap = new RedisTap(Options(server, log));
        await tap.StartAsync();
        Assert.Equal("+PONG\r\n", await PingAsync(tap));

        // The loop is waiting in a real accept, which the stop ends with an exception of the platform's choosing.
        await tap.DisposeAsync();
        await Task.Delay(100);

        Assert.Equal(0, tap.Accepting.Failures);
        Assert.Empty(Lines(log, "failed ("));
        Assert.Empty(Lines(log, "stopped accepting"));
        Assert.Empty(tap.Diagnostics());
    }

    [Fact]
    public async Task AnAcceptThatKeepsFailingIsTriedAtAPace()
    {
        await using var server = Pong();
        var log = new List<string>();
        await using var tap = new RedisTap(Options(server, log));
        var failing = 1;
        var tries = 0;
        tap.Accepting.Accept = (listener, ct) =>
        {
            if (Volatile.Read(ref failing) == 0)
                return listener.AcceptTcpClientAsync(ct);
            Interlocked.Increment(ref tries);
            return ValueTask.FromException<TcpClient>(OutOfDescriptors());
        };
        await tap.StartAsync();

        await Task.Delay(500);
        Volatile.Write(ref failing, 0);
        var triedWhileFailing = Volatile.Read(ref tries);

        // Pauses of 10, 20, 40, 80, 160 and 320 ms allow six or seven tries in half a second; with no pause the loop
        // tried tens of thousands of times a second (harness accept/ E2). A loaded machine only ever tries fewer.
        Assert.InRange(triedWhileFailing, 2, 12);
        Assert.Equal("+PONG\r\n", await PingAsync(tap));
    }

    [Fact]
    public async Task ThePauseDoublesUpToASecondAndStartsAgainAfterAnAccept()
    {
        await using var server = Pong();
        var log = new List<string>();
        await using var tap = new RedisTap(Options(server, log));
        var pauses = new List<TimeSpan>();
        tap.Accepting.Pause = (pause, _) =>
        {
            lock (pauses) pauses.Add(pause);
            return Task.CompletedTask;
        };
        var tries = 0;
        tap.Accepting.Accept = (listener, ct) => Interlocked.Increment(ref tries) switch
        {
            <= 9 => ValueTask.FromException<TcpClient>(OutOfDescriptors()),
            10 => listener.AcceptTcpClientAsync(ct),
            <= 12 => ValueTask.FromException<TcpClient>(OutOfDescriptors()),
            _ => listener.AcceptTcpClientAsync(ct),
        };
        await tap.StartAsync();

        Assert.True(await Wait.UntilAsync(() => Volatile.Read(ref tries) >= 10, 15_000),
            $"the loop stopped trying after {Volatile.Read(ref tries)} accept(s)");
        lock (pauses)
        {
            Assert.Equal(
                new[] { 10, 20, 40, 80, 160, 320, 640, 1000, 1000 },
                pauses.Select(p => (int)p.TotalMilliseconds).ToArray());
        }

        // The tenth accept takes this client; the two failures after it start again from the first pause.
        Assert.Equal("+PONG\r\n", await PingAsync(tap));
        Assert.True(await Wait.UntilAsync(() => Volatile.Read(ref tries) >= 13, 15_000));
        lock (pauses)
        {
            Assert.Equal(
                new[] { 10, 20, 40, 80, 160, 320, 640, 1000, 1000, 10, 20 },
                pauses.Select(p => (int)p.TotalMilliseconds).ToArray());
        }

        Assert.Equal(11, tap.Accepting.Failures);
        Assert.Equal(2, Lines(log, "failed (").Length);
        Assert.Contains(Lines(log, "accepting again"), line => line.Contains(" after 9 failed accept(s) since ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailingAcceptIsLoggedOnceAMinute()
    {
        await using var server = Pong();
        var log = new List<string>();
        await using var tap = new RedisTap(Options(server, log));
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
        tap.Accepting.Clock = clock;
        tap.Accepting.Pause = (_, _) => Task.CompletedTask;
        var tries = 0;
        tap.Accepting.Accept = (listener, ct) =>
        {
            var n = Interlocked.Increment(ref tries);
            if (n > 50)
                return listener.AcceptTcpClientAsync(ct);
            if (n == 30)
                clock.Advance(TimeSpan.FromSeconds(61));
            return ValueTask.FromException<TcpClient>(OutOfDescriptors());
        };
        await tap.StartAsync();

        Assert.True(await Wait.UntilAsync(() => Volatile.Read(ref tries) > 50, 15_000),
            $"the loop stopped trying after {Volatile.Read(ref tries)} accept(s)");
        Assert.Equal("+PONG\r\n", await PingAsync(tap));

        // Fifty failures: the first is logged at once, the thirtieth because a minute has passed since, and the rest not.
        var failed = Assert.Single(Lines(log, "failed ("));
        Assert.Contains("TooManyOpenSockets", failed);
        var still = Assert.Single(Lines(log, "still failing"));
        Assert.Contains("still failing: 30 failed accept(s) since 09:00:00Z, the last SocketException TooManyOpenSockets: ", still);
        var again = Assert.Single(Lines(log, "accepting again"));
        Assert.Contains(" after 50 failed accept(s) since 09:00:00Z", again);
        Assert.Equal(50, tap.Accepting.Failures);
        Assert.Contains(tap.Diagnostics(), e => e.Message.StartsWith("svc→redis: 50 accept(s) failed, the first at 09:00:00Z (", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoppingTheTapEndsTheAcceptLoopQuietly()
    {
        await using var server = Pong();
        var log = new List<string>();
        var tap = new RedisTap(Options(server, log));
        var stopped = 0;
        var failures = 0;
        tap.Accepting.Accept = (_, _) =>
        {
            if (Volatile.Read(ref stopped) == 1)
                return ValueTask.FromException<TcpClient>(new ObjectDisposedException(typeof(Socket).FullName));
            Interlocked.Increment(ref failures);
            return ValueTask.FromException<TcpClient>(OutOfDescriptors());
        };
        await tap.StartAsync();

        // After the eighth failure the loop pauses for a second.
        Assert.True(await Wait.UntilAsync(() => Volatile.Read(ref failures) >= 8, 15_000),
            $"the loop stopped trying after {Volatile.Read(ref failures)} accept(s)");
        await Task.Delay(100);

        Volatile.Write(ref stopped, 1);
        var stopping = Stopwatch.StartNew();
        await tap.DisposeAsync();
        stopping.Stop();
        await Task.Delay(100);

        Assert.True(stopping.ElapsedMilliseconds < 500, $"DisposeAsync waited {stopping.ElapsedMilliseconds} ms for the loop's pause");
        Assert.Equal(Volatile.Read(ref failures), tap.Accepting.Failures);
        Assert.Single(Lines(log, "failed ("));
        Assert.Empty(Lines(log, "stopped accepting"));
        Assert.Empty(Lines(log, "still failing"));
    }

    [Fact]
    public async Task AListenerClosedUnderARunningTapIsLoggedAndEndsItsLoop()
    {
        await using var server = Pong();
        var log = new List<string>();
        await using var tap = new RedisTap(Options(server, log));
        var tries = 0;
        tap.Accepting.Accept = (_, _) =>
        {
            Interlocked.Increment(ref tries);
            return ValueTask.FromException<TcpClient>(new ObjectDisposedException(typeof(Socket).FullName));
        };
        await tap.StartAsync();

        Assert.True(await Wait.UntilAsync(() => Lines(log, "stopped accepting").Length > 0, 15_000), "nothing said the tap stopped accepting");
        var line = Assert.Single(Lines(log, "stopped accepting"));
        Assert.Contains($"tcp-tap stopped accepting on 127.0.0.1:{tap.BoundPort}: its listening socket was closed (ObjectDisposedException: ", line);
        await Task.Delay(200);
        Assert.Equal(1, Volatile.Read(ref tries));
        Assert.Equal(0, tap.Accepting.Failures);
    }

    [Fact]
    public async Task ALogCallbackThatThrowsDoesNotStopTheAcceptLoop()
    {
        await using var server = Pong();
        var log = new List<string>();
        var throwing = 0;
        await using var tap = new RedisTap(Options(server, log, o => o.Log = line =>
        {
            lock (log) log.Add(line);
            if (Volatile.Read(ref throwing) == 1)
                throw new InvalidOperationException("There is no currently active test.");
        }));
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tries = 0;
        tap.Accepting.Accept = async (listener, ct) =>
        {
            await go.Task.WaitAsync(ct);
            if (Interlocked.Increment(ref tries) <= 3)
                throw OutOfDescriptors();
            return await listener.AcceptTcpClientAsync(ct);
        };
        await tap.StartAsync();
        Volatile.Write(ref throwing, 1);
        go.SetResult();

        try
        {
            Assert.Equal("+PONG\r\n", await PingAsync(tap));
            Assert.Equal(3, tap.Accepting.Failures);
            Assert.Single(Lines(log, "failed ("));
            Assert.Single(Lines(log, "accepting again"));
        }
        finally
        {
            // Stopping has a fact of its own, so a red run here fails on what this fact is about.
            Volatile.Write(ref throwing, 0);
        }
    }

    [Fact]
    public async Task ALogCallbackThatThrowsDoesNotFailStartingOrStopping()
    {
        await using var server = Pong();
        var log = new List<string>();
        var tap = new RedisTap(Options(server, log, o => o.Log = line =>
        {
            lock (log) log.Add(line);
            throw new InvalidOperationException("There is no currently active test.");
        }));

        var starting = await Record.ExceptionAsync(() => tap.StartAsync());
        string answer;
        Exception? stopping;
        try
        {
            answer = await PingAsync(tap);
        }
        finally
        {
            stopping = await Record.ExceptionAsync(async () => await tap.DisposeAsync());
        }

        Assert.Null(starting);
        Assert.Equal("+PONG\r\n", answer);
        Assert.Null(stopping);
        Assert.Single(Lines(log, "tcp-tap listening on"));
        Assert.Single(Lines(log, "tcp-tap stopped ("));
    }
}

/// <summary>A clock a fact moves by hand.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
