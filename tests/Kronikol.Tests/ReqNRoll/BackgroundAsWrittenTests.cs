using System.Globalization;
using System.Reflection;
using Io.Cucumber.Messages.Types;
using GherkinFeature = Io.Cucumber.Messages.Types.Feature;
using GherkinScenarioNode = Io.Cucumber.Messages.Types.Scenario;
using Feature = Kronikol.Reports.Feature;
using Scenario = Kronikol.Reports.Scenario;
using Kronikol.Ingestion;
using Kronikol.Ingestion.Cucumber;
using Kronikol.Query;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.Formatters.PayloadProcessing;
using Reqnroll.Formatters.RuntimeSupport;

namespace Kronikol.Tests.ReqNRoll;

/// <summary>
/// #222: a Reqnroll step is drawn as a Background step when the feature file wrote it in a <c>Background:</c>, and each
/// scenario keeps its own steps. The first cut of plans/BACKGROUND_AS_WRITTEN_PLAN.md section 6's facts.
/// </summary>
public class BackgroundAsWrittenTests
{
    private sealed class FakeMessages(GherkinDocument? document, List<Pickle> pickles) : IFeatureLevelCucumberMessages
    {
        public bool HasMessages => document is not null;
        public GherkinDocument GherkinDocument => document!;
        public IEnumerable<Pickle> Pickles => pickles;
        public Source Source => null!;
    }

    private static Location Loc => new(1, 1);

    private static Step GherkinStep(string id, string text) => new(Loc, "Given ", StepKeywordType.CONTEXT, text, null!, null!, id);

    private static GherkinScenarioNode GherkinScenario(string id, string name, params Step[] steps) =>
        new(Loc, [], "Scenario", name, "", steps.ToList(), [], id);

    private static Background GherkinBackground(string id, params Step[] steps) =>
        new(Loc, "Background", "", "", steps.ToList(), id);

    private static Pickle PickleOf(string id, string name, string scenarioAstId) =>
        new(id, "Features/Orders.feature", name, "en", [], [], [scenarioAstId]);

    /// <summary>A feature's Background, a rule's on top of it, a rule without one, and a scenario outside any rule.</summary>
    private static FeatureInfo Orders()
    {
        var plain = GherkinScenario("plain", "Outside any rule", GherkinStep("p1", "the order is placed"));
        var noOwnSteps = GherkinScenario("empty", "With no steps of its own");
        var ruled = GherkinScenario("ruled", "Inside the rule", GherkinStep("r1", "the order is placed"));
        var bare = GherkinScenario("bare", "Inside the bare rule", GherkinStep("b1", "the order is placed"));

        var feature = new GherkinFeature(Loc, [], "en", "Feature", "Orders", "",
        [
            new FeatureChild(null, GherkinBackground("bg", GherkinStep("bg1", "a customer is signed in"), GherkinStep("bg2", "the basket is full")), null),
            new FeatureChild(null, null, plain),
            new FeatureChild(null, null, noOwnSteps),
            new FeatureChild(new Rule(Loc, [], "Rule", "Paying", "",
                [new RuleChild(GherkinBackground("rbg", GherkinStep("rbg1", "the card is valid")), null), new RuleChild(null, ruled)], "rule-1"), null, null),
            new FeatureChild(new Rule(Loc, [], "Rule", "Bare", "", [new RuleChild(null, bare)], "rule-2"), null, null),
        ]);
        return new FeatureInfo(CultureInfo.InvariantCulture, "Features", "Orders", "", ProgrammingLanguage.CSharp, [],
            new FakeMessages(new GherkinDocument("Features/Orders.feature", feature, []),
            [
                PickleOf("p0", "Outside any rule", "plain"),
                PickleOf("p1", "With no steps of its own", "empty"),
                PickleOf("p2", "Inside the rule", "ruled"),
                PickleOf("p3", "Inside the bare rule", "bare"),
            ]));
    }

    private static ScenarioInfo Running(string title, string? pickleIndex) => new(title, null, [], null!, [], pickleIndex!);

    /// <summary>
    /// Two implementations of one rule: one run of the shape suite, recorded with Reqnroll 3.3.4's message formatter, read by the
    /// resolver and by the Cucumber lane. They give each scenario the same Background, but for shapes 11 and 15, where it is
    /// Reqnroll's own stream that differs: it drifts past a step that runs a step, and a scenario with no steps has a pickle with none.
    /// </summary>
    [Fact]
    public void Both_Gherkin_lanes_read_the_same_Background_from_one_recorded_run()
    {
        var recorded = Path.Combine(AppContext.BaseDirectory, "TestData", "ReqNRoll", "BackgroundShapes.ndjson");
        var envelopes = File.ReadLines(recorded).Select(NdjsonSerializer.Deserialize).ToList();
        var pickles = envelopes.Where(e => e.Pickle is not null).Select(e => e.Pickle).ToList();
        var fromTheDocument = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var document in envelopes.Where(e => e.GherkinDocument is not null).Select(e => e.GherkinDocument))
        {
            var own = pickles.Where(pickle => pickle.Uri == document.Uri).ToList();
            var info = new FeatureInfo(CultureInfo.InvariantCulture, "Features", document.Feature.Name, "", ProgrammingLanguage.CSharp, [], new FakeMessages(document, own));
            for (var i = 0; i < own.Count; i++)
                fromTheDocument[own[i].Name] = ExamplesBlockResolver.ResolveBackgroundSteps(info, Running(own[i].Name, i.ToString(CultureInfo.InvariantCulture)));
        }

