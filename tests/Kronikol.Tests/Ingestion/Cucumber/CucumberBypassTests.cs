using System.Text.Json;
using System.Text.Json.Serialization;
using Kronikol.Ingestion;
using Kronikol.Ingestion.Cucumber;
using Kronikol.Reports;

namespace Kronikol.Tests.Ingestion.Cucumber;

/// <summary>
/// A step a test skipped over while the steps after it ran is <see cref="ExecutionResult.Bypassed"/> in the Cucumber
/// lane, and so is its scenario (#105, <c>plans/CUCUMBER_BYPASS_PLAN.md</c> §4.1 and §4.3). The streams are built here,
/// one test step per line of the fact, and the two producers measured while planning are checked in as fixtures: no
/// Cucumber runner measured runs a step after a skipped one, so nothing they write may read as a bypass.
/// </summary>
public class CucumberBypassTests
{
    private const string Reason = "Bypassed on dev: mock Gemini is not in the deployed path";
    private const string TestId = "bypass-test-1";

    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>An attachment one attempt made inside a test step.</summary>
    internal sealed record Attachment(string Name, string Body, string? Encoding = null, string MediaType = "text/plain");

    /// <summary>
    /// One test step of the stream and what one attempt reported for it: a Gherkin step, or a hook when
    /// <paramref name="Hook"/> is set (its text is then only a label for the fact).
    /// </summary>
    internal sealed record Step(string Text, string Status, string? Message = null, bool Hook = false, Attachment[]? Attachments = null);

    /// <summary>
    /// A messages stream of one scenario with one attempt, its test steps in order. The attempt attaches a
    /// <c>kronikol-test-id</c> (<see cref="TestId"/>), as the Kronikol fixture does.
    /// </summary>
    internal static string Stream(params Step[] steps) => Attempts(steps);

    /// <summary>
    /// As <see cref="Stream"/>, with one attempt per array (a retried scenario, the last attempt its verdict), every
    /// attempt carrying the same test steps in the first one's order and the same test id.
    /// </summary>
    internal static string Attempts(params Step[][] attempts)
    {
        var layout = attempts[0];
        var indexed = layout.Select((step, index) => (Step: step, Index: index)).ToArray();
        var gherkin = indexed.Where(x => !x.Step.Hook).ToArray();
        var lines = new List<string>
        {
            Json(new
            {
                gherkinDocument = new
                {
                    uri = "features/bypass.feature",
                    feature = new
                    {
                        name = "Bypass",
                        children = new[]
                        {
                            new
                            {
                                scenario = new
                                {
                                    id = "sc",
                                    keyword = "Scenario",
                                    name = "a scenario with a bypass",
                                    steps = gherkin.Select((x, n) => new
                                    {
                                        id = $"st-{x.Index}",
                                        keyword = n == 0 ? "Given " : "And ",
                                        keywordType = n == 0 ? "Context" : "Conjunction",
                                        text = x.Step.Text,
                                    }).ToArray(),
                                },
                            },
                        },
                    },
                },
            }),
        };

        foreach (var (_, index) in indexed.Where(x => x.Step.Hook))
            lines.Add(Json(new { hook = new { id = $"hk-{index}", type = "AFTER_TEST_CASE" } }));

        lines.Add(Json(new
        {
            pickle = new
            {
                id = "pk",
                uri = "features/bypass.feature",
                astNodeIds = new[] { "sc" },
                name = "a scenario with a bypass",
                language = "en",
                steps = gherkin.Select(x => new { id = $"ps-{x.Index}", text = x.Step.Text, type = "Context", astNodeIds = new[] { $"st-{x.Index}" } }).ToArray(),
            },
        }));
        lines.Add(Json(new
        {
            testCase = new
            {
                id = "tc",
                pickleId = "pk",
                testSteps = indexed.Select(x => x.Step.Hook
                    ? (object)new { id = $"ts-{x.Index}", hookId = $"hk-{x.Index}" }
                    : new { id = $"ts-{x.Index}", pickleStepId = $"ps-{x.Index}" }).ToArray(),
            },
        }));

        var clock = 1_790_605_400_000L;
        for (var a = 0; a < attempts.Length; a++)
        {
            var attemptId = $"att-{a}";
            lines.Add(Json(new { testCaseStarted = new { id = attemptId, attempt = a, testCaseId = "tc", timestamp = Timestamp(clock) } }));
            lines.Add(Json(new { attachment = new { testCaseStartedId = attemptId, mediaType = "text/plain", fileName = "kronikol-test-id", body = TestId, contentEncoding = "IDENTITY" } }));
            for (var i = 0; i < attempts[a].Length; i++)
            {
                var step = attempts[a][i];
                clock += 100;
                lines.Add(Json(new { testStepStarted = new { testCaseStartedId = attemptId, testStepId = $"ts-{i}", timestamp = Timestamp(clock) } }));
                foreach (var attachment in step.Attachments ?? [])
                {
                    lines.Add(Json(new
                    {
                        attachment = new
                        {
                            testCaseStartedId = attemptId,
                            testStepId = $"ts-{i}",
                            mediaType = attachment.MediaType,
                            fileName = attachment.Name,
                            body = attachment.Body,
                            contentEncoding = attachment.Encoding ?? "IDENTITY",
                        },
                    }));
                }

                clock += 50;
                lines.Add(Json(new
                {
                    testStepFinished = new
                    {
                        testCaseStartedId = attemptId,
                        testStepId = $"ts-{i}",
                        testStepResult = new { duration = new { seconds = 0, nanos = 50_000_000 }, status = step.Status, message = step.Message },
                        timestamp = Timestamp(clock),
                    },
                }));
            }

            clock += 100;
            lines.Add(Json(new { testCaseFinished = new { testCaseStartedId = attemptId, timestamp = Timestamp(clock) } }));
        }

        return string.Join("\n", lines);
    }

