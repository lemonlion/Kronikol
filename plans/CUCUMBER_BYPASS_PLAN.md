# Cucumber bypass plan: #105

**Written:** 2026-10-06, at 4.6.0 (`37e93813`), eight days after #105 was filed. **Status: green-lit 2026-10-07 by the
owner ("complete the plan in full"), decision D34, every question as recommended; executed the same day as R1 (patch,
4.6.2) and R2 (minor, 4.7.0), §11.** Evidence
labels: **RUN** (measured here), **READ** (in the source, `file:line` at `37e93813`), **INFERRED** (reasoned from facts,
stated by none), **ISSUE** (taken from #105, not re-measured). The probes behind every RUN line are in
[`CUCUMBER_BYPASS_PLAN.harness/`](CUCUMBER_BYPASS_PLAN.harness/README.md), with their output: the issue's three ingests
and three more on a 4.6.0 build, a real playwright-bdd 9.2.0 run on @playwright/test 1.62.1 (published, unpatched), and
a cucumber-js 12.9.0 run.

#105 asks for a step that was skipped over while the steps after it ran to come out of `kronikol ingest` as `Bypassed`,
as it does from the LightBDD adapter, whichever of the two ingest inputs says so, and for its scenario to be `Bypassed`
too. The issue is right on every count, and its table reproduces verbatim on 4.6.0 (§1). This plan checks each claim,
lists what the issue does not say (§3), and designs the fix around four things it does not mention:

- **The explicit signal works on today's playwright-bdd.** An attachment made inside a step's body lands on that step in
  the messages file, and the step is reported `PASSED` (RUN). The `kronikol-bypass` attachment the issue proposes needs
  nothing from playwright-bdd.
- **The inference is Kronikol's own definition of the word.** The report's tooltip for a bypassed scenario reads "some
  or all of the logic in a step was intentionally skipped over at runtime without preventing execution of subsequent
  steps" (`ReportGenerator.cs:2152`). A `SKIPPED` step followed by a step that ran is that, by construction. In every
  case measured, neither playwright-bdd nor cucumber-js ran a step after a skipped one (RUN), so the rule finds nothing
  in their output that is not a bypass. Its blind spot is the one the issue names: a bypassed last step.
- **Of the lanes that record a bypassed step today, only LightBDD rolls it up to the scenario.** The tests-file
  lane and every in-process adapter that records `SkipIf` steps (xUnit v2 and v3, NUnit, MSTest, TUnit, Reqnroll) keep
  the framework's verdict, `Passed` (F2). The issue asks for the ingest lanes; the in-process ones are a question (Q2).
- **Two summaries hide a bypassed scenario today.** `kronikol query summary` counts every scenario that did not fail as
  passed (F3, RUN), and the CI summary's rows leave a bypassed scenario out (F4). Once ingest writes `Bypassed`, both
  would misreport exactly the runs #105 is about, so both are fixed in the patch.

## 0. Summary

| | |
|---|---|
| What ships | **R1, a patch:** the Cucumber lane reads a `SKIPPED` step that a later step of the same attempt ran after as `Bypassed`, with the step's message as its bypass reason; a bypassed step rolls up to its scenario in both ingest lanes (Failed over Skipped over Bypassed over Passed); a tests-file `bypassed` step survives an ingest that also reads Cucumber Messages; `kronikol query summary` and the CI summary count skipped and bypassed scenarios. **R2, a minor:** a `kronikol-bypass` step attachment marks that step `Bypassed`, with its body as the reason, whatever status the producer gave it, unless it failed |
| What it fixes | The issue's three runs give one answer: middle step `Bypassed` with its reason in `bypassReason`, scenario `Bypassed`, history `B` (T1, T9, T12) |
| What it cannot fix | A bypassed **last** step from a producer that says only `SKIPPED` and writes no tests file: nothing in the messages tells it from a step that skipped the rest of the scenario. R2's attachment, or the tests file, covers it (§4.1) |
| What does not change | Every diagram (an ingested step bar does not draw its status, F7); every run with no bypass in it (T8); the in-process adapters (Q2); Kronikol4J, which has no Cucumber Messages ingest (F13) |
| Bumps | R1 patch, R2 minor (§6.2). No new option in either |
| Open | Q1 to Q10 (§9). The ones that shape the work are Q1 (ship the inference at all), Q3 (the tests-file roll-up's scope) and Q4 (how a tests-file step finds its Gherkin step) |

## 1. How far each claim was checked

| # | The issue says | Level | Verdict |
|---|---|---|---|
| C1 | Kronikol has `Bypassed` for steps and scenarios: the LightBDD adapter maps it, tracked C# steps get it from `SkipIf`, the tests file accepts `status: "bypassed"` with `bypassReason` | READ | **True.** `FeatureResultExtensions.cs:419` (scenario) and `:432` (step); `StepCollector.cs:161-198` (`BypassStep`, which the IL weaver calls when `SkipIf` holds) and `:439-448`; `FeatureSynthesizer.cs:439`, `:446`, `:510`; `TestRunRecord.cs:94-95` |
| C2 | The Cucumber importer has no `Bypassed`: a step skipped from inside, with the later steps run, comes out `Skipped` with its reason in `Comments` | READ, RUN | **True.** `MapStatus` maps `SKIPPED` and `PENDING` to `Skipped` and nothing to `Bypassed` (`CucumberFeatureSynthesizer.cs:723-730`), and a step's message goes to `Comments` whatever its status (`:456`). Run A |
| C3 | One skipped step makes the scenario `Skipped`; history records `S` | READ, RUN | **True.** `Worse` ranks Failed, then Skipped, then Passed (`:714-720`). Run A: `Skipped`, history `S` |
| C4 | The merge drops the tests file's bypass | READ, RUN | **True, and earlier than the issue says.** The records are removed before synthesis, at `IngestPipeline.cs:390` (`IsReplacedStep`), so `CucumberFeatureMerger` never sees them; its `CarryOver` keeps the attachments, the error text and the duration (`CucumberFeatureMerger.cs:120-129`). Run C equals run A |
| C5 | In the tests-file lane the step is `Bypassed` and the scenario stays `Passed`, where LightBDD would say `Bypassed` | READ, RUN | **True.** The verdict is the `end` record's alone (`FeatureSynthesizer.cs:184`). Run B. LightBDD is the only lane that rolls up, though (F2) |
| C6 | History flips between `P` and `S` for one scenario by ingest path | RUN, READ | **True of the letters, not of the verdicts.** Run A writes `S`, run B `P`. The analyzer reads only `P` and `F` as verdicts (`HistoryFormat.cs:115`), so neither letter makes a scenario flaky or failing; the sparkline's colour is what differs (F6) |
| C7 | The code references at `f710f745` | READ | **Moved.** 4.2.0 added step locations and attempts. `Worse` is now `:714`, `MapStatus` `:723`, `ExtractError` `:732`, the comment `:456`, the roll-up `:489` |
| C8 | Fix 1a works with today's playwright-bdd, which reports the step `PASSED` | RUN | **True.** `$testInfo.attach('kronikol-bypass', …)` in a step body produced an attachment carrying that step's `testStepId`, and the step `PASSED` (probe, scenario 1) |
| C9 | After a failure, playwright-bdd and cucumber-js skip every remaining step | RUN | **True.** Both reported every step after a failure `SKIPPED`. Both also skip every later step after one skipped from inside (playwright-bdd's `$test.skip`, cucumber-js's `return 'skipped'`), cucumber-js reports a later undefined step `UNDEFINED` while it skips, and both run after-hooks after skipped steps (§4.1 counts neither) |
| C10 | Fix 1b cannot recognise a bypassed last step | RUN, INFERRED | **True, and the message cannot decide it either.** Run E (`PASSED`, `PASSED`, `SKIPPED` with a message) reads exactly like a scenario whose last step skipped the rest. playwright-bdd's `$test.skip` writes no message (RUN), so a message's presence would not separate the two there, and Cucumber-JVM's assumption failures write one (A3) |
| C11 | Five tests would pin it | READ | **Necessary, not sufficient.** They miss the after-hook that runs after skipped steps, retries, a failed step with the attachment, a bypassed sub-step in the tests file, the join's failure case and the two summaries (§5) |

## 2. Where it stands today

What each lane reports for a step the test bypassed, in the issue's scenario:

| Lane | The step | Its reason is in | The scenario | History | Level |
|---|---|---|---|---|---|
| LightBDD, in process (`StepExecution.Current.Bypass`) | `Bypassed` | `bypassReason` | `Bypassed` | `B` | READ |
| Tracked steps with `SkipIf`, in process (xUnit v2 and v3, NUnit, MSTest, TUnit, Reqnroll) | `Bypassed` | `bypassReason` | the framework's verdict, `Passed` | `P` | READ |
| `kronikol ingest --tests` (run B) | `Bypassed` | `bypassReason` | `Passed` | `P` | RUN |
| `kronikol ingest --cucumber-messages` (run A) | `Skipped` | `comments` | `Skipped` | `S` | RUN |
| both (run C, what a playwright-bdd consumer runs) | `Skipped` | `comments` | `Skipped` | `S` | RUN |

How an ingest with both inputs runs (READ, `IngestPipeline.cs:384-438`):

1. The messages are synthesised into the report model and into `start`, `step` and `end` records (`Markers`), one set
   per scenario they own. Only `IncludeHooks` and `DefaultFeatureName` reach the synthesizer from the CLI (`:385-386`).
2. The tests file's `step` records for those scenarios are removed (`:390`), and the markers are added in their place
   (`:391`).
3. Attribution, attempts and diagram markers run on the combined records. A step marker draws its text, keyword, table
   and doc string (`:933-934`); its status is not drawn.
4. `FeatureSynthesizer.Build` builds a model from all the records (`:432`), and `CucumberFeatureMerger.Merge` replaces
   every scenario the messages own with the messages' own (`:438`), keeping the tests file's attachments, error text and
   duration, and nesting its `assertion` records under the Gherkin steps.

So what the messages say about a step is decided in the synthesizer, which builds both the scenario and the markers, and
a tests-file step that should survive has to be read before `:390`.

## 3. Findings the issue does not state

| # | Finding | Level | Where |
|---|---|---|---|
| F1 | **The tests file's steps are gone before the merge runs.** `IngestPipeline.cs:390` removes every tests-file `step` record of a scenario the messages own, so the merge has nothing to carry across. The fix reads the `bypassed` records before that line and hands them to the synthesizer | READ | §4.2 |
| F2 | **Only LightBDD rolls a bypassed step up to its scenario.** Every other adapter takes the scenario's verdict from its framework: xUnit v3 (`TestContextEnumerableExtensions.cs:40`), NUnit (`:42`), MSTest (`:33`), TUnit (`:44`), Reqnroll (`ScenarioInfoEnumerableExtensions.cs:59`) and xUnit v2 (`ReportingTestFramework.cs:85-136`). A `SkipIf` step is `Bypassed` in each, and its scenario `Passed`, which is what the tests-file lane does. After this plan the ingest lanes agree with LightBDD and disagree with these six | READ | Q2 |
| F3 | **`kronikol query summary` counts every scenario that did not fail as passed.** `QueryCommand.Overview.cs:80-90` prints `{total - failed} passed` per feature and puts the same number in the JSON item. Run A's skipped scenario reads "Bypass prototype  1 passed"; the playwright-bdd probe's three scenarios (passed, skipped, failed) read "2 passed, 1 FAILED". The narrative verbs count it properly, under a comment that names this exact mistake: "A scenario that was skipped did not pass, and saying it did is the one line here that ends an investigation" (`QueryCommand.Narrative.cs:36-43`). Once ingest writes `Bypassed`, every bypassed scenario would read as passed in the verb an agent runs first | RUN, READ | R1 |
| F4 | **The CI summary leaves bypassed scenarios out of its rows.** `CiSummaryGenerator.cs:27-29` counts `Passed`, `Failed` and `Skipped` with `==`, so a `Bypassed` or `SkippedAfterFailure` scenario is in the "Scenarios" total and in no other row, and the rows stop adding up. True for LightBDD today; ingest would add the issue's runs | READ | R1 |
| F5 | **`ExecutionResult.Bypassed`'s doc names a case that never produces it.** "The scenario was bypassed by the framework (e.g. inconclusive)" (`ExecutionResult.cs:17`). MSTest and NUnit map an inconclusive result to `Skipped` (`Kronikol.MSTest/TestOutcomeExtensions.cs:20`, `Kronikol.NUnit4/TestResultExtensions.cs:19`), and nothing maps to `Bypassed` but LightBDD's bypass, `SkipIf` and the tests file. The doc ships in the package | READ | R1 |
| F6 | **History does not read the flip as a verdict.** `IsRealVerdict` is `P` or `F` (`HistoryFormat.cs:115`), so `S` and `B` give no flaky, failing or alternating verdict. What the issue sees is the sparkline drawing `P` and `S` in two colours (`HistoryHtml.cs:308-323`), on the labs page since 4.6.0. After the fix a live run draws `B` beside the local run's `P`: still no verdict, and now the true picture | READ | §4.6 |
| F7 | **No diagram changes.** An ingested step bar draws the step's text, keyword, table and doc string (`IngestPipeline.cs:933-934`); its status and error are not drawn. The fix changes the step list, the JSON and the counts, never a diagram's source | READ | §4.6, §6.6 |
| F8 | **A `kronikol-bypass` attachment shows nothing today, and would show as a file with text attachments on.** A plain-text body is written out only with `WriteTextAttachments` (`CucumberFeatureSynthesizer.cs:956-958`). Run F and the probe's scenario 1 drew a `Passed` scenario with no trace of it. The fix consumes the attachment as it consumes `kronikol-test-id` (`:313`), so it never appears as a file | RUN, READ | §4.1 |
| F9 | **playwright-bdd's `$test.skip(condition, reason)` loses its reason.** Called in a step, it reported that step `SKIPPED` with no `message` and every later step `SKIPPED`. That is a skip of the rest of the test, which the inference leaves `Skipped`, and its reason is in no envelope Kronikol could read | RUN | §8 |
| F10 | **The lanes word a step after a failure differently.** LightBDD's adapter marks it `SkippedAfterFailure` (`FeatureResultExtensions.cs:426-437`); the Cucumber lane marks it `Skipped` (run D) | READ, RUN | Q9 |
| F11 | **The wiki says `Bypassed` is LightBDD's alone.** `Generated-Reports.md:511`: "Step body ran, developer opted out; execution continues (LightBDD only)". `SkipIf` has produced it since 2.34.3 and the tests file since 3.0.45 | READ | §6.4 |
| F12 | **A bypassed scenario counts against the pie chart's pass rate.** `ReportGenerator.cs:2656` divides passed by all four counts. A tests-file run (run B) that reads 100% today reads 0% after R1. Not a defect, since a bypassed scenario did not run all of its checks, but a visible change the changelog states | READ | §6.3 |
| F13 | **Kronikol4J has no Cucumber Messages ingest.** Its Cucumber-JVM plugin records a scenario status and no steps, and maps `UNDEFINED` to skipped (`KronikolCucumberPlugin.java:69-74`), where the .NET importer maps it to failed (`CucumberFeatureSynthesizer.cs:727`) and the Reqnroll adapter to skipped (`ExecutionStatusExtensions.cs:18`) | READ | §6.6, §8 |

## 4. The design

### 4.1 A step's verdict in the Cucumber lane

Three signals, read in this order. Each applies only to a step that did not fail (`PASSED`, `SKIPPED` or `PENDING`); a
`FAILED`, `UNDEFINED` or `AMBIGUOUS` step stays `Failed` whatever else is said about it.

1. **The `kronikol-bypass` attachment (R2).** An attachment named `kronikol-bypass` (matched as `kronikol-test-id` is,
   ignoring case, `:695`) whose `testStepId` is a Gherkin step of the attempt the scenario is built from.
   - Its decoded body, trimmed, is the reason. `BASE64` bodies are decoded as the test id's are (`DecodeText`, `:698`).
     An empty body bypasses with no reason: the attachment's presence is the signal.
   - Two on one step: the first non-empty body is the reason, as the first test id wins (`:640-653`).
   - The attachment is consumed: never written as a file, whatever `WriteTextAttachments` says (F8).
   - On a hook step, or on no step, it bypasses nothing: it is consumed, and the synthesis gets a warning naming the
     scenario. Only a Gherkin step can be bypassed.
2. **The tests file's `bypassed` step record (R1, when `--tests` is given too):** §4.2.
3. **The inference (R1).** A step whose status is exactly `SKIPPED` is bypassed when a later Gherkin step of the same
   attempt finished `PASSED` or `FAILED`. Its `TestStepResult.message`, or its exception's message, is the reason.
   - "Later" is the order of `testCase.testSteps`, the test case's own plan, which the synthesizer already walks
     (`:361`). The order envelopes arrive in does not matter.
   - Hook steps count on neither side: after-hooks run after skipped steps in both producers measured (C9).
   - A later `UNDEFINED` or `AMBIGUOUS` step proves nothing: cucumber-js reports them while it skips (C9). A later
     `PENDING` step is not counted either. No producer measured runs any step after a skipped one, and the rule should
     claim no more than it needs.
   - Only the attempt the scenario is built from (the last, as today) is read.

The reason comes from the first signal that has one: the attachment, then the tests file's `bypassReason`, then the
step's message. The two explicit signals are written for Kronikol; the message is whatever the producer put there.

| The producer said | Attachment | A later Gherkin step ran | The tests file says `bypassed` | The step | Its reason |
|---|---|---|---|---|---|
| `PASSED` | yes | any | any | `Bypassed` | the attachment's body |
| `PASSED` | no | any | yes | `Bypassed` | the record's `bypassReason` |
| `PASSED` | no | any | no | `Passed` | none |
| `SKIPPED` | yes | any | any | `Bypassed` | the attachment's body |
| `SKIPPED` | no | yes or no | yes | `Bypassed` | the record's `bypassReason`, else the message |
| `SKIPPED` | no | yes | no | `Bypassed` | the message |
| `SKIPPED` | no | no | no | `Skipped` | none; the message stays a comment, as in run E |
| `PENDING` | yes | any | any | `Bypassed` | the attachment's body |
| `PENDING` | no | any | yes | `Bypassed` | the record's `bypassReason`, else the message |
| `PENDING` | no | any | no | `Skipped`, as today | none; the message stays a comment |
| `FAILED`, `UNDEFINED`, `AMBIGUOUS` | any | any | any | `Failed` | none; an attachment's body joins the comments (Q10) |

A step that is not bypassed is built exactly as today.

### 4.2 The tests file across the merge

- Before `IngestPipeline.cs:390` removes them, the `step` records with `status: "bypassed"` of every scenario the
  messages own are set aside, in file order, by test id.
- Each is matched to a Gherkin step of its scenario by text: the record's `text`, trimmed, against the pickle step's
  text, ordinal. The k-th record with a given text takes the k-th Gherkin step with that text, background steps first,
  in the order they ran (Q4).
  - A match by text survives a reporter that writes hook steps, nested steps or steps of its own that the messages do
    not have, where a match by position would bypass the wrong step.
  - A match by time would lean on the stamps playwright-bdd gets least right: it gives a step it never reached the test
    case's start time (`CucumberFeatureSynthesizer.cs:341-344`).
- A record that matches no Gherkin step (other wording, a `level` above 0, the keyword inside the text) changes nothing.
  All such records are counted in one diagnostic: "N bypassed step record(s) of scenarios the Cucumber messages own
  matched no Gherkin step by text; their bypass was not applied." Nothing else in the run changes.
- A matched step takes `Bypassed` under §4.1's rules, so a failed Gherkin step stays failed (T15).
- The bypass is applied in the synthesizer, before it rolls the scenario up and writes its markers, so the markers, the
  scenario and history agree. `CucumberFeatureSynthesizer.BuildFromFiles` gains an internal overload that takes the
  set-aside records; the pipeline already holds the tests file when it builds the synthesis (`:384-391`). No public
  member changes.

### 4.3 A scenario's verdict

- **The Cucumber lane.** `Worse` ranks `Failed`, then `Skipped`, then `Bypassed`, then `Passed`, the order LightBDD's own
  status takes. Run A becomes `Bypassed`. A scenario with a bypassed step and a later step that skipped the rest stays
  `Skipped`, and a failure anywhere wins. With hooks dropped (the default), only a failed hook counts, as today
  (`:403-408`); with `--include-hooks`, hooks rank as steps do, as today (`:436`).
- **The tests-file lane.** An `end` record that maps to `Passed` becomes `Bypassed` when any step of the scenario, at
  any depth, is `Bypassed` (Q3).
  - Every other `end` stands. The contract makes `end` "the verdict" (`TestRunRecord.cs:25`), and a reporter that writes
    `failed` or `skipped` there knows more than its steps do.
  - A scenario with no `end` keeps its defaulted verdict (`ResultWhenUnknown`), which is already a guess and is
    reported as one. Turning it into `Bypassed` would make the guess look like a reading.
  - Any depth, because LightBDD's status rolls up from sub-steps, and a reporter puts a bypassed sub-step at `level` 1.
- Both lanes put the result on the model, so the report, `Run.json`, the CI summary, CTRF and history all read it from
  one place.

### 4.4 The reason and the markers

- A bypassed step's reason goes to `ScenarioStep.BypassReason`, not to `Comments`, and its `FailureMessage` stays null.
  That is what LightBDD and the tests-file lane already write, so the report, `query` (a `~` mark and a `bypassed:`
  line, `QueryCommand.Narrative.cs:302-319`) and the mergeable-report reader (`MergeableReportReader.cs:274`) show it
  with no change of their own.
- The synthesizer's step marker writes `status: "bypassed"` and the reason in `bypassReason`, with no `error`. Today
  `StepMarkerRecord` writes the message as `error` for any status (`:590-606`). The end marker writes `bypassed` (`:582`).

### 4.5 The two summaries (R1)

- **`kronikol query summary` (F3).** Per feature: `{passed} passed`, then `, {skipped} skipped` and
  `, {bypassed} bypassed` when not zero, then `, {failed} FAILED` as today. Skipped counts `Skipped` and
  `SkippedAfterFailure`. The JSON item's `passed` becomes the count of `Passed` scenarios. New `skipped` and `bypassed`
  members in the JSON item are new surface, a minor, so they wait for R2 (Q7).
- **The CI summary (F4).** `Skipped` counts `SkippedAfterFailure` too, and a `Bypassed` row is written below it only
  when the run has a bypassed scenario, so the summary of every run without one stays byte for byte as it is (Q8).

### 4.6 What each output shows after the fix

For run C, the issue's own consumer, after R1:

| Output | 4.6.0 | After R1 |
|---|---|---|
| `TestRunReport.html`, the scenario | `Skipped` | `Bypassed`: its own status, tooltip and pie-chart segment. The feature's skip count includes it, as it includes every bypassed scenario today (`ReportGenerator.cs:1587`) |
| the step list | the middle step skipped, its reason a comment | the middle step with the bypassed icon (`ReportGenerator.cs:3641`) and its reason |
| `TestRunReport.json` | step `Skipped`, reason in `comments`; scenario `Skipped` | step `Bypassed` with `bypassReason`; scenario `Bypassed` |
| `History.run.json` | `S` | `B`; neither is a verdict (F6) |
| `Failures.md` | the "Nothing failed" line | unchanged; its "(skipped, bypassed, or skipped after an earlier failure)" covers it (`FailuresDigestGenerator.cs:540`) |
| CI summary | Skipped 1 | Bypassed 1 (F4) |
| `kronikol query summary` | `1 passed` | `0 passed, 1 bypassed` (F3) |
| CTRF | `skipped` | `other`, with `rawStatus` `Bypassed` (`CtrfReportGenerator.cs:253-261`) |
| Pie chart pass rate | 0% | 0%; run B's 100% becomes 0% (F12) |
| Diagram | | unchanged (F7) |

### 4.7 What stays as it is

- LightBDD and the in-process adapters (Q2).
- Both public `MapStatus` methods keep their signatures and mappings. A `SKIPPED` status alone still maps to `Skipped`:
  the bypass is decided from the step's context, not from the status word, and
  `Cucumber_statuses_map_to_kronikol_verdicts` stays as it is.
- A run with no bypass anywhere writes the same report, byte for byte (T8).

## 5. Tests, red first

Every fact that §5 marks red is run against 4.6.0 first, in a worktree at `v4.6.0`, and fails there for its own reason.
The fixtures are the harness's files, checked in under `tests/Kronikol.Tests/TestData/Cucumber/`: the playwright-bdd and
cucumber-js probes' messages (with their feature files and step definitions, so they can be regenerated as the golden
fixture can, `CucumberFixtures.cs:6-13`), and the issue's messages and tests file.

| # | Fact | File | Red on 4.6.0 because | Slice |
|---|---|---|---|---|
| T1 | `PASSED`, `SKIPPED` with a message, `PASSED`: the middle step is `Bypassed` with the message as `BypassReason` and no comment; the scenario is `Bypassed`; its end marker says `bypassed`; its step marker carries the reason and no `error`. And with a fourth step `SKIPPED` that nothing ran after: the scenario is `Skipped` | `Ingestion/Cucumber/CucumberFeatureSynthesizerTests.cs` | `Skipped`, a comment (run A) | S1 |
| T2 | The same through `kronikol ingest` writes history `B` | `Ingestion/Cucumber/CucumberIngestPipelineTests.cs` | `S` | S1 |
| T3 | `FAILED`, `SKIPPED`, `SKIPPED`, and `PASSED`, `FAILED`, `SKIPPED`: every skipped step stays `Skipped`, the scenario `Failed` | synthesizer facts | green today (run D); a guard | S1 |
| T4 | The cucumber-js probe's skipped scenario (`PASSED`, `SKIPPED`, `SKIPPED`, `UNDEFINED`, then an after-hook `PASSED`), with hooks dropped and with them kept: no step is `Bypassed` | synthesizer facts | green today; the guard against counting a hook or `UNDEFINED` as a step that ran | S1 |
| T5 | `PASSED`, `PASSED`, `SKIPPED` with a message: `Skipped`, the message a comment | synthesizer facts | green today (run E); pins the documented limit | S1 |
| T6 | Two attempts, the first `PASSED`, `SKIPPED`, `PASSED` and the second all `PASSED`: no step is `Bypassed`, and the `retry 1` label is kept | synthesizer facts | green today; the guard that the inference reads one attempt | S1 |
| T7 | The real playwright-bdd probe as a second golden fixture: scenario 2 (`$test.skip`) `Skipped` with both later steps `Skipped`; scenario 3 `Failed`, its last step `Skipped`; scenario 1 (the attachment) `Passed` in R1 and `Bypassed` in R2 | synthesizer facts | scenario 1 in R2 | S1, S5 |
| T8 | The existing golden fixture (`playwright-bdd-9.2-messages.ndjson`) ingests to the same `TestRunReport.json` as 4.6.0, byte for byte, apart from its version fields | `CucumberIngestPipelineTests.cs` | green today; the "nothing else changes" guard | S1 |
| T9 | Tests-file lane: a `bypassed` step and an `end` of `passed` make the scenario `Bypassed`; so does a `bypassed` step at `level` 1 | `Ingestion/FeatureSynthesizerTests.cs` | `Passed` (run B) | S2 |
| T10 | Tests-file lane: a `bypassed` step with an `end` of `failed`, of `skipped`, and with no `end`: `Failed`, `Skipped`, and the defaulted verdict | `FeatureSynthesizerTests.cs` | green today; guards Q3's scope | S2 |
| T11 | Both inputs, the messages saying `PASSED` for the middle step (what an unpatched producer writes) and the tests file `bypassed`: the step is `Bypassed` with the tests file's reason, the scenario `Bypassed` | `CucumberIngestPipelineTests.cs` | `Passed` | S3 |
| T12 | Run C as the issue wrote it, and the same with the bypassed step last: both `Bypassed` | same | `Skipped` | S3 |
| T13 | A `bypassed` record whose text matches no Gherkin step changes nothing, and one diagnostic counts it | same | no diagnostic | S3 |
| T14 | Two Gherkin steps with the same text, the second bypassed in the tests file, which also carries a step of its own (a reporter's setup step) before them: the second Gherkin step is `Bypassed`, the first is not | same | neither is `Bypassed` | S3 |
| T15 | A `bypassed` record for a step the messages report `FAILED`: `Failed` | same | green today; a guard | S3 |
| T16 | `query summary` over a report with one scenario of each result prints `1 passed, 2 skipped, 1 bypassed, 1 FAILED`, and its JSON item says `passed` 1 | `Tool/QueryCommandTests.cs`, `Tool/QueryJsonTests.cs` | `4 passed, 1 FAILED` | S4 |
| T17 | The CI summary's rows add up: a `Bypassed` row for a bypassed scenario, `SkippedAfterFailure` counted as skipped, and a run with neither byte for byte as 4.6.0 writes it | `Reports/CiSummaryGeneratorTests.cs` | no row | S4 |
| T18 | `kronikol-bypass` on a `PASSED` step: `Bypassed` with the body. On a `SKIPPED` last step: `Bypassed`. With `WriteTextAttachments` on: no attachment file | synthesizer facts | `Passed`, `Skipped`, a file (run F) | S5 |
| T19 | `kronikol-bypass` on a `FAILED` step: `Failed`, the body in its comments. On a hook step: nothing bypassed, one warning. An empty body: `Bypassed`, no reason. A `BASE64` body: decoded | synthesizer facts | | S5 |
| T20 | Paint: a report ingested from T7's fixture shows scenario 1 (R2) and the issue's scenario (R1) with the bypassed status and the step's reason, read from the page as drawn, not from its markup | `tests/Kronikol.Tests.EndToEnd/IngestedBypassTests.cs` (new), beside `IngestedSetupPartitionTests.cs` | drawn as skipped | S1, S5 |

Mutations to run once each, every one caught by the fact named:

| Mutation | Caught by |
|---|---|
| Count a hook as a step that ran | T4 |
| Count `UNDEFINED` as a step that ran | T4 |
| Read every attempt, not the last | T6 |
| Drop "later": a `SKIPPED` step with a `PASSED` step anywhere in the attempt | T3's second case |
| Rank `Bypassed` above `Skipped` | T1's fourth step |
| Let a failed tests-file step overturn a `passed` end | T10 |
| Join by position instead of text | T14 |
| Keep the message as a comment as well as the reason | T1 |
| Write the reason as the marker's `error` | T1 |
| Count `SkippedAfterFailure` as passed in `query summary` | T16 |
| Let the attachment override `FAILED` | T19 |
| Write the attachment out as a file | T18 |

## 6. Slices, releases and records

### 6.1 Slices

| Slice | What | Bump |
|---|---|---|
| **S0** | The harness's fixtures checked in under `TestData/Cucumber/`; the facts of §5, red where marked | none alone |
| **S1** | The Cucumber lane: the inference, the roll-up, the reason and the markers (§4.1 rule 3, §4.3, §4.4); T1 to T8, T20's R1 case | patch |
| **S2** | The tests-file lane's roll-up (§4.3); T9, T10 | patch |
| **S3** | The tests file across the merge (§4.2) and its diagnostic; T11 to T15 | patch |
| **S4** | The two summaries (§4.5); T16, T17 | patch |
| **S5** | The `kronikol-bypass` attachment (§4.1 rule 1), and `skipped` and `bypassed` in `query summary --json` if Q7 says so; T18, T19, T7's scenario 1, T20's R2 case | minor |
| **S6** | Docs: §6.4, §6.5, the changelog | with each release |

### 6.2 Releases

| Release | Contents | Bump | Why this part moved |
|---|---|---|---|
| **R1** | S0 to S4, and S6's share | patch | Bug fixes: a bypass the input already states is read as one, and two summaries count what they show. Nothing new to call. The changelog calls out the behaviour that changes (§6.3) |
| **R2** | S5, and S6's share | minor | A new input convention a producer can write is something new for a consumer to use (`CLAUDE.md`) |

R1 ships first and waits only for Q1, Q3 and Q4. It closes the issue's three runs for the issue's own consumer, whose
tests file also covers a bypassed last step. R2 waits for Q6's name.

### 6.3 Changelog drafts

> **Patch - a bypassed step reads as bypassed through `kronikol ingest` (#105).** A Cucumber Messages step reported
> `SKIPPED` while a later step of the same attempt ran is now `Bypassed`, with its message as the step's bypass reason
> instead of a comment. No Cucumber runner measured runs a step after skipping one (playwright-bdd 9.2, cucumber-js
> 12.9), so only a bypass produces that shape. A bypassed step now makes its scenario `Bypassed` in both ingest lanes,
> below a failed or a skipped step, as the LightBDD adapter does; before, the messages made the scenario `Skipped` and
> the tests file `Passed`. A tests file's `bypassed` step, with its `bypassReason`, now survives an ingest that also
> reads Cucumber Messages: it was dropped with the reporter's other step records, and is now matched to its Gherkin step
> by text, with a diagnostic counting any that match none. `kronikol query summary` counted every scenario that did not
> fail as passed, and now counts skipped and bypassed scenarios apart. The CI summary's rows left bypassed scenarios
> out, and now add up. **What changes in the output:** an ingested scenario with a bypassed step is `Bypassed` in the
> report, its JSON, CTRF (`other`) and history (`B`), and the report's pass rate no longer counts it as passed. A
> bypassed last step that only the messages report, as `SKIPPED`, still reads `Skipped`: nothing in the messages tells
> it from a step that skipped the rest of the scenario. The patch part moved because nothing is new for a consumer to
> call.

> **Minor - a `kronikol-bypass` attachment bypasses a Cucumber step (#105).** A step that attaches `text/plain` named
> `kronikol-bypass` is `Bypassed`, with the attachment's body as the reason, whatever status its runner gave it, unless
> it failed. It works with any Cucumber Messages producer that can attach, including playwright-bdd as published, which
> reports a step that returns early as `PASSED`, and it covers a bypassed last step, which nothing else in the messages
> can. The attachment is never shown as a file. The minor part moved because the attachment is a new convention a
> producer can use.

### 6.4 Wiki

| Page | Change | Release |
|---|---|---|
| `Integration-Cucumber-Messages.md`, lines 54 to 65 | The status table and the two sentences under it: a `SKIPPED` step that a later step ran after is `Bypassed`, the roll-up order, where the reason goes | R1 |
| same, a new section "Bypassing a step" after "Joining the captured traffic" (line 84) | The attachment, with the playwright-bdd line `await $testInfo.attach('kronikol-bypass', { body: reason, contentType: 'text/plain' })` before returning from the step; why a last step needs it; that `$test.skip` skips the rest of the test and writes no reason (F9) | R2 |
| `Ingesting-External-Captures.md`, lines 114 and 148 | A `bypassed` step makes a `passed` scenario `Bypassed`; with `--cucumber-messages`, the record is matched to its Gherkin step by text | R1 |
| `Generated-Reports.md`, line 511 | "LightBDD only" is false (F11) | R1 |
| `Step-Tracking.md`, line 452 | A `SkipIf` step leaves its scenario's verdict to the framework (F2), until Q2 is answered | R1 |
| `Querying-Reports.md` | `summary`'s per-feature counts | R1 |

### 6.5 Doc comments

They ship in the package, so they are part of each release. Grep the XML docs of every type below for sentences about
the old behaviour before tagging.

- `ExecutionResult.Bypassed` (F5): a step was skipped over at runtime while the steps after it ran.
- `CucumberFeatureMerger`'s remarks (`:12-17`) and `IsReplacedStep`'s summary (`:90-93`): a `bypassed` step record
  carries its status and reason onto its Gherkin step.
- `CucumberFeatureSynthesizer`'s remarks (`:100-111`): the bypass rule; `CucumberSynthesisOptions.TestIdAttachmentName`
  (`:27-31`) gains a sentence on the bypass attachment beside it (R2; no new property, Q6).
- `TestRunRecord`'s `step` and `end` sentences (`:13-16`, `:25`) and `BypassReason` (`:94`): what a bypassed step does to
  the verdict.
- `IngestRequest.CucumberMessagesFiles` (`IngestPipeline.cs:20-25`): the merge's exception.

### 6.6 Kronikol4J

Kronikol4J has no Cucumber Messages ingest (F13), so there is nothing to mirror. R1 changes report output for ingested
runs only, and no diagram (F7). A ledger line in `../Kronikol4J/docs/REMAINING_PARITY.md` says so. If Kronikol4J ports
`CiSummaryGenerator` or `query summary` by then, the same entry says whether it shares F3 or F4.

### 6.7 Before declaring done

- The harness's `probe.sh` on the release build: runs A, B and C each print the issue's expected row; D and E as §4
  says. The playwright-bdd and cucumber-js probes re-ingested.
- Every fact of §5 found in the diff, red on 4.6.0 where §5 says red, and each mutation caught.
- The wiki pages of §6.4 and the doc comments of §6.5, grepped for "LightBDD only", "(e.g. inconclusive)" and
  `SKIPPED`/`PENDING → Skipped`.
- `release.slnf` built in Release for every target framework.
- **The consumer.** The owner's playwright-bdd suite, run against the local stack and against a deployed environment,
  each ingested with `--tests` and `--cucumber-messages`: `P` for every scenario locally, `B` for the bypassed ones live.
- Close #105 with `gh issue close` after the last release that serves it; a commit message that says "(#105)" closes
  nothing, and one that says "fixes #105" in passing closes it early.

## 7. Where it sits in the roadmap

Row **1.18** in stage 1, written into `ROADMAP.md` on 2026-10-06 (row 1.17 went to #115 the same day), placed by
**rule 1**: a shipped ingest reads a bypass its input states as a skip or a pass, and history records the wrong letter.
Marked **patch** for R1 and **minor** for R2. It touches `Ingestion/` (both synthesizers, the pipeline, the merger),
`Query/QueryCommand.Overview.cs` and `Reports/CiSummaryGenerator.cs`. Other plans written the same week may also touch
`IngestPipeline.cs`; whichever lands second rebases. Its decision, the green light and the answers to Q1 to Q10, is
**D34**, taken 2026-10-07 when the owner asked for the plan to be completed in full (the sessions placing #105 and #130
that day gave the lower issue the lower number). `ROADMAP.md` §7 lists #105 and the plan under 1.18.

## 8. Found on the way, not in this plan

| What | Level | Where it goes |
|---|---|---|
| F9: playwright-bdd's `$test.skip(reason)` writes no `message` for the step it skips | RUN | An upstream report to playwright-bdd, the owner's call. The owner's `$step.skip` patch is not proposed upstream yet either (ISSUE) |
| F10: steps after a failure are `SkippedAfterFailure` from LightBDD and `Skipped` from the Cucumber lane | READ, RUN | Q9 |
| `UNDEFINED` is failed in the Cucumber lane (`CucumberFeatureSynthesizer.cs:727`), skipped from Reqnroll (`ExecutionStatusExtensions.cs:18`) and from Kronikol4J's Cucumber-JVM plugin (`KronikolCucumberPlugin.java:73`) | READ | Whoever next touches the status vocabularies. Roadmap row 1.15 already lists four step-to-phase vocabularies that disagree |
| `kronikol ctrf` reads a report's `result` with `Enum.TryParse` and takes an unreadable one as `Bypassed` (`CtrfCommand.cs:133`). `Enum.TryParse` accepts undefined numbers and comma lists (the INGEST_FEED audit's trap), so a `result` of `"7"` would pass | READ, not run | Check against a real file first, then a patch if it misreads one |
| The pie chart counts `Bypassed` against the pass rate while the feature counts put it with skipped (`ReportGenerator.cs:1587`, `:2656`) | READ | What every LightBDD report already does. Noted, not changed |

## 9. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Ship the inference (the issue's 1b), or only the explicit signals | **Ship it, with §4.1's rule.** It is Kronikol's own definition of a bypass, it reads the owner's patched producer with no extra code in the steps, and neither producer measured writes its shape for anything else. Its gap, the last step, is documented, and both the attachment and the tests file cover it. Without it every bypass needs an attachment, and run A stays wrong |
| Q2 | Roll a `SkipIf` bypass up to the scenario in the in-process adapters too (F2) | **Yes, but not in this plan.** One word should mean one thing in every lane, and after R1 the ingest lanes and LightBDD agree while six adapters do not. It changes the report of every in-process `SkipIf` user (scenarios from `Passed` to `Bypassed`, a lower pass rate), which #105 did not ask for. A patch of its own, on the owner's word |
| Q3 | The tests-file roll-up: only a `passed` end to `Bypassed`, or the full ranking over the steps | **Only `passed` to `Bypassed`.** The `end` record is the verdict by contract. Letting a failed step overturn a `passed` end is a different change, for a reporter nobody has shown |
| Q4 | Join a tests-file step to its Gherkin step by text, by position, or by time | **By text, in order**, with a diagnostic for no match. Position breaks on a reporter that writes a step the messages lack; time is what playwright-bdd stamps least honestly |
| Q5 | Two releases or one | **Two, R1 first.** The fixes need no naming decision, and R1 alone closes the issue's runs for its own consumer |
| Q6 | The attachment's name: a fixed `kronikol-bypass`, or an option beside `TestIdAttachmentName` | **Fixed.** It matches the convention it extends, and the CLI cannot set the test id's name either (`IngestPipeline.cs:385-386`). An option can follow a request for one |
| Q7 | `query summary --json`: add `skipped` and `bypassed` per feature | **Yes, in R2**, which is a minor anyway. R1 corrects `passed` and the text line |
| Q8 | The CI summary: a `Bypassed` row only when there is one, and `SkippedAfterFailure` under `Skipped` | **Yes.** The rows add up, and every run without either keeps its summary byte for byte. A row on every run would change every summary |
| Q9 | Steps after a failure: `SkippedAfterFailure` in the Cucumber lane too (F10) | **Not in this plan.** Nothing in #105 depends on it, and it changes the step list of every failed scenario |
| Q10 | A failed step that carries the attachment: drop the reason, or keep it as a comment | **A comment.** The failure wins, and the reason is evidence of what the step meant to skip |

## 10. Assumption ledger

| # | Assumption | Level | What would change |
|---|---|---|---|
| A1 | The owner's patched playwright-bdd reports `$step.skip` as `SKIPPED` with the reason as `message`, and runs the later steps | ISSUE (the patch is not available here) | If it reported `PASSED`, the inference reads nothing there, and the attachment or the tests file is needed |
| A2 | playwright-bdd maps an attachment made inside a nested `test.step` of a step to that step, as it maps one made in the step's body | INFERRED; the probe attached in the body | If not, the wiki's recipe says to attach in the step's body |
| A3 | Cucumber-JVM reports a step whose assumption failed `SKIPPED` with a message, and skips the rest | INFERRED | Only the argument against reading a message as the signal (C10); nothing is built on it |
| A4 | A reporter that writes the tests file for playwright-bdd writes the step text without its keyword, as the issue's file does | ISSUE | If it includes the keyword, §4.2's match also tries the text with a leading keyword removed, and T14 gains that case |
| A5 | No Cucumber producer runs a step after a skipped one for any reason but a bypass | RUN for playwright-bdd and cucumber-js | A third producer that does would be misread; it would need the attachment, and the wiki states the rule so its users can tell |

## 11. Log

- **2026-10-06.** Written at 4.6.0 (`37e93813`). Probes (harness):
  - The issue's runs A, B and C reproduced verbatim on a 4.6.0 build. D (`FAILED`, `SKIPPED`, `SKIPPED`) gave `Failed`
    with both steps `Skipped`; E (a skipped last step with a message) `Skipped`, the message a comment; F (a
    `kronikol-bypass` attachment on a `PASSED` step) `Passed`, the attachment not shown.
  - playwright-bdd 9.2.0 on @playwright/test 1.62.1, as published: an attachment made in a step's body carried that
    step's id, and the step `PASSED`; `$test.skip` gave that step and the rest `SKIPPED` with no message; a failure gave
    the rest `SKIPPED`. Ingested on 4.6.0: `Passed`, `Skipped`, `Failed`, and `query summary` printed
    `2 passed, 1 FAILED`.
  - cucumber-js 12.9.0: `return 'skipped'` gave the rest `SKIPPED`, a later undefined step `UNDEFINED` and the after-hook
    `PASSED`; a failure gave the rest `SKIPPED`. Ingested on 4.6.0: both scenarios `Failed` (the undefined step fails
    the first), every skipped step `Skipped`.
  - The probe was built in a detached worktree at `37e93813`, since another session was working in the main checkout.
- **2026-10-07. Green-lit and executed** at the owner's word ("complete the plan in full, in a separate worktree"), in
  the worktree `C:/Code/Kronikol-bypass` (branch `fix/cucumber-bypass`), while three other sessions executed #113,
  #115 and #130 in worktrees of their own. Decision **D34** (§7), every question as §9 recommends. The fixtures went in
  under `tests/Kronikol.Tests/TestData/Cucumber/` (`issue-105-*`, `playwright-bdd-9.2-bypass-*`,
  `cucumber-js-12.9-skip-*`), and a pin of the golden fixture's verdicts, steps, comments and markers as 4.6.0 synthesised
  them (`playwright-bdd-9.2-verdicts.json`, T8's guard).
  - **Where the facts went.** Not into the files §5 names, which peers were editing the same day: the Cucumber lane's
    in `Ingestion/Cucumber/CucumberBypassTests.cs` (a stream builder, one test step per line of the fact) and
    `CucumberBypassIngestTests.cs` (through `IngestPipeline`, history letters read from `History.run.json`), the
    tests-file lane's in `Ingestion/FeatureSynthesizerBypassTests.cs`, the summaries' in `Tool/QuerySummaryCountsTests.cs`
    and `Reports/CiSummaryCountsTests.cs`, and T20 in `tests/Kronikol.Tests.EndToEnd/IngestedBypassTests.cs`.
  - **Red first.** R1's facts against the 4.6.0 source (the worktree's `src` equals `v4.6.0`'s): 21 of the first 43 facts failed, each for its own reason, and the 22 guards passed (`r1/red-4.6.0.txt`); run again with the facts added during the work, 25 failed and 26 passed (`r1/red-4.6.0-all.txt`), the three paint facts among the red (the step drawn as skipped, or its reason not drawn). Output in
    `CUCUMBER_BYPASS_PLAN.harness/r1/`; R2's are in its own entry.
  - **Found while executing, and fixed in R1:**
    - **F14. The report never drew a bypassed step's reason.** The step list draws a step's comments and not
      `ScenarioStep.BypassReason`, so a `SkipIf` reason and a tests file's `bypassReason` reached the JSON and the query
      verbs and nowhere on the page, though `Step-Tracking.md` said the reason "is displayed alongside the step text".
      §4.4 assumed the report showed it; T20 showed it did not (its reason line timed out on R1's first build). R1 draws
      `Bypassed: <reason>` as the step's first comment line, the line Kronikol4J already wrote, without which moving the Cucumber message out of the comments would
      have hidden it from the page.
    - **F15. `kronikol query failures` called a bypassed scenario one that "did not run"**: with nothing failed, its
      footer counted every scenario that did not pass as not run. A sibling of F3, fixed with it.
    - **F16. `CucumberSynthesisResult.Warnings` was read by nothing** (`PHASE_FROM_STEPS_PLAN.md`'s F9, taken over from
      that plan's session by agreement): R2's warning for a misplaced attachment would have reached no one. The pipeline
      now records each warning as a diagnostic. Measuring that on the issue's own file found the next one.
    - **F17. The "envelope(s) of unknown type ignored" warning fires on every real producer's file**: the reader counts
      `source`, `stepDefinition` and `suggestion`, which every producer writes and no report shows, as unknown. As a
      diagnostic it would have stood at the top of `query summary` for every Cucumber ingest. The reader now names only
      the types a report could have needed (`parseError`, `externalAttachment`, a newer type), and the warning lists
      them; `CucumberMessagesReaderTests.Counts_source_envelopes_as_unknown_rather_than_failing`, which asserted the old
      warning, asserts its absence. The scenarios without a test id are named in one warning, not one each.
    - **F18. Kronikol4J's CI summary has F4's defect** (it counts `PASSED`, `FAILED` and `SKIPPED` by exact status, and
      writes `INCONCLUSIVE` as `Bypassed`): its ledger line says so.
  - **Mutations** (`CUCUMBER_BYPASS_PLAN.harness/mutations/mutate.py`, run in a detached worktree of the commit):
    R1, 26 of 26 caught by the fact each names, 22 on the first commit and 4 more for the reason line and the envelope
    filter (`mutations/r1-results.txt`).
  - **Probes** on the R1 build, from a copy of the harness outside any repository: runs A, B and C each give the issue's
    expected row (the step `Bypassed` with its reason in `bypassReason`, the scenario `Bypassed`, history `B`); D, E, G
    and H as §4 says; F (the attachment) `Passed`, since R1 does not read the attachment (`r1/probe-results.txt`).
  - **Numbering.** #115's session was ready first and released 4.6.1, so R1 is 4.6.2 and R2 4.7.0, each pushed only once the release before it is listed on NuGet (the templates pin it). **Suites.** On the work's last commit, before the numbers were taken: the core suite 6,556 passed and 2 skipped, its one failure a fact that read a process-wide queue another fact had left an entry in (fixed in R1: `PendingRequestResponseLogsTests.Count_reflects_pending_entries` starts from an empty queue, and `DeferredLogFlushHandlerTests`' fact that leaves its entry queued on purpose clears it); the full Playwright suite 986 passed and 28 skipped; StepTracking 43, AssertionTracking (three targets) and LightBDD.xUnit3 26 passed; `release.slnf` built in Release for every target, with no warning in a file this plan touched. On the release commits: 4.6.2 (R1, on 4.6.1): the core suite 6,595 passed and 2 skipped, the full Playwright suite 985 passed and 28 skipped, StepTracking, LightBDD.xUnit3 and AssertionTracking (three targets) passed, `release.slnf` built in Release for every target, and the probe read runs A to H as above.
- **2026-10-07, R2 as 4.7.0.** The `kronikol-bypass` attachment (§4.1 rule 1, with Q6's fixed name and Q10's comment on
  a failed step) and `skipped` and `bypassed` in `summary --json` (Q7). Red first: R2's facts against R1's source, 11
  failed, the paint fact among them, and R1's 24 passed (`r2/red-on-r1.txt`). Mutations: 9 of 9 caught by the fact each
  names (`mutations/r2-results.txt`). The probe on the R2 build reads run F and playwright-bdd's attaching scenario as
  `Bypassed` (`r2/probe-results.txt`). Suites on 4.7.0: the core suite 6,604 passed and 2 skipped, the full Playwright suite 986 passed and 28 skipped,
  and `release.slnf` built in Release for every target, with no warning in a file this plan touched. **F19**, found
  while assembling it: the `kronikol-test-debugging` skill's command reference (`references/commands.md`, both
  copies) still called `kronikol query summary`'s feature lines pass/fail after R1 made them count skipped and
  bypassed scenarios; R2 corrects it.
