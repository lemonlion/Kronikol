# Woven assertions lose their text, values and pragmas in a build with deterministic source paths (#165)

**Date:** 2026-10-13 · **Repo version:** 4.20.2 (`origin/main` at 3240c983). Measured on the published 4.18.1
packages; the line numbers are 3240c983's. Of the files cited, only `ReportGenerator.cs` moved after 4.18.1 (34145c44),
by eight lines above its two calls, so every other number holds for both.
The weaver's three source-reading methods, `ReadSourceText`, `CutColumns` and `IsPragmaDisabled`, are byte for byte
the same as at 98c4806d, the commit the issue cites, 93 lines lower: the issue's L775, L783 and L979 are L868, L876
and L1072 here · **Status:** a plan. It green-lights nothing; the questions are in §9.

Evidence labels: **RUN** (measured on 2026-10-13 with the published 4.18.1 packages, SDK 10.0.300, xUnit 2.9.3,
Shouldly 4.3.0 and FluentAssertions 6.12.1 on net8.0; the project, the scripts and the saved output are in
[`WEAVER_SOURCE_PATHS_PLAN.harness/`](WEAVER_SOURCE_PATHS_PLAN.harness/README.md)), **READ** (in the source, tests,
wiki, changelog, SDK or a dependency, at the revision named), **INFERRED** (reasoned from two facts, stated by
neither), **ESTIMATED** (not measured; the step that would measure it is named).

The owner's issue, [#165](https://github.com/lemonlion/Kronikol/issues/165) (2026-10-11, found in the CI report of
#132's multi-host suite on 4.14.6):

> In a build with `ContinuousIntegrationBuild=true`, every tracked assertion is labelled `Assertion at line N`, in the
> diagram notes, the step list and `kronikol query assertions`. In our suite, all 747 assertion notes read that way in
> the CI report, and none in a local run of the same commit. [...] Pragma comments (`pragma:TrackAssertions:disable`)
> are ignored in such a build too, since they are read through the same path.

## 0. Summary

**The defect.** The weave reads each assertion statement from the file its PDB names, and takes that name for a path
on disk. A portable PDB's document name is whatever the compiler was told to write. With deterministic source paths,
which `ContinuousIntegrationBuild=true` turns on, the SDK maps every source root to `/_/`, `/_1/` and so on, and no
machine has those files. Measured on 4.18.1 with the issue's project (§2.2): every note reads "Assertion at line N"
(12 of 12), none carries a value, the three statements its pragma comments disable are woven anyway (12 woven where
9 should be), and the build warns about nothing.

**The underlying problem is wider than the label.** The statement's text is what the weave matches the IL against:
it chooses which variables' values are captured, and since 4.16.0 which arguments' values, and it holds the pragma
comments. So a CI report loses the label, every value and the author's exclusions at once (F1, F3). The weave is
already handed half of the map that would undo it: `@(SourceRoot)`, with each root's `MappedPath`, has reached the
task since the weaver's first commit (2.30.7, 2026-05-06), and nothing reads it (F2). The other half, a map a project
writes itself, only `$(PathMap)` carries (F4, row g). Nothing in this repository builds a woven project with
deterministic paths, or runs the weaver's targets file at all, so no test could have failed (F10).

**The plan.**

- **R1 (a patch).** The weave reads each statement where the compiler read it: the document's name mapped back through
  `$(PathMap)` and `@(SourceRoot)`, the compiler's own map, read by the compiler's own rules, and taken only when the
  file hashes to the checksum the PDB records; else the source the PDB embeds, when the build embeds it; else the name
  as written, as today. It decodes the file and splits its lines as the compiler does, so the PDB's line numbers point
  at the right text (F12). The woven calls keep the name the compiler wrote, so the woven assembly stays
  byte-identical across clones (F6). Three more defects of the same component, found on the way, ride with it:
  `TrackAssertionsEnabled=false`, the documented off switch, breaks the build of every project that declares
  `[assembly: TrackAssertions]` (F7); the report reads no expected or actual value from Shouldly's failure text, and
  clusters it by its actual value, when Shouldly could not read the source itself (F8); and the weave's rewrite of the
  PDB points Roslyn's type-to-document records at the wrong files (F13).
- **R2 (a minor).** `KRONIKOL002`: a warning when a woven statement's source still cannot be read, naming the file
  and the paths tried, so a label that falls back is never silent again (F9).

## 1. How far each claim was checked

- **Every RUN finding was measured on the published 4.18.1 packages** with the issue's project (`harness/probe/`,
  the #163 scaffold): eleven build configurations, rows a to k (§2.2), one clean `dotnet test` each; the same project
  with three tests that fail on purpose, in a local and a CI build and from the built DLL; and a build of one commit
  from two clones at different paths. Every count quoted is in `harness/s0/`.
- **The owner's suite** (747 notes) is the issue's and was not re-measured; its acceptance is §5.4.
- **Every code path is READ at 34145c44** with the line cited, and every citation was read again at c28e8948. A
  research pass read every place `src/` takes a path from a PDB, a stack frame or `[CallerFilePath]` (§2.4).
- **The SDK's targets** were READ in the installed SDKs 8.0.303, 8.0.421, 9.0.314 and 10.0.300 (their task
  assemblies decompiled), Roslyn at `main` ee4be673 (2026-10-13) and Mono.Cecil at its 0.11.6 tag (§2.3). The measured
  `PathMap` and `@(SourceRoot)` values were printed by a target that runs where the weave's does, after
  `CoreCompile` (`_DumpPathMap` in the probe). A second scratch build (`harness/mechanics/`: a submodule, a linked
  file, a source generator, a `#line` directive, a type with no methods, a BOM-less Windows-1252 file and a file
  holding U+2028) checked the compiler's side of each rule R1 relies on.

The issue's claims, one by one:

| The issue says | Verdict |
|---|---|
| A CI build labels every tracked assertion "Assertion at line N", a local build none | **Confirmed** (RUN): row b 12 of 12, row a 0 of 9 |
| The build still says `Instrumented N assertion(s)`, and nothing at normal verbosity says the labels fell back | **Confirmed** (RUN), and it instruments 12 where 9 should be (F3); no row gave any warning |
| `ContinuousIntegrationBuild` turns on `DeterministicSourcePaths` (`Microsoft.Managed.Core.targets` L215 in SDK 10.0.401), which writes each source root as `/_/…` | **Confirmed** (RUN, READ): L215 in 10.0.300 too (L208 in 8.0 and 9.0). Row f (`DeterministicSourcePaths=true` alone) is byte-identical to row b; the trigger is that property, whatever sets it (§2.3) |
| DotNet.ReproducibleBuilds sets `ContinuousIntegrationBuild` on GitHub Actions, Azure Pipelines, TeamCity and the other common CI services | **Confirmed** (READ, v2.0.5): on `CI`, `TF_BUILD`, `GITHUB_ACTIONS`, `APPVEYOR`, `TRAVIS` or `CIRCLECI` set to `true`, and on AWS CodeBuild, Jenkins, Google Cloud Build, TeamCity and JetBrains Space by their own variables. It also defaults `DebugType` to `embedded`, the weave's other symbols path (row e) |
| Pragma comments are ignored too | **Confirmed** (RUN): the three statements they disable are woven (F3) |
| The repro: 0 of 10, 10 of 10, 0 of 10 | **Reproduced** on 4.18.1 as 0 of 9, 12 of 12, 0 of 9 (rows a, b, c) |
| The cause: `ReadSourceText` reads `Document.Url` with `File.Exists`, `IsPragmaDisabled` the same way | **Confirmed** (READ), L876 and L1072 here |
| `Document.Url` is `/_/…`, "which no machine has" | **Confirmed**, with a correction: `/_/` is not always the repository's root. Outside a git repository the NuGet cache takes it (row h), and a project's own `PathMap` writes any prefix (row g, `/src/`) |
| Workaround: `DeterministicSourcePaths=false` | **Confirmed** (RUN): row c is byte-identical to row a. It puts the build machine's paths back into the PDB |
| Suggestion 1: pass `@(SourceRoot)` with `MappedPath` (or `$(PathMap)`) and map a prefix back | **The right direction, and half built**: `@(SourceRoot)` is passed today and never read (F2). On its own it cannot undo a project's `PathMap` (row g: the roots' `MappedPath` stays local), so R1 reads `$(PathMap)` first |
| Suggestion 2: read the source from the PDB when it is embedded | **Works where the build embeds** (row d: the source survives the weave's rewrite of the PDB), but a tracked test file is embedded only under `EmbedAllSources`, a project's choice (§2.3), so it is R1's second source, not the fix |
| Suggestion 3: warn, naming the path tried | **Yes**: R2 |
| The weave runs in the same build as the compiler, so the sources are on disk | **Confirmed** (READ): `AfterTargets="CoreCompile"`, targets L47 |

## 2. Where it stands today

### 2.1 The code (READ)

- **The task.** `WeaveAssertionsTask` declares `SourceRoots` ("Root paths for source files, used to locate source
  code referenced by PDB sequence points", `WeaveAssertionsTask.cs` L23 to L26), and the targets fill it
  (`SourceRoots="@(SourceRoot)"`, `Kronikol.AssertionTracking.targets` L54). `ExecuteCore` (L47 to L111) uses
  `AssemblyPath` and `References` only, and `AssertionWeaver`'s constructor (L25) takes a log and search directories.
  `$(PathMap)` reaches neither. `git log -S SourceRoots` finds the property in b4b92eb9 (2026-05-06, 2.30.7, the
  weaver's first commit, then under `src/TestTrackingDiagrams.AssertionTracking`) and nowhere since but the 3.0.0
  rename.
- **Where the text is read.** `FindAssertionStatements` asks `IsPragmaDisabled(sp)` (L602), then
  `ReadSourceText(sp)` (L620). `ReadSourceText` (L868 to L970) looks the document up by `sp.Document.Url` in
  `_sourceFileCache` (L23), reads it with `File.Exists` and `File.ReadAllLines` (L876), and answers
  `assertion at line {N}` when it is not there (L881, L884 and the catch at L968). `IsPragmaDisabled` (L1066 to
  L1100) repeats the lookup and answers false when the file is not there (L1076 to L1077): the statement is woven.
- **What the text decides.**
  - The label: the woven call passes it as the expression (taken at L2714 and L3103 for the call's `ldstr`).
  - The variables whose values are captured: `DetectCapturedVariables` (L674) keeps a variable only when
    `NameAppearsInExpression(name, sourceText)`, at L1457, L1478, L1506, L1544, L1577, L1613, L1659, L1681, L1733 and
    L1811. No name appears in "assertion at line 31".
  - The arguments whose values are captured (4.16.0): `DetectCapturedArguments` (called at L686; L1114 to L1191)
    finds `.Should()` in the text (L1140) and the call's argument texts (`ArgumentTexts`, L1152), and gives up on
    null.
  - Shouldly's custom message, removed from the label by `WithoutCustomMessage` (L627).
  - The pragma comments.
- **What the woven call carries.** `filePath = assertion.SequencePoint.Document.Url` (L2715, L3104) is passed as
  `callerFilePath`. At run time `Track.SourceFileName` keeps only the last segment (`Track.cs` L380 to L387), for the
  step's source location (L345) and the note's PlantUML comment (L368 to L369), so a `/_/…` name reads the same there
  as a local path. The local `fileName` at L2718 to L2720 is computed and never used.
- **What reads the same files besides.** Nothing in the weave: `StepWeaver` never reads documents or sequence points
  (`StepWeaver.cs` L57 to L139).

### 2.2 The measurements (RUN)

Rows a to k (`harness/s0/<row>-summary.txt`). "Woven" is the weave's own count; a weave that honours the pragmas
instruments 9 statements of the probe, one that ignores them 12; rows j and k add a test of their own.

| Row | Build | Woven | "Assertion at line N" | Values in the labels | Pragma statements woven |
|---|---|---|---|---|---|
| a | `dotnet test` | 9 | 0 of 9 | yes | no |
| b | `-p:ContinuousIntegrationBuild=true` | 12 | **12 of 12** | **none** | **yes** |
| c | b + `-p:DeterministicSourcePaths=false` | 9 | 0 of 9 | yes | no |
| d | b + `-p:EmbedAllSources=true` | 12 | 12 of 12 | none | yes |
| e | b + `-p:DebugType=embedded` | 12 | 12 of 12 | none | yes |
| f | `-p:DeterministicSourcePaths=true` | 12 | 12 of 12 | none | yes |
| g | `-p:PathMap=<project dir>\=/src/` | 12 | 12 of 12 | none | yes |
| h | b, outside any git repository | 9 | 0 of 9 | yes | no |
| i | b in Release | 12 | 12 of 12 | none | yes |
| j | b + a linked file from outside the repository | 13 | 12 of 13 | the linked file's only | yes |
| k | b in a repository with a git submodule holding a test | 13 | 13 of 13 | none | yes |

- Row a's labels, which every fallback row loses: "Actual id should be 'A1'" (Shouldly L31, FluentAssertions L37),
  "Actual colour should be 'Blue'" (L32, L35), "First value of actual lines should be 'x'", "Result split id should
  be 'S1'", "Response status code should be OK".
- c is byte-identical to a, and f to b (DLL and PDB). No row printed a warning.
- The PDB documents of b: `/_/IssueStack/Scenarios/IssueFeature.cs`, and the test SDK's
  `/_1/microsoft.net.test.sdk/18.3.0/build/net8.0/Microsoft.NET.Test.Sdk.Program.cs`. g: `/src/Scenarios/…`. j: the
  linked file keeps its absolute path, and is the one statement labelled. k: the submodule's root is nested
  (`/_/IssueStack/External/`), and `PathMap` lists only the top-level roots, which map its files too.
- **The failing tests** (`a-fail`, `b-fail`): the labels go from `✗ Id should be "B2"`, `✗ Result should be 5` and
  `✗ Total should be 8` to `✗ Assertion at line 33`, `17` and `25`. Each library's own message follows the label;
  §2.4 says what CI does to those.

### 2.3 What the SDK writes (RUN, READ)

- `$(PathMap)`, as the weave's target sees it in row b, after `CoreCompile`:
  `C:\Users\cex\.nuget\packages\=/_1/,C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\=/_2/,<S>\repo\=/_/,`.
  The trailing comma is the project's own, empty `PathMap` appended after the roots'. `@(SourceRoot)` holds the same
  three roots, each with that `MappedPath`. In rows a and c, `PathMap` is empty and each root's `MappedPath` is the
  root itself.
- The repository's root takes `/_/`, and the two NuGet roots `/_1/` and `/_2/`. Outside a repository (row h) the
  NuGet cache takes `/_/`: the project's own files are under no root, keep their absolute paths, and are read. A
  project's `PathMap` (row g) is applied with no `DeterministicSourcePaths`, and the roots' `MappedPath` stays local.
- A nested root (row k, a submodule) carries `NestedRoot` and `ContainingRoot` metadata and a `MappedPath` under its
  container's (`/_/IssueStack/External/`); `PathMap` takes only top-level roots, and the container's entry maps the
  nested files too.
- **Where the roots come from** (READ, RUN). NuGet restore writes its package folders as `SourceRoot` items into
  `obj/*.nuget.g.props`, so they come first; Source Link's `LocateRepository` (implicit since SDK 8,
  `Microsoft.Build.Tasks.Git.targets` L25 to L36) adds the repository and its submodules at execution time. There is
  no fallback to the project's directory. `MapSourceRoots` numbers source-controlled top-level roots first
  (`"/_" + (index == 0 ? "" : index) + "/"`) and gives a nested root its container's mapped path plus its own
  (`MapSourceRoots.cs` L99 to L226). Outside a repository nothing warns unless the project references Source Link
  itself, and the build fails only when no top-level root exists at all ("SourceRoot items must include at least one
  top-level (not nested) item when DeterministicSourcePaths is true").
- **When the property turns on** (READ, RUN): `Microsoft.Managed.Core.targets` L215 (10.0.300) sets
  `DeterministicSourcePaths` when it is empty and `Deterministic` and `ContinuousIntegrationBuild` are true. That file
  is imported before `Directory.Build.targets`, so `ContinuousIntegrationBuild` set there leaves `PathMap` empty; set
  in `Directory.Build.props`, the project or on the command line, it maps (`harness/mechanics/directory-build.txt`).
- **`PathMap`'s composition** (`_SetPathMapFromSourceRoots`, L280 to L302): the top-level roots only, each key and
  value escaped (`,` as `,,` and `=` as `==`), joined by `,`, then `,$(PathMap)`, the project's own value appended
  unescaped, which leaves the trailing comma when it is empty. The target runs `BeforeTargets="CoreCompile"` with no
  inputs and outputs, so a target after `CoreCompile` sees the property and each root's `MappedPath` on a full build
  and on one where `CoreCompile` is skipped as up to date (RUN, `harness/mechanics/build-ci.txt`). `Csc` gets it as
  `/pathmap:` (`Microsoft.CSharp.Core.targets` L180).
- **How the compiler applies it** (Roslyn, READ): `CommandLineParser` (L349 to L430) splits on a single `,` and `=`,
  reads a doubled one as the character, skips an empty entry, and gives both sides a trailing separator
  (`EnsureTrailingSeparator`: `/` if the text holds only `/`, `\` if only `\`, else the platform's). It then **sorts
  the entries by key length, longest first** (`SortPathMap`, L1407 to L1408, since dotnet/roslyn#49670 in SDK
  5.0.200), so the comment in `_SetPathMapFromSourceRoots` that "the compiler applies the first mapping that
  matches" is out of date. `PathUtilities.NormalizePathPrefix` (L729 to L763) takes the first key that begins the
  path, ordinally (case matters), and turns every separator in the result into the replacement's kind when the
  replacement uses only one. The path is made full first (`Path.GetFullPath`, so `..` is resolved).
- **What it maps** (READ, RUN): PDB documents, `#line` targets, `[CallerFilePath]` and the PDB path in the CodeView
  entry. In the mechanics build: a tracked file `/_/App/Program.cs` (not embedded); a submodule's `/_/inner/Inner.cs`;
  the files in `obj/` and a source generator's output under `/_/App/obj/…` (embedded, the generator's always, the
  `obj/` files as untracked); a linked file outside the repository keeps its local path (embedded, untracked); a
  `#line 100 "Mapped.txt"` target is `/_/App/Mapped.txt` with no checksum. `EmbedUntrackedSources` is on by default
  (`Microsoft.NET.Sdk.SourceLink.targets` L24); **a tracked test file is embedded only under `EmbedAllSources`**.
- **The checksum** (READ, RUN): the compiler hashes the bytes it read (`SourceText.cs` L297), SHA-256 unless
  `ChecksumAlgorithm` says SHA1, and Cecil exposes it as `Document.Hash` and `Document.HashAlgorithm`. Mapping the
  mechanics build's documents back through its parsed `PathMap` and hashing the files matched every document on disk,
  the Windows-1252 file included (`harness/mechanics/pdbdump.txt`).
- **How the compiler reads a file** (READ, RUN): a byte order mark wins (UTF-8, UTF-16 LE or BE only). Else, when the
  build sets `$(CodePage)`, that code page decodes the file, valid UTF-8 or not: `CommonCompiler.cs` L273 passes it as
  the default encoding, and `EncodedStringText.Create` tries UTF-8 only when there is none (L112). With no `CodePage`,
  strict UTF-8, and on invalid bytes the ANSI code page, Windows-1252, then Latin-1 (L33 to L42, L91 to L133). It
  breaks lines at CR, LF, CRLF, U+0085, U+2028 and U+2029, and a sequence point's columns count UTF-16 units within
  those lines. `File.ReadAllLines`, the weave's reader, decodes a BOM-less non-UTF-8 file as UTF-8 (`"café"` in
  Windows-1252 read back with U+FFFD) and breaks only at CR and LF, so in a file holding U+2028 the PDB's line 6 was
  `ReadAllLines`' `}` (F12).
- **Mono.Cecil 0.11.6** (READ, RUN): embedded source is the document's `EmbeddedSourceDebugInformation`, whose
  `Content` Cecil inflates on first read (`AssemblyReader.cs` L3240 to L3257): the file's own bytes, byte order mark
  included. Read it and Cecil deflates it again when it writes the PDB (1,845 bytes became 1,835, same text); leave it
  and the blob is copied as it was (`harness/mechanics/pdbdump.txt`). (`Document.EmbeddedSource` exists, but the
  reader never sets it: it stays empty.) Writing the PDB back keeps the documents' names, checksums, the Source Link
  map and the compilation's records, keeps the MVID and timestamp, and recomputes the PDB id; two writes of one input
  are byte-identical.

### 2.4 Who else reads these paths (READ, RUN)

- **The run time is not affected.** Every reader of `callerFilePath` keeps the file name (§2.1). The "Thrown at"
  frame (`FailureText.ThrownAt`, L189 to L276) parses `in /_/…:line N` and matches the scenario's file by name, and
  prints the frame as the runtime wrote it; `ThrownAtTests` L115 to L153 already hold `/_/` frames.
  `Scenario.SourceFile` never comes from a PDB. Nothing in `src/` calls `StackFrame.GetFileName`.
- **Shouldly reads the source too, at run time, and undoes the map itself.** Its `buildTransitive/Shouldly.targets`
  (4.3.0) runs `CapturePathMapsForShouldly` before `CoreCompile` when `DeterministicSourcePaths` is true: it writes
  `$(PathMap)` beside the binary (`ShouldlyPathMaps_<assembly>`) and sets `SHOULDLY_SOURCE_PATH_MAP` to it in the
  MSBuild process, and under `--no-build` `SetShouldlyPathMaps` sets the variable from the file before `VSTest`. Its
  run time reads only the variable (4.3.0's DLL holds the variable's name and the `^/_\d*/` pattern, not the file's)
  and swaps the mapped prefix back. So the map reaches a test host that MSBuild's `VSTest` task starts: Shouldly's
  failure text names the subject in a CI build run by `dotnet test`, with or without `--no-build` (RUN, `b-fail`,
  `b-fail-nobuild`). Run the DLL directly (`dotnet test bin/Debug/net8.0/IssueStack.dll`, RUN, `b-fail-dll`) and no
  variable is set: Shouldly writes the actual value where the code would be and ends "but was not" (F8).
  5.0.0-preview.2 reads the file itself (READ: its DLL holds the file's name). By its pattern, a project's own `/src/`
  map is not undone either (INFERRED). It is the same idea as R1's, done at run time.
- **FluentAssertions 6.12.1 reads the caller's source too and does not undo the map**: in a CI build its message says
  `Expected string to be "B2"` where a local build says `Expected id to be "B2"` (RUN). Upstream; §8.
- **Ingest** treats a `/_/…` source path as absolute and outside the source root, keeps it, and says the report
  "carries a build machine's paths" (`IngestPipeline.cs` L856 to L860). No producer known to write such a path; §8.
- **Prior art in this repository:** `query.cs` takes its own location from `EntryPointFilePath`, not
  `[CallerFilePath]`, because it holds under `ContinuousIntegrationBuild` (`QUERY_FALLBACK_PLAN.md` L454 to L455), and
  `QueryScriptEndToEndTests` builds a generated repository with `ContinuousIntegrationBuild` on (L204). The weaver was
  never checked the same way.
- **Prior art elsewhere** (READ): Coverlet's collector writes `@(SourceRoot)` with each `MappedPath` beside the binary
  (`CoverletSourceRootsMapping_<assembly>`, `coverlet.collector.targets` L35 to L57, v10.1.0) and its reader replaces
  the mapped prefix and checks the file exists (`SourceRootTranslator.cs` L86 to L162). Shouldly (above) passes
  `$(PathMap)` the same way but splits it on `,` and `=` with no unescaping and no sorting. ReportGenerator guesses: a
  `\/_\d?\/` pattern (which misses `/_10/`) and a list of directories to try. AssertMessage.Fody, the nearest weaver
  to this one, reads `File.ReadAllLines(sequencePoint.Document.Url)` and has this defect too. Source Link's own
  documentation says to turn deterministic paths on only on a build server, never locally (`docs/README.md` L75),
  which is why the defect shows only in CI. R1 takes Coverlet's and Shouldly's idea (the build hands the map over)
  with the compiler's own parsing and the PDB's checksum, and does it in the same build, so nothing needs writing
  beside the binary.

### 2.5 Why nothing caught it (READ)

- The weaver's facts compile in-process with Roslyn and write each source file at the absolute path the syntax tree
  names (`TestAssemblyBuilder.cs` L49 to L122, L70 to L77), with no path map and no embedded text, so the file is
  always there. `BuildWithSdk` (L191 to L313) runs a real `dotnet build` and then weaves through the API, not the
  targets. `WeaveAssertionsTaskTests.RunTask` (L66 to L84) sets `AssemblyPath` and `References` only.
- No test runs `Kronikol.AssertionTracking.targets` through a build: the one project referencing the weaver
  (`Kronikol.Tests.AssertionTracking`, a `ProjectReference`) does not import a package's `build/` files.
- Nothing in the repository sets `ContinuousIntegrationBuild`, `DeterministicSourcePaths`, `PathMap` or
  `EmbedAllSources` (`Directory.Build.props`, `ci.yml`, `release.yml`, `codeql.yml`), except the generated repository
  above. BreakfastProvider, the consumer, declares `[assembly: TrackAssertions]` in seven test projects and does not
  set it either, so its published reports are drawn right.
- No fact covers the "assertion at line N" fallback at all.

## 3. Findings the issue does not state

| # | Finding | Evidence |
|---|---|---|
| F1 | **Every value goes with the label.** The text chooses the captured variables and, since 4.16.0, the captured arguments, so a CI note shows neither; the work of #163 (4.14.8, 4.16.0) never reaches a CI report. The woven DLL is 2,048 bytes smaller in row b than in row a, though it weaves three statements more | RUN (rows b to k), READ (§2.1) |
| F2 | **The fix was wired halfway in 2.30.7 and never finished.** `SourceRoots` reaches the task and nothing reads it | READ |
| F3 | **Pragmas fail open.** A statement its author excluded is woven when the file cannot be read: 12 woven where 9 should be | RUN |
| F4 | **`/_/` is not a rule to undo.** Outside a repository the NuGet cache takes `/_/` (row h), and a project's `PathMap` writes any prefix (row g). The only correct inverse is the map the compiler was given: `$(PathMap)`. Shouldly's run-time regex (`^/_\d*/`) would miss row g | RUN, READ |
| F5 | **`EmbedAllSources` is no workaround today.** The source is in the woven PDB (row d keeps it for every document with sequence points) and the weave never looks there | RUN |
| F6 | **The woven output is deterministic only because the fallback is.** Built from two clones with `ContinuousIntegrationBuild`, the woven DLL and PDB are byte-identical (`697253b8…`, `eba9c271…`), and the DLL holds `/_/IssueStack/Scenarios/IssueFeature.cs` and no local path. A default build's woven DLL holds the absolute source paths. A fix that wrote the resolved local path into the woven calls would leak the build machine's paths into every CI binary and break that identity | RUN (`determinism.txt`, `determinism-bytesearch.txt`) |
| F7 | **The documented off switch breaks the build.** `-p:TrackAssertionsEnabled=false` fails `error CS0234: The type or namespace name 'TrackAssertionsAttribute' does not exist in the namespace 'Kronikol.Tracking'`: the attributes are generated only when the switch is on (targets L4 to L6, since ec276b76, 2026-05-08), and the wiki's "Disabling the Weaver" (Assertion-Tracking.md L306 to L315 at 85abe19) tells a project with the attribute to set it | RUN (`harness/s0/f7-off-switch.txt`), READ |
| F8 | **Shouldly's text without its source loses the report's expected and actual values.** When Shouldly cannot read the caller's file (4.3.0's map not handed to the test process, as when the DLL is run directly in a CI build; tests run where the sources are not; or `ShouldlyConfiguration.DisableSourceInErrors()`), it writes the actual value where the code would be: `4` / `    should be` / `5` / `    but was not`. `ErrorDiffParser`'s Shouldly pattern (L28) needs `    but was` and the value after it, so `Failures.md` and the HTML report's diff (`FailuresDigestGenerator.cs` L256, `ReportGenerator.cs` L2230 and L3296) show no expected or actual value. `FailureText.ClusterKey` (L63) reads the first line as the subject's code; here it is the actual value, so failures of one assertion with different values do not share a cluster, against its own doc comment. Of the 33 checks measured, the 9 whose text with the source ends `    but was` and the actual value end `    but was not` without it | RUN (`b-fail-dll-Failures.md`, `harness/shouldly/forms.txt`), READ |
| F9 | **Silent at every verbosity.** The weave logs nothing when it falls back, not even among its low-importance diagnostics | RUN, READ |
| F10 | **Nothing could catch it** (§2.5): no test builds through the targets, none maps a path, and no build here or in BreakfastProvider sets the property | READ |
| F11 | **`query compare` reads two fallback labels on one line as one assertion**: it compares `Display [Status]` sequences (`QueryCommand.Search.cs` L247 to L249, L307 to L321). Gone with R1 | READ |
| F12 | **The weave reads a file differently from the compiler, in every build.** `File.ReadAllLines` decodes a BOM-less non-UTF-8 file as UTF-8 (a Windows-1252 `"café"` is labelled with U+FFFD in place of its `é`), does not follow a build's `CodePage`, and does not break lines at U+0085, U+2028 or U+2029, which the compiler does. After such a character every sequence point's line points at other text, so a statement is labelled with a neighbour's code, or with whole lines when the columns do not fit (`CutColumns`, L977 to L995): a wrong label, not a fallback. Local builds included | READ, RUN (`harness/mechanics/tdd.txt`: the PDB's line 6 of `Lsep.cs` read as `}`, and `café` with U+FFFD) |
| F13 | **The weave's PDB rewrite misplaces Roslyn's type-to-document records.** The `TypeDefinitionDocuments` custom debug information (which documents a type without method bodies lives in) is copied with its old document row numbers, while Cecil writes only the documents that have sequence points (12 became 5 in row b) and renumbers the types; reading it back by token, types that had none pick up another's. In the mechanics build the one record, on the type with no methods, became two: the type that took its old row number (the regex generator's `Utilities`) gained one, and both name `Ansi.cs`, the document now at the old document row. Reading every type's records before the write keeps the record on its own type, still naming `Ansi.cs`; removing them after that read leaves none | RUN (`harness/mechanics/tdd.txt`, `tdd-touch.txt`, `tdd-drop.txt`), READ (`TypeDefinition.cs` L290 to L297) |

## 4. The design

### 4.1 R1 (a patch): read each statement where the compiler read it

**R1a, the source of a document.** A new internal class, `SourceDocuments` (`src/Kronikol.AssertionTracking/
SourceDocuments.cs`), answers one question, the lines of a PDB document, and both readers ask it: `ReadSourceText`
and `IsPragmaDisabled` keep their rules and lose their own cache and `File.Exists` (L23, L874 to L878, L1068 to
L1074). It looks for the document's bytes in this order and keeps the first that answers:

1. **A file the compiler's map leads back to.** The candidates: for each `$(PathMap)` entry whose value begins the
   name, the entry's key and the rest of the name, the rest's separators turned to the key's kind (the compiler turns
   the whole name to the value's when the value uses one kind, `PathUtilities.cs` L752 to L758, so on Linux a value
   written with `\` leaves no `/` to find); the same for each `@(SourceRoot)` whose `MappedPath` differs from
   its path; then the name as written. Longer matched values come first. A candidate is taken when the file exists
   and, where the PDB records a checksum for the document, its bytes hash to it.
2. **The source the PDB embeds** (the document's `EmbeddedSourceDebugInformation`), when the build embeds it: a
   generator's output always, untracked files such as those in `obj/` by default where Source Link finds a
   repository, and a tracked file only under `EmbedAllSources`.
3. **The name as written, unchecked**, when the file is there but fails the checksum: what the weave does today, so
   a local build never reads less than it did. A mapped candidate is never taken unchecked: a file that fails the
   checksum there is another file.

When nothing answers, the label falls back as today, and the document, with every candidate tried, is recorded in
`WeaveResult` for R2. Every document read through a map is listed at low importance with where it was found.

Why this order and these rules:

- **The compiler's map, not a pattern.** F4: `/_/` is sometimes the NuGet cache, and a project's `PathMap` can write
  any prefix. `$(PathMap)` is exactly what `Csc` was given (§2.3), the roots' entries and the project's; R1 parses it
  with the compiler's rules (a single `,` and `=` split, a doubled one is the character, empty entries skipped, a
  trailing separator on both sides), since a key or value read differently is a candidate that never exists.
  `@(SourceRoot)` adds nothing to it in a normal build; it stays a source because the task already receives it, and a
  build that names roots without `PathMap` (a custom target) still gets them.
- **The checksum makes a candidate certain.** Two entries can map different directories to one value, or to values
  one of which begins the other (`/src/` and `/src/sub/`), and then one name has two candidates, each perhaps on
  disk; only the file the compiler read has the bytes it hashed. The compiler's order (the longest key first, §2.3)
  decides which name a file gets, not which file a name came from, so it does not settle this; the checksum does.
  Where the PDB records none (a `#line` target), the longest matched value wins, which is right whenever the values
  do not overlap. The cost is one hash per document holding a woven statement.
- **A checked file before embedded source.** A file with the PDB's checksum is the compiled text byte for byte, so
  embedded source adds nothing where one exists, and reading it costs something: Cecil deflates a source it has read
  again when it writes the PDB (§2.3), so the woven PDB's bytes would change for every build that embeds. Coming
  second, it still covers what no file holds: a generator's output, a tree moved after the compile.
- **The name as written stays.** In a build with no map, the first candidate is the name itself, so a local build
  reads the same file as today and every existing fact keeps its input.
- **Considered and not taken: mapping forward from `@(Compile)`.** Applying the compiler's map to every compiled
  file's full path and looking the name up gives no ambiguity at all, but it must reproduce the compiler's
  normalisation exactly (full paths, the longest key first, separators) and still misses `#line` targets and
  generated files, which are not `Compile` items. The inverse with the checksum is exact wherever the compiler left a
  checksum, which is every file it read, and needs only the map.
- **Considered and not taken: matching `@(Compile)` by checksum alone.** Hashing each compiled file whose name ends as
  the document's does, and taking the one with the PDB's checksum, needs no map at all, but it finds only `Compile`
  items, and a document can name a file that is not one: a `#line` target, such as the `.razor` file a component's
  generated code points its lines at. The inverse map finds those too.
- **Considered and not taken: Roslyn's own `SourceText` for the decoding.** The task would load a compiler assembly of
  its own version into MSBuild's process, beside the compiler's. The rules it needs are a few (§2.3), and facts 14 to
  16 hold them to the compiler's.

**R1a, reading the bytes as the compiler did (F12).** The bytes found are decoded and split as Roslyn does (§2.3): a
byte order mark's encoding (UTF-8, UTF-16 LE or BE); else the build's `$(CodePage)` when it sets one, for every file
without a mark, valid UTF-8 included, as the compiler does; else strict UTF-8, and on invalid bytes Windows-1252, else
Latin-1. Lines break at CR, LF, CRLF, U+0085, U+2028 and U+2029, so line N and its columns are the PDB's. The targets
pass `CodePage="$(CodePage)"` too. The ANSI code page of the build machine, which the compiler tries before
Windows-1252, is not followed: it is the machine's, and a build that depends on it is not reproducible anyway (§10).

**R1a, what does not change.** The woven calls keep passing `Document.Url`, the name the compiler wrote, as
`callerFilePath` (F6): the label text read from the file is the same on every machine, so a CI build stays
byte-identical across clones, and its binaries carry no local path. The PDB the weave writes back keeps its documents'
names. The label rules, `CutColumns`, the value capture and the pragma rules are untouched: they get the text they
were written for.

**R1a, the plumbing.**

- The targets pass `PathMap="$(PathMap)"` and `CodePage="$(CodePage)"` (targets L52 to L55). The weave target runs
  after `CoreCompile`, where the property holds the map the compiler used, up-to-date builds included (§2.3).
- `WeaveAssertionsTask` gains `public string? PathMap { get; set; }` and `public string? CodePage { get; set; }`, and
  reads `SourceRoots` (each item's spec and its `MappedPath`). It parses `PathMap` the compiler's way (§2.3; an entry
  the compiler would refuse, which cannot reach a successful compile, is skipped and noted) and gives the weaver the
  maps and the code page through a new constructor overload; the current constructor stays, so every existing fact's
  call compiles as it is.
- `SourceRoots`' doc comment becomes true; `PathMap` gets one; the class summary's "It reads the statements from the
  build's symbols" says where the text comes from.

**R1b, the off switch (F7).** `_GenerateAssertionTrackingAttributes` runs whatever `TrackAssertionsEnabled` says;
only `_WeaveAssertionTracking` keeps the condition. A project that declares the attribute then builds with the
switch off, unwoven, as the wiki says. The two generated types are `internal` and already in every woven build, so a
build with the switch on is byte-identical to today's.

**R1c, Shouldly's text without its source (F8).** Measured on Shouldly 4.3.0's 33 checks with and without the source
(`harness/shouldly/forms.txt`). `ErrorDiffParser` gains the form of `should be` (`ShouldBe`, `ShouldBeTrue`,
`ShouldBeFalse`): the actual value, `    should be`, the expected value, `    but was not`, then a `difference` table
or `Additional Info` as with the source. It is the only check the parser reads with the source, so it is the only one
it gains. `FailureText.ClusterKey` reads a message whose second line is a check and whose text ends `    but was not`
(before any `difference` table or `Additional Info`) by that check and its expected value, in place of the actual
value on its first line, so the failures of one assertion share a cluster whatever their actual values (Q6). Shouldly
writes that ending only without the source, for the nine checks whose text with the source ends `    but was` and the
actual value (`should be`, `be within … of`, `be greater than`, `be less than`, `be in range`, `be one of`,
`start with`, `match`, `be same as`). A check whose two forms end alike (`should not be`, `should contain key`,
`should throw`) keeps today's key: nothing in its text says which form it is. Its doc comment and `FirstLine`'s say
which form they read. The key is also the history ledger's error key (`HistoryRunBuilder.cs` L101,
`QueryCommand.History.cs` L579), so a failure of that form recorded before R1 and the same failure after it carry
different keys: history reads it as a new cause once, at the upgrade. Only runs that produce the form are touched; the
changelog says so.

**R1d, the type-to-document records (F13).** Before the write (`Weave`, L151 to L190), the weave reads every type's
custom debug information while the tokens are still the compiler's, and drops `TypeDefinitionDocuments` records, so
the woven PDB holds none rather than wrong ones. Rebuilding them right would mean keeping the documents Cecil drops
(a type with no method bodies often lives in a document no sequence point reaches) and renumbering the rest; dropping
them is the small, safe fix, and a type without methods is then found as in a build whose compiler wrote no such
record (Q8).

### 4.2 R2 (a minor): `KRONIKOL002`

When a woven statement's text could not be read (R1a found nothing), the task warns once per build:

> Kronikol.AssertionTracking: the source of N assertion statement(s) in M file(s) could not be read, so they are
> labelled by line number, show no values, and their pragma comments were not read. First: `<document name>`; tried
> `<candidate>`, `<candidate>`. Setting `<EmbedAllSources>true</EmbedAllSources>` puts the source in the symbols,
> where the weave finds it without the file.

Under the code `KRONIKOL002`, which `<NoWarn>` silences, as `KRONIKOL001` (`WeaveAssertionsTask.cs` L88 to L96). At
normal verbosity the task lists every such document with its candidates, as it lists unwoven statements (L76 to L84).
A minor, as `KRONIKOL001` was in 4.14.0, whose changelog counts the warning among what is new: a new diagnostic code
is surface a project can act on (`NoWarn`, `-warnaserror`). The statement is still woven, labelled by its line, as
today (Q2).

### 4.3 Not in this plan

- **Turning `DeterministicSourcePaths` off** from the package: it changes every consumer's PDB and Source Link to fix
  the weave's reading. Rejected.
- **Turning `EmbedAllSources` on** from the package: larger PDBs for every consumer, and no help to a build whose PDB
  is written by another tool. Rejected; R2's warning names it as the project's choice.
- **Writing the resolved local path into the woven calls**: F6. Rejected.
- **A label built from the IL** when no source exists anywhere: a feature, not this defect.
- **Matching `/_\d*/` by pattern** as Shouldly does: F4. Rejected for the compiler's own map.

## 5. Tests, red first

Every fact below is written first and run on the release R1 starts from (a worktree at its tag, the facts copied in,
a stub for a helper that does not exist yet; the weaver is the same from 4.18.1, which §2 measured, to 4.20.2), where
each must fail on its own assertion; the log keeps the red output. The guards are the exception: they pin what must
not change, hold before R1 by design, and are each proved by a mutation in §5.5 instead: facts 12 and 16 (M6, M17),
fact 13's label clause (M18), fact 9's clause for an unchanged file (M15), fact 24's last clause (M11), and the last
clauses of facts 25 and 26 (M21, M22).

### 5.1 The weave (`tests/Kronikol.Tests.AssertionTracking`)

`TestAssemblyBuilder` gains three options: a document name for the syntax tree that differs from where the source
file is written (the name a mapped build writes, `/_/tests/X.cs`, with the file at `<temp>/tests/X.cs`), the tree
built from the file's bytes so the PDB's checksum is the file's, and embedded text (`EmbeddedText.FromSource`). New
facts, in a new `DeterministicSourcePathsTests.cs`:

1. A `/_/` document with `PathMap` `<temp>/=/_/`: the probe's #163 statements are labelled as in a local build, with
   their values (the Shouldly and FluentAssertions cases of row a, Debug and Release).
2. The same with the inline pragma, the block pragma, and a statement after `enable`: 9 woven of 12 (F3).
3. The map in `@(SourceRoot)` only (`MappedPath`), no `PathMap`: labelled.
4. A project's `PathMap` to `/src/` (row g's shape): labelled.
5. Two entries mapping two directories to one value, a file of the name in each, different content: the label is
   the compiled file's. Then the overlapping form (`/src/` and `/src/sub/`): the same. Red with the checksum dropped.
6. A document with no checksum (a `#line` target) and two candidates: the longer matched value's file.
7. `PathMap` parsing, against the compiler's own parser on the same strings (`CSharpCommandLineParser` with
   `/pathmap:`, so the two cannot drift): a directory whose name holds `,` and `=`, an empty trailing entry, keys with
   and without a trailing separator, `/` and `\`. Then a value written with `\` for a key written with `/`, the file
   two directories down: labelled (red on Linux, where CI runs this suite, with the rest's separators left as the
   name has them).
8. Embedded source, no file anywhere: labelled, values captured, pragmas read.
9. A mapped document with embedded source whose file was rewritten after the compile (it fails the checksum): the
   embedded text wins. Embedded source and the file unchanged: the file is read, and the woven PDB's embedded blob is
   the compiler's, byte for byte (Cecil was never made to inflate it).
10. A nested root's document (`/_/sub/X.cs`) with only the top-level entry: labelled.
11. `/_/` and `/_1/` documents in one assembly: each read from its own root.
12. **The determinism invariant:** in the woven IL every `callerFilePath` literal is the PDB's `/_/…` name, never the
    temp path, and the woven PDB's document names are unchanged.
13. Nothing readable (no map entry matches, no file, no embedded text): the label falls back as today and the
    document is in `WeaveResult` with the candidates tried (R2's input).
14. A file holding U+2028 in a comment above two assertions (F12), no map: each is labelled with its own statement.
    Red before R1 (the neighbour's text). The same for U+0085 and U+2029.
15. A BOM-less Windows-1252 file whose assertion compares with `"café"`: the label holds `café`. With `CodePage` set,
    that code page decodes every file without a byte order mark, a valid UTF-8 one included, as the compiler does: a
    UTF-8 `"café"` under `CodePage` 1252 is labelled `cafÃ©`, the text the PDB's columns count. Red before R1 (U+FFFD).
16. A UTF-16 file with a byte order mark, and a UTF-8 one with and without: labelled as today.
17. **The PDB's type records (F13):** an assembly with a type that has no methods, in a file of its own, and another
    in a file with woven statements: after the weave no `TypeDefinitionDocuments` row names another type's document
    (read back with `System.Reflection.Metadata`). Red before R1.
18. `WeaveAssertionsTaskTests`: `RunTask` with `PathMap`, `CodePage` and `SourceRoots` items carrying `MappedPath`
    metadata: the statements are labelled, and each mapped document is listed at low importance with the file it was
    read from.

### 5.2 Through the targets (`tests/Kronikol.Tests.AssertionTracking`, a new `TargetsBuildTests.cs`)

None exists (F10). A fixture project, built by `dotnet build` in a temp directory with a sandboxed
`Directory.Build.props` (the `BuildWithSdk` pattern), imports the weaver's real `build/` props and targets with the
task directory pointed at the test's own build of `Kronikol.AssertionTracking.dll` (`-p:_AssertionTrackingTaskDir=…`,
a global property, which wins over the props' own value at L7), references the core library and Shouldly, and declares
a `SourceRoot` for its directory (so the facts need no git; row h shows the SDK gives none without one).

19. `-p:ContinuousIntegrationBuild=true`: labels and values as a local build of the same fixture, and pragmas
    honoured, with a portable PDB and with `-p:DebugType=embedded` (DotNet.ReproducibleBuilds' default). Red
    before R1 ("Assertion at line N").
20. `-p:PathMap=<fixture dir>=/src/`: the same.
21. `-p:ContinuousIntegrationBuild=true -p:EmbedAllSources=true`: the same.
22. `-p:TrackAssertionsEnabled=false` with `[assembly: TrackAssertions]` declared: builds, and nothing is woven. Red
    before R1 (CS0234; F7).
23. **Determinism:** the fixture copied to two directories at different depths, each built with
    `ContinuousIntegrationBuild`: woven DLL and PDB byte-identical, and the labels are the statements' (before R1 the
    bytes are identical and the labels fall back, so the fact fails on its label clause).
24. (R2) A document no candidate reaches (the fixture deletes a file between `CoreCompile` and the weave, in a
    target of its own): one `KRONIKOL002`, naming the document and the paths tried; `<NoWarn>KRONIKOL002</NoWarn>`
    silences it; a build where every document is read has none.

The class runs `dotnet build` like `CrossSdkWeaverTests` and takes their trait and time limits.

### 5.3 The report (`tests/Kronikol.Tests`)

25. `ErrorDiffParserTests`' measured theory (L176 to L186) gains Shouldly 4.3.0's `should be` texts without the
    source, taken from `harness/shouldly/forms.txt`: an integer (`4` / `    should be` / `5` / `    but was not` reads
    expected `5`, actual `4`), a string with its `difference` table, a custom message's `Additional Info`, a
    collection, `null` expected, and `ShouldBeTrue`. A check the parser does not read with the source (`should be
    greater than`) is not read without it either.
26. `FailureClusterKeyTests`: two failures of one Shouldly assertion without the source, with different actual values,
    share a key, for `should be` and for `should be greater than`; two with different expected values do not; `should
    not be` without the source keeps today's key (its first line).
27. A `Failures.md` fact through the digest: the Expected/Actual table is there for that form.

### 5.4 Acceptance

- **The probe** (`harness/probe/`) on locally packed packages: rows a to k again. Every row labels as row a does,
  with row a's values; 9 woven in every row (10 with j's and k's extra test); rows d and e included.
- **Determinism** with `harness/scripts/determinism.sh`: two clones, identical woven bytes, real labels.
- **BreakfastProvider** at its current pin, moved to the local packages: its seven tracked test projects built with
  and without `-p:ContinuousIntegrationBuild=true`, the notes compared label by label (none "Assertion at line N",
  the same labels both ways). The local checkout is behind its origin; use a fresh worktree.
- **The owner's suite** (#132's multi-host suite, 747 notes): the owner's, with `harness/scripts/summarize.py`.

### 5.5 Mutations

Each must turn the facts named red: M1 ignore `PathMap` (4, 20); M2 ignore `SourceRoots` (3); M3 skip embedded
source (8); M4 drop the checksum (5); M5 read `,,` as a separator (7); M6 pass the resolved local path as
`callerFilePath` (12, 23); M7 `IsPragmaDisabled` reads the name as written (2); M8 restore the targets' condition on
the attributes (22); M9 drop the new `ErrorDiffParser` pattern (25, 27); M10 key the sourceless form on its first
line (26); M11 (R2) warn when every document was read (24); M12 break lines at CR and LF only (14); M13 decode a
non-UTF-8 file as UTF-8, ignore `CodePage`, or try UTF-8 before it (15); M14 keep the `TypeDefinitionDocuments`
records (17); M15 read embedded source before the files (9, its byte-identical blob); M16 take a mapped candidate
that fails the checksum (9); M17 ignore a byte order mark (16); M18 answer an empty text when nothing is readable
(13); M19 take the shorter matched value first (6); M20 leave the rest's separators as the name has them (7, on
Linux); M21 read every check that ends `    but was not` in the parser (25); M22 key `should not be` by its check
and value (26).

## 6. Releases and records

### 6.1 Releases

- **R1, a patch**: R1a to R1d. Bug fixes, nothing new for a consumer to call: the task's new parameters are set by
  the package's own targets, and the task assembly, with the weaver's new constructor, ships in the package's `tasks/`
  folder (`BuildOutputTargetFolder`, `Kronikol.AssertionTracking.csproj` L12), which no consumer compiles against.
  Behaviour changes, said in the changelog: a build with deterministic source paths draws labels, values and pragmas
  as a local build does; a statement after U+0085, U+2028 or U+2029, in a BOM-less non-UTF-8 file or in a build that
  sets `CodePage`, gets its own text; the off switch builds; Shouldly's text without its source gets its values in the
  report and its own cluster; the woven PDB holds no misplaced type records.
- **R2, a minor**: `KRONIKOL002`. It can follow R1 at once, or ride with it as one minor (Q1).

Both bump every package (CLAUDE.md), and each moves the templates' package pins and the history action's `VERSION`
as every release does. The labels change only where they were wrong. R1c changes report output (the expected and
actual values and the cluster of a Shouldly failure written without its source), which Kronikol4J renders too (§6.5).

### 6.2 Changelog draft (R1)

> **Assertion tracking in CI builds.** In a build with deterministic source paths (`ContinuousIntegrationBuild=true`
> turns them on, as does `DeterministicSourcePaths`), every woven assertion was labelled "Assertion at line N",
> carried no values, and was woven even when a pragma comment disabled it, because the weave read each statement from
> the path the PDB names (`/_/…`), which is not on disk (#165). The weave now reads the file the compiler read, found
> through the build's own `PathMap` and source roots and checked against the checksum in the PDB, or else the source
> the PDB embeds. The woven calls still carry the PDB's names, so a deterministic build's output does not depend on
> the clone's path. In every build, the weave now decodes a file and counts its lines as the compiler does: a
> statement after a U+0085, U+2028 or U+2029 character was labelled with another line's text, a BOM-less Windows-1252
> file's accented letters read as U+FFFD, and a build that sets `CodePage` had its files read as UTF-8.
> `TrackAssertionsEnabled=false` no longer breaks the build of a project that declares `[assembly: TrackAssertions]`.
> A Shouldly `ShouldBe` failure written without its source (`4 should be 5 but was not`, as Shouldly 4.3.0 writes when
> a CI build's tests run from the DLL) now shows its expected and actual values in Failures.md and the report, and a
> failure of any of the nine Shouldly checks written that way clusters with the other failures of its assertion; since
> the cluster is also history's error key, history reads such a failure as a new cause once, in the first run after
> the upgrade. The woven PDB no longer points types without methods at other types' files. A patch: bug fixes, nothing
> new for a consumer to call.

### 6.3 Wiki (a worktree of the wiki of its own; read at 85abe19, the page unchanged at 2a193d8)

- `Assertion-Tracking.md` L72 (the label is read from the source file through the PDB's sequence points): say where,
  including embedded source and a mapped build.
- L136 to L155 (pragmas): they are read in a CI build too, from R1.
- L306 to L315 ("Disabling the Weaver"): works from R1; earlier versions fail CS0234 when the attribute is declared.
- L321 to L338 ("When Nothing Is Drawn"): `KRONIKOL002` beside `KRONIKOL001` (R2), and a line for a CI build on an
  older version (`DeterministicSourcePaths=false` as the workaround, with its cost).
- L339 to L345 ("Limitations"): what the weave needs to read a statement (the file the compiler read, or the
  source the PDB embeds).
- Grep the wiki for `DebugType`, `PDB` and `symbols` for any other page that says how the label is found.

### 6.4 Doc comments (they ship in the package)

`WeaveAssertionsTask`'s summary and `SourceRoots`; the new `PathMap`; `AssertionWeaver.Weave`'s `pdbPath`;
`FailureText.ClusterKey` and `FirstLine`; `ErrorDiffParser`'s summary.

### 6.5 Plans, issue and Kronikol4J

- `PLANS_STATUS.md`: a row (added with this plan), kept current.
- Close #165 with a comment naming the release(s) (a commit saying "#165" closes nothing).
- Kronikol4J: its assertion agent (`kronikol4j-assertj-agent`) reads no source file (READ), so R1a, R1b, R1d and R2
  have nothing to port. R1c does: Kronikol4J renders the same expected and actual values and clusters through its own
  `ErrorDiffParser.java` and `FailureClusterer` (READ), so the new Shouldly form and its key are ported there, or the
  divergence ledger records them, in the release's ledger line.
- Leftovers go to `ROADMAP.md` Appendix C.

## 7. Where it sits in the roadmap

Placement is the owner's (Q7). It is a defect in a shipped feature with a one-line workaround that costs the CI
build its deterministic paths, and it voids #163's work in CI reports, so stage 1 beside #163's row 1.27 fits;
no row is taken until the owner green-lights the plan ("taking row 1.N now" to the other sessions first).

## 8. Found on the way, not in this plan

- **FluentAssertions' subject name in a CI build** (`Expected string to be` for `Expected id to be`): FluentAssertions
  6.12.1 reads the caller's source at run time and does not undo the map (RUN). Upstream; whether 7.x, 8.x and
  AwesomeAssertions do was not measured. Kronikol's label names the subject from R1 on.
- **Ingest's sentence about a `/_/` source path** ("a build machine's paths", `IngestPipeline.cs` L856 to L860): a
  `/_/` path is the opposite. No producer is known to write one into a feed; recorded for the first that does.
- **The weave's rewritten PDB keeps only documents with sequence points** (row b: 12 documents become 5, with their
  embedded source; AssemblyInfo, GlobalUsings and the generated attributes go) and its CodeView entry names the bare
  `IssueStack.pdb`. No method is lost, so no debugger or Source Link check was seen to notice; measured, not fixed.
- **The woven PDB keeps the compiler's records of the compilation** (its options and references): they describe a
  compilation that produces the unwoven assembly. Inherent to rewriting IL after the compiler; a checker that rebuilds
  from them gets the unwoven binary. Recorded, not a defect to fix.
- **AssertMessage.Fody**, the nearest weaver to this one, reads `File.ReadAllLines(sequencePoint.Document.Url)` and has
  the same defect (READ). Another project's; not reported unasked.
- **The report reads only `should be` among Shouldly's checks**, with its source or without: `should be greater than`,
  `should contain` and the rest get no Expected/Actual table (`harness/shouldly/forms.txt`). Their texts name a
  bound or a member, not an expected value; recorded, not changed.
- **`WovenIlVerifiesTests`** finds its own folder through `[CallerFilePath]` (L21, L106), so it would fail in a build
  of the test project with deterministic paths. Nothing builds it so; noted for whoever adds one.

## 9. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | R2's warning: a minor of its own after R1, or one minor with R1? | **Its own minor, after R1**: R1 restores CI reports and can ship at once; R2 adds surface a project may need to answer (`NoWarn`, `-warnaserror`) |
| Q2 | When no source can be read, weave the statement labelled by line (pragmas unread, R2 warns), or leave it unwoven? | **Weave it, as today**: unwoven, the note disappears; the warning says the pragmas were not read |
| Q3 | Embedded source before the checked file, or after? | **After**: a file with the PDB's checksum is the compiled text byte for byte, and reading embedded source makes Cecil deflate it again, changing the woven PDB of every build that embeds (§2.3); embedded source still covers what no file holds |
| Q4 | R1b (the off switch, F7) in R1, or a patch of its own? | **In R1**: same package, same targets file, a measured break of a documented setting |
| Q5 | R1c (Shouldly's text without its source, F8) in R1, or an issue of its own? | **In R1**: found measuring this issue's CI build, and CLAUDE.md asks for it |
| Q6 | R1c's cluster key for that form: the check and the expected value, or the first line as today? | **The check and the expected value**: the failures of one assertion share it, as `ClusterKey`'s doc comment promises; two assertions expecting one value on one check would share it too, which is rarer than one assertion failing with two values. The cost: the key is history's error key too, so each such failure reads as a new cause once, at the upgrade (§4.1). Leaving the key alone keeps history continuous and fixes only the expected and actual values |
| Q7 | Roadmap placement | The owner's; stage 1 after row 1.27 fits (§7) |
| Q8 | R1d (the misplaced type records, F13): drop them or rebuild them, and in R1 or later? | **Drop them, in R1**: wrong records are worse than none, and rebuilding needs the documents Cecil drops; keeping those documents is a larger change to the weave's PDB writing, for a later issue if anyone needs the records |
| Q9 | F12 (decoding and line breaks as the compiler does) changes labels in local builds too: in R1? | **In R1**: the labels it changes were wrong (another line's text, or U+FFFD), and `SourceDocuments` is the one place that reads the bytes |

## 10. Assumption ledger

| Assumption | Status |
|---|---|
| `$(PathMap)` after `CoreCompile` is the map the compiler applied | RUN (rows b and g, printed after `CoreCompile`; the mechanics build on a full and an up-to-date build, `harness/mechanics/build-ci.txt`); READ (§2.3: the same property reaches `Csc`) |
| The PDB's checksum is over the bytes the compiler read, so a file on disk matches it | READ (§2.3); RUN (every on-disk document of the mechanics build matched, the Windows-1252 file included, `pdbdump.txt`); fact 5 holds it |
| Embedded source survives the weave's own PDB rewrite for documents with sequence points | RUN (row d) |
| Cecil copies an embedded source it was not made to inflate unchanged | READ (`AssemblyWriter.cs` L2467 to L2494); RUN (re-deflated only after `Content` was read, `pdbdump.txt`); fact 9 holds it |
| Following `$(CodePage)` but not the build machine's ANSI code page decodes every file the compiler decoded | ESTIMATED: they differ only for a BOM-less file that is not UTF-8, in a build with no `CodePage`, on a machine whose ANSI code page is not Windows-1252; such a build reads differently on each machine already |
| Reading the text from a mapped path keeps the woven bytes identical across clones | INFERRED from F6 (the text is the commit's, the literal is the PDB's); fact 23 and §5.4 measure it |
| Shouldly writes `    but was not` only when it has no source, in the versions projects use | RUN for 4.3.0's 33 checks (`harness/shouldly/forms.txt`); ESTIMATED for others: the 5.0 previews were not run; fact 25 pins 4.3.0's texts |
| No consumer sets the task's parameters by hand | ESTIMATED: the task is loaded from the package's `tasks/` folder by its own targets; a project that calls it directly would see a new optional parameter only |
| The weave never runs where the compiler did not (another machine, a moved tree) | READ: the target runs after `CoreCompile` in the same build; embedded source (R1a's second source) covers a tree moved in between |

## 11. Log

- **2026-10-13** (kronikol-1b): written at the owner's ask to analyse #165 critically and plan a fix for the
  underlying problem, in a worktree of its own (`plan/165-weaver-deterministic-paths`). Measured on the published
  4.18.1 packages (§2.2, `harness/s0/`), with a second scratch build for the compiler's side (`harness/mechanics/`);
  three research passes read the code paths (§2.1, §2.4), the SDK, Roslyn and Cecil (§2.3) and the prior art. One
  guess was measured wrong on the way: Shouldly does not fall back to "The provided expression" under deterministic
  paths; it undoes `/_/` itself (§2.4). Placed in no roadmap row (§7): the sessions writing the #168 to #172 plans
  took none either. Nothing in `src/`, the tests or the wiki changed.
- **2026-10-13** (kronikol-1b), a quality sweep at the owner's ask, against `origin/main` c28e8948 (4.19.2; no cited
  file changed, nor by 4.19.3 and 4.19.4, which touched only the changelog among them). Every citation was read again.
  Corrected: a build that sets `CodePage` has the compiler decode every file without a byte order mark in that code
  page, valid UTF-8 included, and UTF-8 is tried only when none is set (§2.3, R1a, fact 15, M13); the inverse map
  turns the rest's separators back to the key's kind, which matters on Linux (R1a, fact 7, M20); F13's counts, from a
  saved re-run (one record became two, not two four). Measured and saved: the mechanics build's outputs
  (`harness/mechanics/*.txt`), F7's build, `Directory.Build.targets` against `.props` for
  `ContinuousIntegrationBuild`, and Shouldly 4.3.0's 33 checks with and without their source (`harness/shouldly/`),
  which scopes R1c: the parser gains `should be`, the only check it reads with the source, and the key covers the nine
  checks whose sourceless text ends `but was not`. Shouldly 4.3.0 hands its map to the test host through an
  environment variable set in MSBuild's process, so a DLL run directly gets none (§2.4). Added: the facts each
  mutation turns red, M19 to M22 and the guards' list; why R1 is a patch though the task gains parameters (the task
  assembly ships in `tasks/`); how the targets fixture points at the test's build of the task; two designs considered
  and not taken.

## Appendix A. Edit sites at 34145c44

| File | Lines | Change |
|---|---|---|
| `src/Kronikol.AssertionTracking/build/Kronikol.AssertionTracking.targets` | L4 to L6, L52 to L55 | R1b: no condition on the attributes; R1a: `PathMap="$(PathMap)"`, `CodePage="$(CodePage)"` |
| `src/Kronikol.AssertionTracking/WeaveAssertionsTask.cs` | L10 to L26, L47 to L111 | R1a: `PathMap`, `CodePage`, read `SourceRoots`, parse; R2: `KRONIKOL002` beside `KRONIKOL001` (L88 to L96) |
| `src/Kronikol.AssertionTracking/SourceDocuments.cs` | new | R1a: candidates, checksum, embedded source, decoding, line breaks |
| `src/Kronikol.AssertionTracking/AssertionWeaver.cs` | L23, L25 to L29, L151 to L190, L868 to L878, L977 to L995, L1066 to L1077, L2718 to L2720, `WeaveResult` L3423 | R1a: the overload, both readers through `SourceDocuments` (`CutColumns` gets the compiler's lines), the unused `fileName` removed, unreadable documents recorded; R1d: the type records dropped before the write |
| `src/Kronikol/Reports/ErrorDiffParser.cs` | L24 to L42 | R1c |
| `src/Kronikol/Reports/FailureText.cs` | L40 to L78 | R1c |
| `tests/Kronikol.Tests.AssertionTracking/TestAssemblyBuilder.cs` | L49 to L122 | a document name apart from the file; the tree from the file's bytes; embedded text |
| `tests/Kronikol.Tests.AssertionTracking/DeterministicSourcePathsTests.cs`, `TargetsBuildTests.cs` | new | §5.1, §5.2 |
| `tests/Kronikol.Tests.AssertionTracking/WeaveAssertionsTaskTests.cs` | L66 to L84 | fact 18 |
| `tests/Kronikol.Tests/Reports/ErrorDiffParserTests.cs`, `FailureClusterKeyTests.cs` | L176 to L186; new facts | §5.3 |
| `tests/Kronikol.Tests/Reports/FailuresDigestGeneratorTests.cs` | new fact | fact 27 |
| Kronikol4J `ErrorDiffParser.java`, `FailureClusterer` | | R1c: the port, or a divergence ledger line (§6.5) |
| `CHANGELOG.md`, every package's version | | §6.1, §6.2 |
| Wiki `Assertion-Tracking.md` | L72, L136 to L155, L306 to L345 | §6.3 |
