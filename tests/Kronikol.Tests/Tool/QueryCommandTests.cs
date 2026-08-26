using System.Net;
using System.Text;
using Kronikol.Reports;
using Kronikol.Tool;
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
        Assert.True(Encoding.UTF8.GetByteCount(output) < 300);
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
    public void Grouping_collapses_repeated_calls_into_one_row()
    {
        var ungrouped = Run("interactions", Report(), "s0", "--limit", "500");
        var grouped = Run("interactions", Report(), "s0", "--group", "--limit", "500");

        Assert.True(grouped.Split('\n').Length < ungrouped.Split('\n').Length);
        Assert.Contains("×", grouped);
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
        """{"items":[{"sku":"a","price":12.5},{"sku":"b","price":1250},{"sku":"c","price":-3}],"total":4173,"display":"4,173.00","region":null,"status":"APPROVED","flags":{"feature.x":true}}""";

    /// <summary>A W3C trace id that stays inside one scenario (t3) — the healthy case.</summary>
    private const string ChainTrace = "4bf92f3577b34da6a3ce929d0e0e4736";

    /// <summary>A W3C trace id shared across t4 and t5 — the fixture-leakage smell `trace` warns about.</summary>
    private const string LeakedTrace = "feedfacefeedfacefeedfacefeedface";

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