        var scenarios = CucumberFeatureSynthesizer.BuildFromFiles([recorded]).Features.SelectMany(feature => feature.Scenarios).ToList();

        Assert.Equal(41, scenarios.Count);
        var differ = scenarios
            .Where(scenario => !fromTheDocument[scenario.DisplayName].SequenceEqual((scenario.BackgroundSteps ?? []).Select(step => step.Text)))
            .Select(scenario => scenario.DisplayName)
            .ToArray();
        Assert.Equal(["11 one", "11 two", "15 empty"], differ.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_scenario_is_given_its_features_background()
    {
        Assert.Equal(["a customer is signed in", "the basket is full"],
            ExamplesBlockResolver.ResolveBackgroundSteps(Orders(), Running("Outside any rule", "0")));
    }

    [Fact]
    public void A_scenario_in_a_rule_with_a_background_runs_the_features_then_the_rules()
    {
        Assert.Equal(["a customer is signed in", "the basket is full", "the card is valid"],
            ExamplesBlockResolver.ResolveBackgroundSteps(Orders(), Running("Inside the rule", "2")));
    }

    [Fact]
    public void A_rule_without_a_background_leaves_the_features()
    {
        Assert.Equal(["a customer is signed in", "the basket is full"],
            ExamplesBlockResolver.ResolveBackgroundSteps(Orders(), Running("Inside the bare rule", "3")));
    }

    [Fact]
    public void A_scenario_with_no_steps_of_its_own_still_runs_the_background()
    {
        // Gherkin compiles it to a pickle with no steps, so the answer has to come from the document.
        Assert.Equal(2, ExamplesBlockResolver.ResolveBackgroundSteps(Orders(), Running("With no steps of its own", "1")).Count);
    }

    [Fact]
    public void A_scenario_no_pickle_is_found_for_is_found_by_its_title()
    {
        Assert.Equal(3, ExamplesBlockResolver.ResolveBackgroundSteps(Orders(), Running("Inside the rule", null)).Count);
    }

    [Fact]
    public void Without_reqnrolls_messages_no_step_is_a_background_step()
    {
        var bare = new FeatureInfo(CultureInfo.InvariantCulture, "Features", "Orders", "", ProgrammingLanguage.CSharp, [],
            new FakeMessages(null, []));

        Assert.Empty(ExamplesBlockResolver.ResolveBackgroundSteps(bare, Running("Outside any rule", "0")));
    }

    [Theory]
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "b" }, 2)] // as written
    [InlineData(new[] { "a" }, new[] { "a", "b" }, 1)]           // the first step failed, so the second never ran
    [InlineData(new[] { "x", "a", "b" }, new[] { "a", "b" }, 0)] // a step that disagrees with the document is never counted as it
    [InlineData(new[] { "a", "x" }, new[] { "a", "b" }, 1)]
    [InlineData(new[] { "a", "b" }, new string[0], 0)]           // nothing written
    public void Only_steps_that_ran_and_agree_with_the_document_are_counted(string[] ran, string[] written, int expected)
    {
        var steps = ran.Select(text => new ScenarioStep { Keyword = "Given", Text = text }).ToArray();

        var (background, own) = ScenarioInfoEnumerableExtensions.SplitBackground(steps, written);

        Assert.Equal(steps.Take(expected).Select(s => s.Text), (background ?? []).Select(s => s.Text));
        Assert.Equal(steps.Skip(expected).Select(s => s.Text), (own ?? []).Select(s => s.Text));
    }

    [Fact]
    public void A_row_no_framework_step_opened_is_never_the_Background_though_it_reads_the_same()
    {
        // A hook's [GivenStep] with the Background's own words stands as what it is, and the Background follows it.
        ScenarioStep[] ran =
        [
            new() { Keyword = "Given", Text = "a", OutsideFrameworkStep = true },
            new() { Keyword = "Given", Text = "a" },
            new() { Keyword = "Given", Text = "b" },
            new() { Keyword = "Then", Text = "c" },
        ];

        var (background, own) = ScenarioInfoEnumerableExtensions.SplitBackground(ran, ["a", "b"]);

        Assert.Equal([ran[1], ran[2]], background!);
        Assert.Equal([ran[0], ran[3]], own!);
    }

    [Fact]
    public void A_row_no_framework_step_opened_does_not_end_the_Background_it_ran_inside()
    {
        ScenarioStep[] ran =
        [
            new() { Keyword = "Given", Text = "a" },
            new() { Text = "a step method", OutsideFrameworkStep = true },
            new() { Keyword = "Given", Text = "b" },
            new() { Keyword = "Then", Text = "c" },
        ];

        var (background, own) = ScenarioInfoEnumerableExtensions.SplitBackground(ran, ["a", "b"]);

        Assert.Equal([ran[0], ran[2]], background!);
        Assert.Equal([ran[1], ran[3]], own!);
    }

    [Fact]
    public void Nothing_listed_is_neither_a_Background_nor_steps()
    {
        var (background, own) = ScenarioInfoEnumerableExtensions.SplitBackground([], ["a"]);

        Assert.Null(background);
        Assert.Null(own);
    }

    private static ReqNRollScenarioInfo Collected(string id, string title, int backgroundCount, params ReqNRollStepInfo[] steps) => new()
    {
        ScenarioId = id,
        ScenarioTitle = title,
        FeatureTitle = "Orders",
        ScenarioTags = [],
        CombinedTags = [],
        Steps = [.. steps],
        ExecutionStatus = steps.Any(s => s.Status == ScenarioExecutionStatus.TestError) ? ScenarioExecutionStatus.TestError : ScenarioExecutionStatus.OK,
        WrittenBackground = steps.Take(backgroundCount).Select(s => s.Text).ToArray(),
    };

    [Fact]
    public void Steps_that_read_alike_stay_each_scenarios_own()
    {
        var first = Collected("a", "A first scenario", 0,
            new ReqNRollStepInfo("Given", "the basket holds", TableText: "| sku |\n| APPLE |\n", Duration: TimeSpan.FromSeconds(1)),
            new ReqNRollStepInfo("When", "the customer pays"),
            new ReqNRollStepInfo("Then", "the order is confirmed"));
        var second = Collected("b", "B second scenario", 0,
            new ReqNRollStepInfo("Given", "the basket holds", TableText: "| sku |\n| BANANA |\n", Duration: TimeSpan.FromSeconds(2)),
            new ReqNRollStepInfo("When", "the customer pays", ScenarioExecutionStatus.TestError));

        var scenarios = new[] { first, second }.ToFeatures()[0].Scenarios;

        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
        Assert.Equal(3, scenarios[0].Steps!.Length);
        Assert.Equal(ExecutionResult.Failed, scenarios[1].Steps![1].Status);
        Assert.Equal(TimeSpan.FromSeconds(2), scenarios[1].Steps![0].Duration);
        Assert.Equal("BANANA", scenarios[1].Steps![0].Parameters![0].TabularValue!.Rows[0].Values[0].Value);
    }

    [Fact]
    public void The_steps_a_background_gave_a_scenario_come_first_and_are_its_own()
    {
        var first = Collected("a", "A first scenario", 1,
            new ReqNRollStepInfo("Given", "a customer is signed in"),
            new ReqNRollStepInfo("When", "the customer pays"));
        var second = Collected("b", "B second scenario", 1,
            new ReqNRollStepInfo("Given", "a customer is signed in", ScenarioExecutionStatus.TestError));

        var scenarios = new[] { first, second }.ToFeatures()[0].Scenarios;

        Assert.Equal("a customer is signed in", Assert.Single(scenarios[0].BackgroundSteps!).Text);
        Assert.Equal(ExecutionResult.Passed, scenarios[0].BackgroundSteps![0].Status);
        Assert.Equal("the customer pays", Assert.Single(scenarios[0].Steps!).Text);

        Assert.Equal(ExecutionResult.Failed, Assert.Single(scenarios[1].BackgroundSteps!).Status);
        Assert.Null(scenarios[1].Steps);
        Assert.NotSame(scenarios[0].BackgroundSteps![0], scenarios[1].BackgroundSteps![0]);
    }

    [Fact]
    public void A_scenario_keeps_its_layout_when_another_scenario_fails()
    {
        var first = Collected("a", "A first scenario", 0,
            new ReqNRollStepInfo("Given", "a customer is signed in"),
            new ReqNRollStepInfo("Given", "the address is checked"),
            new ReqNRollStepInfo("When", "the customer pays"),
            new ReqNRollStepInfo("Then", "the order is confirmed"));
        var failing = Collected("b", "B second scenario", 0,
            new ReqNRollStepInfo("Given", "a customer is signed in"),
            new ReqNRollStepInfo("Given", "the address is checked", ScenarioExecutionStatus.TestError));
        var passing = Collected("b", "B second scenario", 0,
            new ReqNRollStepInfo("Given", "a customer is signed in"),
            new ReqNRollStepInfo("Given", "the address is checked"),
            new ReqNRollStepInfo("When", "the customer pays"),
            new ReqNRollStepInfo("Then", "a receipt is sent"));

        var whenAllPass = new[] { first, passing }.ToFeatures()[0].Scenarios[0];
        var whenOneFails = new[] { first, failing }.ToFeatures()[0].Scenarios[0];

        Assert.Equal(whenAllPass.Steps!.Length, whenOneFails.Steps!.Length);
        Assert.Equal(whenAllPass.BackgroundSteps?.Length, whenOneFails.BackgroundSteps?.Length);
    }

    [Fact]
    public void A_scenario_on_its_own_is_laid_out_as_it_is_among_others()
    {
        var only = Collected("a", "A first scenario", 1,
            new ReqNRollStepInfo("Given", "a customer is signed in"),
            new ReqNRollStepInfo("When", "the customer pays"));

        var scenario = new[] { only }.ToFeatures()[0].Scenarios[0];

        Assert.Single(scenario.BackgroundSteps!);
        Assert.Single(scenario.Steps!);
    }

    [Fact]
    public void A_step_that_differs_from_the_document_only_in_case_is_not_counted()
    {
        var ran = Collected("a", "A", 1, new ReqNRollStepInfo("Given", "A customer is signed in")) with { WrittenBackground = ["a customer is signed in"] };

        var scenario = new[] { ran }.ToFeatures()[0].Scenarios[0];

        Assert.Null(scenario.BackgroundSteps);
        Assert.Single(scenario.Steps!);
    }

    [Fact]
    public void A_count_past_the_steps_that_ran_takes_them_all()
    {
        var ran = Collected("a", "A", 1, new ReqNRollStepInfo("Given", "only one ran")) with { WrittenBackground = ["only one ran", "the second never ran"] };

        var scenario = new[] { ran }.ToFeatures()[0].Scenarios[0];

        Assert.Single(scenario.BackgroundSteps!);
        Assert.Null(scenario.Steps);
    }
}

