# Harness for CALLS_UNDER_THEIR_STEP_PLAN.md

Everything the plan marks RUN, so it can be run again. Nothing here is built by the solution: the probes are consumers, and
they restore from NuGet, from a local feed, or from a source copy.

## What is here

| Path | What it is |
|---|---|
| `repro/` | #222's suite, which #225 was found in (`scripts/extract_222.py` writes it from the issue), plus `Features/Probes.feature`, `Features/Shapes.feature`, `Steps/ProbeSteps.cs` and a `PROBE_NO_BARS` switch in `Hooks/Hooks.cs` |
| `repro-lightbdd/` | Composite steps on LightBDD: nested, and failing |
| `review-probes/` | The independent review's probes: Reqnroll (`rq-src`), LightBDD (`lb-src`), BDDfy (`bddfy-src`), a console harness of awkward shapes (`plain-src`), and a flow run through each framework's setup and test (`fw-gen.py`, `fw-src`). `gen-projects.py` writes their project files, which this harness left out at first, recovered from the reviewer's transcript |
| `review-probes/sibling-223/` | A copy of the probes of #223's second review, as they sit in another uncommitted worktree: plain xUnit v3 (`xu`), Reqnroll (`rq`), LightBDD (`lb`) |
| `perf/` | What logging one record costs, on either version |
| `review-probes-third/` | The third review's probes and dumps, the audit's. For each of `flows`, `hosts`, `reqnroll`, `lightbdd`, `bddfy`: `src/`, the two project files, `control.txt` (published), `change-225r.txt` (the cut reviewed), `change-225y.txt` (the audit's fixes, on `abf7b0a3`), `change.txt` (the cut at `10bbbe81`), `change-c7252f6c.txt` (the cut at `c7252f6c`) and `change-638f275d.txt` (the final cut). `dump.py` and `show.py` read them, and `flows/src/FlowsLastActSkip.cs` was added by the audit |
| `probes-service/` | A service on `TestServer`'s defaults that makes an onward call for each request: `S0` to `S5`, which show the trace join and the `OutOfItsSteps` marker |
| `probes-runtime-async/` | `A0` to `A5`, state machines and .NET 11's runtime async, on #226's patch |
| `probes-ingest/` | `gen.py` writes ingest files of each label shape and of nested levels, and `paths.py` reads each call's step |
| `probes-128-probe.cs.txt` | #128's own repro, as a file-based program |
| `prototype/on-223-638f275d.patch` | **The final cut**: this plan's whole layer with R2 on #223's prototype at `638f275d`, the branch `proto/223-on-5282`, on 5.28.2. It is `on-223-306557a4.patch` with `apply_r2.py --on-layer` on top, neither with a conflict |
| `prototype/end-state-638f275d-on-9d994a4b.patch` | The same tree as one patch on `9d994a4b`, #223's part included, for a reader without that branch |
| `prototype/on-223-c7252f6c.patch`, `end-state-c7252f6c-on-9d994a4b.patch` | The cut before it, on #223's `c7252f6c`, with R2's first form. It is `on-223-10bbbe81.patch` with its one conflict settled by `resolve_on_223_r11.py` |
| `prototype/on-223-306557a4.patch` | The layer alone on #223's `306557a4`, for its core suite (`results/core-306557a4.txt`), and the base of the final cut |
| `prototype/on-223-10bbbe81.patch` | The cut before it, on #223's `10bbbe81`, on which the audit's first results ran. `resolve_on_223_r9.py` settles its one conflict |
| `prototype/end-state-on-9d994a4b.patch` | That tree as one patch on `9d994a4b`, #223's part included |
| `prototype/r2-alone.patch`, `apply_r2.py` | **The code of R2**, on clean main (its comments and the wiki's lines, which §7.2 gives it, are not in the patch): F9, the matcher, the rows with no bar and F16. `apply_r2.py <worktree>` writes it, and `--on-layer` lays it on this plan's layer, where F9 is the layer's own line |
| `prototype/r2-alone-first-form.patch`, `apply_r2_matcher.py`, `apply_r2_alone.py` | R2's first form, which two reviews broke (the plan's §8.4) |
| `prototype/apply_forced_read.py` | #129's read, forced: the read R5 would ship, and what the plan's Q11 was measured with. It leaves the message as it is |
| `prototype/apply_end_state.py`, `apply_left_marker.py`, `apply_bypass_and_no_shared.py` | The audit's fixes as scripts, on `on-223-abf7b0a3.patch` and the matcher: the trace join, F9, `FlowIsInAFrame`, `failures --json`, the second copy of `query.py`; `OutOfItsSteps`; `LeftABypass` and no step when nothing is shared |
| `prototype/without_left_a_bypass.py` | What whichever of R1 and #226's weave lands second does |
| `prototype/226-305e03ff.patch`, `226-07badf13.patch`, `226-f8ac78b4.patch`, `226-measured-1100.patch`, `224-cut11-r1-r2-r3.patch`, `224-r1-r2-r3-measured.patch` | The sibling patches as they were when measured, since each lives uncommitted in another worktree and has moved or may move |
| `scripts/gates/` | The scripts that repeated every gate on R2's last form, in the order `all_gates.sh` runs them, with this session's scratch paths in them (`m129`, `k225-*`): the order and the commands are what carry over |
| `scripts/run_with_226_newer.sh` | #226's patch on the final cut: the hunks to settle, the skip shapes with and without `LeftABypass`, state machines and runtime async (`results/*-f8ac78b4.txt`, `*-07badf13.txt`, `*-305e03ff.txt`). `scripts/compare_226_patches.py` compares two of the patches file by file |
| `prototype/224-r1-r2-r3-cut10-1658.patch` | #224's later patch, which changes how a bypass ends. Only checked for conflicts (`results/224-newer-patch-apply.txt`) |
| `prototype/apply_with_224.py`, `apply_with_224_rename.py` | #224's `r1-r2-r3.patch`, kept as `224-r1-r2-r3-measured.patch`, settled on this plan's end state, with the two flows side by side |
| `prototype/cut_r1.py` | The line between R1 and R3: cuts the final build's paths back to top-level, for R1's own results |
| `prototype/fifth-cut.patch` | The final cut before the audit, on `27cc0e46`: the whole change with the first review's fixes, and #222's suggested line |
| `prototype/first-cut.patch`, `second-cut.patch` | Earlier cuts, kept because the plan's §8.4 is about what the review found in the second. `apply_proto*.py` are the same edits as scripts |
| `prototype/apply_one.py`, `on-223-abf7b0a3.patch` | The two prototypes built as one, as first done: the fifth cut on #223's `abf7b0a3`. The patch is against that commit, and `scripts/make_one.sh` builds it on main |
| `scripts/apply_probe_one.py`, `probe_final_path.py`, `differential_final.py` | The differential on the final cut: what the walk says against the path a record ends with, split by whose flow answered |
| `scripts/compare_reports.py` | Two reports of one suite, by stable id: the tree and each call's step, generated ids normalised |
| `scripts/read_calls.py` | A report's rows and each call's step, through the query engine alone |
| `scripts/run_breakfastprovider.sh`, `bp-bump.py` | BreakfastProvider's four in-memory lanes in a scratch clone, published against a prototype's packages. Never in `C:/Code/BreakfastProvider`, and never pushed |
| `scripts/read_breakfastprovider.sh` | Reads a finished run of that: each lane's published report against its prototype report, then the walk against the final path |
| `scripts/run_examples_differential.sh` | The example suites on a probe build, the walk against the final path, one TSV per suite |
| `scripts/run_final_probes.sh` | Every probe here on the final cut's packages against published 5.28.2 |
| `scripts/run_cut.sh`, `compare_repro.py` | Packs a worktree's state and reads the probe suite and service probes on it, for R1 alone or a stack with #222's R1; two such outputs compared by scenario |
| `scripts/run_perf.sh` | `perf/` on published and a prototype's packages, two rounds of each, alternating |
| `scripts/run_kept_outputs.sh` | Reruns the few outputs the plan quotes that an earlier run did not keep |
| `scripts/add_probes2.py`, `add_probes3.py`, `fix_shapes.py` | How `repro/`'s probes were added to the copies of the suite, already in `repro/` |
| `scripts/apply_222.py` | #222's own suggested line on an early prototype, to see whether it removes the wrong answer. #222's plan owns the real fix |
| `scripts/run_labels.sh`, `run_matchers.sh`, `probes-ingest/` | The ingest's labels through one build, or through each matcher on one worktree (`results/matchers-compared.txt`, on the first form; `results/r2-labels.txt`, on the final form) |
| `probes-matcher/generators/` | Ingest cases as folders of a tests file and a capture. `make.py`, `make2.py` and `make3.py` are #223's session's (its fifth review, `A` to `W`), `calls.py` its capture for the checked-in Cucumber runs, `make_position.py` and `make_position2.py` the rows with no bar (`X`, `Y`, `Z`). Each takes an output folder, but for `calls.py`, which takes `<messages.ndjson> <out capture file>` |
| `probes-matcher/review-cases/` | The fresh review of R2's second form: `make1.py` to `make5.py` write `cases`, `cases2`, `cases4` and `cases5` to the folder named. `make3.py` called a helper of the reviewer's that was not kept, so its output is kept as `cases3/`. `H1` and `H2` carry a `flags.txt`, which `scripts/run_cases.py` passes to the ingest. `run.py` and `race.py` hold the reviewer's own paths and are kept to read |
| `probes-matcher/store-cases/` | A store's own captures, `S1` to `S12`, kept as data, as their generator was not kept |
| `scripts/for-the-releases/`, `results/r2-red-proof.txt`, `r2-mutations.txt`, `r2-whole-suite.txt`, `r2-core-release.txt`, `r2-examples.txt`, `r2-e2e-step.txt`, `r2-bp-release.txt`, and `r5-red-proof.txt`, `r5-whole-suite.txt`, `r5-examples.txt`, `r5-bp-release.txt` for R5, 5.28.5 | R2 as released, 5.28.4: `make_tests.py` copied the kept cases into `tests/Kronikol.Tests/TestData/StepWalk/` and wrote `StepWalkTests.cs` from `StepWalkTests.template.cs`, with what each read from the result files here, `compare_red.py` checks the red run against those reads, and `mutations_r2.py` and `full_suite.py` ran §8.5's mutations and the suite |
| `probes-matcher/narrow-check-cases/` | The narrow check of R2's exact compare: `A1` to `A9`, `B1` to `B5`, `C1` to `C4`, `E1`, with `make_cases.py` and `make_cases2.py`, which wrote them (`results/r2-narrow-check-cases.txt`, the plan's §8.4) |
| `probes-matcher/narrow-check-cases/second/` | The second narrow check, of the look-ahead pass-over: `F`, `S`, `O`, `R`, `N`, `B` and `C` cases, kept as data, with the reviewer's `make_cases*.py`, `patch*.py` and `time_cases.py` (`results/r2-second-narrow-check-cases.txt`). `T1` to `T5`, 7.6 MB, were not kept (`results/r2-walk-time.txt`). `results/r2-without-the-pass-over.txt` compares R2 with and without it on the 94 cases. `third/` holds the text check's `K1` to `K5` (`results/r2-text-check-cases.txt`) |
| `review-probes/bddfy-fluent-src/`, `scripts/run_bddfy_fluent.sh`, `bddfy_fluent_project.py`, `bddfy_fluent_read.py` | BDDfy's `And` and `But` on the real adapter: fluent with titles and without, and reflective names, plain, escaped and long (`results/bddfy-fluent-r2-alone.txt`, `results/bddfy-fluent-638f275d.txt`) |
| `probes-matcher/fuzz/` | The matcher against the real `Build`, by reflection: `Fuzz <Kronikol.dll> <iterations> [seed]` (`results/r2-fuzz.txt`) |
| `probes-matcher/consumer-shapes/`, `scripts/run_consumer_shapes.sh`, `read_shape.py` | Scenarios a consumer builds by hand, on published and a worktree (`results/r2-consumer-shapes.txt`) |
| `probes-matcher/reused-records/`, `scripts/run_reused_records.py` | Ingests over one list of a host's records. Build with `-p:KronikolDll=<any build's Kronikol.dll>`, and the script runs it against each build's folder (`results/r2-reused-records.txt`) |
| `probes-matcher/peer-case-af/` | #222's session's own bar, as data |
| `probes-matcher/fresh-check-cases/` | `N1` to `N4`, written by the check that found the plan's last 29 faults, as data, with its generators, which hold its paths |
| `scripts/run_one_case.py` | One case's step rows and every record's step, responses included, on each build (`results/r2-responses-and-trees.txt`) |
| `scripts/run_cases.py`, `run_cucumber.py`, `flags_table.py`, `explain.py`, `dump_diff.py` | Cases through one or more builds, each call's step and what was flagged; the flags as a table; what a case holds beside what the report reads; two dumps' differing lines. Each says how to run it in its first lines |
| `scripts/round_trip.py` | R3's round-trip fact over the dumps: does every call's stepPath name a row its scenario lists? (`results/round-trip.txt`) |
| `scripts/run_lanes.sh` | The lanes around step tracking, the core suite and the example suites on a worktree |
| `scripts/run_third_review.sh`, `compare_dumps.py`, `run_223_alone.sh`, `run_bypass_with_226.sh` | The third review's probes on a worktree; two dumps compared with call order ignored; the same probes on #223's prototype alone, to say whose a tree change is; R1's bypass rule against #226's weave, both ways |
| `scripts/run_repro.sh`, `run_service_probes.sh`, `run_runtime_async.sh` | Each probe on a given version, printing rows and each call's step |
| `scripts/apply_probe.py` | Probe only, never for a release: writes one TSV line per record with what the walk says and what the stamp says |
| `scripts/differential.py` | Reads those TSV files |
| `scripts/compare_diagrams.py` | Compares `kronikol query diagram` output between two runs, ignoring what differs by nature |
| `scripts/compare_frameworks.py`, `run_review_probes.sh` | Rerun and compare the review's probes |
| `scripts/run_p223.sh`, `p223_deep.py` | The review's wrong-row probes on a copy of #223's prototype, with its map changed to give the whole path: tests §7.1's claim that its pairing cures them |
| `scripts/run_sibling_review.sh` | The probes of #223's second review on a copy of a prototype: did the faults it measured reach this plan? |
| `scripts/apply_r1_stage.py` | Cuts a copy back to one stage of the release order (R1 alone, R1 reading the step last opened, R1 reading the flow), for §7.1's table |
| `scripts/density.py` | Counts steps, sub-steps and assertions in a data file |
| `results/` | A header saying "the final cut" with no commit was written before `c7252f6c`, and means `10bbbe81` or earlier. Files named `r2-*` are R2 alone, in its final form, on `9d994a4b`. The output each RUN line in the plan quotes, and the outputs of earlier cuts and stages that the plan no longer quotes (`lanes-one*`, `sibling-review-*-one*`, `*-stage-*`, `*-fifth-cut*`) |

## Running it

1. **Published.** In a copy of `repro/` outside this repo (so `Directory.Build.props` is not picked up):
   `dotnet test -c Release`, then `PROBE_NO_BARS=1 dotnet test -c Release --no-build`. Reports land in
   `bin/Release/net10.0/Reports`. Two scenarios of #222's suite and one probe fail on purpose: 3 failed.
2. **Prototype.** `git worktree add --detach <dir> 27cc0e46`, `git apply prototype/fifth-cut.patch`. Build and pack
   `Kronikol`, `Kronikol.ReqNRoll.Core`, `Kronikol.ReqNRoll.xUnit2`, `Kronikol.StepTracking`, `Kronikol.AssertionTracking`,
   `Kronikol.LightBDD.Core`, `Kronikol.LightBDD.xUnit3` and `Kronikol.xUnit3` with `-p:Version=<a prerelease of your own>`, build first and pack
   with `--no-build` (a pack alone fails with NU5026 on a fresh worktree). Point a `nuget.config` at the folder and change the suite's
   versions. A new prerelease each time: NuGet caches a version once.
3. **Query.** `dotnet run --file <reports>/query.cs -- steps <reports> s4`, or `Kronikol.Tool` built from the prototype, which holds the
   reader changes. The published query engine reads a prototype report too: `results/old-tool-new-report.txt`.
4. **Differential.** On the prototype, `python scripts/apply_probe.py <dir>`, then for each suite under `examples/Example.Api/tests`:
   `KRONIKOL_PROBE_ATTRIBUTION=<file> dotnet test <suite> -c Release`, `python scripts/differential.py <files>`. A TUnit suite takes
   `dotnet run --project <suite> -c Release` in place of `dotnet test`.
5. **The review's probes** expect two source copies of the repo beside them, `base` and `proto`, each with its weaver built in Debug
   (`KRoot` in their project files). `python review-probes/gen-projects.py <probes dir> base proto` writes the projects and copies the sources, and `fw-gen.py` writes the framework
   projects, each run appending to `fw-out.txt`, or to `FW_OUT` if set.
6. **#223's second review.** `scripts/run_sibling_review.sh review-probes/sibling-223 <work dir> <prototype> <Kronikol.Tool.dll> results <label>`. Its published output is in that plan's harness.
7. **As one.** `git worktree add --detach <dir> abf7b0a3`, which is on the local branch `proto/223-hook-steps`, `git apply --3way prototype/fifth-cut.patch` there (six files conflict), then `python prototype/apply_one.py <dir>`. `prototype/on-223-abf7b0a3.patch` is the result, against that commit, and `scripts/apply_r1_stage.py` cuts a copy back to a stage of the order.
8. **The final cut.** `git worktree add --detach <dir> 638f275d`, which is on the local branch `proto/223-on-5282`, then `git apply prototype/on-223-638f275d.patch`. The cut before it is `c7252f6c` with `on-223-c7252f6c.patch`. The same layer as `on-223-10bbbe81.patch` applies with `git apply --3way` on each commit of that branch since: with no conflict on `10bbbe81`'s next commit `9b85227f`, which changes BDDfy alone, with one on `c7252f6c`, which `python prototype/resolve_on_223_r11.py <dir>` settles, and with none on `306557a4`, which held R2's matcher itself until that session took it out. It also applies with no conflict on `proto/222-then-223`, #222's R1 under #223's. With no such branch, `git worktree add --detach <dir> 9d994a4b` and `git apply prototype/end-state-638f275d-on-9d994a4b.patch`.
9. **The third review's probes.** Their project files hold absolute paths to a worktree, which `scripts/run_third_review.sh <probes dir> <worktree name>` rewrites. Set `MSBUILDDISABLENODEREUSE=1`, as the scripts do: a reused build node keeps the weaver loaded from the last build, and the next build cannot copy over it.
10. **BreakfastProvider.** On a worktree with `scripts/apply_probe_one.py` and `probe_final_path.py` applied: `SKIP_CONTROL=1 VER=<a new prerelease> bash scripts/run_breakfastprovider.sh <worktree> <work dir> results <bp-bump.py>`, leaving out `SKIP_CONTROL` the first time. Then `python scripts/differential_final.py results/bp-*.tsv` and `python scripts/compare_reports.py <work dir>/out/published/<lane> <work dir>/out/proto/<lane> dotnet <Kronikol.Tool.dll> decode`.

## Probe scenarios

Addresses are in `results/probe-scenario-addresses.txt`.

| Address | Scenario | Published | Prototype |
|---|---|---|---|
| s4 | The issue's: a `Given` with two sub-steps | Every call reads `0` | `0.0`, `0.0.0`, `0.1` |
| s6 | A step's own calls around a sub-step | All read `0` | Own calls `0`, the sub-step's `0.0` |
| s7 | A call outlives its step | Reads the later step | The same (Q1) |
| s8, s10 | A backslash in a label, a label over 110 characters (#128) | No step | `0` |
| s11 | A sub-step left running | The `When` step becomes its sibling's sub-step, and has no bar | `0.0` and `1`, with the bar |
| s12 | A call on another thread | `0` | `0.0` |
| s13 | An assertion while a sub-step runs | Under the sub-step | Under its own step |
| s14 | Two at once, the first throws | **The second reads failed** | The first reads failed |
| s15 | One sub-step twice | Both `0` | `0.0`, `0.1` |
| s16 | Two at once | The second nests under the first | Siblings, each with its own calls |
| s17 | A step definition that is also a woven step | `0` | `0.0.0.0` for the deepest: the Gherkin frame and the woven step are one level each |
| s18 | A `[Step]` in a hook (#223) | Every call has no step | On the final cut, with #223's pairing, the rows and each call read right |
| s23, s24 | A skipped sub-step under a Gherkin step, async and sync | `own=0 sub=None first=None second=None` | `own=0 sub=0.1 first=1 second=1` |

## Traps met

- **My own probes passed what a reviewer broke in an hour.** They all had one test at a time and short-lived tasks. Before calling a design done, have something
  attack it that was given the code and not the reasoning.
- **A script read stale reports.** `find bin -name Reports | head -1` found the reviewer's `bin/Debug` folder, so a fix looked as if it had done nothing. The script now reads
  `bin/Release` alone. When a fix seems to change nothing, check which output was read.
- A Bash heredoc collapses a doubled backslash, so a C# regex with `\\` came out broken (the binding error failed all 18). Write such
  edits with a script file (`scripts/fix_binding.py`).
- A probe with a long scenario **title** does not show #128: the step's own text has to be long. s9 is that wrong probe, left in as a
  control, and s10 is the right one.
- `dotnet test` does not start a TUnit suite here (Microsoft.Testing.Platform): use `dotnet run`.
- Reqnroll writes the assembly's name into a class name, so a project called `rq-proto` does not compile. It needs an `AssemblyName`.
- An early claim here said BreakfastProvider's reports hold no sub-steps. That read two lanes' reports, Reqnroll's and one with 212 scenarios, and not LightBDD's. Its LightBDD lane has 1,676 records on sub-steps, so that suite does test R3 (`results/breakfastprovider-differential.txt`).
- A probe build that references a worktree loads that worktree's weaver into a reused MSBuild node, which then locks it. Export `MSBUILDDISABLENODEREUSE=1`, or a later build fails on the copy and the next one on a parameter the stale weaver does not know.
- A comparison can pass on nothing. `query diagram <report> s3/d0` prints no source without `--out`, so a first run of the diagram gate compared 25 empty files and reported all the same. `scripts/run_diagrams.sh` now counts files with content.
- A shape that passes can hide the one that fails: `F16` and `F17` have a step after the skip, so a completion taken for the bypass's never shows. A parent whose last act is a skip does (`flows/src/FlowsLastActSkip.cs`).
- A comparison can pass on nothing, again: R2's second form first read a bar still wrapped in line feeds (`InteractionRecord.cs:512`), so it refused every bar, and 22 of 22 cases "read as published". The 20 labels, which must change, showed it. A check that only proves nothing moved needs a case beside it that must move.
- NuGet takes the next version up when a package is not in the feed, and says so only as a warning, NU1603. The probe suite takes `Kronikol.ReqNRoll.xUnit2`, which `scripts/run_breakfastprovider.sh` does not pack, so one run read another session's cached `5.28.3-r1` and every sub-step's call fell to the top level. Read `obj/project.assets.json` when a whole suite moves at once.
- A gain is read by reading the case. `compare_reports.py` said 9 LightBDD calls gained a step, and the plan called that #128's fault. R2 alone left them as they were, and forcing #129's read named the bars (F17).
