"""R2 mutations: each one-line change must turn at least one named fact red. Runs in a snapshot worktree.

python mutate_r2.py [--dry] [n ...]   (results are appended to OUT)
"""
import os
import re
import subprocess
import sys

ROOT = r"C:\Code\Kronikol-168-mut2"
OUT = r"C:\Users\cex\AppData\Local\Temp\claude\c--Code-Kronikol\9503d133-ae25-4fc9-b12e-cc3b8c2ac5d5\scratchpad\mutations-r2.txt"
ENV = dict(os.environ, KRONIKOL_TEST_MONGO="mongodb://127.0.0.1:27217/?replicaSet=rs0&directConnection=true")

NOTES = "src/Kronikol.Extensions.MongoDB/MongoDbNotes.cs"
SUB = "src/Kronikol.Extensions.MongoDB/MongoDbTrackingSubscriber.cs"
CLS = "src/Kronikol.Extensions.MongoDB/MongoDbOperationClassifier.cs"
SUMMARY = "src/Kronikol.Extensions.MongoDB/MongoDbResponseSummary.cs"
DEC = "src/Kronikol.Extensions.TcpTap/Protocols/MongoProtocolDecoder.cs"
TAPOPT = "src/Kronikol.Extensions.TcpTap/MongoTapOptions.cs"
SHAPE = "src/Kronikol/History/InteractionShape.cs"
DIGEST = "src/Kronikol/Reports/FailuresDigestGenerator.cs"

M3 = "tests/Kronikol.Tests.MongoDB"
TT = "tests/Kronikol.Tests.TcpTap"
CORE = "tests/Kronikol.Tests"

OBSOLETE = ('    [Obsolete("Kronikol\'s MongoDB notes are relaxed extended JSON since 4.19.6 (MongoDbTrackingSubscriber and '
            'MongoTap), with a write\'s counts in the JSON; this text form is no longer used, and is removed in 5.0.")]\n')

MUTATIONS = [
    ("$clusterTime kept in a request", NOTES, '"$db", "lsid", "txnNumber", "$clusterTime",', '"$db", "lsid", "txnNumber",',
     [(M3, "The_driver_fields_are_not_in_the_note")]),
    ("recoveryToken kept in a request", NOTES, '"startTransaction", "autocommit", "recoveryToken",', '"startTransaction", "autocommit",',
     [(M3, "The_driver_fields_are_not_in_the_note|A_commitTransaction_has_no_request_note")]),
    ("ordered: false stripped", NOTES, '|| (element.Name == "ordered" && element.Value is BsonBoolean { Value: true })',
     '|| element.Name == "ordered"', [(M3, "An_unordered_write_keeps_ordered_false")]),
    ("ordered: true kept", NOTES, '\n                || (element.Name == "ordered" && element.Value is BsonBoolean { Value: true })', '',
     [(M3, "The_driver_fields_are_not_in_the_note|An_insert_request_note_is_its_documents")]),
    ("an aggregate's cursor kept", NOTES, '\n                || (isAggregate && element.Name == "cursor")', '',
     [(M3, "An_aggregate_request_note_is_its_pipeline")]),
    ("the first element dropped whatever its value", NOTES, 'if (element.Value.IsString || element.Value.IsNumeric)', 'if (true)',
     [(M3, "An_explain_keeps_the_command_it_explains")]),
    ("the command's name kept", NOTES, '                if (element.Value.IsString || element.Value.IsNumeric)\n                    continue;\n', '',
     [(M3, "A_find_request_note_is_its_filter_sort_and_limit"), (TT, "AFindIsLabelledAndAddressedLikeTheInProcessExtension")]),
    ("documents not capped", NOTES, 'documentsNotShown += WriteBatch(builder, entries, max);', 'documentsNotShown += WriteBatch(builder, entries, int.MaxValue);',
     [(M3, "Request_documents_are_capped_with_a_footnote_the_page_strips|MaxRequestDocuments_defaults_to_10")]),
    ("statements not capped", NOTES, 'statementsNotShown += WriteBatch(builder, statements, max);', 'statementsNotShown += WriteBatch(builder, statements, int.MaxValue);',
     [(M3, "Request_statements_are_capped_with_a_footnote_of_their_own")]),
    ("statements given the documents footnote", NOTES, '$"... ({statementsNotShown} more)"', '$"... ({statementsNotShown} more documents not shown)"',
     [(M3, "Request_statements_are_capped_with_a_footnote_of_their_own")]),
    ("an empty request written as { }", NOTES,
     '        if (kept.Count == 0)\n            return null;\n\n        var max = Math.Max(0, maxRequestDocuments);',
     '        var max = Math.Max(0, maxRequestDocuments);', [(M3, "A_commitTransaction_has_no_request_note")]),
    ("a Raw request written by ToString", SUB, 'MongoDbTrackingVerbosity.Raw => MongoDbNotes.Whole(command),', 'MongoDbTrackingVerbosity.Raw => command?.ToString(),',
     [(M3, "A_raw_request_is_the_whole_command_in_relaxed_json")]),
    ("LogFilterText ignored", SUB, '_ => _options.LogFilterText && command is not null ? MongoDbNotes.Request(', '_ => command is not null ? MongoDbNotes.Request(',
     [(M3, "LogFilterText_off_leaves_no_request_note")]),
    ("MaxRequestDocuments not passed", SUB, 'MongoDbNotes.Request(command, _options.MaxRequestDocuments)', 'MongoDbNotes.Request(command, 10)',
     [(M3, "Request_documents_are_capped_with_a_footnote_the_page_strips")]),
    ("a Raw label's filter written by ToString", CLS, '? MongoJson.Write(filter)', '? filter.ToString()!',
     [(M3, "A_raw_label_writes_its_filter_in_the_same_json")]),
    ("nsInfo ignored", CLS, 'if (operation == MongoDbOperation.BulkWrite && SingleNamespace(command) is { } written)', 'if (false && SingleNamespace(command) is { } written)',
     [(M3, "A_client_bulkWrite_is_drawn_against_its_namespace")]),
    ("nsInfo with two namespaces taken", CLS, 'nsInfo is not BsonArray { Count: 1 } entries', 'nsInfo is not BsonArray { Count: > 0 } entries',
     [(M3, "A_client_bulkWrite_over_two_namespaces_stays_against_admin")]),
    ("ExtractMetadata not obsolete", SUMMARY, OBSOLETE + '    public static string? ExtractMetadata', '    public static string? ExtractMetadata',
     [(M3, "MongoDbResponseSummary_says_it_is_obsolete")]),
    ("the tap's request note left out", DEC, '_ => _options.LogFilterText ? MongoDbNotes.Request(command, _options.MaxRequestDocuments) : null,', '_ => null,',
     [(TT, "AFindIsLabelledAndAddressedLikeTheInProcessExtension|EveryRealCommandAndReplyRendersAlike")]),
    ("the tap's Raw request written by ToString", DEC, 'MongoDbTrackingVerbosity.Raw => MongoDbNotes.Whole(command),', 'MongoDbTrackingVerbosity.Raw => command.ToString(),',
     [(TT, "Raw|EveryRealCommandAndReplyRendersAlike")]),
    ("the tap's MaxRequestDocuments not copied", TAPOPT, '        mongo.MaxRequestDocuments = MaxRequestDocuments;\n', '',
     [(TT, "CopyToCarriesEveryProtocolOption")]),
    ("the shape version left at 4", SHAPE, 'public const int Version = 5;', 'public const int Version = 4;',
     [(CORE, "The_shape_rule_has_a_version_a_run_records")]),
    ("Failures.md quotes a MongoDB note again", DIGEST, '? DependencyCategories.IsStatementShaped(category) && !IsMongoDb(category)', '? DependencyCategories.IsStatementShaped(category)',
     [(M3, "Failures_md_lists_a_MongoDB_call_by_its_label")]),
    ("the tap's LogResponseContent not copied (found by #21)", TAPOPT, '        mongo.LogResponseContent = LogResponseContent;\n', '',
     [(TT, "CopyToCarriesEveryProtocolOption")]),
    ("the tap's DocumentRedaction not copied (found by #21)", TAPOPT, '        mongo.DocumentRedaction = DocumentRedaction;\n', '',
     [(TT, "CopyToCarriesEveryProtocolOption")]),
]


