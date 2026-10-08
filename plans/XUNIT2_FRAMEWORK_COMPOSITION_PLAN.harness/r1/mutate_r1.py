"""R1 mutations for plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md section 5, run in the throwaway worktree
C:/Code/Kronikol-xunit2mut. Each mutation is applied alone, the named facts are run, and the file is restored with
git checkout (which stamps it now, so the next build recompiles). A mutation is CAUGHT when its facts fail."""
import subprocess, sys, pathlib, re, time

ROOT = pathlib.Path("C:/Code/Kronikol-xunit2mut")
XU2 = "tests/Kronikol.Tests.xUnit2/Kronikol.Tests.xUnit2.csproj"
CORE = "tests/Kronikol.Tests/Kronikol.Tests.csproj"

SINK = "src/Kronikol.xUnit2/KronikolResultsSink.cs"

MUTATIONS = [
    ("pair results by display name again", SINK,
     "            case ITestResultMessage result when _byTest.TryGetValue(result.Test, out var scenario):",
     "            case ITestResultMessage result when ByName(result.Test.DisplayName) is { } scenario:",
     XU2, "FullyQualifiedName~A_failing_fact_with_a_display_name|FullyQualifiedName~Each_theory_row|FullyQualifiedName~Each_row_of_a_theory_whose|FullyQualifiedName~Under_method_display",
     [("    private void Start(ITest test)",
      "    private static ScenarioInfo? ByName(string displayName) =>\n"
      "        XUnit2TestTrackingContext.CollectedScenarios.Values.FirstOrDefault(s => !s.HasResult\n"
      "            && (displayName == s.MethodMatchKey || displayName.StartsWith(s.MethodMatchKey + \"(\")));\n\n"
      "    private void Start(ITest test)")]),
    ("key by ITestCase.UniqueID instead of the ITest object", SINK,
     "    private readonly ConcurrentDictionary<ITest, ScenarioInfo> _byTest = new(ReferenceEqualityComparer.Instance);",
     "    private readonly ConcurrentDictionary<ITest, ScenarioInfo> _byTest = new(new ByUniqueId());\n"
     "    private sealed class ByUniqueId : IEqualityComparer<ITest>\n    {\n"
     "        public bool Equals(ITest? x, ITest? y) => x?.TestCase.UniqueID == y?.TestCase.UniqueID;\n"
     "        public int GetHashCode(ITest obj) => obj.TestCase.UniqueID.GetHashCode();\n    }",
     XU2, "FullyQualifiedName~Each_row_of_a_theory_whose|FullyQualifiedName~Each_row_of_a_theory_is_its_own_scenario", None),
    ("create no scenario at ITestStarting", SINK,
     "            case ITestStarting starting:\n                Start(starting.Test);",
     "            case ITestStarting starting:",
     XU2, "FullyQualifiedName~A_tracked_skipped_test|FullyQualifiedName~Tests_that_fail_before", None),
    ("create a scenario for every test", SINK,
     "        if (type is null || method is null || !Tracked.GetOrAdd((type, method), key => AppliesTo(key.Type, key.Method, testMethod)))",
     "        if (type is null || method is null)",
     XU2, "FullyQualifiedName~A_tracked_skipped_test|FullyQualifiedName~A_test_the_attribute_does_not_apply_to", None),
    ("turn synchronous reporting off", "src/Kronikol.xUnit2/SynchronousReportingOptions.cs",
     "        _values[SynchronousMessageReporting] = true;",
     "        _values[SynchronousMessageReporting] = false;",
     XU2, "FullyQualifiedName~Each_theory_row_carries", None),
    ("pass ITestAssemblyFinished on before writing", SINK,
     "            try\n            {\n                Finish();\n            }\n            catch (Exception ex)\n            {\n                ReportLifecycle.WriteErrorLog(ex);\n            }\n            finally\n            {\n                _forwarder.Send(message);\n                _forwarder.Complete();\n            }",
     "            _forwarder.Send(message);\n            _forwarder.Complete();\n            try\n            {\n                Finish();\n            }\n            catch (Exception ex)\n            {\n                ReportLifecycle.WriteErrorLog(ex);\n            }",
     XU2, "FullyQualifiedName~The_reports_are_written_before_the_runner_hears|FullyQualifiedName~The_reports_are_complete_when_dotnet_test_returns", None),
    ("let a write failure leave OnMessage", SINK,
     "            catch (Exception ex)\n            {\n                ReportLifecycle.WriteErrorLog(ex);\n            }\n            finally",
     "            finally",
     XU2, "FullyQualifiedName~A_report_that_fails_to_write|FullyQualifiedName~A_report_that_cannot_be_written", None),
    ("set the option on the runner's object", "src/Kronikol.xUnit2/SynchronousReportingOptions.cs",
     "        runner as SynchronousReportingOptions ?? new SynchronousReportingOptions(runner);",
     "        Mutated(runner);\n    private static ITestFrameworkExecutionOptions Mutated(ITestFrameworkExecutionOptions runner)\n"
     "    {\n        runner.SetValue(SynchronousMessageReporting, true);\n        return runner;\n    }",
     XU2, "FullyQualifiedName~SynchronousReportingOptionsTests", None),
    ("pass messages on inline, without the queue", SINK,
     "        return _forwarder.Send(message);\n    }",
     "        return _runnerSink.OnMessage(message);\n    }",
     XU2, "FullyQualifiedName~Messages_sent_from_many_test_threads",
     [("        _forwarder = new OrderedMessageForwarder(runnerSink);",
       "        _forwarder = new OrderedMessageForwarder(runnerSink);\n        _runnerSink = runnerSink;"),
      ("    private DateTime? _start;", "    private DateTime? _start;\n    private readonly IMessageSink _runnerSink;")]),
    ("drop the ResultDefaulted mark (sink)", SINK,
     "            scenario.ResultDefaulted = true;",
     "            scenario.ResultDefaulted = false;",
     XU2, "FullyQualifiedName~A_test_that_ends_with_no_result", None),
    ("drop the ResultDefaulted mark (collection-fixture path)", "src/Kronikol.xUnit2/XUnit2ReportGenerator.cs",
     "            scenario.ResultDefaulted = true;",
     "            scenario.ResultDefaulted = false;",
     XU2, "FullyQualifiedName~NoFrameworkLaneTests", None),
    ("specifications follow failures only", "src/Kronikol/Reports/ReportGenerator.cs",
     "y.Result == ExecutionResult.Failed || y.ResultDefaulted",
     "y.Result == ExecutionResult.Failed",
     XU2, "FullyQualifiedName~The_specifications_are_not_written", None),
    ("open the identity window at ITestStarting", SINK,
     "        XUnit2TestTrackingContext.HandOver(scenario, method);",
     "        XUnit2TestTrackingContext.HandOver(scenario, method);\n        XUnit2TestTrackingContext.SetCurrentTest($\"{type.Name}.{method.Name}\", scenario.Id);",
     XU2, "FullyQualifiedName~A_call_made_in_the_test_class_constructor", None),
    ("take the run's start at the end", SINK,
     "        _writeReports(scenarios, _start ?? DateTime.UtcNow, DateTime.UtcNow);",
     "        _writeReports(scenarios, DateTime.UtcNow, DateTime.UtcNow);",
     XU2, "FullyQualifiedName~With_no_options_set_the_run_starts|FullyQualifiedName~The_run_starts_when_the_assembly_starts", None),
    ("an empty error message for a passing scenario", "src/Kronikol.xUnit2/ScenarioInfoCollectionExtensions.cs",
     "                                ErrorMessage = FailureText.OrNull(x.ErrorMessage),",
     "                                ErrorMessage = x.ErrorMessage ?? string.Empty,",
     XU2, "FullyQualifiedName~Every_tracked_test_is_one_scenario", None),
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
