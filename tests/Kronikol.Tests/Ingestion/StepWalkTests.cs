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
    [InlineData("peer/I-raw-bars-unescaped", "s0 /read=0 /send=1 /confirm=2")]
    [InlineData("peer/AF-unescaped-marker", "s0 /x=0")]
    public void A_bar_written_without_todays_escapes_reads_as_published_reads_it(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: a hook's bar, then rows sharing a first line, an opening of 400 characters, or apart by a space. Only the text can tell.
    [InlineData("peer/G-shared-first-line-one-out", "s0 /seed=- /cash=- /card=- /confirm=-")]
    [InlineData("peer/H-shared-400-chars-one-out", "s0 /seed=- /cash=- /card=- /confirm=-")]
    [InlineData("peer/J-fold-false-match-one-out", "s0 /seed=- /n=- /m=- /confirm=-")]
    public void Bars_that_are_one_out_read_no_row_that_did_not_draw_them(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: a doc string or a table's opening on a cut bar that is not the row's
    [InlineData("review/J12-capture-doc-cut-prefix-escaped", "s0 /pay=-")]
    // pin: a cut bar whose label runs on past the row's text
    [InlineData("review/J13-capture-doc-cut-prefix-wrapped", "s0 /pay=-")]
    // pin: a line break after a label with no cut
    [InlineData("fresh/N2-line-break-after-label-no-cut", "s0 /pay=-")]
    // pin: a blank line followed by anything but a table or a doc string
    [InlineData("review/J14-capture-blank-line-label", "s0 /pay=-")]
    [InlineData("review/H5-blank-line-label-false-accept", "s0 /pay=-")]
    [InlineData("review/H6-doc-string-cut-prefix", "s0 /pay=-")]
    // pin: a later letter in another case
    [InlineData("review/J15-capture-case-only", "s0 /pay=-")]
    public void A_bar_that_is_not_its_rows_stays_no_step(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // A cut in or after a table's name, and a circled first letter.
    [InlineData("narrow/C1-cut-after-a-table-name", "s0 /pay=0")]
    [InlineData("narrow/C1b-cut-inside-a-table-name", "s0 /pay=0")]
    [InlineData("narrow/C2-circled-letter", "s0 /pay=0")]
    // pin: what follows the cut is not a table's
    [InlineData("narrow/C3-micro-sign", "s0 /pay=-")]
    [InlineData("narrow/C4-two-word-keyword-other-case", "s0 /pay=-")]
    public void A_cut_bar_reads_as_its_rows_up_to_the_cut(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // BDDfy's title keeps its And or But, so it draws And And y.
    [InlineData("second/B1-bddfy-and-under-and-escape", "s0 /x=0 /y=1")]
    [InlineData("second/B2-bddfy-and-under-and-short-control", "s0 /x=0 /y=1")]
    public void A_bars_first_word_can_come_before_the_rows_own_keyword(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: a first letter's case under a keyword, a quote or a digit first, a camelCase first word
    [InlineData("narrow/A1-first-letter-under-a-keyword", "s0 /pay=-")]
    [InlineData("narrow/A9-first-letter-wrapped-no-escape", "s0 /pay=-")]
    [InlineData("narrow/A4-quote-first", "s0 /pay=-")]
    [InlineData("narrow/A5-digit-first", "s0 /pay=-")]
    [InlineData("narrow/A6-camelCase-first", "s0 /pay=-")]
    // pin: a row with no text takes no other bar's first word for a keyword
    [InlineData("second/N1-no-text-row-takes-a-two-word-bar", "s0 /hook=- /ship=1")]
    [InlineData("second/N2-no-text-keyword-row-takes-a-two-word-bar", "s0 /hook=- /ship=1")]
    public void A_row_apart_from_a_bar_by_what_StepText_does_not_do_stays_no_step(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // A row with no keyword, or a bar with none, reads a text as StepText would have it on the other side.
    [InlineData("text/K1-keyword-row-bar-with-none-capital", "s0 /pay=0 /ship=1")]
    [InlineData("text/K2-keyword-row-capital-bar-with-none-lower", "s0 /pay=0 /ship=1")]
    public void A_bar_with_no_keyword_reads_a_text_as_StepText_would_have_it(string name, string reads) => AssertReads(name, reads);

    [Theory]
    [InlineData("fresh/N1-two-word-keyword-escaped", "s0 /read=0 /pay=1")]
    public void A_bars_keyword_of_two_words_reads_where_the_row_holds_them(string name, string reads) => AssertReads(name, reads);

    // ─── A row with no text ───────────

    [Theory]
    [InlineData("peer/D-step-with-no-text", "s0 /a=0 /b=1")]
    public void A_row_with_no_text_keeps_the_step_bar_it_draws(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin
    [InlineData("review/F3-no-text-row-capture-bars", "s0 /basket=- /pay=-")]
    [InlineData("review/K2-no-text-middle-capture-bars", "s0 /basket=0 /pay=- /send=-")]
    [InlineData("review/J10-literal-step-text-no-bar", "s0 /a=-")]
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
    [InlineData("position/X1-marked-background", "s0 /seed=- /basket=0 /check=1")]
    [InlineData("position/X2-two-rows-two-scenarios", "s0 /seed=- /basket=0 /check=1; s1 /seed=- /basket=0 /check=1")]
    // An assertion ahead of the first step.
    [InlineData("position/X3-assertion-first", "s0 /basket=1 /check=2")]
    [InlineData("peer/B-assertion-before-first-step", "s0 /basket=1 /check=2")]
    [InlineData("peer/U-assertions-before-the-first-step", "s0 /basket=2 /check=3")]
    // A step with no time.
    [InlineData("position/X4-untimed-row-between", "s0 /basket=1 /send=2 /check=3")]
    // A marked Background whose text a later step holds, whose first line it shares, or that it holds only once spaces are folded.
    [InlineData("position/X5-marked-background-text-in-the-bar", "s0 /orders=0 /confirm=1")]
    [InlineData("peer/A-bg-text-in-first-bar", "s0 /orders=0 /confirm=1; s1 /orders=0 /confirm=1")]
    [InlineData("peer/R-bg-first-line-is-a-steps", "s0 /card=0 /confirm=1")]
    [InlineData("peer/S-bg-text-in-a-bar-once-folded", "s0 /order=0 /confirm=1")]
    // With a step with no text, and under a long label.
    [InlineData("position/X6-no-text-and-a-background", "s0 /a=0 /b=1")]
    [InlineData("position/Z6-long-label-and-a-background", "s0 /long=0 /check=1")]
    // Opening rows with no time that scenarios share, and rows the detector lifted into a Background.
    [InlineData("position/Z1-detector-lifts-untimed-rows", "s0 /basket=1 /check=2; s1 /basket=1 /check=2; s2 /basket=1 /check=2")]
    [InlineData("review/F2-heuristic-bg-first-untimed", "s0 /a=b1; s1 /seed=b0 /b=b1")]
    [InlineData("review/F2b-heuristic-bg-overlap-wrong", "s0 /a=b1; s1 /seed=b0 /b=b1")]
    [InlineData("review/F2c-heuristic-bg-capture-own-bars", "s0 /a=b1; s1 /seed=b0 /b=b1")]
    [InlineData("review/J3-heuristic-second-untimed", "s0 /b=b1; s1 /seed=b0 /a=b1")]
    // A retried test.
    [InlineData("position/Z3-retry-with-a-marked-background", "s0 /basket=0 /check=1")]
    [InlineData("review/J5-retry-untimed-row-last-attempt", "s0 /second=1")]
    [InlineData("review/J6-retry-raw-bars-only-in-first-attempt", "s0 /second=0")]
    // A nested row with no parent to go under, assertions on either side, and a row with the next one's text.
    [InlineData("position/Z4-nested-row-first", "s0 /inner=- /basket=1 /check=2")]
    [InlineData("position/Z5-failing-assertion-first", "s0 /basket=1 /check=2")]
    [InlineData("position/Z7-background-text-is-the-next-row", "s0 /own=0 /check=1")]
    [InlineData("review/J8-levels", "s0 /o=- /b=1 /sb=1 /n=1 /p=3")]
    public void A_row_the_tests_file_lists_and_draws_no_bar_for_is_left_out_of_the_walk(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: the capture draws the bars, as raw markers or as kind: step records, so a row keeps the bar it drew.
    [InlineData("position/Y1-store-draws-a-marked-background", "s0 /seed=b0 /basket=0 /check=1")]
    [InlineData("position/Y3-store-with-untimed-rows", "s0 /basket=0 /send=1")]
    [InlineData("review/F1-kindstep-untimed-rows", "s0 /basket=0 /pay=1 /send=2")]
    [InlineData("review/F1b-kindstep-untimed-row-overlap", "s0 /first=0 /second=1")]
    [InlineData("review/F1c-kindstep-background-row", "s0 /seed=b0 /pay=0")]
    [InlineData("review/J4-kindstep-failing-step", "s0 /basket=0 /pay=1")]
    public void Where_the_capture_draws_the_bars_a_row_keeps_the_bar_it_drew(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: one that holds its bar keeps it
    [InlineData("review/H4-capture-bar-for-assertion-row", "s0 /seed=0 /basket=1 /pay=2")]
    // pin: a bar it does not hold leaves it and every row after it with no step
    [InlineData("position/Y2-store-with-assertion-first", "s0 /basket=- /check=-")]
    // pin: a hook's bar lands on it, and the rows after read as they did, whatever that bar holds
    [InlineData("narrow/B1-assertion-then-a-hook-bar-raw", "s0 /hook=- /pay=1 /ship=2")]
    [InlineData("narrow/B2-assertion-then-a-hook-bar-kind-step", "s0 /hook=- /pay=1 /ship=2")]
    [InlineData("narrow/B3-assertion-then-a-hook-bar-between", "s0 /pay=- /hook=- /ship=2")]
    [InlineData("second/F1-hook-bar-holds-next-rows-text", "s0 /hook=- /pay=1 /ship=2")]
    [InlineData("second/F2-row-text-in-the-bar-prefix", "s0 /hook=- /stepcall=1 /ship=2")]
    [InlineData("second/F3-blank-row-after-an-assertion", "s0 /hook=- /blank=1 /ship=2")]
    [InlineData("second/F4-assertion-bar-no-glyph-holds-next-rows-text", "s0 /assertcall=- /pay=1 /ship=2")]
    [InlineData("second/F5-run-of-assertions-hook-reads-a-later-one", "s0 /hook1=- /hook2=- /pay=2 /ship=3 /bill=4")]
    [InlineData("second/S1-b3-longer-rows-after-the-hook", "s0 /pay=- /hook=- /ship=2 /bill=3 /file=4")]
    public void An_assertions_row_in_a_capture_counts_as_any_row(string name, string reads) => AssertReads(name, reads);

    [Theory]
    // pin: rows with no bar in one scenario do not move another's walk
    [InlineData("review/J1-skipped-first-scenario-no-times", "s0; s1 /seed=b0 /place=b1 /check=0; s2 /seed=b0 /place=b1 /check=0")]
    [InlineData("review/J2-skipped-last-scenario-no-times", "s0; s1 /seed=b0 /place=b1 /check=0; s2 /seed=b0 /place=b1 /check=0")]
    public void A_row_with_no_bar_in_one_scenario_does_not_move_anothers_walk(string name, string reads) => AssertReads(name, reads);

    // ─── A store's records, and the rest of what was measured ───────────

    [Theory]
    // A store's labels, escaped and on two lines.
    [InlineData("store/S4-store-escapes", "s0 /long=0 /read=1 /send=2 /confirm=3")]
    [InlineData("store/S5-store-two-lines", "s0 /cash=0 /card=1 /confirm=2")]
    // pin: a store's outline row, whose bar is in other words: no step, and no wrong one
    [InlineData("store/S8-store-outline-row", "s0 /shop=- /basket=0 /check=1")]
    [InlineData("store/S9-store-outline-untimed", "s0 /basket=0 /order=- /confirm=2")]
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
    [InlineData("review/H1-reused-id-chronological", "s0 /retry=0 /retry=1")]
    [InlineData("review/H3-no-ids-no-times-raw-bars", "s0 /same=0 /same=1")]
    [InlineData("review/F4-reused-id", "s0 /retry=0 /retry=1")]
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
    [InlineData("narrow/A2-another-keyword", "s0 /pay=0")]
    [InlineData("narrow/A2p-another-keyword-plain", "s0 /pay=0")]
    [InlineData("narrow/A3-no-keyword-row-under-a-keyword-bar", "s0 /pay=0")]
    [InlineData("narrow/A7-no-capitalise", "s0 /pay=0")]
    [InlineData("narrow/A8-wrapped-first-line-blank-and-pipe", "s0 /pay=0")]
    [InlineData("narrow/B4-control-no-assertion", "s0 /hook=- /pay=- /ship=-")]
    [InlineData("narrow/B5-only-an-assertion-row", "s0 /hook=-")]
    [InlineData("narrow/E1-past-a-cut-no-escape", "s0 /pay=0")]
    [InlineData("peer/C-retry", "s0 /basket=0 /check=1")]
    [InlineData("peer/E-capture-own-bars-hook-bar", "s0 /seed=- /basket=- /check=-")]
    [InlineData("peer/F-untimed-rows-own-bars-one-bar-differs", "s0 /basket=0 /order=- /confirm=2")]
    [InlineData("peer/K-control", "s0 /basket=0 /check=1")]
    [InlineData("peer/L-store-bars-with-background", "s0 /shop=b0 /basket=0 /check=1; s1 /shop=b0 /basket=0 /check=1")]
    [InlineData("peer/M-store-hook-bar-and-background", "s0 /seed=- /shop=- /basket=- /check=-")]
    [InlineData("peer/N-nested-doc-table", "s0 /basket=0 /inner=0 /send=1 /check=2")]
    [InlineData("peer/O-heuristic-bg-other-outcome", "s0 /shop=b0 /check=0; s1 /shop=b0 /check=0; s2 /shop=b0 /check=0")]
    [InlineData("peer/P-store-bg-bar-in-other-words", "s0 /shop=- /customer=b1 /basket=0 /check=1")]
    [InlineData("peer/Q-store-own-step-bar-in-other-words", "s0 /shop=- /basket=1 /check=2")]
    [InlineData("peer/V-assertions-and-no-steps", "s0 /basket=-")]
    [InlineData("position/Y4-plain", "s0 /basket=0 /check=1")]
    [InlineData("position/Z2-detector-lifts-timed-rows", "s0 /shop=0 /basket=1 /check=2; s1 /shop=0 /basket=1 /check=2; s2 /shop=0 /basket=1 /check=2")]
    [InlineData("position/Z8-only-marked-rows", "s0 /call=-")]
    [InlineData("review/F3b-blank-text-row-capture-bars", "s0 /basket=0 /pay=-")]
    [InlineData("review/F5b-bar-in-flight-tree", "s0 /slow=0 /pay=1")]
    [InlineData("review/H2-bar-in-flight-chronological", "s0 /slow=0 /pay=1")]
    [InlineData("review/J11-all-no-text-rows-capture-bars", "s0 /a=- /b=-")]
    [InlineData("review/J16-raw-closing-half-only", "s0 /a=-")]
    [InlineData("review/J7-retry-raw-untimed-bar-stays", "s0 /second=-")]
    [InlineData("review/J9-literal-step-text", "s0 /a=0 /b=1")]
    [InlineData("review/K1-identical-rows-substeps", "s0 /x=1")]
    [InlineData("review/K3-no-text-then-repeat", "s0 /first=- /second=1")]
    [InlineData("review/K4-capture-bars-not-for-background", "s0 /basket=- /pay=-")]
    [InlineData("review/K5-one-instant", "s0 /x=2")]
    [InlineData("review/K6-first-word-overlap-capture", "s0 /x=-")]
    [InlineData("review/K7-annotations", "s0 /a=0 /b=1")]
    [InlineData("review/L1-sibling-failed-early-unrun-steps-untimed", "s0 /seed=2; s1 /seed=0 /place=1 /check=2; s2 /seed=0 /place=1 /check=2")]
    [InlineData("review/L2-control-no-extraction", "s0 /seed=1; s1 /seed=0 /place=1")]
    [InlineData("second/C1-assertion-first", "s0 /pay=- /ship=-")]
    [InlineData("second/C2-several-assertions", "s0 /pay=- /ship=-")]
    [InlineData("second/C3-assertion-last-hook", "s0 /pay=0 /hook=-")]
    [InlineData("second/C4-row-and-custom-markers", "s0 /before=- /pay=- /ship=-")]
    [InlineData("second/C5-two-plain-hooks-then-rows", "s0 /hook1=- /hook2=- /pay=2 /ship=3")]
    [InlineData("second/C6-assertion-holds-glyph-bar-late", "s0 /pay=- /assertcall=- /ship=2")]
    [InlineData("second/F6-assertion-then-mid-list-assertion-hook", "s0 /pay=0 /hook=1 /ship=- /bill=-")]
    [InlineData("second/F7-hook-bar-read-by-drawsstep-alone", "s0 /hook=- /pay=1 /ship=2")]
    [InlineData("second/N3-no-text-row-after-an-assertion", "s0 /hook=- /own=1 /ship=2")]
    [InlineData("second/O01-no-kw-escape", "s0 /own=0 /tail=1")]
    [InlineData("second/O02-no-kw-escape-no-capitalise", "s0 /own=0 /tail=1")]
    [InlineData("second/O03-bar-capitalised-row-not", "s0 /own=-")]
    [InlineData("second/O04-bar-not-row-capitalised", "s0 /own=-")]
    [InlineData("second/O05-legacy-cut", "s0 /own=0 /tail=1")]
    [InlineData("second/O05b-legacy-cut-no-capitalise", "s0 /own=0 /tail=1")]
    [InlineData("second/O05c-legacy-cut-escape", "s0 /own=0 /tail=1")]
    [InlineData("second/O06-rich-cut-kw-table", "s0 /own=0 /tail=1")]
    [InlineData("second/O07-rich-cut-escape-no-kw-doc", "s0 /own=0 /tail=1")]
    [InlineData("second/O07b-rich-cut-escape-no-kw-no-capitalise", "s0 /own=0 /tail=1")]
    [InlineData("second/O08-multi-line", "s0 /own=0 /tail=1")]
    [InlineData("second/O09-keyword-trailing-space", "s0 /own=0 /tail=1")]
    [InlineData("second/O10-and-for-a-repeated-keyword", "s0 /own=-")]
    [InlineData("second/O11-blank-keyword", "s0 /own=0 /tail=1")]
    [InlineData("second/O12-spaces-round-text", "s0 /own=0 /tail=1")]
    [InlineData("second/O13-crlf", "s0 /own=0 /tail=1")]
    [InlineData("second/O14-no-text", "s0 /own=0 /tail=1")]
    [InlineData("second/O14b-no-text-keyword", "s0 /own=0 /tail=1")]
    [InlineData("second/O15-table-then-doc-no-kw", "s0 /own=0 /tail=1")]
    [InlineData("second/O16-camel-and-glyph", "s0 /own=0 /own2=1 /own3=2")]
    [InlineData("second/O17-micro-sign-row-on-bar-off", "s0 /own=-")]
    [InlineData("second/O17b-micro-sign-short-control", "s0 /own=-")]
    [InlineData("second/R1-retry-assertion-first-capture-bars", "s0 /pay2=- /ship=-")]
    [InlineData("second/R2-retry-assertion-first-tests-file-bars", "s0 /pay2=1 /ship=2")]
    [InlineData("second/R3-retry-no-numbers", "s0 /pay2=- /ship=-")]
    [InlineData("second/S2-capture-kind-step-and-tests-file-both-draw", "s0 /pay=2 /ship=2")]
    [InlineData("second/S2-kind-step-and-tests-file-both-draw", "s0 /pay=1 /ship=-")]
    [InlineData("second/S2b-control-no-assertion", "s0 /pay=- /ship=-")]
    [InlineData("second/S3-row-bar-drawn-twice", "s0 /pay1=- /pay2=1 /ship=2")]
    [InlineData("second/S4-nested-assertion-bar-b3-kind", "s0 /pay=- /assertcall=- /ship=2")]
    [InlineData("store/S1-store-plain", "s0 /shop=0 /basket=1 /check=2")]
    [InlineData("store/S10-store-nested", "s0 /seed=0 /shop=0 /check=1")]
    [InlineData("store/S11-store-row-not-run", "s0 /shop=- /check=-")]
    [InlineData("store/S12-store-same-text", "s0 /one=0 /two=1 /check=2")]
    [InlineData("store/S2-store-background", "s0 /shop=b0 /customer=b1 /basket=0 /check=1")]
    [InlineData("store/S3-store-untimed", "s0 /shop=0 /basket=1 /check=2")]
    [InlineData("store/S6-store-hook-bar", "s0 /seed=- /shop=- /basket=-")]
    [InlineData("store/S7-store-hook-bar-background", "s0 /seed=- /shop=- /basket=- /check=-")]
    [InlineData("text/K3-control-keyword-row-bar-with-none-same-case", "s0 /pay=0 /ship=1")]
    [InlineData("text/K4-control-keyword-row-own-keyword-capital", "s0 /pay=- /ship=1")]
    [InlineData("text/K5-no-text-keyword-row-bar-with-none", "s0 /own=0 /ship=1")]
    public void The_other_kept_cases_read_as_they_were_measured(string name, string reads) => AssertReads(name, reads);

    // ─── A mismatch reaches the report (#129, the plan's R5) ───────────

    [Fact]
    public void A_bar_that_does_not_fit_its_row_reaches_every_output()
    {
        var (_, reports) = Ingest("review/J15-capture-case-only");

        using (var data = JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(reports, "TestRunReport.json"))))
        {
            var entry = Assert.Single(data.RootElement.GetProperty("diagnostics").EnumerateArray(),
                d => d.GetProperty("kind").GetString() == nameof(DiagnosticKind.StepAttributionMismatch));
            Assert.Contains("up to the next marker that matches", entry.GetProperty("message").GetString());
        }

        using (var card = JsonDocument.Parse(File.ReadAllText(Path.Combine(reports, "service.json"))))
            Assert.True(card.RootElement.GetProperty("run").GetProperty("degraded").GetProperty(nameof(DiagnosticKind.StepAttributionMismatch)).GetInt32() > 0);

        var summary = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["summary", reports], summary, new StringWriter()));
        Assert.Contains(nameof(DiagnosticKind.StepAttributionMismatch), summary.ToString());

        var answer = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["failures", reports], answer, new StringWriter()));
        Assert.Contains(answer.ToString().Split('\n'), line => line.StartsWith('!') && line.Contains("does not match step"));

        Assert.True(File.Exists(Path.Combine(reports, "TestRunReport.labs.html")));
    }

    [Fact]
    public void A_run_whose_bars_all_fit_lists_no_mismatch_and_writes_no_labs_page_for_it()
    {
        // pin
        var (_, reports) = Ingest("position/Y4-plain");

        using (var data = JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(reports, "TestRunReport.json"))))
            if (data.RootElement.TryGetProperty("diagnostics", out var diagnostics))
                Assert.DoesNotContain(diagnostics.EnumerateArray(), d => d.GetProperty("kind").GetString() == nameof(DiagnosticKind.StepAttributionMismatch));

        Assert.False(File.Exists(Path.Combine(reports, "TestRunReport.labs.html")));
    }

    [Fact]
    public void A_mismatch_reaches_the_data_file_with_the_digest_off()
    {
        var (_, reports) = Ingest("review/J15-capture-case-only", options => options.GenerateFailuresDigest = false);

        using var data = JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(reports, "TestRunReport.json")));
        Assert.Contains(data.RootElement.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("kind").GetString() == nameof(DiagnosticKind.StepAttributionMismatch));
    }

    [Fact]
    public void A_mismatch_reaches_the_labs_page_with_the_data_file_off()
    {
        var (_, reports) = Ingest("review/J15-capture-case-only", options => options.GenerateTestRunReportData = false);

        Assert.False(File.Exists(Path.Combine(reports, "TestRunReport.json")));
        Assert.True(File.Exists(Path.Combine(reports, "TestRunReport.labs.html")));
    }

    [Fact]
    public void A_mismatch_is_listed_once_and_says_where_the_walk_picks_up_again()
    {
        // A hook's bar first: the bars are one out, and the last bar has no row. The message used to say every call after it
        // lost its step, which is false past the next bar that matches.
        var (result, _) = Ingest("review/J15-capture-case-only");

        var entry = Assert.Single(result.Diagnostics, e => e.Kind == DiagnosticKind.StepAttributionMismatch);
        Assert.DoesNotContain("interactions after it carry no stepPath", entry.Message);
    }

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

    private (IngestResult Result, string Reports) Ingest(string name, Action<ReportConfigurationOptions>? configure = null)
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

        configure?.Invoke(options);
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
