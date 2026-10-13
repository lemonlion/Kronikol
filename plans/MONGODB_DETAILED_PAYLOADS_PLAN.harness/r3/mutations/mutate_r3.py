"""R3 mutations: each one-line change must turn at least one named fact red. Runs in a snapshot worktree.

python mutate_r3.py [--dry] [n ...]   (results are appended to OUT)
"""
import os
import re
import subprocess
import sys

ROOT = r"C:\Code\Kronikol-168-mut3"
OUT = r"C:\Users\cex\AppData\Local\Temp\claude\c--Code-Kronikol\9503d133-ae25-4fc9-b12e-cc3b8c2ac5d5\scratchpad\mutations-r3.txt"
ENV = dict(os.environ)

PATH = "src/Kronikol/Query/PathEngine.cs"
WHERE = "src/Kronikol/Query/WhereClause.cs"
SHARED = "src/Kronikol/Query/QueryCommand.Shared.cs"
FOOT = "src/Kronikol/Tracking/CaptureFootnote.cs"
CACHE = "src/Kronikol/Query/BodyCache.cs"
READER = "src/Kronikol/Query/PayloadReader.cs"
PAYLOADS = "src/Kronikol/Query/QueryCommand.Payloads.cs"
DIFF = "src/Kronikol/Query/QueryCommand.Diff.cs"
LOGGER = "src/Kronikol/Tracking/RequestResponseLogger.cs"
NUMBER = "src/Kronikol/Query/QueryCommand.NumberGrep.cs"

CORE = "tests/Kronikol.Tests"
CAPPED = "A_capped_batch_and_a_capped_rows_body|A_capped_body_answers_where_and_path|A_capped_body_shows_its_keys|Grep_values_names_the_json_path_in_a_capped|Diff_bodies_reads_two_capped"

MUTATIONS = [
    ("`..` read as `.` again", PATH, "if (i + 1 < text.Length && text[i + 1] == '.')", "if (false)",
     [(CORE, "Path_recursive_descent")]),
    ("the suggestion drops `[*]`", PATH, '+ text[..i] + "[*]." + text[(i + 2)..]', '+ text[..i] + "." + text[(i + 2)..]',
     [(CORE, "Path_recursive_descent")]),
    ("an ordering against a number orders text again", WHERE,
     'if (literalIsNumber && !isNumber && Operator is "<" or ">" or "<=" or ">=")', "if (false)",
     [(CORE, "Where_never_orders_a_non_number|Where_does_not_order_an_extended_json_decimal")]),
    ("an inequality counts a non-number too", WHERE,
     '&& !isNumber && Operator is "<" or ">" or "<=" or ">=")', '&& !isNumber && Operator is "<" or ">" or "<=" or ">=" or "!=")',
     [(CORE, "Where_only_an_ordering_counts")]),
    ("a non-number is not counted", WHERE, "            NotNumbers++;\n", "",
     [(CORE, "Where_never_orders_a_non_number|Where_does_not_order_an_extended_json_decimal")]),
    ("the count is never noted", SHARED, "if (clause.NotNumbers > 0)", "if (false && clause.NotNumbers > 0)",
     [(CORE, "Where_never_orders_a_non_number|Where_does_not_order_an_extended_json_decimal")]),
    ("the footnote pattern forgets documents", FOOT, "(?: (?:rows|documents) not shown)?", "(?: rows not shown)?",
     [(CORE, CAPPED)]),
    ("no reading past the footnote", CACHE,
     "Parse(body) ?? (Tracking.CaptureFootnote.DocumentBefore(body) is { } before ? Parse(before) : null)", "Parse(body)",
     [(CORE, CAPPED)]),
    ("a footnoted body is not counted as capped", CACHE,
     ' || Tracking.CaptureFootnote.DocumentBefore(body) is not null', "",
     [(CORE, "A_capped_batch_and_a_capped_rows_body|A_capped_body_shows_its_keys|Diff_bodies_reads_two_capped")]),
    ("--body drops the footnote", READER, '? pretty + "\\n" + capped.Footnote : pretty', "? pretty : pretty",
     [(CORE, "A_capped_body_shows_its_keys")]),
    ("--keys reads a capped body as text", PATH, '                Walk(document.RootElement, "$", 0);\n',
     '                if (!body.EndsWith(")")) Walk(document.RootElement, "$", 0);\n',
     [(CORE, "A_capped_body_shows_its_keys")]),
    ("grep --values reads a capped body as text", PATH, '                Walk(document.RootElement, "$");\n',
     '                if (!body.EndsWith(")")) Walk(document.RootElement, "$");\n',
     [(CORE, "Grep_values_names_the_json_path_in_a_capped")]),
    ("--path parses the whole body", PAYLOADS, "if (BodyCache.ParseJson(body) is not { } document)",
     "if (System.Text.Json.JsonDocument.Parse(body) is not { } document)",
     [(CORE, "A_capped_body_answers_where_and_path")]),
    ("diff --body notes only a cut body", DIFF,
     "if (BodyCache.CappedAtCapture(left.Content) || BodyCache.CappedAtCapture(right.Content))",
     'if (left.Content.Contains("…truncated (", StringComparison.Ordinal) || right.Content.Contains("…truncated (", StringComparison.Ordinal))',
     [(CORE, "Diff_bodies_reads_two_capped")]),
    ("diff --body does not name the footnotes", DIFF,
     '+ (before != after ? $": {before ?? "(no footnote)"} → {after ?? "(no footnote)"}" : ""));', '+ "");',
     [(CORE, "Diff_bodies_reads_two_capped")]),
    ("diff --body reads a capped body as text", DIFF, "if (BodyCache.ParseJson(leftBody) is not { } leftDocument)",
     "if (System.Text.Json.JsonDocument.Parse(leftBody) is not { } leftDocument || leftBody.EndsWith(\")\"))",
     [(CORE, "Diff_bodies_reads_two_capped")]),
    ("integers compared as doubles", DIFF, "if (b.ValueKind == JsonValueKind.Number && IsInteger(a) && IsInteger(b))", "if (false)",
     [(CORE, "Diff_bodies_reports_two_integers")]),
    ("every number compared by its digits", DIFF, "IndexOfAny('.', 'e', 'E') < 0", "IndexOfAny('.', 'e', 'E') < 0 || true",
     [(CORE, "Diff_bodies_reports_two_integers")]),
    ("a setup variant left whole", LOGGER, "if (log.SetupVariant is { Content: { } setup } && setup.Length > max)",
     "if (log.SetupVariant is { Content: { } setup } && setup.Length > int.MaxValue)",
     [(CORE, "Log_MaxContentLength_caps_each_phase_variants_content_too")]),
    ("an action variant left whole", LOGGER, "if (log.ActionVariant is { Content: { } action } && action.Length > max)",
     "if (log.ActionVariant is { Content: { } action } && action.Length > int.MaxValue)",
     [(CORE, "Log_MaxContentLength_caps_each_phase_variants_content_too")]),
    ("grep --number reads a capped body as text", NUMBER, "if (cache.Json(body.Hash) is not { } document)",
     'if (content.EndsWith(")") || cache.Json(body.Hash) is not { } document)',
     [(CORE, "Grep_number_names_the_json_path_in_a_capped")]),
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
