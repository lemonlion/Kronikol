// Shared source: also compiled into Kronikol.Extensions.Otlp as a linked file, in that package's own namespace.
using System.Net.Sockets;

#if KRONIKOL_OTLP_SHARED
namespace Kronikol.Extensions.Otlp;
#else
namespace Kronikol.Extensions.TcpTap;
#endif

/// <summary>The accept loop of a tap that listens on its own sockets (<c>TcpTap</c>, <c>OtlpTap</c>).</summary>
internal sealed class TapAcceptLoop
{
    private readonly Action<string> _log;

    /// <param name="log">Writes one line; the tap adds its name and guards the host's callback.</param>
    internal TapAcceptLoop(Action<string> log) => _log = log;

    /// <summary>Accepts one caller. A fact makes it throw.</summary>
    internal Func<TcpListener, CancellationToken, ValueTask<TcpClient>> Accept { get; set; } =
        static (listener, ct) => listener.AcceptTcpClientAsync(ct);

    /// <summary>Waits between failed accepts. A fact records the pauses instead.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Pause { get; set; } = static (pause, ct) => Task.Delay(pause, ct);

    /// <summary>The clock the log's once-a-minute rule and the times in its lines read.</summary>
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>Accepts that failed while the tap was running.</summary>
    internal long Failures => 0;

    /// <summary>The <c>Diagnostics()</c> line, or null when no accept failed.</summary>
    internal string? DiagnosticLine(string tapName) => null;

    /// <summary>Accepts callers on <paramref name="listener"/> and hands each to <paramref name="accepted"/>.</summary>
    internal async Task RunAsync(TcpListener listener, Action<TcpClient> accepted, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await Accept(listener, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or InvalidOperationException)
            {
                return;
            }

            accepted(client);
        }
    }
}
