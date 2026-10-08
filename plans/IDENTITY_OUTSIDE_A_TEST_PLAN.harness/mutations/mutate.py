"""Undoes one part of the fix at a time and runs the adapter's tests, to show which fact catches each.

Run from a checkout that holds the fix (a throwaway worktree: every file is restored from a backup after its mutation):
    python mutate.py <checkout> > mutations.txt
"""
import pathlib
import re
import shutil
import subprocess
import sys

ROOT = pathlib.Path(sys.argv[1])

XUNIT2 = "tests/Kronikol.Tests.xUnit2"
NUNIT4 = "tests/Kronikol.Tests.NUnit4"
MSTEST = "tests/Kronikol.Tests.MSTest"
TUNIT = "tests/Kronikol.Tests.TUnit"

MUTATIONS = [
    ("M1", "xUnit v2 fetcher answers outside a test as GetCurrentTestInfo() does (a new random id)",
     "src/Kronikol.xUnit2/CurrentTestInfo.cs",
     '() => XUnit2TestTrackingContext.Current\n              ?? throw new InvalidOperationException("Test context not available on this thread.");',
     "() => XUnit2TestTrackingContext.GetCurrentTestInfo();", XUNIT2),
    ("M2", "TestTrackingAttribute's Track.TestIdResolver answers a random id outside a test",
     "src/Kronikol.xUnit2/TestTrackingAttribute.cs",
     "Track.TestIdResolver ??= () => XUnit2TestTrackingContext.Current?.Id;",
     "Track.TestIdResolver ??= () => XUnit2TestTrackingContext.GetCurrentTestInfo().Id;", XUNIT2),
    ("M3", "xUnit v2 DiagrammedTestRun's Track.TestIdResolver answers a random id outside a test",
     "src/Kronikol.xUnit2/DiagrammedTestRun.cs",
     "Track.TestIdResolver ??= () => XUnit2TestTrackingContext.Current?.Id;",
     "Track.TestIdResolver ??= () => XUnit2TestTrackingContext.GetCurrentTestInfo().Id;", XUNIT2),
    ("M4", "xUnit v2 override back to GetCurrentTestInfo().Id (its 4.10.0 reading)",
     "src/Kronikol.xUnit2/TrackingDiagramOverride.cs",
     "XUnit2TestTrackingContext.Current?.Id\n        ?? (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;",
     "XUnit2TestTrackingContext.GetCurrentTestInfo().Id;", XUNIT2),
    ("M5", "xUnit v2 override without the scope and the global fallback",
     "src/Kronikol.xUnit2/TrackingDiagramOverride.cs",
     "XUnit2TestTrackingContext.Current?.Id\n        ?? (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;",
     "XUnit2TestTrackingContext.Current?.Id;", XUNIT2),
    ("M6", "xUnit v2 override ignores the running test",
     "src/Kronikol.xUnit2/TrackingDiagramOverride.cs",
     "XUnit2TestTrackingContext.Current?.Id\n        ?? (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;",
     "(TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;", XUNIT2),
    ("M7", "xUnit v2 InsertPlantUml without its no-test guard",
     "src/Kronikol.xUnit2/TrackingDiagramOverride.cs",
     "        if (testId is null) return;\n        DefaultTrackingDiagramOverride.InsertPlantUml(testId, plantUml);",
     "        DefaultTrackingDiagramOverride.InsertPlantUml(testId!, plantUml);", XUNIT2),
    ("M8", "NUnit RunningTest takes the ad hoc context as a test",
     "src/Kronikol.NUnit4/RunningTest.cs",
     "if (context is TestExecutionContext.AdhocContext || context.CurrentTest is not { IsSuite: false })",
     "if (context.CurrentTest is not { IsSuite: false })", NUNIT4),
    ("M9", "NUnit RunningTest takes a fixture (a suite) as a test",
     "src/Kronikol.NUnit4/RunningTest.cs",
     "if (context is TestExecutionContext.AdhocContext || context.CurrentTest is not { IsSuite: false })",
     "if (context is TestExecutionContext.AdhocContext || context.CurrentTest is null)", NUNIT4),
    ("M10", "NUnit fetcher reads TestContext.CurrentContext.Test (its 4.10.0 reading)",
     "src/Kronikol.NUnit4/CurrentTestInfo.cs",
     "var test = RunningTest.Current",
     "var test = NUnit.Framework.TestContext.CurrentContext?.Test", NUNIT4),
    ("M11", "NUnit DiagrammedComponentTest's Track.TestIdResolver back to TestContext.CurrentContext",
     "src/Kronikol.NUnit4/DiagrammedComponentTest.cs",
     "Track.TestIdResolver ??= () => RunningTest.Current?.ID;",
     "Track.TestIdResolver ??= () => TestContext.CurrentContext?.Test?.ID;", NUNIT4),
    ("M12", "NUnit DiagrammedTestRun's Track.TestIdResolver back to TestContext.CurrentContext",
     "src/Kronikol.NUnit4/DiagrammedTestRun.cs",
     "Track.TestIdResolver ??= () => RunningTest.Current?.ID;",
     "Track.TestIdResolver ??= () => TestContext.CurrentContext?.Test?.ID;", NUNIT4),
    ("M13", "NUnit override without the scope and the global fallback",
     "src/Kronikol.NUnit4/TrackingDiagramOverride.cs",
     "RunningTest.Current?.ID\n        ?? (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;",
     "RunningTest.Current?.ID;", NUNIT4),
    ("M14", "NUnit override ignores the running test",
     "src/Kronikol.NUnit4/TrackingDiagramOverride.cs",
     "RunningTest.Current?.ID\n        ?? (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;",
     "(TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;", NUNIT4),
    ("M15", "MSTest override without the scope and the global fallback",
     "src/Kronikol.MSTest/TrackingDiagramOverride.cs",
     ": (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;",
     ": null;", MSTEST),
    ("M16", "MSTest override ignores the running test",
     "src/Kronikol.MSTest/TrackingDiagramOverride.cs",
     "return ctx is not null\n            ? $\"{ctx.FullyQualifiedTestClassName}.{ctx.TestName}\"\n            : (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;",
     "return (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;", MSTEST),
    ("M17", "TUnit override without the scope and the global fallback",
     "src/Kronikol.TUnit/TrackingDiagramOverride.cs",
     "TestContext.Current?.Id\n        ?? (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;",
     "TestContext.Current?.Id;", TUNIT),
    ("M18", "TUnit override ignores the running test (no fact can catch it: the TUnit tests run under xUnit v3, with no TUnit test)",
     "src/Kronikol.TUnit/TrackingDiagramOverride.cs",
     "TestContext.Current?.Id\n        ?? (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;",
     "(TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;", TUNIT),
]