/// <summary>#222: what the detector, which ingest still calls, and the attachment copier must hold whoever calls them.</summary>
public class StepsAStepRanTests
{
    private static ReqNRollScenarioInfo Ran(string id, params ReqNRollStepInfo[] finished) => new()
    {
        ScenarioId = id,
        ScenarioTitle = "A",
        FeatureTitle = "Orders",
        ScenarioTags = [],
        CombinedTags = [],
        Steps = [.. finished],
        WrittenBackground = [],
        ExecutionStatus = finished[^1].Status,
    };

    [Fact]
    public void A_step_a_step_ran_keeps_its_table_doc_string_and_outcome()
    {
        var id = nameof(A_step_a_step_ran_keeps_its_table_doc_string_and_outcome);
        var outerFrame = StepCollector.StartFrame(id, "Given", "a composite step");
        StepCollector.StartStep(id, "Given", "woven first", null, null);
        StepCollector.CompleteStep(id, true);
        var innerFrame = StepCollector.StartFrame(id, "Given", "the basket is listed");
        StepCollector.CompleteFrame(id, innerFrame, false);
        StepCollector.CompleteFrame(id, outerFrame, false);
        var inner = new ReqNRollStepInfo("Given", "the basket is listed", ScenarioExecutionStatus.TestError, null, "|sku|\n|APPLE|", "a doc string") { FrameId = innerFrame };
        var outer = new ReqNRollStepInfo("Given", "a composite step", ScenarioExecutionStatus.TestError) { FrameId = outerFrame };

        var mapped = new[] { Ran(id, inner, outer) }.ToFeatures()[0].Scenarios[0].Steps![0];

        Assert.Equal(ExecutionResult.Failed, mapped.Status);
        Assert.Equal(["woven first", inner.Text], mapped.SubSteps!.Select(s => s.Text));
        Assert.Null(mapped.SubSteps![0].DocString);
        Assert.Equal("a doc string", mapped.SubSteps![1].DocString);
        Assert.Equal("table", Assert.Single(mapped.SubSteps![1].Parameters!).Name);
        Assert.Equal(ExecutionResult.Failed, mapped.SubSteps![1].Status);
    }
}

