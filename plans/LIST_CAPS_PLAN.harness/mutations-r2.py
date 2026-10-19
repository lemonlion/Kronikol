"""Mutation proof for LIST_CAPS_PLAN R2 (#161): each announcement of a cut, removed or miscounted, must fail a fact.

Usage: python -I mutations-r2.py <Kronikol checkout> <log folder> [N1 N2 ...]

As mutations.py does for R1: one mutation at a time, as a text replacement that must match exactly once, then a build of
the test project and a run of the facts that hold the caps (the census, the lint, and the facts ListCapRegistry names),
and the file's bytes put back, also when the build or the run fails. The checkout is the one under test, so commit or
stash nothing while it runs. The builds run without node reuse or the shared compiler, so that every process this starts
takes the priority it was started with. Exits 1 unless every mutation is killed.
"""
import pathlib
import re
import subprocess
import sys

REPO = pathlib.Path(sys.argv[1]).resolve()
LOGS = pathlib.Path(sys.argv[2]).resolve()
ONLY = set(sys.argv[3:])
LOGS.mkdir(parents=True, exist_ok=True)

Q = "src/Kronikol/Query/"
NARR = Q + "QueryCommand.Narrative.cs"
OVERVIEW = Q + "QueryCommand.Overview.cs"
SIZES = Q + "QueryCommand.Sizes.cs"
SEARCH = Q + "QueryCommand.Search.cs"
NUMBERGREP = Q + "QueryCommand.NumberGrep.cs"
DIFF = Q + "QueryCommand.Diff.cs"
HISTORY = Q + "QueryCommand.History.cs"
PAYLOADS = Q + "QueryCommand.Payloads.cs"
FDG = "src/Kronikol/Reports/FailuresDigestGenerator.cs"
CISUM = "src/Kronikol/Reports/CiSummaryGenerator.cs"
FACTS = "|".join("FullyQualifiedName~" + name for name in (
    "CapCensusTests", "ListCapSourceTests", "QueryHistoryTests", "QueryHistoryLedgerOnlyTests", "BaselineDiffTests",
    "DigestSuppressionCapTests", "CiSummaryGeneratorTests", "Summary_says_how_many_failed", "FailuresDigestGeneratorTests",
    "DiffFormsTests", "RetainedRunsTests", "DigestCallScopeTests"))

# (id, what it does, file, text, replacement)
MUTATIONS = [
    ("N1", "steps: no cut line under a step's parameters", NARR, "if (parameters.Cut.Count > 0)", "if (false)"),
    ("N2", "steps: the parameters' count off by one", NARR,
     'QueryWriter.CutLine(parameters.Cut.Count, "parameter(s)"', 'QueryWriter.CutLine(parameters.Cut.Count - 1, "parameter(s)"'),
    ("N3", "steps: a scoped tree cuts its parameters too", NARR, "var parameters = scope is null", "var parameters = true"),
    ("N4", "steps: a step's attachment cut names the scenario", NARR,
     'step.Attachments, $"{indent}      ", $"attachments {scenario.Address}/{path}");',
     'step.Attachments, $"{indent}      ", $"attachments {scenario.Address}");'),
    ("N5", "services: no +N more in the column", OVERVIEW,
     '$"{shown} +{_statuses.Count - MaxStatusesShown} more"', "shown"),
    ("N6", "services: no line naming the JSON", OVERVIEW, "if (statusesCut && !writer.Json)", "if (false)"),
    ("N7", "sizes: largestScenariosTotal is the ten", SIZES,
     'writer.Data("largestScenariosTotal", sizes.Scenarios.Count);', 'writer.Data("largestScenariosTotal", LargestScenarios);'),
    ("N8", "grep --values: no more paths", SEARCH,
     '(p == shown - 1 ? MorePaths(paths.Count - shown, body.Hash) : "")', '""'),
    ("N9", "grep --number: no more paths", NUMBERGREP, "if (found > MaxPathsPerBody)", "if (false)"),
    ("N10", "diff --body: the total counts the listed lines only", DIFF,
     "var differences = rows.Count + notListed;", "var differences = rows.Count;"),
    ("N11", "diff --body: no line for the lines not listed", DIFF, "if (notListed > 0)", "if (false)"),
    ("N12", "compare: the heading counts the listed differences", SEARCH,
     '$", {differing.Count} differ"', '$", {Math.Min(differing.Count, MaxDifferencesShown)} differ"'),
    ("N13", "compare: no cut line", SEARCH, "if (differing.Count > MaxDifferencesShown)", "if (false)"),
    ("N14", "history sN: no earlier-runs line", HISTORY, "if (entry.Points.Count > MaxRunsListed)", "if (false)"),
    ("N15", "http b:: no more-carriers line", PAYLOADS, "if (carried.Occurrences.Count > MaxCarriersListed)", "if (false)"),
    ("N16", "body: no more-carriers line", PAYLOADS, "if (entry.Occurrences.Count > MaxCarriersListed)", "if (false)"),
    ("N17", "Failures.md: no earlier-steps line", FDG, "if (entry.StepsBeforeTotal > entry.Context.Count)", "if (false)"),
    ("N18", "Failures.md: no more-calls line", FDG, "if (entry.CallsTotal > entry.Calls.Count)", "if (false)"),
    ("N19", "Failures.jsonl: stepsBeforeTotal is the listed steps", FDG,
     '["stepsBeforeTotal"] = entry.StepsBeforeTotal,', '["stepsBeforeTotal"] = entry.Context.Count,'),
    ("N20", "Failures.jsonl: callsTotal is the listed calls", FDG,
     '["callsTotal"] = entry.CallsTotal,', '["callsTotal"] = entry.Calls.Count,'),
    ("N21", "digest: the scenario-wide total is the chosen calls", FDG,
     '(chosen, "scenario", requests.Length)', '(chosen, "scenario", chosen.Length)'),
    ("N22", "digest: the failing step's total is the listed calls", FDG,
     'return (inStep, "failingStep", attributedToFailing.Length);', 'return (inStep, "failingStep", inStep.Length);'),
    ("N23", "digest: the steps before it counted one short", FDG,
     "var stepsBeforeTotal = failingIndexes.Length == 0 ? 0 : failingIndexes[0];",
     "var stepsBeforeTotal = failingIndexes.Length == 0 ? 0 : failingIndexes[0] - 1;"),
    ("N24", "CI summary: a green run counts every scenario again", CISUM,
     "var remaining = scenariosWithDiagrams.Length - shown;", "var remaining = features.Sum(f => f.Scenarios.Length) - shown;"),
    ("N25", "summary: no more-failed line", OVERVIEW, "if (failed > 10)", "if (false)"),
    ("N26", "history: no more-absent line", HISTORY, "if (verdicts.Absent.Count > 20)", "if (false)"),
    ("N27", "history --run: eleven runs listed", HISTORY, ".TakeLast(10).Reverse()", ".TakeLast(11).Reverse()"),
    ("N28", "diff of runs: no more-tracking line", SEARCH, "if (trackingRows.Count > 10)", "if (false)"),
    ("N29", "Failures.md: no elision under the further failures", FDG,
     "AppendElision(markdown, further.Length - MaxFurtherFailuresListed, here);", ""),
    ("N30", "Failures.md: no elision under a cluster", FDG,
     "AppendElision(markdown, cluster.Count() - MaxClusterMembersListed, here);", ""),
]


