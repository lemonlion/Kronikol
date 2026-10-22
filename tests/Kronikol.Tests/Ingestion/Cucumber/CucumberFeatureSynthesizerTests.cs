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
    public void Feature_tags_are_the_feature_node_s_tags_less_the_kronikol_conventions()
    {
        // @feature-tag @category:demo: a category is a tag (F1)
        Assert.Equal(["feature-tag", "demo"], Feature(CucumberFixtures.DemoFeature).Tags);
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
    public void Scenario_tags_are_the_pickle_s_less_the_feature_s_and_the_happy_path_is_a_flag()
    {
        var happy = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario);
        Assert.True(happy.IsHappyPath);
        Assert.Null(happy.Tags);                         // @happy-path and @endpoint: are conventions, not tags

        var validation = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.TableScenario);
        Assert.False(validation.IsHappyPath);
        // the feature's @category:demo is the feature's, as is every tag the pickle inherits from it
        Assert.Equal(["validation"], validation.Tags);

        Assert.Equal(["failing"], Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.FailingScenario).Tags);
        // the outline's own @category:demo repeats the feature's (§4.3)
        Assert.Null(Outline("overview").Tags);
    }

    /// <summary>
    /// A feature with a tag, a rule with a tag and one scenario in it, and a scenario outside the rule. A pickle carries
    /// every tag in its scope, the feature's and its rule's included.
    /// </summary>
    private static string RuleTagsStream() =>
        """
        {"gherkinDocument":{"uri":"features/rules.feature","feature":{"keyword":"Feature","name":"Rules","tags":[{"name":"@feature","id":"t-f"}],"children":[{"rule":{"id":"r-1","keyword":"Rule","name":"Refunds need a receipt","tags":[{"name":"@rule-tag","id":"t-r"}],"children":[{"scenario":{"id":"sc-1","keyword":"Scenario","name":"in the rule","tags":[{"name":"@own","id":"t-o"}],"steps":[]}}]}},{"scenario":{"id":"sc-2","keyword":"Scenario","name":"outside the rule","tags":[],"steps":[]}}]}}}
        {"pickle":{"id":"pk-1","uri":"features/rules.feature","astNodeIds":["sc-1"],"name":"in the rule","language":"en","steps":[],"tags":[{"name":"@feature","astNodeId":"t-f"},{"name":"@rule-tag","astNodeId":"t-r"},{"name":"@own","astNodeId":"t-o"}]}}
        {"pickle":{"id":"pk-2","uri":"features/rules.feature","astNodeIds":["sc-2"],"name":"outside the rule","language":"en","steps":[],"tags":[{"name":"@feature","astNodeId":"t-f"}]}}
        {"testCase":{"id":"tc-1","pickleId":"pk-1","testSteps":[]}}
        {"testCase":{"id":"tc-2","pickleId":"pk-2","testSteps":[]}}
        {"testCaseStarted":{"id":"att-1","attempt":0,"testCaseId":"tc-1","timestamp":{"seconds":1787393374,"nanos":0}}}
        {"testCaseFinished":{"testCaseStartedId":"att-1","timestamp":{"seconds":1787393375,"nanos":0}}}
        {"testCaseStarted":{"id":"att-2","attempt":0,"testCaseId":"tc-2","timestamp":{"seconds":1787393376,"nanos":0}}}
        {"testCaseFinished":{"testCaseStartedId":"att-2","timestamp":{"seconds":1787393377,"nanos":0}}}
        """;

    [Fact]
    public void A_rule_s_tags_reach_its_scenarios_and_no_others()
    {
        // F10: 4.x read a scenario's labels from its own tags and its Examples block's, so a rule's tags reached no one
        var feature = CucumberFeatureSynthesizer.Build(CucumberMessagesReader.Read(new StringReader(RuleTagsStream()))).Features.Single();

        Assert.Equal(["feature"], feature.Tags);
        Assert.Equal(["rule-tag", "own"], feature.Scenarios.Single(s => s.DisplayName == "in the rule").Tags);
        Assert.Null(feature.Scenarios.Single(s => s.DisplayName == "outside the rule").Tags);
    }

    /// <summary>
    /// Two files whose features share the title Checkout, with feature tags of their own and one they share, and a scenario
    /// in each; <paramref name="apiFirst"/> runs the second file's first.
    /// </summary>
    private static string SameTitleStream(bool apiFirst)
    {
        // whichever runs first starts first, so that neither the messages' order nor the times put web first
        var (webAt, apiAt) = apiFirst ? (1787393376, 1787393374) : (1787393374, 1787393376);
        static string At(string line, int seconds) => line.Replace("SECONDS", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string[] web =
        [
            """{"pickle":{"id":"pk-1","uri":"features/web/checkout.feature","astNodeIds":["sc-1"],"name":"pay","language":"en","steps":[],"tags":[{"name":"@web","astNodeId":"t-w"},{"name":"@shop","astNodeId":"t-s1"}]}}""",
            """{"testCase":{"id":"tc-1","pickleId":"pk-1","testSteps":[]}}""",
            At("""{"testCaseStarted":{"id":"att-1","attempt":0,"testCaseId":"tc-1","timestamp":{"seconds":SECONDS,"nanos":0}}}""", webAt),
            At("""{"testCaseFinished":{"testCaseStartedId":"att-1","timestamp":{"seconds":SECONDS,"nanos":0}}}""", webAt + 1),
        ];
        string[] api =
        [
            """{"pickle":{"id":"pk-2","uri":"features/api/checkout.feature","astNodeIds":["sc-2"],"name":"refund","language":"en","steps":[],"tags":[{"name":"@api","astNodeId":"t-a"},{"name":"@shop","astNodeId":"t-s2"},{"name":"@own","astNodeId":"t-o"}]}}""",
            """{"testCase":{"id":"tc-2","pickleId":"pk-2","testSteps":[]}}""",
            At("""{"testCaseStarted":{"id":"att-2","attempt":0,"testCaseId":"tc-2","timestamp":{"seconds":SECONDS,"nanos":0}}}""", apiAt),
            At("""{"testCaseFinished":{"testCaseStartedId":"att-2","timestamp":{"seconds":SECONDS,"nanos":0}}}""", apiAt + 1),
        ];
        string[] documents =
        [
            """{"gherkinDocument":{"uri":"features/web/checkout.feature","feature":{"keyword":"Feature","name":"Checkout","tags":[{"name":"@web","id":"t-w"},{"name":"@shop","id":"t-s1"}],"children":[{"scenario":{"id":"sc-1","keyword":"Scenario","name":"pay","tags":[],"steps":[]}}]}}}""",
            """{"gherkinDocument":{"uri":"features/api/checkout.feature","feature":{"keyword":"Feature","name":"Checkout","tags":[{"name":"@api","id":"t-a"},{"name":"@shop","id":"t-s2"}],"children":[{"scenario":{"id":"sc-2","keyword":"Scenario","name":"refund","tags":[{"name":"@own","id":"t-o"}],"steps":[]}}]}}}""",
        ];
        return string.Join("\n", documents.Concat(apiFirst ? api.Concat(web) : web.Concat(api)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Two_files_whose_features_share_a_title_keep_their_own_feature_tags(bool apiFirst)
    {
        // One feature, Checkout, whose tags were the first file's until 5.2.7: the other file's scenarios carried them and
        // lost their own, though a scenario's effective tags are its pickle's (§4.5). The tag both files have stays the feature's.
        var feature = CucumberFeatureSynthesizer.Build(CucumberMessagesReader.Read(new StringReader(SameTitleStream(apiFirst)))).Features.Single();

        Assert.Equal(["shop"], feature.Tags);
        Assert.Equal(["web"], feature.Scenarios.Single(s => s.DisplayName == "pay").Tags);
        Assert.Equal(["api", "own"], feature.Scenarios.Single(s => s.DisplayName == "refund").Tags);
    }

    [Fact]
    public void A_feature_s_or_a_rule_s_happy_path_tag_marks_none_of_its_scenarios()
    {
        // §4.5: a scenario's happy path is read from its own tags and its Examples block's, as before; a feature's or a
        // rule's happy-path tag is neither its scenarios' happy path nor a tag
        var stream = RuleTagsStream().Replace("@rule-tag", "@happy-path").Replace("\"@feature\"", "\"@happy-path\"");
        var feature = CucumberFeatureSynthesizer.Build(CucumberMessagesReader.Read(new StringReader(stream))).Features.Single();

        Assert.Null(feature.Tags);
        Assert.All(feature.Scenarios, s => Assert.False(s.IsHappyPath));
        Assert.Equal(["own"], feature.Scenarios.Single(s => s.DisplayName == "in the rule").Tags);
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

    [Fact]
    public void The_scenario_records_when_its_test_case_finished()
    {
        var messages = CucumberMessagesReader.Read(new StringReader(SubstitutedArgumentStream()));
        var scenario = CucumberFeatureSynthesizer.Build(messages).Features.Single().Scenarios.Single();

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1787393375), scenario.EndedAt);
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

    [Fact]
    public void Each_gherkin_step_carries_its_feature_file_name_and_line_and_a_failed_one_its_failure_message()
    {
        // plans/INGEST_FIDELITY_PLAN.md T11 (#76 on the ingest lane): the lane parsed each step's location and never
        // mapped it, and put a failure only into the comments, which the digest does not read.
        var simple = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.SimpleScenario);
        Assert.Equal([15, 16, 17, 18, 19], simple.Steps!.Select(s => s.SourceLine ?? 0));
        Assert.All(simple.Steps!, s => Assert.Equal("kronikol-demo.feature", s.SourceFile));
        var background = Assert.Single(simple.BackgroundSteps!);
        Assert.Equal(("kronikol-demo.feature", 8), (background.SourceFile, background.SourceLine));
        Assert.All(simple.Steps!, s => Assert.Null(s.FailureMessage));

        var failing = Scenario(CucumberFixtures.DemoFeature, CucumberFixtures.FailingScenario);
        Assert.Equal(38, failing.Steps![1].SourceLine);
        Assert.Contains("Deliberate failure: the widget did not appear", failing.Steps[1].FailureMessage);
        Assert.Null(failing.Steps[2].FailureMessage); // skipped after the failure
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
    public void The_last_attempt_wins_and_no_retry_tag_is_made()
    {
        var flaky = Scenario(CucumberFixtures.RetryFeature, CucumberFixtures.FlakyScenario);

        Assert.Equal(ExecutionResult.Passed, flaky.Result);       // attempt 2 passed
        // Q6: the report draws retry 1 from the attempt; a tag would be a chip
        Assert.Equal(2, flaky.Attempt);
        Assert.Null(flaky.Tags);
        Assert.Equal(["retry-demo"], Feature(CucumberFixtures.RetryFeature).Tags);
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

    [Fact]
    public void IncludeHooks_keeps_each_hook_where_it_ran()
    {
        // Every kept hook was listed after the Gherkin steps, a before hook too, while the step markers follow the order the
        // steps ran: matched one for one, every marker after the first missed its step and the calls in it lost their step.
        var result = CucumberFixtures.Build(new CucumberSynthesisOptions { IncludeHooks = true });
        var scenario = result.Features.SelectMany(f => f.Scenarios).First(s => s.DisplayName == CucumberFixtures.SimpleScenario);

        Assert.Equal("BeforeEach hook", scenario.Steps![0].Text);
        Assert.Equal("AfterEach hook", scenario.Steps[^1].Text);
        Assert.DoesNotContain(scenario.Steps[1..^1], s => s.Text.Contains("hook", StringComparison.Ordinal));
    }

    [Fact]
    public void An_attachment_of_a_test_step_that_names_no_pickle_step_is_kept_on_the_scenario()
    {
        // Such a step has no row to go on; what it attached was dropped with it.
        var stream = CucumberBypassTests.Stream(
            new CucumberBypassTests.Step("the page is open", "PASSED",
                Attachments: [new CucumberBypassTests.Attachment("shot.png", Convert.ToBase64String([137, 80, 78, 71]), "BASE64", "image/png")]),
            new CucumberBypassTests.Step("the check passes", "PASSED"));
        var unresolved = stream.Replace("""
            "pickleStepId":"ps-0"
            """, """
            "pickleStepId":"ps-elsewhere"
            """, StringComparison.Ordinal);
        Assert.NotEqual(stream, unresolved);

        var scenario = CucumberBypassTests.Synthesise(unresolved).Features.Single().Scenarios.Single();

        Assert.Equal("shot.png", Assert.Single(scenario.Attachments!).Name);
        Assert.Equal("the check passes", Assert.Single(scenario.Steps!).Text);
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

    // ─── Source locations (M2.6) ───────────────────────────────

    [Fact]
    public void The_feature_file_and_the_scenario_line_survive_ingest()
    {
        // Both were parsed and thrown away: `GherkinScenario` has carried the document uri since it was
        // written and nothing read it, and the scenario node never declared `location` at all.
        var result = CucumberFixtures.Build();
        var feature = result.Features.Single(f => f.DisplayName == CucumberFixtures.DemoFeature);

        Assert.Equal("features/kronikol-demo.feature", feature.SourceFile);

        var scenario = feature.Scenarios.Single(s => s.DisplayName == CucumberFixtures.SimpleScenario);
        Assert.Equal("features/kronikol-demo.feature", scenario.SourceFile);
        Assert.Equal(11, scenario.SourceLine);
    }

    [Fact]
    public void A_windows_producers_backslashes_are_normalised()
    {
        // The golden fixture was captured by playwright-bdd on Windows and says
        // `features\kronikol-demo.feature`; cucumber-js on Linux says `features/…`. A consumer
        // globbing for the path should not have to know which machine ran the tests.
        var result = CucumberFixtures.Build();

        Assert.All(result.Features, f => Assert.DoesNotContain('\\', f.SourceFile ?? ""));
        Assert.All(result.Features.SelectMany(f => f.Scenarios),
            s => Assert.DoesNotContain('\\', s.SourceFile ?? ""));
    }

    [Fact]
    public void Every_row_of_an_outline_points_at_the_outline_declaration()
    {
        // One line for the whole outline, deliberately: `exampleValues` is what says which row, and a
        // per-row line would make two scenarios that are the same code look like different code.
        var result = CucumberFixtures.Build();
        var rows = result.Features.SelectMany(f => f.Scenarios)
            .Where(s => s.DisplayName.Contains("outline over pages", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(rows.Length > 1, "the fixture's outline should produce more than one row");
        Assert.Single(rows.Select(r => r.SourceLine).Distinct());
        Assert.NotNull(rows[0].SourceLine);
    }

    [Fact]
    public void A_retried_scenario_records_which_attempt_finally_ran()
    {
        // 1-based, as the retry N label 4.x made for each earlier attempt was numbered (Attempt + 1 of each), so the
        // report's badge, retry 1 for a second attempt, reads what the label read. Cucumber's own attempt is 0-based.
        var result = CucumberFixtures.Build();
        var flaky = result.Features.SelectMany(f => f.Scenarios)
            .Single(s => s.DisplayName == CucumberFixtures.FlakyScenario);

        Assert.Equal(2, flaky.Attempt);
        Assert.DoesNotContain("retry 1", flaky.Tags ?? []);

        var first = result.Features.SelectMany(f => f.Scenarios)
            .Single(s => s.DisplayName == CucumberFixtures.SimpleScenario);
        Assert.Equal(1, first.Attempt);
    }
}