public class StepsUnderAWovenStepTests
{
    // As the hooks do: a frame for each step, and the step listed once it has finished, with the frame it opened.
    private static int? Start(string id, string text) => StepCollector.StartFrame(id, "Given", text);

    private static ReqNRollStepInfo Close(string id, int? frame, string text, ScenarioExecutionStatus status = ScenarioExecutionStatus.OK, string? table = null,
        string? docString = null, InlineParamCapture[]? inlineParams = null)
    {
        StepCollector.CompleteFrame(id, frame, status == ScenarioExecutionStatus.OK);
        return new ReqNRollStepInfo("Given", text, status, null, table, docString, inlineParams) { FrameId = frame };
    }

    private static ScenarioStep[] SubStepsOf(string id, params ReqNRollStepInfo[] finished)
    {
        var info = new ReqNRollScenarioInfo
        {
            ScenarioId = id,
            ScenarioTitle = "A",
            FeatureTitle = "Orders",
            ScenarioTags = [],
            CombinedTags = [],
            Steps = [.. finished],
            WrittenBackground = [],
            ExecutionStatus = finished[^1].Status,
        };

        return new[] { info }.ToFeatures()[0].Scenarios[0].Steps![0].SubSteps!;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Given")]
    public void A_nested_step_is_not_taken_for_a_woven_step_that_reads_the_same(string? wovenKeyword)
    {
        var id = nameof(A_nested_step_is_not_taken_for_a_woven_step_that_reads_the_same) + wovenKeyword;
        var outerFrame = Start(id, "a composite step");
        StepCollector.StartStep(id, wovenKeyword, "the namesake step", null, null);
        StepCollector.CompleteStep(id, true);
        var innerFrame = Start(id, "the namesake step");
        var inner = Close(id, innerFrame, "the namesake step", ScenarioExecutionStatus.TestError, "|sku|\n|APPLE|", "a doc string");
        var outer = Close(id, outerFrame, "a composite step", ScenarioExecutionStatus.TestError);

        var subSteps = SubStepsOf(id, inner, outer);

        Assert.Equal(ExecutionResult.Passed, subSteps[0].Status);
        Assert.Null(subSteps[0].Parameters);
        Assert.Null(subSteps[0].DocString);
        Assert.Equal(ExecutionResult.Failed, subSteps[1].Status);
        Assert.Equal("table", Assert.Single(subSteps[1].Parameters!).Name);
        Assert.Equal("a doc string", subSteps[1].DocString);
    }

    [Fact]
    public void A_woven_step_that_failed_stays_failed_beside_a_nested_step_that_reads_the_same()
    {
        var id = nameof(A_woven_step_that_failed_stays_failed_beside_a_nested_step_that_reads_the_same);
        var outerFrame = Start(id, "a composite step");
        StepCollector.StartStep(id, "Given", "the namesake step", null, null);
        StepCollector.CompleteStep(id, false);
        var inner = Close(id, Start(id, "the namesake step"), "the namesake step");
        var outer = Close(id, outerFrame, "a composite step");

        var subSteps = SubStepsOf(id, inner, outer);

        Assert.Equal(ExecutionResult.Failed, subSteps[0].Status);
        Assert.Equal(ExecutionResult.Passed, subSteps[1].Status);
    }

    [Fact]
    public void A_step_found_under_a_woven_step_keeps_its_arguments_as_does_the_one_after_it()
    {
        var id = nameof(A_step_found_under_a_woven_step_keeps_its_arguments_as_does_the_one_after_it);
        var outerFrame = Start(id, "a composite step");
        StepCollector.StartStep(id, "Given", "a woven helper", null, null);
        var inHelper = Close(id, Start(id, "the basket is listed"), "the basket is listed", table: "|sku|\n|APPLE|");
        StepCollector.CompleteStep(id, true);
        var afterHelper = Close(id, Start(id, "a note says"), "a note says", docString: "a doc string");
        var outer = Close(id, outerFrame, "a composite step");

        var subSteps = SubStepsOf(id, inHelper, afterHelper, outer);

        Assert.Equal("table", Assert.Single(subSteps[0].SubSteps![0].Parameters!).Name);
        Assert.Equal("a doc string", subSteps[1].DocString);
    }

    [Fact]
    public void A_nested_steps_own_nested_steps_keep_their_arguments()
    {
        var id = nameof(A_nested_steps_own_nested_steps_keep_their_arguments);
        var outerFrame = Start(id, "a composite step");
        var middleFrame = Start(id, "the customer pays");
        var leaf = Close(id, Start(id, "the card is read"), "the card is read", table: "|sku|\n|APPLE|");
        var middle = Close(id, middleFrame, "the customer pays");
        var outer = Close(id, outerFrame, "a composite step");

        Assert.Equal("table", Assert.Single(Assert.Single(SubStepsOf(id, leaf, middle, outer)[0].SubSteps!).Parameters!).Name);
    }

    [Fact]
    public void A_nested_step_keeps_its_inline_values()
    {
        var id = nameof(A_nested_step_keeps_its_inline_values);
        var outerFrame = Start(id, "a composite step");
        var inner = Close(id, Start(id, "the basket holds APPLE"), "the basket holds APPLE", inlineParams: [new InlineParamCapture(17, 5, "APPLE", "sku")]);
        var outer = Close(id, outerFrame, "a composite step");

        Assert.NotNull(Assert.Single(SubStepsOf(id, inner, outer)).TextSegments);
    }

    [Fact]
    public void A_step_a_step_ran_reads_what_the_hooks_saw_not_what_the_collector_was_told()
    {
        // The collector only hears passed or not, so a pending step is failed there and skipped here.
        var id = nameof(A_step_a_step_ran_reads_what_the_hooks_saw_not_what_the_collector_was_told);
        var outerFrame = Start(id, "a composite step");
        var inner = Close(id, Start(id, "a pending step"), "a pending step", ScenarioExecutionStatus.StepDefinitionPending);
        var outer = Close(id, outerFrame, "a composite step", ScenarioExecutionStatus.StepDefinitionPending);

        Assert.Equal(ExecutionResult.Skipped, Assert.Single(SubStepsOf(id, inner, outer)).Status);
    }
}

public class SharedStepsTests
{
    private static Scenario Plain(string id, ExecutionResult second, string doc) => new()
    {
        Id = id,
        DisplayName = id,
        Result = second == ExecutionResult.Failed ? ExecutionResult.Failed : ExecutionResult.Passed,
        Steps =
        [
            new ScenarioStep { Keyword = "Given", Text = "the basket holds", Status = ExecutionResult.Passed, DocString = doc },
            new ScenarioStep { Keyword = "When", Text = "the customer pays", Status = second },
            new ScenarioStep { Keyword = "Then", Text = "the outcome of " + id, Status = ExecutionResult.Passed },
        ],
    };

