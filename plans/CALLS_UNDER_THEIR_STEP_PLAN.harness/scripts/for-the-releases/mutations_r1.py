"""R1's mutations (the plan's section 8.5), one at a time: apply, build, run the facts, restore. Each must turn a fact red.

Usage: mutations_r1.py <worktree> <out file> [name...]
A build failure is reported as BUILD FAILED, never as caught.
"""
import os
import pathlib
import re
import subprocess
import sys

WT = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2])
ONLY = set(sys.argv[3:])
COL = "src/Kronikol/Tracking/StepCollector.cs"
GEN = "src/Kronikol/Reports/ReportGenerator.cs"
LOGGER = "src/Kronikol/Tracking/RequestResponseLogger.cs"
PROJECT = os.environ.get("MUT_PROJECT", "tests/Kronikol.Tests.StepTracking/Kronikol.Tests.StepTracking.csproj")
FILTER = os.environ.get("MUT_FILTER", "FullyQualifiedName~StepFlow")

M = [
    ("left-its-steps-takes-any-open-step", COL,
     [("return ReferenceEquals(flow, state.OutOfItsSteps) ? LastOpenByACaller(state) : running;", "return ReferenceEquals(flow, state.OutOfItsSteps) ? LastOpenInItsCaller(state) : running;")]),
    ("a-body-flow-knows-nothing", COL,
     [("        if (isTheTestsFlow)\n            return LastOpenByACaller(state);\n\n", "")]),
    ("a-scope-of-its-own-reads-as-a-body", COL,
     [("&& ReferenceEquals(TestIdentityScope.Mark, state.IdentityMark)\n", "\n")]),
    ("another-id-reads-as-the-tests", COL,
     [("&& string.Equals(ResolveTestId(), testId, StringComparison.Ordinal);", ";")]),
    ("flow-forgets-the-other-tests-step", COL,
     [("step.Parent ?? step.CameFrom ?? step.State!.OutOfItsSteps;", "step.Parent ?? step.State!.OutOfItsSteps;")]),
    ("service-step-ignores-its-trace", COL,
     [("        if (TestIdentityScope.InboundTrace is { } trace && state.CallsInFlight.TryGetValue(trace, out var call) && call.Step.IsOpen)\n            return call.Step;\n\n", "")]),
    ("answered-call-still-in-flight", COL,
     [("                state.CallsInFlight.TryRemove(log.TraceId, out _);\n", "                _ = call;\n")]),
    ("a-hooks-step-not-passed-over", COL,
     [("                foreach (var left in state.Open)\n                    left.Left = left.PassedOver = true;\n", "                _ = state;\n")]),
    ("passed-over-still-read", COL,
     [("if (step.OpenChildren > 0 || step.PassedOver)", "if (step.OpenChildren > 0)")]),
    ("join-takes-the-innermost-of-any-party", GEN,
     [("            if (string.Equals(candidate.ServiceName, record.CallerName, StringComparison.OrdinalIgnoreCase))\n                return waiting[k];\n", "")]),
    ("join-needs-the-names-to-line-up", GEN,
     [("            innermost ??= waiting[k];\n", "")]),
    ("join-reads-the-case", GEN,
     [("if (string.Equals(candidate.ServiceName, record.CallerName, StringComparison.OrdinalIgnoreCase))", "if (string.Equals(candidate.ServiceName, record.CallerName, StringComparison.Ordinal))")]),
    ("join-takes-the-same-party", GEN,
     [("if (candidate.TraceId != record.TraceId || string.Equals(candidate.CallerName, record.CallerName, StringComparison.OrdinalIgnoreCase))", "if (candidate.TraceId != record.TraceId)")]),
    ("join-whatever-was-open", GEN,
     [("""                    && (paths[i] is null || Query.Address.PathCoveredBy(paths[i], theirs) || Query.Address.PathCoveredBy(theirs, paths[i]!)))
""", """                    )
""")]),
    ("failures-no-result-not-read", "src/Kronikol/Query/QueryCommand.Narrative.cs",
     [("        return steps.Where(s => s.Step.Status is null && !s.Step.IsAssertion).ToArray();", "        return failed;")]),
    ("digest-no-result-not-read", "src/Kronikol/Reports/FailuresDigestGenerator.cs",
     [("        if (failingIndexes.Length == 0 && ordered.Any(step => step.Step.Status is not null))\n", "        if (failingIndexes.Length == -1)\n")]),
    ("steps-no-mark-for-no-result", "src/Kronikol/Query/QueryCommand.Narrative.cs",
     [(""" : step.IsAssertion ? "·" : step.Status is null && hasAResult ? "?" : " ";""", """ : step.IsAssertion ? "·" : " ";""")]),
    ("flow-no-header-for-no-step", "src/Kronikol/Query/QueryCommand.Narrative.cs",
     [("""else if (currentStep is null && stepHeaderShown)""", """else if (currentStep is null && !stepHeaderShown)""")]),
    ("no-result-counted-skipped", GEN,
     [("                default: break;\n", "                default: skipped++; break;\n")]),
    ("flow-crosses-attempts", COL,
     [("for (var step = FlowStep.Value; step is not null && ReferenceEquals(step.State, state); step = step.Parent)", "for (var step = FlowStep.Value; step is not null; step = step.Parent)")]),
    ("spare-takes-a-step-left-running", COL,
     [("completed = state.Open.LastOrDefault(s => !s.IsFrame && (!s.Left || s.ByHand || s.PassedOver));", "completed = state.Open.LastOrDefault(s => !s.IsFrame);")]),
    ("bypass-by-id-moves-any-flow-up", COL,
     [("            bypassed.StubStillToComplete = true;\n", "            FlowStep.Value = FlowAfter(bypassed);\n")]),
    ("no-results-read-as-a-hang", "src/Kronikol/Query/QueryCommand.Narrative.cs",
     [("        if (failed.Length > 0 || !HasAResult(scenario))\n", "        if (failed.Length > 0)\n")]),
    ("no-results-marked-in-steps", "src/Kronikol/Query/QueryCommand.Narrative.cs",
     [("step.Status is null && hasAResult ? ", "step.Status is null ? ")]),
    ("no-results-read-as-a-hang-in-the-digest", "src/Kronikol/Reports/FailuresDigestGenerator.cs",
     [("        if (failingIndexes.Length == 0 && ordered.Any(step => step.Step.Status is not null))\n", "        if (failingIndexes.Length == 0)\n")]),
    ("digest-calls-a-hang-failing", "src/Kronikol/Reports/FailuresDigestGenerator.cs",
     [("""{(step.Status is null ? "**Still running when the report was written**" : "**Failing step**")} — """, """**Failing step** — """)]),
    ("json-does-not-tell-a-hang-apart", "src/Kronikol/Query/QueryCommand.Narrative.cs",
     [("                stillRunning = !s.Step.Failed,\n", "                stillRunning = false,\n")]),
    ("jsonl-does-not-tell-a-hang-apart", "src/Kronikol/Reports/FailuresDigestGenerator.cs",
     [("                    StillRunning = s.Status is null,\n", "                    StillRunning = false,\n")]),
    ("message-identity-has-no-mark", "src/Kronikol/Tracking/TestIdentityScope.cs",
     [("        CurrentMark.Value = new object();\n        FromMessage.Value = true;\n", "        FromMessage.Value = true;\n")]),
    ("own-bypass-leaves-the-flow-in-the-step", COL,
     [("""        if (CloseBypassed(ResolveTestId(), reason) is { } bypassed && ReferenceEquals(FlowStep.Value, bypassed))
            FlowStep.Value = FlowAfter(bypassed);
""", """        CloseBypassed(ResolveTestId(), reason);
""")]),
    ("flow-not-moved-back", COL,
     [("        step.Left = true;\n        FlowStep.Value = FlowAfter(step);\n", "        step.Left = true;\n")]),
    ("left-not-set", COL, [("        step.Left = true;\n", "")]),
    ("straggler-as-never-in-a-step", COL,
     [("return ReferenceEquals(flow, state.OutOfItsSteps) ? LastOpenByACaller(state) : running;",
       "return ReferenceEquals(flow, state.OutOfItsSteps) ? LastOpenByACaller(state) : LastOpenInItsCaller(state) ?? running;")]),
    ("out-of-its-steps-gone", COL,
     [("step.Parent ?? step.CameFrom ?? step.State!.OutOfItsSteps;", "step.Parent ?? step.CameFrom!;")]),
    ("rule-1-trusts-an-ended-step", COL,
     [("            if (step.IsOpen)\n                return step;\n", "            return step;\n")]),
    ("place-taken-at-completion", COL,
     [("            (parent?.SubSteps ?? state.TopLevel).Add(step);\n", ""),
      ("        state.Open.Remove(step);\n", "        state.Open.Remove(step);\n        (step.Parent?.SubSteps ?? state.TopLevel).Add(step);\n")]),
    ("deepest-for-shared", COL,
     [("shared = any ? (shared is null ? null : NearestShared(shared, step)) : step;", "shared = step;")]),
    ("nothing-shared-reads-last-top-level", COL,
     [("return shared?.Ordinal ?? 0;", "return shared?.Ordinal ?? state.LastTopLevel?.Ordinal;")]),
    ("join-takes-an-answered-parent", GEN,
     [("                waiting.RemoveAll(w => records[w].RequestResponseId == record.RequestResponseId);\n", "")]),
    ("flow-is-in-a-frame-gone", COL,
     [("public static Task CompleteStepAsync(Task task) => LeftABypass() || FlowIsInAFrame() ? task", "public static Task CompleteStepAsync(Task task) => LeftABypass() ? task"),
      ("public static Task<T> CompleteStepAsync<T>(Task<T> task) => LeftABypass() || FlowIsInAFrame() ? task", "public static Task<T> CompleteStepAsync<T>(Task<T> task) => LeftABypass() ? task")]),
    ("left-a-bypass-gone", COL,
     [("public static Task CompleteStepAsync(Task task) => LeftABypass() || FlowIsInAFrame() ? task", "public static Task CompleteStepAsync(Task task) => FlowIsInAFrame() ? task"),
      ("public static Task<T> CompleteStepAsync<T>(Task<T> task) => LeftABypass() || FlowIsInAFrame() ? task", "public static Task<T> CompleteStepAsync<T>(Task<T> task) => FlowIsInAFrame() ? task"),
      ("        if (LeftABypass())\n            return;\n\n", "")]),
    ("a-woven-completion-closes-a-frame", COL,
     [("            if (completed is null || completed.IsFrame)\n                completed = state.Open.LastOrDefault(s => !s.IsFrame && (!s.Left || s.ByHand || s.PassedOver));\n", "")]),
    ("open-children-not-counted-down", COL,
     [("        if (step.Parent is { } parent)\n            parent.OpenChildren--;\n", "")]),
]


