"""Mutation proof for LIST_CAPS_PLAN R1 (#161): each mutation of the new code must make at least one fact fail.

Usage: python -I mutations.py <Kronikol checkout> <log folder> [M1 M2 ...]

Applies one mutation at a time to the working tree, as a text replacement that must match exactly once, builds the test
project, runs the facts of the list cut, records which failed, and puts the file's bytes back (also when the build or the
run fails). The checkout is the one under test, so commit or stash nothing while it runs. Prints one line per mutation:
KILLED (with the facts that failed), SURVIVED, or DID NOT BUILD (not a valid mutation). Exits 1 unless every one is killed.
"""
import pathlib
import re
import subprocess
import sys

REPO = pathlib.Path(sys.argv[1]).resolve()
LOGS = pathlib.Path(sys.argv[2]).resolve()
ONLY = set(sys.argv[3:])
LOGS.mkdir(parents=True, exist_ok=True)

NARR = "src/Kronikol/Query/QueryCommand.Narrative.cs"
QW = "src/Kronikol/Query/QueryWriter.cs"
FDG = "src/Kronikol/Reports/FailuresDigestGenerator.cs"
FACTS = ("FullyQualifiedName~QueryAttachmentListTests|FullyQualifiedName~QueryListCutTests"
         "|FullyQualifiedName~DigestStepAttachmentTests|FullyQualifiedName~CucumberAttachmentIngestTests")