    private static string Json(object envelope) => JsonSerializer.Serialize(envelope, JsonOptions);

    private static object Timestamp(long milliseconds) => new { seconds = milliseconds / 1000, nanos = (int)(milliseconds % 1000) * 1_000_000 };

    internal static CucumberSynthesisResult Synthesise(string stream, CucumberSynthesisOptions? options = null) =>
        CucumberFeatureSynthesizer.Build(CucumberMessagesReader.Read(new StringReader(stream)), options);

    private static Scenario Only(CucumberSynthesisResult result) => result.Features.Single().Scenarios.Single();

    private static TestRunRecord[] StepMarkers(CucumberSynthesisResult result) =>
        result.Markers.Where(m => m.Event == "step").ToArray();

    private static ExecutionResult?[] Statuses(Scenario scenario) => scenario.Steps!.Select(s => s.Status).ToArray();

    // ---- the inference (§4.1 rule 3) -----------------------------------------------------------

    [Fact]
    public void A_skipped_step_that_a_later_step_ran_after_is_bypassed_with_its_message_as_the_reason()
    {
        var result = Synthesise(Stream(
        [
            new("the overview has loaded", "PASSED"),
            new("mock Gemini served the summary", "SKIPPED", Reason),
            new("the figure on screen is the figure the API returned", "PASSED"),
        ]));

        var scenario = Only(result);
        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Bypassed, ExecutionResult.Passed], Statuses(scenario));
        var bypassed = scenario.Steps![1];
        Assert.Equal(Reason, bypassed.BypassReason);
        Assert.Null(bypassed.Comments);
        Assert.Null(bypassed.FailureMessage);
        Assert.Equal(ExecutionResult.Bypassed, scenario.Result);
        Assert.Null(scenario.ErrorMessage);

        // The markers say what the scenario says, so attribution, the diagram and history read one verdict.
        var marker = StepMarkers(result)[1];
        Assert.Equal(("bypassed", Reason, (string?)null), (marker.Status, marker.BypassReason, marker.Error));
        Assert.Equal("bypassed", Assert.Single(result.Markers, m => m.Event == "end").Status);
    }

    [Fact]
    public void A_bypassed_step_before_a_step_that_skipped_the_rest_leaves_the_scenario_skipped()
    {
        // Failed over Skipped over Bypassed over Passed, the order LightBDD's own status takes.
        var scenario = Only(Synthesise(Stream(
        [
            new("the overview has loaded", "PASSED"),
            new("mock Gemini served the summary", "SKIPPED", Reason),
            new("the figure on screen is the figure the API returned", "PASSED"),
            new("the export matches the screen", "SKIPPED", "Skipped: no export on dev"),
        ])));

        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Bypassed, ExecutionResult.Passed, ExecutionResult.Skipped], Statuses(scenario));
        Assert.Equal(ExecutionResult.Skipped, scenario.Result);
        Assert.Equal(["Skipped: no export on dev"], scenario.Steps![3].Comments!);
        Assert.Null(scenario.Steps[3].BypassReason);
    }

    [Theory]
    [InlineData("FAILED", "SKIPPED", "SKIPPED")]
    [InlineData("PASSED", "FAILED", "SKIPPED")]
    public void Steps_skipped_after_a_failure_stay_skipped_and_the_scenario_failed(string first, string second, string third)
    {
        // The second case catches a rule that reads any step that ran, not a LATER one: the skipped step has a passed
        // step before it and nothing after it.
        var scenario = Only(Synthesise(Stream(
        [
            new("the overview has loaded", first, first == "FAILED" ? "Error: boom" : null),
            new("mock Gemini served the summary", second, second == "FAILED" ? "Error: boom" : null),
            new("the figure on screen is the figure the API returned", third),
        ])));

        Assert.DoesNotContain(ExecutionResult.Bypassed, Statuses(scenario));
        Assert.All(scenario.Steps!.Where(s => s.Status != ExecutionResult.Failed && s.Status != ExecutionResult.Passed),
            s => Assert.Equal(ExecutionResult.Skipped, s.Status));
        Assert.Equal(ExecutionResult.Failed, scenario.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_hook_that_ran_after_a_skipped_step_is_not_a_step_that_ran(bool includeHooks)
    {
        // Both producers measured run after-hooks after the steps they skipped (CUCUMBER_BYPASS_PLAN.md C9).
        var scenario = Only(Synthesise(Stream(
        [
            new("the overview has loaded", "PASSED"),
            new("mock Gemini served the summary", "SKIPPED", Reason),
            new("After hook", "PASSED", Hook: true),
        ]), new CucumberSynthesisOptions { IncludeHooks = includeHooks }));

        Assert.Equal(ExecutionResult.Skipped, scenario.Steps![1].Status);
        Assert.Equal([Reason], scenario.Steps[1].Comments!);
        Assert.Equal(ExecutionResult.Skipped, scenario.Result);
    }

    [Theory]
    [InlineData("UNDEFINED")]
    [InlineData("AMBIGUOUS")]
    [InlineData("PENDING")]
    public void A_later_step_that_did_not_run_is_not_a_step_that_ran(string later)
    {
        // cucumber-js reports a later undefined step UNDEFINED while it skips; none of the three ran its body.
        var scenario = Only(Synthesise(Stream(
        [
            new("the overview has loaded", "PASSED"),
            new("mock Gemini served the summary", "SKIPPED", Reason),
            new("a step nobody defined", later),
        ])));

        Assert.Equal(ExecutionResult.Skipped, scenario.Steps![1].Status);
        Assert.Null(scenario.Steps[1].BypassReason);
    }

    [Fact]
    public void A_pending_step_that_a_later_step_ran_after_stays_skipped()
    {
        // Only a step reported exactly SKIPPED is read as skipped over; PENDING is a step not yet written.
        var scenario = Only(Synthesise(Stream(
        [
            new("the overview has loaded", "PASSED"),
            new("mock Gemini served the summary", "PENDING", "TODO: write this step"),
            new("the figure on screen is the figure the API returned", "PASSED"),
        ])));

        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Skipped, ExecutionResult.Passed], Statuses(scenario));
        Assert.Equal(["TODO: write this step"], scenario.Steps![1].Comments!);
        Assert.Equal(ExecutionResult.Skipped, scenario.Result);
    }

    [Fact]
    public void A_skipped_last_step_reads_as_skipped_with_its_message_a_comment()
    {
        // The documented limit: nothing in the messages tells a bypassed last step from one that skipped the rest.
        var scenario = Only(Synthesise(Stream(
        [
            new("the overview has loaded", "PASSED"),
            new("the figure on screen is the figure the API returned", "PASSED"),
            new("mock Gemini served the summary", "SKIPPED", Reason),
        ])));

        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Passed, ExecutionResult.Skipped], Statuses(scenario));
        Assert.Equal([Reason], scenario.Steps![2].Comments!);
        Assert.Null(scenario.Steps[2].BypassReason);
        Assert.Equal(ExecutionResult.Skipped, scenario.Result);
    }

    [Fact]
    public void Only_the_attempt_the_scenario_is_built_from_is_read()
    {
        var scenario = Only(Synthesise(Attempts(
            [
                new("the overview has loaded", "PASSED"),
                new("mock Gemini served the summary", "SKIPPED", Reason),
                new("the figure on screen is the figure the API returned", "PASSED"),
            ],
            [
                new("the overview has loaded", "PASSED"),
                new("mock Gemini served the summary", "PASSED"),
                new("the figure on screen is the figure the API returned", "PASSED"),
            ])));

        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Passed, ExecutionResult.Passed], Statuses(scenario));
        Assert.Equal(ExecutionResult.Passed, scenario.Result);
        Assert.Contains("retry 1", scenario.Labels!);
    }

    [Fact]
    public void A_bypass_in_the_last_attempt_is_read_after_an_earlier_attempt_failed()
    {
        var scenario = Only(Synthesise(Attempts(
            [
                new("the overview has loaded", "FAILED", "Error: flaky"),
                new("mock Gemini served the summary", "SKIPPED"),
                new("the figure on screen is the figure the API returned", "SKIPPED"),
            ],
            [
                new("the overview has loaded", "PASSED"),
                new("mock Gemini served the summary", "SKIPPED", Reason),
                new("the figure on screen is the figure the API returned", "PASSED"),
            ])));

        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Bypassed, ExecutionResult.Passed], Statuses(scenario));
        Assert.Equal(ExecutionResult.Bypassed, scenario.Result);
    }

    // ---- the producers measured ------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nothing_cucumber_js_wrote_reads_as_a_bypass(bool includeHooks)
    {
        // cucumber-js 12.9.0: `return 'skipped'` skipped the rest, reported a later undefined step UNDEFINED and ran the
        // After hook after them; a failure skipped the rest. Neither is a bypass.
        var result = CucumberFeatureSynthesizer.BuildFromFiles([Fixture("cucumber-js-12.9-skip-messages.ndjson")],
            new CucumberSynthesisOptions { IncludeHooks = includeHooks });

        var scenarios = result.Features.SelectMany(f => f.Scenarios).ToArray();
        Assert.Equal(2, scenarios.Length);
        Assert.All(scenarios, s => Assert.DoesNotContain(ExecutionResult.Bypassed, Statuses(s)));
        var skipped = scenarios.Single(s => s.DisplayName == "A step returns skipped");
        Assert.Equal(ExecutionResult.Skipped, skipped.Steps![1].Status);
        Assert.Equal(ExecutionResult.Skipped, skipped.Steps[2].Status);
        Assert.Equal(ExecutionResult.Failed, skipped.Result); // its undefined step
        Assert.Equal(ExecutionResult.Failed, scenarios.Single(s => s.DisplayName == "A step fails").Result);
        Assert.DoesNotContain(result.Markers, m => m.Status == "bypassed");
    }

    [Fact]
    public void What_playwright_bdd_wrote_reads_as_it_ran()
    {
        // playwright-bdd 9.2.0 on @playwright/test 1.62.1, as published: `$test.skip` skipped the rest of the test and
        // wrote no message; a failure skipped the rest; a step that attached kronikol-bypass and returned was PASSED.
        var result = CucumberFeatureSynthesizer.BuildFromFiles([Fixture("playwright-bdd-9.2-bypass-messages.ndjson")]);
        Scenario Named(string name) => result.Features.SelectMany(f => f.Scenarios).Single(s => s.DisplayName == name);

        var testSkip = Named("A step calls test.skip");
        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Skipped, ExecutionResult.Skipped], Statuses(testSkip));
        Assert.Equal(ExecutionResult.Skipped, testSkip.Result);

        var fails = Named("A step fails");
        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Failed, ExecutionResult.Skipped], Statuses(fails));
        Assert.Equal(ExecutionResult.Failed, fails.Result);

        var attached = Named("A step attaches kronikol-bypass and returns early");
        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Passed, ExecutionResult.Passed], Statuses(attached));
        Assert.Equal(ExecutionResult.Passed, attached.Result);
    }

    [Fact]
    public void The_golden_playwright_bdd_run_synthesises_as_it_did_before_the_bypass_rule()
    {
        // The "nothing else changes" guard: every verdict, step status, reason, comment and marker of the golden fixture,
        // pinned from 4.6.0 (CUCUMBER_BYPASS_PLAN.md T8). It has no bypass in it, so not one of them may move.
        var actual = VerdictProjection(CucumberFixtures.Build());
        var expectedPath = Fixture("playwright-bdd-9.2-verdicts.json");
        if (!File.Exists(expectedPath))
        {
            var written = Path.Combine(Path.GetTempPath(), "playwright-bdd-9.2-verdicts.actual.json");
            File.WriteAllText(written, actual);
            Assert.Fail($"No pinned projection at {expectedPath}; this run's is at {written}.");
        }

        Assert.Equal(File.ReadAllText(expectedPath).Replace("\r\n", "\n"), actual);
    }

    internal static string Fixture(string name) => Path.Combine(CucumberFixtures.Directory, name);

    /// <summary>
    /// What the bypass rule could move in a synthesis: each scenario's verdict and error, each step's status, reason,
    /// comments and failure message (background and hook steps included), and each marker's status, error and reason.
    /// </summary>
    internal static string VerdictProjection(CucumberSynthesisResult result)
    {
        static object StepOf(ScenarioStep s) => new
        {
            s.Keyword,
            s.Text,
            Status = s.Status?.ToString(),
            s.BypassReason,
            s.Comments,
            s.FailureMessage,
            SubSteps = s.SubSteps?.Select(StepOf).ToArray(),
        };

        var projection = new
        {
            Scenarios = result.Features.SelectMany(f => f.Scenarios).Select(s => new
            {
                s.Id,
                s.DisplayName,
                Result = s.Result.ToString(),
                s.ErrorMessage,
                s.Labels,
                BackgroundSteps = s.BackgroundSteps?.Select(StepOf).ToArray(),
                Steps = s.Steps?.Select(StepOf).ToArray(),
            }).ToArray(),
            Markers = result.Markers.Select(m => new { m.Event, m.TestId, m.Text, m.Status, m.Error, m.BypassReason }).ToArray(),
        };

        return JsonSerializer.Serialize(projection, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
    }
}
