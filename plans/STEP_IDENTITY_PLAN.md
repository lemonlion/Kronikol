# Step identity plan: #223

**Written:** 2026-11-02, at 5.27.13 (`v5.27.13`, `c58d98c0`; main at `27cc0e46`). **Status: executing, on the owner's word of 2026-11-04 ("implement the plan in full"), with the recommended answers to Q1 to Q6.** R1 is 5.29.3. R2 and R3 follow. The sections below stand as they were reviewed, and each carries an **As executed** note where the release differs.

**Checked again:** 2026-11-03, at 5.28.2 (main at `9d994a4b`), line by line, with three more independent reviews and a narrow one. Each of them changed the design (§12). The prototype rebases on it with no conflict. Source references stay at `27cc0e46`: of the
files they name, only `ReportGenerator.cs` and `RequestResponseLog.cs` moved, neither near what is cited. **RUN at 5.28.2** marks what ran there.

Evidence labels: **RUN** (measured here, against the published 5.27.13 packages unless the line says otherwise),
**READ** (in the source, `file:line` at `27cc0e46`, or Reqnroll's source at `v3.3.4`), **INFERRED** (reasoned from facts,
stated by none), **ISSUE** (taken from an issue, not re-measured). Every RUN line has its probe and its output in
[`STEP_IDENTITY_PLAN.harness/`](STEP_IDENTITY_PLAN.harness/README.md). Trigger numbers (T01, B1, L2) name the scenarios
of those probes.

#223 reports that a `[Step]` method called from a `[BeforeScenario]` hook moves every Gherkin step's sub-steps down by
one and leaves the scenario's calls with no `stepPath`. That report is right, and it reproduces exactly (T01). The hook
is not the cause. It is one of a dozen ways into the same fault.

---

## 0. Summary

**The underlying problem: a step has no identity.** Four separate lists describe the same steps, each filled by a
different party under a different condition, and they are joined by position alone:

| List | Filled by | Filled when |
|---|---|---|
| The framework's steps (`ReqNRollStepInfo`, `BDDfyStepInfo`, LightBDD's results) | the adapter | Reqnroll's `AfterStep` hook is reached |
| `StepCollector`'s `CompletedSteps` | whoever finds the stack empty | a step closes with nothing open under it |
| The step bars in the log | the same rule | a step opens with nothing open |
| `OrderedStepPaths` | derived from the first list | report time, after background extraction |

`MapSteps` takes `collected[i]` for step `i`. Attribution takes the nth bar for the nth step. `CompleteStep` closes
whatever is on top of the stack. Those joins hold only while the lists agree in length and order, and nothing keeps them
in agreement. Anything that adds to one list and not another moves everything after it.

A second, separate root: **the Reqnroll adapter learns the scenario's lifecycle from hooks, and hook order is only a
convention.** Reqnroll runs none of a type's later hooks once one throws, so a suite's own throwing hook stops
Kronikol's last hook from running.

**What that costs, measured.** These are all on the published 5.27.13 packages:

