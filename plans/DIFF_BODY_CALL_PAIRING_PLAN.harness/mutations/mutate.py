"""Mutation checks for plans/DIFF_BODY_CALL_PAIRING_PLAN.md section 5.

Usage: python mutate.py <worktree> <results file>

Run in a worktree that is a copy of the branch's state, never the working copy: each mutation edits one source
file, builds the test project, runs the facts below and puts the file back. A mutation that leaves every fact
green is a hole in the facts. The unmutated run comes first and must be green, or nothing after it means anything.
"""
import pathlib
import re
import shutil
import subprocess
import sys

FILTER = "|".join("FullyQualifiedName~Kronikol.Tests.Tool." + c for c in [
    "DiffBodyPairingTests", "DiffFormsTests", "CountFlagTests", "DescribeTests", "QueryCommandTests",
    "BaselineDiffTests", "RetainedRunsTests", "SkillDriftTests", "QueryJsonTests", "RunDiffTests"])

PAIRING = "src/Kronikol/Query/CallPairing.cs"
DIFF = "src/Kronikol/Query/QueryCommand.Diff.cs"
SEARCH = "src/Kronikol/Query/QueryCommand.Search.cs"
OPTIONS = "src/Kronikol/Query/QueryOptions.cs"
PAYLOADS = "src/Kronikol/Query/QueryCommand.Payloads.cs"

