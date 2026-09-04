using Kronikol.Ingestion.Cucumber;
using Kronikol.Reports;

namespace Kronikol.Tests.Ingestion.Cucumber;

/// <summary>
/// Every field the Cucumber Messages importer maps, asserted against the checked-in golden fixture
/// produced by a real playwright-bdd 9.2 run (see <see cref="CucumberFixtures"/>).
/// </summary>
public class CucumberFeatureSynthesizerTests
{
    private static readonly CucumberSynthesisResult Result = CucumberFixtures.Build();

    private static Feature Feature(string name) =>
        Assert.Single(Result.Features, f => f.DisplayName == name);

    private static Scenario Scenario(string featureName, string scenarioName) =>
        Feature(featureName).Scenarios.First(s => s.DisplayName == scenarioName);

    private static Scenario Outline(string page) =>
        Feature(CucumberFixtures.DemoFeature).Scenarios
            .Single(s => s.DisplayName == CucumberFixtures.OutlineScenario && s.ExampleValues!["page"] == page);

    // ---- features -------------------------------------------------------------------------------

    [Fact]
    public void Features_come_from_the_gherkin_documents()
    {
        Assert.Equal([CucumberFixtures.DemoFeature, CucumberFixtures.RetryFeature],
            Result.Features.Select(f => f.DisplayName));
    }

