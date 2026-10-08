"""R2 mutations for plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md section 5, run in the throwaway worktree
C:/Code/Kronikol-xunit2mut. Each mutation is applied alone, the named facts are run, and the file is restored with
git checkout (which stamps it now, so the next build recompiles). A mutation is CAUGHT when its facts fail."""
import subprocess, sys, pathlib, re, time

ROOT = pathlib.Path("C:/Code/Kronikol-xunit2mut")
XU2 = "tests/Kronikol.Tests.xUnit2/Kronikol.Tests.xUnit2.csproj"
CORE = "tests/Kronikol.Tests/Kronikol.Tests.csproj"

SINK = "src/Kronikol.xUnit2/KronikolResultsSink.cs"

EXT = "src/Kronikol.xUnit2/TestFrameworkExecutorExtensions.cs"
DEC = "src/Kronikol.xUnit2/KronikolReportingExecutor.cs"

MUTATIONS = [
    ("wrap regardless", EXT,
     "        return executor is ReportingTestFrameworkExecutor or KronikolReportingExecutor\n            ? executor\n            : new KronikolReportingExecutor(executor);",
     "        return new KronikolReportingExecutor(executor);",
     XU2, "FullyQualifiedName~Wrapping_twice|FullyQualifiedName~Kronikols_own_executor_is_not_wrapped", None),
    ("dispose the framework each time", DEC,
     "        if (Interlocked.Exchange(ref _disposed, 1) == 0)\n            inner.Dispose();",
     "        inner.Dispose();",
     XU2, "FullyQualifiedName~The_framework_is_disposed_once", None),
    ("set the option on the runner's object (decorator)", DEC,
     "        inner.RunTests(testCases, ResultsSink(executionMessageSink), SynchronousReportingOptions.Over(executionOptions));",
     "        inner.RunTests(testCases, ResultsSink(executionMessageSink), Set(executionOptions));\n"
     "    private static ITestFrameworkExecutionOptions Set(ITestFrameworkExecutionOptions o)\n"
     "    {\n        o.SetValue(SynchronousReportingOptions.SynchronousMessageReporting, true);\n        return o;\n    }",
     XU2, "FullyQualifiedName~A_run_reaches_the_runner_through_Kronikols_sink", None),
    ("run without Kronikol's sink", DEC,
     "        inner.RunTests(testCases, ResultsSink(executionMessageSink), SynchronousReportingOptions.Over(executionOptions));",
     "        inner.RunTests(testCases, executionMessageSink, SynchronousReportingOptions.Over(executionOptions));",
     XU2, "FullyQualifiedName~A_run_reaches_the_runner_through_Kronikols_sink|FullyQualifiedName~Under_AssemblyFixture_every_tracked_test", None),
    ("pass ITestAssemblyFinished on before writing (under AssemblyFixture)", SINK,
     "            try\n            {\n                Finish();\n            }\n            catch (Exception ex)\n            {\n                ReportLifecycle.WriteErrorLog(ex);\n            }\n            finally\n            {\n                _forwarder.Send(message);\n                _forwarder.Complete();\n            }",
     "            _forwarder.Send(message);\n            _forwarder.Complete();\n            try\n            {\n                Finish();\n            }\n            catch (Exception ex)\n            {\n                ReportLifecycle.WriteErrorLog(ex);\n            }",
     XU2, "FullyQualifiedName~Under_AssemblyFixture_the_reports_are_complete", None),
    ("drop the fallback pairing", SINK,
     "        var scenarios = PairTheAttributesOwn(_inOrder.ToArray(), diagnostics);",
     "        var scenarios = _inOrder.ToArray();",
     XU2, "FullyQualifiedName~Under_a_framework_that_keeps_the_asynchronous_bus|FullyQualifiedName~When_nothing_is_handed_over", None),
    ("pair the fallback by name", SINK,
     "            var partner = scenario.TakenByBefore ? null : own.FirstOrDefault(o => o.MethodMatchKey == scenario.MethodMatchKey);",
     "            var partner = scenario.TakenByBefore ? null : own.FirstOrDefault(o => o.ScenarioName == scenario.ScenarioName);",
     XU2, "FullyQualifiedName~Under_a_framework_that_keeps_the_asynchronous_bus|FullyQualifiedName~When_nothing_is_handed_over", None),
    ("record no diagnostic for the pairing", SINK,
     "        if (paired + own.Count > 0)\n            diagnostics.Add(",
     "        if (false)\n            diagnostics.Add(",
     XU2, "FullyQualifiedName~Under_a_framework_that_keeps_the_asynchronous_bus|FullyQualifiedName~When_nothing_is_handed_over", None),
]




def run(args, timeout=3000):
    p = subprocess.run(args, cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    return p.returncode, p.stdout + p.stderr


def main(only=None):
    results = []
    for index, (name, path, old, new, project, filter_, extra) in enumerate(MUTATIONS, 1):
        if only and index not in only:
            continue
        file = ROOT / path
        text = file.read_text(encoding="utf-8")
        if old not in text:
            results.append((index, name, "NOT APPLIED: old text not found"))
            print(f"M{index} {name}: NOT APPLIED", flush=True)
            continue
        mutated = text.replace(old, new, 1)
        missing = False
        for anchor, replacement in extra or []:
            if anchor not in mutated:
                missing = True
                break
            mutated = mutated.replace(anchor, replacement, 1)
        if missing:
            results.append((index, name, "NOT APPLIED: extra anchor not found"))
            print(f"M{index} {name}: NOT APPLIED (extra anchor)", flush=True)
            continue
        file.write_text(mutated, encoding="utf-8", newline="\n")
        started = time.time()
        try:
            code, out = run(["dotnet", "test", project, "--filter", filter_])
        finally:
            run(["git", "checkout", "--", path])
        summary = [l for l in out.splitlines() if re.search(r"Passed!|Failed!|error CS", l)]
        failed = [l.strip() for l in out.splitlines() if "[FAIL]" in l]
        verdict = "CAUGHT" if code != 0 and failed else ("BUILD FAILED" if "error CS" in out else "SURVIVED")
        results.append((index, name, verdict, summary[-1] if summary else "", failed))
        print(f"M{index} {name}: {verdict} ({time.time() - started:.0f} s) {summary[-1] if summary else ''}", flush=True)
        for f in failed:
            print(f"    {f}", flush=True)
        if verdict == "BUILD FAILED":
            print("\n".join(l for l in out.splitlines() if "error CS" in l)[:2000], flush=True)
    return results


if __name__ == "__main__":
    only = {int(a) for a in sys.argv[1:]} if len(sys.argv) > 1 else None
    main(only)
