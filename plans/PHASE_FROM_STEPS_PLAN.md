# Phases from steps: one phase per call, and no diagnostic on a healthy run (#130)

**Date:** 2026-10-06 · **Repo version:** 4.6.0 (`main` at 37e93813; line numbers are for that commit) · **Status:**
**green-lit 2026-10-07** by the owner ("complete the plan in full"), every open question taken as recommended
(`ROADMAP.md` row 1.20, D35). Executed in a worktree of its own beside three other plans' sessions; see the Log.

Evidence labels: **RUN** (measured on 2026-10-06 on a Debug build of 37e93813; inputs, scripts and saved answers in
[`PHASE_FROM_STEPS_PLAN.harness/`](PHASE_FROM_STEPS_PLAN.harness/README.md)), **READ** (in the source, tests, wiki or
Kronikol4J at that commit), **INFERRED** (reasoned from two facts, stated by neither), **ESTIMATED** (not measured; the
step that would measure it is named).

The owner's issue, [#130](https://github.com/lemonlion/Kronikol/issues/130) (2026-10-05, filed against 4.5.0 at
`425d9ba`; every line it cites is unchanged in 4.6.0):

> 1. Don't record the count as a diagnostic. If it's worth keeping, print it as an ingest progress line beside
>    `Replayed N interaction record(s) into M scenario(s).` The wiki's `Other` row then needs another example.
> 2. Phase a pair by its request, as the run window, the attempt filter and both attributions do, so a call's two
>    halves always share a phase.
> 3. Optional: if a diagnostic is wanted, count the calls the pass could not phase. After 2, that is zero on a
>    healthy run.

The issue names two siblings, #128 (a step label over 110 characters, or one holding a backslash, loses its calls'
`stepPath`) and #129 (a `StepAttributionMismatch` never reaches the report). Neither touches the phase pass and
neither is in this plan; §7 says why the order does not matter.

## TL;DR

- **The count is a diagnostic, so a healthy run is never clean (RUN, §2.1).** Whenever `--phase-from-steps` phases
  anything it records `Other: N interaction record(s) took their phase from the step they happened during.`
  `kronikol query` prints it as a `!` line above every answer, `failures` included when nothing failed;
  `--diagnostics-section` lists it; and with history off it is the only reason the run writes
  `TestRunReport.labs.html`, which 4.6.0 writes whenever a run records a diagnostic.
- **A call's two halves can take different phases (RUN, §2.3).** The pass looks each record up on its own timestamp.
  The repro's database read, answered 2 s after its step ended, has an `Action` request and an `Unknown` response.
  The run window, the attempt filter, both attribution passes, background attribution and the in-process handler
  all give a call's two halves one answer.
- **R1, a patch (§5):** the count leaves the diagnostics and `kronikol ingest` prints it as a line under
  `Replayed …` (ask 1). A request and its response take one phase, judged on the pair's earliest record (ask 2).
  A phase string that names no `TestPhase` member is phased from the steps instead of being kept and reported as
  `Unknown` (F3, in the same twenty lines). No public member is added or removed: `ApplyPhaseFromSteps` keeps its
  signature, and the count reaches the CLI through an internal member.
- **R2, a patch, recommended (Q4):** the same defect sits in window and claims attribution. They record their
  successes ("N interaction record(s) attributed to a test by time window.") as `UnattributedInteractions`, a kind
  documented as "could not be attributed", so those lines head every query answer too. And because the header
  prints one message per kind, a run where some records really were left unattributed shows the success line
  with "(×2)" instead of the failure.
- **Ask 3 is Q2, not in R1.** "Zero on a healthy run" holds for the issue's Jest suite. It does not hold in general:
  calls in hooks, between steps, in Background steps (which open no phase window) and in tests without steps all
  fall outside every step on a healthy run. Whether to add a narrower diagnostic, or put the number on R1's line,
  is the owner's call.
- **Found on the way (§8):**
  - `kronikol ingest`, run from a build inside a git checkout, appends to that checkout's history ledger (RUN; it
    happened while measuring this plan).
  - Cucumber synthesis warnings are never read.
  - The wire/span merger can leave a call unphased.
  - The wiki's ingest diagnostics table lacks five kinds.

## 0. Findings

