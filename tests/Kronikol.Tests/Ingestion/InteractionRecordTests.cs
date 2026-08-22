using System.Net;
using Kronikol.Constants;
using Kronikol.Ingestion;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

[Collection("DiagramsFetcher")]
public class InteractionRecordTests
{
    [Fact]
    public void Round_trips_a_log_through_json_preserving_identity_and_pairing()
    {
        var traceId = Guid.NewGuid();
        var rrId = Guid.NewGuid();
        var ts = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        var log = new RequestResponseLog("Overview renders", "0af7651916cd43dd8448eb211c80319c", HttpMethod.Post, """{"query":"{ me }"}""",
            new Uri("http://localhost:8081/sidekick?x=1"), [("Content-Type", "application/json")], "graphql", "web",
            RequestResponseType.Request, traceId, rrId, false, null, RequestResponseMetaType.Default, DependencyCategories.AI)
        {
            Timestamp = ts, Phase = TestPhase.Action, ActivityTraceId = "0af7651916cd43dd8448eb211c80319c", ActivitySpanId = "b7ad6b7169203331",
        };

        var json = InteractionRecord.FromLog(log).ToJson();
        var back = InteractionRecord.FromJson(json).ToLog();

        Assert.Contains("\"type\":\"Request\"", json);
        Assert.Contains("\"testId\":\"0af7651916cd43dd8448eb211c80319c\"", json);
        Assert.Contains("\"dependencyCategory\":\"AI\"", json);
        Assert.DoesNotContain("\"statusCode\"", json); // nulls omitted
        Assert.Equal(log.TestId, back.TestId);
        Assert.Equal(log.TestName, back.TestName);
        Assert.Equal(traceId, back.TraceId);
        Assert.Equal(rrId, back.RequestResponseId);
        Assert.Equal(HttpMethod.Post, back.Method.Value);
        Assert.Equal(log.Uri, back.Uri);
        Assert.Equal(log.Content, back.Content);
        Assert.Equal("application/json", back.Headers.Single().Value);
        Assert.Equal(RequestResponseType.Request, back.Type);
        Assert.Equal(ts, back.Timestamp);
        Assert.Equal(TestPhase.Action, back.Phase);
        Assert.Equal(DependencyCategories.AI, back.DependencyCategory);
        Assert.Equal("b7ad6b7169203331", back.ActivitySpanId);
    }

    [Fact]
    public void Status_codes_round_trip_as_numbers_and_custom_labels()
    {
        var ok = new RequestResponseLog("T", "t", HttpMethod.Get, null, new Uri("http://a/"), [], "S", "C", RequestResponseType.Response, Guid.NewGuid(), Guid.NewGuid(), false, HttpStatusCode.Created);
        var custom = ok with { StatusCode = "Responded" };

        Assert.Equal("201", InteractionRecord.FromLog(ok).StatusCode);
        Assert.Equal(HttpStatusCode.Created, InteractionRecord.FromLog(ok).ToLog().StatusCode!.Value);
        Assert.Equal("Responded", InteractionRecord.FromLog(custom).StatusCode);
        Assert.Equal("Responded", InteractionRecord.FromLog(custom).ToLog().StatusCode!.Value);
    }

    [Fact]
    public void Custom_method_labels_survive_and_http_verbs_become_HttpMethod()
    {
        Assert.Equal(HttpMethod.Delete, InteractionRecord.ParseMethod("delete").Value);
        Assert.Equal("generate [gemma]", InteractionRecord.ParseMethod("generate [gemma]").Value);
        Assert.Equal("CALL", InteractionRecord.ParseMethod(null).Value);
    }

    [Fact]
    public void Non_guid_ids_hash_to_stable_guids_so_pairs_still_match()
    {
        var a = InteractionRecord.ToGuid("job-leader-42");
        var b = InteractionRecord.ToGuid("job-leader-42");
        Assert.Equal(a, b);
        Assert.NotEqual(a, InteractionRecord.ToGuid("job-leader-43"));
        // 32-hex W3C trace ids parse as Guids directly.
        Assert.Equal(Guid.Parse("0af76519-16cd-43dd-8448-eb211c80319c"), InteractionRecord.ToGuid("0af7651916cd43dd8448eb211c80319c"));
    }

