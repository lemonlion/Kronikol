using System.Text;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Reports;

/// <summary>
/// Generates a standalone HTML diagnostic report containing tracking health information,
/// warnings, and statistics from the test run.
/// </summary>
public static class DiagnosticReportGenerator
{
    public static void Generate(
        RequestResponseLog[] logs,
        Feature[] features,
        ReportConfigurationOptions options)
    {
        var html = BuildHtml(logs, features, options);
        var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, options.ReportsFolderPath);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "DiagnosticReport.html");
        File.WriteAllText(path, html);
        // Resolved here rather than from the run's ambient directory, so the run's manifest is told where
        // the file actually went (a no-op outside a run, and for a file that landed somewhere else).
        RunFileCollector.Record(path);
    }

    internal static string BuildHtml(
        RequestResponseLog[] logs,
        Feature[] features,
        ReportConfigurationOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
        sb.AppendLine("<title>Kronikol Diagnostic Report</title>");
        sb.AppendLine("<style>body{font-family:system-ui,sans-serif;margin:2em;color:#333}table{border-collapse:collapse;margin:1em 0}th,td{border:1px solid #ddd;padding:6px 12px;text-align:left}th{background:#f5f5f5}h2{margin-top:1.5em;border-bottom:1px solid #ddd;padding-bottom:4px}.warn{color:#b45309}.info{color:#1d4ed8}</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine("<h1>Kronikol — Diagnostic Report</h1>");

        // Configuration dump
        sb.AppendLine("<h2>Configuration</h2><table>");
        AppendRow(sb, "InternalFlowTracking", options.InternalFlowTracking);
        AppendRow(sb, "InternalFlowSpanGranularity", options.InternalFlowSpanGranularity);
        AppendRow(sb, "InternalFlowActivitySources", options.InternalFlowActivitySources is { Length: > 0 } ? string.Join(", ", options.InternalFlowActivitySources) : "<not configured>");
        AppendRow(sb, "InternalFlowDiagramStyle", options.InternalFlowDiagramStyle);
        AppendRow(sb, "InternalFlowNoDataBehavior", options.InternalFlowNoDataBehavior);
        AppendRow(sb, "DiagramFormat", options.DiagramFormat);
        AppendRow(sb, "PlantUmlRendering", options.PlantUmlRendering);
        AppendRow(sb, "GenerateComponentDiagram", options.GenerateComponentDiagram);
        sb.AppendLine("</table>");

        // Log summary
        var distinctTestIds = logs.Select(l => l.TestId).Distinct().ToArray();
        sb.AppendLine("<h2>Request/Response Log Summary</h2>");
        sb.AppendLine($"<p>{logs.Length} total log entries across {distinctTestIds.Length} distinct test ID(s).</p>");

        // Entries per service
        var byService = logs.GroupBy(l => l.ServiceName).OrderByDescending(g => g.Count()).ToArray();
        if (byService.Length > 0)
        {
            sb.AppendLine("<h3>Entries per Service</h3><table><tr><th>Service</th><th>Count</th></tr>");
            foreach (var g in byService)
                sb.AppendLine($"<tr><td>{Escape(g.Key)}</td><td>{g.Count()}</td></tr>");
            sb.AppendLine("</table>");
        }

        // Entries per test
        var byTest = logs.GroupBy(l => l.TestId).OrderByDescending(g => g.Count()).ToArray();
        if (byTest.Length > 0)
        {
            sb.AppendLine("<h3>Entries per Test (top 50)</h3><table><tr><th>Test Name</th><th>Test ID</th><th>Count</th></tr>");
            foreach (var g in byTest.Take(50))
            {
                var name = g.First().TestName;
                sb.AppendLine($"<tr><td>{Escape(name)}</td><td>{Escape(g.Key)}</td><td>{g.Count()}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        // Unknown entries breakdown
        var unknownLogs = logs.Where(l => l.TestId == TestIdentityScope.UnknownTestId).ToArray();
        if (unknownLogs.Length > 0)
        {
            var byServiceMethod = unknownLogs
                .GroupBy(l => (l.ServiceName, Method: l.Method.Value?.ToString() ?? "?"))
                .OrderByDescending(g => g.Count())
                .ToArray();
            sb.AppendLine($"<h3 class=\"warn\">⚠ Unknown Entries Breakdown ({unknownLogs.Length} entries)</h3>");
            sb.AppendLine("<p>These log entries have test ID \"unknown\" — typically from background threads without test correlation.</p>");
            sb.AppendLine("<table><tr><th>Service</th><th>Method</th><th>Count</th><th>First Seen (UTC)</th><th>Last Seen (UTC)</th></tr>");
            foreach (var g in byServiceMethod.Take(50))
            {
                var timestamps = g.Where(l => l.Timestamp.HasValue).Select(l => l.Timestamp!.Value).ToArray();
                // UTC, and said so in the column heading: these are compared against instants from other
                // machines, and an unconverted local reading with no label cannot be compared with anything.
                var first = timestamps.Length > 0 ? timestamps.Min().ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss") : "?";
                var last = timestamps.Length > 0 ? timestamps.Max().ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss") : "?";
                sb.AppendLine($"<tr><td>{Escape(g.Key.ServiceName)}</td><td>{Escape(g.Key.Method)}</td><td>{g.Count()}</td><td>{first}</td><td>{last}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        // Unpaired requests
        var requests = logs.Where(l => l.Type == RequestResponseType.Request).ToArray();
        var responseIds = logs.Where(l => l.Type == RequestResponseType.Response).Select(l => l.RequestResponseId).ToHashSet();
        var unpaired = requests.Where(r => !responseIds.Contains(r.RequestResponseId)).ToArray();
        if (unpaired.Length > 0)
        {
            sb.AppendLine($"<h3 class=\"warn\">⚠ {unpaired.Length} Unpaired Request(s)</h3><table><tr><th>Test</th><th>Service</th><th>Method</th><th>URI</th></tr>");
            foreach (var r in unpaired.Take(50))
                sb.AppendLine($"<tr><td>{Escape(r.TestName)}</td><td>{Escape(r.ServiceName)}</td><td>{r.Method.Value}</td><td>{Escape(r.Uri.ToString())}</td></tr>");
            sb.AppendLine("</table>");
        }

        // Orphaned test IDs
        if (features.Length > 0)
        {
            var scenarioIds = features.SelectMany(f => f.Scenarios).Select(s => s.Id).ToHashSet();
            var orphaned = distinctTestIds.Where(id => !scenarioIds.Contains(id)).ToArray();
            if (orphaned.Length > 0)
            {
                sb.AppendLine($"<h3 class=\"warn\">⚠ {orphaned.Length} Orphaned Test ID(s)</h3>");
                sb.AppendLine("<p>These test IDs appear in logs but don't match any feature scenario:</p><ul>");
                foreach (var id in orphaned.Take(50))
                {
                    var name = logs.FirstOrDefault(l => l.TestId == id)?.TestName ?? "?";
                    sb.AppendLine($"<li>{Escape(name)} ({Escape(id)})</li>");
                }
                sb.AppendLine("</ul>");
            }

            var testsWithNoDiagrams = features.SelectMany(f => f.Scenarios)
                .Where(s => !logs.Any(l => l.TestId == s.Id))
                .ToArray();
            if (testsWithNoDiagrams.Length > 0)
            {
                sb.AppendLine($"<h3 class=\"warn\">⚠ {testsWithNoDiagrams.Length} Scenario(s) with No Log Entries</h3><ul>");
                foreach (var s in testsWithNoDiagrams.Take(50))
                    sb.AppendLine($"<li>{Escape(s.DisplayName)}</li>");
                sb.AppendLine("</ul>");
            }
        }

        // Activity span summary
        var allSpans = InternalFlowSpanStore.GetSpans();
        sb.AppendLine($"<h2>Activity Spans</h2><p>{allSpans.Length} total span(s) in InternalFlowSpanStore.</p>");

        var sources = ActivitySourceDiscovery.GetDiscoveredSources();
        if (sources.Count > 0)
        {
            var tracked = options.InternalFlowActivitySources?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            sb.AppendLine("<h3>Discovered Activity Sources</h3><table><tr><th>Source</th><th>Spans</th><th>Status</th></tr>");
            foreach (var (name, count) in sources.OrderByDescending(s => s.Value))
            {
                var isTracked = tracked.Count == 0 || tracked.Contains(name);
                var wellKnown = InternalFlowSpanCollector.WellKnownAutoInstrumentationSources.Contains(name);
                var status = isTracked ? "<span class=\"info\">✓ tracked</span>" : wellKnown ? "well-known (auto)" : "not tracked";
                sb.AppendLine($"<tr><td>{Escape(name)}</td><td>{count}</td><td>{status}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        // Tracking components — grouped by ComponentName
        var allComponents = TrackingComponentRegistry.GetRegisteredComponents();
        if (allComponents.Count > 0)
        {
            sb.AppendLine("<h2>Tracking Components</h2>");

            var groups = allComponents
                .GroupBy(c => c.ComponentName)
                .OrderByDescending(g => g.Sum(c => c.InvocationCount))
                .ToArray();

            sb.AppendLine("<table><tr><th>Component</th><th>Instances</th><th>Total Invocations</th><th>Active</th><th>HttpContextAccessor</th></tr>");
            foreach (var g in groups)
            {
                var instances = g.ToArray();
                var totalInvocations = instances.Sum(c => c.InvocationCount);
                var activeCount = instances.Count(c => c.WasInvoked);
                var totalCount = instances.Length;
                var activeLabel = activeCount == totalCount
                    ? $"<span class=\"info\">{activeCount} of {totalCount}</span>"
                    : activeCount == 0
                        ? $"<span class=\"warn\">0 of {totalCount}</span>"
                        : $"{activeCount} of {totalCount}";

                // HttpContextAccessor status — pick from first instance
                var accessorStatus = instances[0].HasHttpContextAccessor
                    ? "<span class=\"info\">✓ configured</span>"
                    : instances.Any(c => c.InvocationCount > 0)
                        ? "<span class=\"warn\">⚠ null</span>"
                        : "—";

                if (totalCount == 1)
                {
                    sb.AppendLine($"<tr><td>{Escape(g.Key)}</td><td>1</td><td>{totalInvocations}</td><td>{activeLabel}</td><td>{accessorStatus}</td></tr>");
                }
                else
                {
                    sb.AppendLine($"<tr><td><details><summary>{Escape(g.Key)} ({totalCount} instances)</summary><table><tr><th>#</th><th>Invocations</th></tr>");
                    for (var i = 0; i < instances.Length; i++)
                        sb.AppendLine($"<tr><td>{i + 1}</td><td>{instances[i].InvocationCount}</td></tr>");
                    sb.AppendLine("</table></details></td>");
                    sb.AppendLine($"<td>{totalCount}</td><td>{totalInvocations}</td><td>{activeLabel}</td><td>{accessorStatus}</td></tr>");
                }
            }
            sb.AppendLine("</table>");

            // Smart "never invoked" warning — distinguish all-inactive types from some-inactive
            var fullyInactiveTypes = groups.Where(g => g.All(c => !c.WasInvoked)).ToArray();
            var partiallyInactiveTypes = groups.Where(g => g.Any(c => !c.WasInvoked) && g.Any(c => c.WasInvoked)).ToArray();

            if (fullyInactiveTypes.Length > 0)
            {
                sb.AppendLine($"<h3 class=\"warn\">⚠ {fullyInactiveTypes.Length} Component Type(s) Never Invoked</h3>");
                sb.AppendLine("<p>These component types have zero invocations across <b>all</b> instances — likely a misconfiguration.</p><ul>");
                foreach (var g in fullyInactiveTypes)
                    sb.AppendLine($"<li>{Escape(g.Key)} — {g.Count()} instance(s), 0 invocations</li>");
                sb.AppendLine("</ul>");
                sb.AppendLine("<p><b>Common causes:</b></p><ul>");
                sb.AppendLine("<li><b>EF Core:</b> The interceptor was added to <code>DbContextOptions&lt;TDerived&gt;</code> but the DbContext constructor accepts <code>DbContextOptions&lt;TBase&gt;</code> (e.g. Duende IdentityServer's ConfigurationDbContext). Fix: add <code>WithSqlTestTracking(sp)</code> inside the <code>ResolveDbContextOptions</code> implementation that runs at resolution time. <b>Note:</b> <code>PostConfigure</code> does not work with Duende IdentityServer because it registers store options as a direct singleton, not via <code>IOptions&lt;T&gt;</code>.</li>");
                sb.AppendLine("<li><b>HTTP:</b> The handler was added to an HttpClient that isn't being used by the target service.</li>");
                sb.AppendLine("<li><b>Redis:</b> An untracked IDatabase instance is being used instead of the tracked one.</li>");
                sb.AppendLine("</ul>");
            }

            if (partiallyInactiveTypes.Length > 0)
            {
                sb.AppendLine($"<h3>ℹ {partiallyInactiveTypes.Length} Component Type(s) with Some Inactive Instances</h3>");
                sb.AppendLine("<p>These types are active in some instances but not others — typically expected when using collection fixtures with uneven test distribution.</p><ul>");
                foreach (var g in partiallyInactiveTypes)
                {
                    var active = g.Count(c => c.WasInvoked);
                    sb.AppendLine($"<li>{Escape(g.Key)} — {active} of {g.Count()} instance(s) active</li>");
                }
                sb.AppendLine("</ul>");
            }
        }

        // Unmatched HTTP client names
        var unmatchedNames = UnmatchedClientNameRegistry.GetRecordedNames();
        if (unmatchedNames.Count > 0)
        {
            sb.AppendLine("<h2 class=\"warn\">⚠ Unmatched HTTP Client Names</h2>");
            sb.AppendLine("<p>These <code>clientName</code> values were passed to <code>TestTrackingMessageHandler</code> but did not match any key in <code>ClientNamesToServiceNames</code>. The handler fell back to port-based mapping.</p>");
            sb.AppendLine("<table><tr><th>Client Name</th><th>Requests</th></tr>");
            foreach (var (clientName, requestCount) in unmatchedNames)
                sb.AppendLine($"<tr><td>{Escape(clientName)}</td><td>{requestCount}</td></tr>");
            sb.AppendLine("</table>");
            sb.AppendLine("<p><b>Fix:</b> Add the exact client name to <code>ClientNamesToServiceNames</code>. For typed HttpClients registered via <code>services.AddHttpClient&lt;TClient&gt;()</code>, the client name is the <b>full type name</b> (e.g. <code>\"TenantHierarchyHttpClient\"</code>).</p>");
        }

        // Assertion value resolution diagnostics
        var diagLog = Track.DiagnosticLog;
        if (diagLog.Count > 0)
        {
            sb.AppendLine("<h2>Assertion Value Resolution</h2>");
            sb.AppendLine($"<p>{diagLog.Count} fallback(s) recorded during closure value resolution.</p>");
            sb.AppendLine("<table><tr><th>#</th><th>Details</th></tr>");
            var i = 0;
            foreach (var entry in diagLog)
            {
                i++;
                sb.AppendLine($"<tr><td>{i}</td><td>{Escape(entry)}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string key, object? value)
    {
        sb.AppendLine($"<tr><td><code>{Escape(key)}</code></td><td>{Escape(value?.ToString() ?? "<null>")}</td></tr>");
    }

    private static string Escape(string s) => System.Net.WebUtility.HtmlEncode(s);
}
