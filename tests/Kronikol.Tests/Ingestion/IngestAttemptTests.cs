using System.Text.Json;
using Kronikol.History;
using Kronikol.Ingestion;
using Kronikol.Ingestion.Cucumber;
using Kronikol.Reports;
using Kronikol.Tests.Ingestion.Cucumber;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// <c>plans/INGEST_FIDELITY_PLAN.md</c> S3 (T7, T8 and the Cucumber lane's check from §9): a retried test through the
/// pipeline is its last attempt, with the earlier attempts' calls left out and counted, as the Cucumber lane has always
/// built a retried scenario. P3 measured the tests lane before it: one passed scenario carrying the failed attempt's
/// error, with both attempts' steps and calls in one diagram.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestAttemptTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-attempts-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    public IngestAttemptTests()
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_retried_test_is_reported_as_its_last_attempt_with_only_that_attempts_calls(bool numbered)
    {
        var name = numbered ? "numbered" : "unnumbered";
        var (result, scenario) = Ingest(name, Retried("p3", numbered), Calls("p3"));

        Assert.Equal("Passed", scenario.GetProperty("result").GetString());
        Assert.Equal(2, scenario.GetProperty("attempt").GetInt32());
        Assert.Equal(JsonValueKind.Null, scenario.GetProperty("errorMessage").ValueKind);
        // A keyword-less label reads as a sentence in the report (--no-capitalise leaves it).
        Assert.Equal(["Attempt two"], scenario.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("text").GetString()));
        Assert.Contains("retry 1", scenario.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
        var calls = scenario.GetProperty("httpInteractions").EnumerateArray().ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, c => Assert.StartsWith(T0.AddMilliseconds(4000).ToString("yyyy-MM-ddTHH:mm:ss"), c.GetProperty("timestamp").GetString()));
        Assert.Equal("# No failures", File.ReadLines(Path.Combine(_dir, name, "Failures.md")).First());

        Assert.Single(result.Diagnostics, d => d.Message.Contains("2 interaction record(s) of earlier attempts", StringComparison.Ordinal));
        // Without numbers the second start is read as the retry it most likely is, and the report says it did so.
        Assert.Equal(numbered ? 0 : 1, result.Diagnostics.Count(d => d.Message.Contains("more than one start without an attempt number", StringComparison.Ordinal)));
    }

    [Fact]
    public void History_and_CTRF_read_the_retry_from_the_ingested_scenario()
    {
        // T8: nothing downstream changes; the attempt reaches them through Scenario.Attempt, which ingest never set.
        var (result, _) = Ingest("downstream", Retried("p3", numbered: true), Calls("p3"));
        var features = result.Features;

        using var ctrf = JsonDocument.Parse(CtrfReportGenerator.Generate(features, result.Start, result.End, null, "test"));
        var test = ctrf.RootElement.GetProperty("results").GetProperty("tests")[0];
        Assert.Equal(1, test.GetProperty("retries").GetInt32());
        Assert.True(test.GetProperty("flaky").GetBoolean());

        var (roster, run) = HistoryRunBuilder.Build(features, [], null, null, T0.AddMinutes(1), new HistoryBuildOptions(), "test:1:1");
        var ledger = HistoryLedgerReader.Parse(HistoryJson.HeaderLine("test") + "\n", 0).Ledger!;
        var verdict = Assert.Single(HistoryAnalyzer.Analyse(ledger, roster, run, new HistoryAnalysisOptions()).Scenarios);
        Assert.Equal(HistoryVerdictKind.Flaky, verdict.Primary);
        Assert.Contains("passed on retry 2 in this run", verdict.Evidence);
    }

    [Fact]
    public void A_retried_cucumber_scenario_draws_only_its_last_attempts_calls()
    {
        // The golden fixture's flaky scenario carries one kronikol-test-id across its two attempts, so the first
        // attempt's calls joined the scenario the last attempt built. The reporter's own starts, one per attempt, are
        // not attempts of their own: the messages say how many there were.
        var cucumber = CucumberFixtures.Build();
        var flaky = cucumber.Features.SelectMany(f => f.Scenarios).Single(s => s.DisplayName == CucumberFixtures.FlakyScenario);
        var first = DateTimeOffset.FromUnixTimeMilliseconds(1787393375319);
        var second = DateTimeOffset.FromUnixTimeMilliseconds(1787393375732);
        var capture = WriteCapture("flaky.ndjson", [.. Call(flaky.Id, "/first", first.AddMilliseconds(1)), .. Call(flaky.Id, "/second", second.AddMilliseconds(5))]);
        var tests = WriteTests("flaky-tests.ndjson",
            new TestRunRecord { Event = "start", TestId = flaky.Id, TestName = "reporter name", Timestamp = first.AddMilliseconds(-1) },
            new TestRunRecord { Event = "end", TestId = flaky.Id, Status = "failed", Error = "the first attempt failed", Timestamp = first.AddMilliseconds(6) },
            new TestRunRecord { Event = "start", TestId = flaky.Id, TestName = "reporter name", Timestamp = second.AddMilliseconds(-1) },
            new TestRunRecord { Event = "end", TestId = flaky.Id, Status = "passed", Timestamp = second.AddMilliseconds(23) });

        var output = Path.Combine(_dir, "cucumber-flaky");
        var result = IngestPipeline.Run(new IngestRequest
        {
            CucumberMessagesFiles = [CucumberFixtures.MessagesPath],
            InteractionFiles = [capture],
            TestsFile = tests,
            Options = Options(output),
        });

        var scenario = ScenarioById(output, flaky.Id);
        Assert.Equal("Passed", scenario.GetProperty("result").GetString());
        Assert.Equal(2, scenario.GetProperty("attempt").GetInt32());
        Assert.Equal(["http://localhost:5000/second"], scenario.GetProperty("httpInteractions").EnumerateArray()
            .Where(i => i.GetProperty("type").GetString() == "Request").Select(i => i.GetProperty("uri").GetString()));
        Assert.Equal(JsonValueKind.Null, scenario.GetProperty("errorMessage").ValueKind);
        Assert.Single(result.Diagnostics, d => d.Message.Contains("2 interaction record(s) of earlier attempts", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("more than one start without an attempt number", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cucumber_attempt_that_minted_its_own_test_id_leaves_no_scenario_behind()
    {
        // A fixture that mints a kronikol-test-id per attempt: the first attempt's id is no scenario the messages own,
        // so its reporter records and its calls made a scenario of their own, failed, and the run read as failed.
        var messages = Path.Combine(_dir, "minted.ndjson");
        File.WriteAllText(messages, MintedPerAttemptStream());
        var start = DateTimeOffset.FromUnixTimeSeconds(1787393374);
        var capture = WriteCapture("minted-calls.ndjson",
            [.. Call("first-attempt-id", "/first", start.AddMilliseconds(500)), .. Call("second-attempt-id", "/second", start.AddMilliseconds(2500))]);
        var tests = WriteTests("minted-tests.ndjson",
            new TestRunRecord { Event = "start", TestId = "first-attempt-id", TestName = "Eine Karte belasten", Timestamp = start },
            new TestRunRecord { Event = "end", TestId = "first-attempt-id", Status = "failed", Error = "the first attempt failed", Timestamp = start.AddMilliseconds(1500) },
            new TestRunRecord { Event = "start", TestId = "second-attempt-id", TestName = "Eine Karte belasten", Timestamp = start.AddMilliseconds(2000) },
            new TestRunRecord { Event = "end", TestId = "second-attempt-id", Status = "passed", Timestamp = start.AddMilliseconds(3500) });

        var output = Path.Combine(_dir, "cucumber-minted");
        var result = IngestPipeline.Run(new IngestRequest
        {
            CucumberMessagesFiles = [messages],
            InteractionFiles = [capture],
            TestsFile = tests,
            Options = Options(output),
        });

        var scenario = Assert.Single(result.Features.SelectMany(f => f.Scenarios));
        Assert.Equal("second-attempt-id", scenario.Id);
        Assert.Equal(ExecutionResult.Passed, scenario.Result);
        Assert.Equal(2, scenario.Attempt);
        Assert.Contains("retry 1", scenario.Labels!);
        Assert.Equal("# No failures", File.ReadLines(Path.Combine(output, "Failures.md")).First());
        Assert.Single(result.Diagnostics, d => d.Message.Contains("2 interaction record(s) of earlier attempts", StringComparison.Ordinal));
    }

    [Fact]
    public void Source_paths_are_made_relative_to_the_source_root_and_a_path_outside_it_is_named()
    {
        // T9's pipeline half: an absolute path under the root loses the build machine's prefix; one outside it is kept as
        // written, and a diagnostic says how many were, since a runner's paths do not belong in a downloadable report.
        var root = Path.Combine(_dir, "checkout");
        Directory.CreateDirectory(root);
        var under = Path.Combine(root, "src", "charge.test.ts");
        var outside = Path.Combine(_dir, "elsewhere", "refund.test.ts");
        TestRunRecord[] tests =
        [
            new() { Event = "start", TestId = "under", TestName = "charges a card", SourceFile = under, SourceLine = 3, Timestamp = T0 },
            new() { Event = "step", TestId = "under", Text = "a saved card", SourceFile = under, SourceLine = 4, Timestamp = T0.AddMilliseconds(10) },
            new() { Event = "end", TestId = "under", Status = "passed", Timestamp = T0.AddSeconds(1) },
            new() { Event = "start", TestId = "outside", TestName = "refunds a card", SourceFile = outside, SourceLine = 9, Timestamp = T0.AddSeconds(2) },
            new() { Event = "end", TestId = "outside", Status = "passed", Timestamp = T0.AddSeconds(3) },
        ];
        var output = Path.Combine(_dir, "sources");

        var result = IngestPipeline.Run(new IngestRequest { TestRecords = tests, Options = Options(output), SourceRoot = root });

        var scenarios = result.Features.SelectMany(f => f.Scenarios).ToDictionary(s => s.Id);
        Assert.Equal("src/charge.test.ts", scenarios["under"].SourceFile);
        Assert.Equal("charge.test.ts", scenarios["under"].Steps![0].SourceFile);
        Assert.Equal(outside.Replace('\\', '/'), scenarios["outside"].SourceFile);
        Assert.Single(result.Diagnostics, d => d.Message.Contains("1 source path(s)", StringComparison.Ordinal)
                                              && d.Message.Contains("outside the source root", StringComparison.Ordinal));
    }

    private static TestRunRecord[] Retried(string id, bool numbered) =>
    [
        new() { Event = "start", TestId = id, TestName = "charges a card", Feature = "charge.test.ts", Attempt = numbered ? 1 : null, Timestamp = T0 },
        new() { Event = "step", TestId = id, Text = "attempt one", Timestamp = T0.AddMilliseconds(500) },
        new() { Event = "end", TestId = id, Status = "failed", Error = "expected SETTLED, received PENDING", DurationMs = 2000, Timestamp = T0.AddMilliseconds(2000) },
        new() { Event = "start", TestId = id, TestName = "charges a card", Feature = "charge.test.ts", Attempt = numbered ? 2 : null, Timestamp = T0.AddMilliseconds(3000) },
        new() { Event = "step", TestId = id, Text = "attempt two", Timestamp = T0.AddMilliseconds(3500) },
        new() { Event = "end", TestId = id, Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(5000) },
    ];

    private static InteractionRecord[] Calls(string id) =>
        [.. Call(id, "/charges", T0.AddMilliseconds(1000)), .. Call(id, "/charges", T0.AddMilliseconds(4000))];

    private static InteractionRecord[] Call(string testId, string path, DateTimeOffset at)
    {
        var (request, response) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:5000" + path, "psp", "superpay-graphql",
            requestContent: "{}", responseContent: "{}", statusCode: "200", requestTimestamp: at, responseTimestamp: at.AddMilliseconds(2));
        return [request, response];
    }

    private string WriteCapture(string name, InteractionRecord[] records)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, records.Select(r => r.ToJson()));
        return path;
    }

    private string WriteTests(string name, params TestRunRecord[] records)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, records.Select(r => r.ToJson()));
        return path;
    }

    private static ReportConfigurationOptions Options(string output)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = output;
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        return options;
    }

    private (IngestResult Result, JsonElement Scenario) Ingest(string name, TestRunRecord[] tests, InteractionRecord[] calls)
    {
        var output = Path.Combine(_dir, name);
        var result = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [WriteCapture(name + ".ndjson", calls)],
            TestRecords = tests,
            Options = Options(output),
        });
        Assert.True(result.Generated);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        var scenario = Assert.Single(json.RootElement.GetProperty("features").EnumerateArray().SelectMany(f => f.GetProperty("scenarios").EnumerateArray()));
        return (result, scenario.Clone());
    }

    private static JsonElement ScenarioById(string output, string id)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        return json.RootElement.GetProperty("features").EnumerateArray().SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Single(s => s.GetProperty("id").GetString() == id).Clone();
    }

    /// <summary>One scenario, two attempts, each with a kronikol-test-id of its own: the first fails, the second passes.</summary>
    private static string MintedPerAttemptStream() =>
        """
        {"gherkinDocument":{"uri":"features/zahlung.feature","feature":{"language":"de","keyword":"Funktionalität","name":"Zahlungen","children":[{"scenario":{"id":"sc-1","keyword":"Szenario","name":"Eine Karte belasten","steps":[{"id":"st-1","keyword":"Wenn ","keywordType":"Action","text":"die Karte belastet wird"}]}}]}}}
        {"pickle":{"id":"pk-1","uri":"features/zahlung.feature","astNodeIds":["sc-1"],"name":"Eine Karte belasten","language":"de","steps":[{"id":"ps-1","text":"die Karte belastet wird","type":"Action","astNodeIds":["st-1"]}]}}
        {"testCase":{"id":"tc-1","pickleId":"pk-1","testSteps":[{"id":"ts-1","pickleStepId":"ps-1"}]}}
        {"testCaseStarted":{"id":"att-1","attempt":0,"testCaseId":"tc-1","timestamp":{"seconds":1787393374,"nanos":0}}}
        {"attachment":{"testCaseStartedId":"att-1","mediaType":"text/plain","fileName":"kronikol-test-id","body":"first-attempt-id","contentEncoding":"IDENTITY"}}
        {"testStepStarted":{"testCaseStartedId":"att-1","testStepId":"ts-1","timestamp":{"seconds":1787393374,"nanos":100000000}}}
        {"testStepFinished":{"testCaseStartedId":"att-1","testStepId":"ts-1","testStepResult":{"duration":{"seconds":1,"nanos":0},"status":"FAILED","message":"the first attempt failed"},"timestamp":{"seconds":1787393375,"nanos":100000000}}}
        {"testCaseFinished":{"testCaseStartedId":"att-1","timestamp":{"seconds":1787393375,"nanos":500000000},"willBeRetried":true}}
        {"testCaseStarted":{"id":"att-2","attempt":1,"testCaseId":"tc-1","timestamp":{"seconds":1787393376,"nanos":0}}}
        {"attachment":{"testCaseStartedId":"att-2","mediaType":"text/plain","fileName":"kronikol-test-id","body":"second-attempt-id","contentEncoding":"IDENTITY"}}
        {"testStepStarted":{"testCaseStartedId":"att-2","testStepId":"ts-1","timestamp":{"seconds":1787393376,"nanos":100000000}}}
        {"testStepFinished":{"testCaseStartedId":"att-2","testStepId":"ts-1","testStepResult":{"duration":{"seconds":1,"nanos":0},"status":"PASSED"},"timestamp":{"seconds":1787393377,"nanos":100000000}}}
        {"testCaseFinished":{"testCaseStartedId":"att-2","timestamp":{"seconds":1787393377,"nanos":500000000}}}
        """;
}
