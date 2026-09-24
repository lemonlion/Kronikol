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
        // The label the capture recorded, not an upper-cased version of it: BigQuery's operation is
        // called `Query`, and until 3.6.0 all three data writers upper-cased the whole method slot -
        // a no-op for a real verb, and the writer rewriting the tracker's own text for everything else.
        Assert.Equal("Query", interactions[2].GetProperty("method").GetString());

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
        var paired = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).Select(l => $"{l.Type} {l.Uri}").ToArray();
        Assert.Equal(["Request http://a/one", "Response http://a/one", "Request http://a/two", "Response http://a/two"], paired);

        options.ReportsFolderPath = Path.Combine(_dir, "R2");
        IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], Options = options, CallTreeOrdering = false });
        var chronological = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).Select(l => $"{l.Type} {l.Uri}").ToArray();
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
    public void A_scenario_with_only_markers_keeps_the_no_interactions_affordance_and_never_sends_an_empty_body_to_the_browser_engine()
    {
        // An API-only test: it asserted things but touched no tracked dependency. Its diagram is
        // nothing but assertion notes — which the browser hides by default, leaving an empty body.
        const string testId = "markers-only";
        var tests = WriteTests(
            new TestRunRecord { Event = "start", TestId = testId, TestName = "api › probes the mock", Timestamp = T0 },
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "the mock answers 200", Status = "passed", Timestamp = T0.AddSeconds(1) },
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 2000, Timestamp = T0.AddSeconds(2) });
        var output = Path.Combine(_dir, "R-markers-only");
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = output;

        var result = IngestPipeline.Run(new IngestRequest { TestsFile = tests, Options = options });

        Assert.True(result.Generated);
        var html = File.ReadAllText(result.TestRunReportHtml);
        // The note is in the diagram, the marker is on the page, and the browser script refuses to
        // hand plantuml.js a diagram whose body the filters emptied.
        Assert.Contains("data-no-interactions=\"true\"", html);
        Assert.Contains("function hasDrawableBody", html);
        Assert.Contains("Nothing to draw with the current filters", html);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        var diagram = json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("diagrams")[0].GetString()!;
        Assert.Contains("<<assertionNote>>", diagram);
        Assert.Contains("✓ The mock answers 200", diagram);
        // …and when the notes ARE shown (or the report is rendered server-side / with node), the
        // notes have a lifeline to span — a bare `hnote across` is a PlantUML syntax error.
        Assert.Contains(global::Kronikol.PlantUml.PlantUmlCreator.MarkerOnlyParticipant, diagram);
        Assert.Contains("_markerOnlyParticipantRx", html);
    }

    [Fact]
    public void Markers_nest_under_the_user_action_in_flight_never_inside_a_backend_call()
    {
        const string t = "markers";
        // The user clicks [0,10]; the app's call runs [1,9]; an assertion fires at 5 — mid-call.
        var click = InteractionRecord.UserAction(t, "Click \"Go\"", "http://app/", T0, durationMs: 10_000);
        var (req, resp) = InteractionRecord.Pair(t, null, "POST", "http://gql/sidekick", "graphql", "web", statusCode: "200",
            requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(9));
        var note = InteractionRecord.AssertionMarker(t, "the spinner is visible", passed: true, T0.AddSeconds(5));
        // And a step bar between two actions, owned by neither.
        var bar = InteractionRecord.StepMarker(t, "the user moves on", T0.AddSeconds(11));
        var next = InteractionRecord.UserAction(t, "Click \"Next\"", "http://app/", T0.AddSeconds(12), durationMs: 1000);

        var ordered = IngestPipeline.OrderAsCallTree([click, req, note, resp, bar, next]);

        // The note is the click's child (after the call, which is atomic), not buried between request and response.
        Assert.Equal([click, req, resp, note, bar, next], ordered);
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
        Assert.Contains("✗ The customers figure equals 42", diagram);
        Assert.Contains("Expected 42, received 41", diagram);
        Assert.DoesNotContain("the button is clicked", diagram);

        // Step list: the top-level step with its nested step and the two assertions as sub-steps.
        var steps = result.Features[0].Scenarios[0].Steps!;
        var top = Assert.Single(steps);
        Assert.Equal("When", top.Keyword);
        Assert.Equal("the user accepts the trial", top.Text);
        Assert.Equal(ExecutionResult.Passed, top.Status);
        Assert.NotNull(top.SubSteps);
        Assert.Equal(["The button is clicked", "✓ The trial banner is visible", "✗ The customers figure equals 42"], top.SubSteps!.Select(s => s.Text));
        Assert.Equal(ExecutionResult.Failed, top.SubSteps![2].Status);
        Assert.Contains("Expected 42, received 41", top.SubSteps![2].Comments!);

        // The report shows the Steps and Assertions toggles, as for in-process step/assertion tracking.
        var html = File.ReadAllText(result.TestRunReportHtml);
        Assert.Contains("data-toggle=\"steps\"", html);
        Assert.Contains("data-toggle=\"assertions\"", html);
    }

    [Fact]
    public void A_step_records_table_and_doc_string_are_drawn_inside_its_delimiter_bar()
    {
        const string testId = "p8";
        var (req, resp) = InteractionRecord.Pair(testId, null, "POST", "http://api/muffins", "api", "web", statusCode: "200",
            requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(2));
        var file = WriteCapture("t.ndjson", req, resp);
        var tests = WriteTests(
            new TestRunRecord { Event = "start", TestId = testId, TestName = "muffins are stocked", Timestamp = T0 },
            new TestRunRecord
            {
                Event = "step", TestId = testId, Text = "the following muffins exist", Keyword = "Given",
                Timestamp = T0.AddSeconds(0.5), Status = "passed",
                Table = [["name", "price"], ["Blueberry", "3.50"], ["Double Chocolate", "4.00"]],
            },
            new TestRunRecord
            {
                Event = "step", TestId = testId, Text = "the request body is", Keyword = "When",
                Timestamp = T0.AddSeconds(3), Status = "passed",
                DocString = "{ \"muffin\": \"Blueberry\" }",
            },
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 5000, Timestamp = T0.AddSeconds(5) });
        var output = Path.Combine(_dir, "R-p8");
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = output;

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], TestsFile = tests, Options = options });

        Assert.True(result.Generated);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        var diagram = json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("diagrams")[0].GetString()!;

        // The table-carrying bar takes the styled body form; the doc-string bar likewise; and the
        // diagram carries the .stepBody style that colours them.
        Assert.Contains(@"<<stepDelimiter>><<stepBody>>: Given the following muffins exist\n\n|= name |= price |\n| Blueberry | 3.50 |\n| Double Chocolate | 4.00 |\n", diagram);
        Assert.Contains(@"<<stepDelimiter>><<stepBody>>: When the request body is\n\n{ ""muffin"": ""Blueberry"" }\n", diagram);
        Assert.Contains(".stepBody {", diagram);
        Assert.Contains("FontColor white", diagram);
    }

    [Fact]
    public void Ingested_step_and_assertion_markers_are_classified_so_step_paths_and_annotations_follow()
    {
        // Plan F7: the ingest builder never set MarkerKind, so every ingested step bar and assertion note
        // was a Custom marker: exported as an annotation holding raw PlantUML, and never advancing the step
        // cursor, so no ingested run has ever carried a stepPath.
        const string testId = "f7-classified";
        var (req, resp) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:8081/sidekick", "graphql", "web",
            requestContent: "{}", responseContent: """{"data":{}}""", statusCode: "200",
            requestTimestamp: T0.AddMilliseconds(2000), responseTimestamp: T0.AddMilliseconds(2050));
        var file = WriteCapture("f7.ndjson", req, resp);
        var tests = WriteTests(
            new TestRunRecord { Event = "start", TestId = testId, TestName = "basket › starts empty", Feature = "basket.feature", Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = testId, Text = "a basket", Keyword = "Given", Status = "passed", DurationMs = 1500, Timestamp = T0.AddMilliseconds(1000) },
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "the basket is empty", Status = "passed", Timestamp = T0.AddMilliseconds(3000) },
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 6000, Timestamp = T0.AddMilliseconds(6000) });
        var output = Path.Combine(_dir, "R-f7");
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = output;

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], TestsFile = tests, Options = options, CallTreeOrdering = false });

        Assert.True(result.Generated);
        // Scoped to this test's id: the store is process-wide and the pipeline is the one that clears it.
        var kinds = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.IsDiagramMarker)
            .Select(l => l.MarkerKind)
            .ToArray();
        Assert.Equal([DiagramMarkerKind.Step, DiagramMarkerKind.Step, DiagramMarkerKind.Assertion, DiagramMarkerKind.Assertion], kinds);

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        var scenario = json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];
        Assert.Equal(0, scenario.GetProperty("annotations").GetArrayLength());
        var interactions = scenario.GetProperty("httpInteractions").EnumerateArray().ToArray();
        Assert.Equal(2, interactions.Length);
        Assert.All(interactions, i => Assert.Equal("0", i.GetProperty("stepPath").GetString()));
        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == DiagnosticKind.StepAttributionMismatch);
    }

    [Fact]
    public void A_projected_store_ingests_with_no_junk_and_a_tests_file_step_draws_no_second_bar()
    {
        // #93: an in-process store written through NdjsonInteractionWriter used to turn every marker half
        // into a request line with no sender and no receiver, which the ingest listed and drew.
        const string testId = "projected-markers";
        var logs = ProjectedStore(testId);
        var capture = Path.Combine(_dir, "projected.ndjson");
        using (var writer = new NdjsonInteractionWriter(capture))
            foreach (var log in logs)
                writer.Log(log);
        Assert.Equal(11, File.ReadAllLines(capture).Length);

        var start = new TestRunRecord { Event = "start", TestId = testId, TestName = "basket › warms the cache", Feature = "basket.feature", Timestamp = T0 };
        var end = new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 8000, Timestamp = T0.AddMilliseconds(8000) };

        // Alone: the four interactions, the two exportable annotations, one bar, nothing drawn from nothing.
        var alone = Ingest(capture, [start, end], "R-projected");
        Assert.Equal(4, alone.Interactions.Length);
        Assert.Equal(["Row: Row 3", "Custom: cache warmed"], alone.Annotations);
        Assert.DoesNotContain("actor \"\"", alone.Diagram);
        Assert.DoesNotContain(alone.Diagram.Split('\n'), l => l.StartsWith(" -[", StringComparison.Ordinal));
        Assert.DoesNotContain("CALL: /", alone.Diagram);
        Assert.Equal(1, Count(alone.Diagram, "<<stepDelimiter>>"));
        Assert.Equal(1, Count(alone.Diagram, "cache warmed"));
        Assert.Equal(1, Count(alone.Diagram, "Row 3"));
        // No tests file, so no step list: a bar with no step to open leaves the path null rather than guessing.
        Assert.All(alone.Interactions, i => Assert.Equal(JsonValueKind.Null, i.GetProperty("stepPath").ValueKind));

        // With a tests file naming the same step (and an assertion): the capture's markers are the drawing,
        // the tests file's events fill the step list and draw nothing, or the diagram grows two bars.
        var same = Ingest(capture,
        [
            start,
            new TestRunRecord { Event = "step", TestId = testId, Text = "a basket", Keyword = "Given", Status = "passed", DurationMs = 5000, Timestamp = T0.AddMilliseconds(1000) },
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "the cache is warm", Status = "passed", Timestamp = T0.AddMilliseconds(6000) },
            end,
        ], "R-projected-same");
        Assert.Equal(1, Count(same.Diagram, "<<stepDelimiter>>"));
        Assert.Equal(0, Count(same.Diagram, "<<assertionNote>>"));
        Assert.Equal(alone.Diagram, same.Diagram);
        var step = Assert.Single(same.Result.Features[0].Scenarios[0].Steps!);
        Assert.Equal("a basket", step.Text);
        Assert.Equal(["✓ The cache is warm"], step.SubSteps!.Select(s => s.Text));
        Assert.All(same.Interactions, i => Assert.Equal("0", i.GetProperty("stepPath").GetString()));
        Assert.DoesNotContain(same.Result.Diagnostics, d => d.Kind == DiagnosticKind.StepAttributionMismatch);

        // With a tests file whose step is not the bar's: one mismatch and null paths, the in-process rule.
        var other = Ingest(capture,
        [
            start,
            new TestRunRecord { Event = "step", TestId = testId, Text = "checkout", Keyword = "When", Status = "passed", DurationMs = 5000, Timestamp = T0.AddMilliseconds(1000) },
            end,
        ], "R-projected-mismatch");
        Assert.Equal(1, Count(other.Diagram, "<<stepDelimiter>>"));
        Assert.Single(other.Result.Diagnostics, d => d.Kind == DiagnosticKind.StepAttributionMismatch);
        Assert.All(other.Interactions, i => Assert.Equal(JsonValueKind.Null, i.GetProperty("stepPath").ValueKind));
    }

    [Fact]
    public void The_steps_twice_rule_reads_a_marker_kind_the_way_the_replay_does()
    {
        // A capture's step bar with its kind written by number: the replay drew it as a Step bar, but the
        // rule that keeps a tests file from drawing a second bar compared the name, so the scenario had two
        // and its calls lost their step. The bar is a little before the tests file's step, as two clocks
        // put them; at the same instant the second pair nests inside the first and only the paths show it.
        const string testId = "steps-twice-by-number";
        var bar = InteractionRecord.FromLog(Marker(testId, DiagramMarkerKind.Step, $"\n{InteractionRecord.StepDelimiterPlantUml("Given", "a basket")}\n\n", isStart: true, T0.AddMilliseconds(900)));
        var barEnd = InteractionRecord.FromLog(Marker(testId, DiagramMarkerKind.Step, null, isStart: false, T0.AddMilliseconds(901)));
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://localhost:8081/health", "web", "web", statusCode: "200",
            requestTimestamp: T0.AddMilliseconds(2000), responseTimestamp: T0.AddMilliseconds(2005));
        var capture = WriteCapture("steps-by-number.ndjson",
            bar with { MarkerKind = ((int)DiagramMarkerKind.Step).ToString() }, barEnd with { MarkerKind = ((int)DiagramMarkerKind.Step).ToString() }, req, resp);

        var ingested = Ingest(capture,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "Basket by number", Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = testId, Text = "a basket", Keyword = "Given", Status = "passed", DurationMs = 5000, Timestamp = T0.AddMilliseconds(1000) },
            new TestRunRecord { Event = "end", TestId = testId, Status = "passed", DurationMs = 6000, Timestamp = T0.AddMilliseconds(6000) },
        ], "R-steps-by-number");

        Assert.Equal(1, Count(ingested.Diagram, "<<stepDelimiter>>"));
        Assert.All(ingested.Interactions, i => Assert.Equal("0", i.GetProperty("stepPath").GetString()));
    }

    [Fact]
    public void A_projected_store_without_a_tests_file_names_each_scenario_from_its_calls_not_its_markers()
    {
        // The store's markers carry the test id as their name (DefaultTrackingDiagramOverride builds each
        // with the id twice), and a run's markers usually come first: a step bar, a test delimiter. The first
        // name among the records was the id, so the scenario was named after it.
        const string testId = "c0ffee16cd43dd8448eb211c80319c01";
        DefaultTrackingDiagramOverride.InsertPlantUml(testId, "note over web : starting");
        var emitted = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        Assert.Equal(2, emitted.Length);
        Assert.All(emitted, l => Assert.Equal(testId, l.TestName));

        var markers = emitted.Select((l, i) => InteractionRecord.FromLog(l with { Timestamp = T0.AddMilliseconds(1000 + i) }));
        var (req, resp) = InteractionRecord.Pair(testId, "Basket named by its calls", "GET", "http://localhost:8081/health", "web", "web", statusCode: "200",
            requestTimestamp: T0.AddMilliseconds(2000), responseTimestamp: T0.AddMilliseconds(2005));
        var capture = WriteCapture("named.ndjson", [.. markers, req, resp]);

        var ingested = Ingest(capture, [], "R-named");

        Assert.Equal("Basket named by its calls", Assert.Single(Assert.Single(ingested.Result.Features).Scenarios).DisplayName);
    }

    [Fact]
    public void A_phase_that_is_no_member_reaches_the_report_as_Unknown_not_as_a_number()
    {
        // The report's schema allows the member names only; the undefined value was written as "7".
        const string testId = "phase-seven";
        var (req, resp) = InteractionRecord.Pair(testId, "Phase seven", "GET", "http://localhost:8081/health", "web", "web", statusCode: "200",
            requestTimestamp: T0.AddMilliseconds(1000), responseTimestamp: T0.AddMilliseconds(1005), phase: "7");
        var capture = WriteCapture("phase-seven.ndjson", req, resp);

        var ingested = Ingest(capture, [], "R-phase-seven");

        Assert.All(ingested.Interactions, i => Assert.Equal(nameof(TestPhase.Unknown), i.GetProperty("phase").GetString()));
    }

    [Fact]
    public void Marker_records_keep_their_place_in_call_tree_order()
    {
        // No two calls of the fixture overlap, so the call tree is the timeline: every marker half stays
        // between the neighbours it was written between.
        var records = ProjectedStore("ordered").Select(InteractionRecord.FromLog).ToList();
        Assert.All(records.Where(r => r.Uri.StartsWith("http://override.com", StringComparison.Ordinal)), r => Assert.True(r.IsMarker));
        Assert.Equal(records, IngestPipeline.OrderAsCallTree(records));

        // Under a user action in flight a marker half is the action's child, after the call in flight
        // (atomic), never between that call's request and response.
        const string t = "ordered-ui";
        var click = InteractionRecord.UserAction(t, "Click \"Go\"", "http://app/", T0, durationMs: 10_000);
        var (req, resp) = InteractionRecord.Pair(t, null, "POST", "http://gql/sidekick", "graphql", "web", statusCode: "200",
            requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(9));
        var half = InteractionRecord.FromLog(Marker(t, DiagramMarkerKind.Custom, "\nnote over graphql : mid-call\n\n", isStart: true, T0.AddSeconds(5)));
        Assert.Equal([click, req, resp, half], IngestPipeline.OrderAsCallTree([click, req, half, resp]));
    }

    /// <summary>Two pairs and seven marker halves, every kind, strictly increasing tick-aligned timestamps, no overlap: what an in-process run leaves in the store.</summary>
    private static List<RequestResponseLog> ProjectedStore(string testId)
    {
        var traceA = Guid.NewGuid(); var rrA = Guid.NewGuid();
        var traceB = Guid.NewGuid(); var rrB = Guid.NewGuid();
        return
        [
            Marker(testId, DiagramMarkerKind.Step, $"\n{InteractionRecord.StepDelimiterPlantUml("Given", "a basket")}\n\n", isStart: true, T0.AddMilliseconds(1000)),
            Marker(testId, DiagramMarkerKind.Step, null, isStart: false, T0.AddMilliseconds(1001)),
            new("Probe", testId, HttpMethod.Post, "{}", new Uri("http://localhost:8081/sidekick"), [("Content-Type", "application/json")], "graphql", "web", RequestResponseType.Request, traceA, rrA, false) { Timestamp = T0.AddMilliseconds(2000), Phase = TestPhase.Setup },
            new("Probe", testId, HttpMethod.Post, """{"data":{}}""", new Uri("http://localhost:8081/sidekick"), [], "graphql", "web", RequestResponseType.Response, traceA, rrA, false, System.Net.HttpStatusCode.OK) { Timestamp = T0.AddMilliseconds(2050), DurationMs = 77, Phase = TestPhase.Setup },
            Marker(testId, DiagramMarkerKind.Row, "\nhnote across #lightyellow : Row 3\n\n", isStart: true, T0.AddMilliseconds(3000)),
            Marker(testId, DiagramMarkerKind.Row, null, isStart: false, T0.AddMilliseconds(3001)),
            new("Probe", testId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsActionStart = true, MarkerKind = DiagramMarkerKind.Phase, Timestamp = T0.AddMilliseconds(3500) },
            Marker(testId, DiagramMarkerKind.Custom, "\nnote over graphql : cache warmed\n\n", isStart: true, T0.AddMilliseconds(4000)),
            Marker(testId, DiagramMarkerKind.Custom, null, isStart: false, T0.AddMilliseconds(4001)),
            new("Probe", testId, HttpMethod.Get, null, new Uri("http://localhost:8081/health"), [], "web", "web", RequestResponseType.Request, traceB, rrB, false) { Timestamp = T0.AddMilliseconds(5000), Phase = TestPhase.Action },
            new("Probe", testId, HttpMethod.Get, """{"ok":true}""", new Uri("http://localhost:8081/health"), [], "web", "web", RequestResponseType.Response, traceB, rrB, false, System.Net.HttpStatusCode.OK) { Timestamp = T0.AddMilliseconds(5005), Phase = TestPhase.Action },
        ];
    }

    private static RequestResponseLog Marker(string testId, DiagramMarkerKind kind, string? plantUml, bool isStart, DateTimeOffset at) =>
        new("Probe", testId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = isStart, IsOverrideEnd = !isStart, MarkerKind = kind, PlantUml = plantUml, Timestamp = at,
        };

    private (IngestResult Result, JsonElement[] Interactions, string[] Annotations, string Diagram) Ingest(string capture, TestRunRecord[] testRecords, string folder)
    {
        var output = Path.Combine(_dir, folder);
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = output;
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [capture], TestRecords = testRecords, Options = options, CallTreeOrdering = false });

        Assert.True(result.Generated);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "TestRunReport.json")));
        var scenario = json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].Clone();
        return (result,
            scenario.GetProperty("httpInteractions").EnumerateArray().ToArray(),
            scenario.GetProperty("annotations").EnumerateArray().Select(a => $"{a.GetProperty("kind").GetString()}: {a.GetProperty("text").GetString()}").ToArray(),
            scenario.GetProperty("diagrams")[0].GetString()!);
    }

    private static int Count(string text, string needle)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    [Fact]
    public void Nothing_is_written_for_the_specification_files_when_both_are_turned_off()
    {
        // The command line cannot turn them off; a library caller can, and then there is nothing to say
        // about them being blank, because they do not exist.
        const string testId = "specs-off";
        var (req, resp) = InteractionRecord.Pair(testId, null, "GET", "http://a/x", "A", "Test", statusCode: "200",
            requestTimestamp: T0, responseTimestamp: T0.AddSeconds(1));
        var file = WriteCapture("specs-off.ndjson", req, resp);
        var tests = WriteTests(
            new TestRunRecord { Event = "start", TestId = testId, TestName = "off", Timestamp = T0 },
            new TestRunRecord { Event = "end", TestId = testId, Status = "failed", Error = "boom", Timestamp = T0.AddSeconds(2) });
        var output = Path.Combine(_dir, "R-specs-off");
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = output;
        options.GenerateSpecificationsReport = false;
        options.GenerateSpecificationsData = false;

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [file], TestsFile = tests, Options = options });

        Assert.True(result.Generated);
        Assert.True(File.Exists(result.TestRunReportHtml));
        Assert.False(File.Exists(Path.Combine(output, "Specifications.html")));
        Assert.False(File.Exists(Path.Combine(output, "Specifications.yml")));
    }
}
