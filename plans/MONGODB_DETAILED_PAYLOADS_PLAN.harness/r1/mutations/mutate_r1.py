"""R1 mutations: each one-line change must turn at least one named fact red. Runs in a snapshot worktree."""
import os
import re
import subprocess
import sys

ROOT = r"C:\Code\Kronikol-168-mut"
OUT = r"C:\Users\cex\AppData\Local\Temp\claude\c--Code-Kronikol\9503d133-ae25-4fc9-b12e-cc3b8c2ac5d5\scratchpad\mutations-r1.txt"
ENV = dict(os.environ, KRONIKOL_TEST_MONGO="mongodb://127.0.0.1:27217/?replicaSet=rs0&directConnection=true")

NOTES = "src/Kronikol.Extensions.MongoDB/MongoDbNotes.cs"
JSON = "src/Kronikol.Extensions.MongoDB/MongoJson.cs"
SUB = "src/Kronikol.Extensions.MongoDB/MongoDbTrackingSubscriber.cs"
CLS = "src/Kronikol.Extensions.MongoDB/MongoDbOperationClassifier.cs"
DEC = "src/Kronikol.Extensions.TcpTap/Protocols/MongoProtocolDecoder.cs"
TAP = "src/Kronikol.Extensions.TcpTap/TcpTap.cs"
PUML = "src/Kronikol/PlantUml/PlantUmlCreator.cs"

M3 = "tests/Kronikol.Tests.MongoDB"
M2 = "tests/Kronikol.Tests.MongoDB.V2"
TT = "tests/Kronikol.Tests.TcpTap"
CORE = "tests/Kronikol.Tests"