    [Fact]
    public void The_detector_leaves_each_scenario_its_own_steps()
    {
        var scenarios = new[] { Plain("a", ExecutionResult.Passed, "apple"), Plain("b", ExecutionResult.Failed, "banana") };

        BackgroundStepsDetector.DetectAndExtract(scenarios);

        Assert.Equal(2, scenarios[1].BackgroundSteps!.Length);
        Assert.Equal("banana", scenarios[1].BackgroundSteps![0].DocString);
        Assert.Equal(ExecutionResult.Failed, scenarios[1].BackgroundSteps![1].Status);
        Assert.Equal(ExecutionResult.Passed, scenarios[0].BackgroundSteps![1].Status);
    }

    [Fact]
    public void A_file_attached_to_a_step_two_scenarios_share_does_not_stop_the_report()
    {
        var folder = Directory.CreateTempSubdirectory("krk-222-").FullName;
        var file = Path.Combine(folder, "shot.png");
        File.WriteAllText(file, "x");
        var shared = new ScenarioStep { Keyword = "Given", Text = "the basket holds", Status = ExecutionResult.Passed, Attachments = [new FileAttachment("shot.png", file)] };
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "a", DisplayName = "a", BackgroundSteps = [shared] },
                    new Scenario { Id = "b", DisplayName = "b", BackgroundSteps = [shared] },
                ],
            },
        };

        ReportGenerator.CopyAttachmentsToReportsFolder(features, Path.Combine(folder, "Reports"));

        Assert.Equal("attachments/shot.png", shared.Attachments![0].RelativePath);
    }

    [Fact]
    public void A_scenario_a_list_holds_twice_is_placed_once()
    {
        var folder = Directory.CreateTempSubdirectory("krk-222-").FullName;
        var file = Path.Combine(folder, "shot.png");
        File.WriteAllText(file, "x");
        var scenario = new Scenario { Id = "a", DisplayName = "a", Attachments = [new FileAttachment("shot.png", file)] };

        ReportGenerator.CopyAttachmentsToReportsFolder([new Feature { DisplayName = "Orders", Scenarios = [scenario, scenario] }], Path.Combine(folder, "Reports"));

        Assert.Equal("attachments/shot.png", scenario.Attachments![0].RelativePath);
    }

    [Fact]
    public void A_record_handed_to_the_generator_says_what_it_says()
    {
        foreach (var toggles in new ResolvedToggleDefaults?[] { null, new(), ResolvedToggleDefaults.BuiltIn, ResolvedToggleDefaults.BuiltIn with { HeadersShown = true }, new() { BackgroundStepsOpen = false } })
            Assert.Contains("<details class=\"scenario-background\">", WithAFailedBackgroundStep(toggles));
        Assert.Contains("<details class=\"scenario-background\" open>", WithAFailedBackgroundStep(new ResolvedToggleDefaults { BackgroundStepsOpen = true }));
    }

    [Fact]
    public void The_built_in_toggles_are_a_record_nobody_configured() =>
        Assert.Equal(new ResolvedToggleDefaults(), ResolvedToggleDefaults.BuiltIn);

    private static string WithAFailedBackgroundStep(ResolvedToggleDefaults? toggles)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "a", DisplayName = "a", Result = ExecutionResult.Failed,
                        BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "a customer is signed in", Status = ExecutionResult.Failed }],
                    },
                ],
            },
        };
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("a", "", "@startuml\n@enduml")], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"Bg222_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            separateBackgroundSteps: true, toggleDefaults: toggles);

        return ReportHtml.Read(path);
    }

    private static string Report(ScenarioStep step)
    {
        var features = new[]
        {
            new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "a", DisplayName = "a", Result = ExecutionResult.Failed, Steps = [step] }] },
        };
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("a", "", "@startuml\n@enduml")], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"Bg222_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);

        return ReportHtml.Read(path);
    }

    [Fact]
    public void A_failed_step_whose_many_sub_steps_passed_has_them_drawn_and_not_held()
    {
        var html = Report(new ScenarioStep
        {
            Keyword = "When", Text = "the customer pays", Status = ExecutionResult.Failed,
            SubSteps = Enumerable.Range(1, 30).Select(i => new ScenarioStep { Keyword = "Given", Text = $"the card is read {i} " + new string('x', 100), Status = ExecutionResult.Passed }).ToArray(),
        });

        Assert.DoesNotContain("data-steps=", html, StringComparison.Ordinal);
        Assert.Contains("the card is read 30 ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_with_a_failed_step_two_levels_down_starts_open()
    {
        var html = Report(new ScenarioStep
        {
            Keyword = "When", Text = "the customer pays", Status = ExecutionResult.Passed,
            SubSteps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "the card is read", Status = ExecutionResult.Passed,
                    SubSteps = [new ScenarioStep { Keyword = "Given", Text = "the chip answers", Status = ExecutionResult.Failed }],
                },
            ],
        });

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<details class=\"step step-collapsible\" open>").Count);
    }

    [Fact]
    public void A_failed_step_whose_sub_steps_passed_starts_open()
    {
        var step = new ScenarioStep
        {
            Keyword = "When", Text = "the customer pays", Status = ExecutionResult.Failed,
            SubSteps = [new ScenarioStep { Keyword = "Given", Text = "the card is read", Status = ExecutionResult.Passed }],
        };
        var features = new[]
        {
            new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "a", DisplayName = "a", Result = ExecutionResult.Failed, Steps = [step] }] },
        };
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("a", "", "@startuml\n@enduml")], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"Bg222_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);

        Assert.Contains("<details class=\"step step-collapsible\" open>", ReportHtml.Read(path));
    }
}