def run(args, log):
    with open(log, "w", encoding="utf-8") as out:
        return subprocess.run(args, cwd=REPO, stdout=out, stderr=subprocess.STDOUT).returncode


def build(log):
    return run(["dotnet", "build", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "-c", "Debug", "-v", "q", "-nologo",
                "-nodeReuse:false", "-p:UseSharedCompilation=false"], log)


def facts(log):
    run(["dotnet", "test", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "--no-build", "--filter", FACTS,
         "--", "RunConfiguration.TreatNoTestsAsError=true"], log)
    text = pathlib.Path(log).read_text(encoding="utf-8", errors="replace")
    failed = sorted(set(re.findall(r"^\s+Failed Kronikol\.Tests\.[\w.]+\.(\w+)", text, re.M)))
    ran = re.search(r"Total:\s+(\d+)", text)
    return failed, int(ran.group(1)) if ran else 0


names = sorted({m[2] for m in MUTATIONS})
originals = {name: (REPO / name).read_bytes() for name in names}
results = []
try:
    for ident, what, name, text, replacement in MUTATIONS:
        if ONLY and ident not in ONLY:
            continue
        path = REPO / name
        source = originals[name].decode("utf-8")
        if source.count(text) != 1:
            results.append((ident, what, f"NOT APPLIED: the text matches {source.count(text)} times"))
            continue
        path.write_bytes(source.replace(text, replacement).encode("utf-8"))
        try:
            if build(LOGS / f"{ident}-build.log") != 0:
                results.append((ident, what, "DID NOT BUILD"))
                continue
            failed, ran = facts(LOGS / f"{ident}-test.log")
            if ran == 0:
                results.append((ident, what, "NO FACTS RAN"))
            else:
                results.append((ident, what, f"KILLED by {', '.join(failed)}" if failed else "SURVIVED"))
        finally:
            path.write_bytes(originals[name])
        print(f"{ident:4} {what}: {results[-1][2]}", flush=True)
finally:
    for name, data in originals.items():
        (REPO / name).write_bytes(data)
    build(LOGS / "restored-build.log")

print()
for ident, what, outcome in results:
    print(f"{ident:4} {what}: {outcome}")
killed = sum(1 for r in results if r[2].startswith("KILLED"))
print(f"{killed} of {len(results)} killed")
sys.exit(0 if killed == len(results) else 1)
