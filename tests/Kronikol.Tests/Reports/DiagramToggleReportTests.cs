using System.Diagnostics;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

public class DiagramToggleReportTests : IDisposable
{
    private readonly ActivitySource _source = new("Tests.DiagramToggle");
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public DiagramToggleReportTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        foreach (var a in _activities) a.Dispose();
        _listener.Dispose();
        _source.Dispose();
    }

    private Activity CreateSpan(string name)
    {
        var span = _source.StartActivity(name)!;
        span.SetEndTime(span.StartTimeUtc + TimeSpan.FromMilliseconds(50));
        _activities.Add(span);
        return span;
    }

    private static Feature[] MakeFeatures(string testId = "t1", string name = "Test scenario") =>
    [
        new Feature
        {
            DisplayName = "Test Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = testId,
                    DisplayName = name,
                    IsHappyPath = true,
                    Result = ExecutionResult.Passed
                }
            ]
        }
    ];

    private static DefaultDiagramsFetcher.DiagramAsCode[] MakeDiagrams(string testId = "t1") =>
    [
        new(testId, "", "@startuml\nA->B: call\n@enduml")
    ];

    private Dictionary<string, InternalFlowSegment> MakeWholeTestSegments(string testId = "t1")
    {
        var span = CreateSpan("operation");
        return new Dictionary<string, InternalFlowSegment>
        {
            [$"iflow-test-{testId}"] = new(Guid.Empty, RequestResponseType.Request, testId,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMilliseconds(50), [span])
        };
    }

    private string GenerateReport(
        DefaultDiagramsFetcher.DiagramAsCode[] diagrams,
        Feature[] features,
        string fileName,
        Dictionary<string, InternalFlowSegment>? wholeTestSegments = null,
        WholeTestFlowVisualization visualization = WholeTestFlowVisualization.Both)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: wholeTestSegments is not null,
            wholeTestSegments: wholeTestSegments,
            wholeTestVisualization: visualization);
        return ReportHtml.Read(path);
    }

    [Fact]
    public void Report_with_diagrams_only_shows_sequence_diagrams_summary()
    {
        var content = GenerateReport(MakeDiagrams(), MakeFeatures(), "ToggleSeqOnly.html");

        Assert.Contains("<summary class=\"h4\" title=\"This scenario's sequence diagrams\">Sequence Diagrams<button type=\"button\" class=\"scenario-diagram-controls-toggle gear-in-summary\"", content);
        Assert.DoesNotContain("data-dtype=", content);
    }

    [Fact]
    public void Report_with_diagrams_and_whole_test_flow_shows_toggle_buttons()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleBoth.html",
            wholeTestSegments: MakeWholeTestSegments());

        Assert.Contains("diagram-toggle", content);
        Assert.Contains("data-dtype=\"seq\"", content);
        Assert.Contains("data-dtype=\"activity\"", content);
        Assert.Contains("data-dtype=\"flame\"", content);
        Assert.Contains(">Sequence Diagrams</button>", content);
        Assert.Contains(">Activity Diagrams</button>", content);
        Assert.Contains(">Flame Chart</button>", content);
    }

    [Fact]
    public void Report_with_diagrams_and_whole_test_flow_shows_diagrams_summary()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleSummary.html",
            wholeTestSegments: MakeWholeTestSegments());

        Assert.Contains("<summary class=\"h4\" title=\"This scenario's diagrams\">Diagrams<button type=\"button\" class=\"scenario-diagram-controls-toggle gear-in-summary\"", content);
        Assert.DoesNotContain(">Sequence Diagrams<button type=\"button\" class=\"scenario-diagram-controls-toggle", content);
    }

    [Fact]
    public void Report_with_diagrams_and_whole_test_flow_has_sequence_diagrams_active_by_default()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleDefault.html",
            wholeTestSegments: MakeWholeTestSegments());

        Assert.Contains("diagram-toggle-active\" role=\"tab\" aria-selected=\"true\" tabindex=\"0\" data-dtype=\"seq\"", content);
    }

    [Fact]
    public void Report_with_diagrams_and_whole_test_flow_wraps_sequence_in_view_div()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleSeqView.html",
            wholeTestSegments: MakeWholeTestSegments());

        Assert.Contains("diagram-view-seq", content);
        Assert.Contains("diagram-view-activity", content);
        Assert.Contains("diagram-view-flame", content);
    }

    [Fact]
    public void Report_with_diagrams_and_whole_test_flow_hides_activity_and_flame_by_default()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleHidden.html",
            wholeTestSegments: MakeWholeTestSegments());

        Assert.Contains("diagram-view-activity\" style=\"display:none\"", content);
        Assert.Contains("diagram-view-flame\" style=\"display:none\"", content);
        Assert.DoesNotContain("diagram-view-seq\" style=\"display:none\"", content);
    }

    [Fact]
    public void Report_with_flame_chart_only_shows_two_toggle_buttons()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleFlameOnly.html",
            wholeTestSegments: MakeWholeTestSegments(),
            visualization: WholeTestFlowVisualization.FlameChart);

        Assert.Contains("data-dtype=\"seq\"", content);
        Assert.Contains("data-dtype=\"flame\"", content);
        Assert.DoesNotContain("data-dtype=\"activity\"", content);
    }

    [Fact]
    public void Report_with_activity_only_shows_two_toggle_buttons()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleActivityOnly.html",
            wholeTestSegments: MakeWholeTestSegments(),
            visualization: WholeTestFlowVisualization.ActivityDiagram);

        Assert.Contains("data-dtype=\"seq\"", content);
        Assert.Contains("data-dtype=\"activity\"", content);
        Assert.DoesNotContain("data-dtype=\"flame\"", content);
    }

    [Fact]
    public void Report_no_diagrams_no_whole_test_flow_has_no_diagram_section()
    {
        var content = GenerateReport(
            Array.Empty<DefaultDiagramsFetcher.DiagramAsCode>(),
            MakeFeatures(), "ToggleNone.html");

        Assert.DoesNotContain("<details class=\"example-diagrams\"", content);
        Assert.DoesNotContain("<div class=\"diagram-toggle\"", content);
    }

    [Fact]
    public void Report_no_whole_test_flow_data_for_visualization_Both_still_shows_sequence_only()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleNoFlow.html",
            wholeTestSegments: new Dictionary<string, InternalFlowSegment>(),
            visualization: WholeTestFlowVisualization.Both);

        Assert.Contains("<summary class=\"h4\" title=\"This scenario's sequence diagrams\">Sequence Diagrams<button type=\"button\" class=\"scenario-diagram-controls-toggle gear-in-summary\"", content);
        Assert.DoesNotContain("data-dtype=", content);
    }

    [Fact]
    public void Report_whole_test_flow_only_no_sequence_diagrams_shows_diagrams_summary()
    {
        var content = GenerateReport(
            Array.Empty<DefaultDiagramsFetcher.DiagramAsCode>(),
            MakeFeatures(), "ToggleFlowOnly.html",
            wholeTestSegments: MakeWholeTestSegments());

        Assert.Contains("Diagrams</summary>", content);
        Assert.Contains("diagram-toggle", content);
        Assert.DoesNotContain("data-dtype=\"seq\"", content);
    }

    [Fact]
    public void Report_does_not_contain_whole_test_flow_as_separate_section()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleNoWTFSection.html",
            wholeTestSegments: MakeWholeTestSegments());

        Assert.DoesNotContain("<details class=\"whole-test-flow\"", content);
        Assert.DoesNotContain("Whole Test Flow", content);
    }

    [Fact]
    public void Report_contains_diagram_toggle_css()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleCss.html",
            wholeTestSegments: MakeWholeTestSegments());

        Assert.Contains("diagram-toggle-btn", content);
        Assert.Contains("diagram-toggle-active", content);
    }

    [Fact]
    public void Report_contains_diagram_toggle_javascript()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleJs.html",
            wholeTestSegments: MakeWholeTestSegments());

        Assert.Contains("diagram-toggle-btn", content);
        Assert.Contains("data-dtype", content);
    }

    private Dictionary<string, InternalFlowSegment> MakeWholeTestSegmentsWithSpanCount(string testId, int spanCount)
    {
        var spans = new List<Activity>();
        for (int i = 0; i < spanCount; i++)
        {
            Activity.Current = null;
            var s = _source.StartActivity($"op{i}", ActivityKind.Internal,
                new ActivityContext(ActivityTraceId.CreateRandom(), default, ActivityTraceFlags.Recorded))!;
            s.SetEndTime(s.StartTimeUtc + TimeSpan.FromMilliseconds(10));
            _activities.Add(s);
            spans.Add(s);
        }
        return new Dictionary<string, InternalFlowSegment>
        {
            [$"iflow-test-{testId}"] = new(Guid.Empty, RequestResponseType.Request, testId,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMilliseconds(50), spans.ToArray())
        };
    }

    private static Feature[] MakeTwoScenarioFeatures() =>
    [
        new Feature
        {
            DisplayName = "Test Feature",
            Scenarios =
            [
                new Scenario { Id = "t1", DisplayName = "Normal test", IsHappyPath = true, Result = ExecutionResult.Passed },
                new Scenario { Id = "t2", DisplayName = "Outlier test", IsHappyPath = true, Result = ExecutionResult.Passed }
            ]
        }
    ];

    [Fact]
    public void Report_shows_span_count_warning_when_10x_median_and_over_100()
    {
        var features = MakeTwoScenarioFeatures();
        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode("t1", "", "@startuml\nA->B: call\n@enduml"),
            new DefaultDiagramsFetcher.DiagramAsCode("t2", "", "@startuml\nA->B: call\n@enduml")
        };

        // t1 has 10 spans (median), t2 has 150 spans (15x median, >100)
        var segments = MakeWholeTestSegmentsWithSpanCount("t1", 10);
        foreach (var kv in MakeWholeTestSegmentsWithSpanCount("t2", 150))
            segments[kv.Key] = kv.Value;

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "ToggleSpanWarning.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: true,
            wholeTestSegments: segments,
            wholeTestVisualization: WholeTestFlowVisualization.Both);
        var content = ReportHtml.Read(path);

        Assert.Contains("<span class=\"span-count-warning\">", content);
        Assert.Contains("150 spans", content);
        Assert.Contains("might indicate a problem", content);
    }

    // 4.x wrote the warning only beside tabs, so a scenario with one view of its flow and no sequence diagrams lost it.
    [Fact]
    public void A_flow_with_one_view_and_no_sequence_diagrams_shows_the_span_warning_on_its_title()
    {
        var features = MakeTwoScenarioFeatures();
        var diagrams = new[] { new DefaultDiagramsFetcher.DiagramAsCode("t1", "", "@startuml\nA->B: call\n@enduml") };
        var segments = MakeWholeTestSegmentsWithSpanCount("t1", 10);
        foreach (var kv in MakeWholeTestSegmentsWithSpanCount("t2", 150))
            segments[kv.Key] = kv.Value;

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "ToggleSpanWarningOneView.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: true,
            wholeTestSegments: segments,
            wholeTestVisualization: WholeTestFlowVisualization.ActivityDiagram);
        var content = ReportHtml.Read(path);

        Assert.Contains("<summary class=\"h4\" title=\"The work inside the services, span by span, as an activity diagram\">Activity Diagrams<span class=\"span-count-warning\">(Warning: 150 spans.", content);
    }

    [Fact]
    public void The_span_warning_sits_on_the_tabs_bar_where_there_are_tabs()
    {
        var features = MakeTwoScenarioFeatures();
        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode("t1", "", "@startuml\nA->B: call\n@enduml"),
            new DefaultDiagramsFetcher.DiagramAsCode("t2", "", "@startuml\nA->B: call\n@enduml")
        };
        var segments = MakeWholeTestSegmentsWithSpanCount("t1", 10);
        foreach (var kv in MakeWholeTestSegmentsWithSpanCount("t2", 150))
            segments[kv.Key] = kv.Value;

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "ToggleSpanWarningTabs.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: true,
            wholeTestSegments: segments,
            wholeTestVisualization: WholeTestFlowVisualization.Both);
        var content = ReportHtml.Read(path);

        Assert.Contains("data-dtype=\"flame\" title=\"Where the time went inside the services, span by span, as nested bars\">Flame Chart</button><span class=\"span-count-warning\">(Warning: 150 spans.", content);
    }

    [Fact]
    public void Report_no_span_warning_when_10x_median_but_under_100()
    {
        var features = MakeTwoScenarioFeatures();
        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode("t1", "", "@startuml\nA->B: call\n@enduml"),
            new DefaultDiagramsFetcher.DiagramAsCode("t2", "", "@startuml\nA->B: call\n@enduml")
        };

        // t1 has 5 spans (median), t2 has 50 spans (10x median, but <=100)
        var segments = MakeWholeTestSegmentsWithSpanCount("t1", 5);
        foreach (var kv in MakeWholeTestSegmentsWithSpanCount("t2", 50))
            segments[kv.Key] = kv.Value;

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "ToggleSpanWarningUnder100.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: true,
            wholeTestSegments: segments,
            wholeTestVisualization: WholeTestFlowVisualization.Both);
        var content = ReportHtml.Read(path);

        Assert.DoesNotContain("<span class=\"span-count-warning\"", content);
    }

    [Fact]
    public void Report_no_span_warning_when_below_threshold()
    {
        var content = GenerateReport(
            MakeDiagrams(), MakeFeatures(), "ToggleNoWarning.html",
            wholeTestSegments: MakeWholeTestSegments());

        Assert.DoesNotContain("<span class=\"span-count-warning\"", content);
    }
}
