# History analyzer cost plan (#91)

**Date:** 2026-09-19 · **Repo version:** 3.22.0 (`125a32cc`) · **Status: EXECUTED 2026-09-21 as 3.25.2**
(S1, S2, S3; §8 is the log and says where execution departed from the plan, chiefly that
`HistoryPoint.CallSet` had gained a reader in the tool by then). *As written before execution:* cause
found, **S1 and S2 prototyped in a throwaway worktree and proven** (§1.1; the patch is
`HISTORY_ANALYZER_COST_PLAN.prototype.patch`, which no longer applies), nothing implemented in the
repository, **NOT green-lit**. §7 is the assumption ledger: what is RUN, what is only READ, what is not
known.
**A verification round on 2026-09-19 (§1.2, §1.3) closed the open items** — a real-ledger replay,
the tool path, the guard under CPU starvation — found F5, and **replaced the guard this plan first
proposed, which did not survive being run beside other tests**. `main` moved to 3.22.1 (`82abeb7f`)
meanwhile; the patch still applies.

Covers GitHub issue **#91** (`HistoryAnalyzer.Analyse` takes about 2 s at 5,000 scenarios × 50 runs,
and no test guards it). The issue was filed from `EVIDENCE_SURVIVES_A_RERUN_PLAN.md` §11.4 with the
cause not investigated. It is investigated here.

**Standing permission that shapes this plan (user, 2026-09-18):** breaking changes to recent features
are fine, because nobody is using them yet. S2 removes a public member added in 3.17.0 on that
permission. It still never bumps MAJOR (CLAUDE.md: ask first) — §6.

**Why this is its own plan and not a slice of another (user asked, 2026-09-19).** Two other plans
touch the same method, and neither is the right home:

- `EVIDENCE_SURVIVES_A_RERUN_PLAN.md` found the cost and said in the same breath that it is "not this
  plan's" and "should be settled before any slice here adds analyzer work". That plan is four slices
  across the tool, the report directory and the ledger, not green-lit. A two-line cause with a
  one-file fix should not wait on it.
- `HISTORY_VERDICT_NOISE_PLAN.md` S3 rewrites the same loop in `AnalyseScenario`, and its S4 rests on
  the figure this plan shows to be wrong (§5). It is five slices, not green-lit either, and its
  subject is what a verdict means, not what one costs.

So: own plan, smallest of the three, **goes first**, and §5 records exactly what the other two must
pick up from it. Nothing in either of their files was edited from here.

---

## 0. Summary

The issue guessed "something linear in the roster is done once per scenario". That is the cause, and
it is one line. `AnalyseScenario` finds a scenario in each prior run's roster with
`priorRoster.IndexOf(candidate, slot)` (`HistoryAnalyzer.cs:190`), and `HistoryRoster.IndexOf` is a
`for` loop over the roster (`HistoryModel.cs:102–108`). 5,000 scenarios × 50 prior runs × 2,500
comparisons on average is 625 million string comparisons per analysis. Timed alone, that loop is
**1,832 of the 2,060 ms**.

Four findings the issue does not state:

| # | Finding | Where |
|---|---|---|
| F1 | **The lookup is 90% of the cost, and the analysis is quadratic in scenario count**: 200 → 580 → 2,060 → 7,650 ms at 1,250 → 2,500 → 5,000 → 10,000 scenarios. The issue's "growth curve unknown" is closed. | §1.1 |
| F2 | **A third of what is left is call sets nobody reads.** Every one of ~255,000 `HistoryPoint`s gets its call set spelled out into a fresh string array (`Resolve`), and `CallSet` is read in two places, both only for a scenario whose set of calls changed, and only for two of its points. Nothing outside the analyzer reads it: not the tool, not the HTML, not a test. | §3 |
| F3 | **The 150 ms budget cannot be met on a real ledger by any change to the analyzer**, because `Read` alone is 190–280 ms on the 15 MB a real run's lines make. The budget was set on results-only lines (3.34 MB). It needs restating, not only meeting. | §4 |
| F5 | **Through the tool it is 6.9 s, not 2 s.** `kronikol query history`, `query failures` and `history gate` each take ~6.9 s on the 5,000 × 50 ledger, against 0.15 s for a command that does not analyse. The tool ships `TieredCompilationQuickJitForLoops=false` (deliberately, `QUERY_PERF_PLAN` §1.1: a CLI process lives and dies in tier-0), and that setting makes this one loop **twice as slow** (3.6 s with it flipped): a loop method compiled optimized on first call has no profile, so `Ids[i]` on an `IReadOnlyList<string>` that is an array is never devirtualized. With S1 the commands take 1.0 s and the setting is a win again (1.0 s against 1.2 s). | §1.2 |
| F4 | **A counter of lookups would not have caught this; a counter of roster reads does.** The number of lookups is 250,000 before and after the fix; what changed is the cost of one, so the obvious `HistoryStats` counter stays green through the regression. This plan's first draft concluded from that that the pin had to be a time, and proposed a ratio of two timings. **The verification round showed that ratio cannot tell the two analyzers apart inside a parallel suite** (§1.3). What can is the cost itself, counted: the reads of a prior roster's ids, 1,604,400 against a few hundred. | §2.3 |

Three slices:

| Slice | What | Bump |
|---|---|---|
| **S1** | The lookup: one `(id, slot) → position` dictionary per distinct prior roster, built once per `AnalyseStream`. The red-first test that counts reads of a prior roster (1,604,400 → 800). 2,060 → ~310 ms; through the tool 6.9 → 1.0 s | patch |
| **S2** | Call sets spelled out only for the two points a change is named from; `HistoryPoint.CallSet` goes. ~310 → ~200 ms, 41 → 25 KB allocated per scenario | the same patch release as S1, the removal named in the changelog (decided, §6) |
| **S3** | The budget restated from measurement; `CROSS_RUN_HISTORY_PLAN.md` §2.6, §7.7 and §17.1b corrected | none (plan files only) |

Not taken: making `Read` cheaper (§4), and a cached index inside `HistoryRoster` (§2.2).

