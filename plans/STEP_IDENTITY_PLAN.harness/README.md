# Harness for `plans/STEP_IDENTITY_PLAN.md` (#223)

Every RUN line in the plan comes from here, unless the line names the peer session that ran it. Written 2026-11-02 at 5.27.13, checked again 2026-11-03 at 5.28.2, on a Windows machine, .NET 10 SDK.

## What is here

| Path | What |
|---|---|
| `repro/` | Reqnroll on xUnit v2: `Features/Triggers.feature`, one scenario per trigger T00 to T27, `Bindings/` one hook per trigger. Published 5.27.13 packages by default |
| `repro-bddfy/` | BDDfy on xUnit v3: B0 to B7 |
| `repro-xunit3/` | Plain xUnit v3: P0 to P4, the lane with no framework steps, whose answers must not move |
| `repro-every/` | Reqnroll: a hook step in every scenario of a feature, in one, before a `When`, and with a file attached. `run-every.sh` runs each feature alone, as one of them stops the report |
| `repro-lightbdd/` | LightBDD on xUnit v3: L0 to L4, and V1 to V4, the variants that isolated the last-call fault |
| `repro-more/` | Reqnroll, shapes added at the second check: E01 to E03 outlines and a rule, E04 feature and run hooks, E05 a binding constructor, E06 an argument transformation, E07 a throwing `BeforeStep`, E08 a pending step, E09 a tie with Kronikol's last hook, E13 a call outliving its step, E14 phases, E16 a nested step inside a Background |
| `repro-bddfy-retry/` | BDDfy: a test run again under its own test id (a retrying runner's shape) |
| `make-lane.py`, `run-lanes.sh` | `repro/` on Reqnroll's xUnit v3 and TUnit lanes and its floor 3.0.1 (`WITH_FLOOR=1`), published or prototype: `results/lane-*` |
| `compare-reads.py`, `review-probes/compare-z.py` | Compare two reads by scenario title, rows and calls, with times and ids taken out |
| `hot-path/` | What the stamp costs per logged pair, on both builds: `results/hot-path.txt` |
| `read-report.sh`, `read-lane.sh`, `read-more.sh`, `review-probes/read-z.sh` | Read one run's report through its own `query.cs`, never the report file itself |
| `paint-probe.js` | What the page paints: step rows and bars, in the bundled Chromium through `launchInstalled` |
| `prototype/` | `apply_r1.py` to `apply_r6.py`, each on top of the one before, with the patches they made. R4 is the second cut, from the first review; R5 the third, for a step run by another step; R6 the fourth, from the second review, which takes the flow out. These are stages, not the plan's releases (§7). `rerun-all.sh` runs every probe and suite against a worktree |
| `review-probes/` | The second reviewer's probes: `xu` plain xUnit v3 (P0 to P7, overlaps and a long step), `rq` Reqnroll, `lb` LightBDD. `run.sh` runs one from a copy outside the repo, results in `results/review-*`. The third reviewer's: `z-rq` Reqnroll (BG1 to BG12, N1 to N3), `z-bd` BDDfy (Z8 to Z10), run by `run-z.sh`, results in `results/z-*` and `results/third-review-*` |
| `review-probes/fourth/` | The fourth reviewer's probes: `shapes/` S1 to S7 with `rq6-src`, `rq5-src` (D2 and the hooks), `bd5-src` (X1, X2), `con-src` (a consumer's own rows, a reused step object), `hand-src` (H1, H2), `ingest-probe/make.py` (a capture and a tests file, then `kronikol ingest <captures> --tests <file> -o <dir>`). Copy the folder outside the repo first: `run-shapes.sh` and `run-rq.sh` write `runs/` beside themselves. Results in `results/fourth-review-*`, `results/ingest-r11.txt` |
| `review-probes/fifth/` | The fifth reviewer's: `con7-src` (a consumer's mixed rows, raw bars and the false matches), `lb7-src` (M1 to M3, LightBDD), `store-src` (a store projected from an in-process run), `fmt-src`, `ingest/make.py`, `make2.py` (ingest shapes). Results in `results/fifth-review/` |
| `review-probes/seventh/` | The narrow reviewer's: `rv7-bd` BDDfy, `rv7-xu` plain xUnit v3, `rv7-lb` LightBDD, `rv7-con` a consumer's rows; `NOBARS=1`, or `NO_BARS=1` for `rv7-xu`, hides the bars. Results in `results/seventh-review/` |
| `results/` | Every output the plan quotes. **The last cut** (`638f275d`, at 5.28.2): `lane-*-proto.txt`, `cut-final-*`, `z-*-final*`, `every-final.txt`, `suites-final.txt`, `examples-final-*`, `ingest-final.txt`, `fourth-review-shapes-final.txt`, `seventh-review/con-final.txt`, `seventh-review/rv7-*`. **The fourth reviewer's** own outputs and the same probes on `c7252f6c`: `fourth-review-*`. **The fifth reviewer's**, on `306557a4`: `fifth-review/`. `ingest-r11.txt` is R12's walk, a cut that left the plan. **From `9b85227f`, not rerun, as nothing they read has changed since:** `more-final.txt`, `core-suite-r10.txt`, `core-suite-r11.txt` (the failing run, kept as a record), `hot-path.txt`, `paint-*`, `no-bars-prototype.txt`, `review-xu-final-nobars.txt`, `specifications-final.yml.txt`. **R1 alone:** `cut-r1-*`, `z-*-r1.txt`, `suites-r1.txt`, `fourth-review-shapes-r1-final.txt`, at the commit the plan's §7 names; `core-suite-r1-final.txt` is an older cut. **The merge with #222's R1:** `cut-m222-*`, `every-m222.txt`, `third-review-z-rq-merge222.txt`. **Published:** `*-5.27.13*`, `*-5.28.2*`, `*-published*`, `lane-*-pub.txt`, `z-*-pub.txt`, `examples-base-*`. **Earlier cuts, kept as a record:** `*-prototype-final*` (the fourth, `abf7b0a3`), `*-prototype-r1`, `-r2`, `-r3` (the first), `review-*-proto*` (the fourth). `*.compact.txt` are the same with the noise taken out by `results/compact.awk`. `third-review-*` are that reviewer's own outputs |
| `NOTES.md` | Working notes, in the order things were found |
| `REVIEW.md` | The three independent reviews and what was done about each finding, and the audit of the plan's claims |
| `REVIEW-FINAL-2.md`, `review-probes/final-2/` | The second final review, of the merged code: its report, as written, and its probes. `Probe.Reqnroll` and `Probe.BDDfy` are the shapes, `rows` and `held` read one report's rows and held calls |
| `REVIEW-FINAL-3.md`, `review-probes/final-3/` | The narrow third review of the second's fixes: its report, its probes (`fixture/`, `unit/`), and its readings before the fixes it reviewed (`old-rows.txt`), on them (`new-rows.txt`), and on the fixes for what it found (`fix3-rows.txt`) |
| `execution/` | `mutate_r1_on_222.py`, the mutation pass over the merged code: `python mutate_r1_on_222.py <worktree> <out file> [ids]`, on a clean worktree, as it restores each file from its own copy. Any `BUILD` in its output is a broken fact and proves nothing. `mutate_r1.py` is the same pass before the merge |
| `results/execution/` | The execution's own outputs: `red-*`, every fact run before its fix; `mutate-r1-*`, the mutation passes; `breakfastprovider-*`, a real consumer by row and by tree; `playwright-whole-on-rehearsal.txt` |

