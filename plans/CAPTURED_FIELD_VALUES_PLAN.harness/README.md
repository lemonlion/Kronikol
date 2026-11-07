# Harness for #213: a captured field's value read off the async state machine

Everything here was measured on Windows 11 (x64), SDK 10.0.300, runtime 10.0.11, on 2026-10-29, against the
**published** packages: Kronikol 5.27.2 (and 5.12.0, the issue's version, as a control) restored from nuget.org,
and again the same day on the published 5.27.5, which carries #211's release (`rerun-version.sh 5.27.5`): every
outcome the same.
`results/packages.txt` shows that every Kronikol package restored is byte-identical to the `.nupkg` nuget.org serves,
and that every DLL in it matches its extracted copy.

`results/summary.txt` is the table of findings. The rest of this file says what each project is and how to run it
again.

## The findings in brief

- In every async test, under xUnit v2 and v3, in Debug and in Release, on 5.27.2 and on 5.12.0 (Release), the values
  array loads a primary constructor parameter with `ldarg.0; ldfld '<name>P'` straight off the state machine, where
  the compiler's own code reads the same field through `<>4__this` (`ldloc.1`). An ordinary field (T4) is loaded
  through `<>4__this`, which is right, and so is the synchronous method (T3), where `ldarg.0` is `this`.
- On 5.27.2 the test host dies with AccessViolationException (exit code -1073741819) in 3 of 3 runs for T2 (the
  issue's first test), T5 and T6 in Release, and for T1 (the issue's second test, verbatim) and T7 in Debug, the
  same under xUnit v2 and v3. Every other test passes 3 of 3.
- Debug is not safe on 5.27.2. 5.12.0's Debug did not weave these statements with values (#212); 5.27.2's does, so
  the issue's second test, which passes in Release, crashes in Debug.
- No note shows the value read for the parameter. The probes show that the load reads null (T1 and T7 in Release,
  T6 in Debug), the test class's own instance (T5 in Debug) or an object that prints nothing (T2 in Debug), and never
  the parameter's object.

## What is in this folder

| Path | What it is |
|---|---|
| `Directory.Build.props`, `Directory.Build.targets` | Empty on purpose: they stop MSBuild reading the repository's own, so each project builds as a consumer's does. |
| `global.json` | SDK 10.0.300, `latestFeature`. |
| `nuget.config` | nuget.org alone (`<clear />` first), so every Kronikol package is the published one. |
| `run.sh` | Runs a built project three times whole, then three times per test class alone, and writes what each run printed, its exit code and the notes its report holds. |
| `outcomes.py` | Reads `run.sh`'s files and prints each run's outcome (passed, failed, or crashed with the crash's frames and exit code) and the notes. |
| `values_il.py` | Prints every values array the weave builds in an `ilspycmd -il` dump, with each element's load. |
| `rerun-version.sh` | `./rerun-version.sh <version>`: T1 to T7 on the published packages of one version, both projects in Debug and Release, every class through `run.sh`, the woven IL dumped and its values arrays read (`results/outcomes-<version>.txt`, `results/il-values-arrays-<version>.txt`). |
| `prototype.patch`, `prototype-5.27.6.patch` | The fix as prototyped: on 5.27.2, and re-made on 5.27.6 in place of #211's guards (`src/` only). |
| `repro/` | The projects (below). |
| `results/` | Plain-text results (below). |
| `il/` | Whole-assembly `ilspycmd -il` dumps, one per project, version and configuration. |

## repro/

Every project pins Kronikol through a `KronikolVersion` property (`AssertionTrackingVersion` defaults to it) and makes
NuGet's NU1603 an error, so a version nuget.org does not list fails the restore instead of resolving the next one up.

| Project | Packages | Release build |
|---|---|---|
| `XunitV2/` | `xunit` 2.9.3, `xunit.runner.visualstudio` 3.0.2, `Microsoft.NET.Test.Sdk` 18.0.1, `Kronikol.xUnit2` + `Kronikol.AssertionTracking` 5.27.2, `Shouldly` 4.3.0, net10.0 | `DebugSymbols=true`: csc gets `/debug+ /debug:portable /optimize+`, Roslyn's debug-plus mode, as in the issue (xunit.core 2.9.3's props set it) |
| `XunitV3/` | `xunit.v3` 3.2.2, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.5.1, `Kronikol.xUnit3` + `Kronikol.AssertionTracking` 5.27.2, `Shouldly` 4.3.0, net10.0 | `DebugSymbols=false`: `/debug- /debug:portable /optimize+`, a plain Release |
| `XunitV2-5.12.0/` | as `XunitV2/`, on Kronikol 5.12.0 | as `XunitV2/`; compiles `XunitV2/`'s own `AssemblyInfo.cs`, `Types.cs`, T1 and T2 (linked, not copied) |
| `XunitV2-Values/`, `XunitV3-Values/` | as `XunitV2/` and `XunitV3/` | the value probes V1, V2, V5, V6 and V7 (below) |

Each project is a directory of its own: two builds of one project directory that differ only in a property such as
`DebugSymbols` share `obj/` and silently reuse the first build's DLL.

### The tests

One class per test, one test method per class, all in namespace `WeaveRepro`, in both `XunitV2/` and `XunitV3/`:

| | Class | Method | Shape |
|---|---|---|---|
| T1 | `PrimaryConstructorParameterTests(Harness first, Harness second, Harness third) : IClassFixture<Harness>` | `Issue_second_test_verbatim` | the issue's second test as filed: `await Task.Yield();`, then `_exception = await Should.ThrowAsync<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()));`, `_exception.ShouldNotBeNull();` and `(first, second).ShouldNotBe((null, null));` |
| T2 | `IssueFirstShapeTests(Db db, Handlers handlers, State state)`, a class fixture of each | `Issue_first_shape` | the issue's first test (a Reqnroll step there): `_service.ShouldNotBeNull(); using var cts = new CancellationTokenSource(); await cts.CancelAsync(); _exception = await Should.ThrowAsync<OperationCanceledException>(() => handlers.Run(nameof(Service), () => _service.Start(0, cts.Token)));`. The lambda captures a local and `this`, so it lives on a display class |
| T3 | `SyncMethodControlTests`, as T1 | `Sync_method_control` | a control: T1 as a synchronous method, with `Should.Throw<InvalidOperationException>(() => third.Run(...).GetAwaiter().GetResult())`; there `ldarg.0` is `this` |
| T4 | `OrdinaryFieldControlTests(Harness first, Harness second)` with `private readonly Harness _third = new();` | `Ordinary_field_control` | a control: T1 with an ordinary field in place of the third parameter |
| T5 | `OneParameterTests(Harness third)` | `One_parameter` | T1 with one primary constructor parameter; the tuple assertion is left out, as there is no `first` or `second` |
| T6 | `SixParametersTests(Harness first, ..., Harness sixth)` | `Six_parameters_lambda_reads_the_sixth` | T1 with six parameters, the lambda reading the sixth |
| T7 | `ParametersAndValueFieldsTests`, as T1, plus `private long _count = 7; private int _flag = 1;` | `Parameters_and_value_fields` | T1 with a long and an int field beside the parameters |

What the harness adds to the issue's code, and why:

- **xUnit v2**: `[assembly: TestFramework("Kronikol.xUnit2.ReportingTestFramework", "Kronikol.xUnit2")]` beside the issue's
  `[assembly: TestTracking]` and `[assembly: TrackAssertions]`. Under xUnit v2 the report, which the notes are read
  from, is written only by Kronikol's test framework.
- **xUnit v3** has no `[assembly: TestTracking]`. Each test class derives from `DiagrammedComponentTest`, and
  `TestRun.cs` holds the collection fixture that writes the report, as the wiki's Integration-xUnit3 page sets it up.
  The base class has no instance fields, so the test classes' field layout is the same as under xUnit v2.
- **T2 and T6** each have a private method that is not a test and reads the parameters the test does not (`db` and
  `state`; `third` to `fifth`). A parameter nothing reads gets no field. In the issue's binding the other steps read
  them, and in T6 the sixth's field has to come after five others.
- Method names are the harness's (T1's class and body are the issue's).

### The value probes (V1, V2, V5, V6, V7)

No note of T1 to T7 can show the value the weave read for the parameter. The note of a ThrowAsync statement reads
"Should throw InvalidOperationException": Kronikol puts a value into a label only in place of an argument it prints
(`AssertionExpressionFormatter.FormatArgs`), never into a subject or into a `() =>` action, which the label leaves out.
And `ChainValues.Keep` keeps a value read before a method call (here `third` of `third.Run(...)`) only when it is a
scalar type, after calling `GetType()` on it, which is where T6 crashes (`src/Kronikol/Tracking/AccessChain.cs` and
`Track.cs` at 5.27.2).

Each probe is its T test's class and body with one statement added right after the ThrowAsync statement:

```csharp
third.ShouldSatisfyAllConditions(h => GC.KeepAlive(third));   // handlers in V2, sixth in V6
```

The lambda reads the parameter, so the weave loads it the defective way (`ldarg.0; ldfld '<third>P'`, the same load
as the ThrowAsync statement's, as `il/xunit*-values-*.il` shows), and the label prints a lambda that has a parameter,
with the value substituted: `Third should satisfy all conditions [ h => GC.KeepAlive('null') ]`. A value whose
`ToString()` is only its type name prints as nothing (`Track.FormatValue`), so the probes' `Types.cs` gives `Harness`
and `Handlers` a `ToString()` naming them, and each probe class one naming its instance. These are methods, so no
field moves. A value that still prints as nothing is then neither the fixture nor the test class's instance.

Two earlier forms of the probe could not show the value, and were replaced:

- `_exception.ShouldNotBeSameAs(third)`: the weave passes an argument's value as the call received it, which the
  compiler's own code loaded correctly (`ldloc.1; ldfld '<third>P'`), not the defective load.
- `new[] { third }.ShouldContain(h => h == third)`: Shouldly takes that predicate as an expression tree, so the
  statement is woven without values (`Track.AssertionPassed`).

The probes are in projects of their own, so the T1 to T7 assemblies stay as specified, and each was run only where
its T test survives: its statement comes after the ThrowAsync statement the T test dies in.

## Running it again

From this folder, in Git Bash:

```bash
# build, at each configuration (normal verbosity prints the weave's "Instrumented N assertion(s)" line)
(cd repro/XunitV2 && dotnet build -c Release -v:n)
(cd repro/XunitV2 && dotnet msbuild -p:Configuration=Release -getProperty:DebugSymbols -getProperty:DebugType -getProperty:Optimize)

# three whole runs, then three runs of each class alone; results/<label>-<config>-<all|class>.txt
./run.sh xunit2-5.27.2 repro/XunitV2 Release PrimaryConstructorParameterTests IssueFirstShapeTests \
    SyncMethodControlTests OrdinaryFieldControlTests OneParameterTests SixParametersTests ParametersAndValueFieldsTests

# the woven IL, and the values arrays in it
ilspycmd --disable-updatecheck -il repro/XunitV2/bin/Release/net10.0/XunitV2Repro.dll > il/xunit2-5.27.2-Release.il
python -I values_il.py il/xunit2-5.27.2-Release.il
```

The same for `repro/XunitV3` (label `xunit3-5.27.2`) and `repro/XunitV2-5.12.0` (label `xunit2-5.12.0`, classes
`PrimaryConstructorParameterTests IssueFirstShapeTests`). The probes, without the whole-assembly runs:

```bash
RUN_ALL=0 ./run.sh xunit2-values-5.27.2 repro/XunitV2-Values Release PrimaryConstructorParameterTestsValues ParametersAndValueFieldsTestsValues
RUN_ALL=0 ./run.sh xunit2-values-5.27.2 repro/XunitV2-Values Debug IssueFirstShapeTestsValues OneParameterTestsValues SixParametersTestsValues
# and the same two for repro/XunitV3-Values, label xunit3-values-5.27.2

python -I outcomes.py results/xunit2-5.27.2-Release-*.txt   # each run's outcome and the notes, in short
```

The xUnit v2 host's exit code (VSTest does not print it) is in the `--diag` log:
`dotnet test -c Release --no-build --filter FullyQualifiedName~WeaveRepro.IssueFirstShapeTests. --diag:<log>`, then
look for `exited with exitcode` in the log files.

`run.sh` notes:

- It sets `KRONIKOL_HISTORY=off`. Otherwise Kronikol's history ledger goes to the nearest `.kronikol` or `.git` above
  the test output, the repository's root; the first run here wrote `.kronikol/history.jsonl` there before this was set,
  and that file was removed.
- It deletes the project's `Reports/` folder before each run, so the notes after a run are that run's. A run whose
  test host crashed writes no report.
- It reads the notes with the `query.cs` written beside each report (`dotnet run --file Reports/query.cs -- note
  Reports s0/d0`, then each note in full), never by opening a report file. The data file lists assertions only with
  `IncludeTrackedAssertionsInStepList` on, which the harness leaves off, so `assertions` says "This report tracks no
  assertions" and the notes are read from the diagram's notes instead.
- Under xUnit v3 a crashed test process is reported only after about 60 seconds ("Test process crashed with exit
  code -1073741819", 0xC0000005, an access violation), so a set of crashing runs takes about three minutes.

## results/

- `summary.txt`: the table.
- `outcomes.txt`: `outcomes.py` over every run file: each run's outcome and the notes its report held.
- `crash-exit-codes.txt`: the xUnit v2 host's exit code at each crash site, from `--diag` logs.
- `packages.txt`: the versions nuget.org lists and the provenance of every Kronikol package restored.
- `build-<label>-<config>.txt`: each build at normal verbosity (the csc command line, the weave's line). The labels:
  `xunit2-5.27.2` (`repro/XunitV2`), `xunit3-5.27.2` (`repro/XunitV3`), `xunit2-5.12.0` (`repro/XunitV2-5.12.0`),
  `xunit2-values-5.27.2` and `xunit3-values-5.27.2` (the probe projects).
- `<label>-<config>-all.txt`: three whole-assembly runs; `<label>-<config>-<class>.txt`: three runs of one class.
  Each run: the command, everything `dotnet test` printed, its exit code, then `report: none written`, or the
  scenarios and every note of each, as `query.cs` prints them.
- `il-values-arrays.txt`: every values array in every dump, from `newarr [System.Runtime]System.Object` to the Track
  call, with each element's load.

## il/

`<label>-<config>.il`: the whole woven assembly, `ilspycmd -il` (ilspycmd 10.0.1; its `-t` option did not
narrow an `-il` dump to one type, so each file holds every type). Each test's `MoveNext` is the method of its nested
`<Method>d__N` type (a struct in Release, a class in Debug); T3's is the method itself.

## probe/ (the shape catalogue, run through the weaver suite's own builder)

`Probe213Tests.cs` is the catalogue: shapes P1 to P23 and P22b (the issue's two tests, controls, a lambda that uses
`this` only for the parameter, a generic class, an expression tree, an async lambda, a nested type, eight P12 layouts,
the receivers P13 to P17, an awaited statement P18, value types P19, a static local function P20, a static method's own
field read P21, FluentAssertions before and after `.Should()` P22 and P22b, a closure's method P23), each compiled as
Debug, plain Release and debug-plus Release (`TestAssemblyBuilder`), woven, checked by ILVerify (`WovenIl`) and run
woven and unwoven. It is not in the test project: copy it into `tests/Kronikol.Tests.AssertionTracking/` to run it,
and delete it after. `run-all.sh <worktree> <label> [scratch classes to skip]` does the whole round on one worktree's
weaver: it copies the probe and the scratch fixture in, builds, runs the weaver suite (the probe's facts filtered out,
TRX), the dive, the scratch fixture, the cases and the values, and leaves the files in the test project for the next
round (delete them after). `run-cases.sh` runs every case in a process
of its own, in rounds (a crash ends only its case); `run-values.sh` runs the values pass, which records what the weave
put in each values array, named by identity. Both write under a `PROBE213_LABEL`, so a run on another weaver does not
overwrite these results. `Proto213ScratchTests.cs` is the prototype's own 19 shapes (F7, the static field in an
expression tree, the generic class's async lambda, `Receivers`, `InLambdas`), run once against each weaver in one
process (a crash ends the run: `PROTO213_SKIP` leaves classes out, `PROTO213_OUT` names the report). It calls the
weaver by reflection, as the probe does, so the suite's `WovenIlVerifiesTests` scan passes while it is in the project.

## prototype.patch

The design of the plan's section 4.1, a to e, on 5.27.2 (`e4a1c82a`), `src/` only. It was built and measured in the
worktree `C:/Code/Kronikol-213p` (branch `proto/213-receiver-rule`, kept in that worktree's stash). `prototype-5.27.6.patch`
is the same design re-made on 5.27.6 (`7b7a238d`, whose weaver is 5.27.4's), in place of #211's two guards, with
`OwnField` and the carry kept (branch `proto/213-on-5275`, uncommitted).

## results/ from the catalogue and the prototype

| File | What |
|---|---|
| `dive-5.27.2.txt` | the catalogue on 5.27.2: per shape and compile, ILVerify, outcome, the value recorded, the load. Open this first |
| `dive-raw-5.27.2.txt` | the static dump per shape: the ILVerify errors the weave added and every capture's load. It re-detects on the WOVEN IL, so for a statement with an entry spill its load lines are not the emitted IL; the disassembly in `case-compare-proto213.txt` is |
| `case-5.27.2.txt`, `case-logs/` | the runtime: 96 cases once, and the 60 with a wrong load twice more (216 runs); a crash's log holds the runtime's own "Fatal error." stack |
| `values-5.27.2.txt`, `values-logs/` | the values pass on 5.27.2 |
| `baseline-5.27.2.txt`, `baseline-5.27.2-final-probe.log` | the weaver suite on 5.27.2 (1,039 passed, 28 skipped for the .NET 11 SDK) |
| `weaver-suite-proto213.txt` | the weaver suite on the prototype: the same, test for test |
| `dive-proto213.txt` | the catalogue on the prototype (ILVerify errors added: 50 on 5.27.2, 0) |
| `case-proto213.txt`, `case-compare-proto213.txt`, `case-logs-proto213/` | the runtime on the prototype (all 216 runs as unwoven), the per-case comparison and the woven IL of each case that differed |
| `scratch-runs-proto213.txt` | `probe/Proto213ScratchTests.cs` on both weavers (F7's BadImageFormatException, D-B's ILVerify errors) |
| `xunit{2,3}-5.27.5-*.txt`, `outcomes-5.27.5.txt`, `il-values-arrays-5.27.5.txt`, `../il/xunit*-5.27.5-*.il` | T1 to T7 on the published 5.27.5: every outcome as on 5.27.2 |
| `suite-5.27.6.trx`, `suite-proto-5.27.6.trx` (and `.txt`) | the weaver suite on 5.27.6 and on the re-made prototype: 1,431 passed, 28 skipped and the same one failure each, the scan of `WovenIlVerifiesTests` flagging the scratch fixture's old direct call; test for test the same. `woven-il-scan-*.txt`: that test passing once the fixture calls the weaver by reflection |
| `dive-5.27.6.txt`, `dive-proto-5.27.6.txt` | the static dump of the whole catalogue (93 shape-builds): 51 ILVerify errors on 5.27.6, 0 on the prototype |
| `case-5.27.6.txt`, `case-proto-5.27.6.txt`, `case-*-p23.txt` (and their `case-logs-*/`) | the runtime, 120 cases and then P22b and P23 (129 cases, 285 runs per weaver) |
| `values-5.27.6.txt`, `values-proto-5.27.6.txt`, `values-*-p23.txt` | the values pass on both weavers |
| `scratch-runs-5.27.6-*.txt`, `scratch-runs-proto-5.27.6-*.txt` | the scratch fixture per compile: 7, 8 and 8 ILVerify errors on 5.27.6 (`Pc` and the generic `Gen<T>` not run there: they crash), 0 and every method run on the prototype |

## The release, measured on 5.31.1 (2026-11-07)

The plan was executed on 5.31.1 (`896ddad3`), whose weaver had moved three times since 5.27.10 (plan section 11).
`probe/run-all.sh` ran on both weavers: label `main-5.31.1` in the release worktree before the fix, label `proto-main`
in a second worktree holding the fix. The probe's files were then turned into the release's own tests
(`tests/Kronikol.Tests.AssertionTracking/CapturedFieldValueTests.cs`), and these files are what they measured.

| File | What it holds |
|---|---|
| `suite-main-5.31.1.trx.gz`, `suite-proto-main.trx.gz` (and `.txt`), `suite-compare-main-proto.txt` | the weaver suite on 5.31.1 and on the fix: 1,603 passed and 28 skipped each, no outcome changed |
| `dive-main-5.31.1.txt`, `dive-proto-main.txt` | the catalogue's static dump: 51 ILVerify errors on 5.31.1 (as on 5.27.6), 0 on the fix |
| `case-main-5.31.1.txt`, `case-proto-main.txt` (and their `case-logs-*/`) | the runtime, 129 cases: on 5.31.1 the same 21 crash in all three rounds as on 5.27.6; on the fix none, each as unwoven |
| `values-main-5.31.1.txt`, `values-proto-main.txt` (and `values-logs-*/`), `values-compare-main-proto.txt` | the values pass on both weavers, and the 72 cases whose values differ |
| `scratch-runs-main-5.31.1-Debug.txt`, `scratch-runs-main-5.31.1-noPc-*.txt`, `scratch-runs-proto-main-*.txt` | the scratch fixture: on 5.31.1 `Pc` ends the process in Debug, and without it 6 ILVerify errors (5.27.6: 23); F7 is fixed there (5.29.2); 0 on the fix |
| `red-main-5.31.1.trx.gz`, `red-main-5.31.1-b.trx.gz` (and `.txt`) | the new tests on 5.31.1 (the second with an empty `DropWhatCannotBeLoaded` so that they compile): 138 of 264, then 205 of 412 fail, and none ends the process |
| `green-213.trx.gz`, `green-213-b.trx.gz` (and `.txt`) | the same tests on the fix: every one passes |
| `crosssdk-213q.txt`, `crosssdk-213r.txt` | the issue's two tests built by the .NET 8, 9 and 10 SDKs in Debug and Release: 6 of 6 fail at ILVerify on 5.31.1, 6 of 6 pass on the fix |
| `mutations/mutations-213.txt` (and `mutations/mutant-*/`) | the plan's mutations (6.3) on the fix, each with the facts that caught it |
| `outcomes-5.31.3-local.213b.txt`, `xunit{2,3}-5.31.3-local.213b-*.txt` | `rerun-local.sh`: T1 to T7 on the release's weaver, packed locally, over the published 5.31.1 core |
| `review-213q.trx.gz`, `review-213r.trx.gz` (and `.txt`) | after the independent review: the new shapes on the fix with the review's changes (only the three scratch rows, then placeholders, fail) and on 5.31.1 |
| `suite-final-213.trx.gz` (and `.txt`) | the weaver suite on the fix before the review's changes, 2,021 passed and 28 skipped |
| `suite-final-213-b.trx.gz` (and `.txt`), `red-main-5.31.1-final.trx.gz` (and `.txt`) | the weaver suite on the final code, 2,108 passed and 28 skipped; the 505 new facts on 5.31.1, 274 failing and none ending the process |
| `steptracking-213.trx.gz` (and `.txt`), `linux-weaver-213.trx.gz` (and `.txt`) | the step-tracking suite (2,237 of 2,237) and the weaver suite on Linux (1,987 passed, 149 skipped) |
| `mutations/mutations-213-final.txt`, `mutations/mutations-213-M8-rerun.txt` | the mutations again on the final code, with M13 to M19 for the review's changes; M8 once the generic-base expression case was added |

`tools/` holds the scripts that read these: `trx_compare.py` (two TRX files test for test), `trx_fails.py` (a TRX's
failures by fact and cause), `values_diff.py` (two values passes), `gen_notes_table.py` (the decided notes of the new
fact, from two case files) and `mutate213.py` (the mutation run). `rerun-local.sh` runs `repro/` on a local feed.
Machine paths in the logs read `~` for the user's folder.
