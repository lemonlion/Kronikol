# Review of 8aba55f6: a BDDfy async step stays open until its task ends

(The reviewer's answer, written here by the session that asked: the harness refused the reviewer's own write of this file.)

## Result

The change does what it claims. The #224 phase fault and the step-closing half of #122 are gone on every BDDfy overload tried, on BDDfy 8.0.1.3, 8.0.11 and 8.0.12. All 103 `shapes` tests, and every test in the other four probes, keep their xUnit outcome and message against 5.28.2.

One fault should stop publication (F1, with F2 from the same cause). An attachment written after a step's first `await` now goes on the step, and the report reads collected steps only by their index in the reported list. 5.28.2 kept those attachments on the scenario, where they could not be lost. 13 of the 52 attachment files 5.28.2 copies in the suite are now absent. A trial fix holds on all of these shapes except a woven step that outlives the test (L10).

## How it was measured

Labels: `5.28.2` (nuget.org), `v0` = `c1837cdc` to separate this change from the phase rule, `v1` = `8aba55f6`, `v2` = `8aba55f6` plus a trial fix (`fix-v2.patch`). Probes: `shapes` 103 tests, `executors`, `parallel` 48 tests on one static client, `asserts` Shouldly with the assertion weaver, `host` an in-process `TestServer`. SDK 10.0.300, Windows 11, xunit.v3 3.2.2, BDDfy 8.0.1.3 unless stated. Reports read with `kronikol query`.

## Findings

### F1. An attachment after the first await is lost from the report (measured, high)

`public async Task TearDownA() { await Task.Yield(); Track.Attachment(path, "t1-td"); }` on `.TearDownWith(x => x.TearDownA())`.

Cause: `AddAttachment` uses the open step, or the scenario if none is open. `ScenarioInfoExtensions.MapSteps` reads `collectedSteps[i]` only for `i < reportedSteps.Count`, and drops the rest.

`kronikol query attachments`: T1 async `TearDownWith` 1 / 0 / 1 (5.28.2 / this change / trial fix); T2 the same on a failing run 1 / 0, gone from `Failures.md` / 1; At3 `WithExamples` 3 rows 6 / 2 / 6; At4 two stories in one test 4 / 2 / 4; At5 the second fails 3 / 2 / 3; N2 by name async `Setup` and `TearDown` 5 / 3 on the wrong steps / 5; N3 by name sync `Setup` 3 / 2 on the wrong steps / 3; L10 woven step outlives the test 1 / 0 / 0. Attachments folder 52 files / 39.

Plain fluent, by-name and attribute stories with no `Setup` keep every file, moved from the scenario to the right step. Sync steps already lose attachments this way on 5.28.2 (Y1, Y2, Y3, T5).

Fix, tried (`fix-v2.patch`, 44 added, 14 removed): the executor passes BDDfy's `step.Id` as an owner, `MapSteps` pairs by that id, any collected step no reported step claims gives its attachments to the scenario. Run on BDDfy 8.0.1.3 and 8.0.12. Repairs the sync losses too. Limits: repository suite not run on it; `BDDfyStepInfo`'s record equality now includes the id; S1 and S2 stop showing sub-steps they never owned; L10 untouched. Stopgap, not tried: `step.ShouldReport` in the wrap condition saves tear downs only.

Test gap: `BDDfyAsyncStepTests` stops at the collector with a fake inner executor, so it never reaches `MapSteps`. A fact must go through `.BDDfy()` and `ToFeatures`.

### F2. One step's contents show on another, and the real failing assertion goes (measured, high for by-name stories with a `Setup`)

Same cause. A `Setup` method runs first with `ShouldReport = false`, so every index is off by one. A by-name story with `Setup`, `GivenX`, `WhenX` (soft failure) and `ThenX` (the real failure, thrown): 5.28.2 reads Given, When, Then [Failed]; this change reads `When` with `A: given`, `Then [Failed]` with `A: soft failure in when`, and the failing assertion is nowhere, while `query failures`' error line still reads "the real failure". Same cause: E4, an executor that retries a step. Fix, tried: the same patch.

### F3. A woven step left running takes its parent's completion (measured, medium)

