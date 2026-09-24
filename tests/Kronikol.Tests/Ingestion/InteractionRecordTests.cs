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
            DurationMs = 123.4,
        };

        var json = InteractionRecord.FromLog(log).ToJson();
        var back = InteractionRecord.FromJson(json).ToLog();

        Assert.Contains("\"type\":\"Request\"", json);
        Assert.Contains("\"testId\":\"0af7651916cd43dd8448eb211c80319c\"", json);
        Assert.Contains("\"dependencyCategory\":\"AI\"", json);
        // #94: a measured duration is written and read back, so the report believes it over the timestamp delta.
        Assert.Contains("\"durationMs\":123.4", json);
        Assert.Equal(123.4, back.DurationMs);
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
        // Classified at the source (plan F7): step attribution, the annotation export and the Setup
        // partition all switch on the kind, and an unclassified marker is a Custom one to every one of them.
        Assert.All(stepLogs, l => Assert.Equal(DiagramMarkerKind.Step, l.MarkerKind));
        var assertionLogs = assertion.ToLogs().ToArray();
        Assert.All(assertionLogs, l => Assert.Equal(DiagramMarkerKind.Assertion, l.MarkerKind));
        Assert.Contains("hnote across <<assertionNote>> " + Track.FailColor, assertionLogs[0].PlantUml);
        // Keyword-less label: the note is capitalised (Reports.StepText) so the diagram reads as a sentence.
        Assert.Contains(Track.FailSymbol + " The banner is visible", assertionLogs[0].PlantUml);
        Assert.Contains("not visible", assertionLogs[0].PlantUml);
        Assert.Contains("end note", assertionLogs[0].PlantUml);
        Assert.Equal(t0.AddSeconds(2), assertionLogs[0].Timestamp);
    }

    [Theory]
    [InlineData(DiagramMarkerKind.Custom)]
    [InlineData(DiagramMarkerKind.Row)]
    [InlineData(DiagramMarkerKind.Step)]
    [InlineData(DiagramMarkerKind.Assertion)]
    public void Every_override_half_round_trips_through_the_writer_as_one_marker_record(DiagramMarkerKind kind)
    {
        // #93: the writer used to turn every marker log into the same junk request line ("CALL" to
        // http://override.com/ between two participants named ""), which an ingest then drew. A marker is
        // now one kind: marker record per override half, restored as the half it stands for.
        var t0 = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
        var start = MarkerLog(kind, isStart: true, "\nnote over graphql : cache warmed\n\n", t0);
        var end = MarkerLog(kind, isStart: false, null, t0.AddMilliseconds(1));

        foreach (var original in new[] { start, end })
        {
            var json = InteractionRecord.FromLog(original).ToJson();
            Assert.Contains("\"kind\":\"marker\"", json);
            Assert.Equal(1, json.Split("\"kind\"").Length - 1);
            Assert.Contains($"\"markerKind\":\"{kind}\"", json);
            // A control record has neither: what the store held ("") is restored, not written.
            Assert.DoesNotContain("\"method\"", json);
            Assert.DoesNotContain("\"content\"", json);
            Assert.Equal(original.IsOverrideEnd, json.Contains("\"markerEnd\":true"));

            var record = InteractionRecord.FromJson(json);
            Assert.True(record.IsMarker);
            var back = Assert.Single(record.ToLogs());
            Assert.Equal(original.IsOverrideStart, back.IsOverrideStart);
            Assert.Equal(original.IsOverrideEnd, back.IsOverrideEnd);
            Assert.False(back.IsActionStart);
            Assert.Equal(kind, back.MarkerKind);
            Assert.Equal(original.PlantUml, back.PlantUml); // byte for byte, buffering newlines included
            Assert.Equal(original.Timestamp, back.Timestamp);
            Assert.Equal(original.TestId, back.TestId);
            Assert.Equal(original.TestName, back.TestName);
            Assert.Equal(original.TraceId, back.TraceId);
            Assert.Equal(original.RequestResponseId, back.RequestResponseId);
            Assert.Equal("", back.Method.Value);
            Assert.Equal("", back.Content);
            Assert.Equal(new Uri("http://override.com"), back.Uri);
            Assert.Equal(RequestResponseType.Request, back.Type);
        }
    }

    [Fact]
    public void The_phase_boundary_and_a_wrapping_pair_round_trip_as_marker_records()
    {
        var t0 = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

        // StartAction: IsActionStart with MarkerKind.Phase and no fragment.
        var phase = new RequestResponseLog("Probe", "t1", "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsActionStart = true, MarkerKind = DiagramMarkerKind.Phase, Timestamp = t0 };
        var phaseJson = InteractionRecord.FromLog(phase).ToJson();
        Assert.Contains("\"kind\":\"marker\"", phaseJson);
        Assert.Contains("\"markerKind\":\"Phase\"", phaseJson);
        Assert.DoesNotContain("\"plantUml\"", phaseJson);
        Assert.DoesNotContain("\"markerEnd\"", phaseJson);
        var phaseBack = Assert.Single(InteractionRecord.FromJson(phaseJson).ToLogs());
        Assert.True(phaseBack.IsActionStart);
        Assert.False(phaseBack.IsOverrideStart);
        Assert.False(phaseBack.IsOverrideEnd);
        Assert.Equal(DiagramMarkerKind.Phase, phaseBack.MarkerKind);
        Assert.Null(phaseBack.PlantUml);

        // StartOverride("group x") … EndOverride("end"): both halves carry a fragment, and both keep it.
        var open = MarkerLog(DiagramMarkerKind.Custom, isStart: true, "\ngroup retries\n\n", t0.AddMilliseconds(1));
        var close = MarkerLog(DiagramMarkerKind.Custom, isStart: false, "\nend\n\n", t0.AddMilliseconds(2));
        var openBack = Assert.Single(InteractionRecord.FromJson(InteractionRecord.FromLog(open).ToJson()).ToLogs());
        var closeBack = Assert.Single(InteractionRecord.FromJson(InteractionRecord.FromLog(close).ToJson()).ToLogs());
        Assert.True(openBack.IsOverrideStart);
        Assert.Equal("\ngroup retries\n\n", openBack.PlantUml);
        Assert.True(closeBack.IsOverrideEnd);
        Assert.Equal("\nend\n\n", closeBack.PlantUml);

        // An unknown or absent markerKind is the enum's own unclassified value, never a guess.
        var unclassified = InteractionRecord.FromJson("""{"type":"Request","uri":"http://override.com/","serviceName":"","callerName":"","testId":"t1","kind":"marker","markerKind":"Banner","plantUml":"note over a : x"}""");
        Assert.Equal(DiagramMarkerKind.Custom, Assert.Single(unclassified.ToLogs()).MarkerKind);
    }

    [Theory]
    [InlineData("7")] // a number that is no member: Enum.TryParse accepts it as (DiagramMarkerKind)7
    [InlineData("Step, Row")] // a list: Enum.TryParse ORs it into Row
    [InlineData("Banner")]
    [InlineData("")]
    [InlineData(null)]
    public void A_marker_kind_that_is_not_one_member_reads_as_Custom(string? markerKind)
    {
        var record = new InteractionRecord
        {
            Type = "Request", Uri = "http://override.com/", ServiceName = "", CallerName = "", TestId = "t1",
            Kind = InteractionRecord.Kinds.Marker, MarkerKind = markerKind, PlantUml = "\nnote over a : x\n\n",
        };

        var log = Assert.Single(record.ToLogs());

        Assert.Equal(DiagramMarkerKind.Custom, log.MarkerKind);
        Assert.True(log.IsOverrideStart);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("Setup, Action")] // ORed into 3, which is no member
    public void A_phase_or_meta_type_that_is_not_one_member_reads_as_the_default(string value)
    {
        // Parsed with Enum.TryParse, an undefined value reached the log, and the report wrote it where its
        // own schema allows only the member names.
        var record = InteractionRecord.FromJson($$"""{"type":"Request","uri":"http://a/x","serviceName":"S","callerName":"C","testId":"t1","phase":"{{value}}","metaType":"{{value}}"}""");

        var log = record.ToLog();

        Assert.Equal(TestPhase.Unknown, log.Phase);
        Assert.Equal(RequestResponseMetaType.Default, log.MetaType);
    }

    [Fact]
    public void A_member_named_in_any_case_or_by_its_number_still_reads_as_that_member()
    {
        // What the reader accepted before 3.29.1 and still does: only values that are no member changed.
        var record = InteractionRecord.FromJson("""{"type":"Request","uri":"http://a/x","serviceName":"S","callerName":"C","testId":"t1","phase":"setup","metaType":"1"}""");
        Assert.Equal(TestPhase.Setup, record.ToLog().Phase);
        Assert.Equal(RequestResponseMetaType.Event, record.ToLog().MetaType);

        var marker = record with { Kind = InteractionRecord.Kinds.Marker, MarkerKind = "row" };
        Assert.Equal(DiagramMarkerKind.Row, Assert.Single(marker.ToLogs()).MarkerKind);
    }

    [Fact]
    public void A_marker_record_is_a_marker_to_every_ingestion_site()
    {
        var record = InteractionRecord.FromJson("""{"type":"Request","uri":"http://override.com/","serviceName":"","callerName":"","testId":"t1","kind":"marker","markerKind":"Custom","markerEnd":true}""");
        Assert.True(record.IsMarker);
        Assert.False(record.IsUserAction);
        Assert.Equal(InteractionRecord.Kinds.Marker, record.Kind);
    }

    private static RequestResponseLog MarkerLog(DiagramMarkerKind kind, bool isStart, string? plantUml, DateTimeOffset at) =>
        new("Probe", "t1", "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request,
            Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = isStart,
            IsOverrideEnd = !isStart,
            MarkerKind = kind,
            PlantUml = plantUml,
            Timestamp = at,
        };

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
