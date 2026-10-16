"""Mutations for the #175 audit's fixes (plans/ATTACHMENT_MERGE_PLAN.md section 10, the audit).

Run from anywhere: python -I mutations.py <worktree> <results dir> [tags, comma-separated]. Each mutant is an exact,
single-occurrence text replacement in one source file. The file's bytes are restored after each mutant whatever
happens, so no git checkout is involved. A mutant is KILLED when at least one fact fails, SURVIVED when every fact
passes, and DID NOT COMPILE when the build fails (never counted as killed).
"""
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

WT = sys.argv[1]
OUT = sys.argv[2]
os.makedirs(OUT, exist_ok=True)

MERGER = "src/Kronikol/Ingestion/Cucumber/CucumberFeatureMerger.cs"
SYNTH = "src/Kronikol/Ingestion/Cucumber/CucumberFeatureSynthesizer.cs"
ATTEMPTS = "src/Kronikol/Ingestion/TestAttempts.cs"
INDEX = "src/Kronikol/Query/ReportIndex.cs"
NARRATIVE = "src/Kronikol/Query/QueryCommand.Narrative.cs"
RENDERER = "src/Kronikol/Reports/Merge/MergeableReportRenderer.cs"

FILTER = ("FullyQualifiedName~Kronikol.Tests.Ingestion|FullyQualifiedName~DigestStepAttachmentTests"
          "|FullyQualifiedName~MergeCarriesAttachmentsTests")

