"""R3's mutations (the plan's section 8.5), one at a time: apply, build, run the facts, restore. Each must turn a fact red.

Usage: mutations_r3.py <worktree> <out file> [name...]
A build failure is reported as BUILD FAILED, never as caught.
"""
import pathlib
import re
import subprocess
import sys

WT = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2])
ONLY = set(sys.argv[3:])
COL = "src/Kronikol/Tracking/StepCollector.cs"
GEN = "src/Kronikol/Reports/ReportGenerator.cs"
NAR = "src/Kronikol/Query/QueryCommand.Narrative.cs"
DIG = "src/Kronikol/Reports/FailuresDigestGenerator.cs"
LOGGER = "src/Kronikol/Tracking/RequestResponseLogger.cs"
PROJECT = "tests/Kronikol.Tests.StepTracking/Kronikol.Tests.StepTracking.csproj"
FILTER = "FullyQualifiedName~StepFlow|FullyQualifiedName~StepPathQuery"

M = [
    ("resolver-reads-the-top-level-alone", GEN,
     [("""            var index = 0;
            foreach (var sub in step.SubSteps ?? [])
                Add(sub, $"{path}.{index++}");
""", """            foreach (var sub in step.SubSteps ?? [])
                Add(sub, path);
""")]),
    ("covers-one-way-again", DIG,
     [("Query.Address.PathCoveredBy(attributed, failingStep) || Query.Address.PathCoveredBy(failingStep, attributed);", "Query.Address.PathCoveredBy(failingStep, attributed);")]),
    ("covers-the-other-way-alone", DIG,
     [("Query.Address.PathCoveredBy(attributed, failingStep) || Query.Address.PathCoveredBy(failingStep, attributed);", "Query.Address.PathCoveredBy(attributed, failingStep);")]),
    ("failures-lists-a-call-twice", NAR,
     [("""        Address.PathCoveredBy(call.StepPath, failing)
        && !failingSteps.Any(deeper => deeper.Path != failing
                                       && Address.PathCoveredBy(deeper.Path, failing)
                                       && Address.PathCoveredBy(call.StepPath, deeper.Path));
""", """        Address.PathCoveredBy(call.StepPath, failing);
""")]),
    ("failures-reads-exact-paths", NAR,
     [("""        Address.PathCoveredBy(call.StepPath, failing)
        && !failingSteps.Any(deeper => deeper.Path != failing
                                       && Address.PathCoveredBy(deeper.Path, failing)
                                       && Address.PathCoveredBy(call.StepPath, deeper.Path));
""", """        call.StepPath == failing;
""")]),
    ("json-reads-exact-paths", NAR,
     [("""                    .Where(i => InFailingStep(i, s.Path, failingSteps) && i.Type == "Request")
                    .Take(MaxFailingStepCalls)
""", """                    .Where(i => i.StepPath == s.Path && i.Type == "Request")
                    .Take(MaxFailingStepCalls)
""")]),
    ("steps-counts-its-own-row-alone", NAR,
     [("""for (var at = stepPath; at.Length > 0; at = at.LastIndexOf('.') is var dot and > 0 ? at[..dot] : "")""", """for (var at = stepPath; at.Length > 0; at = "")""")]),
    ("deepest-for-shared", COL,
     [("shared = any ? (shared is null ? null : NearestShared(shared, step)) : step;", "shared = step;")]),
    ("deep-writer-at-the-default", "src/Kronikol/Reports/ReportGenerator.cs",
     [("        MaxDepth = DataFileMaxDepth,\n", "")]),
    ("deep-scanner-at-the-default", "src/Kronikol/Query/ReportScanner.cs",
     [(", MaxDepth = Reports.ReportGenerator.DataFileMaxDepth }", " }")]),
    ("deep-pruner-at-the-old-limit", "src/Kronikol/Compression/ReportJsonPruner.cs",
     [("private const int MaxDepth = Kronikol.Reports.ReportGenerator.DataFileMaxDepth;", "private const int MaxDepth = 64;")]),
    ("too-deep-listed-whole", "src/Kronikol/Tracking/StepCollector.cs",
     [("private const int DeepestListedDepth = 199;", "private const int DeepestListedDepth = 100000;")]),
    ("too-deep-cut-in-silence", "src/Kronikol/Tracking/StepCollector.cs",
     [("""? [$"steps below this one are not listed: a report lists {DeepestListedDepth + 1} levels of them"]
                : null,""", """? null
                : null,""")]),
    ("deep-sizes-at-the-default", "src/Kronikol/Query/ReportSizes.cs",
     [(", MaxDepth = Reports.ReportGenerator.DataFileMaxDepth }", " }")]),
]


def read(path):
    return (WT / path).read_bytes().decode("utf-8")


def write(path, text):
    (WT / path).write_bytes(text.encode("utf-8"))


lines = []
for name, path, edits in M:
    if ONLY and name not in ONLY:
        continue
    original = read(path)
    crlf = "\r\n" in original
    text = original.replace("\r\n", "\n")
    for old, new in edits:
        assert text.count(old) == 1, (name, old[:70], text.count(old))
        text = text.replace(old, new)
    write(path, text.replace("\n", "\r\n") if crlf else text)
    try:
        run = subprocess.run(
            ["dotnet", "test", PROJECT, "-c", "Release", "-p:WarningLevel=0", "-nologo", "--filter", FILTER, "--logger", "console;verbosity=normal"],
            cwd=WT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=400)
        out = run.stdout + run.stderr
        failed = re.findall(r"^\s+Failed Kronikol\S*\.(\S+?)(?:\(| \[)", out, re.M)
        if re.search(r"\berror CS\d+", out):
            lines.append(f"{name}: BUILD FAILED")
        elif failed:
            lines.append(f"{name}: caught by {len(failed)}: {', '.join(sorted(set(failed)))[:400]}")
        elif re.search(r"Catastrophic failure|The active test run was aborted|Test host process crashed|Stack overflow", out):
            lines.append(f"{name}: caught, the test process died")
        elif run.returncode != 0:
            lines.append(f"{name}: caught, no fact named: exit {run.returncode}: {out[-300:]!r}")
        else:
            lines.append(f"{name}: SURVIVED")
    except subprocess.TimeoutExpired:
        lines.append(f"{name}: caught, by a hang past the limit")
    finally:
        write(path, original)
    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(lines[-1][:200], flush=True)
print("# done", flush=True)
