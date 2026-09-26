using System.Net;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query flow</c>: one scenario's calls in capture order, under the steps that made them.
///
/// <para>Before 3.30.3 the step headers and annotations were printed as the records went by, before any
/// filter ran, so a filtered view and a step address printed the header of every step that made a call,
/// with nothing under it; an annotation recorded after the scenario's last call was never printed, since
/// the loop that printed annotations stopped at the last record; and <c>--count</c> was accepted and the
/// whole flow printed anyway. <c>FlowTests</c> pins the view whole, then each of those
/// (plans/FLOW_NESTING_PLAN.md §6.2 and §6.3).</para>
/// </summary>
public class FlowTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-flow").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── What the view is (pinned before the fixes, held after them) ───

    [Fact]
    public void Flow_pairs_interleaved_calls_to_one_service_by_requestResponseId()
    {
        // Two calls to one service answered in the other order: reqA, reqB, respB, respA. Proximity would
        // give each the other's status.
        var output = Run("flow", Report(), "s0");

        Assert.Matches(@"(?m)^  s0/i2\s+test → payments  POST /charge  OK\b", output);
        Assert.Matches(@"(?m)^  s0/i3\s+test → payments  POST /charge  InternalServerError\b", output);
    }

    [Fact]
    public void Flow_pairs_by_proximity_when_the_id_is_absent()
    {
        var output = Run("flow", Report(), "s3");

        Assert.Matches(@"(?m)^  s3/i0\s+test → legacy  GET /ping  OK\b", output);
    }

    [Fact]
    public void Unfiltered_flow_prints_steps_annotations_and_calls_in_capture_order()
    {
        // Held byte for byte by every change to the verb that is not meant to change this view.
        Assert.Equal(
            """
            s0  Checkout  [Passed]

              ── Row 3
            ── 0  Given a basket
              s0/i0     test → api  GET /basket  OK  20 ms
            ── 1  When the order is placed
              s0/i2     test → payments  POST /charge  OK  60 ms  b:d4d85e59 13 B
              s0/i3     test → payments  POST /charge  InternalServerError  35 ms  b:cdddc3f6 13 B
              ── stock reserved
              s0/i6     test → stock-db  QUERY /stock  OK  3 ms  b:6c3c4992 11 B
              s0/i8     test → payments  POST /capture  OK  1.24 s  b:aadffc4b 12 B
            5 calls shown · http s0/iN --keys for a payload

            """.ReplaceLineEndings("\n"),
            Run("flow", Report(), "s0"));
    }

    // ─── A header or an annotation stands above a call that is shown ───

    [Fact]
    public void A_step_address_prints_only_that_steps_header()
    {
        var output = Run("flow", Report(), "s0/1");

        Assert.Contains("── 1  When the order is placed", output);
        Assert.DoesNotContain("── 0", output);
    }

    [Fact]
    public void A_filter_that_matches_nothing_prints_no_step_header()
    {
        // s1 made two calls and both were answered OK, so --errors-only shows nothing. Its two step headers
        // read as two steps that made no calls.
        Assert.Equal(
            """
            s1  Browse the catalogue  [Passed]

              (nothing matched the filters)
            0 calls shown · http s1/iN --keys for a payload

            """.ReplaceLineEndings("\n"),
            Run("flow", Report(), "s1", "--errors-only"));
    }

    [Fact]
    public void A_step_whose_calls_are_all_filtered_out_prints_no_header()
    {
        var output = Run("flow", Report(), "s0", "--service", "payments");

        Assert.DoesNotContain("── 0", output);
        Assert.Contains("── 1  When the order is placed", output);
    }

    [Fact]
    public void An_annotation_after_the_last_call_is_printed()
    {
        // The recorder numbers an annotation by the records before it, so one recorded after the last
        // call carries the record count, an index the old loop never reached. `annotations` listed it.
        var lines = Lines(Run("flow", Report(), "s2"));

        var call = lines.FindIndex(l => l.StartsWith("  s2/i0 ", StringComparison.Ordinal));
        Assert.True(call >= 0, string.Join("\n", lines));
        Assert.Equal("  ── after the last call", lines[call + 1]);
    }

    [Fact]
    public void An_annotation_whose_call_is_filtered_out_prints_above_the_next_shown_call()
    {
        // "Row 3" was recorded before s0/i0, which --service payments drops; it stands above s0/i2, the next
        // call shown, and above that call's step header, as an annotation always has.
        var lines = Lines(Run("flow", Report(), "s0", "--service", "payments"));

        var row = lines.IndexOf("  ── Row 3");
        Assert.True(row >= 0, string.Join("\n", lines));
        Assert.Equal("── 1  When the order is placed", lines[row + 1]);
        Assert.StartsWith("  s0/i2 ", lines[row + 2], StringComparison.Ordinal);

        var reserved = lines.IndexOf("  ── stock reserved");
        Assert.True(reserved > row, string.Join("\n", lines));
        Assert.StartsWith("  s0/i8 ", lines[reserved + 1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_view_that_shows_no_call_prints_no_annotation()
    {
        var output = Run("flow", Report(), "s0", "--service", "no-such-service");

        Assert.DoesNotContain("──", output);
        Assert.Contains("  (nothing matched the filters)", output);
    }

    // ─── Found beside them ─────────────────────────────────────

    [Theory]
    [InlineData("5", "s0")]
    [InlineData("3", "s0", "--service", "payments")]
    [InlineData("4", "s0/1")]
    [InlineData("1", "s0", "--errors-only")]
    [InlineData("0", "s1", "--errors-only")]
    [InlineData("0", "s4")]
    public void Count_prints_how_many_calls_the_flow_shows(string expected, params string[] args)
    {
        // `--count` was declared for flow and never read: the whole flow was printed where one number was
        // documented.
        Assert.Equal(expected + "\n", Run("flow", Report(), [.. args, "--count"]));
    }

    [Fact]
    public void Count_agrees_with_the_footer()
    {
        var footer = Lines(Run("flow", Report(), "s0", "--service", "payments")).Last(l => l.Length > 0);

        Assert.StartsWith(Run("flow", Report(), "s0", "--service", "payments", "--count").Trim() + " calls shown", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scenario_that_made_no_calls_says_so_rather_than_blaming_a_filter()
    {
        // No filter was given, so "nothing matched the filters" named a cause that was not there.
        var output = Run("flow", Report(), "s4");

        Assert.Contains("  (no tracked calls in this scenario)", output);
        Assert.DoesNotContain("filters", output);
        Assert.DoesNotContain("──", output);
    }

    [Fact]
    public void A_filter_on_a_scenario_that_made_no_calls_says_it_made_none()
    {
        // The filter did not empty the view; the scenario had nothing to filter.
        Assert.Contains("  (no tracked calls in this scenario)", Run("flow", Report(), "s4", "--errors-only"));
    }

    [Fact]
    public void A_file_that_cannot_carry_calls_does_not_say_the_scenario_made_none()
    {
        // A mergeable file written before 3.1.0 carries no httpInteractions by construction; its empty
        // list is not a scenario that made no calls.
        var output = Run("flow", OldMergeableReport(), "s0");

        Assert.DoesNotContain("no tracked calls in this scenario", output);
        Assert.Contains("before 3.1.0", output);
    }

    // ─── Harness ───────────────────────────────────────────────

    private string Run(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    private static List<string> Lines(string output) => [.. output.Split('\n')];

    // ─── Fixtures ──────────────────────────────────────────────

    private string? _report;

    /// <summary>
    /// s0: two steps, a Row annotation before the first call, an interleaved pair to one service, a custom
    /// annotation mid-step and a failed call. s1: two steps, every call answered OK. s2: one call and an
    /// annotation after it. s3: a pair with no requestResponseId. s4: steps and no calls.
    /// </summary>
    private string Report()
    {
        if (_report is not null)
            return _report;

        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    Scenario("t0", "Checkout", ("Given", "a basket"), ("When", "the order is placed")),
                    Scenario("t1", "Browse the catalogue", ("Given", "a catalogue"), ("When", "browsing")),
                    Scenario("t2", "Print a receipt", ("When", "printing")),
                    Scenario("t3", "Ping the legacy service", ("When", "pinging")),
                    Scenario("t4", "Idle", ("Given", "nothing to do"), ("Then", "nothing is called"))
                ]
            }
        ];

        var at = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var logs = new List<RequestResponseLog>();

        logs.Add(Marker("t0", DiagramMarkerKind.Row, "hnote across #lightyellow : Row 3"));
        logs.Add(StepMarker("t0", "a basket"));
        logs.AddRange(Call("t0", "api", "GET", "http://api/basket", null, HttpStatusCode.OK, at, at.AddMilliseconds(20)));
        logs.Add(StepMarker("t0", "the order is placed"));
        var chargeA = Call("t0", "payments", "POST", "http://payments/charge", "{\"attempt\":1}", HttpStatusCode.OK, at.AddMilliseconds(100), at.AddMilliseconds(160));
        var chargeB = Call("t0", "payments", "POST", "http://payments/charge", "{\"attempt\":2}", HttpStatusCode.InternalServerError, at.AddMilliseconds(105), at.AddMilliseconds(140));
        logs.AddRange([chargeA[0], chargeB[0], chargeB[1], chargeA[1]]);
        logs.Add(Marker("t0", DiagramMarkerKind.Custom, "note across : stock reserved"));
        logs.AddRange(Call("t0", "stock-db", "QUERY", "http://stock-db/stock", "{\"sku\":\"a\"}", HttpStatusCode.OK, at.AddMilliseconds(200), at.AddMilliseconds(203)));
        logs.AddRange(Call("t0", "payments", "POST", "http://payments/capture", "{\"charge\":1}", HttpStatusCode.OK, at.AddMilliseconds(300), at.AddMilliseconds(1_540)));

        logs.Add(StepMarker("t1", "a catalogue"));
        logs.AddRange(Call("t1", "api", "GET", "http://api/catalogue", null, HttpStatusCode.OK, at.AddSeconds(2), at.AddSeconds(2).AddMilliseconds(10)));
        logs.Add(StepMarker("t1", "browsing"));
        logs.AddRange(Call("t1", "search", "GET", "http://search/muffins", null, HttpStatusCode.OK, at.AddSeconds(3), at.AddSeconds(3).AddMilliseconds(10)));

        logs.Add(StepMarker("t2", "printing"));
        logs.AddRange(Call("t2", "printer", "POST", "http://printer/receipt", "print please", HttpStatusCode.OK, at.AddSeconds(4), at.AddSeconds(4).AddMilliseconds(10)));
        logs.Add(Marker("t2", DiagramMarkerKind.Custom, "note across : after the last call"));

        logs.Add(StepMarker("t3", "pinging"));
        logs.AddRange(Call("t3", "legacy", "GET", "http://legacy/ping", null, HttpStatusCode.OK, at.AddSeconds(5), at.AddSeconds(5).AddMilliseconds(10), pairId: Guid.Empty));

        logs.Add(StepMarker("t4", "nothing to do"));
        logs.Add(StepMarker("t4", "nothing is called"));

        var written = ReportGenerator.GenerateTestRunReportData(
            features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Flow_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, diagrams: null, logs.ToArray());

        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return _report = path;
    }

    /// <summary>A shard as a runner wrote it before 3.1.0: the mergeable format, which then carried no calls.</summary>
    private string OldMergeableReport()
    {
        var path = Path.Combine(_directory, "OldMergeable.json");
        File.WriteAllText(path, """
            {
              "kronikolVersion": "3.0.80",
              "mergeableFormatVersion": 1,
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [ { "name": "Orders", "labels": [], "scenarios": [
                { "id": "t0", "stableId": "aaaabbbbccccdddd", "name": "Checkout", "result": "Passed", "durationSeconds": 1.0,
                  "labels": [], "categories": [],
                  "steps": [ { "keyword": "Given", "text": "a basket", "status": "Passed", "durationSeconds": 0.1, "subSteps": [], "attachments": [] } ] }
              ] } ]
            }
            """);
        return path;
    }

    private static Scenario Scenario(string id, string name, params (string Keyword, string Text)[] steps) => new()
    {
        Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
        Steps = [.. steps.Select(s => new ScenarioStep { Keyword = s.Keyword, Text = s.Text, Status = ExecutionResult.Passed })]
    };

    private static RequestResponseLog StepMarker(string testId, string stepText) =>
        Marker(testId, DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>" + stepText);

    private static RequestResponseLog Marker(string testId, DiagramMarkerKind kind, string plantUml) =>
        new(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsOverrideStart = true, PlantUml = plantUml, MarkerKind = kind };

    /// <summary>A request and its response, the request first; the caller is always <c>test</c>.</summary>
    private static RequestResponseLog[] Call(string testId, string service, OneOf<HttpMethod, string> method, string uri, string? body,
        OneOf<HttpStatusCode, string> status, DateTimeOffset sent, DateTimeOffset answered, Guid? pairId = null)
    {
        var id = pairId ?? Guid.NewGuid();
        var traceId = Guid.NewGuid();
        return
        [
            new RequestResponseLog(testId, testId, method, body, new Uri(uri), [], service, "test",
                RequestResponseType.Request, traceId, id, false) { Timestamp = sent },
            new RequestResponseLog(testId, testId, method, "{\"ok\":true}", new Uri(uri), [], service, "test",
                RequestResponseType.Response, traceId, id, false, status) { Timestamp = answered }
        ];
    }

}