# (id, what it does, file, text, replacement)
MUTATIONS = [
    ("M1", "keep the first six (head 6)", NARR,
     "private const int AttachmentsHead = 1;", "private const int AttachmentsHead = 6;"),
    ("M2", "drop the cut line", NARR,
     "        if (cut.Cut.Count > 0)\n            writer.Line(indent + QueryWriter.CutLine(",
     "        if (false)\n            writer.Line(indent + QueryWriter.CutLine("),
    ("M3", "count off by one", NARR,
     'QueryWriter.CutLine(cut.Cut.Count, "attachment(s)"', 'QueryWriter.CutLine(cut.Cut.Count - 1, "attachment(s)"'),
    ("M4", "attachmentsTotal is the printed count", NARR,
     "attachmentsTotal = scenario.Attachments.Count,", "attachmentsTotal = Math.Min(scenario.Attachments.Count, MaxAttachmentsShown),"),
    ("M5", "a step address lists the scenario's own attachments too", NARR,
     "        if (scope is null)\n            rows.AddRange(scenario.Attachments",
     "        if (true)\n            rows.AddRange(scenario.Attachments"),
    ("M6", "a step's cut line names the scenario", NARR,
     '$"  {indent}  ", $"attachments {scenario.Address}/{path}");', '$"  {indent}  ", $"attachments {scenario.Address}");'),
    ("M7", "the other-steps count includes the failing steps", NARR,
     ".Where(s => !failingPaths.Contains(s.Path)).SelectMany(", ".Where(s => true).SelectMany("),
    ("M8", "the JSON lists every attachment", NARR,
     "            attachments = QueryWriter.CutList(scenario.Attachments, MaxAttachmentsShown, AttachmentsHead).Shown.Select(",
     "            attachments = scenario.Attachments.Select("),
    ("M9", "the Failures.md column counts the scenario's own attachments only", FDG,
     '(entry.Attachments.Count > 0 ? entry.Attachments.Count.ToString(CultureInfo.InvariantCulture) : "")',
     '(entry.Attachments.Count(a => a.Step is null) > 0 ? entry.Attachments.Count(a => a.Step is null).ToString(CultureInfo.InvariantCulture) : "")'),
    ("M10", "attachments lists the steps' attachments before the scenario's own", NARR,
     "        if (scope is null)\n"
     "            rows.AddRange(scenario.Attachments.Select(a => (scenario.Address, a, (string?)null)));\n"
     "        foreach (var (path, _, step) in scenario.AllSteps().Where(row => scope is null || Address.PathCoveredBy(row.Path, scope)))\n"
     "            rows.AddRange(step.Attachments.Select(a => ($\"{scenario.Address}/{path}\", a, (string?)$\"{scenario.Address}/{path}\")));\n",
     "        foreach (var (path, _, step) in scenario.AllSteps().Where(row => scope is null || Address.PathCoveredBy(row.Path, scope)))\n"
     "            rows.AddRange(step.Attachments.Select(a => ($\"{scenario.Address}/{path}\", a, (string?)$\"{scenario.Address}/{path}\")));\n"
     "        if (scope is null)\n"
     "            rows.AddRange(scenario.Attachments.Select(a => (scenario.Address, a, (string?)null)));\n"),
    ("M11", "repeated names are not grouped", QW,
     "            .GroupBy(name => Flat(name), StringComparer.Ordinal)\n"
     "            .Select(group => group.Count() > 1 ? $\"{group.Key} ×{group.Count().ToString(CultureInfo.InvariantCulture)}\" : group.Key)\n",
     "            .Select(name => Flat(name))\n"),
    ("M12", "attachments resumes one entry late", NARR,
     'writer.Page(rows, options.Offset, options.PageSize(200, writer, "attachments")',
     'writer.Page(rows, options.Offset > 0 ? options.Offset + 1 : 0, options.PageSize(200, writer, "attachments")'),
    ("M13", "steps prints its attachments line in a scoped view", NARR,
     "        if (scope is null)\n        {\n            var own = scenario.Attachments.Count;",
     "        if (true)\n        {\n            var own = scenario.Attachments.Count;"),
    ("M14", "the name list is cut with OneLine at 100 characters", QW,
     'return taken < counted.Count ? list.Append(", …").ToString() : list.ToString();',
     'return OneLine(string.Join(", ", counted), 100);'),
    ("M15", "callsTotal is the printed count", NARR,
     'callsTotal = scenario.Interactions.Count(i => i.StepPath == s.Path && i.Type == "Request"),',
     'callsTotal = Math.Min(scenario.Interactions.Count(i => i.StepPath == s.Path && i.Type == "Request"), MaxFailingStepCalls),'),
    ("M16", "drop the calls cut line", NARR,
     "                    if (scoped.Length > MaxFailingStepCalls)\n",
     "                    if (false)\n"),
    ("M17", "steps counts the top-level steps' attachments only", NARR,
     "            var onSteps = scenario.AllSteps().Sum(s => s.Step.Attachments.Count);",
     "            var onSteps = scenario.Steps.Sum(s => s.Attachments.Count);"),
    ("M18", "stepAttachmentsTotal counts the top-level steps only", NARR,
     "stepAttachmentsTotal = scenario.AllSteps().Sum(s => s.Step.Attachments.Count)",
     "stepAttachmentsTotal = scenario.Steps.Sum(s => s.Attachments.Count)"),
    ("M19", "a failing step's JSON keeps its first six", NARR,
     "attachments = QueryWriter.CutList(s.Step.Attachments, MaxAttachmentsShown, AttachmentsHead).Shown.Select(",
     "attachments = s.Step.Attachments.Take(MaxAttachmentsShown).Select("),
]


def run(args, log):
    with open(log, "w", encoding="utf-8") as out:
        return subprocess.run(args, cwd=REPO, stdout=out, stderr=subprocess.STDOUT).returncode


def build(log):
    return run(["dotnet", "build", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "-c", "Debug", "-v", "q", "-nologo"], log)


def facts(log):
    run(["dotnet", "test", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "--no-build", "--filter", FACTS,
         "--", "RunConfiguration.TreatNoTestsAsError=true"], log)
    text = pathlib.Path(log).read_text(encoding="utf-8", errors="replace")
    failed = sorted(set(re.findall(r"^\s+Failed Kronikol\.Tests\.[\w.]+\.(\w+)", text, re.M)))
    ran = re.search(r"Total:\s+(\d+)", text)
    return failed, int(ran.group(1)) if ran else 0


originals = {name: (REPO / name).read_bytes() for name in (NARR, QW, FDG)}
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
finally:
    for name, data in originals.items():
        (REPO / name).write_bytes(data)
    build(LOGS / "restored-build.log")

for ident, what, outcome in results:
    print(f"{ident:4} {what}: {outcome}")
killed = sum(1 for r in results if r[2].startswith("KILLED"))
print(f"{killed} of {len(results)} killed")
sys.exit(0 if killed == len(results) else 1)
