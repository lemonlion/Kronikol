using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Ingestion;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// <c>plans/CALLS_UNDER_THEIR_STEP_PLAN.md</c> R2, #128: the walk over a test's step bars, where a walk still runs. Each case
/// under <c>TestData/StepWalk</c> is a tests file and a capture, as <c>kronikol ingest captures --tests tests.ndjson</c> reads
/// them, and a row names the case and what each of its calls reads: <c>s0 /pay=1</c> is the call to <c>/pay</c> in scenario
/// <c>s0</c> on step <c>1</c>, and <c>-</c> is no step. A row with a comment saying pin holds what published read.
/// </summary>
[Collection("DiagramsFetcher")]
public class StepWalkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-step-walk-" + Guid.NewGuid().ToString("N"));

    public StepWalkTests()
    {
        Directory.CreateDirectory(_dir);
        RequestResponseLogger.Redaction = null;
    }

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch (IOException) { /* best effort */ }
    }

    // ─── A label the bar changes (#128, F12) ───────────

    [Theory]
    [InlineData("s2", "/long-label=0 /next=1")] // wraps at 110 characters
    [InlineData("s3", "/backslash=0 /next=1")]
    [InlineData("s4", "/param=0 /next=1")] // loader markup, <$param>
    [InlineData("s7", "/huge=0 /next=1")] // cut by the bar
    [InlineData("s9", "/newline=0 /next=1")] // more than one line
    [InlineData("s11", "/percent=0 /next=1")] // a builtin call, %date()
    [InlineData("s12", "/separator=0 /next=1")] // a line separator
    [InlineData("s13", "/tiny=0 /next=1")]
    [InlineData("s14", "/markup2=0 /next=1")] // a later line the bar escapes
    [InlineData("s15", "/tab=0 /next=1")]
    [InlineData("s16", "/late=0 /next=1")]
    [InlineData("s17", "/slashes=0 /next=1")]
    [InlineData("s18", "/cut-styled=0 /next=1")]
    [InlineData("s19", "/cut-coloured=0 /next=1")]
    // pin: a bar that draws the label as it is, a creole table, a lower-case label, a tilde
    [InlineData("s10", "/tilde=0 /next=1")]
    [InlineData("s5", "/creole=0 /next=1")]
    [InlineData("s6", "/table=0 /next=1")]
    [InlineData("s8", "/lower=0 /next=1")]
    public void A_label_keeps_its_calls_whatever_the_bar_does_to_it(string scenario, string reads)
    {
        var (_, reports) = Ingest("labels/all");

        Assert.Equal($"{scenario} {reads}", Reads(reports).Single(line => line.StartsWith(scenario + " ", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_label_the_bar_changes_records_no_mismatch()
    {
        var (result, _) = Ingest("labels/all");

        Assert.DoesNotContain(result.Diagnostics, e => e.Kind == DiagnosticKind.StepAttributionMismatch);
    }

    [Fact]
    public void A_bar_that_is_not_its_rows_still_reads_no_step_and_records_a_mismatch()
    {
        // The matcher must not pass everything.
        var (result, reports) = Ingest("review/J15-capture-case-only");

        Assert.Equal("s0 /pay=-", Reads(reports).Single());
        Assert.Contains(result.Diagnostics, e => e.Kind == DiagnosticKind.StepAttributionMismatch);
    }

    // ─── What published reads stays read, and what it refuses stays refused ───────────

    [Theory]
    // pin: a bar an older version or another producer wrote without today's escapes
    @@peer/I
    @@peer/AF
    public void A_bar_written_without_todays_escapes_reads_as_published_reads_it(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: a hook's bar, then rows sharing a first line, an opening of 400 characters, or apart by a space. Only the text can tell.
    @@peer/G
    @@peer/H
    @@peer/J
    public void Bars_that_are_one_out_read_no_row_that_did_not_draw_them(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: a doc string or a table's opening on a cut bar that is not the row's
    @@review/J12
    // pin: a cut bar whose label runs on past the row's text
    @@review/J13
    // pin: a line break after a label with no cut
    @@fresh/N2 => s0 /pay=-
    // pin: a blank line followed by anything but a table or a doc string
    @@review/J14
    @@review/H5
    @@review/H6
    // pin: a later letter in another case
    @@review/J15
    public void A_bar_that_is_not_its_rows_stays_no_step(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // A cut in or after a table's name, and a circled first letter.
    @@narrow/C1
    @@narrow/C1b
    @@narrow/C2
    // pin: what follows the cut is not a table's
    @@narrow/C3
    @@narrow/C4
    public void A_cut_bar_reads_as_its_rows_up_to_the_cut(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // BDDfy's title keeps its And or But, so it draws And And y.
    @@second/B1
    @@second/B2
    public void A_bars_first_word_can_come_before_the_rows_own_keyword(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: a first letter's case under a keyword, a quote or a digit first, a camelCase first word
    @@narrow/A1
    @@narrow/A9
    @@narrow/A4
    @@narrow/A5
    @@narrow/A6
    // pin: a row with no text takes no other bar's first word for a keyword
    @@second/N1
    @@second/N2
    public void A_row_apart_from_a_bar_by_what_StepText_does_not_do_stays_no_step(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // A row with no keyword, or a bar with none, reads a text as StepText would have it on the other side.
    @@text/K1
    @@text/K2
    public void A_bar_with_no_keyword_reads_a_text_as_StepText_would_have_it(string name, string reads) => AssertReads(name, reads);

    [Theory]
    @@fresh/N1 => s0 /read=0 /pay=1
    public void A_bars_keyword_of_two_words_reads_where_the_row_holds_them(string name, string reads) => AssertReads(name, reads);

    // ─── A row with no text ───────────

    [Theory]
    @@peer/D
    public void A_row_with_no_text_keeps_the_step_bar_it_draws(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin
    @@review/F3
    @@review/K2
    @@review/J10
    public void A_row_with_no_text_takes_no_other_bar(string name, string reads) => AssertReads(name, reads);

    [Fact]
    public void A_blank_text_still_takes_any_bar_as_on_published()
    {
        // pin: an empty string is inside every bar. §11 of the plan lists it as a wrong read this release leaves.
        AssertReads("review/F3b-blank-text-row-capture-bars", "s0 /basket=0 /pay=-");
    }

    // ─── Rows that draw no bar (F15) ───────────

    [Theory]
    // The wiki's own shape, a background: true row, and two of them over two scenarios.
    @@position/X1
    @@position/X2
    // An assertion ahead of the first step.
    @@position/X3
    @@peer/B
    @@peer/U
    // A step with no time.
    @@position/X4
    // A marked Background whose text a later step holds, whose first line it shares, or that it holds only once spaces are folded.
    @@position/X5
    @@peer/A
    @@peer/R
    @@peer/S
    // With a step with no text, and under a long label.
    @@position/X6
    @@position/Z6
    // Opening rows with no time that scenarios share, and rows the detector lifted into a Background.
    @@position/Z1
    @@review/F2
    @@review/F2b
    @@review/F2c
    @@review/J3
    // A retried test.
    @@position/Z3
    @@review/J5
    @@review/J6
    // A nested row with no parent to go under, assertions on either side, and a row with the next one's text.
    @@position/Z4
    @@position/Z5
    @@position/Z7
    @@review/J8
    public void A_row_the_tests_file_lists_and_draws_no_bar_for_is_left_out_of_the_walk(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: the capture draws the bars, as raw markers or as kind: step records, so a row keeps the bar it drew.
    @@position/Y1
    @@position/Y3
    @@review/F1
    @@review/F1b
    @@review/F1c
    @@review/J4
    public void Where_the_capture_draws_the_bars_a_row_keeps_the_bar_it_drew(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: one that holds its bar keeps it
    @@review/H4
    // pin: a bar it does not hold leaves it and every row after it with no step
    @@position/Y2
    // pin: a hook's bar lands on it, and the rows after read as they did, whatever that bar holds
    @@narrow/B1
    @@narrow/B2
    @@narrow/B3
    @@second/F1
    @@second/F2
    @@second/F3
    @@second/F4
    @@second/F5
    @@second/S1
    public void An_assertions_row_in_a_capture_counts_as_any_row(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: rows with no bar in one scenario do not move another's walk
    @@review/J1
    @@review/J2
    public void A_row_with_no_bar_in_one_scenario_does_not_move_anothers_walk(string name, string reads) => AssertReads(name, reads);

    // ─── A store's records, and the rest of what was measured ───────────

    [Theory]
    // A store's labels, escaped and on two lines.
    @@store/S4
    @@store/S5
    // pin: a store's outline row, whose bar is in other words: no step, and no wrong one
    @@store/S8
    @@store/S9
    public void A_stores_bars_read_as_its_rows(string name, string reads) => AssertReads(name, reads);

    [Fact]
    public void A_second_ingest_over_one_list_of_a_hosts_records_reads_as_fresh_records_do()
    {
        // Nothing is written on a record. A first capture with no bars must leave the second free to draw its own.
        var t0 = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        TestRunRecord Row(string ev, string? text = null, double? ms = null, string? keyword = null) => new()
        {
            Event = ev, TestId = "s", TestName = "t", Text = text, Keyword = keyword, Status = "passed",
            Timestamp = ms is { } at ? t0.AddMilliseconds(at) : null,
        };
        InteractionRecord Bar(string label, double ms, string id, bool end) => new()
        {
            Kind = "marker", MarkerKind = "Step", Type = "Request", Uri = "http://override.com/", ServiceName = "", CallerName = "", TestId = "s",
            RequestResponseId = id, Timestamp = t0.AddMilliseconds(ms),
            PlantUml = end ? null : "\nhnote across <<stepDelimiter>> #black:<color:white>" + label + "\n\n", MarkerEnd = end ? true : null,
        };
        InteractionRecord Half(string type, string path, double ms) => new()
        {
            Type = type, Method = "GET", Uri = "http://x.test/" + path, ServiceName = "S", CallerName = "T", TestId = "s", RequestResponseId = path,
            Timestamp = t0.AddMilliseconds(ms),
        };
        var records = new List<TestRunRecord>
        {
            Row("start", ms: 0), Row("step", "a customer", null, "Given"), Row("step", "a basket", 100, "Given"), Row("step", "it is paid", 200, "When"), Row("end", ms: 500),
        };
        var calls = new List<InteractionRecord> { Half("Request", "basket", 110), Half("Response", "basket", 115), Half("Request", "pay", 210), Half("Response", "pay", 215) };
        var bars = new List<InteractionRecord>
        {
            Bar("Given a customer", 50, "b0", false), Bar("", 50, "b0e", true), Bar("Given a basket", 100, "b1", false), Bar("", 100, "b1e", true),
            Bar("When it is paid", 200, "b2", false), Bar("", 200, "b2e", true), Half("Request", "first", 60), Half("Response", "first", 65),
        };
        bars.AddRange(calls);

        string Once(string name, List<InteractionRecord> capture, List<TestRunRecord> tests)
        {
            var options = Options(name);
            Assert.True(IngestPipeline.Run(new IngestRequest { Interactions = capture, TestRecords = tests, Options = options }).Generated);
            return Reads(options.ReportsFolderPath!).Single();
        }

        Assert.Equal("s0 /basket=1 /pay=2", Once("first", calls, records));
        Assert.Equal("s0 /first=0 /basket=1 /pay=2", Once("second", bars, records));
        Assert.Equal("s0 /first=0 /basket=1 /pay=2", Once("fresh", bars, records.Select(r => r with { }).ToList()));
    }

    // ─── A response's step (F9) ───────────

    [Fact]
    public void A_response_takes_its_requests_step_across_a_bar()
    {
        var (_, reports) = Ingest("review/H2-bar-in-flight-chronological");

        Assert.Equal(["s0/0", "s0/0", "s0/1", "s0/1"], Enumerable.Range(0, 4).Select(i => InStep(reports, $"s0/i{i}")).ToArray());
    }

    [Theory]
    // pin: it takes the last request's where an id repeats
    @@review/H1
    @@review/H3
    @@review/F4
    public void A_repeated_id_reads_the_last_requests_step(string name, string reads) => AssertReads(name, reads);

    [Fact]
    public void A_response_where_an_id_repeats_reads_the_last_requests_step()
    {
        // pin
        var (_, reports) = Ingest("review/H1-reused-id-chronological");

        Assert.Equal(["s0/0", "s0/0", "s0/1", "s0/1"], Enumerable.Range(0, 4).Select(i => InStep(reports, $"s0/i{i}")).ToArray());
    }

    // ─── Two rows that read alike (F16) ───────────

    [Fact]
    public void Two_rows_that_read_alike_keep_their_own_sub_steps()
    {
        var (_, reports) = Ingest("review/K1-identical-rows-substeps");

        Assert.Equal(["0", "0.0", "1", "1.0"], Rows(reports));
    }

    [Fact]
    public void Two_rows_apart_by_their_durations_keep_their_own_sub_steps_as_they_did()
    {
        // pin
        var (_, reports) = Ingest("fresh/N4-K1-durations-apart");

        Assert.Equal(["0", "0.0", "1", "1.0"], Rows(reports));
    }

    // ─── Everything else the plan measured stays as it was measured ───────────

    [Theory]
    @@REST
    public void The_other_kept_cases_read_as_they_were_measured(string name, string reads) => AssertReads(name, reads);

    // ─── Helpers ───────────

    private void AssertReads(string name, string reads)
    {
        var (_, reports) = Ingest(name);

        Assert.Equal(reads, string.Join("; ", Reads(reports)));
    }

    private ReportConfigurationOptions Options(string name)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, name.Replace('/', '-'));
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        return options;
    }

    private (IngestResult Result, string Reports) Ingest(string name)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "StepWalk", name.Replace('/', Path.DirectorySeparatorChar));
        var flagsFile = Path.Combine(source, "flags.txt");
        var flags = File.Exists(flagsFile) ? File.ReadAllText(flagsFile).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries) : [];

        var options = Options(name);
        if (flags.Contains("--no-capitalise"))
        {
            options.CapitaliseStepText = false;
            options.CapitaliseTitles = false;
        }

        var result = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [.. Directory.GetFiles(Path.Combine(source, "captures"))],
            TestsFile = Path.Combine(source, "tests.ndjson"),
            Options = options,
            CallTreeOrdering = !flags.Contains("--chronological"),
        });

        Assert.True(result.Generated);
        return (result, options.ReportsFolderPath!);
    }

    /// <summary>One line per scenario, each call once for each step it reads.</summary>
    private static string[] Reads(string reports)
    {
        var lines = new List<string>();
        foreach (var scenario in Items(["scenarios", reports]))
        {
            var address = scenario.TryGetProperty("address", out var named) ? named.GetString()! : scenario.GetProperty("id").GetString()!;
            var calls = Items(["interactions", reports, address])
                .Select(call => (Uri: call.GetProperty("uri").GetString()!, Step: call.TryGetProperty("stepPath", out var step) ? step.GetString() : null))
                .Distinct()
                .Select(call => $"/{call.Uri[(call.Uri.LastIndexOf('/') + 1)..]}={call.Step ?? "-"}");
            lines.Add($"{address} {string.Join(" ", calls)}".TrimEnd());
        }

        return [.. lines];
    }

    private static JsonElement[] Items(string[] args)
    {
        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run([.. args, "--json", "--max-bytes", "0"], output, new StringWriter()));
        using var json = JsonDocument.Parse(output.ToString());
        return [.. json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone())];
    }

    /// <summary>What <c>http</c> says the record at this address is in.</summary>
    private static string? InStep(string reports, string address)
    {
        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["http", reports, address], output, new StringWriter()));
        const string prefix = "in step ";
        return output.ToString().Split('\n').Select(line => line.TrimEnd('\r')).FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim();
    }

    /// <summary>The address of each row the first scenario lists.</summary>
    private static string[] Rows(string reports)
    {
        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["steps", reports, "s0"], output, new StringWriter()));
        return [.. Regex.Matches(output.ToString(), @"^\s*[✗~·]?\s*(b?\d+(?:\.\d+)*)\s+(?!calls\b)", RegexOptions.Multiline).Select(row => row.Groups[1].Value)];
    }
}
