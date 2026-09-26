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

    // ─── Which call ran inside which (plan §6.5) ───────────────

    [Fact]
    public void A_nested_flow_is_pinned_whole()
    {
        // The API's calls ran inside the test's POST, the bank's inside the charge, the stock database's inside
        // the stock call although the charge's lines stand between them, the delivery inside the POST on its
        // trace, and the audit call inside the POST from the next step.
        Assert.Equal(
            """
            s0  Place an order  [Passed]

            ── 0  When the order is placed
              s0/i0     test → api  POST /orders  Created  100 ms  b:6c3c4992 11 B
                s0/i1     api → stock  GET /items/a  OK  40 ms
                s0/i2     api → payments  POST /charge  BadGateway  33 ms  b:2d2bc192 11 B
                  s0/i3     payments → bank  POST /debit  ServiceUnavailable  25 ms
                  s0/i4     stock → stock-db  QUERY /items  OK  2 ms  inside s0/i1
                s0/i9     broker → api  CONSUME /order-placed  Ack  1 ms  b:357f5667 11 B
            ── 1  Then it is audited
                s0/i11    api → audit  POST /entries  Created  5 ms  inside s0/i0
              s0/i14    test → api  GET /orders/1  OK  10 ms
            8 calls shown · http s0/iN --keys for a payload · indented calls ran inside the call above them

            """.ReplaceLineEndings("\n"),
            Run("flow", NestedReport(), "s0"));
    }

    [Fact]
    public void A_nested_call_is_indented_two_spaces_under_its_parent()
    {
        var lines = Lines(Run("flow", NestedReport(), "s0"));

        Assert.Contains(lines, l => l.StartsWith("  s0/i0 ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("    s0/i2 ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("      s0/i3 ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_legend_is_printed_only_when_a_line_is_indented()
    {
        const string legend = "indented calls ran inside the call above them";

        Assert.Contains(legend, Run("flow", NestedReport(), "s0"));
        Assert.DoesNotContain(legend, Run("flow", Report(), "s0"));

        // One line, not indented, naming the call it ran inside: nothing here needs the legend.
        var named = Run("flow", NestedReport(), "s0", "--service", "stock-db");
        Assert.Contains("inside s0/i1", named);
        Assert.DoesNotContain(legend, named);
    }

    [Fact]
    public void A_line_whose_parent_is_filtered_out_names_it()
    {
        // Which request each query belonged to is what a filtered view cannot show by indentation.
        var lines = Lines(Run("flow", NestedReport(), "s0", "--service", "stock-db"));

        Assert.Contains("  s0/i4     stock → stock-db  QUERY /items  OK  2 ms  inside s0/i1", lines);
    }

    [Fact]
    public void Errors_only_shows_a_failure_with_the_failure_inside_it()
    {
        // The 502 came from the 503 inside it; its own parent, which succeeded, is filtered out and named.
        var lines = Lines(Run("flow", NestedReport(), "s0", "--errors-only"));

        var charge = lines.IndexOf("  s0/i2     api → payments  POST /charge  BadGateway  33 ms  b:2d2bc192 11 B  inside s0/i0");
        Assert.True(charge >= 0, string.Join("\n", lines));
        Assert.Equal("    s0/i3     payments → bank  POST /debit  ServiceUnavailable  25 ms", lines[charge + 1]);
    }

    [Fact]
    public void Every_inside_reference_is_an_address_http_takes()
    {
        var report = NestedReport();
        string[][] views =
        [
            ["s0"], ["s0", "--service", "stock"], ["s0", "--service", "stock-db"], ["s0", "--service", "bank"],
            ["s0", "--errors-only"], ["s0/1"], ["s1"], ["s2"]
        ];

        var references = views
            .SelectMany(view => System.Text.RegularExpressions.Regex.Matches(Run("flow", report, view), @"inside (s\d+/i\d+)$",
                System.Text.RegularExpressions.RegexOptions.Multiline))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(references);
        foreach (var reference in references)
        {
            var output = Run("http", report, reference);
            Assert.Contains(reference, output);
        }
    }

    [Fact]
    public void Interleaved_branches_name_their_parent()
    {
        // The line above s0/i4 one level up is the charge, which the database query did not run inside.
        var lines = Lines(Run("flow", NestedReport(), "s0"));

        Assert.Contains(lines, l => l.StartsWith("      s0/i4 ", StringComparison.Ordinal) && l.EndsWith("  inside s0/i1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_child_recorded_in_a_later_step_names_its_parent()
    {
        var lines = Lines(Run("flow", NestedReport(), "s0"));

        var header = lines.IndexOf("── 1  Then it is audited");
        Assert.True(header >= 0, string.Join("\n", lines));
        Assert.Equal("    s0/i11    api → audit  POST /entries  Created  5 ms  inside s0/i0", lines[header + 1]);
    }

    [Fact]
    public void A_line_below_a_shallower_line_that_is_not_its_parent_names_it()
    {
        // Indentation alone points at the nearest line above with less of it, the test's second call, which the
        // query did not run inside; the line one level up, further above, is its parent.
        var lines = Lines(Run("flow", NestedReport(), "s2"));

        Assert.Contains("  s2/i2     test → other  GET /y  OK", lines);
        Assert.Contains("      s2/i3     svc → db  QUERY /z  OK  inside s2/i1", lines);
    }

    [Fact]
    public void A_step_address_names_the_call_its_first_line_ran_inside()
    {
        Assert.Contains("  s0/i11    api → audit  POST /entries  Created  5 ms  inside s0/i0", Lines(Run("flow", NestedReport(), "s0/1")));
    }

    [Fact]
    public void No_flow_line_ends_in_whitespace()
    {
        // An empty status or duration left the separators behind it at the end of the line.
        string[][] views =
        [
            ["s0"], ["s1"], ["s2"], ["s3"], ["s4"], ["s0", "--service", "payments"], ["s1", "--errors-only"]
        ];
        string[][] nestedViews = [["s0"], ["s1"], ["s2"], ["s0", "--errors-only"], ["s0", "--service", "stock-db"]];
        var outputs = views.Select(view => Run("flow", Report(), view))
            .Concat(nestedViews.Select(view => Run("flow", NestedReport(), view)))
            .Append(Run("flow", NoIdsReport(), "s0"));

        foreach (var output in outputs)
            foreach (var line in Lines(output))
                Assert.True(line == line.TrimEnd(), $"ends in whitespace: [{line}]");
    }

    [Fact]
    public void A_line_is_built_from_its_non_empty_fields()
    {
        // No status before a duration, and no duration before a body, left a double gap in the line.
        var lines = Lines(Run("flow", NestedReport(), "s1"));

        Assert.Contains("  s1/i5     test → cache  GET /key  3 ms", lines);
        Assert.Contains("  s1/i7     test → cosmos  CREATE /orders  Created  b:b1595f81 12 B", lines);
    }

    [Fact]
    public void A_report_without_requestResponseIds_prints_flat()
    {
        // With no pairing id nobody knows when a call ended, so no call is ever a parent, whatever the names
        // and the trace say.
        var output = Run("flow", NoIdsReport(), "s0");

        Assert.Contains(Lines(output), l => l.StartsWith("  s0/i0     test → api  POST /orders", StringComparison.Ordinal));
        Assert.Contains(Lines(output), l => l.StartsWith("  s0/i1     api → db  QUERY /items", StringComparison.Ordinal));
        Assert.DoesNotContain("inside", output);
        Assert.DoesNotContain("indented", output);
    }

    [Theory]
    [InlineData("8", "s0")]
    [InlineData("1", "s0", "--service", "stock-db")]
    [InlineData("2", "s0", "--errors-only")]
    [InlineData("7", "s1")]
    public void Count_is_unchanged_by_nesting(string expected, params string[] args) =>
        Assert.Equal(expected + "\n", Run("flow", NestedReport(), [.. args, "--count"]));

    // ─── A request never answered (plan Q1) ────────────────────

    [Fact]
    public void A_request_never_answered_says_no_response()
    {
        // It printed nothing where the status goes, as the answered calls with no status do.
        var lines = Lines(Run("flow", NestedReport(), "s1"));

        Assert.Contains("  s1/i0     test → api  GET /asyncapi  no response", lines);
    }

    [Fact]
    public void A_request_never_answered_holds_no_calls()
    {
        // The service's query after it is at the top level: a call never answered was never known to be
        // waiting, so nothing is placed inside it.
        Assert.Contains("  s1/i1     api → db  QUERY /specs  OK  4 ms", Lines(Run("flow", NestedReport(), "s1")));
    }

    [Fact]
    public void A_call_answered_without_a_status_is_not_said_to_have_had_no_response()
    {
        Assert.Contains("  s1/i5     test → cache  GET /key  3 ms", Lines(Run("flow", NestedReport(), "s1")));
    }

    [Fact]
    public void A_user_action_is_not_said_to_have_had_no_response()
    {
        // A click is never answered: a response is not what it waits for.
        Assert.Contains("  s1/i9     User → web  Click web", Lines(Run("flow", NestedReport(), "s1")));
    }

    [Fact]
    public void A_request_without_a_pairing_id_is_not_said_to_have_had_no_response()
    {
        // Without an id its response is looked for among the records beside it, so not finding one says
        // nothing about whether it was answered.
        Assert.Contains("  s1/i10    test → legacy  GET /ping", Lines(Run("flow", NestedReport(), "s1")));
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

    private string? _nested;

    /// <summary>
    /// s0: the test's POST, inside it a stock call and a charge answered in another order, the bank inside the
    /// charge (502 from a 503), the stock database inside the stock call, a delivery on the POST's trace, and
    /// an audit call made inside the POST after the next step began. s1: a request never answered, a call
    /// answered without a status, a body with no duration, a user action and a request with no pairing id.
    /// s2: the test's two calls at once, and a database query two levels inside the first.
    /// </summary>
    private string NestedReport()
    {
        if (_nested is not null)
            return _nested;

        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    Scenario("n0", "Place an order", ("When", "the order is placed"), ("Then", "it is audited")),
                    Scenario("n1", "Ask the async API", ("When", "the async API is asked")),
                    Scenario("n2", "Call two services at once", ("When", "both are called"))
                ]
            }
        ];

        var at = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset Ms(int ms) => at.AddMilliseconds(ms);
        var trace = Guid.NewGuid();

        var order = Pair("n0", "test", "api", "POST", "http://api/orders", "{\"sku\":\"a\"}", HttpStatusCode.Created, Ms(0), Ms(100), trace);
        var stock = Pair("n0", "api", "stock", "GET", "http://stock/items/a", null, HttpStatusCode.OK, Ms(10), Ms(50));
        var charge = Pair("n0", "api", "payments", "POST", "http://payments/charge", "{\"total\":5}", HttpStatusCode.BadGateway, Ms(12), Ms(45));
        var debit = Pair("n0", "payments", "bank", "POST", "http://bank/debit", null, HttpStatusCode.ServiceUnavailable, Ms(15), Ms(40));
        var items = Pair("n0", "stock", "stock-db", "QUERY", "http://stock-db/items", null, HttpStatusCode.OK, Ms(16), Ms(18));
        var placed = Pair("n0", "broker", "api", "CONSUME", "http://broker/order-placed", "{\"order\":1}", "Ack", Ms(60), Ms(61), trace);
        var audit = Pair("n0", "api", "audit", "POST", "http://audit/entries", null, HttpStatusCode.Created, Ms(70), Ms(75));
        var read = Pair("n0", "test", "api", "GET", "http://api/orders/1", null, HttpStatusCode.OK, Ms(110), Ms(120));

        var asked = Pair("n1", "test", "api", "GET", "http://api/asyncapi", null, HttpStatusCode.OK, Ms(1_000), null);
        var specs = Pair("n1", "api", "db", "QUERY", "http://db/specs", null, HttpStatusCode.OK, Ms(1_010), Ms(1_014));
        var again = Pair("n1", "test", "api", "GET", "http://api/asyncapi", null, HttpStatusCode.OK, Ms(1_100), Ms(1_106));
        var key = Pair("n1", "test", "cache", "GET", "http://cache/key", null, null, Ms(1_200), Ms(1_203));
        var created = Pair("n1", "test", "cosmos", "CREATE", "http://cosmos/orders", "{\"id\":\"o-1\"}", HttpStatusCode.Created, null, null);
        var click = new RequestResponseLog("n1", "n1", "Click", null, new Uri("http://web/"), [], "web", "User",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsUserAction = true };
        var ping = Pair("n1", "test", "legacy", "GET", "http://legacy/ping", null, HttpStatusCode.OK, null, null, pairId: Guid.Empty);

        var first = Pair("n2", "test", "api", "GET", "http://api/a", null, HttpStatusCode.OK, null, null);
        var behind = Pair("n2", "api", "svc", "GET", "http://svc/b", null, HttpStatusCode.OK, null, null);
        var second = Pair("n2", "test", "other", "GET", "http://other/y", null, HttpStatusCode.OK, null, null);
        var query = Pair("n2", "svc", "db", "QUERY", "http://db/z", null, HttpStatusCode.OK, null, null);

        RequestResponseLog[] logs =
        [
            StepMarker("n0", "the order is placed"),
            order[0], stock[0], charge[0], debit[0], items[0], items[1], debit[1], charge[1], stock[1], placed[0], placed[1],
            StepMarker("n0", "it is audited"),
            audit[0], audit[1], order[1], read[0], read[1],

            StepMarker("n1", "the async API is asked"),
            asked[0], specs[0], specs[1], again[0], again[1], key[0], key[1], created[0], created[1], click, ping[0],

            StepMarker("n2", "both are called"),
            first[0], behind[0], second[0], query[0], query[1], second[1], behind[1], first[1]
        ];

        var written = ReportGenerator.GenerateTestRunReportData(
            features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Nested_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, diagrams: null, logs);

        var path = Path.Combine(_directory, "Nested.json");
        File.Move(written, path, overwrite: true);
        return _nested = path;
    }

    /// <summary>
    /// A report as Kronikol wrote it before the pairing id: two calls, the second made by the service
    /// handling the first, on one trace. Written literally, as the current generator cannot produce it.
    /// </summary>
    private string NoIdsReport()
    {
        var path = Path.Combine(_directory, "NoIds.json");
        File.WriteAllText(path, """
            {
              "kronikolVersion": "3.0.44",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [ { "name": "Orders", "labels": [], "scenarios": [
                { "id": "old-1", "stableId": "aaaabbbbccccdddd", "name": "Checkout", "result": "Passed", "durationSeconds": 1.0,
                  "labels": [], "categories": [],
                  "steps": [ { "keyword": "When", "text": "the order is placed", "status": "Passed", "durationSeconds": 0.1, "subSteps": [], "attachments": [] } ],
                  "backgroundSteps": [], "attachments": [],
                  "httpInteractions": [
                    { "type": "Request", "method": "POST", "uri": "http://api/orders", "serviceName": "api", "callerName": "test", "content": null, "headers": [], "statusCode": null, "traceId": "00000000-0000-0000-0000-000000000001" },
                    { "type": "Request", "method": "QUERY", "uri": "http://db/items", "serviceName": "db", "callerName": "api", "content": null, "headers": [], "statusCode": null, "traceId": "00000000-0000-0000-0000-000000000001" },
                    { "type": "Response", "method": "QUERY", "uri": "http://db/items", "serviceName": "db", "callerName": "api", "content": null, "headers": [], "statusCode": "OK", "traceId": "00000000-0000-0000-0000-000000000001" },
                    { "type": "Response", "method": "POST", "uri": "http://api/orders", "serviceName": "api", "callerName": "test", "content": null, "headers": [], "statusCode": "Created", "traceId": "00000000-0000-0000-0000-000000000001" }
                  ] }
              ] } ]
            }
            """);
        return path;
    }

    /// <summary>
    /// A request and its response between any two parties, on a trace of its own unless given one. A call
    /// never answered is its first half alone; a null status is a response that carried none.
    /// </summary>
    private static RequestResponseLog[] Pair(string testId, string caller, string service, OneOf<HttpMethod, string> method, string uri, string? body,
        OneOf<HttpStatusCode, string>? status, DateTimeOffset? sent, DateTimeOffset? answered, Guid? traceId = null, Guid? pairId = null)
    {
        var id = pairId ?? Guid.NewGuid();
        var trace = traceId ?? Guid.NewGuid();
        return
        [
            new RequestResponseLog(testId, testId, method, body, new Uri(uri), [], service, caller,
                RequestResponseType.Request, trace, id, false) { Timestamp = sent },
            new RequestResponseLog(testId, testId, method, "{\"ok\":true}", new Uri(uri), [], service, caller,
                RequestResponseType.Response, trace, id, false, status) { Timestamp = answered }
        ];
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
