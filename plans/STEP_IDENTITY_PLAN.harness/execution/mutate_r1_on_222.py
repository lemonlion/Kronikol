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
ASYNC = "FullyQualifiedName~BDDfyAsyncFrameTests"
NONE = "FullyQualifiedName~NoUnitFactReachesThis"
BG = "FullyQualifiedName~BackgroundAsWrittenTests"

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
    ("M19 a step visited twice", A, "                if (!visited.Add(step))\n                    continue;\n", "", COPY),
    ("M20 the gate asks only for one id", G, "if (rows.Length == 0 || rows.Any(step => step.CollectedId is null && !step.DidNotRun))", "if (!rows.Any(step => step.CollectedId is not null))", ATTR),
    ("M21 a row that did not run closes the gate", G, "rows.Any(step => step.CollectedId is null && !step.DidNotRun)", "rows.Any(step => step.CollectedId is null)", ATTR),
    ("M22 a stamp naming no row keeps the step before", G, "if (collected is not null && log.OpenStep is { } open)\n                        current = collected.GetValueOrDefault(open);", "if (collected is not null && log.OpenStep is { } open && collected.TryGetValue(open, out var stamped))\n                        current = stamped;", ATTR),
    ("M23 a bar is read against its text", G, "case DiagramMarkerKind.Step when collected is not null && log.OpenStep is { } drawn:\n                        current = collected.GetValueOrDefault(drawn);\n                        break;\n\n", "", ATTR),
    ("M24 a sub-step's stamp reads nothing", G, "            foreach (var sub in step.SubSteps ?? [])\n                Add(sub, path);\n", "", ATTR),
    ("M25 a background row has no address", G, "        for (var i = 0; i < background.Length; i++)\n            Add(background[i], $\"b{i}\");\n", "", ATTR),
    ("M27 a step over a failed inner step reads passed", H, "var status = stepContext.Status != ScenarioExecutionStatus.OK ? stepContext.Status : _scenarioContext.ScenarioExecutionStatus;", "var status = stepContext.Status;", NONE),
    ("M29 a later example row takes a frame", BE, "else if (!RanAgain.ContainsKey(testId))\n                Frames.TryAdd((testId, step.Id), opened);", "else\n                Frames.TryAdd((testId, step.Id), opened);", NONE),
    ("M32 LightBDD's by-position rows keep an id", LF, "MapStep(stepArray[i], priorFailure, scenarioSkipped, collected) with { CollectedId = null }", "MapStep(stepArray[i], priorFailure, scenarioSkipped, collected)", LB),
    ("M33 LightBDD pairs by position with nothing run", LF, "\n            && stepArray.Any(s => s.Status != ExecutionStatus.NotRun))", ")", LB),
    ("M36 an assertion takes id 0", C, "CollectedId = step.Id == 0 ? null : step.Id,", "CollectedId = step.Id,", FRAME),
    ("M37 the Reqnroll scenario with no step of its own lists nothing", R, "return mapped.Count > 0 ? mapped.ToArray() : null;", "return steps.Count > 0 ? mapped.ToArray() : null;", RQ),
    ("M26 each level shares one open step", H, "_scenarioContext.StepContext[ReqNRollConstants.OpenStepKey] = opened;\n", "", NONE),
    ("M30 BDDfy's rows that did not run say nothing", BS, "                DidNotRun = BDDfyStepTrackingExecutor.FrameOf(testId, step.StepId) is null,\n", "", NONE),
    ("M31 LightBDD's rows that did not run say nothing", LF, " with { DidNotRun = StepTrackingStepDecorator.FrameOf(step.Info.RuntimeId) is null }", "", LB),
    ("M34 Reqnroll pairs a hand-built step by frame", R, "if (steps.Any(s => s.FrameId is null && s.NotRunAfter is null))\n            return MapStepsByPosition(steps, collectedSteps);\n\n", "", RQ),
    ("M35 BDDfy pairs a list with no frames by frame", BS, " || steps.All(s => BDDfyStepTrackingExecutor.FrameOf(testId, s.StepId) is null)", "", BD),
    ("M38 a frame no framework step names stands as a row", P, "else if (!collected[k].OpenedAsFrame)", "else", PAIR),
    ("M39 Reqnroll: one rebuilt row leaves the rest paired by frame", R, "if (steps.Any(s => s.FrameId is null && s.NotRunAfter is null))", "if (steps.Count > 0 && steps.All(s => s.FrameId is null && s.NotRunAfter is null))", RQ),
    ("M40 BDDfy: one rebuilt row leaves the rest paired by frame", BS, "steps.Any(s => s.StepId is null) || ", "", NONE),
    ("M41 Reqnroll: a row that ran reads as not run when its steps are gone", R, "DidNotRun = step.FrameId is null,", "DidNotRun = collected is null,", RQ),
    ("M42 LightBDD: a row that ran reads as not run when its steps are gone", LF, "with { DidNotRun = StepTrackingStepDecorator.FrameOf(step.Info.RuntimeId) is null }", "with { DidNotRun = collected is null }", LB),
    ("M43 BDDfy: a row that ran reads as not run when its steps are gone", BS, "DidNotRun = BDDfyStepTrackingExecutor.FrameOf(testId, step.StepId) is null,", "DidNotRun = collected is null,", NONE),
    ("M44 a skip reads failed", C, "var skipped = !step.Passed && !step.Bypassed && step.ErrorMessage?.StartsWith(XunitSkipMarker, StringComparison.Ordinal) == true;", "var skipped = false;", FRAME),
    ("M45 the marker anywhere in the message reads as a skip", C, "step.ErrorMessage?.StartsWith(XunitSkipMarker, StringComparison.Ordinal) == true", "step.ErrorMessage?.Contains(XunitSkipMarker, StringComparison.Ordinal) == true", FRAME),
    ("M46 a file in a step not listed is lost", BS, "return notListed.Length > 0 ? [.. own ?? [], .. notListed] : own;", "return own;", NONE),
    ("M48 a hook's row can be the Background", R, "if (!row.OutsideFrameworkStep && !closed && background.Count < writtenBackground.Count", "if (!closed && background.Count < writtenBackground.Count", BG),
    ("M49 a hook's row ends the Background", R, "closed |= !row.OutsideFrameworkStep;", "closed = true;", BG),
    ("M50 a row that disagrees does not end the Background", R, "closed |= !row.OutsideFrameworkStep;", "", BG),
    ("M51 a row listed with no frame has no place", H, "steps.Add(opened is null ? info with { NotRunAfter = StepCollector.LastStepIdOf(scenarioId) } : info);", "steps.Add(info);", NONE),
    ("M52 what did not run ignores what opened before it", P, "            while (next < natives.Count && frameOf(natives[next]) is null && notRunAfter?.Invoke(natives[next]) is { } after && collected[k].CollectedId > after)\n                Through(next + 1);\n\n", "", PAIR),
    ("M53 the last step opened is not kept", C, "            state.LastStepId = step.Id;\n", "", FRAME),
    ("M54 a frame says it is no frame", C, "            OpenedAsFrame = step.IsFrame,\n", "", FRAME),
    ("M55 a closed step stays the innermost", H, "            _scenarioContext.Remove(ReqNRollConstants.OpenStepKey);\n        else if (opened is not null)\n", "            { }\n        else if (opened is not null)\n", NONE),
    ("M56 the guess runs over what a real engine ran", R, "if (featureGroup.All(x => x.WrittenBackground is null))", "if (true)", NONE),
    ("M57 the public stopwatch key is not set", H, "_scenarioContext[ReqNRollConstants.StepStopwatchKey] = Open(stepType).Stopwatch;", "Open(stepType);", NONE),
    ("M47 a file under a step not listed is lost", BE, "(step.Attachments ?? []).Concat((step.SubSteps ?? [])\n            .Where(sub => !sub.OpenedAsFrame || !IsListed(sub, testId, stepsOfTheScenario))\n            .SelectMany(sub => FilesOf(sub, testId, stepsOfTheScenario)));", "step.Attachments ?? [];", NONE),
    ("M58 an async step closes at its first await", BE, "if (Configurator.AsyncVoidSupportEnabled && result is Task task)", "if (false && result is Task task)", ASYNC),
    ("M59 with async void support off it still waits", BE, "Configurator.AsyncVoidSupportEnabled && ", "", ASYNC),
    ("M60 a faulted task closes as passed", BE, "await task.ConfigureAwait(false);\n        }\n        catch\n        {\n            StepCollector.CompleteFrame(testId, frame, passed: false);", "await task.ConfigureAwait(false);\n        }\n        catch\n        {\n            StepCollector.CompleteFrame(testId, frame, passed: true);", ASYNC),
    ("M61 a nested step takes a place of its own by position", R, "steps = steps.Except(nested).ToList();\n", "", RQ),
    ("M72 a nested step's table is lost by position", R, "collectedSteps = collectedSteps.Select(c => StepPairing.WithNested(c, nested, s => s.FrameId, (s, under) => MapStep(s, under, priorFailure: false))).ToArray();\n", "", RQ),
    ("M62 a list not in run order is read as if it were", P, "var inRunOrder = framed.Zip(framed.Skip(1)).All(pair => pair.First < pair.Second);", "var inRunOrder = true;", PAIR),
    ("M63 a held call moves the walk's place", G, "if (open > latestOpen)", "if (true)", ATTR),
    ("M64 a bar leaves the walk's place where it was", G, "latestOpen = Math.Max(latestOpen, drawn);", "", ATTR),
    ("M66 BDDfy hands the scenario files its rows hold by position", BS, "info.Steps.Count == 0 || PairsByPosition(info.Steps, info.TestId)", "info.Steps.Count == 0", NONE),
    ("M67 what a frame no row names holds stays hidden", P, "collected = collected.SelectMany(c => c.OpenedAsFrame && !Named(c) && NamedInside(c).Any() ? NamedInside(c) : [c]).ToArray();", "", PAIR),
    ("M68 a listed frame's files go to the scenario too", BE, "            .Where(sub => !sub.OpenedAsFrame || !IsListed(sub, testId, stepsOfTheScenario))\n", "", NONE),
    ("M69 the guess reads a row no framework step opened", D, "                !s.Steps![i].OutsideFrameworkStep &&\n", "", DET),
    ("M70 the guess reads a row that did not run", D, "                !s.Steps![i].DidNotRun &&\n", "", DET),
    ("M71 what did not run follows the last frame whatever its id says", P, "while (next < natives.Count && notRunAfter?.Invoke(natives[next]) is null)\n                    Through(next + 1);", "Through(natives.Count);", PAIR),
    ("M73 rows that read alike are one row by position", R, "steps = steps.Where(s => !Nested(s)).ToList();", "steps = steps.Except(steps.Where(Nested).ToList()).ToList();", "FullyQualifiedName~StepPairingTests"),
    ("M74 a step that did not run between two that did says nothing about the order", P, "\n            && !natives.Where((_, i) => framed.Length > 0 && i > framed[0] && i < framed[^1]).Any(n => frameOf(n) is null && notRunAfter?.Invoke(n) is null);", ";", "FullyQualifiedName~StepPairingTests"),
    ("M75 a step placed by its id counts as out of order too", P, "Any(n => frameOf(n) is null && notRunAfter?.Invoke(n) is null);", "Any(n => frameOf(n) is null);", "FullyQualifiedName~StepPairingTests"),
    ("M76 by position a file past the last row is lost", "src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs", "? collected.Skip(info.Steps.Count).SelectMany(AllFilesOf)", "? Enumerable.Empty<FileAttachment>()", "FullyQualifiedName~BDDfyScenarioInfoExtensionsTests"),
    ("M77 by position every frame's files go on the scenario as well", "src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs", "? collected.Skip(info.Steps.Count).SelectMany(AllFilesOf)", "? collected.SelectMany(AllFilesOf)", "FullyQualifiedName~BDDfyScenarioInfoExtensionsTests"),
    ("M78 a step left running stays open at the next step", "src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs", "CloseWaiting(testId, waiting => !RunsInside(waiting));", "", "FullyQualifiedName~BDDfyAsyncFrameTests"),
    ("M79 the next step closes one it runs inside", "src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs", "CloseWaiting(testId, waiting => !RunsInside(waiting));", "CloseWaiting(testId, _ => true);", "FullyQualifiedName~BDDfyAsyncFrameTests"),
    ("M80 a step still running at the read is lost", "src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs", "BDDfyStepTrackingExecutor.CloseLeftRunning(x.TestId);", "", "FullyQualifiedName~BDDfyAsyncFrameTests"),
    ("M81 a flow stays inside a step after its call returned", "src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs", "RunningIn.Value = outer;", "", "FullyQualifiedName~BDDfyAsyncFrameTests"),
    ("M83 only the innermost step counts as what a flow is inside", "src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs", "running = running.Outer)", "running = null)", "FullyQualifiedName~BDDfyAsyncFrameTests"),
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