    [Fact]
    public void Unknown_properties_are_ignored_and_relative_uris_are_tolerated()
    {
        var record = InteractionRecord.FromJson("""{"type":"Request","uri":"/api/x","serviceName":"S","callerName":"C","testId":"t","extraDiagnostic":42,"method":"GET"}""");
        var log = record.ToLog();
        Assert.Equal("/api/x", log.Uri.PathAndQuery);
        Assert.Equal(TestIdentityScope.UnknownTestName, log.TestName);
    }

    [Fact]
    public void Pair_builds_request_and_response_sharing_ids()
    {
        var (req, resp) = InteractionRecord.Pair("t1", "Test", "Query", "http://bq/query", "BigQuery", "DataInsights",
            requestContent: "SELECT 1", responseContent: "rows", statusCode: "200",
            requestTimestamp: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), responseTimestamp: new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero),
            dependencyCategory: DependencyCategories.BigQuery);

        Assert.Equal("Request", req.Type);
        Assert.Equal("Response", resp.Type);
        Assert.Equal(req.RequestResponseId, resp.RequestResponseId);
        Assert.Equal(req.TraceId, resp.TraceId);
        Assert.Null(req.StatusCode);
        Assert.Equal("200", resp.StatusCode);
        Assert.Equal("SELECT 1", req.Content);
        Assert.Equal("rows", resp.Content);
        Assert.Equal(req.ToLog().RequestResponseId, resp.ToLog().RequestResponseId);
    }

    [Fact]
    public void Writer_and_reader_round_trip_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "kronikol-ndjson-" + Guid.NewGuid().ToString("N") + ".ndjson");
        try
        {
            using (var writer = new NdjsonInteractionWriter(path))
            {
                writer.Write(InteractionRecord.Pair("t1", "Test", "GET", "http://a/1", "A", "Test", statusCode: "200"));
                writer.Log(new RequestResponseLog("Test", "t1", HttpMethod.Get, null, new Uri("http://a/2"), [], "A", "Test", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false));
                Assert.Equal(3, writer.LinesWritten);
            }

            var records = NdjsonInteractionReader.ReadFile(path);
            Assert.Equal(3, records.Count);
            Assert.All(records, r => Assert.Equal("t1", r.TestId));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Readers_can_tail_files_a_writer_still_holds_open()
    {
        // Live reporting reads the capture while the proxy tap's writer (FileShare.Read) and the test
        // fixture's appender still have the files open — the readers must not demand exclusive access.
        var dir = Directory.CreateTempSubdirectory("kronikol-tail-").FullName;
        try
        {
            var capture = Path.Combine(dir, "capture.ndjson");
            var tests = Path.Combine(dir, "tests.jsonl");
            using var writer = new NdjsonInteractionWriter(capture);
            using var testsWriter = new StreamWriter(new FileStream(tests, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };

            writer.Write(InteractionRecord.Pair("t1", "Test", "GET", "http://a/1", "A", "Test", statusCode: "200"));
            testsWriter.WriteLine("""{"event":"start","testId":"t1","testName":"Test","timestamp":"2026-01-01T00:00:00Z"}""");

            Assert.Equal(2, NdjsonInteractionReader.ReadFile(capture).Count);
            Assert.Single(NdjsonTestRunReader.ReadFile(tests));

            // And they see what was appended since, without reopening anything on the writer side.
            writer.Write(InteractionRecord.Pair("t1", "Test", "GET", "http://a/2", "A", "Test", statusCode: "200"));
            Assert.Equal(4, NdjsonInteractionReader.ReadFile(capture).Count);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void User_actions_and_markers_round_trip_and_map_to_the_right_log_entries()
    {
        var t0 = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
        var ui = InteractionRecord.UserAction("t1", "Click \"Accept trial\"", "http://localhost:4000/overview", t0, durationMs: 1500,
            detail: "Click getByRole('button', { name: 'Accept trial' })");
        var step = InteractionRecord.StepMarker("t1", "the user accepts the trial", t0.AddSeconds(1), keyword: "When");
        var assertion = InteractionRecord.AssertionMarker("t1", "the banner is visible", passed: false, t0.AddSeconds(2), message: "not visible");

        // JSON round trip keeps the kind-specific fields.
        var ui2 = InteractionRecord.FromJson(ui.ToJson());
        Assert.Equal("ui", ui2.Kind);
        Assert.True(ui2.IsUserAction);
        Assert.Equal(1500, ui2.DurationMs);
        Assert.Equal("User", ui2.CallerName);
        var assertion2 = InteractionRecord.FromJson(assertion.ToJson());
        Assert.True(assertion2.IsMarker);
        Assert.False(assertion2.Passed);
        Assert.Equal("not visible", assertion2.Message);

        // A user action is one request-type log flagged IsUserAction with a User-category caller (→ actor).
        var uiLog = Assert.Single(ui2.ToLogs());
        Assert.True(uiLog.IsUserAction);
        Assert.Equal(RequestResponseType.Request, uiLog.Type);
        Assert.Equal("Click \"Accept trial\"", uiLog.Method.Value!.ToString());
        Assert.Equal(Kronikol.Constants.DependencyCategories.User, uiLog.CallerDependencyCategory);
        Assert.Equal("ui", InteractionRecord.FromLog(uiLog).Kind);

        // Markers become the override pair carrying Kronikol's own delimiter / assertion PlantUML.
        var stepLogs = step.ToLogs().ToArray();
        Assert.Equal(2, stepLogs.Length);
        Assert.True(stepLogs[0].IsOverrideStart);
        Assert.Contains("hnote across <<stepDelimiter>> #black:<color:white>When the user accepts the trial", stepLogs[0].PlantUml);
        Assert.True(stepLogs[1].IsOverrideEnd);
        var assertionLogs = assertion.ToLogs().ToArray();
        Assert.Contains("hnote across <<assertionNote>> " + Track.FailColor, assertionLogs[0].PlantUml);
        // Keyword-less label: the note is capitalised (Reports.StepText) so the diagram reads as a sentence.
        Assert.Contains(Track.FailSymbol + " The banner is visible", assertionLogs[0].PlantUml);
        Assert.Contains("not visible", assertionLogs[0].PlantUml);
        Assert.Contains("end note", assertionLogs[0].PlantUml);
        Assert.Equal(t0.AddSeconds(2), assertionLogs[0].Timestamp);
    }

    [Fact]
    public void Reader_reports_the_offending_line_number()
    {
        using var reader = new StringReader("{\"type\":\"Request\",\"uri\":\"/\",\"serviceName\":\"S\",\"callerName\":\"C\",\"testId\":\"t\"}\n\nnot json\n");
        var ex = Assert.Throws<FormatException>(() => NdjsonInteractionReader.Read(reader, "capture.ndjson"));
        Assert.Contains("capture.ndjson:3", ex.Message);
    }

    [Fact]
    public void Composite_sink_writes_to_every_sink()
    {
        var path = Path.Combine(Path.GetTempPath(), "kronikol-composite-" + Guid.NewGuid().ToString("N") + ".ndjson");
        var testId = "composite-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new NdjsonInteractionWriter(path))
            {
                var sink = new CompositeRequestResponseSink(RequestResponseLoggerSink.Instance, file, null);
                sink.Log(new RequestResponseLog("Test", testId, HttpMethod.Get, null, new Uri("http://a/"), [], "A", "Test", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false));
            }

            Assert.Single(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId));
            Assert.Single(NdjsonInteractionReader.ReadFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
