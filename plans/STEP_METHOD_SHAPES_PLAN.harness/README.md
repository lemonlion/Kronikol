# Harness for STEP_METHOD_SHAPES_PLAN.md

Everything the plan marks RUN, so it can be run again. Nothing here is built by the solution.

## What is here

| Path | What it is |
|---|---|
| `probe/` | The shape probe. `Shapes.cs` is the list of method shapes and what each should do, and `ShapesFromReview.cs` the ones an independent review found that it missed, with those a later re-check added (family Z); `Program.cs` compiles each with Roslyn, IL-verifies it, weaves it with this worktree's weaver, verifies again, JIT-compiles every step method, runs its driver and compares. Each shape runs in a child process, since some kill theirs |
| `run-matrix.sh` | The plan's runs on one shape list: the weaver as main has it (the prototype patch taken out) and the prototype, each also compiled with `/debug+`, and the prototype with the JIT at full optimisation and on the .NET 11 runtime. About a quarter of an hour for a run of Debug and Release, on a machine shared with other work |
| `mutations.py` | Takes one decision out of the prototype at a time and names the shapes that should see it go |
| `consumer/` | A user's project, built from packages and run as a program, under each `DebugType` in Debug and Release. `run-consumer.sh <label> <step weaver version> [feed]` |
| `consumer-xunit/` | Three xUnit facts under the real adapter, the second calling a step with an `in` parameter. `run-xunit.sh <label> <version> [feed]` |
| `consumer-ns20/` | A netstandard2.0 driver library, whose `ValueTask` comes from a package, run from net10.0. `run-ns20.sh <label> <version> [feed]` |
| `consumer-reqnroll/` | The issue's own setting: a Reqnroll suite whose step definitions call `[Step]` methods, through `dotnet test` and `Kronikol.ReqNRoll.xUnit2`, read back from the report with the `query.cs` each run writes beside it. `run-reqnroll.sh <label> <version> [feed]` |
| `consumer-lock/` | Three projects whose steps return a type from a project they reference, built, edited and built again on reused MSBuild nodes. A race, so a clean run proves little: read the plan's F19. `run-lock.sh <label> <version> [feed] [extra PackageReference]` |
| `consumer-net11/` | A net11.0 test project under C# 15 on the .NET 11 SDK, as it comes and with runtime async on: each fact's result, what the compiler wrote, and which phase a step leaves its caller in. `DOTNET11=<dotnet.exe> run-net11.sh <label> <version> [feed]` |
| `probe/`'s shapes | They declare the assembly and step attributes themselves, as the build generates them, so the compiler keeps no reference to the core and the step weaver adds one at version 0.0.0.0. That is a real project's form when it names no type of the core, and it is what hid F25 for so long: a real consumer that names a core type does not show it. A consumer here that does is `consumer/`; `consumer-returned-assertion/` does not |
| `consumer-throw-arms/` | Assertions beside a `throw`, in 15 shapes: the note on the path a run takes, with each weaver (F26). `run-throw-arms.sh <label> <weaver version> [feed]`, results in `results/throw-arms-*.txt` |
| `probe/ShapesDiff.cs` | A differential, left out of the catalogue's counts: each row holds one body twice, with an assertion and with a plain identity in its place, and only the first is woven, so any input on which they differ is a run the weave changed. 15 conditions in 31 kinds of statement, in a plain method under a step and as a value step. `DIFFERENTIAL=1 probe all <label> <out dir> D-`, read by `read-differential.py <matrix>`, results in `results/differential-*.txt` |
| `consumer-skipif-count/` | Two refused iterators, only the second with a `SkipIf`: what the warning says of it when the first has none (Q1). `run-skipif-count.sh <label> <step weaver version> [feed]`, results in `results/skipif-count-*.txt` |
| `consumer-conditions/` | An assertion's result used in a condition, with no step in the project: each should read as with no weaver, and a line ending WRONG took the other way (F29). `run-conditions.sh <label> <assertion weaver version> [feed]`, results in `results/conditions-*.txt` |
| `results/*f29-patch*`, `results/conditions-by-library-on-5.28.2.txt` | Q9's patch alone, the assertion weaver with F27, F29 and F30 and the step weaver as 5.28.2 has it: the catalogue (`matrix-f29-patch.tsv`, 18 rows from wrong to right and none the other way), the differential (none of its 405 silent differences left, and a value step's 840 crashes, which are the minor's), the real build, and each decision taken out in turn. The by-library file is that weaver with the fix taken out: what Shouldly, FluentAssertions, AwesomeAssertions and TUnit read on the logic as published |
| `results/*prototype*`, `results/mutations.txt`, `results/breakfastprovider.txt`, `results/coverage.txt`, `results/linux-consumers.txt`, `results/real-build-settings.txt` | Measured again on the code as released, 5.29.0, under the names the plan cites: the package's local version stays `5.28.99-proto226`, and `prototype` in a file's name means the fix, not an earlier cut of it |
| `results/red-minor-on-5.28.3.txt` | Each new fact of the minor over the source of the release before it, with the sentinel in and taken out |
| `results/hand-mutants.txt` | What only a real build sees, taken out by hand: a stale assembly, each weaver's PDB name, a PDB named for itself, the first sequence point |
| `results/linux-suite.txt` | The step project and the condition facts on Linux, in the SDK container |
| `consumer-returned-assertion/` | A library that names no type of the core, with a step that hands back an assertion's task (F25). `run-returned-assertion.sh <label> <weaver version> [feed]`, results in `results/returned-assertion-*.txt` |
| `consumer/run-build-settings.sh` | What a project's settings do to a real build: `DebugType=full` and `pdbonly` on net10.0 and netstandard2.0, Visual Studio's MSBuild on both weavers, and a `DebugType` changed with the old PDB still in `obj`. `WORK=<short path> MSBUILD_EXE=<MSBuild.exe> run-build-settings.sh` |
| `consumer-xunit/run-coverage.sh` | `coverlet.collector` over a woven assembly, on both weavers, line by line. `WORK=<short path> run-coverage.sh` |
| `consumer-weave-time/` | What a weave of 3,000 step methods takes. `run-weave-time.sh <label> <version> [feed]` |
| `consumer-breakfastprovider/` | BreakfastProvider's xUnit, NUnit and TUnit lanes before and after, the prototype's two weavers over the core it pins, comparing their step lists. `WORK=<short path> BP_CLONE=<clone> run-breakfastprovider.sh 5.14.0` |
| `consumer-assertion-handler/` | An assertion first in a `catch` or a `finally`, with no step in the project: the plan's F23. `run-assertion-handler.sh <label> <assertion weaver version> [feed]` |
| `corelib-skew/` | Whether a runtime loads an assembly naming a newer `System.Private.CoreLib`. Build it for net8.0 and net10.0 with `Mono.Cecil` |
| `linux-matrix.sh` | The shape probe on Linux, in the `mcr.microsoft.com/dotnet/sdk:10.0` container, from the Windows-built binaries in `%TEMP%/kronikol-226-probe/bin/Release/net10.0` |
| `linux-consumers.sh` | `consumer/` and `consumer-lock/` built and run on Linux, in the same container, on the published weaver and the prototype. Its header has the command |
| `scrub-results.py` | Takes this machine's home folder out of `results/` |
| `results/` | What the plan quotes |

