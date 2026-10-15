"""#168 audit mutations. python -u mutate_audit.py <worktree> [first-id]

Each mutation replaces one exact text in one file of <worktree> (a worktree of the audit branch), runs the facts that
should catch it in one test project, records CAUGHT (a test failed), SURVIVED (all passed) or BUILD FAILED, and restores
the file from its bytes in memory (never git checkout). Results append to audit/mutations.txt, the folder above this script.
"""
import pathlib
import subprocess
import sys

WT = pathlib.Path(sys.argv[1])
FIRST = int(sys.argv[2]) if len(sys.argv) > 2 else 1
OUT = pathlib.Path(__file__).parent.parent / "mutations.txt"

CORE = "tests/Kronikol.Tests"
MONGO = "tests/Kronikol.Tests.MongoDB"
TAP = "tests/Kronikol.Tests.TcpTap"
OTLP = "tests/Kronikol.Tests.Otlp"

M = [
    # (id, description, file, old, new, project, filter)
    (1, "CaptureRedaction leaves Error alone", "src/Kronikol/Tracking/CaptureRedaction.cs",
     "var error = RedactContentText(log.Error);", "var error = log.Error;", CORE, "Content_patterns_scrub_a_calls_error_too"),
    (2, "the tap's Error skips DocumentRedaction", "src/Kronikol.Extensions.TcpTap/Protocols/MongoProtocolDecoder.cs",
     "Error = Redact(error)", "Error = error", TAP, "DocumentRedactionRunsOverTheErrorToo"),
    (3, "CaptureReplies off keeps Error", "src/Kronikol.Extensions.TcpTap/TcpTap.cs",
     "Error = _options.CaptureReplies ? interaction.Error : null,", "Error = interaction.Error,", TAP, "WithoutCaptureRepliesTheServersMessageIsNotRecordedEither"),
    (4, "a handshake starting authentication is recorded", "src/Kronikol.Extensions.TcpTap/Protocols/MongoProtocolDecoder.cs",
     "if (!ShouldRecord(commandName) || StartsAuthentication(commandName, command))", "if (!ShouldRecord(commandName))", TAP, "AHandshakeThatStartsAuthentication"),
    (5, "saslSupportedMechs alone is not authentication", "src/Kronikol.Extensions.TcpTap/Protocols/MongoProtocolDecoder.cs",
     '&& (command.Contains("speculativeAuthenticate") || command.Contains("saslSupportedMechs"));', '&& command.Contains("speculativeAuthenticate");', TAP, "AHandshakeThatStartsAuthentication"),
    (6, "every hello is withheld", "src/Kronikol.Extensions.TcpTap/Protocols/MongoProtocolDecoder.cs",
     '&& (command.Contains("speculativeAuthenticate") || command.Contains("saslSupportedMechs"));', '&& true;', TAP, "AHandshakeThatStartsNoAuthenticationIsRecorded"),
    (7, "subscriber: Summarised without content keeps the reply", "src/Kronikol.Extensions.MongoDB/MongoDbTrackingSubscriber.cs",
     "MongoDbTrackingVerbosity.Summarised when !logResponseContent => MongoDbNotes.FailureReason(result) ?? message,", "", MONGO, "A_summarised_failure_without_response_content"),
    (8, "tap: Summarised without content keeps the reply", "src/Kronikol.Extensions.TcpTap/Protocols/MongoProtocolDecoder.cs",
     'MongoDbTrackingVerbosity.Summarised when !_options.LogResponseContent => MongoDbNotes.FailureReason(reply) ?? "command failed",', "", TAP, "ASummarisedFailureWithoutResponseContent"),
    (9, "no reply array is capped", "src/Kronikol.Extensions.MongoDB/MongoDbNotes.cs",
     "            ? entries\n            : null;", "            ? null\n            : null;", MONGO, "A_distinct_reply_keeps_its_first_values|Upserted_ids_and_write_errors"),
    (10, "upserted is not capped", "src/Kronikol.Extensions.MongoDB/MongoDbNotes.cs",
     'element.Name is "writeErrors" or "upserted"', 'element.Name is "writeErrors"', MONGO, "Upserted_ids_and_write_errors"),
    (11, "capped entries say documents", "src/Kronikol.Extensions.MongoDB/MongoDbNotes.cs",
     '$"... ({notShown + entriesNotShown} more)"', '$"... ({notShown + entriesNotShown} more documents not shown)"', MONGO, "A_distinct_reply_keeps_its_first_values"),
    (12, "bulkWrite keeps every operation without content", "src/Kronikol.Extensions.MongoDB/MongoDbNotes.cs",
     "new(results.Where(result => result is BsonDocument operation && operation.TryGetValue(\"ok\", out var ok) && Number(ok) == 0d));",
     "new(results);", MONGO, "Without_response_content_a_client_bulkWrite"),
    (13, "bulkWrite drops its failures without content", "src/Kronikol.Extensions.MongoDB/MongoDbNotes.cs",
     "kept.Add(new BsonElement(element.Name, new BsonDocument(\"firstBatch\", failed)));", "{ }", MONGO, "Without_response_content_a_client_bulkWrite"),
    (14, "no count of failed operations", "src/Kronikol.Extensions.MongoDB/MongoDbNotes.cs",
     'return string.Create(CultureInfo.InvariantCulture, $"{count:0} operation{(count == 1d ? "" : "s")} failed");', "return null;", MONGO, "past_its_first_batch_says_how_many_failed"),
    (15, "a failed write claims its document", "src/Kronikol.Extensions.MongoDB/MongoDbTrackingSubscriber.cs",
     "        if (!statementFailed)\n            AutoCorrelateIfWrite(pending);", "        AutoCorrelateIfWrite(pending);", MONGO, "A_write_that_failed_does_not_claim_its_document"),
    (16, "an unclassified command loses its name", "src/Kronikol.Extensions.MongoDB/MongoDbTrackingSubscriber.cs",
     "? MongoDbNotes.Request(command, _options.MaxRequestDocuments, classified: operation != MongoDbOperation.Other)",
     "? MongoDbNotes.Request(command, _options.MaxRequestDocuments, classified: true)", MONGO, "An_unclassified_command_keeps_its_name"),
    (17, "Raw label keeps its trailing dot", "src/Kronikol.Extensions.MongoDB/MongoDbOperationClassifier.cs",
     '$"{op.Operation} {op.DatabaseName}" + (op.CollectionName is null ? "" : $".{op.CollectionName}") +',
     '$"{op.Operation} {op.DatabaseName}.{op.CollectionName}" +', MONGO, "A_raw_label_has_no_trailing_dot"),
    (18, "booleans stay in the head", "src/Kronikol/History/InteractionShape.cs",
     'templated = BooleanPattern().Replace(templated, "{b}");', "", CORE, "A_documents_booleans_signs_and_list_lengths_are_data"),
    (19, "signs stay in the head", "src/Kronikol/History/InteractionShape.cs",
     'templated = SignPattern().Replace(templated, "");', "", CORE, "A_documents_booleans_signs_and_list_lengths_are_data"),
    (20, "list lengths stay in the head", "src/Kronikol/History/InteractionShape.cs",
     'return RepeatedScalarPattern().Replace(templated, "${scalar}");', "return templated;", CORE, "A_documents_booleans_signs_and_list_lengths_are_data"),
    (21, "a run folds part of an element", "src/Kronikol/History/InteractionShape.cs",
     r"(?:\s*,\s*\k<scalar>(?=\s*[,\]]))+(?=\s*[,\]])", r"(?:\s*,\s*\k<scalar>)+", CORE, "A_documents_keys_nulls_and_flags_still_tell_shapes_apart"),
    (22, "booleans fold in a query too", "src/Kronikol/History/InteractionShape.cs",
     "        if (!isDocument)\n            return templated;\n", "", CORE, "A_querys_booleans_and_signs_are_left_as_they_were"),
    (23, "the version stays 5", "src/Kronikol/History/InteractionShape.cs",
     "public const int Version = 6;", "public const int Version = 5;", CORE, "The_shape_rule_has_a_version_a_run_records"),
    (24, "a label is not capped in Failures.md", "src/Kronikol/Reports/FailuresDigestGenerator.cs",
     ": $\"{Truncate(FirstLine(method), OneLineLimit)} {Truncate(target, OneLineLimit)}\";", ": $\"{method} {Truncate(target, OneLineLimit)}\";", MONGO, "Failures_md_caps_a_raw_label"),
    (25, "Failures.jsonl carries no error", "src/Kronikol/Reports/FailuresDigestGenerator.cs",
     'call["error"] = Capped(c.Error);', "{ }", MONGO, "Failures_jsonl_says_why_a_call_failed"),
    (26, "Atlas calls are quoted again", "src/Kronikol/Reports/FailuresDigestGenerator.cs",
     "        || string.Equals(category, DependencyCategories.AtlasDataApi, StringComparison.OrdinalIgnoreCase);", ";", CORE, "An_atlas_data_api_call_is_listed_by_its_label"),
    (27, "the OTLP status message drops Error", "src/Kronikol.Extensions.Otlp/OtlpSpanMapper.cs",
     "private static string? ServiceMessage(RequestResponseLog log) => string.IsNullOrWhiteSpace(log.Error) ? null : log.Error;",
     "private static string? ServiceMessage(RequestResponseLog log) => null;", OTLP, "A_failure_status_carries_the_services_own_message"),
    (28, "a lone surrogate fails the diagram", "src/Kronikol/PlantUml/PlantUmlCreator.cs",
     "        catch (InvalidOperationException) { return null; }\n", "", CORE, "A_json_note_holding_a_lone_surrogate"),
    (29, "a negative MaxContentLength is accepted", "src/Kronikol/Tracking/RequestResponseLogger.cs",
     "set => _maxContentLength = value < 0", "set => _maxContentLength = value < -1000000", CORE, "MaxContentLength_refuses_a_negative_length"),
    (30, "a cap splits a surrogate pair", "src/Kronikol/Tracking/RequestResponseLogger.cs",
     "var cut = max > 0 && char.IsHighSurrogate(content[max - 1]) ? max - 1 : max;", "var cut = max;", CORE, "Log_MaxContentLength_never_splits_a_surrogate_pair"),
    (31, "a note past the budget is dropped", "src/Kronikol/Query/QueryWriter.cs",
     "        foreach (var note in _lateNotes)\n            text.Append(note).Append('\\n');\n", "", CORE, "Where_says_what_it_left_out_even_when_the_rows_fill_the_budget"),
    (32, "--count notes go to stdout", "src/Kronikol/Query/QueryCommand.Shared.cs",
     "Action<string> note = options.Count && !options.Json ? error.WriteLine : writer.Note;", "Action<string> note = writer.Note;", CORE, "Where_says_what_it_left_out_under_count"),
    (33, "--group-by says nothing", "src/Kronikol/Query/QueryCommand.GroupBy.cs",
     "        });\n        NoteWhereExclusions(options, writer, error, clauses, unevaluable, capped);\n", "        });\n", CORE, "Where_says_what_it_left_out_under_count"),
    (34, "capped reads are not counted", "src/Kronikol/Query/BodyCache.cs",
     "            _readBeforeFootnote.Add(hash);", "            { }", CORE, "Where_on_a_capped_body_says_it_read_only_what_was_recorded"),
    (35, "a changed footnote is no difference", "src/Kronikol/Query/QueryCommand.Diff.cs",
     "            if (before != after)\n                rows.Add(", "            if (false)\n                rows.Add(", CORE, "Diff_bodies_counts_a_footnote_that_changed"),
    (36, "a quoted literal is a number", "src/Kronikol/Query/WhereClause.cs",
     "if (literalIsNumber && !LiteralQuoted && !isNumber", "if (literalIsNumber && !isNumber", CORE, "Where_orders_a_quoted_literal_as_text"),
    (37, "only the first .. is rewritten", "src/Kronikol/Query/PathEngine.cs",
     "                while (i + 1 < path.Length && path[i + 1] == '.')\n                    i++;\n                text.Append(text.Length > 0 && text[^1] == ']' ? \".\" : \"[*].\");",
     "                i++;\n                text.Append(\"[*].\");", CORE, "Path_recursive_descent_suggests_a_path_that_parses|Path_recursive_descent_with_nothing"),
    (38, "an empty key is written with a dot", "src/Kronikol/Query/PathEngine.cs",
     "name.Length == 0 || name.Contains('.')", "name.Contains('.')", CORE, "An_empty_key_is_written_as_a_path_that_parses_back"),
    (39, "the Redis marker is not a cap", "src/Kronikol/Query/BodyCache.cs",
     '        || body.Contains("…[bulk string truncated: ", StringComparison.Ordinal)\n', "", CORE, "A_redis_value_cut_on_the_wire_is_counted_as_capped"),
    (40, "a text body has a footnote", "src/Kronikol/Tracking/CaptureFootnote.cs",
     "return start.Length > 0 && start[0] is '{' or '[' ? (document, match.Value.Trim()) : null;", "return (document, match.Value.Trim());", CORE,
     "A_text_body_ending_in_a_footnote_like_line_is_not_capped|A_text_body_is_not_split_whatever_its_last_line"),
    (41, "numbers compare as doubles", "src/Kronikol/Query/QueryCommand.Diff.cs",
     "return b.ValueKind == JsonValueKind.Number && JsonElement.DeepEquals(a, b);",
     "return a.TryGetDouble(out var x) && b.TryGetDouble(out var y) && x == y;", CORE, "Diff_bodies_compares_numbers_by_their_value"),
    (42, "the body keeps the platform's line ending", "src/Kronikol/Query/PayloadReader.cs",
     'JsonSerializer.Serialize(document.RootElement, PrettyJson).ReplaceLineEndings("\\n");', "JsonSerializer.Serialize(document.RootElement, PrettyJson);", CORE,
     "A_body_printed_indented_uses_one_kind_of_line_ending"),
    (43, "query failures says nothing of a failed call", "src/Kronikol/Query/QueryCommand.Narrative.cs",
     "                            + Failure(FindResponse(scenario, interaction)));", "                            );", CORE, "Failures_says_which_call_in_the_step_failed_and_why"),
    (44, "query.py reads no footnote", "templates/skills/kronikol-test-debugging/scripts/query.py",
     "    match = FOOTNOTE.search(content)\n", "    match = None\n", CORE, "It_reads_a_capped_body_as_the_tool_reads_it"),
    (45, "query.py prints no error", "templates/skills/kronikol-test-debugging/scripts/query.py",
     "    if interaction.get(\"error\"):\n", "    if False:\n", CORE, "It_says_why_a_call_failed_as_the_tool_does"),
    (46, "query.py keeps the console encoding", "templates/skills/kronikol-test-debugging/scripts/query.py",
     '            stream.reconfigure(encoding="utf-8")\n', "            pass\n", CORE, "It_prints_its_arrows_through_a_pipe"),
]


