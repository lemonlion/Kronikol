# AWAITED_ASSERTION_LABELS_PLAN harness

The measurements behind `plans/AWAITED_ASSERTION_LABELS_PLAN.md` (#212), taken on 5.12.0 (`1ee6373a`), Windows 11, .NET
SDK 10.0.300.

## probe/

- `Probe212Tests.cs.txt`: the probe, an xUnit fact class that ran inside `tests/Kronikol.Tests.AssertionTracking` and was
  moved out afterwards (the test project is unchanged). For each shape it builds the source, weaves it with
  `AssertionWeaver`, runs it under a test identity with `WovenRun.Run`, and records the woven count, the reasons
  `WeaveResult.Unwoven` gives, the notes drawn, whether the woven and the unwoven run threw, the text the weave passed
  to `Track` and what `AssertionExpressionFormatter.Format` makes of it. To run it again, copy it back as
  `Probe212Tests.cs` and run `dotnet test tests/Kronikol.Tests.AssertionTracking --filter FullyQualifiedName~Probe212`.
- `summary.txt`: the two result tables. Pass 1 builds every shape with `TestAssemblyBuilder.Build` (Roslyn 5.3.0, the
  suite's builder). Pass 2 builds the shapes #211 and #212 report with `dotnet build` and SDK 10.0.300 (Roslyn 5.6.0),
  as the issues' suites are built. The two agree on every shape both ran.
- `results-debug.txt`, `results-release.txt` (Pass 1) and `results-sdk-debug.txt`, `results-sdk-release.txt` (Pass 2):
  every run in full.
- `il/`: for each shape, its source (`<shape>.source.txt`) and the IL of each method before and after the weave
  (`<shape>.[sdk.]<config>.{unwoven,woven}.il.txt`), with each instruction's sequence point and the stack before it.
  The plan cites `S2.Debug.unwoven`, `S9b.Debug.woven`, `S9.Release.woven`, and from the second probe
  `S17a.Release.unwoven` and `S20.Debug.unwoven` / `S20.Release.unwoven`. Only those, `S2.sdk8.Debug.unwoven` and the
  shapes' sources are committed; the probes write the rest again (about 9 MB).
- `shouldly-async-overloads.txt`: Shouldly 4.3.0's async assertion overloads and what each returns.
- `Probe212bTests.cs.txt`, `summary-212b.txt`, `results-212b-*.txt`: the second probe, run the same way
  (`--filter FullyQualifiedName~Probe212b`). It measures S17 to S23 through the suite's builder, and S2, S4, S7, S8 and
  S16a through `BuildWithSdk` on SDKs 8.0.421, 9.0.314 and 10.0.300 (Roslyn 4.11, 4.14 and 5.6). `summary-212b.txt`
  holds the census of merge points (section E, 226 awaits) and the ranges a wrap to the next visible sequence point
  would take (table D). Its IL is in `il/` as `S17a.*`, `S17c.*`, `S20.*` and `S2.sdk8.Debug.unwoven.il.txt`.
- `Probe212cTests.cs.txt`, `summary-212c.txt`, `results-212c-debugplus.txt`: the third pass, Release with debug-plus
  (`dotnet build -c Release -p:DebugSymbols=true`, as xUnit v2 builds) beside plain Release, SDK 10.0.300, each build
  in a project directory of its own. It also measures S24 in those two builds and in Debug (section G).

## line-map-5.16.4.txt

Where each code citation of the plan (at `1ee6373a`, 5.12.0) is at `f33ed3e9` (5.16.4, after #210's two releases),
from `git diff -U0` per file, with the ranges changed inside named.

## tl-probe/

`results.txt`: which build messages the console logger and the terminal logger show, at which verbosity, on SDKs
8.0.421, 9.0.314 and 10.0.300, and which properties turn a task's warning into an error or silence it (plan 3.2 and
Q2). It holds the throwaway project it built, so the measurement can be repeated by hand.

## unwrap-probe/

`probe.cs.txt` and `output.txt`: a file-based app (`dotnet run --file probe.cs`) that checks what 4.1 f's helper
relies on: `ContinueWith(…, ExecuteSynchronously).Unwrap()` hands back the same exception object and the same result,
records its note before the awaiter resumes, completes at once for a task already complete, and keeps a cancellation.

## Shapes

S1 bare `await Should.ThrowAsync`; S2, S3 `var e = await` (async and plain lambda); S4 `return await` in a helper; S5
typed declaration; S6, S6b assignment to a local and a field; S7 discard; S8 `ConfigureAwait(false)`; S9, S9b
synchronous `Throw`, assigned and returned; S10, S10c `NotThrowAsync` and `NotThrow` (S10b, `NotThrowAsync<T>`, does not compile: Shouldly 4.3.0 has none); S11 a variable used after a later
await; S12a, S12b FluentAssertions; S13 TUnit; S14a to S14c S2, S1 and S3 without the issue's `await Task.Yield()`, S14d to S14o the #211 variants; S15a to S15d a helper returning an
assertion task without awaiting it, passing and failing; S16a, S16b an awaited assertion feeding another, passing and
failing; R211 and R212 the two issues' tests verbatim.

The second probe: S17a to S17c an assertion on an awaited value (Shouldly, FluentAssertions, and TUnit with two
awaits); S18a, S18c TUnit's `Throws` and AwesomeAssertions with a used result; S19a to S19d an assertion task or
object that leaves its statement un-awaited (a FluentAssertions helper, a TUnit helper, a stored task, an
expression-bodied test); S20 a second await after the assertion's; S21a, S21b statements across lines; S22a, S22b an
awaited assertion failing inside an `AssertionScope` and inside TUnit's `Assert.Multiple()`; S23 an awaited assertion
passed into another call. S17d and S18b (xUnit's `Assert`) ran as controls only: the weave does not read xUnit's
assertions.

The third pass adds S24a and S24c: a suite's own async assertion on its `[ShouldlyMethods]` type, awaited bare and
with `ConfigureAwait(false)`.