def read(path):
    return (WT / path).read_bytes().decode("utf-8")


def write(path, text):
    (WT / path).write_bytes(text.encode("utf-8"))


lines = []
for name, path, edits in M:
    if ONLY and name not in ONLY:
        continue
    original = read(path)
    crlf = "\r\n" in original
    text = original.replace("\r\n", "\n")
    for old, new in edits:
        assert text.count(old) == 1, (name, old[:70], text.count(old))
        text = text.replace(old, new)
    write(path, text.replace("\n", "\r\n") if crlf else text)
    try:
        run = subprocess.run(
            ["dotnet", "test", PROJECT, "-c", "Release", "-p:WarningLevel=0", "-nologo", "--filter", FILTER, "--logger", "console;verbosity=normal"],
            cwd=WT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=900)
        out = run.stdout + run.stderr
        failed = re.findall(r"^\s+Failed Kronikol\S*\.(\S+?)(?:\(| \[)", out, re.M)
        if re.search(r"\berror CS\d+", out):
            lines.append(f"{name}: BUILD FAILED")
        elif failed:
            lines.append(f"{name}: caught by {len(failed)}: {', '.join(sorted(set(failed)))[:400]}")
        else:
            lines.append(f"{name}: SURVIVED")
    except subprocess.TimeoutExpired:
        lines.append(f"{name}: caught, by a hang past the limit")
    finally:
        write(path, original)
    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(lines[-1][:200], flush=True)
print("# done", flush=True)
