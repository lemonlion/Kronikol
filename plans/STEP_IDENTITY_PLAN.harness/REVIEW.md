# Independent review of the first design (2026-11-02)

A read-only reviewer, given the code and the draft and asked what is wrong. Status is what was done about each, at the time: every "to measure" below was measured, and the fix named in row 6 is itself gone, as the second review's table says.

| # | Finding | Status |
|---|---|---|
| 1 | BDDfy async steps lose their calls: the frame closes at the first `await` (#122), later calls stamp "no step open", and resolve to nothing, where today's walk says the step | to measure: B6. Design changes: no step open reads as today (below) |
| 2 | Nested Reqnroll steps (`ITestRunner` from a step): one `StepFrameKey` per scenario, the inner `AfterStep` removes it, the outer never closes, later frames nest barless, `MapSteps` returns null | to measure: T21. Fix: keep the frame on `scenario.StepContext`, which is per level |
| 3 | The prototype's Reqnroll `MapSteps` does not use `StepPairing` as the plan says, and a fire-and-forget `[Step]` in a hook makes frames nest under it and `Steps` null | to measure: T24. Fix: a frame opening with no frame below it treats what is open as left running |
| 4 | "Their stamps resolve to the same paths the walk gives" is false for a call between steps in the plain lanes, and Kronikol4J's `InteractionAttribution.java:68` has the walk, so byte parity would fail | design change, below. To measure: a plain xUnit lane probe |
| 5 | "A suite's throwing hook cannot lose a step" is false: a throwing block hook (`:504`), `AfterStep` (`:646`) or `StopAtFirstError` (`:649`) aborts the test method, so later steps are unlisted. `BeforeScenario` rethrows under `StopAtFirstError` at 3.3.4 and not 3.0.1. At 3.0.1 a throwing plugin hook skips `HookFinishedEvent` | to measure: T22. Plan wording corrected, a stated limit |
| 6 | `_ = A(); _ = B();` where A ends before the frame closes and B after: A's completion pops B, B's pops the next frame. The flag is read outside the lock | to measure: T23. Fix: the flow's step closes by identity |
| 7 | BDDfy frames every step, natives filter `ShouldReport`, so `TearDownWith` would become a listed row | to measure: B7 |
| 8 | B5 pairs the first row's natives with the last row's frames, so a failing first row paints the wrong row. `repro-bddfy-5.27.13.txt` had no B5 | Fix: keep the first row's frames. Results regenerated |
| 9 | `StepPairing` has no position fallback, so hand-built steps double in BDDfy and LightBDD; the fact at `BDDfyScenarioInfoExtensionsTests.cs:485` indexes from 0 and still passes | Fix: by position when no native has a frame |
| 10 | R2's fuller lists change `BackgroundStepsDetector`'s prefix, so passing siblings' addresses can move, not only failed scenarios' rows | Plan §9 |
| 11 | `ClearSteps` restarts per-test ids: stale stamps resolve to new steps, `ToDictionary` throws on a repeat | Fix: a process-wide counter. Ids are never written, so nothing needs them per test |
| 12 | The harness had the old name, and the suite log was empty at review time | Renamed; suites rerun |

Checked by the reviewer and found fine: no deadlock (`state` is never held across `Log` or `InsertPlantUml`); static dictionaries bounded by step count like `States`; the stamp is set after redaction and copies use `with`; ingest replay's synthesised steps have no ids and walk; `LogPair`, held records, explicit-id markers, retries, outline rows, theories and service-thread calls match the walk apart from finding 4; attachments, `Address`, `ReportScanner`, the digest and `MergeableReportReader` go by path; history, OTLP, `WarmUpCalls`, `ParameterGrouper`, `ServiceCardWriter` and `ExampleValueGrouper` read no step by position; `report-select-row-function.js` is cosmetic, no JS nth-bar join; the `scenario.StepContext` read is valid at both Reqnroll tags.

## The design change from findings 1 and 4

The first design stamped "no step open" as an answer of its own, and read it as no step. That moved calls between steps in every lane, and it took BDDfy's async calls away. It also went past the ask: T20's `/teardown` under the last step is #147, not #223.

So a record is stamped **only when a step is open**, and a record with no stamp reads as today: the step before it. The running `current` follows stamped markers and records. Bars still only draw, as a stamped marker sets `current` from its id rather than by counting. `/teardown` stays under the last step, as on published, and #147 stays whole.

# Independent review of the third cut (2026-11-02)

A second read-only reviewer, given the finished plan, the code and the harness, and asked what is wrong. It ran its own probes on published and on the prototype's 1914f362: `review-probes/`, `results/review-*`. Status is what was done about each, on abf7b0a3 (`prototype/apply_r6.py`).

| # | Finding | Status |
|---|---|---|
| 1 | Plain lanes regress: `Task.WhenAll(Fast(), Slow())` with Fast ending first, and `Own() { _ = Slow(); await Post("own"); }`: close by identity lifts the running step to the top level, and `/fast` reads another step's row | **Design change.** The flow leaves the plan. P5 and P7 match published line by line |
| 2 | T17's fix misses non-`async` step methods: a forwarder or a sync wrapper left running reads a stale flow, closes a frame early and makes a row of a sub-step | **Design change.** Only `CompleteFrame` closes a frame. R01, R02, L6 right at row level. A failing one can still paint a later sub-step failed (R18), as published does: stated in the plan's §5.1 and §9, #225's R1 ends it |
| 3 | The position fallback runs live in LightBDD: a `[Step]` failing in a scenario decorator is unlisted, and its call lands on a step that never ran | Fixed: the fallback needs results that ran. L5 right |
| 4 | "The scenario itself is never lost" and "T16 harms nothing else" are false: a hook at `int.MinValue` that throws loses the scenario | Same on both builds. Plan wording corrected, a stated limit |
| 5 | The plan's "what does not change" contradicted itself: rows do move for a passing sibling, T17, T21; a step over 110 characters gains its step (#128's in-process half); byte parity differs with bars off; a call between steps reads differently with bars off | Plan §5.3, §5.9, §11 corrected. P6 measured |
| 6 | #129's order is not in the prototype, and a stamp with no row records nothing | Plan §5.3 and §6.5 say so |
| 7 | The list of what was not run understated: five adapter suites, the examples' other lanes, `ReqNRollDuplicateStepsTests`, Kronikol4J parity, no build equal to a release | Run where possible (`results/examples-prototype.txt`); the rest listed in §6.5 and §7 |
| 8 | A hook leaves a `[Step]` running and the first step is undefined: Gherkin steps nest under it. A hook's `[Step]` awaiting a Gherkin step reads as left running | First fixed: nested only under a frame (R03). Second a stated limit (R05), plan §9 |
| 9 | The new event handlers do not catch, though the plan said they must | Fixed: `Quietly` |
| 10 | Stale: F15's count, L0 called right on published, "FlowStep is read only for a flagged step", the `RuntimeId` floor claim with no output, #225's shapes run on an earlier cut | Corrected; `results/lightbdd-floor-runtimeid.txt` added |

