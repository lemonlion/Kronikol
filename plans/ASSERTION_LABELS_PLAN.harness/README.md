# Harness for `ASSERTION_LABELS_PLAN.md` (#210)

Measured on 2026-10-27 at Kronikol 5.12.0 (`1ee6373a`), and again on 5.14.0 (`8d7d1e1d`), which changed none of the
files the plan edits, with the published packages `Kronikol` and `Kronikol.AssertionTracking` 5.12.0, Shouldly 4.3.0
and FluentAssertions 6.12.1, on net10.0, built by SDK 10.0.300 on Windows 11, in Debug and in Release.

| Path | What it is |
|---|---|
| `probe/` | The woven probe, after the #163 plan's: a console app with no test framework. `Track.TestIdResolver` names a test per case, and each case's notes are read back from `RequestResponseLogger`. `Woven210.cs` holds 19 Shouldly statements (the issue's two, the constants beside `nameof`, the subject shapes), `Fluent210.cs` three in FluentAssertions, `That210.cs` three through `Track.That`, and `Audit210.cs` nine the plan's audit asked for (a framework enum, a `const decimal`, a nullable enum, an enum-typed `const`, two chains, a `Track.ThatAsync` block body). `-p:KronikolVersion=<v>` picks the packages (default 5.12.0) |
| `s0/` | 5.12.0, before `Audit210.cs`: `run-5.12.0-debug.txt` and `-release.txt` (identical), the weave's line, the woven `Probe.dll` sizes, and the weaver suite's result on the unchanged tree (`weaver-tests-5.12.0.txt`) |
| `s1/` | The first prototype: the probe on local packages `5.12.1-local.210` (`run-local-*.txt`, identical; `build-local-*.txt`; `woven-size-local.txt`), the Tracking facts (`tracking-tests-prototype.txt`: 997 of 999), the weaver suite (`weaver-tests-prototype.txt`), and the whole core and weaver suites on 5.14.0 (`suites-5.14.0-prototype.txt`: the core suite fails only the two `nameof` pins) |
| `s2/` | The final prototype (`prototype.patch`): the probe with `Audit210.cs` on published 5.12.0 (`run-5.12.0-*.txt`) and on local packages `5.14.1-local.210b` (`run-local-*.txt`), Debug and Release identical in each, `woven-size.txt`, and both suites on 5.14.0 (`suites-5.14.0-prototype.txt`) |
| `formatter/probe.cs` | Runs a file of statements through the worktree's `AssertionExpressionFormatter.Format`, with no values: `dotnet run --file probe.cs -- corpus.txt`. A `\n` in a line is a line break |
| `formatter/published.cs` | The same over the published `Kronikol` 5.12.0 package |
| `formatter/*.txt` | Shape files, each with a `-5.12.0.txt` and a `-prototype.txt` output (`corpus.txt`'s are `probe-5.12.0.txt` and `probe-prototype.txt`): `corpus.txt` (42 shapes), `multiline.txt` (five over lines), `f6.txt` (FluentAssertions' split), `chains.txt` (F9, F10), `edges.txt` (F11 to F13), `audit.txt` (the audit's regressions), `lambdas.txt` (F16), `c6.txt` (F14, F15, F17: #212's S18a and S23) |
| `corpus/extract.py` | Collects a source tree's one-line Shouldly, FluentAssertions and TUnit statements (a trailing comment is not part of one) |
| `corpus/extract_multiline.py`, `join_lines.py` | The statements spread over lines, as `Track.That` passes them and (`join_lines.py`) as the weave joins them |
| `corpus/classify.py` | Counts the defect's shapes in a probe output, and looks each `A.B` argument up in the source (enum member, `const` with its value, `static`, or a type outside the tree) |
| `corpus/bracket_space.py` | Counts labels with a space inside a bracket or a line break, outside literals and the lambda brackets |
| `corpus/fa_chains.py` | Counts FluentAssertions chains past the assertion's first call |
| `corpus/breakfastprovider*`, `kronikol-examples*` | BreakfastProvider at `0d29537` (1,016 one-line statements, 49 multi-line) and the repo's `examples/` at `1ee6373a` (53): the statements, their labels on 5.12.0 and on the prototype, and the counts (`*-classified.txt`, `--list` in `*-classified-list.txt`) |
| `corpus/shouldly_corpora.sh`, `shouldly-counts.txt` | AutoMapper, MediatR and Marten, fetched at the commits measured into a directory outside the repository, labelled and counted. Only the counts are kept: AutoMapper and MediatR are under the Lucky Penny Software licence |
| `probe_local.sh` | Packs a worktree's `Kronikol` and `Kronikol.AssertionTracking` under a local version label into an isolated feed and cache, and runs the probe on them, Debug and Release; a fourth argument names the probe to run, for a worktree that holds the prototype but not the harness |
| `prototype.patch` | R1 and R2 as prototyped, against `8d7d1e1d` (`git apply --check` passes). Not a release: no tests, no docs |

## Traps met here

- Run every Python script with `PYTHONUTF8=1` (or `python -I -X utf8`) on Windows. `extract.py` once wrote its
  output in the console's code page, so two statement files were not UTF-8 and the formatter saw replacement
  characters; the files were converted and every output taken again.
- `dotnet run --file` on a cold build writes the compiler's warnings to standard output: filter them
  (`grep -v 'warning CS'`) and the carriage returns (`tr -d '\r'`) before comparing outputs.
- A line that ends in a comment does not end in `;`: the extractors cut a trailing `//` comment first. Before that,
  such one-line statements were skipped and the multi-line extractor glued the lines after them on.

## Re-running

Build the probe in a copy outside the repository, so no `bin/` or `obj/` lands under `plans/` and the repository's
`Directory.Build.props` does not apply:

```sh
cp -r probe "$TMP/probe" && cd "$TMP/probe"
dotnet build -v:n | grep Instrumented
dotnet run --no-build > run-debug.txt
dotnet build -c Release -v:n | grep Instrumented
dotnet run -c Release --no-build > run-release.txt
diff run-debug.txt run-release.txt                       # no output
```

The prototype on local packages, from a worktree at `8d7d1e1d` with `prototype.patch` applied:

```sh
sh probe_local.sh <worktree> 5.14.1-local.210b <out-dir> "$PWD/probe"
```

The formatter over a corpus, on 5.12.0 and on the prototype:

```sh
cd formatter
dotnet run --file published.cs -- ../corpus/breakfastprovider.txt | grep -v 'warning CS' | tr -d '\r' > ../corpus/breakfastprovider-5.12.0.txt
git -C ../../.. apply plans/ASSERTION_LABELS_PLAN.harness/prototype.patch
dotnet run --file probe.cs -- ../corpus/breakfastprovider.txt | grep -v 'warning CS' | tr -d '\r' > ../corpus/breakfastprovider-prototype.txt
git -C ../../.. checkout -- src
cd ../corpus && python -I -X utf8 classify.py breakfastprovider-5.12.0.txt <BreakfastProvider at 0d29537>
```

The Shouldly corpora:

```sh
sh corpus/shouldly_corpora.sh "$TMP/shouldly" <worktree with prototype.patch and this harness>
```