/// <summary>#222's F8: the Features table counts what each scenario ran. A step lent from another scenario left a failed
/// scenario with no failed step, or counted one failure twice, whether or not the hooks saw the run.</summary>
[Collection("DiagramsFetcher")]
public class FeaturesTableOverOwnStepsTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-features-table").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static ReqNRollScenarioInfo Ran(string id, string title, bool hooksSaw, params ReqNRollStepInfo[] steps) => new()
    {
        ScenarioId = id,
        ScenarioTitle = title,
        FeatureTitle = "Orders",
        ScenarioTags = [],
        CombinedTags = [],
        Steps = [.. steps],
        ExecutionStatus = steps.Any(s => s.Status == ScenarioExecutionStatus.TestError) ? ScenarioExecutionStatus.TestError : ScenarioExecutionStatus.OK,
        WrittenBackground = hooksSaw ? [] : null,
    };

    private string Page(params ReqNRollScenarioInfo[] scenarios)
    {
        var options = new ReportConfigurationOptions { ReportsFolderPath = _dir, GenerateSpecificationsData = false };
        DefaultDiagramsFetcher.Reset();
        ReportGenerator.CreateStandardReportsWithDiagrams(scenarios.ToFeatures(), Start, Start.AddSeconds(30), options);
        DefaultDiagramsFetcher.Reset();
        return ReportHtml.Read(Path.Combine(_dir, "TestRunReport.html"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_scenario_that_failed_counts_its_own_failed_step(bool hooksSaw)
    {
        var page = Page(
            Ran("a", "A passes", hooksSaw,
                new ReqNRollStepInfo("Given", "the basket holds"),
                new ReqNRollStepInfo("When", "the customer pays"),
                new ReqNRollStepInfo("Then", "the order is confirmed")),
            Ran("b", "B fails", hooksSaw,
                new ReqNRollStepInfo("Given", "the basket holds"),
                new ReqNRollStepInfo("When", "the customer pays", ScenarioExecutionStatus.TestError)));

        Assert.Contains("<td class=\"grp-start\">5</td><td>4</td><td>1</td><td>0</td>", page);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_scenario_that_passed_counts_none_of_another_scenarios_failure(bool hooksSaw)
    {
        var page = Page(
            Ran("a", "A fails", hooksSaw,
                new ReqNRollStepInfo("Given", "the basket holds", ScenarioExecutionStatus.TestError)),
            Ran("b", "B passes", hooksSaw,
                new ReqNRollStepInfo("Given", "the basket holds"),
                new ReqNRollStepInfo("Then", "the order is confirmed")));

        Assert.Contains("<td class=\"grp-start\">3</td><td>2</td><td>1</td><td>0</td>", page);
    }
}

/// <summary>#222: what a report made from the options does with a Background section holding a failed step.</summary>
[Collection("DiagramsFetcher")]
public class BackgroundSectionFromTheOptionsTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-background-section").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Report(ReportConfigurationOptions options, params Scenario[] scenarios)
    {
        options.ReportsFolderPath = _dir;
        options.SeparateBackgroundSteps = true;
        options.GenerateSpecificationsData = false;
        DefaultDiagramsFetcher.Reset();
        ReportGenerator.CreateStandardReportsWithDiagrams([new Feature { DisplayName = "Orders", Scenarios = scenarios }], Start, Start.AddSeconds(30), options);
        DefaultDiagramsFetcher.Reset();
        return ReportHtml.Read(Path.Combine(_dir, "TestRunReport.html"));
    }

    [Fact]
    public void The_specifications_page_reads_its_own_value()
    {
        // A scenario that passed on a retry keeps the failed attempt's step, and the page is not blank for it.
        var retried = Holding(FailedStep) with { Result = ExecutionResult.Passed };
        var options = new ReportConfigurationOptions { SpecificationsToggleDefaults = { BackgroundStepsOpen = false } };

        var testRun = Report(options, retried);

        Assert.Contains("<details class=\"scenario-background\" data-holds-failure open>", testRun);
        Assert.Contains("<details class=\"scenario-background\">", ReportHtml.Read(Path.Combine(_dir, "Specifications.html")));
    }

    private static Scenario Holding(ScenarioStep background) => new()
    {
        Id = "a222section", DisplayName = "a", Result = ExecutionResult.Failed, BackgroundSteps = [background],
    };

    private static Scenario Row(string id, string row, ExecutionResult background) => new()
    {
        Id = id,
        DisplayName = "Pay",
        OutlineId = "Pay",
        Result = background,
        ExampleValues = new Dictionary<string, string> { ["row"] = row },
        ExampleRawValues = new Dictionary<string, object?> { ["row"] = row },
        BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "a customer is signed in", Status = background }],
    };

    private static readonly ScenarioStep FailedStep = new() { Keyword = "Given", Text = "a customer is signed in", Status = ExecutionResult.Failed };

    [Fact]
    public void Left_unset_a_section_holding_a_failed_step_starts_open() =>
        Assert.Contains("<details class=\"scenario-background\" data-holds-failure open>", Report(new ReportConfigurationOptions(), Holding(FailedStep)));

    [Fact]
    public void Left_unset_a_section_whose_step_has_a_failed_sub_step_starts_open() =>
        Assert.Contains("<details class=\"scenario-background\" data-holds-failure open>", Report(new ReportConfigurationOptions(), Holding(new ScenarioStep
        {
            Keyword = "Given", Text = "a composite step", Status = ExecutionResult.Passed,
            SubSteps = [new ScenarioStep { Keyword = "Given", Text = "the inner step", Status = ExecutionResult.Failed }],
        })));

    [Fact]
    public void Set_to_false_a_section_holding_a_failed_step_stays_closed()
    {
        var html = Report(new ReportConfigurationOptions { TestRunReportToggleDefaults = { BackgroundStepsOpen = false } }, Holding(FailedStep));

        Assert.Contains("<details class=\"scenario-background\">", html);
        Assert.DoesNotContain("<details class=\"scenario-background\" data-holds-failure", html);
    }

    [Fact]
    public void Set_to_true_a_section_holding_a_failed_step_is_open_with_no_hold() =>
        Assert.Contains("<details class=\"scenario-background\" open>", Report(new ReportConfigurationOptions { TestRunReportToggleDefaults = { BackgroundStepsOpen = true } }, Holding(FailedStep)));

    [Fact]
    public void Left_unset_a_failing_rows_section_starts_open_and_a_passing_rows_does_not()
    {
        var html = Report(new ReportConfigurationOptions(), Row("a222rowa", "passes", ExecutionResult.Passed), Row("a222rowb", "fails", ExecutionResult.Failed));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<details class=\"scenario-background\" data-holds-failure open>"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<details class=\"scenario-background\">"));
    }

    [Fact]
    public void Set_to_false_a_failing_rows_section_stays_closed() =>
        Assert.DoesNotContain("<details class=\"scenario-background\" data-holds-failure", Report(new ReportConfigurationOptions { TestRunReportToggleDefaults = { BackgroundStepsOpen = false } },
            Row("a222rowa", "passes", ExecutionResult.Passed), Row("a222rowb", "fails", ExecutionResult.Failed)));

    [Theory]
    [InlineData(null, null, false, true)]
    [InlineData(null, null, true, true)]
    [InlineData(false, null, false, false)]
    [InlineData(false, null, true, false)]
    [InlineData(null, false, false, true)]
    [InlineData(null, false, true, false)]
    [InlineData(true, null, false, false)]
    [InlineData(true, false, true, false)]
    public void Only_a_section_left_unset_opens_for_a_failure(bool? testRun, bool? specifications, bool forSpecifications, bool expected)
    {
        var options = new ReportConfigurationOptions
        {
            TestRunReportToggleDefaults = { BackgroundStepsOpen = testRun },
            SpecificationsToggleDefaults = { BackgroundStepsOpen = specifications },
        };

        Assert.Equal(expected, ReportToggleDefaultsResolver.BackgroundOpensOnFailure(options, forSpecifications));
    }
}

