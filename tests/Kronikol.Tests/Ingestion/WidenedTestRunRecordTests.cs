using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// The widened tests NDJSON: everything a Gherkin runner knows — feature and scenario descriptions,
/// rules, tags, outlines, backgrounds, doc-strings, data tables, stack traces — reaching the report
/// without a Cucumber Messages file.
/// </summary>
[Collection("DiagramsFetcher")] // shares the process-global tracking store with every other class that touches it
public class WidenedTestRunRecordTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    private static Scenario Build(params TestRunRecord[] records) =>
        FeatureSynthesizer.Build(records, null).Features[0].Scenarios[0];

    [Fact]
    public void Every_new_field_round_trips_through_ndjson()
    {
        var record = new TestRunRecord
        {
            Event = "start",
            TestId = "t",
            FeatureDescription = "The overview page summarises the account.",
            Description = "A returning customer opens the overview.",
            Rule = "Only signed-in customers see figures",
            Tags = ["@gemma", "@category:smoke", "@endpoint:/overview", "@happy-path"],
            OutlineId = "Overview renders for <plan>",
            ExampleValues = new Dictionary<string, string> { ["plan"] = "pro" },
            Timestamp = T0,
        };

        // Collections compare by reference in a record's generated equality, so they are checked by value.
        var roundTripped = TestRunRecord.FromJson(record.ToJson());
        Assert.Equal(record with { Tags = null, ExampleValues = null }, roundTripped with { Tags = null, ExampleValues = null });
        Assert.Equal(record.Tags, roundTripped.Tags);
        Assert.Equal(record.ExampleValues, roundTripped.ExampleValues);

        var step = new TestRunRecord
        {
            Event = "step",
            TestId = "t",
            Text = "the customers are listed",
            Keyword = "And",
            KeywordType = "Context",
            Background = true,
            DocString = "{ \"plan\": \"pro\" }",
            DocStringMediaType = "json",
            Table = [["name", "plan"], ["ada", "pro"]],
            StackTrace = "at Overview.Render()",
            BypassReason = "the backend is down",
            Timestamp = T0,
        };

        var roundTrippedStep = TestRunRecord.FromJson(step.ToJson());
        Assert.Equal(step with { Table = null }, roundTrippedStep with { Table = null });
        Assert.Equal(step.Table, roundTrippedStep.Table);
    }

    [Fact]
    public void The_start_record_supplies_description_rule_outline_and_example_values()
    {
        var scenario = Build(
            new TestRunRecord
            {
                Event = "start", TestId = "t", TestName = "overview renders", Feature = "overview.feature",
                Description = "A returning customer opens the overview.",
                Rule = "Only signed-in customers see figures",
                OutlineId = "Overview renders for <plan>",
                ExampleValues = new Dictionary<string, string> { ["plan"] = "pro" },
                Timestamp = T0,
            },
            new TestRunRecord { Event = "end", TestId = "t", Status = "passed", Timestamp = T0.AddSeconds(1) });

        Assert.Equal("A returning customer opens the overview.", scenario.Description);
        Assert.Equal("Only signed-in customers see figures", scenario.Rule);
        Assert.Equal("Overview renders for <plan>", scenario.OutlineId);
        Assert.Equal("pro", scenario.ExampleValues!["plan"]);
        // The flattened and raw views drive the pivot table's columns and value rendering.
        Assert.Equal("pro", scenario.ExampleFlatValues!["plan"]);
        Assert.Equal("pro", scenario.ExampleRawValues!["plan"]);
    }

    [Fact]
    public void Tags_follow_the_req_n_roll_conventions()
    {
        var scenario = Build(
            new TestRunRecord
            {
                Event = "start", TestId = "t", TestName = "overview", Feature = "overview.feature",
                Tags = ["@gemma", "@category:smoke", "@category:ui", "@endpoint:/overview", "@happy_path"],
                Timestamp = T0,
            },
            new TestRunRecord { Event = "end", TestId = "t", Status = "passed", Timestamp = T0.AddSeconds(1) });

        Assert.Equal(["gemma"], scenario.Labels!);
        Assert.Equal(["smoke", "ui"], scenario.Categories!);
        Assert.True(scenario.IsHappyPath);
    }

    [Fact]
    public void A_tag_carried_by_every_scenario_of_a_feature_is_the_feature_s_own()
    {
        // In Gherkin a feature tag is inherited by every scenario, so the intersection recovers it.
        var features = FeatureSynthesizer.Build(
        [
            new TestRunRecord { Event = "start", TestId = "a", TestName = "a", Feature = "overview.feature", Tags = ["@gemma", "@slow"], Timestamp = T0 },
            new TestRunRecord { Event = "start", TestId = "b", TestName = "b", Feature = "overview.feature", Tags = ["@gemma", "@endpoint:/overview"], Timestamp = T0 },
        ], null).Features;

        var feature = Assert.Single(features);
        Assert.Equal(["gemma"], feature.Labels!);
        Assert.Equal("/overview", feature.Endpoint);
    }

    [Fact]
    public void The_feature_description_comes_from_the_first_scenario_that_carries_one()
    {
        var feature = Assert.Single(FeatureSynthesizer.Build(
        [
            new TestRunRecord { Event = "start", TestId = "a", TestName = "a", Feature = "overview.feature", Timestamp = T0 },
            new TestRunRecord
            {
                Event = "start", TestId = "b", TestName = "b", Feature = "overview.feature",
                FeatureDescription = "The overview page summarises the account.", Timestamp = T0.AddSeconds(1),
            },
        ], null).Features);

        Assert.Equal("The overview page summarises the account.", feature.Description);
    }

    [Fact]
    public void A_background_step_lands_in_the_background_list_and_draws_no_delimiter()
    {
        var background = new TestRunRecord { Event = "step", TestId = "t", Text = "the account exists", Keyword = "Given", Background = true, Timestamp = T0.AddSeconds(1) };
        var scenario = Build(
            new TestRunRecord { Event = "start", TestId = "t", TestName = "overview", Timestamp = T0 },
            background,
            new TestRunRecord { Event = "step", TestId = "t", Text = "the overview is opened", Keyword = "When", Timestamp = T0.AddSeconds(2) },
            new TestRunRecord { Event = "end", TestId = "t", Status = "passed", Timestamp = T0.AddSeconds(3) });

        Assert.Equal("the account exists", Assert.Single(scenario.BackgroundSteps!).Text);
        Assert.Equal("the overview is opened", Assert.Single(scenario.Steps!).Text);
        // A background step is not part of the run's timeline: no bar in the sequence diagram.
        Assert.False(background.IsDiagramMarker);
    }

    [Fact]
    public void The_background_heuristic_runs_only_when_no_explicit_background_was_given()
    {
        // Two Gherkin scenarios sharing a Given prefix and no explicit background: the detector extracts it.
        var detected = FeatureSynthesizer.Build(
        [
            new TestRunRecord { Event = "start", TestId = "a", TestName = "a", Feature = "f", Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = "a", Text = "the account exists", Keyword = "Given", Timestamp = T0.AddSeconds(1) },
            new TestRunRecord { Event = "step", TestId = "a", Text = "the figures are shown", Keyword = "Then", Timestamp = T0.AddSeconds(2) },
            new TestRunRecord { Event = "start", TestId = "b", TestName = "b", Feature = "f", Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = "b", Text = "the account exists", Keyword = "Given", Timestamp = T0.AddSeconds(1) },
            new TestRunRecord { Event = "step", TestId = "b", Text = "the trial is offered", Keyword = "Then", Timestamp = T0.AddSeconds(2) },
        ], null).Features[0];

        Assert.Equal("the account exists", Assert.Single(detected.Scenarios[0].BackgroundSteps!).Text);
        Assert.Equal("the figures are shown", Assert.Single(detected.Scenarios[0].Steps!).Text);

        // The same run with one explicit background: the heuristic keeps its hands off the other scenario.
        var explicitly = FeatureSynthesizer.Build(
        [
            new TestRunRecord { Event = "start", TestId = "a", TestName = "a", Feature = "f", Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = "a", Text = "the account exists", Keyword = "Given", Background = true, Timestamp = T0.AddSeconds(1) },
            new TestRunRecord { Event = "step", TestId = "a", Text = "the figures are shown", Keyword = "Then", Timestamp = T0.AddSeconds(2) },
            new TestRunRecord { Event = "start", TestId = "b", TestName = "b", Feature = "f", Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = "b", Text = "the account exists", Keyword = "Given", Timestamp = T0.AddSeconds(1) },
            new TestRunRecord { Event = "step", TestId = "b", Text = "the trial is offered", Keyword = "Then", Timestamp = T0.AddSeconds(2) },
        ], null).Features[0];

        Assert.Null(explicitly.Scenarios[1].BackgroundSteps);
        Assert.Equal(2, explicitly.Scenarios[1].Steps!.Length);
    }

    [Fact]
    public void A_common_prefix_of_keyword_less_steps_is_a_coincidence_not_a_background()
    {
        // Playwright-style UI steps have no keywords; extracting "Open the overview" into a Background
        // section would invent Gherkin structure the run never had.
        var feature = FeatureSynthesizer.Build(
        [
            new TestRunRecord { Event = "start", TestId = "a", TestName = "a", Feature = "f", Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = "a", Text = "Open the overview", Timestamp = T0.AddSeconds(1) },
            new TestRunRecord { Event = "step", TestId = "a", Text = "Click Accept", Timestamp = T0.AddSeconds(2) },
            new TestRunRecord { Event = "start", TestId = "b", TestName = "b", Feature = "f", Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = "b", Text = "Open the overview", Timestamp = T0.AddSeconds(1) },
            new TestRunRecord { Event = "step", TestId = "b", Text = "Click Decline", Timestamp = T0.AddSeconds(2) },
        ], null).Features[0];

        Assert.All(feature.Scenarios, s => Assert.Null(s.BackgroundSteps));
        Assert.All(feature.Scenarios, s => Assert.Equal(2, s.Steps!.Length));
    }

    [Fact]
    public void A_data_table_becomes_the_step_s_tabular_parameter_with_a_reference_in_the_text()
    {
        var scenario = Build(
            new TestRunRecord { Event = "start", TestId = "t", TestName = "overview", Timestamp = T0 },
            new TestRunRecord
            {
                Event = "step", TestId = "t", Keyword = "Given", Text = "these customers exist",
                Table = [["name", "plan"], ["ada", "pro"], ["grace", "free"]],
                Timestamp = T0.AddSeconds(1),
            },
            new TestRunRecord { Event = "end", TestId = "t", Status = "passed", Timestamp = T0.AddSeconds(2) });

        var step = Assert.Single(scenario.Steps!);
        var parameter = Assert.Single(step.Parameters!);
        Assert.Equal("table", parameter.Name);
        Assert.Equal(StepParameterKind.Tabular, parameter.Kind);
        Assert.Equal(["name", "plan"], parameter.TabularValue!.Columns.Select(c => c.Name));
        Assert.Equal(2, parameter.TabularValue!.Rows.Length);
        Assert.Equal("ada", parameter.TabularValue!.Rows[0].Values[0].Value);
        // The step line gets a toggle for the table rendered below it.
        Assert.Equal("table", step.TextSegments![1].TableReference);
    }

    [Fact]
    public void A_ragged_table_row_is_padded_rather_than_dropped()
    {
        var scenario = Build(
            new TestRunRecord { Event = "start", TestId = "t", TestName = "overview", Timestamp = T0 },
            new TestRunRecord
            {
                Event = "step", TestId = "t", Text = "these customers exist",
                Table = [["name", "plan"], ["ada"]],
                Timestamp = T0.AddSeconds(1),
            });

        var table = Assert.Single(Assert.Single(scenario.Steps!).Parameters!).TabularValue!;
        Assert.Equal("ada", table.Rows[0].Values[0].Value);
        Assert.Equal("", table.Rows[0].Values[1].Value);
    }

    [Fact]
    public void A_header_only_table_is_no_table_at_all()
    {
        var scenario = Build(
            new TestRunRecord { Event = "start", TestId = "t", TestName = "overview", Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = "t", Text = "no rows", Table = [["name"]], Timestamp = T0.AddSeconds(1) });

        Assert.Null(Assert.Single(scenario.Steps!).Parameters);
        Assert.Null(Assert.Single(scenario.Steps!).TextSegments);
    }

    [Fact]
    public void Doc_strings_bypass_reasons_and_stack_traces_reach_the_step()
    {
        var scenario = Build(
            new TestRunRecord { Event = "start", TestId = "t", TestName = "overview", Timestamp = T0 },
            new TestRunRecord
            {
                Event = "step", TestId = "t", Text = "the payload is sent", Status = "failed",
                DocString = "{ \"plan\": \"pro\" }", DocStringMediaType = "json",
                Error = "the service refused it", StackTrace = "at Overview.Send()",
                BypassReason = "the backend is down",
                Timestamp = T0.AddSeconds(1),
            },
            new TestRunRecord { Event = "end", TestId = "t", Status = "failed", Error = "boom", StackTrace = "at Overview.Run()", Timestamp = T0.AddSeconds(2) });

        var step = Assert.Single(scenario.Steps!);
        Assert.Equal("{ \"plan\": \"pro\" }", step.DocString);
        Assert.Equal("json", step.DocStringMediaType);
        Assert.Equal("the backend is down", step.BypassReason);
        Assert.Equal(["the service refused it", "at Overview.Send()"], step.Comments!);
        Assert.Equal("boom", scenario.ErrorMessage);
        Assert.Equal("at Overview.Run()", scenario.ErrorStackTrace);
    }

    [Fact]
    public void Keyword_type_resolves_and_but_the_way_step_tracking_does()
    {
        Assert.Equal(TestPhase.Setup, IngestAttribution.PhaseForStep("Given", null, TestPhase.Unknown));
        Assert.Equal(TestPhase.Action, IngestAttribution.PhaseForStep("When", null, TestPhase.Setup));
        Assert.Equal(TestPhase.Action, IngestAttribution.PhaseForStep("Then", null, TestPhase.Setup));
        // And/But inherit whatever the previous step established.
        Assert.Equal(TestPhase.Setup, IngestAttribution.PhaseForStep("And", null, TestPhase.Setup));
        Assert.Equal(TestPhase.Action, IngestAttribution.PhaseForStep("But", null, TestPhase.Action));
        // The Cucumber vocabulary means the same things, and wins over a literal keyword.
        Assert.Equal(TestPhase.Setup, IngestAttribution.PhaseForStep("And", "Context", TestPhase.Action));
        Assert.Equal(TestPhase.Action, IngestAttribution.PhaseForStep("*", "Outcome", TestPhase.Unknown));
        Assert.Equal(TestPhase.Setup, IngestAttribution.PhaseForStep("*", "Conjunction", TestPhase.Setup));
    }

    [Fact]
    public void The_scenario_description_reaches_the_html_and_the_data_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kronikol-description-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = IngestPipeline.DefaultOptions();
            options.ReportsFolderPath = directory;
            options.GenerateComponentDiagram = false;

            var result = IngestPipeline.Run(new IngestRequest
            {
                TestRecords =
                [
                    new TestRunRecord
                    {
                        Event = "start", TestId = "t", TestName = "overview renders",
                        Description = "A returning customer opens the overview.", Timestamp = T0,
                    },
                    new TestRunRecord { Event = "end", TestId = "t", Status = "passed", Timestamp = T0.AddSeconds(1) },
                ],
                Options = options,
            });

            Assert.Contains("A returning customer opens the overview.", File.ReadAllText(result.TestRunReportHtml));

            using var document = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(result.ReportsDirectory, "TestRunReport.json")));
            Assert.Equal("A returning customer opens the overview.",
                document.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("description").GetString());
            Assert.Contains("\"description\"", File.ReadAllText(Path.Combine(result.ReportsDirectory, "TestRunReport.schema.json")));
        }
        finally
        {
            DefaultDiagramsFetcher.Reset();
            try { Directory.Delete(directory, true); } catch { /* best effort */ }
        }
    }
}
