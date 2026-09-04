using System.Diagnostics;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The TOGGLE_DEFAULTS_PLAN M2 byte-identity pin: on identical inputs, generating a report with
/// (a) no toggleDefaults, (b) <see cref="ResolvedToggleDefaults.BuiltIn"/>, and (c) the defaults
/// resolved from a fresh options record must produce byte-identical HTML for both report shapes —
/// "unset config = zero effect", forever. The rich fixture exercises the report toolbar, all five
/// scenario diagram-toolbar sites (seq-only, seq+flow, flow-only, and both parameterized-group
/// variants), rules, failure clusters, diagnostics and whole-test-flow views. (The transient
/// pre/post-refactor capture that proved the M2 five-string extraction inert used this same
/// fixture and was discarded per the plan.)
/// </summary>
public class ToggleDefaultsBaselineTests
{
    public static (DefaultDiagramsFetcher.DiagramAsCode[] Diagrams, Feature[] SpecFeatures, Feature[] TestRunFeatures,
        Dictionary<string, InternalFlowSegment> Segments, IDisposable Cleanup) BuildRichFixture()
    {
        const string noteDiagram = """
            @startuml
            actor "Caller" as caller
            participant "OrderService" as svc
            database "OrderDb" as db

            hnote across #black <<stepDelimiter>>: <color:white>Given an order request</color>
            caller -> svc : POST /api/orders
            note left
            <color:gray>[content-type=application/json]</color>

            {
              "query": "SELECT o.id,\nFROM orders o",
              "id": 42
            }
            end note
            svc -> db : INSERT INTO orders
            db --> svc : 1 row
            svc --> caller : 200 OK
            note right <<assertionNote>>: ✓ status code should be OK
            @enduml
            """;

        const string simpleDiagram = """
            @startuml
            actor "Caller" as caller
            participant "Svc" as svc
            caller -> svc : GET /health
            svc --> caller : 200 OK
            @enduml
            """;

        var specFeatures = new List<Feature>
        {
            new()
            {
                DisplayName = "Ordering Feature",
                Description = "Orders are placed and confirmed",
                Endpoint = "/api/orders",
                Labels = ["team-a"],
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Alpha creates an order", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(1200),
                        Categories = ["smoke"], Labels = ["fast"],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "an order request", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "the order is confirmed", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Beta with internal flow",
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(600),
                        Steps = [new ScenarioStep { Keyword = "When", Text = "the flow runs", Status = ExecutionResult.Passed }]
                    },
                    new Scenario
                    {
                        Id = "s3", DisplayName = "Gamma flow only",
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(300),
                        Steps = [new ScenarioStep { Keyword = "When", Text = "only spans exist", Status = ExecutionResult.Passed }]
                    },
                    new Scenario
                    {
                        Id = "s4", DisplayName = "Delta under a rule", Rule = "Order rules",
                        Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "the rule holds", Status = ExecutionResult.Passed }]
                    }
                ]
            },
            new()
            {
                DisplayName = "Parameterized Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "p1", DisplayName = "Calc(a: 1, b: 2)", OutlineId = "Calc",
                        ExampleValues = new() { ["a"] = "1", ["b"] = "2" },
                        ExampleFlatValues = new() { ["a"] = "1", ["b"] = "2" },
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(80),
                        Steps = [new ScenarioStep { Keyword = "When", Text = "calculating", Status = ExecutionResult.Passed }]
                    },
                    new Scenario
                    {
                        Id = "p2", DisplayName = "Calc(a: 3, b: 4)", OutlineId = "Calc",
                        ExampleValues = new() { ["a"] = "3", ["b"] = "4" },
                        ExampleFlatValues = new() { ["a"] = "3", ["b"] = "4" },
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(90),
                        Steps = [new ScenarioStep { Keyword = "When", Text = "calculating", Status = ExecutionResult.Passed }]
                    },
                    new Scenario
                    {
                        Id = "q1", DisplayName = "Fetch(key: x)", OutlineId = "Fetch",
                        ExampleValues = new() { ["key"] = "x" },
                        ExampleFlatValues = new() { ["key"] = "x" },
                        Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "When", Text = "fetching", Status = ExecutionResult.Passed }]
                    },
                    new Scenario
                    {
                        Id = "q2", DisplayName = "Fetch(key: y)", OutlineId = "Fetch",
                        ExampleValues = new() { ["key"] = "y" },
                        ExampleFlatValues = new() { ["key"] = "y" },
                        Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "When", Text = "fetching", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };

        var testRunFeatures = new List<Feature>(specFeatures)
        {
            new()
            {
                DisplayName = "Failing Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "f1", DisplayName = "Something breaks",
                        Result = ExecutionResult.Failed,
                        ErrorMessage = "Expected OK but got Error",
                        ErrorStackTrace = "at Example.Test() in Example.cs:line 42",
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "it fails", Status = ExecutionResult.Failed }]
                    },
                    new Scenario
                    {
                        // Same normalized error → the two failures form a failure cluster.
                        Id = "f2", DisplayName = "Something else breaks",
                        Result = ExecutionResult.Failed,
                        ErrorMessage = "Expected OK but got Error",
                        ErrorStackTrace = "at Example.Other() in Example.cs:line 77",
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "it fails too", Status = ExecutionResult.Failed }]
                    }
                ]
            }
        };

        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode("s1", "", noteDiagram),
            new DefaultDiagramsFetcher.DiagramAsCode("s2", "", simpleDiagram),
            new DefaultDiagramsFetcher.DiagramAsCode("s4", "", simpleDiagram),
            new DefaultDiagramsFetcher.DiagramAsCode("p1", "", simpleDiagram),
            new DefaultDiagramsFetcher.DiagramAsCode("p2", "", simpleDiagram),
            new DefaultDiagramsFetcher.DiagramAsCode("q1", "", simpleDiagram),
            new DefaultDiagramsFetcher.DiagramAsCode("q2", "", simpleDiagram),
            new DefaultDiagramsFetcher.DiagramAsCode("f1", "", simpleDiagram)
        };

        // Whole-test-flow segments for s2 (seq + flow), s3 (flow only) and p1/p2 (param + flow).
        var activitySource = new ActivitySource("Kronikol.Tests.ToggleDefaults.Baseline");
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        Activity.Current = null;
        var baseTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var allSpans = new List<Activity>();
        Activity[] MakeSpans(string rootName)
        {
            var root = activitySource.StartActivity(rootName, ActivityKind.Server)!;
            root.SetStartTime(baseTime);
            root.SetEndTime(baseTime.AddMilliseconds(500));
            var rootCtx = new ActivityContext(root.TraceId, root.SpanId, ActivityTraceFlags.Recorded);
            var child = activitySource.StartActivity("EF Core: SELECT Orders", ActivityKind.Internal, rootCtx)!;
            child.SetStartTime(baseTime.AddMilliseconds(20));
            child.SetEndTime(baseTime.AddMilliseconds(180));
            var child2 = activitySource.StartActivity("HttpClient: PUT /stock", ActivityKind.Internal, rootCtx)!;
            child2.SetStartTime(baseTime.AddMilliseconds(200));
            child2.SetEndTime(baseTime.AddMilliseconds(450));
            allSpans.AddRange([root, child, child2]);
            return [root, child, child2];
        }

        var segments = new Dictionary<string, InternalFlowSegment>();
        foreach (var testId in new[] { "s2", "s3", "p1", "p2" })
        {
            var spans = MakeSpans($"HTTP POST /api/{testId}");
            segments[$"iflow-test-{testId}"] = new InternalFlowSegment(
                Guid.Empty, RequestResponseType.Request, testId,
                baseTime, baseTime.AddMilliseconds(500), spans);
        }

        var cleanup = new FixtureCleanup(activitySource, listener, allSpans);
        return (diagrams, specFeatures.ToArray(), testRunFeatures.ToArray(), segments, cleanup);
    }

    private sealed class FixtureCleanup(ActivitySource source, ActivityListener listener, List<Activity> spans) : IDisposable
    {
        public void Dispose()
        {
            foreach (var s in spans) s.Dispose();
            listener.Dispose();
            source.Dispose();
        }
    }

    public static string GenerateSpecShape(DefaultDiagramsFetcher.DiagramAsCode[] diagrams, Feature[] features,
        Dictionary<string, InternalFlowSegment> segments, string fileName, ResolvedToggleDefaults? toggleDefaults = null)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            new DateTime(2026, 1, 2, 3, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 3, 10, 0, DateTimeKind.Utc),
            Stylesheets.VioletThemeStyleSheet, fileName, "Service Specifications", false,
            generateBlankOnFailedTests: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: true,
            wholeTestSegments: segments,
            wholeTestVisualization: WholeTestFlowVisualization.Both,
            showStepNumbers: true,
            toggleDefaults: toggleDefaults);
        return path;
    }

    public static string GenerateTestRunShape(DefaultDiagramsFetcher.DiagramAsCode[] diagrams, Feature[] features,
        Dictionary<string, InternalFlowSegment> segments, string fileName, ResolvedToggleDefaults? toggleDefaults = null)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            new DateTime(2026, 1, 2, 3, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 3, 10, 0, DateTimeKind.Utc),
            null, fileName, "Test Run Report", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: true,
            wholeTestSegments: segments,
            wholeTestVisualization: WholeTestFlowVisualization.Both,
            diagnostics: [new DiagnosticEntry(DiagnosticKind.OutputFailure, "example diagnostic", null)],
            toggleDefaults: toggleDefaults);
        return path;
    }

    [Fact]
    public void Unset_config_produces_byte_identical_output_across_null_builtin_and_resolved()
    {
        var (diagrams, specFeatures, testRunFeatures, segments, cleanup) = BuildRichFixture();
        using (cleanup)
        {
            var resolvedSpec = ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions(), specifications: true);
            var resolvedTestRun = ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions(), specifications: false);

            var specA = File.ReadAllText(GenerateSpecShape(diagrams, specFeatures, segments, "ToggleBaseline_Spec_A.html"));
            var specB = File.ReadAllText(GenerateSpecShape(diagrams, specFeatures, segments, "ToggleBaseline_Spec_B.html", ResolvedToggleDefaults.BuiltIn));
            var specC = File.ReadAllText(GenerateSpecShape(diagrams, specFeatures, segments, "ToggleBaseline_Spec_C.html", resolvedSpec));
            Assert.Equal(specA, specB);
            Assert.Equal(specA, specC);

            var runA = File.ReadAllText(GenerateTestRunShape(diagrams, testRunFeatures, segments, "ToggleBaseline_Run_A.html"));
            var runB = File.ReadAllText(GenerateTestRunShape(diagrams, testRunFeatures, segments, "ToggleBaseline_Run_B.html", ResolvedToggleDefaults.BuiltIn));
            var runC = File.ReadAllText(GenerateTestRunShape(diagrams, testRunFeatures, segments, "ToggleBaseline_Run_C.html", resolvedTestRun));
            Assert.Equal(runA, runB);
            Assert.Equal(runA, runC);
        }
    }
}
