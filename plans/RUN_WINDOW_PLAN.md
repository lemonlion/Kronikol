# Traffic outside the run window that another window explains, and a stack's start-up kept as the run's Setup (#187)

**Date:** 2026-10-21 · **Repo version:** 5.2.3 (`main` at `9d89b793`; line numbers are for that commit, and no file under
`src/Kronikol/Ingestion/` changed since `4a0dad9e`, the 5.2.2 the issue cites, so the issue's line references hold) ·
**Status:** 📝 **Draft, not green-lit.** Written in a worktree of its own (`Kronikol-187`, branch `plan/187-run-windows`)
while other sessions worked in theirs. Nothing outside `plans/` changes.

Evidence labels: **RUN** (measured on 2026-10-21 against Kronikol 5.2.3 from nuget.org, or against the prototype of
section 3, with .NET 10 file-based apps; scripts and verbatim answers in
[`RUN_WINDOW_PLAN.harness/`](RUN_WINDOW_PLAN.harness/README.md)), **READ** (in the source and tests at `9d89b793`, the
wiki at `7363a88`, or Kronikol4J at `92d6b28`), **INFERRED** (reasoned from two facts, stated by neither),
**ESTIMATED** (not measured; the step that would measure it is named).

The owner's issue, [#187](https://github.com/lemonlion/Kronikol/issues/187) (2026-10-21, filed against 5.2.2), abridged:

> `DropOutsideRunWindow` drops every interaction whose request falls outside the run window, and counts all of them as
> one `DroppedOutsideRunWindow` diagnostic [...]:
> `3623 interaction record(s) outside the run window (… → …) dropped: 1694 before the run began, 1929 after it ended.`
>
> - **The 1,694 before** were the stack starting up, 13 minutes before the run [...]. That's useful context for the run
>   [...]. But a host can only drop it, or start the window early and have `FoldUnknownTestsInto` pour it into "Traffic
>   outside any test", among the run's own unattributed calls.
> - **The 1,929 after** were the device suite, which started on the same stack 0.6 s after the browser run's verdict.
>   Its own report shows them. That report's diagnostic, the other way round, counts the browser run's 19,190 records
>   as dropped before its run began.
>
> So the diagnostic that's meant to say what a report left out reads as a large loss on every report. A real loss, such
> as a test's call made after the verdict, or traffic between two runs that no report covers, can't be told apart.
>
> **Suggestion:** two options on `IngestRequest`: `SetupWindow` (a start, an end and a scenario name: interactions
> inside it are folded into one scenario of that name, drawn before the tests, in the Setup phase, `NotATest`, no
> verdict, not counted as dropped) and `OtherRunWindows` (named windows: an interaction inside another run's window is
> dropped without counting; at most one note says how many belong to which run). `DroppedOutsideRunWindow` then counts
> only what no window explains. Related: #181.

## TL;DR

- **The issue is right, and it reproduces on 5.2.3 with the issue's shape** (RUN, P1). One capture holds a stack's
  start-up, a browser run, two real losses (a test's call made after the verdict, and a call between the runs), and a
  device run 0.6 s after the browser run's verdict. The browser report says `26 ... dropped: 16 before the run began,
  10 after it ended`, of which 4 are real losses; the device report says `30 ... 30 before`, of which the same 4 are.
- **The cause is where the window's knowledge comes from.** The run window is built from this run's tests file alone
  (`IngestPipeline.cs:714-738`), so the ingest cannot know what any record outside it is, and it says so with one
  count (`:795-800`). Nothing in a record, a tap or a tests record carries a run id or a run boundary (READ). The host
  is the only party that knows, and it has no way to say it.
- **The count is louder than a diagnostic tab.** `DroppedOutsideRunWindow` is one of the kinds that mean "the report
  holds less than the run produced" (`Query/QueryCommand.cs:383-406`), so every `kronikol query` answer on both reports
  opens with a `!` line saying its counts are a lower bound. That line is cut at 160 characters (`:426`), which on
  today's text falls before the second figure's meaning (RUN).
- **The workarounds cost more than the issue says** (RUN, P1). Starting the window at the stack's start keeps the
  start-up, but the run then lasts **13m 10s instead of 9 s** in the run band (its start is the start-up's), the
  start-up is counted as `could not be attributed to a test` (2 records become 18), and it shares "Traffic outside any
  test" with the run's own global login. Dropping it with `DropUnattributed` instead keeps the health checks, and the
  run still lasts 770 s.
- **Found on the way: `NotATest` is honoured only by the history** (READ, RUN). The fold scenario counts as a passed
  test in the run band, the ring, the features table, the timeline (where a long span makes it the top bar), the
  duration percentiles, the CI summary, `Failures.md` ("All 3 scenarios passed" for two tests) and `kronikol query`,
  and the Specifications list it as a scenario. The data files do not carry the flag, so `kronikol merge` loses it: the
  merged report's history calls the fold scenario `stable: passing`, read against the very ledger in which its shard
  recorded it as `N` (RUN, P4). A start-up scenario would inherit all of it: on the prototype, a report of two tests
  reads `Scenarios 4, Passed 4`, and its timeline's top bar is the stack's start-up (RUN, P3).
- **Also found:** a run that wrote no end marker keeps the next run's traffic and takes that run's end as its own
  (RUN, P2a); an explicit window that ends before it starts drops every record, the tests' own calls included, without
  saying why (RUN, P2b); no CLI fact covers `--run-window`, `--run-start` or `--run-end` (READ).
- **The design is the issue's, with six refinements**, each measured on a prototype (RUN, P3):
  1. one rule for every record outside the run window, in a fixed order: another run's window, then the setup window,
     then unexplained;
  2. an open end is closed where the next declared run begins;
  3. the start-up scenario is a feature of its own, drawn first, outside the run's start and end, and drawn inside the
     Setup partition when `SeparateSetup` is on (a Phase boundary after its last call, which 5.2.3 already draws);
  4. the unexplained count says how many of its records name a test in this run's tests file (the "test's call made
     after the verdict" case, or clocks that disagree);
  5. the message puts its figures first, so the 160-character query line keeps them;
  6. what the windows explained is returned on `IngestResult` and printed by `kronikol ingest`, and is never a
     diagnostic, which keeps the rule of #130: what a pass did as asked is not "what went wrong".
- **Two releases.** R1, a patch: `NotATest` honoured by every reader and kept by the data files and the merge, the
  inverted window refused, and the CLI facts. R2, a minor: `SetupWindow`, `OtherRunWindows`, the accounting, the
  `IngestResult` counts and the CLI flags.
- **What the prototype prints on the issue's shape** (RUN, P3): the browser report keeps the 16 start-up records in
  "Stack start-up", leaves the device run's 6 to its report, lasts 9.5 s as before, and says
  `4 ... dropped that no other window explains: 0 before the run began, 4 after it ended`, 2 of which name `b2`. On
  the owner's capture, if the host's windows cover the start-up and the device run, the browser report would keep the
  1,694 and drop nothing it cannot explain (INFERRED; the capture is the owner's, so the check is too).

## 0. Findings

| # | Finding | Evidence | Where |
|---|---|---|---|
| F1 | **Everything outside the run window is one count.** The pass judges a call by its earliest record and counts `before` and `after` (`IngestPipeline.cs:745-803`); nothing it is given can say what a record outside the window is | RUN (P1), READ | R2 |
| F2 | **The count heads every query answer.** `DroppedOutsideRunWindow` is in `AnswerAffectingDiagnostics` (`QueryCommand.cs:393-406`), whose remarks say each kind there "means the report holds less than the run produced" (`:383-384`). Another run's traffic is not something this run produced | READ, RUN (P3 summary) | R2 |
| F3 | **The query line cuts the message before its second figure's meaning.** `ProvenanceNotes` keeps 160 characters (`QueryCommand.cs:426`), and today's text spends 70 of them on the window's two instants before the figures: P1's browser line ends `16 before the run began, 10 …` | RUN | R2 |
| F4 | **Starting the window early stretches the run.** With `RunStartedAt` at the stack's start, the run band says `Duration 13m 10s` and `09:47:00–10:00:10` where the run took 9 s, and `UnattributedInteractions` goes from 2 to 18, the start-up counted as "could not be attributed to a test" | RUN (P1 C) | R2 |
| F5 | **A predicate drops only what it names.** `DropUnattributed` on the database drops the 12 seeding records and leaves the health checks in the fold; the run still lasts 770 s | RUN (P1 D) | R2 |
| F6 | **`NotATest` is read only by the history.** Its one setter is `IngestPipeline.cs:554`; every reader is under `History/` (`HistoryRunBuilder.cs:82`, `:99`, `HistoryAnalyzer.cs:104`, `:235-236`, `HistoryHtml.cs:367`, `:379`). The run band, ring, features table, timeline, percentiles, CI summary, `Failures.md`, the console run summary, `Run.json` and the CTRF report count it as a passed scenario with a duration (`ReportGenerator.cs:1761-1766`, `:1944-1948`, `:2190-2194`; `CiSummaryGenerator.cs:26-31`; `FailuresDigestGenerator.cs:183`; `RunSummaryConsoleWriter.cs:134-146`; `RunRotation.cs:512-537`; `CtrfReportGenerator.cs:215`), and the Specifications list it. P1's browser report of two tests reads `Scenarios 3, Passed 3`, and its `Failures.md` "All 3 scenarios passed." | READ, RUN | R1 |
| F7 | **The data files do not carry `NotATest`, so the merge and the query lose it.** The JSON scenario object has no such key (`ReportGenerator.cs:5296-5333`), nor the XML (`:5890`, `:5928`) or YAML (`:6173-6187`); `MergeableReportReader.ReadScenario` (`:236`) cannot read it. `kronikol merge` never writes the ledger ("a merge is a render", `MergeCommand.cs:194-197`) but judges the merged run against it (`:217-218`): the shard's report calls the fold scenario `stable: not a test`, the merged report `stable: passing`, its series `N` then `P`, and the ledger stays `NPP`. `kronikol query summary` lists the fold scenario as passed, and as the slowest when it is | READ, RUN (P4, P2d, P3) | R1 |
| F8 | **The run's start and end are taken over every scenario**, a not-a-test one included (`FeatureSynthesizer.cs:243-249`). A start-up scenario would move the run's start to the start-up's | READ, RUN (F4 is the fold's case) | R2 |
| F9 | **A run with no end marker takes in the next run.** Its window stays open (`IngestPipeline.cs:725-735`), so the device run's calls are kept, folded into "Traffic outside any test", and the browser run ends at the device run's last call (`10:00:13.100`) | RUN (P2a) | R2 |
| F10 | **An explicit window that ends before it starts drops every record**, the tests' own calls included, and the message prints the inverted window as if it were a window: `6 ... (10:00:10 → 10:00:00) dropped: 2 before the run began, 4 after it ended` | RUN (P2b) | R1 |
| F11 | **A record that names one of this run's tests can still be another run's.** Playwright-style test ids are stable across runs; with the same suite run twice on one stack, the first run's calls carry the second run's ids. With the window they are 4 records dropped before the run; without it they land in the second run's tests, and the run spans an hour | RUN (P2c) | R2 (Q6) |
| F12 | **A scenario whose every call is Setup draws no Setup partition**: the partition needs a Phase boundary with a call before it (`PlantUml/PlantUmlCreator.cs:206-214`), and a call's phase alone draws nothing (`IngestPipeline.cs:201-206`). A Phase boundary after a scenario's last call wraps every call in `partition #F6F6F6 Setup`, on 5.2.3 as it is | RUN (P2d), READ | R2 |
| F13 | **No CLI fact covers `--run-window`, `--run-start` or `--run-end`** (`IngestCommand.cs:221-234`, `:451-453`). `IngestCommandTests` covers the fold, the window-attribution line and `--diagnostic` | READ | R1 |
| F14 | **What a pass did is a console line, not a diagnostic** (`IngestPipeline.cs:821-823`, `IngestCommand.cs:470-476`, #130). The issue's "one note" about other runs is such a count | READ | R2 (design) |
| F15 | **`DropUnattributed` is asked about the start-up today**, which is half of its documented use ("a database tee that also sees the seeder's traffic", `IngestPipeline.cs:154-158`); #181 is about the other half. With the setup window the predicate is not asked about the start-up (RUN, P3 E) | RUN, READ | R2, #181 |
| F17 | **A fold scenario was named after its id when its records already carried the id.** `Order` renamed only the records whose id was neither a test's nor the fold's (`IngestPipeline.cs:1166`), so a capture whose unattributed calls carry the fold's id (the wiki's recipe folds into `"session"`, the window attribution's fallback id) drew the fold scenario as `session` | RUN (found writing fact 4's ingest), R1 | R1 |
| F18 | **The history rebuilt from a data file took the fold for a pass.** Without `History.run.json`, `kronikol history import`, `history gate` and `query history` rebuild the run from the data file (`QueryCommand.History.cs:578-584`), which had no flag, and the gate's pass rate counted the fold (`HistoryCommand.Maintenance.cs:114-115`) | READ, RUN (`HistoryNotATestTests`), R1 | R1 |
| F19 | **Export Filtered CSV listed the fold as a passed row** (`report-export-function.js`) | RUN (`NotATestPaintTests`), R1 | R1 |
| F20 | **"No scenarios match the filters" read the readout's count**, which counts tests from R1, so it would have shown while a scenario that is not a test was all that was shown (`report-filter-bar-script.js`) | READ, R1 | R1 |
| F21 | **The CI summary drew the fold's diagram among the passed scenarios', with a tick** (`CiSummaryGenerator.cs`) | READ, R1 | R1 |
| F22 | **The run diff's header and `kronikol ingest`'s `Replayed` line counted the fold as a scenario of the run** (`QueryCommand.Search.cs`, `IngestCommand.cs:478`) | READ, R1 | R1 |
| F23 | **The agent skill's fallback `query.py` counted the fold as a test**, where the tool it mirrors counts tests (`scripts/query.py`, `cmd_summary` and `cmd_failures`; `FallbackScriptTests` holds the two to the same numbers, on a fixture with no such scenario) | READ, RUN, R1 | R1 |
| F16 | **No producer in this repo writes the `testrun` markers the window reads**, except the Go prototype's harness (`plans/GO_PLATFORM_PLAN.harness/h11_prototype/kgo/testing.go:15-21`); the Playwright and Jest recipes in plan harnesses write none, and no wiki recipe does. Such a run's window starts at its first test, and its runner's global set-up falls "before the run began" | READ | §8 |

## 1. How far each claim of the issue was checked

| Claim | Checked | Result |
|---|---|---|
| The diagnostic counts all of it as one `DroppedOutsideRunWindow` (L745-L800) | READ at `9d89b793` (the same lines); RUN P1 | Holds |
| 1,694 before and 1,929 after on the owner's run | Not reproducible here (the owner's capture) | Consistent with the code: P1's shape gives `16 before, 10 after` |
| A host can only drop the start-up, or start the window early and fold it with the run's own unattributed calls | RUN P1 C and D | Holds, and each way costs more: F4 and F5 |
| The device report counts the browser run as dropped before its run began | RUN P1 B | Holds: `30 before`, the browser run's 10 among them |
| A real loss can't be told apart | RUN P1 A | Holds: 10 after = the device run's 6 + a late call of `b2` (2) + a call between the runs (2) |
| The fold scenario "is `NotATest` and has no verdict" | READ, RUN | **Partly.** It is `NotATest` in the history only; everywhere else it has the verdict `Passed` and counts as a test (F6, F7) |
| `FoldUnknownTestsInto` is at L88 | READ | Holds |

## 2. What the code does today

### 2.1 The run window (READ)

`IngestRequest.DropOutsideRunWindow` (`IngestPipeline.cs:162-183`) keeps "only the traffic of this run". Its remarks
name this issue's use case: "taps that append for as long as the stack is up [...] without a window, the previous run's
traffic [...] and the stack's start-up traffic are all folded into 'Traffic outside any test', which then dwarfs the run
it is supposed to describe". `RunStartedAt` and `RunEndedAt` (`:185-189`) set the bounds and imply the option.

- **The window** (`ResolveRunWindow`, public, `:714-738`): start = `RunStartedAt`, else the earliest tests record of
  any kind; end = `RunEndedAt`, else the latest `testrun` marker whose status is not `started`, else open ("a run that
  died never wrote one, and its late traffic must not vanish", `:177-178`).
- **The pass** (`DropOutsideRunWindow`, `:745-803`) runs first (`:489`), before claims, windows, phases, the merge and
  `DropUnattributed` (`:490`, `:817-873`). A call is judged by its earliest record (`:760-773`); a record with no
  timestamp stays. One diagnostic counts the rest: `{n} interaction record(s) outside the run window ({start:o} →
  {end}) dropped: {before} before the run began, {after} after it ended.` (`:795-800`).
- **The fold** (`Order`, `:1156-1173`) gives every record whose test id is not in the tests records the fold
  scenario's id; `RunCore` then makes that scenario `Passed`, not `ResultDefaulted`, and `NotATest` (`:536-557`).
- **The CLI** (`IngestCommand.cs`): `--run-window` (`:221-223`, `:451`), `--run-start` and `--run-end` (ISO-8601,
  universal, each implying `--run-window`, `:224-234`, `:452-453`), help `:717-722`. There is no CLI form of
  `DropUnattributed`, claims or `ExclusiveOnly`.
- The run window arrived in 3.0.45 with the `DroppedOutsideRunWindow` kind (`CHANGELOG.md:11693`); the comment of its
  first fact says it came from "a second `sidekick test` on one stack" (`IngestAttributionTests.cs:680-682`).

### 2.2 The repro (RUN, P1)

[`p1-two-runs.cs`](RUN_WINDOW_PLAN.harness/p1-two-runs.cs) writes one capture and two tests files, each run bracketed by
`testrun` markers as the issue's host writes them, and ingests each run as the host does (`DropOutsideRunWindow`,
window attribution with the fallback id `session`, a fold). Answers in
[`results/p1.txt`](RUN_WINDOW_PLAN.harness/results/p1.txt).

| Part of the capture | Records | Time |
|---|---|---|
| The stack's start-up: 6 seeding inserts, 2 health checks | 16 | 09:47:00 to 09:47:25 |
| The browser run (`testrun` 10:00:00.000 to 10:00:11.000): a global login, `b1` and `b2` with a database call each | 10 | 10:00:00.5 to 10:00:07.3 |
| `b2`'s call made after the verdict (a real loss) | 2 | 10:00:11.300 |
| A database call between the runs (a real loss) | 2 | 10:00:11.400 |
| The device run (`testrun` 10:00:11.600 to 10:00:21.000): `d1`'s two calls and a database call | 6 | 10:00:13 to 10:00:15.3 |

| Ingest | Diagnostic | Scenarios | Run |
|---|---|---|---|
| A. browser, as the host does | `26 ... dropped: 16 before the run began, 10 after it ended.` | `b1` 4, `b2` 4, fold 2 (the login) | 10:00:00.500 to 10:00:10.000, 9.5 s |
| B. device, as the host does | `30 ... dropped: 30 before the run began, 0 after it ended.` | `d1` 6 | 8 s |
| C. browser, `RunStartedAt` at the stack's start | `10 ... dropped: 0 before ..., 10 after ...`; `UnattributedInteractions: 18` | fold 18 (the start-up and the login) | **790 s; the band says 13m 10s** |
| D. as C, with `DropUnattributed` on the database | `DroppedUnattributed: 12`; `UnattributedInteractions: 6` | fold 6 (health checks, login) | 770 s |

### 2.3 Where the count goes (READ)

- **Every `kronikol query` answer**: a `!` line per answer-affecting kind (`QueryCommand.cs:375-376`, `:393-406`,
  `:417-434`), cut at 160 characters (`:426`). On P1's browser report the line ends `16 before the run began, 10 …`
  (RUN). `query summary`'s inventory prints 90 characters of it (`QueryCommand.Overview.cs:177-190`).
- **The report**: the Diagnostics tab and the labs page (`ReportGenerator.cs:4853-4882`, `LabsReportGenerator.cs:31-32`),
  the data files' `diagnostics` (JSON `:4997-4999`, XML `:5961-5966`, YAML `:6308-6322`); the JSON schema's kind enum
  is `Enum.GetNames(typeof(DiagnosticKind))` (`:7512`).
- **The console**: `kronikol ingest` prints every entry (`IngestCommand.cs:572-589`).
- **Not read** by `Failures.md`, the CI summary, the history, or any exit code (`IngestCommand.cs:506`).

### 2.4 `NotATest` (READ, RUN)

`Scenario.NotATest` (`Reports/Scenario.cs:91-97`) says "The report draws it like any scenario. The cross-run ledger
records it as `N`." The history does, and nothing else reads it (F6):

| Reader | What it does with the fold scenario |
|---|---|
| Run band and ring (`ReportGenerator.cs:1761-1766`, `:3129-3147`) | counts it as a passed scenario: P1 A reads `Scenarios 3, Passed 3` for two tests (RUN) |
| Features table, readout, status filters (`:1804-1818`, `:1889`, `:2394`) | a passed scenario of feature "Ingested" |
| Timeline (`:2190-2194`) | a bar, sorted by duration; a fold that spans the run would be the longest bar (INFERRED). On the prototype the start-up is the top bar (RUN, P3) |
| Duration percentiles (`:1944-1948`) | one of the durations |
| CI summary (`CiSummaryGenerator.cs:26-31`), `Failures.md` (`FailuresDigestGenerator.cs:183`) | counted in the totals and the passed count: P1's `Failures.md` reads "All 3 scenarios passed." for two tests (RUN) |
| Specifications (`ReportGenerator.cs:6463`, `:6482`) | listed: P1's `Specifications.yml` has `Feature: Ingested`, `Scenario: Traffic outside any test` (RUN) |
| Data files (`ReportGenerator.cs:5296-5333`, `:5890`, `:6173-6187`) | no key: a reader cannot tell |
| `kronikol query` (`ReportScanner.cs:337`, `:937`; `QueryCommand.Overview.cs:82-95`, `:139`) | `Ingested  1 passed`; listed under `Slowest:` when it is (RUN, P3: `s3  25.01s  Stack start-up`) |
| `kronikol merge` (`MergeableReportReader.cs:236`, `MergeCommand.cs:194-197`, `:217-218`) | the merged features lose it, and the merged report's history calls it `stable: passing`; the ledger, which only `kronikol history record` writes, keeps `N` (RUN, P4) |
| Component diagram (`ReportGenerator.cs:424-429`) | built from every call of the run, so its calls are components and relationships (Q16) |
| Warm-up (`WarmUpCalls.cs:140`) | its calls are judged with the tests' (Q11) |

### 2.5 The edges (RUN, P2)

[`p2-edges.cs`](RUN_WINDOW_PLAN.harness/p2-edges.cs), answers in [`results/p2.txt`](RUN_WINDOW_PLAN.harness/results/p2.txt):

- **a. No end marker.** The browser run's window is open; the device run's call and its database call are kept and
  folded, and the run ends at `10:00:13.100`, inside the device run (F9).
- **b. An inverted explicit window** (`RunStartedAt` 10:00:10, `RunEndedAt` 10:00:00) drops all 6 records, `b1`'s own
  call included, and says `6 ... (10:00:10 → 10:00:00) dropped: 2 before the run began, 4 after it ended` (F10).
- **c. The same suite twice with stable ids.** With the window, the first run's two calls are `4 ... before the run
  began`; without it, `t1` and `t2` each hold both runs' calls and the run spans 09:00:02 to 10:00:10 (F11).
- **d. A Phase boundary after the fold scenario's last call, `SeparateSetup` on**: the diagram is
  `partition #F6F6F6 Setup` around both calls, then `end` ([`results/p2d-fold.puml`](RUN_WINDOW_PLAN.harness/results/p2d-fold.puml)) (F12).

### 2.6 Producers and taps (READ)

- **No run identity anywhere.** `TestRunRecord` has no run id, run name or suite (`Ingestion/TestRunRecord.cs:34-196`);
  `testrun` is not a scenario event (`:215-232`, `FeatureSynthesizer.cs:104`). `InteractionRecord`'s kinds are
  `interaction`, `ui`, `step`, `assertion` and `marker`, each about one test (`InteractionRecord.cs:148-164`). No
  capture record can bound a run.
- **The taps cannot be told a run began or ended.** ProxyTap and TcpTap are hosted services that live as long as their
  host (`ProxyTapServiceCollectionExtensions.cs:14-25`, `TcpTapServiceCollectionExtensions.cs:14-26`), write through
  `Sink` (an `NdjsonInteractionWriter` with `append: true` for a file that outlives a run,
  `NdjsonInteractionWriter.cs:23-33`), and have no control endpoint: ProxyTap forwards every request with its own path
  (`ProxyTapOptions.cs:35-39`), and TcpTap reads only the protocol it relays. TcpTap's fallback id
  is `unknown` (`TcpTapOptions.cs:96-101`), ProxyTap's is a fresh GUID per unattributed request unless set
  (`ProxyTap.cs:585-586`); `session` is a convention (`IngestPipeline.cs:139-144`).
- **So the host is the only party that knows the windows.** It starts the stack, starts each run and writes each run's
  `testrun` markers. It can already read another run's window with the same rule, since `ResolveRunWindow` is public.

### 2.7 The tests today (READ)

- `IngestAttributionTests.cs:678-727` (`Traffic_before_the_run_began_or_after_it_ended_is_dropped_not_folded`) pins the
  message by two substrings: `"3 interaction record(s) outside the run window"` (`:724`) and `"2 before the run
  began, 1 after it ended"` (`:725`). `:730-756` pins `ResolveRunWindow`; `:759-777` the no-window `Other`.
- `IngestPipelineTests.cs:220`, `:264` pin the fold; `IngestAttributionTests.cs:513` pins `NotATest` on it.
- `Tool/ProvenanceBannerTests.cs:34-51` pins that `DroppedOutsideRunWindow` heads an answer (made-up messages).
- `Tool/IngestCommandTests.cs:688` covers `--fold-unknown`; nothing covers the run-window flags (F13).

## 3. Design

### 3.1 What was considered

| Option | For | Against | Taken |
|---|---|---|---|
| **A1. The host declares the windows** (the issue): a setup window, and other runs' windows | The host is the only party that knows (§2.6); works for any number of runs, and for traffic that is no run at all | The host has to pass them; a host that does not keeps today's count | **Yes**, with §3.3 to §3.8 |
| A2. Classify by test id alone, no host input | Needs nothing new | Start-up and another run's database traffic both carry the fallback id; a stable id can be another run's (F11) | **As a refinement** of A1's remainder (§3.6) |
| A3. Run boundaries in the capture (the taps write run markers) | The capture describes itself | No tap host exists yet (`kronikol tap` is `PLATFORM_FOUNDATIONS_PLAN.md`'s L1, `:642`, and F10, `:773`), the taps have no control channel, and the host still has to say which run is which | No; belongs with that `kronikol tap` |
| A4. The start-up in the background-calls section | An existing home for traffic with no scenario | A table with no diagram (`ReportGenerator.cs:4890-4938`), which an ingest never produces; the issue asks for a scenario drawn first | No |
| A5. One list of windows, each with a disposition (keep / leave) | One concept | Two options read better, and the setup scenario is one | No |
| A6. Other runs read from their tests files (`OtherRunTestsFiles`) | No timestamps to pass | A host can already call the public `ResolveRunWindow` on the other file; a CLI convenience at most | Q8 |

### 3.2 The surface (R2)

```csharp
public sealed class IngestRequest
{
    // ... as today ...

    /// The stack's start-up: what the services did before this run began. Interactions outside the run window whose
    /// call began inside it are kept, in one scenario of their own, instead of dropped. Implies DropOutsideRunWindow.
    public SetupWindow? SetupWindow { get; init; }

    /// Other runs that share the capture: an interaction outside this run's window whose call began inside one of
    /// these is left to that run's report, and is not counted as dropped. Implies DropOutsideRunWindow.
    public IReadOnlyList<OtherRunWindow> OtherRunWindows { get; init; } = [];
}

/// The stack's start-up, kept as one scenario (IngestRequest.SetupWindow).
public sealed record SetupWindow(
    DateTimeOffset Start, DateTimeOffset End, string ScenarioName = "Stack start-up", string ScenarioId = "stack-start-up");

/// Another run on the same stack (IngestRequest.OtherRunWindows). A null End is a run still going.
public sealed record OtherRunWindow(string Name, DateTimeOffset Start, DateTimeOffset? End = null);

public sealed record IngestResult(...)
{
    /// Interactions outside the run window kept in the setup scenario; 0 without a SetupWindow.
    public int SetupRecords { get; init; }

    /// Interactions outside the run window left to other runs' reports, by run name; empty without OtherRunWindows.
    public IReadOnlyDictionary<string, int> OtherRunRecords { get; init; } = ...;
}
```

Every addition is an `init` property or a new type: no public signature changes, so no binary break
(`IngestResult`'s positional constructor stays as it is). `SetupWindow` follows `UnknownTestFold`
(`IngestPipeline.cs:278`): a name and an id, the id also being the `testId` the records take.

### 3.3 One rule for a record outside the run window (R2)

Each call is judged once, by its earliest record as today (`:760-773`), in this order:

| # | Where its call began | What happens | Counted |
|---|---|---|---|
| 1 | Inside the run window | Kept, untouched: claims, windows, the fold and `DropUnattributed` see it as today | no |
| 2 | Inside another run's window | Left out, with its pair | `OtherRunRecords[name]` |
| 3 | Inside the setup window | Kept, both halves given the setup scenario's id and name and the phase `Setup` | `SetupRecords` |
| 4 | Anywhere else | Left out, with its pair | the `DroppedOutsideRunWindow` entry (§3.6) |

- **The run window wins** over every declared window, so a host's windows can never take a record from this run's
  tests, and the setup window never touches a record inside the run (Q15 asks about the runner's own set-up).
- **Another run wins over the setup window** (Q4): a host that declares "everything before my run" as its setup, and
  the run before it as another run, gets the start-up kept and the other run left to its report. RUN, P3 H, the device
  run: setup 20, `browser` 10, and no `DroppedOutsideRunWindow` entry at all. The 20 are the start-up's 16 and the 4
  records between the runs, `b2`'s late call among them, now drawn in "Stack start-up" (791 s long) under the setup
  scenario's id: a setup window that reaches past another run's verdict takes that run's late calls into it (§10). Two
  other runs that overlap: the first declared wins (they are both left out either way).
- **Bounds are inclusive**, as the run window's are (`:780`, `:786`), and an `OtherRunWindow` with no end holds
  everything after its start.
- **Markers are never kept in the setup scenario.** A capture's raw step bar, assertion note or Phase boundary
  (`InteractionRecord.IsMarker`) belongs to a test; outside the run window it goes on to row 4 and is counted as today.
  A test's bar drawn in the start-up's diagram would say nothing true, and a stray Phase boundary would split the
  start-up's Setup partition. The prototype does not do this yet; fact 29 pins it.
- **The setup window reaches only what the run window leaves.** A host that writes its `testrun` `started` marker
  before it starts the stack has the start-up inside the run window, where the setup window cannot take it (§10).
- Records with no timestamp stay, as today.
- Both options imply `DropOutsideRunWindow`, as `RunStartedAt` does.
- The records are rewritten before attribution, so the setup scenario's records carry an id that `NeedsAttribution`
  passes over (`IngestAttribution.cs:383-385`). Two places that also have to pass over it, both found by the
  prototype: the fold (`IngestPipeline.cs:1166`), which would fold any id the tests file lacks, and
  `DropUnattributed`'s fold-mode test (`:957-960`), which would ask the predicate about it.

### 3.4 An open end is closed by the next declared run (R2)

A run with no end marker and no `RunEndedAt` has an open window (`:725-735`), so today the next run's traffic is kept in
it (F9). With other runs declared, the open end is closed at the start of the first declared run that begins after
this run began, exclusively: a call that began at that instant is the other run's. The diagnostic's window then reads
`(start → <that start>, where <name> began)`. On P1's capture with the browser run's end marker taken out (P2a's case),
the prototype keeps `b2`'s late call in `b2` (it began before the device run) and leaves the device run's 6 records to
it (RUN, P3 G). `ResolveRunWindow` applies the same rule, since it
takes the request, so a host that calls it sees the window the ingest uses; with no other runs its answer is today's.
Its end is inclusive (the pass drops only what lies after it, `:786`), so the closed end it returns is the last instant
before the other run's start, one tick (100 ns) earlier, which keeps "exclusively" true for every caller.

### 3.5 The setup scenario (R2)

| Property | Value | Why |
|---|---|---|
| Id and name | `SetupWindow.ScenarioId`, `ScenarioName` (default `stack-start-up`, "Stack start-up") | As the fold's |
| Feature | **one of its own, named after the scenario, drawn first** (Q2), placed by the pipeline **after** the Cucumber merge | The synthesizer orders tests-file ids first and log-only ids after (`FeatureSynthesizer.cs:84-94`, `:154-166`), and features by first appearance (`:224-231`), so without this it is drawn last. With `--cucumber-messages` the merge then puts the messages' features first and appends every other feature after them (`Cucumber/CucumberFeatureMerger.cs:79-88`, `:434-447`), so the placement belongs after `IngestPipeline.cs:522-526`, not in the synthesizer (the prototype placed it in the synthesizer, and has no Cucumber case). The HTML follows the model's order (`ReportGenerator.cs:2282`), and the Features tab's sort starts at "Report order", the page's own (`:3105-3106`, `report-features-tools-script.js:62-63`); the data files sort features by name (`:5289`) |
| Result | `Passed`, not `ResultDefaulted`, `NotATest` | As the fold's (`IngestPipeline.cs:536-557`); with R1 the report counts it as no test |
| Phase | `Setup`, on both halves of every call | The issue's "in the Setup phase"; `kronikol query ... --group-by phase` reads it (`QueryCommand.GroupBy.cs:15`) |
| Diagram | with `SeparateSetup`, **a Phase boundary after its last call**, so every call is drawn inside the Setup partition and the page's Setup filter hides it as it hides a test's setup (`collapsible-notes-script.js:2485-2490`); without it, drawn plain, as every scenario is (Q3) | F12; RUN P3 F: `partition #F6F6F6 Setup`, the six inserts, the two health checks folded into `loop ×2 · 10 ms`, `end` ([`results/p3f-setup.puml`](RUN_WINDOW_PLAN.harness/results/p3f-setup.puml)) |
| The run's start and end | **not taken from it** | F8. RUN P3: with it, the browser run still lasts 9.5 s |
| Fold, `DropUnattributed`, `UnattributedInteractions` | it is none of these | §3.3; RUN P3 E: the predicate is asked about the login only |
| Warm-up | its calls are judged with the run's (Q11) | The start-up did warm the stack: a test's first call of a shape the start-up already made is not the run's first |

### 3.6 What the count says (R2)

The entry counts row 4 of §3.3 only, and is written only when that is not zero. A report whose windows explain
everything outside it has no `DroppedOutsideRunWindow` entry, and so no `!` line in a query answer.

```
{n} interaction record(s) outside the run window dropped[ that no other window explains]: {before} before the run began, {after} after it ended[ ({own} name a test in this run's tests file)]. The run window: {start:o} → {end}.
```

- **The figures come first** (F3): the query line keeps 160 characters, and the window's two instants take 70. Both of
  the substrings the existing fact pins stay (`IngestAttributionTests.cs:724-725`).
- `that no other window explains` is said when a setup window or another run was given.
- `{own}` counts the records whose test id is a test in this run's tests file (Q6). It is the issue's "a test's call
  made after the verdict", and it is also what a clock that disagrees with the tests file's looks like at the run's
  edges, and what an earlier or later run of the same tests looks like (F11), so it says what is known and no more.
  Such records are counted, never kept.
- `{end}` is an instant, `open`, or `<instant>, where <name> began` (§3.4).
- The kind, its place among the answer-affecting kinds and its other readers do not change.

### 3.7 What the windows explained (R2)

Not a diagnostic (F14): the host asked for it, and the list is "what went wrong". `IngestResult.SetupRecords` and
`OtherRunRecords` carry the counts for a library host (the issue's host is one), and `kronikol ingest` prints them as
it prints the window-attribution count (`IngestCommand.cs:469-476`): whenever the flag is given, zero included ("the
value worth a look", `:471-472`), in the order the passes run, right after `Replayed ...`:

```
--setup-window: 16 interaction record(s) outside the run window kept in "Stack start-up".
--other-run: 6 interaction record(s) outside the run window left to other runs: device 6.
```

### 3.8 The CLI (R2)

| Flag | Maps to | Notes |
|---|---|---|
| `--setup-window <start>/<end>` | `SetupWindow` | ISO-8601 instants as `--run-start` reads them (`IngestCommand.cs:224-234`), joined by `/` as an ISO interval; implies `--run-window` |
| `--setup-name <name>` | `SetupWindow.ScenarioName` | default "Stack start-up"; refused without `--setup-window` |
| `--other-run <name>=<start>/[<end>]` | one `OtherRunWindow` | repeatable; an empty end is a run still going; implies `--run-window` |

Each refuses a malformed value, an end before its start, or a blank name, with exit 2 and the message shape of
`--run-start` (`:228-231`). The help text gains three rows after `:722`.

### 3.9 Windows that cannot be windows (R1, R2)

The ingest says what is wrong with a window instead of throwing, as it does when no window can be derived (`:752-756`),
since "report generation is diagnostics, never a reason for a run to fail" (`:305-309`):

- **R1:** a run window that ends before it starts (explicit bounds, or an explicit start after the derived end) is not
  applied: nothing is dropped, and an `Other` entry says `DropOutsideRunWindow: the run window ends before it starts
  ({start:o} → {end:o}); nothing was dropped.` The CLI refuses `--run-start` after `--run-end` with exit 2 (Q14).
- **R2:** a setup window or another run's window that ends before it starts, or another run with a blank name, is
  not applied, and one `Other` entry names each; a setup window whose id is the fold's is not applied either. With no
  derivable run window, the existing entry also says that the setup window and other runs were not applied.

### 3.10 `NotATest` in the report (R1)

A scenario that is not a test is drawn like any scenario, as its doc says, and **counted as no test**:

| Reader | Change |
|---|---|
| Run band, ring (`ReportGenerator.cs:1761-1766`, `:3125-3150`) | `Scenarios`, `Passed`, `Failed`, `Skipped`, `Bypassed` and the ring count tests; `Features` counts the features that hold one |
| Features table, readout (`:1804-1818`, `:1839-1843`, `:1889`) | the counts leave it out; a feature that holds only such scenarios reads "not a test", and its duration cells are empty, so the Features tab's "Slowest total", "Slowest average" and "Longest scenario" sorts (`:3105-3108`), which read those cells, put the start-up's 13 minutes last instead of first |
| The scenario (`:2394`, `:2459`) | a muted `not a test` badge in its summary and a `data-not-a-test` attribute; `data-status` stays `Passed`, so the status filters treat it as they do today (Q9) |
| Timeline (`:2186-2196`), percentiles and the duration filter (`:1940-1950`) | left out: its span is not a test's time |
| CI summary (`CiSummaryGenerator.cs:26-31`), `Failures.md` (`FailuresDigestGenerator.cs:183`) | the totals leave it out |
| The console run summary (`RunSummaryConsoleWriter.cs:134-146`) and `Run.json` (`RunRotation.cs:512-537`) | count tests only |
| The CTRF report (`CtrfReportGenerator.cs:188`, `:215`, `:249`), which CI tools read as test results | left out of `tests` and its summary: it is no test result (Q9) |
| Specifications (`ReportGenerator.cs:6438-6441`, `:6463`, `:6482`) | left out: it specifies nothing (Q9) |
| Data files (`:5296-5333`; XML `:5890`; YAML `:6187`; schema `:7298`; XSD, generated at `:7818`) | `"notATest": true` (XML `NotATest`, YAML `NotATest:`) on such a scenario only, so every other scenario's bytes stay as they are |
| `kronikol query` (`ReportScanner.cs:337`, `:937`; `QueryCommand.Overview.cs:82-95`, `:139`) | read; the feature line says `1 not a test`, `Slowest:` leaves it out, a scenario listing marks it |
| `kronikol merge` (`MergeableReportReader.cs:236`) | read, so the merged report counts it as no test and its history calls it `not a test` |
| Component diagram (`:424-429`), `query services` and `interactions` | unchanged: its calls are the stack's traffic, and the report holds them (Q16) |

### 3.11 What does not change

- A host that passes neither option gets today's behaviour, figures and kind; only the message's order changes (§3.6).
- The run window's rule, the tests-file markers, the fold, claims, windows, phases, the merge and `DropUnattributed`.
- The history's handling of `NotATest` (already `N`).

### 3.12 What a reader sees

| | 5.2.3 | After R1 and R2, the host declaring its windows |
|---|---|---|
| Browser report's Diagnostics tab and every query answer | `26 ... dropped: 16 before ..., 10 after ...` (the `!` line cut after `10`) | `4 ... dropped that no other window explains: 0 before ..., 4 after ... (2 name a test in this run's tests file)` |
| Browser report's features | Orders (2), Ingested (the fold) | **Stack start-up** (16 records, drawn first, `not a test`), Orders (2), Ingested (the fold, `not a test`) |
| Browser run band | `Scenarios 3, Passed 3`, `Duration 9s` | `Scenarios 2, Passed 2`, `Duration 9s` |
| `kronikol ingest` | the diagnostic | `--setup-window: 16 ... kept in "Stack start-up".` and `--other-run: 6 ... device 6.` |
| Device report | `30 ... 30 before ...` | `4 ... that no other window explains: 4 before ..., 0 after ...`; `--other-run: 10 ... browser 10.` |

On the owner's run, a browser report given the start-up as its setup window and the device run as another run would
keep the 1,694 and leave the 1,929, and the entry would count only what falls between the windows (INFERRED).

### 3.13 Kronikol4J

Kronikol4J has no ingest layer (its `docs/REMAINING_PARITY.md:2430` says so, at `92d6b28`), so R2 ports nothing. R1's
data-file key is a schema change: Kronikol4J's `ReportDataSchema.java` mirrors the schema (its diagnostic kinds are at
`:155`), so it gains `notATest`, which the port never writes. Both go in the divergence ledger.

### 3.14 Relation to #181 and #179

- **#181** (`DropUnattributed` cannot tell a contested call from start-up traffic): with a setup window, start-up
  traffic never reaches the predicate (F15), so a host no longer needs the predicate for it. The contested-call half of
  #181 is untouched; #181 stays open (Q13).
- **#179** (`ATTRIBUTION_COUNTS_PLAN.md`, in `Kronikol-179`, not green-lit, its line numbers at 4.27.0): edits the
  window pass in `IngestAttribution.cs`, `Attribute` and the replay's test ids in `IngestPipeline.cs`, and the request
  docs of `WindowAttribution`. This plan edits the run-window pass, the exemptions in `DropUnattributed` and the fold,
  the synthesizer and the request docs of the run window. No method is edited by both; whichever ships second rebases.

## 4. Tests, in the order they are written

Each fact is written first and run red on 5.2.3 (or on R1 for R2's), then made green. Messages are compared whole.

### 4.1 Facts for R1

| # | Fact | Red on 5.2.3 because |
|---|---|---|
| 1 | A report with a fold scenario: the run band's `Scenarios`/`Passed` and the ring count the tests only (emitted attributes, `report-html-substring-assertions` style) | counts 3 for 2 tests |
| 2 | The features count and table leave a not-a-test-only feature out of the totals, say "not a test", and leave its duration cells empty, so "Slowest total" sorts it last | counted; its duration sorts first |
| 3 | The timeline and the percentiles leave it out | a bar; a duration |
| 4 | The CI summary, `Failures.md`, the console run summary and `Run.json` count tests only; the CTRF report leaves it out of `tests` | counted: "All 3 scenarios passed" (P1); a `passed` CTRF test |
| 5 | Specifications leave it out (if Q9 is taken) | listed (P1) |
| 6 | JSON, XML and YAML carry `notATest` on that scenario only; the files validate against the schema and the XSD | no key |
| 7 | A mergeable file round-trips `NotATest`, and `kronikol merge --history` calls the fold scenario `not a test` (P4's shape) | `stable: passing` (P4) |
| 8 | `kronikol query summary`: `Ingested  1 not a test`, and `Slowest:` leaves it out | `1 passed` |
| 9 | An inverted explicit window drops nothing and says why; `--run-start` after `--run-end` exits 2 | drops all 6 |
| 10 | `--run-window`, `--run-start` and `--run-end` reach the pipeline (F13) | green on 5.2.3: a coverage fact, proved by mutating the mapping |
| 11 | **Playwright:** the band reads the tests' count and the fold scenario's summary paints the `not a test` badge | no badge |

### 4.2 Facts for R2

| # | Fact |
|---|---|
| 12 | **The issue's shape** (P1's capture): the browser report keeps 16 records in "Stack start-up", leaves 6 to `device`, and writes exactly the §3.6 message with `0 before`, `4 after`, `2 name a test`; the device report likewise |
| 13 | A call in both another run's window and the setup window goes to the other run |
| 14 | A call inside the run window and the setup window stays with the run (attributed as before) |
| 15 | The setup scenario: id, name, its own feature first in the model, `NotATest`, `Passed`, not `ResultDefaulted`, phase `Setup` on both halves |
| 16 | Its records are not folded, not asked about by `DropUnattributed`, not counted as unattributed, and not in the run's start and end (`IngestResult.Start`/`End` equal those of the same ingest without the window) |
| 17 | With `SeparateSetup`, its diagram is inside the Setup partition; without it, no partition |
| 18 | An open end is closed at the next declared run's start, exclusively, and the window text names the run |
| 19 | All explained: no `DroppedOutsideRunWindow` entry, and no `!` line in a query answer |
| 20 | No windows: today's figures, the new order, and a query line that keeps both figures and their meaning |
| 21 | Windows that cannot be windows (§3.9): not applied, one `Other` each; the CLI exits 2 |
| 22 | `IngestResult.SetupRecords` and `OtherRunRecords`; the console lines appear only with their flags, after `Replayed` |
| 23 | CLI parsing: `--setup-window`, `--setup-name`, `--other-run` with and without an end, repeated, and each refusal |
| 24 | With no derivable run window, the setup window and other runs are not applied and the entry says so |
| 25 | The setup scenario's slow first call of a shape takes the warm-up mark, and the test's later call of it does not (Q11) |
| 26 | `MergeDuplicateInteractions` with a call captured from both sides before the run: both twins land in the setup scenario and merge there |
| 27 | **Playwright:** "Stack start-up" is the first feature painted, with its badge; with `SeparateSetup`, the page's Setup filter hides its calls and leaves its participants (P5) |
| 28 | With Cucumber messages (the playwright-bdd fixture), the setup feature is still the first in the model and on the page |
| 29 | A capture's raw step marker outside the run window, inside the setup window, is not kept in the setup scenario and is counted in the entry, as today |

### 4.3 Mutations

Each must turn a fact red: precedence swapped (13); the run window not checked first (14); the open end closed
inclusively or not at all (18); the setup scenario's time kept in the run (16); its feature appended rather than first
(15, 27); the phase set on the request half only (15); the fold's or `DropUnattributed`'s exemption removed (16, found
by the prototype); every test id counted as "names a test" (12); the explained records counted in the entry (12, 19);
the figures after the window (20); an option that does not imply the window (12); the setup feature placed before the
Cucumber merge only (28); markers kept in the setup scenario (29); R1's `notATest` written on every scenario (6); the
merge reading it and dropping it (7); a not-a-test feature's duration cells kept (2).

### 4.4 Proofs before each tag

Facts 1 to 9 and 11 red on 5.2.3, read for the right failure; fact 10 and every mutation of §4.3; the core suite and
the full Playwright suite; `release.slnf` in Release. The prototype (§4.5) is the executable spec for R2's numbers:
fact 12 must print what P3 printed.

### 4.5 The prototype (RUN)

R2's core, in a scratch copy of `src/Kronikol` at `9d89b793`
([`prototype.diff`](RUN_WINDOW_PLAN.harness/prototype.diff), 277 lines: the two options, the result counts, §3.3, §3.4,
§3.6's figures, the setup scenario's feature, time and boundary), built for net10.0 alone, and driven by
[`p3-prototype.cs`](RUN_WINDOW_PLAN.harness/p3-prototype.cs) over P1's capture
([`results/p3.txt`](RUN_WINDOW_PLAN.harness/results/p3.txt)). Every prediction held: A' (setup 16, `device` 6,
`0 before, 4 after`, 2 naming `b2`, 9.5 s), B' (setup 16, `browser` 10, `4 before`), B'' (the start-up declared as the
browser run's: `browser` 26), E (the predicate asked about the login only), F (the Setup partition), G (the open end
closed at 10:00:11.6), H (a setup window over the browser run: `browser` 10, setup 20, no entry). Its report, without
R1, shows why R1 comes first: `Scenarios 4, Passed 4` for two tests, the
start-up as the timeline's top bar, and `Stack start-up  1 passed` in `query summary`
([`results/p3-report-grep.txt`](RUN_WINDOW_PLAN.harness/results/p3-report-grep.txt)).

The existing suite was **not** run against the prototype: C: had 11 GB free with other sessions' worktrees built, and
the test project builds every extension. ESTIMATED: no existing fact turns red, since the two pinned substrings survive
§3.6 and no fact reads the message's window part; the step that would measure it is fact 20's red run.

## 5. Releases and bumps

| Release | Contents | Bump | Why |
|---|---|---|---|
| R1 | §3.10, §3.9's run-window half, facts 1 to 11 | patch: the next free one at push | Bug fixes: a scenario that is not a test was counted as a passed test, and the merge lost the flag (CLAUDE.md: a fix that changes observable behaviour is still a patch; the changelog says which counts move). Q10 asks whether the data-file key makes it a minor |
| R2 | §3.2 to §3.8, §3.9's other half, facts 12 to 29 | **minor**, after R1 | New public types and members, new options and new CLI flags |

Version numbers are claimed at push, after reading the other worktrees' `Directory.Build.props`. R1's changelog states
the counts that move for every report with a fold scenario. R2's states the message's new order.

## 6. Documentation

| Where | Change | Release |
|---|---|---|
| `Reports/Scenario.cs:91-97` (`NotATest`) | drawn like any scenario, counted as no test, written to the data files | R1 |
| `IngestPipeline.cs:82-88` (`FoldUnknownTestsInto`) | its scenario is not a test in the report too | R1 |
| `IngestPipeline.cs:162-189` (`DropOutsideRunWindow`, `RunStartedAt`, `RunEndedAt`), `:710-744` (`ResolveRunWindow`, the pass) | the windows, the order of §3.3, the open end, what the entry counts | R2 |
| `Reports/DiagnosticEntry.cs:43-44` (`DroppedOutsideRunWindow`) | "that no window explains" | R2 |
| `IngestCommand.cs:717-722` (help) | three rows | R2 |
| Wiki `Ingesting-External-Captures.md:273-274` (flags), `:512-529` (the run window: a new subsection, "Two runs on one stack", with the issue's shape), `:678` (the diagnostics table) | as above | R2 |
| Wiki `Diagnostics-and-Debugging.md:108`, `Querying-Reports.md:1502-1519` | what the count counts; the `!` line after R2 | R2 |
| Wiki `Integration-Cucumber-Messages.md:203` (scenarios the messages do not own are kept after theirs) | except the setup scenario, which goes first | R2 |
| Wiki `Cross-Run-History.md:911-915` | a merged report's history calls it `not a test` too | R1 |
| Wiki `Ingesting-External-Captures.md:269` (the `--fold-unknown` row), `:298` (what the fold scenario does to the specifications and the verdict counts), `:321`, `:465`; `Integration-TcpTap-Extension.md:124`, `Integration-Otlp-Extension.md:148`, `Integration-Cucumber-Messages.md:203` (each names the fold's bucket) | not a test is counted as no test and left out of the specifications; each line read again for a sentence the change makes false | R1 |
| `CHANGELOG.md` | each release | R1, R2 |
| Kronikol4J ledger (`docs/REMAINING_PARITY.md`) | R1: the schema's `notATest`, never written by the port; R2: nothing ported, no ingest layer | R1, R2 |
| `PLANS_STATUS.md` | this plan's row | every step |

The wiki line numbers are for `7363a88`. Whoever executes this edits the wiki in a worktree of their own and commits
straight away.

## 7. Where it sits in the roadmap

Not placed. Placing it edits `ROADMAP.md`, which other sessions edit too, so it is placed when it is green-lit, beside
the ingest rows of stage 1 (#179's plan is in the same state).

## 8. Found on the way, not in this plan

- **No recipe writes the run markers** (F16). A Playwright or Jest host that follows the recipes gets a window that
  starts at its first test, so `--run-window` drops its runner's global set-up as "before the run began". The recipes
  could write `testrun` on `onBegin` and `onEnd`; a docs change for its own issue.
- **The query line shows the first message of each kind** (`QueryCommand.cs:417-434`), #179's F8; this plan writes one
  `DroppedOutsideRunWindow` entry, so it is not affected.
- **A raw marker that carries the fallback id is counted as an unattributed interaction** (P2d: 5 for 4 records and
  the marker). No producer writes such a marker (INFERRED); noted only.
- **ProxyTap gives every unattributed request a fresh GUID as its test id** unless `FallbackTestId` is set
  (`ProxyTap.cs:585-586`), so without a fold each one is a scenario of its own; documented as the default, noted only.
- **A host could tell a tap a run began** only by swapping its `Sink` or `FallbackTestId` (the taps read their options
  object on every capture, `ProxyTap.cs:85-89`), which nothing documents. Capture-side boundaries belong with a future
  `kronikol tap` (A3).

## 9. What is not known

- **What R2 prints on the owner's capture.** The prediction is §3.12's; the windows and the capture are the owner's.
- **Whether the device suite's tests file and the taps share a clock.** The run window comes from the tests file's
  clock, the declared windows from the host's, and the records from the taps'. A skew moves records across a bound;
  §3.6's count is where it shows, at the run's edges.
- **How large the start-up's diagram is.** 824 seeding calls with different bodies do not collapse
  (`CollapseConsecutiveIdenticalCalls` folds identical neighbours), so the diagram is drawn in parts. ESTIMATED; the
  owner's capture measures it, and `LONG_NOTE_SCROLL_PLAN.md`'s measurements say what a long diagram costs.
- **Whether a host outside this repo reads the message's text.** None in `src/`, `tools/`, `templates/` or the wiki
  parses it (READ).

## 10. Risks and traps

- **Three clocks** (§9): document that the declared windows should come from the clock the taps use, which for the
  issue's host is its own.
- **A setup scenario id that a test uses**: the default cannot collide with a tests-file id in practice; a host's own
  id can. §3.9 refuses the fold's id; a clash with a test id is documented, not checked (the tests file is data).
- **A setup window wider than the start-up** takes whatever else falls in it and no other window claims: P3 H's took
  `b2`'s late call and the call between the runs, and gave them the setup scenario's id. The docs say to end the setup
  window where the start-up ends, or to declare the run before as running up to its last call.
- **The run's time**: the setup scenario is left out of `Start`/`End`, and so of the history's run duration. A fact
  pins it (16); a later change to the synthesizer's run time must keep it.
- **A run window that starts before the start-up** (a host that writes `testrun` `started` before it starts the stack,
  or an early `RunStartedAt`) leaves the setup window nothing to take: the start-up is the run's, as today. The docs say
  to write the marker when the runner starts, as `TestRunRecord.cs:216-218` already does.
- **Keeping the start-up makes the report larger** by the start-up's records and bodies (on the owner's run, 1,694
  records, 824 of them seeding calls), where today they are dropped. It is what the host asks for, and no larger than
  workaround C's fold; `kronikol query sizes` measures it on the owner's report (ESTIMATED).
- **The message's order changes for every user** (§3.6). The two pinned substrings survive; the changelog says so.
- **`ResolveRunWindow` is public**: it gains the open-end rule through the request it already takes; its signature
  stays, and its answer with no other runs is today's (a fact pins both).
- **R1 moves every fold user's counts.** The changelog names the readers; history is unaffected (already `N`).
- **Line endings and disk**: a worktree made without `-c core.autocrlf=false -c core.eol=lf` writes CRLF and its
  report bytes differ from CI's. C: had 11 GB free on 2026-10-21; build what each step needs and clear `bin`/`obj`.

## 11. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | The names | **The issue's: `SetupWindow` and `OtherRunWindows`**, with the record `OtherRunWindow`. `StartUpWindow` says what it holds more exactly, but the issue calls it the run's Setup, and the scenario's phase is `Setup` |
| Q2 | The setup scenario's feature | **One of its own, named after the scenario, drawn first.** In `DefaultFeatureName`'s feature with the fold it would need that whole feature first, the fold included |
| Q3 | The Setup partition | **With `SeparateSetup`, a boundary after its last call** (F12, P3 F), so the page's Setup filter treats it as it treats a test's setup. Without the option, no partition, as for every scenario. With the filter on, its diagram keeps its participants and no call (RUN, P5: `stripSetup` leaves out the participants nothing calls only while a call stays, `collapsible-notes-script.js:2490-2564`); the reader asked for no setup, so that is right, and a note saying "all of this is setup" would be a new control (a minor of its own) |
| Q4 | A call in both the setup window and another run's | **The other run's**: a run's traffic belongs to that run's report; the setup window keeps what is left before |
| Q5 | Close an open end at the next declared run | **Yes**, exclusively (§3.4): the host has said where the next run begins, and P2a shows the alternative |
| Q6 | The records outside the window that name a test of this run | **Count them in the entry, never keep them.** Keeping would put an earlier run of the same tests into this run's (F11) |
| Q7 | Where the explained counts go | **`IngestResult` and the console**, never a diagnostic (F14). The issue's host is a library host, so the members are public. `WindowAttributedRecords` and `PhasedRecords` stay internal: making them public is not asked |
| Q8 | `--other-run-tests <name>=<file>`, reading another run's window from its tests file | **Not now.** A library host can call `ResolveRunWindow` on the other file; ask again if a CLI user needs it |
| Q9 | How the report shows a scenario that is not a test | **(a)** A `not a test` badge, `data-status` unchanged, so no filter changes; the alternative, a status of its own with a toggle, is a new report control (a minor). **(b)** Specifications leave it out. **(c)** The CTRF report leaves it out of `tests`; CTRF's `other` status means a result that is none of the others (`CtrfReportGenerator.cs:243-249`), and this is no result at all |
| Q10 | R1's bump, given the data files gain a key | **Patch**: the key is how the merge stops losing a flag the report already had. If the owner reads a new key as new surface, R1 is a minor with the same contents |
| Q11 | Warm-up judging and the setup scenario | **Judged with the run** (as the fold's calls are today, `WarmUpCalls.cs:140`): the start-up did warm the stack |
| Q12 | One release or two | **Two**: R1 fixes every fold user's report whether or not they adopt R2, and R2 needs R1 to be readable |
| Q13 | A comment on #181 | **Yes, one line after R2 ships**: start-up traffic no longer needs `DropUnattributed`; #181 stays open for contested calls |
| Q14 | A window that cannot be a window | **A diagnostic, and the window not applied**, as when none can be derived; the CLI exits 2 at parse |
| Q15 | Should the setup window also take the runner's own set-up inside the run window (a global login before the first test)? | **No.** The run window decides what is this run's (§3.3); such calls stay in the fold. A host that wants them in the setup scenario can start its run window after them |
| Q16 | The component diagram and `kronikol query services`/`interactions` count a not-a-test scenario's calls (today the fold's; after R2 the start-up's 824 seeding calls too) | **Keep them.** The issue calls the start-up "useful context for the run: what was loaded, and what the services did before any test", the report holds those calls, and R1's listings say which scenario is not a test. Leaving them out would also change what every fold user's component diagram draws today |

## Appendix A. Edit sites

| File | Release | Change |
|---|---|---|
| `src/Kronikol/Reports/ReportGenerator.cs:1758-1766`, `:3125-3150` | R1 | the band and ring count tests |
| `src/Kronikol/Reports/ReportGenerator.cs:1804-1818`, `:1839-1843`, `:1889` | R1 | features table (counts, duration cells) and readout |
| `src/Kronikol/Reports/ReportGenerator.cs:2394`, `:2459` and the report styles | R1 | the badge and `data-not-a-test` |
| `src/Kronikol/Reports/ReportGenerator.cs:1940-1950`, `:2186-2196` | R1 | percentiles, duration filter, timeline |
| `src/Kronikol/Reports/ReportGenerator.cs:6438-6441`, `:6463`, `:6482` | R1 | specifications (Q9) |
| `src/Kronikol/Reports/ReportGenerator.cs:5296-5333`, `:5890`, `:6187`, `:7298`, `:7818` (the XSD) | R1 | `notATest` in the data files, the schema and the XSD |
| `src/Kronikol/Reports/CiSummaryGenerator.cs:26-31`, `src/Kronikol/Reports/FailuresDigestGenerator.cs:183` | R1 | totals |
| `src/Kronikol/Reports/RunSummaryConsoleWriter.cs:134-146`, `src/Kronikol/Reports/RunRotation.cs:512-537`, `src/Kronikol/Reports/CtrfReportGenerator.cs:188`, `:215`, `:249` | R1 | the console summary, `Run.json`, the CTRF report |
| `src/Kronikol/Query/ReportScanner.cs:337`, `:937`; `src/Kronikol/Query/QueryCommand.Overview.cs:82-95`, `:139` | R1 | the query |
| `src/Kronikol/Reports/Merge/MergeableReportReader.cs:236` | R1 | the merge |
| `src/Kronikol/Ingestion/IngestPipeline.cs:745-756`; `src/Kronikol.Tool/IngestCommand.cs:224-234` | R1 | the inverted window |
| `src/Kronikol/Ingestion/IngestPipeline.cs:162-189`, `:278`, `:288-323` | R2 | the options, the records, the result |
| `src/Kronikol/Ingestion/IngestPipeline.cs:489`, `:497-498`, `:508-509`, `:535-557` | R2 | the pass's counts, the boundary, the synthesis, not a test |
| `src/Kronikol/Ingestion/IngestPipeline.cs:714-803` | R2 | the window, the open end, §3.3, §3.6 |
| `src/Kronikol/Ingestion/IngestPipeline.cs:957-960`, `:1166` | R2 | the two exemptions |
| `src/Kronikol/Ingestion/FeatureSynthesizer.cs:70-76`, `:224-231`, `:243-249` | R2 | the setup scenario's own feature, outside the run's time |
| `src/Kronikol/Ingestion/IngestPipeline.cs:522-533` | R2 | the setup feature moved first after the Cucumber merge |
| `src/Kronikol.Tool/IngestCommand.cs:67-284`, `:440-460`, `:469-476`, `:717-722` | R2 | flags, mapping, console lines, help |
| `tests/Kronikol.Tests/Ingestion/` (a new `RunWindowTests` beside `IngestAttributionTests`), `tests/Kronikol.Tests/Tool/IngestCommandTests.cs`, `tests/Kronikol.Tests/Reports/...`, `tests/Kronikol.Tests.EndToEnd/` | R1, R2 | §4 |
| `CHANGELOG.md`, every package's version (`Directory.Build.props`), the history action's `VERSION`, the templates' pin | R1, R2 | the release |
| Wiki and Kronikol4J ledger | R1, R2 | §6 |

## Appendix B. The harness

[`RUN_WINDOW_PLAN.harness/README.md`](RUN_WINDOW_PLAN.harness/README.md) says how to run each probe. P1 and P2 run on
Kronikol 5.2.3 from nuget.org; P3 runs on the prototype, which is `src/Kronikol` at `9d89b793` with `prototype.diff`
applied. Run each from a folder outside the repository, so that the repository's `Directory.Build.props` is not read.

## Log

- 2026-10-21, **written** at the owner's ask ("investigate #187 and come up with a well researched detailed plan to
  implement a solution to the underlying issue, which may or may not be what was suggested"), in a worktree of its own
  on `9d89b793`, with other sessions running.
  - The repro (P1) and the edges (P2) were run on 5.2.3 from nuget.org; R2's core was prototyped in a scratch copy of
    the core project and run on P1's capture (P3). No shared checkout was changed, and nothing was built in the
    worktree.
  - Read-only sweeps of the diagnostics' readers, the report's readers of `NotATest`, the tests-file producers and the
    taps were made, and each citation used here was read again by hand.
  - The wiki was read at `7363a88` and Kronikol4J at `92d6b28`, without updating either checkout.
- 2026-10-21, **verified and extended** at the owner's ask ("make sure the plan is complete and accurate and do any deep
  dives which might change it"), in the same worktree, with nothing outside it changed but scratch folders:
  - **Corrected:** F7 said a merged ledger records the fold scenario as `P`. `kronikol merge` never writes a ledger
    (`MergeCommand.cs:194-197`); it judges the merged run against one, and P4 measured what that does: the merged report
    calls the fold scenario `stable: passing` where its shard's says `not a test`, and the ledger stays `NPP`. The TL;DR,
    §2.4, §3.10 and fact 7 say so now.
  - **Changed the design:** with Cucumber messages the merge appends every feature the messages do not own after
    theirs (`CucumberFeatureMerger.cs:434-447`), so the setup feature is moved first after that merge, not in the
    synthesizer (§3.5, fact 28). Markers are never kept in the setup scenario (§3.3, fact 29). `ResolveRunWindow`'s
    closed end is one tick before the next run's start, since its end is inclusive (§3.4).
  - **Added from runs:** P3 H (a setup window over everything before the device run: the other run wins, and nothing
    is left to count), P4 (the merge), P5 (the page's Setup filter leaves the start-up's participants and no call), and
    P1's `Failures.md` and `Specifications.yml`, which count and list the fold scenario.
  - **Added from reading:** the Features tab's sort starts at the report's order, and its duration sorts read the
    features table's cells, which R1 empties for a scenario that is not a test (§3.10, fact 2); three more readers that
    count the fold scenario as a test (the console run summary, `Run.json`, the CTRF report); the component diagram and
    `query services` count its calls (Q16); the XML, YAML and XSD edit sites; Kronikol4J's schema mirrors the scenario
    object, so its ledger takes `notATest`; the wiki lines R1 and R2 touch, each read again at `7363a88`; and two risks
    (a run window that starts before the start-up, and the report's growth).
  - Every reference was read again by hand at `9d89b793`. The existing test suite was still not run against the
    prototype (§4.5).
- 2026-10-22, **R1 executed** at the owner's ask ("implement the plan in full", every recommendation of section 11 taken),
  in a worktree of its own on 5.2.5, and **released as 5.2.6**.
  - Facts 1 to 11 were written first and run on 5.2.5: 27 red at the readers they name, the 7 CLI facts of F13 green
    (proved by mutating the flags' mapping), and a guard of the one-instant window; fact 11's five Playwright facts red on
    5.2.5's page. 47 of 47 mutations killed: 40 of the C# code and 7 of the page scripts through the Playwright facts.
  - Found on the way and fixed in R1: F17 to F23, added to section 0. F20 is a consequence of R1 itself (the readout
    counts tests), caught before it shipped.
