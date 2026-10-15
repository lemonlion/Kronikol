"""Mutation run for R1 (plans/ATTACHMENT_MERGE_PLAN.md section 6.4): each mutant is applied to a copy of the release
tree, built, and run against the facts of the four classes that hold R1's facts. A mutant is killed when one of them
fails, and the facts that failed are recorded.

usage: python mutations.py <tree> <out file> [mutation id ...]
The tree must be a clean git worktree of the release; every mutated file is restored with git after its run.
"""
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

tree, out = sys.argv[1], sys.argv[2]
only = set(sys.argv[3:])

MERGER = 'src/Kronikol/Ingestion/Cucumber/CucumberFeatureMerger.cs'
PIPELINE = 'src/Kronikol/Ingestion/IngestPipeline.cs'
DIGEST = 'src/Kronikol/Reports/FailuresDigestGenerator.cs'
QUERY = 'src/Kronikol/Query/QueryCommand.Narrative.cs'

CLASSES = ['CucumberAttachmentIngestTests', 'CucumberFeatureMergerTests', 'DigestStepAttachmentTests', 'CucumberIngestPipelineTests']

M = [
    ('M1', 'compare the displayed name only (today\'s key)', [(MERGER,
        "            string.Equals(cucumber.AttachmentOrigins.GetValueOrDefault(candidate)?.OriginalName ?? candidate.Name, fromTest.Name, StringComparison.OrdinalIgnoreCase)\n"
        "            || string.Equals(candidate.Name, fromTest.Name, StringComparison.OrdinalIgnoreCase);",
        "            string.Equals(candidate.Name, fromTest.Name, StringComparison.OrdinalIgnoreCase);")]),
    ('M2', 'no byte check', [(MERGER,
        "                        if (!SameBytes(fromTest.RelativePath, candidate, origin))\n                            continue;\n", "")]),
    ('M3', 'a paired slot offered again', [(MERGER,
        "if (items[index] is not { IsUrl: false } candidate || taken.Contains((holder, index)) || !SameName(fromTest, candidate))",
        "if (items[index] is not { IsUrl: false } candidate || !SameName(fromTest, candidate))")]),
    ('M4', 'the reporter\'s copy always survives', [(MERGER,
        "merged = new FileAttachment(fromTest.Name, candidate.RelativePath, candidate.MediaType ?? fromTest.MediaType);",
        "merged = new FileAttachment(fromTest.Name, fromTest.RelativePath, candidate.MediaType ?? fromTest.MediaType);")]),
    ('M5', 'the messages\' copy always survives', [(MERGER,
        "                        if (origin?.WrittenPath is { } written)\n                            TryDelete(written);\n"
        "                        merged = new FileAttachment(fromTest.Name, fromTest.RelativePath, fromTest.MediaType ?? candidate.MediaType);",
        "                        merged = new FileAttachment(fromTest.Name, candidate.RelativePath, fromTest.MediaType ?? candidate.MediaType);")]),
    ('M6', 'every pair to the scenario\'s list', [(MERGER,
        "            if (holders[holder].IsScenario)\n            {\n                holders[holder].Items[index] = null;",
        "            if (holders[holder].IsScenario || true)\n            {\n                holders[holder].Items[index] = null;")]),
    ('M7a', 'the walk skips the background steps', [(MERGER, "        Walk(target.BackgroundSteps);\n", "")]),
    ('M7b', 'the walk skips the hook steps (no keyword)', [(MERGER,
        "            foreach (var step in steps ?? [])\n            {\n                if (step.Attachments is { Length: > 0 } own)",
        "            foreach (var step in (steps ?? []).Where(s => s.Keyword is not null))\n            {\n                if (step.Attachments is { Length: > 0 } own)")]),
    ('M7c', 'the walk skips sub-steps', [(MERGER, "                Walk(step.SubSteps);\n", "")]),
    ('M8a', 'no signal filter', [(MERGER,
        "            if (cucumber.SignalAttachmentNames.Contains(fromTest.Name))\n                continue;\n", "")]),
    ('M8b', 'the signal filter on every scenario', [(MERGER,
        "            var keep = feature.Scenarios.Where(s => !owned.Contains(s.Id)).ToArray();\n",
        "            var keep = feature.Scenarios.Where(s => !owned.Contains(s.Id)).ToArray();\n"
        "            foreach (var s in keep) s.Attachments = s.Attachments?.Where(a => !cucumber.SignalAttachmentNames.Contains(a.Name)).ToArray();\n")]),
    ('M9', 'a link paired', [(MERGER, "            if (!fromTest.IsUrl)\n            {\n                var reportersFileExists",
        "            if (fromTest is not null)\n            {\n                var reportersFileExists")]),
    ('M10', 'the messages\' unpaired entries first', [(MERGER,
        "FileAttachment[] kept = isScenario ? [.. scenarioList, .. items.OfType<FileAttachment>()]",
        "FileAttachment[] kept = isScenario ? [.. items.OfType<FileAttachment>(), .. scenarioList]")]),
    ('M11', 'replaced copies not deleted', [(MERGER,
        "                        if (origin?.WrittenPath is { } written)\n                            TryDelete(written);\n", "")]),
    ('M12a', 'the staging folder kept', [(PIPELINE,
        "        if (directory is null || !Directory.Exists(directory))\n            return;\n        var inside",
        "        if (directory is null || !Directory.Exists(directory) || directory.Length > 0)\n            return;\n        var inside")]),
    ('M12b', 'the staging folder deleted while the report links into it', [(PIPELINE,
        "            .Any(a => a.RelativePath.StartsWith(inside, StringComparison.OrdinalIgnoreCase)))\n            return;\n",
        "            .Any(a => a.RelativePath.StartsWith(inside, StringComparison.OrdinalIgnoreCase)))\n            { }\n")]),
    ('M13', 'an ordinal (case-sensitive) name comparison', [(MERGER,
        "            string.Equals(cucumber.AttachmentOrigins.GetValueOrDefault(candidate)?.OriginalName ?? candidate.Name, fromTest.Name, StringComparison.OrdinalIgnoreCase)\n"
        "            || string.Equals(candidate.Name, fromTest.Name, StringComparison.OrdinalIgnoreCase);",
        "            string.Equals(cucumber.AttachmentOrigins.GetValueOrDefault(candidate)?.OriginalName ?? candidate.Name, fromTest.Name, StringComparison.Ordinal)\n"
        "            || string.Equals(candidate.Name, fromTest.Name, StringComparison.Ordinal);")]),
    ('M14', 'the digest reads the scenario\'s own list only (today\'s)', [(DIGEST,
        "                .. ordered.SelectMany(step => (step.Step.Attachments ?? []).Select(a => new AttachmentLine(a, $\"{address}/{step.Path}\"))),\n", "")]),
    ('M15', 'the merged entry takes the tests\' media type when the messages\' copy survives', [(MERGER,
        "merged = new FileAttachment(fromTest.Name, candidate.RelativePath, candidate.MediaType ?? fromTest.MediaType);",
        "merged = new FileAttachment(fromTest.Name, candidate.RelativePath, fromTest.MediaType ?? candidate.MediaType);")]),
    ('M16', 'query failures prints the failing step\'s attachments in the scenario\'s capped lines instead', [
        (QUERY,
         "                foreach (var attachment in step.Attachments)\n"
         "                    writer.Line($\"  {new string(' ', depth * 2)}  attachment: {attachment.Name} → {attachment.Resolve(index.Directory)}\");\n", ""),
        (QUERY, "                foreach (var attachment in scenario.Attachments.Take(4))",
         "                foreach (var attachment in scenario.Attachments.Concat(failingSteps.SelectMany(s => s.Step.Attachments)).Take(4))")]),
    ('M17', 'the kept-twice count is never made', [(MERGER,
        "var keptTwice = unpaired.Count(KeptBesideItsNamesake);", "var keptTwice = unpaired.Count(KeptBesideItsNamesake) * 0;")]),
    ('M18', 'the merge drops the tests file\'s defaulted count (4.22.2)', [(MERGER,
        "DefaultedResultCount = features.SelectMany(f => f.Scenarios).Count(s => s.ResultDefaulted),", "DefaultedResultCount = 0,")]),
    ('M19', 'query failures --json leaves out the failing step\'s attachments', [(QUERY,
        "attachments = s.Step.Attachments.Select(a => new { name = a.Name, path = a.Resolve(index.Directory) })",
        "attachments = s.Step.Attachments.Take(0).Select(a => new { name = a.Name, path = a.Resolve(index.Directory) })")]),
    ('M20', 'Failures.jsonl leaves out the step member', [(DIGEST,
        ": new { a.Attachment.Name, Path = a.Attachment.RelativePath, a.Step }).ToArray()",
        ": new { a.Attachment.Name, Path = a.Attachment.RelativePath }).ToArray()")]),
    ('M21', 'Failures.md leaves out the step label', [(DIGEST,
        "+ (step is null ? \"\" : $\" (step {Code(step)})\") + \"\\n\");", "+ \"\\n\");")]),
    ('M22', 'a reporter\'s file takes the messages\' media type', [(MERGER,
        "merged = new FileAttachment(fromTest.Name, fromTest.RelativePath, fromTest.MediaType ?? candidate.MediaType);",
        "merged = new FileAttachment(fromTest.Name, fromTest.RelativePath, candidate.MediaType ?? fromTest.MediaType);")]),
]

NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def run(args, **kw):
    return subprocess.run(args, cwd=tree, capture_output=True, text=True, encoding='utf-8', errors='replace', **kw)


lines = []
for mid, what, edits in M:
    if only and mid not in only:
        continue
    files = sorted({f for f, _, _ in edits})
    originals = {f: open(os.path.join(tree, f), encoding='utf-8', newline='').read() for f in files}
    texts = dict(originals)
    for f, old, new in edits:
        assert texts[f].count(old) == 1, f'{mid}: {old!r} found {texts[f].count(old)} times in {f}'
        texts[f] = texts[f].replace(old, new)
    try:
        for f in files:
            open(os.path.join(tree, f), 'w', encoding='utf-8', newline='').write(texts[f])
        build = run(['dotnet', 'build', 'tests/Kronikol.Tests', '-f', 'net10.0', '-v', 'q', '-nologo', '--no-restore'])
        if build.returncode != 0:
            errors = sorted(set(re.findall(r'error CS\d+[^\[]*', build.stdout)))
            lines.append(f'{mid}  DID NOT COMPILE  {what}: {errors[:3]}')
            print(lines[-1], flush=True)
            continue
        trx = os.path.abspath(os.path.join(os.path.dirname(out), f'mut-{mid}.trx'))
        if os.path.exists(trx):
            os.remove(trx)
        run(['dotnet', 'test', 'tests/Kronikol.Tests', '-f', 'net10.0', '--no-build',
             '--filter', '|'.join(f'FullyQualifiedName~{c}' for c in CLASSES),
             '--logger', f'trx;LogFileName={trx}'], timeout=1800)
        failed = []
        for result in ET.parse(trx).getroot().iterfind('.//t:UnitTestResult', NS):
            if result.get('outcome') == 'Failed':
                failed.append(result.get('testName').split('.')[-1])
        verdict = 'KILLED  ' if failed else 'SURVIVED'
        lines.append(f'{mid}  {verdict}  {what}' + (f'\n      by {", ".join(sorted(failed))}' if failed else ''))
        print(lines[-1], flush=True)
    finally:
        for f in files:
            open(os.path.join(tree, f), 'w', encoding='utf-8', newline='').write(originals[f])

with open(out, 'w', encoding='utf-8', newline='\n') as f:
    f.write('\n'.join(lines) + '\n')
