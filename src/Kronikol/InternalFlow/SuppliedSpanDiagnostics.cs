using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.InternalFlow;

/// <summary>
/// What an ingest handed spans (<c>IngestRequest.Spans</c>, <c>kronikol ingest --spans</c>) is told about the join between
/// its calls and its spans, which is the capturer's to get right and fails silently otherwise: an empty popup reads as
/// "nothing happened". Recorded in the run's diagnostics (<c>IngestResult.Diagnostics</c>, the report's diagnostics).
/// An in-process run records none of it.
/// </summary>
internal static class SuppliedSpanDiagnostics
{
    private const int NamesShown = 3;

    /// <param name="logs">The calls the segments were built from.</param>
    /// <param name="supplied">Every span the ingest was handed.</param>
    /// <param name="drawn">The spans left after the granularity filter, which the segments were built from.</param>
    /// <param name="segments">The calls' segments.</param>
    internal static void Record(
        RequestResponseLog[] logs,
        IReadOnlyList<FlowSpan> supplied,
        FlowSpan[] drawn,
        Dictionary<string, InternalFlowSegment> segments)
    {
        var calls = logs.Where(l => l.Type == RequestResponseType.Request && !l.IsDiagramMarker && !l.IsUserAction).ToArray();

        // The likely harness mistake: a call stamped with a trace the exported spans do not hold (the wrong span read
        // as active, a span file of another run, a processor that dropped the trace).
        var suppliedTraces = supplied.Select(s => s.TraceId).ToHashSet(StringComparer.Ordinal);
        var unjoined = calls
            .Where(l => l.ActivityTraceId is { } trace && !suppliedTraces.Contains(trace))
            .Select(l => l.RequestResponseId)
            .Distinct()
            .Count();
        if (unjoined > 0)
            ReportDiagnosticsScope.Record(DiagnosticKind.Other,
                $"{unjoined} call(s) carry an activityTraceId no supplied span has, so they show no internal flow: a capturer stamps "
                + "each call with the trace of the span it was made under (trace.getActiveSpan() in OpenTelemetry JS), and the "
                + "span files must hold that trace.");

        var held = new HashSet<FlowSpan>(ReferenceEqualityComparer.Instance);
        foreach (var segment in segments.Values)
            held.UnionWith(segment.FlowSpans);
        var notHeld = drawn.Where(s => !held.Contains(s)).ToArray();
        if (notHeld.Length == 0)
            return;

        var carried = calls.Where(l => l.ActivityTraceId is not null).Select(l => l.ActivityTraceId!).ToHashSet(StringComparer.Ordinal);
        var ofNoCall = notHeld.Where(s => !carried.Contains(s.TraceId)).ToArray();
        var ofACall = notHeld.Where(s => carried.Contains(s.TraceId)).ToArray();
        var parts = new List<string>(2);
        if (ofNoCall.Length > 0)
            parts.Add($"{ofNoCall.Length} of a trace no call carries ({Names(ofNoCall)})");
        if (ofACall.Length > 0)
            parts.Add($"{ofACall.Length} of a trace a call carries, which started outside the time of every call carrying it "
                      + $"(from 50 ms before its request to its response) or which calls of two tests would both take ({Names(ofACall)})");
        ReportDiagnosticsScope.Record(DiagnosticKind.Other,
            $"{notHeld.Length} supplied span(s) are in no call's internal flow: {string.Join("; ", parts)}.");
    }

    private static string Names(FlowSpan[] spans)
    {
        var names = spans.Select(s => s.Name).Distinct(StringComparer.Ordinal).ToArray();
        return string.Join(", ", names.Take(NamesShown)) + (names.Length > NamesShown ? ", …" : "");
    }
}