/// <summary>#222: what the fix leaves as it was, for whoever reads the public lists or builds a scenario by hand.</summary>
public class WhatTheFixLeavesTests
{
    private static ReqNRollScenarioInfo ByHand(string id, string title, params ReqNRollStepInfo[] steps) => new()
    {
        ScenarioId = id,
        ScenarioTitle = title,
        FeatureTitle = "Orders",
        ScenarioTags = [],
        CombinedTags = [],
        Steps = [.. steps],
    };

    [Fact]
    public void Scenarios_built_by_hand_keep_the_guess_each_with_its_own_steps()
    {
        var first = ByHand("a", "A first", new ReqNRollStepInfo("Given", "a customer is signed in"), new ReqNRollStepInfo("Then", "the order is confirmed"));
        var second = ByHand("b", "B second", new ReqNRollStepInfo("Given", "a customer is signed in", ScenarioExecutionStatus.TestError), new ReqNRollStepInfo("Then", "a receipt is sent"));

        var scenarios = new[] { first, second }.ToFeatures()[0].Scenarios;

        Assert.Equal("a customer is signed in", Assert.Single(scenarios[0].BackgroundSteps!).Text);
        Assert.Equal(ExecutionResult.Failed, Assert.Single(scenarios[1].BackgroundSteps!).Status);
        Assert.NotSame(scenarios[0].BackgroundSteps![0], scenarios[1].BackgroundSteps![0]);
    }