| Trigger | Published 5.27.13 | With the prototype |
|---|---|---|
| T01 `[Step]` in `BeforeScenario` (#223) | sub-steps one out, a step's lost, 4 of 4 calls no step | each step its own, every call its step |
| T02 `[Step]` in `BeforeScenarioBlock` | shifted from its first step and again at each block, 6 of 6 calls no step | right |
| T03 `[Step]` in `AfterScenarioBlock` | right for one step, then shifted | right |
| T04 `[Step]` in `AfterScenario` | the step appears nowhere, its bar is drawn | listed last |
| T05 `[Step]` that fails in `BeforeScenario` | **no steps listed, the digest names no failing step** | failing step named with its call, steps listed |
| T06 `AfterStep` hook that throws | **no steps listed** | the step listed failed, with its sub-step and call |
| T08 a failing step with one after it | the skipped `Then` is not listed | listed, painted as skipped |
| T13 T01 plus a failing step | **the failing step shows another step's sub-step, and the digest lists no calls for it** | its own, with `POST /payments` |
| T15 `AfterScenario` hook that throws | **the failed scenario is not in the report**; `dotnet test` says 9 failed, the report 7 (T18 reads as skipped, F17) | present, failed, its calls attributed |
| T17 a `[Step]` left running | **every later step loses its bar, its sub-steps and its calls** | right |
| T21 a step that runs another through `ITestRunner` | rows scrambled, calls lost | nested under the outer step, each call its own step |
| T23, T24 `[Step]`s left running, one by a hook | later steps corrupted | every row its own, the left-running step keeps its row |
| T25 a step that runs steps with a table and a doc string | rows scrambled, 4 of 4 calls no step | nested, each with its arguments, every call its step |
| T26 a step that runs two, the first failing | the failed inner step on a row of its own, its parent reads passed, the rest unlisted | the parent failed, the failed and the skipped inner steps under it, the rest listed |
| E06 a `[Step]` in a step argument transformation, which Reqnroll runs before `BeforeStep` | shifted from that step on, 3 of 4 calls no step | a row of its own, every call its step |
| E01, E03 an outline or a rule with a hook's `[Step]` | the same shift as T01 | right, and a sibling's rows as they were |
| T00 with `ShowStepDelimiters = false` | **3 of 3 calls lose their step: a display option deletes data** | every call keeps its step |
| B1, B4 BDDfy: `[Step]` before `.BDDfy()` or in the constructor | the same shift as T01 | right |
| L2 LightBDD: `[Step]` before a composite step's own steps | the `[Step]` lost, a sub-step under the wrong parent, a row listed twice | right |
| L1 LightBDD: a tracked call in a scenario decorator | **the test fails**: "Current task is not executing any scenario steps" | passes, listed |
| Controls: T00, T09, T10, T12, T14; B0, B6, B7; plain xUnit P0 to P5, P7, overlaps included | right, apart from P5's nesting, which is T19's and #225's | unchanged, compared line by line in the plain lane |
| T20 a plain call in `AfterScenario` | under the last step (#147) | unchanged on purpose: #147 stays open and whole |
| A file attached in a hook's `[Step]`, in a feature with a shared prefix (#222's F3) | **no report at all** | report written, the file on the hook's row |
| A file on a step the guess shares, no hook in the run (S3, BG10) | **no report at all** (#222's F3) | report written, each scenario's file on its own row |
| The same file with a hook's `[Step]` in the run (S1, S2, S4, S5, S6) | report written, each file on a row it does not belong to | report written, each file on its own row. Unless each scenario keeps its own rows, this fix stops the report (§5.4) |

**Recommendation (Q1 to Q6 in §10 are the owner's):** three patches, no new public surface.

- **R1:** give a step an identity, and use it at every join: one pairing rule for all three adapters, a stamp on each log
  record, closing a framework step by id. A step method outside a framework step stays in the list, in the order it ran, with its
  bar, and the Background guess goes on reading only what a framework step ran, so no sibling moves. Each scenario keeps its own rows of what the guess finds, and a walk visits a step once: #222's 5.4 and 5.5, in its words, because the right row is the one the guess used to share. Closes #223. What only the flow of execution can tell (which step a call or a sub-step belongs to in an overlap) stays with #225, whose R1 lands after this one.
- **R2:** the Reqnroll adapter also listens to the engine's finished events, so a suite's throwing hook no longer loses the
  step that ran or the scenario (save a hook at Kronikol's own order, T16), and it lists the steps that did not run, which stay out of that guess too. A throw that aborts the test method (a block hook,
  `AfterStep`, `StopAtFirstError`) still leaves the steps after it unlisted, because Reqnroll never reaches them (T22, §5.5).
- **R3:** two LightBDD defects found on the way (L1, and a scenario's last call pushed out of it). #129 stays open (§7).

**Against the issue's suggestion.** Its first half (pair each Gherkin step with the step its hooks opened) is right, and it
is R1. Its second half (no bar, a synthetic "Before scenario" step, hook events) is not needed, §5.7 says why: a step
outside a framework step can be told without any event, and the bar only had to go because attribution reads bars.

---

## 1. How far each claim was checked

| Claim | How |
|---|---|
| Every row of the §0 table, published column | **RUN** `repro/`, `repro-bddfy/`, `repro-lightbdd/`, on the nuget.org 5.27.13 packages, read through each report's own `query.cs` (`results/triggers-5.27.13.txt`, `results/repro-*-5.27.13.txt`) |
| Every row, prototype column | **RUN at 5.28.2** the same projects built against the prototype's last commit (`results/lane-xunit2-proto.txt`, `results/cut-final-*.txt`, `results/more-final.txt`, `results/no-bars-prototype.txt`) |
| The page draws it | **RUN** `paint-probe.js` in Chromium: rows and bars for T01, T05, T08, T11 (`results/paint-*.txt`, `results/paint/`) |
| The prototype breaks nothing | **RUN at 5.28.2** the core suite and every adapter's (§6.5). They held no fact where a failed or skipped scenario shares a prefix, nor with a file on a row the guess extracts and a hook's step in the run, nor a consumer's own rows, nor an ingest with a background step read through its flags, so green said nothing about what the third and fourth reviews found: the probes showed it. The Playwright suite was **not** run |
| The design survives a reader who did not write it | Five independent reviews, a line-by-line audit of this plan's claims, and two peer sessions measuring on the prototype. The first review changed the stamp, the second took the flow out, the third changed what the Background guess reads, the fourth gave each scenario its own Background rows and set when a stamp is read, and the fifth took #129's work back out of this plan. Every finding is listed with what was done (`REVIEW.md`) |
| A hook step and the background detector | **RUN at 5.28.2** `repro-every/` (`results/every-final.txt`) and the third review's `review-probes/z-rq` on both builds (`results/z-rq-pub.txt`, `z-rq-final.txt`): §5.4 |
| Reqnroll's engine order and events, at 3.3.4 and at the floor 3.0.1 | **READ** `TestExecutionEngine.cs`, `ExecutionEvent.cs` at both tags. `ExecutionEvent.cs` is identical between them |
| xUnit v3 and TUnit lanes of Reqnroll, and its floor 3.0.1 | **RUN at 5.28.2** every trigger on each, published and prototype (`run-lanes.sh`, `results/lane-*`): the same answers as xUnit v2 on all 28, but T23's `/background`, which races a 300 ms delay against the next step and read either way on xUnit v2 as well |
| Each release on its own | **RUN at 5.28.2** R1 cut from the final code (`prototype/split_releases.py`, `results/cut-r1-*`, `results/suites-r1.txt`): §7 |
| A file on a row the guess shares | **RUN at 5.28.2** S1 to S7, each alone, on published, on the code before this fix, on R1 alone, with #222's guard alone and on the final code (`review-probes/fourth/`, `results/fourth-review-shapes-*`): §5.4 |
| What #129 would need | **RUN at 5.28.2** two cuts of it tried, then reviewed (`prototype/apply_r11.py`, `apply_r12.py`, `review-probes/fifth/`, `results/fifth-review/`): §7 |
| A consumer's own rows, hand-built rows, a by-position row in a live run | **RUN at 5.28.2** (`review-probes/fourth/con-src`, `hand-src`, `bd5-src`, `results/fourth-review-bd5-*`): §5.3 |
| The order against #222 | **RUN at 5.28.2** a scratch merge, #222's R1 under this plan (`prototype/merge_222_resolve.py`, `results/cut-m222-*`, `results/every-m222.txt`): §5.8 |
| What Specifications shows | **RUN** on a passing subset, as the page is blank once anything fails (`results/specifications-final.yml.txt`, `results/paint-specifications.txt`) |
| The stamp's cost | **RUN** `hot-path/` on both builds (`results/hot-path.txt`): §9 |
| Kronikol4J has no such pairing | **READ** `KronikolCucumberPlugin.java` reads the step type and never touches `StepCollector` |

---

## 2. Where it stands today

**The top-level rule.** `StepCollector.cs:76`: `isTopLevel = state.StepStack.Count == 0`. A step that opens with nothing
open draws a bar (`:81-101`) and, on closing, joins `CompletedSteps` (`:151`). Nothing distinguishes a Gherkin step from a
`[Step]` method that happens to find the stack empty.

**The pairing.** `ScenarioInfoEnumerableExtensions.cs:100-126`: `collectedSteps[i].SubSteps` for step `i`. `:62-64` gives a
scenario no steps at all when the adapter's own list is empty, whatever was collected. BDDfy has the same `MapSteps`
(`ScenarioInfoExtensions.cs:104`). LightBDD has it recursively, and appends the collected tail (`FeatureResultExtensions.cs:211`,
`:263-268`).

**The attribution.** `ReportGenerator.cs:5381-5441`, documented as positional: the nth `DiagramMarkerKind.Step` marker opens
the nth entry of `OrderedStepPaths` (`:5448`), checked by a loose text match (`:5463`). A marker that does not match sets the
current path to null. One bar too many at the front therefore loses every call after it (T01), and a call after the last
step stays under it, as nothing closes it (T20, #147).

**The stack.** `CompleteStep` (`:124-154`) pops the top, whichever step that is. A `[Step]` left running means the Gherkin
step's close pops the wrong frame: the Gherkin step stays open, the next one nests under it and draws no bar (T17).

**The hooks.** `ReqNRollTrackingHooks.cs:107` runs `AfterStep` at `int.MaxValue` and `:163` runs `AfterScenario` there, so that
they come last. Reqnroll's `FireEventsAsync` (`TestExecutionEngine.cs:350`) notes "if a (user-)hook throws an exception the
subsequent hooks of the same type are not executed". So coming last means not coming at all when a suite's hook throws.
Reqnroll also runs no `BeforeStep` or `AfterStep` for a step it skips (`:508`, `:591`, `:640`), so `MapSteps`' `priorFailure`
branch is unreachable in this lane: a skipped step never reaches the list it reads.

**What does work (RUN, the controls).** T00, no hook. T09, a plain call in a hook: no step, the rest right. T10 and T12, a
`[Step]` in `BeforeStep` or `AfterStep`: nested under the Gherkin step, which is open. T14, an attachment in a hook: on the
scenario. These must stay as they are.

---

## 3. Findings the issue does not state

| # | Finding | Evidence |
|---|---|---|
| F1 | Block hooks shift the pairing mid-scenario, so the first step can be right and the rest wrong | **RUN** T02, T03 |
| F2 | A `[Step]` in `AfterScenario` vanishes from the list while its bar is drawn, and its call loses its step | **RUN** T04. **INFERRED** for sub-steps and assertions, which T04's step does not have |
| F3 | A failing `[Step]` in a hook leaves a failed scenario with no steps, and the digest with no failing step | **RUN** T05 |
| F4 | `Failures.md` and `query failures` lose the failing step's calls, the first thing a reader goes to | **RUN** T13 against T08: "1 calls in this step" against none |
| F5 | A throwing `AfterStep` hook lists no steps at all, as the step is never closed or recorded | **RUN** T06 |
| F6 | A throwing `AfterScenario` hook removes a failed scenario from the report, and orphans its calls | **RUN** T15: 9 failed in the runner, 7 in the report, as T18 also reads as skipped (F17); "3 calls carried a test id that no scenario of this run has" |
| F7 | A `[Step]` not awaited corrupts every later step of the scenario | **RUN** T17, T23, T24, B3, L4 |
| F8 | Reqnroll's skipped and undefined steps are never listed; `priorFailure` is dead here | **RUN** T07, T08, T18; **READ** engine `:508-646` |
| F9 | A call after the last step is that step's call | **RUN** T20. #147 reports it for the ingest lane; this is the in-process half |
| F10 | `ShowStepDelimiters = false`, a display option, removes every call's step | **RUN** `results/no-bars-5.27.13.txt` |
| F11 | BDDfy has the same shift | **RUN** B1, B4 |
| F12 | LightBDD has the same join at sub-step depth, and lists a row twice | **RUN** L2 |
| F13 | A tracked call outside a LightBDD step **fails the test**: the option's step type fetcher throws there, and `InjectImplicitActionStartIfNeeded` lets it through. `TestInfoResolver` catches a throwing identity fetcher, so the two fetchers are treated differently | **RUN** L1; `TestTrackingMessageHandler.cs:455`, `LightBddTestTrackingMessageHandlerOptions.cs:27`, `TestInfoResolver.cs:109` |
| F14 | LightBDD's last call of a scenario can be pushed out of it: `EndedAt` is LightBDD's own timer, the call's stamp is `DateTimeOffset.UtcNow`, and `BackgroundAttribution.Expire` moves what started after the end | **RUN** `BackgroundCalls ×6` of 9 scenarios and `×6` of 11 on two published runs (`results/repro-lightbdd-5.27.13.txt`, `results/review-lb-published.txt`), timing dependent, and none on the prototype. Separate cause, found through the control |
| F15 | #129 holds, and hid all of this: the report has no `StepAttributionMismatch` despite the scenarios above that have one. `ReportAttributionTests:192` reads the live collector, so it stays green | **RUN**, **READ** `ReportGenerator.cs:534`, `:541` |
| F16 | `AttachmentCapturingPlugin`'s `[assembly: RuntimePlugin]` never loads: Reqnroll only searches `*.ReqnrollPlugin.dll` and the test assembly. So it is no route to the events, and it is dead public surface | **READ** `RuntimePluginLocator.cs`, `AttachmentCapturingPlugin.cs:6` |
| F17 | An undefined step fails the run and reads as skipped in the report | **RUN** T18. Q5 |
| F18 | A BDDfy `.WithExamples` test reports one scenario, so the later rows' calls belong to no step | **RUN** B5. The prototype keeps published's rows, so these calls still read no step (`results/cut-final-bddfy.txt`, §5.2). It stays one scenario, which is not this plan's to change |
| F19 | A Gherkin step that runs another through `ITestRunner` scrambles the rows and loses every call's step | **RUN** T21, T25 |
| F20 | The same step with its first inner step failing lists the failure on a row of its own, and its parent reads passed on a failed scenario. Reqnroll writes that failure to the inner step's context alone | **RUN** T26; **READ** engine `:509`, `:693-696` |
| F21 | A file attached in a hook's `[Step]` writes no report at all, once that step's row lands in a shared prefix. It is #222's F3, reached here because the shift puts the file on a background step | **RUN** `results/every-5.27.13.txt` |
| F22 | A step argument transformation runs before `BeforeStep`, so a `[Step]` in one is a mid-scenario trigger with no hook in sight | **RUN** E06 |
| F23 | A `[Step]` in an `AfterScenario` hook at Kronikol's own order is unlisted and its call has no step | **RUN** E09 |
| F24 | A BDDfy test run again under one test id (a retry) keeps its first attempt: a test that passes on its second attempt reads failed, and a step the first attempt never reached shows a later attempt's sub-step. Calls of the later attempts expire. R10 ends the wrong sub-step; the verdict is not this plan's (§11) | **RUN** `results/bddfy-retry-5.28.2.txt`, `results/cut-final-bddfy-retry.txt` |
| F25 | Specifications is blank for any run with a failure (`CannotVouchForSpecifications`), so a probe that fails on purpose never shows it, which is how the hook's row went unlooked at until now. Passing runs carry the shift there too | **RUN** `results/specifications-*` |
| F26 | A pending step fails the run and reads as skipped, like F17 | **RUN** E08. Q5 |
| F27 | On published a file on a row the guess shares stops the report unless the shift has moved it off that row. So fixing the shift alone loses reports that published writes | **RUN** S1 to S7, §5.4 |
| F28 | An ingest's walk counts a background step that drew no bar, and never matches a bar that wraps, cuts or breaks its text, so those calls read no step on published too, and the mismatch reaches no output. Not fixed here (§7) | **RUN** `review-probes/fourth/ingest-probe` |
| F30 | LightBDD's bar draws an object parameter as `[TypeName]` (`StepTrackingStepDecorator.cs:51`), and its row lists the value (`FeatureResultExtensions.cs:233`), so the walk refuses a row's own bar, and no matcher could read it. On BreakfastProvider's LightBDD lane that is 20 bars in 10 scenarios, and 9 calls read no step on published. A row's id ends it: all 9 read their step on the final cut | **RUN by kronikol-ca** (its F17) |
| F29 | `query steps` prints the same blank mark for a step that did not run as for one that passed. LightBDD lists such rows on published, and R2 adds them to Reqnroll's | **RUN** D2 on Reqnroll; **READ** on LightBDD |

**Found by measuring the prototype, and fixed in it.** Feeding the new rows to `BackgroundStepsDetector` let one failed or skipped scenario rewrite its passing siblings, in a green run too, and lost whole reports, which the third review measured and no suite held (§5.4). The first cuts had patched its guards instead. A nested step lost its table and doc string, and its parent's outcome stayed wrong (§5.2, §5.5), which kronikol-af found on #222's prototype and which held here too.

**Two corrections to my own first cut.** The first is T11. A `[GivenStep]` in a hook draws the first Gherkin step's bar as "And a registered
customer", which looked like a defect. It is not, once the hook's step is listed: the page collapses a repeated keyword
itself and paints that row as `And` too (**RUN** `results/paint-prototype.txt`). The first prototype sequenced framework steps
separately, which made the bar disagree with the list. The final design keeps one sequence, and §5.2 says so.

The second is F13. The first cut made the LightBDD option's fetcher return null, and the suite caught it: `ShouldThrowWhenStepTypeFetcherCalledOutsideScenarioContext`, in two test projects, pins that throw. So the
contract stays, and the handler guards its call instead, as the identity fetcher's resolver does. That also covers a consumer's own fetcher.

---

## 4. The underlying problem

Each symptom is a place where the lists disagree:

- A list gets an entry the others do not: a hook's step in `CompletedSteps` and the bars but not in the Gherkin list (T01, T02,
  T03, T11, T13, B1, B4, L2), or a nested Gherkin step in the Gherkin list but not in `CompletedSteps` (T21, T25, T26).
- A list misses an entry the others have: a step whose `AfterStep` never ran (T06), or skipped (T08), or undefined (T18).
- The stack's top is not the step being closed (T17, T23, T24, B3, L4).
- A bar is the only record of which step a call was under, so hiding bars or adding one changes the data (T01, F10).

Patching the trigger the issue names leaves the rest: a rule for hooks would not reach a `[Step]` before `.BDDfy()` or in a constructor, a composite
step in LightBDD, or an unawaited step anywhere. The position join is the defect. **Once a step has an id, none of these
needs a rule of its own.** A list may then hold whatever ran, and the joins do not care how many entries there are.

The hooks are a second cause and need their own fix, since identity cannot help a hook that never ran. T06, T15 and T08 stay
wrong with identity alone (**RUN** R1-only, at the first cut: T06 no steps, T15 absent), and go green once the adapter listens to the engine.

---

## 5. The design

### 5.1 A step has an id (R1)

`CollectedStep` gets an `int` id from one process-wide counter (`Interlocked.Increment`), assigned in `StartStep`. `ToScenarioStep`
copies it to an **internal** `ScenarioStep.CollectedId`, never written to a report, so reports stay deterministic. A per-test counter
was the first cut and failed review: `ClearSteps` restarted it, so a stale stamp resolved to a new step and a repeated id made
`ToDictionary` throw (first review 11).

Internal members on the public `ScenarioStep` and `RequestResponseLog` records are not new surface: precedent `RowName`,
`RecordedBefore8`. `Kronikol` already has `InternalsVisibleTo` for the three adapters (`Kronikol.csproj:36-40`).

The weaver resolves `StartStep` and `CompleteStep` by signature, so **neither public signature changes**. The adapters use two
new internal methods:

```csharp
internal static int? StartFrame(string? testId, string? keyword, string text, string[][]? table = null, string? docString = null);
internal static void CompleteFrame(string? testId, int? frameId, bool passed, string? errorMessage = null);
```

A step a framework's adapter opens for a framework step is a **frame**. `CompleteFrame` closes that frame by id. A step still open
above it was left running: it keeps its place under its parent, reads as passed with no duration until it finishes, and the adapter's
close no longer pops the wrong one. This also ends the double pop in BDDfy's and LightBDD's `catch` then `finally` (**READ**
`StepTrackingStepDecorator.cs:37-43`, no visible harm measured at L3, as LightBDD's native statuses win).

A step left running finishes later, through the weaver's id-less `CompleteStep`, which pops the top as it always has, with one rule added: **only `CompleteFrame` closes a
frame.** A completion that finds a frame on top, or nothing, is a step left running finishing, and it closes the one left last. `BypassStep` has the same guard. A frame opening with no frame below it treats everything
open as left running, which is what a `[Step]` left running by a hook is (T24, first review 3).

That keeps every row right, and leaves two things inexact. A completion does not say whose it is, so in an overlap two sub-steps can swap end times and status: a
`[Step]` left running that fails can paint a later step's sub-step failed, with its message, while the top-level row is right and the verdict is the runner's. And a call made by a step left running reads the step open when it is made,
which can be a later row than the step that made it (§9). Published does the same and worse, and both are #225's R1 to end: there the flow replaces the stack for nesting (§5.8).

**An earlier cut tracked the flow here,** with an `AsyncLocal` and a close by identity. The independent review of it measured two faults (`NOTES.md`, 14:50; `results/review-*-proto*` hold the last cut, which does not have them): a step lifted to the
top level in a plain lane when two step methods overlapped, with `/fast` on another step's row, and a stale flow for a non-`async` wrapper, which closed a frame early and made a row of a sub-step.
Both came from this plan doing part of #225's job, so it is gone. **Plain lanes have no frames, so their collector runs as published by construction**, overlaps included.

**As executed.** A frame closed by id took away what published had by accident: BDDfy's `finally` popped the open `[Step]`, so the frame stayed open until that `[Step]` ended. The second final review measured the cost (`REVIEW-FINAL-2.md`, AN1 to AN10): a BDDfy async step's inner steps became rows, an assertion after the `await` was lost, and a file went to the scenario. So the executor returns the step's task wrapped, and the frame closes when it ends (`CloseWhenDone`), which is the step-closing half of #122 and of #224's R3, agreed with kronikol-60. BDDfy's `AsyncTestRunner` waits on what the executor returns. It has no task to wait on for an `async void` step, or with `Configurator.AsyncVoidSupportEnabled` off, and those close at the first `await` (§9). The narrow third review measured what that assumes, that BDDfy receives the task: under an executor around Kronikol's that drops it, or stops waiting at a time limit, the frame stayed open, lost what it held and took the next step under it (`REVIEW-FINAL-3.md`, RV5, RV6). A frame still waiting when the next step starts is over now, unless that step runs in its flow, as a story run inside it does, and one still waiting when the report reads the scenario closes then.

### 5.2 One pairing rule: what ran, in the order it ran (R1)

`StepPairing.InOrderTheyRan`, one internal helper in `Kronikol` used by all three adapters, replacing their `MapSteps` loops:

- The collected list is the sequence. It is execution order by construction.
- A collected step that a framework step opened takes that framework step's keyword, text, status and table, and keeps its sub-steps
  and attachments from the frame.
- **A collected step no framework step opened stands as itself**, with the keyword it declares and its own bar.
- A framework step with no frame did not run: it stands where the framework lists it, after the last step that did (§5.5).

The adapters pair by their framework's own id: Reqnroll by the frame `BeforeStep` opened (an internal `ReqNRollStepInfo.FrameId`),
BDDfy by `Step.Id`, LightBDD by `IStepInfo.RuntimeId` (in `IMetadataInfo`; the name is in the 3.10.0 floor's assembly, **RUN** `results/lightbdd-floor-runtimeid.txt`, and the floor lane in §6.3 is what proves it). LightBDD applies it at
each depth, which removes the row listed twice (L2). When no native has a frame (a report built from hand-made steps), an adapter pairs by position as
today (first review 9), and those rows carry no id: a position is a guess, and an id would make a stamped call certain of it (§5.3). LightBDD also needs a result that ran: with every step `NotRun`, a `[Step]` failing in a scenario decorator stands as itself, where position would hand its call to a step that never ran (**RUN** L5, second review 3).

A framework step whose frame opened inside another step (T21, T25, T26) is that step's sub-step, and it goes through the same mapping: its table, doc string and
inline values are the framework's to give, and the collector only keeps a sub-step's named parameters. kronikol-af found this on #222's prototype and the first
two cuts here had the same fault (**RUN** `results/triggers-prototype-before-nested-fix.txt`, T25 loses both, where published shows them on scrambled rows). A
row no framework step opened carries an internal mark, `ScenarioStep.OutsideFrameworkStep`, because it can have a keyword (a hook's `[GivenStep]`, T11).

**BDDfy can run a test's steps more than once under one test id**: an example table (`.WithExamples` copies `Step` with its `Id`, **READ**), a retry, two `.BDDfy()` calls. `ScenarioInfoExtensions.cs:20` keeps the first per test id. So a frame is listed only for a step of that scenario, and only the first time that step ran; a step running again is the table's next row, and what runs from there on is not the first row's (B5, Z8, Z9). A hidden step (`!ShouldReport`, B7) is not one of the scenario's steps either, so it draws a bar and lists no row. Published's tree is kept, and so are its limits: those later runs' calls read no step (F18). An earlier cut listed them all, and an example table with no overlap had three times the rows (kronikol-ca, B06, B13). A framed native nested elsewhere is skipped. With no frames at all an adapter pairs its rows by position as today, those rows carry no id, and the scenario's calls are read against the bars (§5.3).

A failed row no framework step opened seeds Reqnroll's `priorFailure`, so what did not run after a failing hook's step reads as skipped (T05).

**As executed.** That seed is R2's, as R1 lists nothing that did not run. A list that is not in the order its frames ran (BDDfy lists by kind, so a `.When` after a `.Then` is out of order) places what did not run after the last step that ran, as its neighbours say nothing, and lists each row once (OR2). A frame no row names that holds frames the list does name (a BDDfy story run in a step of another, HS3) gives way to them. What did not run with `NotRunAfter` waits for that id rather than following the last frame (Q4).

A step built by hand through the public `ReqNRollStepInfo` carries no frame and pairs by position as it does today, with no id, so its calls are read against the bars, text check included (**RUN** H1, `review-probes/fourth/hand-src`). A consumer who
drives `CreateStandardReportsWithDiagrams(IEnumerable<ReqNRollScenarioInfo>, ...)` sees no change (§5.3), with one exception: a scenario that has no steps of its own and a collected step now lists that step (H2), as a scenario whose only step is a hook's does, and nothing tells the two apart.

**Keyword sequencing stays as it is** (§3's correction): `ResolveKeyword` keeps one sequence, so the bar and the painted list read alike.

### 5.3 A call names its own step; a bar only draws (R1)

`RequestResponseLogger.Log` stamps each record with an internal `OpenStep`, before the `DocumentFlow` hold at `:88`, since that hold can
release a record long after:

- the id of the deepest step open now, when one is open,
- nothing otherwise.

`AttributeInteractionsToSteps` resolves a stamped record through a per-scenario map from id to top-level path, and a stamped step marker
sets the running step from its id and does not count. **A record with no stamp reads as today: the step before it.** The walk therefore
stays for what has no stamp: every ingest lane, and a call between steps in any lane. This does not touch #128's matcher for those lanes.

**The map exists only where every top-level row that ran carries an id.** A scenario with one that does not (built by hand, paired by position, or a row a consumer rebuilt among collected rows) is read by the walk whole, with its text check, stamps or not, so it reads as published by construction. A row that did not run takes no call and has no id, so it does not count, and each adapter's such rows say so (`DidNotRun`). Three faults made this the rule, each measured (`review-probes/fourth/`, `fifth/`): a consumer's own rows over the collector's sub-steps kept 1 of 4 calls' steps, with no diagnostic; a by-position row gave a confident wrong step, as a BDDfy `Setup` that fails put `/setup` under a step that never ran; and a row rebuilt among collected rows lost its calls. **RUN at 5.28.2**: all as published. A `with` copy keeps the id, so a consumer who copies rows that way keeps the stamps.

The first cut stamped "no step open" as an answer of its own (a sentinel `0`). Review rejected it (first review 1, 4): it took BDDfy's async
calls away from their step, as the frame closes at the first `await` (#122), moved plain-lane calls made between steps, and broke Kronikol4J's
parity with `InteractionAttribution.java:68`. So T20's `/teardown` stays under the last step, as published, and #147 stays open and whole.
What this does end is F10 for calls inside a step: with bars hidden they keep their steps (**RUN** `results/no-bars-prototype.txt`). A call between steps has no stamp, and with bars hidden
nothing says a step with no call ran: `/after-empty-step` reads `0` with bars off, `1` with them on, and `-` on published with bars off (**RUN** `results/review-xu-final-nobars.txt`, `results/review-xu-published-nobars.txt`).

**Why the answers agree in a plain lane.** A bar opens only on an empty stack, so the bottom of the stack is always the step whose bar opened last, and a stamp resolves to its top-level
ancestor, which is that step. So a stamped record reads what the walk reads, unless the walk's text check refused the bar: a step with an id and over 110 characters, #128's shape, reads `-` on published and
its own step here (**RUN** P6).

**As executed.** A record a `DocumentFlow` hold releases late carries an earlier stamp. It reads that step, and it does not move the walk's place: the walk keeps the highest id it has seen, so what follows it reads as it would have (the second final review's finding 4, `A_held_call_keeps_its_step_and_what_follows_it_reads_the_step_before_it`). A stamped row is one paired by its frame, so a step an adapter could only pair by position is read against the bars and can record `StepAttributionMismatch` (HS1).

**A stamp whose id has no row reads as no step and records nothing.** That is a hidden BDDfy step, a later run of a BDDfy step (F18), a step still open at report time, a row a consumer dropped. The row before it did not make that call. An earlier cut read such a stamp as no stamp, for the consumer's rebuilt row above, and a narrow review measured what that cost: with bars hidden a hidden step's call took the row before it, and with bars on a later example row's bar took a row that never ran, by its text (`REVIEW.md`).

**The mismatch diagnostic.** A bar that names a row is not checked against text, so `StepAttributionMismatch` does not fire for it, as position is no longer what is trusted there. It stays for what the walk reads: every ingest lane, hand-built rows, a bar a consumer drew. As on published, it reaches no output, and only `kronikol ingest` prints it (#129, §7).

**As executed.** #225's R5, 5.28.5, closed #129: the entry now reaches the data file, `summary`, `run.degraded` and the labs page. The rule above stands: a step a framework's adapter collected cannot record it, and the walk's cases still can.

The trap: on published `BackgroundStepsDetector.cs:44-51` hands every scenario the first scenario's step objects (#222), so a later scenario's ids would not be in its
`BackgroundSteps`. R1 gives each scenario its own rows (§5.4), and the map reads the real list.

R1 resolves a stamp to the **top-level** step, so no `stepPath` moves to a sub-step. That is #225's change to make (§5.8).

### 5.4 Where a step outside a framework step goes (R1, Q1)

In the list, in the order it ran, with its bar. This is the representation the Cucumber lane already has for `--include-hooks`:
`CucumberFeatureSynthesizer.cs:569-580`, a keyword-less step, before or after the Gherkin steps as it ran. No schema, query, renderer or
Kronikol4J change follows, because the report already draws such a step.

```
  0     Reference data is seeded  [i0]
  1     Given a registered customer  [i2]
    1.0   A customer is registered
  2     When the customer places the order  [i4]
    2.0   An order is submitted
  3     Then the order is confirmed  [i6]
    3.0   The order is read back
```

**RUN** `results/paint/prototype-*.png`: the keyword-less row reads apart from the Gherkin ones, and the bar over `POST /seed` says where
that call came from.

The Gherkin steps' addresses move by the steps before them (`s1/1` is the `Given`), only in scenarios with such a step, which are wrong today.

**The Background guess reads only what a framework step ran.** `BackgroundStepsDetector` guesses a shared Background from keyword and text alone, and on published it hands every scenario the first scenario's row objects (#222). Earlier cuts fed it the new rows, a hook's and those of steps that did not run, and the third review measured what follows (`REVIEW.md`, `results/third-review-*`):

| Shape, all right on published | Earlier cut |
|---|---|
| BG1, BG2: a `Given` fails in one scenario | the failed scenario reads `✓`, or a **passed** one reads `✕`, and the digest names no failing step |
| BG3, BG11: a hook fails in one scenario, or it skips itself, **in a green run** | its passing siblings read skipped, with their sub-steps gone |
| BG8, BG12: a file on a step, one scenario failing or skipped, BG12 **in a green run** | `KeyNotFoundException`, **no report**, and the run exits 1 |
| BG5: a hook's `[Step]` with its own argument in every scenario | `b0 Seed for alice` on the scenario that seeded bob |

So the guess now reads what it has always read, on published: the rows a framework step ran. A row with `OutsideFrameworkStep` or `DidNotRun` (§5.5) is invisible to it and stays in `Steps`, in order, so **its answers are published's by construction**, and the ingest lanes, whose rows carry no mark, do not move. **RUN at 5.28.2** `results/z-rq-pub.txt` against `z-rq-final.txt`: every passing sibling reads as published, BG8 and BG12 write their report, each hook's row keeps its own arguments and outcome. 
**Each scenario keeps its own rows of what the guess finds, and a walk visits a step once** (#222's 5.4 and 5.5, in its words; one ships, whichever lands first). The fourth review measured why this is not optional. Published's shift happens to move a file off a row the guess shares, and R1 puts it back on the right row, so a file in a shared opening step, or from an `AfterStep` hook, with a hook's `[Step]` in the run, stopped the report in suites that are green on 5.28.2 (`KeyNotFoundException`, S1, S2, S4, S5, S6; S7 with R2; `results/fourth-review-shapes-proto3.txt` against `fourth-review-shapes-pub3.txt`). #222's guard alone was tried: the report is written, and the second scenario shows the first's file and loses its own (`fourth-review-shapes-r1guard.txt`). With its own rows, all seven write a report, each file on its own scenario's row, and S3 and BG10, which stop the report on published too, do as well (`fourth-review-shapes-r11.txt`, `z-rq-final-bg10.txt`).

Which rows are in a Background stays published's. What changes is whose values a later scenario's Background row shows: its own outcome, arguments, duration and file, in place of the first scenario's. That is #222's own suggestion, whose F4, F5, F7, F8 and F9's data go with it: `Failures.md` finds a failing step in a later scenario again (D2 reads `✗ b0`, `results/fourth-review-rq5-r11.txt`). A row is removed by its place among the rows that ran, as a step object listed twice must lose one copy, as `Skip` did on published. `OwnBackgroundSteps`, which earlier cuts needed for the ids, is gone.

In a feature where the guess finds a shared prefix, a hook's row therefore reads after the Background though it ran before it: `b0` the `Given`, `0` the hook, `1` the `And` (**RUN** `results/every-final.txt`). That is where the Cucumber lane and #222's split put it (§5.8). A sibling with no hook keeps `b0, 0, 1, 2`, and a file on the hook's row cannot stop the report, as a row is never shared.

A plain hook with no `[Step]` lists nothing, as today (T09). Kronikol shows what the author chose to name.

**As executed.** #222's R1 landed first (Q4), so what this section had to carry is already there: each scenario keeps its own rows, a walk visits a step once, and a Reqnroll scenario's Background is read from the document. Three things moved with it. `BackgroundStepsDetector` stays as #222 leaves it: its guess now runs only for a hand-built Reqnroll scenario and an ingest, and no row carrying `OutsideFrameworkStep` or `DidNotRun` reaches it. `SplitBackground` takes that job for the written Background: a row `OutsideFrameworkStep` is never a Background row and stays in `Steps`. #222's `OpenedBy`, `Nested`, `RanByAStep`, `DescribeNested` and `CountBackgroundSteps` go, as frames and `StepPairing` answer what they asked. So the table of guess shapes above stands as the reason. A consumer that rebuilds a `ReqNRollScenarioInfo` with `new` has no document and keeps its rows' marks, so the guess still stops at such a row (Q51, Q52, `BackgroundStepsDetector`). And a row a consumer rebuilds beside a nested step pairs the top-level rows by position, as published, with the nested native left out as it has no place there (Q3, `MapStepsByPosition`).

### 5.5 The adapter listens to the engine (R2)

Reqnroll publishes these whatever the hooks did (**READ**, at 3.3.4 and 3.0.1):

| Event | When | Used for |
|---|---|---|
| `HookFinishedEvent` with `AfterStep` | after that type's hooks, even if one threw (`:378`) | close the step when `AfterStep` was not reached (T06). A hook's failure is the step's failure |
| `StepFinishedEvent` | every step, skipped ones too (`:632`) | list a step no frame was opened for (T07, T08, T18) |
| `ScenarioFinishedEvent` | in a `finally` (`:267`) | collect the scenario when `AfterScenario` was not reached (T15) |

The existing hooks stay and remain the normal path, as `AfterStep` and `AfterScenario` are public and removing them is a break. Each
hands off to one static method, and the event calls the same method, which does nothing the second time. The subscription joins the existing
once-per-publisher block (`ReqNRollTrackingHooks.cs:66-84`), which runs in `BeforeScenario`, ahead of all three events.

A not-run step is placed by the id of the step opened before it (`NotRunAfter`), which is exact where §5.2's general rule is not (a failing
before-hook step with an after-hook step as well). Its row carries an internal `DidNotRun`, which keeps it out of the Background guess (§5.4). The double listing one might fear does not happen: `StepFinishedEvent` comes before the `AfterStep` hooks, and the frame key is still set for a step that ran (**READ**, decompiled at 3.0.0, 3.0.1, 3.0.3, 3.1.2 and 3.3.4 by the third review).

**What this cannot fix: T16.** A suite's hook at `Order = int.MinValue` ties with Kronikol's and ran first here, before the scenario has an
identity, so its `[Step]` records nothing and its call is dropped. Moving identity into an event does not help: `HookStartedEvent` would come
first, but an `AsyncLocal` set inside `PublishEventAsync` does not flow out of that async method. T16 shifts nothing and goes in the docs. If that hook also throws, Kronikol's never ran, and the scenario is absent from the report on both builds (**RUN** `results/review-rq-*`): a limit, as narrow as it sounds.

**What this cannot fix either: a throw that aborts the test method.** A throwing block hook (`:504`), a throwing `AfterStep` (`:646`) and
`StopAtFirstError` (`:649`) leave the generated method, so the steps after it never reach `ExecuteStepAsync` and no event names them. T22 lists
the `Given` that ran and fails the scenario, with the digest saying the failure was outside a tracked step. At the floor 3.0.1 a throwing plugin
hook also skips `HookFinishedEvent`. These go in the docs as limits. A throw in a hook after Kronikol's own does not lose the scenario, as `ScenarioFinishedEvent` is in a `finally`.

**A Gherkin step can run another** (`ITestRunner`, T21), so the frame key and the stopwatch sit on `scenario.StepContext`, which is per level. The
stopwatch stays set on the scenario too: `ReqNRollConstants.StepStopwatchKey` is public, and a suite may read it there. Two more things follow from it:

- **Outcome.** Reqnroll writes a failure to the inner step's context alone (`:509`, `:693-696`), so the outer step would read passed over a failed sub-step on a
  failed scenario. A frame opens only while the scenario is well, as a skipped step runs no hooks, so **the scenario failing by the time a frame closes means it failed
  in that step**: `CloseStep` reads the step's status, then the scenario's. That also covers a throwing `AfterStep` hook, so no separate path remains for it.
- **A skipped inner step** has no frame, and `StepFinishedEvent` reports it while its parent is open. It is listed under that parent (`NotRunUnder`, the frame open then),
  at the end of what ran there, and not as a top-level row (**RUN** T26).

**As executed.** No `HookFinishedEvent` subscription: #222's `OpenStep` chain and its `CloseLeftOpen`, run at `[AfterScenario(Order = int.MinValue)]`, already close a step a suite's hook kept the adapter's own `AfterStep` from closing (T06), so the event had no job left. `StepFinishedEvent` is the one R2 adds (`NoteStepThatDidNotRun`), and `ScenarioFinishedEvent` is #222's R4's.

### 5.6 The LightBDD defects (R3)

F13: `InjectImplicitActionStartIfNeeded` guards its call to the step type fetcher, as `TestInfoResolver` guards the identity fetcher. The fetcher itself still throws, as two facts pin. F14: `EndedAt` is the later of LightBDD's end
and the last step's end by the clock calls are stamped with, kept by the step decorator. **RUN** both on the prototype: L1 passes,
`BackgroundCalls` is gone.

### 5.7 Rejected

**The issue's "no bar, under a 'Before scenario' step, with hook events".** A step that no framework step opened is identifiable from the list,
with no event. Removing the bar only serves a walk that R1 no longer uses for these steps, and the bar is useful: it says where `POST /seed`
came from. `HookBinding*` events are Reqnroll's alone, and BDDfy and LightBDD have the same trigger. The synthetic step needs invented text
in a living document, further names for block and after hooks, and it still moves every Gherkin step's address by one.

**A separate `Scenario.HookSteps` with `h0` addresses.** It keeps the Gherkin addresses. It is new public surface, a schema key, a merge reader
case, a query grammar case, a rendered section and a Kronikol4J port, for scenarios that are all broken today. Q1 offers it.

**Drop them, as the Cucumber lane's default does.** Its stance is that a hook named `BeforeEach hook` is plumbing. A `[Step]` is one the author
named and tracked, it holds assertions, and dropping a failed one is F3.

**Identity on the step marker instead of a stamp on every record.** Smaller by one hot-path read, but it keeps bars as the data channel (F10), and
#225 needs the stamp anyway (§5.8).

**"No step open" as a stamped answer of its own (a sentinel).** The first cut. It moved calls between steps in every lane, took BDDfy's async
calls away from their step and broke Kronikol4J's parity (first review 1, 4; §5.3).

**`Status` in the Background guess's comparison.** The third review measured it as a cure: every shape reads right. But a passing sibling then moves from `b0, b1, 0` to `0, 1, 2`, BG5 stays wrong, and the ingest lanes call the same method. Reading only what a framework step ran gives published's answers instead (§5.4).

**Listing all of a BDDfy test's runs.** An earlier cut did, which placed every call of an example table and tripled its rows (§5.2). The calls are worth having, but not at the price of a tree nobody asked to change, and F18 can take them on its own.

**A forward text search in the walk (resynchronise on the next matching bar).** No new field, and `Contains` matching makes a false match likely on short
step texts. It is unsound where identity is exact.

**#222's guard alone, in R1.** A walk that visits a step once writes the report, and the second scenario shows the first's file and loses its own (**RUN** `results/fourth-review-shapes-r1guard.txt`).

**#222's R1 as a hard prerequisite.** It would end the same fault, and it brings that plan's visible change and its Q1 into the way of this fix. The two lines this needs are that plan's 5.4 and 5.5, and they do not depend on the rest of it.

### 5.8 With #225 and #222

kronikol-ca is planning #225 (a call's `stepPath` names the sub-step it was in) as `CALLS_UNDER_THEIR_STEP_PLAN.md`, uncommitted in `C:/Code/Kronikol-225`, on the same code. Agreed with that session
on 2026-11-02, as a proposal of two plans, neither green-lit: an int id from `StartStep` (this plan's is process-wide, §5.1, and kronikol-ca has been told), an internal copy on `ScenarioStep`, a stamp on
`RequestResponseLog`, close by id; this plan adds them, and if this plan is declined, that plan's R1 brings them itself. The names and whether a bar is stamped are still open between the two: that plan's §6 says markers are not stamped and names `StepOrdinal`, where §5.3 here stamps bars and the scratch merge below uses `CollectedId` and `OpenStep`. Its stamp reads the
flow's own step, not simply the deepest, because `Task.WhenAll` of two steps nests them on the per-test stack (T19). At top-level resolution the two
readings agree, apart from a step left running past its step (§9).

**The boundary with #225: rows here, the flow there.** This plan keeps no `AsyncLocal`. That plan's R1 replaces the stack with the flow for nesting, with parent links and a `CompleteStepAsync` that moves the
caller's flow back, which is the exact rule and needs more than this plan should carry. **That R1 has to land after this one**, its R2 has no stamp and no dependency and can land at any time, and its R3 needs its R1. It waited on #222's R1 while later scenarios' rows were the first scenario's, as the address then named a row the report did not list (BV2), and this R1's own rows end that: **RUN by kronikol-ca** on R11, BV2 holds with no #222 under it, and across the third review's five suites 733 calls sit on a step, none naming a row its scenario does not list. Its layer applies on `306557a4` with no conflict (core 11,825 passed, 0 failed), and BreakfastProvider on that merge has 825 scenarios with none moved to another top-level step and none losing its step. On `638f275d` with its layer and its R2, core reads 11,824 passed, 0 failed, every lane and example suite and 115 end-to-end Step facts pass, this plan's triggers read as on R11, and BreakfastProvider passes all four lanes with no call moved, 838 calls reading a sub-step and 9 gaining a step (F30). kronikol-ca measured the order on this prototype (**RUN** by that session, a scratch merge of the two,
`CALLS_UNDER_THEIR_STEP_PLAN.harness/prototype/on-223-abf7b0a3.patch` in its worktree). For P5, published reads `/fast` 0, `/slow` 0, `/orders` 1. Its R1 alone, or on this R1 with the stamp reading the step last opened, reads 1, 1, 2, which is
the fault the second review found here. On this R1 with the stamp reading the flow it reads 0, 1, 2. So it moved that reading into its R1, which now needs this plan's stamp and pairing under it. It has since moved its layer onto the fifth cut (`on-223-10bbbe81.patch`, one conflict, the bypass guard).

What the two give together, by the same merge: this plan's frame rule stays (with no frame open a frame has no parent, in both; with one open its parent is the stack's top here, and that frame in the merge), and its list of steps left running has no job left, as such a step is simply still open. R18 no longer paints the
innocent sub-step, and R01, R02 and L6 read `/background` as `0.0.0`. P5 stops matching published on purpose, as that R1 makes the two step methods siblings, so §5.9's "as published" holds until then.

#222 is planned as `plans/BACKGROUND_AS_WRITTEN_PLAN.md`, uncommitted in `C:/Code/Kronikol-222`, not green-lit. Its R1 reads the Background
from the Gherkin document, gives each scenario its own rows, and makes a walk visit a step once (its 5.4 and 5.5). **This plan's R1 takes those two in the same words, so whichever lands first ships them, and with either its F3 (a file on a shared step stops the report) ends.** So neither plan needs the other, and that is because each carries those lines. An earlier version of this paragraph said so of the guess alone, and the fourth review measured it false (§5.4).

**RUN at 5.28.2, a scratch merge** with that R1 under this plan's code (`prototype/merge_222_resolve.py`; six conflict hunks in four files, `results/cut-m222-*`, `results/every-m222.txt`, `results/third-review-z-rq-merge222.txt`). All 28 triggers read as they do on this plan alone. A written Background reads as written, in a rule too, a hook's row after it (E03, `@every`), a sibling keeps its addresses (`@some`), a nested step inside a Background is counted right (E16), and all of the third review's shapes read right, BG10 included, so #222's R1 ends what this plan leaves of them. Whichever lands second settles three points, as that plan's §7 says, and the merge settled them this way: the guard against a step that runs a step, where this plan's frame per level wins, as ids need one anyway; #222's split, which takes the first rows a Gherkin step opened, by `OutsideFrameworkStep`, as a keyword
cannot tell (a hook's `[GivenStep]`); and `CountBackgroundSteps`, which reads the mapped rows by the document's text. Its final R1 keeps nested steps in the public list with an internal `RanByAStep`, which `ToFeatures` maps around, honours an explicit `BackgroundStepsOpen = false`, and leaves a hand-built scenario the guess, with its own copies. **RUN by kronikol-af**, its final R1 on this plan's R10 (`prototype/apply_on_223.py` in its harness): zero wrong rows in all five modes, its nested and edge shapes right, and this plan's 28-scenario suite line for line (`results/with-223-its-own-suite.diff.txt`). **RUN by kronikol-af on R11**, with that script rewritten for it (no detector or `ForEachList` edits left to make): zero wrong rows again, E35 `✗ b0`, E36 `✗ b1`, E39 `✗ b0` over `✗ b0.1`, E41 `✗ 0`, E47 listed, and this plan's suite differs only by `query`'s new `-` mark. That plan's section 7 carries the correction.

T21 is its shape 11. This plan lists skipped steps from `StepFinishedEvent`, #222's R2 from the pickle; its Q3 recommends the event, and only one ships. That plan's §7 says this plan takes the public stopwatch key off the scenario, which was true of an earlier cut and is not of the last (§5.5).
Q4 recommends its R1 first if both are taken. #225's plan now states that this plan's R1 is a hard prerequisite for its R1, and kronikol-ca ran five wrong-row shapes on a
copy of the prototype (BDDfy Setup and TearDown, a `[Step]` after the first `await`, a LightBDD composite with `Track.That` or a `[Step]` in its own method, a step through
`ITestRunner`), all reading the right row (`CALLS_UNDER_THEIR_STEP_PLAN.harness/results/review-probes-on-223-pairing.txt`, in its worktree). It reran them on `abf7b0a3` (`review-probes-on-223-pairing-abf7b0a3.txt`), and they read line for line as they did before the flow left, so the flow played no part.

kronikol-f6 is planning #226 as `STEP_METHOD_SHAPES_PLAN.md`, uncommitted in `C:/Code/Kronikol-226`. It changes the weaver, and this plan does not touch `StepWeaver.cs`, so either can land first. It says every
woven method will end through the ambient `CompleteStep` or `CompleteStepAsync`, which this plan leaves as published apart from the frame rule (§5.1). A `SkipIf` bypass completing twice, which that plan stops, cannot close a
frame here either (**READ** the prototype's `StepCollector.cs`).

#224 is planned as `STEP_PHASE_PLAN.md`, uncommitted in `C:/Code/Kronikol-224`, by another session, and neither plan has been run on the other. It edits the same code: `StartStep`'s body moves to an internal overload taking a `phaseKeyword`, its phase switch goes, and the
same three adapter calls change (Reqnroll's `BeforeStep`, LightBDD's decorator, BDDfy's executor). Its adapter closes go through `CompleteStep`, which here does not close a frame. **Whichever lands second adds `phaseKeyword` to `StartFrame` and `OpenStep`, and routes that plan's closes through `CompleteFrame`.** That plan
deletes the decorator's `finally`, where F14's `LastEnded` lives here. Its F15 is the double pop §5.1 ends, and its F16 and F13 here are one guard, of which one ships: it catches `InvalidOperationException`, and this plan catches everything, so that a consumer's own fetcher is covered. Its R3 keeps a BDDfy async frame open until its task ends, which is the closing half of #122, so §5.3's note on BDDfy's async calls
stops applying once it lands. Its chain reads `EndTime`, which `CompleteFrame` sets and a step left running does not, so the rule itself does not conflict (**READ** both prototypes).

### 5.9 What does not change

**Plain lanes** (xUnit v2, xUnit v3, NUnit, TUnit, MSTest): no frames, so the collector runs as published, and stamps give what the walk gives (§5.3). **RUN** on xUnit v3 by P0 to P7
(`results/repro-xunit3-*.txt`, `results/review-xu-*`), overlaps included: `/between` under step 0, `/before` no step, `/after` under step 0, `/after-empty-step` under step 1, `/teardown` under step 0,
two step methods in parallel as published. Three differences, all a call gaining or keeping its step, so they go in the Kronikol4J ledger, with own rows on an ingest, as Java's `BackgroundStepsDetector.java` still shares the first scenario's rows (**READ** by the fifth review), and as Java's walk keeps the old answer (`StepCollector.java:79`,
`InteractionAttribution.java:68`, READ): a step over 110 characters whose steps came from the collector (P6, #128's shape), any call with bars hidden that is inside a step or follows a stamped call (F10), and a call `DocumentOwnership` held across a step boundary, which now reads the step it was logged under and not the one open at release (**READ**, `RequestResponseLogger.cs:88`, `DetachedFlow.Confirm`, not probed).

**Every ingest lane:** no stamp and no mark, so which rows the detector takes is as today. R1 gives each scenario its own rows there too, which is #222's F9 data, measured in that plan (`results/ingest-lane-detector-own-copies.txt` in its harness). The walk, its matcher and the flags are published's (§7). **The data file's shape, schema and query grammar:** unchanged.

**A scenario with no step outside a framework step, none left running and no nested Gherkin step, in a feature where that holds for every scenario:** no addresses or bars move in R1, and the rows are the same rows. Where the guess finds a prefix, a later scenario's Background row shows its own values (§5.4), so a golden pin moves only where those differ from the first scenario's. T17 and T21 do move, being wrong today. R2 adds rows to failed and skipped Reqnroll scenarios (Q3), and no passing sibling moves (§5.4), with one narrow exception: when a suite's `AfterStep` hook throws, the step it closed through the event is a row the guess never saw on published, so a sibling's layout can change, and its report is written (S7). Kronikol4J has no Reqnroll lane.

---

## 6. Tests

Red first, per `CLAUDE.md`. Each stage's prototype is the first cut, not the release: it has no facts of its own.

### 6.1 The collector (R1)

`StartFrame` and `CompleteFrame` by id; a frame closes through steps left running, which keep their place; only `CompleteFrame` closes a frame, and a completion that finds one on top closes the step left last; with no frame, a stack behaves as published (P5, P7);
a second bypass does not close a frame; `OpenStepOf` returns an id or null, with no sentinel; ids never repeat, across `ClearSteps` too; a frame opening with no frame below it parks what is open as left running (T24); two left running, one ending before the frame closes (T23); the public `StartStep` and `CompleteStep` behave as before, including the 4-arg overload the weaver
binds to; a new fact that `CollectedId` and `OpenStep` reach no data file, in JSON, YAML, XML or `.krkl`, and no NDJSON line (`RequestResponseLogRoundTripTests` reads public members only, so it cannot pin an internal one).

### 6.2 Pairing and attribution (R1)

`StepPairing`, by table: nothing outside, one before, between, after, a not-run step, no frames, hand-built steps by position. Attribution: stamped resolves, no stamp reads as
the step before (P1 to P4, T20, B6), a call before the first step is no step, a stamped bar is not checked against text, a later scenario's id resolves through its own background rows, a sub-step's id gives
its top-level path. `Scenario.Steps` is set with an empty Gherkin list and a collected step. Position fallback: hand-built natives with no frames; strengthen
`BDDfyScenarioInfoExtensionsTests.cs:485`, which indexes from 0 and passes either way (first review 9). A framed native nested elsewhere is skipped; a hidden BDDfy frame lists no row;
the first example row pairs and later rows list nothing (B5), a first row that stopped early does not take a later row's frame (Z8), a second `.BDDfy()` and a retry list only the first run (Z9, F24). `BackgroundStepsDetector` reads only what a framework step ran: a hook row first and `When` next extracts nothing; a hook row before the remaining `Given` extracts nothing; a hook in every
scenario with a common `Given` then `And` extracts the `Given` and leaves the hook's row in `Steps`; a passing sibling keeps its addresses and its rows when a scenario fails at its `Given`, fails or skips itself in a hook, in a green run too (BG1, BG2, BG3, BG11); a report comes out with a file on a step in those shapes and on a hook's row (BG8, BG9, BG12, `@everyfile`); a hook's row keeps its own arguments (BG5) and its failure (BG7); `DidNotRun` rows are not compared. The public stopwatch key is still set on the scenario. Nested frames: a nested row carries its table, doc string and inline values (T25); a parent reads failed over a failed inner
step (T26); in R2, a skipped inner step lists under it, with no stray top-level row; a hook that runs a Gherkin step pairs (T27). A `[Step]` failing in a LightBDD scenario decorator lists as itself, and its call stays off a step that never ran (L5). The mark: a hook's `[GivenStep]` first and `When` next
extracts nothing; an ingest's rows, which carry no mark, keep today's answers.

**The fourth review's, red then green in `prototype/apply_r11.py`:** each scenario keeps its own Background rows; a step object listed twice is taken once; a step two scenarios hold is visited once by `ForEachList`; rows built by hand over the collector's sub-steps are read against the bars. To write: S1 to S7 as an engine fact (§6.3), by-position rows carry no id on each adapter (X1, H1), and `query steps` prints `-` for a row that did not run (R2).

**The gate and a stamp with no row:** `A_row_rebuilt_by_hand_among_collected_rows_keeps_its_calls`, a row rebuilt in `Steps` and in `BackgroundSteps` (`prototype/apply_r13.py`); `A_stamp_that_names_no_row_reads_no_step`, and `A_row_that_did_not_run_leaves_the_others_read_by_their_stamps`, its control (`apply_r14.py`), red then green where a fix was due. To write: BDDfy's and LightBDD's rows that did not run carry the mark.

### 6.3 A real engine (R1, R2)

No Reqnroll project is under `tests/`, and the existing facts drive the hooks directly (`OutlineRowHooksTests.cs`), which cannot show engine order. As executed the host is `tests/Kronikol.Tests.RealEngines/` with its fixtures in `tests/Kronikol.Tests.RealEngines.Fixtures/`, one per engine and lane, holding the harness's `repro/`. It runs them the way `tests/Kronikol.Tests.xUnit2/Lane/FixtureLane.cs` runs its fixtures: a child
`dotnet test`, then facts over its report. These scenarios fail on purpose, which is why it cannot be a plain test project. One fact per T row, and one each for E01, E03, E06, E09, E16. Run it on xUnit v2, xUnit v3
and TUnit, and on the floor with `-p:FloorRun=true`, since the undefined-step path differs at 3.0.1 (`StepInstance` is set after the match there, **READ**, hence the guard). `run-lanes.sh` has shown all four read alike, so these facts are the guard against that changing.

The same for BDDfy and LightBDD, from `repro-bddfy/` and `repro-lightbdd/`, including BDDfy's `.WithExamples` (B5, §5.2).

### 6.4 What is painted (R1, R2)

A new Playwright class, `HookStepTests`, in `tests/Kronikol.Tests.EndToEnd/`, under the repo's E2E rules. T01: rows in order, each sub-step under its own parent, four bars in the rendered SVG, Hide
Steps hiding the hook's bar with the rest. T05: the failed row, skipped rows painted as such. A failing scenario's digest link goes to the hook step. Dark theme for the keyword-less row. T01's row on the Specifications page.

**As executed.** The digest has no step link: what it prints is the address, `Failing step sN/0`, and that address is the hook step, which a real-engine fact reads. The class's rows are built by hand, so its six facts pin the page and pass on published as well. They are not red-then-green facts, and the real-engine lane is what shows the fix.

### 6.5 Proof before the tag

**Run at 5.28.2** (`proto/223-on-5282`, `638f275d`; 21 source files, +628 -97. The test edits are `prototype/apply_r11.py`'s, `apply_r13.py`'s and `apply_r14.py`'s, so most of §6's facts do not exist yet):

| What | Result |
|---|---|
| `Kronikol.Tests` | 11,824 passed, 2 skipped, 0 failed (`results/suites-final.txt`). An earlier cut, R11, did not pass: 1 failed, a Cucumber fact (`REVIEW.md`), then 2 on a rerun under load, the second unnamed, as the script kept only the count. It now keeps each failing fact's name |
| `Kronikol.Tests.LightBDD.xUnit3`, `.xUnit2`, `.TUnit` | 35, 11 and 26 passed |
| `Kronikol.Tests.StepTracking` | 61 passed |
| `Kronikol.Tests.xUnit2`, `.xUnit3`, `.NUnit4`, `.MSTest`, `.TUnit` | 111, 33, 49, 85 and 50 passed |
| The examples, base against prototype | 24 scenarios on the Reqnroll, BDDfy, LightBDD and plain xUnit v3 lanes: no difference in rows, status, or calls by step and path (`results/examples-*`, `prototype/run-examples.sh`) |
| `repro/` | 28 scenarios. The runner fails 9, all on purpose (`results/runner-counts.txt`). The report says 8 failed and T18 skipped, which is Q5 |
| `repro-more/` | 16 scenarios, 2 failing on purpose (E07, E08). The report says 1 failed and E08 skipped, which is Q5 (`results/more-*`) |
| `repro-bddfy/`, `repro-bddfy-retry/`, `repro-lightbdd/` | 8 passed; 2 passed and 1 failing on purpose; 8 passed and L3 failing on purpose |
| `review-probes/xu` P0 to P7, which holds `repro-xunit3/`'s P0 to P4 | no differences from published but P6, a call gaining its step (§5.9) |
| `review-probes/rq`, `lb`, `z-rq`, `z-bd` | rows right, with R05 and R18 the limits §9 states, and BG10 writing its report (`results/cut-final-*`, `results/z-*-final*`) |
| `review-probes/fourth/` S1 to S7, D2, X1, H1, the consumer's rows, the ingest | every shape writes its report, each file on its own row; 4 of 4 calls; X1 and H1 as published; the ingest as published (`results/fourth-review-shapes-final.txt`, `results/fourth-review-*`, `results/ingest-final.txt`) |
| `review-probes/seventh/`, the narrow reviewer's 80 consumer shapes, and BDDfy, plain xUnit v3 and LightBDD with bars on and off | every consumer shape reads as published or as the reviewed `2720adf0`, and where it differs from published a call gains its step, but for `cleared-then-run-again` and `dropped-sub` (§9); the other three read as `2720adf0` (`results/seventh-review/`) |
| `repro-every/` | as §5.4 says, every feature writing its report (`results/every-final.txt`) |
| Triggers on Reqnroll xUnit v3, TUnit, floor 3.0.1 | as §1 says, at `638f275d` (`results/lane-*-proto.txt`), and published on each the same day (`results/lane-*-pub.txt`) |

None of the examples holds a hook step, so they show that nothing broke, not that the fix works in those lanes.

**Not run, and required before the tag:**

- §6's facts, which are the release's proof and do not exist yet, and the mutation pass over `StepPairing`, the stamp and the guess's input that the release plans use.
- The full Playwright suite and §6.4's new class. Only `paint-probe.js` has looked at the page, on both report pages. Scenarios with no hook step give the page the same data, so the existing suite should add little, which is an inference and not a measurement.
- Kronikol4J parity, and the ledger entries §5.9 names.
- Linux. Nothing here is platform dependent, which is an inference.
- A real consumer's suite (BreakfastProvider) on this prototype alone: kronikol-ca ran it with its layer on top (§5.8). #222's shape suites on the scratch merge are kronikol-af's, above.
- R2 alone after R1 as a separate build: the final code is R1, R2 and R3, and R1 alone is cut from it, so R2's only unmeasured state is without R3, which touches LightBDD alone.

**As executed: R1's proof, on the commit it released as 5.29.3.** §6's facts exist and are in `tests/Kronikol.Tests.RealEngines/`, its fixtures, and the unit classes named in §6.1 and §6.2. Each was run red on the published packages or on the code before its fix (`results/execution/red-*`). A fact that read right before the fix, as a pin, is not counted as red: `HookStepTests`' six, whose rows are built by hand, and the sibling shapes BG1 to BG13 and WB1 to WB11, which guard what an earlier cut broke and which `M56` shows reach the guess.

| What | Result |
|---|---|
| `Kronikol.Tests` | 12,209 passed, 2 skipped, 0 failed |
| `Kronikol.Tests.StepTracking`, `.LightBDD.xUnit3`, `.xUnit2`, `.TUnit`, `.xUnit2`, `.xUnit3`, `.NUnit4`, `.MSTest`, `.TUnit` | 2,091, 35, 11, 26, 111, 33, 49, 85, 50 passed |
| `Kronikol.Tests.RealEngines` | 284 passed, weaving with the published 5.29.0: Reqnroll's four lanes (xUnit v2, xUnit v3, TUnit, the floor) 51 each, BDDfy 38, LightBDD 7, the plain lane and the files lane the rest |
| Every example lane | all passed, Integration 265 of 265 |
| The whole Playwright suite | 2,141 passed, 28 skipped, 1 failed, with `HookStepTests` among those that passed. The failure is `BackForwardRestoreTests.R16` in WebKit, a scroll position under load and a known flake: it read 3 of 3 engines on each of three runs alone and its class 25 of 25, and this release touches none of the page's scripts. The run before the third review's fixes read 2,142 passed, 28 skipped, 0 failed (`results/execution/playwright-whole-*`) |
| BreakfastProvider at `640c190`, four lanes | no failures on any lane, published 5.29.2 against a preview of this source. Every call on the same row: ReqNRoll 1342 of 1342, BDDfy 1398, NUnit 1398, LightBDD 1359 of 1368, the other 9 gaining a step. BDDfy's trees differ in 77 scenarios, each gaining the assertions made after its first await, #122's second point, and LightBDD's in one, by a date in an assertion (`results/execution/breakfastprovider-r1-final-*`) |
| Mutation over the merged code (`execution/mutate_r1_on_222.py`) | every mutant caught but M55, which is equivalent: `results/execution/mutate-r1-*` |
| Linux | CI's Ubuntu lane on the commit, ahead of the tag |

---

## 7. Releases

All **patches**: bug fixes that change observable behaviour, nothing new for a consumer to call, every new member internal. State the behaviour changes in the changelog, and record the report output
in the Kronikol4J divergence ledger. No version is named until each lands on origin, as peers release in between.

| Release | Holds | Closes |
|---|---|---|
| **R1** | §5.1 to §5.4 for all three adapters, #222's 5.4 and 5.5, §5.5's per-level frame key and stopwatch and its outcome rule (they belong to nested frames), the docs | #223. #147 stays open and whole: T20 is unchanged |
| **R2** | The rest of §5.5: the engine's events, the steps that did not run, and `query`'s mark for them | |
| **R3** | §5.6 | |

The prototype's stages (`prototype/apply_r1.py` to `apply_r14.py`) are the order things were tried, not these releases. **RUN at 5.28.2, R1 alone**, cut from the final code by `prototype/split_releases.py` (`proto/223-r1-final3`, `ed8cd528`). It differs from the final code only in what R2 owns (T05, T06, T07, T08, T13, T15, T18, T26, E07, E08, R03, R13: rows that did not run, T15 and R13 absent, T06 no steps, and `query`'s `-`) and R3's (L1, L5, and F14's calls at a LightBDD scenario's end, which vary from run to run on published too). On the triggers it differs from published in T01, T02, T03, T04, T05, T11, T13, T17, T21, T23, T24, T25 and T26, each wrong on published (`results/cut-r1-*`). S1 to S7 all write their report (`results/fourth-review-shapes-r1-final.txt`). Its suites are green: core 11,824 passed, 2 skipped, 0 failed, and every adapter's (`results/suites-r1.txt`). So R1 is a release on its own. An earlier version of this paragraph said "nothing measured gets worse" of a cut that lost reports, and it was false. This one rests on the shapes above, it has one stated exception (M1, §9), and a shape nobody has thought of stays possible, which is what §6's facts and the mutation pass are for.

R1 ships the three adapters together. **R1's core alone would regress BDDfy and LightBDD**: their sub-steps would carry ids while their top-level steps did not, so a call under a top-level step
would resolve to nothing. That is INFERRED from the code, and it is why the pairing is not split by adapter.

**#129 stays open.** It was in R1, then R3, and the reviews measured why it should leave. Forcing the lazy attribution before the diagnostics copy is a small change (**RUN** `prototype/apply_r7.py`). The walk then flags healthy runs: `run.degraded`, a banner on every `query` answer and a new labs page, on a tests file's background step, a long or two-line step (F28), Cucumber's `--include-hooks` with a Background, a tests-file assertion before the first step, a Background's text inside a step's text, a step with no text, some unstamped bars in process and some hand-built rows, and a BDDfy `Setup` that fails. And each fix tried for those brought a regression of its own: a pass-over over background rows lost the walk's place on a store projected from an in-process run (S8, S9, P, F), and a looser matcher refused unescaped bars and matched bars a row did not draw (G, H, I, J, R, S), so that calls published read rightly, or read as no step, read wrong (`REVIEW.md`, `results/fifth-review/`, `apply_r11.py`, `apply_r12.py`). #129 asked for a real suite's silent loss to show, and R1 ends most of that loss in process, where a suite has it. What is left is an ingest's, a hand-built scenario's, and in process a later example row's or a step still open at report time (F18, §9), which read no step with nothing to say why. It belongs after #225's R2, which now holds #128 alone, as #222 dropped its copy of the matcher. That R2 has been redesigned since these reviewers measured its first form, and it also takes on what draws no bar, so that plan holds the current form and what it measures. Neither plan forces the read, and that plan's Q11 offers it as a patch straight after that R2. **RUN by kronikol-ca** on that R2 alone with the read forced: BreakfastProvider's four lanes all pass, no call moves, and the only flags are 20 on the LightBDD lane, all F30.

---

## 8. Edit sites

A list of what was thought of, not of what the change touches: run the audit checklist after.

| File (at `27cc0e46`) | Change |
|---|---|
| `src/Kronikol/Tracking/StepCollector.cs` | §5.1, `OpenStepOf` |
| `src/Kronikol/Tracking/StepPairing.cs` *(new)* | §5.2 |
| `src/Kronikol/Reports/ScenarioStep.cs` | `CollectedId`, `OutsideFrameworkStep`, `DidNotRun` |
| `src/Kronikol/Reports/AttachmentPlacement.cs:136` | a walk visits a step once (#222's 5.5) |
| `src/Kronikol/Reports/StepKeywordCollapser.cs:8-11`, the render step's summary in `ReportGenerator.cs` | comments that say the rows are shared |
| `src/Kronikol/Query/QueryCommand.Narrative.cs`, the `mark` in `steps`; wiki `Querying-Reports.md`, the skill's `commands.md` | R2: `-` for a skipped row, which is what its status says, so a LightBDD step that ran and then ignored itself reads `-` with its call under it (M3) |
| `src/Kronikol/Tracking/RequestResponseLog.cs`, `RequestResponseLogger.cs:88` | `OpenStep`, the stamp |
| `src/Kronikol/Reports/BackgroundStepsDetector.cs:30`, `:40-41`, `:44-51`, `:70` | reads only what a framework step ran, own rows, removed by place (§5.4) |
| `src/Kronikol/Reports/ReportGenerator.cs:5381-5441`, `:5448` | §5.3 |
| `src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs`, `ReqNRollStepInfo.cs`, `ReqNRollConstants.cs`, `ScenarioInfoEnumerableExtensions.cs:62-64`, `:100-126` | frames, pairing, R2 |
| `src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs`, `ScenarioInfoExtensions.cs:79`, `:104`, `StepExtensions.cs`, `BDDfyScenarioInfo.cs:65` | frames, pairing |
| `src/Kronikol.LightBDD.Core/StepTrackingStepDecorator.cs`, `FeatureResultExtensions.cs:94-96`, `:211`, `:263-268` | frames, pairing, `EndedAt` |
| `src/Kronikol/Tracking/TestTrackingMessageHandler.cs:455` | F13 |
| XML docs that become false | `AttributeInteractionsToStepPaths` ("Attribution is positional"), `OrderedStepPaths`, `DiagnosticKind.StepAttributionMismatch` (ships in the package; in-process lanes no longer record it), `StepCollector`'s class summary |
| `src/Kronikol/Query/QueryCommand.Narrative.cs:444`, `Query/ReportScanner.cs:874` | "(before the first step, or attribution was not trusted)" also covers after the last, and between |
| Wiki `Step-Tracking.md:699`, a new "step methods outside a framework step" section | also the T16 limit, and that a `[Step]` belongs to the frame it finds open |
| Wiki `Generated-Reports.md:426` (`stepPath`), `Diagnostics-and-Debugging.md:111`, `Background-Thread-Correlation.md:498`, `Integration-ReqNRoll-*.md` | the same |
| `CHANGELOG.md`, Kronikol4J ledger, `plans/PLANS_STATUS.md` | per release |

---

## 9. Risks

- **The stamp is on the hot path.** One dictionary read, one lock and one record copy per logged record. **RUN** `hot-path/`, microseconds per logged pair, published against prototype: with a step open, 1.4 to 1.9 against 2.9 to 3.5, so about 1.5 µs per call; with none open, within the noise; eight threads on one test cost what one does, so the lock does not contend. A run of 10,000 calls pays about 15 ms (`results/hot-path.txt`).
- **Hook steps change some reports' addresses (§5.4).** Only scenarios with such a step, which are wrong today. A consumer's scripted `s1/0` moves. The changelog says so.
- **More rows in failed and skipped Reqnroll scenarios (R2).** Every such scenario gains the steps that did not run, and `ExampleValueGrouper.BuildStructured`, which reads the framework's steps, sees them. **INFERRED** an improvement, as a failed outline row today groups without its later steps' tables. Pin it.
- **An adapter calls core's internals.** `StartFrame`, `CompleteFrame` and the rest are called across packages, and an adapter depends on `Kronikol` as a minimum, so a consumer can float core ahead of an adapter. A later change to one of those signatures (#224 adds `phaseKeyword`) would then throw `MissingMethodException` inside `BeforeStep`, failing every step. Precedent exists (`RowName`), and packages ship at one version, but whoever changes one keeps the old overload (**READ** the nuspec, not probed).
- **An event handler that throws fails the step.** `PublishEventAsync` runs handlers inline, so the three new handlers catch (`Quietly`), because a tracking fault must not fail a test. The attachment subscription's `try` only wraps subscribing, and that handler's own body does not catch.
- **Sub-steps can swap in an overlap (§5.1).** A `[Step]` left running that fails can paint a later sub-step failed, with its message, until #225's R1. Rows stay right, and the verdict is the runner's. Published does this too, with its rows wrong as well.
- **A hook's `[Step]` that awaits a Gherkin step** reads as left running, not as its parent (**RUN** `results/review-rq-*`), so a call it makes afterwards reads the step before it. Narrow. #225's flow reading gets the call right (`/seed-after` reads 0, against 1 here) and does not settle the row, as the frame's hook does not see that flow (**RUN** by kronikol-ca).
- **Duplicate discovery.** Examples deliberately list both binding assemblies (#71). The owner guard stays on the hooks, the subscription is once per publisher, and the shared method is idempotent. Covered by `examples/Example.Api/tests/Example.Api.Tests.Integration/Tests/ReqNRollDuplicateStepsTests.cs`.
- **A call by a step left running reads the step open when it is made.** The stamp reads the stack's top, so T23's `/background` reads the `When` it landed in. kronikol-ca's LightBDD
  shape (a sub-step left running past its step) reads `1` here, `0` on published and `0.0` on #225's flow reading. Published's `0` comes with every later call also under `0`, so nothing
  this plan measured gets worse overall, but the single call does read differently until #225's R1 (`0`, and `0.0` with its R3).
- **The guess itself stays until #222's R1.** A Background that is written and not marked, a prefix marked that is not written, a layout that depends on who ran (its F1, F2, F6). A failed Background row now reads `✗` where published read `✓`, in a section that starts closed until its 5.6 (its F12), with the scenario's failure block on the page as today.
- **A false `Re-run` line on a scenario newly listed from an after-hook (R2).** `FailureText.RerunCommand` reads the first frame with a file, and in a hook's error that is the binding, so `FullyQualifiedName~Rq5.Driver.Cleaning_fails` selects no test. BDDfy has the class on published. Not fixed here; the digest would need the test's own name, worth an issue of its own.
- **Open states between outline rows sync by index.** `selectRowStepPath` (`report-select-row-function.js`) can point at another row when a hook runs on only some rows. Cosmetic: it decides whether a step starts open, and nothing else joins a row to a bar by index (**READ** by the fourth review).
- **What a consumer does to collected rows that keep their ids.** A row in another order than the run reads by its stamp, which is better than the walk's answer but differs from it (`rows-reversed`). A call in a sub-step the consumer dropped reads no step, where the walk read its parent (`dropped-sub`). With `StepCollector.ClearSteps` between two runs under one id, the first run's calls read no step, where the walk read them under the second run's rows (`cleared-then-run-again`, which has no caller in `src`). All narrow (`review-probes/seventh/rv7-con`).
- **A LightBDD retry written as a scenario decorator gains a row.** LightBDD has no retry of its own, and a suite's decorator that invokes the scenario again runs its steps with no step decorators, so nothing marks a second run: its `[Step]` is a step method outside a framework step, listed as one, `Then`'s address moves by it, and a call after it reads it as the step before. Published's tree is right there and the calls read no step. Narrow, and stated, not fixed, as nothing tells it from a `[Step]` between two steps (**RUN** M1, `review-probes/fifth/lb7-src`, `results/fifth-review/lb7-*.txt`).
- **A BDDfy test run more than once under one id** lists its first run, as published does, with the later runs' calls reading no step (F18, F24). A retried test that passes reads failed. Not this plan's, and worth an issue of its own.
- **The public stopwatch key.** `ReqNRollConstants.StepStopwatchKey` stays set on the scenario as well as on the step context (§5.5).
- **A top-level step still open at report time, with no frame opened after it** (a plain lane, or a fire-and-forget `[Step]` in `AfterScenario`) is in no list, as `GetSteps` reads completed steps only. Unchanged, and rare. A before-hook's is parked when the first frame opens, and listed passed with no duration (T24). #225's slot-at-start design resolves the rest.
- **Gaps the final review measured, stated and not fixed** (`results/execution/`, the wiki's limits). A hook that runs a Gherkin step through `ITestRunner` ahead of a written Background leaves that scenario's Background steps among its own, as a row that disagrees with the document ends the Background (H6, H7). A Gherkin step that starts another through `ITestRunner` without awaiting it leaves the next step under it. A `[Step]` method that skips a TUnit test reads failed on a scenario that reads skipped, as all a step method hands on is the exception's message; under xUnit v3 the marker reads skipped.
- **A consumer's rebuilt rows.** A row rebuilt with `new` before the reports are written leaves the scenario's rows paired by position and its calls read against the bars, as published. Rebuilt with `with`, it keeps its identity. A row removed from the list stays removed.
- **A BDDfy step that is `async void`, or any async step with `Configurator.AsyncVoidSupportEnabled` off,** closes at its first `await`, as BDDfy gives no task to wait on: its `AsyncTestSyncContext` is internal. What it opens after that reads as a row of its own, where published's double pop could leave it under the step.
- **A row with a `NotRunAfter` id behind a frame under a step method** can stand after a step method that ran later than it, as the walk waits for the next framework step. The third review traced it on a fuzz sample, and the code before the fixes misplaces the same row by another route.

---

## 10. Open questions

These are the owner's decisions. Everything else is settled above, or is measurement left for execution.

**Q1. Where does a step method outside a framework step go?** Recommended: **in the list, in the order it ran, with its bar**, and after a Background where one is found (§5.4). The alternatives: the issue's "Before scenario" wrapper (invented text, addresses still move, §5.7), or a separate
section with `h0` addresses, which keeps Gherkin addresses for the price of new surface, which would make R1 a minor.

**Q2. Should those steps appear in Specifications as well as the test run report?** Recommended: **yes, for now**. **RUN**: `Specifications.yml` and the page list the row first, keyword-less, with its bar (`results/specifications-final.yml.txt`, `results/paint-specifications.txt`). They would need a new data key and a filter to leave out, and the Cucumber lane's `--include-hooks` does not filter either. The case against: the
Specifications page is a living document, and the Gherkin as written has no such line.

**Q3. Should R2 list Reqnroll's skipped and undefined steps?** It is not in #223, so it is not assumed. Recommended: **yes**. LightBDD and the Cucumber lane list theirs, `priorFailure` shows it was meant, and T05 otherwise reads as a scenario with one step. Neither of those lanes runs the Background guess, which is why it took §5.4 to make this safe here. It adds rows to every failed or skipped
Reqnroll scenario, and no sibling moves. Saying no leaves the rest of R2 intact.

**Q4. In what order against #225 and #222?** Recommended, if all are taken: **#222's R1, then #223's R1, then #225's R1**, with #225's R2 at any time. Against #222 either order works, as each carries its 5.4 and 5.5, whichever lands first shipping them (§5.8). #222's R1 first is the order the scratch merges measured, and it asks its own question (its Q1) first. Against #225 there is no choice while both are taken: its R1 needs this R1's stamp and pairing. If this plan is declined, #225's R1 brings them itself.

**Q5. An undefined or pending step fails the run and reads as skipped in the report (F17, F26).** `UndefinedStep => Skipped` in `ExecutionStatusExtensions.cs`, while the runner reports a failure. Recommended: **fix it separately**, not in these releases: it changes a verdict for scenarios no hook is involved in, and it should
follow what Reqnroll's `missingOrPendingStepsOutcome` makes of it, which was not researched here.

**Q6. Is R1 acceptable without the flow, if #225 is not taken?** Recommended: **yes**. Rows are right without it. What stays inexact: a call made by a step left running, or by a hook's step after it awaits a Gherkin step, reads the step open when it is made, which can be a later row (§9), and sub-steps' end times and status can cross in an overlap (§5.1). Published has those as well, with its rows wrong too. The alternative is to commit to #225's R1 as well, straight after this one, as it cannot land first (§5.8).

---

## 11. What it leaves

#225 (a call under a sub-step, and sub-steps' end times and status in an overlap), #222 (a background's first copy and F3, a file attached in a shared step), #147 in full (a call after the last step stays under it), #129 (§7), #128 itself, which #225's R2 closes with its matcher, on clean main too (**RUN** by kronikol-ca). R1 fixes a long label only for steps with ids (P6); the issue's own repro builds its steps by hand and reads as published (**RUN** by kronikol-ca at the ninth stage). It needs a comment, T16 and T22 (limits, §5.5), #224 and #195 (phases), T19 (`Task.WhenAll` nesting, with #225), F16 (dead public plugin, removal is a v6 break), F18 and F24 (a BDDfy test's later runs, and a retry's verdict), #122 (BDDfy's async steps close
at their first `await`, which R1's left-running rule meets but does not fix).

---

## 12. Log

- 2026-11-02: written by kronikol-fe at 5.27.13. Issue reproduced on published packages. Twenty-one Reqnroll scenarios, five BDDfy, nine LightBDD. Prototype in `C:/Code/Kronikol-223p`, branch `proto/223-hook-steps`, a commit per stage; scripts and patches in the harness. Shape agreed with kronikol-ca
  (#225). Nothing committed on this branch, nothing pushed.
- 2026-11-02: the first design reviewed independently (`REVIEW.md`): 12 findings, the sentinel dropped, a process-wide id, frames parked and closed by identity, nested steps per level.
  Probes added: T21 to T24, B5 to B7, P0 to P4, `repro-every/`. The prototype's second cut is R4, and it fixes a regression the first prototype had (§9). Not green-lit, nothing committed or pushed.
- 2026-11-02: third cut, from kronikol-af's review of the same shapes on #222: T25, T26, T27 added; a nested frame goes through the adapter's mapping, a parent reads the scenario's failure, a skipped inner step lists under its parent, and a row no framework step opened carries a mark. Not green-lit, nothing committed or pushed.
- 2026-11-02: fourth cut, from a second independent review (`REVIEW.md`), which measured a plain-lane regression and a hole for non-`async` wrappers, both in a narrow copy of #225's flow rule. The flow left this plan: only `CompleteFrame` closes a frame, and plain lanes run the collector as published. The numbers in §6.5 are this commit's (`abf7b0a3`). Not green-lit, nothing committed or pushed.
- 2026-11-03: checked again at 5.28.2, on the owner's word to make sure it is complete and accurate and to do any deep dive that might change it. A line-by-line audit found 31 mismatches, the worst that "rows and calls are right" overstated what R1 gives alone (Q6), and that this plan had no word on #224. A third independent review, the first to see the fourth cut, measured regressions in R2 that every suite passed: rows that did not run reached the Background guess, so one failed or skipped scenario rewrote its passing siblings and could lose the whole report, in a green run too. The fifth cut: the guess reads only what a framework step ran, which also ended the moved sibling and the hook's lost report without #222 (`apply_r9.py`). kronikol-ca measured a BDDfy example table tripling, so a test's later runs are no longer listed (`apply_r10.py`). Measured for the first time: R1 alone, #129's order, the triggers on Reqnroll's other lanes and its floor, a scratch merge with #222's R1, Specifications, the stamp's cost. New probes: E01 to E16, a retried BDDfy test. Not green-lit, nothing committed or pushed.
- 2026-11-03: the fourth review, on the code that check had called final (`REVIEW.md`), measured a regression in suites that are green on published: the fix puts a file on the row the guess shares, and that row stops the report. Five more faults, eight false claims. So R1 now carries #222's 5.4 and 5.5, in its words, as a guard alone shows another scenario's file; a call is read by its stamp only over top-level ids; by-position rows carry none; #129's order moves to R3, with a fix for the false mismatches on an ingest it would otherwise flag. `OwnBackgroundSteps` is gone. kronikol-ca, measuring under its layer, found that R11's pass-over broke a Cucumber fact and that its matcher lost labels: R12 marks the row where the tests file is read, and takes #225's R2's matcher. Both peers measured the result on their own plans (§5.8). 
- 2026-11-03: the fifth review, on `306557a4`, lost no report, and measured six regressions in what a report says: four in what R11 and R12 added for #129, two in R1. So #129's work leaves this plan and #129 stays open, as each fix for its false flags brought a fault of its own (§7). A stamp that names no row of its scenario now reads as no stamp (§5.3). The LightBDD retry decorator stays, stated (§9).
- 2026-11-03: a narrow review of that last rule measured it regressing live adapters (`REVIEW.md`). It went, and a scenario with a row that ran and carries no id is read by the walk whole, so it reads as published by construction. That last change was checked with the reviewers' own probes and the suites, and has had no fresh reviewer.
- 2026-11-04: the owner's word to implement the plan in full. All of §6 written and proved red on the published 5.28.2 packages (`results/execution/red-*`); R1 merged onto #222's R1 and its R4 (Q4). The merge kept #222's `OpenStep` chain, where the plan expected frames alone.
- 2026-11-05: a final independent review of the merged code found four narrow regressions, each fixed and proved red then green: a hidden BDDfy `Setup`'s files lost (F1 to F3, `FilesNotListed`), a consumer's rebuilt or removed row through `CreateStandardReportsWithDiagrams` (F5, `OpenedAsFrame`, the any-row rule), `ClearSteps` with rows still listed (F4, `DidNotRun` means no frame), and a hook's `[Step]` that skips under xUnit v3 reading failed with the marker (X2). The three gaps it found beyond those are in §9.
- 2026-11-05: mutation on the merged code (`execution/mutate_r1_on_222.py`, 52 mutants): all caught but M28, which showed BDDfy's own `Listed` filter had no job left (removed), M35, for which a fact was added, and M55, equivalent (a duplicate native lists no row by the pairing rule). A catch reported as `BUILD` was a compile break of one of my facts, and those were run again. An earlier pass on the code before the merge caught 36 and M13 survived, which added a fact.
- 2026-11-05: BreakfastProvider at `640c190` on a preview of R1 over #222's R1: ReqNRoll 214, LightBDD 187, BDDfy 212 and NUnit 212 all pass, published against preview. By the row a call's step path names, ReqNRoll 1342 of 1342 calls on the same row, LightBDD 1359 of 1368 and the other 9 gain a step they had none for, BDDfy 1398 of 1398, NUnit 1398 of 1398. ReqNRoll's 64 calls with another address and 10 trees that differ are all #222's: a shared opening step in a feature that writes no Background, no longer read as one, which renumbers the rows. Three counts differ, each a background or retry call of the run's own wobble.
- 2026-11-05: a second final review, on the merged code with the first review's fixes (`REVIEW-FINAL-2.md`), measured six regressions in what a report says, none stopping a report or changing a verdict: a BDDfy async step's inner steps as rows (§5.1), a rebuilt row beside a nested Gherkin step, a row listed twice in a BDDfy list not in run order, a held call rewinding what followed it, a BDDfy file listed twice by position, and a BDDfy story inside a step losing its rows. Each fixed and proved red then green (`results/execution/red-second-review-*`), with its claims corrected above.
- 2026-11-05: a narrow third review of those fixes (`REVIEW-FINAL-3.md`) measured three regressions in them and one shape a fix still missed: rows that read alike became one row by position (`Except` on a record), a file on a frame past the last row was lost by position, which took a hidden `Setup`'s file from a test that threw with no consumer's hand in it, a step stayed open under an executor around Kronikol's that does not hand BDDfy the task, and a list by kind whose frames still ascend counted as in run order. Each fixed and proved red then green (`results/execution/red-third-review-*`), M73 to M83 are the mutants for those rules, and the reviewer's own shapes read right on the fixes (`review-probes/final-3/fix3-rows.txt`).

