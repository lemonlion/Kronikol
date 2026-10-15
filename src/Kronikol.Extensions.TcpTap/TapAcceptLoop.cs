// Shared source: also compiled into Kronikol.Extensions.Otlp as a linked file, in that package's own namespace.
using System.Globalization;
using System.Net.Sockets;

#if KRONIKOL_OTLP_SHARED
namespace Kronikol.Extensions.Otlp;
#else
namespace Kronikol.Extensions.TcpTap;
#endif

/// <summary>
/// The accept loop of a tap that listens on its own sockets (<c>TcpTap</c>, <c>OtlpTap</c>). A failed accept never ends
/// it. With the process out of file descriptors (EMFILE), a loop that returned on the first <see cref="SocketException"/>
/// left the listening socket open and never accepted again: callers connected and were never served, and nothing said so
/// (plans/TCPTAP_FAILED_CONNECT_PLAN.md §3.1). The runtime itself recovers once descriptors come back, so the loop counts
/// the failure, pauses (10 ms, doubling to 1 s, back to 10 ms after an accept succeeds) and tries again. Without the pause
/// it failed some 46,000 times a second. It ends only when the tap stops, or when its listening socket is closed under it,
/// which is logged.
/// </summary>
internal sealed class TapAcceptLoop
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(1);

    private readonly Action<string> _log;
    private readonly Action<string>? _failed;
    private long _failures;
    private long _firstFailureAtTicks;
    private string? _firstFailure;

    /// <param name="log">Writes one line; the tap adds its name and guards the host's callback.</param>
    /// <param name="failed">
    /// Told of each failure the log reports (the first in a row, then at most one a minute), with the line's text;
    /// <c>TcpTap</c> raises it as an event. It must not throw, as the loop would end: <c>TcpTap.Degrade</c> catches what
    /// the host's callback throws and logs it through the guarded writer.
    /// </param>
    internal TapAcceptLoop(Action<string> log, Action<string>? failed = null)
    {
        _log = log;
        _failed = failed;
    }

    /// <summary>Accepts one caller. A fact makes it throw.</summary>
    internal Func<TcpListener, CancellationToken, ValueTask<TcpClient>> Accept { get; set; } =
        static (listener, ct) => listener.AcceptTcpClientAsync(ct);

    /// <summary>Waits between failed accepts, on the tap's token, so stopping the tap never waits for it. A fact records the pauses instead.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Pause { get; set; } = static (pause, ct) => Task.Delay(pause, ct);

    /// <summary>The clock the log's once-a-minute rule and the times in its lines read.</summary>
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>The pause after the first failed accept in a row; each failure after it doubles the pause.</summary>
    internal static TimeSpan FirstPause { get; } = TimeSpan.FromMilliseconds(10);

    /// <summary>The longest pause, which bounds how long a caller queued during an outage waits once its cause clears.</summary>
    internal static TimeSpan LongestPause { get; } = TimeSpan.FromSeconds(1);

    /// <summary>Accepts that failed while the tap was running.</summary>
    internal long Failures => Interlocked.Read(ref _failures);

    /// <summary>The <c>Diagnostics()</c> line, or null when no accept failed.</summary>
    internal string? DiagnosticLine(string tapName)
    {
        var failures = Failures;
        if (failures == 0)
            return null;
        var first = new DateTimeOffset(Interlocked.Read(ref _firstFailureAtTicks), TimeSpan.Zero);
        return string.Create(CultureInfo.InvariantCulture,
            $"{tapName}: {failures:N0} accept(s) failed, the first at {first:HH:mm:ss}Z ({Volatile.Read(ref _firstFailure)}); the tap kept listening and tried again, so callers that arrived meanwhile waited in the listen queue, or found it full");
    }

    /// <summary>
    /// Accepts callers on <paramref name="listener"/> and hands each to <paramref name="accepted"/> until
    /// <paramref name="ct"/> is cancelled. Nothing it logs can throw: <c>log</c> is the tap's guarded writer.
    /// </summary>
    internal async Task RunAsync(TcpListener listener, Action<TcpClient> accepted, CancellationToken ct)
    {
        var pause = FirstPause;
        long failuresInARow = 0;
        var firstInARow = default(DateTimeOffset);
        var lastReported = default(DateTimeOffset);

        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await Accept(listener, ct).ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return; // the tap is stopping
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                // Nothing in Kronikol closes a listener while its tap runs, so this is never passed over in silence.
                _log($"stopped accepting on {Endpoint(listener)}: its listening socket was closed ({Describe(ex)}); callers that dial it are no longer served");
                return;
            }
            catch (Exception ex)
            {
                var now = Clock.GetUtcNow();
                if (Interlocked.Increment(ref _failures) == 1)
                {
                    Volatile.Write(ref _firstFailure, Describe(ex));
                    Interlocked.Exchange(ref _firstFailureAtTicks, now.UtcTicks);
                }

                if (++failuresInARow == 1)
                {
                    firstInARow = now;
                    lastReported = now;
                    ReportFailure(string.Create(CultureInfo.InvariantCulture,
                        $"accept on {Endpoint(listener)} failed ({Describe(ex)}); still listening, and trying again with pauses of up to {LongestPause.TotalSeconds:0} s"));
                }
                else if (now - lastReported >= ReportInterval)
                {
                    lastReported = now;
                    ReportFailure(string.Create(CultureInfo.InvariantCulture,
                        $"accept on {Endpoint(listener)} still failing: {failuresInARow:N0} failed accept(s) since {firstInARow:HH:mm:ss}Z, the last {Describe(ex)}"));
                }

                try
                {
                    await Pause(pause, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }

                pause = TimeSpan.FromTicks(Math.Min(pause.Ticks * 2, LongestPause.Ticks));
                continue;
            }

            if (failuresInARow > 0)
            {
                _log(string.Create(CultureInfo.InvariantCulture,
                    $"accepting again on {Endpoint(listener)} after {failuresInARow:N0} failed accept(s) since {firstInARow:HH:mm:ss}Z"));
                failuresInARow = 0;
                pause = FirstPause;
            }

            accepted(client);
        }
    }

    private void ReportFailure(string line)
    {
        _log(line);
        _failed?.Invoke(line);
    }

    private static string Endpoint(TcpListener listener) => listener.LocalEndpoint.ToString() ?? "its listener";

    /// <summary>An exception as the taps' lines name it: its type, a socket error's code, and its message.</summary>
    internal static string Describe(Exception ex) => ex is SocketException socket
        ? $"{ex.GetType().Name} {socket.SocketErrorCode}: {ex.Message}"
        : $"{ex.GetType().Name}: {ex.Message}";
}
