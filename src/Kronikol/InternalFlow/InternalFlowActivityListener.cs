using System.Diagnostics;

namespace Kronikol.InternalFlow;

/// <summary>
/// A non-invasive <see cref="ActivityListener"/> that captures completed
/// <see cref="Activity"/> spans into <see cref="InternalFlowSpanStore"/>
/// without interfering with the SUT's existing diagnostic pipelines.
/// <para>
/// Subscribes to <b>all</b> <see cref="ActivitySource"/>s except those named in
/// <see cref="ExcludedSourcePrefixes"/>, the runtime's connection-level sources.
/// It hears <c>System.Net.Http</c>, so HttpClient's span keeps its source and
/// its kind, but takes that span only under a parent: a call made outside a
/// trace makes no span and sends no <c>traceparent</c>, as when nothing listens.
/// </para>
/// <para>
/// From 2.21.1 until #138 the listener left <c>System.Net.Http</c> out, on the view
/// that its source path skips the <c>DiagnosticListener</c> events Application
/// Insights 2.x builds its HTTP dependencies from. It does not: .NET writes those
/// events whichever way it makes the span, and measured with Application
/// Insights 2.23's dependency collector the dependencies, their ids and their
/// correlation are the same either way (<c>plans/INTERNAL_FLOW_SOURCES_PLAN.md</c>
/// section 2.3, <c>HttpClientSpanCaptureTests</c>). The break 2.21.1 fixed was the
/// tracking handler's own <c>traceparent</c>, written ahead of HttpClient's. Leaving
/// the source out kept out no span either: HttpClient makes its span with
/// <c>new Activity(...)</c> when nothing hears the source, which belongs to no
/// source, so the span arrived without its source and its kind.
/// </para>
/// <para>
/// Uses <see cref="ActivitySamplingResult.AllData"/> (not <c>AllDataAndRecorded</c>)
/// so that the <see cref="Activity.Recorded"/> flag remains <c>false</c> —
/// existing OTel exporters that check <c>Recorded</c> will skip these activities.
/// </para>
/// </summary>
public sealed class InternalFlowActivityListener : IDisposable
{
    /// <summary>
    /// The sources the listener does not hear, by the start of their names: the runtime's connection-level sources
    /// (<c>Experimental.System.Net.Http.Connections</c>, <c>.NameResolution</c>, <c>.Sockets</c>, <c>.Security</c>), which
    /// from .NET 9 build spans for every connection only because something listens. Their socket connect, DNS lookup,
    /// TLS and connection setup spans are roots, never drawn, and the wait for a connection is pool noise under the
    /// first request.
    /// </summary>
    internal static readonly string[] ExcludedSourcePrefixes =
    [
        "Experimental.System.Net."
    ];

    private static readonly object Lock = new();
    private static volatile InternalFlowActivityListener? _autoStarted;
    private static int _starting;

    private readonly ActivityListener _listener;

    public InternalFlowActivityListener(params string[] additionalActivitySources)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => !IsExcluded(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Source.Name == SpanSource.HttpClient && options.Parent == default
                    ? ActivitySamplingResult.None
                    : ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> options) =>
                options.Source.Name == SpanSource.HttpClient && string.IsNullOrEmpty(options.Parent)
                    ? ActivitySamplingResult.None
                    : ActivitySamplingResult.AllData,
            ActivityStopped = Keep
        };

        ActivitySource.AddActivityListener(_listener);
    }

    private static bool IsExcluded(string sourceName)
    {
        foreach (var prefix in ExcludedSourcePrefixes)
            if (sourceName.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// Stores a stopped span, except HttpClient's with no parent. A span another listener sampled is reported to every
    /// listener of its source when it stops, whatever each one sampled, so an app's OpenTelemetry that samples every span
    /// would otherwise put the root span of each call made outside a trace in the store.
    /// </summary>
    private static void Keep(Activity activity)
    {
        if (activity.Source.Name == SpanSource.HttpClient && activity.ParentSpanId == default
            && string.IsNullOrEmpty(activity.ParentId))
            return;
        InternalFlowSpanStore.Add(activity);
    }

    /// <summary>
    /// Ensures a single process-wide listener is started with the given additional sources.
    /// Called automatically by <see cref="Tracking.TestTrackingMessageHandler"/>.
    /// Subsequent calls are no-ops (first caller's sources win).
    /// Uses lock-free initialization to avoid thread-pool starvation on constrained
    /// runners where multiple handlers race on first use (issue #70). A caller that arrives while another is
    /// registering the listener waits for it, for at most <see cref="RegistrationWait"/>: one that returned at once
    /// started its next span before the listener was registered, so the span was not sampled and no store held it.
    /// </summary>
    internal static void EnsureStarted(string[]? additionalActivitySources = null)
    {
        if (_autoStarted != null) return;
        if (Interlocked.CompareExchange(ref _starting, 1, 0) != 0)
        {
            SpinWait.SpinUntil(() => _autoStarted != null, RegistrationWait);
            return;
        }
        _autoStarted = new InternalFlowActivityListener(additionalActivitySources ?? []);
    }

    /// <summary>
    /// How long a caller waits for another to register the listener. Registering takes microseconds; the bound is there
    /// so a caller never waits on a registration that does not finish.
    /// </summary>
    private static readonly TimeSpan RegistrationWait = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether the listener <see cref="EnsureStarted"/> starts is registered.
    /// </summary>
    internal static bool IsStarted => _autoStarted is not null;

    /// <summary>
    /// Resets the auto-started singleton for test isolation. Not for production use.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (Lock)
        {
            _autoStarted?.Dispose();
            _autoStarted = null;
            Interlocked.Exchange(ref _starting, 0);
        }
    }

    public void Dispose() => _listener.Dispose();
}