    [Fact]
    public void Feature_description_is_carried_over_and_dedented()
    {
        var description = Feature(CucumberFixtures.DemoFeature).Description;

        Assert.Equal(
            "This feature exercises every Gherkin construct the Kronikol\nCucumber Messages importer has to map.",
            description!.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Feature_tags_become_labels_minus_the_kronikol_conventions()
    {
        // @feature-tag @category:demo → the category is a Category, not a Label.
        Assert.Equal(["feature-tag"], Feature(CucumberFixtures.DemoFeature).Labels!);
    }

    [Fact]
    public void An_endpoint_tag_becomes_the_feature_endpoint()
    {
        Assert.Equal("/api/orders", Feature(CucumberFixtures.DemoFeature).Endpoint);
    }

    // ---- scenarios ------------------------------------------------------------------------------

    [Fact]
    public void Every_pickle_becomes_a_scenario_and_a_retry_does_not_duplicate_it()
    {
        Assert.Equal(5, Feature(CucumberFixtures.DemoFeature).Scenarios.Length);
        Assert.Single(Feature(CucumberFixtures.RetryFeature).Scenarios);
    }

    [Fact]
    public void Scenario_description_is_carried_over()
    {
        Assert.Equal("The canonical happy path: one customer, one order.",
            Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario).Description);
    }

    [Fact]
    public void A_rule_becomes_the_scenario_rule_and_scenarios_outside_it_have_none()
    {
        Assert.Null(Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario).Rule);
        Assert.Equal(CucumberFixtures.Rule, Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.TableScenario).Rule);
        Assert.Equal(CucumberFixtures.Rule, Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.FailingScenario).Rule);
    }

    [Fact]
    public void Background_steps_are_explicit_and_not_mixed_into_the_scenario_steps()
    {
        var scenario = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario);

        var background = Assert.Single(scenario.BackgroundSteps!);
        Assert.Equal("Given", background.Keyword);
        Assert.Equal("the catalogue is loaded", background.Text);
        Assert.DoesNotContain(scenario.Steps!, s => s.Text == "the catalogue is loaded");
    }

    [Fact]
    public void Scenario_tags_split_into_labels_categories_and_happy_path()
    {
        var happy = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario);
        Assert.True(happy.IsHappyPath);
        Assert.Null(happy.Labels);                       // @happy-path and @endpoint: are conventions, not labels
        Assert.Equal(["demo"], happy.Categories!);       // inherited from the feature's @category:demo

        var validation = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.TableScenario);
        Assert.False(validation.IsHappyPath);
        Assert.Equal(["demo", "validation"], validation.Categories!);

        var failing = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.FailingScenario);
        Assert.Contains("failing", failing.Labels!);
    }

    // ---- steps ----------------------------------------------------------------------------------

    [Fact]
    public void Step_keywords_keep_the_authored_and_but_sequencing()
    {
        var steps = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario).Steps!;

        Assert.Equal(["Given", "When", "And", "Then", "But"], steps.Select(s => s.Keyword));
    }

    [Fact]
    public void A_data_table_becomes_a_tabular_step_parameter_named_table()
    {
        var step = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.TableScenario).Steps![0];

        var parameter = Assert.Single(step.Parameters!);
        Assert.Equal("table", parameter.Name);
        Assert.Equal(StepParameterKind.Tabular, parameter.Kind);
        Assert.Equal(["sku", "quantity", "price"], parameter.TabularValue!.Columns.Select(c => c.Name));
        Assert.Equal(2, parameter.TabularValue.Rows.Length);
        Assert.Equal(["APPLE-1", "2", "1.50"], parameter.TabularValue.Rows[0].Values.Select(v => v.Value));
        Assert.Equal(["PEAR-7", "1", "2.25"], parameter.TabularValue.Rows[1].Values.Select(v => v.Value));
    }

    [Fact]
    public void A_doc_string_becomes_the_step_doc_string_with_its_media_type()
    {
        var step = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.TableScenario).Steps![1];

        Assert.Equal("""{ "channel": "web", "currency": "GBP" }""", step.DocString);
        Assert.Equal("json", step.DocStringMediaType);
    }

    [Fact]
    public void Step_markers_carry_the_table_and_doc_string_for_the_delimiter_bar()
    {
        var tableMarker = Result.Markers.Single(m =>
            m.Event == "step" && m.Text == "the following order lines:");
        Assert.Equal([["sku", "quantity", "price"], ["APPLE-1", "2", "1.50"], ["PEAR-7", "1", "2.25"]],
            tableMarker.Table);
        Assert.Null(tableMarker.DocString);

        var docStringMarker = Result.Markers.Single(m =>
            m.Event == "step" && m.Text == "the payload is submitted:");
        Assert.Null(docStringMarker.Table);
        Assert.Equal("""{ "channel": "web", "currency": "GBP" }""", docStringMarker.DocString);
    }

    [Fact]
    public void The_pickle_argument_wins_over_the_authored_gherkin_argument()
    {
        // An outline's authored table keeps its <placeholders>; the pickle's argument carries the
        // substituted values the step actually received. Both the step list and the delimiter-bar
        // marker must show the substituted values.
        var messages = CucumberMessagesReader.Read(new StringReader(SubstitutedArgumentStream()));
        var result = CucumberFeatureSynthesizer.Build(messages);

        var scenario = result.Features.Single().Scenarios.Single();
        var step = Assert.Single(scenario.Steps!);
        var parameter = Assert.Single(step.Parameters!);
        Assert.Equal(["sku", "qty"], parameter.TabularValue!.Columns.Select(c => c.Name));
        Assert.Equal(["SUB-1", "2"], parameter.TabularValue.Rows.Single().Values.Select(v => v.Value));

        var marker = result.Markers.Single(m => m.Event == "step");
        Assert.Equal([["sku", "qty"], ["SUB-1", "2"]], marker.Table);
    }

    /// <summary>
    /// A minimal messages stream: one scenario whose authored table cell is the placeholder
    /// <c>&lt;sku&gt;</c> while the pickle step's argument carries the substituted <c>SUB-1</c>.
    /// </summary>
    private static string SubstitutedArgumentStream() =>
        """
        {"gherkinDocument":{"uri":"features/sub.feature","feature":{"name":"Substitution","children":[{"scenario":{"id":"sc-1","keyword":"Scenario","name":"a substituted row","steps":[{"id":"st-1","keyword":"Given ","keywordType":"Context","text":"the order lines:","dataTable":{"rows":[{"id":"r0","cells":[{"value":"sku"},{"value":"qty"}]},{"id":"r1","cells":[{"value":"<sku>"},{"value":"2"}]}]}}]}}]}}}
        {"pickle":{"id":"pk-1","uri":"features/sub.feature","astNodeIds":["sc-1"],"name":"a substituted row","language":"en","steps":[{"id":"ps-1","text":"the order lines:","type":"Context","astNodeIds":["st-1"],"argument":{"dataTable":{"rows":[{"cells":[{"value":"sku"},{"value":"qty"}]},{"cells":[{"value":"SUB-1"},{"value":"2"}]}]}}}]}}
        {"testCase":{"id":"tc-1","pickleId":"pk-1","testSteps":[{"id":"ts-1","pickleStepId":"ps-1"}]}}
        {"testCaseStarted":{"id":"att-1","attempt":0,"testCaseId":"tc-1","timestamp":{"seconds":1787393374,"nanos":0}}}
        {"testStepStarted":{"testCaseStartedId":"att-1","testStepId":"ts-1","timestamp":{"seconds":1787393374,"nanos":100000000}}}
        {"testStepFinished":{"testCaseStartedId":"att-1","testStepId":"ts-1","testStepResult":{"duration":{"seconds":0,"nanos":50000000},"status":"PASSED"},"timestamp":{"seconds":1787393374,"nanos":200000000}}}
        {"testCaseFinished":{"testCaseStartedId":"att-1","timestamp":{"seconds":1787393375,"nanos":0}}}
        """;

    [Fact]
    public void Step_status_and_duration_come_from_the_step_results()
    {
        var steps = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.FailingScenario).Steps!;

        Assert.Equal(ExecutionResult.Passed, steps[0].Status);
        Assert.Equal(ExecutionResult.Failed, steps[1].Status);
        Assert.Equal(ExecutionResult.Skipped, steps[2].Status);   // SKIPPED after the failure
        Assert.All(steps, s => Assert.NotNull(s.Duration));
    }

    [Fact]
    public void A_failed_step_carries_the_exception_as_a_comment_and_onto_the_scenario()
    {
        var scenario = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.FailingScenario);

        Assert.Equal(ExecutionResult.Failed, scenario.Result);
        Assert.Contains("Deliberate failure: the widget did not appear", scenario.ErrorMessage);
        Assert.Contains("Deliberate failure: the widget did not appear", scenario.ErrorStackTrace);
        Assert.Contains("Deliberate failure: the widget did not appear", Assert.Single(scenario.Steps![1].Comments!));
    }

    [Theory]
    [InlineData("PASSED", ExecutionResult.Passed)]
    [InlineData("FAILED", ExecutionResult.Failed)]
    [InlineData("SKIPPED", ExecutionResult.Skipped)]
    [InlineData("PENDING", ExecutionResult.Skipped)]
    [InlineData("UNDEFINED", ExecutionResult.Failed)]
    [InlineData("AMBIGUOUS", ExecutionResult.Failed)]
    public void Cucumber_statuses_map_to_kronikol_verdicts(string status, ExecutionResult expected)
    {
        Assert.Equal(expected, CucumberFeatureSynthesizer.MapStatus(status));
    }

    [Fact]
    public void The_scenario_result_is_the_worst_step_result()
    {
        Assert.Equal(ExecutionResult.Passed, Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario).Result);
        Assert.Equal(ExecutionResult.Failed, Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.FailingScenario).Result);
    }

    // ---- scenario outlines ----------------------------------------------------------------------

    [Fact]
    public void Outline_rows_share_an_outline_id_and_carry_their_example_values()
    {
        var first = Outline("overview");
        var second = Outline("customers");

        Assert.Equal(CucumberFixtures.OutlineScenario, first.OutlineId);
        Assert.Equal(CucumberFixtures.OutlineScenario, second.OutlineId);
        Assert.Equal(new Dictionary<string, string> { ["customer"] = "Ada", ["page"] = "overview" }, first.ExampleValues);
        Assert.Equal(new Dictionary<string, string> { ["customer"] = "Grace", ["page"] = "customers" }, second.ExampleValues);
        Assert.Equal("Ada", first.ExampleRawValues!["customer"]);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void A_plain_scenario_has_no_outline_id_or_example_values()
    {
        var scenario = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario);

        Assert.Null(scenario.OutlineId);
        Assert.Null(scenario.ExampleValues);
    }

    [Fact]
    public void Outline_steps_carry_text_segments_with_the_substituted_placeholder()
    {
        var step = Outline("overview").Steps!.Single(s => s.Keyword == "When");

        Assert.Equal("""the customer opens the "overview" page""", step.Text);
        var segments = step.TextSegments!;
        var parameter = Assert.Single(segments, s => s.Parameter is not null);
        Assert.Equal("page", parameter.ParameterName);
        Assert.Equal("overview", parameter.Parameter!.Value);
        Assert.Equal("""the customer opens the "”" page""".Replace("”", "overview"),
            string.Concat(segments.Select(s => s.Text ?? s.Parameter?.Value)));
    }

    [Fact]
    public void The_outline_rows_group_into_one_parameterised_group_for_the_report()
    {
        var scenarios = Feature(CucumberFixtures.DemoFeature).Scenarios;

        var (groups, _) = ParameterGrouper.Analyze(scenarios, enabled: false);

        var group = Assert.Single(groups, g => g.Scenarios.Length == 2);
        Assert.Equal(["customer", "page"], group.ParameterNames);
        Assert.Equal(ParameterDisplayRule.ScalarColumns, group.Rule);
    }

    // ---- retries --------------------------------------------------------------------------------

    [Fact]
    public void The_last_attempt_wins_and_the_earlier_one_leaves_a_retry_label()
    {
        var flaky = Scenario(CucumberFixtures.RetryFeature, CucumberFixtures.FlakyScenario);

        Assert.Equal(ExecutionResult.Passed, flaky.Result);       // attempt 2 passed
        Assert.Contains("retry 1", flaky.Labels!);
    }

    // ---- attachments and identity ----------------------------------------------------------------

    [Fact]
    public void The_kronikol_test_id_attachment_becomes_the_scenario_id()
    {
        var scenario = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario);

        Assert.Matches("^[0-9a-f]{32}$", scenario.Id);
        Assert.Contains(scenario.Id, Result.JoinedTestIds);
        Assert.All(Result.Features.SelectMany(f => f.Scenarios), s => Assert.Contains(s.Id, Result.JoinedTestIds));
        Assert.DoesNotContain(Result.Warnings, w => w.Contains("cannot be joined"));
    }

    [Fact]
    public void Without_the_identity_attachment_the_id_is_minted_and_a_warning_says_interactions_cannot_join()
    {
        var result = CucumberFixtures.Build(new CucumberSynthesisOptions { TestIdAttachmentName = "not-present" });

        Assert.Empty(result.JoinedTestIds);
        Assert.All(result.Features.SelectMany(f => f.Scenarios), s => Assert.Contains('#', s.Id));
        Assert.Contains(result.Warnings, w => w.Contains("cannot be joined"));
    }

    [Fact]
    public void Base64_attachment_bodies_are_written_out_and_named_by_their_media_type()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kronikol-cucumber-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = CucumberFixtures.Build(new CucumberSynthesisOptions { AttachmentsDirectory = directory });
            var scenario = result.Features.SelectMany(f => f.Scenarios).First(s => s.DisplayName == CucumberFixtures.SimpleScenario);

            var attachments = scenario.Attachments!;
            Assert.Contains(attachments, a => a.Name == "end-screenshot.png" && a.MediaType == "image/png");
            Assert.Contains(attachments, a => a.Name == "start-screenshot.png");
            Assert.All(attachments, a => Assert.True(File.Exists(a.RelativePath), a.RelativePath));
            Assert.DoesNotContain(attachments, a => a.Name.StartsWith("kronikol-test-id"));
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { /* best effort */ }
        }
    }

    // ---- hooks ----------------------------------------------------------------------------------

    [Fact]
    public void Hook_steps_are_dropped_by_default_but_their_attachments_survive()
    {
        var scenario = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario);

        Assert.DoesNotContain(scenario.Steps!, s => s.Text.Contains("hook", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(scenario.Attachments!);   // attached by the Before/After hooks
    }

    [Fact]
    public void IncludeHooks_keeps_the_hook_steps_named_by_the_hook()
    {
        var result = CucumberFixtures.Build(new CucumberSynthesisOptions { IncludeHooks = true });
        var scenario = result.Features.SelectMany(f => f.Scenarios).First(s => s.DisplayName == CucumberFixtures.SimpleScenario);

        Assert.Contains(scenario.Steps!, s => s.Text == "BeforeEach hook");
        Assert.Contains(scenario.Steps!, s => s.Text == "AfterEach hook");
        Assert.All(scenario.Steps!.Where(s => s.Text.Contains("hook")), s => Assert.Null(s.Keyword));
    }

    // ---- run window and markers -------------------------------------------------------------------

    [Fact]
    public void Run_start_and_end_come_from_the_test_run_envelopes()
    {
        var messages = CucumberFixtures.Read();

        Assert.Equal(messages.TestRunStarted!.Timestamp!.ToInstant().UtcDateTime, Result.Start);
        Assert.Equal(messages.TestRunFinished!.Timestamp!.ToInstant().UtcDateTime, Result.End);
        Assert.True(Result.End > Result.Start);
    }

    [Fact]
    public void Markers_are_a_tests_ndjson_equivalent_of_the_messages()
    {
        var scenario = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario);
        var markers = Result.Markers.Where(m => m.TestId == scenario.Id).ToArray();

        Assert.Equal("start", markers[0].Event);
        Assert.Equal(CucumberFixtures.SimpleScenario, markers[0].TestName);
        Assert.Equal(CucumberFixtures.DemoFeature, markers[0].Feature);
        Assert.Equal("end", markers[^1].Event);
        Assert.Equal("passed", markers[^1].Status);

        var steps = markers.Where(m => m.Event == "step").ToArray();
        // Background step plus the five authored steps, each drawing a delimiter bar at its start.
        Assert.Equal(["Given", "Given", "When", "And", "Then", "But"], steps.Select(s => s.Keyword));
        Assert.All(steps, s => Assert.True(s.IsDiagramMarker));
        Assert.All(steps, s => Assert.Equal(0, s.Level));
        Assert.True(steps.Zip(steps.Skip(1)).All(pair => pair.First.Timestamp <= pair.Second.Timestamp));
    }

    [Fact]
    public void Step_windows_are_exposed_in_start_order_for_every_scenario()
    {
        var scenario = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario);

        var windows = Result.StepWindows[scenario.Id];
        Assert.Equal(6, windows.Count);
        Assert.All(windows, w => Assert.True(w.End >= w.Start));
        Assert.Equal(scenario.BackgroundSteps![0], windows[0].Step);
    }

    [Fact]
    public void Step_windows_carry_the_resolved_phase_an_And_step_inherits()
    {
        var scenario = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario);

        var windows = Result.StepWindows[scenario.Id];
        // Background Given, Given, When, And (continues the When), Then, But (continues the Then).
        Assert.Equal(["Context", "Context", "Action", "Action", "Outcome", "Outcome"],
            windows.Select(w => w.KeywordType));
        Assert.Equal(["Given", "Given", "When", "And", "Then", "But"], windows.Select(w => w.Step.Keyword));
    }

    [Fact]
    public void Test_names_map_every_scenario_id_to_its_display_name()
    {
        Assert.Equal(6, Result.TestNames.Count);
        foreach (var scenario in Result.Features.SelectMany(f => f.Scenarios))
            Assert.Equal(scenario.DisplayName, Result.TestNames[scenario.Id]);
    }

    // ---- degenerate input -------------------------------------------------------------------------

    [Fact]
    public void Empty_messages_synthesise_nothing_without_throwing()
    {
        var result = CucumberFeatureSynthesizer.Build(CucumberMessagesReader.Read(new StringReader("")));

        Assert.Empty(result.Features);
        Assert.Empty(result.Markers);
        Assert.Empty(result.TestNames);
    }

    [Fact]
    public void A_test_case_without_its_pickle_is_reported_rather_than_thrown()
    {
        var result = CucumberFeatureSynthesizer.Build(CucumberMessagesReader.Read(new StringReader(
            """
            {"testCase":{"id":"tc1","pickleId":"missing","testSteps":[]}}
            {"testCaseStarted":{"id":"tc1-attempt-0","attempt":0,"testCaseId":"tc1","timestamp":{"seconds":1,"nanos":0}}}
            """)));

        Assert.Empty(result.Features);
        Assert.Contains(result.Warnings, w => w.Contains("no pickle"));
    }
}