MUTANTS = [
    # The audit's own fixes.
    ("A1", MERGER, "one pass: a gone file pairs in the reporter's order (X2)",
     """            if (Describe(fromTest.RelativePath) is not { } mine)
            {
                unreadable.Add(i);
                continue;
            }""",
     """            if (Describe(fromTest.RelativePath) is not { } mine)
            {
                if (First(Namesakes(fromTest).Where(n => BytesOf(n.Candidate) is not null)) is { } early)
                    Take(i, early, new FileAttachment(fromTest.Name, early.Candidate.RelativePath, early.Candidate.MediaType ?? fromTest.MediaType));
                continue;
            }"""),
    ("A2", MERGER, "a reporter's file that exists but cannot be read pairs with nothing (X3)",
     """            if (Describe(fromTest.RelativePath) is not { } mine)
            {
                unreadable.Add(i);
                continue;
            }""",
     """            if (!File.Exists(fromTest.RelativePath))
            {
                unreadable.Add(i);
                continue;
            }
            if (Describe(fromTest.RelativePath) is not { } mine)
                continue;"""),
    ("A3", MERGER, "no pair with a copy whose bytes cannot be read (X4)",
     """
                        ?? First(Namesakes(fromTest).Where(n => BytesOf(n.Candidate) is null));""",
     ";"),
    ("A4", MERGER, "the replaced copy deleted when paired, linked or not (X5)",
     "                replaced.Add(written);",
     "                TryDelete(written);"),
    ("A5", MERGER, "the tests scenario's own list only (X1)",
     "        FileAttachment[] attachments = [.. AllAttachments(fromTests)];",
     "        FileAttachment[] attachments = [.. fromTests.Attachments ?? []];"),
    ("A6", MERGER, "a gone file pairs with any namesake, readable or not",
     "            if (First(Namesakes(fromTest).Where(n => BytesOf(n.Candidate) is not null)) is { } slot)",
     "            if (First(Namesakes(fromTest)) is { } slot)"),
    ("A7", MERGER, "kept twice beside a namesake whose bytes were not read (expected equivalent)",
     "            !fromTest.IsUrl && First(Namesakes(fromTest).Where(n => BytesOf(n.Candidate) is not null)) is not null",
     "            !fromTest.IsUrl && First(Namesakes(fromTest)) is not null"),
    ("T1", ATTEMPTS, "an untimed record placed by the counted starts alone (X7)",
     """        if (uncounted.Length > 0)
            return Math.Clamp(uncounted.Count(start => start < index) - 1, 0, starts.Count - 1);
""",
     ""),
    ("T2", ATTEMPTS, "an untimed record one attempt late (X7)",
     "uncounted.Count(start => start < index) - 1",
     "uncounted.Count(start => start < index)"),
    ("S1", SYNTH, "an unresolved test step's attachments dropped (X8)",
     """                if (stepAttachments is not null)
                    scenarioAttachments.AddRange(stepAttachments);
                continue;
            }
""",
     """                continue;
            }
"""),
    ("S2", SYNTH, "every kept hook after the steps (X9)",
     "                (sawStep ? hookStepsAfter : hookStepsBefore).Add(hookStep);",
     "                hookStepsAfter.Add(hookStep);"),
    ("S3", SYNTH, "before and after hooks swapped (X9)",
     "                (sawStep ? hookStepsAfter : hookStepsBefore).Add(hookStep);",
     "                (sawStep ? hookStepsBefore : hookStepsAfter).Add(hookStep);"),
    ("Q1", INDEX, "a link resolved as a path (C1)",
     """        if (FileAttachment.IsUrlPath(RelativePath))
            return RelativePath;
""",
     ""),
    ("Q2", INDEX, "a path no file system takes throws (C2)",
     """        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {""",
     """        catch (Exception ex) when (ex is ArgumentException && false)
        {"""),
    ("Q3", NARRATIVE, "text: every attachment of the failing step (C3)",
     "                foreach (var attachment in step.Attachments.Take(MaxFailingStepAttachments))",
     "                foreach (var attachment in step.Attachments)"),
    ("Q4", NARRATIVE, "json: every attachment of the failing step (C3)",
     "attachments = s.Step.Attachments.Take(MaxFailingStepAttachments).Select(",
     "attachments = s.Step.Attachments.Select("),
    ("Q5", NARRATIVE, "no line naming the rest (C3)",
     "                if (step.Attachments.Count > MaxFailingStepAttachments)",
     "                if (step.Attachments.Count > MaxFailingStepAttachments && false)"),
    ("M1", RENDERER, "a shard's attachments left relative to its folder (P1)",
     "                PointAttachmentsAtTheirShard(report, Path.GetDirectoryName(Path.GetFullPath(path)) ?? \"\");",
     ""),
    ("M2", RENDERER, "nothing copied beside the merged report (P1)",
     "            CarryAttachments(report, destinationDirectory);",
     "            _ = destinationDirectory;"),
    ("M3", RENDERER, "a name another shard's file holds is written over (P1)",
     """                    if (!File.Exists(destination))
                    {
                        File.Copy(source, destination);""",
     """                    if (true)
                    {
                        File.Copy(source, destination, overwrite: true);"""),
    ("M4", RENDERER, "no fast path for a file already in the folder (expected equivalent: same bytes reused)",
     """            if (string.Equals(Path.GetDirectoryName(source), folder, comparison))
                return "attachments/" + Path.GetFileName(source);
""",
     ""),
    # The release's own mutants, on the rewritten merge (r1/mutations.txt).
    ("R1", MERGER, "M1: the displayed name only",
     """            string.Equals(cucumber.AttachmentOrigins.GetValueOrDefault(candidate)?.OriginalName ?? candidate.Name, fromTest.Name, StringComparison.OrdinalIgnoreCase)
            || """,
     "            "),
    ("R2", MERGER, "M2: no byte check",
     "First(Namesakes(fromTest).Where(n => BytesOf(n.Candidate) is { } copy && copy.Length == mine.Length && copy.Hash.AsSpan().SequenceEqual(mine.Hash)))",
     "First(Namesakes(fromTest))"),
    ("R2b", MERGER, "the length alone",
     " && copy.Hash.AsSpan().SequenceEqual(mine.Hash)",
     ""),
    ("R3", MERGER, "M3: a paired slot offered again",
     "            taken.Add((slot.Holder, slot.Index));",
     "            _ = slot;"),
    ("R4", MERGER, "M4: the reporter's path survives when its file is gone",
     "Take(i, slot, new FileAttachment(fromTest.Name, slot.Candidate.RelativePath, slot.Candidate.MediaType ?? fromTest.MediaType));",
     "Take(i, slot, new FileAttachment(fromTest.Name, fromTest.RelativePath, slot.Candidate.MediaType ?? fromTest.MediaType));"),
    ("R9", MERGER, "M9: a link offered as a copy",
     "                    if (items[index] is { IsUrl: false } candidate && !taken.Contains((holder, index)) && SameName(fromTest, candidate))",
     "                    if (items[index] is { } candidate && !taken.Contains((holder, index)) && SameName(fromTest, candidate))"),
    ("R10", MERGER, "M10: the messages' unpaired entries first",
     "            FileAttachment[] kept = isScenario ? [.. scenarioList, .. items.OfType<FileAttachment>()] : [.. items.OfType<FileAttachment>()];",
     "            FileAttachment[] kept = isScenario ? [.. items.OfType<FileAttachment>(), .. scenarioList] : [.. items.OfType<FileAttachment>()];"),
    ("R11", MERGER, "M11: replaced copies never deleted",
     "                TryDelete(written);",
     "                _ = written;"),
    ("R15", MERGER, "M15: a gone pair takes the tests' media type",
     "slot.Candidate.RelativePath, slot.Candidate.MediaType ?? fromTest.MediaType));",
     "slot.Candidate.RelativePath, fromTest.MediaType ?? slot.Candidate.MediaType));"),
    ("R17", MERGER, "M17: the kept-twice count never made",
     "                    keptTwice++;",
     "                    keptTwice += 0;"),
    ("R22", MERGER, "M22: a reporter's file takes the copy's media type",
     "Take(i, slot, new FileAttachment(fromTest.Name, fromTest.RelativePath, fromTest.MediaType ?? slot.Candidate.MediaType));",
     "Take(i, slot, new FileAttachment(fromTest.Name, fromTest.RelativePath, slot.Candidate.MediaType ?? fromTest.MediaType));"),
]


