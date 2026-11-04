"""R2's mutations (the plan's section 8.5), one at a time in a scratch worktree: apply, build, run the facts, restore. Each must turn a fact red.

Usage: mutations_r2.py <worktree> <out file> [name...]
A build failure is reported as BUILD FAILED, never as caught.
"""
import pathlib
import re
import subprocess
import sys

WT = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2])
ONLY = set(sys.argv[3:])
BAR = "src/Kronikol/PlantUml/StepBarPlantUml.cs"
GEN = "src/Kronikol/Reports/ReportGenerator.cs"
SYN = "src/Kronikol/Ingestion/FeatureSynthesizer.cs"
PIPE = "src/Kronikol/Ingestion/IngestPipeline.cs"
MERGE = "src/Kronikol/Ingestion/Cucumber/CucumberFeatureMerger.cs"
FILTER = "FullyQualifiedName~StepWalkTests|FullyQualifiedName~ReportAttributionTests|FullyQualifiedName~CucumberFeatureMergerTests|FullyQualifiedName~BDDfyScenarioInfoExtensionsTests"

M = [
    ("matcher-passes-everything", GEN,
     r"""|| (stepText == ScenarioStep.NoText""",
     r"""|| true || (stepText == ScenarioStep.NoText"""),
    ("published-check-gone", GEN,
     r"""return marker.Contains(stepText, StringComparison.OrdinalIgnoreCase)
               || marker.Contains(StepText.CapitaliseIfEnabled(stepText) ?? stepText, StringComparison.OrdinalIgnoreCase)
               || (stepText""",
     r"""return (stepText"""),
    ("case-ignored-past-the-first-letter", BAR,
     r"""if (cut && drawn.StartsWith(kept, StringComparison.Ordinal))
            return true;

        return kept.StartsWith(drawn, StringComparison.Ordinal)""",
     r"""if (cut && drawn.StartsWith(kept, StringComparison.OrdinalIgnoreCase))
            return true;

        return kept.StartsWith(drawn, StringComparison.OrdinalIgnoreCase)"""),
    ("first-letter-flips-under-a-rows-keyword", BAR,
     r"""(hasKeyword && DrawsLabel(content, budget, $"{keyword} {text}"))""",
     r"""(hasKeyword && DrawsText(content, budget, $"{keyword} ", text))"""),
    ("no-first-letter-as-StepText-would", BAR,
     r"""        var capitalised = Reports.StepText.Capitalise(text) ?? text;
        if (capitalised != text)""",
     r"""        return false;
#pragma warning disable CS0162
        var capitalised = Reports.StepText.Capitalise(text) ?? text;
        if (capitalised != text)"""),
    ("no-text-takes-a-first-word", GEN,
     r"""ScenarioStep.NoTextOnABar, anyFirstWord: false)""",
     r"""ScenarioStep.NoTextOnABar, anyFirstWord: true)"""),
    ("first-word-before-own-keyword-not-read", BAR,
     r""" || DrawsLabel(content, budget, $"{before}{keyword} {text}")""",
     r""""""),
    ("folds-spaces", BAR,
     r"""var kept = cut ? content[..^PlantUmlStatementLimits.TruncationMarker.Length] : content;""",
     r"""var kept = (cut ? content[..^PlantUmlStatementLimits.TruncationMarker.Length] : content).Replace(" ", ""); drawn = drawn.Replace(" ", "");"""),
    ("first-line-alone", BAR,
     r"""var drawn = string.Join(@"\n", lines.Skip(1).SelectMany(EscapeWrappedBodyLine).Prepend(LabelLine(lines[0])));""",
     r"""var drawn = LabelLine(lines[0]);"""),
    ("any-line-break-after-the-text", BAR,
     r"""return cut && rest.SequenceEqual(@"\n");""",
     r"""return rest.StartsWith(@"\n", StringComparison.Ordinal);"""),
    ("any-blank-line-after-a-label", BAR,
     r"""        rest = rest[4..];
        if (rest.IsEmpty)
            return cut;""",
     r"""        return true;
#pragma warning disable CS0162
        rest = rest[4..];
        if (rest.IsEmpty)
            return cut;"""),
    ("cut-after-a-tables-name-not-read", BAR,
     r"""return table.StartsWith("|", StringComparison.Ordinal) || (cut && table.IsEmpty);""",
     r"""return table.StartsWith("|", StringComparison.Ordinal);"""),
    ("cut-inside-a-tables-name-not-read", BAR,
     r"""        if (nameEnd < 0)
            return cut;""",
     r"""        if (nameEnd < 0)
            return false;"""),
    ("rows-keyword-does-not-reach-it", GEN,
     r"""StepMarkerMatches(log.PlantUml, ordered[stepMarkerCount].Keyword, ordered[stepMarkerCount].Text)""",
     r"""StepMarkerMatches(log.PlantUml, null, ordered[stepMarkerCount].Text)"""),
    ("barless-read-where-a-capture-holds-bars", SYN,
     r"""BarlessRows = barsInCapture is null || barsInCapture.Contains(testId)""",
     r"""BarlessRows = barsInCapture is null"""),
    ("kind-step-not-counted-as-a-bar", PIPE,
     r"""drawnByCapture.Concat(records.Where(r => string.Equals(r.Kind, InteractionRecord.Kinds.Step, StringComparison.OrdinalIgnoreCase)).Select(r => r.TestId)),""",
     r"""drawnByCapture,"""),
    ("capture-assertion-row-left-out", GEN,
     r"""if (scenario.BarlessRows?.Contains(row) != true)""",
     r"""if (scenario.BarlessRows?.Contains(row) != true && !steps![i].IsAssertion)"""),
    ("no-text-matches-any-bar", GEN,
     r"""? Kronikol.PlantUml.StepBarPlantUml.DrawsStep(marker, keyword, ScenarioStep.NoTextOnABar, anyFirstWord: false)""",
     r"""? true"""),
    ("a-response-keeps-its-own-path", GEN,
     r"""paths.Add(log.Type == RequestResponseType.Response && requestPaths.TryGetValue(log.RequestResponseId, out var ofRequest) ? ofRequest : current);""",
     r"""paths.Add(current);"""),
    ("a-repeated-id-keeps-the-first-request", GEN,
     r"""requestPaths[log.RequestResponseId] = current;""",
     r"""requestPaths.TryAdd(log.RequestResponseId, current);"""),
    ("F16-by-value-in-the-synthesizer", SYN,
     r"""new Dictionary<ScenarioStep, List<ScenarioStep>>(ReferenceEqualityComparer.Instance);""",
     r"""new Dictionary<ScenarioStep, List<ScenarioStep>>();"""),
    ("F16-by-value-in-the-merger", MERGE,
     r"""new Dictionary<ScenarioStep, List<ScenarioStep>>(ReferenceEqualityComparer.Instance);""",
     r"""new Dictionary<ScenarioStep, List<ScenarioStep>>();"""),
]


