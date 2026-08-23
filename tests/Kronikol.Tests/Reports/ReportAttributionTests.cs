using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What used to reach the diagram and nothing else: why an assertion failed and where it lives in the
/// source, which example row was in flight, and which step a given call belongs to. All of it now has to
/// survive into the data file, because that is the file a debugging agent reads.
/// </summary>
[Collection("DiagramsFetcher")]
public class ReportAttributionTests
{
    // ─── §2.2 Failure detail on steps and assertions ───────────

    [Fact]
    public void A_failing_step_exports_its_message_and_source_location()
    {
        var features = WithSteps(new ScenarioStep
        {
            Keyword = "Then",
            Text = "the total is right",
            Status = ExecutionResult.Failed,
            FailureMessage = "Expected 4173 but found 3902",
            SourceFile = "OverviewTests.cs",
            SourceLine = 142
        });

        var step = FirstStep(Write(features, "Attr_failure.json"));

        Assert.Equal("Expected 4173 but found 3902", step.GetProperty("failureMessage").GetString());
        Assert.Equal("OverviewTests.cs", step.GetProperty("sourceFile").GetString());
        Assert.Equal(142, step.GetProperty("sourceLine").GetInt32());
    }

    [Fact]
    public void Failure_detail_survives_with_full_step_detail_turned_off()
    {
        // The smaller file is for saving payload bytes, not for hiding why a test failed.
        var features = WithSteps(new ScenarioStep
        {
            Keyword = "Then",
            Text = "the total is right",
            Status = ExecutionResult.Failed,
            FailureMessage = "boom"
        });

        var path = ReportGenerator.GenerateTestRunReportData(features, Start, End, "Attr_failure_lean.json",
            DataFormat.Json, fullStepDetail: false);

        Assert.Equal("boom", FirstStep(path).GetProperty("failureMessage").GetString());
    }

    [Fact]
    public void Collected_step_error_message_reaches_the_scenario_step()
    {
        var testId = Guid.NewGuid().ToString();
        StepCollector.StartStep(testId, "Then", "it works", null, null);
        StepCollector.CompleteStep(testId, passed: false, errorMessage: "assertion blew up");

        var steps = StepCollector.GetSteps(testId);

        Assert.Equal("assertion blew up", Assert.Single(steps).FailureMessage);
    }

    [Fact]
    public void A_tracked_assertion_carries_its_message_and_source_location()
    {
        var testId = Guid.NewGuid().ToString();
        StepCollector.StartStep(testId, "Then", "the total is right", null, null);
        StepCollector.AddAssertionSubStep(testId, "total == 4173", passed: false,
            failureMessage: "Expected 4173 but found 3902", sourceFile: "OverviewTests.cs", sourceLine: 142);
        StepCollector.CompleteStep(testId, passed: false);

        var assertion = Assert.Single(Assert.Single(StepCollector.GetSteps(testId)).SubSteps!);

        Assert.Equal("Expected 4173 but found 3902", assertion.FailureMessage);
        Assert.Equal("OverviewTests.cs", assertion.SourceFile);
        Assert.Equal(142, assertion.SourceLine);
    }

    // ─── §2.4 Annotations ──────────────────────────────────────

    [Fact]
    public void Marker_records_declare_what_kind_of_marker_they_are()
    {
        var testId = Guid.NewGuid().ToString();

        StepCollector.StartStep(testId, "Given", "a basket", null, null);
        DefaultTrackingDiagramOverride.InsertPlantUml(testId, "hnote across #lightyellow : Row 3", DiagramMarkerKind.Row);
        DefaultTrackingDiagramOverride.InsertPlantUml(testId, "note over api : anything");

        // Scoped to this test's own id rather than isolated by clearing the store: the store is
        // process-wide, so clearing it is not this test's to do — it wipes whatever another test is
        // mid-way through asserting on.
        var kinds = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.IsOverrideStart && l.PlantUml is not null)
            .Select(l => l.MarkerKind)
            .ToArray();