---

## 1. How far each claim was checked

- **RUN** — executed here, output read.
- **COMPUTED** — arithmetic from RUN numbers.
- **READ** — the code was read and the claim follows; nothing was executed.
- **ISSUE** — taken from the issue.

| Claim | Level |
|---|---|
| `Analyse` at 5,000 × 50 on `main` takes about 2 s | **RUN** — 2,038–2,120 ms over eight calls in the first round, 2,457–3,139 ms in a second round on a busier machine. The issue measured 2,336 ms |
| The cost is quadratic in scenario count | **RUN** — four sizes, below |
| The `IndexOf` loop is ~90% of it | **RUN** — the same 250,000 lookups timed alone, outside the analyzer: 1,832 ms of 2,060 |
| S1 makes it linear and ~310 ms | **RUN** |
| S2 takes a further ~40% | **RUN** — same-load A/B, below |
| S1, and S1 + S2, break nothing in the unit suite | **RUN** — 5,148 and 5,149 passed, 0 failed, 1 skipped |
| The tests are red on `main` and green patched, every time, under CPU starvation and beside the rest of the suite | **RUN** (§1.3) — after the first test proposed here was shown not to be |
| Nothing but the analyzer reads `HistoryPoint.CallSet` | **RUN** — grep of every `.cs` in the repository: the declaration and two reads, `HistoryAnalyzer.cs:382` and `:406`. The tool's JSON projects named fields (`QueryCommand.History.cs:445`) and does not name it |
| `query history`, `query failures` and `history gate` pay the cost — **more than three times it** | **RUN** (§1.2): 6.9 s each on `main`, 1.0 s with the patch. `merge --history` reaches the same `Analyse` (`MergeCommand.cs:207`) and was **not** timed: it needs shard directories |
| S1 + S2 change no verdict, no evidence text, no number, on a real ledger | **RUN** (§1.2): BreakfastProvider's CI ledger replayed run by run through both analyzers, 272 MB of output, the same SHA-256 |
| …nor on a ledger built to exercise what the real one does not | **RUN** (§1.2): 70 distinct rosters, repeated ids, 60 renames, partial runs; the same SHA-256, and two controls showing the comparison can fail |
| `Read` is 190–280 ms on the realistic 15 MB ledger | **RUN**, noisy — 189 to 745 ms across runs on a loaded machine; the quiet ones sit at 190–280. The issue measured 233 ms |
| `Read` cost follows the bytes per line | **ISSUE** — its three rows: 2.5 MB → 49 ms, 4.2 MB → 101 ms, 15 MB → 233 ms |
| S3's corrected figures | the RUN rows above; S3 itself is prose |

### 1.1 The prototype

Two detached `git worktree`s of `125a32cc` under a temp directory, both removed; the repository's
working tree was never touched. Saved beside this plan as
`HISTORY_ANALYZER_COST_PLAN.prototype.patch` (3 files, +126 −15 as re-cut in §1.3; `git apply
--check` passes on `82abeb7f`). It holds S1, S2 and the two tests. It is evidence, not the
implementation: no version bump, no changelog, and S1's test 3 and S2's test 3 are not in it.

The ledger is the issue's generator with the scenario count as an argument: one roster, 60 run lines
carrying everything a real run writes, 0.4% failures, window 50. Release build, Windows 11,
.NET 10.0.11. Four `Analyse` calls per size; the machine was shared with other sessions and single
readings swing by 30%, so ranges are quoted where they were seen.

**Round one — the cause, and S1.**

| Scenarios | `Analyse` on `main` | the 250k-style `IndexOf` loop alone | `Analyse` with S1 |
|---|---|---|---|
| 1,250 | 187–212 ms | 120 ms | 128–200 ms |
| 2,500 | 562–604 ms | 453 ms | 141–254 ms |
| 5,000 | **2,038–2,069 ms** | **1,832 ms** | **285–332 ms** |
| 10,000 | 7,489–7,743 ms | 7,376 ms | 646–781 ms |

`main` quadruples per doubling (×3.0, ×3.6, ×3.7, diluted at the small end by the linear work). With
S1 each doubling roughly doubles.

**Round two — S2, same load, fastest of four.** `main`, S1 alone and S1 + S2 were run back to back
because the machine had got slower since round one (the `IndexOf` loop alone read 2.0–2.3 s).

| Scenarios | `main` | S1 | S1 + S2 |
|---|---|---|---|
| 5,000 | 2,457 ms | 313 ms | **196 ms** |
| 10,000 | — | 557 ms | **306 ms** |

Before S2 was built, stubbing `Resolve` to return null put the 5,000 case at 169–213 ms against
285–332 ms: the same third, by a different route.

| Step | Result |
|---|---|
| The first draft's ratio test (5,000 against 1,250 scenarios, under 6×), alone in a process, on `main` | **red**: "1,250 scenarios took 379 ms and 5,000 took 4096 ms: 10.8 times, for four times the work" |
| + S1 | green, three runs of three; forced to print: **2.0, 2.2, 2.1, 2.0 times**. *Superseded — §1.3 shows these readings do not survive company, and the test was replaced* |
| S1, whole unit suite | **5,148 passed, 0 failed**, 1 skipped |
| S1 + S2 + the test, whole unit suite | **5,149 passed, 0 failed**, 1 skipped — no existing test needed touching, which is F2 seen from the other side: nothing pinned `CallSet` |

One thing the prototype taught that the reading had not: **`[IO.File]` calls from the PowerShell tool
resolve relative paths against the process directory, not the shell's**, so a scripted edit of the
worktree silently targeted another repository and failed; and `Get-Content | Set-Content` on a patch
re-writes its line endings so it no longer applies. Both cost a round. Patches are byte-copied.

### 1.2 The verification round (2026-09-19)

Asked for by the user, against the three items §7.2 then listed as open. Two detached worktrees of
`82abeb7f` (3.22.1), one unmodified and one with the patch, removed afterwards.

