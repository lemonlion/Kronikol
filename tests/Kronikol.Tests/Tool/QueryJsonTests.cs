using System.Net;
using System.Text;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tool.Query;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The contract <c>kronikol query --json</c> makes with a script, which is a different reader from the one
/// the text path serves. A terminal reader can skim past a warning, infer that an empty listing means
/// nothing matched, and notice that a resume pointer is going in circles. A script can do none of that, so
/// the envelope has to say all three, and every fact here is about something that would otherwise be
/// implied by layout.
///
/// <para>Text stays the default and stays byte-identical: the facts that pin that live in
/// <c>QueryCommandTests</c>, and this class exists beside them rather than inside them so that a change
/// which forks the two formats fails in a file whose name says which one broke.</para>
/// </summary>
public class QueryJsonTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-json").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── The envelope ──────────────────────────────────────────

    [Fact]
    public void Every_supported_verb_answers_with_one_well_formed_envelope()
    {
        foreach (var (command, args) in Verbs())
        {
            var output = Run(command, args);

            // Parses, and is ONE object - not a stream, not an array, not text with an object in it.
            var envelope = JsonDocument.Parse(output).RootElement;
            Assert.Equal(JsonValueKind.Object, envelope.ValueKind);

            Assert.Equal(1, envelope.GetProperty("formatVersion").GetInt32());
            Assert.Equal(command, envelope.GetProperty("command").GetString());
            Assert.EndsWith(".json", envelope.GetProperty("report").GetString());
            Assert.Equal(JsonValueKind.Array, envelope.GetProperty("notes").ValueKind);
            Assert.Equal(JsonValueKind.Array, envelope.GetProperty("items").ValueKind);
            Assert.Equal(JsonValueKind.Null, envelope.GetProperty("truncated").ValueKind);
            Assert.Equal(JsonValueKind.Null, envelope.GetProperty("next").ValueKind);
        }
    }

    [Fact]
    public void Every_supported_verb_projects_its_rows_rather_than_wrapping_rendered_text()
    {
        // QueryWriter falls back to {"text": "<the rendered row>"} when a verb pages without a projector,
        // which keeps an un-converted verb honest rather than silently empty. That fallback must never be
        // what ships: an array of terminal rows is the thing --json exists to stop handing to a script.
        foreach (var (command, args) in Verbs())
        {
            foreach (var item in JsonDocument.Parse(Run(command, args)).RootElement.GetProperty("items").EnumerateArray())
            {
                Assert.Equal(JsonValueKind.Object, item.ValueKind);
                Assert.False(item.TryGetProperty("text", out _),
                    $"{command} fell back to wrapping rendered text - it is missing a projector");
            }
        }
    }

    [Fact]
    public void The_kronikol_version_is_present_and_null_rather_than_absent_when_the_report_has_none()
    {
        // A consumer must be able to read the key unconditionally. Branching on key presence is the bug
        // this prevents, and an older or hand-written report is exactly where it would bite.
        var envelope = JsonDocument.Parse(Run("summary")).RootElement;

        Assert.True(envelope.TryGetProperty("kronikolVersion", out var version));
        Assert.True(version.ValueKind is JsonValueKind.String or JsonValueKind.Null);
    }

    [Fact]
    public void A_verb_without_an_object_form_says_so_instead_of_answering_with_an_empty_envelope()
    {
        var (output, error, exit) = RunFull("steps", Report(), "s0", "--json");

        Assert.Equal(2, exit);
        Assert.Contains("--json is not available on 'steps'", error);
        foreach (var supported in QueryCommand.JsonCommands)
            Assert.Contains(supported, error, StringComparison.Ordinal);

        // The refusal is itself an envelope. `items` is empty rather than absent, so a consumer's loop is
        // safe on both paths and the failure shows up where it is checked for.
        var envelope = JsonDocument.Parse(output).RootElement;
        Assert.Equal(2, envelope.GetProperty("error").GetProperty("exitCode").GetInt32());
        Assert.Equal(0, envelope.GetProperty("items").GetArrayLength());
    }

    /// <summary>
    /// The single thing that decides whether a machine channel is usable. Before 3.1.0 a non-zero exit
    /// printed prose on stderr and <b>nothing at all</b> on stdout - the whole buffered answer, provenance
    /// banners included, was discarded - so a wrapper got valid JSON when the call worked and an empty
    /// string when it did not, which is the one path it most needs to handle.
    /// </summary>
    [Fact]
    public void A_failure_answers_with_an_envelope_too_rather_than_an_empty_stdout()
    {
        var (output, error, exit) = RunFull("scenarios", Report(), "--json", "--sort", "nonsense");

        Assert.Equal(2, exit);
        // The prose is unchanged and still goes to stderr, where the rest of the tool's failures go.
        Assert.Contains("cannot sort", error);

        var envelope = JsonDocument.Parse(output).RootElement;
        Assert.Equal(1, envelope.GetProperty("formatVersion").GetInt32());
        Assert.Equal("scenarios", envelope.GetProperty("command").GetString());

        var failure = envelope.GetProperty("error");
        Assert.Equal(2, failure.GetProperty("exitCode").GetInt32());
        Assert.Contains("cannot sort", failure.GetProperty("message").GetString());
        // Everything the tool said after the first line is remediation, and it is the half a caller can act on.
        Assert.Contains("--sort", failure.GetProperty("hint").GetString());
    }

    [Fact]
    public void A_successful_answer_carries_error_present_and_null()
    {
        // Present-and-null, not absent - the same rule `kronikolVersion` and `total` follow. A consumer
        // branching on key PRESENCE cannot tell "this call succeeded" from "an older tool wrote this",
        // and one shape answering both paths is the whole point of the envelope.
        var envelope = JsonDocument.Parse(Run("summary")).RootElement;

        Assert.True(envelope.TryGetProperty("error", out var error));
        Assert.Equal(JsonValueKind.Null, error.ValueKind);
    }

    [Fact]
    public void A_failure_before_the_report_is_resolved_still_answers_with_an_envelope()
    {
        // The earliest possible failure: the file does not exist, so there is no report, no version and no
        // scan. The envelope still has to be well formed, with nulls where the facts are unknown.
        var (output, _, exit) = RunFull("summary", "./no-such-report.json", "--json");

        Assert.Equal(2, exit);
        var envelope = JsonDocument.Parse(output).RootElement;
        Assert.Equal(JsonValueKind.Null, envelope.GetProperty("report").ValueKind);
        Assert.Equal(JsonValueKind.Null, envelope.GetProperty("kronikolVersion").ValueKind);
        Assert.Contains("No such file", envelope.GetProperty("error").GetProperty("message").GetString());
    }

    // ─── Notes carry what layout used to ───────────────────────

    [Fact]
    public void The_negative_answer_is_a_note_rather_than_an_empty_array_with_no_explanation()
    {
        // `services` exists to answer "was this called at all?". As text that is a sentence; as JSON an
        // empty items[] alone cannot tell "nothing was captured" from "the filter excluded everything".
        var envelope = JsonDocument.Parse(Run("services", ["s1"])).RootElement;

        Assert.Empty(envelope.GetProperty("items").EnumerateArray());
        Assert.Contains("no services were called", Notes(envelope));
    }

    [Fact]
    public void A_provenance_banner_becomes_a_note_and_never_a_stray_line_of_text()
    {
        var output = Run("summary", report: UnenrichedReport());

        // Parses at all - which it would not if the banner had been written before the opening brace.
        var envelope = JsonDocument.Parse(output).RootElement;
        Assert.Contains(Notes(envelope), n => n.Contains("predates step attribution", StringComparison.Ordinal));
    }

    // ─── Paging a script can actually follow ───────────────────

    [Fact]
    public void Next_is_a_whole_command_and_carries_the_format_it_was_asked_in()
    {
        var envelope = JsonDocument.Parse(Run("scenarios", ["--limit", "2"])).RootElement;
        var argv = envelope.GetProperty("next").EnumerateArray().Select(a => a.GetString()!).ToArray();

        // The verb and the report are in it, so a consumer needs to remember nothing about the call
        // it is resuming.
        Assert.Equal("query", argv[0]);
        Assert.Equal("scenarios", argv[1]);
        Assert.EndsWith(".json", argv[2], StringComparison.Ordinal);
        // Without this a script's second page comes back as text and its parser dies on page two.
        Assert.Contains("--json", argv);
        Assert.Equal("--offset", argv[^2]);
        Assert.Equal("2", argv[^1]);
        Assert.Equal(7, envelope.GetProperty("total").GetInt32());
    }

    [Fact]
    public void Following_next_to_the_end_terminates()
    {
        // The loop a consumer will actually write. Before the paging fix this did not terminate: a page
        // that rendered nothing answered with the offset it had been given.
        var seen = new List<string>();
        string[] args = ["scenarios", Report(), "--limit", "2", "--json"];
        var guard = 0;

        while (true)
        {
            Assert.True(++guard < 20, "next never became null - the resume pointer is not advancing");

            var envelope = JsonDocument.Parse(RunArgv(args)).RootElement;
            seen.AddRange(envelope.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("address").GetString()!));

            if (envelope.GetProperty("next").ValueKind is JsonValueKind.Null)
                break;

            // Handed back whole. The page size rides along, so page two is the same width as page one.
            args = envelope.GetProperty("next").EnumerateArray().Select(a => a.GetString()!).Skip(1).ToArray();
        }

        Assert.Equal(7, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public void Next_is_an_argument_vector_so_a_value_with_a_space_cannot_break_it()
    {
        // A service name with a space in it is ordinary. `next` was one interpolated string, so it came
        // back as `--service Dessert Provider --offset 3`; a consumer splitting it on whitespace passed
        // `Provider` as a positional and the tool answered `Not an address: Provider`. No quoting
        // convention fixes that for every consumer — an argument vector cannot express the bug at all.
        var envelope = JsonDocument.Parse(Run("interactions", ["--service", "Dessert Provider", "--limit", "1"])).RootElement;
        var next = envelope.GetProperty("next");

        Assert.Equal(JsonValueKind.Array, next.ValueKind);
        var argv = next.EnumerateArray().Select(a => a.GetString()!).ToArray();

        Assert.Contains("Dessert Provider", argv);   // one element, not two
        Assert.Equal("query", argv[0]);
        Assert.Equal("interactions", argv[1]);
        Assert.Contains("--json", argv);
        Assert.Equal("--offset", argv[^2]);
        Assert.Equal("1", argv[^1]);
    }

    [Fact]
    public void The_argument_vector_runs_verbatim_and_reaches_the_end()
    {
        // The whole contract in one loop: hand `next` straight back to the tool — unsplit, unquoted, and
        // without remembering anything about the first call — and every row arrives exactly once.
        var seen = new List<string>();
        string[] args = ["interactions", Report(), "--service", "Dessert Provider", "--limit", "1", "--json"];
        var guard = 0;

        while (true)
        {
            Assert.True(++guard < 20, "next never became null — the resume pointer is not advancing");

            var envelope = JsonDocument.Parse(RunArgv(args)).RootElement;
            seen.AddRange(envelope.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("address").GetString()!));

            var next = envelope.GetProperty("next");
            if (next.ValueKind is JsonValueKind.Null)
                break;

            // Verbatim, minus the argv[0] the dispatcher strips.
            args = next.EnumerateArray().Select(a => a.GetString()!).Skip(1).ToArray();
        }

        Assert.Equal(3, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public void A_text_footer_quotes_a_value_that_would_not_survive_being_pasted_back()
    {
        // The text twin: `next:` is for a human to paste, so the space has to be quoted rather than
        // separated. Both formats read the same tokens, which is why they cannot disagree.
        var (output, _, exit) = RunFull("interactions", Report(), "--service", "Dessert Provider", "--limit", "1");

        Assert.Equal(0, exit);
        Assert.Contains("next: --service \"Dessert Provider\" --offset 1", output);
    }

    [Fact]
    public void The_argument_vector_keeps_the_address_it_was_scoped_to()
    {
        // `interactions s0` is scoped to one scenario, and the scope is positional rather than a flag.
        // An argv rebuilt from the flags alone would resume across the whole report — the same envelope
        // shape, a silently wider answer, and nothing a consumer could notice.
        var envelope = JsonDocument.Parse(Run("interactions", ["s0", "--limit", "1"])).RootElement;
        var argv = envelope.GetProperty("next").EnumerateArray().Select(a => a.GetString()!).ToArray();

        Assert.Equal("s0", argv[3]);

        var resumed = JsonDocument.Parse(RunArgv(argv.Skip(1).ToArray())).RootElement;
        Assert.NotEmpty(resumed.GetProperty("items").EnumerateArray());
        foreach (var item in resumed.GetProperty("items").EnumerateArray())
            Assert.StartsWith("s0/", item.GetProperty("address").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_too_large_for_the_budget_gives_no_next_and_says_why()
    {
        var envelope = JsonDocument.Parse(Run("scenarios", ["--max-bytes", "40"])).RootElement;

        // An object, not a bool: a consumer learning that rows were dropped and never how many is the
        // silent-skip defect re-encoded rather than fixed.
        var truncated = envelope.GetProperty("truncated");
        Assert.Equal(40, truncated.GetProperty("limitBytes").GetInt32());
        Assert.True(truncated.GetProperty("droppedItems").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Null, envelope.GetProperty("next").ValueKind);
        Assert.Contains(Notes(envelope), n => n.Contains("raise --max-bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void A_truncated_envelope_is_still_one_parseable_object()
    {
        // The reason items are costed whole. Half a serialized row is not JSON, and `truncated: true`
        // inside a cut-off array would be unreachable to the consumer that most needs to read it.
        var output = Run("interactions", ["--max-bytes", "300"]);
        var envelope = JsonDocument.Parse(output).RootElement;

        Assert.True(envelope.GetProperty("truncated").GetProperty("droppedItems").GetInt32() > 0);
        foreach (var item in envelope.GetProperty("items").EnumerateArray())
            Assert.Equal(JsonValueKind.Object, item.ValueKind);
    }

    [Fact]
    public void An_offset_past_the_end_is_an_empty_page_with_no_next()
    {
        var envelope = JsonDocument.Parse(Run("scenarios", ["--offset", "99"])).RootElement;

        Assert.Empty(envelope.GetProperty("items").EnumerateArray());
        Assert.Equal(7, envelope.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, envelope.GetProperty("next").ValueKind);
    }

    // ─── --count ───────────────────────────────────────────────

    [Fact]
    public void Count_answers_with_a_number_and_no_items_to_iterate()
    {
        var envelope = JsonDocument.Parse(Run("scenarios", ["--result", "Failed", "--count"])).RootElement;

        Assert.Equal(1, envelope.GetProperty("count").GetInt32());
        Assert.False(envelope.TryGetProperty("items", out _), "an empty items[] beside count invites a script to iterate it");
        Assert.False(envelope.TryGetProperty("total", out _));
        Assert.Equal(JsonValueKind.Null, envelope.GetProperty("next").ValueKind);
    }

    // ─── Per-verb shapes ───────────────────────────────────────

    [Fact]
    public void Summary_keeps_its_five_sections_as_named_members_rather_than_one_flattened_list()
    {
        var envelope = JsonDocument.Parse(Run("summary")).RootElement;

        var run = envelope.GetProperty("run");
        Assert.Equal(7, run.GetProperty("scenarios").GetInt32());
        Assert.Equal(1, run.GetProperty("failed").GetInt32());
        Assert.True(run.GetProperty("sizeBytes").GetInt64() > 0);

        // items are the per-feature rows: the one repeated thing summary has.
        Assert.Contains(envelope.GetProperty("items").EnumerateArray(),
            f => f.GetProperty("feature").GetString() == "Orders" && f.GetProperty("failed").GetInt32() == 1);

        Assert.Equal(1, envelope.GetProperty("failedTotal").GetInt32());
        Assert.NotEmpty(envelope.GetProperty("slowest").EnumerateArray());
    }

    [Fact]
    public void Services_exposes_the_status_mix_as_a_map_rather_than_the_rendered_column()
    {
        var payments = JsonDocument.Parse(Run("services")).RootElement.GetProperty("items")
            .EnumerateArray().Single(s => s.GetProperty("service").GetString() == "payments");

        Assert.True(payments.GetProperty("calls").GetInt32() > 0);
        Assert.Equal(2, payments.GetProperty("errors").GetInt32());
        // "OK×412 InternalServerError×1" is a column, not a fact. A consumer must be able to count.
        Assert.Equal(JsonValueKind.Object, payments.GetProperty("statuses").ValueKind);
        Assert.NotEmpty(payments.GetProperty("statuses").EnumerateObject());
    }

    [Fact]
    public void Failures_uses_the_field_names_the_run_already_wrote_into_Failures_jsonl()
    {
        // One concept, one shape. A consumer that read the digest the run emitted must not have to learn
        // a second vocabulary to read the same failure back out of the tool.
        var failure = JsonDocument.Parse(Run("failures")).RootElement.GetProperty("items").EnumerateArray().First();

        foreach (var field in new[]
                 {
                     "address", "stableId", "feature", "scenario", "exampleValues", "durationSeconds",
                     "errorMessage", "sourceFile", "sourceLine", "failingSteps", "attachments"
                 })
            Assert.True(failure.TryGetProperty(field, out _), $"failures item has no {field}");

        var step = failure.GetProperty("failingSteps").EnumerateArray().First();
        Assert.StartsWith("s2/", step.GetProperty("path").GetString());
        Assert.Contains("4173", step.GetProperty("message").GetString()!);
    }

    [Fact]
    public void Grouped_interactions_key_their_buckets_by_dimension_name()
    {
        // The bucket holds its values positionally against --group-by. Handing a script ["payments","500"]
        // makes it re-derive the header the text path prints and JSON suppresses.
        var bucket = JsonDocument.Parse(Run("interactions", ["--group-by", "service,status"]))
            .RootElement.GetProperty("items").EnumerateArray().First();

        var key = bucket.GetProperty("key");
        Assert.True(key.TryGetProperty("service", out _));
        Assert.True(key.TryGetProperty("status", out _));
        Assert.True(bucket.GetProperty("calls").GetInt32() > 0);
    }

    [Fact]
    public void A_folded_group_is_a_record_with_a_count_rather_than_a_rendered_row()
    {
        var groups = JsonDocument.Parse(Run("interactions", ["s0", "--group"]))
            .RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.NotEmpty(groups);
        foreach (var group in groups)
        {
            Assert.NotNull(group.GetProperty("address").GetString());
            Assert.True(group.GetProperty("count").GetInt32() >= 1);
        }
    }

    [Fact]
    public void A_run_diff_is_one_flat_list_keyed_by_what_kind_of_change_each_row_is()
    {
        // Old first, new second - `kronikol query diff <old> <new>` - or every regression reads as a fix.
        var older = Report(allPassing: true, fileName: "Old.json");
        var envelope = JsonDocument.Parse(Run("diff", [Report()], report: older)).RootElement;

        var kinds = envelope.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("kind").GetString()).ToArray();

        Assert.Contains("broke", kinds);
        Assert.All(envelope.GetProperty("items").EnumerateArray(),
            i => Assert.False(string.IsNullOrEmpty(i.GetProperty("address").GetString())));

        // The two sides, because the envelope's own `report` can only name one of them.
        Assert.EndsWith("Old.json", envelope.GetProperty("left").GetProperty("report").GetString());
        Assert.EndsWith("TestRunReport.json", envelope.GetProperty("right").GetProperty("report").GetString());
        Assert.Equal(0, envelope.GetProperty("left").GetProperty("failed").GetInt32());
        Assert.Equal(1, envelope.GetProperty("right").GetProperty("failed").GetInt32());
    }

    [Fact]
    public void A_body_diff_says_byte_identical_rather_than_leaving_an_empty_array_to_interpret()
    {
        var envelope = JsonDocument.Parse(Run("diff", [RequestAddress("s5"), RequestAddress("s5")])).RootElement;

        Assert.Empty(envelope.GetProperty("items").EnumerateArray());
        Assert.True(envelope.GetProperty("byteIdentical").GetBoolean());
    }

    [Fact]
    public void A_body_diff_row_is_a_path_and_its_two_sides()
    {
        var items = JsonDocument.Parse(Run("diff", [RequestAddress("s5"), RequestAddress("s6")]))
            .RootElement.GetProperty("items").EnumerateArray().ToArray();

        var total = items.Single(i => i.GetProperty("path").GetString() == "$.total");
        Assert.Equal("changed", total.GetProperty("kind").GetString());
        Assert.Equal("4173", total.GetProperty("before").GetString());
        Assert.Equal("3902", total.GetProperty("after").GetString());

        Assert.Contains(items, i => i.GetProperty("kind").GetString() == "added");
    }

    // ─── The payload invariant, in the format that can break it ─

    [Theory]
    [InlineData("summary")]
    [InlineData("scenarios")]
    [InlineData("failures")]
    [InlineData("services")]
    [InlineData("interactions")]
    [InlineData("assertions")]
    public void No_listing_verb_leaks_a_payload_into_its_envelope(string command)
    {
        // The core invariant, asserted here because a projector is exactly how it would be broken by
        // accident: serialize an InteractionEntry wholesale and a captured body ships inside items[]
        // with nothing red. The text facts that guard the same thing cannot see this path at all.
        var output = Run(command, command == "interactions" ? [] : []);

        Assert.DoesNotContain("customerReference", output, StringComparison.Ordinal);
        Assert.DoesNotContain("@startuml", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sku\"", output, StringComparison.Ordinal);
    }

    // ─── Budget and --out ──────────────────────────────────────

    [Fact]
    public void Every_supported_verb_stays_under_the_budget_in_json_too()
    {
        // The text loop has had this fact for a long time; without its twin the whole --json surface
        // could go unbudgeted with nothing red - the silent-weakening class this repo keeps meeting.
        foreach (var (command, args) in Verbs())
        {
            var bytes = Encoding.UTF8.GetByteCount(Run(command, args));
            Assert.True(bytes <= 6400, $"{command} --json produced {bytes} bytes");
        }
    }

    [Fact]
    public void Out_writes_the_envelope_to_the_file_and_prints_one_line()
    {
        var path = Path.Combine(_directory, "envelope.json");
        var output = Run("scenarios", ["--out", path]);

        Assert.Contains("wrote ", output);
        Assert.Contains(path, output);
        Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        var envelope = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        Assert.Equal(7, envelope.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Out_lifts_the_budget_because_a_file_is_not_a_context_window()
    {
        var path = Path.Combine(_directory, "big.json");
        Run("interactions", ["--out", path, "--max-bytes", "200"]);

        var envelope = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        Assert.Equal(JsonValueKind.Null, envelope.GetProperty("truncated").ValueKind);
        Assert.True(envelope.GetProperty("items").GetArrayLength() > 1);
    }

    [Fact]
    public void Out_on_a_text_listing_writes_the_text_and_no_longer_ignores_the_flag()
    {
        // It used to be parsed and silently discarded on every verb but the four payload ones - shipping
        // a machine-readable format while `--out F` did nothing was the trap worth closing with it.
        var path = Path.Combine(_directory, "listing.txt");
        var (output, error, exit) = RunFull("scenarios", Report(), "--out", path);

        Assert.True(exit == 0, error);
        Assert.Contains("wrote ", output);
        Assert.Contains("Browse the catalogue", File.ReadAllText(path));
    }

    [Fact]
    public void A_payload_verb_still_writes_its_payload_to_out_rather_than_one_line_about_it()
    {
        // http, body, note and diagram handle --out themselves. A second writer aiming at the same file
        // would overwrite the payload with the sentence announcing it.
        var path = Path.Combine(_directory, "payload.json");
        Run2("body", Report(), BodyHashOf(RequestAddress("s5")), "--out", path);

        // The body s5 sent, not the sentence announcing that it was written.
        var written = File.ReadAllText(path);
        Assert.Contains("\"region\"", written);
        Assert.DoesNotContain("wrote ", written);
    }

    // ─── The rerun prefix keeps text footers untouched ─────────

    [Fact]
    public void The_rerun_prefix_is_unchanged_when_json_was_not_asked_for()
    {
        var error = new StringWriter();
        var text = QueryOptions.Parse(["r.json", "--service", "payments"], error);
        var json = QueryOptions.Parse(["r.json", "--service", "payments", "--json"], error);

        Assert.NotNull(text);
        Assert.NotNull(json);
        Assert.Equal("--service payments ", text.RerunPrefix());
        Assert.Equal("--service payments --json ", json.RerunPrefix());
    }

    // ─── Harness ───────────────────────────────────────────────

    /// <summary>Every verb <c>--json</c> claims to support, with arguments that make it produce rows.</summary>
    private static IEnumerable<(string Command, string[] Args)> Verbs() =>
    [
        ("summary", []),
        ("scenarios", []),
        ("failures", []),
        ("services", []),
        ("interactions", ["s0"]),
        ("assertions", [])
    ];

    private string Run(string command, string[]? args = null, string? report = null)
    {
        var (output, error, exit) = RunFull(command, report ?? Report(), [.. args ?? [], "--json"]);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output;
    }

    private string Run2(string command, string report, params string[] args)
    {
        var (output, error, exit) = RunFull(command, report, args);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output;
    }

    /// <summary>Runs a raw argv — what a consumer does with <c>next</c>, with no splitting or unquoting.</summary>
    private static string RunArgv(string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    private static (string Output, string Error, int Exit) RunFull(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    /// <summary>
    /// The address of the first REQUEST in a scenario, asked of the tool rather than assumed. An ordinal
    /// counts responses too, so a hard-coded <c>s5/i1</c> is as likely to name the response - which has a
    /// different body, and made three facts here assert against the wrong payload.
    /// </summary>
    private string RequestAddress(string scenario)
    {
        var listing = Run2("interactions", Report(), scenario);
        return listing.Split('\n').First(l => l.StartsWith(scenario + "/i", StringComparison.Ordinal))
            .Split(' ')[0];
    }

    private static string[] Notes(JsonElement envelope) =>
        envelope.GetProperty("notes").EnumerateArray().Select(n => n.GetString()!).ToArray();

    private string BodyHashOf(string address)
    {
        var line = Run2("http", Report(), address).Split('\n').First(l => l.StartsWith("body:", StringComparison.Ordinal));
        return line.Split("· ")[1].Split(' ')[0].Trim();
    }

    // ─── Fixtures ──────────────────────────────────────────────
    //
    // The same shape as QueryCommandTests', deliberately duplicated rather than shared: these facts are
    // about the format, and a fixture change made for a text fact must not be able to quietly move what
    // the envelope contains.

    private string? _report;
    private string? _unenriched;

    private string Report(bool allPassing = false, string fileName = "TestRunReport.json")
    {
        if (!allPassing && fileName == "TestRunReport.json" && _report is not null)
            return _report;

        var path = Write(fileName, BuildFeatures(allPassing), BuildLogs());
        if (!allPassing && fileName == "TestRunReport.json")
            _report = path;
        return path;
    }

    /// <summary>
    /// A report as an older Kronikol wrote it. Written out literally rather than generated: the current
    /// generator cannot produce the old shape, and enrichment is detected by the presence of keys rather
    /// than of values, so a doctored current report is not the thing under test.
    /// </summary>
    private string UnenrichedReport()
    {
        if (_unenriched is not null)
            return _unenriched;

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
                      "httpInteractions": []
                    }
                  ]
                }
              ]
            }
            """);

        return _unenriched = path;
    }

    private string Write(string fileName, Feature[] features, RequestResponseLog[]? logs)
    {
        var written = ReportGenerator.GenerateTestRunReportData(
            features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Json_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, null, logs);

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
                            SourceFile = "OverviewTests.cs", SourceLine = 142
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
                    Steps = [new ScenarioStep { Keyword = "When", Text = "checking out again", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private static RequestResponseLog[] BuildLogs()
    {
        var start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var logs = new List<RequestResponseLog>();

        // A handful of ordinary calls, so `interactions s0` and `--group` both have rows to fold.
        for (var i = 0; i < 4; i++)
            logs.AddRange(Pair("t0", "catalogue", "GET", "http://catalogue/items", null,
                HttpStatusCode.OK, start.AddMilliseconds(i * 5), "{\"items\":[]}"));

        logs.AddRange(Pair("t2", "payments", "POST", "http://payments/charge",
            "{\"customerReference\":\"CUST-1\",\"total\":4173}",
            HttpStatusCode.InternalServerError, start.AddSeconds(1), "{\"error\":\"boom\"}"));
        logs.AddRange(Pair("t2", "payments", "POST", "http://payments/charge",
            "{\"customerReference\":\"CUST-2\",\"total\":4173}",
            HttpStatusCode.InternalServerError, start.AddSeconds(2), "{\"error\":\"boom\"}"));

        // A service whose name has a space in it — ordinary, and the shape that broke `next`.
        for (var i = 0; i < 3; i++)
            logs.AddRange(Pair("t0", "Dessert Provider", "GET", "http://desserts/menu", null,
                HttpStatusCode.OK, start.AddMilliseconds(200 + i * 5), "{\"desserts\":[]}"));

        // s5/i1 and s6/i1: the two bodies the diff facts compare.
        logs.AddRange(Pair("t5", "orders", "POST", "http://orders/create",
            "{\"customer\":{\"region\":\"EU\"},\"total\":4173,\"items\":[{\"sku\":\"a\",\"price\":12.5},{\"sku\":\"b\",\"price\":3}]}",
            HttpStatusCode.OK, start.AddSeconds(3), "{\"ok\":true}"));
        logs.AddRange(Pair("t6", "orders", "POST", "http://orders/create",
            "{\"customer\":{\"region\":null},\"total\":3902,\"items\":[{\"sku\":\"a\",\"price\":1250},{\"sku\":\"b\",\"price\":3},{\"sku\":\"c\",\"price\":9}]}",
            HttpStatusCode.OK, start.AddSeconds(4), "{\"ok\":true}"));

        return logs.ToArray();
    }

    private static IEnumerable<RequestResponseLog> Pair(string scenarioId, string service, string method, string uri,
        string? requestBody, HttpStatusCode status, DateTimeOffset at, string responseBody)
    {
        yield return new RequestResponseLog("F", scenarioId, new HttpMethod(method), requestBody, new Uri(uri), [],
            service, "test", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { Timestamp = at };
        yield return new RequestResponseLog("F", scenarioId, new HttpMethod(method), responseBody, new Uri(uri), [],
            service, "test", RequestResponseType.Response, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            Timestamp = at.AddMilliseconds(20), StatusCode = status, DurationMs = 20
        };
    }
}