        Assert.Equal([DiagramMarkerKind.Step, DiagramMarkerKind.Row, DiagramMarkerKind.Custom], kinds);
    }

    [Fact]
    public void Row_and_custom_markers_export_as_scenario_annotations()
    {
        var logs = new[]
        {
            Marker("t1", DiagramMarkerKind.Row, "hnote across #lightyellow : Row 3"),
            Marker("t1", DiagramMarkerKind.Custom, "note over api : cache warmed"),
        };

        var annotations = Scenario(Write(Features(), "Attr_annotations.json", logs)).GetProperty("annotations");

        Assert.Equal(2, annotations.GetArrayLength());
        Assert.Equal("Row", annotations[0].GetProperty("kind").GetString());
        Assert.Equal("Row 3", annotations[0].GetProperty("text").GetString());
        Assert.Equal("Custom", annotations[1].GetProperty("kind").GetString());
        Assert.Equal("cache warmed", annotations[1].GetProperty("text").GetString());
    }

    [Fact]
    public void Step_and_assertion_markers_are_not_repeated_as_annotations()
    {
        // They are already structured in `steps`; repeating them is duplication, not disclosure.
        var logs = new[]
        {
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>Given a basket"),
            Marker("t1", DiagramMarkerKind.Assertion, "hnote across <<assertionNote>> #green\n✓ ok\nend note"),
        };

        var scenario = Scenario(Write(Features(), "Attr_annotations_none.json", logs));

        Assert.Equal(0, scenario.GetProperty("annotations").GetArrayLength());
    }

    // ─── §2.5 Step ↔ interaction attribution ───────────────────

    [Fact]
    public void Interactions_are_stamped_with_the_step_they_happened_under()
    {
        var logs = new[]
        {
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a basket"),
            Real("t1", "svc-a"),
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>checkout"),
            Real("t1", "svc-b"),
            Real("t1", "svc-c"),
        };

        var interactions = Interactions(Write(TwoStepFeature(), "Attr_steppath.json", logs));

        Assert.Equal("0", interactions[0].GetProperty("stepPath").GetString());
        Assert.Equal("1", interactions[1].GetProperty("stepPath").GetString());
        Assert.Equal("1", interactions[2].GetProperty("stepPath").GetString());
    }

    [Fact]
    public void Interactions_before_the_first_step_have_no_step_path()
    {
        var logs = new[]
        {
            Real("t1", "svc-a"),
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a basket"),
            Real("t1", "svc-b"),
        };

        var interactions = Interactions(Write(TwoStepFeature(), "Attr_steppath_before.json", logs));

        Assert.Equal(JsonValueKind.Null, interactions[0].GetProperty("stepPath").ValueKind);
        Assert.Equal("0", interactions[1].GetProperty("stepPath").GetString());
    }

    [Fact]
    public void More_step_markers_than_steps_leaves_the_path_null_rather_than_guessing()
    {
        var logs = new[]
        {
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a basket"),
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>checkout"),
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a third"),
            Real("t1", "svc-a"),
        };

        var interactions = Interactions(Write(TwoStepFeature(), "Attr_steppath_overrun.json", logs));

        Assert.Equal(JsonValueKind.Null, Assert.Single(interactions).GetProperty("stepPath").ValueKind);
    }

    [Fact]
    public void Background_steps_are_numbered_before_the_scenario_steps()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t1", DisplayName = "S",
                        BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "logged in" }],
                        Steps = [new ScenarioStep { Keyword = "When", Text = "checkout" }]
                    }
                ]
            }
        };
        var logs = new[]
        {
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>logged in"),
            Real("t1", "svc-a"),
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>checkout"),
            Real("t1", "svc-b"),
        };

        var interactions = Interactions(Write(features, "Attr_steppath_background.json", logs));

        Assert.Equal("b0", interactions[0].GetProperty("stepPath").GetString());
        Assert.Equal("0", interactions[1].GetProperty("stepPath").GetString());
    }

    // ─── Helpers ───────────────────────────────────────────────

    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

    private static RequestResponseLog Marker(string testId, DiagramMarkerKind kind, string plantUml) =>
        new(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsOverrideStart = true, PlantUml = plantUml, MarkerKind = kind };

    private static RequestResponseLog Real(string testId, string service) =>
        new("Test", testId, HttpMethod.Get, null, new Uri("http://" + service + "/x"), [], service, "api",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false);

    private static Feature[] Features() =>
    [
        new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = "t1", DisplayName = "S" }] }
    ];

    private static Feature[] TwoStepFeature() =>
    [
        new Feature
        {
            DisplayName = "F",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t1", DisplayName = "S",
                    Steps =
                    [
                        new ScenarioStep { Keyword = "Given", Text = "a basket" },
                        new ScenarioStep { Keyword = "When", Text = "checkout" }
                    ]
                }
            ]
        }
    ];

    private static Feature[] WithSteps(params ScenarioStep[] steps) =>
    [
        new Feature
        {
            DisplayName = "F",
            Scenarios = [new Scenario { Id = "t1", DisplayName = "S", Steps = steps }]
        }
    ];

    private static string Write(Feature[] features, string fileName, RequestResponseLog[]? logs = null) =>
        ReportGenerator.GenerateTestRunReportData(features, Start, End, fileName, DataFormat.Json, trackedLogs: logs);

    private static JsonElement Scenario(string path) =>
        JsonDocument.Parse(File.ReadAllText(path)).RootElement
            .GetProperty("features")[0].GetProperty("scenarios")[0];

    private static JsonElement FirstStep(string path) => Scenario(path).GetProperty("steps")[0];

    private static JsonElement[] Interactions(string path) =>
        Scenario(path).GetProperty("httpInteractions").EnumerateArray().ToArray();
}