MUTATIONS = [
    ("opTime left out of the bookkeeping", NOTES, '"electionId", "opTime",', '"electionId",', [(M3, "The_servers_bookkeeping"), (TT, "TheServersBookkeeping")]),
    ("recoveryToken left out of the bookkeeping", NOTES, '"recoveryToken", "retriedStmtIds",', '"retriedStmtIds",', [(M3, "The_servers_bookkeeping"), (TT, "TheServersBookkeeping")]),
    ("nextBatch left unread", NOTES, ': cursor.TryGetValue("nextBatch", out var next) && next is BsonArray nextBatch ? nextBatch\n', '', [(M3, "A_getMore_reply_holds_its_next_batch"), (TT, "AGetMoreIsDrawnAgainstItsCollectionWithItsNextBatch")]),
    ("a write error left at 200 (writeErrors)", NOTES, '(reply.TryGetValue("writeErrors", out var writeErrors) && writeErrors is BsonArray { Count: > 0 })\n        || ', '', [(M3, "A_write_error_reply_is_a_failure"), (TT, "AWriteErrorIsA500")]),
    ("writeConcernError not a failure", NOTES, '|| (reply.TryGetValue("writeConcernError", out var concern) && concern is BsonDocument)\n        ', '', [(M3, "A_write_concern_error_reply_is_a_failure"), (TT, "AWriteConcernErrorIsA500")]),
    ("nErrors not a failure", NOTES, '|| (reply.TryGetValue("nErrors", out var errors) && Number(errors) > 0d)', '', [(M3, "A_client_bulkWrite_with_errors_is_a_failure")]),
    ("server state replies kept", NOTES, 'operation is MongoDbOperation.ServerStatus or MongoDbOperation.DbStats or MongoDbOperation.CollStats', 'operation is (MongoDbOperation)(-1)', [(M3, "Server_state_replies")]),
    ("findAndModify value kept without LogResponseContent", NOTES, '|| (operation == MongoDbOperation.FindAndModify && element.Name == "value")', '', [(M3, "Without_LogResponseContent")]),
    ("the $uuid branch removed", JSON, 'if (subType == 4 && bytes.Length == 16)', 'if (false)', [(M3, "A_guid_is_written_whole|Every_bson_type_is_written|A_reply_batch_is_json_on_both_drivers")]),
    ("the writer made compact", JSON, 'builder.Append(" : ");\n            Write(builder, element.Value, binaryPlaceholders);', 'builder.Append(":");\n            Write(builder, element.Value, binaryPlaceholders);', [(M3, "A_content_pattern_written_for_todays_text_still_redacts|An_update_reply_is_its_counts_as_json")]),
    (".0 not added to an integral double", JSON, '            text += ".0";\n', '', [(M3, "Doubles_keep_a_fraction")]),
    ("a modifier letter written as itself", JSON, 'UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter', 'UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter', [(M3, "Every_utf16_unit")]),
    ("regex options not sorted", JSON, 'WriteString(builder, new string(regex.Options.OrderBy(c => c).ToArray()));', 'WriteString(builder, regex.Options);', [(M3, "Regex_options")]),
    ("milliseconds always written", JSON, 'var format = instant.Millisecond == 0 ? "yyyy-MM-dd\'T\'HH:mm:ss\'Z\'" : "yyyy-MM-dd\'T\'HH:mm:ss.fff\'Z\'";', 'var format = "yyyy-MM-dd\'T\'HH:mm:ss.fff\'Z\'";', [(M3, "Every_bson_type|A_date_in_a_batch_is_a_date")]),
    ("a binary of exactly 256 bytes made a placeholder", JSON, 'bytes.Length > MaxInlineBinaryBytes', 'bytes.Length >= MaxInlineBinaryBytes', [(M3, "A_binary_over_256_bytes")]),
    ("the deferred note rendered at the reply", SUB, 'LogRequest(pending, verbosity => deferred.Notes[verbosity], effectiveVerbosity, deferred.StartedAt, deferred.Phase);\n        }\n\n        AutoCorrelateIfWrite(pending);', 'LogRequest(pending, verbosity => RequestNote(pending.OpInfo, deferredCommand, verbosity), effectiveVerbosity, deferred.StartedAt, deferred.Phase);\n        }\n\n        AutoCorrelateIfWrite(pending);', [(M3, "A_deferred_claim")]),
    ("OnCommandStarted lets an exception through", SUB, '        catch (Exception ex)\n        {\n            // A command whose request could not be logged is dropped whole: its reply finds nothing pending.\n            _pending.TryRemove(e.RequestId, out _);\n            NotRecorded(ex);\n        }', '        catch (Exception ex) when (ex is ArithmeticException)\n        {\n            _pending.TryRemove(e.RequestId, out _);\n            NotRecorded(ex);\n        }', [(M3, "No_handler_exception_reaches_the_driver|A_command_whose_request_could_not_be_logged")]),
    ("a note that cannot be rendered throws", SUB, '        catch (Exception ex)\n        {\n            return $"<{what} could not be rendered: {ex.GetType().Name}>";\n        }', '        catch (Exception ex) when (ex is ArithmeticException)\n        {\n            return $"<{what} could not be rendered: {ex.GetType().Name}>";\n        }', [(M3, "A_reply_that_cannot_be_read")]),
    ("the diagnostic not recorded", SUB, '            Kronikol.Reports.ProcessDiagnostics.RecordOnce(', '            _ = (', [(M3, "No_handler_exception_reaches_the_driver")]),
    ("Error left unset on a write error", SUB, '            Error = error,\n            AttributionSource = pending.TestInfo.Source,\n            Phase = TestPhaseContext.Current\n        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,\n            v => new PhaseVariant(\n                MongoDbOperationClassifier.GetDiagramLabel(pending.OpInfo, v),\n                BuildUri(pending.OpInfo, v),\n                ReplyNote(', '            AttributionSource = pending.TestInfo.Source,\n            Phase = TestPhaseContext.Current\n        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,\n            v => new PhaseVariant(\n                MongoDbOperationClassifier.GetDiagramLabel(pending.OpInfo, v),\n                BuildUri(pending.OpInfo, v),\n                ReplyNote(', [(M3, "A_write_error_sets_Error_to_its_errmsg|Failures_md_says_why_a_mongo_write_failed")]),
    ("a server error's Result ignored", SUB, 'var result = (e.Failure as global::MongoDB.Driver.MongoCommandException)?.Result;', 'BsonDocument? result = null;', [(M3, "A_server_error_keeps|A_failed_command_sets_Error|Failures_md_says_why_a_mongo_command_failed")]),
    ("getMore's collection not read", CLS, '        if (commandName.Equals("getMore", StringComparison.OrdinalIgnoreCase))\n            return command.TryGetValue("collection", out var collection) && collection.IsString ? collection.AsString : null;\n', '', [(M3, "A_getMore_is_drawn_against_its_collection"), (TT, "AGetMoreIsDrawnAgainstItsCollection")]),
    ("the tap's write errors left at 200", DEC, '                if (reply is not null && MongoDbNotes.IsWriteFailure(reply))', '                if (false)', [(TT, "AWriteErrorIsA500|AWriteConcernErrorIsA500|EveryRealCommandAndReplyRendersAlike")]),
    ("the tap's Error not stamped on the response", TAP, '            Error = interaction.Error,\n', '', [(TT, "AWriteErrorIsA500WithItsErrmsgAsTheError|ACommandThatFailedBecomesA500")]),
    ("the tap's failed note is the errmsg alone", DEC, ': MongoDbNotes.Failure(reply) ?? "command failed";', ': MongoDbNotes.FailureReason(reply) ?? "command failed";', [(TT, "ACommandThatFailedBecomesA500|AMongoFailureIsA500WithTheServersWords")]),
    ("a null dropped again from a statement's request note", PUML, 'focusDeEmphasis, graphQlBodyFormat, keepNulls: Constants.DependencyCategories.IsStatementShaped(trace.DependencyCategory));', 'focusDeEmphasis, graphQlBodyFormat);', [(CORE, "StatementNoteNullTests")]),
    ("a null dropped again from a statement's reply note", PUML, 'focusEmphasis, focusDeEmphasis, keepNulls: Constants.DependencyCategories.IsStatementShaped(trace.DependencyCategory));', 'focusEmphasis, focusDeEmphasis);', [(CORE, "StatementNoteNullTests")]),
]
from mutate_r1_extra import EXTRA
MUTATIONS += EXTRA


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
    if name == "the deferred note rendered at the reply":
        # The command is kept, as before the fix, and read when the reply comes.
        text = text.replace("Deferred = new DeferredRequest(testInfo, notes, DateTimeOffset.UtcNow, TestPhaseContext.Current)",
                            "Deferred = new DeferredRequest(testInfo, notes, DateTimeOffset.UtcNow, TestPhaseContext.Current) { Command = command }")
        text = text.replace("private sealed record DeferredRequest(TestIdentity? Resolved, IReadOnlyDictionary<MongoDbTrackingVerbosity, string?> Notes, DateTimeOffset StartedAt, TestPhase Phase);",
                            "private sealed record DeferredRequest(TestIdentity? Resolved, IReadOnlyDictionary<MongoDbTrackingVerbosity, string?> Notes, DateTimeOffset StartedAt, TestPhase Phase) { public BsonDocument? Command { get; init; } }")
        text = text.replace("            // The claim by filter: the reply names the document it took, or nothing.\n",
                            "            // The claim by filter: the reply names the document it took, or nothing.\n            var deferredCommand = deferred.Command;\n")
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