# (name, [(file, old, new), ...]) - every old string must occur exactly once.
MUTATIONS = [
    ("rank by ordinal again", [(DIFF,
        "        var pairing = CallPairing.Pair(oldScenario, oldInteraction, match);\n",
        "        var pairing = CallPairing.Pair(oldScenario, oldInteraction, match) with { Partner = match.Interactions.FirstOrDefault(i => i.Ordinal == oldInteraction.Ordinal), OnShape = false, OldCount = 1, NewCount = 1 };\n")]),
    ("drop the half from the key", [(PAIRING,
        "        var mine = scenario.Interactions.Where(i => Is(i, half)).ToList();\n        var theirs = other.Interactions.Where(i => Is(i, half)).ToList();\n",
        "        var mine = scenario.Interactions.ToList();\n        var theirs = other.Interactions.ToList();\n")]),
    ("rank a response among the responses", [(PAIRING,
        "        var request = isResponse ? QueryCommand.FindRequest(scenario, entry) : entry;\n",
        "        var request = isResponse ? null : entry;\n")]),
    ("skip the templated tier", [(PAIRING,
        "        if (shapedRank < theirsShaped.Count)\n",
        "        if (shapedRank < theirsShaped.Count && shapedRank < 0)\n")]),
    ("try the templated tier first", [(PAIRING,
        "        var mineExact = mine.Where(i => Key(i) == key).ToList();\n        var theirsExact = theirs.Where(i => Key(i) == key).ToList();\n",
        "        var mineExact = mine.Where(i => ShapeKey(i) == ShapeKey(anchor)).ToList();\n        var theirsExact = theirs.Where(i => ShapeKey(i) == ShapeKey(anchor)).ToList();\n")]),
    ("take the first candidate instead of the n-th", [(PAIRING,
        "            return Found(theirsExact[rank], onShape: false, rank, mineExact.Count, theirsExact.Count);\n",
        "            return Found(theirsExact[0], onShape: false, rank, mineExact.Count, theirsExact.Count);\n")]),
    ("key on the full URI", [(PAIRING,
        "        entry.Uri.Contains(\"://\", StringComparison.Ordinal) && Uri.TryCreate(entry.Uri, UriKind.Absolute, out var uri)\n            ? uri.PathAndQuery\n            : entry.Uri;\n",
        "        entry.Uri;\n")]),
    ("rank the shape tier over every call with the shape (F19)", [(PAIRING,
        "        var mineShaped = Unpaired(mine, theirs).Where(i => ShapeKey(i) == shapeKey).ToList();\n        var theirsShaped = Unpaired(theirs, mine).Where(i => ShapeKey(i) == shapeKey).ToList();\n",
        "        var mineShaped = mine.Where(i => ShapeKey(i) == shapeKey).ToList();\n        var theirsShaped = theirs.Where(i => ShapeKey(i) == shapeKey).ToList();\n")]),
    ("a response address pairs with the partner's request", [(PAIRING,
        "            if (!isResponse || request is null)\n",
        "            if (!isResponse || request is null || call is not null)\n")]),
    ("omit the tie note", [(DIFF,
        "        if (pairing.OldCount > 1 || pairing.NewCount > 1)\n            note(",
        "        if (pairing.OldCount > 1 && pairing.OldCount < 0)\n            note(")]),
    ("omit the shape note", [(DIFF,
        "        if (pairing.OnShape)\n            note($\"! no call in",
        "        if (pairing.OnShape && pairing.Position < 0)\n            note($\"! no call in")]),
    ("label the new side with the old ordinal", [(DIFF,
        "        var rightRef = new BodyRef($\"{rightLabel} {newAddress}\", ",
        "        var rightRef = new BodyRef($\"{rightLabel} {match.Address}/i{oldInteraction.Ordinal}\", ")]),
    ("label the sides by file name alone", [(DIFF,
        "        var (leftLabel, rightLabel) = DiffLabels(left.Path, right.Path);\n\n        if (left.Scenario(address.Scenario) is not { } oldScenario)",
        "        var (leftLabel, rightLabel) = (Path.GetFileName(left.Path), Path.GetFileName(right.Path));\n\n        if (left.Scenario(address.Scenario) is not { } oldScenario)")]),
    ("skip the run diff's refusals for --body", [(DIFF,
        "        if (RefuseUnmatchable(left, right, before, after, error) is { } refusal)\n            return refusal;\n\n        // Repeated rows and retries",
        "\n        // Repeated rows and retries")]),
    ("keep the scenario fallback silent", [(DIFF,
        "        if (scenarioNote is not null)\n            note(scenarioNote);\n",
        "")]),
    ("refuse reports without ids again", [(DIFF,
        "        var candidates = after.GetValueOrDefault(oldScenario.StableId) ?? [];\n",
        "        var candidates = oldScenario.StableId.Length == 0 ? new List<ScenarioEntry>() : after.GetValueOrDefault(oldScenario.StableId) ?? [];\n")]),
    ("no provenance in the one-report body diff", [(DIFF,
        "        foreach (var line in ProvenanceNotes(index))\n            note(\"! \" + line);\n",
        "        foreach (var line in ProvenanceNotes(index).Take(0))\n            note(\"! \" + line);\n")]),
    ("notes on stdout under --count", [(DIFF,
        "        options.Count && !options.Json ? error.WriteLine : writer.Note;\n",
        "        writer.Note;\n")]),
    ("the refusal lists no calls", [(DIFF,
        "            error.WriteLine($\"{match.Address}'s {noun} calls: {rows}\"",
        "            _ = ($\"{match.Address}'s {noun} calls: {rows}\"")]),
    ("accept a third address beside two bodies", [(SEARCH,
        "            if (options.Positional.Count > 2)\n",
        "            if (options.Positional.Count > 99)\n")]),
    ("accept --body beside two bodies", [(SEARCH,
        "            if (options.BodyAddress is not null || options.Body)\n",
        "            if (options.BodyAddress is not null && options.Body)\n")]),
    ("accept a third positional beside two reports", [(SEARCH,
        "        if (options.Positional.Count > 1)\n        {\n            var extra = options.Positional[1];\n",
        "        if (options.Positional.Count > 99)\n        {\n            var extra = options.Positional[1];\n")]),
    ("accept a bare --body beside two reports", [(SEARCH,
        "            if (options.Body)\n            {\n                error.WriteLine(\"--body names the call to compare across the two runs:",
        "            if (options.Body && options.Count)\n            {\n                error.WriteLine(\"--body names the call to compare across the two runs:")]),
    ("let the run diff read --offset and --limit without paging", [(SEARCH,
        "            if (options.Given.FirstOrDefault(flag => flag is \"--offset\" or \"--limit\") is { } paging)\n",
        "            if (options.Given.FirstOrDefault(flag => flag is \"--never\") is { } paging)\n")]),
    ("accept a report beside --baseline", [(SEARCH,
        "        if (options.Baseline && options.Positional.Count > 0)\n",
        "        if (options.Baseline && options.Positional.Count > 99)\n")]),
    ("the run diff ignores --count", [(SEARCH,
        "        if (options.Count)\n        {\n            if (!leftCarries)",
        "        if (options.Count && options.Json)\n        {\n            if (!leftCarries)")]),
    ("cut Gone silently", [(SEARCH,
        "            if (goneRows.Count > 10)\n                writer.Line(",
        "            if (goneRows.Count > 99)\n                writer.Line(")]),
    ("cut a section without saying where the rest is", [(SEARCH,
        "                writer.Line($\"  … {rows.Count - 15} more (--json lists every row)\");\n",
        "                writer.Line($\"  … {rows.Count - 15} more\");\n")]),
    ("compare pairs by position again", [(SEARCH,
        "            if (CallPairing.Pair(left, leftRequest, right).Partner is not { } rightRequest)\n                continue;\n",
        "            var position = AllInteractions(index, left).Select(p => p.Request).ToList().IndexOf(leftRequest);\n            if (AllInteractions(index, right).Select(p => p.Request).ElementAtOrDefault(position) is not { } rightRequest)\n                continue;\n")]),
    ("give --body its value for every verb", [(OPTIONS,
        "                    if (verb is \"diff\" && i + 1 < args.Count && !args[i + 1].StartsWith(\"--\", StringComparison.Ordinal))\n",
        "                    if (i + 1 < args.Count && !args[i + 1].StartsWith(\"--\", StringComparison.Ordinal))\n")]),
    ("--body takes a b: value again", [(OPTIONS,
        "                        if (parsed && bodyAddress.Kind == AddressKind.Body)\n",
        "                        if (parsed && bodyAddress.Kind == AddressKind.Body && given.Length < 0)\n"), (OPTIONS,
        "                        if (!parsed || bodyAddress.Kind != AddressKind.Interaction)\n",
        "                        if (!parsed || bodyAddress.Kind is not (AddressKind.Interaction or AddressKind.Body))\n")]),
    ("http drops a second address", [(PAYLOADS,
        "        if (options.Positional.Count > 1)\n        {\n            error.WriteLine($\"http describes one call;",
        "        if (options.Positional.Count > 99)\n        {\n            error.WriteLine($\"http describes one call;")]),
]


