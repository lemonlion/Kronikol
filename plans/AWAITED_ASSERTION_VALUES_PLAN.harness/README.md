# Harness for AWAITED_ASSERTION_VALUES_PLAN.md (#211)

Everything the plan cites, measured on Windows 11 with SDK 10.0.300 on 2026-10-27. The `Directory.Build.props` and
`Directory.Build.targets` here are empty on purpose: they stop MSBuild reading the repository's own, so each probe
builds as a consumer's project does.

## repro/

The issue's project as filed: `ReproTests.cs` is its code unchanged, `WeaveRepro.csproj` its project with the Kronikol
version as a property (`KronikolVersion`, default 5.12.0; `AssertionTrackingVersion` defaults to it).

```bash
cd repro
dotnet test -c Release                                   # 2 of 3 fail with NullReferenceException
dotnet test -c Release -p:KronikolVersion=5.11.0         # the issue's version: the same
dotnet test -c Release -p:KronikolVersion=5.26.1         # after #212's first release: the same
dotnet test -c Release -p:KronikolVersion=5.27.4         # the release: 3 of 3 pass
dotnet test -c Debug                                     # 3 of 3 pass
dotnet test -c Release -p:TrackAssertionsEnabled=false   # the weave off: 3 of 3 pass
```

## shapes/

Probes that vary the issue's statement, each a test of its own. P8 and P9 fail on purpose (their assertions are meant
to fail, with the libraries' own messages); every other probe should pass.

| Probe | Shape | 5.12.0, Release |
|---|---|---|
| P2 | ThrowAsync in a `foreach` after an earlier await | NullReferenceException |
| P2b | ThrowAsync in an `if` block after an earlier await | passes (the block's closure is merged into the method's) |
| P3 | the first await, the variable read again after it | passes (the closure becomes a field) |
| P4 | the delegate's task already faulted | NullReferenceException (Shouldly always suspends) |
| P5, P6 | FluentAssertions, the delegate as the subject | pass (the subject is not read) |
| P7 | Shouldly on a task in a plain local | passes (the subject is not read) |
| P8 | a failing ShouldThrowAsync, a control | fails with Shouldly's message, as unwoven |
| P9 | FluentAssertions `WithMessage(message)`, a plain local, failing on purpose | fails with FluentAssertions' message; the note's value is null (see `il/`) |
| P10 | FluentAssertions `WithMessage(…)` reading `act`'s closure | NullReferenceException |
| P11 | a synchronous test, closure capturing one local | passes, read correctly (the closure is in a local) |
| P12 | an async test that never uses `this` | NullReferenceException (the closure is in a local) |
| P13 | a generic async method of the test class | Debug passes; Release NullReferenceException |

```bash
cd shapes
dotnet test -c Release
dotnet test -c Release -p:TrackAssertionsEnabled=false   # the control
```

## frameworks/

One project per test framework, at the versions this repository uses, each with nothing but its packages: what a
Release build of it sets for `DebugSymbols`, which decides whether csc gets `/debug+` (debug-plus mode). Restore one,
then:

```bash
dotnet msbuild -p:Configuration=Release -getProperty:DebugSymbols -getProperty:DebugType -getProperty:Optimize
```

`results/frameworks-release.txt` holds the six answers (true for xUnit v2 alone), the two package files that decide
them, and the csc switches of the suite's `BuildWithSdk` fixture and of a `-p:DebugSymbols=false` build.

## results/

- `repro-<version>-<configuration>.txt`, `shapes-5.12.0-<configuration>[-unwoven|-P11|-P12|-P13].txt`: the runs above.
- The release's: `repro-release-<configuration>.txt` and `shapes-release-<configuration>.txt`, the two projects on the
  release's weaver packed as `5.27.4-proto211b` (core at 5.27.2), as in the prototype recipe below;
  `catalogue-5.27.2.txt`, the shape catalogue with the release's shapes on 5.27.2's weaver (124 of 868 rows fail),
  `catalogue-release.txt` the same on the release's (all pass), and `mutations-release.txt`, plan 6.3's mutations.
- `repro-proto211-*.txt`, `shapes-proto211-*.txt`: the same projects woven by the prototype (below).
- `facts-5.12.0.txt`, `facts-proto211.txt`: the prototype's facts (`AwaitedAssertionResumeTests`) run against the
  5.12.0 weaver and against the prototype; `notes-5.12.0.txt`, `notes-proto211.txt`: every note those runs drew, Debug
  and Release side by side.
