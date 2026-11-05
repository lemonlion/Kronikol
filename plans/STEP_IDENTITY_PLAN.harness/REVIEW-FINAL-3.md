# Third final review: the second review's fixes

A narrow independent review of `git diff c0359e8f 391c57d3 -- src`, in the reviewer's words. What was done about each finding is at the end. Its probes and readings are in `review-probes/final-3/`.

I found three regressions and one shape fix 3 still misses, each measured against the pre-fix code.

**How I measured:**

- **Before and after:** each shape ran on `cd191d56`, then again with the six code files of `c0359e8f..391c57d3` put back. The seventh file, `DiagnosticEntry.cs`, only changes a comment.
- **BDDfy shapes:** these ran on the real engine, by running the `Engine.BDDfy` fixture directly and reading it through `ReportRows`.
- **Reqnroll shapes:** these go through the adapter's `ToFeatures`, not a real Reqnroll run.
- **Not run:** the repo's own `Kronikol.Tests` and `Kronikol.Tests.RealEngines` facts, and the LightBDD lane. LightBDD's use of `InOrderTheyRan` is covered only by the fuzz below.

## Regressions

**1. Fix 2: `Except` removes rows that read alike, even with nothing nested.**

- **Source:** `src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs:146`. `Except` returns distinct items and `ReqNRollStepInfo` is a record, so two equal rows become one and later rows pair one place early.
- **When rows are equal:** same keyword, text, status, duration and other fields, for example hand-built rows with no duration.
- **Reach:** `ReqNRollScenarioCollector.Collect` is public, so a consumer can hit this.
- **Probes:** U1, U1b and U1c. All pass on the old code and fail on the new.

| Probe | Shape | Reads | Should read |
|---|---|---|---|
| U1c | No frames: `When I click next`, `When I click next`, `Then I am on the last page` | 2 rows | 3 rows |
| U1 | Same-text rows over frames | `Then it shows` holds `second refresh`; `shown` is lost | Each row keeps its own sub-step |
| U1b | One real row, then two masked `When ***` rows | `Then done` holds `secret two.txt`; `done.txt` is lost | `Then done` holds `done.txt` |

**2. Fix 5: a file on a frame with no row at its position is lost.**

- **Source:** `src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs:108`. It returns `own`, while `MapSteps` only copies `collected[i]` for `i < steps.Count`. The old code duplicated some files but kept every one.

| Shape | Old `all files` | New `all files` |
|---|---|---|
| RV1: masked row, file in a tear down | `rv1-teardown-file` | none |
| RV1b: HS2's shape with a hidden `Setup` ahead of it | `rv1b-rebuilt-row-file` | none |
| RV3: masked row, file in a later example row | `rv3-row-alice`, `rv3-row-bob` | `rv3-row-alice` |
| RV2: `EstablishContext` and `Setup`, the second attaches then throws (no consumer rebuild) | `rv2-context-file` twice, `rv2-setup-file` | `rv2-context-file` |

In RV2 the lost file belongs to the step that threw, and `Failures.md` no longer lists it either.

**3. Fix 1: a consumer's executor around this one that doesn't hand BDDfy the task leaves the frame open.**

- **Source:** `src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs:85`. It assumes `AsyncTestRunner` receives what it returns. This needs such an executor, so it does not affect the default setup.

| Shape | Old | New |
|---|---|---|
| RV6: time limit on a step that hangs | `1.0 Inner one`, `rv6-file` on the scenario | No sub-step, file gone |
| RV6b: a Then over the limit, then another Then | `2.0 The order is read back`, `2 /orders/1`, file kept | No sub-step, `1 /orders/1`, file gone |
| RV5: executor drops the task | `2 Then the order is confirmed`, `2 /orders/1` | `1.0 Then the order is confirmed`, `1 /orders/1` |

## A shape fix 3 still misses (not a regression)

**RV10:** `Given, When one, Then one fails, When two, Then two`. Old and new read the same.

- **Reads:** `1 When one`, `2 When two [Skipped]`, `3 Then one fails [Failed]`, `4 Then two [SkippedAfterFailure]`, with `3 /t1`.
- **Should read, by OR2's rule:** `Then one fails`, then `When two [SkippedAfterFailure]`, then `Then two [SkippedAfterFailure]`.
- **Cause:** `src/Kronikol/Tracking/StepPairing.cs:33` only compares frames that ran. They ascend here, so a list sorted by kind counts as in run order.

## No fault found

**Fix 1, default path:**

- `Task<T>`, suspending or not: RV4, RV4b.
- A step that never suspends, or throws without suspending: RV12, RV13.
- `NotImplementedException` and a cancelled step: RV14, RV15, with the same message as before.
- `Task.WhenAll`, an example table, a retry: RV16, RV9, RV25.
- A story run inside an async step: RV8.
- An outer executor that waits itself and inner ones that block: RV7, RV23, RV24.
- A close racing the next step: 12 repeat runs of the fixture read identically.
- `Configurator.AsyncVoidSupportEnabled` and `ExecuteStep` read the same in BDDfy 8.0.1.3, 8.0.11 and 8.0.12.
- The only side effect I saw: failures in async steps gain one `CloseWhenDone` line in `ErrorStackTrace`. It does not appear in `Failures.md`.

**Fix 3:**

- A fuzz of 300,000 random trees found every native listed once, top-level step methods once, and rows that ran in run order.
- Its differences from the old code were in hoisted frames, out-of-order lists and rows waiting for an id. I read a sample from each kind, not all of them.
- A frame listed twice or nested passes (U5a to U5d). The old code fails U5b and U5d.
- One leftover, not a regression: a row with a `NotRunAfter` id can stand too late when a frame under a step method comes before it in the list. On one traced sample the old code misplaces it too, by a different route.

**Fix 4:** a stamped response with an unstamped request, rows in another order than their ids, and a parent's call after its sub-step (U4a to U4c) all read correctly.

**Fix 5, HS1:** `Failures.md` now lists its two files once each instead of twice, and nothing is lost.

**Fix 6:** unmarked rows, including `Skipped` ones, extract as before (U6). The marks are internal, only the adapters set them, and the ingest path sets none.

## What was done

- **1:** `MapStepsByPosition` partitions by the rule itself, so a row is taken out only for being nested. `Rows_that_read_alike_*` in `ReqNRollStepPairingTests`.
- **2:** by position, the files of a frame past the last row go on the scenario. `HS4` on the real engine.
- **3:** a step still waiting on its task when the next step starts is over, unless the next step runs in its flow (a story run inside it), and one still waiting when the report reads the scenario closes then, keeping what it opened. Five facts in `BDDfyAsyncFrameTests`.
- **RV10:** a step that did not run between two that did says the list is not in run order. `OR3` on the real engine.
- **The leftover:** stated in the plan's section 9, not fixed.

Each fact was run red first (`results/execution/red-third-review-*`), and M73 to M83 are the mutants for these rules. The reviewer's own shapes run on the fixes are `review-probes/final-3/fix3-rows.txt`: RV1, RV1b, RV2, RV3, RV5, RV6, RV6b and RV10 each read as the table above says they should, and no other shape moved.