def run(worktree, *args):
    return subprocess.run(args, cwd=worktree, capture_output=True, text=True, encoding="utf-8", errors="replace")


def build_and_test(worktree):
    # The engine alone, copied over the one the test project already holds. Building the test project took
    # minutes even unchanged (and a plain build rebuilds every extension that references Kronikol); every
    # mutation here edits a method body in the query engine, so the test assembly binds to the new Kronikol.dll
    # as it bound to the old one. The test project is built once, unmutated, before the first run.
    build = run(worktree, "dotnet", "build", "src/Kronikol/Kronikol.csproj", "-f", "net10.0", "-c", "Debug", "-v", "q", "-nologo")
    if build.returncode != 0:
        errors = [l.strip() for l in build.stdout.splitlines() if " error " in l][:3]
        return None, "build failed: " + " | ".join(errors)
    for name in ("Kronikol.dll", "Kronikol.pdb"):
        shutil.copy2(worktree / "src/Kronikol/bin/Debug/net10.0" / name, worktree / "tests/Kronikol.Tests/bin/Debug/net10.0" / name)
    test = run(worktree, "dotnet", "test", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "--no-build", "-c", "Debug", "--filter", FILTER)
    failed = sorted(set(m.group(1) for m in re.finditer(r"^\s+Failed (Kronikol\.Tests\.Tool\.\S+)", test.stdout, re.MULTILINE)))
    summary = re.search(r"(Failed|Passed)!\s+- Failed:\s+(\d+), Passed:\s+(\d+)", test.stdout)
    return failed, summary.group(0) if summary else test.stdout[-300:]


def main():
    worktree = pathlib.Path(sys.argv[1])
    out = open(sys.argv[2], "w", encoding="utf-8", newline="\n")

    def say(text):
        print(text, flush=True)
        out.write(text + "\n")
        out.flush()

    failed, summary = build_and_test(worktree)
    say(f"unmutated: {summary}")
    if failed is None or failed:
        say("the unmutated tree is not green; stopping")
        return

    survivors = []
    for name, edits in MUTATIONS:
        originals = {}
        for path, old, new in edits:
            file = worktree / path
            text = originals.setdefault(path, file.read_text(encoding="utf-8"))
            current = file.read_text(encoding="utf-8")
            assert current.count(old) == 1, f"{name}: {path} holds the text {current.count(old)} times"
            file.write_text(current.replace(old, new), encoding="utf-8", newline="\n")
        try:
            failed, summary = build_and_test(worktree)
        finally:
            for path, text in originals.items():
                (worktree / path).write_text(text, encoding="utf-8", newline="\n")
        if failed is None:
            say(f"BUILD  {name}: {summary}")
            survivors.append(name)
        elif not failed:
            say(f"ALIVE  {name}: every fact passed ({summary})")
            survivors.append(name)
        else:
            say(f"KILLED {name}: {len(failed)} red - " + ", ".join(f.replace("Kronikol.Tests.Tool.", "") for f in failed[:4])
                + (" …" if len(failed) > 4 else ""))

    failed, summary = build_and_test(worktree)
    say(f"restored: {summary}")
    say(f"{len(MUTATIONS) - len(survivors)} of {len(MUTATIONS)} mutations killed" + (f"; not killed: {survivors}" if survivors else ""))


if __name__ == "__main__":
    main()