None of the projects here is built by CI: its discovery lane loops over `tests/*/`, and `plans/` is outside every solution.

## Running it

With the prototype applied (`git -c core.autocrlf=false -c core.eol=lf apply plans/STEP_METHOD_SHAPES_PLAN.prototype.patch`):

```bash
bash plans/STEP_METHOD_SHAPES_PLAN.harness/run-matrix.sh
python plans/STEP_METHOD_SHAPES_PLAN.harness/mutations.py

# Build, then pack: dotnet pack does not build the two weaver projects, which set GeneratePackageOnBuild, and packs what is in bin.
dotnet build src/Kronikol.StepTracking -c Release -p:Version=5.28.99-proto226
dotnet pack src/Kronikol.StepTracking -c Release --no-build -p:Version=5.28.99-proto226 -o <feed>
dotnet build src/Kronikol.AssertionTracking -c Release -p:Version=5.28.99-proto226
dotnet pack src/Kronikol.AssertionTracking -c Release --no-build -p:Version=5.28.99-proto226 -o <feed>
export ASSERTION_TRACKING_VERSION=5.28.99-proto226
bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer/run-consumer.sh prototype 5.28.99-proto226 <feed>
bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-xunit/run-xunit.sh prototype 5.28.99-proto226 <feed>
bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-ns20/run-ns20.sh prototype 5.28.99-proto226 <feed>
bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-reqnroll/run-reqnroll.sh prototype 5.28.99-proto226 <feed>
bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-lock/run-lock.sh prototype 5.28.99-proto226 <feed>
DOTNET11=<dotnet.exe of an 11.0 SDK> bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-net11/run-net11.sh prototype 5.28.99-proto226 <feed>
bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-assertion-handler/run-assertion-handler.sh prototype 5.28.99-proto226 <feed>
bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-conditions/run-conditions.sh prototype 5.28.99-proto226 <feed>
bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-skipif-count/run-skipif-count.sh prototype 5.28.99-proto226 <feed>
bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-returned-assertion/run-returned-assertion.sh prototype 5.28.99-proto226 <feed>
bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-throw-arms/run-throw-arms.sh prototype 5.28.99-proto226 <feed>
bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-weave-time/run-weave-time.sh prototype 5.28.99-proto226 <feed>
WORK=<a folder holding feed/> MSBUILD_EXE=<Visual Studio's MSBuild.exe> bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer/run-build-settings.sh
WORK=<a folder holding feed/> bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-xunit/run-coverage.sh
WORK=<a folder holding feed/> BP_CLONE=<a clone of BreakfastProvider> bash plans/STEP_METHOD_SHAPES_PLAN.harness/consumer-breakfastprovider/run-breakfastprovider.sh 5.14.0
```