**The replay.** Each run line of a ledger is analysed as the current run, against the ledger cut at
that line (the analyzer does not cut `prior` itself — the evidence plan's F4). Everything
`HistoryVerdicts` holds is serialized, one JSON line per run, bar `HistoryPoint.CallSet`, which one
side no longer has. `ReportReordered` on, so that path runs too.

| Ledger | Runs | Scenario analyses | Points | Named-call scenarios | Output | `main` = patched? |
|---|---|---|---|---|---|---|
| BreakfastProvider CI (`origin/kronikol-history`, 4.5 MB, 18 suites) | 394 | 77,740 | 890,176 | 39 | 272 MB | **same SHA-256** |
| Churn (synthetic, below), 60 renames | 70 | 23,379 | 628,363 | 2,196 | 147 MB | **same SHA-256** |
| Churn with every slot written as 0 | 70 | | | | | **same SHA-256** |

The CI ledger alone would have been the noise plan's first ledger row again — *true and empty*: it
has 18 rosters for 18 suites, so no roster ever changes, no lookup ever misses, there is not one
`new` verdict and no id repeats. S1 is a change to a lookup, and that ledger never asks it a hard
question. Hence the churn ledger: 70 distinct rosters in 70 runs (scenarios added, removed,
shuffled), every run carrying 20 repeated ids (slots 1 and 2), partial runs, skips, retries, call
sets that drift, and an alias file of 60 renames. It produces every verdict kind but `quarantined`
(435 `new`, 6,849 `flaky`, 2,151 `behaviour-changed`, 186 `alternating`, 105 `reordered`, …).

Two controls, because a comparison that cannot fail proves nothing:

| Control | Result |
|---|---|
| The churn ledger with and without its alias file, on `main` | **different** output — the renames are reached |
| Every slot 0 (a repeated `(id, slot)`), patched with `TryAdd` swapped for the indexer (last holder wins) | **different** from `main`; with `TryAdd`, the same. §2.2's "first holder wins" is a behaviour, and this is its proof |

The replay also timed `Analyse` over the whole ledger: CI 1,619 → 1,065 ms, churn 1,902 → 987 ms.
So on a real 200-scenario suite the fix is worth a third, not a factor of seven — the quadratic term
is small there, as the issue said — and 70 distinct rosters cost nothing visible (§7.2's worry).

**The tool.** A 5,000-scenario `TestRunReport.json` beside the 5,000 × 60 ledger, the Release
`Kronikol.Tool.dll` of each worktree, fastest of five process runs:

| Command | `main` | patched |
|---|---|---|
| `query history` | **6,882 ms** | **1,018 ms** |
| `query failures` (its `history:` line) | 6,807 ms | 995 ms |
| `history gate` | 6,567 ms | 991 ms |
| `query summary` (no analysis; the floor) | 151 ms | 151 ms |

Three and a half times the issue's figure. One `Analyse` per command (READ), so the multiplier is in
how the loop is compiled: `Kronikol.Tool.csproj` sets `TieredCompilationQuickJitForLoops=false`.
Flipping it for one run (`DOTNET_TC_QuickJitForLoops=1`): `main` 3,552 ms, patched 1,208 ms.
`TieredPGO=0` and `TieredCompilation=0` both made `main` slower still (7,981 and 8,083 ms). The
mechanism — no profile, so no guarded devirtualization of `IReadOnlyList<string>` over an array — is
**inferred**; the effect is RUN. Nothing is to be changed about the setting: with S1 it wins again.
What it means for the test process at the end of a run (default tiering, one cold call) is the
harness's first call, ~2 s: measured in §1.1, not through a real test run.

**Allocation** (S2's pin), `GC.GetAllocatedBytesForCurrentThread` around one warm `Analyse`,
identical to the byte over three readings:

| Scenarios | `main` | patched |
|---|---|---|
| 2,000 | 79.0 MB — 41,417 B per scenario | 48.7 MB — 25,513 B per scenario |
| 5,000 | 197.3 MB — 41,385 B | 121.2 MB — 25,419 B |

### 1.3 The guard, under starvation — and why it was replaced

Kronikol's CI runs on `ubuntu-latest` and only on a push or pull request to `main`, so a reading on
a real runner needs a pull request and was not taken. The stand-in: the test process pinned to two
logical CPUs of one physical core (`ProcessorAffinity = 3`, confirmed on the process), harsher than
a four-vCPU runner, on a machine another session was also running tests on.

**The whole unit suite, pinned, five times: green five times**, 208–244 s against ~65 s free. The
ratio test passed each time — reading **4.58, 4.55, 2.25, 3.07, 5.27** against its threshold of 6.
Not the 2.0–2.2 of §1.1: alone in a process the 1,250 reading was 107–125 ms of mostly cold JIT;
beside 5,000 other tests the analyzer is already compiled and it is 30–43 ms. Warm, the linear
analyzer reads about 4.5 for four times the work, which is what linear means.

So both analyzers were read under the same conditions, the History namespace only (warm, parallel),
five runs each, with a 10,000 reading added:

| | `main` | patched |
|---|---|---|
| 5,000 / 1,250, pinned | 5.90 – 13.05 | 4.02 – **7.65** |
| 5,000 / 1,250, free | 8.99 – 34.8 | 3.99 – 5.96 |
| 10,000 / 1,250, pinned | 21.6 – 36.4 | 3.85 – 9.12 |
| 10,000 / 1,250, free | 40.7 – 127 | 12.5 – 14.8 |

**The ranges overlap.** Under a threshold of 6 the fixed analyzer failed once and the broken one
passed twice. The 8× variant separates, by a factor of 1.5 between the worst readings — and the
small reading moves by 3× from run to run, because it is 30–140 ms of work sharing a core with
whatever else is running. No choice of sizes fixes that; the quantity is wrong. (Noted in passing:
free, with twenty cores of other tests, `main` took 5.8–8.3 s at 5,000 and 26–29 s at 10,000. The
scan is memory-bound and does not like company.)

**The replacement** (§2.3, §3.3) counts instead. The same ten runs — three pinned and two free, per
side, the whole History namespace in parallel:

| | `main` ×5 | patched ×5 |
|---|---|---|
| Reads of the prior roster's ids (400 scenarios, 20 runs) | **1,604,400** every run | **800** every run |
| Bytes allocated per scenario (50-run window) | 33,357 – 33,428 | **19,254** every run |
| Exit code | 1, the two new tests and nothing else | 0 |

Both numbers were predicted before they were run (400 × 20 × 200; one index pass plus one absent
pass). The allocation test's first version used 2,000 scenarios; it reads the same per scenario at
300 (33,466 on `main`) and the whole analyzer test class then takes 0.35 s.

**The whole unit suite, interleaved, four rounds a side, both trees carrying the two tests:**
patched **0 failed, four times of four**; `main` the two new tests red four times of four. Stray
failures of *other* wall-clock tests turned up on this loaded machine on both sides and are not
this plan's — `MergedJsonOutputTests.The_merged_json_merges_again` once on `main`; and, in three
earlier patched runs made while the allocation test still built a 2,000-scenario ledger,
`NodeJsPlantUmlRendererTests.Batch_of_five_is_faster_than_five_single_spawns` twice and the `Read`
budget test once (10 s against its 1,500 ms). That last one is this plan's argument made by the
repository's own test; whether the heavier allocation test contributed is not known, which is one
more reason it is 300 scenarios now.

**On a GitHub runner** (user's go-ahead, 2026-09-19): draft pull request **#92**, branch
`issue-91-analyzer-cost`, two commits so that CI is red first too — the workflow has no concurrency
group, so both runs complete.

| Commit | Core Tests on `ubuntu-latest` | The two tests |
|---|---|---|
| `dde16290` — the tests alone | 5,152 tests, **2 failed**, 5,149 passed | reads **1,604,400**; **33,508 B** per scenario |
| `7584c797` — the fix | 5,152 tests, **0 failed**, 5,151 passed | green |

Across the whole 28-job matrix: the tests-only run failed one job, Core Tests, and passed 27; the
fix run passed all 28 — E2E, integration, adapters, Playwright, template scaffolds and the release
build, which §7.2 had listed as not run.

The read count is the same number on Linux as on Windows, as a count must be. The allocation figure
on `main` is 33,508 B against 33,357–33,466 on Windows — 0.2% apart — so the 26 KB bound keeps its
margin there. The patched figure on Linux was not printed (the test passed); by the `main` figure it
is within a fraction of a percent of 19,254. The pull request is a probe and says so: no version
bump, no changelog, S1's test 3 and S2's test 3 missing.

The prototype patch beside this plan holds these two tests, not the ratio test (3 files, +126 −15).

---

## 2. S1 — the lookup

### 2.1 What is wrong

```csharp
foreach (var candidate in lookFor)
{
    at = priorRoster.IndexOf(candidate, slot);   // a for-loop over the roster
    if (at >= 0) break;
}
```

Once per scenario, per prior run, per alias. A scenario that is *not* in a prior roster costs the
whole roster, per alias. A window of 50 runs almost always shares one or two roster instances
(rosters are interned by content hash), so the same 5,000-entry list is scanned 250,000 times.

Why nothing caught it is in the issue: the pinned test times `Read` only, and the 0.8 ms came from
the `tools/history-bench` prototype before the shipped analyzer existed.

### 2.2 The change

In `AnalyseStream`, after `priorRosters`:

```csharp
var priorPositions = PositionsOf(priorRosters);
```

`PositionsOf` builds one `Dictionary<(string Id, int Slot), int>` per **distinct roster instance**
(`ReferenceEqualityComparer`), and hands each prior run its roster's dictionary. `AnalyseScenario`
takes the array in place of `priorRosters` — it used the rosters for nothing else — and does a
`TryGetValue` per alias.

Two details that are the reason it is written this way:

- **`TryAdd`, not the indexer.** `IndexOf` returns the *first* position holding an (id, slot). A
  well-formed roster has each pair once, but a roster line comes from a file, and union merges are
  the reason the format exists. First holder wins, as today.
- **Not a cached field on `HistoryRoster`.** It is a `record`: a lazily built private field joins the
  synthesized equality (one roster built, its equal not — unequal) and is copied by `with` (a copy
  with different `Ids` carrying the old index). The analyzer owns the index for the length of one
  analysis and nothing else can see it.

`HistoryRoster.IndexOf` stays. It is public, correct, and fine for one lookup; the doc comment gains
a line saying it is a scan.

### 2.3 Tests (red first)

1. **`A_prior_roster_is_read_a_few_times_per_analysis_not_once_per_scenario`** — in
   `HistoryAnalyzerTests`. **Counted, not timed.** `HistoryRoster.Ids` is an `IReadOnlyList<string>`,
   so the test hands the ledger a roster whose ids are a `CountingList` (every index and every
   enumeration step is one read), through the ledger's internal constructor (`InternalsVisibleTo`
   is already granted). 400 scenarios, 20 prior runs, one analysis. Asserts
   **`reads ≤ 4 × scenarios`**.
   - *Red on `main`:* **1,604,400 reads**, the same number on every one of five runs — predicted
     before running as 400 × 20 × 200. *Green patched:* §1.3.
   - *What the bound allows.* One read per position to build the index, one for the absent pass, and
     room for one more pass of that kind. It does not grow with the window, which is the point: a
     second prior run must cost nothing.
   - *Cost.* Milliseconds, on both sides.
2. The test also asserts `RunsRecorded == runs` and every scenario's `RunsSeen == runs`, so an
   analysis that reads nothing because it found nothing is not green.

**This replaces the test this plan first proposed, which the verification round showed to be
wrong** (§1.3): a ratio of two timings — `Analyse` at 5,000 scenarios against 1,250, fastest of
three each, under 6×. Alone in a process it read 10.8 on `main` and 2.0–2.2 patched. Beside other
tests it read 5.90–13.05 on `main` and 4.02–7.65 patched: the two ranges overlap, the fixed analyzer
failed once and the broken one passed twice. The issue asks for a test that "times `Analyse`
itself"; what it needs is a test that fails when the lookup is a scan, and this one does, every
time, on any machine.
3. **A roster line with a repeated (id, slot)** — first position wins. New, small, pins the `TryAdd`.
4. Every existing analyzer test, unchanged: aliases (`HistoryAnalyzerTests`, two), slots, a roster
   missing from the ledger (`positions is null` → skipped, as `priorRoster is null` was).

---

## 3. S2 — call sets, when asked for

### 3.1 What is wrong

Both `HistoryPoint` constructions end in `Resolve(run.CallSetAt(at), shapes)`: a LINQ chain and a new
`string[]` per point, for every scenario, every run. At 5,000 × 51 that is 255,000 arrays. They are
read at `:382` (alternating, only if `changed`) and `:406` (behaviour-changed), in both cases for
`currentPoint` and `previousShaped` and no other point. On a healthy suite that is a handful of
scenarios.

### 3.2 The change

`AnalyseScenario` keeps `sources`, a `List<(int Run, int At)>` parallel to `points`, and a local
function:

```csharp
IReadOnlyList<string>? CallSetOf(HistoryPoint point)   // currentPoint, or one of points by reference
```

The two read sites call it. `HistoryPoint.CallSet` is removed: a field that is null except on the two
points of a changed scenario would be a trap for the next reader, and the names it produced are
already on `ScenarioHistory.NewCalls` / `GoneCalls`, which is what every surface uses.

Found by reference (`FindIndex(ReferenceEquals)`), not `IndexOf`: `HistoryPoint` is a record, so
`IndexOf` compares every field of every point, and what is wanted is the identity of one. A ledger
holding the same run id twice would also make two points equal; the lookup should not care.

### 3.3 Tests (red first)

1. S1's read count does not go red for this; S2 is a constant factor. Its pin is
   **allocation, which is deterministic**: `GC.GetAllocatedBytesForCurrentThread()` around one
   `Analyse` of `RealisticLedger(300)` where no scenario's set changed, asserted **under 26 KB per
   scenario** — `An_analysis_does_not_spell_out_the_calls_of_every_point`, **built** (§1.3): 33.4 KB
   on `main` (±0.1% inside a parallel process), 19,254 B patched, to the byte, five runs of five.
   (The harness ledger of §1.2 reads 41.4 and 25.5 KB: its call sets are five calls with repeats,
   the test's are distinct.) S1 alone was not measured separately; it adds one dictionary entry per
   roster position (COMPUTED: tens of bytes per scenario), so the test is red after S1 and green
   after S2, which is the order they land in. It is a bound on a 50-run window: it moves with the
   window and with what a `HistoryPoint` holds — a plan that adds a field to `HistoryPoint` re-pins
   it, knowingly.
2. `HistoryShapesTests`' evidence tests (new/gone calls named on a behaviour change and on an
   alternation) unchanged and green — they were in the 5,149.
3. A changed scenario whose previous shaped point is *not* the previous run (a failed run with no
   shape between them): the names come from the right run. Pins `sources`.

---

## 4. S3 — the budget, restated

`CROSS_RUN_HISTORY_PLAN.md` §7.7 budgets "under 150 ms for 5,000 scenarios over a 50-run window
(measured 65 ms)". Both numbers were the prototype harness on results-only lines.

| | §2.6 said | Shipped, realistic lines (15 MB) |
|---|---|---|
| Read + parse | 64.5 ms | 190–280 ms |
| Analyse | 0.8 ms | 2,060 ms on `main`; ~310 with S1; ~200 with S1 + S2 |
| Total | 65.3 ms | ~2.3 s on `main`; **~450 ms after this plan** |

Restated budget, to be written into §7.7: **under 600 ms for read, parse and analyse at 5,000 × 50 on
lines that carry what a run writes; the analysis linear in scenarios × window.** The reason to give:
a real run line is more than four times the results-only line the budget was set on (15 MB against
3.34 MB for the same window: durations, fingerprints, counts, and call sets since 3.17.0), reading
follows the bytes, and half a second at the end of a 5,000-scenario run is
still nothing. The pins are the roster-read count (S1), the allocation bound (S2) and the existing
`LinesParsed ≤ window`. **No wall-clock assertion guards the budget**, and §1.3 is why: the number
is a statement of what was measured, the counters are what keep it true.

Edits, all in `CROSS_RUN_HISTORY_PLAN.md`:

- **§2.6** — a dated note under the table: the figures are the prototype's; the shipped analyzer
  measured 2,060 ms (#91); the "parsing dominates, the arithmetic is 0.8 ms" bullet was true of the
  prototype only.
- **§7.7** — the restated budget, and `HistoryStats` named as it shipped (it has no
  `ScenariosAnalysed`).
- **§17.1b** — the row "Analyzer cost is 65 ms at 5,000 × 50 — **RUN**" becomes **RUN — of the
  prototype harness, not the analyzer**, with the real figure; and §17.0 gains the row this is (§7.0
  below).
- The comment on `Ledger_for_5000_scenarios_over_50_runs_stays_under_the_budget` ("measured at 65 ms;
  budgeted at 150 ms") is corrected to say what it times: `Read`, on results-only lines.

**Not taken: a cheaper `Read`.** By the issue's rows ~10.8 of the 15 MB is shape data. In the
generator `shapeSet` and `shapeOrdered` are the same string written twice per position per run; how
often that holds in a real ledger is not measured. Either way it is a ledger-format question (elide
`shapeOrdered` when equal; intern fingerprints) with a format
version and a fold behind it. It deserves its own issue and measurement; nothing here depends on it.

---

## 5. What the other two plans take from this

Written here because their files are not edited from this plan.

| Plan | What | Why |
|---|---|---|
| `HISTORY_VERDICT_NOISE_PLAN.md` §6.2 and §10.1b | "Pace costs nothing noticeable — 250k divisions against a measured 65 ms analysis" | The 65 ms is the prototype's. Against ~200–310 ms the conclusion survives, but the row should cite this plan, and its "to be re-measured" now has a harness: `RealisticLedger` |
| the same, S3 | S3 edits the `points` loop and adds a `comparable` filter in `AnalyseScenario` | Same lines as S1 and S2. **This plan lands first**; S3 rebases onto `priorPositions` and `CallSetOf` — mechanical. Its prototype patch was cut from `2491185e` and will need the one hunk redone |
| `EVIDENCE_SURVIVES_A_RERUN_PLAN.md` §11.4 | "it should be settled before any slice here adds analyzer work" | This is that. Its S3/F4 (cut `prior` at the current run) edits `AnalyseStream`'s `prior` list, above `PositionsOf`; no conflict |
| the same, S2 | `FlakyShortfall` is per-scenario work | Linear, fine. Nothing here would notice if it were not — the read count guards the roster lookup only; time it with `big.cs` when it is built |

---

## 6. Versioning, docs, parity

- **S1: patch.** Performance work, nothing new to call (CLAUDE.md).
- **S2: patch, with the removal named in the changelog.** Removing `HistoryPoint.CallSet` is by the
  letter a breaking change to a public member. It is taken on the standing permission above: the
  member is from 3.17.0, nothing reads it, and MAJOR is not bumped without asking. **Decided (user,
  2026-09-19): S1 and S2 ship together as one patch release, the removal named in the changelog**
  with which part of the version moved and why. (The alternative considered: keep the member,
  `[Obsolete]` and always null — the trap §3.2 describes.)
- **S3: no bump** — plan files and a test comment.
- Changelog for S1: "History analysis was quadratic in the number of scenarios: about 2 s at 5,000
  scenarios, on every run with history on. It is linear now (about 0.3 s)."
- Wiki: nothing to change. Grepped `../Kronikol.wiki` for `CallSet`, `HistoryPoint`, `IndexOf` and
  both budget figures: one hit, `Cross-Run-History.md:461`, which is the ledger's `callSets` field
  and unaffected. `README.md` has none; `CHANGELOG.md:374` is the same ledger field.
- Kronikol4J has no history (`HISTORY_VERDICT_NOISE_PLAN.md` §1, RUN by grep); no parity work.

---

## 7. Assumption ledger

In the form of `CROSS_RUN_HISTORY_PLAN.md` §17.

### 7.0 The error class, and the rule that catches it

This issue *is* the history plan's error class, in the history plan's own ledger:

| Verified | Then claimed, unverified | Actually |
|---|---|---|
| A C# harness analysed 5,000 × 50 in 0.8 ms (`tools/history-bench`, §2.6 — true) | "Analyzer cost is 65 ms at 5,000 × 50 — **RUN**" (§17.1b), and a 150 ms budget "asserted on a generated fixture" | the harness was the prototype; the shipped analyzer was never timed. The fixture test times `Read` alone, on lines with a sixth of the bytes. Shipped: 2,060 ms |

A RUN mark on a claim about a thing that did not exist yet. The rule is the same one — *what would I
have seen if it were false, and did I look there?* "There" was one `Stopwatch` around the real
`Analyse`.

This plan's first draft made the mistake itself, and its verification round nearly made the noise
plan's row five again:

| Verified | Then claimed, unverified | Actually |
|---|---|---|
| The ratio test is red on `main` (10.8) and green with S1 (2.0–2.2) — true, of a test run alone in a process, eight times | it is the guard the issue asks for, "so this cannot come back unnoticed" | in the suite it lives in, the fixed analyzer read up to 7.65 and the broken one as low as 5.90, under a threshold of 6. A test that was seen red and seen green, and would have gone red on a good analyzer and green on a bad one. The draft also drew a wrong conclusion on the way — "the pin has to be a time" — from a true premise (a lookup counter cannot see it). What would I have seen if it were false? Two timings that disagree when something else is running; the place to look was the suite, pinned |
| BreakfastProvider's CI ledger replays byte-identical through `main` and the patch — 394 runs, 272 MB | S1 is accepted on a real ledger | true and nearly empty **for S1**: 18 rosters for 18 suites, so no lookup ever misses, no roster changes, no id repeats. It does accept S2 (39 scenarios name their calls). The churn ledger and its two controls are what accept S1 |

And one of this plan's own, caught before it was written down: after S1 the first instinct was
"`Analyse` is fixed, the budget is met". It was 310 ms against 150, and `Read` alone was over. The
fix for the cause is not the fix for the issue's first done-when; hence S3.

### 7.1a Existence and shape

| Claim | How verified |
|---|---|
| The lookup is `priorRoster.IndexOf(candidate, slot)` inside the per-run loop | `HistoryAnalyzer.cs:182–196` read |
| `IndexOf` is a linear scan | `HistoryModel.cs:102–108` read |
| `HistoryRoster` is a `sealed record` | `HistoryModel.cs:27` read |
| `AnalyseScenario` uses `priorRosters` for nothing but the lookup | the prototype compiles with the parameter replaced |
| `CallSet` has two reads and no other | grep of all `.cs`, repository-wide |
| The tool's JSON does not emit `CallSet` | `QueryCommand.History.cs:445` read: an anonymous projection of named fields |
| `HistoryStats` has no `ScenariosAnalysed`, contrary to §7.7 | `HistoryLedger.cs:19` read |
| The existing perf test times `Read` only, results-only lines, budget 1,500 ms | `HistoryLedgerTests.cs:591–628` read |
| The patch applies to `main` | `git apply --check` against `125a32cc`'s working tree |

### 7.1b Behaviour

| Claim | Depth | How verified |
|---|---|---|
| ~2 s on `main` at 5,000 × 50 | **RUN** | twelve calls over two rounds |
| Quadratic | **RUN** | four sizes |
| The lookup is ~90% | **RUN** | the lookups alone, outside the analyzer |
| S1 → ~310 ms, linear | **RUN** | four sizes |
| S2 → ~200 ms | **RUN** | same-load A/B; and independently by stubbing `Resolve` |
| S1 + S2 change nothing the analyzer returns | **RUN** | 5,149 tests, 0 failed; and the replay (§1.2): 394 real runs and 70 churn runs, 1.5 million points, every field of `HistoryVerdicts`, byte-identical, with two controls that do differ |
| The first holder of a repeated `(id, slot)` wins, as `IndexOf` had it | **RUN** | the every-slot-0 ledger: `TryAdd` matches `main`, the indexer does not |
| The tool pays 6.9 s, the patch makes it 1.0 s | **RUN** | three commands, fastest of five process runs each, against a 0.15 s floor |
| The tool's multiplier is `QuickJitForLoops=false` | **RUN** for the effect (6.9 → 3.6 s with it flipped), **inferred** for the mechanism (no profile, no devirtualization) |
| S2 takes allocation from 41.4 to 25.5 KB per scenario | **RUN** | deterministic: identical to the byte over three readings, at two sizes |
| ~~A ratio of two timings separates broken from fixed~~ | **RUN — and false** | alone in a process, 10.8 against 2.0–2.2; warm and in company, 5.90–13.05 against 4.02–7.65 (§1.3). Replaced |
| The read count and the allocation bound separate them, every time | **RUN** | 1,604,400 against 800, and 33.4 KB against 19,254 B, ten parallel runs of which six pinned to two CPUs; both predicted before running |
| `merge --history` pays it too | **READ** | `MergeCommand.cs:207`, same entry point; needs shard directories, not timed |
| `Read` follows bytes | **ISSUE** | three rows |

### 7.2 Not verified — and what this plan does about each

| Assumption | Status | How the plan avoids depending on it |
|---|---|---|
| ~~The guards behave on a GitHub runner as they do here~~ | **Closed (RUN, §1.3, PR #92)**: red on the tests-only commit with 1,604,400 reads and 33,508 B, green on the fix, `ubuntu-latest` | The bound (26,624 B) sits 38% above the patched figure and 20% below `main`'s, on both systems |
| The allocation bound survives a runtime update | **Unknown** | The message prints the figure, so a drift is read, not guessed at |
| The residual ~200 ms is allocation (`HistoryPoint`s, `ToList` copies) | **Guessed, not profiled** | Nothing is built on it. §7.3 item 2 |
| ~~Real suites share one roster across the window~~ | **Closed (RUN, §1.2)**: true of the CI ledger (18 rosters, 18 suites); and the churn ledger, 70 rosters in 70 runs, analyses in 987 ms against `main`'s 1,902 | — |
| No consumer reads `HistoryPoint.CallSet` | **Cannot be known**; the permission covers it | Changelog names it (decided, §6) |
| ~~The wiki says nothing about either~~ | **Closed (RUN)**: grepped, §6 | — |
| ~~The S2 allocation test~~ | **Closed (RUN, §1.3)**: built, red on `main`, green patched | — |
| What a real test run pays at its end (default tiering, one cold call, inside the test host) | **Not measured through a real run** — the harness's cold first call is ~2 s on `main` | Nothing is built on it; F5 is about the tool, where it was measured |
| ~~The per-framework and Playwright suites~~ | **Closed (RUN, PR #92)**: all 28 CI jobs green on the fix commit | — |

### 7.3 Open investigations

1. **~~A real ledger, before and after.~~ CLOSED (RUN, §1.2)** — identical to the byte, and the
   churn ledger with it. The harness (`replay.cs`, a file-based app over `Kronikol.csproj`, 60 lines)
   is what the noise plan's M0 asks for. It is saved with the generators and the timing harnesses in
   `HISTORY_ANALYZER_COST_PLAN.harness/` (copy a `.cs` to the repository root and
   `dotnet run -c Release <file>.cs -- <args>`); it belongs under `tools/` once a plan that needs it
   lands.
2. **Profile the residual.** One `dotnet-trace` or allocation profile of the S1 + S2 analyzer at
   10,000 × 50. If `HistoryPoint` construction dominates, the next lever is analysing from the run
   columns directly and materialising `Points` for the scenarios a surface asks about — a bigger
   change, its own plan, and only if somebody has a 10,000-scenario suite.
3. **~~The tool path, timed.~~ CLOSED (RUN, §1.2)** — and it found F5. Left over: `merge --history`.
4. **The `Read` issue** (§4): file it with the byte breakdown by field.

---

## 8. Execution log

**2026-09-21 - S1, S2 and S3 executed as 3.25.2** (patch), on the owner's word ("Ok, so what do we need to do
the History analyser plan next and then do anything that comes out of that?", then "continue"). `main` had moved a long way since the plan
was written: `HISTORY_VERDICT_NOISE_PLAN.md` landed first as 3.22.2 to 3.25.0 and #95 as 3.25.1, so
the prototype patch no longer applied (`git apply --check`: both source hunks failed) and the change
was made again by hand on today's loop. What §5 says the noise plan "rebases onto" happened the other
way round.

### 8.1 Where execution departed from the plan

| Plan said | What happened | Why |
|---|---|---|
| F2: "Nothing outside the analyzer reads `CallSet`: not the tool" | **The tool did, by then.** 3.25.0's `kronikol query history sN --calls` printed `entry.Points[^1].CallSet`. The member still goes, as decided; `--calls` reads the run's own line instead (`ReportHistory.CallsOf`: `run.CallSetAt(position)` through `HistoryShapes.At`, the position found by reference in `verdicts.Scenarios`) | A claim that was RUN on 2026-09-19 and stopped being true two days later, by this session's own hand. The grep was repeated before the member was touched, which is the only reason it was caught before the compiler caught it |
| - | **A defect found on the way and fixed:** `--calls` on the scenario that collects unattributed traffic (`N`) answered "no call list for this run: it needs the History.run.json" with the file beside the report, because the analyzer gave that point no call list. Red first (`Calls_are_printed_for_the_scenario_that_collects_the_traffic_no_test_was_given`) | Reading the run's own line fixes it for free |
| `CallSet` "read in two places, both only for `currentPoint` and `previousShaped`" | Three places: the noise plan's S3 added the alternating evidence that names the OTHER set, read from a third point (`other`, the last run in the memory holding a different set). `CallSetOf(point)` serves any point by reference, so nothing else was needed | - |
| Test 1: 400 scenarios, 20 runs, results only: 1,604,400 reads on `main`, 800 patched | **The runs carry durations**, so that the noise plan's `RunPaces` pass over each distinct roster is under the count too: **1,604,800 before, 1,200 after** (one pass to index, one to pace, one for the absent scenarios), both predicted before running. Bound unchanged at 4 x scenarios; the pace pass took the "room for one more pass" | The newest code in that loop is the likeliest place for the scan to come back |
| The ledger is built through the internal constructor | Since 3.25.1 an analysis reads `PriorRuns` from the ledger's index of run lines, which a ledger built from rosters and runs alone does not have: the prototype's test would have read 0 prior runs and failed its own `RunsRecorded` assertion. The test builds the `HistorySuiteLines` too (`InMemory`) | The second assertion of §2.3 earned its place |
| Allocation bound 26 KB: 33.4 KB on `main`, 19,254 B patched | **Re-pinned, knowingly, as §3.3 said a plan adding fields to the point would have to: 36,910 B before, 21,354 B after, bound 28 KB** (34% above the one, 22% below the other). The point gained five members in 3.23.0 to 3.25.0. Pre-sizing `points` and `sources` to the window took 22,730 to 21,354 | - |
| "S4 keys its usuals by this plan's per-roster position map" (the noise plan, §6.2) | **Two maps, on purpose.** This plan's is `(id, slot) -> position` per distinct roster, by the id the roster recorded, tried alias by alias in order. `RunPaces`' is `position -> scenario` under alias resolution, for full runs that carry durations only. Deriving one from the other changes what a roster with a repeated `(id, slot)` reads: the index holds the first holder, and pace reads every position | The every-slot-0 ledger is exactly that case, and nothing may move |
| Harness: copy `replay.cs`, `big.cs`, `alloc.cs` to the repository root | **`tools/history-replay` does all three now**: `--full` (everything the analysis returned, one JSON line per run, `CallSet` left out as `replay.cs` had it), `--aliases`, and `--time <ledger>...` (read, five analyses, bytes allocated by a warm one). The files in `HISTORY_ANALYZER_COST_PLAN.harness/` stay as the record of the first two rounds; the generators are still the generators | §7.3 item 1 said it belongs under `tools/` once a plan that needs it lands |

### 8.2 Measured (Release, Windows 11, .NET 10, base = `d958403f` in a detached worktree)

**Replay, everything the analysis returns, SHA-256 of the output:**

| Ledger | Runs | Output | 3.25.1 = 3.25.2? | Analyzer time |
|---|---|---|---|---|
| BreakfastProvider CI (`origin/kronikol-history`, 18 suites) | 430 | 426 MB | **same** | 1,930 -> 1,202 ms |
| Churn, 60 renames | 70 | 213 MB | **same** | 2,444 -> 1,583 ms |
| Churn without its alias file | 70 | 209 MB | **same** (and different from the row above: the renames are reached) | 2,184 -> 1,349 ms |
| Churn, every slot written 0 | 70 | 224 MB | **same** | 2,397 -> 1,496 ms |
| The noise plan's chain and contended ledgers | 17, 15 | 11 MB, 9 MB | **same** | 214 -> 100, 173 -> 93 ms |
| Control: every slot 0, `TryAdd` swapped for the indexer | 70 | | **different** (`a1073fb4` against `3864475a`) | |

**`--time`, two interleaved rounds, five analyses each:**

| Scenarios | 3.25.1 | 3.25.2 | B per scenario |
|---|---|---|---|
| 1,250 | 183-208 ms | 84-107 ms | 36,894 -> 21,235 |
| 2,500 | 531-659 ms | 120-296 ms | 36,886 -> 21,225 |
| 5,000 | 1,914-2,051 ms | 179-246 ms | 36,880 -> 21,221 |
| 10,000 | 7,167-7,417 ms | 379-490 ms | 36,891 -> 21,226 |

Read alone: 190-220 ms at 5,000 (15.6 MB), 330-410 ms at 10,000.

**Through the tool, fastest of five process runs, a 5,000-scenario report beside the 5,000 x 60 ledger:**

| Command | 3.25.1 | 3.25.2 |
|---|---|---|
| `query history` | 6,825 ms | 1,142 ms |
| `history gate` | 6,905 ms | 1,116 ms |
| `query failures` (20 failures, its `history:` line) | 7,094 ms | 1,137 ms |
| `query summary` (the floor) | 197 ms | 193 ms |
| `query history`, `DOTNET_TC_QuickJitForLoops=1` | 3,512 ms | 1,257 ms |

F5 stands as written: the setting doubled this one loop and wins again without it.

**Pins:** the three characterization tests (`The_first_holder_of_a_repeated_id_and_slot...`, and the
two `The_calls_are_named_from...` in `HistoryShapesTests`, which are §3.3 test 3 in two halves: a failed
run with no fingerprint between, and a scenario second in the earlier roster, first in this one and
absent from the run between) pass before and after, and **all three fail under mutation** (`TryAdd` ->
indexer; `sources.Add((r, position))`; `sources[^1]`), checked in one build.

**Suites at 3.25.2:** unit 5,277 passed / 0 failed (5,271 + 6 new); SearchEngine 210; TcpTap 258 (4
skipped); PlantUml.Ikvm 49; Playwright E2E 778 passed / 28 skipped. Whole solution builds, so nothing
else read the member.

### 8.3 S3

`CROSS_RUN_HISTORY_PLAN.md` §2.6 (a dated correction under the table), §7.7 (the restated budget with
the measured table and the three counts that hold it), §17.0 (the row, and one sentence saying a
sixteenth came after shipping) and §17.1b (the row struck and re-marked), and the comment on
`Ledger_for_5000_scenarios_over_50_runs_stays_under_the_budget`. The budget as restated: **under 600 ms
for read, parse and analyse at 5,000 x 50 on lines that carry what a run writes; about 400 ms
measured.**
