using System.Diagnostics;

namespace Kronikol.InternalFlow;

/// <summary>
/// A non-invasive <see cref="ActivityListener"/> that captures completed
/// <see cref="Activity"/> spans into <see cref="InternalFlowSpanStore"/>
/// without interfering with the SUT's existing diagnostic pipelines.
/// <para>
/// Subscribes to <b>all</b> <see cref="ActivitySource"/>s <b>except</b>
/// <c>System.Net.Http</c>, whose <c>DiagnosticsHandler</c> uses a mutually
/// exclusive code path when <see cref="ActivitySource.HasListeners"/> is true —
/// the new path bypasses the legacy <c>DiagnosticListener</c> events that
/// Application Insights' <c>DependencyTrackingTelemetryModule</c> depends on.
/// Other well-known sources (e.g. <c>Microsoft.AspNetCore</c>,
/// <c>Microsoft.EntityFrameworkCore</c>) are safe to listen to because they
/// either fire <c>DiagnosticListener</c> events regardless, or are not
/// subscribed to by the Application Insights SDK.
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
    /// Sources excluded from the listener because subscribing to them causes
    /// a mutually exclusive code path that breaks Application Insights
    /// dependency tracking via <c>DiagnosticListener</c>.
    /// </summary>
    internal static readonly HashSet<string> AppInsightsConflictSources =
    [
        "System.Net.Http"
    ];

    private static readonly object Lock = new();
    private static volatile InternalFlowActivityListener? _autoStarted;
    private static int _starting;

    private readonly ActivityListener _listener;

    public InternalFlowActivityListener(params string[] additionalActivitySources)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => !AppInsightsConflictSources.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStopped = InternalFlowSpanStore.Add
        };

        ActivitySource.AddActivityListener(_listener);
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