| # | Finding | Level | Where |
|---|---|---|---|
| F1 | **The phase count is recorded as `DiagnosticKind.Other` on every run that phases anything** (`IngestPipeline.cs:746-747`). `Other` is in the query's answer-affecting set (`QueryCommand.cs:383-396`), so the line heads every answer | RUN, READ | §2.1, §2.2 |
| F2 | **Each record is phased on its own timestamp** (`IngestAttribution.cs:493`), so a response that outlives its step takes no phase, or the next step's | RUN, READ | §2.3 |
| F3 | **A phase string that names no member counts as known** (`IngestAttribution.cs:487` compares to the literal `"Unknown"`). `"7"`, `"Teardown"` or `" unknown"` is never phased from the steps, and the replay then reports it as `Unknown` (`ResolvedPhase`, `InteractionRecord.cs:178-179`) | READ | §3.2 |
| F4 | **Window and claims attribution record their successes as `UnattributedInteractions`** (`IngestPipeline.cs:710`, `729`); the kind's doc says "could not be attributed" (`DiagnosticEntry.cs:23`). `ProvenanceNotes` prints the first message of each kind with a count (`QueryCommand.cs:412-418`), and the success line is recorded first, so it stands in for a real failure | READ | R2 |
| F5 | **`WindowAttribution ExclusiveOnly` records its count even at zero**, by design ("the line is what proves the mode ran", `IngestPipeline.cs:732-735`), as `Other` | READ | Q5 |
| F6 | **`kronikol ingest` resolves the history ledger from the tool's own folder first** (`HistoryPathResolver.cs:69`; ingest passes no base directory, `ReportGenerator.cs:360`). A tool built inside a git checkout appends the ingest to that checkout's `.kronikol/history.jsonl` | RUN | §8 |
| F7 | **The wire/span merger never reconciles phases** (`InteractionMerger.cs:312-320`, `Adopt`). A wire call still unattributed when phases are applied gets none, and keeps none after the merge hands it its span twin's test | READ, INFERRED | Q6 |
| F8 | **Background steps open no phase window** (`IngestAttribution.cs:448`, pinned by `Background_and_nested_steps_open_no_phase_window`), so calls made during a Cucumber Background are never phased | READ | Q7 |
| F9 | **`CucumberSynthesisResult.Warnings` is never read** in `src/` (`CucumberFeatureSynthesizer.cs:61`, written at 127-131, 201, 207, 664-671, 975) | READ | §8 |
| F10 | **The wiki's ingest Diagnostics table** uses F1's line as its `Other` example and has no row for `StepAttributionMismatch`, `ResultDefaulted`, `BackgroundCalls`, `ReportRotationFailed` or `OptionNotApplied` (`Ingesting-External-Captures.md:620-634`) | READ | §6, R1 |

## 1. How far each claim was checked

- The repro was RUN on 4.6.0 in four configurations, plus three controls (§2.1). The tool ran from a copy outside
  every git checkout, for the reason F6 gives.
- The Jest suite's numbers (520 scenarios, 3,910 records, the one late response) are the issue's. The suite is the
  owner's and was not re-measured.
- Every code path is READ at 37e93813, with the line cited.
- What ask 3 would count on a healthy run is ESTIMATED from the code (Q2); S0b measures it if Q2 is taken.
- The fix is not prototyped. It is small, and the facts in §4 are its specification.

## 2. What the code does today

### 2.1 The repro on 4.6.0 (RUN)

The issue's inputs (`harness/s0/`): one test, one `When` step from 10:00:00.000 lasting 10 ms, the test's end at
10:00:00.010. Call `a` (request .001, response .008) is answered inside the step. Call `b`, a database read
(request .003, response 10:00:02.000), is answered after the step and the test have ended.

`kronikol ingest calls/calls.ndjson --tests tests.ndjson --phase-from-steps -o outB`:

```
Replayed 6 interaction record(s) into 1 scenario(s).
  Other: 3 interaction record(s) took their phase from the step they happened during.
  HistoryUnavailable: no history ledger: no .git or .kronikol directory above …
Kronikol: reports written to …\outB  (TestRunReport.html · TestRunReport.json 7.5 KB · Failures.md · TestRunReport.labs.html)
```

`TestRunReport.json`, each record's `phase`:

| Call | Half | Timestamp | `--phase-from-steps` | No flag |
|---|---|---|---|---|
| a | Request | 10:00:00.001 | Action | Unknown |
| a | Response | 10:00:00.008 | Action | Unknown |
| b | Request | 10:00:00.003 | Action | Unknown |
| b | Response | 10:00:02.000 | **Unknown** | Unknown |

`kronikol query failures outB`:

```
! 3 interaction record(s) took their phase from the step they happened during.
nothing failed
1 scenarios, all passed · next: scenarios · services
```

- **Every verb carries the line.** `summary` lists it under Diagnostics, and `interactions --json` carries it in
  `notes`. Without the flag there is no `!` line and no diagnostic.
- **`--diagnostics-section` lists it** in the report: `Report diagnostics (2: Other, HistoryUnavailable)`.
- **`--separate-setup` adds nothing here.** The test has no `Given` step, so no boundary is drawn and the outputs
  equal run B's.
- **The labs page.**
  - With history off (`KRONIKOL_HISTORY=off`, which the test projects set, `HistoryPathResolver.cs:44`), the flagged
    run writes `TestRunReport.labs.html` holding only `Report diagnostics (1: Other)`. The same run without the flag
    writes no page.
  - Inside a git folder, history writes the page anyway. Outside one with history on, `HistoryUnavailable` (not
    answer-affecting) does.
  - So R1 changes the page only where history is off.
- **`query interactions --group-by phase` hides the split.** It reads the request's phase and puts both calls in one
  `Action` bucket. `query interactions --json` carries no phase.