`CloseWhenDone` closes the top of the whole test's stack, and a sub-step still running is on top. L8: `When` starts a woven step and does not await it, `Then` awaits it: 5.28.2 `When [i4-i6] 2 calls`, "4 calls belong to no step (attribution was not trusted)"; this change `When [i4-i14] 6 calls`, the `Then`'s calls given to `When`, `Then` a sub-step of `When`, no doubt expressed. L2, L3: left running after the first await: "6 calls belong to no step" / `Given [i0-i10] 6 calls`. L6 `Task.WhenAny` leaves it running: "4 calls belong to no step" / `When [i4-i10] 4 calls`. L7 `WaitAsync` times out, step fails: `Woven slow [Passed]` / `Woven slow [Failed]`, `query failures` adds it though it went on to finish, tear down a sub-step of the failed `When`. R5 step fails, what it left is running: `Woven gated [Passed]` / `[Failed]`, the collector says `When [Passed]`. L4, L10 it outlives the test: `Given` closed and kept / collector empty, `Given` never closes, its sub-steps and attachments go.

`AttributeInteractionsToSteps` pairs bars with steps by position and checks the text: 5.28.2's extra bar made it refuse; here nothing disagrees, so the report states a wrong answer. 5.28.2 is not right here either (L1 already attributed this way). Fix, not tried: close by identity, not by stack top: `CloseWhenDone` captures its own step, `CompleteStepAsync` takes the node `LeaveFlow` finds, `isTopLevel` asks the flow. Smaller fix, not tried: in `AttributeInteractionsToSteps`, when fewer bars arrived than steps ran, give later calls no step and keep the diagnostic.

### F4. Two stories at once in one test collapse into one tree (measured, low)

5.28.2 mostly flat, five or six bars; this change `Given a-g` holds the other five, one or two bars. Two sync stories at once nest the same way on 5.28.2. Fix the same as F3's.

### F5. A consumer's own step executor (measured, low)

E3 outer executor drops the task: 4 bars flat / 1 bar, nested. E2 outer executor gives a step a time limit with `t.Wait(50)`: 4 bars / 2 bars, tear down a sub-step of the timed-out `When`. E1 outer executor reads the task's type: `Task<int>` / plain `Task`, so `r is Task<int>` is now false. I0 to I3, E5 hold. Fix: F3's for E2 and E3; for E1 a reflective generic wrapper, not tried.

### F6. On an older `Kronikol.StepTracking`, a `SkipIf` closes the step early (measured, low)

On the 5.28.2 and 5.14.0 weavers the bypass path's `ret` also gets `CompleteStepAsync`, so it pops a second step, now the BDDfy step. This commit's own weaver holds. No worse than 5.28.2. Fix: ship with #226's weaver, or say all packages move together.

### F7. Changes the rule implies, which want a changelog line (measured, by design)

O2 `TrackDuringSetup = false`, woven `Given` under async `When`: 2 calls ignored / recorded as Action. O3 `TrackDuringAction = false`, woven `When` under async `Given`: ignored / recorded as Setup. O4 `TrackDuringAction = false`, woven `Given` under async `When`: recorded / ignored. O5 `TrackDuringSetup = false`, woven `When` under async `Given`: recorded / ignored. W1 thin async step over woven sub-steps: 8 bars / 3 bars, as a sync step has always had. M15 hand-made step with table and doc string after the first await: drawn in its bar / no bar, stored nowhere else, so gone. M8 background task attaches and asserts during a later async step: scenario attachment, assertion dropped / both on that later step. H10 `HasActiveStep` and `GetSteps` inside an async step: false, 2 / true, 1. Only M15 loses data with no gain.

### F8. An async step's duration still reads the part before its first await (measured, low, not caused by this change)

D1, a When of about 700 ms: `When slow <5ms` and a scenario under 50 ms on both versions, because BDDfy's stopwatch stops when the task returns. New: the sub-step is visible, so `Given Woven slow ~400ms` sits under a parent of under 5 ms. `query scenarios` says 0.02 s, so the "Slowest" list and `--slower-than` miss it. Fix, not tried: `MapSteps` could use the collected duration.

## What the change claims, checked (measured)

