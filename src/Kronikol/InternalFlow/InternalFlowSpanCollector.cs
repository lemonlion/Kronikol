using System.Diagnostics;

namespace Kronikol.InternalFlow;

/// <summary>
/// Collects and filters spans based on the configured
/// <see cref="InternalFlowSpanGranularity"/>.
/// </summary>
public static class InternalFlowSpanCollector
{
    public static readonly HashSet<string> WellKnownAutoInstrumentationSources =
    [
        "Microsoft.AspNetCore",
        "System.Net.Http",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "StackExchange.Redis",
        "Azure.Cosmos",
        "Azure.Storage",
        "Microsoft.Azure.Cosmos",
        "OpenTelemetry.Instrumentation.Http",
        "OpenTelemetry.Instrumentation.AspNetCore",
        "OpenTelemetry.Instrumentation.SqlClient",
        "OpenTelemetry.Instrumentation.EntityFrameworkCore",
        "Kronikol.Grpc",
        "Grpc.Net.Client"
    ];

    /// <summary>
    /// Collects spans from <see cref="InternalFlowSpanStore"/> and filters
    /// them according to the specified granularity.
    /// </summary>
    public static Activity[] CollectSpans(
        InternalFlowSpanGranularity granularity = InternalFlowSpanGranularity.AutoInstrumentation,
        string[]? manualActivitySources = null)
    {
        var spans = InternalFlowSpanStore.GetSpans();
        return FilterSpans(spans, granularity, manualActivitySources);
    }

    /// <summary>
    /// The spans an ingest was handed (<c>IngestRequest.Spans</c>), filtered for <paramref name="granularity"/>:
    /// all of them, unless <see cref="InternalFlowSpanGranularity.Manual"/> names sources, which narrows them to those.
    /// <see cref="InternalFlowSpanGranularity.AutoInstrumentation"/>'s list is .NET's instrumentation, which would
    /// drop every span another language recorded, and the capturer already chose what to export
    /// (<c>plans/INGEST_FIDELITY_PLAN.md</c> Q3).
    /// </summary>
    internal static FlowSpan[] FilterSupplied(
        IReadOnlyList<FlowSpan> spans,
        InternalFlowSpanGranularity granularity,
        string[]? manualActivitySources)
    {
        if (granularity != InternalFlowSpanGranularity.Manual || manualActivitySources is null or { Length: 0 })
            return spans.ToArray();

        var sourceSet = new HashSet<string>(manualActivitySources, StringComparer.OrdinalIgnoreCase);
        return spans.Where(s => !string.IsNullOrEmpty(s.Source) && sourceSet.Contains(s.Source)).ToArray();
    }

    private static Activity[] FilterSpans(
        Activity[] spans,
        InternalFlowSpanGranularity granularity,
        string[]? manualActivitySources)
    {
        return granularity switch
        {
            InternalFlowSpanGranularity.Full => spans,
            InternalFlowSpanGranularity.Manual => FilterByManualSources(spans, manualActivitySources),
            _ => FilterByAutoInstrumentation(spans)
        };
    }

    private static Activity[] FilterByAutoInstrumentation(Activity[] spans)
    {
        // Find TraceIds that contain at least one well-known auto-instrumentation span.
        // Then include ALL spans with those TraceIds — this captures child spans from
        // custom application ActivitySources (e.g. "MyApp.Services") that are part of
        // the same trace as a well-known server or client span.
        var wellKnownTraceIds = new HashSet<string>();
        foreach (var span in spans)
        {
            if (!string.IsNullOrEmpty(span.Source.Name) &&
                WellKnownAutoInstrumentationSources.Contains(span.Source.Name))
                wellKnownTraceIds.Add(span.TraceId.ToString());
        }

        return spans
            .Where(s => wellKnownTraceIds.Contains(s.TraceId.ToString()))
            .ToArray();
    }

    private static Activity[] FilterByManualSources(Activity[] spans, string[]? sources)
    {
        if (sources is null or { Length: 0 })
            return spans;

        var sourceSet = new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase);
        return spans
            .Where(s => !string.IsNullOrEmpty(s.Source.Name) &&
                        sourceSet.Contains(s.Source.Name))
            .ToArray();
    }
}
