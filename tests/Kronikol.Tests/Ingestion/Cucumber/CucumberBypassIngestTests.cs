using System.Text.Json.Nodes;
using Kronikol.History;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.Tests.Ingestion.Cucumber.CucumberBypassTests;

namespace Kronikol.Tests.Ingestion.Cucumber;

/// <summary>
/// #105 through <see cref="IngestPipeline"/>: the issue's own files, with a tests file, Cucumber Messages or both, give
/// one answer (the step <c>Bypassed</c> with its reason, the scenario <c>Bypassed</c>, history <c>B</c>), and a tests
/// file's <c>bypassed</c> step survives an ingest that also reads the messages (CUCUMBER_BYPASS_PLAN.md §4.2).
/// </summary>
[Collection("DiagramsFetcher")]
public class CucumberBypassIngestTests : IDisposable
{
    private const string IssueTestId = "proto-bypass-1";
    private const string IssueReason = "Bypassed on dev: mock Gemini is not in the deployed path";
    private const string IssueScenario = "A local-only assertion is bypassed live and the rest still run";
    private const string BypassedText = "mock Gemini served the summary";
    private const string LastText = "the figure on screen is the figure the API returned";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-cucumber-bypass-" + Guid.NewGuid().ToString("N"));

    public CucumberBypassIngestTests()
    {
        Directory.CreateDirectory(_dir);
        RequestResponseLogger.Redaction = null;
    }

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string PathOf(string name) => Path.Combine(_dir, name);

