"""R2's mutations (plans/PARTICIPANT_ORDER_PLAN.md 7): each breaks one part of the release, and a named fact must fail.

Run from the repository root, on R2's commit, with nothing else editing src/:
    python -u plans/PARTICIPANT_ORDER_PLAN.harness/r2/mutate.py [M1 M2 ...]
Each mutated file is backed up and written back after its run, whatever happens. Prints, per mutation, the facts that
failed; a mutation no fact catches is SURVIVED, one that does not build is BUILD FAILED (rewrite it, it caught nothing).
"""
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path.cwd()
TESTS = "tests/Kronikol.Tests/Kronikol.Tests.csproj"
FILTER = "FullyQualifiedName~ParticipantOrder|FullyQualifiedName~DeclaredDouble"
CREATOR = "src/Kronikol/PlantUml/PlantUmlCreator.cs"

MUTATIONS = {
    "M9": ("leave the loop's actor unmarked (F7)", CREATOR, "                    actorDefined |= actor;\n", ""),
    "M10": ("draw a pure caller under test as the actor (F2)", CREATOR, "else if (!MayBeActor(pureCallerAlias))", "else if (false)"),
    "M11": ("let the loop give the actor to a party under test", CREATOR,
            "var actor = !actorDefined && MayBeActor(callerShortName);", "var actor = !actorDefined;"),
    "M12": ("take the actor from every party not held as the test (5.6's first wording)", CREATOR,
            "roles.GroupOf(rawNames![alias]) != 2;", "roles.GroupOf(rawNames![alias]) == 1;"),
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