def run(args, log):
    with open(log, "w", encoding="utf-8") as fh:
        return subprocess.run(args, cwd=WT, stdout=fh, stderr=subprocess.STDOUT).returncode


def build(tag):
    log = os.path.join(OUT, f"{tag}.build.log")
    if run(["dotnet", "build", "src/Kronikol/Kronikol.csproj", "-f", "net10.0", "-v", "q", "-nologo"], log) != 0:
        return False
    return run(["dotnet", "build", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "-f", "net10.0", "-p:BuildProjectReferences=false",
                "-v", "q", "-nologo"], log + "2") == 0


def facts(tag):
    trx = os.path.join(OUT, f"{tag}.trx")
    if os.path.exists(trx):
        os.remove(trx)
    run(["dotnet", "test", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "-f", "net10.0", "--no-build", "--filter", FILTER,
         "--logger", f"trx;LogFileName={tag}.trx", "--results-directory", OUT], os.path.join(OUT, f"{tag}.test.log"))
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    root = ET.parse(trx).getroot()
    results = list(root.iterfind(".//t:UnitTestResult", ns))
    failed = [r.get("testName").split(".")[-1] for r in results if r.get("outcome") == "Failed"]
    return len(results), failed


lines = []


def say(text):
    print(text, flush=True)
    lines.append(text)


# The baseline: a full build, and every fact green.
with open(os.path.join(OUT, "baseline.build.log"), "w", encoding="utf-8") as fh:
    full = subprocess.run(["dotnet", "build", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "-f", "net10.0", "-v", "q", "-nologo"],
                          cwd=WT, stdout=fh, stderr=subprocess.STDOUT).returncode
total, failed = facts("baseline")
say(f"baseline: build {'ok' if full == 0 else 'FAILED'}, {total} facts, failed: {failed or 'none'}")
if full != 0 or failed:
    sys.exit(1)

ONLY = set(sys.argv[3].split(",")) if len(sys.argv) > 3 else None
for tag, path, description, old, new in MUTANTS:
    if ONLY is not None and tag not in ONLY:
        continue
    file = os.path.join(WT, path)
    original = open(file, "rb").read()
    text = original.decode("utf-8")
    count = text.count(old)
    if count != 1:
        say(f"{tag}: NOT APPLIED ({count} occurrences) - {description}")
        continue
    try:
        open(file, "wb").write(text.replace(old, new).encode("utf-8"))
        if not build(tag):
            say(f"{tag}: DID NOT COMPILE - {description}")
            continue
        total, failed = facts(tag)
        verdict = f"KILLED by {len(failed)}: {', '.join(failed[:4])}{' ...' if len(failed) > 4 else ''}" if failed else "SURVIVED"
        say(f"{tag}: {verdict} - {description}")
    finally:
        open(file, "wb").write(original)

build("restored")
open(os.path.join(OUT, "mutations.txt"), "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
