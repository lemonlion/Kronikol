#!/usr/bin/env python
"""R1's mutation pass (plan 6.5): each mutant breaks one rule the facts claim to hold. usage: mutate_r1.py <worktree> <out-file> [ids...]

A mutant is caught when the unit facts named for it fail; one they do not catch runs the real-engine facts as well. A mutant that does
not compile is reported as ERROR and must be rewritten, never read as caught. The worktree must be clean of anything but this run: each
file is restored from its own copy afterwards, never with git.
"""
import pathlib
import re
import subprocess
import sys
import time

root = pathlib.Path(sys.argv[1])
out = pathlib.Path(sys.argv[2])
only = set(sys.argv[3:])

C = "src/Kronikol/Tracking/StepCollector.cs"
P = "src/Kronikol/Tracking/StepPairing.cs"
L = "src/Kronikol/Tracking/RequestResponseLogger.cs"
D = "src/Kronikol/Reports/BackgroundStepsDetector.cs"
A = "src/Kronikol/Reports/AttachmentPlacement.cs"
G = "src/Kronikol/Reports/ReportGenerator.cs"
H = "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs"
R = "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs"
BE = "src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs"
BS = "src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs"
LF = "src/Kronikol.LightBDD.Core/FeatureResultExtensions.cs"

FRAME = "FullyQualifiedName~StepFrameTests"
PAIR = "FullyQualifiedName~StepPairingTests"
ATTR = "FullyQualifiedName~ReportAttributionTests"
DET = "FullyQualifiedName~BackgroundStepsDetectorTests"
COPY = "FullyQualifiedName~AttachmentCopyTests"
RQ = "FullyQualifiedName~ReqNRollStepPairingTests"
BD = "FullyQualifiedName~BDDfyScenarioInfoExtensionsTests"
LB = "FullyQualifiedName~FeatureResultExtensionsTests"
DOC = "FullyQualifiedName~DocumentOwnershipTests"
NONE = "FullyQualifiedName~NoUnitFactReachesThis"