- **"Replayed 6" counts records, not calls:** the four call records, plus the step marker's two halves
  (`AddDiagramMarkers`, `InteractionRecord.ToLogs`).

### 2.2 The count (READ)

```csharp
// IngestPipeline.cs:739-748
if (request.PhaseFromSteps)
{
    var stepWindows = IngestAttribution.BuildStepWindows(currentAttempts);
    var (phasedRecords, tagged) = IngestAttribution.ApplyPhaseFromSteps(records, stepWindows);
    records = phasedRecords;
    if (tagged > 0)
        diagnostics.Add(DiagnosticKind.Other, $"{tagged} interaction record(s) took their phase from the step they happened during.");
}
```

The repo already states the rule this line breaks, in four places:

- `IngestResult.Diagnostics`: "Empty is the happy path" (`IngestPipeline.cs:288-291`; the schema text says the same,
  `ReportGenerator.cs:6192`).
- `WriteProvenance`: the header is written "only when it changes how the answer should be read … Silence means the
  answer came from the full data" (`QueryCommand.cs:342-345`).
- `ExplainBlankSpecifications`, the CLI's other progress line: "One line, not a diagnostic: the diagnostics list means
  'silence is a clean run', and this is the run behaving as designed."
- `DIAGRAM_COLOURS_PLAN.md` F7 recorded that a cosmetic line filed under `Other` heads every query answer, and gave its
  own case a kind of its own (`OptionNotApplied`).

### 2.3 The lookup (READ)

`ApplyPhaseFromSteps` (`IngestAttribution.cs:471-505`) walks the records once. It skips markers and any record whose
`phase` is set and is not the literal `"Unknown"` (L487). Every other record gets the phase of the latest-started step
window of its own test that holds its own timestamp (L493, `FindPhase` L507-522). How every other reader treats a pair:

| Pass or reader | A pair is judged on | Where |
|---|---|---|
| Run window | the earliest timestamp among the records sharing its `requestResponseId` | `IngestPipeline.cs:643-656` |
| Earlier attempts | the same | `IngestPipeline.cs:769-780` |
| Window and claims attribution | the request; the response inherits (in file order, so a response read first is judged alone) | `IngestAttribution.cs:136-139`, `155-156`, `285-286` |
| Background attribution | the earliest stamp, for both halves | `BackgroundAttribution.cs:76-79` |
| The in-process HTTP handler | `TestPhaseContext.Current`, read once and stamped on both halves | `TestTrackingMessageHandler.cs:235`, `256`, `291`, `318` |
| `query interactions --group-by phase` | the request's phase | `QueryCommand.GroupBy.cs:103` |
| The Setup boundary from phases | requests only | `IngestPipeline.cs:996-1009` |

A response's own phase shows in four places:

- `TestRunReport.json` `httpInteractions[].phase`, one entry per half and always written (`ReportGenerator.cs:4904`).
- The XML and YAML files, left out when `Unknown` (`ReportGenerator.cs:5164`, `5547-5548`).
- Mergeable shards, which read it back (`MergeableReportReader.cs:526`).
- `kronikol query http` on the response half's address (`QueryCommand.Payloads.cs:313`; `ReportScanner.cs:736` reads
  `Unknown` as none).

Nothing draws a phase. The diagram partitions at a marker (`PlantUmlCreator.cs:201-226`), and the HTML and history do
not read phases.

### 2.4 The tests today (READ)

No fact has a pair whose halves cross a step boundary:

- `IngestAttributionTests.Anonymous()` (L30-40) builds lone requests with no pair id.
- `IngestSetupBoundaryTests.Call()` (L173-180) puts each response 100 ms after its request, inside the same step.
- The German Cucumber fact (`CucumberIngestPipelineTests.cs:236-271`) is the only one that reads a response's phase,
  and its responses stay in their step.
- No fact asserts the count's diagnostic, or counts diagnostics on a phased run, and "took their phase" appears in
  no test.

So nothing pins the behaviour #130 reports, and no existing fact should change in R1 (§4.2).

## 3. Design

### 3.1 The count leaves the diagnostics (ask 1)

- **Delete `IngestPipeline.cs:746-747`.** `Attribute` hands its caller the count.
- **Carry the count to the CLI internally.** `IngestResult` gets `internal int PhasedRecords { get; init; }`, set on
  both of its returns (L469, L496). `Kronikol.Tool` sees internals (`Kronikol.csproj:18`), so this adds no public
  surface; a public member is Q3.
- **`kronikol ingest` prints one line after `Replayed …` and before the diagnostics**, whenever `--phase-from-steps`
  was given, zero included:

  ```
  Replayed 6 interaction record(s) into 1 scenario(s).
  --phase-from-steps: 4 interaction record(s) took the phase of the step their call started in.
  ```

- **Why records, not calls:** the line sits beside `Replayed …`, which counts records, and `Tagged` already counts
  them.
- **Why zero is printed:** zero is the one value worth a look (for example a tests file whose steps carry no
  `durationMs`, so every window is an instant), and the line costs nothing on a run that asked for the option. Q1
  holds the alternatives.
