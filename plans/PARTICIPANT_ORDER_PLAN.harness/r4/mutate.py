"""R4's mutations (#241, plans/PARTICIPANT_ORDER_PLAN.md 7): each breaks one part of the release, and a named fact must fail.

Run from the repository root, on R4's commit, with nothing else editing src/:
    python -u plans/PARTICIPANT_ORDER_PLAN.harness/r4/mutate.py [M13 M14 ...]
With no names it runs the mutations the core facts catch; M20 is caught by the Playwright fact, which runs the BDDfy
example, so it is named on its own, in a Playwright slot. Each mutated file is backed up and written back after its run,
whatever happens. Prints, per mutation, the facts that failed; a mutation no fact catches is SURVIVED, one that does not
build is BUILD FAILED (rewrite it, it caught nothing).
"""
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path.cwd()
CORE = ("tests/Kronikol.Tests/Kronikol.Tests.csproj", "FullyQualifiedName~BDDfyPage")
E2E = ("tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj", "FullyQualifiedName~BDDfyPageLinkTests")
LINKS = "src/Kronikol/Reports/ReportLinks.cs"
GENERATOR = "src/Kronikol/Reports/ReportGenerator.cs"
PROCESSOR = "src/Kronikol.BDDfy.xUnit3/DiagramEnhancingBatchProcessor.cs"

MUTATIONS = {
    "M13": ("draw the default renderer's empty address as an img again (F6)", LINKS, "if (src.Length == 0)", "if (false)", CORE),
    "M14": ("put inline markup into an img's address", LINKS, "else if (src.StartsWith('<'))", "else if (false)", CORE),
    "M15": ("leave a local file's address as it is from the report folder", LINKS, "IsAddress(src) || report is null", "true", CORE),
    "M16": ("rebase a data URI as if it were a file", LINKS,
            'src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)', "false", CORE),
    "M17": ("link without escaping each part of the path", LINKS, ".Select(Uri.EscapeDataString)", ".Select(part => part)", CORE),
    "M18": ("never record where the report put each scenario", GENERATOR, "ReportLinks.Record(", "_ = (", CORE),
    "M19": ("record the test id as the anchor, not the scenario's stable id", GENERATOR,
            'Anchor: "sid-" + ScenarioStableId.For(suite, f, s)', 'Anchor: "sid-" + s.Id', CORE),
    "M20": ("never tell BDDfy's page where the report is", PROCESSOR, "Reports.ReportLinks.Last));", "null));", E2E),
}


def run(args):
    return subprocess.run(args, cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")


def mutate(key):
    title, path, old, new, (tests, test_filter) = MUTATIONS[key]
    file = ROOT / path
    original = file.read_bytes()
    text = original.decode("utf-8")
    if text.count(old) != 1:
        return f"{key} NOT APPLIED ({text.count(old)} matches): {title}"
    try:
        file.write_bytes(text.replace(old, new).encode("utf-8"))
        build = run(["dotnet", "build", tests, "-v", "q", "-nologo"])
        if build.returncode != 0:
            return f"{key} BUILD FAILED: {title}"
        test = run(["dotnet", "test", tests, "--no-build", "--filter", test_filter])
        failed = sorted({m.group(1) for m in re.finditer(r"^\s+Failed Kronikol\.Tests[\w.]*\.(\w+)", test.stdout, re.M)})
        verdict = "caught by " + ", ".join(failed) if failed else "SURVIVED"
        return f"{key} {verdict}: {title}"
    finally:
        file.write_bytes(original)


if __name__ == "__main__":
    keys = sys.argv[1:] or [key for key, m in MUTATIONS.items() if m[4] is CORE]
    for key in keys:
        print(mutate(key), flush=True)
    for tests in {MUTATIONS[key][4][0] for key in keys}:
        run(["dotnet", "build", tests, "-v", "q", "-nologo"])