MUTANTS = [
    ("M01 every step takes the same id", C, "step.Id = Interlocked.Increment(ref _lastStepId);", "step.Id = 1;", FRAME),
    ("M02 CompleteFrame takes any id", C, "if (state.StepStack.All(s => s.Id != frameId))\n                return;\n", "", FRAME),
    ("M03 a step left running loses its row", C, "        state.LeftRunning.Add(open);\n        (state.StepStack.Count > 0 ? state.StepStack.Peek().SubSteps : state.CompletedSteps).Add(open);\n", "        state.LeftRunning.Add(open);\n", FRAME),
    ("M04 a frame nests under a hook's step left running", C, "if (isFrame && state.StepStack.Count > 0 && state.StepStack.All(s => !s.IsFrame))", "if (false)", FRAME),
    ("M05 CompleteStep closes a frame", C, "if (state.StepStack.Count == 0 || state.StepStack.Peek().IsFrame)\n            {\n                if (state.LeftRunning.Count > 0)", "if (state.StepStack.Count == 0)\n            {\n                if (state.LeftRunning.Count > 0)", FRAME),
    ("M06 a step left running never finishes", C, "                if (state.LeftRunning.Count > 0)\n                {", "                if (false)\n                {", FRAME),
    ("M07 BypassStep closes a frame", C, "if (state.StepStack.Count == 0 || state.StepStack.Peek().IsFrame)\n                return;\n\n            var bypassed", "if (state.StepStack.Count == 0)\n                return;\n\n            var bypassed", FRAME),
    ("M08 OpenStepOf reads the bottom of the stack", C, "return state.StepStack.Count > 0 ? state.StepStack.Peek().Id : null;", "return state.StepStack.Count > 0 ? state.StepStack.Last().Id : null;", FRAME),
    ("M09 no stamp", L, "if (log.OpenStep is null && StepCollector.OpenStepOf(log.TestId) is { } openStep)\n            log = log with { OpenStep = openStep };\n", "", FRAME),
    ("M10 the stamp after the hold", L, "if (log.OpenStep is null && StepCollector.OpenStepOf(log.TestId) is { } openStep)\n            log = log with { OpenStep = openStep };\n\n", "", DOC, ("        Store.Enqueue(log);\n    }\n\n    /// <summary>Stores an entry", "        if (log.OpenStep is null && StepCollector.OpenStepOf(log.TestId) is { } openStep)\n            log = log with { OpenStep = openStep };\n        Store.Enqueue(log);\n    }\n\n    /// <summary>Stores an entry")),
    ("M11 a step outside a framework step carries no mark", P, "merged.Add(WithNested(collected[k]) with { OutsideFrameworkStep = true });", "merged.Add(WithNested(collected[k]));", PAIR),
    ("M12 a nested step also lists a row of its own", P, "if (!ListedInside(natives[next]))\n", "", PAIR),
    ("M13 what did not run before a frame goes unlisted", P, "                Through(native);\n", "", PAIR),
    ("M14 what did not run waits for the end", P, "            if (k == lastFramed)\n                Through(natives.Count);\n", "", PAIR),
    ("M15 a nested step is not mapped", P, "subSteps.Add(sub.CollectedId is { } id && indexOfFrame.TryGetValue(id, out var native) ? map(natives[native], nested) : nested);", "subSteps.Add(nested);", PAIR),
    ("M16 the guess reads every row", D, "(scenario.Steps ?? []).Where(step => !step.OutsideFrameworkStep && !step.DidNotRun).ToArray();", "(scenario.Steps ?? []).ToArray();", DET),
    ("M17 a row removed by Skip", D, "scenario.Steps = AfterTheFirstThatRan(scenario.Steps!, commonPrefixLength);", "scenario.Steps = scenario.Steps!.Skip(commonPrefixLength).ToArray();", DET),
    ("M18 a shared row", D, "scenario.BackgroundSteps = ran.Take(commonPrefixLength).ToArray();", "scenario.BackgroundSteps = members[0].Ran.Take(commonPrefixLength).ToArray();", DET),
    ("M19 a step visited twice", A, "                if (!visited.Add(step))\n                    continue;\n", "", COPY),
    ("M20 the gate asks only for one id", G, "if (rows.Length == 0 || rows.Any(step => step.CollectedId is null && !step.DidNotRun))", "if (!rows.Any(step => step.CollectedId is not null))", ATTR),
    ("M21 a row that did not run closes the gate", G, "rows.Any(step => step.CollectedId is null && !step.DidNotRun)", "rows.Any(step => step.CollectedId is null)", ATTR),
    ("M22 a stamp naming no row keeps the step before", G, "if (collected is not null && log.OpenStep is { } open)\n                        current = collected.GetValueOrDefault(open);", "if (collected is not null && log.OpenStep is { } open && collected.TryGetValue(open, out var stamped))\n                        current = stamped;", ATTR),
    ("M23 a bar is read against its text", G, "case DiagramMarkerKind.Step when collected is not null && log.OpenStep is { } drawn:\n                        current = collected.GetValueOrDefault(drawn);\n                        break;\n\n", "", ATTR),
    ("M24 a sub-step's stamp reads nothing", G, "            foreach (var sub in step.SubSteps ?? [])\n                Add(sub, path);\n", "", ATTR),
    ("M25 a background row has no address", G, "        for (var i = 0; i < background.Length; i++)\n            Add(background[i], $\"b{i}\");\n", "", ATTR),
    ("M26 the frame key on the scenario", H, "_scenarioContext.StepContext[ReqNRollConstants.StepFrameKey] = StepCollector.StartFrame(", "_scenarioContext[ReqNRollConstants.StepFrameKey] = StepCollector.StartFrame(", NONE),
    ("M27 a step over a failed inner step reads passed", H, "var status = stepContext.Status != ScenarioExecutionStatus.OK ? stepContext.Status : scenario.ScenarioExecutionStatus;", "var status = stepContext.Status;", NONE),
    ("M28 BDDfy lists a step it does not report", BS, "var listed = BDDfyStepTrackingExecutor.Listed(collectedSteps, testId, steps.Select(s => s.StepId).ToArray());", "var listed = collectedSteps;", NONE),
    ("M29 a later example row takes a frame", BE, "else if (!RanAgain.ContainsKey(testId))\n                Frames.TryAdd((testId, step.Id), opened);", "else\n                Frames.TryAdd((testId, step.Id), opened);", NONE),
    ("M30 BDDfy's rows that did not run say nothing", BS, "                DidNotRun = collected is null,\n", "", NONE),
    ("M31 LightBDD's rows that did not run say nothing", LF, " with { DidNotRun = collected is null }", "", LB),
    ("M32 LightBDD's by-position rows keep an id", LF, "MapStep(stepArray[i], priorFailure, scenarioSkipped, collected) with { CollectedId = null }", "MapStep(stepArray[i], priorFailure, scenarioSkipped, collected)", LB),
    ("M33 LightBDD pairs by position with nothing run", LF, "\n            && stepArray.Any(s => s.Status != ExecutionStatus.NotRun))", ")", LB),
    ("M34 Reqnroll pairs a hand-built step by frame", R, "if (steps.Count > 0 && steps.All(s => s.FrameId is null))\n            return MapStepsByPosition(steps, collectedSteps);\n\n", "", RQ),
    ("M35 BDDfy pairs a hand-built step by frame", BS, "if (steps.All(s => BDDfyStepTrackingExecutor.FrameOf(testId, s.StepId) is null))", "if (false)", BD),
    ("M36 an assertion takes id 0", C, "CollectedId = step.Id == 0 ? null : step.Id,", "CollectedId = step.Id,", FRAME),
    ("M37 the Reqnroll scenario with no step of its own lists nothing", R, "return mapped.Count > 0 ? mapped.ToArray() : null;", "return steps.Count > 0 ? mapped.ToArray() : null;", RQ),
]