- **Nothing else changes.** The generator, the labs-page rule (`LabsReportGenerator.cs:29`) and
  `AnswerAffectingDiagnostics` stay as they are. With no entry recorded, a healthy run has no `!` line and an empty
  diagnostics section, and with history off it writes no labs page.

### 3.2 One phase per call (ask 2, and F3)

The rule, as `ApplyPhaseFromSteps`'s doc will state it:

1. **What a pair is.** The records that share a non-empty `requestResponseId`, markers excluded as now. Its start is
   its earliest timestamp, as the run window and the attempt filter judge it. A record with no pair id is judged on
   its own timestamp, as now.
2. **The pair's phase.** If a capturer wrote a phase on either half, that is the pair's phase (the request's, when
   both halves carry one). Otherwise it is the phase of the step window of the record's test that holds the pair's
   start (`FindPhase`, unchanged).
3. **Who takes it.** Every record of the pair whose own phase is unknown takes the pair's phase. A record whose
   capturer wrote a phase keeps it, so a capturer that knows better still wins, record by record.
4. **"Unknown" means `ResolvedPhase == TestPhase.Unknown`** (F3), the parser the replay already uses. A phase of
   `"7"` or `"Teardown"` is then phased from the steps, where today it is kept and reported as `Unknown`.

Further points:

- **Implementation:** two passes over the list, the first building the pair map (start, and any capturer phase), the
  second stamping. A response read before its request is then handled like any other.
- **Halves that name different tests:** each half looks up its own test's windows (`byTest[record.TestId]`), so such
  a pair is phased half by half at the pair's start. This happens only when a response is read before its request and
  window attribution judged it alone.
- **Why the earliest timestamp rather than "the record whose type is Request":**
  - The two agree whenever a call's request is stamped first, which every capturer in the repo does.
  - Where they disagree (a clock that put the response first, or a pair whose request line is missing), the earliest
    is what the run window, the attempt filter and background attribution already use.
  - It needs no type test; a record of any other type is read as a request (`InteractionRecord.cs:36-37`).
  - On every input in the issue and the repo's facts, it gives the issue's "by its request" answer.
- **API:** `ApplyPhaseFromSteps` keeps its signature, `(List<InteractionRecord> Records, int Tagged)`, and `Tagged`
  keeps counting the records it gave a phase. Its only caller in `src/` is `IngestPipeline.cs:744`. Its doc
  (L464-470) and `IngestRequest.PhaseFromSteps`'s (`IngestPipeline.cs:188-201`) change.
- **Pass order:** unchanged (claims, window, phases, merge, drop). The wiki and Kronikol4J's ledger
  (`REMAINING_PARITY.md:1879-1884`) record it.

### 3.3 What a reader sees

| Reader | 4.6.0 (RUN) | After R1 |
|---|---|---|
| `kronikol ingest`'s console | `Other: 3 interaction record(s) took their phase …` | `--phase-from-steps: 4 interaction record(s) took the phase of the step their call started in.` |
| `IngestResult.Diagnostics`, `TestRunReport.json` `diagnostics` | the count, as `Other` | no entry |
| Every `kronikol query` answer | `! 3 interaction record(s) …` | no `!` line |
| `--diagnostics-section`, the labs page | `Report diagnostics (1: Other)`; with history off, a labs page for that line alone | nothing to list; with history off, no labs page |
| `b`'s response in `TestRunReport.json`, XML, YAML | `Unknown` | `Action` |
| `kronikol query http` on `b`'s response | no phase | `phase Action` |
| `--group-by phase`, the diagram, the HTML | `Action` ×2; unchanged | the same |

### 3.4 Kronikol4J

No ledger entry. Kronikol4J has no ingest layer (`docs/OTLP_TAP_PLAN.md:32-35`), and 4.1.0's ingest-only change set
the precedent (`CHANGELOG.md:394-396`: "Kronikol4J does not have [ingest], so there is no ledger entry"). The pass
order its ledger records does not change.

## 4. Tests, in the order they are written

### 4.1 New facts