A pack leaves its version in `~/.nuget/packages/kronikol.steptracking/` and `kronikol.assertiontracking/`: delete those folders when you are done, and before packing the same version again, or the old one is what a restore finds.

If the probe reports `PROBE` with a missing `ILVerification.dll`, something swept `%TEMP%` under it: it took NuGet's copies and left the files compiled that day, so probably by their old dates (INFERRED). Run from a copy of the binaries instead (`SNAPSHOT=` on `run-matrix.sh`), whose files carry a new date.

Settings a run of the probe takes: `PROBE_CONFIGS=ReleasePlus` compiles each shape as a project whose `DebugSymbols` is true does in Release (`/debug+`), in place of Debug and Release. `DOTNET_ROOT=<a folder holding .NET 11> DOTNET_ROLL_FORWARD=Major` runs the same binaries on that runtime, children included, and `DOTNET11_ROOT` does it from `run-matrix.sh`. Unzip an SDK to a short path, since a long one fails on Windows' path limit, and run the SDK from outside this repository, whose `global.json` pins an older one.

The published runs take `5.28.2` and no feed. A prototype run keeps the core at 5.28.2, which is the point: a new weaver over an older core is the skew the packages allow. It names the packed assertion weaver through `ASSERTION_TRACKING_VERSION`, since a release moves the two together, and `consumer-prototype-over-published-assertions.txt` is the run that does not, the mix a project can hold (the plan's 7.7).

Two results were measured at 5.27.13 and not repeated, since neither weaver changed up to 5.28.2: `results/lock-review.txt`, which is the independent review's own, and `results/lock-prototype-without-the-fix.txt`, which needs a build with the disposal taken out. Everything else in `results/` is from 2026-11-03, at `9d994a4b`.

## Reading a result

`matrix-<label>.tsv` has one row per shape and configuration. `outcome` is the worst of `all`:

| Outcome | Means |
|---|---|
| `OK` | It did what the shape says |
| `CRASH` | The process died before it could report |
| `JIT`, `RUN-IPE` | `InvalidProgramException`, from `RuntimeHelpers.PrepareMethod` or at the call |
| `RUN-OTHER`, `THROWS`, `VALUE` | Another exception, not the one expected, or a wrong return value |
| `OPEN`, `TREE`, `EARLY` | A step left open, the wrong steps recorded, or a step closed before its work was done |
| `LOCKED` | A referenced assembly stayed held for five seconds after the weave, so a reused MSBuild node would keep it from the next build |
| `NOT-REFUSED` | The weave should have listed this method as left as written, and did not |
| `BAD-IL` | It ran, and ILVerify found an error weaving added |
| `WRONGLY-REFUSED` | The weave listed this method as left as written, and it should have been woven |
| `FILE-LOST`, `LEFT-BEHIND` | The weave changed or removed a file of the project's that bears the PDB's name, or left a file of its own beside the output (Z136c) |
| `PDB-PATH` | The assembly names its symbols by anything but that file's own name, exactly: a path carries this machine's folder into a build with mapped paths, and another case finds nothing on Linux (Z136, Z136d) |
| `ASSERTION-LOST` | An assertion ran, and left neither a note nor a word in the build |
| `WARNING` | The build warning the shape expects is missing, or one it should not raise is there |
| `COMPILE`, `PROBE` | A fault of the shape or the probe. None in the quoted runs |

A shape states what should happen, so a run on main reads as what is wrong today, and the same list is the prototype's acceptance. `STRICT_SENTINEL=1` holds the weaver's own sentinel to ILVerify as well; on main every woven assembly fails it (the plan's F9), so that run leaves it out and lists it in the `ilverify` column.