def run(project, flt):
    filt = "|".join("FullyQualifiedName~" + f for f in flt.split("|"))
    r = subprocess.run(["dotnet", "test", project, "--filter", filt, "-nologo", "-v", "q"], cwd=WT, capture_output=True,
                       text=True, encoding="utf-8", errors="replace")
    out = r.stdout + r.stderr
    if "error CS" in out or "Build FAILED" in out:
        return "BUILD FAILED"
    if "No test matches" in out:
        return "NO TEST MATCHED"
    if r.returncode != 0 and "Failed:" in out:
        return "CAUGHT"
    if r.returncode == 0:
        return "SURVIVED"
    return f"UNKNOWN rc={r.returncode}"


for mid, desc, rel, old, new, project, flt in M:
    if mid < FIRST:
        continue
    p = WT / rel
    original = p.read_bytes()
    text = original.decode("utf-8")
    n = text.count(old)
    if n != 1:
        line = f"M{mid:02d} {desc}: ANCHOR FOUND {n} TIMES in {rel}"
    else:
        p.write_bytes(text.replace(old, new).encode("utf-8"))
        try:
            verdict = run(project, flt)
        finally:
            p.write_bytes(original)
        line = f"M{mid:02d} {verdict:15} {desc} ({rel.split('/')[-1]}; {flt})"
    print(line, flush=True)
    with OUT.open("a", encoding="utf-8") as handle:
        handle.write(line + "\n")