| # | Fact | File | Red on 4.6.0 |
|---|---|---|---|
| 1 | `A_response_after_its_step_ended_takes_its_requests_phase` (the issue's call `b`) | `IngestAttributionTests.cs` | yes, the response is unphased |
| 2 | `A_response_that_lands_in_a_later_step_keeps_its_requests_phase` (request in a `Given`, response in the `When`) | same | yes, the response is `Action` |
| 3 | `A_response_read_before_its_request_is_judged_on_the_pairs_start` (the response line first, its timestamp outside every step) | same | yes |
| 4 | `A_capturers_phase_on_either_half_is_the_pairs` (`Setup` on the request, an unphased response inside a `When`) | same | yes, `Action` |
| 5 | `Halves_the_capturer_phased_each_keep_their_own` (`Setup` and `Action`, both kept) | same | no, a guard (M4) |
| 6 | `A_phase_that_names_no_member_is_taken_from_the_steps` (theory: `"7"`, `"Teardown"`, `" unknown"`) | same | yes, each is kept |
| 7 | `A_record_without_a_pair_id_is_phased_on_its_own_timestamp` | same | no, a guard (M1) |
| 8 | `Tagged_counts_every_record_given_a_phase` (the repro: 4) | same | yes, 3 |
| 9 | `A_healthy_phase_from_steps_ingest_records_no_diagnostic` (the repro through `IngestPipeline.Run`: no `Other`, `PhasedRecords` is 4) | new `IngestPhaseFromStepsTests.cs`, `[Collection("DiagramsFetcher")]` | yes |
| 10 | `Both_halves_of_a_late_call_carry_one_phase_in_the_data_file` | same | yes |
| 11 | `A_healthy_phase_from_steps_run_writes_no_labs_page_with_history_off` | same | yes (the RUN control) |
| 12 | `A_phase_from_steps_report_heads_no_query_answer` (`query failures` prints `nothing failed` and no `!` line) | same | yes |
| 13 | `The_response_halfs_address_shows_its_calls_phase` (`query http`) | same | yes |
| 14 | `Ingest_prints_the_phase_count_as_a_line_after_replayed` (the order of the lines; no `Other:`) | `IngestCommandTests.cs` | yes |
| 15 | `Ingest_prints_a_phase_count_of_zero` (a step with no `durationMs`, calls after its instant) | same | yes, nothing is printed |
| 16 | `Ingest_without_phase_from_steps_prints_no_phase_line` | same | no, a guard (M8) |
| 17 | `The_usage_says_a_response_takes_its_requests_phase` | same | yes |

Facts 9 to 13 run the pipeline, so they join the `DiagramsFetcher` collection, as `IngestAttributionTests` and
`IngestSetupBoundaryTests` do (the store and the fetcher are process-wide). Facts 14 to 17 capture the console through
`ThreadScopedConsole`, never `Console.SetOut` alone.

### 4.2 Existing facts

R1 changes none. They stay green for these reasons:

- `IngestAttributionTests` L177-237: their records are lone requests, so the pair rule never applies, and `Tagged`
  still counts records.
- `IngestSetupBoundaryTests`: each response stays in its request's step.
- The German Cucumber fact: its responses stay in their steps.
- `IngestCommandTests.Separate_setup_flag_reaches_the_report`: it reads the partition and the usage text, which R1
  does not touch. Its usage assert at L276 checks for an absent phrase that R1 does not add.

R2 changes `The_pipeline_wires_window_attribution_and_phases_end_to_end` (L296 asserts the success line as
`UnattributedInteractions`) and, if Q5 is taken, `The_pipeline_reports_the_exclusive_mode_even_when_nothing_was_ambiguous`
(L705-727).

### 4.3 Mutations, one per behaviour

| # | Mutation | Turns red |
|---|---|---|
| M1 | each record looked up on its own timestamp again | 1, 2, 3, 10, 13 |
| M2 | a pair judged on its latest record | 1, 3 |
| M3 | a capturer's phase on one half ignored by the other half | 4 |
| M4 | a capturer-phased half overwritten by the pair's phase | 5 |
| M5 | the literal `"Unknown"` comparison restored | 6 |
| M6 | the diagnostic recorded again | 9, 11, 12 |
| M7 | the line dropped, or printed only above zero | 14, 15 |
| M8 | the line printed without the flag | 16 |
| M9 | `Tagged` counting calls | 8 |

### 4.4 Proofs before the tag

- **Red on v4.6.0.** Copy every new fact into a worktree at v4.6.0, with `PhasedRecords` stubbed so it compiles, run
  them there, and read each failure's message (a fact can be red for the wrong reason).
- **Mutations.** Run them in a `git stash create` snapshot worktree, with each file backed up rather than reverted
  by `git checkout`, so uncommitted release work survives.
- **Suites and build.** The core suite, then `release.slnf` built in Release for every target framework.
- **The harness repro.** Run it on the new tool, copied outside the checkout. Expected: the line above, four `Action`
  phases, no `!` line, and with `KRONIKOL_HISTORY=off` no labs page.
- **Acceptance on the owner's Jest suite** (the owner's to run, or to hand over): 3,910 of 3,910 records phased, no
  diagnostic, and `TestRunReport.json` differing from 4.6.0's only in that one response's `phase` and in
  `diagnostics`.

## 5. Releases and bumps

| Release | Contents | Bump | Why this part moved |
|---|---|---|---|
| R1 | Asks 1 and 2, F3, the wiki table (F10) | patch: 4.6.1, or the next free patch | Bug fixes. The count reaches the CLI through an internal member, and `ApplyPhaseFromSteps` keeps its signature. The changelog states the behaviour changes |
| R2, if Q4 is taken | F4: the attribution successes out of the diagnostics, and F5 if Q5 is taken | patch | Bug fixes, nothing new to call. It can ship inside R1 if the owner prefers one release |
| Q2's narrower diagnostic, or Q3 | a new `DiagnosticKind` member or a public `IngestResult` member | minor | New public surface (CLAUDE.md) |