Checked by that reviewer and found fine: the remaining §0 rows and counts against results, `file:line` references, event order at both Reqnroll tags, `CloseStep`'s rule (reasoned, no counterexample), the all-patches versioning.

Before it, kronikol-af's review of the same shapes on #222 found three faults that held here (a nested step's arguments and outcome, a stray row for a skipped inner step) and a fourth in the detector fix (a hook's `[GivenStep]` keeps its keyword): `prototype/apply_r5.py`.

# Independent review of the fourth cut (2026-11-03)

A third read-only reviewer, given the code before any prose and asked what is wrong. It ran its own probes on published and on abf7b0a3: `review-probes/z-rq`, `z-bd`, `results/third-review-*`. Status is what was done, on `apply_r9.py` and `apply_r10.py`, measured in `results/z-rq-final.txt` and `z-bd-final.txt`.

| # | Finding | Status |
|---|---|---|
| 1 | **Regression.** R2's rows reach `BackgroundStepsDetector`, so one failed or skipped scenario rewrites its siblings' rows and statuses (BG1, BG2, BG3, and BG11 in a green run) | **Design change.** The guess reads only what a framework step ran, so its answers are published's by construction. All four as published |
| 2 | **Regression.** The same mechanism loses files and the whole report (BG8, BG9, and BG12 in a green run); BG10 crashes on both | Same change. BG8, BG9, BG12 write their report with the file. BG10 is #222's F3, ended by #222's R1 (`results/third-review-z-rq-merge222.txt`) |
| 3 | Two `.BDDfy()` calls in one test list the second's rows under the first (Z9) | Fixed with a peer's example-table finding: a frame lists only for a step of the scenario, the first time it ran |
| 4 | §9 "R2's rows remove the cause of #222's F6" and §5.9 "R2 adds rows" hid the above; "not a regression" held only for a hook's file; "the prototype breaks nothing" had no fact for it | Plan corrected; facts added to §6.2 |
| 5 | §0's T05 row false with the hook in every scenario (BG7); §9's "`b0` hook" row false with differing arguments (BG5) | Both right now, as a hook's row is never shared |
| 6 | Q3's precedent (LightBDD, Cucumber) does not run the guess | Plan corrected |
| 7 | Q4 treated #222's R1 first as a preference, and nothing stopped R2 shipping before it | Neither needs the other now; the merge measured (§5.8) |
| 8 | §5.2's first example row: false when row one never reached a step (Z8) | Fixed: a step running again ends the first row |

Its two cures, measured on its own worktree: the not-run handler off, and `Status` in the guess's comparison (`results/third-review-variant-*.txt`). The second is rejected in the plan's §5.7.

Checked by that reviewer and found fine: no double listing (`StepFinishedEvent` before the `AfterStep` hooks at 3.0.0, 3.0.1, 3.0.3, 3.1.2, 3.3.4); the triggers on the floor and the xUnit v3 and TUnit lanes; a hook swallowing a step's failure, outline rows with differing text, a pending step, a runtime skip, `@ignore`; BDDfy's reflective Setup and TearDown; the plain-lane collector; attribution across stamps, held records and hidden frames; record equality on the new internals; no crash path in `StepPairing`; the weaver's signatures.

# Audit of the plan's claims (2026-11-03)

A read-only agent checked every `file:line`, every §0 and §3 number, each statement about the prototype, §5.8 against the other plans, and §6.5 against `results/`. 31 mismatches, the plan's corrections are `git diff` of the two copies. The ones that mattered: Q6's "rows and calls are right" (a call by a step left running can read a later row); Q4's and §5.8's "#225 only changes how a stamp resolves" against its R1 having to land after; no word on #224, which edits the same code; R1 and R2's split in §7 and §6.2. It also found two prototype faults, fixed in `apply_r8.py`: a hand-built Reqnroll step carried no id, and a bypass could pop a frame.

From peers on the same day: kronikol-ca measured an example table tripling (`apply_r10.py`), #128's own repro staying as published, and its order against this plan (§5.8).

# Independent review of the fifth cut (2026-11-03)

The first review to see the final code of that cut (`9b85227f`), run because a design unreviewed since its last change has not been reviewed. It measured every finding on both builds, with its probes in `review-probes/fourth/` and its outputs in `results/fourth-review-*`. Every suite passed on the code it reviewed.

| # | Finding, on `9b85227f` | Measured | What was done (`apply_r11.py`, `c7252f6c`) |
|---|---|---|---|
| 1 | A file on a row the guess shares stops the report, in suites that are green on published. Published's shift happens to move the file off the shared row, and the fix puts it back | S1, S2, S4, S6: `Failed: 2, Passed: 2, Total: 4`, `KeyNotFoundException`, no report; S5, S7: `Failed: 3`, no report. 5.28.2 writes a report on all six | Each scenario keeps its own Background rows, and a walk visits a step once: #222's 5.4 and 5.5, in its words. Its guard alone was measured and is not enough: the second scenario shows the first's file and loses its own (`fourth-review-shapes-r1guard.txt`). All seven shapes now write a report, each file on its own scenario's row (`fourth-review-shapes-r11.txt`), including S3, which stops the report on published too |
| 2 | #129's forced read flags a healthy ingest as degraded, because the walk counts a background step that drew no bar, and a bar that wraps or cuts its text never matches it | `run.degraded` `{}` to `{'StepAttributionMismatch': 8}`, a banner on every `query` answer, a new labs page. The calls had no step on both builds | A tests file's step that draws no bar is marked where the file is read, the walk passes over a marked row that does not match the bar, and the matcher is #225's R2's. #129's order and this fix move to R3, so R1 leaves an ingest's walk and flags alone (`apply_r12.py`, `results/ingest-r11.txt`) |
| 3 | A step that did not run reads as passed in `query`, and `query failures` says no step is marked failed | D2: `b0` unmarked, blank rows under it | A `-` mark of its own (R2). With its own row, `b0` reads `✗`, so the false line goes too (`fourth-review-rq5-r11.txt`) |
| 4 | By-position rows carry a collected id, so a stamped call skips the text check that used to refuse it | BDDfy, a failing reflective `Setup`: `/setup` under a step that never ran. Hand-built with an extra collected step: `/seed` under `0 Given` | By-position rows carry no id (R8 reversed). Both shapes read as published |
| 5 | A consumer's own rows over the collector's sub-steps: a map exists as soon as a sub-step has an id, and top-level stamps resolve to nothing | 1 of 4 calls keeps its step, published 4 of 4, no diagnostic | A call is read by its stamp only where a scenario's top-level rows carry ids. Otherwise it is read by the walk, as published. 4 of 4 |
| 6 | The detector removes rows by reference, so a step object listed twice loses both copies | `logged in` lost | Removed by place among the rows that ran. As published |
| 7 | False `Re-run` line on a scenario newly listed from an after-hook (`Rq5.Driver.Cleaning_fails`) | Matches no test. BDDfy has the class on published | Not fixed: stated in §9 |

**False claims it found in the plan**, all corrected: a file on a shared Gherkin step "stops the report on both"; "this plan neither widens nor narrows that"; "neither plan needs the other", as R1 alone would have lost reports until #222's R1; "nothing measured gets worse" with R1 alone; the `AfterStep` exception "a sibling's layout can change", when it could lose the report; the ingest lanes "do not move", when `run.degraded`, the banner and the file list did; a hand-built scenario "sees no change"; "pairs by position as today", true of the rows and not of the calls.

**What the fix for finding 2 got wrong, twice.** R11's first version passed over any `b` row, and the core suite caught it: `CucumberAttachmentIngestTests.With_hooks_kept_the_step_markers_match_the_steps`, expected 2 mismatches, actual 5, as a Cucumber scenario with hooks kept has a bar arriving out of order for that row. kronikol-ca reported that from its merge before my own run finished. Its matcher also lost 4 of the labels that session ran through `kronikol ingest` (a backslash, `%date()`, a U+2028, a second line with markup), where its R2's loses none, so R12 takes that one, and a mark set by the tests file's reader in place of the guess.

**Why every suite passed.** No fact had a file on a row the guess extracts with a hook's step in the run, nor a consumer's own rows, nor an ingest with a background step through `service.json`. The same lesson as the third review: new rows or a new reading need the question of what else reads them, and here it was the same list, read by the attachment walk.

**What it checked and found fine:** nothing in C# or the page joins a step row to a bar by index; order readers go by address or call order; the marks survive `with`; `kronikol merge` of a prototype run reads the same rows; #129's order adds no new way to lose outputs; BDDfy in parallel, 32 of 32 scenarios' calls on the right rows; `StepFinishedEvent` comes before the `AfterStep` hooks at 3.3.4. One cosmetic join stays: `selectRowStepPath` syncs open states between outline rows by index, and can point at another row when a hook runs on only some rows (§9).

# Independent review of the sixth cut (2026-11-03)

On `306557a4`, the code after the fourth cut's review, with its probes in `review-probes/fifth/` and its outputs in `results/fifth-review/`. **It lost no report.** S1 to S7, an outline with a file on a shared row and one failing row, and an ingest with a file per scenario on a shared step all write one, and published fails on the last two. Own rows, by-position rows and the query mark held up. It measured six regressions in what a report says, against 5.28.2.

| # | Release | Finding | Measured | What was done (`apply_r13.py`) |
|---|---|---|---|---|
| 1 | R1 | A consumer's adapter mixing rows: `GetSteps`, with one row rebuilt by `new ScenarioStep`, or the opening rows rebuilt into `BackgroundSteps`. The map exists, the rebuilt row is not in it, and its stamps read as no step | `/when -`, `/shop -`, silently; published `/when 1`, `/shop b0` | First a stamp naming no row read as no stamp, which regressed (below). Then a scenario with a row that ran and carries no id is read by the walk whole, as published (`apply_r14.py`, §5.3) |
| 2 | R3 | The pass-over loses the walk's place on a store projected from an in-process run, with a marked row whose text differs from its bar | S8, S9, P, F: `/basket 0 /check 1` to `-` and `-` | It left the plan with #129 |
| 3 | R3 | The new matcher refuses bars the old one accepted: not escaped, with text the escape changes (`\`, `<$`), from an old store, a `kind: marker` line or `InsertPlantUml` | `/read 0 /send 1` to `-` and `-`, flagged | The same. Both plans that carry that matcher were told |
| 4 | R3 | False matches, from the first line only, a 400-character cap and folding spaces away | G, H, J, R, S: a call under a row it does not belong to, where published read no step | The same |
| 5 | R3 | #129's flag still fires on healthy runs | Cucumber `--include-hooks` with a Background, a tests-file assertion before the first step, a Background's text in a step's text, a step with no text, an unstamped bar, H1, a BDDfy `Setup` that fails | The same: #129 stays open (§7) |
| 6 | R1 | A LightBDD retry written as a scenario decorator gains a row, and `Then`'s address moves | M1 | Stated, not fixed: LightBDD's decorators do not run on a second invocation, so nothing marks it (§9) |

**Why this cut went the way it did.** The plan took #129 in on an audit item that asked whether R1 could close it. It went through three fixes in one day, each measured wrong by the next reader. It is the plan's own lesson again: a narrow copy of another plan's rule that needs a second patch should go, and the boundary be drawn.

**False claims it found in the plan,** all corrected: the flag "does not fire in process"; a hand-built consumer "sees no change" after R3; "three differences, all a call gaining or keeping its step"; after R3 "a tests file's calls gain steps"; "nothing measured gets worse" with R1 alone; a mismatch left on an ingest "is a real one".

**What it checked and found fine:** own rows through JSON, YAML, XML, every Specifications format, `Failures.md`, the page in bundled Chromium, an outline's table cells and row clicks, the feature table's counts and `SeparateBackgroundSteps`; `kronikol merge`; `query diff` across builds, with stableIds matching; internal members reach no decoded JSON; the Cucumber lane's calls; plain xUnit v3 gains only; Reqnroll on xUnit v3 and TUnit; BDDfy and LightBDD coherent; its own cuts of R1 and of R1 and R2 compile. It did not run BreakfastProvider, the floor lane at that commit, LightBDD on xUnit v2 and TUnit, plain NUnit, MSTest and TUnit probes, `query history` with a real ledger, the `DocumentOwnership` hold, or Linux, and it did not paint `Specifications.html` or drive the search UI.

# Narrow review of that fix (2026-11-03)

On `f9dda54a`, R13 only. Probes in `review-probes/seventh/`. It measured three builds, `f9dda54a`, `2720adf0` and 5.28.2, and the rule R13 added regressed live adapters that `2720adf0` read rightly:

| Shape | Call | `f9dda54a` | `2720adf0` | 5.28.2 |
|---|---|---|---|---|
| Bars off, BDDfy `TearDownWith` (B7) | `/teardown-with` | 1 | - | - |
| Bars off, BDDfy hidden step (H2) | `/h2/hidden` | 0 | - | - |
| Bars off, a later example row (B5, Z8) | `/customers/bob` | 1, 0 | - | - |
| Bars off, plain xUnit v3, a step still open (P8) | four calls | all 0 | all - | all - |
| Bars on, a later row's bar over a row that never ran (L1) | `/l1/add-bob` | 2 | - | 2 |

So R13's rule went (`apply_r14.py`), and `mix-new-row` and `mix-background` read as published through the wider gate instead. Its other finding was a line left over from R7, `var reportDiagnostics` still moved, and that went too. It found the ingest identical to 5.28.2 on 37 cases, and the rows of hook steps, not-run steps and nested bars unaffected.

**This last change has had no fresh reviewer.** It takes code out and widens a gate towards published's walk, and its proof is the suites and these reviewers' own probes run on it (`results/seventh-review/`).
