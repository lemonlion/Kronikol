# Harness: STEP_PHASE_PLAN.md (#224)

The probes behind every RUN line of [`../STEP_PHASE_PLAN.md`](../STEP_PHASE_PLAN.md), with what each printed. Baseline: the published 5.28.2 packages, measured on 2026-11-03 on Windows 11 with the .NET SDK 10.0.300.
The prototype is the patch set in `prototype/`, on `9d994a4b`.

## The four suites

Each is a small real test suite on a real framework, with no server: a tracked `HttpClient` over a handler that really waits and answers
everything, and a `MessageTracker`. At each labelled point a test makes one HTTP call and sends one message, so a dump shows
two phases per point. The message tracker reads the ambient phase as it stands in the test's flow; the HTTP handler can change
it first, inside its own `SendAsync`, so the two columns differ exactly where that happens.

| Folder | Framework | Tests |
|---|---|---|
| `probes/reqnroll-xunit2` | Reqnroll 3.3.4 on xUnit v2, with `Kronikol.StepTracking` | R00 to R21 (22 tests), one scenario each in `Features/Phases.feature` |
| `probes/xunit3-plain` | xUnit v3 3.2.2, `Kronikol.StepTracking` and no BDD framework | X01 to X37 in `Shapes.cs`, 36 tests (X14 was #226's shape, filed on its own, and is gone) |
| `probes/lightbdd-xunit2` | LightBDD 3.12.0 on xUnit v2, with `Kronikol.StepTracking` | L01 to L16 in `Scenarios.cs` |
| `probes/bddfy-xunit3` | BDDfy 8.0.1.3 on xUnit v3, with `Kronikol.StepTracking` | B01 to B11 in `Scenarios.cs` |

They take the **published 5.28.2** packages from nuget.org by default, so the baseline is what a consumer has, and they build
nothing in the repository. `-p:KronikolVersion=5.28.3-<suffix>` takes a prototype from `probes/localfeed` instead, which
`run-variant.sh` fills (not kept: it rebuilds from a patch).

Each suite's end-of-run hook calls `Dump.Write`, which writes `results/<variant>/<suite>.txt`: for every test, its calls in the
order they were logged, with the HTTP phase, the message phase, `IGNORED` where the handler's `TrackDuringSetup` or
`TrackDuringAction` dropped the call, `--- ACTION START MARKER ---` where the Setup/Action boundary falls, each step bar, the
step tree `StepCollector` holds, and a `late message:` line for a message sent after the marker though its row sits before it.

## Reproducing

```bash
# The baseline, in each suite's folder (nothing to build):
PHASE_PROBE_OUT=../../results/5.28.2 dotnet test

# A stage: a worktree of the repo at 9d994a4b with the patch applied.
git worktree add ../wt 9d994a4b && git -C ../wt apply prototype/r1-r2-r3.patch
bash probes/run-variant.sh ../wt r3

python -I probes/score.py results r3 5.28.2 issue r1 r2 r3      # results/score.txt
python -I probes/against_boundary.py results r3                 # results/against-boundary.txt, one variant at a time
python -I probes/side_by_side.py results 5.28.2 issue r1 r2 r3  # results/side-by-side.txt, every labelled point across the variants
bash probes/run-examples.sh ../wt <label>                       # the repo's 11 example suites, results/examples/<label>/
bash probes/compare-examples.sh base final                      # results/examples-compare.txt
```

`score.py` needs a key, and the key is R3's own output, read line by line against the plan's §5.1 before it was used as one. So its zero for R3 says only that the key is R3. `against_boundary.py` needs none: it counts a call drawn after a test's boundary that does not read Action, and one before it that does. A row of a dump sits where its call's first record is, so a message sent after the marker is read from its `late message:` line.

A rebuilt prototype needs its copies gone from the package cache, as a package is cached by version: `run-variant.sh` deletes them. An edit restored from a backup needs a new modification time, or an incremental build does not see it. A package missing from a feed is no error: NuGet takes the lowest version at or above the one asked for, and prerelease labels compare by text, so `5.28.3-rev10` with no such package took `5.28.3-rev5`, an earlier cut, with only NU1603, which the scripts filter. That had one adapter's dumps reading the wrong cut until marker counts across the stages gave it away, so check that a feed holds every id at the version asked for.

## What else is here

| Folder | What | Baseline and results |
|---|---|---|
| `probes/matrix` | The same shapes on NUnit, MSTest, TUnit, xUnit v2 and v3, Reqnroll on four frameworks, lifecycle methods, retries, unflowed threads, nested Gherkin steps and a real server. Named `matrix/<test>` in the plan. `run-all.sh` runs them, `matrix_moves.py` lists what moved | `results/matrix/5.28.2`, `results/matrix/final`, `results/matrix/moves.txt` |
| `probes/review3` | The third review's probes, `rev3-*`, on every adapter. `run-suite.sh` and `racy.sh` run them, `sbs.py` compares two dumps, `racy_scan.py` reads the runs of a shape that races. Named `review/<test>` in the plan | `results/review3/5.28.2`, `results/review3/final` (one run each; the racing suites were run 8 times) |
| `probes/review4` | The fourth review's probes, `rev4-*`: detached workers, repeated `StartAction()`, a request and response read apart, in-flight flows, diagrams, weaving, F#. Named `review4/<test>` in the plan. `pack-rev4.sh`, `run-suite.sh` | `results/review4/5.28.2`, `results/review4/final` |
| `probes/review5` | The fifth review's probes, `rev5-*`, and the adapters `adapter-*`: hand-made steps, flows, BDDfy's reset, the `[Obsolete]` LightBDD path on one host, Reqnroll on three hosts and in parallel, readers. Named `review5/<test>` in the plan. `pack.sh`, `run-suite.sh`, `run-tunit.sh`, `run-race.sh`, `run-readers.sh` | `results/review5/5.28.2`, `r1`, `r2`, `final`, one run each, and `results/review5/race` |
| `probes/review6` | The sixth review's probes, `rev6-*`: `SkipIf` getters, a matrix of skipped sub-steps, hand-made steps, fetchers that throw, mixed package versions, retries on NUnit. Named `review6/S01` and so on in the plan. `pack.sh <worktree> <version>`, then `run-suite.sh <suite> <version>`; dumps at each stage in `results/review6/`, by version, `mix-*` the mixed packages |
| `probes/consumer` | BreakfastProvider, the reference consumer, at `640c190` in a scratch clone: `run-bp-final10.sh` moves its pins to 5.28.2 and to the patch, lane by lane, in memory. `repeat.sh`, `lost.sh` repeat its LightBDD lane. `compare_consumer.py`, `compare_consumer_structure.py` compare them | `results/consumer`, `consumer-compare.txt`, `consumer-structure.txt`, `consumer-repeat`, `consumer-lost` |
| `probes/review` | The first two reviews' probes, kept as they were: measured on 5.27.13 and on the cuts of that day | `probes/review/out`, `out-first-cut`, `second` |
| `probes/runtime-async` | Whether an `AsyncLocal` write reaches an async method's caller on .NET 11 RC3 with `runtime-async=on` | Printed on a run; the plan's F22 quotes it |
| `probes/net11` | The weaver's facts on the .NET 11 SDK, on #226's weaver with this fix (`prototype/on-226.patch`). The SDK is RC3, in a private folder: `DOTNET11` names it | `results/net11-on226.txt` |
| `probes/read-cost` | What a read of the phase costs, and a whole tracked call, on one version: `run.sh` builds Release against each | `results/read-cost.txt` |
| `probes/mutations` | The mutation run of the plan's §6: one piece of the fix broken at a time, packed, and run by the suites | `results/mutations.txt` (the reports), `results/mutations/` (each mutant's changes), `tools/mutations11.py` (the list), `mut-extra/` (the shapes it had to write) |

