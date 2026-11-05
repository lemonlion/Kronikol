"""Cuts the prototype's last commit down to what a release holds, so each release can be measured on its own.

python -I split_releases.py <checkout> r1      R1 alone: no engine events and no query mark (R2), neither LightBDD defect (R3)
python -I split_releases.py <checkout> r1r2    R1 and R2: neither LightBDD defect (R3)
"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
stage = sys.argv[2]


def edit(path, change):
    p = root / path
    t = p.read_bytes().decode("utf-8")
    newline = "\r\n" if "\r\n" in t else "\n"
    t = change(t.replace("\r\n", "\n"))
    p.write_bytes(t.replace("\n", newline).encode("utf-8"))


def cut(t, start, end):
    a = t.index(start)
    b = t.index(end, a) + len(end)
    return t[:a] + t[b:]


def replace(t, old, new):
    assert t.count(old) == 1, old[:60]
    return t.replace(old, new)


# R3: the guard around the step type fetcher
guard = "        // A fetcher may throw where it has no step to read"
edit("src/Kronikol/Tracking/TestTrackingMessageHandler.cs", lambda t: (
    t[:t.index(guard)] + "        var currentStepType = _currentStepTypeFetcher();\n"
    + t[t.index("        if (currentStepType is null)", t.index(guard)):]))

# R3: a LightBDD scenario's end by the clock its calls are stamped with
edit("src/Kronikol.LightBDD.Core/FeatureResultExtensions.cs", lambda t: cut(
    t,
    "            // LightBDD's own timer can read earlier than the clock the scenario's calls are stamped with\n",
    "                : result.ExecutionTime?.End,\n").replace(
    "            ErrorStackTrace = failedException?.StackTrace,\n            Duration = result.ExecutionTime?.Duration,\n",
    "            ErrorStackTrace = failedException?.StackTrace,\n            Duration = result.ExecutionTime?.Duration,\n            EndedAt = result.ExecutionTime?.End,\n"))

if stage == "r1":
    # R2: a mark of its own for a row that did not run
    edit("src/Kronikol/Query/QueryCommand.Narrative.cs", lambda t: replace(
        t, ' : step.Status is "Skipped" or "SkippedAfterFailure" ? "-" : step.IsAssertion', " : step.IsAssertion"))
    # R2: the engine's finished events. CloseStep and CollectScenario stay, called from the hooks alone.
    edit("src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs", lambda t: cut(
        t,
        "\n                // Reqnroll runs none of a type's later hooks once one throws, so a suite's own hook can keep this class's\n",
        "Quietly(() => CollectScenario(scenario, feature));\n                });\n"))
elif stage != "r1r2":
    raise SystemExit("r1 or r1r2")
print("cut to", stage)