## Run it

Published packages:

```bash
cd repro && dotnet test                # 9 fail on purpose: T05 T06 T07 T08 T13 T15 T18 T22 T26
cd .. && bash read-report.sh repro/bin/Debug/net10.0/Reports results/out.txt
awk -f results/compact.awk results/out.txt
```

`[assembly: TrackSteps]` in `repro/Bindings/Driver.cs` is what turns weaving on. `results/control-no-weaving-5.27.13.txt` is the run without it, and a useful control.

`HOOKSTEPS_NO_BARS=1 dotnet test` runs the same suite with `ShowStepDelimiters = false`.

The prototype, at 5.28.2 (`prototype/final-on-5.28.2.patch` is all of it, against `9d994a4b`; the local branch is `proto/223-on-5282`, with `proto/223-r1-final3` for R1 alone and `proto/222-then-223` for the older merge with #222's R1):

```bash
git worktree add C:/Code/Kronikol-223p -b proto/223-again 9d994a4b
git -C C:/Code/Kronikol-223p apply --whitespace=nowarn "$PWD/prototype/final-on-5.28.2.patch"
bash prototype/measure-cut.sh mine C:/Code/Kronikol-223p <fresh scratch folder>     # every probe, from copies outside the repo: results/cut-mine-*
bash review-probes/run-z.sh "$PWD" <fresh scratch folder> mine C:/Code/Kronikol-223p  # the third review's shapes: results/z-*-mine.txt
bash prototype/run-suites.sh mine C:/Code/Kronikol-223p                             # the suites: results/suites-mine.txt
ONLY_PROTO=1 bash run-lanes.sh <fresh scratch folder> C:/Code/Kronikol-223p 5.28.2  # the triggers on every Reqnroll lane: results/lane-*-proto.txt
python compare-reads.py results/lane-xunit2-proto.txt results/triggers-5.27.13.txt  # what differs from published, scenario by scenario
```

`prototype/apply_r1.py` to `apply_r14.py` are the order things were tried, each on the one before, on `27cc0e46` to R6 and on 5.28.2 after. `prototype/split_releases.py` cuts R1 alone, or R1 and R2, from the final code. `prototype/merge_222_resolve.py` settles the merge with #222's R1 (its docstring has the recipe).

## Traps met

- **A checkout-built adapter changes every stable id.** A `ProjectReference` build has no single suite to name, so ids differ from the published run's. Take the ids for `paint-probe.js` from that run's own results, not from another's.
- **Inside the repo, a run appends to the worktree's `.kronikol/history.jsonl`.** A copy outside the repo says `HistoryUnavailable`, and `KRONIKOL_HISTORY=off` keeps a run out of it.
- **`repro/` must not be built with the prototype in place**, as a `bin/proto` beside `bin/Debug` gets compiled as source. Copy it out.
- **A failed `dotnet build` leaves the old dll**, and `dotnet test --no-build` then runs it happily. Two results here were thrown away for that. Read the build's error count first.
- **`innerText` is empty for a `content-visibility` element not laid out yet**, which read as an empty scenario. The probe uses `textContent`.
- **A LightBDD scenario's missing last call looked like a broken control.** It was `BackgroundCalls` in `query summary`, plan F14.
- **A step method named `Given_...` under `given =>`** reads `GIVEN Given ...` in LightBDD. That is the probe's naming, not a fault.
- **An empty suite log is not a pass.** `--logger "console;verbosity=normal"` prints `Total tests:` and not the `Passed!` line a filter looks for, and one run's whole suite section came back blank that way.
- **A fact can pin what looks like a bug.** The first cut made LightBDD's step type fetcher return null, and two test projects pin its throw. Run the adapters' own suites, not only the core.
- **Screenshots** in `results/paint/` are small and are written again by the probe.
- **Specifications is blank for a run with a failure**, by design. These probes fail on purpose, so to see that page run a passing subset (`--filter`).
- **One report-stopping shape takes the whole run's report with it**, and doubles the runner's count, as every test then reads failed at assembly cleanup. BG10 runs alone for that reason.
- **`reqnroll.json` names the adapter's binding assembly**, so a copy for another lane must change it, or every test fails at once.
- **Generated `*.feature.cs` in a copy** can be stale. The copies leave them out, and a build makes them.
- **A project named `Con` cannot be committed from Windows.** CON is a reserved device name, whatever the extension, so `git add` fails on opening `Con.csproj`, and a checkout would too. Three consumer probes were renamed `ConsumerRows.csproj` at landing.
- **A removal with a shell variable in its path is refused by a safety check.** The scripts use fresh folders instead, and scratch is removed by literal path, or left.
