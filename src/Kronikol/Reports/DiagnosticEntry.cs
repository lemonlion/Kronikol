using System.Diagnostics.CodeAnalysis;

namespace Kronikol.Reports;

/// <summary>
/// What a <see cref="DiagnosticEntry"/> is about. New kinds are appended, never renumbered: hosts
/// surface them by name (the sidekick dashboard, <c>kronikol ingest</c>'s output, CI summaries).
/// </summary>
public enum DiagnosticKind
{
    /// <summary>A diagram could not be produced or rendered; the scenario shows a placeholder note instead.</summary>
    RenderFailure,

    /// <summary>One report output (an HTML file, a data file, the component diagram) failed; the others were still written.</summary>
    OutputFailure,

    /// <summary>A capture line could not be parsed and was skipped (<see cref="Kronikol.Ingestion.IngestRequest.StrictParsing"/> turns this back into a throw).</summary>
    MalformedLine,

    /// <summary>Step or assertion labels that still do not start with a capital letter after <see cref="StepText"/> ran.</summary>
    StepsNotStartingWithCapital,

    /// <summary>Interactions that could not be attributed to a scenario.</summary>
    UnattributedInteractions,

    /// <summary>Unattributed interactions that <see cref="Kronikol.Ingestion.IngestRequest.DropUnattributed"/> discarded.</summary>
    DroppedUnattributed,

    /// <summary>A capture referenced an attachment that could not be found or copied.</summary>
    AttachmentFailure,

    /// <summary>Anything a host wants to surface that has no dedicated kind yet.</summary>
    Other,
}

/// <summary>
/// One machine-readable diagnostic from a report generation or an ingest — the structured counterpart
/// to the human-readable strings <see cref="ReportDiagnostics.Analyse"/> returns.
/// </summary>
/// <param name="Kind">What the entry is about.</param>
/// <param name="Message">A one-line description, safe to print.</param>
/// <param name="ScenarioId">The scenario the entry belongs to, when it is scenario-specific.</param>
public sealed record DiagnosticEntry(DiagnosticKind Kind, string Message, string? ScenarioId = null)
{
    /// <inheritdoc />
    public override string ToString() =>
        ScenarioId is null ? $"{Kind}: {Message}" : $"{Kind} [{ScenarioId}]: {Message}";
}

/// <summary>
/// Thread-safe bag of <see cref="DiagnosticEntry"/> for one report generation. Report generation runs
/// its outputs through <see cref="System.Threading.Tasks.Parallel"/> and its diagram production may be
/// parallel too, so every add is locked and <see cref="Entries"/> hands back a snapshot.
/// </summary>
public sealed class ReportDiagnosticsCollector
{
    private readonly List<DiagnosticEntry> _entries = [];

    /// <summary>Records an entry.</summary>
    public void Add(DiagnosticEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_entries)
            _entries.Add(entry);
    }

    /// <summary>Records an entry.</summary>
    public void Add(DiagnosticKind kind, string message, string? scenarioId = null) =>
        Add(new DiagnosticEntry(kind, message, scenarioId));

    /// <summary>A snapshot of everything recorded so far, in the order it was recorded.</summary>
    public IReadOnlyList<DiagnosticEntry> Entries
    {
        get
        {
            lock (_entries)
                return _entries.ToArray();
        }
    }

    /// <summary>How many entries of <paramref name="kind"/> were recorded.</summary>
    public int CountOf(DiagnosticKind kind)
    {
        lock (_entries)
            return _entries.Count(e => e.Kind == kind);
    }

    /// <summary>Copies everything from <paramref name="other"/> into this collector.</summary>
    public void AddRange(IEnumerable<DiagnosticEntry>? other)
    {
        if (other is null)
            return;
        foreach (var entry in other)
            Add(entry);
    }
}

/// <summary>
/// The ambient <see cref="ReportDiagnosticsCollector"/> for the report generation currently in flight.
/// </summary>
/// <remarks>
/// An <see cref="AsyncLocal{T}"/>, for the same reason <c>ReportGenerator</c>'s active reports directory
/// is one: it flows through <see cref="System.Threading.Tasks.Parallel.Invoke(System.Action[])"/> into the
/// output workers, so a failure recorded deep inside one of them belongs to the generation that started
/// it — even when two generations run concurrently in one process (a dashboard regenerating while a suite
/// finishes). Recording when nothing is scoped is a silent no-op, so the low-level emitters
/// (<c>DefaultDiagramsFetcher</c>, the NDJSON readers) can always call it.
/// </remarks>
public static class ReportDiagnosticsScope
{
    private static readonly AsyncLocal<ReportDiagnosticsCollector?> ActiveCollector = new();

    /// <summary>The collector for the generation in flight, or null when none was scoped.</summary>
    public static ReportDiagnosticsCollector? Current => ActiveCollector.Value;

    /// <summary>Scopes <paramref name="collector"/> until the returned handle is disposed.</summary>
    public static IDisposable Begin(ReportDiagnosticsCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);
        var previous = ActiveCollector.Value;
        ActiveCollector.Value = collector;
        return new Scope(previous);
    }

    /// <summary>Records an entry on the current collector, if any.</summary>
    public static void Record(DiagnosticKind kind, string message, string? scenarioId = null) =>
        ActiveCollector.Value?.Add(kind, message, scenarioId);

    /// <summary>Records an exception as a <paramref name="kind"/> entry, if a collector is scoped.</summary>
    public static void Record(DiagnosticKind kind, string context, Exception exception, string? scenarioId = null) =>
        ActiveCollector.Value?.Add(kind, $"{context}: {exception.GetType().Name}: {exception.Message}", scenarioId);

    [SuppressMessage("Design", "CA1063", Justification = "Trivial scope handle.")]
    private sealed class Scope(ReportDiagnosticsCollector? previous) : IDisposable
    {
        public void Dispose() => ActiveCollector.Value = previous;
    }
}