Steps:

- **S0, done (RUN, §2.1):** the repro measured on 4.6.0.
- **S1:** facts 1 to 17, each run red on v4.6.0.
- **S2:** §3.1 and §3.2.
- **S3:** the documentation (§6).
- **S4:** the proofs (§4.4) and the release.
- **S5:** R2, if it is taken.
- **S0b:** ask 3's count on the repo's Cucumber fixture and the owner's suite, only if Q2 is taken.

Draft of R1's changelog entry:

> **Fixed.** `kronikol ingest --phase-from-steps` (`IngestRequest.PhaseFromSteps`) recorded how many records it gave
> a phase as a diagnostic of kind `Other`, so every healthy run that used it carried one. `kronikol query` printed it
> as a `!` line above every answer, `failures` included when nothing failed, `--diagnostics-section` listed it, and
> with history off it alone made the run write `TestRunReport.labs.html`. The count is no longer a diagnostic:
> `kronikol ingest` prints it as a line after `Replayed …`, and `IngestResult.Diagnostics` no longer holds it. The
> pass also gave each record the phase of the step its own timestamp fell in, so the response to a call that
> outlived its step took no phase, or the next step's, while its request took the phase of the step it was made in.
> A request and its response now take one phase: the one a capturer wrote on either half, else that of the step the
> call started in, judged on the pair's earliest record as the run window and the attempt filter judge it. In
> `TestRunReport.json`, the XML and the YAML, such a response's `phase` changes. A `phase` that names no `TestPhase`
> member is now taken from the steps, where it was kept and reported as `Unknown`. `IngestAttribution.ApplyPhaseFromSteps`
> behaves the same way and keeps its signature. Patch: bug fixes, nothing new to call (#130).

The per-release checklist:

- **Versions:** bump every package (`Directory.Build.props`, `plugin.json`, `marketplace.json`, the history action's
  `VERSION`). Template pins stay strictly behind the repo version, and only once nuget.org lists every pinned id.
- **Commit message:**
  - Close #130 with `gh issue close` after publication.
  - Never write "fixes #128" or "closes #129" in passing; those words close an issue.
  - Never quote the bracketed skip-CI marker.
- **After the push:** check the CI, CodeQL and Summary Preview runs of every pushed SHA.

## 6. Documentation

| Where | Change | Release |
|---|---|---|
| Wiki `Ingesting-External-Captures.md:265`, the flag's row | "the step its call started in; a response takes its request's phase" | R1 |
| Same page, L491-505, "Phases from steps" | the pair rule, the capturer rule, F3, and the console line | R1 |
| Same page, L620-634, the Diagnostics table | `Other`'s example becomes "e.g. that no run window could be derived, so nothing was dropped" (`IngestPipeline.cs:637`); a row for each kind an ingest can record that the table lacks (F10), each checked against `DiagnosticEntry.cs` before it is written | R1 |
| Same page, L626, `UnattributedInteractions` | "how many records are still unattributed" only | R2 |
| `Diagnostics-and-Debugging.md:96` | the same kind's wording | R2 |
| `Phase-Aware-Tracking.md:259-262` | check the flag's sentence against the pair rule | R1 |
| `IngestPipeline.cs:188-201`, `IngestAttribution.cs:464-470` (XML docs, shipped) | the pair rule; the count is printed by the CLI, not recorded | R1 |
| `IngestCommand.cs:675-677`, the usage | "of the Given/When/Then step its call started in (a response takes its request's phase)" | R1 |
| `CHANGELOG.md` | §5's entry, folding in any `[Unreleased]` section | R1 |
| Kronikol4J's ledger, the schema text (`ReportGenerator.cs:6552`), `Querying-Reports.md` | no change (§3.4; "Unknown when phase detection is off" stays true; the header's rule is unchanged) | none |

## 7. Where it sits in the roadmap