def run(args, cwd):
    p = subprocess.run(args, cwd=cwd, env=ENV, capture_output=True, text=True, encoding="utf-8", errors="replace")
    return p.returncode, p.stdout + p.stderr


def filter_for(names):
    return "|".join(f"FullyQualifiedName~{n}" for n in names.split("|"))


results = []
DRY = "--dry" in sys.argv
only = set(a for a in sys.argv[1:] if a != "--dry")
for i, (name, path, old, new, checks) in enumerate(MUTATIONS, 1):
    if only and str(i) not in only:
        continue
    full = os.path.join(ROOT, path)
    original = open(full, "rb").read()
    text = original.decode("utf-8")
    count = text.count(old)
    if DRY:
        print(f"{i:2}. {name}: {count}")
        continue
    if count != 1:
        results.append(f"{i:2}. {name}: PATTERN MATCHED {count} TIMES, not run")
        print(results[-1], flush=True)
        continue
    open(full, "wb").write(text.replace(old, new).encode("utf-8"))
    try:
        verdicts = []
        caught = False
        for project, names in checks:
            code, out = run(["dotnet", "test", project, "--no-restore", "--filter", filter_for(names)], ROOT)
            if "error CS" in out or "Build FAILED" in out:
                errors = sorted(set(re.findall(r"error CS\d+: [^\[]+", out)))[:2]
                verdicts.append(f"{project}: BUILD FAILED {errors}")
                continue
            summary = re.search(r"(Passed!|Failed!)\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+)", out)
            failed = re.findall(r"\s{4}(\S+) \[FAIL\]", out)
            if summary is None:
                verdicts.append(f"{project}: NO TESTS RAN")
            elif int(summary.group(2)) > 0:
                caught = True
                verdicts.append(f"{project}: {summary.group(2)} failed ({', '.join(f.split('.')[-1] for f in failed[:4])})")
            else:
                verdicts.append(f"{project}: all {summary.group(3)} passed")
        results.append(f"{i:2}. {name}: {'CAUGHT' if caught else 'SURVIVED'} | " + " | ".join(verdicts))
    finally:
        open(full, "wb").write(original)
    print(results[-1], flush=True)

if DRY:
    sys.exit(0)
with open(OUT, "a", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(results) + "\n")