| Patch | What |
|---|---|
| `prototype/issue-suggestion.patch` | The issue's own line, for `results/issue` |
| `prototype/r1.patch`, `r1-r2.patch`, `r1-r2-r3.patch` | The three stages, each from `9d994a4b`. No tests: the plan's §6 lists them |
| `prototype/on-226.patch` | The fix's core on #226's wrapper, with no weaver edit: `r1-r2-r3.patch` minus `StepWeaver.cs`, applied over `226-latest.patch` |
| `prototype/226-latest.patch` | #226's draft as it stood when that was measured |
| `prototype/cut5/`, `prototype/cut8/` | Set aside in §5.9: the "last decision everywhere" cut, and the cut whose `TestPhaseContext.Current` read the test's decision |
| `prototype/cut9/`, `make_cut9.py`, `make_cut10.py`, `apply_trial2.py` | The cut the fifth review saw, and the scripts that made this cut: `make_cut10.py` on a stage of `cut9/` is exactly what changed after that review |
| `prototype/cut10/`, `prototype/cut11/`, `make_cut11.py`, `apply_fix_review6.py` | The cut the sixth review saw, this cut, and what changed between them: `make_cut11.py` on a stage of `cut10/` is exactly the sixth review's fixes. The root patches are copies of `cut11/`. `cut11/r1-r3.patch` is R1 and R3 without R2 |