- **Stage:** stage 1, the patch train, by rule 1 (defects before features).
- **Row and decision:** the next free stage 1 row and decision number at green-light. On 2026-10-06 other plans took
  rows 1.17 (#115), 1.18 (#105) and 1.19 (#113), and D33 (#113), so at the time of writing they would be 1.20 and
  D34. This plan reserves neither; `ROADMAP.md` is not edited until the owner places it.
- **Files and order:** it touches `Ingestion/` and `Kronikol.Tool/IngestCommand.cs`. #128 and #129 touch
  `Reports/ReportGenerator.cs` and `PlantUml/StepBarPlantUml.cs`, so the three can land in any order.
- **This plan's other findings:** §8's items go in the same row, as `INGEST_FIDELITY_PLAN.md` §8 went into row 1.15.

## 8. Found on the way, not in this plan

Each is a patch unless marked.

| # | Defect | Level | Recommendation |
|---|---|---|---|
| 1 | F6. `kronikol ingest` reads and appends to the ledger of whatever git checkout holds the tool's folder. While measuring, a Debug build in this repo's `bin/` appended three lines to `C:\Code\Kronikol\.kronikol\history.jsonl` and printed `HistoryPartialRun` (removed afterwards; a copy is kept outside the repo) | RUN | A history-area patch: an ingest resolves the ledger from the reports folder, then the current directory, never the tool's folder |
| 2 | F7. Phases are applied before the merge, which keeps the wire halves' phases and discards the span twins' | READ, INFERRED | Q6: measure first |
| 3 | F9. Cucumber synthesis warnings reach no reader | READ | Print them, or record those that change the report as diagnostics |
| 4 | `ProvenanceNotes` prints one message per kind, so two unrelated `Other` lines read as the first one "(×2)" | READ | Design, not a defect on its own; R2 removes the case where it hides a failure |

## 9. What is not known

- Whether any host reads the count from `IngestResult.Diagnostics`. None in this repo does: L747 is its only
  producer, and nothing matches its text. The owner's Node app uses the CLI.
- Whether any capturer writes a phase that names no member (F3's reach). This repo's writers write member names
  (`FromLog`, `InteractionRecord.cs:256`), INFERRED.
- Ask 3's count on real suites (ESTIMATED, Q2).
- What the in-process lanes give the calls of a Background step (Q7).

## 10. Risks and traps

1. **The history ledger.** Run the tool from a copy outside every checkout, or with `KRONIKOL_HISTORY=off`, or an
   acceptance run writes to the repo's ledger (F6).
2. **Process-wide state.** Pipeline facts go in the `DiagramsFetcher` collection, and console capture goes through
   `ThreadScopedConsole`.
3. **Release numbers.** Parallel sessions are planning in this checkout. The first release to land takes the number,
   and comments keep a placeholder (`4.6.R1`) until it is taken.
4. **A fact red for the wrong reason on the old tag.** The compile stubs are where it would come from, so read every
   failure message.
5. **Target frameworks.** Build `release.slnf` in Release for every target before tagging.

## 11. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Keep the count, and in what form | **Keep it** as one line after `Replayed …`, whenever the flag is given, zero included, counting records (§3.1). Dropping it is the alternative the issue allows; counting calls would need a second unit beside `Replayed` |
| Q2 | Ask 3: a diagnostic for the calls the pass could not phase | **Not in R1.** Calls in hooks, between steps, in Background steps (F8) and in tests with no steps fall outside every step on a healthy run, as do calls folded into "Traffic outside any test", so the count would be the same noise in many suites. If wanted, S0b measures it first, and the actionable case gets the diagnostic: a step with no `durationMs`, whose window holds only its own instant. A new kind is a minor; the number on Q1's line is a patch |
| Q3 | A public `IngestResult` member for the count | **No.** No reader has asked, and it would be a minor |
| Q4 | Take R2 (F4) | **Yes**, as its own patch after R1, or inside R1 if the owner wants one release. Same principle as ask 1, and it stops a success line from standing in for a failure |
| Q5 | `ExclusiveOnly`'s line at zero (F5) | **Record it only when records were left ambiguous**, and print the zero case as a console line, as R1 does. The console line still proves the mode ran; a non-zero count is a real loss and stays a diagnostic |
| Q6 | The merger's phases (F7) | **Measure first**, with a wire capture plus spans and parallel workers. If it reproduces, a merged call takes its span pair's phase when its wire pair has none (`Adopt`). A patch |
| Q7 | Background steps open no phase window (F8) | **Measure the in-process answer first.** It belongs with roadmap row 1.15's item 6 (four step-to-phase vocabularies that disagree on And and But) |

## Appendix A. Edit sites

| File | Release | Change |
|---|---|---|
| `src/Kronikol/Ingestion/IngestAttribution.cs:464-505` | R1 | the pair map, the capturer rule, `ResolvedPhase` as "known", the doc |
| `src/Kronikol/Ingestion/IngestPipeline.cs:188-201` | R1 | `PhaseFromSteps`'s doc |
| `src/Kronikol/Ingestion/IngestPipeline.cs:276-299`, `414`, `469`, `496`, `700-760` | R1 | `IngestResult.PhasedRecords` (internal); `Attribute` hands back the count; L746-747 deleted |
| `src/Kronikol.Tool/IngestCommand.cs:445-446`, `675-677` | R1 | the line; the usage |
| `tests/Kronikol.Tests/Ingestion/IngestAttributionTests.cs` | R1, R2 | facts 1-8; L254-297 and L705-727 in R2 |
| `tests/Kronikol.Tests/Ingestion/IngestPhaseFromStepsTests.cs` (new) | R1 | facts 9-13 |
| `tests/Kronikol.Tests/Tool/IngestCommandTests.cs` | R1 | facts 14-17 |
| `src/Kronikol/Ingestion/IngestPipeline.cs:704-737` | R2 | the success counts as console lines; F5 if Q5 is taken |
| `CHANGELOG.md`, the version files, the template pins | R1, R2 | the release |
| `../Kronikol.wiki` (§6) | R1, R2 | §6 |

## Log

- 2026-10-06: drafted from #130. S0 RUN on 4.6.0 (`harness/s0/`). Nothing implemented; not green-lit.
- 2026-10-07: **green-lit** by the owner ("complete the plan in full, in a separate worktree so that you don't affect
  the other sessions"), every open question taken as recommended. Executed in worktree `Kronikol-130` while three
  other plans' sessions released beside it: `ROADMAP.md` row 1.20 and D35 (D34 went to #105 and D36 to #115 the same
  morning, by the rule that the lower issue number takes the lower decision number), and the releases took the next
  free numbers after 4.6.1 (#115) and 4.6.2 and 4.7.0 (#105): R1 took 4.7.1, which #113's session offered while its
  own suites ran.
- 2026-10-07, **R1 = 4.7.1** (patch). Shipped: asks 1 and 2 and F3 as §3.1 and §3.2 describe; the wiki's ingest
  Diagnostics table (F10). Proofs, each red first:
  - The 17 facts (19 cases) on 4.6.1 (no change under `Ingestion/` or `IngestCommand.cs` since 4.6.0): the 16 cases
    expected to fail did, each for its own reason, and the three guards passed (`harness/r1/red-on-4.6.1.txt`).
  - 11 mutations, each turning its facts red (`harness/mutations/r1-results.txt`; `mutate.py`).
  - The issue's repro on the new tool, copied outside every checkout, with history off: the line
    `--phase-from-steps: 4 interaction record(s) took the phase of the step their call started in.`, four `Action`
    phases, no diagnostic, no labs page, and `query failures` with no `!` line (`harness/r1/repro-B.txt`,
    `repro-A.txt` with `--diagnostics-section`).
  - The core suite (6,565 passed, 2 skipped on 4.6.1; 6,623 passed, 2 skipped on the release commit), the Playwright suite (982 passed, 28 skipped), the search-engine suite
    (214), and `release.slnf` built in Release for every target and packed (62 packages).
  What differed:
  - The red run was on 4.6.1, not 4.6.0: 4.6.1 landed first and changed no file R1 touches.
  - The wiki table lacked nine kinds an ingest can record, not five: the four `History*` kinds too (S0 printed
    `HistoryUnavailable` from an ingest). Each row was checked against `DiagnosticEntry.cs`.
  - Fact 7 is a guard no plan mutation could turn red (M1 makes it pass), so it got its own: M10, records with no pair
    id keyed together as one pair.
  - The acceptance on the owner's Jest suite is the owner's (§4.4).
- 2026-10-07, **R2 = 4.7.2** (patch). Shipped: F4 and F5 (Q5), and two defects found on the way: Q6's merger phase and
  §8 item 1, the ledger an ingest finds. Proofs, each red first:
  - On R1: 9 facts red for their own reasons (the window, claims and `ExclusiveOnly` diagnostics; the CLI line, its
    zero and the usage; the end-to-end fact; the merged call), the guard green; the ledger fact red on R1, where the
    tool from the test's bin appended to the worktree's own ledger (4 lines, deleted).
  - 11 mutations (`harness/mutations/r2-results.txt`; `mutate_r2.py`).
  - The core suite (6,575 passed, 2 skipped before the rebase; 6,633 passed, 2 skipped on the release commit), the Playwright suite
    (982 passed, 28 skipped), the search-engine suite (214), and `release.slnf` built in Release and packed (62 packages).
  What differed:
  - Q5 assumed a console line for `ExclusiveOnly` at zero, but no CLI flag sets that mode (`--attribute-by-window` is
    the innermost rule), and neither does any flag set content claims. So only the window pass's count is printed,
    and the two API-only counts leave the diagnostics with no replacement, as R1's did (Q3: no public member).
  - Q6 measured (RUN): a wire record that named no test when phases were given took its span twin's test in the merge
    and not its phase, so both halves of the merged call were `Unknown`. `Adopt` now takes the twin's phase when the
    wire record has none; a guard fact keeps a phase the wire capturer wrote. It rode with R2, as Q6 allowed.
  - §8 item 1 rode with R2 rather than wait for a release of its own: the ingest hands the generator the directory it
    runs in as the history base directory (an internal parameter), checked by a fact that runs the tool from the test's
    bin, inside the checkout, as a child process.
  - Q7 (READ, not RUN: no example's Background step makes a call): in-process, ReqNRoll, LightBDD and BDDfy phase
    every step from its keyword (`PhaseConfiguration.ResolvePhaseFromStepType`, Given, And and But are `Setup`),
    Background steps included, while an ingest opens no window for a Background step (`IngestAttribution.cs:448`,
    pinned by `Background_and_nested_steps_open_no_phase_window`). The two lanes disagree; recorded on row 1.15's
    item 6 (the step-to-phase vocabularies), with no code change.
  - §8 item 3 (F9, Cucumber synthesis warnings) was taken by #105's session: 4.6.2 records them in the run's
    diagnostics.
  - §8 item 4 (one message per kind in the query header) stays a design question; R2 removes the case where it hid a
    failure.
