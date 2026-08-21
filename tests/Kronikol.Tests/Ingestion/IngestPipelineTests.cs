using System.Text.Json;
using Kronikol.Constants;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

[Collection("DiagramsFetcher")]
public class IngestPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-ingest-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    public IngestPipelineTests()
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

    private string WriteCapture(string name, params InteractionRecord[] records)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, records.Select(r => r.ToJson()));
        return path;
    }

    private string WriteTests(params TestRunRecord[] records)
    {
        var path = Path.Combine(_dir, "tests.ndjson");
        File.WriteAllLines(path, records.Select(r => r.ToJson()));
        return path;
    }

    [Fact]
    public void Replays_captures_attributes_by_test_id_and_writes_a_full_report_to_the_output_dir()
    {
        const string testId = "0af7651916cd43dd8448eb211c80319c";
        var (req1, resp1) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:8081/sidekick", "graphql", "web",
            requestContent: """{"query":"query Overview { overview }"}""", responseContent: """{"data":{}}""", statusCode: "200",
            requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(1.2));
        var (req2, resp2) = InteractionRecord.Pair(testId, null, "Query", "http://bq/projects/p/queries", "bigquery", "data-insights",
            requestContent: "SELECT 1", responseContent: "rows", statusCode: "200",
            requestTimestamp: T0.AddSeconds(2), responseTimestamp: T0.AddSeconds(2.5), dependencyCategory: DependencyCategories.BigQuery);
        // Written out of order on purpose: the pipeline must order by timestamp.
        var webFile = WriteCapture("web.ndjson", resp1, req1);
        var bqFile = WriteCapture("bq.ndjson", req2, resp2);
        var testsFile = WriteTests(
            new TestRunRecord { Event = "start", TestId = testId, TestName = "overview › renders", Feature = "overview.spec.ts", Timestamp = T0 },
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 3000, Timestamp = T0.AddSeconds(3) });

        var output = Path.Combine(_dir, "Reports");
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = output;

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [webFile, bqFile], TestsFile = testsFile, Options = options });

        Assert.True(result.Generated);
        Assert.Equal(4, result.InteractionCount);
        Assert.Equal(1, result.ScenarioCount);
        Assert.Equal(Path.GetFullPath(output), result.ReportsDirectory);
        Assert.True(File.Exists(result.TestRunReportHtml));

        var html = File.ReadAllText(result.TestRunReportHtml);
        Assert.Contains("overview › renders", html);
        Assert.Contains("overview.spec.ts", html);
        Assert.DoesNotContain("data-no-interactions", html); // the calls were attributed

        // The data file carries the interactions under the scenario, in timestamp order, with the name from the tests file.
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        var scenario = json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];
        Assert.Equal(testId, scenario.GetProperty("id").GetString());
        var interactions = scenario.GetProperty("httpInteractions").EnumerateArray().ToArray();
        Assert.Equal(4, interactions.Length);
        Assert.Equal("Request", interactions[0].GetProperty("type").GetString());
        Assert.Equal("http://localhost:8081/sidekick", interactions[0].GetProperty("uri").GetString());
        Assert.Equal("Response", interactions[1].GetProperty("type").GetString());
        Assert.Equal("QUERY", interactions[2].GetProperty("method").GetString());

        // Stored logs carry the normalised name.
        Assert.All(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId), l => Assert.Equal("overview › renders", l.TestName));
    }

    [Fact]
    public void Without_a_tests_file_scenarios_are_synthesised_from_the_captures()
    {
        var (req, resp) = InteractionRecord.Pair("t-only", "Named by capturer", "GET", "http://a/x", "A", "Test", statusCode: "200", requestTimestamp: T0);
        var file = WriteCapture("only.ndjson", req, resp);
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "R2");

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], Options = options, DefaultFeatureName = "Captured" });

        Assert.True(result.Generated);
        var feature = Assert.Single(result.Features);
        Assert.Equal("Captured", feature.DisplayName);
        Assert.Equal("Named by capturer", feature.Scenarios.Single().DisplayName);
        Assert.Contains("Named by capturer", File.ReadAllText(result.TestRunReportHtml));
    }

    [Fact]
    public void Empty_input_skips_generation_unless_allowed()
    {
        var file = Path.Combine(_dir, "empty.ndjson");
        File.WriteAllText(file, "\n");
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "R3");

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], Options = options });

        Assert.False(result.Generated);
        Assert.False(File.Exists(result.TestRunReportHtml));
    }

    [Fact]
    public void Redaction_applies_during_replay_so_a_raw_capture_does_not_leak_into_the_report()
    {
        var (req, resp) = InteractionRecord.Pair("t-secret", "Secret", "GET", "http://a/x", "A", "Test", statusCode: "200",
            requestHeaders: [new InteractionHeader("Authorization", "Bearer leaked-in-capture")], requestTimestamp: T0);
        var file = WriteCapture("secret.ndjson", req, resp);
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "R4");
        RequestResponseLogger.Redaction = CaptureRedaction.Secrets();

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], Options = options });

        var json = File.ReadAllText(Path.Combine(result.ReportsDirectory, "TestRunReport.json"));
        Assert.DoesNotContain("leaked-in-capture", json);
        Assert.Contains("[REDACTED]", json);
    }

    [Fact]
    public void Missing_files_throw_clearly()
    {
        Assert.Throws<FileNotFoundException>(() => IngestPipeline.Run(new IngestRequest { InteractionFiles = [Path.Combine(_dir, "nope.ndjson")] }));
        Assert.Throws<FileNotFoundException>(() => IngestPipeline.Run(new IngestRequest { TestsFile = Path.Combine(_dir, "nope-tests.ndjson") }));
    }

    [Fact]
    public void Default_options_suit_external_capture()
    {
        var options = IngestPipeline.DefaultOptions();
        Assert.False(options.InternalFlowTracking);
        Assert.True(options.GenerateComponentDiagram);
        Assert.True(options.CollapseConsecutiveIdenticalCalls);
        Assert.Equal(PlantUmlRendering.BrowserJs, options.PlantUmlRendering);
    }

    [Fact]
    public void Responses_follow_their_requests_by_default_so_concurrent_calls_stay_paired()
    {
        const string testId = "pairing";
        // Two overlapping calls: A starts first and finishes last. Chronologically that is A B B A.
        var (reqA, respA) = InteractionRecord.Pair(testId, "T", "GET", "http://a/one", "A", "Test", statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddSeconds(3));
        var (reqB, respB) = InteractionRecord.Pair(testId, "T", "GET", "http://a/two", "A", "Test", statusCode: "200",
            requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(2));
        var file = WriteCapture("c.ndjson", reqA, reqB, respB, respA);
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "R1");

        IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], Options = options });
        var paired = RequestResponseLogger.RequestAndResponseLogs.Select(l => $"{l.Type} {l.Uri}").ToArray();
        Assert.Equal(["Request http://a/one", "Response http://a/one", "Request http://a/two", "Response http://a/two"], paired);

        options.ReportsFolderPath = Path.Combine(_dir, "R2");
        IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], Options = options, CallTreeOrdering = false });
        var chronological = RequestResponseLogger.RequestAndResponseLogs.Select(l => $"{l.Type} {l.Uri}").ToArray();
        Assert.Equal(["Request http://a/one", "Request http://a/two", "Response http://a/two", "Response http://a/one"], chronological);

        // Orphans and id-less records keep their place.
        var orphan = respB with { RequestResponseId = "never-requested", Timestamp = T0.AddSeconds(-1) };
        var reordered = IngestPipeline.OrderAsCallTree([orphan, reqA, reqB, respB, respA]);
        Assert.Equal([orphan, reqA, respA, reqB, respB], reordered);
    }

    [Fact]
    public void Calls_a_service_made_while_handling_a_request_nest_inside_it()
    {
        const string t = "tree";
        // web→graphql [0,10] handles the page query; while doing so graphql→data-insights [1,8], which
        // in turn polls bigquery twice [2,3] and [4,5]; then graphql makes a second, sibling call [8.5,9].
        // A concurrent web→graphql call from the browser [0.5,6] is a sibling of the first (its caller is
        // web, not graphql), not a child — even though its interval is contained.
        var (pReq, pResp) = InteractionRecord.Pair(t, null, "POST", "http://gql/sidekick", "graphql", "web", statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddSeconds(10));
        var (sReq, sResp) = InteractionRecord.Pair(t, null, "POST", "http://gql/sidekick?links", "graphql", "web", statusCode: "200",
            requestTimestamp: T0.AddSeconds(0.5), responseTimestamp: T0.AddSeconds(6));
        var (cReq, cResp) = InteractionRecord.Pair(t, null, "POST", "http://di/insights", "data-insights", "graphql", statusCode: "200",
            requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(8));
        var (g1Req, g1Resp) = InteractionRecord.Pair(t, null, "GET", "http://bq/poll", "bigquery", "data-insights", statusCode: "200",
            requestTimestamp: T0.AddSeconds(2), responseTimestamp: T0.AddSeconds(3));
        var (g2Req, g2Resp) = InteractionRecord.Pair(t, null, "GET", "http://bq/poll", "bigquery", "data-insights", statusCode: "200",
            requestTimestamp: T0.AddSeconds(4), responseTimestamp: T0.AddSeconds(5));
        var (c2Req, c2Resp) = InteractionRecord.Pair(t, null, "GET", "http://di/dates", "data-insights", "graphql", statusCode: "200",
            requestTimestamp: T0.AddSeconds(8.5), responseTimestamp: T0.AddSeconds(9));
        // Another test's records interleave in time but never nest into this tree.
        var (oReq, oResp) = InteractionRecord.Pair("other", null, "GET", "http://di/other", "data-insights", "graphql", statusCode: "200",
            requestTimestamp: T0.AddSeconds(2.5), responseTimestamp: T0.AddSeconds(2.6));

        var chronological = new[] { pReq, sReq, cReq, g1Req, oReq, oResp, g1Resp, g2Req, g2Resp, sResp, cResp, c2Req, c2Resp, pResp };
        var tree = IngestPipeline.OrderAsCallTree(chronological);

        Assert.Equal(
            [pReq, cReq, g1Req, g1Resp, g2Req, g2Resp, cResp, c2Req, c2Resp, pResp, sReq, sResp, oReq, oResp],
            tree);
    }

    [Fact]
    public void Unknown_test_ids_can_be_folded_into_one_scenario()
    {
        const string known = "known-test";
        var (r1, s1) = InteractionRecord.Pair(known, null, "GET", "http://a/k", "A", "Test", statusCode: "200", requestTimestamp: T0, responseTimestamp: T0.AddSeconds(1));
        var (r2, s2) = InteractionRecord.Pair("warmup-1", null, "GET", "http://a/w1", "A", "Test", statusCode: "200", requestTimestamp: T0.AddSeconds(2), responseTimestamp: T0.AddSeconds(3));
        var (r3, s3) = InteractionRecord.Pair("warmup-2", null, "GET", "http://a/w2", "A", "Test", statusCode: "200", requestTimestamp: T0.AddSeconds(4), responseTimestamp: T0.AddSeconds(5));
        var file = WriteCapture("c.ndjson", r1, s1, r2, s2, r3, s3);
        var tests = WriteTests(
            new TestRunRecord { Event = "start", TestId = known, TestName = "known › test", Timestamp = T0 },
            new TestRunRecord { Event = "end", TestId = known, Status = "passed", Timestamp = T0.AddSeconds(1) });
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "R1");

        // Default: every unknown test id is its own scenario.
        var separate = IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], TestsFile = tests, Options = options });
        Assert.Equal(3, separate.ScenarioCount);

        options.ReportsFolderPath = Path.Combine(_dir, "R2");
        var folded = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [file], TestsFile = tests, Options = options,
            FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test", "outside"),
            // Applies to tests that started but never ended — never to the fold scenario, which is not a test.
            ResultWhenUnknown = ExecutionResult.Failed,
        });
        Assert.Equal(2, folded.ScenarioCount);
        var outside = folded.Features.SelectMany(f => f.Scenarios).Single(s => s.Id == "outside");
        Assert.Equal("Traffic outside any test", outside.DisplayName);
        Assert.Equal(ExecutionResult.Passed, outside.Result);
        Assert.Equal(4, RequestResponseLogger.RequestAndResponseLogs.Count(l => l.TestId == "outside"));
        Assert.Equal(2, RequestResponseLogger.RequestAndResponseLogs.Count(l => l.TestId == known));

        // No tests records at all: everything is outside any test.
        options.ReportsFolderPath = Path.Combine(_dir, "R3");
        var all = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [file], Options = options,
            FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test", "outside"),
        });
        Assert.Equal(1, all.ScenarioCount);
        Assert.Equal("outside", all.Features.Single().Scenarios.Single().Id);
    }

    [Fact]
    public void User_actions_steps_and_assertions_render_like_the_in_process_extensions()
    {
        const string testId = "p7";
        // The user opens the page (owning the next 5 s), the app calls graphql → data-insights meanwhile,
        // then a top-level step, a click that triggers one more call, a passing and a failing assertion.
        var open = InteractionRecord.UserAction(testId, "Open /intelligence-pro/overview", "http://localhost:4000/intelligence-pro/overview",
            T0, durationMs: 5000, detail: "Navigate to \"/intelligence-pro/overview\"");
        var (gqlReq, gqlResp) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:8081/sidekick", "graphql", "web", statusCode: "200",
            requestContent: """{"query":"query AppStartup { app }"}""", requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(3));
        var (diReq, diResp) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:9091/api/insights", "data-insights", "graphql", statusCode: "200",
            requestTimestamp: T0.AddSeconds(1.5), responseTimestamp: T0.AddSeconds(2.5));
        var click = InteractionRecord.UserAction(testId, "Click \"Accept trial\"", "http://localhost:4000/intelligence-pro/overview",
            T0.AddSeconds(6), durationMs: 4000, detail: "Click getByRole('button', { name: 'Accept trial' })");
        var (trialReq, trialResp) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:8081/sidekick", "graphql", "web", statusCode: "200",
            requestContent: """{"query":"mutation AcceptIntelligenceTrial { ok }"}""", requestTimestamp: T0.AddSeconds(6.5), responseTimestamp: T0.AddSeconds(7));
        var file = WriteCapture("c.ndjson", open, gqlReq, gqlResp, diReq, diResp, click, trialReq, trialResp);
        var tests = WriteTests(
            new TestRunRecord { Event = "start", TestId = testId, TestName = "overview › accepts the trial", Feature = "overview.spec.ts", Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = testId, Text = "the user accepts the trial", Keyword = "When", Timestamp = T0.AddSeconds(5.5), DurationMs = 4500, Status = "passed" },
            new TestRunRecord { Event = "step", TestId = testId, Text = "the button is clicked", Level = 1, Timestamp = T0.AddSeconds(5.9), Status = "passed" },
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "the trial banner is visible", Status = "passed", Timestamp = T0.AddSeconds(8) },
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "the customers figure equals 42", Status = "failed", Error = "Expected 42, received 41", Timestamp = T0.AddSeconds(9) },
            new TestRunRecord { Event = "end", TestId = testId, Status = "failed", DurationMs = 10000, Error = "Expected 42, received 41", Timestamp = T0.AddSeconds(10) });
        var output = Path.Combine(_dir, "R-p7");
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = output;

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], TestsFile = tests, Options = options });

        Assert.True(result.Generated);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        var scenario = json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];
        var diagram = scenario.GetProperty("diagrams")[0].GetString()!;
        var lines = diagram.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        // The user is the actor; web is an ordinary participant.
        Assert.Contains(lines, l => l.StartsWith("actor \"User\""));
        Assert.DoesNotContain(lines, l => l.StartsWith("actor \"web\""));

        // Arrows in call-tree order: Open → (graphql → data-insights) → step bar → Click → mutation → ✓ → ✗.
        // Requests render as `a -[#col]> b`, responses as `b -[#col]-> a`; markers as hnote lines.
        var arrows = lines.Where(l => l.Contains("]> ") || l.Contains("]-> ") || l.Contains("hnote")).ToArray();
        var open0 = Array.FindIndex(arrows, l => l.Contains("user -") && l.Contains("Open /intelligence-pro/overview"));
        var gql = Array.FindIndex(arrows, l => l.Contains("web -") && l.Contains("(query AppStartup)"));
        var di = Array.FindIndex(arrows, l => l.Contains("graphql -") && l.Contains("dataInsights") && l.Contains("/api/insights"));
        var gqlBack = Array.FindIndex(arrows, l => l.StartsWith("graphql -") && l.Contains("-> web: OK"));
        var bar = Array.FindIndex(arrows, l => l.Contains("<<stepDelimiter>>") && l.Contains("When the user accepts the trial"));
        var click0 = Array.FindIndex(arrows, l => l.Contains("user -") && l.Contains("Click \"Accept trial\""));
        var trial = Array.FindIndex(arrows, l => l.Contains("(mutation AcceptIntelligenceTrial)"));
        var pass = Array.FindIndex(arrows, l => l.Contains("<<assertionNote>>") && l.Contains(Track.PassColor));
        var fail = Array.FindIndex(arrows, l => l.Contains("<<assertionNote>>") && l.Contains(Track.FailColor));
        Assert.True(open0 >= 0 && gql > open0 && di > gql && gqlBack > di && bar > gqlBack && click0 > bar && trial > click0 && pass > trial && fail > pass,
            "unexpected order:\n" + string.Join("\n", arrows));
        // A user action has no response arrow.
        Assert.DoesNotContain(lines, l => l.StartsWith("web -") && l.Contains("-> user"));
        // The failing assertion carries its message; the delimiter is the sub-step-free top-level step only.
        Assert.Contains("✗ the customers figure equals 42", diagram);
        Assert.Contains("Expected 42, received 41", diagram);
        Assert.DoesNotContain("the button is clicked", diagram);

        // Step list: the top-level step with its nested step and the two assertions as sub-steps.
        var steps = result.Features[0].Scenarios[0].Steps!;
        var top = Assert.Single(steps);
        Assert.Equal("When", top.Keyword);
        Assert.Equal("the user accepts the trial", top.Text);
        Assert.Equal(ExecutionResult.Passed, top.Status);
        Assert.NotNull(top.SubSteps);
        Assert.Equal(["the button is clicked", "✓ the trial banner is visible", "✗ the customers figure equals 42"], top.SubSteps!.Select(s => s.Text));
        Assert.Equal(ExecutionResult.Failed, top.SubSteps![2].Status);
        Assert.Contains("Expected 42, received 41", top.SubSteps![2].Comments!);

        // The report shows the Steps and Assertions toggles, as for in-process step/assertion tracking.
        var html = File.ReadAllText(result.TestRunReportHtml);
        Assert.Contains("data-toggle=\"steps\"", html);
        Assert.Contains("data-toggle=\"assertions\"", html);
    }
}