CONTROL = ("M00 control: the unmutated code passes", C, "", "", FRAME)


def run(args, cwd, timeout=1500):
    p = subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    return p.returncode, p.stdout + p.stderr


def build():
    # The project reference chain is what a mutant has to reach: every adapter builds on the core.
    code, text = run(["dotnet", "build", str(root / "tests/Kronikol.Tests"), "--nologo", "-v", "q"], root)
    errors = sorted(set(re.findall(r"[^\s]+\.cs\(\d+,\d+\): error [A-Z0-9]+: [^\[]+", text)))
    return code, errors


def unit(filter_):
    code, text = run(["dotnet", "test", str(root / "tests/Kronikol.Tests"), "--no-build", "--nologo", "--filter", filter_], root)
    return code, re.findall(r"^\s+Failed (\S+)", text, re.M), re.findall(r"(?:Passed|Failed)!.*", text)


def engines():
    host = root / "tests/Kronikol.Tests.RealEngines"
    code, text = run(["dotnet", "build", str(host), "--nologo", "-v", "q"], root)
    if code != 0:
        return code, ["BUILD"], []
    code, text = run(["dotnet", "test", str(host), "--no-build", "--nologo"], root, timeout=2400)
    return code, re.findall(r"^\s+Failed (\S+)", text, re.M), re.findall(r"(?:Passed|Failed)!.*", text)


def log(line):
    with out.open("a", encoding="utf-8", newline="\n") as f:
        f.write(line + "\n")
    print(line, flush=True)


mutants = MUTANTS if not only else [m for m in MUTANTS if m[0].split()[0] in only]
if not only:
    mutants = [CONTROL] + mutants

for mutant in mutants:
    name, path, old, new, filter_ = mutant[:5]
    second = mutant[5] if len(mutant) > 5 else None
    file = root / path
    original = file.read_bytes()
    text = original.decode("utf-8").replace("\r\n", "\n")
    started = time.time()
    try:
        if old:
            assert text.count(old) == 1, (name, text.count(old))
            text = text.replace(old, new)
            if second:
                assert text.count(second[0]) == 1, (name, "second", text.count(second[0]))
                text = text.replace(second[0], second[1])
            file.write_bytes(text.encode("utf-8"))
        code, errors = build()
        if code != 0:
            log(f"ERROR  {name}: does not compile: {errors[:2]}")
            continue
        code, failed, summary = unit(filter_)
        if not old:
            log(f"{'ok' if code == 0 else 'BROKEN'}  {name}: {summary[-1:] if summary else ''}")
            continue
        if failed:
            log(f"caught {name}: by {len(failed)} unit, {[f.split('.')[-1][:70] for f in failed[:3]]} ({time.time() - started:.0f}s)")
            continue
        code, failed, summary = engines()
        if failed:
            log(f"caught {name}: by {len(failed)} on a real engine, {[f.split('.')[-1][:70] for f in failed[:3]]} ({time.time() - started:.0f}s)")
        else:
            log(f"SURVIVED {name}: {summary[-1:] if summary else ''} ({time.time() - started:.0f}s)")
    finally:
        file.write_bytes(original)
log("done")