#224 woven `Given` after the first await (A01, A02, A03, M4, R3, H3, H4): bar, top-level, rest reads Setup / sub-step, rest reads Action. #122 assertion after the first await (At1, A17, `asserts` S1 to S4): dropped / under the step. #122 async failure read Passed (A19b, A16b, A10): `[Passed]` or absent / `[Failed]` with its message. Attribution: "6 calls belong to no step" / every call on its step. Flag off closes at first await (O1): same. Sync and async void as they were (A05, Y1 to Y3, M6): identical to `v0`.

## Shapes that held (measured)

BDDfy 8.0.11 and 8.0.12 as 8.0.1.3 apart from races and a title-casing difference on 8.0.12. Parallel tests on one static client, 24 of 24 trees alike on 8 threads and on 1, no hang. Release builds of the probe. Task ends on another thread (A07), a cold task started later (M13), `Task.Run` (M14), a task an earlier step started (A08), `Task<T>` (A03), `ValueTask` (A06, not waited on either version), a completed task (M6), a woven step's task returned (M7), title-only steps (M5), `LazyBDDfy` (M10), a story on `Task.Run` (M9), `Theory` rows (M3). Throws before the first await, after it, synchronously; `Task.FromException`; cancelled; two failures; an aggregate; `NotImplementedException`; `Assert.Skip`: the same result (`A14a` a race in which `WhenAll` failure comes first; the stack trace gains one frame, `CloseWhenDone`, which `ThrownAt` skips). `IAsyncLifetime`, `BeforeAfterTestAttribute`, a class fixture with a shared client, the phase after the story. `TestPhaseContext` written by hand, `StartAction` and `StartSetup` by hand, inserted fragments. Hand-made steps (H3 to H7) read as sub-steps with correct phases; one that closes or bypasses itself (H8, H9) does not disturb the next. `SkipIf` as the story step and under a step, on this commit's weaver. A woven step on the server becomes a sub-step and keeps its phase to itself; one left past the response nests as in F3.

## Bugs on both versions, met on the way (measured)

- At5 two stories in one test, the second fails (`DistinctBy(TestId)` keeps the first): xUnit Failed, report Passed, no entry in `Failures.md`.
- M11 a whole story retried, second attempt passes (same `DistinctBy`): xUnit Passed, report Failed, with the first attempt's error.
- A16 `Assert.Skip` in a step: xUnit Skipped, report Failed, text `$XunitDynamicSkip$skipped a16-w`.
- Z1 step never ends, `[Fact(Timeout)]` fires: xUnit Failed, no scenario; 6 calls "carried a test id that no scenario of this run has".
- The bar reads `And And follows` for `.And(x => x.AndFollows())` (A17). A custom `OperationCanceledException` message reads "A task was canceled." (A13a). `[Given]` attribute steps read keyword `Step` (N4). A `ValueTask` step is not waited for, so its calls can arrive as background calls (A06).

## Inferred, not measured

A task that never ends hangs a sync test on both versions. `AsyncVoidSupportEnabled` is read once in BDDfy and once in the adapter: a parallel test flipping it gets a step nobody waits on. A failing story step nested under a woven step would show `✗` with no text, since `CloseWhenDone` passes no message. `BDDfyStepInfo` equality under the trial fix, F8's collected duration, the untried fixes.

Measured: returning the wrapper stops `Task.Wait` inlining a queued `Task.Run` body. With `SetMinThreads(1, 1)` two runs each took 1.4 s and 3.7 s on 5.28.2, 2.2 s and 2.1 s on this change: no clear cost, noisy machine.

## What was not reached

The repository's own suite on this commit or on the trial fix. A real `WebApplicationFactory` or a real database or broker. `xRetry`. Runtime async, .NET 11, Linux. FluentAssertions and AwesomeAssertions. `kronikol merge` where attachments moved from scenario to step. `history`, beyond nothing under `History/` reading a phase. The HTML report and diagrams. Other adapters.

## Files

`fix-v2.patch`, `apply-fix-v2.py`, `probes/`, `pack.sh`, `run-suite.sh`, `compare.py`, `qdiff.py`, `trxdiff.py`, `attachments-table.py`, `out/<label>/`, `out/diff-shapes-*.txt`, `out/qdiff-shapes-*.txt`, `out/attachments-table.txt`, `feed/`, `tools/kt` (the 5.28.2 query tool).