    private IngestResult Ingest(string? testsFile, string? messagesFile, string output = "Reports")
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = PathOf(output);
        options.HistoryFilePath = PathOf(output + ".history.ndjson");
        options.SuiteName = "Bypass";
        options.WriteRunSummaryToConsole = false;
        return IngestPipeline.Run(new IngestRequest
        {
            TestsFile = testsFile,
            CucumberMessagesFiles = messagesFile is null ? [] : [messagesFile],
            Options = options,
        });
    }

    private static Scenario Only(IngestResult result) => result.Features.SelectMany(f => f.Scenarios).Single();

    private static ExecutionResult?[] Statuses(Scenario scenario) => scenario.Steps!.Select(s => s.Status).ToArray();

    private string HistoryLetters(string output = "Reports") =>
        HistoryFragment.Parse(File.ReadAllText(Path.Combine(PathOf(output), HistoryFormat.FragmentFileName))).Run.Results;

    /// <summary>
    /// The issue's messages with one Gherkin step's result rewritten (<c>step-0</c> to <c>step-2</c>): what an
    /// unpatched producer writes for the same run, or the bypass moved to another step.
    /// </summary>
    private string IssueMessagesWith(params (int Step, string Status, string? Message)[] rewrites)
    {
        var lines = File.ReadAllLines(Fixture("issue-105-messages.ndjson")).Where(l => !string.IsNullOrWhiteSpace(l)).Select(line =>
        {
            var node = JsonNode.Parse(line)!.AsObject();
            if (node["testStepFinished"] is not JsonObject finished)
                return line;
            var stepId = finished["testStepId"]!.GetValue<string>();
            foreach (var (step, status, message) in rewrites)
            {
                if (!stepId.EndsWith($"-step-{step}", StringComparison.Ordinal))
                    continue;
                var result = finished["testStepResult"]!.AsObject();
                result["status"] = status;
                result.Remove("message");
                if (message is not null)
                    result["message"] = message;
            }

            return node.ToJsonString();
        });
        var path = PathOf($"messages-{Guid.NewGuid():N}.ndjson");
        File.WriteAllLines(path, lines);
        return path;
    }

    private string TestsFile(params TestRunRecord[] records)
    {
        var path = PathOf($"tests-{Guid.NewGuid():N}.ndjson");
        File.WriteAllLines(path, records.Select(r => r.ToJson()));
        return path;
    }

    private static TestRunRecord StepRecord(string testId, string text, string status, string? reason = null, int level = 0) => new()
    {
        Event = "step",
        TestId = testId,
        Text = text,
        Keyword = "Then",
        Level = level,
        Status = status,
        BypassReason = reason,
        Timestamp = DateTimeOffset.Parse("2026-09-28T14:23:24.840Z"),
    };

    // ---- the issue's three runs (T2, T12) --------------------------------------------------------

    [Fact]
    public void The_issues_three_runs_give_one_answer()
    {
        var runs = new[]
        {
            ("messages", Ingest(null, Fixture("issue-105-messages.ndjson"), "A")),
            ("tests", Ingest(Fixture("issue-105-tests.ndjson"), null, "B")),
            ("both", Ingest(Fixture("issue-105-tests.ndjson"), Fixture("issue-105-messages.ndjson"), "C")),
        };

        foreach (var (name, result) in runs)
        {
            var scenario = Only(result);
            Assert.True(scenario.Result == ExecutionResult.Bypassed, $"{name}: the scenario was {scenario.Result}");
            Assert.Equal([ExecutionResult.Passed, ExecutionResult.Bypassed, ExecutionResult.Passed], Statuses(scenario));
            var step = scenario.Steps![1];
            Assert.Equal(BypassedText, step.Text);
            Assert.Equal(IssueReason, step.BypassReason);
            Assert.Null(step.Comments);
            // The synthesis's warnings reach the diagnostics now, and a well-formed run has none: not its source envelope.
            Assert.DoesNotContain(result.Diagnostics, d => d.Message.StartsWith("Cucumber messages:", StringComparison.Ordinal));
        }

        Assert.Equal(["B", "B", "B"], new[] { "A", "B", "C" }.Select(HistoryLetters));
    }

    [Fact]
    public void A_tests_file_bypass_survives_the_merge_where_the_messages_say_the_step_passed()
    {
        // T11: what an unpatched playwright-bdd writes for a step that returned early is PASSED.
        var result = Ingest(Fixture("issue-105-tests.ndjson"), IssueMessagesWith((1, "PASSED", null)));

        var scenario = Only(result);
        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Bypassed, ExecutionResult.Passed], Statuses(scenario));
        Assert.Equal(IssueReason, scenario.Steps![1].BypassReason);
        Assert.Equal(ExecutionResult.Bypassed, scenario.Result);
        Assert.Equal(IssueScenario, scenario.DisplayName); // the messages still own the structure
        Assert.Equal("B", HistoryLetters());
    }

    [Fact]
    public void A_bypassed_last_step_is_read_from_the_tests_file()
    {
        // T12's second case: the messages alone cannot tell a bypassed last step from one that skipped the rest.
        var messages = IssueMessagesWith((1, "PASSED", null), (2, "SKIPPED", IssueReason));
        var tests = TestsFile(
            new TestRunRecord { Event = "start", TestId = IssueTestId, TestName = IssueScenario, Timestamp = DateTimeOffset.Parse("2026-09-28T14:23:24.837Z") },
            StepRecord(IssueTestId, "the overview has loaded", "passed"),
            StepRecord(IssueTestId, BypassedText, "passed"),
            StepRecord(IssueTestId, LastText, "bypassed", IssueReason),
            new TestRunRecord { Event = "end", TestId = IssueTestId, Status = "passed", Timestamp = DateTimeOffset.Parse("2026-09-28T14:23:24.846Z") });

        var scenario = Only(Ingest(tests, messages));

        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Passed, ExecutionResult.Bypassed], Statuses(scenario));
        Assert.Equal(IssueReason, scenario.Steps![2].BypassReason);
        Assert.Null(scenario.Steps[2].Comments);
        Assert.Equal(ExecutionResult.Bypassed, scenario.Result);

        var withoutTestsFile = Only(Ingest(null, messages, "messages-only"));
        Assert.Equal(ExecutionResult.Skipped, withoutTestsFile.Result);
    }

    [Fact]
    public void A_bypassed_record_that_matches_no_gherkin_step_changes_nothing_and_is_counted_once()
    {
        // T13: the reporter worded the step differently, or wrote it nested.
        var messages = IssueMessagesWith((1, "PASSED", null));
        var tests = TestsFile(
            StepRecord(IssueTestId, "mock Gemini served a summary", "bypassed", IssueReason),
            StepRecord(IssueTestId, BypassedText, "bypassed", IssueReason, level: 1));

        var result = Ingest(tests, messages);

        var scenario = Only(result);
        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Passed, ExecutionResult.Passed], Statuses(scenario));
        Assert.Equal(ExecutionResult.Passed, scenario.Result);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Message.Contains("matched no Gherkin step"));
        Assert.StartsWith("2 bypassed step record(s)", diagnostic.Message);
    }

    [Fact]
    public void A_tests_file_step_finds_its_gherkin_step_by_text_in_order()
    {
        // T14: the reporter wrote a setup step of its own before two Gherkin steps that read the same; the second of them
        // was bypassed. By position the bypass would land on the third Gherkin step.
        var stream = Stream(
        [
            new("the cache is warm", "PASSED"),
            new("the cache is warm", "PASSED"),
            new("the answer is shown", "PASSED"),
        ]);
        var messages = PathOf("same-text.ndjson");
        File.WriteAllText(messages, stream);
        var tests = TestsFile(
            StepRecord("bypass-test-1", "the reporter's own setup", "passed"),
            StepRecord("bypass-test-1", "the cache is warm", "passed"),
            StepRecord("bypass-test-1", " the cache is warm ", "bypassed", "warmed already"),
            StepRecord("bypass-test-1", "the answer is shown", "passed"));

        var scenario = Only(Ingest(tests, messages));

        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Bypassed, ExecutionResult.Passed], Statuses(scenario));
        Assert.Equal("warmed already", scenario.Steps![1].BypassReason);
        Assert.Equal(ExecutionResult.Bypassed, scenario.Result);
    }

    [Fact]
    public void Only_the_tests_files_last_attempt_is_read()
    {
        // The reporter wrote both attempts of a retried test; the scenario is its last attempt's, which bypassed nothing.
        var messages = IssueMessagesWith((1, "PASSED", null));
        var tests = TestsFile(
            new TestRunRecord { Event = "start", TestId = IssueTestId, Attempt = 1, Timestamp = DateTimeOffset.Parse("2026-09-28T14:23:20.000Z") },
            StepRecord(IssueTestId, BypassedText, "bypassed", IssueReason),
            new TestRunRecord { Event = "end", TestId = IssueTestId, Status = "failed", Timestamp = DateTimeOffset.Parse("2026-09-28T14:23:21.000Z") },
            new TestRunRecord { Event = "start", TestId = IssueTestId, Attempt = 2, Timestamp = DateTimeOffset.Parse("2026-09-28T14:23:24.837Z") },
            StepRecord(IssueTestId, BypassedText, "passed"),
            new TestRunRecord { Event = "end", TestId = IssueTestId, Status = "passed", Timestamp = DateTimeOffset.Parse("2026-09-28T14:23:24.846Z") });

        var scenario = Only(Ingest(tests, messages));

        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Passed, ExecutionResult.Passed], Statuses(scenario));
        Assert.Equal(ExecutionResult.Passed, scenario.Result);
    }

    [Fact]
    public void A_tests_file_bypass_never_overturns_a_failed_gherkin_step()
    {
        // T15.
        var messages = IssueMessagesWith((1, "FAILED", "Error: expected 3 to be 4"), (2, "SKIPPED", null));

        var scenario = Only(Ingest(Fixture("issue-105-tests.ndjson"), messages));

        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Failed, ExecutionResult.Skipped], Statuses(scenario));
        Assert.Null(scenario.Steps![1].BypassReason);
        Assert.Equal(ExecutionResult.Failed, scenario.Result);
    }

    // ---- the synthesis warnings reach the run's diagnostics -----------------------------------

    [Fact]
    public void The_synthesis_warnings_reach_the_ingest_diagnostics()
    {
        // PHASE_FROM_STEPS_PLAN.md F9: CucumberSynthesisResult.Warnings was read by nothing, so a malformed line and a
        // scenario its captures could never join were said nowhere a user looks.
        var lines = File.ReadAllLines(Fixture("issue-105-messages.ndjson"))
            .Where(l => !l.Contains("\"kronikol-test-id\"", StringComparison.Ordinal))
            .Append("{ not json");
        var messages = PathOf("warned.ndjson");
        File.WriteAllLines(messages, lines);

        var result = Ingest(null, messages);

        Assert.Contains(result.Diagnostics, d => d.Message.Contains("malformed line"));
        var join = Assert.Single(result.Diagnostics, d => d.Message.Contains("cannot be joined"));
        Assert.Contains(IssueScenario, join.Message);
    }
}
