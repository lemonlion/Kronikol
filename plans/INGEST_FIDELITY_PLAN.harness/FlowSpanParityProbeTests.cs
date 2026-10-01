using System.Diagnostics;
using System.Net;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// TEMPORARY probe for plans/INGEST_FIDELITY_PLAN.md R4 (kept in the plan's harness, deleted from the test project after
/// the run): an in-process report with every internal-flow surface on, written to $KRONIKOL_PROBE_OUT, run once before
/// internal flow moves to FlowSpan and once after, the two outputs compared with their random ids normalised.
/// </summary>
[Collection("DiagramsFetcher")]
public class FlowSpanParityProbeTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow.AddMinutes(-5);

    [Fact]
    public void Writes_an_in_process_report_with_every_internal_flow_surface()
    {
        var output = Environment.GetEnvironmentVariable("KRONIKOL_PROBE_OUT");
        if (string.IsNullOrEmpty(output))
            return;

        var sourceName = "Kronikol.Probe.FlowSpanParity";
        using var source = new ActivitySource(sourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        var spans = new List<Activity>();
        var tick = 0;

        Activity Span(string name, ActivityTraceId trace, ActivitySpanId parent, DateTimeOffset start, int ms)
        {
            Activity.Current = null;
            var span = source.StartActivity(name, ActivityKind.Internal, new ActivityContext(trace, parent, ActivityTraceFlags.Recorded))!;
            // Every span its own instant, so no order depends on a hash.
            span.SetStartTime(start.UtcDateTime.AddTicks(++tick));
            span.SetEndTime(start.UtcDateTime.AddTicks(tick).AddMilliseconds(ms));
            spans.Add(span);
            return span;
        }

        var tests = new[] { ("probe-a", "Orders"), ("probe-b", "Payments"), ("probe-c", "Orders") };
        var features = new List<Feature>();
        var at = T0;
        foreach (var (testId, service) in tests)
        {
            for (var call = 0; call < 3; call++)
            {
                var trace = ActivityTraceId.CreateRandom();
                var root = Span($"{service} handler {call}", trace, default, at.AddMilliseconds(1), 40);
                var child = Span($"SELECT {service.ToLowerInvariant()} {call}", trace, root.SpanId, at.AddMilliseconds(5), 10);
                Span($"cache get {call}", trace, child.SpanId, at.AddMilliseconds(6), 2);
                Span($"publish {call}", trace, root.SpanId, at.AddMilliseconds(20), 5);
                var rr = Guid.NewGuid();
                RequestResponseLogger.Log(new RequestResponseLog(testId, testId, HttpMethod.Post, "{\"n\":" + call + "}", new Uri($"http://{service.ToLowerInvariant()}/call/{call}"), [],
                    service, "Caller", RequestResponseType.Request, Guid.NewGuid(), rr, false)
                    { Timestamp = at, ActivityTraceId = trace.ToString(), ActivitySpanId = root.SpanId.ToString() });
                RequestResponseLogger.Log(new RequestResponseLog(testId, testId, HttpMethod.Post, "{}", new Uri($"http://{service.ToLowerInvariant()}/call/{call}"), [],
                    service, "Caller", RequestResponseType.Response, Guid.NewGuid(), rr, false, HttpStatusCode.OK)
                    { Timestamp = at.AddMilliseconds(50), ActivityTraceId = trace.ToString(), ActivitySpanId = root.SpanId.ToString() });
                at = at.AddMilliseconds(100);
            }

            // A call with no trace id, which takes what its test's traced calls may.
            var untraced = Guid.NewGuid();
            RequestResponseLogger.Log(new RequestResponseLog(testId, testId, HttpMethod.Get, null, new Uri($"http://db/{testId}"), [],
                "Database", service, RequestResponseType.Request, Guid.NewGuid(), untraced, false) { Timestamp = at.AddMilliseconds(-95) });
            RequestResponseLogger.Log(new RequestResponseLog(testId, testId, HttpMethod.Get, "[]", new Uri($"http://db/{testId}"), [],
                "Database", service, RequestResponseType.Response, Guid.NewGuid(), untraced, false, HttpStatusCode.OK) { Timestamp = at.AddMilliseconds(-60) });
            features.Add(new Feature { DisplayName = "Feature " + testId, Scenarios = [new Scenario { Id = testId, DisplayName = "Scenario " + testId, Result = ExecutionResult.Passed }] });
        }

        // A span no call claims.
        Span("startup", ActivityTraceId.CreateRandom(), default, T0.AddMilliseconds(-30), 3);
        foreach (var span in spans)
            InternalFlowSpanStore.Add(span);

        foreach (var (style, name) in new[] { (InternalFlowDiagramStyle.ActivityDiagram, "activity"), (InternalFlowDiagramStyle.CallTree, "calltree") })
        {
            DefaultDiagramsFetcher.Reset();
            ReportGenerator.CreateStandardReportsWithDiagrams(features.ToArray(), T0.UtcDateTime.AddSeconds(-1), at.UtcDateTime.AddSeconds(1),
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = Path.Combine(output, name),
                    PlantUmlRendering = PlantUmlRendering.BrowserJs,
                    InternalFlowTracking = true,
                    InternalFlowSpanGranularity = InternalFlowSpanGranularity.Manual,
                    InternalFlowActivitySources = [sourceName],
                    InternalFlowDiagramStyle = style,
                    InternalFlowShowFlameChart = true,
                    WholeTestFlowVisualization = WholeTestFlowVisualization.Both,
                    GenerateComponentDiagram = true,
                    GenerateMergeableData = true,
                    WriteRunSummaryToConsole = false,
                    CompressTestRunReportPayloads = false,
                });
            DefaultDiagramsFetcher.Reset();
        }
    }
}
