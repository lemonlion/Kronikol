using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Reports;

/// <summary>
/// Analyses captured log entries and features to produce diagnostic warnings
/// (e.g. unpaired requests, orphan logs, unused tracking components).
/// </summary>
public static class ReportDiagnostics
{
    public static string[] Analyse(RequestResponseLog[] logs, Feature[] features,
        bool includeSourceDiscovery = false, bool internalFlowTracking = true)
    {
        if (logs.Length == 0 && features.Length == 0)
            return [];

        var warnings = new List<string>();

        var distinctTestIds = logs.Select(l => l.TestId).Distinct().ToArray();

        warnings.Add($"Report diagnostics: {logs.Length} log entries across {distinctTestIds.Length} test(s).");

        if (features.Length == 0 && logs.Length > 0)
            warnings.Add("Warning: Logs were recorded but no test contexts were provided — reports will be empty. " +
                "Ensure DiagrammedTestRun.TestContexts.Enqueue(TestContext.Current) is called in every test's DisposeAsync().");

        var unpairedCount = CountUnpairedRequests(logs);
        if (unpairedCount > 0)
            warnings.Add($"Warning: {unpairedCount} unpaired request(s) detected (no matching response with same RequestResponseId).");

        var background = logs.Count(l => l.AttributionSource == AttributionSource.Expired && !l.IsDiagramMarker);
        if (background > 0)
            warnings.Add($"Info: {background} interaction(s) carried a scenario's identity after that scenario had ended " +
                "and are listed as background calls, not the scenario's (see the report's Background calls section).");

        if (features.Length > 0)
        {
            var scenarioIds = features
                .SelectMany(f => f.Scenarios)
                .Select(s => s.Id)
                .ToHashSet();

            var orphanedTestIds = distinctTestIds
                // The unknown identity is background by definition, not an orphan: the section above says so.
                .Where(id => id != TestIdentityScope.UnknownTestId && !scenarioIds.Contains(id))
                .ToArray();

            if (orphanedTestIds.Length > 0)
                warnings.Add($"Warning: {orphanedTestIds.Length} orphaned test ID(s) in logs do not match any feature scenario.");
        }

        // Only a run that draws activity diagrams has anything to say about the span store: with the
        // feature off (kronikol ingest turns it off, there being no in-process spans to show) an empty
        // store is the expected state, and warning that the diagrams will be empty told every ingest
        // about a feature it never had.
        if (internalFlowTracking)
        {
            var totalSpans = InternalFlowSpanStore.GetSpans().Length;
            if (totalSpans == 0)
                warnings.Add("Warning: InternalFlowSpanStore has 0 spans — activity diagrams will be empty.");
            else
                warnings.Add($"InternalFlowSpanStore: {totalSpans} span(s).");
        }

        if (includeSourceDiscovery)
        {
            var sources = ActivitySourceDiscovery.GetDiscoveredSources();
            if (sources.Count > 0)
            {
                var sourceList = string.Join(", ", sources.OrderByDescending(s => s.Value).Select(s => $"{s.Key} ({s.Value})"));
                warnings.Add($"Activity sources discovered: {sourceList}");
            }
        }

        var unused = TrackingComponentRegistry.GetUnusedComponents();
        if (unused.Count > 0)
        {
            var names = string.Join(", ", unused.Select(c => c.ComponentName));
            warnings.Add($"Warning: {unused.Count} tracking component(s) were registered but never invoked: {names}. " +
                "This usually means the component was added to the wrong pipeline or options. " +
                "Enable DiagnosticMode for details.");
        }

        var assertionFallbacks = Track.DiagnosticLog;
        if (assertionFallbacks.Count > 0)
        {
            warnings.Add($"Info: {assertionFallbacks.Count} assertion argument(s) could not be resolved to runtime values " +
                "(enable DiagnosticMode and check DiagnosticReport.html for details).");
        }

        return warnings.ToArray();
    }

    private static int CountUnpairedRequests(RequestResponseLog[] logs)
    {
        // Diagram markers (step bars, assertion notes, the Setup/Action boundary) and user actions are
        // single records by design — counting them as unpaired turned every healthy run into a warning.
        var requests = logs
            .Where(l => l.Type == RequestResponseType.Request && !l.IsDiagramMarker && !l.IsUserAction)
            .ToArray();
        var responseIds = logs
            .Where(l => l.Type == RequestResponseType.Response)
            .Select(l => l.RequestResponseId)
            .ToHashSet();

        return requests.Count(r => !responseIds.Contains(r.RequestResponseId));
    }
}
