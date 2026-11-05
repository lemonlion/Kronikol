# Background as written plan: #222

**Written:** 2026-11-02, at 5.27.13 (`v5.27.13`, `c58d98c0`; main at `27cc0e46`). **Checked again:** 2026-11-03, on main at `9d994a4b` (5.28.2), where the deep dives in §12 changed R1. **Status: executing.** R1 is 5.28.6, at the owner's "implement the plan in full" of 2026-11-04, and R4 follows it, with an issue of its own. R2 is dropped, as #223's R2 lists those steps, and R3 waits for v6, by a line in `plans/V6_PLAN.md`. The sections below keep the plan's words, written before it ran, and §12's last entries say what its execution changed.
The fix was first tried in throwaway worktrees, and its patches are kept in the harness.

Evidence labels: **RUN** (measured here, against the published 5.27.13 packages unless the line says otherwise), **READ** (in
the source, `file:line` at `9d994a4b`, or Reqnroll's at tag `v3.3.4`), **DOC** (a project's own documentation, read
2026-11-02), **INFERRED** (reasoned from facts, stated by none), **ISSUE** (taken from #222, not re-measured). Every RUN line
has its probe and its output in [`BACKGROUND_AS_WRITTEN_PLAN.harness/`](BACKGROUND_AS_WRITTEN_PLAN.harness/README.md).

#222 reports that scenarios sharing their first steps all show the first scenario's copy of them, so a failed step reads as
passed and an outline row shows the first row's table. It asks for each scenario to keep its own steps, with "the background
marking, the `b0`, `b1` addresses and the layout" left as they are. The report is right. The suggested line is the right edit
to that method, but it treats the symptom, and what it asks to keep is the larger fault.

## 0. Summary

1. **The underlying problem is that the report guesses a Background it can read.** `BackgroundStepsDetector` answers "do
   these steps read alike?". The report, its data file's schema, its tooltip and the wiki all say the answer means "the feature
   file wrote these in a `Background:`". They are different questions, and the code then treats reading alike as being the same
   step, which is how one scenario's results reach another. The true answer has been in the process all along: Reqnroll embeds
   the Gherkin document in every `FeatureInfo`, `ExamplesBlockResolver` already reads it, and the Cucumber lane already decides
   from it ("explicit, never guessed", in the wiki's own words).
2. **The guess is wrong in almost every shape, not only the one reported.** On a matrix of 20 feature shapes (41 scenarios,
   every one passing), 38 scenarios are wrong in some way. A real `Background:` goes unmarked in 25 scenarios, in 21 of them
   followed by a `When` or a `Given`, the shape Reqnroll's documentation teaches. Steps nobody wrote in a Background are marked as one, a `When`
   and a `Then` among them: 11 scenarios. 8 show another scenario's steps. The guess is right only where a Background is followed
   straight by a `Then`.

   | Scenarios, of 41, on a green run | 5.27.13 | The issue's suggestion | This plan's R1 |
   |---|---|---|---|
   | A Background is written and not marked | 25 | 25 | 0 |
   | Marked as a Background, none written | 11 | 11 | 0 |
   | Drawn with another scenario's steps | 8 | 0 | 0 |
   | Wrong in any way | 38 | 36 | 0 |

3. **Sharing the objects does more than mislead: it can lose the whole report.** A file attached inside a shared step makes
   report generation throw `KeyNotFoundException`. All 41 tests pass, `dotnet test` exits 1, and no report is written. The same
   input stops `kronikol ingest` with an unhandled exception and exit code 0xE0434352 (Git Bash shows 127). Live since 4.27.2, unreported.
4. **The issue understates what the copy costs.** `Failures.md`, the file everyone is told to read first, loses the failing
   step altogether: `failingSteps` is empty and the calls are no longer narrowed to the step. The reverse of the issue's title
   also happens: a passing scenario shows a failed step, when the failing scenario sorts first. And the layout changes with the
   outcome: one scenario failing moves another, passing scenario's steps from `b0, b1, b2, 0` to `0, 1, 2, 3`.
5. **The issue's title says Reqnroll, but the ingest lane has the same fault.** `kronikol ingest` of a tests NDJSON calls the
   same detector, with the same result.
6. **Found on the way, and not this issue's: a failed run can read as clean.** A suite's `[AfterScenario]` hook that throws loses that scenario from the report, and `Failures.md` can then open "# No failures" while `dotnet test` reports a failure (F16). A small fix is measured on its own (R4, Q9).

**What this plan does:** in the Reqnroll lane, read the Background from the document, give every scenario its own steps, and
stop guessing (§5.1 to §5.3). Fix the detector's copying and the crash for every caller (§5.4, §5.5), keep a failed step in sight (§5.6), and fix the smaller faults found on the way (§5.3, §5.6). All in one patch that
closes #222 (R1). It adds, removes and renames no public member or option (RUN: `results/public-surface.txt`, every public and protected member of the two assemblies against published 5.28.2), and a configured value, a toggle record handed in or a scenario built by hand behaves as it did (§7). Then, each only if the owner says go (§10): list the steps Reqnroll skipped after a failure (R2, dropped if
#223's plan does it), stop the guess in the ingest lane too (R3, recommended for v6), and keep a scenario a suite's hook stopped ours from collecting (R4, dropped if #223's R2 is taken first). The prototype of R1, on today's main, took every wrong row to zero in all five modes on
xUnit v2, xUnit v3, TUnit and with row tests off, and on Reqnroll 3.0.1 and 3.3.4, the newest. It ends the crash in both lanes, passes the core suite, all 265 facts of `Example.Api.Tests.Integration` and the whole Playwright suite but for a known flake (§12), leaves all 41 scenarios' diagrams as they were, and takes
BreakfastProvider's 10 invented marks to none with its 214 tests passing. It also runs with #223's prototype under it (§7).

**What it costs:** a visible change. A real Background becomes marked where it was not, a shared prefix stops being marked where
no Background was written, and those steps' addresses move (`b1` to `1`, `0` to `b0`). History and `query diff` do not notice
(§9). Q1 asks the owner to confirm that this is wanted, since the wiki documents the guess as a feature.

## 1. How far each claim was checked

| Claim in #222 | Verdict | Level |
|---|---|---|
| The detector hands every scenario `members[0]`'s step objects | Holds. `BackgroundStepsDetector.cs:44-51` | READ, RUN |
| So a later scenario shows the first's result, table, sub-steps and parameters | Holds. `s9` shows APPLE, customer-2 and a passed `When`; it added BANANA for customer-10 and its `POST /payments` got a 402 | RUN |
| ... and duration, attachments and doc string | Holds. Attachments go further: F3 | RUN |
| The scenario fails "with no failed step in its list" | Holds, and the digest loses the step too: F4 | RUN |
| A row of an outline with its placeholders in a table shows the first row's | Holds. The BANANA row shows APPLE in all three steps | RUN |
| It happens where the next step is a `Then`, `And` or `But`, or there is none | Holds. `BackgroundStepsDetector.cs:40-42` | READ, RUN |
| A step Reqnroll skips after a failure isn't in the report, "for a reason of its own" | Holds. Reqnroll runs no step hook for it (F13), and the Cucumber lane does list them | READ, RUN |
| With the suggestion "only whose results and arguments are drawn changes" | Holds as a description, and is the problem: `✗ b2 When` stays marked as a Background step | RUN |
| "the background marking ... stay as they are" is what should happen | **Does not hold.** The marking is wrong for 36 of 41 scenarios (§0) | RUN |

## 2. Where it stands today

### 2.1 What "background" is said to mean

Everything a reader meets says a Background step came from a `Background:`:

- The data file's schema: "Steps from the Gherkin Background:, addressed b0, b1, ... by stepPath" (`ReportGenerator.cs:7851`).
- The separate section's tooltip: "The steps of the feature's background, which run before this scenario's own"
  (`ReportGenerator.cs:4371`).
- The stylesheet: "A step that came from the feature's Background" (`stylesheets.css:1012`).
- The wiki's Cucumber page: "`Background` steps | `Scenario.BackgroundSteps`: **explicit**, never guessed"
  (`Integration-Cucumber-Messages.md:45`, wiki `c9e0698`).
- The wiki's Step Tracking page, on keyword-less steps: extracting a common prefix "would invent structure the run never had"
  (`Step-Tracking.md:468`).

The same page also says the detector does the job: "If you use Gherkin `Background:` blocks ... The detector identifies them as
a common prefix and extracts them into `Scenario.BackgroundSteps` automatically" (`Step-Tracking.md:460`). RUN: it does not.

### 2.2 What the Reqnroll lane does

`ToFeatures` maps each scenario's steps, then runs the detector over the feature (`ScenarioInfoEnumerableExtensions.cs:62-64,80`).
The detector groups by rule and takes the longest prefix matching on keyword and text. It gives up if any scenario starts with
`And` or `When` (`BackgroundStepsDetector.cs:28-31`), or if any scenario's next step is a `Given` or a `When` (`:37-42`). It then hands every scenario the
first's objects (`:44-51`).

History explains the shape. READ: the detector arrived on 2026-05-10 (`9f6a9257`, v2.33.46), and the adapter first read the embedded
document on 2026-08-29 (`ef9b370a`). Two days after it arrived, a false-positive fix made it skip scenarios starting `And` or `When`
(`eddc1219`), and on 2026-05-17 another made it skip when the next step is a `Given` or `When` (`27da10f9`). That second rule removes the
false positive by removing the textbook case. DOC: Reqnroll's reference says "A `Background` ... can contain one or more `Given` steps",
and in its own example every scenario after the Background opens with a `Given` (`docs/gherkin/gherkin-reference.md:305-343`). By the
detector's rule, that example marks nothing. A unit fact pins the rule (`BackgroundStepsDetectorTests.cs:27-43`). Sharing the objects was
then taken as a design fact: "background steps are shared by reference across every scenario of a Rule group"
(`StepKeywordCollapser.cs:8-9`, and `ReportGenerator.cs:4469-4470`).

### 2.3 The measured matrix

RUN (`results/shapes-*.txt`): "marks" is how many steps the report addresses `b0, b1, ...`. "R1" is the prototype.

| Shape | Written | 5.27.13 marks | Suggestion marks | R1 marks |
|---|---|---|---|---|
| 01 Background, then `When` | 1 | 0 | 0 | 1 |
| 02 Background, then `Given` | 1 | 0 | 0 | 1 |
| 03 Background, then `Then` | 1 | 1, second borrows | 1 | 1 |
| 04 None; two scenarios share `Given`, `When` (the issue's) | 0 | 2, second borrows | 2 | 0 |
| 05 None; outline, placeholders only in a table (the issue's) | 0 | 3, later rows borrow | 3 | 0 |
| 06 Background, one scenario | 1 | 0 | 0 | 1 |
| 07 Feature's and rule's Backgrounds | 2, or 1 | 0 | 0 | 2, or 1 |
| 08 Background, then `When` | 1 | 0 | 0 | 1 |
| 09 None; shared `Given`, `And`, `When` | 0 | 3, second borrows | 3 | 0 |
| 10 Background, then an outline | 1 | 0 | 0 | 1 |
| 11 A Background step that runs a step | 1 | 0, and the list is wrong (F11) | 0 | 1 |
| 12 Background in German | 1 | 0 | 0 | 1 |
| 13 Background and steps written `*` | 1 | 0 | 0 | 1 |
| 14 None; a doc string differs | 0 | 1, second borrows | 1 | 0 |
| 15 Background, one scenario with no steps of its own | 1 | 0 | 0 | 1 |
| 16 Two files, one title; one writes a Background | 1, and 0 | 1, and 1 borrowed | 1, and 1 | 1, and 0 |
| 17 Background, then `Then` | 1 | 1, second borrows | 1 | 1 |
| 18 None; a shared `Given` | 0 | 1, second borrows | 1 | 0 |
| 19 Background with a table | 1 | 0 | 0 | 1 |
| 20 A placeholder written in a Background | 1 | 0 | 0 | 1 |

The same counts hold in all five modes the suite runs (all passing, an `And` failing, a `When` failing, a Background step failing
before a `When`, and before a `Then`) on 5.27.13 and the suggestion, within two rows; the prototype is at zero in each. RUN 2026-11-03: nothing has moved. Published-equivalent 5.28.2 reads 25, 11 and 8 too (`results/shapes-control-5.28.2-*.txt`), and with stop-at-first-error on the prototype stays at zero where 5.27.13 stays wrong (`results/edge-shapes-*.txt`).

### 2.4 On a real suite

RUN (`results/corpus-breakfastprovider-reqnroll.txt`): a census of BreakfastProvider's Reqnroll suite, by the detector's own rule
with every scenario passing, on a clone at `640c190`. 73 features, 214 rows, **no `Background:` written anywhere**, and the rule marks 10 scenarios
as one, in three features (`DailySpecialsIdempotency`, `CrossFieldValidation`, `ToppingsUpdate`). Every mark it makes there is
an invention. That is a static reading of the files; the census, run over the shape suite's own files, gives
the matrix's counts less its German shape (`results/corpus-shapes-suite-validation.txt`). English keywords only.

RUN 2026-11-03 (`results/breakfastprovider-across-the-fix.txt`): the suite itself, in memory, from a scratch clone with its pins moved to 5.28.2, on the published packages and with the prototype's assemblies swapped in, twice each. 214 of 214 pass on all four. The census holds: 10 scenarios with a `b` address and 10 `BackgroundSteps:` blocks in `Specifications.yml` become none, on both runs of the fix. `probes/addresses-across-the-fix.py`, reading every scenario through `query`, moves those 10 and no other, where two runs of the published packages move none. `query diff` reports only times between any two of the four, and two runs of one build differ as much. `probes/census-against-report.py` reads the report's rows against the feature files, file by file, and none differs.

This repository is a third witness. `CakeQuality.feature:7-8` writes a one-step `Background:`, and
`ReqNRollDuplicateStepsTests.cs:96-120` pins `BackgroundSteps` as that step **and** the `When` after it.

## 3. Findings the issue does not state

**F1. A real Background is usually not marked.** RUN: shapes 01, 02, 06, 07, 08, 10 to 13, 15, 19, 20. `Step-Tracking.md:460` says
the opposite.

**F2. Steps are called a Background that nobody wrote as one.** RUN: shapes 04, 05, 09, 14, 16, 18. A `When` and a `Then` among them.
A green run publishes it: `Specifications.yml` lists a `BackgroundSteps:` block holding the `Given` and the `When` for feature 04,
and all three steps with no `Steps` for feature 05 (`results/one-page-two-answers-and-specifications.txt`). That file is a
specification people read.

**F3. A file attached in a shared step stops the report.** RUN (`results/shapes-5.27.13-attach.txt`,
`results/shared-step-attachment-crash-5.27.13.txt`): 41 of 41 tests pass, `[Test Assembly Cleanup Failure]
KeyNotFoundException`, exit code 1, no `TestRunReport` of any kind. READ: `CopyAttachments` keys its sources by object reference in
a first pass, and `AttachmentPlacement.ForEachList` replaces each list in a second (`ReportGenerator.cs:7102,7122,7127`). A shared
step is visited twice, and the second visit finds the copies the first made. Since 4.27.2 (`d6fb8a19`, 2026-10-19). RUN: `kronikol
ingest` of the same shape ends in an unhandled exception, exit code 0xE0434352 (RUN in PowerShell; Git Bash shows 127), no report, and the prototype's tool exits 0 with both files copied (`results/ingest-lane-attachment-two-builds.txt`).
INFERRED: a suite that attaches a screenshot in an `[AfterStep]` hook meets this wherever the detector extracts anything.

**F4. `Failures.md` loses the failing step.** RUN (`results/issue-suite-*.txt`). For the issue's `s9`: `failingSteps: []`,
`stepsBefore: []`, `callsScope: scenario`, four calls. For `s4`, the same failure in a feature the detector leaves alone: the failing
step, the steps before it, and `POST /payments` alone. On R1 `s9` reads like `s4`.

**F5. The reverse of the issue's title happens.** RUN (`results/shapes-5.27.13-fail-bg2.txt`). `17 holds [Passed]` shows `✗ b0`, with
`who: 17 breaks` under it. The scenarios sort happy path first, then by title (`ScenarioInfoEnumerableExtensions.cs:29-30`), so whoever
sorts first decides what the rest show.

**F6. The layout depends on who ran and who failed.** RUN. `09 one` passes in each case, yet its steps are `b0, b1, b2, 0` when
`09 two` passes or fails at its `When`, and `0, 1, 2, 3` when `09 two` fails at its `And`, as a skipped step is not in the list and so
shortens the prefix. Filtered to one scenario, `04 one` is `0, 1, 2`, against `b0, b1, 0` in the full run (`results/filtered-run-5.27.13.txt`). Two files with one title
are one feature, so one file's Background is lent to the other's scenarios (shape 16).

**F7. One page gives two answers.** RUN: `s9`'s own diagram holds `customer-10` and `BANANA`, and its step list on the same page
holds `customer-2` and `APPLE`. The bars come from the running step (`ReqNRollTrackingHooks.cs:103-104`); the list from the copy.

**F8. The Features summary is wrong both ways.** RUN (`results/features-table-counts.txt`), as painted. With a `When` failing: one
failed scenario, 14 steps, 14 passed, **0 failed** (R1: 13 and 1). With a Background failing in one of two scenarios: **2 failed
steps** for one failed scenario (R1: 1). The picture is `results/paint-5.27.13-failed-when-reads-passed.png`: a red failure block over
a list of green ticks.

**F9. The ingest lane has the same data loss.** RUN (`results/ingest-lane-5.27.13.txt`). `FeatureSynthesizer.cs:272,304-312` runs the
detector when no record says `background: true` and the steps carry keywords. A failed `When` reads as passed, the doc string is the
other test's, and `failingSteps` is empty. Giving each scenario its own steps fixes the data
(`results/ingest-lane-detector-own-copies.txt`), and `✗ b1 When` stays marked as a Background: Q2.

**F10. The "muted left accent" has never painted.** RUN (`results/paint-failed-background.json`): `border-left-width` is `0px` on every
`step-background`, with or without sub-steps. READ: `.scenario-steps > .step { border: none }` (`stylesheets.css:988-995`) out-specifies
`.step-background { border-left: 3px ... }` (`:1014-1019`), since the inline list arrived in 3.0.48. `Step-Tracking.md:430` says it is
there. Only the muted text marks a Background step. Q4.

**F11. A step that runs a step corrupts the list.** RUN (shape 11). The hooks add the inner step first, as a top-level step of its
own (`ReqNRollTrackingHooks.cs:153`), while `StepCollector` rightly nests it. So `MapSteps`' positional pairing
(`ScenarioInfoEnumerableExtensions.cs:115-120`) is one out from there on, the last step has no sub-steps, and the report says "calls
belong to no step". The inner step also overwrites the outer's stopwatch (`ReqNRollTrackingHooks.cs:96`). R1 fixes it, as the Background count needs the list
to be the feature file's.

**F12. A separate Background section hides a failed step.** RUN (`results/paint-separate-section-failed.json`, and
`results/paint-separate-section-after-r1.json`). With `SeparateBackgroundSteps`, a section holding the only, failed step starts closed. The Cucumber lane has this
today, and so does this lane where a Background step fails alone (`17 breaks` in `results/shapes-5.27.13-fail-bg2.txt`). R1 marks far more Backgrounds,
so R1 fixes it: `startsOpen` goes from false to true.

**F13. Steps skipped after a failure are missing here and listed in the Cucumber lane.** RUN (`results/skipped-steps-two-lanes.txt`):
the same failure lists 4 steps there and 2 here. READ: Reqnroll invokes `BeforeStep` only for a step with nothing wrong yet, and
`AfterStep` only if `BeforeStep` ran (`TestExecutionEngine.cs:592-598,644-647`). So the `priorFailure` branch in `MapSteps`
(`ScenarioInfoEnumerableExtensions.cs:103,110,122`), which would give `SkippedAfterFailure`, is unreachable on this lane. This is also what makes F6 possible. R2.

**F14. The core suite has no coverage of this.** RUN: the prototype's first cut, with the guess gone from this lane, passed 11,578 of 11,580 core
facts unchanged (2 skipped as ever, `results/core-suite-r1-before-its-facts.txt`), and the final one, on 5.28.2, passed 11,870 of 11,872 with its facts added (`results/core-suite-on-prototype.txt`). The only pin is the example test that enshrines the wrong answer (§2.4).

**F15. Kronikol4J holds a faithful copy of the detector, with no caller.** READ: `BackgroundStepsDetector.java` takes
`members.get(0)` too, and no production code calls `detectAndExtract`, only its own test and `GoldenHtmlParityTest.java:414`, though `docs/REMAINING_PARITY.md:1152` says it runs.
A ledger line, and Q6.

**F16. A suite's `[AfterScenario]` hook that throws loses the scenario, and can leave `Failures.md` saying nothing failed.** RUN (`E47`, `results/edge-shapes-5.27.13.txt`, `results/edge-shapes-prototype.txt`): a throw stops the later hooks of that type (`FireEventsAsync`), and the adapter collects a scenario in its own, which sorts last (`ReqNRollTrackingHooks.cs:163`). So `dotnet test` reports the failure, and the report has no row for it. Beside one scenario that passes, `Failures.md` opens "# No failures" and "All 1 scenarios passed". Alone, no report is written at all. The only hint is `query summary`'s "calls carried a test id that no scenario of this run has". INFERRED: a step's real failure under such a hook goes with it, as the row does, and a cleanup hook is the kind that throws after a failure. A scenario ignored at run time has the same cause on the floor: at Reqnroll 3.0.1 a skipped scenario runs no `AfterScenario` hooks (READ: `TestExecutionEngine.cs:252-256` at `v3.0.1`), so `E27` has no row there, on 5.27.13 and on R1 alike (`results/edge-shapes-5.27.13-reqnroll-3.0.1.txt`). It is not this issue's fault, and R1 neither causes nor ends it. #223's plan holds it as its T15. R4, Q9.

## 4. The underlying problem

Three faults, one under the other.

**The symptom.** One scenario's step objects replace another's. The issue's line fixes this, and F3, F4, F5, F7 and F8 with it.

**The assumption.** Steps that read alike were taken to be the same step. They were never that: a step is what happened in one
scenario, with its own result, arguments, sub-steps, duration and files. The code said so out loud ("shared by reference"), and
later code was written on top of it (`ForEachList`), which is how a display fault became a lost report.

**The cause.** The report reconstructs from what ran and how it reads something the feature file states. A Background is a fact about
the source. Text comparison across scenarios can't recover it: it can't see a Background over one scenario, can't tell a Background from
a shared `Given` and `When`, and changes its answer when a scenario fails or is filtered out. Each rule added to the detector removed
some false positives by also removing true ones, because the information was not in what it compared.

The guess was reasonable in May 2026, when the adapter had nothing better. It stopped being necessary on 2026-08-29, when the adapter
started reading the embedded Gherkin document for `Examples:` blocks. Nobody came back to it because the feature looked like it worked: it
produces a muted step and a `b0`, and a wrong one looks the same as a right one.

So the fix is to ask the source. The rule this plan proposes:

> **A step is a Background step when its source says so, and it is always the step of the scenario that ran it.**

## 5. The design

### 5.1 Read the Background from the document (R1)

READ, RUN: the seam exists. `ExamplesBlockResolver.BuildLookup` already reads `FeatureInfo.FeatureCucumberMessages` by reflection, caches
it per feature, and degrades silently. Add one method beside `ResolveRowName` and `ResolveSource`:

```csharp
public static IReadOnlyList<string> ResolveBackgroundSteps(FeatureInfo? featureInfo, ScenarioInfo? scenarioInfo)
```

It returns the texts of the steps the feature's `Background:` and then the scenario's rule's give that scenario, finding the scenario
through `FindPickle(...).AstNodeIds[0]`, else by title. Empty when none is written or the messages are unavailable.

Why this route, measured against the others:

| Route | Verdict |
|---|---|
| The document, via the pickle's scenario id | **Taken.** RUN: right in all 20 shapes, on Reqnroll 3.0.1 and 3.3.4 (the newest on nuget.org on 2026-11-03), and with row tests off |
| `StepInfo.PickleStepId` per step, as Reqnroll's own formatter does | Rejected. RUN: empty on all 118 steps unless a formatter is configured (`results/pickle-probe-3.3.4.tsv`); READ: `FeatureExecutionTracker.cs:120-127` sets it only then. And RUN, with one on: it drifts past a step that runs a step (`-formatter-on.tsv`) |
| Counting leading background steps in the pickle | Rejected. RUN: shape 15. Gherkin compiles a scenario with no steps to a pickle with none (`PickleCompiler`: `if (scenario.Steps.Any())`), and Reqnroll runs the Background anyway |
| Reading the `.feature` from disk | Rejected. It is not beside the test binary on CI, and the embedded copy is what ran |
| Walking the stack for `FeatureBackgroundAsync` | Rejected. Not there for a rule's Background (READ: inlined, `ScenarioPartHelper.cs:55-77`), and async frames |

The messages are present in every shape measured. READ: the generator has no setting for them; `HasMessages` is false only where generation failed, with a build warning
(`UnitTestFeatureGenerator.cs:263-268`), or where the embedded resource is missing or unreadable (`FeatureLevelCucumberMessages.cs`). Then every step is the scenario's own, which is true, and only the mark is lost. RUN by the fourth review, with the `.ndjson` taken out of the embedded resources: every step right and no mark, where 5.28.2 guesses `b0`. That is §5.9's choice, not an oversight.

The Kronikol.ReqNRoll.Core floor stays Reqnroll 3.0.1, as it needs no member the adapter does not already read. RUN: the matrix, the edge shapes and the nested shapes on 3.0.1 read as on 3.3.4 (`results/shapes-prototype-reqnroll-3.0.1-*.txt`, `results/edge-shapes-prototype-reqnroll-3.0.1.txt`, `results/nested-shapes-prototype-reqnroll-3.0.1.txt`), but for `E27`, which has no row there on 5.27.13 either (F16).

A suite using Reqnroll's external-data plugin writes rows the feature file does not. RUN (`probes/external-data/`, `results/external-data-shapes.txt`): a Background over outlines fed from a CSV, alone and beside a written `Examples:` row, marks `b0` on all six rows, then `0`, `1`, each with its own values, where 5.27.13 marks none.

### 5.2 Count against what ran (R1)

```csharp
internal static int CountBackgroundSteps(IReadOnlyList<ScenarioStep> ran, IReadOnlyList<string> writtenBackground)
```

The leading rows whose text equals the document's. A failed step ends a scenario early, so a Background of two steps may show one,
and a step that disagrees is never called a Background step. This is the cross-check the same class already uses before trusting a
pickle (`ExamplesBlockResolver.cs:150-158`). RUN (`results/edge-shapes-prototype.txt`, E29): the texts compare equal through quotes, a backslash, accents,
doubled spaces, `<angle>` brackets, a `#` and braces.

The document's texts ride on `ReqNRollScenarioInfo` as an `internal` init property, `WrittenBackground`, as `RowName` does. `ReqNRollStepInfo` is a public positional
record, so a new positional member would be new surface; §5.3's `Nested` and `RanByAStep` are internal too, and `ExamplesBlockResolver` is an internal class, so nothing public is added.

`ToFeatures` keeps one mapped list, counts over it and splits it (`ran[..n]`, `ran[n..]`), null when empty, as the Cucumber lane does. The count reads the rows the
report will show and not the hooks' list, so it never has to know how a row came to be there (§7). It stays a prefix on purpose: a step's bars and calls are matched by position against
`OrderedStepPaths` (`ReportGenerator.cs:5417-5421,5448`), so a row moved ahead of one that ran first would cost the calls after it their step. `MapSteps`' pairing with collected steps is not touched. `priorFailure` behaves as it did, except
that a step now reads the scenario's outcome (§5.3).

The detector call stays for one path, and only that one. `ReqNRollScenarioInfo` is public (`ReqNRollReportGenerator.cs:18`), and one built by hand never had a document. There `WrittenBackground` is null, where the hooks always set it (empty when nothing is written or the document is out of reach), and `ToFeatures` runs the detector only over a feature all of whose scenarios are null, with §5.4's own copies. No fix needs that path to change, and a guess kept there brings none of §3's faults back to a Reqnroll run. RUN: `WhatTheFixLeavesTests`, in `prototype/BackgroundAsWrittenTests.cs`.

### 5.3 A step a step runs stays a sub-step (R1)

F11. `StepCollector` already nests it, so the hooks only have to agree. Each open step has a frame (internal `ReqNRollConstants.OpenStepKey`) holding its stopwatch, the frame it opened inside and the
steps it ran. The frame is kept on the step context, of which Reqnroll gives each level its own, and the scenario context holds the innermost. `AfterStep` still adds every step to the
scenario's list, in the order they finish: that list is public (`ReqNRollScenarioCollector.GetAll()`, `ScenarioContext[StepsCollectionKey]`), #223's prototype keeps it too, and no fix needs it changed. A step with a parent frame is marked, with an internal `RanByAStep`, and `ToFeatures` maps only the rest. `StepStopwatchKey` is a public constant and keeps being set, where a step opens in earnest: the sixth review read 0 ms in a composite's hook after an earlier cut set it for a nested step a hook had stopped, where 5.28.2 reads the composite's time. RUN
(`results/shapes-prototype-steps-11.txt`): shape 11 goes from four listed steps with calls belonging to none to `b0`, with the inner step under it, then `0`, `1`.

Taking the step off the rows must not lose what only a row held, or trust that the hooks pair up. The first cuts did both. Two independent reviews and the edge shapes found these
(`probes/nested-shapes/`, `probes/edge-shapes/`, `results/nested-shapes-*.txt`, `results/edge-shapes-*.txt`), and they were fixed in the prototype and measured again:

- **Its arguments.** The collector keeps a sub-step's named parameters only, as its table and doc string go to the top-level bar alone. So
  `ReqNRollStepInfo` gets an internal `Nested` list, and `MapSteps` gives each sub-step its table, doc string and inline values, finding it by
  what opened it, under any woven step between (`DescribeNested`): the hooks tell `StepCollector`, by an internal overload, that the frame's own list opened the step, and that rides to an internal `ScenarioStep.OpenedBy`. Two reviews found pairing by text wrong in turn. The fourth found a `[Step]` of the same text taking the table, and the fifth a `[GivenStep]` of the same text taking a nested step's outcome, so a woven step that passed read failed and `Failures.jsonl` listed it (`Q02`), and one that failed read passed (`Q15`). RUN: `A_nested_step_is_not_taken_for_a_woven_step_that_reads_the_same`, `A_woven_step_that_failed_stays_failed_beside_a_nested_step_that_reads_the_same`, `results/fifth-review-on-the-final-cut.txt`. RUN: `21 one` shows `b0` with the table on `b0.1` and the doc string on `b0.2`, and its five calls each on a step, where
  5.27.13 sets the table on a row with another step's sub-steps and all five calls belong to no step. `E42` keeps both with a `[Step]` helper in between: the table on `b0.0.0`, the doc string on `b0.1`.
  An outline's column read only by such a step still reaches `ExampleValueGrouper`, as the list it reads still holds those steps.
- **Its failure.** Reqnroll writes a failure only to the inner step's context, and the outer step carries on with OK (READ:
  `TestExecutionEngine.cs:509,693-696`), which would draw a passed step over a failed sub-step, F8's picture. An inner step can also fail with no hook at all, being undefined or ambiguous or having an argument that does not convert
  (READ: `:591-599`), which is then a failed scenario with no failed step. But Reqnroll opens a step only while the scenario is well (READ: `:508-509,592`), so the scenario failing by the time a step closes failed in that step, and a step whose own status is OK reads the scenario's.
  It is the rule #223's prototype reached. RUN: `22 breaks` shows `✗ b0` over `✗ b0.1`, and `Failures.md` names both; `E38` carries on past the inner failure and reads the same; `E43`, whose inner argument does not convert, shows `✗ b0`. `priorFailure` is set, so §5.7's rows would read as skipped after a failure.
- **Its level.** Reqnroll runs `AfterStep` once `BeforeStep` has been tried (READ: `TestExecutionEngine.cs:592-598,644-647`), and a hook that throws stops the later hooks of its type
  (`FireEventsAsync`). So a suite's `[BeforeStep(Order = int.MinValue)]` that sorts ahead of the adapter's and throws leaves `AfterStep` with no frame. An early cut kept the frames on a stack, and `Pop` threw "Stack empty", which
  became the test's failure in place of the suite's own error and lost the failing step, where 5.27.13 lists it (`E36`). A frame on its own step context cannot be handed to another level: `AfterStep` with none lists the step and leaves the
  suite's error standing, and on a scenario's own step closes nothing of the collector's. And where a suite's `AfterStep` throws on an inner step, that frame stays open, so the outer step closes and describes what is left before closing its own, as the scenario's AfterScenario hooks do (`CloseLeftOpen`), so its table and doc string are kept and it stays in the public list (the sixth review found them lost). RUN: `E36` shows `✗ b1`. On an inner step, `E39`, the collector has not seen it, so `AfterStep` opens it there, to close at its place among the steps the outer step ran (the fifth review found an earlier cut adding it at the end, after a woven step that ran after it, `Q13`): `✗ b0` over `✗ b0.1`, with the digest naming both, where 5.27.13 lists the inner as a row of its own, `✗ 0`, the outer as passed, and a call on no step. `E40` and `E45` show `✗ b0` with the sub-steps kept, with the digest naming each.
- **What a hook kept ours from closing.** A suite's `[AfterStep]` that throws stops the adapter's, so on a scenario's own step nothing listed it or closed its frame, on 5.27.13 too: a failed scenario with no failed step, and calls belonging to none. The frame is still there and holds the step's `StepInfo`, so a hook of the adapter's at `[AfterScenario(Order = int.MinValue)]` closes it as failed and lists it, through a `Describe` that `AfterStep` shares. The hook is an `internal` method: the class is public, so a public one would be new surface and a minor by `CLAUDE.md`, and Reqnroll runs a non-public hook, own or inherited, on 3.0.1 and 3.3.4 (RUN: `results/non-public-hooks.txt`; only a base class's `private` one is missed, as reflection does not show it through the adapters' derived classes). `The_hooks_public_methods_are_the_four_they_were` pins it. It runs ahead of a suite's own at any later order, so the step takes neither their time nor their woven steps, and they find it in the list: the seventh review found a 0.3 s step reading 1.54 s on a cut that closed it after them. The adapter's last hook comes back for a step one of those hooks ran and left open (`H23` of the eighth review, the same on 5.28.2), so a scenario does not read failed with no failed step, and the example's `An AfterScenario hook runs a step an AfterStep hook fails` runs it. RUN: `E35`, in a Background, lists no step on 5.27.13, with its call on none and `failingSteps` empty, and now shows `✗ b0` holding the call. `E41`, a scenario's own step, lists only the `Given` before it on 5.27.13, and now shows `✗ 0`, with the digest naming it.

An inner step Reqnroll does not reach is not listed, as no hook fires for it (`E31`, `E43`). One that is pending is, under a parent that reads as it does (`E32`).

`STEP_IDENTITY_PLAN.md` (#223) measures the same fault as its T21 and guards it its own way, with a frame per level too, and only one of the two should ship (§7).

### 5.4 The detector stops sharing (R1)

The issue's line, in the issue's place. `BackgroundStepsDetector` is a public type, so it stays (removing it is a major). Its summary
says plainly that it guesses, and the comments that describe sharing as a design come out (§8). RUN: it fixes the data in the ingest lane, and for a scenario built by hand.

### 5.5 The attachment walk visits a step once (R1)

No lane shares a step after §5.4, but `Feature` and `ScenarioStep` are public, and a caller of `CreateStandardReportsWithDiagrams` may. A
display fault should not be able to lose a report. `ForEachList` keeps a `HashSet<object>(ReferenceEqualityComparer.Instance)` of the scenarios and steps
it has visited, the comparer being needed as `ScenarioStep` is a record.

### 5.6 A section holding a failed step starts open (R1)

F12: a section with a failed step in it starts open, as R1 moves failed steps behind that summary and a failure should not need finding. `HasAnyFailed` now reads the step as well as its sub-steps, which also ends a second fault: a failed step whose sub-steps passed started collapsed everywhere (`ReportGenerator.cs:4505`), in every lane, and its sub-steps are now drawn with it and not held back. RUN: `A_failed_step_whose_sub_steps_passed_starts_open`.

`HasAnyFailed` costs something where a composite failed by itself: its sub-steps were held until it was opened, and now are drawn. On reports built to be the worst case, every row such a composite, 300 rows of 60 steps go from 8,675 elements to 98,674 and load in about 850 ms where they took 570, and a row click takes 28 ms where it took 5 (`results/fifth-review.md`). A failed step is what a reader opens first, so it stays.

`ToggleDefaults.BackgroundStepsOpen` keeps its meaning. Left unset, a section starts closed unless it holds a failed step. Set to `false`, it stays closed with one in it too, as `StepsSectionOpen = false` does, since whoever set it asked for that. Only the options can tell unset from `false`, as `ResolvedToggleDefaults` is a public record with a plain `bool`. So that record does not change at all. The generator's internal entry takes a `backgroundOpensOnFailure`, which `ReportToggleDefaultsResolver.BackgroundOpensOnFailure` reads from the options for the two standard pages, and a merged report has no such section. A caller of the public `GenerateHtmlReport` hands in a record or none and gets what that says, closed with a failure in it, as on published. Three reviews found a flag on the record wrong before this: a record built by hand opening, `BuiltIn with { BackgroundStepsOpen = false }` carrying the flag, and then `new() != BuiltIn`, with a JSON round trip and a no-op `with` losing it. RUN: `Left_unset_a_section_holding_a_failed_step_starts_open`, `Set_to_false_a_section_holding_a_failed_step_stays_closed`, `Set_to_false_a_failing_rows_section_stays_closed`, `Only_a_section_left_unset_opens_for_a_failure`, `A_record_handed_to_the_generator_says_what_it_says`, `The_built_in_toggles_are_a_record_nobody_configured`.

That alone does not hold in an outline, which the second review found. Only the first row starts visible, and `selectRow` copies the row left's `<details>` states to the row selected, by position (`report-select-row-function.js:25,37`), so clicking a failing row from a passing one would close its section. The fourth and fifth reviews found two answers wrong in turn. Forcing the section open on arrival leaked back to the passing row and undid a reader's close. Then letting nothing cross to or from that section lost a reader's choice past that row, as states came only from the row just left. By position there was a third fault: a row that never reached its steps has a Failure Result where the row left has its section, so the closed section closed the Failure Result, which 5.28.2 does only where it guesses a Background and R1 would have made common.

So the row script keeps what the reader left, for each outline, for as long as the page is open: a `<details>` by its kind, of which a panel has one, a composite by its place among the steps as before. A section the generator marks `data-holds-failure` takes no part, and neither does a composite with a failed step in it, which the script reads from the page, as such a composite always starts open and has its sub-steps drawn. Each of those keeps its own state on its own row. So a choice carries past a row that holds a failure or lacks that section, one the reader closed on a failing row stays closed when they come back, and a section set to stay closed has no such mark. A click on the row already chosen changes nothing.

The filter search leaned on the old carry. Its reveal opened the first row's sections and left the row's click to carry them, so the fifth review found a single match shut where the first row holds a failure (`Q06`). A first answer opened every row's sections, and the sixth review found that losing what the reader closed on them. A second kept what the search opened on lists of its own, to shut it again, which drew faults in the seventh and eighth reviews, new ones among them (a section the reader closed and reopened shut when the search was emptied, and one outline's row choice shut another's section), all to end a leak 5.28.2 has. So the lists went, as the flag on the public record did. The search opens the sections of the row shown once it has chosen that row (`revealMatchSections`), each section once until the search is emptied, so one the reader closes stays closed, and it closes none of them, as on 5.28.2. A row switch the search makes shows that row as it stands and brings nothing the reader left (`selectRowBySearch`), so the reader's rows come back as they left them when the search is emptied. It does keep what the row it left held, and a row the search chose keeps only what the reader has changed there since (`selectRowChosen`): the ninth review found such a row's untouched state undoing a reader's close on the next switch. Before the search opens a closed section on the row shown, what that row holds is kept as the reader's, and from there the row counts as one the search chose (`selectRowSearchOpens`), so what the search opens is never passed on, and what the reader opened before it still is: the tenth review found both wrong on the ninth's first answer. A search ends with the field emptied, with Clear All, and with an address that names another search or none (`forget_sections_revealed`), so the next one reveals afresh, and going back to the same search keeps closed what the reader closed there.

RUN (`tests/Kronikol.Tests.EndToEnd/BackgroundSectionRowSwitchTests.cs`, in the first cut, on reports made from the options): the section is closed on the first row, open on the failing row and closed again back on the first; open on arrival, and closed when the reader closed it and came back; open past a failing row and back; a Steps section closed past a row without one; a Failure Result kept open, and one the reader closed staying closed on the next row with one; a section set to stay closed holding nothing; a failed composite keeping its state on its own row, and a reader's open composite carrying past it; a single search match shown in its row, with and without the reader having moved first; emptying the search putting back the row the reader left as they left it; a match that moves between rows shown on each, with the row it never reached untouched; the search changing no row but the one it chose; a row it chooses taking nothing from the row it left; a section the reader closed during a search staying closed as it narrows and when the match moves away and back, and opening again in a new search, after Clear All and after going Back; a row the search chose undoing neither what the reader closed nor what they opened, on the next rows, passing on what the reader changed there and not what the search opened, and the reader's once they come back to it; a search landing on the row shown passing on none of what it opens there; what the reader opened before the search opened the rest carrying; going back to the same search keeping closed what the reader closed; sub-steps drawn since the search chose a row being no change of the reader's; the diagrams section the same (`SearchFunctionPageTests`); two outlines keeping their own; a composite at another place among the steps taking no state; a click on the row already chosen, for a section and a composite; and a `<details>` of any class a suite's script adds. A real run agrees (`results/paint-row-switch-prototype.txt`, E44): closed on the first row, open on the failing row after the click.

### 5.7 Steps that never ran (R2, if green-lit)

F13. The pickle plans every step. When the steps that ran are the first of those it plans, the rest are added with
`ScenarioExecutionStatus.Skipped`, and `MapSteps`' dormant branch makes them `SkippedAfterFailure`. A small resolver method gives each one's
keyword (from the written step's type, so a German file reads in English as this lane does, though a conjunction such as `Aber` reads `And` where this lane says `But`), text, table and doc string.

RUN (`results/shapes-prototype-r2-*.txt`, `prototype/r2-first-cut.patch`): `09 two` lists all four steps, `08 breaks` shows `✗ b0` then
`0`, `1`. The matrix stays at zero. It is the same cause, read a step further, and it closes the last difference in what the two Gherkin
lanes list. It is kept apart because it changes the step counts of every Reqnroll scenario that stops early (failed, undefined or pending), and the issue set it aside.

**#223's plan proposes the same thing another way, and only one should ship.** `STEP_IDENTITY_PLAN.md` (kronikol-fe, uncommitted in
`C:/Code/Kronikol-223`), its R2 and Q3, lists these steps from Reqnroll's `StepFinishedEvent`, which fires for a skipped step too (READ:
`TestExecutionEngine.cs:631-637`). The event is the better source: it carries the step as it was called, needs no keyword mapping, lists an
undefined step as well, and can place a step that never ran against a hook's step (its `NotRunAfter`), which a pickle cannot. The pickle's one
advantage is a run the engine abandons, where later steps raise no event: `stopAtFirstError` (READ: `:649-650`), and, as that plan states as its
limit, any throw that aborts the test method, such as a block hook or `AfterStep`. Those are rare, and on the event route they stay as they are
today. So if that R2 is taken, this one is dropped. RUN (`results/edge-shapes-with-223.txt`, `results/shapes-with-223-*.txt`): the split reads the mapped rows by their text (§7), so a skipped Background step counts as one either way. This first cut stays in the
harness as the measurement that the list comes out right.

### 5.8 The ingest lane (R3, if green-lit)

The tests NDJSON has an explicit `background: true`, so a producer that knows can say so. The guess there has the same fault as here, F2
and F6, and the wiki's own reason for not guessing on keyword-less steps applies word for word. The change is small: remove
`DetectBackgroundSteps`, rewrite `The_background_heuristic_runs_only_when_no_explicit_background_was_given`, correct
`TestRunRecord.cs:88-93` and `Ingesting-External-Captures.md:146`.

But no fix forces it. R1's own copies already fix that lane's data (§5.4), and what is left is a mark the wiki documents, on a stream that does not say. By `CLAUDE.md`, that is a default changing so that existing input behaves differently without being touched, which is a major. So it is recommended for `plans/V6_PLAN.md`, by a line there, and Q2 asks.

### 5.9 Rejected

- **The issue's suggestion alone.** Fixes F3, F4, F5, F7, F8 and F9's data. Leaves 36 of 41 scenarios wrong, keeps a failed `When` labelled
  Background, keeps the layout depending on the outcome (F6), and leaves the Specifications saying what F2 says. Worth doing, and §5.4
  does it, but it is not the fix.
- **Keeping the guess and labelling it differently** ("shared steps"). Nobody asked for such a feature, and it would keep F6. Offered in Q1
  in case the owner values the grouping.
- **Using the guess when the document is missing.** It would bring the fault back on the path nobody tests.
- **A frozen classifier for old reports**, as `AsOf5164` does for titles. Nothing stores an address across runs (§9), and each report is
  internally consistent.

### 5.10 What does not change

Stable ids (RUN: `s9` is `d1a3b65632e63092` before and after). The data file's shape and every schema string. `CollapseRepeatedStepKeywords`. The Cucumber lane. `MapSteps`' pairing, which is #223's to change. `GetAll()` and `StepsCollectionKey`, and a scenario built by hand. Every option.

Every diagram, and every call. RUN 2026-11-03 (`probes/compare-across-the-fix.py`, `results/across-the-fix-*.txt`), scenario by scenario through `kronikol query`, pristine main against main with R1, in all five modes, per-run ids and times aside: all 41 scenarios' diagrams and calls are alike in every mode. `flow` is alike in 39, and the other two, `11 one` and `11 two`, gain the `<step>` lines they lacked, as their calls now have steps. Step addresses move in 36, 34, 36, 36 and 36 scenarios (pass, fail-and, fail-when, fail-bg, fail-bg2), each listed in the file. `Specifications.yml` goes from 583 lines to 596 on the green run, and `Failures.md` differs in the failing modes only.

### 5.11 The step marker's matcher, which R1 leaves alone

`StepMarkerMatches` (`ReportGenerator.cs:5463`) is #128: a step whose text the bar escapes, wraps or cuts fails the check, and its calls and the next steps' belong to none (RUN: `E46`, `E48`, on every build). It is not this issue's, no fix here needs it, and R1 leaves it as published. An earlier cut changed it, and two reviews measured why that is no side job (`results/fourth-review.md`). The form #225's R2 had then, the first line escaped up to 400 characters with spaces folded out, reads kronikol-ca's 20 labels where published loses 14 (`results/matchers-compared.txt` in its harness). But it refused a bar that is not escaped, which published reads: a store from before v3.31.8, another producer's `kind: marker` lines, `InsertPlantUml(..., DiagramMarkerKind.Step)`. And it made confident false matches where published refuses: a step `a cart` in a bar `When a car takes the order` put the call under `b0`. #223's plan has dropped its copy for the same reason. #128 stays with #225's R2, whose session has both findings and has since given it a second form, under its own review, where that session measures an unescaped bar reading as on published. Nothing in this plan waits on it.

## 6. Tests

Red first, per `CLAUDE.md`. `prototype/BackgroundAsWrittenTests.cs`, `prototype/apply_examples.py` and the facts in the last `prototype/apply_r1*` scripts are the first cut: 142 nearby facts pass on the prototype (`results/first-cut-facts.txt`), and 24 in `BackgroundSectionRowSwitchTests`, with `SearchFunctionPageTests`' 14 beside them. All 65 one-line mutations are caught, those in the hooks by §6.3's real run and those in the page's scripts by that class (`results/mutations-first-cut.txt`). One break stays unpinned, and `results/sixth-review.md` says which.

### 6.1 Core facts, `tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs`

- The resolver: a feature's Background; a rule's after its feature's; a rule without one; a scenario with no steps; no pickle, by title; no
  messages.
- `CountBackgroundSteps`: as written; a failure ending it early; a disagreement at the start and midway; nothing written.
- `ToFeatures`: steps reading alike keep their own result, duration and table, with no Background; a Background's steps come first and are
  each scenario's own objects; a scenario keeps its layout when another fails (F6); alone as among others; a count past what ran.

### 6.2 What holds whoever calls

- The detector gives each scenario its own steps, by result and doc string (`BackgroundStepsDetectorTests.cs`).
- `CopyAttachmentsToReportsFolder` over a step two scenarios hold does not throw, and the file is placed once (`AttachmentCopyTests.cs`).
- `kronikol ingest` of the shape of `probes/ingest-lane/tests-with-attachments.ndjson` writes a report (`IngestCommandTests.cs`).
- A separate section holding a failed step starts open, for a failed step with and without failed sub-steps, and the Specifications page reads its own value (`BackgroundSectionFromTheOptionsTests`).
- `query failures` says "no step is marked failed" for a scenario listing Background steps alone (`QueryCommand.Narrative.cs:161`, E49, a block hook that throws, which is the one way left to fail with no step under way).
- A failed step whose sub-steps passed starts open with them drawn, one with a failed step two levels down starts open, a section set to stay closed does so with a failed step in it, on a scenario and on an outline's row, and a toggle record says what it says (§5.6).
- A nested step is paired by what opened it, whatever a woven step reads, and keeps its table, doc string, inline values and own nested steps (`StepsUnderAWovenStepTests`, §5.3).
- `WhatTheFixLeavesTests`: scenarios built by hand keep the guess with their own steps; a feature the hooks saw is not guessed at, nor one only some of whose scenarios they saw; a step a step ran is no row and stays in the list.

### 6.3 A real Reqnroll run

No step definition in `tests/` or `examples/` runs a step through `ITestRunner`, and the only `Background:` in `examples/` is in the xUnit3 project, so nothing in the repository runs §5.3's hooks today. `prototype/apply_examples.py` is the first cut. `Example.Api.Tests.Component.ReqNRoll.xUnit2`, `.xUnit3` and `.TUnit` each get three features and one step definitions file: a Background and a rule's, with a step that runs steps with a table and a doc string (shape 21); a step whose inner step fails or whose inner argument does not convert (22, E43); a suite's `BeforeStep` ahead of the adapter's that throws, on a scenario's step and on an inner one (E36, E39); a suite's `AfterStep` that throws on each (E41, E40); and a Background step that fails.

Those projects' own runs must stay green: `ci.yml:169-182` runs them, and `RunAndParseScenariosAsync` asserts `result.Success` (`ReqNRollDuplicateStepsTests.cs:26`). So a step fails only when `KRONIKOL_EXAMPLE_STEP_FAILURES` is set, which only the new `A_failure_is_read_on_the_step_that_failed` does, through `TestProjectRunner.RunAsync`'s environment. That fact asserts the run failed and reads `Failures.jsonl`, naming each failing step in order, the inner ones too. It clears `GITHUB_STEP_SUMMARY`, as the fourth review found a nested failing run writing `## Debug this run` into a green job's summary. That run's HTML must hold the doc string of an inner step a suite's `AfterStep` threw on. The green feature's own `[AfterScenario]` hook reads `ScenarioContext[StepsCollectionKey]`, and fails if a step a step ran has left it or `StepStopwatchKey` holds no stopwatch. RUN (`results/reqnroll-examples-on-prototype.txt`): xUnit v2 11 of 11, xUnit v3 20 of 20 and TUnit green on their own, and all 265 facts of `Example.Api.Tests.Integration` pass, the three new ones among them.

`Background_scenarios_record_exactly_the_feature_file_steps` is
corrected, not deleted: `[Given]` and `[When, Then]`, as `CakeQuality.feature` writes them. The TUnit project is not in `TestProjects`, so its copy only has to stay green. Traps from earlier plans: a new outline in the
example must sort after "Parameterized Diagnostic Feature" (`FullPipelineParameterizedE2ETests.GetParamRows` counts rows page-wide), so these features hold none; example
suites bind fixed ports, so runs are announced to peers; generated `.feature.cs` files are never staged.

### 6.4 Both Gherkin lanes agree

One recorded messages file from Reqnroll 3.3.4 (`TestData/ReqNRoll/BackgroundShapes.ndjson`, made with the message formatter on the shape
suite) goes through `ExamplesBlockResolver` and through `CucumberFeatureSynthesizer`, and the fact holds them to the same Background per
scenario. RUN: of the suite's 41 scenarios they agree on 38. The other three are shapes 11 and 15, whose difference is Reqnroll's own message
stream (`results/lanes-agree.txt`, `results/shapes-cucumber-lane-pass.txt`, §5.1), so the fact names those two. Two implementations of one rule get a parity fact.

### 6.5 What is painted (Playwright, `tests/Kronikol.Tests.EndToEnd/`)

- `BackgroundRenderingTests`: a failed Background step shows the failed icon; if Q4 says so, `border-left-width` is `3px` as computed, with and
  without sub-steps.
- `SeparatedBackgroundRenderingTests`: the section holding a failed step is open, and the failure is visible without a click; and in an outline, it is open once its row is selected from a passing row (E44), where `paint-row-switch.js` was the first cut.
- `BackgroundSectionRowSwitchTests`, in the first cut: each case §5.6 lists.
- An end-to-end run through a real report: a failed `When` shows the failed icon in the list, so the page from
  `results/paint-5.27.13-failed-when-reads-passed.png` cannot come back.

### 6.6 Proof before the tag

- The shape matrix on the release build, every mode and every adapter: zero wrong rows (`probes/run-matrix.sh`, `probes/run-lane.sh`).
- The edge and nested shapes on the release build (`probes/edge-shapes/run.sh`, `probes/nested-shapes/run.sh`).
- Pristine main against the release build, scenario by scenario (`probes/compare-across-the-fix.py`), and BreakfastProvider's suite on both.
- The same on the floor, `-p:ReqnrollVersion=3.0.1`, and `-p:FloorRun=true` in the repository.
- The new facts against 5.27.13, to prove them red where they compile; a strengthened fact that stays green there is said so in the log.
- History and `query diff` across the fix (`probes/upgrade-across-the-fix.sh`).
- The public surface against the last published package (`probes/public-api/PublicApi.cs`): no difference, which is why this release is a patch.
- The audit sweep of `plan-execution-audit-checklist`: each behaviour sentence in the changelog has a fact.

### 6.7 If §5.7's R2 is taken

`AddStepsThatNeverRan`: the rest of the plan is added as skipped when the rows that ran are its first; a row that disagrees adds nothing; a step a step ran is not a row. `ResolvePlannedSteps`: a table with an outline's values filled, a German file's keyword with `Aber` reading `And`, no pickle. `MapSteps` makes them `SkippedAfterFailure`. A real run: `09 two` lists four. If #223's R2 is taken, that plan's facts stand instead.

### 6.8 If R4 is taken

`StepsThatFail.feature` gets a scenario tagged `@after-scenario-fails`, and the step definitions an `[AfterScenario]` for that tag which throws when asked. `A_failure_is_read_on_the_step_that_failed` then reads 8 failures, this one with no failing step, and its project's green run is untouched. A core fact: a scenario collected by the event is collected once, when both the hook and the event reach it.

## 7. Releases

Numbers are claimed at push; several sessions are live. Each follows `CLAUDE.md`: all packages, changelog with the bump's reason, tag, push.

**R1, a patch. Closes #222.** §5.1 to §5.6, the facts of §6, comments, wiki, Kronikol4J ledger. The first-cut patch is `prototype/r1-first-cut.patch`, a diff of `9d994a4b`; on that commit it passes the core suite (11,870 of 11,872, 2 skipped), `Kronikol.Tests.SearchEngine` (787 of 787), `Kronikol.Tests.StepTracking` and the LightBDD suites, all 265 Integration facts and the whole Playwright suite (2,117 of 2,146, 28 skipped, and one failure, `R16` on WebKit, a known flake that is not this patch's, §12). A prototype is a
witness, not the release: the facts get rewritten red-first.

Why a patch, against each line of `CLAUDE.md`. No public type, member or option is added, removed or renamed: every new member is internal, and `results/public-surface.txt` finds no difference from published 5.28.2. No default moves: `BackgroundStepsOpen` still starts closed and an explicit value holds, `ResolvedToggleDefaults` is unchanged, a hand-built `ReqNRollScenarioInfo` is still guessed at, and `GetAll()` and `StepsCollectionKey` hold what they held. What changes is output, and each change is a bug fix that cannot be had without it:

| What a consumer sees | Why the fix needs it |
|---|---|
| A real Background is marked, an invented one is not, and addresses move (§9) | It is the fix: the report, its schema and its tooltip all say "from a `Background:`" |
| Each scenario shows its own steps; `Failures.*` and the Features table read right | The issue's own |
| A step a step ran is a sub-step in the report | F11: the list was corrupt, and the count needs it to be the feature file's |
| A step whose inner step failed reads failed, in the report and in the public list's `Status`, and its `Duration` is its own | F8, F11 |
| A step a suite's hook kept ours from closing is listed, failed | Else a failed scenario lists no failed step, and its calls no step |
| With `BackgroundStepsOpen` unset, a section holding a failed step opens, and a failed step with passed sub-steps starts open | R1 moves failed steps behind that section (F12) |
| `Specifications.yml` lists other `BackgroundSteps:` blocks | F2 |

One thing can be read against the MAJOR line, "changing a default so that existing code behaves differently without being touched": the wiki documents marking a shared prefix (`Step-Tracking.md:404`, `Generated-Reports.md:873-880`). A shared prefix was only ever what the guess fell back on, and every other reader of the mark says it means a `Background:`, so this plan reads it as the bug. The fourth review read a second thing against it: with `BackgroundStepsOpen` unset, a section holding a failed step now opens. `BuiltIn`'s value is still `false`, a set value and a record built by hand hold, and what moves is only that a failure is not hidden behind a section R1 itself puts it in (F12). Q1 asks the owner to confirm both. The changelog calls out which steps are now marked, that addresses move, and that a step run by a step is now a sub-step; report output goes in the Kronikol4J ledger.

**R2, a patch, if Q3 says go and #223's plan does not list these steps first.** §5.7.

**R3, if Q2 says now.** §5.8. Its own release, never folded into R1, as its bump is not R1's.

**R4, a patch, if Q9 says on its own.** F16. `prototype/collect-on-finish.patch`, under 30 lines: the adapter also collects from Reqnroll's `ScenarioFinishedEvent`, which 3.0.1 and 3.3.4 both publish in a `finally` (READ: `TestExecutionEngine.cs:257-262` at `v3.0.1`, `:265-270` at `v3.3.4`), and a flag keeps it to once. RUN on pristine main (`results/edge-shapes-collect-on-finish.txt`, `results/edge-shapes-collect-on-finish-reqnroll-3.0.1.txt`, `results/shapes-collect-on-finish-*.txt`): `E47` is listed, failed, with its steps and calls, and `Failures.md` counts 1 failure of 2 scenarios, with the hook's own error and where it threw; alone, a report is written and counts 1 of 1. On the floor `E27` is listed too, skipped. In the edge shapes nothing else moves but the `sN` after `E47` and the warning about calls with no scenario, which goes, and the matrix's tables are line for line pristine main's in all five modes. It needs neither R1 nor #223: the same script applies on main and on main with R1 (RUN on R1 with R4, `results/edge-shapes-prototype-with-r4.txt`: `E47` listed, `E35`, `E39` and `E41` as on R1 alone, and `ReqNRollDuplicateStepsTests` 12 of 12). #223's prototype has the same fix (`CollectScenario`), so if its R2 lands first this is dropped, and if this lands first its R2 keeps its own version.

Order against the sibling issues from the same suite, each with a draft plan written the same day:

- **#223, `STEP_IDENTITY_PLAN.md` (kronikol-fe).** Q8 asks the order. R1 first is recommended, being the smaller, and that plan's Q4 recommends the same; the cost is §5.3's guard shipped and then replaced.

  What ran, and on which cut. **This final R1 landing second:** `prototype/apply_on_223.py` on that plan's prototype at `638f275d`, its R14, on the same main. All five modes are at zero wrong rows (`results/shapes-with-223-*.txt`), the nested and edge shapes read right (`results/nested-shapes-with-223.txt`, `results/edge-shapes-with-223.txt`),
  and that plan's own 28-scenario suite reads line for line as its stored copy, times and R14's `-` mark for a step that did not run aside (`results/with-223-its-own-suite.diff.txt`; none of its features writes a Background, so the shapes are what test the split). **An earlier cut landing first:** that session merged one under its code (`merge_222_resolve.py` in its harness, branch `proto/222-then-223`, whose R1 still counts in the hook) and reports its 28 triggers and a third reviewer's shapes reading right. **This final R1 landing first** has not been run. It is the same code in the other order, and the three meeting points below say what the second to land keeps. That session first reported that neither plan needed the other, and its own review then measured that wrong on its R10: published's shift happens to move a file off a row the detector extracts, and its fix put the file back, so two scenarios sharing an opening step, a hook's `[Step]` and a file in that step lost the report, where 5.28.2 writes one, and §5.3's guard alone does not help. So since its R11 it carries §5.4 and §5.5 in its R1, in the same words, and drops `OwnBackgroundSteps` (READ: `638f275d`). Order is free because each plan carries those two lines: whichever lands first ships them, and the second drops them. What this R1 alone still ends is the guess itself.
  - **The guard.** Both keep a step that runs a step out of the rows and in the public list: §5.3's frame, and that plan's frame on `scenario.StepContext`, with `StepPairing` placing a Gherkin step whose frame is nested (its T21). One ships. If #223 is taken its
    frame should win, as its ids need a frame per level anyway, and `apply_on_223.py` takes it and leaves out §5.3's `OpenStep`, `Nested`, `RanByAStep`, `OpenedBy`, `Describe` and `DescribeNested`. Its version also sets the public `StepStopwatchKey` on the scenario, as §5.3 does. Both read a step's outcome by the same rule (§5.3). RUN (`results/edge-shapes-with-223.txt`): under its frame `E35` shows `✗ b0`, `E39` `✗ b0` over `✗ b0.1` and `E41` `✗ 0`, as here.
  - **The split.** That plan's §5.4 lists a hook's `[Step]` as a row in the order it ran, so its mapped list can start with one, and it can carry a keyword. So on that list §5.2's cut at a count becomes a partition, `SplitBackground` in `apply_on_223.py`, about 20 lines: a row counts if no hook opened it (its internal `ScenarioStep.OutsideFrameworkStep`), no later framework step has ended the run of them, and its text is the next the document writes. A row a hook opened stays in `Steps`, as the Cucumber lane has it (READ: `CucumberFeatureSynthesizer.cs:653-656,734`). RUN: `23 woven from a hook` shows `b0`, then the hook's row `0`, though it ran first, which that plan's §5.8 accepts if this R1 lands first. Its stamps, not positions, then place the calls, so that order costs nothing there. This R1 alone never meets such a row, as `MapSteps` gives one row per Gherkin step.
  - **The count.** Both read rows by the document's text, so neither has to skip a nested frame, where a count in the hook would have.
- **#225, `CALLS_UNDER_THEIR_STEP_PLAN.md` (kronikol-ca).** It shares #223's id and stamp. As that session measured and reports (2026-11-03, on `c7252f6c`), it no longer needs this R1: with #223's R1 carrying §5.4 and §5.5, a sub-step's address in a shared Background names a row the report lists, and across five suites none of 733 calls on a step names a row its scenario does not. So its order is its R2 at any time, its R1 after #223's R1 and its R3 after its R1, and this R1 lands either side of all of them, as it said again on the day this plan closed. #128 is its R2's (§5.11). `OrderedStepPaths` is unchanged here, so what its stamp resolves to differs only in which steps are `b`. Its R2 also has the walk skip a row that draws no bar, in `OrderedStepPaths`, `AddDiagramMarkers` and `FeatureSynthesizer.BuildStep`, none of which R1 edits; R3's `FeatureSynthesizer.cs:272,299-312` is not `BuildStep` (`:465`).
- **#226, `STEP_METHOD_SHAPES_PLAN.md` (kronikol-f6).** The weaver. Neither touches what the other does.
- **#224, `STEP_PHASE_PLAN.md` (kronikol-60), and #195.** Phases, with nothing shared in design. #224's R1 edits `ReqNRollTrackingHooks.cs:93-95,103`, beside this R1's `:96`, so whichever lands second resolves one textual conflict.

## 8. Edit sites

| File | Change | Release |
|---|---|---|
| `src/Kronikol.ReqNRoll.Core/ExamplesBlockResolver.cs` | `ResolveBackgroundSteps`, two lookups, `IndexBackgrounds` | R1 |
| `src/Kronikol.ReqNRoll.Core/ReqNRollScenarioInfo.cs` | internal `WrittenBackground` | R1 |
| `src/Kronikol.ReqNRoll.Core/ReqNRollStepInfo.cs` | internal `Nested`, `RanByAStep` | R1 |
| `src/Kronikol.ReqNRoll.Core/ReqNRollConstants.cs` | internal `OpenStepKey` | R1 |
| `src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs` | a frame per open step, outcome from the scenario, `Describe`, `CloseLeftOpen` for a frame a suite's `AfterStep` left open, the internal `CloseStepsLeftOpen` as the `[AfterScenario]` hooks begin, the document's texts | R1 |
| `src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs:62-64,80` | `CountBackgroundSteps`, split at the count, rows only, `DescribeNested`; the detector for a feature built by hand only | R1 |
| `src/Kronikol/Tracking/StepCollector.cs:54-70,480-498,546-563` | an internal `StartStep` overload saying what opened a step, carried to the report's step | R1 |
| `src/Kronikol/Reports/ScenarioStep.cs` | internal `OpenedBy` | R1 |
| `src/Kronikol/Reports/BackgroundStepsDetector.cs:3-7,44-51` | own steps; says it guesses | R1 |
| `src/Kronikol/Reports/AttachmentPlacement.cs:136-153` | each step once | R1 |
| `src/Kronikol/Reports/ReportGenerator.cs:4370`, `:2608,4096,4351-4358`, `:553,558` | open with a failure when the option is unset, `data-holds-failure`, an internal `backgroundOpensOnFailure` passed down from the options | R1 |
| `src/Kronikol/Reports/ReportGenerator.cs:3552` | `HasAnyFailed` reads the step too | R1 |
| `src/Kronikol/Reports/report-select-row-function.js:18-27,33-39,121` | what the reader left is kept for each outline, by a `<details>`' kind; nothing crosses to or from a section or composite holding a failure, or on a row the search chooses or puts back | R1 |
| `src/Kronikol/Reports/report-search-function.js:22-27,383` | the sections a single match needs open on the row shown, once it has chosen that row and each once until the search ends; its own row clicks marked as its own | R1 |
| `src/Kronikol/Reports/report-export-function.js:36`, `report-url-hash-function.js:210` | Clear All ends the search's reveal, as does an address with another search or none | R1 |
| `tests/Kronikol.Tests/Reports/VerdictFencesTests.cs:71` | the search script's pinned hashes | R1 |
| `tests/Kronikol.Tests/Reports/ToggleDefaultsMarkupTests.cs:605-606` | the text a fact pins of the single match's reveal | R1 |
| `tests/Kronikol.Tests.EndToEnd/SearchFunctionPageTests.cs` | a diagrams section the reader closed stays closed on the next key | R1 |
| `src/Kronikol/Reports/ReportToggleDefaultsResolver.cs` | internal `BackgroundOpensOnFailure`; the public record does not change | R1 |
| `src/Kronikol/Reports/ReportToggleDefaults.cs:122-124` | doc: unset, a section holding a failure opens; `false` keeps it closed | R1 |
| `src/Kronikol/Query/QueryCommand.Narrative.cs:161` | the "no step is marked failed" hint reads both lists | R1 |
| `src/Kronikol/Reports/ReportGenerator.cs:4469-4470` | comment: no longer shared | R1 |
| `src/Kronikol/Reports/StepKeywordCollapser.cs:8-11` | comment: no longer shared | R1 |
| `src/Kronikol/Reports/stylesheets.css:1011-1025` | the accent, per Q4 | R1 |
| `examples/.../ReqNRollDuplicateStepsTests.cs:96-120` | corrected, with its comment; §6.3's two new facts | R1 |
| `tests/Kronikol.Tests.EndToEnd/BackgroundSectionRowSwitchTests.cs` | §6.5, new | R1 |
| `examples/.../Component.ReqNRoll.{xUnit2,xUnit3,TUnit}/Features/{StepsAsWritten,StepsThatFail,BackgroundThatFails}.feature`, `StepDefinitions/StepsAsWrittenStepDefinitions.cs` | §6.3, new, among them a doc-string step an `AfterStep` hook fails and `FindsTheStepAHookLeftOpen` | R1 |
| `src/Kronikol.ReqNRoll.Core/ExamplesBlockResolver.cs` | `PlannedStep`, `ResolvePlannedSteps`, `StepsById` | R2 |
| `src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs` | `AddStepsThatNeverRan` | R2 |
| `src/Kronikol/Ingestion/FeatureSynthesizer.cs:272,299-312` | no guess | R3 |
| `src/Kronikol/Ingestion/TestRunRecord.cs:88-93` | doc | R3 |
| `tests/.../WidenedTestRunRecordTests.cs:154-200` | rewritten | R3 |
| `src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs` | `CollectScenario`, a `ScenarioFinishedEvent` handler | R4 |

Wiki, at `c9e0698` (fetched, not pulled; the lines are as they were at `670b1fc`):

- `Step-Tracking.md:402-404`, "Scenarios That Share Their First Steps", is rewritten, not only its "Until #222 is fixed" sentence: after R1 a Reqnroll run does not draw such steps as Background Steps, and what is left is the ingest lane and a scenario built by hand, each with its own copies. `:408-426`, the section is rewritten around §4's rule; "Added in
  v2.33.46", and its claim that the detector runs for LightBDD and BDDfy (`:420`), which has been false since v2.33.71. `:430`, the accent,
  per Q4. `:441`, "assigns the same array". `:445`, the bars. `:449-460`, the section that says the detector finds a `Background:`.
  `:466-468`, per Q2.
- `Generated-Reports.md:873-880`, "or the leading steps multiple scenarios ... share". `:1220`, a panel for a scenario whose steps were all
  extracted.
- `API-Reference.md:65-66`, the detector and the collapser.
- `Ingesting-External-Captures.md:146`, per Q2.
- `Report-Configuration.md:352`, `BackgroundStepsOpen`, whose row gives its default as `false` with no word of a failure: unset, a section holding a failure opens; `false` keeps it closed.
- The rest of the wiki's mentions of a background step were read at `c9e0698` and hold: `API-Reference.md:62`, `Generated-Reports.md:46,396,426`, `Querying-Reports.md:167,614,757`, `Report-Configuration.md:177-180`, `Search-Syntax.md:24,310`, and the skill's `commands.md:20`.
- `CHANGELOG.md`, and Kronikol4J ledger lines (its `docs/REMAINING_PARITY.md`): §5.4's own copies, against `BackgroundStepsDetector.java:62`; §5.6's `open` and `data-holds-failure` on a section (Java renders it, `DotNetHtmlReportRenderer.java:1641`), the row and search scripts (Java copies them) and `HasAnyFailed`; the stylesheet if Q4 says go (Java copies it); and F15.

## 9. Risks

- **Addresses move.** `b1` becomes `1`, `0` becomes `b0`, for the shapes in §2.3. RUN: `kronikol query history` across the fix says "nothing
  changed (against 7 earlier runs)", `query diff` says "no change in results, timings or tracked calls", and a merge keeps the new addresses
  (`results/upgrade-across-the-fix.txt`). READ: `src/Kronikol/History/` never reads a step or a `stepPath`. An address only ever means something
  in one report, so the exposure is a consumer's script that hard-codes `b0`, which is what the changelog is for.
- **More muted text.** Every real Background becomes muted, where today few are. RUN: a failed one stays readable, with a red icon on muted
  text (`results/paint-prototype-failed-background.png`). Q4 is the related choice.
- **A reader used to the grouping.** A shared prefix with no Background stops being set apart. It was never reliable (F6), but it is a change a
  reader can see, and the wiki documents it. Q1.
- **A Reqnroll that renames an internal.** The three members read are the ones `ExamplesBlockResolver` already reads, and
  `Reqnroll_internal_members_the_resolver_depends_on_still_exist` watches them; `IFeatureLevelCucumberMessages` is public. A rename degrades to
  no marking, and the fact fails loudly. Reqnroll's source for the members is identical at 3.0.1 and 3.3.4 (READ).
- **A nested step becomes a sub-step.** It no longer appears as a row of its own in the report or the data file. That is what
  the feature file says, and today's list is wrong anyway (F11). The public lists still hold it. The changelog says so.
- **A committed `Specifications.yml` shows a diff on upgrade.** Steps move between `BackgroundSteps:` and `Steps:`. RUN: 10 blocks in BreakfastProvider's, which commits that file, though its CI consumes no such diff. The changelog says so.
- **#223 stays until its own plan runs.** A `[Step]` in a `[BeforeScenario]` still shifts sub-steps a row, with the calls on no step, as on 5.28.2. Under a step that runs steps they keep only what the collector heard, and nothing is drawn twice, as a nested step is paired by what opened it (RUN: `results/hook-shift-on-the-final-cut.txt`, where an earlier cut drew a phantom `b0.0`). One shape does read worse than on 5.28.2, found by the sixth review: such a hook before a step that runs a step (`R11`) now moves that step's sub-steps to the next row, as it does for plain steps on both builds (`R19`), where 5.28.2's flat nested row happened to absorb the hook's frame. Pairing rows by what opened them would end the shift here, and would lose a hook frame's attachments, which no row would then hold, so it stays with #223's pairing, which lists that frame as a row. RUN: the count is unaffected, as it reads each row's text, which the
  shift does not touch. A reader may meet that fault more readily now the rest of the list is right. §7 gives the order.
- **A hook that runs a Gherkin step leaves a Background unmarked.** RUN (`results/nested-shapes-prototype-on-5.28.2.txt`, `23 gherkin from a hook`): the hook's step
  comes first in the list, so the count is 0. The fourth review measured the same from a `[BeforeScenario]` and from a `[BeforeStep(Order = int.MinValue)]` that run a step, where 5.28.2 calls the hook's step `b0`. Every step is right and only the mark is lost. If its text equals the Background's first step it would
  be marked in that step's place. Rare, and left: the count stays a prefix for the reason in §5.2, and the second review found this too.
- **A suite that catches what a nested step's `AfterStep` threw.** The frame left open takes what runs next until the step that ran it closes: a woven step that ran after sits under it, and one that returned can read failed (RUN by the seventh review, `R721`, `R723`, `R724`; `R13` of the sixth). On 5.28.2 neither is drawn. And a `[BeforeStep]` ahead of ours that runs a step whose `AfterStep` throws demotes the written step under it (`R750`, `R754`), where 5.28.2 has one row. Nothing tells the adapter such a frame is finished before the next hook. Rare, each needs several of a suite's hooks to line up, and #223's frame replaces this one.
- **A suite's `[AfterScenario(Order = int.MinValue)]`, at exactly the adapter's order.** It runs first, as the test assembly is scanned first and the sort is stable, so the step left open is not yet in its list and a step it weaves draws under that step (RUN by the eighth review, `H02`, `H04`, on all three adapters and both Reqnroll versions). At `int.MinValue + 1` the step is listed and closed. On 5.28.2 there is no row at all and the woven step is dropped, so this is not worse. Reqnroll's `HookStartedEvent` would come first, and is not worth a handler for this one shape.
- **`SeparateBackgroundSteps` users see more behind that section.** Every real Background now goes behind its closed summary, where almost none did, and one
  holding a failed step opens unless it was set to stay closed (§5.6). The changelog says so.

## 10. Open questions

Each is a choice; every fact behind them is measured above.

On 2026-11-04 the owner said to implement the plan in full, so each took its recommendation: Q1 yes, as a patch; Q2 in v6, by the line in `V6_PLAN.md`; Q3 by #223's R2, which kronikol-fe is building, so this plan's R2 is dropped; Q4 yes, in R1; Q5 no; Q6 a ledger line; Q7 not here; Q8 this R1 first, as that session agreed; Q9 on its own, as R4, with an issue of its own. The questions stand below as they were asked.

**Q1. Stop guessing in the Reqnroll lane?** Recommended: yes, as designed, with a scenario built by hand keeping the guess, as it has no document (§5.2). The visible cost is that a shared prefix with no `Background:` stops
being muted and addressed `b0` (RUN: 10 of BreakfastProvider's 214 rows). The other choice is own copies only (the issue's suggestion), leaving F1, F2 and F6, or a new "shared steps" mark,
which is a feature nobody asked for. This also confirms §7's reading of the MAJOR line on both counts, the shared prefix and a section left unset opening for a failure. Recommended: both are bug fixes, so a patch. If the owner reads either as a default moving, that piece goes to v6: the mark stays guessed there, or the section stays closed and the failure has to be found.

**Q2. Stop guessing in the ingest lane, now or in v6?** Recommended: in v6, by a line in `plans/V6_PLAN.md`. RUN (`results/r3-trial-facts.txt`): removing the call fails one of 591 ingest facts, the one §8 names, so it is small whenever it comes. R1 already fixes that lane's data with its own copies. What stays is a producer's keyworded stream showing a `When` as a Background, where that producer could say `background: true`. The mark is wrong, but it is documented and no fix forces its removal, which by `CLAUDE.md`'s MAJOR line makes it a default changing. The other choice is R3 as a patch now, if the owner reads the mark as a plain bug, for one rule everywhere sooner, with the change called out.

**Q3. List the steps skipped after a failure?** Recommended: yes, and by `STEP_IDENTITY_PLAN.md`'s R2 (the event), its Q3 being the same question,
so one answer settles both; §5.7's pickle route only if that plan is not taken. It makes the two Gherkin lanes list the same steps, and step counts
change only on scenarios that stop early (failed, undefined or pending).

**Q4. Paint the accent the wiki describes?** Recommended: yes, in R1, as the marked steps now mean something and muted text is a thin cue. Measured
with it forced on: `results/paint-accent-forced-on.png`, where it sits close to the chevron; as released it stands left of the chevron, the same 25px in all, so the steps still read straight down. The other choice removes
the claim from the wiki and the stylesheet, and leaves muted text alone.

**Q5. `[Obsolete]` on `BackgroundStepsDetector`?** Recommended: no. It keeps a caller whatever Q2 says (§5.2's hand-built path), its summary says it guesses, and a consumer calling it with warnings as errors would stop building in a patch. If it is to go, that belongs with Q2 in v6.

**Q6. File an issue on Kronikol4J for its unused copy?** Recommended: a ledger line only, as nothing calls it. An issue if the owner wants the port's
doc corrected now.

**Q7. Step source lines on this lane?** The same lookup could fill `SourceFile` and `SourceLine` on each Gherkin step, which is the part of #76 still
open. Recommended: not here. It is small and belongs to #76, and is worth doing right after.

**Q8. Which lands first, this R1 or #223's R1?** Recommended: this R1, being the smaller, with #223's plan not green-lit either. The cost is §5.3's guard shipped and then replaced by that plan's. The other order is the one measured on the final cut, and R1 then ships as `apply_on_223.py` has it, smaller by the guard. The end state is the same, and F3 ends with whichever lands first, as each carries §5.4 and §5.5. It is the same question as that plan's Q4.

**Q9. Where is the lost scenario (F16) fixed?** Recommended: on its own, as R4, whenever the owner likes, ahead of both plans. A digest saying "# No failures" on a failed run is the worst single reading found here, the fix is under 30 lines, and it depends on nothing. The other choice is to wait for #223's R2, which has the same fix, if that plan is green-lit soon. Either way it wants an issue of its own, which I have not filed, as that is a public step.

## 11. What it leaves

- #223, and the positional pairing in `MapSteps`, to `STEP_IDENTITY_PLAN.md`. R1 removes one way of it going wrong (F11) and leaves the others.
- Reqnroll's message stream drifting past a nested step (`PickleStepSequence.NextStep`) is upstream's. Worth a report to them if the owner wants one;
  it misattributes steps in their own formatter's output.
- `ToggleDefaults.StepsSectionOpen = false` still closes a list holding a failed step (`ReportGenerator.cs:4413`). Whoever sets it asked for that, where
  the Background section is closed by default.
- A suite's `[AfterScenario]` hook that throws, and `E27` on the floor: F16, R4 and Q9, and none of R1.
- #128, a step whose text the bar escapes, wraps or cuts: §5.11, to #225's R2.
- An ingest whose steps carry `background: true`: such a step draws no bar and stays first in `OrderedStepPaths`, so `StepAttributionMismatch` fires and every call loses its step, on 5.28.2 too. RUN by the fourth review. #225's R2 now holds it, by having the walk skip a row that draws no bar (kronikol-ca, 2026-11-03, under its own review). If that does not ship, it wants an issue, the owner's to file.
- An undefined step in a Background, or skipped after a failure, is not listed until R2 or #223's plan. RUN: E24 and E26.
- Found by the last six reviews, and the same on 5.28.2 or 5.28.3, so none of this issue's, each small: emptying leaves open the sections the search opened on the row shown, as they stay open there (the eighth's fault 1, `F1a` to `F1c`, which it called partly a judgement; they no longer follow the reader to the next row); Clear All and Back leave `tr.row-search-match` and the row the search chose, and the next emptying returns to the old row, even one the reader came back to by hand (the ninth's and tenth's); a Steps section holding a failed step takes a carried close, hiding the failure; an address applied while the outline is hidden chooses no row; a click on the shown row, or a search then emptying, drops what an undrawn nested composite would take; if the first row lacks a section, a search opens it on the next, and after emptying, clicking that row then another shows it open there (the tenth's); emptying leaves open the sections the search opened on the row shown and the diagrams section, though the wiki says it closes what it opened; the scenario itself reopens on the next key (`B10`); a Search-tab line for a later row's sub-step lands on the first row's line; Back and Forward between two row links change the address and not the row shown; Back to an address with no search, and Clear All, undo nothing; the search moves a reader off a matching row to the first matching one; a single-match search does not open a composite holding the word; closing Steps on one row carries onto a row whose failure is in its Steps; a step whose `BeforeStep` and `AfterStep` hooks both throw is listed on neither build (`R710`); a call in `[AfterScenario]` counts under "Calls in the failing step" (`R753`); and `StepStopwatchKey` read in a composite's hook after a normal nested step gives 0 ms.

## 12. Log

- 2026-11-02: written at 5.27.13 in worktree `C:/Code/Kronikol-222`, branch `plan/222-background-steps-per-scenario`, in answer to "a detailed well
  researched plan to fix the underlying problem". Probes in the harness; nothing pushed. Read against `STEP_IDENTITY_PLAN.md` (#223) on the same day:
  §5.7, §7 and Q3 record where the two meet, and kronikol-fe and kronikol-ca were told. kronikol-fe answered that its prototype now guards F11 as well, which
  corrected what §7 had said, and reading its hook rows against §5.2 found the split above. An independent review then read the plan against the code: it
  found R1's first cut lost a nested step's arguments and drew a passed step over a failed one (§5.3), that one claim of "every mode" had one run behind it,
  and a miscount in the table probe. Those are fixed here, and the prototype and every table were run again.
- 2026-11-03: checked again on main at `9d994a4b` (5.28.2), at the owner's request for a complete and accurate plan with any deep dives that might change it. No line reference moved but four past `ReportGenerator.cs:5990` (+3), and the patch applied clean. The deep dives changed R1 in five places, each in §5.2, §5.3 or §5.6: new shapes (`probes/edge-shapes/`) found an early cut replacing a suite's error with "Stack empty" (E36), so a frame sits on its step context; running with #223's prototype moved the count to the mapped rows; a second independent review, given the code and not this plan, found a nested step with no hook left a passed parent (E43), so a step reads the scenario's status, and a kept field went; `DescribeNested` stalling at a woven helper (E42); and the open section closing when an outline's row is switched to (E44). It also found smaller faults, fixed: a frame left open by a suite's `AfterStep` (E45), a null in the scenario context, nested tables missing from `ExampleValueGrouper`. New evidence: all five modes on all adapters (the first cut had one green run on two of them), diagrams and calls compared scenario by scenario, BreakfastProvider run and not only read, R3 tried (`results/r3-trial-facts.txt`: of 591 ingest facts, only the one §8 names pins the guess), R1 and #223 run together. The matrix also ran on Linux, at zero in all five modes and with the crash case writing its report (`results/shapes-prototype-linux.txt`), and the 129 Playwright facts nearest this change pass (`results/playwright-on-prototype.txt`). Still not run: the rest of the Playwright suite and CI's other lanes, which a release runs anyway, and a suite using the external-data plugin, where a pickle may not be found and §5.1's title route and §5.2's text check are what stand.
- 2026-11-03, later: a third, fresh review read the plan against the code and the stored results, and found what it should. Three of R1's changes were called necessary and were not, so they came out rather than being argued: the public lists keep a nested step, an explicit `BackgroundStepsOpen = false` is honoured, and a scenario built by hand keeps the guess. The patch claim now holds by construction (§7). It found §6.3 would have turned CI's example lanes red as written, so failures there are gated by an environment variable and the first cut is now run (`prototype/apply_examples.py`), which also gives the hooks real coverage. It found an inner step with no frame vanished (E39), and that several results predated the final cut, so everything was run again on one final build, labelled, with edge and nested shapes also on the floor, xUnit v3 and TUnit. R1 now fixes outright three faults the plan had left to a plan not green-lit, as `CLAUDE.md` asks: a failed step with passed sub-steps starting collapsed, a step the bar escapes or wraps losing its calls (§5.11), and a step a suite's `AfterStep` kept ours from closing. R3 changed from a recommended patch to a recommendation for v6, and `[Obsolete]` to no (§10). New evidence: core 11,853 of 11,855, all 263 Integration facts, the whole Playwright suite, zero wrong rows in all five modes on every adapter and on the floor, BreakfastProvider's 10 marks to none with 214 of 214 passing, and the whole again with #223's prototype at `c7252f6c`. §2.4's census had read an old checkout of BreakfastProvider, and now reads today's. The mutation pass on the final cut then found two real gaps in the facts: M15 lost an inner failing step while the real run's fact, using `Contains`, stayed green, so it now names each one in order; and nothing covered §6.2's query hint, so it has a fact. A suite using the external-data plugin was run after all (§5.1), and the whole Playwright suite passed, 2,093 of 2,121 with 28 skipped and none failed. Reading E47 alone found F16's worst form, "# No failures" on a failed run, which is not this issue's but cannot wait for it (R4, Q9). Every number in this entry is that cut's; the last entry gives the final patch's.
- 2026-11-03, last: a fourth independent review of the final patch, given the code and not this plan, reproduced ten faults. Two regressions were real and changed R1: the row switch (§5.6), and a toggle record built by hand opening (§5.6). The matcher went back to published's and left R1 with #128 (§5.11), after this review and #223's fifth both measured it refusing raw bars and that one measured false matches; the log entry above calls it a fix R1 carries, and that no longer holds. Smaller: a nested step paired with a woven step of the same text (§5.3), and a failing nested run writing to CI's job summary (§6.3). The rest were already this plan's choices (§5.1, §9), or faults published has too (§9, §11). Each review so far has found real faults, including the last, so a release's own review should not be skipped on the strength of these.
- 2026-11-03 to 11-04, last: four more independent reviews, each given the code and not this plan. Each found real faults, and the faults of the fifth's fixes were the sixth's findings, of the sixth's the seventh's, and of the seventh's the eighth's.
  - The fifth (`results/fifth-review.md`) reproduced seven. A `[GivenStep]` of a nested step's text took its outcome, so a step that passed read failed in `Failures.jsonl`: a nested step is now paired by what opened it (§5.3). A flag on the public toggle record drew its third fault in three reviews: the record is now as published, and only the options say what was left unset (§5.6). A reader's choice was lost past a failing row: what they left now lasts as long as the page (§5.6). A failed composite's state crossed rows, and now holds as a section does.
  - The sixth (`results/sixth-review.md`) found no fault in the toggle, and a red suite: three facts this plan's measurements had not run or had not read. It found the search's first answer losing what the reader closed, a nested step a suite's `AfterStep` threw on losing its arguments, and the public stopwatch reading 0.
  - The seventh, narrow (`results/seventh-review.md`), found a step left open taking in the `[AfterScenario]` hooks' time and work, and the search's second answer reopening what a reader closed. The two rare shapes it left are in §9.
  - Reading the surface after that round found the seventh's answer had added a public method to a public class, which this plan says it does not do and `CLAUDE.md` makes a minor. Reqnroll runs a non-public hook, on the floor too, so it is internal, a fact pins the class's public methods, and `results/public-surface.txt` compares every public member with published 5.28.2.
  - The eighth, narrow (`results/eighth-review.md`), was given what no review had read: that hook and the search's lists. It found seven faults, five in the lists. That made two reviews running to find faults in lists whose only gain was a leak 5.28.2 has, so they went, as the flag on the public record did (§5.6). That ended four of the five, as its own shapes show run again (`results/eighth-review-on-the-final-cut.txt`), and the fifth is as 5.28.2 does it (§11). A step a suite's own hook ran and left open now closes in the adapter's last hook (§5.3), and a suite's hook at exactly `int.MinValue` is in §9. Its hook shapes on the final cut are `results/eighth-review-hooks-final-*.txt`.
  - The final patch, on one build in one go: core 11,870 of 11,872, `Kronikol.Tests.SearchEngine` 787 of 787, `Kronikol.Tests.StepTracking` 61 of 61, LightBDD 35 of 35 and 11 of 11, every Integration fact 265 of 265, the whole Playwright suite 2,117 of 2,146 with 28 skipped and one failure, the flake below, all 65 mutations caught, zero wrong rows in all five modes on xUnit v2, xUnit v3, TUnit, row tests off, the floor and Linux, every diagram and call alike across the fix, BreakfastProvider's 214 of 214 with its 10 invented marks gone, R2 and R4 on it, and #223's final prototype at `638f275d`, whose own 28-scenario suite then differs only in times and its own new mark.
  - `BackForwardRestoreTests.R16` failed on WebKit in the final whole-suite run, as it did on Firefox and WebKit in an earlier one and on about 3 of the sixth reviewer's 15 WebKit runs. It is a known flake and not this patch's: `plans/CALLS_AND_EVENTS_PLAN.md:162` measured it failing 2 of 10 on a clean tree, that plan's log (`:734`) has it failing a whole suite at 5.28.0, and #224's plan has it 1 of 20 on 5.28.2. It scrolls a long report and goes Back, with no row and no search, so nothing this patch changes runs in it. Run here by turns on pristine main and on the patch, under the same load, it failed 0 of 15 and 1 of 15 on all three engines (`results/r16-flake.txt`), then 0 of 40 and 0 of 40 on WebKit alone in equal time (`results/r16-flake-webkit.txt`), so those runs are too few to tell the two apart either way.
  - **The last change to the source, `prototype/apply_r1o_search_by_row.py`, has had no independent review.** Its facts, its mutations and the eighth review's own shapes, run again, are all that check it, though it mostly takes code out. Eight reviews have each found real faults in what changed last, and they were getting smaller, so a release's own review should expect to find more, and should not be skipped on the strength of these.
  - Still not run: CI's other lanes, which a release runs anyway, and this final R1 landing first with #223's second, whose end state is the same code as the order that ran (§7).
- 2026-11-04 to 11-05, executed, at the owner's "implement the plan in full". The other plans from the same suite were told the same that day, so kronikol-f6's 5.28.3, kronikol-ca's 5.28.4 and this R1 went out one after another, with Playwright and example runs announced by message between the sessions.
  - **Red first.** The prototype's facts on main's source with do-nothing members standing in, so they compile: 39 of 110 core facts, 12 of 38 browser facts and 5 of 12 real-run facts fail, and those that pass are guards (`results/red-*-before-the-fix.txt`). Facts §6 lists that the first cut had not written came next, and fail on main for the reasons they should: the ingest crash's `KeyNotFoundException`, the two lanes parity fact over `TestData/ReqNRoll/BackgroundShapes.ndjson`, a section left closed, a 0px accent, and a real failing run's page through `FullPipelineFixture`. `A_failed_background_step_shows_the_failed_icon_on_its_muted_text` passes there, as a guard (`results/red-later-facts-before-the-fix.txt`).
  - **Q4's accent** was painted where §5's forced-on picture left it, and the ninth review found the triangle on top of it. It now stands left of the triangle: `margin-left: 9px; padding-left: 13px`, 25px in all, so the steps read straight down.
  - **The ninth review**, the release's own, given the code and not this plan (`results/ninth-review.md`), found three faults a reader sees that the patch would have brought in: Clear All and Back leaving a match hidden on the next search, a row the search chose undoing what the reader left, and that triangle. It also found the fixture taking a failed build for a run asked to fail, and a fact that did not pin its name. All fixed, red first (`results/ninth-review-red-then-green.txt`). So a section 5.6 sentence changed: every switch now keeps what the row left held, and a row the search chose keeps only what the reader changed there.
  - **The tenth review**, narrow, of the ninth's fix (`results/tenth-review.md`), found four faults in it, all new, and tried a fix for each on the page: a search landing on the row shown passing on what it opened, the reader's own opening counted as the search's, Back to the same search reopening what the reader closed, and sub-steps drawn since the row was chosen reading as the reader's change. All fixed, red first (`results/tenth-review-red-then-green.txt`), and with those fixes on, its 260 random reader sequences flagged nothing. The fact for the fourth proved nothing at first: its red and its first green both failed in its own helper, and then the mutation loop named it on every line, as it read a nested step before the row had drawn it. It waits for the step now, and M82 is what shows it can fail. The fixes as committed have had their facts and mutations and no review after them, so what §12 said before holds: a later review should expect to find more, and they have been getting smaller.
  - **Mutations** on both fixes (`results/mutations-ninth.txt`, `-tenth.txt`, `-after-tenth.txt`, `-accent.txt`): all caught but three, each of which said something. Two guards nothing needed came out (M79's and M85's, the second the tenth review's own, so the code differs from what it tried by that line), and a mark the reader's return must clear got `A_row_the_search_chose_is_the_readers_once_they_come_back_to_it`. The list's other entries were not run again, as the code they break did not change. M72 was never used.
  - **Ticking §6 and the changelog against the diff** found two sentences with no fact and a proof not run. "Its `Duration` is its own" gets a check in the real run's `ReadsThePublicList`, proved by M86, and the Features table's counts get `FeaturesTableOverOwnStepsTests`, proved by M1. §6.6's history and `query diff` check was not in the proof script and now is, as its `upgrade` stanza. §6.5's row chosen from a passing row is `BackgroundSectionRowSwitchTests.A_failing_rows_section_is_open_and_the_row_left_keeps_its_own_state`, not in `SeparatedBackgroundRenderingTests`. No Reqnroll project names `$(FloorRun)`, so §6.6's `-p:FloorRun=true` is CI's floor steps on the pushed commit, and §6.6's floor for this adapter is the matrix's `-p:ReqnrollVersion=3.0.1`.
  - **The release's round** on the rebased commit, by `probes/release-proof/run.sh` (`results/release-proof.txt`): `release.slnf` in Release for every target framework with no warning in a changed line; core 12,084 of 12,086, 2 skipped; `Kronikol.Tests.SearchEngine` 787; `Kronikol.Tests.StepTracking` 61; LightBDD 35 and 11; all 265 Integration facts, and the Reqnroll example projects alone 12, 21 and 12; the whole Playwright suite, 2,136 of 2,164, with 28 skipped and no failure; zero wrong rows in five modes on xUnit v2, xUnit v3, TUnit, row tests off and the floor, where published 5.28.5 invents 11 and borrows 8; diagrams and calls alike on all 41 against it, flow on 39, and 5.28.5's step mismatch warning, which it prints above every answer on this suite, gone from the release; every public member the same as 5.28.5 (5,605 lines); history saying nothing changed and `query diff` no change in results, timings or tracked calls, across seven runs on 5.28.5 and one on the release; BreakfastProvider 214 of 214, its 10 invented marks to none, with its 10 scenarios' addresses moved and no others.
