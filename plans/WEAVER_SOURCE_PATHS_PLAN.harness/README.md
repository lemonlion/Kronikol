# Harness for WEAVER_SOURCE_PATHS_PLAN.md (#165)

Measured on 2026-10-13 with the published 4.18.1 packages (Kronikol.xUnit2 and Kronikol.AssertionTracking from
nuget.org), SDK 10.0.300 (MSBuild 18.6.3, Source Link 10.0.14), net8.0, xUnit 2.9.3, Shouldly 4.3.0 and
FluentAssertions 6.12.1, on Windows 11. `<S>` stands for the scratch directory the runs were made in; the NuGet cache
root is left as it was printed, since which root takes `/_/` is part of what was measured.

## probe/

The issue's project: the #163 scaffold (`ASSERTION_VALUE_SUBSTITUTION_PLAN.harness/r1/`) on 4.18.1, in a git
repository whose root is the probe's parent (`<S>/repo`, origin set to a placeholder GitHub URL so Source Link writes a
map). `Scenarios/IssueFeature.cs` adds, to the #163 statements at L31 to L37:

- L38: a statement disabled by a trailing `// pragma:TrackAssertions:disable`;
- L39 to L42: a disable/enable block around a Shouldly statement (L40) and a FluentAssertions one (L41);
- L43: a tracked statement after the block.

So a weave that honours the pragmas instruments 9 statements, and one that ignores them 12.

Switches in `IssueStack.csproj`: `-p:IncludeLinked=true` compiles a file from outside the repository (row j);
`-p:IncludeFailing=true` compiles `Failing/FailingFeature.cs`, three tests that fail on purpose (two Shouldly
subjects and one FluentAssertions string). Two diagnostic targets run after `CoreCompile`: `_DumpPathMap` prints
`$(PathMap)`, `$(DeterministicSourcePaths)` and every `@(SourceRoot)` with its `MappedPath`, `NestedRoot` and
`ContainingRoot`, as the weave's target sees them; `_SnapshotPreWeave` copies the compiler's DLL and PDB to
`obj/<cfg>/<tfm>/preweave/` before the weave, the baseline for the determinism check.

## scripts/

- `run_row.sh <row> <project-dir> <Debug|Release> [dotnet test args]`: a clean build and run (`rm -rf bin obj`,
  `dotnet test -v:n`), then the weave's lines, `kronikol query assertions`, a count of the diagram notes
  (`query grep "✓" --in notes`, and "✗" and "Assertion at line") and the PDB's documents before and after the weave.
  `query assertions` lists only assertions inside a step, so `SampleFeature`'s note (outside one) is counted from the
  notes. Point `R=` at the directory holding `tool/` (a local `Kronikol.Tool` 4.18.1) and `summarize.py`.
- `summarize.py`: counts a row's notes, its fallback labels and its labels with a quoted value.
- `dumpdocs.cs` (`dotnet run --file dumpdocs.cs <pdb-or-dll>`): each PDB document's name, whether the PDB embeds its
  source, and the Source Link map; reads embedded PDBs too.
- `determinism.sh` and `bytesearch.py`: the two-clone build and the search of the woven DLL for local paths and
  `/_/` literals.

## s0/ (4.18.1, before any fix)

Per row: `<row>-summary.txt` (command, weave lines, `PathMap` and `SourceRoot`, counts), `<row>-notes.txt`,
`<row>-assertions.txt`, `<row>-pdb.txt`. Rows: a default; b `ContinuousIntegrationBuild=true`; c b plus
`DeterministicSourcePaths=false`; d b plus `EmbedAllSources=true`; e b plus `DebugType=embedded`; f
`DeterministicSourcePaths=true` alone; g a project-written `PathMap` (`<project dir>\=/src/`) with no CI flag; h b in a
directory no git repository holds; i b in Release; j b plus a linked file from outside the repository; k b in a
repository with a git submodule holding a test. `a-fail` and `b-fail` add the failing tests and keep `Failures.md` and
`query failures`; `b-fail-nobuild-*` ran `dotnet test --no-build` and `b-fail-dll-*` ran the built DLL
(`dotnet test bin/Debug/net8.0/IssueStack.dll`). `determinism.txt` and `determinism-bytesearch.txt` hold the
two-clone hashes and the byte search. `k-sourcelink-warnings.log` is Source Link's warning for a submodule whose URL is
a local path, shown only with its NoWarn lifted.

The verbose build logs (`*-test.log`, up to 14 MB) were left out; every count and line quoted in the plan is in the
files above.

## mechanics/

The second scratch build the plan's §2.3 rests on (SDK 10.0.300, `ContinuousIntegrationBuild=true`): `repo/App` holds
a tracked file (`Program.cs`), a type with no methods (`IOnly.cs`), a `#line` directive (`LineDir.cs`), a BOM-less
Windows-1252 file (`Ansi.cs`, kept in that encoding) and a file holding U+2028 (`Lsep.cs`); `repo/inner` is the
submodule's file. The project also used the regex source generator and a linked file from outside the repository.
`Probe.targets` prints `PathMap` and the roots after `CoreCompile`; `pdbdump.cs` lists each PDB document with its
checksum and embedded source; `tdd.cs` reads the `TypeDefinitionDocuments` records before and after a Cecil round
trip written the weave's way (F13). The two scripts run as `dotnet run --file <name>.cs <dll>`; `pdbdump.cs` takes
the build's `PathMap` as its second argument, and `tdd.cs` takes `touch` (read every type's records before the write)
or `drop` (then remove the `TypeDefinitionDocuments` records, R1d's fix). `outside/Linked.cs` is the linked file.

Saved output, re-run on 2026-10-13 (`<S>` is this build's scratch directory): `build-ci.txt` (the probe target's
lines for a clean build with `ContinuousIntegrationBuild=true` and for the same build again, `CoreCompile` skipped as
up to date), `pdbdump.txt` (each document mapped back and hashed, the Cecil round trips, the re-deflated embedded
source and two writes compared), `tdd.txt`, `tdd-touch.txt` and `tdd-drop.txt` (F12's lines as `File.ReadAllLines`
reads them, and the type records before and after the write in each mode). `s0/f7-off-switch.txt` is F7's build.

## shouldly/

`ShouldlyForms.csproj` and `Program.cs` (Shouldly 4.3.0 and Kronikol 4.18.1 from nuget.org, net8.0) fail 33 Shouldly
checks, first with the caller's source and then under `ShouldlyConfiguration.DisableSourceInErrors()`, the text Shouldly
writes when it cannot read the file, and print each message JSON-escaped with what `ErrorDiffParser` reads from it and
the key `FailureText.ClusterKey` gives it (copied into `Program.cs`, being internal). `forms.txt` is the output of
`dotnet run` on 2026-10-13. It is what scopes the plan's R1c.
