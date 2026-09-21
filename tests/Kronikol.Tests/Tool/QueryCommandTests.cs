using System.Net;
using System.Text;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tool.Query;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The contract <c>kronikol query</c> makes with an agent: every answer fits in a budget, every truncation
/// says how to resume, every listing hands back addresses that work as input, and no payload is ever
/// printed unless it was named.
/// </summary>
public class QueryCommandTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-query").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── Overview ──────────────────────────────────────────────

    [Fact]
    public void Summary_names_the_run_the_failures_and_where_to_go_next()
    {
        var output = Run("summary", Report());

        Assert.Contains("7 scenarios", output);
        Assert.Contains("1 failed", output);
        Assert.Contains("s2", output);
        Assert.Contains("next: failures", output);
    }

    [Fact]
    public void Summary_stays_small()
    {
        // The whole point: a run that would be megabytes as JSON answers in a couple of kilobytes.
        Assert.True(Encoding.UTF8.GetByteCount(Run("summary", Report())) < 2000);
    }

    [Fact]
    public void Scenarios_filters_by_result()
    {
        var output = Run("scenarios", Report(), "--result", "Failed");

        Assert.Contains("Checkout fails", output);
        Assert.DoesNotContain("Browse the catalogue", output);
    }

    [Fact]
    public void Count_prints_a_number_and_nothing_else()
    {
        Assert.Equal("1", Run("scenarios", Report(), "--result", "Failed", "--count").Trim());
    }

    [Fact]
    public void Services_answers_the_negative_question()
    {
        var output = Run("services", Report());

        Assert.Contains("payments", output);
        Assert.DoesNotContain("bigquery", output);
        Assert.Contains("a service missing here was never called", output);
    }

    [Fact]
    public void Services_counts_errors_and_bytes()
    {
        var output = Run("services", Report());

        Assert.Matches(@"payments\s+\d+\s+2", output);
    }

    // ─── Narrative ─────────────────────────────────────────────

    [Fact]
    public void Failures_says_why_without_being_asked_for_a_payload()
    {
        var output = Run("failures", Report());

        Assert.Contains("Checkout fails", output);
        Assert.Contains("Expected 4173 but found 3902", output);
        Assert.Contains("OverviewTests.cs:142", output);
        Assert.DoesNotContain("4173, \"currency\"", output);
    }

    [Fact]
    public void Summary_says_which_run_the_file_is_when_it_was_built_on_ci()
    {
        // The question a downloaded artifact has to answer before any other: which commit is this?
        var path = Path.Combine(_directory, "OnCi.json");
        File.Move(ReportGenerator.GenerateTestRunReportData(
            BuildFeatures(allPassing: false),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "OnCiSrc_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json,
            ciMetadata: new CiMetadata(CiEnvironment.GitHubActions, "412", "main",
                "9f3c1b2a4d5e6f708192a3b4c5d6e7f809a1b2c3", null, "acme/shop", "99")), path, overwrite: true);

        var output = Run("summary", path);

        Assert.Contains("run: main @9f3c1b2  acme/shop  GitHubActions #412", output);
    }

    [Fact]
    public void Summary_says_nothing_about_ci_on_a_run_that_was_not_on_ci()
    {
        // `provider: None` is a fact about the run, not a line worth spending the budget on.
        Assert.DoesNotContain("run: ", Run("summary", Report(fileName: "OffCi.json")));
    }

    [Fact]
    public void Failures_hands_back_a_link_that_opens_the_scenario_in_the_report()
    {
        // The address an agent prints for a human. `sN` addresses the data file and the stable id
        // addresses the HTML; the two together are the only pair that survives a rename.
        var report = Report();
        File.WriteAllText(Path.ChangeExtension(report, ".html"), "<html></html>");

        var output = Run("failures", report);

        var stableId = ScenarioStableId.Compute(RunSuite.Current, "Orders", "Checkout fails on a wrong total");
        Assert.Contains($"open: TestRunReport.html#sid-{stableId}", output);
    }

    [Fact]
    public void Failures_offers_no_link_when_the_html_was_never_generated()
    {
        // A JSON-only run has nothing to open, and a link into a file that is not there is worse
        // than no link at all.
        var output = Run("failures", Report(fileName: "NoHtml.json"));

        Assert.Contains("Checkout fails on a wrong total", output);
        Assert.DoesNotContain("#sid-", output);
    }

    [Fact]
    public void Steps_offers_the_same_link_next_to_the_stable_id_it_already_prints()
    {
        var report = Report();
        File.WriteAllText(Path.ChangeExtension(report, ".html"), "<html></html>");

        var output = Run("steps", report, "s2");

        Assert.Contains("open: TestRunReport.html#sid-", output);
    }

    [Fact]
    public void Failures_on_a_green_run_says_so_rather_than_printing_nothing()
    {
        var output = Run("failures", Report(allPassing: true));

        Assert.Contains("nothing failed", output);
        Assert.Contains("next: scenarios", output);
    }

    [Fact]
    public void Steps_shows_the_tree_with_interaction_ranges()
    {
        var output = Run("steps", Report(), "s2");

        Assert.Contains("Given a basket", output);
        Assert.Contains("[i0", output);
        Assert.Contains("✗", output);
    }

    [Fact]
    public void Assertions_lists_them_flat_with_their_source()
    {
        var output = Run("assertions", Report(), "--failed");

        Assert.Contains("total == 4173", output);
        Assert.Contains("OverviewTests.cs:142", output);
    }

    [Fact]
    public void Flow_replaces_reading_the_diagram()
    {
        var output = Run("flow", Report(), "s2");

        Assert.Contains("payments", output);
        Assert.Contains("→", output);
        Assert.True(Encoding.UTF8.GetByteCount(output) < 3000);
    }

    [Fact]
    public void Annotations_surface_the_example_row_marker()
    {
        var output = Run("annotations", Report(), "s2");

        Assert.Contains("Row 3", output);
    }

    // ─── Payloads ──────────────────────────────────────────────

    [Fact]
    public void Interactions_prints_body_pointers_never_bodies()
    {
        var output = Run("interactions", Report(), "s2");

        Assert.Contains("b:", output);
        Assert.DoesNotContain("3902", output);
    }

    [Fact]
    public void Http_without_a_payload_flag_describes_the_body_and_offers_the_cheap_views()
    {
        var output = Run("http", Report(), "s2/i0");

        Assert.Contains("body:", output);
        Assert.Contains("--keys", output);
        Assert.DoesNotContain("\"total\"", output);
    }

    [Fact]
    public void Http_keys_shows_the_shape_for_a_fraction_of_the_payload()
    {
        var output = Run("http", Report(), "s2/i1", "--keys");

        Assert.Contains("$.total", output);
        Assert.Contains("number", output);
    }

    [Fact]
    public void Http_path_pulls_one_value()
    {
        var lines = Run("http", Report(), "s2/i1", "--path", "$.total").Trim().ReplaceLineEndings("\n").Split('\n');

        Assert.Equal("3902", lines[^1].Trim());
    }

    [Fact]
    public void A_missing_path_is_an_answer_not_an_error()
    {
        var output = Run("http", Report(), "s2/i1", "--path", "$.nope");

        Assert.Contains("is not in this body", output);
        Assert.Contains("--keys", output);
    }

    [Fact]
    public void Identical_bodies_share_one_address()
    {
        var output = Run("body", Report(), BodyHashOf(Report(), "s0/i1"));

        Assert.Contains("address(es)", output);
    }

    [Fact]
    public void Out_writes_the_payload_and_costs_almost_no_output()
    {
        var target = Path.Combine(_directory, "body.json");

        var output = Run("http", Report(), "s2/i1", "--body", "--out", target);

        Assert.True(File.Exists(target));
        Assert.Contains("3902", File.ReadAllText(target));
        Assert.DoesNotContain("3902", output);
        // The metadata block, and nothing else. It grew by one line in 3.1.0 — the address of the call's
        // other half — so the bound moved with it; what the bound is for is that the payload is not here.
        var cost = Encoding.UTF8.GetByteCount(output);
        Assert.True(cost < 400, $"{cost} bytes of output for a payload that went to a file:\n{output}");
    }

    [Fact]
    public void A_capture_truncated_body_says_so()
    {
        var output = Run("http", Report(), "s0/i18", "--body");

        Assert.Contains("capped at capture time", output);
    }

    [Fact]
    public void Diagram_refuses_stdout_and_says_what_to_do_instead()
    {
        var (output, error, exit) = RunFull("diagram", Report(), "s0/d0");

        Assert.Equal(2, exit);
        Assert.Contains("--out", error);
        Assert.Contains("flow s0", error);
        Assert.DoesNotContain("@startuml", output);
    }

    [Fact]
    public void Diagram_out_writes_the_plantuml()
    {
        var target = Path.Combine(_directory, "d.puml");

        Run("diagram", Report(), "s0/d0", "--out", target);

        Assert.Contains("@startuml", File.ReadAllText(target));
    }

    [Fact]
    public void Note_lists_a_diagram_and_warns_that_a_note_is_a_rendering()
    {
        Assert.Contains("notes", Run("note", Report(), "s0/d0"));
        Assert.Contains("not the captured content", Run("note", Report(), "s0/d0/n0"));
    }

    // ─── The path engine (M1) ──────────────────────────────────

    [Fact]
    public void Path_wildcard_lists_every_match_with_its_concrete_path()
    {
        var output = Run("http", Report(), "s3/i11", "--path", "$.items[*].price");

        Assert.Contains("$.items[0].price = 12.5", output);
        Assert.Contains("$.items[2].price = -3", output);
    }

    [Fact]
    public void Path_length_function_counts_an_array()
    {
        var lines = Run("http", Report(), "s3/i11", "--path", "$.items.length()").Trim().ReplaceLineEndings("\n").Split('\n');

        Assert.Equal("3", lines[^1].Trim());
    }

    [Fact]
    public void Path_length_on_a_scalar_says_what_kind_it_was()
    {
        var output = Run("http", Report(), "s3/i11", "--path", "$.total.length()");

        Assert.Contains("number", output);
    }

    [Fact]
    public void Path_miss_suggests_the_nearest_key()
    {
        var output = Run("http", Report(), "s3/i11", "--path", "$.totl");

        Assert.Contains("nearest: $.total", output);
    }

    [Fact]
    public void Path_bracket_quoted_key_containing_a_dot()
    {
        var lines = Run("http", Report(), "s3/i11", "--path", "$.flags['feature.x']").Trim().ReplaceLineEndings("\n").Split('\n');

        Assert.Equal("true", lines[^1].Trim());
    }

    [Fact]
    public void Big_path_result_describes_itself_instead_of_printing()
    {
        var output = Run("http", BigDiagramReport(), "s0/i1", "--path", "$.items");

        Assert.Contains("500 elements", output);
        Assert.Contains("--path", output);
        Assert.DoesNotContain("\"sku\"", output);
    }

    // ─── Exact pairing and the one error classifier (M1) ───────

    [Fact]
    public void Interleaved_calls_to_one_service_pair_by_requestResponseId()
    {
        var output = Run("interactions", Report(), "s4");

        Assert.Matches(@"(?m)^s4/i0\s+payments\s+POST /charge\s+OK\b", output);
        Assert.Matches(@"(?m)^s4/i1\s+payments\s+POST /charge\s+InternalServerError", output);
    }

    /// <summary>
    /// <c>--status</c> had no test at all, and the class of filter it advertises most prominently — the
    /// range form, documented twice in the usage text — was <b>dead code</b> for every HTTP call ever
    /// captured. The report stored the enum NAME, so <c>int.TryParse</c> failed and the whole <c>Nxx</c>
    /// branch could never match; measured against a report containing a BadRequest, <c>--status 4xx</c>
    /// and <c>--status 400</c> each returned "nothing matched" while <c>--status BadRequest</c> matched.
    ///
    /// <para>All three forms are pinned together because the fix has to keep the name working: a reader
    /// who learned <c>--status InternalServerError</c> should not be re-taught by a release.</para>
    /// </summary>
    [Theory]
    [InlineData("5xx")]
    [InlineData("500")]
    [InlineData("InternalServerError")]
    public void Status_filters_by_range_by_number_and_by_name(string status)
    {
        var output = Run("interactions", Report(), "--status", status);

        Assert.Matches(@"(?m)^s4/i1\s+payments\s+POST /charge\s+InternalServerError", output);
        Assert.DoesNotContain("nothing matched", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_status_range_that_matches_nothing_says_so_rather_than_matching_everything()
    {
        // The other half of the range form: 3xx is absent from this report, and the filter has to be able
        // to return nothing. A filter that silently matched everything would pass the facts above too.
        var output = Run("interactions", Report(), "--status", "3xx");

        Assert.Contains("nothing matched", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Pairing_falls_back_to_proximity_when_the_id_is_absent()
    {
        var output = Run("interactions", Report(), "s4");

        Assert.Matches(@"(?m)^s4/i4\s+legacy\s+GET /ping\s+OK\b", output);
    }

    [Fact]
    public void Created_and_NoContent_are_not_errors_anywhere()
    {
        var flow = Run("flow", Report(), "s3", "--errors-only");
        Assert.DoesNotContain("preauth", flow);
        Assert.DoesNotContain("hold", flow);

        var services = Run("services", Report(), "s3");
        Assert.Matches(@"(?m)^payments\s+\d+\s+0", services);
    }

    [Fact]
    public void Text_ERROR_status_is_an_error_everywhere()
    {
        var flow = Run("flow", Report(), "s3", "--errors-only");
        Assert.Contains("orders-db", flow);

        var services = Run("services", Report(), "s3");
        Assert.Matches(@"(?m)^orders-db\s+\d+\s+1", services);
    }

    /// <summary>
    /// Kronikol stamps its own success labels on the calls that are not HTTP - a broker publish is
    /// <c>Sent</c>, a consume is <c>Ack</c>, a reply is <c>Responded</c>, a cache lookup is <c>Hit</c>
    /// or <c>Miss</c>, a Spanner transaction is <c>Committed</c>. The classifier treated every one of
    /// them as an error because none of them is spelled "OK", so a clean run of a message-driven
    /// service reported one error per call and `services` invented an error rate of 100%.
    /// </summary>
    [Fact]
    public void Kronikols_own_success_labels_are_not_errors()
    {
        var services = Run("services", BrokerStatusReport());

        Assert.Matches(@"(?m)^bus\s+3\s+0\b", services);
        Assert.Matches(@"(?m)^cache\s+2\s+0\b", services);
        Assert.Matches(@"(?m)^ledger\s+1\s+0\b", services);
    }

    /// <summary>The negative twin: a refusal is still a refusal, and must not be swept up by the fix above.</summary>
    [Fact]
    public void A_negative_acknowledgement_or_a_fault_is_still_an_error()
    {
        var services = Run("services", BrokerStatusReport());

        Assert.Matches(@"(?m)^broker\s+2\s+2\b", services);
    }

    /// <summary>
    /// A re-run carries the same <c>runId</c> as the run it retried, so the attempt is the only thing
    /// that tells two downloaded artifacts apart. Reading it into the index and never printing it would
    /// make it write-only - present in the file, absent from every answer.
    /// </summary>
    [Fact]
    public void Summary_tells_a_re_run_apart_from_the_run_it_retried()
    {
        var output = Run("summary", RetriedCiReport());

        Assert.Contains("attempt 2", output);
    }

    [Fact]
    public void The_json_run_block_carries_the_whole_run_identity()
    {
        var envelope = JsonDocument.Parse(Run("summary", RetriedCiReport(), "--json")).RootElement;
        var ci = envelope.GetProperty("run").GetProperty("ci");

        Assert.Equal("99", ci.GetProperty("runId").GetString());
        Assert.Equal("2", ci.GetProperty("runAttempt").GetString());
    }

    // ─── values (M2) ───────────────────────────────────────────

    [Fact]
    public void Values_groups_distinct_values_with_counts_and_an_example_address()
    {
        var output = Run("values", Report(), "--path", "$.status", "--service", "payments");

        Assert.Matches(@"""APPROVED""\s+×5", output);
        Assert.Contains("(absent)", output);
        Assert.Contains("s3/i", output);
    }

    [Fact]
    public void Values_counts_occurrences_not_distinct_bodies()
    {
        // Three calls carried the byte-identical APPROVED body; the rich body adds a fourth occurrence.
        var output = Run("values", Report(), "s3", "--path", "$.status", "--service", "payments");

        Assert.Matches(@"""APPROVED""\s+×4", output);
        Assert.Contains("distinct", output);
    }

    [Fact]
    public void Values_reports_absent_as_a_value()
    {
        var output = Run("values", Report(), "s3", "--path", "$.status", "--service", "payments");

        Assert.Contains("(absent)", output);
    }

    [Fact]
    public void Values_stats_summarises_a_numeric_path_with_extreme_addresses()
    {
        var output = Run("values", Report(), "s3", "--path", "$.total", "--service", "payments", "--stats");

        Assert.Contains("min 12.5 (s3/i", output);
        Assert.Contains("max 4173 (s3/i", output);
        Assert.Contains("absent 1", output);
    }

    [Fact]
    public void Values_wildcard_counts_every_element()
    {
        var output = Run("values", Report(), "s3", "--path", "$.items[*].price");

        Assert.Contains("1250", output);
        Assert.Contains("-3", output);
    }

    [Fact]
    public void Values_footnotes_bodiless_calls()
    {
        var output = Run("values", Report(), "s3", "--path", "$.status", "--service", "payments");

        Assert.Contains("carried no body", output);
    }

    [Fact]
    public void Values_request_flag_targets_request_bodies()
    {
        var output = Run("values", Report(), "s3", "--path", "$.amount", "--request", "--service", "payments");

        Assert.Matches(@"100\s+×3", output);
    }

    [Fact]
    public void Values_scoped_to_one_scenario()
    {
        var output = Run("values", Report(), "s3", "--path", "$.status");

        Assert.DoesNotContain("s4/", output);
    }

    [Fact]
    public void Values_without_a_path_exits_2_with_usage()
    {
        var (_, error, exit) = RunFull("values", Report());

        Assert.Equal(2, exit);
        Assert.Contains("--path", error);
    }

    [Fact]
    public void Values_footnotes_unpaired_calls_under_response_targeting()
    {
        var output = Run("values", Report(), "s3", "--path", "$.ack", "--service", "bus");

        Assert.Contains("no response", output);
    }

    [Fact]
    public void Values_evaluates_a_paired_event_response_normally()
    {
        var output = Run("values", Report(), "s3", "--path", "$.ack", "--service", "bus");

        Assert.Contains("true", output);
    }

    [Fact]
    public void Values_both_tags_each_row_with_direction()
    {
        var output = Run("values", Report(), "s3", "--path", "$.event", "--both", "--service", "bus");

        Assert.Contains("req", output);
        Assert.Contains("resp", output);
    }

    [Fact]
    public void Values_stays_small_on_a_wide_run()
    {
        var output = Run("values", BigDiagramReport(), "--path", "$.items[*].price", "--request");

        Assert.True(Encoding.UTF8.GetByteCount(output) <= 6400, $"values produced {Encoding.UTF8.GetByteCount(output)} bytes");
    }

    // ─── --where (M3) ──────────────────────────────────────────

    [Fact]
    public void Where_filters_on_a_response_value()
    {
        var output = Run("interactions", Report(), "s3", "--where", "$.status = DECLINED", "--service", "payments");

        Assert.Contains("s3/i6", output);
        Assert.DoesNotContain("s3/i0 ", output);
    }

    [Fact]
    public void Where_comparison_is_numeric_not_lexical()
    {
        // Lexically "100" < "99"; numerically 100 > 99. Totals in s3: 100×3, 50, 12.5, 4173.
        var output = Run("interactions", Report(), "s3", "--where", "$.total > 99", "--service", "payments", "--count");

        Assert.Equal("4", output.Trim());
    }

    [Fact]
    public void Where_wildcard_passes_when_any_element_satisfies()
    {
        var output = Run("interactions", Report(), "s3", "--where", "$.items[*].price < 0");

        Assert.Contains("s3/i10", output);
        Assert.Equal("1", Run("interactions", Report(), "s3", "--where", "$.items[*].price < 0", "--count").Trim());
    }

    [Fact]
    public void Where_req_prefix_targets_the_request()
    {
        var output = Run("interactions", Report(), "s3", "--where", "req:$.amount = 50", "--service", "payments");

        Assert.Contains("s3/i6", output);
        Assert.Equal("1", Run("interactions", Report(), "s3", "--where", "req:$.amount = 50", "--service", "payments", "--count").Trim());
    }

    [Fact]
    public void Wheres_compose_as_and()
    {
        var output = Run("interactions", Report(), "s3",
            "--where", "$.status = APPROVED", "--where", "$.total < 200", "--service", "payments", "--count");

        Assert.Equal("3", output.Trim());
    }

    [Fact]
    public void Where_reports_how_many_calls_had_no_evaluable_body()
    {
        var output = Run("interactions", Report(), "s3", "--where", "$.x = 1", "--service", "printer");

        Assert.Contains("no evaluable body", output);
    }

    [Fact]
    public void Where_bad_grammar_exits_2_with_the_grammar()
    {
        var (_, error, exit) = RunFull("interactions", Report(), "s3", "--where", "$.x ??? 1");

        Assert.Equal(2, exit);
        Assert.Contains("exists", error);
    }

    [Fact]
    public void Interactions_without_an_address_cover_the_run()
    {
        var output = Run("interactions", Report(), "--service", "legacy");

        Assert.Contains("s4/i4", output);
    }

    [Fact]
    public void Where_survives_paging_in_the_rerun_footer()
    {
        var output = Run("interactions", Report(), "--where", "$.status = APPROVED", "--limit", "2");

        Assert.Contains("--where \"$.status = APPROVED\"", output);
        Assert.Contains("--offset 2", output);
    }

    [Fact]
    public void Values_where_filters_the_aggregation()
    {
        var output = Run("values", Report(), "s3", "--path", "$.total", "--where", "$.status = APPROVED", "--service", "payments");

        Assert.Matches(@"100\s+×3", output);
        Assert.DoesNotContain("12.5", output);
    }

    // ─── --group-by (M5) ───────────────────────────────────────

    [Fact]
    public void GroupBy_counts_errors_and_distinct_bodies_per_bucket()
    {
        var output = Run("interactions", Report(), "--group-by", "service,status");

        // payments × InternalServerError: the s2 charge and the s4 interleaved failure — 2 calls,
        // 2 errors, 2 distinct response bodies.
        Assert.Matches(@"payments\s+InternalServerError\s+2\s+2\b", output);
    }

    [Fact]
    public void GroupBy_composes_with_where()
    {
        var output = Run("interactions", Report(), "--group-by", "service", "--where", "$.status = APPROVED");

        Assert.Matches(@"payments\s+5\b", output);
    }

    [Fact]
    public void GroupBy_unknown_dimension_lists_the_valid_ones()
    {
        var (_, error, exit) = RunFull("interactions", Report(), "--group-by", "nope");

        Assert.Equal(2, exit);
        Assert.Contains("service", error);
        Assert.Contains("capturedBy", error);
    }

    [Fact]
    public void GroupBy_and_group_refuse_to_compose()
    {
        var (_, error, exit) = RunFull("interactions", Report(), "s3", "--group", "--group-by", "service");

        Assert.Equal(2, exit);
        Assert.Contains("compose", error);
    }

    [Fact]
    public void GroupBy_at_run_scope()
    {
        var output = Run("interactions", Report(), "--group-by", "step");

        Assert.Contains("spans scenarios", output);
    }

    // ─── trace (M7) ────────────────────────────────────────────

    [Fact]
    public void Trace_lists_the_chain_chronologically_with_offsets()
    {
        var output = Run("trace", Report(), ChainTrace);

        Assert.Contains("+0 ms", output);
        Assert.Contains("+300 ms", output);
        Assert.Contains("s3/i6", output);
        Assert.Contains("s3/i10", output);
    }

    [Fact]
    public void Trace_flags_cross_scenario_spans()
    {
        var output = Run("trace", Report(), LeakedTrace);

        Assert.Contains("spans 2 scenarios", output);
    }

    [Fact]
    public void Trace_by_interaction_address()
    {
        var output = Run("trace", Report(), "s3/i6");

        Assert.Contains("s3/i10", output);
    }

    [Fact]
    public void Trace_prefix_must_be_unambiguous()
    {
        var (_, error, exit) = RunFull("trace", Report(), "4bf92f35");

        Assert.Equal(2, exit);
        Assert.Contains(ChainTrace, error);
        Assert.Contains(LeakedTrace, error);
    }

    [Fact]
    public void Trace_on_an_unenriched_report_says_why()
    {
        var (_, error, exit) = RunFull("trace", UnenrichedReport(), "s0/i0");

        Assert.Equal(2, exit);
        Assert.Contains("current Kronikol", error);
    }

    [Fact]
    public void Trace_footer_admits_no_parent_links()
    {
        var output = Run("trace", Report(), ChainTrace);

        Assert.Contains("not its tree", output);
    }

    // ─── Search and comparison ─────────────────────────────────

    [Fact]
    public void Grep_returns_addresses_not_content()
    {
        var output = Run("grep", Report(), "3902");

        Assert.Contains("s2/i1", output);
        Assert.True(Encoding.UTF8.GetByteCount(output) < 1500);
    }

    [Fact]
    public void Grep_values_names_the_json_path_a_number_came_from()
    {
        var output = Run("grep", Report(), "3902", "--values");

        Assert.Contains("$.total", output);
    }

    [Fact]
    public void Grep_that_finds_nothing_says_where_it_looked()
    {
        var output = Run("grep", Report(), "zzz-not-here");

        Assert.Contains("is not in", output);
        Assert.Contains("--in", output);
    }

    // ─── grep --number (M6) ────────────────────────────────────

    [Fact]
    public void NumberGrep_matches_across_formatting()
    {
        // The user quotes the formatted number; the payload holds the raw one.
        var output = Run("grep", Report(), "4,173.00", "--number");

        Assert.Contains("$.total = 4173", output);
    }

    [Fact]
    public void NumberGrep_emits_the_json_path_of_each_hit()
    {
        var output = Run("grep", Report(), "4173", "--number");

        Assert.Contains("$.total", output);
        Assert.Contains("$.display", output);
    }

    [Fact]
    public void NumberGrep_shows_the_raw_text_when_it_differed()
    {
        var output = Run("grep", Report(), "4173", "--number");

        Assert.Contains("≈", output);
    }

    [Fact]
    public void NumberGrep_tolerance_absolute_and_percent()
    {
        Assert.Contains("$.total", Run("grep", Report(), "4170", "--number", "--tolerance", "5"));
        Assert.Contains("$.total", Run("grep", Report(), "4170", "--number", "--tolerance", "1%"));
    }

    [Fact]
    public void NumberGrep_matches_a_european_decimal_comma()
    {
        // "4.173,00" is European for 4173.00; naive comma-stripping reads it as 4.173.
        var output = Run("grep", Report(), "4173", "--number");

        Assert.Contains("$.euDisplay", output);
    }

    [Fact]
    public void NumberGrep_rejects_a_non_numeric_needle()
    {
        var (_, error, exit) = RunFull("grep", Report(), "abc", "--number");

        Assert.Equal(2, exit);
        Assert.Contains("numeric needle", error);
    }

    [Fact]
    public void Compare_puts_two_scenarios_side_by_side()
    {
        var output = Run("compare", Report(), "s0", "s2");

        Assert.Contains("steps:", output);
        Assert.Contains("calls:", output);
    }

    [Fact]
    public void Diff_matches_on_stable_id_and_reports_what_broke()
    {
        var older = Report(allPassing: true, fileName: "Old.json");

        var output = Run("diff", older, Report());

        Assert.Contains("BROKE", output);
        Assert.Contains("stableId", output);
    }

    [Fact]
    public void Diff_across_runs_survives_duplicate_stableIds()
    {
        // A [Theory] with repeated data, the same example row in two Examples: blocks, or a retried
        // scenario gives two scenarios one stableId. The cross-run diff must match them in order and say
        // so, never throw on the duplicate key.
        var older = Write("Dup-old.json", RepeatedRows(secondFails: false), null);
        var newer = Write("Dup-new.json", RepeatedRows(secondFails: true), null);

        var output = Run("diff", older, newer);

        Assert.Contains("BROKE", output);
        Assert.Contains("share a stableId", output);
        Assert.Contains("matched in order", output);
    }

    [Fact]
    public void Diff_reports_a_repeated_row_that_disappeared()
    {
        var older = Write("Dup-gone-old.json", RepeatedRows(secondFails: false), null);
        var newer = Write("Dup-gone-new.json", [RepeatedRows(secondFails: false)[0] with { Scenarios = [RepeatedRows(secondFails: false)[0].Scenarios[0]] }], null);

        var output = Run("diff", older, newer);

        Assert.Contains("Gone (1)", output);
    }

    private static Feature[] RepeatedRows(bool secondFails) =>
    [
        new Feature
        {
            DisplayName = "Retries",
            Scenarios =
            [
                new Scenario { Id = "d0", DisplayName = "Repeated row", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) },
                new Scenario
                {
                    Id = "d1", DisplayName = "Repeated row", Duration = TimeSpan.FromSeconds(1),
                    Result = secondFails ? ExecutionResult.Failed : ExecutionResult.Passed,
                    ErrorMessage = secondFails ? "second attempt failed" : null
                }
            ]
        }
    ];

    [Fact]
    public void Parse_rejects_the_dead_raw_flag()
    {
        var error = new StringWriter();

        var options = QueryOptions.Parse(["TestRunReport.json", "--raw"], error);

        Assert.Null(options);
        Assert.Contains("Unknown option: --raw", error.ToString());
    }

    [Fact]
    public void Provenance_names_the_mergeable_format_not_a_merge()
    {
        // mergeableFormatVersion means "the superset format a runner wrote", which every runner in a
        // sharded build produces; it does not mean the file is the result of a merge.
        var json = ReportGenerator.GenerateMergeableReportJson(BuildFeatures(allPassing: true),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            diagramLookup: null, componentRelationships: null, internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null);
        var path = Path.Combine(_directory, "Mergeable.json");
        File.WriteAllText(path, json);

        var output = Run("summary", path);

        Assert.Contains("! mergeable-format report", output);
        Assert.DoesNotContain("merge of several runs", output);
    }

    // ─── Body diff (M4) ────────────────────────────────────────

    [Fact]
    public void Diff_bodies_prints_only_differing_paths()
    {
        var output = Run("diff", Report(), "s5/i1", "s6/i1");

        Assert.Contains("$.customer.region", output);
        Assert.Contains("→ null", output);
        Assert.Contains("$.total: 4173 → 3902", output);
        Assert.DoesNotContain("\"sku\"", output);
    }

    [Fact]
    public void Diff_identical_bodies_answers_from_the_index()
    {
        var output = Run("diff", Report(), "s3/i0", "s3/i2");

        Assert.Contains("byte-identical", output);
    }

    [Fact]
    public void Diff_array_length_change_is_one_row_then_the_tail()
    {
        var output = Run("diff", Report(), "s5/i1", "s6/i1");

        Assert.Contains("$.items: 2 → 3 elements", output);
        Assert.Contains("$.items[0].price: 12.5 → 1250", output);
    }

    [Fact]
    public void Diff_shifted_array_collapses_to_a_summary()
    {
        var output = Run("diff", Report(), "s5/i5", "s6/i5");

        Assert.Contains("shifted", output);
        Assert.DoesNotContain("$.tags[3]", output);
    }

    [Fact]
    public void Diff_added_subtree_is_a_shape_not_a_dump()
    {
        var output = Run("diff", Report(), "s5/i1", "s6/i1");

        Assert.Contains("(absent) → {sku, price}", output);
    }

    [Fact]
    public void Diff_non_json_falls_back_to_lines()
    {
        var output = Run("diff", Report(), "s5/i3", "s6/i3");

        Assert.Contains("line 2", output);
        Assert.Contains("4173", output);
        Assert.Contains("3902", output);
    }

    [Fact]
    public void Scenarios_marks_the_rows_that_share_one_identity()
    {
        // A [Theory] with repeated data, the same Examples: row twice, or a retry: several scenarios,
        // one stableId. Nothing in the listing said so, and the first symptom was `diff` behaving in a
        // way that looked wrong. The marker says which rows a cross-run match cannot tell apart.
        var report = WriteWithDuplicateStableIds("Repeated.json");

        var output = Run("scenarios", report);

        Assert.Contains("\u00d72", output);
    }

    [Fact]
    public void Scenarios_marks_nothing_when_every_identity_is_distinct()
    {
        Assert.DoesNotContain("\u00d7", Run("scenarios", Report()));
    }

    [Fact]
    public void Diff_of_a_report_with_no_stableIds_says_so_rather_than_claiming_a_collision()
    {
        // A pre-3.0.47 report carries no stableId at all, so every scenario falls into the empty-string
        // group. The duplicate warning then announced that every scenario in the file "shares a
        // stableId (repeated rows or retries)", which is both alarming and untrue - what actually
        // happens is that the two runs are matched by position, and that is what it should say.
        var before = WriteWithoutStableIds("BeforeNoIds.json");
        var after = WriteWithoutStableIds("AfterNoIds.json");

        var output = Run("diff", before, after);

        Assert.DoesNotContain("share a stableId", output);
        Assert.Contains("no stableIds", output);
    }

    [Fact]
    public void Diff_across_runs_matches_the_scenario_by_stableId()
    {
        var output = Run("diff", Report(), ShiftedReport(), "--body", "s5/i1");

        Assert.Contains("4174", output);
    }

    [Fact]
    public void Diff_of_two_scenario_addresses_points_at_compare()
    {
        var (_, error, exit) = RunFull("diff", Report(), "s3", "s5");

        Assert.Equal(2, exit);
        Assert.Contains("compare", error);
    }

    [Fact]
    public void Compare_points_at_the_first_differing_body()
    {
        var output = Run("compare", Report(), "s5", "s6");

        Assert.Contains("first differing body: diff s5/i1 s6/i1", output);
    }

    [Fact]
    public void Diff_notes_a_capture_truncated_body()
    {
        var output = Run("diff", Report(), "s0/i18", "s2/i1");

        Assert.Contains("capped at capture time", output);
    }

    [Fact]
    public void Values_notes_capture_truncated_bodies()
    {
        var output = Run("values", Report(), "s0", "--path", "$.anything", "--request", "--service", "api");

        Assert.Contains("capped at capture time", output);
    }

    [Fact]
    public void Diff_of_two_large_bodies_stays_small()
    {
        var output = Run("diff", BigDiagramReport(), "s0/i0", "s0/i1");

        Assert.True(Encoding.UTF8.GetByteCount(output) <= 6400, $"diff produced {Encoding.UTF8.GetByteCount(output)} bytes");
    }

    // ─── The invariants ────────────────────────────────────────

    [Theory]
    [InlineData("summary")]
    [InlineData("scenarios")]
    [InlineData("failures")]
    [InlineData("services")]
    public void No_overview_command_emits_a_payload(string command)
    {
        var output = Run(command, Report());

        Assert.DoesNotContain("customerReference", output);
        Assert.DoesNotContain("@startuml", output);
    }

    [Theory]
    [InlineData("steps")]
    [InlineData("flow")]
    [InlineData("interactions")]
    [InlineData("annotations")]
    public void No_scenario_command_emits_a_payload(string command)
    {
        var output = Run(command, Report(), "s2");

        Assert.DoesNotContain("customerReference", output);
        Assert.DoesNotContain("@startuml", output);
    }

    [Fact]
    public void Every_command_stays_under_the_budget()
    {
        foreach (var (command, args) in new (string, string[])[]
                 {
                     ("summary", []), ("scenarios", []), ("failures", []), ("services", []),
                     ("steps", ["s2"]), ("flow", ["s2"]), ("interactions", ["s2"]), ("assertions", []),
                     ("annotations", ["s2"]), ("grep", ["a"])
                 })
        {
            var output = Run(command, Report(), args);
            Assert.True(Encoding.UTF8.GetByteCount(output) <= 6400,
                $"{command} produced {Encoding.UTF8.GetByteCount(output)} bytes");
        }
    }

    [Fact]
    public void A_truncated_listing_says_how_to_resume()
    {
        var output = Run("interactions", Report(), "s0", "--limit", "2");

        Assert.Contains("--offset 2", output);
    }

    [Fact]
    public void Offset_resumes_where_the_footer_said()
    {
        var first = Run("interactions", Report(), "s0", "--limit", "2");
        var second = Run("interactions", Report(), "s0", "--limit", "2", "--offset", "2");

        Assert.NotEqual(first, second);
        Assert.Contains("of ", second);
    }

    [Fact]
    public void A_page_whose_first_row_does_not_fit_says_so_instead_of_pointing_at_itself()
    {
        // The budget is smaller than one row, so nothing renders. The old footer read
        // `next: --offset 0` - the offset it was already at. A human raises the budget; a script that
        // follows `next` re-runs the identical command forever. There is no next page at this budget,
        // so there must be no pointer to one.
        var output = Run("interactions", Report(), "s0", "--max-bytes", "40");

        Assert.DoesNotContain("next:", output);
        Assert.Contains("--max-bytes", output);
    }

    [Fact]
    public void An_offset_past_the_end_says_the_listing_is_exhausted_rather_than_pointing_past_it()
    {
        // Same infinite loop from the other end: nothing to render, so the old footer echoed the offset
        // back. It also printed a nonsense range - `scenarios: 100-99 of 7`.
        var output = Run("scenarios", Report(), "--offset", "99");

        Assert.DoesNotContain("next:", output);
        Assert.DoesNotContain("100-99", output);
        Assert.Contains("7 scenarios", output);
    }

    [Fact]
    public void A_last_page_reached_by_offset_does_not_offer_a_page_after_it()
    {
        // Completion was judged as `last >= all.Count && offset == 0`, so the final page of a paged walk
        // - which by definition has a non-zero offset - still advertised a next page that holds nothing.
        var output = Run("scenarios", Report(), "--offset", "5");

        Assert.Contains("6-7 of 7", output);
        Assert.DoesNotContain("next:", output);
    }

    [Fact]
    public void Failures_pages_by_the_limit_it_was_given_rather_than_by_a_hard_coded_25()
    {
        // The footer arithmetic hard-coded 25, the default cap, and ignored --limit entirely: three
        // failures with --limit 2 printed two and then claimed "3 failed" with no way to resume - the
        // one shape the footer contract exists to prevent.
        var output = Run("failures", ManyFailuresReport(), "--limit", "2");

        Assert.Contains("failures: 1-2 of 3", output);
        Assert.Contains("--offset 2", output);
    }

    [Fact]
    public void The_last_page_of_failures_says_it_is_the_last()
    {
        var output = Run("failures", ManyFailuresReport(), "--limit", "2", "--offset", "2");

        Assert.Contains("3 failed", output);
        Assert.DoesNotContain("next: --offset", output);
    }

    [Fact]
    public void Grouping_collapses_repeated_calls_into_one_row()
    {
        var ungrouped = Run("interactions", Report(), "s0", "--limit", "500");
        var grouped = Run("interactions", Report(), "s0", "--group", "--limit", "500");

        Assert.True(grouped.Split('\n').Length < ungrouped.Split('\n').Length);
        Assert.Contains("×", grouped);
    }

    [Fact]
    public void A_mistyped_grep_target_is_refused_rather_than_matching_nothing()
    {
        // The dangerous outcome is not the typo, it is the answer: an unknown target used to be dropped
        // silently, so `--in bodys` searched nothing, found nothing, and read exactly like proof the
        // value is not in the report.
        var (_, error, exit) = RunFull("grep", Report(), "4173", "--in", "bodys");

        Assert.Equal(2, exit);
        Assert.Contains("bodys", error);
        Assert.Contains("bodies", error);
    }

    [Fact]
    public void A_mistyped_grep_target_is_refused_on_the_numeric_path_too()
    {
        var (_, error, exit) = RunFull("grep", Report(), "4173", "--number", "--in", "notez");

        Assert.Equal(2, exit);
        Assert.Contains("notez", error);
    }

    [Fact]
    public void Every_documented_grep_target_is_still_accepted()
    {
        foreach (var target in QueryCommand.GrepTargets)
        {
            var (_, error, exit) = RunFull("grep", Report(), "4173", "--in", target);
            Assert.True(exit == 0, $"--in {target} exited {exit}: {error}");
        }
    }

    [Fact]
    public void An_empty_grep_target_list_is_refused_rather_than_searching_nothing()
    {
        // `--in ""` is not null, so the default set is not applied, and the split drops the empty entry -
        // leaving zero targets, which the validating loop then passes by never running. An agent that
        // builds the flag by joining a list that came back empty would get a confident false negative.
        foreach (var empty in new[] { "", ",", " , " })
        {
            var (_, error, exit) = RunFull("grep", Report(), "4173", "--in", empty);
            Assert.True(exit == 2, $"--in \"{empty}\" exited {exit}");
            Assert.Contains("bodies", error);
        }
    }

    [Fact]
    public void Sorting_the_ungrouped_interaction_list_is_refused_rather_than_ignored()
    {
        // Nothing reads --sort on this path. Now that its two sibling views exit 2 on a value they cannot
        // apply, silently accepting one here is the stronger wrong signal: the agent reads row one as the
        // slowest call when it is merely the first captured.
        var (_, error, exit) = RunFull("interactions", Report(), "--sort", "duration");

        Assert.Equal(2, exit);
        Assert.Contains("--group-by", error);
    }

    [Fact]
    public void A_grouped_listing_hands_back_a_next_line_that_keeps_the_sort()
    {
        // The offset was computed against the sorted order. A next: line that drops --sort re-buckets in
        // the default order, so page two is an offset into a different list - it repeats rows already
        // shown and then reports the listing exhausted.
        var first = Run("interactions", Report(), "--group-by", "service,status", "--sort", "errors", "--limit", "2");
        var next = first.Split('\n').First(l => l.Contains("next:", StringComparison.Ordinal));

        Assert.Contains("--sort errors", next);

        var second = Run("interactions", Report(), "--group-by", "service,status", "--sort", "errors", "--offset", "2");
        Assert.NotEqual(Rows(first), Rows(second));
    }

    [Fact]
    public void A_narrowed_grep_hands_back_a_next_line_that_keeps_the_narrowing()
    {
        var first = Run("grep", Report(), "4173", "--in", "bodies", "--limit", "1");
        var next = first.Split('\n').First(l => l.Contains("next:", StringComparison.Ordinal));

        Assert.Contains("--in bodies", next);
    }

    [Fact]
    public void A_scenario_listing_hands_back_a_next_line_that_keeps_its_filter()
    {
        var output = Run("scenarios", Report(), "--slower-than", "0.1", "--limit", "1");
        var next = output.Split('\n').First(l => l.Contains("next:", StringComparison.Ordinal));

        Assert.Contains("--slower-than 0.1", next);
    }

    [Fact]
    public void The_rerun_prefix_carries_every_flag_that_changes_which_rows_are_listed()
    {
        // Directly on the prefix rather than through a listing: this is the contract every paging footer
        // is built from, and a flag missing here breaks whichever verb happens to truncate first.
        var error = new StringWriter();
        var options = QueryOptions.Parse(
            ["r.json", "--sort", "errors", "--in", "bodies", "--values", "--step", "2", "--slower-than", "5"], error);
        Assert.NotNull(options);

        var prefix = options.RerunPrefix();
        foreach (var flag in new[] { "--sort errors", "--in bodies", "--values", "--step 2", "--slower-than 5" })
            Assert.Contains(flag, prefix, StringComparison.Ordinal);
    }

    /// <summary>The listing rows only, so two pages can be compared without their headers and footers.</summary>
    private static string Rows(string output) =>
        string.Join("\n", output.Split('\n').Where(l => l.Length > 0 && !l.StartsWith("next:", StringComparison.Ordinal)
                                                          && !l.Contains(" of ", StringComparison.Ordinal)));

    [Fact]
    public void A_grep_target_in_the_wrong_case_searches_the_target_rather_than_nothing()
    {
        // The validator accepts it case-insensitively, so the consumer has to as well. Accepting
        // `--in BODIES` and then matching nothing would put the silent false negative straight back,
        // one capital letter further away from being noticed.
        var upper = Run("grep", Report(), "4173", "--in", "BODIES");
        var lower = Run("grep", Report(), "4173", "--in", "bodies");

        Assert.Equal(lower, upper);
        Assert.Contains("body", lower);
    }

    [Fact]
    public void A_sort_in_the_wrong_case_orders_the_way_it_was_asked_to()
    {
        var upper = Run("services", Report(), "--sort", "DURATION");
        var lower = Run("services", Report(), "--sort", "duration");

        Assert.Equal(lower, upper);
        Assert.NotEqual(Run("services", Report()), lower);
    }

    [Fact]
    public void A_mistyped_sort_is_refused_rather_than_silently_ordering_by_something_else()
    {
        var (_, error, exit) = RunFull("services", Report(), "--sort", "slowest");

        Assert.Equal(2, exit);
        Assert.Contains("slowest", error);
        Assert.Contains("duration", error);
    }

    [Fact]
    public void A_sort_that_only_one_view_supports_is_refused_by_the_other()
    {
        // `services --sort bytes` is real; `--group-by ... --sort bytes` is not, and used to fall through
        // to the default ordering as though it had been honoured.
        var (_, error, exit) = RunFull("interactions", Report(), "--group-by", "service", "--sort", "bytes");

        Assert.Equal(2, exit);
        Assert.Contains("bytes", error);
    }

    // ─── Addressing and errors ─────────────────────────────────

    [Fact]
    public void An_address_printed_by_one_command_is_accepted_by_the_next()
    {
        var listing = Run("interactions", Report(), "s2");
        var address = listing.Split('\n').First(l => l.StartsWith("s2/i", StringComparison.Ordinal)).Split(' ')[0];

        var (_, _, exit) = RunFull("http", Report(), address);

        Assert.Equal(0, exit);
    }

    [Fact]
    public void An_out_of_range_scenario_says_what_the_range_is()
    {
        var (_, error, exit) = RunFull("steps", Report(), "s99");

        Assert.Equal(2, exit);
        Assert.Contains("the report has 7", error);
    }

    [Fact]
    public void A_directory_is_accepted_when_it_holds_one_report()
    {
        Report();

        Assert.Contains("scenarios", Run("summary", _directory));
    }

    [Fact]
    public void A_current_report_with_nothing_to_attribute_is_not_mistaken_for_an_old_one()
    {
        // Enrichment is detected by the presence of the stepPath key, not of a value: a current report
        // writes it on every interaction and null is a legitimate answer — before the first step, or
        // where attribution could not be trusted.
        var output = Run("summary", Report(allPassing: true, fileName: "NoAttribution.json"));

        Assert.DoesNotContain("predates step attribution", output);
    }

    /// <summary>
    /// The banner fired on evidence of enrichment rather than on evidence of its absence, so a report
    /// that simply had nothing to attribute — a green run with steps, no failures, no tracked calls and
    /// no annotations — read as a file an older Kronikol had written. It is the ordinary shape of a
    /// passing unit-test suite, and the banner asserted of it that "source locations are absent" while
    /// the steps beneath carried them.
    /// </summary>
    [Fact]
    public void A_green_run_that_made_no_calls_is_not_reported_as_an_old_file()
    {
        var output = Run("summary", CurrentGreenReportWithNothingToAttribute());

        Assert.DoesNotContain("predates step attribution", output);
    }

    /// <summary>
    /// <c>--count</c> is documented as one token, and every caller that reads it back parses the whole
    /// of stdout. A provenance note is written before the verb runs, so on the text path it arrived on
    /// the line above the number and made the count unparseable. The warning still has to reach someone:
    /// it goes to stderr, where a count's reader is not looking and an operator is.
    /// </summary>
    [Fact]
    public void Count_is_one_token_even_when_the_report_has_something_to_declare()
    {
        var (output, error, exit) = RunFull("failures", UnenrichedReport(), "--count");

        Assert.Equal(0, exit);
        Assert.Equal("0", output.Trim());
        Assert.Contains("predates step attribution", error);
    }

    [Fact]
    public void An_unenriched_report_still_works_and_says_it_is_one()
    {
        var output = Run("summary", UnenrichedReport());

        Assert.Contains("predates step attribution", output);
        Assert.Contains("scenarios", output);
    }

    [Fact]
    public void Steps_on_an_unenriched_report_still_lists_the_tree()
    {
        var output = Run("steps", UnenrichedReport(), "s0");

        Assert.Contains("Given a basket", output);
        Assert.Contains("no step attribution", output);
    }

    [Fact]
    public void Unknown_command_and_unknown_flag_both_explain_themselves()
    {
        Assert.Equal(2, RunFull("nope", Report()).Exit);
        Assert.Contains("Unknown option", RunFull("summary", Report(), "--nope").Error);
    }

    // ─── The large-file path ───────────────────────────────────

    [Fact]
    public void A_report_with_a_diagram_larger_than_the_read_window_is_still_indexed()
    {
        // The reader works on a window that is refilled as it advances, so a single token bigger than the
        // window has to grow it. A diagram is one JSON string and the real ones reach 663 KB.
        var path = BigDiagramReport();

        var output = Run("summary", path);

        Assert.Contains("1 scenarios", output);
        Assert.True(Encoding.UTF8.GetByteCount(output) < 2000);
    }

    [Fact]
    public void A_big_body_is_indexed_and_fetched_by_address_without_being_printed()
    {
        var path = BigDiagramReport();

        var listing = Run("interactions", path, "s0");
        Assert.Contains("b:", listing);
        Assert.DoesNotContain("filler", listing);

        var target = Path.Combine(_directory, "big.json");
        Run("http", path, "s0/i0", "--body", "--out", target);
        Assert.Contains("filler", File.ReadAllText(target));
    }

    // ─── Perf observables (plans/QUERY_PERF_PLAN.md) ─────────────────
    // Perf properties are asserted on deterministic observables, never on wall-clock — wall-clock lives
    // in the manual harness at tools/query-bench.

    [Fact]
    public void Tool_runtimeconfig_pins_optimized_jit_for_loops()
    {
        // Guards the <TieredCompilationQuickJitForLoops> line in Kronikol.Tool.csproj: a fresh CLI process
        // lives and dies in tier-0 JIT without it (~25-30% of every command on a large report), and the
        // property would be silently lost by a csproj rewrite.
        var path = Path.Combine(AppContext.BaseDirectory, "Kronikol.Tool.runtimeconfig.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var properties = document.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        Assert.True(properties.TryGetProperty("System.Runtime.TieredCompilation.QuickJitForLoops", out var value)
                    && value.ValueKind == System.Text.Json.JsonValueKind.False,
            "Kronikol.Tool.runtimeconfig.json must pin System.Runtime.TieredCompilation.QuickJitForLoops: false");
    }

    [Fact]
    public void BodyCache_opens_the_file_once_for_many_distinct_bodies()
    {
        var index = ReportScanner.Scan(WideReport());
        Assert.True(index.Bodies.Count > 100, $"fixture too small to prove anything: {index.Bodies.Count} distinct bodies");

        using var cache = new BodyCache(index);
        foreach (var hash in index.Bodies.Keys)
        {
            Assert.NotNull(cache.Raw(hash));
            cache.Json(hash);
        }

        Assert.Equal(1, index.PayloadOpens);
    }

    [Fact]
    public void Grep_opens_the_file_once_across_bodies_and_diagrams()
    {
        var index = ReportScanner.Scan(Report());
        var output = RunAgainst(index, "grep", "4173", "--in", "bodies,notes");

        Assert.Contains("body", output);
        Assert.Equal(1, index.PayloadOpens);
    }

    [Fact]
    public void NumberGrep_opens_the_file_once()
    {
        var index = ReportScanner.Scan(Report());
        var output = RunAgainst(index, "grep", "4173", "--number", "--in", "bodies,notes");

        Assert.Contains("body", output);
        Assert.Equal(1, index.PayloadOpens);
    }

    [Fact]
    public void BodyCache_survives_a_missing_or_malformed_slice()
    {
        // The null/fallback contracts of PayloadReader.Read must hold on the shared-handle path too:
        // a never-recorded slice is null, a slice over a non-string token comes back as raw text.
        var raw = "{\"content\":\"{\\\"a\\\":1}\",\"num\":123}";
        var path = Path.Combine(_directory, "tiny.json");
        File.WriteAllText(path, raw);

        var index = new ReportIndex { Path = path, FileLength = raw.Length, LastWriteUtc = File.GetLastWriteTimeUtc(path) };
        index.Bodies["b:missing"] = new BodyEntry { Hash = "b:missing", First = default };
        index.Bodies["b:notastring"] = new BodyEntry { Hash = "b:notastring", First = new Slice(raw.IndexOf("123", StringComparison.Ordinal), 3) };

        using var cache = new BodyCache(index);
        Assert.Null(cache.Raw("b:missing"));
        Assert.Null(cache.Raw("b:absent"));
        Assert.Null(cache.Json("b:missing"));
        Assert.Equal("123", cache.Raw("b:notastring"));
    }

    /// <summary>Drives one command against an already-scanned index, so tests can observe the index afterwards.</summary>
    private static string RunAgainst(ReportIndex index, string command, params string[] args)
    {
        var error = new StringWriter();
        var options = QueryOptions.Parse([index.Path, .. args], error);
        Assert.NotNull(options);
        var output = new StringWriter();
        var writer = new QueryWriter(output, options.MaxBytes);

        var exit = command switch
        {
            "grep" => QueryCommand.Grep(index, options, writer, error),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };

        Assert.True(exit == 0, $"exit {exit}: {error}");
        writer.Flush(error);
        return output.ToString();
    }

    // ─── Per-verb flag legality ────────────────────────────────

    /// <summary>
    /// A flag the verb never reads is worse than an unknown one. <c>--nonsense</c> is refused, but
    /// <c>failures --service Nope</c> used to print every failure in the report under a filter that had
    /// never been applied — an answer indistinguishable from "no call to Nope broke anything", which is
    /// the one conclusion the output does not support.
    /// </summary>
    [Theory]
    [InlineData("--service", "failures", "--service", "payments")]
    [InlineData("--status", "scenarios", "--status", "500")]
    [InlineData("--limit", "summary", "--limit", "3")]
    [InlineData("--limit", "annotations", "s0", "--limit", "5")]
    [InlineData("--failed", "steps", "s0", "--failed")]
    [InlineData("--grep", "flow", "s2", "--grep", "charge")]
    [InlineData("--errors-only", "interactions", "--errors-only")]
    [InlineData("--headers", "values", "--path", "$.total", "--headers")]
    [InlineData("--stats", "http", "s2/i1", "--stats")]
    public void A_flag_a_verb_never_reads_is_refused_rather_than_ignored(string illegal, string command, params string[] args)
    {
        var (output, error, exit) = RunFull(command, Report(), args);

        Assert.Equal(2, exit);
        Assert.Contains(illegal, error, StringComparison.Ordinal);
        Assert.Contains(command, error, StringComparison.Ordinal);
        // Refusal is the whole answer: half a listing under a filter that did not run is the defect.
        Assert.Equal("", output);
    }

    [Theory]
    [InlineData("failures", "--limit", "1")]
    [InlineData("scenarios", "--feature", "Checkout")]
    [InlineData("scenarios", "--slower-than", "0")]
    [InlineData("assertions", "--failed")]
    [InlineData("interactions", "--service", "payments")]
    [InlineData("interactions", "--status", "5xx")]
    [InlineData("values", "--path", "$.total", "--service", "payments")]
    [InlineData("flow", "s2", "--errors-only")]
    [InlineData("services", "--sort", "duration")]
    [InlineData("grep", "4173", "--in", "bodies")]
    [InlineData("http", "s2/i1", "--headers")]
    public void The_flags_a_verb_does_read_are_still_accepted(string command, params string[] args)
    {
        var (_, error, exit) = RunFull(command, Report(), args);

        Assert.True(exit == 0, $"exit {exit}: {error}");
    }

    [Fact]
    public void Every_verb_declares_the_flags_it_reads()
    {
        var undeclared = QueryCommand.Verbs
            .Where(verb => !QueryCommand.FlagsByVerb.ContainsKey(verb))
            .ToArray();

        Assert.Empty(undeclared);
    }

    [Fact]
    public void Every_flag_the_table_names_is_one_the_parser_knows()
    {
        // Guards the table against a typo, which would otherwise read as "this verb accepts nothing of
        // the sort" and refuse a flag that works.
        var unknown = QueryCommand.FlagsByVerb.Values
            .SelectMany(flags => flags)
            .Concat(QueryCommand.UniversalFlags)
            .Distinct(StringComparer.Ordinal)
            .Where(flag => !QueryOptions.KnownFlags.Contains(flag, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(unknown);
    }

    [Fact]
    public void Every_flag_the_parser_accepts_is_legal_on_some_verb()
    {
        // The non-vacuity half: a table that refused everything would pass every test above. A flag no
        // verb can use is either dead surface or a missing table entry, and both are defects.
        var orphans = QueryOptions.KnownFlags
            .Where(flag => !QueryCommand.UniversalFlags.Contains(flag, StringComparer.Ordinal))
            .Where(flag => !QueryCommand.FlagsByVerb.Values.Any(flags => flags.Contains(flag, StringComparer.Ordinal)))
            .ToArray();

        Assert.Empty(orphans);
    }

    [Fact]
    public void Every_flag_the_table_names_parses_as_a_flag()
    {
        // KnownFlags is a list, and a list drifts. Each entry has to reach a real case in the parser -
        // "Unknown option" here means the name in the table is not the name on the command line.
        foreach (var flag in QueryOptions.KnownFlags)
        {
            var error = new StringWriter();
            QueryOptions.Parse([flag, "value"], error);

            Assert.DoesNotContain("Unknown option", error.ToString(), StringComparison.Ordinal);
        }
    }

    // ─── Addresses round-trip ──────────────────────────────────

    /// <summary>
    /// The listing prints a call at <c>s2/i8</c> and, beside it, the content address of the body that
    /// answered it. Asking for that body used to report an address like <c>s2/i11</c> — the response's own
    /// ordinal, which is real and which <c>http</c> accepts, but which appears in no listing anywhere. An
    /// address a reader cannot get back to is not a reference, it is a dead end.
    /// </summary>
    [Fact]
    public void A_body_names_the_call_the_listing_printed_it_beside()
    {
        var report = Report();
        var row = ResponseRow(report);

        var body = Run("body", report, row.ResponseHash);

        Assert.Contains(row.Address, body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_names_the_response_that_answered_it()
    {
        var report = Report();
        var row = ResponseRow(report);

        var call = Run("http", report, row.Address);

        var line = call.Split('\n').FirstOrDefault(l => l.StartsWith("response ", StringComparison.Ordinal));
        Assert.NotNull(line);
        var responseAddress = line!.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
        // ... and the address it names fetches the body the listing showed on this row.
        Assert.Contains(row.ResponseHash, Run("http", report, responseAddress), StringComparison.Ordinal);
    }

    [Fact]
    public void Body_accepts_the_call_address_a_listing_printed()
    {
        var report = Report();
        var row = RequestBodyRow(report);

        var body = Run("body", report, row.Address);

        Assert.Contains(row.RequestHash, body, StringComparison.Ordinal);
    }

    [Fact]
    public void Http_given_a_content_address_names_the_calls_that_carry_it()
    {
        var report = Report();
        var row = RequestBodyRow(report);

        var (output, error, exit) = RunFull("http", report, row.RequestHash);

        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.Contains(row.Address, output, StringComparison.Ordinal);
    }

    /// <summary>The first listing row that carries a response body, with the addresses it printed.</summary>
    private (string Address, string ResponseHash) ResponseRow(string report)
    {
        var row = Run("interactions", report)
            .Split('\n')
            .First(l => l.Contains("→ ", StringComparison.Ordinal) && l.Contains("b:", StringComparison.Ordinal));
        var tokens = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (tokens[0], tokens.Last(t => t.StartsWith("b:", StringComparison.Ordinal)));
    }

    /// <summary>The first listing row that carries a request body, with the addresses it printed.</summary>
    private (string Address, string RequestHash) RequestBodyRow(string report)
    {
        var row = Run("interactions", report)
            .Split('\n')
            .First(l => l.Contains(" body ", StringComparison.Ordinal) && l.Contains("b:", StringComparison.Ordinal));
        var tokens = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (tokens[0], tokens.First(t => t.StartsWith("b:", StringComparison.Ordinal)));
    }

    // ─── A report replaced under a query ───────────────────────
    // The index is one scan; payloads are fetched later by byte offset through a fresh open of the same
    // path. Nothing compared the file with the one that was scanned, so a run finishing in between had
    // its report read at the old offsets and a slice of some other payload returned as the answer.

    private (string Report, ReportIndex Index, Slice LastBody) AScannedReportAndItsLastBody(string fileName)
    {
        var report = Report(fileName: fileName);
        var index = ReportScanner.Scan(report);
        var last = index.Scenarios.SelectMany(s => s.Interactions).Last(i => i.Body.Exists).Body;
        return (report, index, last);
    }

    [Fact]
    public void A_payload_is_not_read_out_of_a_longer_report_that_replaced_the_one_scanned()
    {
        var (report, index, body) = AScannedReportAndItsLastBody("Longer.TestRunReport.json");
        // A late body: the first sits at the same offset in both files and would "pass" by luck.
        File.WriteAllText(report, new string(' ', 4096) + File.ReadAllText(report));

        var thrown = Assert.Throws<ReportChangedException>(() => PayloadReader.Read(index, body));

        Assert.Contains("changed while it was being read", thrown.Message);
    }

    [Fact]
    public void Nor_out_of_a_shorter_one()
    {
        var (report, index, body) = AScannedReportAndItsLastBody("Shorter.TestRunReport.json");
        File.WriteAllText(report, """{"kronikolVersion":"3.1.0","features":[]}""");

        Assert.Throws<ReportChangedException>(() => PayloadReader.Read(index, body));
    }

    [Fact]
    public void A_report_replaced_between_the_scan_and_the_answer_is_exit_1_and_says_to_run_it_again()
    {
        var report = Report(fileName: "Swapped.TestRunReport.json");
        var address = RequestBodyRow(report).Address;
        var output = new StringWriter();
        var error = new StringWriter();

        QueryCommand.AfterScan = path => File.WriteAllText(path, new string(' ', 4096) + File.ReadAllText(path));
        int exit;
        try
        {
            exit = QueryCommand.Run(["body", report, address], output, error);
        }
        finally
        {
            QueryCommand.AfterScan = null;
        }

        Assert.Equal(1, exit);
        Assert.Contains("changed while it was being read; run the command again", error.ToString());
    }

    [Fact]
    public void A_query_holding_the_report_does_not_stop_a_finishing_run_from_replacing_it()
    {
        // The run's side of the same collision: with read-sharing only, a query in flight cost the run
        // its TestRunReport.json on Windows - overwriting it in place and moving it aside were both an
        // IOException.
        var (report, index, _) = AScannedReportAndItsLastBody("Held.TestRunReport.json");
        var aside = Path.Combine(_directory, "Held.aside.json");

        using (PayloadReader.Open(index))
        {
            File.WriteAllText(report, """{"kronikolVersion":"3.1.0","features":[]}""");
            File.Move(report, aside);
        }

        Assert.StartsWith("""{"kronikolVersion":"3.1.0",""", File.ReadAllText(aside));
    }

    // ─── Harness ───────────────────────────────────────────────

    private string Run(string command, string report, params string[] args)
    {
        var (output, error, exit) = RunFull(command, report, args);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output;
    }

    private (string Output, string Error, int Exit) RunFull(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private string BodyHashOf(string report, string address)
    {
        var line = Run("http", report, address).Split('\n').First(l => l.StartsWith("body:", StringComparison.Ordinal));
        return line.Split("· ")[1].Split(' ')[0].Trim();
    }

    // ─── Fixtures ──────────────────────────────────────────────

    private string? _report;
    private string? _unenriched;

    private string Report(bool allPassing = false, string fileName = "TestRunReport.json")
    {
        if (!allPassing && fileName == "TestRunReport.json" && _report is not null)
            return _report;

        var path = Write(fileName, BuildFeatures(allPassing), BuildLogs(), BuildDiagrams());
        if (!allPassing && fileName == "TestRunReport.json")
            _report = path;
        return path;
    }

    /// <summary>Two scenarios carrying the same <c>stableId</c> - a repeated Theory row, or a retry.</summary>
    private string WriteWithDuplicateStableIds(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, """
            {
              "kronikolVersion": "3.1.0",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [
                {
                  "name": "Catalogue",
                  "labels": [],
                  "scenarios": [
                    { "id": "t0", "stableId": "aaaabbbbccccdddd", "name": "Browse", "result": "Passed", "durationSeconds": 1.0, "labels": [], "categories": [], "steps": [] },
                    { "id": "t1", "stableId": "aaaabbbbccccdddd", "name": "Browse", "result": "Failed", "durationSeconds": 0.4, "labels": [], "categories": [], "steps": [] },
                    { "id": "t2", "stableId": "eeeeffff00001111", "name": "Search", "result": "Passed", "durationSeconds": 0.2, "labels": [], "categories": [], "steps": [] }
                  ]
                }
              ]
            }
            """);
        return path;
    }

    /// <summary>A report from before 3.0.47: no <c>stableId</c> on any scenario at all.</summary>
    private string WriteWithoutStableIds(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, """
            {
              "kronikolVersion": "3.0.44",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [
                {
                  "name": "Catalogue",
                  "labels": [],
                  "scenarios": [
                    { "id": "t0", "name": "Browse", "result": "Passed", "durationSeconds": 1.0, "labels": [], "categories": [], "steps": [] },
                    { "id": "t1", "name": "Search", "result": "Passed", "durationSeconds": 0.4, "labels": [], "categories": [], "steps": [] }
                  ]
                }
              ]
            }
            """);
        return path;
    }

    /// <summary>
    /// What a passing unit-test suite on a current Kronikol looks like: steps with their source
    /// locations, no failure, no tracked call, no annotation — so not one of the four keys the scanner
    /// took as proof of enrichment appears anywhere in the file. The root <c>kronikolVersion</c> says
    /// what wrote it, which is the fact the detector has always had and never read.
    /// </summary>
    private string CurrentGreenReportWithNothingToAttribute()
    {
        var path = Path.Combine(_directory, "CurrentGreen.json");
        File.WriteAllText(path, """
            {
              "kronikolVersion": "3.4.1",
              "formatVersion": 1,
              "suite": "Widgets.Tests",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [
                {
                  "name": "Orders",
                  "labels": [],
                  "scenarios": [
                    {
                      "id": "t0",
                      "stableId": "aaaabbbbccccdddd",
                      "name": "Checkout",
                      "result": "Passed",
                      "durationSeconds": 1.0,
                      "labels": [],
                      "categories": [],
                      "steps": [
                        { "keyword": "Given", "text": "a basket", "status": "Passed", "durationSeconds": 0.1,
                          "sourceFile": "Steps/BasketSteps.cs", "sourceLine": 42, "subSteps": [], "attachments": [] }
                      ],
                      "httpInteractions": [],
                      "attachments": []
                    }
                  ]
                }
              ]
            }
            """);
        return path;
    }

    private string UnenrichedReport()
    {
        if (_unenriched is not null)
            return _unenriched;

        // A report as an older Kronikol wrote it — no stepPath key, no failureMessage, no annotations.
        // Written out literally rather than generated, because the current generator cannot produce the
        // old shape and a simulation of it would not be the thing under test.
        var path = Path.Combine(_directory, "Unenriched.json");
        File.WriteAllText(path, """
            {
              "kronikolVersion": "3.0.44",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [
                {
                  "name": "Orders",
                  "labels": [],
                  "scenarios": [
                    {
                      "id": "old-1",
                      "stableId": "aaaabbbbccccdddd",
                      "name": "Checkout",
                      "result": "Passed",
                      "durationSeconds": 1.0,
                      "isHappyPath": true,
                      "errorMessage": null,
                      "labels": [],
                      "categories": [],
                      "steps": [
                        { "keyword": "Given", "text": "a basket", "status": "Passed", "durationSeconds": 0.1, "subSteps": [], "attachments": [] }
                      ],
                      "backgroundSteps": [],
                      "attachments": [],
                      "httpInteractions": [
                        {
                          "type": "Request",
                          "method": "GET",
                          "uri": "http://api/x",
                          "serviceName": "api",
                          "callerName": "test",
                          "content": "{}",
                          "headers": [],
                          "statusCode": null,
                          "traceId": "00000000-0000-0000-0000-000000000001",
                          "requestResponseId": "00000000-0000-0000-0000-000000000002",
                          "timestamp": "2026-01-01T10:00:01.000Z"
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

        return _unenriched = path;
    }

    private string? _shifted;

    /// <summary>
    /// The same run written a second time with an extra feature that sorts first — every scenario's
    /// ordinal shifts by one while its stableId stays put — and t5's payments total drifted to 4174.
    /// The cross-run <c>diff --body</c> fixture: ordinal matching would land on the wrong scenario.
    /// </summary>
    private string ShiftedReport()
    {
        if (_shifted is not null)
            return _shifted;

        var features = new[]
        {
            new Feature
            {
                DisplayName = "Aardvark",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t9", DisplayName = "A shim scenario", Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "When", Text = "shimming", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        }.Concat(BuildFeatures(allPassing: false)).ToArray();

        return _shifted = Write("Shifted.json", features, BuildLogs(totalDrift: true), BuildDiagrams());
    }

    private string? _manyFailures;

    /// <summary>
    /// Three failing scenarios, which the main fixture cannot supply: it has exactly one, and a footer
    /// that pages can only be wrong about a listing longer than one page.
    /// </summary>
    private string ManyFailuresReport()
    {
        if (_manyFailures is not null)
            return _manyFailures;

        var features = new[]
        {
            new Feature
            {
                DisplayName = "Regressions",
                Scenarios = Enumerable.Range(1, 3).Select(i => new Scenario
                {
                    Id = $"f{i}",
                    DisplayName = $"Regression {i}",
                    Result = ExecutionResult.Failed,
                    Duration = TimeSpan.FromSeconds(0.5),
                    ErrorMessage = $"Assert.Equal() Failure {i}",
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "Then", Text = $"case {i} holds", Status = ExecutionResult.Failed,
                            FailureMessage = $"Expected {i} but found 0",
                            SourceFile = "RegressionTests.cs", SourceLine = 10 + i
                        }
                    ]
                }).ToArray()
            }
        };

        return _manyFailures = Write("ManyFailures.json", features, null);
    }

    private string? _brokerStatuses;

    /// <summary>
    /// One scenario carrying the non-HTTP status labels Kronikol itself stamps, success and failure
    /// side by side, so the error classifier has a fixture that is not all HTTP numbers.
    /// </summary>
    private string BrokerStatusReport()
    {
        if (_brokerStatuses is not null)
            return _brokerStatuses;

        var features = new[]
        {
            new Feature
            {
                DisplayName = "Messaging",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "b0",
                        DisplayName = "An order is placed",
                        Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromSeconds(0.5),
                    }
                ]
            }
        };

        var at = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var logs = new List<RequestResponseLog>();
        logs.AddRange(Pair("b0", "bus", "PUBLISH", "http://bus/orders", "{}", "Sent", at));
        logs.AddRange(Pair("b0", "bus", "CONSUME", "http://bus/orders", "{}", "Ack", at.AddSeconds(1)));
        logs.AddRange(Pair("b0", "bus", "REPLY", "http://bus/orders/reply", "{}", "Responded", at.AddSeconds(2)));
        logs.AddRange(Pair("b0", "cache", "GET", "http://cache/order:9", "{}", "Hit", at.AddSeconds(3)));
        logs.AddRange(Pair("b0", "cache", "GET", "http://cache/order:10", "{}", "Miss", at.AddSeconds(4)));
        logs.AddRange(Pair("b0", "ledger", "COMMIT", "http://ledger/tx", "{}", "Committed", at.AddSeconds(5)));
        logs.AddRange(Pair("b0", "broker", "CONSUME", "http://broker/dead", "{}", "Nack", at.AddSeconds(6)));
        logs.AddRange(Pair("b0", "broker", "SEND", "http://broker/saga", "{}", "Fault", at.AddSeconds(7)));

        return _brokerStatuses = Write("BrokerStatuses.json", features, logs.ToArray());
    }

    private string? _retriedCi;

    /// <summary>A report written by the second attempt of a GitHub Actions run.</summary>
    private string RetriedCiReport()
    {
        if (_retriedCi is not null)
            return _retriedCi;

        var written = ReportGenerator.GenerateTestRunReportData(
            BuildFeatures(allPassing: true),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "RetriedCi_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json,
            ciMetadata: new CiMetadata(CiEnvironment.GitHubActions, "412", "main", "abc1234def5678",
                "https://github.com/acme/shop/actions/runs/99", "acme/shop", "99", "2"));

        var path = Path.Combine(_directory, "RetriedCi.json");
        File.Move(written, path, overwrite: true);
        return _retriedCi = path;
    }

    private string? _wide;

    /// <summary>
    /// A synthesized run at real-report scale — several hundred interactions, a few hundred distinct
    /// bodies — because the repo holds no real large report to validate against. The perf guard for
    /// `values` and `grep --number`: both must finish inside the ordinary test timeout.
    /// </summary>
    private string WideReport()
    {
        if (_wide is not null)
            return _wide;

        var start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var logs = new List<RequestResponseLog>();
        for (var i = 0; i < 300; i++)
            logs.AddRange(Pair("t8", "payments", "POST", "http://payments/charge", $"{{\"attempt\":{i}}}",
                HttpStatusCode.OK, start.AddMilliseconds(i * 3),
                $"{{\"n\":{i % 200},\"status\":\"{(i % 7 == 0 ? "DECLINED" : "APPROVED")}\",\"display\":\"{i % 200:N2}\"}}"));

        var features = new[]
        {
            new Feature
            {
                DisplayName = "Wide",
                Scenarios = [new Scenario { Id = "t8", DisplayName = "A wide scenario", Result = ExecutionResult.Passed }]
            }
        };

        return _wide = Write("Wide.json", features, logs.ToArray(), null);
    }

    [Fact]
    public void Values_completes_on_a_synthesized_wide_run()
    {
        var output = Run("values", WideReport(), "--path", "$.n", "--stats");

        Assert.Contains("count 300", output);
    }

    [Fact]
    public void NumberGrep_completes_on_a_synthesized_wide_run()
    {
        var output = Run("grep", WideReport(), "42", "--number", "--count");

        Assert.True(int.Parse(output.Trim()) > 0);
    }

    private string BigDiagramReport()
    {
        var diagram = "@startuml\n" + string.Join("\n", Enumerable.Range(0, 12000).Select(i => $"note over api : filler line {i} padding padding padding")) + "\n@enduml";
        var body = "{\"filler\":\"" + new string('x', 400_000) + "\"}";

        var features = new[]
        {
            new Feature { DisplayName = "Big", Scenarios = [new Scenario { Id = "big-1", DisplayName = "One big scenario", Result = ExecutionResult.Passed }] }
        };

        var bigArray = "{\"items\":[" + string.Join(",", Enumerable.Range(0, 500).Select(i => $"{{\"sku\":\"s{i}\",\"price\":{i}}}")) + "]}";
        var logs = new[]
        {
            new RequestResponseLog("Big", "big-1", HttpMethod.Post, body, new Uri("http://api/big"), [], "api", "test",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { Timestamp = DateTimeOffset.UtcNow },
            new RequestResponseLog("Big", "big-1", HttpMethod.Post, bigArray, new Uri("http://api/bulk"), [], "api", "test",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { Timestamp = DateTimeOffset.UtcNow.AddSeconds(1) }
        };

        return Write("Big.json", features, logs, [new DefaultDiagramsFetcher.DiagramAsCode("big-1", "Big", diagram)]);
    }

    private string Write(string fileName, Feature[] features, RequestResponseLog[]? logs, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null)
    {
        var written = ReportGenerator.GenerateTestRunReportData(
            features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Query_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, diagrams, logs);

        var path = Path.Combine(_directory, fileName);
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static Feature[] BuildFeatures(bool allPassing) =>
    [
        new Feature
        {
            DisplayName = "Catalogue",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t0", DisplayName = "Browse the catalogue", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1.2),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "browsing", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "t1", DisplayName = "Search the catalogue", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(0.4),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "searching", Status = ExecutionResult.Passed }]
                }
            ]
        },
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t2",
                    DisplayName = "Checkout fails on a wrong total",
                    Result = allPassing ? ExecutionResult.Passed : ExecutionResult.Failed,
                    Duration = TimeSpan.FromSeconds(3.5),
                    ErrorMessage = allPassing ? null : "Assert.Equal() Failure",
                    Steps =
                    [
                        new ScenarioStep { Keyword = "Given", Text = "a basket", Status = ExecutionResult.Passed },
                        new ScenarioStep
                        {
                            Keyword = "Then", Text = "the total is right",
                            Status = allPassing ? ExecutionResult.Passed : ExecutionResult.Failed,
                            FailureMessage = allPassing ? null : "Expected 4173 but found 3902",
                            SourceFile = "OverviewTests.cs", SourceLine = 142,
                            SubSteps =
                            [
                                new ScenarioStep
                                {
                                    Text = "total == 4173",
                                    Status = allPassing ? ExecutionResult.Passed : ExecutionResult.Failed,
                                    FailureMessage = allPassing ? null : "Expected 4173 but found 3902",
                                    SourceFile = "OverviewTests.cs", SourceLine = 142
                                }
                            ]
                        }
                    ]
                }
            ]
        },
        new Feature
        {
            DisplayName = "Payments",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t3", DisplayName = "Charges succeed", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(0.9),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "charging", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "t4", DisplayName = "Parallel charges", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(0.2),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "charging twice", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "t5", DisplayName = "Receipt checkout passes", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(0.3),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "checking out", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "t6", DisplayName = "Receipt checkout variant", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(0.3),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "checking out", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    /// <summary>The response body every path-grammar test aims at: arrays, a formatted number, a null, a dotted key.</summary>
    private const string RichBody =
        """{"items":[{"sku":"a","price":12.5},{"sku":"b","price":1250},{"sku":"c","price":-3}],"total":4173,"display":"4,173.00","euDisplay":"4.173,00","region":null,"status":"APPROVED","flags":{"feature.x":true}}""";

    /// <summary>A W3C trace id that stays inside one scenario (t3) — the healthy case.</summary>
    private const string ChainTrace = "4bf92f3577b34da6a3ce929d0e0e4736";

    /// <summary>
    /// A W3C trace id shared across t4 and t5 — the fixture-leakage smell `trace` warns about. Shares its
    /// first 8 hex with <see cref="ChainTrace"/> so a short prefix is genuinely ambiguous.
    /// </summary>
    private const string LeakedTrace = "4bf92f35feedfacefeedfacefeedface";

    private static RequestResponseLog[] BuildLogs(bool totalDrift = false)
    {
        var logs = new List<RequestResponseLog>();
        var start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        // A scenario whose calls repeat, so grouping and paging have something real to fold.
        logs.Add(Marker("t0", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>browsing"));
        for (var i = 0; i < 8; i++)
            logs.AddRange(Pair("t0", "redis", "GET", "http://redis/catalogue:v1:page", "{\"page\":1}", HttpStatusCode.OK, start.AddMilliseconds(i * 10)));
        logs.AddRange(Pair("t0", "api", "GET", "http://api/catalogue", "{\"customerReference\":\"abc\"}", HttpStatusCode.OK, start.AddSeconds(1)));
        logs.Add(new RequestResponseLog("Catalogue", "t0", HttpMethod.Get, "start of a body\n\n…truncated (900000 chars total)",
            new Uri("http://api/huge"), [], "api", "test", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { Timestamp = start.AddSeconds(2) });

        logs.Add(Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>searching"));
        logs.AddRange(Pair("t1", "search", "POST", "http://search/query", "{\"q\":\"muffin\"}", HttpStatusCode.OK, start.AddSeconds(3)));

        logs.Add(Marker("t2", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a basket"));
        logs.Add(Marker("t2", DiagramMarkerKind.Row, "hnote across #lightyellow : Row 3"));
        logs.AddRange(Pair("t2", "payments", "POST", "http://payments/charge", "{\"amount\":4173,\"currency\":\"GBP\"}",
            HttpStatusCode.InternalServerError, start.AddSeconds(4), "{\"total\":3902,\"currency\":\"GBP\"}"));
        logs.Add(Marker("t2", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>the total is right"));
        logs.AddRange(Pair("t2", "api", "GET", "http://api/order/9", "{\"customerReference\":\"abc\"}", HttpStatusCode.OK, start.AddSeconds(5)));

        // ── t3: the aggregation scenario — repeated values, an absent field, a rich body, events,
        //        non-JSON, and every flavour of status the error classifier has to agree on.
        var t3 = start.AddSeconds(10);
        for (var i = 0; i < 3; i++)
            logs.AddRange(Pair("t3", "payments", "POST", "http://payments/charge", "{\"amount\":100}",
                HttpStatusCode.OK, t3.AddMilliseconds(i * 50), "{\"status\":\"APPROVED\",\"total\":100}"));
        logs.AddRange(Pair("t3", "payments", "POST", "http://payments/charge", "{\"amount\":50}",
            HttpStatusCode.OK, t3.AddMilliseconds(500), "{\"status\":\"DECLINED\",\"total\":50}",
            w3cTraceId: ChainTrace, spanId: "00f067aa00f067aa"));
        logs.AddRange(Pair("t3", "payments", "POST", "http://payments/charge", "{\"amount\":12.5}",
            HttpStatusCode.OK, t3.AddMilliseconds(600), "{\"total\":12.5}"));
        logs.AddRange(Pair("t3", "payments", "POST", "http://payments/summary", "{\"basket\":9}",
            HttpStatusCode.OK, t3.AddMilliseconds(800), RichBody,
            w3cTraceId: ChainTrace, spanId: "a1b2c3d4e5f60718"));
        logs.Add(RequestOnly("t3", "bus", "http://bus/publish", "{\"event\":\"charge.requested\"}",
            t3.AddSeconds(2), RequestResponseMetaType.Event));
        logs.AddRange(Pair("t3", "bus", "POST", "http://bus/settle", "{\"event\":\"charge.settled\"}",
            HttpStatusCode.OK, t3.AddSeconds(3), "{\"ack\":true}", metaType: RequestResponseMetaType.Event));
        logs.AddRange(Pair("t3", "printer", "POST", "http://printer/receipt", "print receipt please",
            HttpStatusCode.OK, t3.AddSeconds(4), "receipt\ntotal 4,173.00\nthanks"));
        logs.AddRange(Pair("t3", "payments", "POST", "http://payments/preauth", "{\"amount\":9}",
            HttpStatusCode.Created, t3.AddSeconds(5), "{\"id\":9}"));
        logs.AddRange(Pair("t3", "payments", "DELETE", "http://payments/hold/1", "{\"hold\":1}",
            HttpStatusCode.NoContent, t3.AddSeconds(6), responseBody: null));
        logs.AddRange(Pair("t3", "orders-db", "QUERY", "http://orders-db/orders", "{\"sql\":\"select 1\"}",
            "ERROR", t3.AddSeconds(7), "{\"error\":\"deadlock\"}"));

        // ── t4: two interleaved calls to the same service with different statuses — the exact-pairing
        //        fixture. File order is reqA, reqB, respB, respA, so proximity attaches the wrong one.
        var t4 = start.AddSeconds(20);
        var interleavedA = Pair("t4", "payments", "POST", "http://payments/charge", "{\"attempt\":1}",
            HttpStatusCode.OK, t4, "{\"status\":\"APPROVED\"}", w3cTraceId: LeakedTrace, spanId: "1111222233334444");
        var interleavedB = Pair("t4", "payments", "POST", "http://payments/charge", "{\"attempt\":2}",
            HttpStatusCode.InternalServerError, t4.AddMilliseconds(5), "{\"status\":\"DECLINED\"}");
        logs.Add(interleavedA[0]);
        logs.Add(interleavedB[0]);
        logs.Add(interleavedB[1]);
        logs.Add(interleavedA[1]);
        logs.AddRange(Pair("t4", "legacy", "GET", "http://legacy/ping", "{}",
            HttpStatusCode.OK, t4.AddSeconds(1), "{\"pong\":true}", pairId: Guid.Empty));

        // ── t5/t6: near-identical paired bodies differing in a few paths — the body-diff fixture —
        //        plus a non-JSON pair for the line-diff fallback.
        var t5 = start.AddSeconds(30);
        logs.AddRange(Pair("t5", "payments", "POST", "http://payments/charge", "{\"basket\":1}",
            HttpStatusCode.OK, t5, "{\"customer\":{\"region\":\"EU\"},\"items\":[{\"sku\":\"a\",\"price\":12.5},{\"sku\":\"b\",\"price\":3}],\"total\":" + (totalDrift ? "4174" : "4173") + "}",
            w3cTraceId: LeakedTrace, spanId: "5555666677778888"));
        logs.AddRange(Pair("t5", "printer", "POST", "http://printer/receipt", "print receipt please",
            HttpStatusCode.OK, t5.AddSeconds(1), "receipt\ntotal: 4173"));
        logs.AddRange(Pair("t5", "catalog", "GET", "http://catalog/tags", "{}",
            HttpStatusCode.OK, t5.AddSeconds(2), "{\"tags\":[\"a\",\"b\",\"c\",\"d\",\"e\"]}"));
        var t6 = start.AddSeconds(40);
        logs.AddRange(Pair("t6", "payments", "POST", "http://payments/charge", "{\"basket\":1}",
            HttpStatusCode.OK, t6, "{\"customer\":{\"region\":null},\"items\":[{\"sku\":\"a\",\"price\":1250},{\"sku\":\"b\",\"price\":3},{\"sku\":\"c\",\"price\":9}],\"total\":3902}"));
        logs.AddRange(Pair("t6", "printer", "POST", "http://printer/receipt", "print receipt please",
            HttpStatusCode.OK, t6.AddSeconds(1), "receipt\ntotal: 3902"));
        logs.AddRange(Pair("t6", "catalog", "GET", "http://catalog/tags", "{}",
            HttpStatusCode.OK, t6.AddSeconds(2), "{\"tags\":[\"z\",\"a\",\"b\",\"c\",\"d\",\"e\"]}"));

        return logs.ToArray();
    }

    private static DefaultDiagramsFetcher.DiagramAsCode[] BuildDiagrams() =>
    [
        new("t0", "Catalogue", """
                               @startuml
                               participant redis
                               note over redis : catalogue page 1 loaded from cache
                               note over redis
                               {
                                 "page": 1
                               }
                               end note
                               @enduml
                               """),
        new("t2", "Orders", """
                            @startuml
                            participant payments
                            note over payments : charge rejected
                            @enduml
                            """)
    ];

    private static RequestResponseLog Marker(string testId, DiagramMarkerKind kind, string plantUml) =>
        new(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsOverrideStart = true, PlantUml = plantUml, MarkerKind = kind };

    private static RequestResponseLog[] Pair(string testId, string service, OneOf<HttpMethod, string> method, string uri, string requestBody,
        OneOf<HttpStatusCode, string> status, DateTimeOffset at, string? responseBody = "{\"ok\":true}",
        Guid? pairId = null, string? w3cTraceId = null, string? spanId = null, RequestResponseMetaType metaType = default)
    {
        var id = pairId ?? Guid.NewGuid();
        var traceId = Guid.NewGuid();
        return
        [
            new RequestResponseLog(testId, testId, method, requestBody, new Uri(uri), [("accept", "application/json")],
                service, "test", RequestResponseType.Request, traceId, id, false, MetaType: metaType)
            { Timestamp = at, DependencyCategory = service == "redis" ? "cache" : null, ActivityTraceId = w3cTraceId, ActivitySpanId = spanId },
            new RequestResponseLog(testId, testId, method, responseBody, new Uri(uri), [],
                service, "test", RequestResponseType.Response, traceId, id, false, status, MetaType: metaType)
            { Timestamp = at.AddMilliseconds(35) }
        ];
    }

    /// <summary>A fire-and-forget half: a request (or event) that never gets a response entry.</summary>
    private static RequestResponseLog RequestOnly(string testId, string service, string uri, string body, DateTimeOffset at,
        RequestResponseMetaType metaType = default) =>
        new(testId, testId, "POST", body, new Uri(uri), [], service, "test",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false, MetaType: metaType)
        { Timestamp = at };
}