def failed_tests(output: str) -> list[str]:
    names = set()
    for line in output.splitlines():
        m = re.match(r"^\s+Failed (\S+)", line)
        if m:
            names.add(m.group(1).split(".")[-1])
    return sorted(names)


for key, what, rel, old, new, project in MUTATIONS:
    path = ROOT / rel
    original = path.read_text(encoding="utf-8")
    if original.count(old) != 1:
        print(f"{key} NOT APPLIED: expected one match in {rel}, found {original.count(old)}")
        continue
    backup = path.with_suffix(path.suffix + ".bak")
    shutil.copyfile(path, backup)
    try:
        path.write_text(original.replace(old, new), encoding="utf-8")
        run = subprocess.run(["dotnet", "test", str(ROOT / project), "--logger", "console;verbosity=normal"],
                             capture_output=True, text=True, encoding="utf-8", errors="replace")
        out = run.stdout + run.stderr
        if " error " in out and "Build FAILED" in out:
            print(f"{key} BUILD FAILED: {what}")
            continue
        failed = failed_tests(out)
        verdict = f"caught by {len(failed)}: " + ", ".join(failed) if failed else "SURVIVED"
        print(f"{key} {what}\n    {verdict}", flush=True)
    finally:
        shutil.copyfile(backup, path)
        backup.unlink()