    [Fact]
    public void A_feature_the_hooks_saw_is_not_guessed_at_when_the_document_writes_nothing()
    {
        var first = ByHand("a", "A first", new ReqNRollStepInfo("Given", "a customer is signed in"), new ReqNRollStepInfo("Then", "the order is confirmed")) with { WrittenBackground = [] };
        var second = ByHand("b", "B second", new ReqNRollStepInfo("Given", "a customer is signed in"), new ReqNRollStepInfo("Then", "a receipt is sent")) with { WrittenBackground = [] };

        var scenarios = new[] { first, second }.ToFeatures()[0].Scenarios;

        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
    }

    [Fact]
    public void A_step_a_step_ran_stays_in_the_list_and_is_no_row()
    {
        const string id = "a222left";
        var outerFrame = StepCollector.StartFrame(id, "When", "the customer pays");
        var innerFrame = StepCollector.StartFrame(id, "Given", "the card is read");
        StepCollector.CompleteFrame(id, innerFrame, true);
        StepCollector.CompleteFrame(id, outerFrame, true);
        var thenFrame = StepCollector.StartFrame(id, "Then", "the order is confirmed");
        StepCollector.CompleteFrame(id, thenFrame, true);
        var inner = new ReqNRollStepInfo("Given", "the card is read") { FrameId = innerFrame };
        var outer = new ReqNRollStepInfo("When", "the customer pays") { FrameId = outerFrame };
        var then = new ReqNRollStepInfo("Then", "the order is confirmed") { FrameId = thenFrame };
        var collected = ByHand(id, "A first", inner, outer, then) with { WrittenBackground = [] };

        var scenario = new[] { collected }.ToFeatures()[0].Scenarios[0];

        Assert.Equal(3, collected.Steps.Count);
        Assert.Equal(["the customer pays", "the order is confirmed"], scenario.Steps!.Select(s => s.Text));
    }

    [Fact]
    public void A_feature_any_scenario_of_which_the_hooks_saw_is_not_guessed_at()
    {
        var first = ByHand("a", "A first", new ReqNRollStepInfo("Given", "a customer is signed in"), new ReqNRollStepInfo("Then", "the order is confirmed")) with { WrittenBackground = [] };
        var second = ByHand("b", "B second", new ReqNRollStepInfo("Given", "a customer is signed in"), new ReqNRollStepInfo("Then", "a receipt is sent"));

        var scenarios = new[] { first, second }.ToFeatures()[0].Scenarios;

        Assert.Null(scenarios[0].BackgroundSteps);
        Assert.Null(scenarios[1].BackgroundSteps);
    }

    [Fact]
    public void The_hooks_public_methods_are_the_four_they_were()
    {
        var methods = typeof(ReqNRollTrackingHooks).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.Equal(["AfterScenario", "AfterStep", "BeforeScenario", "BeforeStep"], methods.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));
    }
}

[Collection("DiagramsFetcher")]
public class QueryOverBackgroundStepsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_failure_outside_any_step_is_said_to_be_so_when_only_Background_steps_are_listed()
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(Path.GetTempPath(), "a222hint-" + Guid.NewGuid().ToString("N"));
        options.GenerateComponentDiagram = false;

        IngestPipeline.Run(new IngestRequest
        {
            Interactions = [],
            TestRecords =
            [
                new() { Event = "start", TestId = "t", TestName = "t", Timestamp = T0 },
                new() { Event = "step", TestId = "t", Keyword = "Given", Text = "a background step", Background = true, Status = "passed", Timestamp = T0.AddSeconds(1) },
                new() { Event = "end", TestId = "t", Status = "failed", Timestamp = T0.AddSeconds(2) },
            ],
            Options = options,
        });

        var output = new StringWriter();
        QueryCommand.Run(["failures", options.ReportsFolderPath], output, new StringWriter());
        Assert.Contains("no step is marked failed", output.ToString());
    }
}