| Result | What |
|---|---|
| `results/5.28.2`, `issue`, `r1`, `r2`, `r3` | The stages. `r3` is the key |
| `results/cut4-r3`, `cut5-r3`, `cut8-r3` | Options set aside in §5.9 |
| `results/cut9-r3` | The cut the fifth review saw, for what its fixes moved |
| `results/cut10-r3`, `results/r1r3` | The cut the sixth review saw, and R1 and R3 without R2, for §7.5's order |
| `results/on226` | The fix on #226's weaver |
| `results/5.28.2/issue-scenario-query.txt` | `query interactions <reports> s0 --group-by step,phase,method,path` on R00, the issue's own table |
| `results/examples/base`, `final` | The repo's example suites at `9d994a4b` and on the patch: each scenario's phases, its diagrams' source and its history fragment |
| `results/examples/p1` | The example suites with service-side flows reading the test's phase, the option of §5.9 |
| `results/history/<stage>/<suite>.json`, `history-compare.txt` | Each suite's `History.run.json` at each stage, and which fingerprints moved |
| `results/history-moves.txt`, `history-same-version.txt` | The moves named, sets apart from orders, and which tests' orders differ between three runs of 5.28.2 itself |
| `results/history-b11.txt`, `probes/b11_runs.py` | B11's fingerprint over eight runs of 5.28.2 and of all three stages: a race at 5.28.2 itself |
| `results/b05-suite-runs.txt`, `results/b05-runs.txt`, `results/review5/b05/`, `probes/b05_keywords.py`, `b05_runs.py` | `review5/B05`, parallel BDDfy stories, eight runs a version, in a whole suite's run and alone: it differs, so read the suite's |
| `results/query-diff.txt` | Every read-only `kronikol query` verb at 5.28.2 against this cut, over `review/rev3-readers`' reports: only what differs is printed |
| `results/retry-verdicts.txt` | A retried test's verdict and calls in the report, on six suites, at 5.28.2 and on this cut |
| `results/review3/q13-5.28.2`, `q13-final` | `review/Q13`'s dumps, a run of its own as it sets a different option |
| `results/review6/mix11-*` | Core and weaver at different versions on this cut, with each version's own pair as control |
| `results/reviews/` | What the third to sixth reviews reported, each on the cut it saw |
| `results/test-runs.txt` | The repo's own suites at each stage |
| `results/read-cost.txt` | What a read of the phase and a tracked call cost on this cut, whose getter is 5.28.2's, against 5.28.2 |
| `results/read-cost-cut8.txt` | The same on the eighth cut, whose getter called the resolver: where a throwing resolver's price comes from |
| `results/restore-at-complete.txt` | Setting a phase back in `CompleteStepAsync`: the write never reaches the caller |

## What building the plan added

| Folder | What |
|---|---|
| `results/reviews/impl-r1-report.md`, `impl-r2-report.md`, `impl-r3-report.md`, `impl-port-report.md` | The independent review of each release, on its own packages, briefed to break it, with its brief and its trial fix beside it; `impl-port` read the releases as ported onto #223's frames and #225's flow |
| `probes/impl-r1`, `impl-r2`, `impl-r3`, `impl-port` | Those reviews' probes, sources only |
| `results/impl/mut-*.txt` | The mutation runs on the real code against the repo's facts: `mut-r1` R1's facts, `mut-r2` R2's and R3's, the others what the reviews' fixes and the missing facts changed; `mut-port*` the releases as they landed, in `mut_port.py`'s words |
| `probes/mutations/tools/impl/` | The runner, and the hand-made namesakes of the mutants whose text the real code words differently |

## What this harness does not do

Nothing here opens a report file: the dumps come from the probes' own hook, and anything else goes through `query`. The probes write a `Reports/` folder under `bin/`, which is not kept. Screens are not measured, so the paint facts of §6 are still to be written.