- `weaver-suite-proto211.txt`: the whole weaver suite on the prototype.
- `debugplus-probe.txt`: the locals of each `MoveNext` with and without csc's debug-plus mode.
- The audit's: `dive-5.14.0.txt` and `dive-proto211.txt`, every shape of `probe/Dive211Tests.cs` in Debug, plain
  Release and debug-plus Release, with what the weaver captured for each statement, the ILVerify errors the weave
  added, and each run beside its unwoven build; `dive-crash-<weaver>-<build>.txt`, D-A's crash shape and its
  controls; `debugplus-scope.txt`, what debug-plus changes in Debug (nothing) and in Release (20 of 134 methods);
  `weaver-suite-debugplus.txt`, the whole weaver suite with every Release fixture built with debug-plus, on 5.14.0 and
  on the prototype; `frameworks-release.txt` (see `frameworks/`).

## il/

The woven (and for the issue's first test, unwoven and Debug) `MoveNext` of each case the plan cites, dumped with
`ilspycmd -il`; `dive-sync-release-woven.il` is the audit's synchronous D-A shape in plain Release, where the weave
stores the closure in a local of its own and still reads its field off `this`.

## probe/

`Probe211DebugPlusTests.cs`, a fact run once in the weaver test project to compare its compile with csc's: copy it into
`tests/Kronikol.Tests.AssertionTracking/`, set `KRONIKOL_211_PROBE` to an output file and run it with
`--filter FullyQualifiedName~Probe211DebugPlusTests`.

`Dive211Tests.cs` and `dive-TestAssemblyBuilder.patch`, the audit's probe. The patch gives `TestAssemblyBuilder` a
debug-plus Release compile, from `CSharpCommandLineParser` (`DebugPlusRelease`, also on when `KRONIKOL_DEBUGPLUS=1`,
and a debug-plus Debug when `KRONIKOL_DEBUGPLUS_DEBUG=1`). In a worktree of its own, apply the patch, copy the file into
`tests/Kronikol.Tests.AssertionTracking/` and run one fact at a time with `-f net10.0 --filter
FullyQualifiedName~Dive211Tests.<fact>`:

- `Dive`, with `KRONIKOL_211_DIVE` set to an output file: every shape, three compiles, woven and unwoven.
- `Crash`, with `KRONIKOL_211_DIVE_CRASH` set to `Release`, `ReleaseUnwoven`, `ReleaseZero`, `ReleaseDebugPlus` or
  `Debug`: D-A's crash shape. On 5.14.0 the `Release` run ends the test process (exit code -1073741819); every other
  run fails on purpose with "the run came back".
- `EnvCheck`, with `KRONIKOL_211_ENVCHECK` set to an output file (and `KRONIKOL_211_ENVCHECK_DEBUG=1` for Debug): every
  method's locals, to show the switches reach the test host and what debug-plus changes.

The whole suite on debug-plus fixtures is a plain `dotnet test` of the project with `KRONIKOL_DEBUGPLUS=1`; its one
failure is `WovenIlVerifiesTests`, which flags the probe file's own direct calls of the weaver.

## prototype.patch

The design of the plan's section 4.1 a to g, and `AwaitedAssertionResumeTests.cs` with its 24 facts, as measured; it
applies to `1ee6373a` (5.12.0). To weave the probes with it, pack it into `feed/` (not committed) and restore through
`proto.nuget.config`, with a package cache of its own so the machine's is left alone:

```bash
git apply plans/AWAITED_ASSERTION_VALUES_PLAN.harness/prototype.patch
dotnet build src/Kronikol.AssertionTracking -c Release -p:Version=5.12.1-proto211   # on 5.27.x, pack alone finds no DLL (NU5026)
dotnet pack src/Kronikol.AssertionTracking -c Release --no-build -p:Version=5.12.1-proto211 -o plans/AWAITED_ASSERTION_VALUES_PLAN.harness/feed
cd plans/AWAITED_ASSERTION_VALUES_PLAN.harness/repro
NUGET_PACKAGES=<scratch folder> dotnet restore -p:AssertionTrackingVersion=5.12.1-proto211 --configfile ../proto.nuget.config
NUGET_PACKAGES=<scratch folder> dotnet test -c Release --no-restore -p:AssertionTrackingVersion=5.12.1-proto211
```

In Git Bash, give NuGet its sources through the config file: a `--source https://…` argument reached NuGet as a local
path.

Both projects make NuGet's NU1603 an error. Without it, a `feed/` that lacks the version asked for restores the next
version up from nuget.org, with a warning only: `5.12.1-proto211` (named before 5.12.1 went to a Search tab patch)
would weave the probes with the published 5.12.1, which does not carry this fix, and the run would pass for the
prototype's.