def run(args, timeout=1200):
    return subprocess.run(args, cwd=WT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)


project = "tests/Kronikol.Tests/Kronikol.Tests.csproj"
with OUT.open("a", encoding="utf-8", newline="\n") as out:
    for name, path, old, new in M:
        if ONLY and name not in ONLY:
            continue
        f = WT / path
        original = f.read_bytes()
        text = original.decode("utf-8")
        assert text.count(old) == 1, (name, old[:60], text.count(old))
        f.write_bytes(text.replace(old, new).encode("utf-8"))
        try:
            build = run(["dotnet", "build", project, "-c", "Debug", "-f", "net10.0", "-v", "q", "-nologo"])
            if build.returncode != 0:
                errors = sorted({l.strip()[:170] for l in build.stdout.splitlines() if " error " in l})
                out.write(f"{name}: BUILD FAILED {errors[:2]}\n")
                out.flush()
                continue
            test = run(["dotnet", "test", project, "-c", "Debug", "-f", "net10.0", "--no-build", "--filter", FILTER, "-nologo"])
            failed = sorted(set(re.findall(r"^\s+Failed (\S+?)(?:\(|\s\[)", test.stdout, flags=re.M)))
            rows = re.findall(r'^\s+Failed \S+?\((?:name|scenario): "([^"]+)"', test.stdout, flags=re.M)
            total = re.search(r"Failed:\s+(\d+)", test.stdout)
            verdict = "SURVIVED" if not failed and test.returncode == 0 else "caught"
            out.write(f"{name}: {verdict}, {total.group(1) if total else '?'} failed: {[n.rsplit('.', 1)[-1] for n in failed][:4]} {rows[:6]}\n")
            out.flush()
        finally:
            f.write_bytes(original)
print("done")
