"""R1's mutations (plans/PARTICIPANT_ORDER_PLAN.md 7): each breaks one part of the release, and a named fact must fail.

Run from the repository root, on R1's commit, with nothing else editing src/:
    python -u plans/PARTICIPANT_ORDER_PLAN.harness/r1/mutate.py [M1 M2 ...]
Each mutated file is backed up and written back after its run, whatever happens. Prints, per mutation, the facts that
failed; a mutation no fact catches is SURVIVED, one that does not build is BUILD FAILED (rewrite it, it caught nothing).
"""
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path.cwd()
TESTS = "tests/Kronikol.Tests/Kronikol.Tests.csproj"
FILTER = "FullyQualifiedName~ParticipantOrder"
CREATOR = "src/Kronikol/PlantUml/PlantUmlCreator.cs"
ORDER_BY = ".OrderBy(d => d.Alias == firstAlias ? 0 : roles.GroupOf(rawNames[d.Alias]))"
SELECT = ".Select(d => text[d.Start..d.End]));"

MUTATIONS = {
    "M1": ("skip the under-test group", "src/Kronikol/PlantUml/DiagramRoles.cs", "group = 2;", "group = 3;"),
    "M2": ("look roles up by alias", CREATOR, ORDER_BY,
           ".OrderBy(d => d.Alias == firstAlias ? 0 : roles.GroupOf([d.Alias]))"),
    "M3": ("the report takes a set fetched before it", "src/Kronikol/Reports/ReportGenerator.cs",
           "        DefaultDiagramsFetcher.Reset();\n        var diagrams", "        var diagrams"),
    "M4": ("apply roles when the run writes no table", "src/Kronikol/Parties/PartyTable.cs",
           "DiagramRoles => Declared\n", "DiagramRoles => true\n"),
    "M5": ("order the subjects by name", CREATOR, ORDER_BY, ORDER_BY + ".ThenBy(d => d.Alias, StringComparer.Ordinal)"),
    "M6": ("decide the order per fragment", CREATOR,
           "noteWrapWidth, systemUiFont, roles);\n            _currentDiagram = new StringBuilder(_currentPrefix);",
           "noteWrapWidth, systemUiFont, null);\n            _currentDiagram = new StringBuilder(_currentPrefix);"),
    "M7": ("resolve after the diagrams again (the diagrams get no roles)", "src/Kronikol/Reports/ReportGenerator.cs",
           "            Roles = parties.DiagramRoles,\n", "            Roles = null,\n"),
    "M8": ("write a moved party's line as a caller's", CREATOR, SELECT,
           ".Select(d => d.Alias == firstAlias || roles.GroupOf(rawNames[d.Alias]) == 3 ? text[d.Start..d.End]"
           " : \"participant\" + text[d.Start..d.End][text[d.Start..d.End].IndexOf(' ')..]));"),
}


def run(args):
    return subprocess.run(args, cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")


def mutate(key):
    title, path, old, new = MUTATIONS[key]
    file = ROOT / path
    original = file.read_bytes()
    text = original.decode("utf-8")
    if text.count(old) != 1:
        return f"{key} NOT APPLIED ({text.count(old)} matches): {title}"
    try:
        file.write_bytes(text.replace(old, new).encode("utf-8"))
        build = run(["dotnet", "build", TESTS, "-v", "q", "-nologo"])
        if build.returncode != 0:
            return f"{key} BUILD FAILED: {title}"
        test = run(["dotnet", "test", TESTS, "--no-build", "--filter", FILTER])
        failed = sorted({m.group(1) for m in re.finditer(r"^\s+Failed Kronikol\.Tests\.[\w.]+\.(\w+)", test.stdout, re.M)})
        verdict = "caught by " + ", ".join(failed) if failed else "SURVIVED"
        return f"{key} {verdict}: {title}"
    finally:
        file.write_bytes(original)


if __name__ == "__main__":
    for key in sys.argv[1:] or list(MUTATIONS):
        print(mutate(key), flush=True)
    run(["dotnet", "build", TESTS, "-v", "q", "-nologo"])
